using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Models.ViewModels;
using HospitalManagementSystem.Services;

namespace HospitalManagementSystem.Controllers
{
    [Authorize(Roles = "Doctor,Admin,Receptionist")]
    public class OperationsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IAppointmentBookingService _appointmentBookingService;

        public OperationsController(
            ApplicationDbContext context,
            IAppointmentBookingService appointmentBookingService)
        {
            _context = context;
            _appointmentBookingService = appointmentBookingService;
        }

        // GET: Operations
        public async Task<IActionResult> Index()
        {
            var operations = await _context.Operations
                .Include(o => o.Patient)
                .Include(o => o.Surgeon)
                .OrderByDescending(o => o.OperationDatetime)
                .ToListAsync();

            return View(operations);
        }

        // GET: Operations/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var operation = await _context.Operations
                .Include(o => o.Patient)
                .Include(o => o.Surgeon)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (operation == null)
            {
                return NotFound();
            }

            var admission = await _context.Admissions
                .Include(a => a.BedTransfers)
                    .ThenInclude(bt => bt.Bed)
                .Where(a => a.PatientId == operation.PatientId)
                .OrderByDescending(a => a.AdmissionDate)
                .FirstOrDefaultAsync();

            ViewBag.LinkedAdmission = admission;

            return View(operation);
        }

        // GET: Operations/Create
        public async Task<IActionResult> Create()
        {
            await PopulateDropDownsAsync();
            return View(new OperationCreateViewModel());
        }

        // POST: Operations/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(OperationCreateViewModel model)
        {
            if (!ModelState.IsValid)
            {
                await PopulateDropDownsAsync();
                return View(model);
            }

            var opStart = DateTime.SpecifyKind(model.OperationDatetime, DateTimeKind.Utc);
            var opEnd = opStart.AddHours(2);

            // Constraint 1: Validate Surgeon Availability (no overlapping clinic slots or concurrent surgeries)
            var surgeon = await _context.Users.FindAsync(model.SurgeonId);
            if (surgeon == null)
            {
                ModelState.AddModelError("SurgeonId", "Selected surgeon does not exist.");
            }
            else
            {
                var surgeonConflict = await _appointmentBookingService.CheckDoctorCollisionAsync(model.SurgeonId, opStart, opEnd);
                if (surgeonConflict)
                {
                    ModelState.AddModelError("SurgeonId", $"Surgeon Dr. {surgeon.FullName} has a conflicting outpatient appointment or active surgical booking during {opStart:yyyy-MM-dd HH:mm} - {opEnd:HH:mm} UTC.");
                }
            }

            // Constraint 2: Validate Operating Theatre Room Availability
            var otCollision = await _context.Operations
                .AnyAsync(o => o.OtRoom == model.OtRoom
                            && o.Status != OperationStatus.Completed
                            && o.OperationDatetime < opEnd
                            && o.OperationDatetime.AddHours(2) > opStart);
            if (otCollision)
            {
                ModelState.AddModelError("OtRoom", $"Operating Theatre '{model.OtRoom}' is already booked for another surgical operation during {opStart:yyyy-MM-dd HH:mm} - {opEnd:HH:mm} UTC.");
            }

            // Constraint 3: Validate Recovery Bed Allocation (must be ICU or Cabin, and currently unoccupied)
            var bed = await _context.Beds
                .Include(b => b.BedTransfers)
                .FirstOrDefaultAsync(b => b.Id == model.RecoveryBedId);

            if (bed == null || (bed.Category != BedCategory.ICU && bed.Category != BedCategory.Cabin))
            {
                ModelState.AddModelError("RecoveryBedId", "Recovery bed must be a valid ICU or Cabin bed.");
            }
            else
            {
                bool isBedOccupied = await _context.BedTransfers.AnyAsync(bt => bt.BedId == model.RecoveryBedId && bt.EndDate == null);
                if (isBedOccupied)
                {
                    ModelState.AddModelError("RecoveryBedId", $"Selected recovery bed {bed.BedNumber} ({bed.Category}) is currently occupied. Please choose an available bed.");
                }
            }

            var patient = await _context.Patients.FindAsync(model.PatientId);
            if (patient == null)
            {
                ModelState.AddModelError("PatientId", "Selected patient does not exist.");
            }

            if (!ModelState.IsValid)
            {
                await PopulateDropDownsAsync();
                return View(model);
            }

            // Multi-resource reservation execution
            var isRelational = _context.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory";
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
            if (isRelational)
            {
                transaction = await _context.Database.BeginTransactionAsync();
            }

            try
            {
                // Ensure inpatient admission exists for post-op recovery
                var activeAdmission = await _context.Admissions
                    .FirstOrDefaultAsync(a => a.PatientId == model.PatientId && a.DischargeDate == null);

                if (activeAdmission == null)
                {
                    activeAdmission = new Admission
                    {
                        PatientId = model.PatientId,
                        AdmittingDoctorId = model.SurgeonId,
                        AdmissionDate = opStart,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _context.Admissions.Add(activeAdmission);
                    await _context.SaveChangesAsync();
                }

                // Automatically earmark recovery bed via BedTransfer
                var bedTransfer = new BedTransfer
                {
                    AdmissionId = activeAdmission.Id,
                    BedId = model.RecoveryBedId,
                    StartDate = opStart
                };
                _context.BedTransfers.Add(bedTransfer);

                // Build preparation checklist with recovery bed metadata
                var checklist = model.PreparationChecklist?.Trim() ?? string.Empty;
                var bedStamp = $"[Recovery Bed: {bed!.BedNumber} ({bed.Category})]";
                if (!checklist.Contains(bedStamp))
                {
                    checklist = string.IsNullOrWhiteSpace(checklist) ? bedStamp : $"{checklist} | {bedStamp}";
                }

                var operation = new Operation
                {
                    PatientId = model.PatientId,
                    SurgeonId = model.SurgeonId,
                    OtRoom = model.OtRoom.Trim(),
                    OperationDatetime = opStart,
                    PreparationChecklist = checklist,
                    Status = OperationStatus.Scheduled,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.Operations.Add(operation);
                await _context.SaveChangesAsync();

                if (transaction != null)
                {
                    await transaction.CommitAsync();
                }

                TempData["SuccessMessage"] = $"Operation scheduled successfully in {operation.OtRoom} for {patient!.FullName}. Recovery bed {bed.BedNumber} ({bed.Category}) earmarked.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                if (transaction != null)
                {
                    await transaction.RollbackAsync();
                }
                ModelState.AddModelError("", $"Failed to schedule operation: {ex.Message}");
                await PopulateDropDownsAsync();
                return View(model);
            }
            finally
            {
                if (transaction != null)
                {
                    await transaction.DisposeAsync();
                }
            }
        }

        // POST: Operations/UpdateStatus/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int id, OperationStatus status)
        {
            var operation = await _context.Operations.FindAsync(id);
            if (operation == null)
            {
                return NotFound();
            }

            operation.Status = status;
            operation.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Operation status updated to {status}.";
            return RedirectToAction(nameof(Details), new { id });
        }

        private async Task PopulateDropDownsAsync()
        {
            var patients = await _context.Patients
                .OrderBy(p => p.FullName)
                .Select(p => new { p.Id, Name = $"{p.FullName} ({p.Uhid})" })
                .ToListAsync();

            var surgeons = await _context.Users
                .Include(u => u.Role)
                .Where(u => u.Role.RoleName == "Doctor" || u.Role.RoleName == "Admin")
                .OrderBy(u => u.FullName)
                .Select(u => new { u.Id, Name = $"Dr. {u.FullName} ({u.Category ?? u.Role.RoleName})" })
                .ToListAsync();

            var availableBeds = await _context.Beds
                .Include(b => b.BedTransfers)
                .Where(b => (b.Category == BedCategory.ICU || b.Category == BedCategory.Cabin)
                         && !b.BedTransfers.Any(bt => bt.EndDate == null))
                .OrderBy(b => b.BedNumber)
                .Select(b => new { b.Id, Name = $"{b.BedNumber} ({b.Category}) - ৳{b.DailyRate:N0}/day" })
                .ToListAsync();

            ViewBag.Patients = new SelectList(patients, "Id", "Name");
            ViewBag.Surgeons = new SelectList(surgeons, "Id", "Name");
            ViewBag.RecoveryBeds = new SelectList(availableBeds, "Id", "Name");
            ViewBag.OtRooms = new List<string>
            {
                "OT-1 (General Surgery)",
                "OT-2 (Orthopedic & Trauma)",
                "OT-3 (Laparoscopic / Day Care)",
                "Cardiac OT",
                "Neuro OT"
            };
        }
    }
}
