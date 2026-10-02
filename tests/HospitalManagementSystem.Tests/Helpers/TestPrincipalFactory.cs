using System.Collections.Generic;
using System.Security.Claims;

namespace HospitalManagementSystem.Tests.Helpers
{
    public static class TestPrincipalFactory
    {
        public static ClaimsPrincipal CreatePrincipal(
            int userId,
            string username,
            string role,
            int? assignedDoctorId = null)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, username),
                new(ClaimTypes.Role, role),
                new("UserId", userId.ToString())
            };

            if (assignedDoctorId.HasValue)
            {
                claims.Add(new Claim("AssignedDoctorId", assignedDoctorId.Value.ToString()));
            }

            var identity = new ClaimsIdentity(claims, "TestAuthType");
            return new ClaimsPrincipal(identity);
        }

        public static ClaimsPrincipal CreateDoctor(int doctorId, string username = "dr_alice") =>
            CreatePrincipal(doctorId, username, "Doctor");

        public static ClaimsPrincipal CreateAssistant(int assistantId, int assignedDoctorId, string username = "asst_anna") =>
            CreatePrincipal(assistantId, username, "Assistant", assignedDoctorId);

        public static ClaimsPrincipal CreateUnassignedAssistant(int assistantId, string username = "asst_unassigned") =>
            CreatePrincipal(assistantId, username, "Assistant", null);

        public static ClaimsPrincipal CreateReceptionist(int cashierId = 30, string username = "cashier1") =>
            CreatePrincipal(cashierId, username, "Receptionist");

        public static ClaimsPrincipal CreatePharmacist(int pharmacistId = 35, string username = "pharm1") =>
            CreatePrincipal(pharmacistId, username, "Pharmacist");

        public static ClaimsPrincipal CreateAdmin(int adminId = 1, string username = "admin") =>
            CreatePrincipal(adminId, username, "Admin");

        public static ClaimsPrincipal CreatePatient(int patientUserId = 100, string uhid = "PT-202610-0100") =>
            CreatePrincipal(patientUserId, uhid, "Patient");
    }
}
