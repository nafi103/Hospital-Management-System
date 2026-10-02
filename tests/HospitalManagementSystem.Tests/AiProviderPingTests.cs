using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HospitalManagementSystem.Controllers;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Services;
using HospitalManagementSystem.Services.Providers;
using HospitalManagementSystem.Tests.Fixtures;
using HospitalManagementSystem.Tests.Helpers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HospitalManagementSystem.Tests
{
    public class AiProviderPingTests
    {
        [Theory]
        [InlineData("", "llama-3.3-70b-versatile", "API key is required.")]
        [InlineData("   ", "llama-3.3-70b-versatile", "API key is required.")]
        [InlineData("gsk_test123", "", "Model ID is required.")]
        [InlineData("gsk_test123", "   ", "Model ID is required.")]
        public async Task GroqProvider_PingAsync_ValidatesEmptyParameters(string apiKey, string modelId, string expectedError)
        {
            using var http = new HttpClient();
            var client = new GroqClient(http);
            var provider = new GroqTextProvider(client, NullLogger<GroqTextProvider>.Instance);
            var (success, error) = await provider.PingAsync(apiKey, modelId, CancellationToken.None);

            Assert.False(success);
            Assert.Equal(expectedError, error);
        }

        [Theory]
        [InlineData("", "gemini-2.5-flash", "API key is required.")]
        [InlineData("   ", "gemini-2.5-flash", "API key is required.")]
        [InlineData("AIzaSyTest", "", "Model ID is required.")]
        [InlineData("AIzaSyTest", "   ", "Model ID is required.")]
        public async Task GeminiProvider_PingAsync_ValidatesEmptyParameters(string apiKey, string modelId, string expectedError)
        {
            var provider = new GeminiTextProvider(NullLogger<GeminiTextProvider>.Instance);
            var (success, error) = await provider.PingAsync(apiKey, modelId, CancellationToken.None);

            Assert.False(success);
            Assert.Equal(expectedError, error);
        }

        [Theory]
        [InlineData("", "claude-3-5-sonnet-20241022", "API key is required.")]
        [InlineData("   ", "claude-3-5-sonnet-20241022", "API key is required.")]
        [InlineData("sk-ant-test", "", "Model ID is required.")]
        [InlineData("sk-ant-test", "   ", "Model ID is required.")]
        public async Task AnthropicProvider_PingAsync_ValidatesEmptyParameters(string apiKey, string modelId, string expectedError)
        {
            var provider = new AnthropicTextProvider(NullLogger<AnthropicTextProvider>.Instance);
            var (success, error) = await provider.PingAsync(apiKey, modelId, CancellationToken.None);

            Assert.False(success);
            Assert.Equal(expectedError, error);
        }

        [Fact]
        public async Task GroqClient_PingAsync_Success200_ReturnsSuccess()
        {
            var mockHandler = new MockHttpMessageHandler();
            mockHandler.SetJsonResponse("{\"choices\":[{\"message\":{\"content\":\"pong\"}}]}", HttpStatusCode.OK);

            using var httpClient = mockHandler.ToHttpClient();
            var client = new GroqClient(httpClient);

            var (success, error) = await client.PingAsync("gsk_test_key", "llama-3.3-70b-versatile", CancellationToken.None);

            Assert.True(success);
            Assert.Null(error);
            Assert.Single(mockHandler.SentRequests);
            Assert.Equal("Bearer", mockHandler.SentRequests[0].Headers.Authorization?.Scheme);
            Assert.Equal("gsk_test_key", mockHandler.SentRequests[0].Headers.Authorization?.Parameter);
        }

        [Fact]
        public async Task GroqClient_PingAsync_RateLimit429_ReturnsFailure()
        {
            var mockHandler = new MockHttpMessageHandler();
            mockHandler.SetErrorResponse(HttpStatusCode.TooManyRequests, "Rate limit exceeded. Please wait 10s.");

            using var httpClient = mockHandler.ToHttpClient();
            var client = new GroqClient(httpClient);

            var (success, error) = await client.PingAsync("gsk_test_key", "llama-3.3-70b-versatile", CancellationToken.None);

            Assert.False(success);
            Assert.NotNull(error);
            Assert.Contains("TooManyRequests", error);
            Assert.Contains("Rate limit exceeded", error);
        }

        [Fact]
        public async Task GroqClient_PingAsync_Unauthorized401_ReturnsFailure()
        {
            var mockHandler = new MockHttpMessageHandler();
            mockHandler.SetErrorResponse(HttpStatusCode.Unauthorized, "Invalid API Key provided.");

            using var httpClient = mockHandler.ToHttpClient();
            var client = new GroqClient(httpClient);

            var (success, error) = await client.PingAsync("bad_key", "llama-3.3-70b-versatile", CancellationToken.None);

            Assert.False(success);
            Assert.NotNull(error);
            Assert.Contains("Unauthorized", error);
            Assert.Contains("Invalid API Key", error);
        }

        [Fact]
        public async Task GroqClient_PingAsync_ServerError500_ReturnsFailure()
        {
            var mockHandler = new MockHttpMessageHandler();
            mockHandler.SetErrorResponse(HttpStatusCode.InternalServerError, "Service Unavailable / Internal Server Error");

            using var httpClient = mockHandler.ToHttpClient();
            var client = new GroqClient(httpClient);

            var (success, error) = await client.PingAsync("gsk_test_key", "llama-3.3-70b-versatile", CancellationToken.None);

            Assert.False(success);
            Assert.NotNull(error);
            Assert.Contains("InternalServerError", error);
        }

        [Fact]
        public async Task GroqClient_PingAsync_Timeout_ReturnsFailure()
        {
            var mockHandler = new MockHttpMessageHandler();
            mockHandler.SetTimeout();

            using var httpClient = mockHandler.ToHttpClient();
            var client = new GroqClient(httpClient);

            var (success, error) = await client.PingAsync("gsk_test_key", "llama-3.3-70b-versatile", CancellationToken.None);

            Assert.False(success);
            Assert.Equal("Request timed out.", error);
        }

        [Fact]
        public async Task GroqClient_PingAsync_NetworkFailure_ReturnsFailure()
        {
            var mockHandler = new MockHttpMessageHandler();
            mockHandler.SetNetworkFailure("Connection reset by peer");

            using var httpClient = mockHandler.ToHttpClient();
            var client = new GroqClient(httpClient);

            var (success, error) = await client.PingAsync("gsk_test_key", "llama-3.3-70b-versatile", CancellationToken.None);

            Assert.False(success);
            Assert.Contains("Connection reset by peer", error);
        }

        [Fact]
        public async Task AiProviderSettingsController_TestConnection_PingSucceeds_SetsSuccessTempData()
        {
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var protector = new ApiKeyProtector(new EphemeralDataProtectionProvider());

            var provider = new TestAiTextProvider
            {
                Type = AiProviderType.Groq,
                PingFunc = (key, model, ct) => Task.FromResult((true, (string?)null))
            };

            var controller = new AiProviderSettingsController(context, protector, new[] { provider });
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateAdmin(adminId: 1));

            var result = await controller.Test(AiProviderType.Groq, "gsk_test_key", "llama-3.3-70b-versatile");

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(AiProviderSettingsController.Index), redirect.ActionName);
            Assert.Contains("verified successfully", controller.TempData["SuccessMessage"]?.ToString());
        }

        [Fact]
        public async Task AiProviderSettingsController_TestConnection_PingFails_SetsErrorTempDataWithoutCrashing()
        {
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var protector = new ApiKeyProtector(new EphemeralDataProtectionProvider());

            var provider = new TestAiTextProvider
            {
                Type = AiProviderType.Groq,
                PingFunc = (key, model, ct) => Task.FromResult((false, (string?)"Groq request failed (429): Rate limit reached"))
            };

            var controller = new AiProviderSettingsController(context, protector, new[] { provider });
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateAdmin(adminId: 1));

            var result = await controller.Test(AiProviderType.Groq, "gsk_test_key", "llama-3.3-70b-versatile");

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(AiProviderSettingsController.Index), redirect.ActionName);
            Assert.Contains("Connectivity test failed", controller.TempData["ErrorMessage"]?.ToString());
            Assert.Contains("429", controller.TempData["ErrorMessage"]?.ToString());
        }

        [Fact]
        public async Task AiProviderSettingsController_TestConnection_MissingApiKey_SetsErrorTempData()
        {
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();
            var protector = new ApiKeyProtector(new EphemeralDataProtectionProvider());

            var provider = new TestAiTextProvider { Type = AiProviderType.Groq };

            var controller = new AiProviderSettingsController(context, protector, new[] { provider });
            ControllerTestHelper.SetupController(controller, TestPrincipalFactory.CreateAdmin(adminId: 1));

            var result = await controller.Test(AiProviderType.Groq, null, "llama-3.3-70b-versatile");

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(AiProviderSettingsController.Index), redirect.ActionName);
            Assert.Contains("no API key provided", controller.TempData["ErrorMessage"]?.ToString());
        }
    }
}
