using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HospitalManagementSystem.Controllers;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HospitalManagementSystem.Tests
{
    public class PrescriptionSecurityTests
    {
        private readonly IConfiguration _config;
        private readonly PrescriptionSecurityService _securityService;

        public PrescriptionSecurityTests()
        {
            var inMemorySettings = new Dictionary<string, string?>
            {
                { "PrescriptionSecurity:SecretKey", "Test-HMAC-Secret-Key-1234567890-Defense!" }
            };

            _config = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();

            _securityService = new PrescriptionSecurityService(_config);
        }

        [Fact]
        public void GenerateSignature_IsDeterministicAndValidates()
        {
            int rxId = 101;
            string uhid = "PT-202610-0042";
            var issuedAt = new DateTime(2026, 10, 22, 10, 30, 0, DateTimeKind.Utc);

            var sig1 = _securityService.GenerateSignature(rxId, uhid, issuedAt);
            var sig2 = _securityService.GenerateSignature(rxId, uhid, issuedAt);

            Assert.Equal(sig1, sig2);
            Assert.True(_securityService.VerifySignature(rxId, uhid, issuedAt, sig1));
        }

        [Fact]
        public void VerifySignature_FailsWhenPrescriptionIdTampered()
        {
            int rxId = 101;
            string uhid = "PT-202610-0042";
            var issuedAt = new DateTime(2026, 10, 22, 10, 30, 0, DateTimeKind.Utc);

            var validSig = _securityService.GenerateSignature(rxId, uhid, issuedAt);

            // Attacker attempts to apply signature of rx 101 to rx 102 (e.g. for unauthorized narcotics)
            bool isValid = _securityService.VerifySignature(102, uhid, issuedAt, validSig);
            Assert.False(isValid);
        }

        [Fact]
        public void VerifySignature_FailsWhenUhidTampered()
        {
            int rxId = 101;
            string uhid = "PT-202610-0042";
            var issuedAt = new DateTime(2026, 10, 22, 10, 30, 0, DateTimeKind.Utc);

            var validSig = _securityService.GenerateSignature(rxId, uhid, issuedAt);

            // Attacker changes patient UHID
            bool isValid = _securityService.VerifySignature(rxId, "PT-202610-9999", issuedAt, validSig);
            Assert.False(isValid);
        }

        [Fact]
        public void GenerateVerificationData_ProducesNonEmptySvgAndUrl()
        {
            var patient = new Patient { Id = 1, Uhid = "PT-202610-0001", FullName = "Karim Rahman" };
            var rx = new Prescription
            {
                Id = 55,
                Patient = patient,
                CreatedAt = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc)
            };

            var data = _securityService.GenerateVerificationData(rx, "https://hospital.example.com");

            Assert.Equal(55, data.PrescriptionId);
            Assert.Equal("PT-202610-0001", data.Uhid);
            Assert.Contains("/Verify/Prescription?id=55", data.VerificationUrl);
            Assert.Contains("sig=", data.VerificationUrl);
            Assert.Contains("<svg", data.QrSvg);
        }

        [Fact]
        public async Task VerifyController_ReturnsValidPrescription_WhenSignatureMatchesDatabase()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            using var context = new ApplicationDbContext(options);

            var patient = new Patient
            {
                Id = 1,
                Uhid = "PT-202610-0010",
                FullName = "Fatima Begum",
                Gender = "Female",
                BloodGroup = "O+"
            };
            var doctor = new User
            {
                Id = 10,
                FullName = "Dr. M. Rahman",
                Category = "Cardiologist",
                Username = "dr.rahman",
                PasswordHash = "hash"
            };
            var medicine = new Medicine
            {
                Id = 100,
                Name = "Napa Extra 500mg",
                GenericName = "Paracetamol + Caffeine",
                StockQuantity = 200
            };
            var item = new PrescriptionItem
            {
                Id = 1,
                Medicine = medicine,
                MedicineId = 100,
                Quantity = 10,
                DoseMorning = 1,
                DoseEvening = 1,
                Instructions = "1 tablet after meals",
                DoseUnit = DoseUnit.Tablet
            };
            var issuedAt = new DateTime(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc);
            var prescription = new Prescription
            {
                Id = 200,
                Patient = patient,
                Doctor = doctor,
                CreatedAt = issuedAt,
                Status = PrescriptionStatus.PendingPharmacy,
                PrescriptionItems = new List<PrescriptionItem> { item }
            };

            context.Patients.Add(patient);
            context.Users.Add(doctor);
            context.Medicines.Add(medicine);
            context.Prescriptions.Add(prescription);
            await context.SaveChangesAsync();

            var controller = new VerifyController(context, _securityService);
            var sig = _securityService.GenerateSignature(prescription.Id, patient.Uhid, issuedAt);

            var actionResult = await controller.Prescription(prescription.Id, patient.Uhid, issuedAt.Ticks, sig);
            var viewResult = Assert.IsType<ViewResult>(actionResult);
            var vm = Assert.IsType<PrescriptionVerificationViewModel>(viewResult.Model);

            Assert.True(vm.IsValid);
            Assert.NotNull(vm.Prescription);
            Assert.Equal("Fatima Begum", vm.Prescription.Patient?.FullName);
            Assert.Equal(200, vm.Prescription.Id);
        }

        [Fact]
        public async Task VerifyController_FlagsCryptographicFailure_WhenSignatureIsForged()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            using var context = new ApplicationDbContext(options);
            var controller = new VerifyController(context, _securityService);

            long ticks = DateTime.UtcNow.Ticks;
            var actionResult = await controller.Prescription(10, "PT-202610-0001", ticks, "FORGED-SIGNATURE-HEX");
            var viewResult = Assert.IsType<ViewResult>(actionResult);
            var vm = Assert.IsType<PrescriptionVerificationViewModel>(viewResult.Model);

            Assert.False(vm.IsValid);
            Assert.Contains("CRYPTOGRAPHIC INTEGRITY FAILURE", vm.ErrorMessage);
        }
    }
}
