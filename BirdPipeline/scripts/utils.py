"""
Shared utilities used across all pipeline steps.
"""
import yaml
import os


def load_config(config_path: str = None) -> dict:
    """Load configuration from config.yaml."""
    if config_path is None:
        # Look for config.yaml relative to this file or in current directory
        base = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
        config_path = os.path.join(base, "config.yaml")

    with open(config_path, "r", encoding="utf-8") as f:
        return yaml.safe_load(f)


def get_mongodb_client(config: dict):
    """Return a connected MongoDB client."""
    from pymongo import MongoClient
    return MongoClient(config["mongodb"]["uri"])


def get_minio_client(config: dict):
    """Return a connected MinIO client."""
    from minio import Minio
    return Minio(
        config["minio"]["endpoint"],
        access_key=config["minio"]["access_key"],
        secret_key=config["minio"]["secret_key"],
        secure=config["minio"]["secure"],
    )


def ensure_minio_bucket(minio_client, bucket_name: str):
    """Create MinIO bucket if it doesn't exist."""
    if not minio_client.bucket_exists(bucket_name):
        minio_client.make_bucket(bucket_name)
        print(f"[MinIO] Created bucket: {bucket_name}")
    else:
        print(f"[MinIO] Bucket already exists: {bucket_name}")
