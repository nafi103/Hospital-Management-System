using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Models.Dto;

namespace HospitalManagementSystem.Services
{
    public class BkashPaymentService : IBkashPaymentService
    {
        private readonly HttpClient _httpClient;
        private readonly BkashSettings _settings;
        private readonly IMemoryCache _cache;
        private readonly ILogger<BkashPaymentService> _logger;

        private const string TokenCacheKey = "Bkash_IdToken_Cache";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public BkashPaymentService(
            HttpClient httpClient,
            IOptions<BkashSettings> options,
            IMemoryCache cache,
            ILogger<BkashPaymentService> logger)
        {
            _httpClient = httpClient;
            _settings = options.Value;
            _cache = cache;
            _logger = logger;
        }

        public async Task<string?> GrantTokenAsync()
        {
            if (_cache.TryGetValue(TokenCacheKey, out string? cachedToken) && !string.IsNullOrEmpty(cachedToken))
            {
                return cachedToken;
            }

            var requestUrl = $"{_settings.BaseUrl.TrimEnd('/')}/tokenized/checkout/token/grant";
            var requestBody = new BkashTokenRequest
            {
                AppKey = _settings.AppKey,
                AppSecret = _settings.AppSecret
            };

            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody, JsonOptions), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("username", _settings.Username);
            request.Headers.Add("password", _settings.Password);

            try
            {
                var response = await _httpClient.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("bKash GrantToken failed with HTTP {StatusCode}: {Content}", response.StatusCode, content);
                    return null;
                }

                var tokenResponse = JsonSerializer.Deserialize<BkashTokenResponse>(content, JsonOptions);
                if (tokenResponse == null || tokenResponse.StatusCode != "0000" || string.IsNullOrEmpty(tokenResponse.IdToken))
                {
                    _logger.LogError("bKash GrantToken returned invalid status: Code={Code}, Message={Message}",
                        tokenResponse?.StatusCode, tokenResponse?.StatusMessage);
                    return null;
                }

                // Cache token with 5-minute safety buffer before expiry
                var expiresInSeconds = tokenResponse.ExpiresIn ?? 3600;
                var cacheDuration = TimeSpan.FromSeconds(Math.Max(60, expiresInSeconds - 300));
                _cache.Set(TokenCacheKey, tokenResponse.IdToken, cacheDuration);

                _logger.LogInformation("bKash token granted successfully, expires in {ExpiresIn}s", expiresInSeconds);
                return tokenResponse.IdToken;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while calling bKash GrantTokenAsync");
                return null;
            }
        }

        public async Task<BkashCreatePaymentResponse?> CreatePaymentAsync(
            int billId,
            decimal amount,
            string payerReference,
            string callbackUrl)
        {
            var token = await GrantTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogError("Cannot create bKash payment: GrantTokenAsync returned null or empty token");
                return null;
            }

            var requestUrl = $"{_settings.BaseUrl.TrimEnd('/')}/tokenized/checkout/create";
            var createRequest = new BkashCreatePaymentRequest
            {
                Mode = "0011",
                PayerReference = payerReference,
                CallbackUrl = callbackUrl,
                Amount = amount.ToString("0.00", CultureInfo.InvariantCulture),
                Currency = "BDT",
                Intent = "sale",
                MerchantInvoiceNumber = $"BILL-{billId}"
            };

            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(createRequest, JsonOptions), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Authorization", token);
            request.Headers.Add("X-APP-Key", _settings.AppKey);

            try
            {
                var response = await _httpClient.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    _logger.LogWarning("bKash token rejected (401 Unauthorized). Evicting cache and retrying once.");
                    _cache.Remove(TokenCacheKey);
                    token = await GrantTokenAsync();
                    if (string.IsNullOrEmpty(token)) return null;

                    var retryRequest = new HttpRequestMessage(HttpMethod.Post, requestUrl)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(createRequest, JsonOptions), Encoding.UTF8, "application/json")
                    };
                    retryRequest.Headers.Add("Authorization", token);
                    retryRequest.Headers.Add("X-APP-Key", _settings.AppKey);

                    response = await _httpClient.SendAsync(retryRequest);
                    content = await response.Content.ReadAsStringAsync();
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("bKash CreatePayment failed with HTTP {StatusCode}: {Content}", response.StatusCode, content);
                    return null;
                }

                var createResponse = JsonSerializer.Deserialize<BkashCreatePaymentResponse>(content, JsonOptions);
                return createResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while calling bKash CreatePaymentAsync for Bill #{BillId}", billId);
                return null;
            }
        }

        public async Task<BkashExecutePaymentResponse?> ExecutePaymentAsync(string paymentId)
        {
            var token = await GrantTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogError("Cannot execute bKash payment: GrantTokenAsync returned null or empty token");
                return null;
            }

            var requestUrl = $"{_settings.BaseUrl.TrimEnd('/')}/tokenized/checkout/execute";
            var executeRequest = new BkashExecutePaymentRequest
            {
                PaymentID = paymentId
            };

            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(executeRequest, JsonOptions), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Authorization", token);
            request.Headers.Add("X-APP-Key", _settings.AppKey);

            try
            {
                var response = await _httpClient.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    _logger.LogWarning("bKash token rejected (401 Unauthorized) during Execute. Evicting cache and retrying once.");
                    _cache.Remove(TokenCacheKey);
                    token = await GrantTokenAsync();
                    if (string.IsNullOrEmpty(token)) return null;

                    var retryRequest = new HttpRequestMessage(HttpMethod.Post, requestUrl)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(executeRequest, JsonOptions), Encoding.UTF8, "application/json")
                    };
                    retryRequest.Headers.Add("Authorization", token);
                    retryRequest.Headers.Add("X-APP-Key", _settings.AppKey);

                    response = await _httpClient.SendAsync(retryRequest);
                    content = await response.Content.ReadAsStringAsync();
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("bKash ExecutePayment failed with HTTP {StatusCode}: {Content}", response.StatusCode, content);
                    return null;
                }

                var executeResponse = JsonSerializer.Deserialize<BkashExecutePaymentResponse>(content, JsonOptions);
                return executeResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while calling bKash ExecutePaymentAsync for PaymentId {PaymentId}", paymentId);
                return null;
            }
        }
    }
}
