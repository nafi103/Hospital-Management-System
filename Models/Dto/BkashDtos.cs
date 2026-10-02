using System.Text.Json.Serialization;

namespace HospitalManagementSystem.Models.Dto
{
    public class BkashTokenRequest
    {
        [JsonPropertyName("app_key")]
        public string AppKey { get; set; } = string.Empty;

        [JsonPropertyName("app_secret")]
        public string AppSecret { get; set; } = string.Empty;
    }

    public class BkashTokenResponse
    {
        [JsonPropertyName("statusCode")]
        public string? StatusCode { get; set; }

        [JsonPropertyName("statusMessage")]
        public string? StatusMessage { get; set; }

        [JsonPropertyName("id_token")]
        public string? IdToken { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }
    }

    public class BkashCreatePaymentRequest
    {
        [JsonPropertyName("mode")]
        public string Mode { get; set; } = "0011";

        [JsonPropertyName("payerReference")]
        public string PayerReference { get; set; } = string.Empty;

        [JsonPropertyName("callbackURL")]
        public string CallbackUrl { get; set; } = string.Empty;

        [JsonPropertyName("amount")]
        public string Amount { get; set; } = string.Empty;

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = "BDT";

        [JsonPropertyName("intent")]
        public string Intent { get; set; } = "sale";

        [JsonPropertyName("merchantInvoiceNumber")]
        public string MerchantInvoiceNumber { get; set; } = string.Empty;
    }

    public class BkashCreatePaymentResponse
    {
        [JsonPropertyName("statusCode")]
        public string? StatusCode { get; set; }

        [JsonPropertyName("statusMessage")]
        public string? StatusMessage { get; set; }

        [JsonPropertyName("paymentID")]
        public string? PaymentID { get; set; }

        [JsonPropertyName("bkashURL")]
        public string? BkashURL { get; set; }

        [JsonPropertyName("callbackURL")]
        public string? CallbackURL { get; set; }

        [JsonPropertyName("amount")]
        public string? Amount { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("intent")]
        public string? Intent { get; set; }

        [JsonPropertyName("merchantInvoiceNumber")]
        public string? MerchantInvoiceNumber { get; set; }

        [JsonPropertyName("transactionStatus")]
        public string? TransactionStatus { get; set; }

        [JsonPropertyName("paymentCreateTime")]
        public string? PaymentCreateTime { get; set; }
    }

    public class BkashExecutePaymentRequest
    {
        [JsonPropertyName("paymentID")]
        public string PaymentID { get; set; } = string.Empty;
    }

    public class BkashExecutePaymentResponse
    {
        [JsonPropertyName("statusCode")]
        public string? StatusCode { get; set; }

        [JsonPropertyName("statusMessage")]
        public string? StatusMessage { get; set; }

        [JsonPropertyName("paymentID")]
        public string? PaymentID { get; set; }

        [JsonPropertyName("payerReference")]
        public string? PayerReference { get; set; }

        [JsonPropertyName("customerMsisdn")]
        public string? CustomerMsisdn { get; set; }

        [JsonPropertyName("trxID")]
        public string? TrxID { get; set; }

        [JsonPropertyName("amount")]
        public string? Amount { get; set; }

        [JsonPropertyName("transactionStatus")]
        public string? TransactionStatus { get; set; }

        [JsonPropertyName("paymentExecuteTime")]
        public string? PaymentExecuteTime { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("intent")]
        public string? Intent { get; set; }

        [JsonPropertyName("merchantInvoiceNumber")]
        public string? MerchantInvoiceNumber { get; set; }
    }
}
