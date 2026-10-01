using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HospitalManagementSystem.Services;
using HospitalManagementSystem.Services.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HospitalManagementSystem.Tests;

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
}
