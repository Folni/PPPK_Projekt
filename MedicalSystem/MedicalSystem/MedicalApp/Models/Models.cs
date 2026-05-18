using MyORM.Attributes;

namespace MedicalApp.Models;

// ─── Patient ────────────────────────────────────────────────────────────────
[Table("patients")]
public class Patient
{
    [PrimaryKey(AutoIncrement = true)]
    [Column("id")]
    public int Id { get; set; }

    [Column("first_name", IsNullable = false)]
    public string FirstName { get; set; } = "";

    [Column("last_name", IsNullable = false)]
    public string LastName { get; set; } = "";

    [Column("oib", IsNullable = false, IsUnique = true)]
    public string OIB { get; set; } = "";

    [Column("date_of_birth", IsNullable = false)]
    public DateTime DateOfBirth { get; set; }

    [Column("gender", IsNullable = false)]
    public string Gender { get; set; } = "";

    [Column("residence_address")]
    public string? ResidenceAddress { get; set; }

    [Column("domicile_address")]
    public string? DomicileAddress { get; set; }

    // Navigation properties (eager-loaded on demand)
    [Navigation("Id")]
    public List<MedicalHistory>? MedicalHistories { get; set; }

    [Navigation("Id")]
    public List<Prescription>? Prescriptions { get; set; }

    [Navigation("Id")]
    public List<Appointment>? Appointments { get; set; }

    public string FullName => $"{FirstName} {LastName}";
}

// ─── Doctor ─────────────────────────────────────────────────────────────────
[Table("doctors")]
public class Doctor
{
    [PrimaryKey(AutoIncrement = true)]
    [Column("id")]
    public int Id { get; set; }

    [Column("first_name", IsNullable = false)]
    public string FirstName { get; set; } = "";

    [Column("last_name", IsNullable = false)]
    public string LastName { get; set; } = "";

    [Column("specialization", IsNullable = false)]
    public string Specialization { get; set; } = "";

    public string FullName => $"Dr. {FirstName} {LastName} ({Specialization})";
}

// ─── Medication ──────────────────────────────────────────────────────────────
[Table("medications")]
public class Medication
{
    [PrimaryKey(AutoIncrement = true)]
    [Column("id")]
    public int Id { get; set; }

    [Column("name", IsNullable = false)]
    public string Name { get; set; } = "";

    [Column("dose", IsNullable = false)]
    public string Dose { get; set; } = "";     // e.g. "500mg", "2 tablete"

    [Column("frequency", IsNullable = false)]
    public string Frequency { get; set; } = ""; // e.g. "3x dnevno", "jednom tjedno"
}

// ─── Prescription ────────────────────────────────────────────────────────────
[Table("prescriptions")]
public class Prescription
{
    [PrimaryKey(AutoIncrement = true)]
    [Column("id")]
    public int Id { get; set; }

    [Column("patient_id", IsNullable = false)]
    [ForeignKey("patients", "id")]
    public int PatientId { get; set; }

    [Column("medication_id", IsNullable = false)]
    [ForeignKey("medications", "id")]
    public int MedicationId { get; set; }

    [Column("condition_name", IsNullable = false)]
    public string ConditionName { get; set; } = ""; // e.g. "Hipertenzija"

    [Column("prescribed_at", IsNullable = false, Default = "NOW()")]
    public DateTime PrescribedAt { get; set; } = DateTime.UtcNow;

    // Navigation (loaded via Include)
    [Navigation("PatientId")]
    public Patient? Patient { get; set; }

    [Navigation("MedicationId")]
    public Medication? Medication { get; set; }
}

// ─── MedicalHistory ──────────────────────────────────────────────────────────
[Table("medical_histories")]
public class MedicalHistory
{
    [PrimaryKey(AutoIncrement = true)]
    [Column("id")]
    public int Id { get; set; }

    [Column("patient_id", IsNullable = false)]
    [ForeignKey("patients", "id")]
    public int PatientId { get; set; }

    [Column("diagnosis", IsNullable = false)]
    public string Diagnosis { get; set; } = "";

    [Column("from_date", IsNullable = false)]
    public DateTime FromDate { get; set; }

    [Column("to_date")]
    public DateTime? ToDate { get; set; }

    [Navigation("PatientId")]
    public Patient? Patient { get; set; }
}

// ─── Appointment ─────────────────────────────────────────────────────────────
[Table("appointments")]
public class Appointment
{
    [PrimaryKey(AutoIncrement = true)]
    [Column("id")]
    public int Id { get; set; }

    [Column("patient_id", IsNullable = false)]
    [ForeignKey("patients", "id")]
    public int PatientId { get; set; }

    [Column("doctor_id", IsNullable = false)]
    [ForeignKey("doctors", "id")]
    public int DoctorId { get; set; }

    [Column("examination_type", IsNullable = false)]
    public string ExaminationType { get; set; } = ""; // CT, MR, ULTRA, EKG, ECHO, OKO, DERM, DENTA, MAMMO, EEG

    [Column("scheduled_at", IsNullable = false)]
    public DateTime ScheduledAt { get; set; }

    [Navigation("PatientId")]
    public Patient? Patient { get; set; }

    [Navigation("DoctorId")]
    public Doctor? Doctor { get; set; }
}
