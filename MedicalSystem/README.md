# MedicalSystem – Vlastiti ORM (PPPK Projektni Zadatak)

## Tehnologije
- **C# / .NET 8** – programski jezik
- **PostgreSQL 16** – relacijska baza podataka (pokrenuta kao Docker kontejner)
- **Npgsql** – low-level PostgreSQL driver (ORM NIJE koristio Entity Framework)
- **MyORM** – vlastita implementacija ORM biblioteke

---

## Struktura projekta

```
MedicalSystem/
├── docker-compose.yml          # PostgreSQL kontejner
├── MedicalSystem.sln
│
├── MyORM/                      ← Vlastita ORM biblioteka
│   ├── Attributes/
│   │   ├── TableAttribute.cs       # Mapiranje klase → tablica
│   │   ├── ColumnAttribute.cs      # Mapiranje property → kolona (NULL, UNIQUE, DEFAULT)
│   │   ├── PrimaryKeyAttribute.cs  # PK + auto-increment
│   │   ├── ForeignKeyAttribute.cs  # FK veze između tablica
│   │   └── NavigationAttribute.cs  # Navigacijska svojstva (eager/lazy)
│   ├── Core/
│   │   ├── DbContext.cs            # Baza kontekst – konekcija, inicijalizacija DbSetova
│   │   ├── DbSet.cs                # CRUD, filtriranje, sortiranje, eager loading
│   │   └── ChangeTracker.cs        # Praćenje stanja objekata, detekcija promjena
│   ├── Mapping/
│   │   └── EntityMapper.cs         # Refleksija: klasa↔tablica, C#→SQL tipovi
│   ├── Migrations/
│   │   └── MigrationEngine.cs      # Generiranje, primjena i rollback migracija
│   └── Query/
│       └── WhereExpressionVisitor.cs # Parsiranje LINQ izraza → SQL WHERE
│
└── MedicalApp/                 ← Konzolna aplikacija
    ├── Data/
    │   └── MedicalDbContext.cs     # Nasljeđuje DbContext, definira DbSetove
    ├── Models/
    │   └── Models.cs               # Patient, Doctor, Medication, Prescription, ...
    ├── Repositories/
    │   ├── PatientService.cs       # CRUD za pacijente
    │   ├── MedicalHistoryService.cs
    │   ├── PrescriptionService.cs
    │   ├── MedicationService.cs
    │   └── AppointmentService.cs
    ├── Services/
    │   ├── SeedService.cs          # Seed liječnika pri prvom pokretanju
    │   └── ConsoleMenu.cs          # Helperi za konzolni UI
    └── Program.cs                  # Entry point, main menu
```

---

## Pokretanje

### 1. Preduvjeti
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)

### 2. Pokretanje PostgreSQL-a
```bash
docker compose up -d
```

Provjeri radi li:
```bash
docker ps
```

### 3. Pokretanje aplikacije
```bash
cd MedicalApp
dotnet run
```

Aplikacija automatski:
1. Spaja se na PostgreSQL
2. Generira i primjenjuje migracije (kreira tablice ako ne postoje)
3. Seeda početne liječnike (samo pri prvom pokretanju)
4. Otvara konzolni izbornik

---

## Ključni koncepti ORM-a

### Refleksija (EntityMapper.cs)
```csharp
// Čita metapodatke klase u runtime i gradi SQL
var tableAttr = type.GetCustomAttribute<TableAttribute>();
var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
```

### Atributi (deklarativno mapiranje)
```csharp
[Table("patients")]
public class Patient {
    [PrimaryKey(AutoIncrement = true)]
    [Column("id")]
    public int Id { get; set; }

    [Column("oib", IsNullable = false, IsUnique = true)]
    public string OIB { get; set; }
}
```

### Expression parsing – WHERE klauzula
```csharp
// LINQ izraz → SQL WHERE (WhereExpressionVisitor.cs)
var patients = db.Patients
    .Where(p => p.LastName.Contains("Horvat") && p.Gender == "M")
    .OrderBy(p => p.DateOfBirth)
    .ToList();
// Generira: WHERE (last_name ILIKE '%Horvat%') AND (gender = 'M') ORDER BY date_of_birth
```

### ChangeTracker – automatska detekcija promjena
```csharp
// ChangeTracker sprema snapshot pri dohvatu
var patient = db.Patients.FirstOrDefault(p => p.Id == 1);
patient.FirstName = "Novo ime"; // promjena

// Update generira SAMO promijenjene kolone:
// UPDATE patients SET first_name = 'Novo ime' WHERE id = 1
db.Patients.Update(patient);
```

### Eager Loading
```csharp
// Include učitava povezane entitete u istom upitu
var prescriptions = db.Prescriptions
    .Where(p => p.PatientId == patientId)
    .Include("Medication")  // → dodatni SELECT JOIN
    .ToList();
```

### Migracije
```csharp
// MigrationEngine uspoređuje information_schema s definicijama klasa
// i generira ALTER TABLE naredbe za razlike
migrationEngine.Apply("MigrationName_v1", entityTypes);
migrationEngine.Rollback(); // vraća zadnju migraciju unazad
```

---

## Eager vs Lazy Loading

| | Eager Loading | Lazy Loading |
|---|---|---|
| **Kada** | Odmah pri dohvatu | Pri prvom pristupu property |
| **SQL upita** | 2 (main + related) | N+1 problem |
| **Koristi se** | `.Include("Property")` | Automatski, transparentno |
| **Nedostatak** | Može učitati nepotrebne podatke | N+1 problem kod kolekcija |

---

## ACID u PostgreSQL-u

- **Atomicity** – WAL (Write-Ahead Log): sve promjene se bilježe prije izvršavanja
- **Consistency** – constraints (FK, UNIQUE, NOT NULL) provjeravaju integritet
- **Isolation** – MVCC (Multi-Version Concurrency Control): čitanje ne blokira pisanje
- **Durability** – Checkpointer periodično upisuje WAL na disk; Vacuum čisti stare verzije redova
