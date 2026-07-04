using Microsoft.EntityFrameworkCore;

namespace HospitalManagementSystem.Models
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        // এই DbSet গুলোই ডাটাবেসে একেকটি টেবিল হিসেবে তৈরি হবে
        public DbSet<Patient> Patients { get; set; }
        public DbSet<Bed> Beds { get; set; }
        public DbSet<Admission> Admissions { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Doctor> Doctors { get; set; }
        public DbSet<Appointment> Appointments { get; set; }
        public DbSet<Billing> Billings { get; set; }

        // ডাটাবেস তৈরি হওয়ার সময় ডিফল্ট কিছু ডেটা (Seed Data) ঢুকিয়ে দেওয়ার লজিক
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ১. Roles টেবিলে বেসিক কিছু রোল সিড করা
            modelBuilder.Entity<Role>().HasData(
                new Role { Id = 1, RoleName = "Admin", Permissions = "All" },
                new Role { Id = 2, RoleName = "Doctor", Permissions = "Read, Write_Patient" },
                new Role { Id = 3, RoleName = "Receptionist", Permissions = "Read, Write_Admission" }
            );

            // ২. Users টেবিলে একজন ডিফল্ট অ্যাডমিন সিড করা
            modelBuilder.Entity<User>().HasData(
                new User { 
                    Id = 1, 
                    RoleId = 1, // Admin রোলটি দিচ্ছি
                    Username = "admin", 
                    PasswordHash = "admin123", // আপাতত প্লেইন টেক্সট
                    FullName = "System Admin", 
                    Department = "Management" 
                }
            );

            // ৩. Beds টেবিলে টেস্ট করার জন্য কিছু ডামি বেড সিড করা
            modelBuilder.Entity<Bed>().HasData(
                new Bed { Id = 1, BedNumber = "ICU-01", Type = "ICU", Status = "Available", DailyRate = 5000 },
                new Bed { Id = 2, BedNumber = "WD-101", Type = "General Ward", Status = "Available", DailyRate = 1000 },
                new Bed { Id = 3, BedNumber = "CABIN-05", Type = "VIP Cabin", Status = "Occupied", DailyRate = 3500 }
            );
        }
    }
}