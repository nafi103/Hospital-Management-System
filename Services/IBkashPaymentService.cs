using System.Threading.Tasks;
using HospitalManagementSystem.Models.Dto;

namespace HospitalManagementSystem.Services
{
    public interface IBkashPaymentService
    {
        Task<string?> GrantTokenAsync();
        Task<BkashCreatePaymentResponse?> CreatePaymentAsync(int billId, decimal amount, string payerReference, string callbackUrl);
        Task<BkashExecutePaymentResponse?> ExecutePaymentAsync(string paymentId);
    }
}
