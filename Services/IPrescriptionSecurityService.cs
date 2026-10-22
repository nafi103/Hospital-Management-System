using System;
using HospitalManagementSystem.Models;

namespace HospitalManagementSystem.Services
{
    public record PrescriptionVerificationData(
        int PrescriptionId,
        string Uhid,
        DateTime IssuedAt,
        string Signature,
        string VerificationUrl,
        string QrSvg);

    public interface IPrescriptionSecurityService
    {
        string GenerateSignature(int prescriptionId, string uhid, DateTime issuedAt);
        bool VerifySignature(int prescriptionId, string uhid, DateTime issuedAt, string signature);
        string GenerateQrSvg(string payload, int pixelsPerModule = 4);
        PrescriptionVerificationData GenerateVerificationData(Prescription prescription, string? baseUrl = null);
    }
}
