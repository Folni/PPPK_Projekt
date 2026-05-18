using MedicalApp.Data;
using MedicalApp.Models;
using MedicalApp.Services;

namespace MedicalApp.Repositories;

public class AppointmentService
{
    private readonly MedicalDbContext _db;
    private readonly PatientService _patientService;

    private static readonly string[] ValidTypes =
        { "CT", "MR", "ULTRA", "EKG", "ECHO", "OKO", "DERM", "DENTA", "MAMMO", "EEG" };

    public AppointmentService(MedicalDbContext db, PatientService patientService)
    {
        _db = db;
        _patientService = patientService;
    }

    public void ManageMenu()
    {
        while (true)
        {
            ConsoleMenu.PrintHeader("SPECIJALISTI»KI PREGLEDI");
            Console.WriteLine("  [1] Pregled zakazanih pregleda");
            Console.WriteLine("  [2] Zakaži pregled");
            Console.WriteLine("  [3] Obriši pregled");
            Console.WriteLine("  [0] Natrag");
            Console.Write("\n  Odabir: ");

            switch (Console.ReadLine()?.Trim())
            {
                case "1": ViewAppointments(); break;
                case "2": Schedule(); break;
                case "3": Cancel(); break;
                case "0": return;
                default: ConsoleMenu.PrintError("Nevažeći odabir."); break;
            }
        }
    }

    private void ViewAppointments()
    {
        var patient = _patientService.SelectPatient();
        if (patient == null) return;

        var appointments = _db.Appointments
            .Where(a => a.PatientId == patient.Id)
            .Include("Doctor")
            .OrderBy(a => a.ScheduledAt)
            .ToList();

        Console.WriteLine($"\n  Pregledi za {patient.FullName}:");
        if (!appointments.Any()) { ConsoleMenu.PrintInfo("Nema zakazanih pregleda."); return; }

        foreach (var a in appointments)
            Console.WriteLine($"  [{a.Id}] {a.ExaminationType} | {a.ScheduledAt:dd.MM.yyyy HH:mm} | {a.Doctor?.FullName ?? "N/A"}");
    }

    private void Schedule()
    {
        var patient = _patientService.SelectPatient();
        if (patient == null) return;

        var doctors = _db.Doctors.OrderBy(d => d.LastName).ToList();
        if (!doctors.Any()) { ConsoleMenu.PrintInfo("Nema liječnika u sustavu."); return; }

        ConsoleMenu.PrintHeader("ZAKAŽI PREGLED");

        // Choose exam type
        Console.WriteLine("  Dostupne vrste pregleda: " + string.Join(", ", ValidTypes));
        string examType;
        while (true)
        {
            examType = ConsoleMenu.ReadRequired("Vrsta pregleda").ToUpper();
            if (ValidTypes.Contains(examType)) break;
            ConsoleMenu.PrintError($"Nevažeća vrsta. Odaberite: {string.Join(", ", ValidTypes)}");
        }

        int doctorIdx = ConsoleMenu.SelectFromList(doctors, d => d.FullName, "Odaberi liječnika specijalista");
        var scheduledDate = ConsoleMenu.ReadDate("Datum pregleda");
        Console.Write("  Sat (HH:mm): ");
        var timeStr = Console.ReadLine()?.Trim() ?? "08:00";
        if (!TimeSpan.TryParseExact(timeStr, @"hh\:mm", null, out var time))
            time = TimeSpan.Zero;

        var appointment = new Appointment
        {
            PatientId = patient.Id,
            DoctorId = doctors[doctorIdx].Id,
            ExaminationType = examType,
            ScheduledAt = scheduledDate.Add(time)
        };

        _db.Appointments.Add(appointment);
        ConsoleMenu.PrintSuccess($"Pregled {examType} zakazan za {appointment.ScheduledAt:dd.MM.yyyy HH:mm}.");
    }

    private void Cancel()
    {
        var patient = _patientService.SelectPatient();
        if (patient == null) return;

        var appointments = _db.Appointments
            .Where(a => a.PatientId == patient.Id)
            .Include("Doctor")
            .ToList();

        if (!appointments.Any()) { ConsoleMenu.PrintInfo("Nema pregleda."); return; }

        int idx = ConsoleMenu.SelectFromList(
            appointments,
            a => $"{a.ExaminationType} – {a.ScheduledAt:dd.MM.yyyy HH:mm} – {a.Doctor?.FullName ?? "N/A"}",
            "Odaberi pregled za otkazivanje"
        );

        _db.Appointments.Delete(appointments[idx]);
        ConsoleMenu.PrintSuccess("Pregled otkazan.");
    }
}
