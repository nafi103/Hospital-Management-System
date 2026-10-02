using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HospitalManagementSystem.Controllers;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Models.ViewModels;
using HospitalManagementSystem.Services;
using HospitalManagementSystem.Tests.Fixtures;
using HospitalManagementSystem.Tests.Helpers;
using Xunit;

namespace HospitalManagementSystem.Tests.Portal
{
    public class DependentFamilyAccountTests
    {
        private async Task<(ApplicationDbContext Context, Patient ParentA, Patient ChildA, Patient ParentB, Patient ChildB, User DoctorA, User DoctorB)> SetupFamilyEnvironmentAsync()
        {
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();

            // Configure Family A: Parent A (Id: 100), Child A (Id: 101, linked as "Father")
            var parentA = await context.Patients.FindAsync(100);
            parentA!.UserId = 1000;
            parentA.ContactInfo = 1712345678;
            parentA.EmergencyContactName = "Jane Doe";
            parentA.EmergencyContactPhone = 1798765432;

            var childA = await context.Patients.FindAsync(101);
            childA!.GuardianPatientId = 100;
            childA.GuardianRelationship = "Father";

            // Configure Family B: Parent B (Id: 102), Child B (Id: 103, linked as "Mother")
            var parentB = new Patient
            {
                Id = 102,
                Uhid = "PT-202610-0102",
                FullName = "Parent B",
                UserId = 1002,
                DateOfBirth = new DateTime(1988, 3, 10, 0, 0, 0, DateTimeKind.Utc),
                Gender = "Female",
                BloodGroup = "A+",
                ContactInfo = 1711111111,
                EmergencyContactName = "Spouse B",
                EmergencyContactPhone = 1722222222,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var childB = new Patient
            {
                Id = 103,
                Uhid = "PT-202610-0103",
                FullName = "Child B",
                IsChild = true,
                GuardianPatientId = 102,
                GuardianRelationship = "Mother",
                DateOfBirth = new DateTime(2021, 5, 5, 0, 0, 0, DateTimeKind.Utc),
                Gender = "Male",
                BloodGroup = "A+",
                EmergencyContactName = "Parent B",
                EmergencyContactPhone = 1711111111,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            context.Patients.AddRange(parentB, childB);

            var doctorA = await context.Users.FindAsync(10);
            doctorA!.Category = "Cardiology";

            var doctorB = await context.Users.FindAsync(11);
            doctorB!.Category = "Pediatrics";

            await context.SaveChangesAsync();

            return (context, parentA, childA, parentB, childB, doctorA, doctorB);
        }

        private PortalController CreatePortalController(
            ApplicationDbContext context,
            ClaimsPrincipal user,
            string? activePatientCookie = null)
        {
            var service = new DoctorScheduleService(context);
            var controller = new PortalController(context, service);
            ControllerTestHelper.SetupController(controller, user);

            if (!string.IsNullOrEmpty(activePatientCookie))
            {
                controller.ControllerContext.HttpContext.Request.Headers["Cookie"] = $"Portal_ActivePatientId={activePatientCookie}";
            }

            return controller;
        }

        private PatientsController CreatePatientsController(
            ApplicationDbContext context,
            ClaimsPrincipal user)
        {
            var controller = new PatientsController(context);
            ControllerTestHelper.SetupController(controller, user);
            return controller;
        }

        // =====================================================================
        // SECTION 1: Schema & Model Navigation Tests
        // =====================================================================

        [Fact]
        public async Task Patient_SelfReferencingRelationship_NavigationsAndCascadeRestrict()
        {
            // Arrange
            var (context, parentA, childA, _, _, _, _) = await SetupFamilyEnvironmentAsync();

            // 1. Verify foreign key delete behavior in EF Core model metadata is Restrict
            var patientEntity = context.Model.FindEntityType(typeof(Patient));
            Assert.NotNull(patientEntity);

            var guardianFk = patientEntity.GetForeignKeys()
                .FirstOrDefault(fk => fk.PrincipalEntityType.ClrType == typeof(Patient));
            Assert.NotNull(guardianFk);
            Assert.Equal(DeleteBehavior.Restrict, guardianFk.DeleteBehavior);

            // 2. Verify navigation from dependent child to guardian parent
            var loadedChild = await context.Patients
                .Include(p => p.GuardianPatient)
                .FirstOrDefaultAsync(p => p.Id == childA.Id);

            Assert.NotNull(loadedChild);
            Assert.NotNull(loadedChild.GuardianPatient);
            Assert.Equal(parentA.Id, loadedChild.GuardianPatient.Id);
            Assert.Equal("Father", loadedChild.GuardianRelationship);

            // 3. Verify inverse navigation from guardian parent to dependents
            var loadedParent = await context.Patients
                .Include(p => p.Dependents)
                .FirstOrDefaultAsync(p => p.Id == parentA.Id);

            Assert.NotNull(loadedParent);
            Assert.Single(loadedParent.Dependents);
            Assert.Equal(childA.Id, loadedParent.Dependents.First().Id);
        }

        // =====================================================================
        // SECTION 2: Receptionist Registration Flow (PatientsController.Create)
        // =====================================================================

        [Fact]
        public async Task Create_MinorWithValidGuardianUhid_SuccessfullyLinksGuardianAndFillsEmergencyContact()
        {
            // Arrange
            var (context, parentA, _, _, _, _, _) = await SetupFamilyEnvironmentAsync();
            var receptionist = TestPrincipalFactory.CreateReceptionist();
            var controller = CreatePatientsController(context, receptionist);

            var newMinor = new Patient
            {
                IsChild = true,
                FullName = "Newborn Doe",
                DateOfBirth = DateTime.UtcNow.AddMonths(-6),
                Gender = "Female",
                BloodGroup = "O+"
                // EmergencyContactName and EmergencyContactPhone omitted intentionally
            };

            // Act: Receptionist registers minor with parent A's UHID
            var result = await controller.Create(
                newMinor,
                guardianUhid: parentA.Uhid,
                guardianRelationship: "Father");

            // Assert: Saved successfully and redirected
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirectResult.ActionName);

            var createdPatient = await context.Patients
                .Include(p => p.GuardianPatient)
                .FirstOrDefaultAsync(p => p.FullName == "Newborn Doe");

            Assert.NotNull(createdPatient);
            Assert.True(createdPatient.IsChild);
            Assert.Equal(parentA.Id, createdPatient.GuardianPatientId);
            Assert.Equal("Father", createdPatient.GuardianRelationship);

            // Emergency contact details auto-populated from parent A
            Assert.Equal(parentA.FullName, createdPatient.EmergencyContactName);
            Assert.Equal(parentA.ContactInfo, createdPatient.EmergencyContactPhone);
        }

        [Fact]
        public async Task Create_MinorWithNonExistentGuardianUhid_ReturnsValidationErrorAndDoesNotSave()
        {
            // Arrange
            var (context, _, _, _, _, _, _) = await SetupFamilyEnvironmentAsync();
            var receptionist = TestPrincipalFactory.CreateReceptionist();
            var controller = CreatePatientsController(context, receptionist);

            var newMinor = new Patient
            {
                IsChild = true,
                FullName = "Unlinked Baby",
                DateOfBirth = DateTime.UtcNow.AddYears(-1),
                Gender = "Male",
                EmergencyContactName = "Guardian Name",
                EmergencyContactPhone = 1700000000
            };

            // Act: Receptionist enters invalid guardian UHID
            var result = await controller.Create(
                newMinor,
                guardianUhid: "PT-NONEXISTENT-9999",
                guardianRelationship: "Mother");

            // Assert: Validation error added for GuardianUhid, patient is NOT saved
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("GuardianUhid"));
            Assert.Contains(controller.ModelState["GuardianUhid"]!.Errors, e => e.ErrorMessage.Contains("was not found"));

            var notFoundBaby = await context.Patients.FirstOrDefaultAsync(p => p.FullName == "Unlinked Baby");
            Assert.Null(notFoundBaby);
        }

