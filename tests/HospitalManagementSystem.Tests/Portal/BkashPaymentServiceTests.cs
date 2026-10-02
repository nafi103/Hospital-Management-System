using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Models.Dto;
using HospitalManagementSystem.Services;
using HospitalManagementSystem.Tests.Helpers;
using Xunit;

namespace HospitalManagementSystem.Tests.Portal
{
    public class BkashPaymentServiceTests
    {
        private (BkashPaymentService Service, MockHttpMessageHandler Handler, IMemoryCache Cache) CreateService()
        {
            var handler = new MockHttpMessageHandler();
            var httpClient = handler.ToHttpClient("https://tokenized.sandbox.bka.sh/v1.2.0-beta");

            var settings = new BkashSettings
            {
                AppKey = "test_app_key",
                AppSecret = "test_app_secret",
                Username = "test_user",
                Password = "test_password",
                BaseUrl = "https://tokenized.sandbox.bka.sh/v1.2.0-beta"
            };

            var options = Options.Create(settings);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var logger = NullLogger<BkashPaymentService>.Instance;

            var service = new BkashPaymentService(httpClient, options, cache, logger);
            return (service, handler, cache);
        }

        [Fact]
        public async Task GrantTokenAsync_SuccessfulResponse_ReturnsTokenAndCachesIt()
        {
            // Arrange
            var (service, handler, _) = CreateService();

            var tokenJson = JsonSerializer.Serialize(new BkashTokenResponse
            {
                StatusCode = "0000",
                StatusMessage = "Successful",
                IdToken = "jwt_token_sample_12345",
                TokenType = "Bearer",
                ExpiresIn = 3600
            });

            handler.SetJsonResponse(tokenJson);

            // Act 1: Initial call - fetches from HTTP
            var token1 = await service.GrantTokenAsync();

            // Assert
            Assert.Equal("jwt_token_sample_12345", token1);
            Assert.Single(handler.SentRequests);

            // Act 2: Second call - should return from IMemoryCache without extra HTTP call
            var token2 = await service.GrantTokenAsync();
            Assert.Equal("jwt_token_sample_12345", token2);
            Assert.Single(handler.SentRequests); // No additional HTTP request sent!
        }

        [Fact]
        public async Task GrantTokenAsync_ApiErrorResponse_ReturnsNull()
        {
            // Arrange
            var (service, handler, _) = CreateService();
            handler.SetErrorResponse(HttpStatusCode.InternalServerError, "bKash Internal Error");

            // Act
            var token = await service.GrantTokenAsync();

            // Assert
            Assert.Null(token);
        }

        [Fact]
        public async Task GrantTokenAsync_NetworkTimeout_ReturnsNullGracefully()
        {
            // Arrange
            var (service, handler, _) = CreateService();
            handler.SetTimeout();

            // Act
            var token = await service.GrantTokenAsync();

            // Assert
            Assert.Null(token);
        }

