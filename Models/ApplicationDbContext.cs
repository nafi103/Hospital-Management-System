using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Models
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<Role> Roles { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Patient> Patients { get; set; }
        public DbSet<Bed> Beds { get; set; }
        public DbSet<BedTransfer> BedTransfers { get; set; }
        public DbSet<Admission> Admissions { get; set; }
        public DbSet<Appointment> Appointments { get; set; }
        public DbSet<Operation> Operations { get; set; }
        public DbSet<Medicine> Medicines { get; set; }
        public DbSet<Prescription> Prescriptions { get; set; }
        public DbSet<PrescriptionItem> PrescriptionItems { get; set; }
        public DbSet<Bill> Bills { get; set; }
        public DbSet<BillItem> BillItems { get; set; }
        public DbSet<PatientVital> PatientVitals { get; set; }
        public DbSet<MedicalRecord> MedicalRecords { get; set; }
        public DbSet<PatientAllergy> PatientAllergies { get; set; }
        public DbSet<AiSuggestion> AiSuggestions { get; set; }
        public DbSet<AiProviderSetting> AiProviderSettings { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure DeleteBehavior to Restrict for User relationships to prevent multiple cascade paths
            modelBuilder.Entity<Admission>()
                .HasOne(a => a.AdmittingDoctor)
                .WithMany()
                .HasForeignKey(a => a.AdmittingDoctorId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Appointment>()
                .HasOne(a => a.Doctor)
                .WithMany()
                .HasForeignKey(a => a.DoctorId)
                .OnDelete(DeleteBehavior.Restrict);



            modelBuilder.Entity<Operation>()
                .HasOne(o => o.Surgeon)
                .WithMany()
                .HasForeignKey(o => o.SurgeonId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Prescription>()
                .HasOne(p => p.Doctor)
                .WithMany()
                .HasForeignKey(p => p.DoctorId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Bill>()
                .HasOne(b => b.DiscountApprovedBy)
                .WithMany()
                .HasForeignKey(b => b.DiscountApprovedById)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PatientVital>()
                .HasOne(v => v.RecordedBy)
                .WithMany()
                .HasForeignKey(v => v.RecordedById)
                .OnDelete(DeleteBehavior.Restrict);

#pragma warning disable CS0618 // RespiratoryDistress is obsolete but still mapped for existing records.
            modelBuilder.Entity<PatientVital>()
                .Property(v => v.RespiratoryDistress)
                .HasDefaultValue(false);
#pragma warning restore CS0618

            modelBuilder.Entity<MedicalRecord>()
                .HasOne(r => r.Doctor)
                .WithMany()
                .HasForeignKey(r => r.DoctorId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PatientAllergy>()
                .HasOne(a => a.RecordedBy)
                .WithMany()
                .HasForeignKey(a => a.RecordedById)
                .OnDelete(DeleteBehavior.Restrict);

            // Server-computed, case/whitespace-normalized shadow copy of Substance, used
            // only to key the uniqueness check below so "Penicillin" and "penicillin " land
            // on the same row instead of silently duplicating.
            modelBuilder.Entity<PatientAllergy>()
                .Property<string>("SubstanceNormalized")
                .HasComputedColumnSql("lower(btrim(\"Substance\"))", stored: true);

            // One row per (patient, substance): prevents the same allergy being recorded
            // twice, which would otherwise make the P4 safety check fire duplicate
            // warnings. Composite and leading on PatientId, so it also serves plain
            // "this patient's allergies" lookups — no separate PatientId index needed.
            modelBuilder.Entity<PatientAllergy>()
                .HasIndex("PatientId", "SubstanceNormalized")
                .IsUnique();

            modelBuilder.Entity<AiSuggestion>()
                .HasOne(s => s.ReviewedBy)
                .WithMany()
                .HasForeignKey(s => s.ReviewedById)
                .OnDelete(DeleteBehavior.Restrict);

            // A patient's portal login, if the front desk issued one. Unique so one login
            // can't be attached to two patient records.
            modelBuilder.Entity<Patient>()
                .HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Patient>()
                .HasIndex(p => p.UserId)
                .IsUnique();

            // Seed Data
            modelBuilder.Entity<Role>().HasData(
                new Role { Id = 1, RoleName = "Admin", Permissions = "All", CreatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc), UpdatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc) },
                new Role { Id = 2, RoleName = "Doctor", Permissions = "Read, Write_Patient", CreatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc), UpdatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc) },
                new Role { Id = 3, RoleName = "Assistant", Permissions = "Read, Write_Admission", CreatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc), UpdatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc) },
                new Role { Id = 4, RoleName = "Pharmacist", Permissions = "Read, Write_Inventory", CreatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc), UpdatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc) },
                new Role { Id = 5, RoleName = "Receptionist", Permissions = "Read, Write_Patient, Write_Admission, Write_Billing", CreatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc), UpdatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc) },
                new Role { Id = 6, RoleName = "Patient", Permissions = "Read_Own", CreatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc), UpdatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc) }
            );

            modelBuilder.Entity<User>().HasData(
                new User {
                    Id = 1,
                    RoleId = 1,
                    Username = "admin",
                    FullName = "System Admin",
                    Category = "Management",
                    CreatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc),
                    UpdatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc)
                },
                new User {
                    Id = 100,
                    RoleId = 2,
                    Username = "drmock",
                    FullName = "Dr. Mock",
                    Category = "Consultant",
                    CreatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc),
                    UpdatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc)
                },
                new User {
                    Id = 1010,
                    RoleId = 4,
                    Username = "pharmacistmock",
                    FullName = "Pharmacist Mock",
                    Category = "Pharmacy",
                    CreatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc),
                    UpdatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc)
                }
            );
            // NOTE: PasswordHash for these seeded rows is populated by the raw-SQL backfill in the
            // AddClinicalDataModel migration (hashing whatever plaintext already lives in the legacy
            // Password column), not here — HasData can't call BCrypt at migration-generation time
            // without baking a fixed hash into the snapshot for every future re-seed.

            modelBuilder.Entity<Bed>().HasData(
                new Bed { Id = 1, BedNumber = "ICU-01", Category = BedCategory.ICU, DailyRate = 5000, CreatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc), UpdatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc) },
                new Bed { Id = 2, BedNumber = "WD-101", Category = BedCategory.GeneralWard, DailyRate = 1000, CreatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc), UpdatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc) },
                new Bed { Id = 3, BedNumber = "CABIN-05", Category = BedCategory.Cabin, DailyRate = 3500, CreatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc), UpdatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc) }
            );

            modelBuilder.Entity<Medicine>().HasData(
                new Medicine { Id = 1, Name = "Napa 500mg", GenericName = "Paracetamol", Strength = "500mg", TherapeuticClass = "Analgesic/Antipyretic", UnitPrice = 2.50m, StockQuantity = 1000, CreatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc), UpdatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc) },
                new Medicine { Id = 2, Name = "Ace 500mg", GenericName = "Paracetamol", Strength = "500mg", TherapeuticClass = "Analgesic/Antipyretic", UnitPrice = 2.00m, StockQuantity = 1500, CreatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc), UpdatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc) },
                new Medicine { Id = 3, Name = "Zithromax 500mg", GenericName = "Azithromycin", Strength = "500mg", TherapeuticClass = "Macrolide Antibiotic", UnitPrice = 35.00m, StockQuantity = 50, CreatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc), UpdatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc) },
                new Medicine { Id = 4, Name = "Azithral 500mg", GenericName = "Azithromycin", Strength = "500mg", TherapeuticClass = "Macrolide Antibiotic", UnitPrice = 25.00m, StockQuantity = 300, CreatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc), UpdatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc) },
                new Medicine { Id = 5, Name = "Zmax 500mg", GenericName = "Azithromycin", Strength = "500mg", TherapeuticClass = "Macrolide Antibiotic", UnitPrice = 30.00m, StockQuantity = 0, CreatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc), UpdatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc) },
                new Medicine { Id = 6, Name = "Seclo 20mg", GenericName = "Omeprazole", Strength = "20mg", TherapeuticClass = "Proton Pump Inhibitor", UnitPrice = 5.00m, StockQuantity = 600, CreatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc), UpdatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc) },
                new Medicine { Id = 7, Name = "Alatrol 10mg", GenericName = "Cetirizine", Strength = "10mg", TherapeuticClass = "Antihistamine", UnitPrice = 2.50m, StockQuantity = 1200, CreatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc), UpdatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc) }
            );
        }
    }
}