        [Fact]
        public async Task Create_MinorWithoutGuardianOrEmergencyContact_FailsValidation()
        {
            // Arrange
            var (context, _, _, _, _, _, _) = await SetupFamilyEnvironmentAsync();
            var receptionist = TestPrincipalFactory.CreateReceptionist();
            var controller = CreatePatientsController(context, receptionist);

            var newMinor = new Patient
            {
                IsChild = true,
                FullName = "Abandoned Child",
                DateOfBirth = DateTime.UtcNow.AddYears(-2),
                Gender = "Female"
                // No emergency contacts and no guardian provided
            };

            // Act
            var result = await controller.Create(newMinor, guardianUhid: null, guardianRelationship: null);

            // Assert: Required guardian contact validation triggered for minors
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("EmergencyContactName"));
            Assert.True(controller.ModelState.ContainsKey("EmergencyContactPhone"));
        }

        // =====================================================================
        // SECTION 3: Receptionist Edit Flow (PatientsController.Edit)
        // =====================================================================

        [Fact]
        public async Task Edit_MinorReassignGuardian_UpdatesGuardianLinkAndRelationship()
        {
            // Arrange: Child A was originally linked to Parent A (Father). Reassign to Parent B (Mother).
            var (context, _, childA, parentB, _, _, _) = await SetupFamilyEnvironmentAsync();
            var receptionist = TestPrincipalFactory.CreateReceptionist();
            var controller = CreatePatientsController(context, receptionist);

            // Act: Update guardian to Parent B
            var result = await controller.Edit(
                childA.Id,
                childA,
                guardianUhid: parentB.Uhid,
                guardianRelationship: "Legal Guardian");

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirectResult.ActionName);

