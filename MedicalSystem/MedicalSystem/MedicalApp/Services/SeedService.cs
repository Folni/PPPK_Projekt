using MedicalApp.Data;
using MedicalApp.Models;

namespace MedicalApp.Services;

/// <summary>
/// Seeds the initial doctor data only on the first application run.
/// Checks if any doctors already exist before inserting.
/// </summary>
public static class SeedService
{
    public static void SeedDoctors(MedicalDbContext db)
    {
        var count = db.Doctors.Count();
        if (count > 0)
        {
            Console.WriteLine($"[Seed] {count} doctor(s) already in database – skipping seed.");
            return;
        }

        Console.WriteLine("[Seed] Seeding initial doctors...");

        var doctors = new[]
        {
            new Doctor { FirstName = "Ana",     LastName = "Horvat",   Specialization = "Opća medicina" },
            new Doctor { FirstName = "Ivan",    LastName = "Perić",    Specialization = "Kardiologija" },
            new Doctor { FirstName = "Maja",    LastName = "Kovač",    Specialization = "Neurologija" },
            new Doctor { FirstName = "Tomislav",LastName = "Jurić",    Specialization = "Radiologija" },
            new Doctor { FirstName = "Petra",   LastName = "Blažević", Specialization = "Dermatologija" },
        };

        foreach (var doc in doctors)
        {
            db.Doctors.Add(doc);
            Console.WriteLine($"  + {doc.FullName}");
        }

        Console.WriteLine("[Seed] Doctors seeded successfully.");
    }
}
