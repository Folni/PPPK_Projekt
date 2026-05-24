"""
Step 3 – Audio Processing
==========================
For each audio file in the target directory:
  1. Upload the file to MinIO (S3-compatible storage)
  2. Send a POST request to the bird classification API
  3. Store the classification log (request + response) in MinIO
  4. Store the classification result in MongoDB, linked to taxonomy data

Each audio file is associated with a geographic location (lat/lon).
For simplicity, all files in the audio directory share one location
defined in config or passed as a parameter.

MinIO buckets: audio-files, classification-logs
MongoDB collection: classifications
"""
import sys
import os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import json
import uuid
import hashlib
import requests
from datetime import datetime
from pathlib import Path
from io import BytesIO
from utils import load_config, get_mongodb_client, get_minio_client, ensure_minio_bucket


DEFAULT_LATITUDE = 45.8150
DEFAULT_LONGITUDE = 15.9819


def upload_audio_to_minio(minio_client, bucket: str, file_path: Path) -> str:
    with open(file_path, "rb") as f:
        content = f.read()

    file_hash = hashlib.sha256(content).hexdigest()[:16]
    object_name = f"{file_hash}_{file_path.name}"

    try:
        minio_client.stat_object(bucket, object_name)
        print(f"  [MinIO] Already exists: {object_name} – skipping upload.")
        return object_name
    except Exception:
        pass

    minio_client.put_object(
        bucket_name=bucket,
        object_name=object_name,
        data=BytesIO(content),
        length=len(content),
        content_type="audio/wav",
    )
    print(f"  [MinIO] Uploaded: {object_name}")
    return object_name


def classify_audio(api_url: str, file_path: Path) -> dict:
    with open(file_path, "rb") as f:
        files = {"file": (file_path.name, f, "audio/wav")}
        response = requests.post(api_url, files=files, timeout=30)

    if response.status_code == 200:
        return response.json()
    else:
        print(f"  [API] Warning: Classification returned {response.status_code}")
        return {"error": response.status_code, "message": response.text}


def store_classification_log(minio_client, bucket: str, log_data: dict) -> str:
    log_id = str(uuid.uuid4())
    object_name = f"log_{log_id}.json"
    log_bytes = json.dumps(log_data, indent=2, default=str).encode("utf-8")

    minio_client.put_object(
        bucket_name=bucket,
        object_name=object_name,
        data=BytesIO(log_bytes),
        length=len(log_bytes),
        content_type="application/json",
    )
    print(f"  [MinIO] Log stored: {object_name}")
    return object_name


def store_classification_result(db, collection_name: str, taxonomy_collection_name: str,
                                 file_name: str, object_name: str, log_object_name: str,
                                 classification: dict, latitude: float, longitude: float):
    collection = db[collection_name]
    taxonomy_col = db[taxonomy_collection_name]

    species_data = None
    detected_species = classification.get("species") or classification.get("results", [])

    if isinstance(detected_species, list) and detected_species:
        top_species = detected_species[0]
        sci_name = top_species.get("scientificName") or top_species.get("species")
        if sci_name:
            species_data = taxonomy_col.find_one({
                "$or": [
                    {"canonicalName": sci_name},
                    {"scientificName": sci_name},
                    {"species": sci_name}
                ]
            })

    doc = {
        "fileName": file_name,
        "minioObjectName": object_name,
        "minioLogObjectName": log_object_name,
        "latitude": latitude,
        "longitude": longitude,
        "classificationResult": classification,
        "taxonomyId": species_data["_id"] if species_data else None,
        "taxonomyData": {
            "scientificName": species_data.get("scientificName") if species_data else None,
            "canonicalName": species_data.get("canonicalName") if species_data else None,
            "vernacularName": species_data.get("vernacularNameEng") if species_data else None,
            "taxonKey": species_data.get("key") if species_data else None,
            "order": species_data.get("order") if species_data else None,
            "family": species_data.get("family") if species_data else None,
        } if species_data else None,
        "classifiedAt": datetime.utcnow(),
    }

    collection.insert_one(doc)
    print(f"  [MongoDB] Classification result stored for: {file_name}")