            var updatedChild = await context.Patients.FindAsync(childA.Id);
            Assert.NotNull(updatedChild);
            Assert.Equal(parentB.Id, updatedChild.GuardianPatientId);
            Assert.Equal("Legal Guardian", updatedChild.GuardianRelationship);
        }

        [Fact]
        public async Task Edit_MinorClearGuardian_UnlinksDependent()
        {
            // Arrange
            var (context, _, childA, _, _, _, _) = await SetupFamilyEnvironmentAsync();
            var receptionist = TestPrincipalFactory.CreateReceptionist();
            var controller = CreatePatientsController(context, receptionist);

            // Act: Clear guardian UHID
            var result = await controller.Edit(
                childA.Id,
                childA,
                guardianUhid: "",
                guardianRelationship: "");

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirectResult.ActionName);

            var updatedChild = await context.Patients.FindAsync(childA.Id);
            Assert.NotNull(updatedChild);
            Assert.Null(updatedChild.GuardianPatientId);
            Assert.Null(updatedChild.GuardianRelationship);
        }

        [Fact]
        public async Task Edit_PatientAssignSelfAsGuardian_FailsValidation()
        {
            // Arrange: Attempting to assign patient as their own guardian
            var (context, parentA, _, _, _, _, _) = await SetupFamilyEnvironmentAsync();
            parentA.IsChild = true; // Simulating edge case if IsChild flag is set
            await context.SaveChangesAsync();

            var receptionist = TestPrincipalFactory.CreateReceptionist();
            var controller = CreatePatientsController(context, receptionist);

            // Act
            var result = await controller.Edit(
                parentA.Id,
                parentA,
                guardianUhid: parentA.Uhid,
                guardianRelationship: "Self");

            // Assert
            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey("GuardianUhid"));
            Assert.Contains(controller.ModelState["GuardianUhid"]!.Errors, e => e.ErrorMessage.Contains("cannot be their own guardian"));
        }

        // =====================================================================
        // SECTION 4: Portal Context Switching & Profile Scoping
        // =====================================================================

        [Fact]
        public async Task Index_DefaultActiveProfile_ResolvesToParent()
        {
            // Arrange
            var (context, parentA, childA, _, _, _, _) = await SetupFamilyEnvironmentAsync();
            var parentUser = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreatePortalController(context, parentUser);

            // Act: Visit portal without specifying patientId
            var result = await controller.Index();

            // Assert
            var viewResult = Assert.IsType<ViewResult>(result);
            var activePatient = Assert.IsType<Patient>(controller.ViewBag.Patient);
            var primaryPatient = Assert.IsType<Patient>(controller.ViewBag.PrimaryPatient);
            var dependents = Assert.IsAssignableFrom<List<Patient>>(controller.ViewBag.Dependents);

            Assert.Equal(parentA.Id, activePatient.Id);
            Assert.Equal(parentA.Id, primaryPatient.Id);
            Assert.False(controller.ViewBag.IsViewingDependent);
            Assert.Single(dependents);
            Assert.Equal(childA.Id, dependents[0].Id);
        }

        [Fact]
        public async Task Index_ExplicitDependentParameter_ResolvesToChild()
        {
            // Arrange
            var (context, parentA, childA, _, _, _, _) = await SetupFamilyEnvironmentAsync();
            var parentUser = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreatePortalController(context, parentUser);

            // Act: Pass child's patientId
            var result = await controller.Index(patientId: childA.Id);

            // Assert
            var viewResult = Assert.IsType<ViewResult>(result);
            var activePatient = Assert.IsType<Patient>(controller.ViewBag.Patient);
            Assert.Equal(childA.Id, activePatient.Id);
            Assert.True(controller.ViewBag.IsViewingDependent);
        }

        [Fact]
        public async Task SwitchProfile_ValidDependent_SetsCookieAndRedirects()
        {
            // Arrange
            var (context, parentA, childA, _, _, _, _) = await SetupFamilyEnvironmentAsync();
            var parentUser = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreatePortalController(context, parentUser);

            // Act: Switch profile to child
            var result = await controller.SwitchProfile(patientId: childA.Id);

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(PortalController.Index), redirectResult.ActionName);

            var setCookieHeader = controller.Response.Headers["Set-Cookie"].ToString();
            Assert.Contains($"Portal_ActivePatientId={childA.Id}", setCookieHeader);
        }

        [Fact]
        public async Task Portal_CookiePersistence_ResolvesDependentWhenCookiePresent()
        {
            // Arrange: Incoming request with cookie set to Child A's Id
            var (context, parentA, childA, _, _, _, _) = await SetupFamilyEnvironmentAsync();
            var parentUser = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreatePortalController(context, parentUser, activePatientCookie: childA.Id.ToString());

            // Act: Visit Index without query parameter
            var result = await controller.Index();

            // Assert: Active patient resolved from cookie to child
            var viewResult = Assert.IsType<ViewResult>(result);
            var activePatient = Assert.IsType<Patient>(controller.ViewBag.Patient);
            Assert.Equal(childA.Id, activePatient.Id);
            Assert.True(controller.ViewBag.IsViewingDependent);
        }

        [Fact]
        public async Task SwitchProfile_BackToParent_ResolvesParentProfile()
        {
            // Arrange
            var (context, parentA, _, _, _, _, _) = await SetupFamilyEnvironmentAsync();
            var parentUser = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreatePortalController(context, parentUser);

            // Act: Switch profile back to parent
            var result = await controller.SwitchProfile(patientId: parentA.Id);

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(PortalController.Index), redirectResult.ActionName);

            var setCookieHeader = controller.Response.Headers["Set-Cookie"].ToString();
            Assert.Contains($"Portal_ActivePatientId={parentA.Id}", setCookieHeader);
        }

        [Fact]
        public async Task Records_ScopedToActiveProfile()
        {
            // Arrange: Add medical records for Parent A and Child A
            var (context, parentA, childA, _, _, doctorA, _) = await SetupFamilyEnvironmentAsync();

            context.MedicalRecords.AddRange(
                new MedicalRecord
                {
                    PatientId = parentA.Id,
                    DoctorId = doctorA.Id,
                    Diagnosis = "Adult Hypertension",
                    Treatment = "Lifestyle",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new MedicalRecord
                {
                    PatientId = childA.Id,
                    DoctorId = doctorA.Id,
                    Diagnosis = "Pediatric Ear Infection",
                    Treatment = "Amoxicillin drops",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            );
            await context.SaveChangesAsync();

            var parentUser = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreatePortalController(context, parentUser);

            // Act 1: View Child A's records
            var childResult = await controller.Records(patientId: childA.Id);
            var childView = Assert.IsType<ViewResult>(childResult);
            var childRecords = Assert.IsAssignableFrom<List<MedicalRecord>>(childView.Model);
            Assert.Single(childRecords);
            Assert.Equal(childA.Id, childRecords[0].PatientId);
            Assert.Equal("Pediatric Ear Infection", childRecords[0].Diagnosis);

            // Act 2: View Parent A's records
            var parentResult = await controller.Records(patientId: parentA.Id);
            var parentView = Assert.IsType<ViewResult>(parentResult);
            var parentRecords = Assert.IsAssignableFrom<List<MedicalRecord>>(parentView.Model);
            Assert.Single(parentRecords);
            Assert.Equal(parentA.Id, parentRecords[0].PatientId);
            Assert.Equal("Adult Hypertension", parentRecords[0].Diagnosis);
        }

        [Fact]
        public async Task Prescriptions_ScopedToActiveProfile()
        {
            // Arrange: Add prescriptions for Parent A and Child A
            var (context, parentA, childA, _, _, doctorA, _) = await SetupFamilyEnvironmentAsync();

            context.Prescriptions.AddRange(
                new Prescription
                {
                    PatientId = parentA.Id,
                    DoctorId = doctorA.Id,
                    Status = PrescriptionStatus.Dispensed,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new Prescription
                {
                    PatientId = childA.Id,
                    DoctorId = doctorA.Id,
                    Status = PrescriptionStatus.PendingPharmacy,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            );
            await context.SaveChangesAsync();

            var parentUser = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreatePortalController(context, parentUser);

            // Act: View Child A's prescriptions
            var result = await controller.Prescriptions(patientId: childA.Id);
            var viewResult = Assert.IsType<ViewResult>(result);
            var prescriptions = Assert.IsAssignableFrom<List<Prescription>>(viewResult.Model);

            Assert.Single(prescriptions);
            Assert.Equal(childA.Id, prescriptions[0].PatientId);
            Assert.Equal(PrescriptionStatus.PendingPharmacy, prescriptions[0].Status);
        }

        [Fact]
        public async Task Bills_ScopedToActiveProfile()
        {
            // Arrange: Add bills for Parent A and Child A
            var (context, parentA, childA, _, _, _, _) = await SetupFamilyEnvironmentAsync();

            context.Bills.AddRange(
                new Bill
                {
                    PatientId = parentA.Id,
                    SubtotalAmount = 1000m,
                    NetTotal = 1000m,
                    PaidAmount = 1000m,
                    Status = BillStatus.Paid,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new Bill
                {
                    PatientId = childA.Id,
                    SubtotalAmount = 500m,
                    NetTotal = 500m,
                    PaidAmount = 0m,
                    Status = BillStatus.Unpaid,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            );
            await context.SaveChangesAsync();

            var parentUser = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreatePortalController(context, parentUser);

            // Act: View Child A's bills
            var result = await controller.Bills(patientId: childA.Id);
            var viewResult = Assert.IsType<ViewResult>(result);
            var bills = Assert.IsAssignableFrom<List<Bill>>(viewResult.Model);

            Assert.Single(bills);
            Assert.Equal(childA.Id, bills[0].PatientId);
            Assert.Equal(500m, bills[0].NetTotal);
        }

        // =====================================================================
        // SECTION 5: Strict IDOR & Cross-Tenant Security Tests
        // =====================================================================

        [Fact]
        public async Task Index_UnlinkedChildId_ReturnsForbid()
        {
            // Arrange: Parent A attempts to view Child B's dashboard
            var (context, parentA, _, _, childB, _, _) = await SetupFamilyEnvironmentAsync();
            var parentUser = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreatePortalController(context, parentUser);

            // Act
            var result = await controller.Index(patientId: childB.Id);

            // Assert
            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task Records_UnlinkedChildId_ReturnsForbid()
        {
            // Arrange: Parent A attempts to view Child B's medical records
            var (context, parentA, _, _, childB, _, _) = await SetupFamilyEnvironmentAsync();
            var parentUser = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreatePortalController(context, parentUser);

            // Act
            var result = await controller.Records(patientId: childB.Id);

            // Assert
            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task Prescriptions_UnlinkedChildId_ReturnsForbid()
        {
            // Arrange: Parent A attempts to view Child B's prescriptions
            var (context, parentA, _, _, childB, _, _) = await SetupFamilyEnvironmentAsync();
            var parentUser = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreatePortalController(context, parentUser);

            // Act
            var result = await controller.Prescriptions(patientId: childB.Id);

            // Assert
            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task Bills_UnlinkedChildId_ReturnsForbid()
        {
            // Arrange: Parent A attempts to view Child B's bills
            var (context, parentA, _, _, childB, _, _) = await SetupFamilyEnvironmentAsync();
            var parentUser = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreatePortalController(context, parentUser);

            // Act
            var result = await controller.Bills(patientId: childB.Id);

            // Assert
            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task Doctors_UnlinkedChildId_ReturnsForbid()
        {
            // Arrange: Parent A attempts to browse doctors scoping to Child B
            var (context, parentA, _, _, childB, _, _) = await SetupFamilyEnvironmentAsync();
            var parentUser = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreatePortalController(context, parentUser);

            // Act
            var result = await controller.Doctors(search: null, category: null, patientId: childB.Id);

            // Assert
            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task SwitchProfile_UnlinkedChildId_ReturnsForbid()
        {
            // Arrange: Parent A attempts to switch profile to Child B
            var (context, parentA, _, _, childB, _, _) = await SetupFamilyEnvironmentAsync();
            var parentUser = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreatePortalController(context, parentUser);

            // Act
            var result = await controller.SwitchProfile(patientId: childB.Id);

            // Assert
            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task Book_POST_UnlinkedChildId_ReturnsForbid()
        {
            // Arrange: Parent A attempts to book an appointment for Child B
            var (context, parentA, _, _, childB, doctorA, _) = await SetupFamilyEnvironmentAsync();
            var parentUser = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreatePortalController(context, parentUser);

            var futureDate = HospitalClock.Today.AddDays(3);
            var slotLocal = futureDate.AddHours(10).AddMinutes(0);
            var slotUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(slotLocal, DateTimeKind.Unspecified),
                HospitalClock.TimeZone);

            var model = new BookAppointmentViewModel
            {
                DoctorId = doctorA.Id,
                PatientId = childB.Id,
                SelectedSlotTime = slotUtc.ToString("O"),
                SelectedDate = futureDate.ToString("yyyy-MM-dd"),
                ReasonForVisit = "Malicious booking for another child"
            };

            // Act
            var result = await controller.Book(model);

            // Assert: Strictly forbidden
            Assert.IsType<ForbidResult>(result);
            Assert.Empty(await context.Appointments.Where(a => a.PatientId == childB.Id).ToListAsync());
        }

        [Fact]
        public async Task Reschedule_GET_UnlinkedChildAppointment_ReturnsNotFound()
        {
            // Arrange: Child B has an upcoming appointment
            var (context, parentA, _, _, childB, doctorA, _) = await SetupFamilyEnvironmentAsync();
            var futureDate = HospitalClock.Today.AddDays(3);
            var slotLocal = futureDate.AddHours(10).AddMinutes(0);
            var slotUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(slotLocal, DateTimeKind.Unspecified),
                HospitalClock.TimeZone);

            var childBAppt = new Appointment
            {
                DoctorId = doctorA.Id,
                PatientId = childB.Id,
                AppointmentDatetime = slotUtc,
                EndTime = slotUtc.AddMinutes(15),
                Status = AppointmentStatus.Scheduled,
                ReasonForVisit = "Child B Visit",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.Add(childBAppt);
            await context.SaveChangesAsync();

            // Act: Parent A tries to open reschedule page for Child B's appointment
            var parentUser = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreatePortalController(context, parentUser);

            var result = await controller.Reschedule(childBAppt.Id);

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task Reschedule_POST_UnlinkedChildAppointment_ReturnsNotFound()
        {
            // Arrange: Child B has an appointment
            var (context, parentA, _, _, childB, doctorA, _) = await SetupFamilyEnvironmentAsync();
            var futureDate = HospitalClock.Today.AddDays(3);
            var slotLocal = futureDate.AddHours(10).AddMinutes(0);
            var slotUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(slotLocal, DateTimeKind.Unspecified),
                HospitalClock.TimeZone);

            var childBAppt = new Appointment
            {
                DoctorId = doctorA.Id,
                PatientId = childB.Id,
                AppointmentDatetime = slotUtc,
                EndTime = slotUtc.AddMinutes(15),
                Status = AppointmentStatus.Scheduled,
                ReasonForVisit = "Child B Visit",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.Add(childBAppt);
            await context.SaveChangesAsync();

            // Act: Parent A tries to POST reschedule for Child B's appointment
            var parentUser = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreatePortalController(context, parentUser);

            var newSlotLocal = futureDate.AddHours(14).AddMinutes(0);
            var newSlotUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(newSlotLocal, DateTimeKind.Unspecified),
                HospitalClock.TimeZone);

            var model = new RescheduleAppointmentViewModel
            {
                AppointmentId = childBAppt.Id,
                DoctorId = doctorA.Id,
                SelectedSlotTime = newSlotUtc.ToString("O"),
                SelectedDate = futureDate.ToString("yyyy-MM-dd"),
                ReasonForVisit = "Unauthorized Reschedule",
                PatientId = childB.Id
            };

            var result = await controller.Reschedule(model);

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task Cancel_POST_UnlinkedChildAppointment_ReturnsNotFound()
        {
            // Arrange: Child B has an appointment
            var (context, parentA, _, _, childB, doctorA, _) = await SetupFamilyEnvironmentAsync();
            var futureDate = HospitalClock.Today.AddDays(3);
            var slotLocal = futureDate.AddHours(10).AddMinutes(0);
            var slotUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(slotLocal, DateTimeKind.Unspecified),
                HospitalClock.TimeZone);

            var childBAppt = new Appointment
            {
                DoctorId = doctorA.Id,
                PatientId = childB.Id,
                AppointmentDatetime = slotUtc,
                EndTime = slotUtc.AddMinutes(15),
                Status = AppointmentStatus.Scheduled,
                ReasonForVisit = "Child B Visit",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.Add(childBAppt);
            await context.SaveChangesAsync();

            // Act: Parent A attempts to cancel Child B's appointment
            var parentUser = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreatePortalController(context, parentUser);

            var result = await controller.Cancel(childBAppt.Id);

            // Assert: Access denied with NotFound
            Assert.IsType<NotFoundResult>(result);

            // Ensure appointment status remains Scheduled
            var unchangedAppt = await context.Appointments.FindAsync(childBAppt.Id);
            Assert.Equal(AppointmentStatus.Scheduled, unchangedAppt!.Status);
        }

        // =====================================================================
        // SECTION 6: Legitimate Family Actions (Parent managing linked child)
        // =====================================================================

        [Fact]
        public async Task Book_ForLinkedChild_CreatesAppointmentUnderChildId()
        {
            // Arrange: Parent A books appointment for Child A
            var (context, parentA, childA, _, _, doctorA, _) = await SetupFamilyEnvironmentAsync();
            var parentUser = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreatePortalController(context, parentUser);

            var futureDate = HospitalClock.Today.AddDays(4);
            var slotLocal = futureDate.AddHours(11).AddMinutes(0);
            var slotUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(slotLocal, DateTimeKind.Unspecified),
                HospitalClock.TimeZone);

            var model = new BookAppointmentViewModel
            {
                DoctorId = doctorA.Id,
                PatientId = childA.Id,
                SelectedSlotTime = slotUtc.ToString("O"),
                SelectedDate = futureDate.ToString("yyyy-MM-dd"),
                ReasonForVisit = "Pediatric routine checkup"
            };

            // Act
            var result = await controller.Book(model);

            // Assert: Redirects to Index
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(PortalController.Index), redirectResult.ActionName);

            // Verify appointment saved under child's ID
            var appt = await context.Appointments.FirstOrDefaultAsync(a => a.PatientId == childA.Id);
            Assert.NotNull(appt);
            Assert.Equal(childA.Id, appt.PatientId);
            Assert.Equal(doctorA.Id, appt.DoctorId);
            Assert.Equal(slotUtc, appt.AppointmentDatetime);
            Assert.Equal("Pediatric routine checkup", appt.ReasonForVisit);

            // Success message explicitly mentions the child's name
            var successMessage = controller.TempData["SuccessMessage"]?.ToString();
            Assert.NotNull(successMessage);
            Assert.Contains(childA.FullName!, successMessage);
        }

        [Fact]
        public async Task Book_ForLinkedChild_DetectsChildScheduleCollision()
        {
            // Arrange: Child A already has an appointment with Doctor B at 10:00 AM
            var (context, parentA, childA, _, _, doctorA, doctorB) = await SetupFamilyEnvironmentAsync();
            var futureDate = HospitalClock.Today.AddDays(4);
            var slotLocal = futureDate.AddHours(10).AddMinutes(0);
            var slotUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(slotLocal, DateTimeKind.Unspecified),
                HospitalClock.TimeZone);

            var existingAppt = new Appointment
            {
                DoctorId = doctorB.Id,
                PatientId = childA.Id,
                AppointmentDatetime = slotUtc,
                EndTime = slotUtc.AddMinutes(15),
                Status = AppointmentStatus.Scheduled,
                ReasonForVisit = "With Doctor B",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.Add(existingAppt);
            await context.SaveChangesAsync();

            // Act: Parent A tries to book Doctor A for Child A at the exact same time
            var parentUser = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreatePortalController(context, parentUser);

            var model = new BookAppointmentViewModel
            {
                DoctorId = doctorA.Id,
                PatientId = childA.Id,
                SelectedSlotTime = slotUtc.ToString("O"),
                SelectedDate = futureDate.ToString("yyyy-MM-dd"),
                ReasonForVisit = "Conflicting consultation"
            };

            var result = await controller.Book(model);

            // Assert: Rejected due to child's active appointment collision
            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.False(controller.ModelState.IsValid);
            Assert.Contains(controller.ModelState.Values, v =>
                v.Errors.Any(e => e.ErrorMessage.Contains(childA.FullName!) &&
                                  e.ErrorMessage.Contains("already has another active appointment scheduled")));
        }

        [Fact]
        public async Task Reschedule_ForLinkedChild_UpdatesChildAppointmentTime()
        {
            // Arrange: Child A has an upcoming scheduled appointment
            var (context, parentA, childA, _, _, doctorA, _) = await SetupFamilyEnvironmentAsync();
            var futureDate = HospitalClock.Today.AddDays(4);
            var oldSlotLocal = futureDate.AddHours(10).AddMinutes(0);
            var oldSlotUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(oldSlotLocal, DateTimeKind.Unspecified),
                HospitalClock.TimeZone);

            var appt = new Appointment
            {
                DoctorId = doctorA.Id,
                PatientId = childA.Id,
                AppointmentDatetime = oldSlotUtc,
                EndTime = oldSlotUtc.AddMinutes(15),
                Status = AppointmentStatus.Scheduled,
                ReasonForVisit = "Initial visit",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.Add(appt);
            await context.SaveChangesAsync();

            var parentUser = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreatePortalController(context, parentUser);

            // Act 1: GET Reschedule - Parent is permitted to view reschedule screen for child's appointment
            var getResult = await controller.Reschedule(appt.Id);
            var viewResult = Assert.IsType<ViewResult>(getResult);
            var viewModel = Assert.IsType<RescheduleAppointmentViewModel>(viewResult.Model);
            Assert.Equal(appt.Id, viewModel.AppointmentId);
            Assert.Equal(childA.Id, viewModel.PatientId);

            // Act 2: POST Reschedule to afternoon slot
            var newSlotLocal = futureDate.AddHours(14).AddMinutes(0);
            var newSlotUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(newSlotLocal, DateTimeKind.Unspecified),
                HospitalClock.TimeZone);

            viewModel.SelectedSlotTime = newSlotUtc.ToString("O");
            viewModel.SelectedDate = futureDate.ToString("yyyy-MM-dd");
            viewModel.ReasonForVisit = "Rescheduled by parent for after school";

            var postResult = await controller.Reschedule(viewModel);

            // Assert: Redirects to Index and datetime is updated
            var redirectResult = Assert.IsType<RedirectToActionResult>(postResult);
            Assert.Equal(nameof(PortalController.Index), redirectResult.ActionName);

            var updatedAppt = await context.Appointments.FindAsync(appt.Id);
            Assert.NotNull(updatedAppt);
            Assert.Equal(newSlotUtc, updatedAppt.AppointmentDatetime);
            Assert.Equal("Rescheduled by parent for after school", updatedAppt.ReasonForVisit);
        }

        [Fact]
        public async Task Cancel_ForLinkedChild_CancelsChildAppointmentAndFreesSlot()
        {
            // Arrange: Child A has an upcoming scheduled appointment
            var (context, parentA, childA, _, _, doctorA, _) = await SetupFamilyEnvironmentAsync();
            var futureDate = HospitalClock.Today.AddDays(4);
            var slotLocal = futureDate.AddHours(10).AddMinutes(0);
            var slotUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(slotLocal, DateTimeKind.Unspecified),
                HospitalClock.TimeZone);

            var appt = new Appointment
            {
                DoctorId = doctorA.Id,
                PatientId = childA.Id,
                AppointmentDatetime = slotUtc,
                EndTime = slotUtc.AddMinutes(15),
                Status = AppointmentStatus.Scheduled,
                ReasonForVisit = "Checkup",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Appointments.Add(appt);
            await context.SaveChangesAsync();

            var parentUser = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreatePortalController(context, parentUser);

            // Act: Parent cancels child's appointment
            var result = await controller.Cancel(appt.Id);

            // Assert: Redirects and marks cancelled
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(PortalController.Index), redirectResult.ActionName);

            var cancelledAppt = await context.Appointments.FindAsync(appt.Id);
            Assert.NotNull(cancelledAppt);
            Assert.Equal(AppointmentStatus.Cancelled, cancelledAppt.Status);

            var successMessage = controller.TempData["SuccessMessage"]?.ToString();
            Assert.NotNull(successMessage);
            Assert.Contains("successfully cancelled", successMessage);
        }
    }
}
