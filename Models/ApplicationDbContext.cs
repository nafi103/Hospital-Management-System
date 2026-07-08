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
        public DbSet<MedicalRecord> MedicalRecords { get; set; }
        public DbSet<Operation> Operations { get; set; }
        public DbSet<Medicine> Medicines { get; set; }
        public DbSet<Prescription> Prescriptions { get; set; }
        public DbSet<PrescriptionItem> PrescriptionItems { get; set; }
        public DbSet<Bill> Bills { get; set; }
        public DbSet<BillItem> BillItems { get; set; }

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

            modelBuilder.Entity<MedicalRecord>()
                .HasOne(m => m.Doctor)
                .WithMany()
                .HasForeignKey(m => m.DoctorId)
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

            // Seed Data
            modelBuilder.Entity<Role>().HasData(
                new Role { Id = 1, RoleName = "Admin", Permissions = "All", CreatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc), UpdatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc) },
                new Role { Id = 2, RoleName = "Doctor", Permissions = "Read, Write_Patient", CreatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc), UpdatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc) },
                new Role { Id = 3, RoleName = "Receptionist", Permissions = "Read, Write_Admission", CreatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc), UpdatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc) }
            );

            modelBuilder.Entity<User>().HasData(
                new User { 
                    Id = 1, 
                    RoleId = 1, 
                    Username = "admin", 
                    Password = "admin123", 
                    FullName = "System Admin", 
                    Category = "Management",
                    CreatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc),
                    UpdatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc)
                }
            );

            modelBuilder.Entity<Bed>().HasData(
                new Bed { Id = 1, BedNumber = "ICU-01", Category = BedCategory.ICU, DailyRate = 5000, CreatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc), UpdatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc) },
                new Bed { Id = 2, BedNumber = "WD-101", Category = BedCategory.GeneralWard, DailyRate = 1000, CreatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc), UpdatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc) },
                new Bed { Id = 3, BedNumber = "CABIN-05", Category = BedCategory.Cabin, DailyRate = 3500, CreatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc), UpdatedAt = new System.DateTime(2024, 1, 1, 0, 0, 0, System.DateTimeKind.Utc) }
            );
        }
    }
}