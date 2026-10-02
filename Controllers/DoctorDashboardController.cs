using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Generic;
using System.Text.Json;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Models.ViewModels;
using HospitalManagementSystem.Services;
using HospitalManagementSystem.Hubs;

namespace HospitalManagementSystem.Controllers
{
    [Authorize(Roles = "Doctor")]
    public class DoctorDashboardController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<NotificationHub> _hubContext;

        public DoctorDashboardController(ApplicationDbContext context, IHubContext<NotificationHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        // GET: DoctorDashboard
        public async Task<IActionResult> Index()
        {
            var userIdClaim = User.FindFirst("UserId")?.Value;
            int.TryParse(userIdClaim, out int doctorId);

            // Fetch appointments that are currently InConsultation, scoped to this doctor
            // Filter to visits from the last 24 hours to prevent stale/abandoned consultations from lingering indefinitely
            var cutoff = DateTime.UtcNow.AddHours(-24);
            var activeConsultations = await _context.Appointments
                .Include(a => a.Patient)
                .Include(a => a.Doctor)
                .Where(a => a.Status == AppointmentStatus.InConsultation && a.DoctorId == doctorId && a.UpdatedAt >= cutoff)
                .OrderBy(a => a.UpdatedAt) // Oldest sent in first
                .ToListAsync();

            // The latest CaseSummary draft per patient
            var patientIds = activeConsultations.Select(a => a.PatientId).ToList();
            var latestSuggestionByPatient = (await _context.AiSuggestions
                    .Include(s => s.ReviewedBy)
                    .Where(s => patientIds.Contains(s.PatientId) && s.SuggestionType == AiSuggestionType.CaseSummary)
                    .OrderByDescending(s => s.CreatedAt)
                    .ToListAsync())
                .GroupBy(s => s.PatientId)
                .ToDictionary(g => g.Key, g => g.First());

            ViewBag.LatestAiSuggestionByPatient = latestSuggestionByPatient;

            // Pre-fetch allergies so the dashboard cards immediately alert for severe anaphylactic reactions
            var allergiesByPatient = (await _context.PatientAllergies
                    .Where(al => patientIds.Contains(al.PatientId))
                    .ToListAsync())
                .GroupBy(al => al.PatientId)
                .ToDictionary(g => g.Key, g => g.ToList());
            ViewBag.AllergiesByPatient = allergiesByPatient;

            // The vitals tied to this specific visit (by AppointmentId)
            var appointmentIds = activeConsultations.Select(a => a.Id).ToList();
            ViewBag.VitalsByAppointment = (await _context.PatientVitals
                    .Where(v => v.AppointmentId.HasValue && appointmentIds.Contains(v.AppointmentId.Value))
                    .OrderByDescending(v => v.CreatedAt)
                    .ToListAsync())
                .GroupBy(v => v.AppointmentId!.Value)
                .ToDictionary(g => g.Key, g => g.First());

            return View(activeConsultations);
        }

        // POST: DoctorDashboard/MarkCompleted/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkCompleted(int id)
        {
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (!int.TryParse(userIdClaim, out int currentDoctorId))
            {
                return Forbid();
            }

            var appointment = await _context.Appointments
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (appointment != null && appointment.Status == AppointmentStatus.InConsultation)
            {
                if (appointment.DoctorId != currentDoctorId)
                {
                    return Forbid();
                }

                try
                {
                    appointment.Status = AppointmentStatus.Completed;
                    appointment.UpdatedAt = DateTime.UtcNow;
                    _context.Update(appointment);
                    await _context.SaveChangesAsync();

                    // Notify Assistant
                    await _hubContext.Clients.Group($"Doctor_{appointment.DoctorId}")
                        .SendAsync("ReceiveNotification", appointment.Id, appointment.Patient?.FullName ?? "Unknown Patient");

                    TempData["SuccessMessage"] = "Consultation marked as completed.";
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Concurrency race (e.g. double-click): already completed
                    TempData["InfoMessage"] = "Consultation was already completed.";
                }
            }
            return RedirectToAction(nameof(Index));
        }

        // POST: DoctorDashboard/ReturnToWaitingRoom/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReturnToWaitingRoom(int id)
        {
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (!int.TryParse(userIdClaim, out int currentDoctorId))
            {
                return Forbid();
            }

            var appointment = await _context.Appointments
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (appointment != null && appointment.Status == AppointmentStatus.InConsultation)
            {
                if (appointment.DoctorId != currentDoctorId)
                {
                    return Forbid();
                }

                try
                {
                    appointment.Status = AppointmentStatus.Scheduled;
                    appointment.UpdatedAt = DateTime.UtcNow;
                    _context.Update(appointment);
                    await _context.SaveChangesAsync();

                    // Notify Assistant that patient is back in queue
                    await _hubContext.Clients.Group($"Doctor_{appointment.DoctorId}")
                        .SendAsync("ReceiveNotification", appointment.Id, $"Patient {appointment.Patient?.FullName ?? "Unknown"} returned to waiting room.");

                    TempData["SuccessMessage"] = "Patient returned to waiting room queue.";
                }
                catch (DbUpdateConcurrencyException)
                {
                    TempData["ErrorMessage"] = "Could not update appointment status due to a concurrent modification.";
                }
            }
            return RedirectToAction(nameof(Index));
        }

        // POST: DoctorDashboard/CancelConsultation/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelConsultation(int id, string? reason)
        {
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (!int.TryParse(userIdClaim, out int currentDoctorId))
            {
                return Forbid();
            }

            var appointment = await _context.Appointments
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (appointment != null && appointment.Status == AppointmentStatus.InConsultation)
            {
                if (appointment.DoctorId != currentDoctorId)
                {
                    return Forbid();
                }

                try
                {
                    appointment.Status = AppointmentStatus.Cancelled;
                    appointment.UpdatedAt = DateTime.UtcNow;
                    if (!string.IsNullOrWhiteSpace(reason))
                    {
                        appointment.ReasonForVisit = string.IsNullOrWhiteSpace(appointment.ReasonForVisit)
                            ? $"Cancelled: {reason}"
                            : $"{appointment.ReasonForVisit} [Cancelled: {reason}]";
                    }
                    _context.Update(appointment);
                    await _context.SaveChangesAsync();

                    // Notify Assistant
                    await _hubContext.Clients.Group($"Doctor_{appointment.DoctorId}")
                        .SendAsync("ReceiveNotification", appointment.Id, $"Consultation for {appointment.Patient?.FullName ?? "Unknown"} was cancelled (walkout / no-show).");

                    TempData["SuccessMessage"] = "Consultation cancelled (patient walkout / no-show).";
                }
                catch (DbUpdateConcurrencyException)
                {
                    TempData["ErrorMessage"] = "Could not cancel appointment due to a concurrent modification.";
                }
            }
            return RedirectToAction(nameof(Index));
        }

        // GET: DoctorDashboard/Cockpit/5
        public async Task<IActionResult> Cockpit(int id)
        {
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (!int.TryParse(userIdClaim, out int doctorId)) return Forbid();

            var appointment = await _context.Appointments
                .Include(a => a.Patient)
                    .ThenInclude(p => p.GuardianPatient)
                .Include(a => a.Doctor)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (appointment == null) return NotFound();

            if (appointment.DoctorId != doctorId)
            {
                return Forbid();
            }

            if (appointment.Status != AppointmentStatus.InConsultation)
            {
                TempData["ErrorMessage"] = $"This appointment is not currently in consultation (Status: {appointment.Status}).";
                return RedirectToAction(nameof(Index));
            }

            var patient = appointment.Patient;
            if (patient == null) return NotFound();

            // Load allergies
            var allergies = await _context.PatientAllergies
                .Where(a => a.PatientId == patient.Id)
                .OrderByDescending(a => a.Severity)
                .ToListAsync();

            // Load latest vitals for this visit (or fallback to latest patient vitals)
            var vitals = await _context.PatientVitals
                .Where(v => v.AppointmentId == appointment.Id)
                .OrderByDescending(v => v.CreatedAt)
                .FirstOrDefaultAsync();

            if (vitals == null)
            {
                vitals = await _context.PatientVitals
                    .Where(v => v.PatientId == patient.Id)
                    .OrderByDescending(v => v.CreatedAt)
                    .FirstOrDefaultAsync();
            }

            // Calculate NEWS2
            News2Calculator.Result? news2 = null;
            if (vitals != null)
            {
                news2 = News2Calculator.TryCalculate(
                    patient.IsChild,
                    vitals.RespiratoryRate,
                    vitals.Spo2,
                    vitals.OnSupplementalOxygen,
                    vitals.Temperature,
                    vitals.SystolicBp,
                    vitals.HeartRate,
                    vitals.Consciousness
                );
            }

            // Load active medications
            var activeMeds = await GetActiveMedicationsAsync(patient.Id);

            // Load latest AI Case Summary
            var latestAi = await _context.AiSuggestions
                .Include(s => s.ReviewedBy)
                .Where(s => s.PatientId == patient.Id && s.SuggestionType == AiSuggestionType.CaseSummary)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync();

            // Load formulary medicines
            var medicines = await _context.Medicines
                .Select(m => new CockpitMedicineDto
                {
                    Id = m.Id,
                    Name = m.Name,
                    GenericName = m.GenericName ?? "",
                    DisplayName = m.Name + " (" + (m.GenericName ?? "") + ") - ৳" + m.UnitPrice.ToString("0.00"),
                    UnitPrice = m.UnitPrice,
                    StockQuantity = m.StockQuantity,
                    Strength = m.Strength ?? ""
                })
                .ToListAsync();

            // Check if existing MedicalRecord exists
            var existingRecord = await _context.MedicalRecords
                .FirstOrDefaultAsync(r => r.AppointmentId == appointment.Id);

            // Check if existing Prescription exists
            var existingPrescription = await _context.Prescriptions
                .Include(p => p.PrescriptionItems)
                .FirstOrDefaultAsync(p => p.AppointmentId == appointment.Id && p.Status == PrescriptionStatus.PendingPharmacy);

            var viewModel = new ClinicalCockpitViewModel
            {
                Appointment = appointment,
                Patient = patient,
                AgeDisplay = ComputeAgeDisplay(patient.DateOfBirth, patient.IsChild),
                Allergies = allergies,
                LatestVitals = vitals,
                News2Result = news2,
                ActiveMedications = activeMeds,
                LatestAiCaseSummary = latestAi,
                FormularyMedicines = medicines,
                ExistingMedicalRecordId = existingRecord?.Id,
                ChiefComplaint = existingRecord?.ChiefComplaint ?? appointment.ReasonForVisit,
                Diagnosis = existingRecord?.Diagnosis ?? existingPrescription?.Diagnosis,
                Treatment = existingRecord?.Treatment ?? existingPrescription?.Notes,
                ExistingPrescriptionId = existingPrescription?.Id,
                ExistingPrescriptionItems = existingPrescription?.PrescriptionItems ?? new()
            };

            return View("Cockpit", viewModel);
        }

        // POST: DoctorDashboard/CompleteConsultation
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompleteConsultation(ClinicalCockpitSubmitModel model)
        {
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (!int.TryParse(userIdClaim, out int doctorId)) return Forbid();

            var appointment = await _context.Appointments
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == model.AppointmentId);

            if (appointment == null) return NotFound();

            if (appointment.DoctorId != doctorId)
            {
                return Forbid();
            }

            if (appointment.Status != AppointmentStatus.InConsultation)
            {
                TempData["ErrorMessage"] = $"This consultation cannot be completed because its status is '{appointment.Status}'.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(model.Diagnosis))
            {
                TempData["ErrorMessage"] = "Clinical diagnosis is required to complete the consultation.";
                return RedirectToAction(nameof(Cockpit), new { id = model.AppointmentId });
            }

            // Deduplication safety check: each medicine can only be prescribed once per prescription
            if (model.PrescriptionItems != null && model.PrescriptionItems.Count > 0)
            {
                var duplicateIds = model.PrescriptionItems
                    .GroupBy(i => i.MedicineId)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToList();

                if (duplicateIds.Count > 0)
                {
                    TempData["ErrorMessage"] = "Duplicate medicines detected in prescription. Each medicine may only be prescribed once per prescription.";
                    return RedirectToAction(nameof(Cockpit), new { id = model.AppointmentId });
                }
            }

            // Run safety check if medicines are prescribed
            List<SafetyWarning> warnings = new();
            if (model.PrescriptionItems != null && model.PrescriptionItems.Count > 0)
            {
                var safetyItems = model.PrescriptionItems
                    .Select(i => new SafetyCheckItem(i.MedicineId, i.Quantity, i.DoseUnit))
                    .ToList();
                warnings = await ComputeSafetyWarningsAsync(appointment.PatientId, safetyItems);

                if (warnings.Count > 0 && string.IsNullOrWhiteSpace(model.SafetyOverrideReason))
                {
                    TempData["ErrorMessage"] = $"Safety warnings were raised ({warnings.Count} warning(s)). You must acknowledge and provide an override rationale to proceed.";
                    return RedirectToAction(nameof(Cockpit), new { id = model.AppointmentId });
                }
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. Update Appointment Status
                appointment.Status = AppointmentStatus.Completed;
                appointment.UpdatedAt = DateTime.UtcNow;
                _context.Update(appointment);

                // 2. Persist Medical Record
                var existingRecord = await _context.MedicalRecords
                    .FirstOrDefaultAsync(r => r.AppointmentId == appointment.Id);

                if (existingRecord == null)
                {
                    var record = new MedicalRecord
                    {
                        PatientId = appointment.PatientId,
                        DoctorId = doctorId,
                        AppointmentId = appointment.Id,
                        ChiefComplaint = model.ChiefComplaint,
                        Diagnosis = model.Diagnosis,
                        Treatment = string.IsNullOrWhiteSpace(model.Treatment) ? "Encounter completed." : model.Treatment,
                        RecordedAt = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _context.MedicalRecords.Add(record);
                }
                else
                {
                    existingRecord.ChiefComplaint = model.ChiefComplaint;
                    existingRecord.Diagnosis = model.Diagnosis;
                    existingRecord.Treatment = string.IsNullOrWhiteSpace(model.Treatment) ? "Encounter completed." : model.Treatment;
                    existingRecord.UpdatedAt = DateTime.UtcNow;
                    _context.MedicalRecords.Update(existingRecord);
                }

                // 3. Persist Prescription if medicines were included
                if (model.PrescriptionItems != null && model.PrescriptionItems.Count > 0)
                {
                    var prescription = new Prescription
                    {
                        PatientId = appointment.PatientId,
                        DoctorId = doctorId,
                        AppointmentId = appointment.Id,
                        ChiefComplaints = model.ChiefComplaint,
                        Diagnosis = model.Diagnosis,
                        Notes = model.Treatment,
                        Status = PrescriptionStatus.PendingPharmacy,
                        SafetyOverrideReason = warnings.Count > 0 ? model.SafetyOverrideReason : null,
                        SafetyWarningsJson = warnings.Count > 0
                            ? JsonSerializer.Serialize(warnings.Select(w => new { w.Category, Severity = w.Severity.ToString(), w.Message }))
                            : null,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    foreach (var itemInput in model.PrescriptionItems)
                    {
                        var med = await _context.Medicines.FindAsync(itemInput.MedicineId);
                        if (med != null)
                        {
                            prescription.PrescriptionItems.Add(new PrescriptionItem
                            {
                                MedicineId = med.Id,
                                Quantity = itemInput.Quantity,
                                DoseMorning = itemInput.DoseMorning,
                                DoseAfternoon = itemInput.DoseAfternoon,
                                DoseEvening = itemInput.DoseEvening,
                                DoseUnit = itemInput.DoseUnit,
                                DurationDays = itemInput.DurationDays,
                                Route = itemInput.Route,
                                Instructions = itemInput.Instructions,
                                UnitPrice = med.UnitPrice
                            });
                        }
                    }

                    _context.Prescriptions.Add(prescription);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // Notify Assistant
                await _hubContext.Clients.Group($"Doctor_{appointment.DoctorId}")
                    .SendAsync("ReceiveNotification", appointment.Id, appointment.Patient?.FullName ?? "Unknown Patient");

                TempData["SuccessMessage"] = $"Consultation for {appointment.Patient?.FullName} completed successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync();
                TempData["InfoMessage"] = "This consultation was already completed or modified concurrently.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                TempData["ErrorMessage"] = "An error occurred while saving the consultation encounter.";
                return RedirectToAction(nameof(Cockpit), new { id = model.AppointmentId });
            }
        }

        private async Task<List<ActiveMedicationInfo>> GetActiveMedicationsAsync(int patientId)
        {
            var now = DateTime.UtcNow;

            var dispensedPrescriptions = await _context.Prescriptions
                .AsNoTracking()
                .Where(p => p.PatientId == patientId && p.Status == PrescriptionStatus.Dispensed)
                .Include(p => p.PrescriptionItems)
                    .ThenInclude(pi => pi.Medicine)
                .OrderByDescending(p => p.DispensedAt ?? p.CreatedAt)
                .Take(20)
                .ToListAsync();

            var activeMeds = new List<ActiveMedicationInfo>();

            foreach (var p in dispensedPrescriptions)
            {
                var startDate = p.DispensedAt ?? p.CreatedAt;
                foreach (var item in p.PrescriptionItems)
                {
                    if (item.Medicine == null) continue;

                    var duration = item.DurationDays ?? 30;
                    var endDate = startDate.AddDays(duration);

                    if (endDate >= now)
                    {
                        activeMeds.Add(new ActiveMedicationInfo(
                            item.MedicineId,
                            item.Medicine.Name,
                            item.Medicine.GenericName,
                            startDate,
                            item.DurationDays,
                            p.Id
                        ));
                    }
                }
            }

            return activeMeds;
        }

        private async Task<List<SafetyWarning>> ComputeSafetyWarningsAsync(int patientId, List<SafetyCheckItem> items)
        {
            var patient = await _context.Patients.FindAsync(patientId);
            if (patient == null || items.Count == 0)
            {
                return new List<SafetyWarning>();
            }

            var allergies = await _context.PatientAllergies
                .Where(a => a.PatientId == patientId)
                .Select(a => new SafetyCheckAllergy(a.Substance, a.AllergenGenericName, a.Severity))
                .ToListAsync();

            var allMedicines = await _context.Medicines
                .Select(m => new MedicineInfo(m.Id, m.Name, m.GenericName, m.Strength, m.StockQuantity))
                .ToListAsync();

            var activeMedications = await GetActiveMedicationsAsync(patientId);

            return PrescriptionSafetyChecker.Check(patient.IsChild, allergies, items, allMedicines, activeMedications);
        }

        private static string ComputeAgeDisplay(DateTime dob, bool isChild)
        {
            var today = DateTime.UtcNow.Date;
            var totalMonths = ((today.Year - dob.Year) * 12) + today.Month - dob.Month;
            if (today.Day < dob.Day) totalMonths--;

            if (totalMonths < 0) return "0 mos";

            int years = totalMonths / 12;
            int months = totalMonths % 12;

            if (isChild)
            {
                if (years == 0) return $"{months} mos";
                return months > 0 ? $"{years} yrs {months} mos" : $"{years} yrs";
            }

            return $"{years} yrs";
        }

        // GET: DoctorDashboard/MyAssistant
        public async Task<IActionResult> MyAssistant()
        {
            var doctorIdClaim = User.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;
            if (int.TryParse(doctorIdClaim, out int docId))
            {
                var assistants = await _context.Users
                    .Where(u => u.AssignedDoctorId == docId && u.Role.RoleName == "Assistant")
                    .ToListAsync();
                
                return View(assistants);
            }
            return RedirectToAction(nameof(Index));
        }

        // POST: DoctorDashboard/CreateAssistant
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAssistant(string username, string password, string fullName)
        {
            var doctorIdClaim = User.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;
            if (int.TryParse(doctorIdClaim, out int docId))
            {
                if (await _context.Users.AnyAsync(u => u.Username == username))
                {
                    TempData["Error"] = "Username is already taken.";
                    return RedirectToAction(nameof(MyAssistant));
                }

                var role = await _context.Roles.FirstOrDefaultAsync(r => r.RoleName == "Assistant");
                
                var assistant = new User
                {
                    Username = username,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                    FullName = fullName,
                    RoleId = role.Id,
                    AssignedDoctorId = docId,
                    Category = "Staff",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.Users.Add(assistant);
                await _context.SaveChangesAsync();
                
                TempData["Success"] = "Assistant created successfully!";
            }
            return RedirectToAction(nameof(MyAssistant));
        }

        // POST: DoctorDashboard/FireAssistant/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FireAssistant(int id)
        {
            var doctorIdClaim = User.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;
            if (int.TryParse(doctorIdClaim, out int docId))
            {
                var assistant = await _context.Users.FindAsync(id);
                if (assistant != null && assistant.AssignedDoctorId == docId)
                {
                    _context.Users.Remove(assistant);
                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Assistant account deleted (fired) successfully!";
                }
            }
            return RedirectToAction(nameof(MyAssistant));
        }

        // POST: DoctorDashboard/UpdateAssistant/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateAssistant(int id, string fullName, string username, string password)
        {
            var doctorIdClaim = User.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;
            if (int.TryParse(doctorIdClaim, out int docId))
            {
                var assistant = await _context.Users.FindAsync(id);
                if (assistant != null && assistant.AssignedDoctorId == docId)
                {
                    if (await _context.Users.AnyAsync(u => u.Username == username && u.Id != id))
                    {
                        TempData["Error"] = "Username is already taken by another user.";
                        return RedirectToAction(nameof(MyAssistant));
                    }

                    assistant.FullName = fullName;
                    assistant.Username = username;
                    if (!string.IsNullOrEmpty(password))
                    {
                        assistant.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
                    }
                    assistant.UpdatedAt = DateTime.UtcNow;

                    _context.Users.Update(assistant);
                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Assistant credentials updated successfully!";
                }
            }
            return RedirectToAction(nameof(MyAssistant));
        }
    }
}
