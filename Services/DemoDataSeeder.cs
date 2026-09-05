using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Services.Contracts;

namespace HospitalManagementSystem.Services
{
    // Rebuilds a believable, internally-consistent hospital snapshot for demos: patients,
    // beds, admissions, prescriptions, bills, and today's appointment queue, spread across
    // all five roles. Invoked only via `dotnet run -- --seed-demo` (see Program.cs) - never
    // from an HTTP endpoint, so it can't be triggered by a stray click during a live demo.
    //
    // Deliberately destructive on domain data (patients, appointments, admissions, beds,
    // bills, prescriptions, medical records, allergies, AI suggestions) every time it runs -
    // that's the point, so the app can be reset to a known-good state right before
    // presenting. It never touches Users rows it didn't create itself, Roles, Medicines, or
    // AiProviderSettings: those hold real login accounts, the seeded formulary, and
    // encrypted API keys that must survive a reseed.
    public class DemoDataSeeder
    {
        private const string StaffPassword = "Passw0rd!";
        private const string PatientPortalPassword = "Patient@123";

        private readonly ApplicationDbContext _context;

        public DemoDataSeeder(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task SeedAsync()
        {
            var roleIds = await _context.Roles.ToDictionaryAsync(r => r.RoleName, r => r.Id);

            await using var transaction = await _context.Database.BeginTransactionAsync();

            Console.WriteLine("Clearing existing demo-owned domain data...");
            await ClearDomainDataAsync(roleIds["Patient"]);

            Console.WriteLine("Ensuring staff accounts...");
            var admin = await GetUserAsync("admin");
            var drOne = await GetUserAsync("drmock");
            var drTwo = await EnsureUserAsync("dr2", roleIds["Doctor"], "Dr. Farhana Chowdhury", "Consultant", null);
            // Unlike admin/drmock/pharmacistmock (guaranteed by migration HasData - see
            // ApplicationDbContext.OnModelCreating), "mock-assistant" was never seeded
            // anywhere in source control; it only exists in a long-running dev database
            // because it was created once through the Staff UI. EnsureUserAsync (not
            // GetUserAsync) so a genuinely fresh clone can seed successfully too.
            var assistantOne = await EnsureUserAsync("mock-assistant", roleIds["Assistant"], "Mock Assistant", "Staff", drOne.Id);
            var assistantTwo = await EnsureUserAsync("assistant2", roleIds["Assistant"], "Tanvir Islam", "Staff", drTwo.Id);
            var pharmacistOne = await GetUserAsync("pharmacistmock");
            _ = await EnsureUserAsync("pharmacist2", roleIds["Pharmacist"], "Nusrat Jahan", "Pharmacy", null);
            _ = await EnsureUserAsync("reception1", roleIds["Receptionist"], "Ayesha Rahman", "Front Desk", null);
            _ = await EnsureUserAsync("reception2", roleIds["Receptionist"], "Kamal Hossain", "Front Desk", null);
            await _context.SaveChangesAsync();

            Console.WriteLine("Seeding beds...");
            var beds = await SeedBedsAsync();

            Console.WriteLine("Seeding patients...");
            var medicines = await _context.Medicines.ToDictionaryAsync(m => m.Name, m => m);
            var patients = await SeedPatientsAsync(roleIds["Patient"]);

            Console.WriteLine("Seeding medical records...");
            var records = await SeedMedicalRecordsAsync(patients, drOne.Id, drTwo.Id);

            Console.WriteLine("Seeding prescriptions...");
            await SeedPrescriptionsAsync(patients, medicines, drOne.Id, drTwo.Id);

            Console.WriteLine("Seeding allergies...");
            await SeedAllergiesAsync(patients, admin.Id);

            Console.WriteLine("Seeding admissions and bed assignments...");
            var admissions = await SeedAdmissionsAsync(patients, beds, drOne.Id, drTwo.Id);

            Console.WriteLine("Seeding bills...");
            await SeedBillsAsync(admissions, admin.Id);

            Console.WriteLine("Seeding today's appointment queue...");
            var todaysAppointments = await SeedAppointmentsAsync(patients, drOne.Id, drTwo.Id);

            Console.WriteLine("Seeding appointment history (last 14 days, for reporting)...");
            await SeedHistoricalAppointmentsAsync(patients, drOne.Id, drTwo.Id);

            Console.WriteLine("Seeding vitals and triage scores...");
            await SeedVitalsAsync(patients, todaysAppointments, assistantOne.Id, assistantTwo.Id);

            Console.WriteLine("Seeding a canned AI suggestion (offline fallback for the demo)...");
            await SeedCannedAiSuggestionAsync(patients, records);

            Console.WriteLine("Seeding additional AI suggestions (for reporting)...");
            await SeedAdditionalAiSuggestionsAsync(patients, records, drOne.Id, drTwo.Id);

            await transaction.CommitAsync();

            PrintSummary();
        }

        private async Task<User> GetUserAsync(string username)
        {
            return await _context.Users.FirstAsync(u => u.Username == username);
        }

        private async Task<User> EnsureUserAsync(string username, int roleId, string fullName, string category, int? assignedDoctorId)
        {
            var existing = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (existing != null)
            {
                return existing;
            }

            var now = DateTime.UtcNow;
            var user = new User
            {
                RoleId = roleId,
                Username = username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(StaffPassword),
                FullName = fullName,
                Category = category,
                AssignedDoctorId = assignedDoctorId,
                CreatedAt = now,
                UpdatedAt = now
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            return user;
        }

        // Deletes leaf-to-root, explicitly, rather than relying on TRUNCATE ... CASCADE:
        // Users has a FK from AiProviderSettings (UpdatedById), and Postgres's CASCADE
        // truncates every table with a FK to the truncated one regardless of that FK's own
        // ON DELETE behavior - a blanket TRUNCATE CASCADE on Users would silently wipe the
        // encrypted AI provider keys along with it. Explicit per-table deletes in
        // dependency order avoid that entirely; Users, Roles, Medicines and
        // AiProviderSettings are never touched here except for the Patient-role Users
        // this seeder itself owns.
        private async Task ClearDomainDataAsync(int patientRoleId)
        {
            var db = _context.Database;
            await db.ExecuteSqlRawAsync("DELETE FROM \"AiSuggestions\"");
            await db.ExecuteSqlRawAsync("DELETE FROM \"PatientAllergies\"");
            await db.ExecuteSqlRawAsync("DELETE FROM \"PatientVitals\"");
            await db.ExecuteSqlRawAsync("DELETE FROM \"PrescriptionItems\"");
            await db.ExecuteSqlRawAsync("DELETE FROM \"Prescriptions\"");
            await db.ExecuteSqlRawAsync("DELETE FROM \"BillItems\"");
            await db.ExecuteSqlRawAsync("DELETE FROM \"Bills\"");
            await db.ExecuteSqlRawAsync("DELETE FROM \"BedTransfers\"");
            await db.ExecuteSqlRawAsync("DELETE FROM \"Admissions\"");
            await db.ExecuteSqlRawAsync("DELETE FROM \"Appointments\"");
            await db.ExecuteSqlRawAsync("DELETE FROM \"MedicalRecords\"");
            await db.ExecuteSqlRawAsync("DELETE FROM \"Patients\"");
            // Patient-role logins only ever come from this seeder or the receptionist's
            // "issue portal login" action - safe to wipe and recreate every run so UHID
            // reuse (same month => same generated UHID) never collides with a leftover
            // orphaned account from a previous seed run.
            await db.ExecuteSqlRawAsync("DELETE FROM \"Users\" WHERE \"RoleId\" = {0}", patientRoleId);
            await db.ExecuteSqlRawAsync("DELETE FROM \"Beds\"");
        }

        private async Task<Dictionary<string, Bed>> SeedBedsAsync()
        {
            var now = DateTime.UtcNow;
            var beds = new List<Bed>();

            void AddRange(string prefix, int start, int count, BedCategory category, decimal rate)
            {
                for (var i = start; i < start + count; i++)
                {
                    beds.Add(new Bed
                    {
                        BedNumber = $"{prefix}-{i:00}",
                        Category = category,
                        DailyRate = rate,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }
            }

            AddRange("ICU", 1, 4, BedCategory.ICU, 5000m);
            AddRange("WD", 101, 10, BedCategory.GeneralWard, 1000m);
            AddRange("CABIN", 1, 4, BedCategory.Cabin, 3500m);
            AddRange("VIP", 1, 2, BedCategory.VIPCabin, 7000m);

            _context.Beds.AddRange(beds);
            await _context.SaveChangesAsync();
            return beds.ToDictionary(b => b.BedNumber, b => b);
        }

        private sealed class PatientSpec
        {
            public required string Key;
            public string? FullName;
            public bool IsChild;
            public int AgeYears;
            public required string Gender;
            public required string BloodGroup;
            public long Phone;
            public string? EmergencyContactName;
            public long EmergencyContactPhone;
            public bool IssuePortalLogin;
        }

        private async Task<Dictionary<string, Patient>> SeedPatientsAsync(int patientRoleId)
        {
            var specs = new List<PatientSpec>
            {
                new() { Key = "rahim",   FullName = "Rahim Uddin",     AgeYears = 45, Gender = "Male",   BloodGroup = "B+",  Phone = 1711000001, EmergencyContactName = "Salma Uddin",    EmergencyContactPhone = 1711000002, IssuePortalLogin = true },
                new() { Key = "fatema",  FullName = "Fatema Begum",    AgeYears = 38, Gender = "Female", BloodGroup = "O+",  Phone = 1811000003, EmergencyContactName = "Jamal Begum",     EmergencyContactPhone = 1811000004, IssuePortalLogin = true },
                new() { Key = "karim",   FullName = "Karim Sheikh",    AgeYears = 60, Gender = "Male",   BloodGroup = "A+",  Phone = 1911000005, EmergencyContactName = "Anwar Sheikh",    EmergencyContactPhone = 1911000006, IssuePortalLogin = true },
                new() { Key = "nasrin",  FullName = "Nasrin Akter",    AgeYears = 29, Gender = "Female", BloodGroup = "AB+", Phone = 1311000007, EmergencyContactName = "Habib Akter",     EmergencyContactPhone = 1311000008, IssuePortalLogin = true },
                new() { Key = "jashim",  FullName = "Jashim Molla",    AgeYears = 52, Gender = "Male",   BloodGroup = "O-",  Phone = 1411000009, EmergencyContactName = "Rina Molla",      EmergencyContactPhone = 1411000010, IssuePortalLogin = false },
                new() { Key = "shirin",  FullName = "Shirin Sultana",  AgeYears = 34, Gender = "Female", BloodGroup = "B-",  Phone = 1511000011, EmergencyContactName = "Kabir Sultan",    EmergencyContactPhone = 1511000012, IssuePortalLogin = false },
                new() { Key = "abdul",   FullName = "Abdul Kader",     AgeYears = 70, Gender = "Male",   BloodGroup = "A-",  Phone = 1611000013, EmergencyContactName = "Momtaz Kader",    EmergencyContactPhone = 1611000014, IssuePortalLogin = true },
                new() { Key = "rupa",    FullName = "Rupa Chakma",     AgeYears = 41, Gender = "Female", BloodGroup = "O+",  Phone = 1711000015, EmergencyContactName = "Biplob Chakma",   EmergencyContactPhone = 1711000016, IssuePortalLogin = true },
                new() { Key = "mizan",   FullName = "Mizanur Rahman",  AgeYears = 55, Gender = "Male",   BloodGroup = "B+",  Phone = 1811000017, EmergencyContactName = "Runa Rahman",     EmergencyContactPhone = 1811000018, IssuePortalLogin = false },
                new() { Key = "taslima", FullName = "Taslima Khatun",  AgeYears = 26, Gender = "Female", BloodGroup = "A+",  Phone = 1911000019, EmergencyContactName = "Selim Khatun",    EmergencyContactPhone = 1911000020, IssuePortalLogin = false },
                new() { Key = "baby1",   IsChild = true, AgeYears = 1, Gender = "Male",   BloodGroup = "O+", Phone = 0, EmergencyContactName = "Nasima Begum", EmergencyContactPhone = 1311000021, IssuePortalLogin = false },
                new() { Key = "baby2",   IsChild = true, AgeYears = 2, Gender = "Female", BloodGroup = "A+", Phone = 0, EmergencyContactName = "Salma Khatun", EmergencyContactPhone = 1411000022, IssuePortalLogin = false },
            };

            var now = DateTime.UtcNow;
            var uhidPrefix = $"PT-{now:yyyyMM}-";
            var result = new Dictionary<string, Patient>();
            var sequence = 1;

            foreach (var spec in specs)
            {
                var uhid = $"{uhidPrefix}{sequence:0000}";
                sequence++;

                var patient = new Patient
                {
                    Uhid = uhid,
                    IsChild = spec.IsChild,
                    FullName = spec.FullName,
                    ContactInfo = spec.IsChild ? null : spec.Phone,
                    DateOfBirth = now.AddYears(-spec.AgeYears),
                    Gender = spec.Gender,
                    BloodGroup = spec.BloodGroup,
                    EmergencyContactName = spec.EmergencyContactName,
                    EmergencyContactPhone = spec.EmergencyContactPhone,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                if (spec.IssuePortalLogin)
                {
                    var portalUser = new User
                    {
                        RoleId = patientRoleId,
                        Username = uhid,
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword(PatientPortalPassword),
                        FullName = spec.FullName ?? uhid,
                        Category = "Patient",
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    _context.Users.Add(portalUser);
                    await _context.SaveChangesAsync();
                    patient.UserId = portalUser.Id;
                }

                _context.Patients.Add(patient);
                result[spec.Key] = patient;
            }

            await _context.SaveChangesAsync();
            return result;
        }

        private sealed record RecordSpec(string PatientKey, string ChiefComplaint, string Diagnosis, string Treatment, int DaysAgo);

        private async Task<Dictionary<string, List<MedicalRecord>>> SeedMedicalRecordsAsync(Dictionary<string, Patient> patients, int drOneId, int drTwoId)
        {
            var doctorByPatient = new Dictionary<string, int>
            {
                ["rahim"] = drOneId, ["fatema"] = drOneId, ["karim"] = drOneId, ["nasrin"] = drOneId,
                ["jashim"] = drOneId, ["shirin"] = drOneId, ["baby1"] = drOneId, ["baby2"] = drOneId,
                ["abdul"] = drTwoId, ["rupa"] = drTwoId, ["mizan"] = drTwoId, ["taslima"] = drTwoId,
            };

            var specs = new List<RecordSpec>
            {
                new("rahim",   "Fever and body ache for 3 days",        "Viral fever",                        "Paracetamol 500mg, rest and fluids",              10),
                new("rahim",   "Follow-up, mild residual fatigue",      "Post-viral fatigue, resolving",       "Continue paracetamol PRN, review in a week",       2),
                new("fatema",  "Burning epigastric pain after meals",   "Acute gastritis",                     "Omeprazole 20mg once daily before breakfast",       5),
                new("karim",   "Sore throat and painful swallowing",    "Bacterial pharyngitis",                "Azithromycin 500mg once daily for 3 days",          6),
                new("nasrin",  "Sneezing, itchy eyes in dusty weather", "Seasonal allergic rhinitis",           "Cetirizine 10mg at night",                          4),
                new("jashim",  "Cough and nasal congestion",            "Upper respiratory tract infection",    "Azithromycin 500mg once daily for 3 days",          7),
                new("shirin",  "Fever with generalized body ache",      "Viral fever",                          "Paracetamol 500mg as needed",                       3),
                new("abdul",   "Recurrent heartburn, worse at night",   "GERD, chronic",                        "Omeprazole 20mg twice daily",                      14),
                new("rupa",    "Sore throat, low-grade fever",          "Acute pharyngitis",                    "Azithromycin 500mg once daily for 3 days",          8),
                new("mizan",   "Chronic upper abdominal discomfort",    "Chronic gastritis",                    "Omeprazole 20mg once daily, dietary advice",       20),
                new("taslima", "Itchy raised skin rash, no known cause","Allergic urticaria",                   "Cetirizine 10mg at night, avoid known triggers",    5),
                new("baby1",   "Prolonged yellowish skin discoloration","Neonatal jaundice, under observation", "Phototherapy course completed, routine follow-up",  9),
                new("baby2",   "Fever, fussiness",                      "Viral fever",                          "Paracetamol syrup as needed, plenty of fluids",      3),
            };

            var now = DateTime.UtcNow;
            var byPatient = patients.Keys.ToDictionary(k => k, _ => new List<MedicalRecord>());

            foreach (var spec in specs)
            {
                var patient = patients[spec.PatientKey];
                var record = new MedicalRecord
                {
                    PatientId = patient.Id,
                    DoctorId = doctorByPatient[spec.PatientKey],
                    ChiefComplaint = spec.ChiefComplaint,
                    Diagnosis = spec.Diagnosis,
                    Treatment = spec.Treatment,
                    RecordedAt = now.AddDays(-spec.DaysAgo),
                    CreatedAt = now.AddDays(-spec.DaysAgo),
                    UpdatedAt = now.AddDays(-spec.DaysAgo)
                };
                _context.MedicalRecords.Add(record);
                byPatient[spec.PatientKey].Add(record);
            }

            await _context.SaveChangesAsync();
            return byPatient;
        }

        private sealed record RxSpec(string PatientKey, int DoctorSlot, string MedicineName, int Quantity, decimal Morning, decimal Afternoon, decimal Evening, int? DurationDays, PrescriptionStatus Status, bool IsBilled, string Diagnosis);

        private async Task SeedPrescriptionsAsync(Dictionary<string, Patient> patients, Dictionary<string, Medicine> medicines, int drOneId, int drTwoId)
        {
            var specs = new List<RxSpec>
            {
                new("rahim",   1, "Napa 500mg",      10, 1, 1, 1, 4,  PrescriptionStatus.Dispensed,     true,  "Viral fever"),
                new("fatema",  1, "Seclo 20mg",       14, 1, 0, 0, 14, PrescriptionStatus.Dispensed,     true,  "Acute gastritis"),
                new("karim",   1, "Zithromax 500mg",   6, 1, 0, 0, 6,  PrescriptionStatus.Dispensed,     true,  "Bacterial pharyngitis"),
                new("nasrin",  1, "Alatrol 10mg",     10, 0, 0, 1, 10, PrescriptionStatus.PendingPharmacy, false, "Seasonal allergic rhinitis"),
                new("jashim",  1, "Azithral 500mg",    6, 1, 0, 0, 6,  PrescriptionStatus.PendingPharmacy, false, "Upper respiratory tract infection"),
                new("shirin",  1, "Ace 500mg",        10, 1, 1, 1, 4,  PrescriptionStatus.Dispensed,     false, "Viral fever"),
                new("abdul",   2, "Seclo 20mg",       14, 1, 0, 1, 14, PrescriptionStatus.Dispensed,     true,  "GERD, chronic"),
                new("rupa",    2, "Zithromax 500mg",   6, 1, 0, 0, 6,  PrescriptionStatus.Dispensed,     true,  "Acute pharyngitis"),
                new("mizan",   2, "Seclo 20mg",       10, 1, 0, 0, 10, PrescriptionStatus.Dispensed,     true,  "Chronic gastritis"),
                new("taslima", 2, "Alatrol 10mg",     10, 0, 0, 1, 10, PrescriptionStatus.PendingPharmacy, false, "Allergic urticaria"),
                new("baby1",   1, "Ace 500mg",         1, 0, 0, 1, 3,  PrescriptionStatus.Dispensed,     false, "Neonatal jaundice, under observation"),
                new("baby2",   2, "Ace 500mg",         1, 1, 0, 1, 3,  PrescriptionStatus.PendingPharmacy, false, "Viral fever"),
            };

            var now = DateTime.UtcNow;

            foreach (var spec in specs)
            {
                var patient = patients[spec.PatientKey];
                var doctorId = spec.DoctorSlot == 1 ? drOneId : drTwoId;
                var medicine = medicines[spec.MedicineName];

                var prescription = new Prescription
                {
                    PatientId = patient.Id,
                    DoctorId = doctorId,
                    ChiefComplaints = spec.Diagnosis,
                    Diagnosis = spec.Diagnosis,
                    Notes = null,
                    Status = spec.Status,
                    IsBilled = spec.IsBilled,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                prescription.PrescriptionItems.Add(new PrescriptionItem
                {
                    MedicineId = medicine.Id,
                    Quantity = spec.Quantity,
                    UnitPrice = medicine.UnitPrice,
                    DoseMorning = spec.Morning,
                    DoseAfternoon = spec.Afternoon,
                    DoseEvening = spec.Evening,
                    DoseUnit = DoseUnit.Tablet,
                    DurationDays = spec.DurationDays,
                    Route = MedicationRoute.Oral,
                    Instructions = "Take after meals"
                });
                _context.Prescriptions.Add(prescription);
            }

            await _context.SaveChangesAsync();
        }

        private sealed record AllergySpec(string PatientKey, string Substance, string? Reaction, AllergySeverity Severity);

        private async Task SeedAllergiesAsync(Dictionary<string, Patient> patients, int recordedById)
        {
            var specs = new List<AllergySpec>
            {
                new("karim",   "Penicillin",   "Skin rash",        AllergySeverity.Moderate),
                new("nasrin",  "Azithromycin", "Nausea and hives", AllergySeverity.Moderate),
                new("abdul",   "Sulfa drugs",  "Severe skin reaction", AllergySeverity.Severe),
                new("mizan",   "Paracetamol",  "Mild rash",        AllergySeverity.Mild),
                new("taslima", "Aspirin",      "Stomach upset",    AllergySeverity.Mild),
            };

            var now = DateTime.UtcNow;
            foreach (var spec in specs)
            {
                _context.PatientAllergies.Add(new PatientAllergy
                {
                    PatientId = patients[spec.PatientKey].Id,
                    Substance = spec.Substance,
                    ReactionType = spec.Reaction,
                    Severity = spec.Severity,
                    RecordedById = recordedById,
                    CreatedAt = now
                });
            }
            await _context.SaveChangesAsync();
        }

        private sealed record AdmissionSpec(string PatientKey, int DoctorSlot, string BedNumber, int DaysAgoAdmitted, int? DaysAgoDischarged);

        private async Task<Dictionary<string, List<Admission>>> SeedAdmissionsAsync(Dictionary<string, Patient> patients, Dictionary<string, Bed> beds, int drOneId, int drTwoId)
        {
            // 11 currently-active admissions (occupied beds) + 3 discharged historical
            // admissions (readmission storyline for abdul/rupa/mizan) = 14 total, spread
            // across 14 of the 20 seeded beds. The 6 untouched beds plus the 3 freed by
            // discharge leave 9 beds free - taslima (outpatient-only) is never admitted.
            var specs = new List<AdmissionSpec>
            {
                new("rahim",  1, "ICU-01",   2, null),
                new("fatema", 1, "ICU-02",   1, null),
                new("karim",  1, "WD-101",   3, null),
                new("nasrin", 1, "WD-102",   1, null),
                new("jashim", 1, "WD-103",   4, null),
                new("shirin", 1, "WD-104",   2, null),
                new("abdul",  2, "CABIN-01", 1, null),
                new("rupa",   2, "CABIN-02", 2, null),
                new("mizan",  2, "CABIN-03", 1, null),
                new("baby1",  1, "WD-105",   5, null),
                new("baby2",  2, "WD-106",   1, null),

                // Historical, already discharged - beds below are free again.
                new("abdul",  2, "VIP-01",  20, 15),
                new("rupa",   2, "VIP-02",  25, 18),
                new("mizan",  2, "WD-107",  30, 22),
            };

            var now = DateTime.UtcNow;
            var byPatient = patients.Keys.ToDictionary(k => k, _ => new List<Admission>());

            foreach (var spec in specs)
            {
                var patient = patients[spec.PatientKey];
                var doctorId = spec.DoctorSlot == 1 ? drOneId : drTwoId;
                var admittedAt = now.AddDays(-spec.DaysAgoAdmitted);
                DateTime? dischargedAt = spec.DaysAgoDischarged.HasValue ? now.AddDays(-spec.DaysAgoDischarged.Value) : null;

                var admission = new Admission
                {
                    PatientId = patient.Id,
                    AdmittingDoctorId = doctorId,
                    AdmissionDate = admittedAt,
                    DischargeDate = dischargedAt,
                    CreatedAt = admittedAt,
                    UpdatedAt = dischargedAt ?? admittedAt
                };
                _context.Admissions.Add(admission);
                await _context.SaveChangesAsync();

                _context.BedTransfers.Add(new BedTransfer
                {
                    AdmissionId = admission.Id,
                    BedId = beds[spec.BedNumber].Id,
                    StartDate = admittedAt,
                    EndDate = dischargedAt
                });

                byPatient[spec.PatientKey].Add(admission);
            }

            await _context.SaveChangesAsync();
            return byPatient;
        }

        private async Task SeedBillsAsync(Dictionary<string, List<Admission>> admissionsByPatient, int discountApprovedById)
        {
            var now = DateTime.UtcNow;

            async Task<Bill> BuildBillAsync(Admission admission, decimal paidAmount, decimal discount = 0)
            {
                var days = Math.Max(1, (int)Math.Ceiling(((admission.DischargeDate ?? now) - admission.AdmissionDate).TotalDays));
                var bedTransfer = await _context.BedTransfers
                    .Include(bt => bt.Bed)
                    .FirstAsync(bt => bt.AdmissionId == admission.Id);

                var pharmacyCharge = await _context.Prescriptions
                    .Where(p => p.PatientId == admission.PatientId && p.Status == PrescriptionStatus.Dispensed && p.IsBilled)
                    .SelectMany(p => p.PrescriptionItems)
                    .SumAsync(i => (decimal?)(i.Quantity * i.UnitPrice)) ?? 0m;

                var bill = new Bill
                {
                    PatientId = admission.PatientId,
                    AdmissionId = admission.Id,
                    DiscountAmount = discount,
                    DiscountApprovedById = discount > 0 ? discountApprovedById : null,
                    PaidAmount = paidAmount,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                bill.BillItems.Add(new BillItem
                {
                    Department = DepartmentType.CabinRent,
                    Description = $"{bedTransfer.Bed.BedNumber} ({bedTransfer.Bed.Category}) x {days} day(s)",
                    Amount = days * bedTransfer.Bed.DailyRate
                });
                if (pharmacyCharge > 0)
                {
                    bill.BillItems.Add(new BillItem
                    {
                        Department = DepartmentType.Pharmacy,
                        Description = "Dispensed medication",
                        Amount = pharmacyCharge
                    });
                }
                bill.RecalculateTotals();
                bill.PaidAmount = paidAmount;
                bill.RecalculateTotals();
                _context.Bills.Add(bill);
                return bill;
            }

            // Paid: the two straightforward discharged admissions.
            var abdulHistorical = admissionsByPatient["abdul"].First(a => a.DischargeDate.HasValue);
            var abdulBill = await BuildBillAsync(abdulHistorical, paidAmount: 0);
            abdulBill.PaidAmount = abdulBill.NetTotal;
            abdulBill.RecalculateTotals();

            var rupaHistorical = admissionsByPatient["rupa"].First(a => a.DischargeDate.HasValue);
            var rupaBill = await BuildBillAsync(rupaHistorical, paidAmount: 0, discount: 200m);
            rupaBill.PaidAmount = rupaBill.NetTotal;
            rupaBill.RecalculateTotals();

            // Partially paid: the third discharged admission, still owes a balance.
            var mizanHistorical = admissionsByPatient["mizan"].First(a => a.DischargeDate.HasValue);
            var mizanBill = await BuildBillAsync(mizanHistorical, paidAmount: 0);
            mizanBill.PaidAmount = Math.Round(mizanBill.NetTotal * 0.4m, 2);
            mizanBill.RecalculateTotals();

            // Unpaid: three currently-active admissions, mid-stay.
            await BuildBillAsync(admissionsByPatient["rahim"].First(a => !a.DischargeDate.HasValue), paidAmount: 0);
            await BuildBillAsync(admissionsByPatient["fatema"].First(a => !a.DischargeDate.HasValue), paidAmount: 0);
            await BuildBillAsync(admissionsByPatient["karim"].First(a => !a.DischargeDate.HasValue), paidAmount: 0);

            await _context.SaveChangesAsync();
        }

        private sealed record AppointmentSpec(string PatientKey, int DoctorSlot, AppointmentStatus Status, int MinutesAgo, string Reason);

        private async Task<Dictionary<string, Appointment>> SeedAppointmentsAsync(Dictionary<string, Patient> patients, int drOneId, int drTwoId)
        {
            var specs = new List<AppointmentSpec>
            {
                new("nasrin",  1, AppointmentStatus.Scheduled,     10, "Follow-up for allergy symptoms"),
                new("jashim",  1, AppointmentStatus.Scheduled,     25, "Persistent cough"),
                new("rahim",   1, AppointmentStatus.InConsultation, 8, "Fever follow-up"),
                new("shirin",  1, AppointmentStatus.Completed,    180, "Fever check-up"),

                new("taslima", 2, AppointmentStatus.Scheduled,     15, "Skin rash follow-up"),
                new("baby2",   2, AppointmentStatus.Scheduled,     30, "Fever check"),
                new("abdul",   2, AppointmentStatus.InConsultation, 6, "GERD follow-up"),
                new("rupa",    2, AppointmentStatus.Completed,    150, "Throat infection review"),
            };

            var now = DateTime.UtcNow;
            var result = new Dictionary<string, Appointment>();
            foreach (var spec in specs)
            {
                var patient = patients[spec.PatientKey];
                var doctorId = spec.DoctorSlot == 1 ? drOneId : drTwoId;
                var startedAt = now.AddMinutes(-spec.MinutesAgo);

                var appointment = new Appointment
                {
                    PatientId = patient.Id,
                    DoctorId = doctorId,
                    AppointmentDatetime = startedAt,
                    EndTime = startedAt.AddMinutes(15),
                    ReasonForVisit = spec.Reason,
                    Status = spec.Status,
                    CreatedAt = startedAt,
                    UpdatedAt = now
                };
                _context.Appointments.Add(appointment);
                result[spec.PatientKey] = appointment;
            }

            await _context.SaveChangesAsync();
            return result;
        }

        private sealed record HistoricalAppointmentSpec(string PatientKey, int DoctorSlot, AppointmentStatus Status, int DaysAgo, string Reason);

        // Backdated appointment history spread across the last two weeks, so the admin
        // Reports "appointment volume" chart has a real trend to plot instead of a
        // single-day spike. Deliberately separate from SeedAppointmentsAsync above -
        // today's queue is exactly what the Assistant/Doctor walkthroughs depend on, and
        // this method never touches it.
        private async Task SeedHistoricalAppointmentsAsync(Dictionary<string, Patient> patients, int drOneId, int drTwoId)
        {
            var specs = new List<HistoricalAppointmentSpec>
            {
                new("shirin",  1, AppointmentStatus.Completed, 1,  "Routine follow-up"),
                new("rupa",    2, AppointmentStatus.Completed, 1,  "Medication review"),
                new("karim",   1, AppointmentStatus.Completed, 2,  "Throat re-check"),
                new("mizan",   2, AppointmentStatus.Cancelled, 2,  "Chronic gastritis review"),
                new("nasrin",  1, AppointmentStatus.Completed, 3,  "Allergy follow-up"),
                new("abdul",   2, AppointmentStatus.Completed, 3,  "GERD review"),
                new("jashim",  1, AppointmentStatus.Completed, 4,  "Cough re-check"),
                new("taslima", 2, AppointmentStatus.Completed, 4,  "Skin rash review"),
                new("fatema",  1, AppointmentStatus.Cancelled, 5,  "Gastritis follow-up"),
                new("rahim",   1, AppointmentStatus.Completed, 5,  "Fever re-check"),
                new("baby2",   2, AppointmentStatus.Completed, 6,  "Fever recheck"),
                new("karim",   1, AppointmentStatus.Completed, 6,  "Throat pain"),
                new("rupa",    2, AppointmentStatus.Completed, 7,  "Sore throat"),
                new("shirin",  1, AppointmentStatus.Completed, 7,  "Body ache"),
                new("mizan",   2, AppointmentStatus.Completed, 8,  "Abdominal discomfort"),
                new("nasrin",  1, AppointmentStatus.Cancelled, 8,  "Allergy check"),
                new("abdul",   2, AppointmentStatus.Completed, 9,  "Heartburn review"),
                new("jashim",  1, AppointmentStatus.Completed, 9,  "Nasal congestion"),
                new("taslima", 2, AppointmentStatus.Completed, 10, "Rash follow-up"),
                new("fatema",  1, AppointmentStatus.Completed, 10, "Epigastric pain"),
                new("rahim",   1, AppointmentStatus.Completed, 11, "General checkup"),
                new("baby1",   1, AppointmentStatus.Completed, 11, "Jaundice follow-up"),
                new("karim",   1, AppointmentStatus.Completed, 12, "Sore throat"),
                new("rupa",    2, AppointmentStatus.Cancelled, 13, "Throat infection"),
                new("mizan",   2, AppointmentStatus.Completed, 14, "Gastritis check"),
            };

            var now = DateTime.UtcNow;
            foreach (var spec in specs)
            {
                var patient = patients[spec.PatientKey];
                var doctorId = spec.DoctorSlot == 1 ? drOneId : drTwoId;
                var startedAt = now.Date.AddDays(-spec.DaysAgo).AddHours(10);

                _context.Appointments.Add(new Appointment
                {
                    PatientId = patient.Id,
                    DoctorId = doctorId,
                    AppointmentDatetime = startedAt,
                    EndTime = startedAt.AddMinutes(15),
                    ReasonForVisit = spec.Reason,
                    Status = spec.Status,
                    CreatedAt = startedAt,
                    UpdatedAt = startedAt
                });
            }

            await _context.SaveChangesAsync();
        }

        private sealed record VitalSpec(
            string PatientKey, int SystolicBp, int DiastolicBp, int HeartRate, decimal Spo2, decimal Temperature,
            int RespiratoryRate, bool OnSupplementalOxygen, ConsciousnessLevel Consciousness, int RecordedBySlot, bool AttachToTodaysAppointment);

        // NEWS2 Scale 1 is validated for adult patients only, so vitals are seeded for
        // the ten adult patients and deliberately not for the two infant patients.
        // TriagePriority is computed through News2Calculator itself, not hardcoded, so
        // the seeded data is provably consistent with the same algorithm the test suite
        // checks. Rahim and Abdul are today's two InConsultation patients, so attaching
        // their vitals to those appointments makes the triage badge visible on the
        // queue and the doctor's consultation card immediately after a reseed.
        private async Task SeedVitalsAsync(Dictionary<string, Patient> patients, Dictionary<string, Appointment> todaysAppointments, int assistantOneId, int assistantTwoId)
        {
            var specs = new List<VitalSpec>
            {
                new("rahim",   95, 60,  78, 94, 37.2m, 22, false, ConsciousnessLevel.Alert, 1, true),  // Urgent: aggregate 5
                new("abdul",  124, 80,  76, 98, 36.8m, 16, false, ConsciousnessLevel.Alert, 2, true),  // Normal

                new("fatema", 118, 76,  82, 97, 37.0m, 18, false, ConsciousnessLevel.Alert, 1, false),
                new("karim",  130, 84,  88, 96, 37.3m, 18, false, ConsciousnessLevel.Alert, 1, false),
                new("nasrin", 112, 70,  72, 99, 36.9m, 15, false, ConsciousnessLevel.Alert, 1, false),
                new("jashim", 122, 78,  84, 91, 37.1m, 18, false, ConsciousnessLevel.Alert, 1, false), // Urgent: single red score (SpO2)
                new("shirin", 116, 74,  68, 98, 36.7m, 14, false, ConsciousnessLevel.Alert, 1, false),
                new("rupa",   120, 78,  74, 97, 37.0m, 16, false, ConsciousnessLevel.Alert, 2, false),
                new("mizan",   88, 55, 118, 90, 35.6m, 26, true,  ConsciousnessLevel.Voice, 2, false), // Emergency
                new("taslima",110, 68,  70, 98, 36.8m, 15, false, ConsciousnessLevel.Alert, 2, false),
            };

            var now = DateTime.UtcNow;
            foreach (var spec in specs)
            {
                var patient = patients[spec.PatientKey];
                var recordedById = spec.RecordedBySlot == 1 ? assistantOneId : assistantTwoId;
                var result = News2Calculator.Calculate(
                    spec.RespiratoryRate, spec.Spo2, spec.OnSupplementalOxygen, spec.Temperature,
                    spec.SystolicBp, spec.HeartRate, spec.Consciousness);

                _context.PatientVitals.Add(new PatientVital
                {
                    PatientId = patient.Id,
                    AppointmentId = spec.AttachToTodaysAppointment ? todaysAppointments[spec.PatientKey].Id : null,
                    SystolicBp = spec.SystolicBp,
                    DiastolicBp = spec.DiastolicBp,
                    HeartRate = spec.HeartRate,
                    Spo2 = spec.Spo2,
                    Temperature = spec.Temperature,
                    RespiratoryRate = spec.RespiratoryRate,
                    OnSupplementalOxygen = spec.OnSupplementalOxygen,
                    Consciousness = spec.Consciousness,
                    TriagePriority = result.Priority,
                    RecordedById = recordedById,
                    CreatedAt = now
                });
            }

            await _context.SaveChangesAsync();
        }

        // A pre-generated draft for Rahim (today's InConsultation patient under drmock), so
        // the doctor-side Accept/Edit/Reject workflow has something to demo even if the
        // real AI provider is unreachable on presentation day. Format matches what
        // AiNarrativeRenderer expects: one **headline** line, then "- " bullets citing real
        // MedicalRecord ids for this patient.
        private async Task SeedCannedAiSuggestionAsync(Dictionary<string, Patient> patients, Dictionary<string, List<MedicalRecord>> recordsByPatient)
        {
            var patient = patients["rahim"];
            var records = recordsByPatient["rahim"];
            if (records.Count == 0)
            {
                return;
            }

            var firstRecordId = records[0].Id;
            var latestRecordId = records[^1].Id;

            var narrative =
                "**Follow-up for viral fever, now with resolving fatigue**\n\n" +
                $"- Diagnosis: Viral fever with body ache [[rec:{firstRecordId}]]\n" +
                $"- Treatment: Paracetamol 500mg and supportive care [[rec:{firstRecordId}]]\n" +
                $"- Status: Fever resolved, mild residual fatigue on follow-up [[rec:{latestRecordId}]]";

            var draft = new CaseSummaryDraft
            {
                NarrativeText = narrative,
                CitedRecordIds = records.Select(r => r.Id).Distinct().ToList()
            };

            _context.AiSuggestions.Add(new AiSuggestion
            {
                SuggestionType = AiSuggestionType.CaseSummary,
                PatientId = patient.Id,
                PayloadJson = JsonSerializer.Serialize(draft),
                SourceRecordIds = JsonSerializer.Serialize(records.Select(r => r.Id)),
                ModelId = "seed-demo-fallback",
                PromptVersion = "case-summary-v1",
                Verdict = AiSuggestionVerdict.Pending,
                InputTokens = 0,
                OutputTokens = 0,
                CachedTokens = 0,
                LatencyMs = 0,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
        }

        private sealed record AdditionalAiSuggestionSpec(string PatientKey, AiSuggestionVerdict Verdict, int DoctorSlot, int InputTokens, int OutputTokens, int CachedTokens, int LatencyMs, int DaysAgo);

        // Past AI activity across all four verdicts, so the admin Reports "AI
        // governance" panel has more than Rahim's single pending draft to summarise.
        // Reuses the same PayloadJson/CaseSummaryDraft shape as the canned suggestion
        // above rather than inventing a second format.
        private async Task SeedAdditionalAiSuggestionsAsync(Dictionary<string, Patient> patients, Dictionary<string, List<MedicalRecord>> recordsByPatient, int drOneId, int drTwoId)
        {
            var specs = new List<AdditionalAiSuggestionSpec>
            {
                new("fatema", AiSuggestionVerdict.Accepted, 1, 1450, 320, 200, 2100, 5),
                new("karim",  AiSuggestionVerdict.Accepted, 1, 1600, 280, 150, 1850, 6),
                new("shirin", AiSuggestionVerdict.Accepted, 1, 1550, 330, 220, 2000, 4),
                new("abdul",  AiSuggestionVerdict.Edited,   2, 1720, 410, 300, 2400, 3),
                new("rupa",   AiSuggestionVerdict.Edited,   2, 1380, 350, 100, 1950, 8),
                new("mizan",  AiSuggestionVerdict.Rejected, 2, 1500, 300, 0,   2200, 9),
                new("nasrin", AiSuggestionVerdict.Pending,  1, 1420, 290, 180, 2050, 0),
            };

            foreach (var spec in specs)
            {
                var records = recordsByPatient[spec.PatientKey];
                if (records.Count == 0)
                {
                    continue;
                }

                var now = DateTime.UtcNow;
                var createdAt = now.AddDays(-spec.DaysAgo);
                var doctorId = spec.DoctorSlot == 1 ? drOneId : drTwoId;
                var recordId = records[^1].Id;

                var draft = new CaseSummaryDraft
                {
                    NarrativeText =
                        $"**Case summary for {patients[spec.PatientKey].FullName}**\n\n" +
                        $"- Summary generated from recent chart review [[rec:{recordId}]]",
                    CitedRecordIds = new List<int> { recordId }
                };

                var suggestion = new AiSuggestion
                {
                    SuggestionType = AiSuggestionType.CaseSummary,
                    PatientId = patients[spec.PatientKey].Id,
                    PayloadJson = JsonSerializer.Serialize(draft),
                    SourceRecordIds = JsonSerializer.Serialize(new[] { recordId }),
                    ModelId = "gemini-3.8-flash",
                    PromptVersion = "case-summary-v1",
                    Verdict = spec.Verdict,
                    InputTokens = spec.InputTokens,
                    OutputTokens = spec.OutputTokens,
                    CachedTokens = spec.CachedTokens,
                    LatencyMs = spec.LatencyMs,
                    CreatedAt = createdAt
                };

                if (spec.Verdict != AiSuggestionVerdict.Pending)
                {
                    suggestion.ReviewedById = doctorId;
                    suggestion.ReviewedAt = createdAt.AddMinutes(3);

                    if (spec.Verdict == AiSuggestionVerdict.Edited)
                    {
                        suggestion.EditedPayloadJson = JsonSerializer.Serialize(new CaseSummaryDraft
                        {
                            NarrativeText = draft.NarrativeText + "\n- Reviewed and refined by the attending physician.",
                            CitedRecordIds = draft.CitedRecordIds
                        });
                    }
                }

                _context.AiSuggestions.Add(suggestion);
            }

            await _context.SaveChangesAsync();
        }

        private void PrintSummary()
        {
            Console.WriteLine();
            Console.WriteLine("=== Demo data seeded ===");
            Console.WriteLine();
            Console.WriteLine("Staff logins (password for any newly-created account is: " + StaffPassword + ")");
            Console.WriteLine("  admin           - Admin        (existing account, password unchanged)");
            Console.WriteLine("  drmock          - Doctor        (existing account, password unchanged)");
            Console.WriteLine("  dr2             - Doctor        (Dr. Farhana Chowdhury)");
            Console.WriteLine("  mock-assistant  - Assistant     (existing account, password unchanged; assigned to drmock)");
            Console.WriteLine("  assistant2      - Assistant     (Tanvir Islam; assigned to dr2)");
            Console.WriteLine("  pharmacistmock  - Pharmacist    (existing account, password unchanged)");
            Console.WriteLine("  pharmacist2     - Pharmacist    (Nusrat Jahan)");
            Console.WriteLine("  reception1      - Receptionist  (Ayesha Rahman)");
            Console.WriteLine("  reception2      - Receptionist  (Kamal Hossain)");
            Console.WriteLine();
            Console.WriteLine("Patient portal logins (password: " + PatientPortalPassword + "), username = UHID:");
            Console.WriteLine("  Rahim Uddin, Fatema Begum, Karim Sheikh, Nasrin Akter, Abdul Kader, Rupa Chakma");
            Console.WriteLine("  (see the Patients list for each one's exact UHID/username)");
            Console.WriteLine();
            Console.WriteLine("12 patients, 20 beds (11 occupied / 9 free), 14 admissions (11 active, 3 discharged),");
            Console.WriteLine("12 prescriptions, 6 bills (2 paid, 1 partially paid, 3 unpaid), 8 appointments today");
            Console.WriteLine("plus 25 historical appointments across the last 14 days (for the admin Reports chart),");
            Console.WriteLine("10 vitals/triage readings (7 Normal, 2 Urgent, 1 Emergency), and 8 AI suggestions");
            Console.WriteLine("across all four verdicts (Rahim Uddin's is the one still Pending review).");
        }
    }
}
