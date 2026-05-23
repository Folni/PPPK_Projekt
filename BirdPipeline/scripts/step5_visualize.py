"""
Step 5 – Visualization
======================
Generates a visual chart from the CSV report.
"""
import os
import pandas as pd
import matplotlib.pyplot as plt
from utils import load_config

def run(config_path: str = None):
    config = load_config(config_path)
    
    base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    csv_path = os.path.join(
        base_dir, 
        config["pipeline"]["output_dir"], 
        config["pipeline"]["output_file"]
    )
    
    if not os.path.exists(csv_path):
        print(f"[Step 5] CSV report not found at {csv_path}. Run step 4 first.")
        return

    # Load data
    df = pd.read_csv(csv_path)
    
    if df.empty:
        print("[Step 5] CSV is empty, skipping visualization.")
        return

    # Plot Top 10 species by classification count
    df_sorted = df.sort_values(by="classificationCount", ascending=False).head(10)
    
    plt.figure(figsize=(12, 6))
    plt.bar(df_sorted["scientificName"], df_sorted["classificationCount"], color='skyblue')
    plt.xlabel("Species (Scientific Name)")
    plt.ylabel("Number of Observations")
    plt.title("Top 10 Detected Bird Species")
    plt.xticks(rotation=45, ha='right')
    plt.tight_layout()

    # Save plot
    output_img = csv_path.replace(".csv", ".png")
    plt.savefig(output_img)
    print(f"[Step 5] Visualization saved to: {output_img}")

if __name__ == "__main__":
    try:
        run()
    except Exception as e:
        print(f"[Step 5] Error during visualization: {e}")