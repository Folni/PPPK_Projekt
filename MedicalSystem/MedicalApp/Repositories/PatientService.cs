using MedicalApp.Data;
using MedicalApp.Models;
using MedicalApp.Services;

namespace MedicalApp.Repositories;

public class PatientService
{
    private readonly MedicalDbContext _db;

    public PatientService(MedicalDbContext db) => _db = db;

    public void ManageMenu()
    {
        while (true)
        {
            ConsoleMenu.PrintHeader("UPRAVLJANJE PACIJENTIMA");
            Console.WriteLine("  [1] Pregled svih pacijenata");
            Console.WriteLine("  [2] Dodaj pacijenta");
            Console.WriteLine("  [3] Uredi pacijenta");
            Console.WriteLine("  [4] Obriši pacijenta");
            Console.WriteLine("  [5] Pretraži po imenu");
            Console.WriteLine("  [0] Natrag");
            Console.Write("\n  Odabir: ");

            switch (Console.ReadLine()?.Trim())
            {
                case "1": ListAll(); break;
                case "2": Create(); break;
                case "3": Edit(); break;
                case "4": Delete(); break;
                case "5": Search(); break;
                case "0": return;
                default: ConsoleMenu.PrintError("Nevažeći odabir."); break;
            }
        }
    }

    private void ListAll()
    {
        var patients = _db.Patients.OrderBy(p => p.LastName).ToList();
        Console.WriteLine();
        if (!patients.Any()) { ConsoleMenu.PrintInfo("Nema pacijenata u bazi."); return; }

        Console.WriteLine($"  {"ID",-5} {"Ime",-20} {"OIB",-15} {"Datum rođenja",-15} {"Spol",-6}");
        Console.WriteLine($"  {new string('-', 65)}");
        foreach (var p in patients)
            Console.WriteLine($"  {p.Id,-5} {p.FullName,-20} {p.OIB,-15} {p.DateOfBirth:dd.MM.yyyy,-15} {p.Gender,-6}");
    }

    private void Create()
    {
        ConsoleMenu.PrintHeader("NOVI PACIJENT");
        var patient = new Patient
        {
            FirstName = ConsoleMenu.ReadRequired("Ime"),
            LastName = ConsoleMenu.ReadRequired("Prezime"),
            OIB = ConsoleMenu.ReadRequired("OIB (11 znakova)"),
            DateOfBirth = ConsoleMenu.ReadDate("Datum rođenja"),
            Gender = ConsoleMenu.ReadRequired("Spol (M/Ž)"),
            ResidenceAddress = ConsoleMenu.ReadOptional("Adresa boravišta"),
            DomicileAddress = ConsoleMenu.ReadOptional("Adresa prebivališta")
        };

        var saved = _db.Patients.Add(patient);
        ConsoleMenu.PrintSuccess($"Pacijent '{saved.FullName}' (ID: {saved.Id}) uspješno dodan.");
    }

    private void Edit()
    {
        var patient = SelectPatient();
        if (patient == null) return;

        ConsoleMenu.PrintHeader($"UREDI: {patient.FullName}");
        ConsoleMenu.PrintInfo("Ostavite prazno za zadržavanje trenutne vrijednosti.");

        var newFirst = ConsoleMenu.ReadOptional($"Ime ({patient.FirstName})");
        var newLast = ConsoleMenu.ReadOptional($"Prezime ({patient.LastName})");
        var newAddress = ConsoleMenu.ReadOptional($"Adresa boravišta ({patient.ResidenceAddress})");

        if (newFirst != null) patient.FirstName = newFirst;
        if (newLast != null) patient.LastName = newLast;
        if (newAddress != null) patient.ResidenceAddress = newAddress;

        // ChangeTracker detects what changed and generates minimal UPDATE
        _db.Patients.Update(patient);
        ConsoleMenu.PrintSuccess("Pacijent ažuriran.");
    }

    private void Delete()
    {
        var patient = SelectPatient();
        if (patient == null) return;

        Console.Write($"  Jeste li sigurni da želite obrisati '{patient.FullName}'? (da/ne): ");
        if (Console.ReadLine()?.Trim().ToLower() != "da") { ConsoleMenu.PrintInfo("Brisanje otkazano."); return; }

        _db.Patients.Delete(patient);
        ConsoleMenu.PrintSuccess("Pacijent obrisan.");
    }

    private void Search()
    {
        var term = ConsoleMenu.ReadRequired("Pretraži po imenu/prezimenu");
        var results = _db.Patients
            .Where(p => p.FirstName.Contains(term) || p.LastName.Contains(term))
            .OrderBy(p => p.LastName)
            .ToList();

        Console.WriteLine($"\n  Pronađeno: {results.Count}");
        foreach (var p in results)
            Console.WriteLine($"  [{p.Id}] {p.FullName} – OIB: {p.OIB}");
    }

    public Patient? SelectPatient()
    {
        var patients = _db.Patients.OrderBy(p => p.LastName).ToList();
        if (!patients.Any()) { ConsoleMenu.PrintInfo("Nema pacijenata."); return null; }

        Console.WriteLine();
        int idx = ConsoleMenu.SelectFromList(patients, p => $"{p.FullName} (OIB: {p.OIB})", "Odaberite pacijenta");
        return patients[idx];
    }
}
