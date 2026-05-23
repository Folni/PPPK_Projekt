"""
Step 2 – Kafka Observations
============================
Reads all current messages from the Kafka broker on the configured topic.
Each message contains a bird observation with:
  - taxon code (identifies the species)
  - geographic location (lat/lon)
  - variable biological data (body size, temperature, migration status, etc.)

All observations are stored in MongoDB. Because different observations may
have different biological properties, MongoDB's flexible schema is ideal here.

MongoDB collection: observations
"""
import sys
import os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import json
from datetime import datetime
from utils import load_config, get_mongodb_client


def consume_kafka_messages(config: dict) -> list[dict]:
    """
    Consume all available messages from Kafka topic.
    Uses auto_offset_reset='earliest' to read from the beginning,
    and stops when no new messages arrive for a short timeout.
    """
    from kafka import KafkaConsumer

    bootstrap_servers = config["kafka"]["bootstrap_servers"]
    topic = config["kafka"]["topic"]
    group_id = config["kafka"]["group_id"]

    print(f"[Step 2] Connecting to Kafka at {bootstrap_servers}, topic: {topic}")

    consumer = KafkaConsumer(
        topic,
        bootstrap_servers=bootstrap_servers,
        group_id=group_id,
        auto_offset_reset="earliest",
        enable_auto_commit=True,
        value_deserializer=lambda m: json.loads(m.decode("utf-8")),
        consumer_timeout_ms=5000,  # Stop after 5s of no messages
    )

    messages = []
    for message in consumer:
        messages.append(message.value)

    consumer.close()
    print(f"[Step 2] Consumed {len(messages)} messages from Kafka.")
    return messages


def store_observations(config: dict, observations: list[dict]):
    """Store observations in MongoDB."""
    client = get_mongodb_client(config)
    db = client[config["mongodb"]["database"]]
    collection = db[config["mongodb"]["collections"]["observations"]]

    if not observations:
        print("[Step 2] No observations to store.")
        client.close()
        return

    # Add ingestion timestamp to each observation
    for obs in observations:
        obs["_ingested_at"] = datetime.utcnow()

    result = collection.insert_many(observations)
    print(f"[Step 2] Stored {len(result.inserted_ids)} observations in MongoDB.")
    client.close()


def seed_kafka_test_messages(config: dict):
    """
    Seeds test messages into Kafka for development/testing.
    Run this manually if you need test data.
    """
    from kafka import KafkaProducer

    producer = KafkaProducer(
        bootstrap_servers=config["kafka"]["bootstrap_servers"],
        value_serializer=lambda v: json.dumps(v).encode("utf-8"),
    )

    test_observations = [
        {
            "taxonKey": 1,
            "scientificName": "Parus major",
            "latitude": 45.8150,
            "longitude": 15.9819,
            "bodySize": "small",
            "bodyTemperature": 41.5,
            "migrationStatus": "resident",
            "flightPattern": "undulating",
            "habitat": "woodland",
            "observedBy": "ornithologist_01",
            "observedAt": "2024-03-15T08:30:00Z"
        },
        {
            "taxonKey": 2,
            "scientificName": "Turdus merula",
            "latitude": 45.8200,
            "longitude": 15.9900,
            "bodySize": "medium",
            "migrationStatus": "partial migrant",
            "habitat": "urban garden",
            "observedBy": "ornithologist_02",
            "observedAt": "2024-03-15T09:15:00Z"
        },
        {
            "taxonKey": 3,
            "scientificName": "Erithacus rubecula",
            "latitude": 45.7900,
            "longitude": 15.9600,
            "bodySize": "small",
            "bodyTemperature": 42.0,
            "migrationStatus": "partial migrant",
            "flightPattern": "direct",
            "habitat": "forest edge",
            "wingspanCm": 21,
            "observedBy": "ornithologist_01",
            "observedAt": "2024-03-16T07:45:00Z"
        },
        {
            "taxonKey": 1,
            "scientificName": "Parus major",
            "latitude": 45.8300,
            "longitude": 16.0100,
            "bodySize": "small",
            "habitat": "park",
            "nestingBehavior": "cavity nesting",
            "observedBy": "ornithologist_03",
            "observedAt": "2024-03-17T10:00:00Z"
        },
        {
            "taxonKey": 9,
            "scientificName": "Falco tinnunculus",
            "latitude": 45.7500,
            "longitude": 15.9200,
            "bodySize": "medium",
            "migrationStatus": "resident",
            "flightPattern": "hovering",
            "habitat": "open countryside",
            "preyType": "small mammals",
            "observedBy": "ornithologist_02",
            "observedAt": "2024-03-17T14:30:00Z"
        },
    ]

    topic = config["kafka"]["topic"]
    for obs in test_observations:
        producer.send(topic, obs)

    producer.flush()
    producer.close()
    print(f"[Seed] Sent {len(test_observations)} test messages to Kafka topic '{topic}'.")


def run(config_path: str = None):
    config = load_config(config_path)

    try:
        observations = consume_kafka_messages(config)
    except Exception as e:
        print(f"[Step 2] WARNING: Could not connect to Kafka: {e}")
        print("[Step 2] Skipping Kafka step.")
        return

    store_observations(config, observations)
    print("[Step 2] Kafka observations step complete.")


if __name__ == "__main__":
    import sys
    if len(sys.argv) > 1 and sys.argv[1] == "--seed":
        config = load_config()
        seed_kafka_test_messages(config)
    else:
        run()