def create_dummy_audio_files(audio_dir: Path):
    audio_dir.mkdir(parents=True, exist_ok=True)
    dummy_files = ["recording_001.wav", "recording_002.wav", "morning_birds.wav"]
    for fname in dummy_files:
        fpath = audio_dir / fname
        if not fpath.exists():
            with open(fpath, "wb") as f:
                f.write(b"RIFF$\x00\x00\x00WAVEfmt \x10\x00\x00\x00\x01\x00\x01\x00"
                        b"D\xac\x00\x00\x88X\x01\x00\x02\x00\x10\x00data\x00\x00\x00\x00")
            print(f"  [Test] Created dummy audio file: {fname}")


def run(config_path: str = None, latitude: float = None, longitude: float = None):
    config = load_config(config_path)

    lat = latitude or DEFAULT_LATITUDE
    lon = longitude or DEFAULT_LONGITUDE

    minio_client = get_minio_client(config)
    mongo_client = get_mongodb_client(config)
    db = mongo_client[config["mongodb"]["database"]]

    audio_bucket = config["minio"]["buckets"]["audio"]
    log_bucket = config["minio"]["buckets"]["logs"]
    ensure_minio_bucket(minio_client, audio_bucket)
    ensure_minio_bucket(minio_client, log_bucket)

    base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    audio_dir = Path(base_dir) / config["pipeline"]["audio_dir"]

    if not audio_dir.exists() or not list(audio_dir.glob("*.wav")):
        print("[Step 3] No audio files found – creating dummy files for testing.")
        create_dummy_audio_files(audio_dir)

    audio_files = list(audio_dir.glob("*.wav")) + list(audio_dir.glob("*.mp3"))

    if not audio_files:
        print("[Step 3] No audio files to process.")
        mongo_client.close()
        return

    print(f"[Step 3] Processing {len(audio_files)} audio file(s)...")

    classify_url = config["aves_api"]["base_url"] + config["aves_api"]["classify_endpoint"]

    for audio_file in audio_files:
        print(f"\n  Processing: {audio_file.name}")

        object_name = upload_audio_to_minio(minio_client, audio_bucket, audio_file)

        try:
            classification = classify_audio(classify_url, audio_file)
            if "error" in classification:
                print(f"  [API] Server error, using mock classification.")
                classification = _mock_classification(audio_file.name)
        except Exception as e:
            print(f"  [API] Error calling classifier: {e}")
            classification = _mock_classification(audio_file.name)

        log_data = {
            "request": {
                "url": classify_url,
                "file": audio_file.name,
                "latitude": lat,
                "longitude": lon,
                "timestamp": datetime.utcnow().isoformat(),
            },
            "response": classification,
        }
        log_object_name = store_classification_log(minio_client, log_bucket, log_data)

        store_classification_result(
            db=db,
            collection_name=config["mongodb"]["collections"]["classifications"],
            taxonomy_collection_name=config["mongodb"]["collections"]["taxonomy"],
            file_name=audio_file.name,
            object_name=object_name,
            log_object_name=log_object_name,
            classification=classification,
            latitude=lat,
            longitude=lon,
        )

    mongo_client.close()
    print(f"\n[Step 3] Audio processing complete. Processed {len(audio_files)} file(s).")


def _mock_classification(filename: str) -> dict:
    """Mock classification using real species from aves.json taxonomy."""
    import random

    # Real species from aves.json
    species_pool = [
        {"scientificName": "Guttera pucherani", "vernacularName": "Crested Guineafowl", "confidence": 0.92},
        {"scientificName": "Numida meleagris", "vernacularName": "Helmeted Guineafowl", "confidence": 0.87},
        {"scientificName": "Agelastes meleagrides", "vernacularName": "White-breasted Guineafowl", "confidence": 0.78},
        {"scientificName": "Perdicula asiatica", "vernacularName": "Jungle Bush Quail", "confidence": 0.85},
        {"scientificName": "Acryllium vulturinum", "vernacularName": "Vulturine Guineafowl", "confidence": 0.91},
        {"scientificName": "Perdicula manipurensis", "vernacularName": "Manipur Bush Quail", "confidence": 0.83},
    ]
    selected = random.sample(species_pool, k=random.randint(1, 3))
    return {
        "fileName": filename,
        "results": selected,
        "species": selected,
        "processingTime": round(__import__('random').uniform(0.5, 2.5), 3),
        "mock": True,
    }


if __name__ == "__main__":
    run()
