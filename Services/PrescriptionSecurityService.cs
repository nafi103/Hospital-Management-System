using System;
using System.Security.Cryptography;
using System.Text;
using HospitalManagementSystem.Models;
using Microsoft.Extensions.Configuration;
using QRCoder;

namespace HospitalManagementSystem.Services
{
    public class PrescriptionSecurityService : IPrescriptionSecurityService
    {
        private readonly byte[] _hmacKey;

        public PrescriptionSecurityService(IConfiguration configuration)
        {
            var keyString = configuration["PrescriptionSecurity:SecretKey"] 
                ?? "HMS-Cryptographic-Prescription-HMAC-SHA256-Key-2026-BanglaMed!";
            _hmacKey = Encoding.UTF8.GetBytes(keyString);
        }

        public string GenerateSignature(int prescriptionId, string uhid, DateTime issuedAt)
        {
            var normalizedUhid = uhid?.Trim().ToUpperInvariant() ?? "";
            var ticks = issuedAt.ToUniversalTime().Ticks;
            var payload = FormattableString.Invariant($"{prescriptionId}|{normalizedUhid}|{ticks}");

            using var hmac = new HMACSHA256(_hmacKey);
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hash);
        }

        public bool VerifySignature(int prescriptionId, string uhid, DateTime issuedAt, string signature)
        {
            if (string.IsNullOrWhiteSpace(signature)) return false;

            var expected = GenerateSignature(prescriptionId, uhid, issuedAt);
            var expectedBytes = Encoding.UTF8.GetBytes(expected);
            var actualBytes = Encoding.UTF8.GetBytes(signature.Trim().ToUpperInvariant());

            if (expectedBytes.Length != actualBytes.Length)
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
        }

        public string GenerateQrSvg(string payload, int pixelsPerModule = 4)
        {
            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
            var qrCode = new SvgQRCode(qrCodeData);
            return qrCode.GetGraphic(pixelsPerModule);
        }

        public PrescriptionVerificationData GenerateVerificationData(Prescription prescription, string? baseUrl = null)
        {
            var uhid = prescription.Patient?.Uhid ?? "";
            var issuedAt = prescription.CreatedAt != default 
                ? prescription.CreatedAt 
                : DateTime.UtcNow;

            var signature = GenerateSignature(prescription.Id, uhid, issuedAt);
            var basePrefix = string.IsNullOrWhiteSpace(baseUrl) ? "" : baseUrl.TrimEnd('/');
            var ticks = issuedAt.ToUniversalTime().Ticks;
            var verificationUrl = $"{basePrefix}/Verify/Prescription?id={prescription.Id}&uhid={Uri.EscapeDataString(uhid)}&ts={ticks}&sig={signature}";

            var svg = GenerateQrSvg(verificationUrl);

            return new PrescriptionVerificationData(
                prescription.Id,
                uhid,
                issuedAt,
                signature,
                verificationUrl,
                svg);
        }
    }
}
