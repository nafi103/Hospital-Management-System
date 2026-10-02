using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using HospitalManagementSystem.Models;

namespace HospitalManagementSystem.Tests.Fixtures
{
    public static class TestDbContextFactory
    {
        public static ApplicationDbContext CreateInMemoryDbContext(string? dbName = null)
        {
            dbName ??= Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            var context = new ApplicationDbContext(options);
            return context;
        }

        public static async Task<ApplicationDbContext> CreateSeededDbContextAsync(string? dbName = null)
        {
            var context = CreateInMemoryDbContext(dbName);
            await SeedBaselineDataAsync(context);
            return context;
        }

        public static async Task SeedBaselineDataAsync(ApplicationDbContext context)
        {
            var now = DateTime.UtcNow;
            var adminRole = new Role { Id = 1, RoleName = "Admin", Permissions = "All", CreatedAt = now, UpdatedAt = now };
            var doctorRole = new Role { Id = 2, RoleName = "Doctor", Permissions = "Doctor", CreatedAt = now, UpdatedAt = now };
            var assistantRole = new Role { Id = 3, RoleName = "Assistant", Permissions = "Assistant", CreatedAt = now, UpdatedAt = now };
            var receptionistRole = new Role { Id = 4, RoleName = "Receptionist", Permissions = "Receptionist", CreatedAt = now, UpdatedAt = now };
            var pharmacistRole = new Role { Id = 5, RoleName = "Pharmacist", Permissions = "Pharmacist", CreatedAt = now, UpdatedAt = now };
            var patientRole = new Role { Id = 6, RoleName = "Patient", Permissions = "Patient", CreatedAt = now, UpdatedAt = now };

            context.Roles.AddRange(adminRole, doctorRole, assistantRole, receptionistRole, pharmacistRole, patientRole);

            var doctorA = new User
            {
                Id = 10,
                Username = "dr_alice",
                FullName = "Dr. Alice",
                RoleId = doctorRole.Id,
                Role = doctorRole,
                Category = "Doctor",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var doctorB = new User
            {
                Id = 11,
                Username = "dr_bob",
                FullName = "Dr. Bob",
                RoleId = doctorRole.Id,
                Role = doctorRole,
                Category = "Doctor",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var assistantA = new User
            {
                Id = 20,
                Username = "asst_anna",
                FullName = "Anna Assistant",
                RoleId = assistantRole.Id,
                Role = assistantRole,
                Category = "Assistant",
                AssignedDoctorId = doctorA.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var cashier = new User
            {
                Id = 30,
                Username = "cashier1",
                FullName = "Charles Cashier",
                RoleId = receptionistRole.Id,
                Role = receptionistRole,
                Category = "Receptionist",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            context.Users.AddRange(doctorA, doctorB, assistantA, cashier);

            var patient1 = new Patient
            {
                Id = 100,
                Uhid = "PT-202610-0100",
                FullName = "John Doe",
                DateOfBirth = new DateTime(1985, 5, 20, 0, 0, 0, DateTimeKind.Utc),
                Gender = "Male",
                BloodGroup = "O+",
                ContactInfo = 1712345678,
                EmergencyContactName = "Jane Doe",
                EmergencyContactPhone = 1798765432,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var patientChild = new Patient
            {
                Id = 101,
                Uhid = "PT-202610-0101",
                IsChild = true,
                DateOfBirth = new DateTime(2022, 1, 15, 0, 0, 0, DateTimeKind.Utc),
                Gender = "Female",
                BloodGroup = "B+",
                EmergencyContactName = "Mother Mary",
                EmergencyContactPhone = 1798765433,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            context.Patients.AddRange(patient1, patientChild);

            var bed1 = new Bed
            {
                Id = 1,
                BedNumber = "101-A",
                Category = BedCategory.GeneralWard,
                DailyRate = 500m,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var bed2 = new Bed
            {
                Id = 2,
                BedNumber = "Cabin-01",
                Category = BedCategory.Cabin,
                DailyRate = 2000m,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            context.Beds.AddRange(bed1, bed2);

            var medicine1 = new Medicine
            {
                Id = 1,
                Name = "Napa 500mg",
                GenericName = "Paracetamol",
                Strength = "500mg",
                StockQuantity = 100,
                UnitPrice = 2.50m
            };

            var medicine2 = new Medicine
            {
                Id = 2,
                Name = "Ace 500mg",
                GenericName = "Paracetamol",
                Strength = "500mg",
                StockQuantity = 50,
                UnitPrice = 2.50m
            };

            context.Medicines.AddRange(medicine1, medicine2);

            await context.SaveChangesAsync();
        }
    }
}
