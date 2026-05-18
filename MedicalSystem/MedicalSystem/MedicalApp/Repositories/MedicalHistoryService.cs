using MedicalApp.Data;
using MedicalApp.Models;
using MedicalApp.Services;

namespace MedicalApp.Repositories;

public class MedicalHistoryService
{
    private readonly MedicalDbContext _db;
    private readonly PatientService _patientService;

    public MedicalHistoryService(MedicalDbContext db, PatientService patientService)
    {
        _db = db;
        _patientService = patientService;
    }

    public void ManageMenu()
    {
        while (true)
        {
            ConsoleMenu.PrintHeader("POVIJEST BOLESTI");
            Console.WriteLine("  [1] Pregled povijesti bolesti pacijenta");
            Console.WriteLine("  [2] Dodaj zapis");
            Console.WriteLine("  [3] Zatvori dijagnozu (postavi datum kraja)");
            Console.WriteLine("  [4] Obriši zapis");
            Console.WriteLine("  [0] Natrag");
            Console.Write("\n  Odabir: ");

            switch (Console.ReadLine()?.Trim())
            {
                case "1": ViewForPatient(); break;
                case "2": AddRecord(); break;
                case "3": CloseRecord(); break;
                case "4": DeleteRecord(); break;
                case "0": return;
                default: ConsoleMenu.PrintError("Nevažeći odabir."); break;
            }
        }
    }

    private void ViewForPatient()
    {
        var patient = _patientService.SelectPatient();
        if (patient == null) return;

        var records = _db.MedicalHistories
            .Where(h => h.PatientId == patient.Id)
            .OrderBy(h => h.FromDate)
            .ToList();

        Console.WriteLine($"\n  Povijest bolesti – {patient.FullName}:");
        if (!records.Any()) { ConsoleMenu.PrintInfo("Nema zapisa."); return; }

        foreach (var r in records)
        {
            var to = r.ToDate.HasValue ? r.ToDate.Value.ToString("dd.MM.yyyy") : "aktivno";
            Console.WriteLine($"  [{r.Id}] {r.Diagnosis} | {r.FromDate:dd.MM.yyyy} – {to}");
        }
    }

    private void AddRecord()
    {
        var patient = _patientService.SelectPatient();
        if (patient == null) return;

        ConsoleMenu.PrintHeader("NOVI ZAPIS POVIJESTI BOLESTI");
        var record = new MedicalHistory
        {
            PatientId = patient.Id,
            Diagnosis = ConsoleMenu.ReadRequired("Dijagnoza"),
            FromDate = ConsoleMenu.ReadDate("Datum početka"),
        };

        Console.Write("  Unesi datum kraja? (da/ne): ");
        if (Console.ReadLine()?.Trim().ToLower() == "da")
            record.ToDate = ConsoleMenu.ReadDate("Datum kraja");

        _db.MedicalHistories.Add(record);
        ConsoleMenu.PrintSuccess("Zapis dodan.");
    }

    private void CloseRecord()
    {
        var patient = _patientService.SelectPatient();
        if (patient == null) return;

        var records = _db.MedicalHistories
            .Where(h => h.PatientId == patient.Id)
            .ToList()
            .Where(h => !h.ToDate.HasValue)
            .ToList();

        if (!records.Any()) { ConsoleMenu.PrintInfo("Nema aktivnih dijagnoza."); return; }

        int idx = ConsoleMenu.SelectFromList(records, r => $"{r.Diagnosis} (od {r.FromDate:dd.MM.yyyy})", "Odaberi dijagnozu");
        records[idx].ToDate = ConsoleMenu.ReadDate("Datum završetka");

        _db.MedicalHistories.Update(records[idx]);
        ConsoleMenu.PrintSuccess("Dijagnoza zatvorena.");
    }

    private void DeleteRecord()
    {
        var patient = _patientService.SelectPatient();
        if (patient == null) return;

        var records = _db.MedicalHistories.Where(h => h.PatientId == patient.Id).ToList();
        if (!records.Any()) { ConsoleMenu.PrintInfo("Nema zapisa."); return; }

        int idx = ConsoleMenu.SelectFromList(records, r => $"{r.Diagnosis} ({r.FromDate:dd.MM.yyyy})", "Odaberi za brisanje");
        _db.MedicalHistories.Delete(records[idx]);
        ConsoleMenu.PrintSuccess("Zapis obrisan.");
    }
}
