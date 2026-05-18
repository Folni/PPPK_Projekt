using MedicalApp.Data;
using MedicalApp.Models;
using MedicalApp.Services;

namespace MedicalApp.Repositories;

public class PrescriptionService
{
    private readonly MedicalDbContext _db;
    private readonly PatientService _patientService;

    public PrescriptionService(MedicalDbContext db, PatientService patientService)
    {
        _db = db;
        _patientService = patientService;
    }

    public void ManageMenu()
    {
        while (true)
        {
            ConsoleMenu.PrintHeader("UPRAVLJANJE RECEPTIMA");
            Console.WriteLine("  [1] Pregled recepata pacijenta");
            Console.WriteLine("  [2] Prepiši lijek");
            Console.WriteLine("  [3] Obriši recept");
            Console.WriteLine("  [0] Natrag");
            Console.Write("\n  Odabir: ");

            switch (Console.ReadLine()?.Trim())
            {
                case "1": ViewForPatient(); break;
                case "2": AddPrescription(); break;
                case "3": DeletePrescription(); break;
                case "0": return;
                default: ConsoleMenu.PrintError("Nevažeći odabir."); break;
            }
        }
    }

    private void ViewForPatient()
    {
        var patient = _patientService.SelectPatient();
        if (patient == null) return;

        // Eager loading – Include("Medication") učitava lijekove u istom upitu
        var prescriptions = _db.Prescriptions
            .Where(p => p.PatientId == patient.Id)
            .Include("Medication")
            .ToList();

        Console.WriteLine($"\n  Recepti za {patient.FullName}:");
        if (!prescriptions.Any()) { ConsoleMenu.PrintInfo("Nema recepata."); return; }

        foreach (var p in prescriptions)
        {
            var med = p.Medication;
            Console.WriteLine($"  [{p.Id}] {med?.Name ?? "N/A"} | Doza: {med?.Dose} | Učestalost: {med?.Frequency} | Stanje: {p.ConditionName}");
        }
    }

    private void AddPrescription()
    {
        var patient = _patientService.SelectPatient();
        if (patient == null) return;

        var medications = _db.Medications.OrderBy(m => m.Name).ToList();
        if (!medications.Any()) { ConsoleMenu.PrintInfo("Nema lijekova u bazi. Dodajte lijekove prvo."); return; }

        ConsoleMenu.PrintHeader("NOVI RECEPT");
        int medIdx = ConsoleMenu.SelectFromList(medications, m => $"{m.Name} ({m.Dose}, {m.Frequency})", "Odaberi lijek");

        var prescription = new Prescription
        {
            PatientId = patient.Id,
            MedicationId = medications[medIdx].Id,
            ConditionName = ConsoleMenu.ReadRequired("Stanje/dijagnoza za koje se propisuje"),
            PrescribedAt = DateTime.UtcNow
        };

        _db.Prescriptions.Add(prescription);
        ConsoleMenu.PrintSuccess("Recept dodan.");
    }

    private void DeletePrescription()
    {
        var patient = _patientService.SelectPatient();
        if (patient == null) return;

        var prescriptions = _db.Prescriptions
            .Where(p => p.PatientId == patient.Id)
            .Include("Medication")
            .ToList();

        if (!prescriptions.Any()) { ConsoleMenu.PrintInfo("Nema recepata."); return; }

        int idx = ConsoleMenu.SelectFromList(
            prescriptions,
            p => $"{p.Medication?.Name ?? "N/A"} – {p.ConditionName}",
            "Odaberi recept za brisanje"
        );

        _db.Prescriptions.Delete(prescriptions[idx]);
        ConsoleMenu.PrintSuccess("Recept obrisan.");
    }
}
