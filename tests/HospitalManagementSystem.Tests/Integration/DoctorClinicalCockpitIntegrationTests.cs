using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HospitalManagementSystem.Controllers;
using HospitalManagementSystem.Hubs;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Models.ViewModels;
using HospitalManagementSystem.Services;
using HospitalManagementSystem.Tests.Fixtures;
using HospitalManagementSystem.Tests.Helpers;
using Xunit;

namespace HospitalManagementSystem.Tests.Integration
{
    public class DoctorClinicalCockpitIntegrationTests
    {
        private async Task<(ApplicationDbContext Context, DoctorDashboardController DashboardCtrl, PrescriptionsController PrescriptionsCtrl)> CreateSetupAsync(int doctorId)
        {
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var hubContext = new TestHubContext<NotificationHub>();

            var dashboardCtrl = new DoctorDashboardController(context, hubContext);
            ControllerTestHelper.SetupController(dashboardCtrl, TestPrincipalFactory.CreateDoctor(doctorId));

            var prescriptionsCtrl = new PrescriptionsController(context);
            ControllerTestHelper.SetupController(prescriptionsCtrl, TestPrincipalFactory.CreateDoctor(doctorId));

            return (context, dashboardCtrl, prescriptionsCtrl);
        }

        [Fact]
        public async Task Cockpit_LoadsCorrectly_WithAllergiesVitalsAndNEWS2()
        {
            var (context, dashboardCtrl, _) = await CreateSetupAsync(10); // Dr Alice

            // Setup a patient in consultation
            var patient = await context.Patients.FirstAsync(p => p.Id == 100);
            patient.IsChild = false;

            // Add allergy
            context.PatientAllergies.Add(new PatientAllergy
            {
                PatientId = 100,
                Substance = "Penicillin",
                AllergenGenericName = "Amoxicillin",
                Severity = AllergySeverity.Severe,
                ReactionType = "Anaphylaxis",
                RecordedById = 10,
                CreatedAt = DateTime.UtcNow
            });

            var appt = new Appointment
            {
                PatientId = 100,
                DoctorId = 10,
                AppointmentDatetime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddMinutes(15),
                Status = AppointmentStatus.InConsultation,
                ReasonForVisit = "High fever and cough",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.Add(appt);
            await context.SaveChangesAsync();

            // Add vitals with high NEWS2 (HR 135, RR 26, SpO2 90%)
            context.PatientVitals.Add(new PatientVital
            {
                PatientId = 100,
                AppointmentId = appt.Id,
                SystolicBp = 85, // 3 pts
                DiastolicBp = 55,
                HeartRate = 135, // 3 pts
                Spo2 = 90, // 3 pts
                Temperature = 39.5m, // 2 pts
                RespiratoryRate = 26, // 3 pts
                Consciousness = ConsciousnessLevel.Alert,
                OnSupplementalOxygen = false,
                WeightKg = 72.5m,
                TriagePriority = TriagePriority.Emergency,
                RecordedById = 10,
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();

            // Act: Load Cockpit
            var result = await dashboardCtrl.Cockpit(appt.Id);

            // Assert
            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<ClinicalCockpitViewModel>(viewResult.Model);

            Assert.Equal(appt.Id, model.Appointment.Id);
            Assert.Equal("Penicillin", model.Allergies.First().Substance);
            Assert.NotNull(model.LatestVitals);
            Assert.Equal(72.5m, model.LatestVitals.WeightKg);
            Assert.NotNull(model.News2Result);
            Assert.True(model.News2Result.Value.Score >= 7);
            Assert.Equal(TriagePriority.Emergency, model.News2Result.Value.Priority);
        }

        [Fact]
        public async Task CompleteConsultation_AtomicallyPersists_MedicalRecordAndPrescription_WithAppointmentId()
        {
            var (context, dashboardCtrl, _) = await CreateSetupAsync(10); // Dr Alice

            var appt = new Appointment
            {
                PatientId = 100,
                DoctorId = 10,
                AppointmentDatetime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddMinutes(15),
                Status = AppointmentStatus.InConsultation,
                ReasonForVisit = "Sore throat and fever",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.Add(appt);
            await context.SaveChangesAsync();

            var submitModel = new ClinicalCockpitSubmitModel
            {
                AppointmentId = appt.Id,
                PatientId = 100,
                ChiefComplaint = "Severe sore throat for 3 days",
                Diagnosis = "Acute Streptococcal Pharyngitis",
                Treatment = "Oral antibiotics, plenty of warm fluids, rest",
                PrescriptionItems = new List<CockpitPrescriptionItemInput>
                {
                    new()
                    {
                        MedicineId = 1, // Napa / Paracetamol
                        Quantity = 10,
                        DoseMorning = 1,
                        DoseAfternoon = 0,
                        DoseEvening = 1,
                        DoseUnit = DoseUnit.Tablet,
                        DurationDays = 5,
                        Instructions = "Take after food"
                    }
                }
            };

            // Act
            var result = await dashboardCtrl.CompleteConsultation(submitModel);

            // Assert: Redirects to Index
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirect.ActionName);

            // Verify Appointment is Completed
            var updatedAppt = await context.Appointments.FindAsync(appt.Id);
            Assert.NotNull(updatedAppt);
            Assert.Equal(AppointmentStatus.Completed, updatedAppt.Status);

            // Verify MedicalRecord exists with AppointmentId
            var medRecord = await context.MedicalRecords.FirstOrDefaultAsync(r => r.AppointmentId == appt.Id);
            Assert.NotNull(medRecord);
            Assert.Equal("Acute Streptococcal Pharyngitis", medRecord.Diagnosis);
            Assert.Equal(10, medRecord.DoctorId);

            // Verify Prescription exists with AppointmentId
            var prescription = await context.Prescriptions
                .Include(p => p.PrescriptionItems)
                .FirstOrDefaultAsync(p => p.AppointmentId == appt.Id);
            Assert.NotNull(prescription);
            Assert.Equal(PrescriptionStatus.PendingPharmacy, prescription.Status);
            Assert.Single(prescription.PrescriptionItems);
            Assert.Equal(1, prescription.PrescriptionItems.First().MedicineId);
            Assert.Equal(10, prescription.PrescriptionItems.First().Quantity);
        }

        [Fact]
        public async Task CompleteConsultation_DuplicateMedicine_RejectedWithError()
        {
            var (context, dashboardCtrl, _) = await CreateSetupAsync(10);

            var appt = new Appointment
            {
                PatientId = 100,
                DoctorId = 10,
                AppointmentDatetime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddMinutes(15),
                Status = AppointmentStatus.InConsultation,
                ReasonForVisit = "Checkup",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.Add(appt);
            await context.SaveChangesAsync();

            var submitModel = new ClinicalCockpitSubmitModel
            {
                AppointmentId = appt.Id,
                PatientId = 100,
                Diagnosis = "Common Cold",
                PrescriptionItems = new List<CockpitPrescriptionItemInput>
                {
                    new() { MedicineId = 1, Quantity = 5, DoseUnit = DoseUnit.Tablet },
                    new() { MedicineId = 1, Quantity = 10, DoseUnit = DoseUnit.Tablet } // Duplicate!
                }
            };

            // Act
            var result = await dashboardCtrl.CompleteConsultation(submitModel);

            // Assert: Redirects to Cockpit with error
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Cockpit", redirect.ActionName);
            Assert.NotNull(dashboardCtrl.TempData["ErrorMessage"]);

            // Verify Appointment did NOT complete
            var unchangedAppt = await context.Appointments.FindAsync(appt.Id);
            Assert.Equal(AppointmentStatus.InConsultation, unchangedAppt!.Status);
        }

        [Fact]
        public async Task ReturnToWaitingRoom_RevertsAppointmentToScheduled()
        {
            var (context, dashboardCtrl, _) = await CreateSetupAsync(10);

            var appt = new Appointment
            {
                PatientId = 100,
                DoctorId = 10,
                AppointmentDatetime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddMinutes(15),
                Status = AppointmentStatus.InConsultation,
                ReasonForVisit = "Routine follow-up",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.Add(appt);
            await context.SaveChangesAsync();

            // Act
            var result = await dashboardCtrl.ReturnToWaitingRoom(appt.Id);

            // Assert
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirect.ActionName);

            var updatedAppt = await context.Appointments.FindAsync(appt.Id);
            Assert.Equal(AppointmentStatus.Scheduled, updatedAppt!.Status);
        }

        [Fact]
        public async Task CancelConsultation_TransitionsToCancelled_WithReason()
        {
            var (context, dashboardCtrl, _) = await CreateSetupAsync(10);

            var appt = new Appointment
            {
                PatientId = 100,
                DoctorId = 10,
                AppointmentDatetime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddMinutes(15),
                Status = AppointmentStatus.InConsultation,
                ReasonForVisit = "Consultation",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.Add(appt);
            await context.SaveChangesAsync();

            // Act
            var result = await dashboardCtrl.CancelConsultation(appt.Id, "Patient left chamber");

            // Assert
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirect.ActionName);

            var updatedAppt = await context.Appointments.FindAsync(appt.Id);
            Assert.Equal(AppointmentStatus.Cancelled, updatedAppt!.Status);
            Assert.Contains("Cancelled: Patient left chamber", updatedAppt.ReasonForVisit);
        }

        [Fact]
        public async Task CancelPrescription_PendingPrescription_SuccessfullyCancelled_AndBlocksDispense()
        {
            var (context, _, prescriptionsCtrl) = await CreateSetupAsync(10);

            var prescription = new Prescription
            {
                PatientId = 100,
                DoctorId = 10,
                Status = PrescriptionStatus.PendingPharmacy,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                PrescriptionItems = new List<PrescriptionItem>
                {
                    new() { MedicineId = 1, Quantity = 10, UnitPrice = 2.5m }
                }
            };
            context.Prescriptions.Add(prescription);
            await context.SaveChangesAsync();

            // Act: Cancel Prescription
            var cancelResult = await prescriptionsCtrl.Cancel(prescription.Id, "Doctor adjusted dosing plan");

            // Assert
            var updatedPrescription = await context.Prescriptions.FindAsync(prescription.Id);
            Assert.NotNull(updatedPrescription);
            Assert.Equal(PrescriptionStatus.Cancelled, updatedPrescription.Status);
            Assert.Equal("Doctor adjusted dosing plan", updatedPrescription.DiscontinuationReason);

            // Act: Pharmacist tries to dispense cancelled prescription
            ControllerTestHelper.SetupController(prescriptionsCtrl, TestPrincipalFactory.CreatePharmacist(35));
            var dispenseResult = await prescriptionsCtrl.Dispense(prescription.Id);

            // Assert
            Assert.Equal(PrescriptionStatus.Cancelled, updatedPrescription.Status);
            Assert.NotNull(prescriptionsCtrl.TempData["ErrorMessage"]);
        }

        [Fact]
        public async Task DiscontinuePrescription_DispensedPrescription_SuccessfullyDiscontinued()
        {
            var (context, _, prescriptionsCtrl) = await CreateSetupAsync(10);

            var prescription = new Prescription
            {
                PatientId = 100,
                DoctorId = 10,
                Status = PrescriptionStatus.Dispensed,
                DispensedAt = DateTime.UtcNow.AddDays(-1),
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                UpdatedAt = DateTime.UtcNow.AddDays(-1),
                PrescriptionItems = new List<PrescriptionItem>
                {
                    new() { MedicineId = 1, Quantity = 10, UnitPrice = 2.5m, DurationDays = 30 }
                }
            };
            context.Prescriptions.Add(prescription);
            await context.SaveChangesAsync();

            // Act: Discontinue therapy
            var result = await prescriptionsCtrl.Discontinue(prescription.Id, "Patient developed skin rash");

            // Assert
            var updated = await context.Prescriptions.FindAsync(prescription.Id);
            Assert.NotNull(updated);
            Assert.Equal(PrescriptionStatus.Discontinued, updated.Status);
            Assert.Equal("Patient developed skin rash", updated.DiscontinuationReason);
        }

        [Fact]
        public async Task DoctorDashboard_Index_ExcludesStaleConsultationsOlderThan24Hours()
        {
            var (context, dashboardCtrl, _) = await CreateSetupAsync(10);

            // Active visit from 30 mins ago
            var freshAppt = new Appointment
            {
                PatientId = 100,
                DoctorId = 10,
                AppointmentDatetime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddMinutes(15),
                Status = AppointmentStatus.InConsultation,
                ReasonForVisit = "Active visit",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow.AddMinutes(-30)
            };

            // Stale visit from 3 days ago
            var staleAppt = new Appointment
            {
                PatientId = 101,
                DoctorId = 10,
                AppointmentDatetime = DateTime.UtcNow.AddDays(-3),
                EndTime = DateTime.UtcNow.AddDays(-3).AddMinutes(15),
                Status = AppointmentStatus.InConsultation,
                ReasonForVisit = "Old visit",
                CreatedAt = DateTime.UtcNow.AddDays(-3),
                UpdatedAt = DateTime.UtcNow.AddDays(-3)
            };

            context.Appointments.AddRange(freshAppt, staleAppt);
            await context.SaveChangesAsync();

            // Act
            var result = await dashboardCtrl.Index();

            // Assert
            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsAssignableFrom<IEnumerable<Appointment>>(viewResult.Model).ToList();

            Assert.Contains(model, a => a.Id == freshAppt.Id);
            Assert.DoesNotContain(model, a => a.Id == staleAppt.Id);
        }
    }
}
