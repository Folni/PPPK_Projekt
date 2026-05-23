"""
Step 4 – Report Generation
===========================
Generates a CSV report for all bird species that have at least one
positive classification result.

Steps:
  1. Load all classifications from MongoDB
  2. Join with taxonomy data
  3. Clean and transform data
  4. Apply optional fuzzy species name filter
  5. Write CSV report

Fuzzy filtering uses the thefuzz library (Levenshtein distance) to match
species names even with typos or partial matches.
"""
import sys
import os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import csv
from datetime import datetime
from collections import defaultdict
from utils import load_config, get_mongodb_client


def load_classifications(db, collection_name: str) -> list[dict]:
    """Load all classification documents from MongoDB."""
    collection = db[collection_name]
    docs = list(collection.find({}))
    print(f"[Step 4] Loaded {len(docs)} classification records.")
    return docs


def load_taxonomy(db, collection_name: str) -> dict:
    """Load taxonomy as a dict keyed by scientificName."""
    collection = db[collection_name]
    taxonomy = {}
    for doc in collection.find({}):
        sci_name = doc.get("scientificName")
        if sci_name:
            taxonomy[sci_name] = doc
    print(f"[Step 4] Loaded {len(taxonomy)} taxonomy entries.")
    return taxonomy


def extract_detected_species(classification_result: dict) -> list[dict]:
    """
    Extract list of detected species from a classification result.
    Handles different response formats from the API.
    """
    if not classification_result or "error" in classification_result:
        return []

    # Try different response formats
    results = (
        classification_result.get("results") or
        classification_result.get("species") or
        []
    )

    if isinstance(results, list):
        return results
    return []


def clean_species_name(name: str) -> str:
    """Clean and normalize a species name."""
    if not name:
        return ""
    # Strip extra whitespace, normalize case
    return " ".join(name.strip().split()).strip()


def fuzzy_filter(species_list: list[str], filter_term: str, threshold: int = 70) -> list[str]:
    """
    Filter species names using fuzzy string matching.
    Returns species whose names match the filter term above the threshold.
    """
    if not filter_term:
        return species_list

    try:
        from thefuzz import fuzz
    except ImportError:
        print("[Step 4] WARNING: thefuzz not installed, using exact substring match instead.")
        return [s for s in species_list if filter_term.lower() in s.lower()]

    matched = []
    for species in species_list:
        # Use partial_ratio for substring matching, ratio for full name matching
        score = max(
            fuzz.ratio(filter_term.lower(), species.lower()),
            fuzz.partial_ratio(filter_term.lower(), species.lower()),
            fuzz.token_sort_ratio(filter_term.lower(), species.lower()),
        )
        if score >= threshold:
            matched.append(species)
            print(f"  [Fuzzy] '{species}' matched '{filter_term}' with score {score}")

    return matched


def aggregate_report(classifications: list[dict], taxonomy: dict) -> dict:
    """
    Aggregate classification results by species.
    Returns dict: scientificName -> aggregated stats
    """
    species_stats = defaultdict(lambda: {
        "scientificName": "",
        "vernacularName": "",
        "order": "",
        "family": "",
        "classificationCount": 0,
        "totalConfidence": 0.0,
        "avgConfidence": 0.0,
        "locations": [],
        "audioFiles": [],
        "firstObserved": None,
        "lastObserved": None,
    })

    for doc in classifications:
        detected = extract_detected_species(doc.get("classificationResult", {}))
        classified_at = doc.get("classifiedAt")
        lat = doc.get("latitude")
        lon = doc.get("longitude")
        file_name = doc.get("fileName", "")

        for species_result in detected:
            sci_name = clean_species_name(
                species_result.get("scientificName") or species_result.get("species") or ""
            )
            if not sci_name:
                continue

            confidence = float(species_result.get("confidence", 0.0))
            tax_data = taxonomy.get(sci_name, {})

            stats = species_stats[sci_name]
            stats["scientificName"] = sci_name
            stats["vernacularName"] = (
                species_result.get("vernacularName") or
                tax_data.get("vernacularName") or ""
            )
            stats["order"] = tax_data.get("order", "")
            stats["family"] = tax_data.get("family", "")
            stats["classificationCount"] += 1
            stats["totalConfidence"] += confidence

            if lat and lon:
                stats["locations"].append({"lat": lat, "lon": lon})
            if file_name:
                stats["audioFiles"].append(file_name)

            if classified_at:
                if stats["firstObserved"] is None or classified_at < stats["firstObserved"]:
                    stats["firstObserved"] = classified_at
                if stats["lastObserved"] is None or classified_at > stats["lastObserved"]:
                    stats["lastObserved"] = classified_at

    # Calculate averages and clean up
    for sci_name, stats in species_stats.items():
        if stats["classificationCount"] > 0:
            stats["avgConfidence"] = round(
                stats["totalConfidence"] / stats["classificationCount"], 4
            )
        # Deduplicate lists
        stats["audioFiles"] = list(set(stats["audioFiles"]))
        stats["locationCount"] = len(stats["locations"])
        # Format locations as string for CSV
        stats["locationsStr"] = "; ".join(
            f"{loc['lat']},{loc['lon']}" for loc in stats["locations"]
        )

    return dict(species_stats)


