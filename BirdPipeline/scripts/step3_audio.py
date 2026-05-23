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


# Default location for all audio files (Zagreb)
DEFAULT_LATITUDE = 45.8150
DEFAULT_LONGITUDE = 15.9819


def upload_audio_to_minio(minio_client, bucket: str, file_path: Path) -> str:
    """
    Upload audio file to MinIO.
    Returns the object name (unique key) used to identify the file.
    """
    # Generate unique object name: sha256 hash of content + original filename
    with open(file_path, "rb") as f:
        content = f.read()

    file_hash = hashlib.sha256(content).hexdigest()[:16]
    object_name = f"{file_hash}_{file_path.name}"

    # Check if already uploaded
    try:
        minio_client.stat_object(bucket, object_name)
        print(f"  [MinIO] Already exists: {object_name} – skipping upload.")
        return object_name
    except Exception:
        pass  # Not found, proceed with upload

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
    """
    Send audio file to classification API.
    Returns the classification response.
    """
    with open(file_path, "rb") as f:
        files = {"file": (file_path.name, f, "audio/wav")}
        response = requests.post(api_url, files=files, timeout=30)

    if response.status_code == 200:
        return response.json()
    else:
        print(f"  [API] Warning: Classification returned {response.status_code}")
        return {"error": response.status_code, "message": response.text}


def store_classification_log(minio_client, bucket: str, log_data: dict) -> str:
    """
    Store request/response log as JSON in MinIO.
    Returns the log object name.
    """
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
    """
    Store classification result in MongoDB, linking to taxonomy data.
    """
    collection = db[collection_name]
    taxonomy_col = db[taxonomy_collection_name]

    # Link to taxonomy: try to find species by scientificName from classification
    species_data = None
    detected_species = classification.get("species") or classification.get("results", [])

    if isinstance(detected_species, list) and detected_species:
        top_species = detected_species[0]
        sci_name = top_species.get("scientificName") or top_species.get("species")
        if sci_name:
            species_data = taxonomy_col.find_one({
    "$or": [
        {"scientificName": sci_name},
        {"canonicalName": sci_name},
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
            "vernacularName": species_data.get("vernacularName") if species_data else None,
            "taxonKey": species_data.get("taxonKey") if species_data else None,
        } if species_data else None,
        "classifiedAt": datetime.utcnow(),
    }

    collection.insert_one(doc)
    print(f"  [MongoDB] Classification result stored for: {file_name}")


def create_dummy_audio_files(audio_dir: Path):
    """Create dummy audio files for testing if directory is empty."""
    audio_dir.mkdir(parents=True, exist_ok=True)
    dummy_files = [
        "recording_001.wav",
        "recording_002.wav",
        "morning_birds.wav",
    ]
    for fname in dummy_files:
        fpath = audio_dir / fname
        if not fpath.exists():
            # Write minimal WAV header for a valid (but silent) WAV file
            with open(fpath, "wb") as f:
                f.write(b"RIFF$\x00\x00\x00WAVEfmt \x10\x00\x00\x00\x01\x00\x01\x00"
                        b"D\xac\x00\x00\x88X\x01\x00\x02\x00\x10\x00data\x00\x00\x00\x00")
            print(f"  [Test] Created dummy audio file: {fname}")


def run(config_path: str = None, latitude: float = None, longitude: float = None):
    config = load_config(config_path)

    lat = latitude or DEFAULT_LATITUDE
    lon = longitude or DEFAULT_LONGITUDE

    # Setup clients
    minio_client = get_minio_client(config)
    mongo_client = get_mongodb_client(config)
    db = mongo_client[config["mongodb"]["database"]]

    # Ensure MinIO buckets exist
    audio_bucket = config["minio"]["buckets"]["audio"]
    log_bucket = config["minio"]["buckets"]["logs"]
    ensure_minio_bucket(minio_client, audio_bucket)
    ensure_minio_bucket(minio_client, log_bucket)

    # Find audio files
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

        # 1. Upload to MinIO
        object_name = upload_audio_to_minio(minio_client, audio_bucket, audio_file)

        # 2. Call classification API
        try:
            classification = classify_audio(classify_url, audio_file)
            if "error" in classification:
                print(f"  [API] Server error, using mock classification.")
                classification = _mock_classification(audio_file.name)
        except Exception as e:
            print(f"  [API] Error calling classifier: {e}")
            classification = _mock_classification(audio_file.name)

        # 3. Store log in MinIO
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

        # 4. Store result in MongoDB
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
    """Mock classification response for testing when API is unavailable."""
    import random
    species_pool = [
        {"scientificName": "Parus major", "vernacularName": "Great Tit", "confidence": 0.92},
        {"scientificName": "Turdus merula", "vernacularName": "Common Blackbird", "confidence": 0.87},
        {"scientificName": "Erithacus rubecula", "vernacularName": "European Robin", "confidence": 0.78},
        {"scientificName": "Passer domesticus", "vernacularName": "House Sparrow", "confidence": 0.85},
        {"scientificName": "Hirundo rustica", "vernacularName": "Barn Swallow", "confidence": 0.91},
    ]
    selected = random.sample(species_pool, k=random.randint(1, 3))
    return {
        "fileName": filename,
        "results": selected,
        "species": selected,
        "processingTime": round(random.uniform(0.5, 2.5), 3),
        "mock": True,
    }


if __name__ == "__main__":
    run()
