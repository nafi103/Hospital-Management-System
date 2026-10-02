using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HospitalManagementSystem.Models;

using System.Collections.Generic;
using System.Globalization;
using Microsoft.AspNetCore.Mvc.Rendering;
using HospitalManagementSystem.Services;
using HospitalManagementSystem.Models.ViewModels;

namespace HospitalManagementSystem.Controllers
{
    // The patient-facing self-service portal. Every action resolves the caller's own
    // Patient row from their login (never from a route/query id) and filters strictly on
    // that patient's Id - a patient portal is the one place in this app where identity,
    // not role, has to gate the data, so no action here may ever trust a caller-supplied
    // patient id.
    [Authorize(Roles = "Patient")]
    public class PortalController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IAppointmentBookingService _bookingService;

        public PortalController(ApplicationDbContext context, IAppointmentBookingService bookingService)
        {
            _context = context;
            _bookingService = bookingService;
        }

        private async Task<(Patient? ActivePatient, Patient? PrimaryPatient, List<Patient> Dependents, bool IsIdorForbidden)> ResolvePatientContextAsync(int? requestedPatientId = null)
        {
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
            {
                return (null, null, new List<Patient>(), false);
            }

            var primaryPatient = await _context.Patients
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.UserId == userId);

            if (primaryPatient == null)
            {
                return (null, null, new List<Patient>(), false);
            }

            var dependents = await _context.Patients
                .Where(p => p.GuardianPatientId == primaryPatient.Id)
                .OrderBy(p => p.FullName)
                .ToListAsync();

            var allowedIds = new HashSet<int>(dependents.Select(d => d.Id)) { primaryPatient.Id };

            Patient? activePatient = null;

            if (requestedPatientId.HasValue)
            {
                if (!allowedIds.Contains(requestedPatientId.Value))
                {
                    // IDOR violation: user requested access to an unlinked patient!
                    return (null, primaryPatient, dependents, true);
                }

                activePatient = requestedPatientId.Value == primaryPatient.Id
                    ? primaryPatient
                    : dependents.FirstOrDefault(d => d.Id == requestedPatientId.Value);

                SetProfileCookie(activePatient!.Id);
            }
            else if (Request.Cookies.TryGetValue("Portal_ActivePatientId", out var cookieVal) &&
                     int.TryParse(cookieVal, out var cookiePatientId) &&
                     allowedIds.Contains(cookiePatientId))
            {
                activePatient = cookiePatientId == primaryPatient.Id
                    ? primaryPatient
                    : dependents.FirstOrDefault(d => d.Id == cookiePatientId);
            }
            else
            {
                activePatient = primaryPatient;
            }

            ViewBag.Patient = activePatient;
            ViewBag.PrimaryPatient = primaryPatient;
            ViewBag.Dependents = dependents;
            ViewBag.IsViewingDependent = (activePatient?.Id != primaryPatient.Id);