def write_csv(report: dict, output_path: str):
    """Write the aggregated report to a CSV file."""
    if not report:
        print("[Step 4] No data to write.")
        return

    fieldnames = [
        "scientificName",
        "vernacularName",
        "order",
        "family",
        "classificationCount",
        "avgConfidence",
        "locationCount",
        "locations",
        "audioFiles",
        "firstObserved",
        "lastObserved",
    ]

    os.makedirs(os.path.dirname(output_path), exist_ok=True)

    with open(output_path, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()

        for sci_name, stats in sorted(report.items(), key=lambda x: x[1]["classificationCount"], reverse=True):
            writer.writerow({
                "scientificName": stats["scientificName"],
                "vernacularName": stats["vernacularName"],
                "order": stats["order"],
                "family": stats["family"],
                "classificationCount": stats["classificationCount"],
                "avgConfidence": stats["avgConfidence"],
                "locationCount": stats["locationCount"],
                "locations": stats["locationsStr"],
                "audioFiles": "; ".join(stats["audioFiles"]),
                "firstObserved": stats["firstObserved"].isoformat() if stats["firstObserved"] else "",
                "lastObserved": stats["lastObserved"].isoformat() if stats["lastObserved"] else "",
            })

    print(f"[Step 4] CSV report written to: {output_path}")
    print(f"[Step 4] Total species in report: {len(report)}")


def run(config_path: str = None, species_filter: str = None):
    config = load_config(config_path)

    # Get filter from config if not passed as parameter
    if species_filter is None:
        species_filter = config["pipeline"].get("species_filter", "")

    fuzzy_threshold = config["pipeline"].get("fuzzy_threshold", 70)

    mongo_client = get_mongodb_client(config)
    db = mongo_client[config["mongodb"]["database"]]

    # Load data
    classifications = load_classifications(db, config["mongodb"]["collections"]["classifications"])
    taxonomy = load_taxonomy(db, config["mongodb"]["collections"]["taxonomy"])
    mongo_client.close()

    if not classifications:
        print("[Step 4] No classifications found. Run step 3 first.")
        return

    # Aggregate
    report = aggregate_report(classifications, taxonomy)

    # Apply fuzzy filter if specified
    if species_filter and species_filter.strip():
        print(f"\n[Step 4] Applying fuzzy filter: '{species_filter}' (threshold: {fuzzy_threshold})")
        # Filter by both scientificName AND vernacularName
        matched_species = []
        for sci_name, stats in report.items():
            names_to_check = [sci_name, stats.get("vernacularName", "")]
            scores = fuzzy_filter(names_to_check, species_filter, fuzzy_threshold)
            if scores:
                matched_species.append(sci_name)
        report = {k: v for k, v in report.items() if k in matched_species}
        print(f"[Step 4] Species after filter: {len(report)}")

    # Output path
    base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    output_path = os.path.join(
        base_dir,
        config["pipeline"]["output_dir"],
        config["pipeline"]["output_file"]
    )

    write_csv(report, output_path)
    print("[Step 4] Report generation complete.")


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser(description="Generate bird observation report")
    parser.add_argument("--filter", type=str, default="", help="Fuzzy species name filter")
    args = parser.parse_args()
    run(species_filter=args.filter if args.filter else None)