        [Fact]
        public async Task CreatePaymentAsync_SuccessfulResponse_ReturnsBkashUrlAndPaymentId()
        {
            // Arrange
            var (service, handler, _) = CreateService();

            var tokenJson = JsonSerializer.Serialize(new BkashTokenResponse
            {
                StatusCode = "0000",
                StatusMessage = "Successful",
                IdToken = "token_xyz_999",
                ExpiresIn = 3600
            });

            var createJson = JsonSerializer.Serialize(new BkashCreatePaymentResponse
            {
                StatusCode = "0000",
                StatusMessage = "Successful",
                PaymentID = "TR0011TESTPAY",
                BkashURL = "https://sandbox.bka.sh/checkout?paymentID=TR0011TESTPAY",
                Amount = "1500.00",
                Currency = "BDT",
                TransactionStatus = "Initiated",
                MerchantInvoiceNumber = "BILL-10"
            });

            handler.SetHandler(req =>
            {
                if (req.RequestUri!.AbsolutePath.Contains("/token/grant"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(tokenJson, System.Text.Encoding.UTF8, "application/json")
                    };
                }

                if (req.RequestUri.AbsolutePath.Contains("/checkout/create"))
                {
                    // Check authorization headers
                    Assert.True(req.Headers.Contains("Authorization"));
                    Assert.True(req.Headers.Contains("X-APP-Key"));
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(createJson, System.Text.Encoding.UTF8, "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            // Act
            var result = await service.CreatePaymentAsync(
                billId: 10,
                amount: 1500.00m,
                payerReference: "PT-202610-0100",
                callbackUrl: "https://hospital.local/Bkash/Callback");

            // Assert
            Assert.NotNull(result);
            Assert.Equal("0000", result.StatusCode);
            Assert.Equal("TR0011TESTPAY", result.PaymentID);
            Assert.Equal("https://sandbox.bka.sh/checkout?paymentID=TR0011TESTPAY", result.BkashURL);
            Assert.Equal("BILL-10", result.MerchantInvoiceNumber);
        }

        [Fact]
        public async Task CreatePaymentAsync_TokenExpired_EvictsCacheAndRetriesWithNewToken()
        {
            // Arrange
            var (service, handler, cache) = CreateService();

            // Pre-seed an expired token in cache
            cache.Set("Bkash_IdToken_Cache", "expired_token_abc");

            var newTokenJson = JsonSerializer.Serialize(new BkashTokenResponse
            {
                StatusCode = "0000",
                IdToken = "fresh_token_def",
                ExpiresIn = 3600
            });

            var createJson = JsonSerializer.Serialize(new BkashCreatePaymentResponse
            {
                StatusCode = "0000",
                PaymentID = "PAY_RETRY_123",
                BkashURL = "https://sandbox.bka.sh/checkout?paymentID=PAY_RETRY_123"
            });

            int createAttempt = 0;

            handler.SetHandler(req =>
            {
                if (req.RequestUri!.AbsolutePath.Contains("/token/grant"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(newTokenJson, System.Text.Encoding.UTF8, "application/json")
                    };
                }

                if (req.RequestUri.AbsolutePath.Contains("/checkout/create"))
                {
                    createAttempt++;
                    if (createAttempt == 1)
                    {
                        // First attempt with expired token fails with 401
                        return new HttpResponseMessage(HttpStatusCode.Unauthorized)
                        {
                            Content = new StringContent("{\"message\":\"Token expired\"}", System.Text.Encoding.UTF8, "application/json")
                        };
                    }

                    // Retry attempt with fresh token succeeds
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(createJson, System.Text.Encoding.UTF8, "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            // Act
            var result = await service.CreatePaymentAsync(1, 500m, "REF", "https://call.back");

            // Assert: Service recovered from 401 and returned successful payment response
            Assert.NotNull(result);
            Assert.Equal("PAY_RETRY_123", result.PaymentID);
            Assert.Equal(2, createAttempt);
        }

        [Fact]
        public async Task ExecutePaymentAsync_Completed_ReturnsTrxId()
        {
            // Arrange
            var (service, handler, _) = CreateService();

            var tokenJson = JsonSerializer.Serialize(new BkashTokenResponse
            {
                StatusCode = "0000",
                IdToken = "token_exec_123",
                ExpiresIn = 3600
            });

            var executeJson = JsonSerializer.Serialize(new BkashExecutePaymentResponse
            {
                StatusCode = "0000",
                StatusMessage = "Successful",
                PaymentID = "PAY_EXEC_999",
                TrxID = "BKH888777666",
                TransactionStatus = "Completed",
                Amount = "1200.00",
                MerchantInvoiceNumber = "BILL-42"
            });

            handler.SetHandler(req =>
            {
                if (req.RequestUri!.AbsolutePath.Contains("/token/grant"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(tokenJson, System.Text.Encoding.UTF8, "application/json")
                    };
                }

                if (req.RequestUri.AbsolutePath.Contains("/checkout/execute"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(executeJson, System.Text.Encoding.UTF8, "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            // Act
            var response = await service.ExecutePaymentAsync("PAY_EXEC_999");

            // Assert
            Assert.NotNull(response);
            Assert.Equal("0000", response.StatusCode);
            Assert.Equal("BKH888777666", response.TrxID);
            Assert.Equal("Completed", response.TransactionStatus);
            Assert.Equal("1200.00", response.Amount);
            Assert.Equal("BILL-42", response.MerchantInvoiceNumber);
        }

        [Fact]
        public async Task ExecutePaymentAsync_Declined_ReturnsFailedStatus()
        {
            // Arrange
            var (service, handler, _) = CreateService();

            var tokenJson = JsonSerializer.Serialize(new BkashTokenResponse
            {
                StatusCode = "0000",
                IdToken = "token_exec_123"
            });

            var executeJson = JsonSerializer.Serialize(new BkashExecutePaymentResponse
            {
                StatusCode = "2023",
                StatusMessage = "Insufficient Balance",
                PaymentID = "PAY_FAILED_111",
                TransactionStatus = "Failed"
            });

            handler.SetHandler(req =>
            {
                if (req.RequestUri!.AbsolutePath.Contains("/token/grant"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(tokenJson, System.Text.Encoding.UTF8, "application/json")
                    };
                }

                if (req.RequestUri.AbsolutePath.Contains("/checkout/execute"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(executeJson, System.Text.Encoding.UTF8, "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            // Act
            var response = await service.ExecutePaymentAsync("PAY_FAILED_111");

            // Assert
            Assert.NotNull(response);
            Assert.Equal("2023", response.StatusCode);
            Assert.Equal("Failed", response.TransactionStatus);
            Assert.Null(response.TrxID);
        }
    }
}
