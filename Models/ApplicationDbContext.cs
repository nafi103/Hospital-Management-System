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
    }
}