using System;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Models.ViewModels;

namespace HospitalManagementSystem.Services
{
    public static class DoctorDisplayExtensions
    {
        public static string ToDoctorTitle(this string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Doctor";
            var trimmed = name.Trim();
            if (trimmed.StartsWith("Dr.", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Dr ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Doctor ", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }
            return "Dr. " + trimmed;
        }

        public static string ToDoctorTitle(this User? doctor)
        {
            if (doctor == null) return "Doctor";
            return doctor.FullName.ToDoctorTitle();
        }

        public static string ToDoctorTitle(this DoctorDirectoryItemViewModel? doctor)
        {
            if (doctor == null) return "Doctor";
            return doctor.FullName.ToDoctorTitle();
        }
    }
}
