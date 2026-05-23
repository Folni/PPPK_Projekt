import sys, os, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import requests
from utils import load_config, get_mongodb_client

def get_english_name(species):
    names = species.get("vernacularNames", [])
    for n in names:
        if n.get("language") == "eng":
            return n.get("vernacularName", "")
    return species.get("canonicalName", "")

def fetch_taxonomy(config):
    base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    local_file = os.path.join(base_dir, "aves.json")

    if os.path.exists(local_file):
        print(f"[Step 1] Loading from local file: {local_file}")
        with open(local_file, "r", encoding="utf-8") as f:
            data = json.load(f)
        species_list = data if isinstance(data, list) else []
        print(f"[Step 1] Loaded {len(species_list)} species.")
        return species_list

    print("[Step 1] Local file not found, trying API...")
    try:
        r = requests.get(config["aves_api"]["base_url"] + "/aves.json", timeout=120)
        r.raise_for_status()
        data = r.json()
        return data if isinstance(data, list) else []
    except Exception as e:
        print(f"[Step 1] WARNING: {e}")
        return []

def store_taxonomy(config, species_list):
    client = get_mongodb_client(config)
    db = client[config["mongodb"]["database"]]
    col = db[config["mongodb"]["collections"]["taxonomy"]]
    col.create_index("key", unique=True)
    ins, skipped = 0, 0
    for s in species_list:
        try:
            s["vernacularNameEng"] = get_english_name(s)
            col.insert_one(s)
            ins += 1
        except:
            skipped += 1
    print(f"[Step 1] Inserted: {ins}, Skipped: {skipped}")
    client.close()

def run(config_path=None):
    config = load_config(config_path)
    client = get_mongodb_client(config)
    db = client[config["mongodb"]["database"]]
    col = db[config["mongodb"]["collections"]["taxonomy"]]
    if col.count_documents({}) > 0:
        print(f"[Step 1] Already has {col.count_documents({})} docs - skipping.")
        return
    species_list = fetch_taxonomy(config)
    if not species_list:
        print("[Step 1] No data.")
        return
    store_taxonomy(config, species_list)
    print("[Step 1] Complete.")

if __name__ == "__main__":
    run()