            return (activePatient, primaryPatient, dependents, false);
        }

        private void SetProfileCookie(int patientId)
        {
            try
            {
                Response.Cookies.Append("Portal_ActivePatientId", patientId.ToString(), new Microsoft.AspNetCore.Http.CookieOptions
                {
                    HttpOnly = true,
                    SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
                    Secure = Request.IsHttps,
                    Expires = DateTimeOffset.UtcNow.AddDays(30)
                });
            }
            catch
            {
                // Fallback for mock contexts that do not implement response cookies
            }
        }

        // POST: Portal/SwitchProfile
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SwitchProfile(int patientId, string? returnUrl = null)
        {
            var (active, primary, dependents, isForbidden) = await ResolvePatientContextAsync(patientId);
            if (isForbidden || active == null)
            {
                return Forbid();
            }

            SetProfileCookie(active.Id);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Index(int? patientId = null)
        {
            var (patient, primary, dependents, isForbidden) = await ResolvePatientContextAsync(patientId);
            if (isForbidden) return Forbid();
            if (patient == null) return Forbid();

            var now = DateTime.UtcNow;
            var appointments = await _context.Appointments
                .Include(a => a.Doctor)
                .Where(a => a.PatientId == patient.Id)
                .OrderBy(a => a.AppointmentDatetime)
                .ToListAsync();

            ViewBag.Upcoming = appointments
                .Where(a => a.AppointmentDatetime >= now && a.Status != AppointmentStatus.Cancelled)
                .OrderBy(a => a.AppointmentDatetime)
                .ToList();
            ViewBag.Past = appointments
                .Where(a => a.AppointmentDatetime < now || a.Status == AppointmentStatus.Cancelled)
                .OrderByDescending(a => a.AppointmentDatetime)
                .ToList();

            return View();
        }

        public async Task<IActionResult> Records(int? patientId = null)
        {
            var (patient, _, _, isForbidden) = await ResolvePatientContextAsync(patientId);
            if (isForbidden) return Forbid();
            if (patient == null) return Forbid();

            var records = await _context.MedicalRecords
                .Include(r => r.Doctor)
                .Where(r => r.PatientId == patient.Id)
                .OrderByDescending(r => r.RecordedAt)
                .ToListAsync();

            return View(records);
        }

        public async Task<IActionResult> Prescriptions(int? patientId = null)
        {
            var (patient, _, _, isForbidden) = await ResolvePatientContextAsync(patientId);
            if (isForbidden) return Forbid();
            if (patient == null) return Forbid();

            var prescriptions = await _context.Prescriptions
                .Include(p => p.Doctor)
                .Include(p => p.PrescriptionItems)
                    .ThenInclude(i => i.Medicine)
                .Where(p => p.PatientId == patient.Id)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            return View(prescriptions);
        }

        public async Task<IActionResult> Bills(int? patientId = null)
        {
            var (patient, _, _, isForbidden) = await ResolvePatientContextAsync(patientId);
            if (isForbidden) return Forbid();
            if (patient == null) return Forbid();

            var bills = await _context.Bills
                .Include(b => b.BillItems)
                .Where(b => b.PatientId == patient.Id)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();

            return View(bills);
        }

        // GET: Portal/Doctors
        public async Task<IActionResult> Doctors(string? search, string? category, int? patientId = null)
        {
            var (patient, _, _, isForbidden) = await ResolvePatientContextAsync(patientId);
            if (isForbidden) return Forbid();
            if (patient == null) return Forbid();

            var doctors = await _bookingService.GetDoctorDirectoryAsync(search, category);

            var categories = await _context.Users
                .AsNoTracking()
                .Include(u => u.Role)
                .Where(u => u.Role.RoleName == "Doctor" && !string.IsNullOrEmpty(u.Category))
                .Select(u => u.Category)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();

            ViewBag.Categories = categories;
            ViewBag.CurrentSearch = search;
            ViewBag.CurrentCategory = category;

            return View(doctors);
        }

        // GET: Portal/GetAvailableSlots?doctorId=1&date=2026-10-03&excludeAppointmentId=5
        [HttpGet]
        public async Task<IActionResult> GetAvailableSlots(int doctorId, string date, int? excludeAppointmentId = null)
        {
            if (doctorId <= 0 || string.IsNullOrWhiteSpace(date) ||
                !DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                return BadRequest(new { error = "Invalid doctor ID or date format (expected yyyy-MM-dd)." });
            }

            var slotsDto = await _bookingService.GetDailySlotsAsync(doctorId, parsedDate, excludeAppointmentId);
            return Json(slotsDto);
        }

        // GET: Portal/Book
        public async Task<IActionResult> Book(int? doctorId, string? date, int? patientId = null)
        {
            var (patient, _, _, isForbidden) = await ResolvePatientContextAsync(patientId);
            if (isForbidden) return Forbid();
            if (patient == null) return Forbid();

            var doctors = await _context.Users
                .AsNoTracking()
                .Include(u => u.Role)
                .Where(u => u.Role.RoleName == "Doctor")
                .OrderBy(u => u.FullName)
                .ToListAsync();

            var doctorItems = doctors.Select(d => new SelectListItem
            {
                Value = d.Id.ToString(),
                Text = $"Dr. {d.FullName} ({(string.IsNullOrEmpty(d.Category) ? "General Physician" : d.Category)})"
            }).ToList();

            var selectedDocId = doctorId.HasValue && doctors.Any(d => d.Id == doctorId.Value)
                ? doctorId.Value
                : (doctors.FirstOrDefault()?.Id ?? 0);

            var selectedDoc = doctors.FirstOrDefault(d => d.Id == selectedDocId);

            var targetDate = string.IsNullOrWhiteSpace(date)
                ? HospitalClock.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : date;

            var model = new BookAppointmentViewModel
            {
                DoctorId = selectedDocId,
                DoctorName = selectedDoc?.FullName,
                DoctorCategory = selectedDoc?.Category,
                SelectedDate = targetDate,
                AvailableDoctors = doctorItems,
                PatientId = patient.Id
            };

            return View(model);
        }

        // POST: Portal/Book
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Book(BookAppointmentViewModel model)
        {
            var (patient, primaryPatient, dependents, isForbidden) = await ResolvePatientContextAsync(model.PatientId);
            if (isForbidden || patient == null) return Forbid();

            if (!DateTime.TryParse(model.SelectedSlotTime, null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var slotStartUtc))
            {
                ModelState.AddModelError("SelectedSlotTime", "Please select a valid consultation time slot.");
            }
            else
            {
                var nowUtc = DateTime.UtcNow;
                if (slotStartUtc <= nowUtc.AddMinutes(5))
                {
                    ModelState.AddModelError("SelectedSlotTime", "Cannot book an appointment in the past or immediately starting.");
                }
                else
                {
                    var slotEndUtc = slotStartUtc.AddMinutes(DoctorScheduleService.SlotDurationMinutes);

                    var doctorConflict = await _bookingService.CheckDoctorCollisionAsync(model.DoctorId, slotStartUtc, slotEndUtc);
                    if (doctorConflict)
                    {
                        ModelState.AddModelError("SelectedSlotTime", "This time slot was just booked by another patient. Please choose another slot.");
                    }

                    var patientConflict = await _bookingService.CheckPatientCollisionAsync(patient.Id, slotStartUtc, slotEndUtc);
                    if (patientConflict)
                    {
                        var errorMsg = patient.Id != primaryPatient?.Id
                            ? $"{patient.FullName} already has another active appointment scheduled during this time interval."
                            : "You already have another active appointment scheduled during this time interval.";
                        ModelState.AddModelError("SelectedSlotTime", errorMsg);
                    }

                    if (ModelState.IsValid)
                    {
                        var appointment = new Appointment
                        {
                            PatientId = patient.Id,
                            DoctorId = model.DoctorId,
                            AppointmentDatetime = slotStartUtc,
                            EndTime = slotEndUtc,
                            ReasonForVisit = model.ReasonForVisit.Trim(),
                            Status = AppointmentStatus.Scheduled,
                            CreatedAt = nowUtc,
                            UpdatedAt = nowUtc
                        };

                        _context.Appointments.Add(appointment);
                        await _context.SaveChangesAsync();

                        var doctor = await _context.Users.FindAsync(model.DoctorId);
                        var localTime = slotStartUtc.ToHospitalTime();
                        var targetName = patient.Id != primaryPatient?.Id ? $" for {patient.FullName}" : "";
                        TempData["SuccessMessage"] = $"Appointment booked successfully{targetName} with Dr. {doctor?.FullName} on {localTime:dddd, MMM dd} at {localTime:hh:mm tt}!";
                        return RedirectToAction(nameof(Index));
                    }
                }
            }

            var doctors = await _context.Users
                .AsNoTracking()
                .Include(u => u.Role)
                .Where(u => u.Role.RoleName == "Doctor")
                .OrderBy(u => u.FullName)
                .ToListAsync();

            model.AvailableDoctors = doctors.Select(d => new SelectListItem
            {
                Value = d.Id.ToString(),
                Text = $"Dr. {d.FullName} ({(string.IsNullOrEmpty(d.Category) ? "General Physician" : d.Category)})"
            }).ToList();

            var selectedDoctor = doctors.FirstOrDefault(d => d.Id == model.DoctorId);
            model.DoctorName = selectedDoctor?.FullName;
            model.DoctorCategory = selectedDoctor?.Category;
            model.PatientId = patient.Id;

            return View(model);
        }

        // GET: Portal/Reschedule/5
        public async Task<IActionResult> Reschedule(int? id)
        {
            if (id == null) return NotFound();

            var (activePatient, primaryPatient, dependents, isForbidden) = await ResolvePatientContextAsync();
            if (isForbidden || primaryPatient == null) return Forbid();

            var allowedPatientIds = dependents.Select(d => d.Id).Append(primaryPatient.Id).ToList();

            var appt = await _context.Appointments
                .Include(a => a.Doctor)
                .FirstOrDefaultAsync(a => a.Id == id && allowedPatientIds.Contains(a.PatientId));

            if (appt == null) return NotFound();

            if (appt.Status != AppointmentStatus.Scheduled || appt.AppointmentDatetime <= DateTime.UtcNow)
            {
                TempData["ErrorMessage"] = "Only upcoming scheduled appointments can be rescheduled.";
                return RedirectToAction(nameof(Index));
            }

            var localTime = appt.AppointmentDatetime.ToHospitalTime();
            var model = new RescheduleAppointmentViewModel
            {
                AppointmentId = appt.Id,
                DoctorId = appt.DoctorId,
                DoctorName = appt.Doctor?.FullName,
                DoctorCategory = appt.Doctor?.Category,
                CurrentDatetimeUtc = appt.AppointmentDatetime,
                CurrentDatetimeDisplay = localTime.ToString("dddd, MMM dd, yyyy - hh:mm tt"),
                SelectedDate = localTime.ToString("yyyy-MM-dd"),
                ReasonForVisit = appt.ReasonForVisit,
                Version = appt.Version,
                PatientId = appt.PatientId
            };

            return View(model);
        }

        // POST: Portal/Reschedule/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reschedule(RescheduleAppointmentViewModel model)
        {
            var (activePatient, primaryPatient, dependents, isForbidden) = await ResolvePatientContextAsync();
            if (isForbidden || primaryPatient == null) return Forbid();

            var allowedPatientIds = dependents.Select(d => d.Id).Append(primaryPatient.Id).ToList();

            var tracked = await _context.Appointments
                .Include(a => a.Doctor)
                .FirstOrDefaultAsync(a => a.Id == model.AppointmentId && allowedPatientIds.Contains(a.PatientId));

            if (tracked == null) return NotFound();

            if (tracked.Status != AppointmentStatus.Scheduled || tracked.AppointmentDatetime <= DateTime.UtcNow)
            {
                ModelState.AddModelError("", "Only upcoming scheduled appointments can be rescheduled.");
            }

            if (!DateTime.TryParse(model.SelectedSlotTime, null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var slotStartUtc))
            {
                ModelState.AddModelError("SelectedSlotTime", "Please select a valid new consultation time slot.");
            }
            else
            {
                var nowUtc = DateTime.UtcNow;
                if (slotStartUtc <= nowUtc.AddMinutes(5))
                {
                    ModelState.AddModelError("SelectedSlotTime", "Cannot reschedule an appointment to the past.");
                }
                else
                {
                    var slotEndUtc = slotStartUtc.AddMinutes(DoctorScheduleService.SlotDurationMinutes);

                    var doctorConflict = await _bookingService.CheckDoctorCollisionAsync(tracked.DoctorId, slotStartUtc, slotEndUtc, tracked.Id);
                    if (doctorConflict)
                    {
                        ModelState.AddModelError("SelectedSlotTime", "This time slot is no longer available. Please select another slot.");
                    }

                    var patientConflict = await _bookingService.CheckPatientCollisionAsync(tracked.PatientId, slotStartUtc, slotEndUtc, tracked.Id);
                    if (patientConflict)
                    {
                        ModelState.AddModelError("SelectedSlotTime", "Another active appointment is already scheduled during this time interval.");
                    }

                    if (model.Version > 0 && _context.Database.IsRelational())
                    {
                        try
                        {
                            _context.Entry(tracked).Property(a => a.Version).OriginalValue = model.Version;
                        }
                        catch
                        {
                            // In-memory provider fallback
                        }
                    }

                    if (ModelState.IsValid)
                    {
                        try
                        {
                            tracked.AppointmentDatetime = slotStartUtc;
                            tracked.EndTime = slotEndUtc;
                            tracked.UpdatedAt = nowUtc;
                            if (!string.IsNullOrWhiteSpace(model.ReasonForVisit))
                            {
                                tracked.ReasonForVisit = model.ReasonForVisit.Trim();
                            }

                            await _context.SaveChangesAsync();
                            var newLocalTime = slotStartUtc.ToHospitalTime();
                            TempData["SuccessMessage"] = $"Appointment successfully rescheduled to {newLocalTime:dddd, MMM dd} at {newLocalTime:hh:mm tt}.";
                            return RedirectToAction(nameof(Index));
                        }
                        catch (DbUpdateConcurrencyException)
                        {
                            ModelState.AddModelError("", "This appointment was modified by hospital staff while you were editing it. Please refresh and try again.");
                        }
                    }
                }
            }

            var currentLocal = tracked.AppointmentDatetime.ToHospitalTime();
            model.DoctorName = tracked.Doctor?.FullName;
            model.DoctorCategory = tracked.Doctor?.Category;
            model.CurrentDatetimeDisplay = currentLocal.ToString("dddd, MMM dd, yyyy - hh:mm tt");
            model.Version = tracked.Version;
            model.PatientId = tracked.PatientId;

            return View(model);
        }

        // POST: Portal/Cancel/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id)
        {
            var (activePatient, primaryPatient, dependents, isForbidden) = await ResolvePatientContextAsync();
            if (isForbidden || primaryPatient == null) return Forbid();

            var allowedPatientIds = dependents.Select(d => d.Id).Append(primaryPatient.Id).ToList();

            var appt = await _context.Appointments
                .Include(a => a.Doctor)
                .FirstOrDefaultAsync(a => a.Id == id && allowedPatientIds.Contains(a.PatientId));

            if (appt == null) return NotFound();

            if (appt.Status != AppointmentStatus.Scheduled)
            {
                TempData["ErrorMessage"] = $"Cannot cancel an appointment with status '{appt.Status}'. Only scheduled visits can be cancelled.";
                return RedirectToAction(nameof(Index));
            }

            if (appt.AppointmentDatetime <= DateTime.UtcNow)
            {
                TempData["ErrorMessage"] = "Cannot cancel an appointment whose scheduled time has already passed.";
                return RedirectToAction(nameof(Index));
            }

            appt.Status = AppointmentStatus.Cancelled;
            appt.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            var doctorName = appt.Doctor != null ? $"Dr. {appt.Doctor.FullName}" : "doctor";
            TempData["SuccessMessage"] = $"The appointment with {doctorName} was successfully cancelled. The time slot has been released.";
            return RedirectToAction(nameof(Index));
        }
    }
}
