using MedicalApp.Data;
using MedicalApp.Models;
using MedicalApp.Services;

namespace MedicalApp.Repositories;

public class MedicationService
{
    private readonly MedicalDbContext _db;

    public MedicationService(MedicalDbContext db) => _db = db;

    public void ManageMenu()
    {
        while (true)
        {
            ConsoleMenu.PrintHeader("UPRAVLJANJE LIJEKOVIMA");
            Console.WriteLine("  [1] Pregled svih lijekova");
            Console.WriteLine("  [2] Dodaj lijek");
            Console.WriteLine("  [3] Uredi lijek");
            Console.WriteLine("  [4] Obriši lijek");
            Console.WriteLine("  [0] Natrag");
            Console.Write("\n  Odabir: ");

            switch (Console.ReadLine()?.Trim())
            {
                case "1": ListAll(); break;
                case "2": Create(); break;
                case "3": Edit(); break;
                case "4": Delete(); break;
                case "0": return;
                default: ConsoleMenu.PrintError("Nevažeći odabir."); break;
            }
        }
    }

    private void ListAll()
    {
        var meds = _db.Medications.OrderBy(m => m.Name).ToList();
        Console.WriteLine();
        if (!meds.Any()) { ConsoleMenu.PrintInfo("Nema lijekova."); return; }
        Console.WriteLine($"  {"ID",-5} {"Naziv",-25} {"Doza",-15} {"Učestalost",-20}");
        Console.WriteLine($"  {new string('-', 68)}");
        foreach (var m in meds)
            Console.WriteLine($"  {m.Id,-5} {m.Name,-25} {m.Dose,-15} {m.Frequency,-20}");
    }

    private void Create()
    {
        ConsoleMenu.PrintHeader("NOVI LIJEK");
        var med = new Medication
        {
            Name = ConsoleMenu.ReadRequired("Naziv lijeka"),
            Dose = ConsoleMenu.ReadRequired("Doza (npr. 500mg, 2 tablete)"),
            Frequency = ConsoleMenu.ReadRequired("Učestalost (npr. 3x dnevno)")
        };
        var saved = _db.Medications.Add(med);
        ConsoleMenu.PrintSuccess($"Lijek '{saved.Name}' dodan (ID: {saved.Id}).");
    }

    private void Edit()
    {
        var meds = _db.Medications.OrderBy(m => m.Name).ToList();
        if (!meds.Any()) { ConsoleMenu.PrintInfo("Nema lijekova."); return; }

        int idx = ConsoleMenu.SelectFromList(meds, m => $"{m.Name} ({m.Dose})", "Odaberi lijek");
        var med = meds[idx];

        var newName = ConsoleMenu.ReadOptional($"Naziv ({med.Name})");
        var newDose = ConsoleMenu.ReadOptional($"Doza ({med.Dose})");
        var newFreq = ConsoleMenu.ReadOptional($"Učestalost ({med.Frequency})");

        if (newName != null) med.Name = newName;
        if (newDose != null) med.Dose = newDose;
        if (newFreq != null) med.Frequency = newFreq;

        _db.Medications.Update(med);
        ConsoleMenu.PrintSuccess("Lijek ažuriran.");
    }

    private void Delete()
    {
        var meds = _db.Medications.OrderBy(m => m.Name).ToList();
        if (!meds.Any()) { ConsoleMenu.PrintInfo("Nema lijekova."); return; }

        int idx = ConsoleMenu.SelectFromList(meds, m => $"{m.Name} ({m.Dose})", "Odaberi lijek za brisanje");
        _db.Medications.Delete(meds[idx]);
        ConsoleMenu.PrintSuccess("Lijek obrisan.");
    }
}
