using MedicalApp.Data;
using MedicalApp.Models;
using MedicalApp.Repositories;
using MedicalApp.Services;
using MyORM.Migrations;

// ─── Connection string ────────────────────────────────────────────────────────
const string connectionString =
    "Host=localhost;Port=5432;Database=medical_db;Username=admin;Password=admin123;";

Console.WriteLine("╔════════════════════════════════════════════╗");
Console.WriteLine("║        MEDICINSKI SUSTAV  v1.0             ║");
Console.WriteLine("║     Powered by MyORM (vlastiti ORM)        ║");
Console.WriteLine("╚════════════════════════════════════════════╝");
Console.WriteLine();

// ─── Initialise DB context ────────────────────────────────────────────────────
using var db = new MedicalDbContext(connectionString);

// ─── Run migrations ───────────────────────────────────────────────────────────
Console.WriteLine("[Startup] Pokrećem migracije...");
var migrationEngine = new MigrationEngine(db.GetConnection());
migrationEngine.Apply("InitialMigration_v1", MedicalDbContext.AllEntityTypes);
Console.WriteLine("[Startup] Migracije završene.\n");

// ─── Seed doctors on first run ────────────────────────────────────────────────
SeedService.SeedDoctors(db);

// ─── Wire up services ─────────────────────────────────────────────────────────
var patientService = new PatientService(db);
var medHistoryService = new MedicalHistoryService(db, patientService);
var prescriptionService = new PrescriptionService(db, patientService);
var medicationService = new MedicationService(db);
var appointmentService = new AppointmentService(db, patientService);

// ─── Main menu loop ───────────────────────────────────────────────────────────
while (true)
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("  ══════════════ GLAVNI IZBORNIK ══════════════");
    Console.ResetColor();
    Console.WriteLine("  [1] Pacijenti");
    Console.WriteLine("  [2] Povijest bolesti");
    Console.WriteLine("  [3] Recepti i lijekovi");
    Console.WriteLine("  [4] Lijekovi");
    Console.WriteLine("  [5] Specijalisti»ki pregledi");
    Console.WriteLine("  [6] Prikaz liječnika");
    Console.WriteLine("  [7] Migracije (napredno)");
    Console.WriteLine("  [0] Izlaz");
    Console.Write("\n  Odabir: ");

    switch (Console.ReadLine()?.Trim())
    {
        case "1":
            patientService.ManageMenu();
            break;

        case "2":
            medHistoryService.ManageMenu();
            break;

        case "3":
            prescriptionService.ManageMenu();
            break;

        case "4":
            medicationService.ManageMenu();
            break;

        case "5":
            appointmentService.ManageMenu();
            break;

        case "6":
            ShowDoctors(db);
            break;

        case "7":
            MigrationMenu(migrationEngine);
            break;

        case "0":
            Console.WriteLine("\n  Doviđenja!");
            return;

        default:
            ConsoleMenu.PrintError("Nevažeći odabir. Molimo unesite broj od 0-7.");
            break;
    }
}

// ─── Helper: show doctors (read-only) ────────────────────────────────────────
static void ShowDoctors(MedicalDbContext db)
{
    ConsoleMenu.PrintHeader("LIJEČNICI U SUSTAVU");
    var doctors = db.Doctors.OrderBy(d => d.LastName).ToList();
    if (!doctors.Any()) { ConsoleMenu.PrintInfo("Nema liječnika."); return; }

    Console.WriteLine($"  {"ID",-5} {"Ime",-25} {"Specijalizacija",-25}");
    Console.WriteLine($"  {new string('-', 58)}");
    foreach (var d in doctors)
        Console.WriteLine($"  {d.Id,-5} {d.FullName,-25} {d.Specialization,-25}");
}

// ─── Helper: migration management menu ───────────────────────────────────────
static void MigrationMenu(MigrationEngine engine)
{
    ConsoleMenu.PrintHeader("UPRAVLJANJE MIGRACIJAMA");
    Console.WriteLine("  [1] Prikaz primijenjenih migracija");
    Console.WriteLine("  [2] Rollback zadnje migracije");
    Console.WriteLine("  [0] Natrag");
    Console.Write("\n  Odabir: ");

    switch (Console.ReadLine()?.Trim())
    {
        case "1":
            var migrations = engine.GetAppliedMigrations();
            if (!migrations.Any()) { ConsoleMenu.PrintInfo("Nema primijenjenih migracija."); break; }
            foreach (var m in migrations)
                Console.WriteLine($"  [{m.Id}] {m.Name} – primijenjena {m.AppliedAt:dd.MM.yyyy HH:mm}");
            break;

        case "2":
            Console.Write("  Jeste li sigurni? Ovo može uništiti podatke! (da/ne): ");
            if (Console.ReadLine()?.Trim().ToLower() == "da")
                engine.Rollback();
            break;

        case "0":
            break;
    }
}
