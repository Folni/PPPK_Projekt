# BirdPipeline – PPPK Projektni Zadatak (Projekt 2)

## Tehnologije
| Komponenta | Tehnologija |
|---|---|
| Jezik | Python 3.11 |
| Nerelacijska baza | MongoDB 7 (Docker) |
| Object storage | MinIO (Docker, S3-kompatibilno) |
| Message broker | Apache Kafka (Docker) |
| Orkestrator | Snakemake |
| CI/CD | GitHub Actions (ručno okidanje) |

---

## Arhitektura pipelinea

```
aves.regoch.net ──► Step 1 ──► MongoDB (taxonomy)
                                    │
Kafka broker ────► Step 2 ──► MongoDB (observations)
                                    │
audio/*.wav ─────► Step 3 ──► MinIO (audio + logs)
                           └──► MongoDB (classifications)
                                    │
                    Step 4 ──► output/bird_report.csv
```

---

## Pokretanje

### 1. Preduvjeti
- Python 3.11+
- Docker Desktop

### 2. Pokreni servise
```bash
docker compose up -d
```

Provjeri:
```bash
docker ps
# trebaju biti pokrenuti: bird_mongodb, bird_minio, bird_kafka, bird_zookeeper
```

### 3. Instaliraj Python pakete
```bash
pip install -r requirements.txt
pip install snakemake
```

### 4. (Opcionalno) Seed Kafka poruka za testiranje
```bash
python scripts/step2_kafka.py --seed
```

### 5. Pokretanje cijelog pipelinea
```bash
snakemake --cores 1
```

### 6. Pokretanje s fuzzy filterom
```bash
snakemake --cores 1 --config species_filter="Robin"
# ili
snakemake --cores 1 --config species_filter="Parus" fuzzy_threshold=60
```

### 7. Re-pokretanje (forsiraj sve korake)
```bash
snakemake --cores 1 --forceall
```

---

## Pokretanje pojedinih koraka
```bash
python scripts/step1_taxonomy.py    # Taksonomski podaci
python scripts/step2_kafka.py       # Kafka opažanja
python scripts/step3_audio.py       # Audio obrada
python scripts/step4_report.py --filter "Robin"  # CSV izvješće
```

---

## MinIO konzola
Otvori http://localhost:9001 u browseru.
- Username: `minioadmin`
- Password: `minioadmin`

---

## MongoDB
Spoji se s bilo kojim MongoDB klientom (npr. MongoDB Compass):
```
mongodb://localhost:27017
```
Database: `bird_pipeline`
Collections: `taxonomy`, `observations`, `classifications`

---

## GitHub Actions
Pipeline se može pokrenuti ručno:
1. Idi na **Actions** tab u GitHub repozitoriju
2. Odaberi **Bird Pipeline**
3. Klikni **Run workflow**
4. Upiši opcionalni `species_filter` i `fuzzy_threshold`
5. CSV report je dostupan kao artifact nakon završetka

---

## Struktura projekta
```
BirdPipeline/
├── docker-compose.yml          # MongoDB, MinIO, Kafka, Zookeeper
├── config.yaml                 # Konfiguracija (URL-ovi, bucket nazivi, ...)
├── requirements.txt            # Python ovisnosti
├── Snakefile                   # Pipeline orkestrator
├── audio/                      # Direktorij s audio datotekama (.wav, .mp3)
├── output/                     # Generirana CSV izvješća
├── scripts/
│   ├── utils.py                # Zajednički helperi (config, konekcije)
│   ├── step1_taxonomy.py       # Dohvat taksonomskih podataka → MongoDB
│   ├── step2_kafka.py          # Konzumiranje Kafka poruka → MongoDB
│   ├── step3_audio.py          # Audio upload → MinIO, klasifikacija → MongoDB
│   └── step4_report.py         # Generiranje CSV izvješća
└── .github/
    └── workflows/
        └── pipeline.yml        # GitHub Actions workflow
```

---

## Ključni koncepti za obranu

### MongoDB – zašto za ovaj projekt?
- **Fleksibilna shema** – različita opažanja mogu imati različita biološka svojstva
- **Dokumentni model** – svako opažanje je jedan dokument, nema JOIN-ova
- Idealno za nestrukturirane/polu-strukturirane podatke

### MinIO – što je i zašto?
- **S3-kompatibilni object storage** – pohranjuje binarne datoteke (audio, JSON logovi)
- Svaka datoteka je jedinstveno identificirana kombinacijom bucket + object name
- Hash-based imenovanje osigurava idempotentnost (isti file = isti key)
- Logovi klasifikacije bilježe cijeli request/response ciklus

### Kafka – message broker
- **Publish/Subscribe** model – ornitolozi objavljuju opažanja, pipeline konzumira
- `auto_offset_reset='earliest'` – čita od početka topica
- `consumer_timeout_ms=5000` – staje nakon 5s bez novih poruka
- Fleksibilni JSON payload – različiti ornitolozi šalju različita biološka polja

### Fuzzy string matching
- Koristi **Levenshtein distance** za uspoređivanje stringova
- `fuzz.ratio` – puna usporedba
- `fuzz.partial_ratio` – substring matching
- `fuzz.token_sort_ratio` – ignorira redoslijed riječi
- Threshold 70 = 70% sličnosti potrebno za match
- Primjer: `"Robin"` će matchati `"European Robin"` i `"American Robin"`

### Snakemake orkestrator
- Definira **DAG** (directed acyclic graph) ovisnosti između koraka
- Koraci se izvršavaju samo ako su inputi noviji od outputa
- `--forceall` forsi re-izvršavanje svih koraka
- `touch()` kreira sentinel datoteke za praćenje završenosti
