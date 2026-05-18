using MedicalApp.Models;
using MyORM.Core;

namespace MedicalApp.Data;

public class MedicalDbContext : DbContext
{
    public DbSet<Patient> Patients { get; set; } = null!;
    public DbSet<Doctor> Doctors { get; set; } = null!;
    public DbSet<Medication> Medications { get; set; } = null!;
    public DbSet<Prescription> Prescriptions { get; set; } = null!;
    public DbSet<MedicalHistory> MedicalHistories { get; set; } = null!;
    public DbSet<Appointment> Appointments { get; set; } = null!;

    public MedicalDbContext(string connectionString) : base(connectionString) { }

    public static Type[] AllEntityTypes => new[]
    {
        typeof(Patient),
        typeof(Doctor),
        typeof(Medication),
        typeof(Prescription),
        typeof(MedicalHistory),
        typeof(Appointment)
    };
}
