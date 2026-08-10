using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using HospitalManagementSystem.Models;

namespace HospitalManagementSystem.Controllers
{
    public class JsonMedicalRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public int PatientId { get; set; }
        public string PatientName { get; set; }
        public string PatientUhid { get; set; }
        public int DoctorId { get; set; }
        public string DoctorName { get; set; }
        public string Diagnosis { get; set; }
        public string Treatment { get; set; }
        public DateTime Date { get; set; } = DateTime.UtcNow;
    }

    public class MedicalRecordsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public MedicalRecordsController(ApplicationDbContext context)
        {
            _context = context;
        }

        private async Task<List<JsonMedicalRecord>> GetAllRecordsAsync()
        {
            var patients = await _context.Patients.ToListAsync();
            var allRecords = new List<JsonMedicalRecord>();

            foreach (var patient in patients)
            {
                if (!string.IsNullOrWhiteSpace(patient.MedicalHistoryJson))
                {
                    try
                    {
                        var records = JsonSerializer.Deserialize<List<JsonMedicalRecord>>(patient.MedicalHistoryJson);
                        if (records != null)
                        {
                            allRecords.AddRange(records);
                        }
                    }
                    catch
                    {
                        // Ignore parsing errors for individual patients to prevent complete failure
                    }
                }
            }

            return allRecords.OrderByDescending(r => r.Date).ToList();
        }

        private List<JsonMedicalRecord> GetPatientRecords(Patient patient)
        {
            if (string.IsNullOrWhiteSpace(patient.MedicalHistoryJson))
                return new List<JsonMedicalRecord>();
            
            try
            {
                return JsonSerializer.Deserialize<List<JsonMedicalRecord>>(patient.MedicalHistoryJson) ?? new List<JsonMedicalRecord>();
            }
            catch
            {
                return new List<JsonMedicalRecord>();
            }
        }

        private void SavePatientRecords(Patient patient, List<JsonMedicalRecord> records)
        {
            patient.MedicalHistoryJson = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = false });
        }

        // GET: MedicalRecords
        public async Task<IActionResult> Index()
        {
            var records = await GetAllRecordsAsync();
            return View(records);
        }

        // GET: MedicalRecords/Create
        public IActionResult Create(int? patientId, int? doctorId)
        {
            var doctors = _context.Users
                .Include(u => u.Role)
                .Where(u => u.Role.RoleName == "Doctor")
                .Select(u => new { u.Id, u.FullName })
                .ToList();
            ViewData["DoctorId"] = new SelectList(doctors, "Id", "FullName", doctorId);

            if (doctorId.HasValue)
            {
                var doctor = _context.Users.Find(doctorId.Value);
                if (doctor != null)
                {
                    ViewBag.PreselectedDoctorId = doctor.Id;
                    ViewBag.PreselectedDoctorName = doctor.FullName;
                }
            }

            if (patientId.HasValue)
            {
                var patient = _context.Patients.Find(patientId.Value);
                if (patient != null)
                {
                    ViewBag.PreselectedPatientId = patient.Id;
                    ViewBag.PreselectedPatientUhid = patient.Uhid;
                    ViewBag.PreselectedPatientName = patient.FullName;
                }
            }

            return View();
        }

        // POST: MedicalRecords/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(JsonMedicalRecord record)
        {
            var patient = await _context.Patients.FindAsync(record.PatientId);
            var doctor = await _context.Users.FindAsync(record.DoctorId);

            if (patient == null) ModelState.AddModelError("PatientId", "Patient is required.");
            if (doctor == null) ModelState.AddModelError("DoctorId", "Doctor is required.");
            if (string.IsNullOrWhiteSpace(record.Diagnosis)) ModelState.AddModelError("Diagnosis", "Diagnosis is required.");
            if (string.IsNullOrWhiteSpace(record.Treatment)) ModelState.AddModelError("Treatment", "Treatment is required.");

            if (ModelState.IsValid)
            {
                record.PatientName = patient.FullName;
                record.PatientUhid = patient.Uhid;
                record.DoctorName = doctor.FullName;
                record.Date = DateTime.UtcNow;

                var records = GetPatientRecords(patient);
                records.Add(record);
                SavePatientRecords(patient, records);
                
                _context.Update(patient);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Medical record saved successfully.";
                return RedirectToAction(nameof(Index));
            }
            
            var doctors = _context.Users.Include(u => u.Role).Where(u => u.Role.RoleName == "Doctor").Select(u => new { u.Id, u.FullName }).ToList();
            ViewData["DoctorId"] = new SelectList(doctors, "Id", "FullName", record.DoctorId);
            return View(record);
        }

        // GET: MedicalRecords/Details/5
        public async Task<IActionResult> Details(string id)
        {
            var records = await GetAllRecordsAsync();
            var record = records.FirstOrDefault(r => r.Id == id);
            if (record == null) return NotFound();
            return View(record);
        }

        // GET: MedicalRecords/Delete/5
        public async Task<IActionResult> Delete(string id)
        {
            var records = await GetAllRecordsAsync();
            var record = records.FirstOrDefault(r => r.Id == id);
            if (record == null) return NotFound();
            return View(record);
        }

        // POST: MedicalRecords/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string id)
        {
            var records = await GetAllRecordsAsync();
            var record = records.FirstOrDefault(r => r.Id == id);
            if (record != null)
            {
                var patient = await _context.Patients.FindAsync(record.PatientId);
                if (patient != null)
                {
                    var patientRecords = GetPatientRecords(patient);
                    var recordToRemove = patientRecords.FirstOrDefault(r => r.Id == id);
                    if (recordToRemove != null)
                    {
                        patientRecords.Remove(recordToRemove);
                        SavePatientRecords(patient, patientRecords);
                        _context.Update(patient);
                        await _context.SaveChangesAsync();
                        TempData["SuccessMessage"] = "Medical record deleted successfully.";
                    }
                }
            }
            return RedirectToAction(nameof(Index));
        }
    }
}
