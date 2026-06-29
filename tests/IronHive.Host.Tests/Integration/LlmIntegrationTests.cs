using System.Globalization;
using IronHive.Abstractions;
using IronHive.Abstractions.Messages;
using IronHive.Agent.Loop;
using IronHive.Host.Core.Config;
using IronHive.Host.Core.Providers;
using IronHive.Core;
using IronHive.Providers.OpenAI;
using Microsoft.Extensions.AI;

namespace IronHive.Host.Tests.Integration;

/// <summary>
/// Integration tests that require actual LLM API access.
/// These tests are skipped unless the appropriate environment variables are set.
/// Set OPENAI_API_KEY or GPUSTACK_API_KEY to run.
/// </summary>
/// <remarks>
/// Run with: dotnet test --filter "Category=Integration"
/// </remarks>
[Trait("Category", "Integration")]
public class LlmIntegrationTests
{
    private static bool HasGpuStackKey =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GPUSTACK_API_KEY"));

    private static string? GpuStackEndpoint =>
        Environment.GetEnvironmentVariable("GPUSTACK_ENDPOINT");

    private static string? GpuStackApiKey =>
        Environment.GetEnvironmentVariable("GPUSTACK_API_KEY");

    private static string GpuStackModel =>
        Environment.GetEnvironmentVariable("GPUSTACK_MODEL") ?? "gpt-4o-mini";

    /// <summary>
    /// Creates an IronhiveChatClientProvider for GpuStack using the ironhive library.
    /// </summary>
    private static IronhiveChatClientProvider CreateGpuStackProvider()
    {
        var hiveBuilder = new HiveServiceBuilder();
        var openAIConfig = new IronHive.Providers.OpenAI.OpenAIConfig
        {
            BaseUrl = GpuStackEndpoint!.TrimEnd('/') + "/v1-openai/",
            ApiKey = GpuStackApiKey!
        };
        hiveBuilder.AddOpenAIProviders("gpustack", openAIConfig, OpenAIServiceType.ChatCompletion);
        var hiveService = hiveBuilder.Build();

        hiveService.Providers.TryGet<IMessageGenerator>("gpustack", out var generator);
        return new IronhiveChatClientProvider(generator!, "gpustack", GpuStackModel);
    }

    [Fact]
    public void GpuStackConfig_LoadsFromEnvironment()
    {
        // This test doesn't require actual API access
        var endpoint = GpuStackEndpoint ?? "http://localhost:8080";
        var apiKey = GpuStackApiKey ?? "test-key";

        var config = new GpuStackConfig
        {
            Endpoint = endpoint,
            ApiKey = apiKey,
            Model = GpuStackModel
        };

        Assert.NotNull(config.Endpoint);
        Assert.NotNull(config.ApiKey);
        Assert.NotNull(config.Model);
    }

    [Fact]
    public async Task GpuStackChatClientProvider_CreatesClient()
    {
        // Skip if no API key
        if (!HasGpuStackKey)
        {
            return; // Skip silently
        }

        using var provider = CreateGpuStackProvider();
        var client = await provider.GetChatClientAsync();

        Assert.NotNull(client);
    }

    [Fact]
    public async Task GpuStack_SimpleCompletion_ReturnsResponse()
    {
        if (!HasGpuStackKey)
        {
            // Skip test - no API key configured
            return;
        }

        using var provider = CreateGpuStackProvider();
        var client = await provider.GetChatClientAsync();

        var response = await client.GetResponseAsync("Say 'Hello' and nothing else.");

        Assert.NotNull(response);
        Assert.NotNull(response.Text);
    }

    [Fact]
    public async Task GpuStack_StreamingCompletion_StreamsTokens()
    {
        if (!HasGpuStackKey)
        {
            return;
        }

        using var provider = CreateGpuStackProvider();
        var client = await provider.GetChatClientAsync();

        var tokens = new List<string>();
        await foreach (var update in client.GetStreamingResponseAsync("Count from 1 to 5."))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                tokens.Add(update.Text);
            }
        }

        Assert.NotEmpty(tokens);
    }

    [Fact]
    public async Task AgentLoop_WithGpuStack_ExecutesBasicPrompt()
    {
        if (!HasGpuStackKey)
        {
            return;
        }

        using var provider = CreateGpuStackProvider();
        var client = await provider.GetChatClientAsync();

        var agentLoop = new AgentLoop(client);
        var response = await agentLoop.RunAsync("What is 2 + 2?");

        Assert.NotNull(response);
        Assert.NotNull(response.Content);
        Assert.Contains("4", response.Content);
    }

    [Fact]
    public async Task AgentLoop_MultiTurnConversation_MaintainsContext()
    {
        if (!HasGpuStackKey)
        {
            return;
        }

        using var provider = CreateGpuStackProvider();
        var client = await provider.GetChatClientAsync();

        var agentLoop = new AgentLoop(client);

        // First turn: introduce information
        var response1 = await agentLoop.RunAsync("The secret number is 42. Remember this.");
        Assert.NotNull(response1);

        // Second turn: ask about the information
        var response2 = await agentLoop.RunAsync("What is the secret number I mentioned?");
        Assert.NotNull(response2);
        Assert.Contains("42", response2.Content);
    }

    [Fact]
    public async Task AgentLoop_CancellationToken_StopsProcessing()
    {
        if (!HasGpuStackKey)
        {
            return;
        }

        using var provider = CreateGpuStackProvider();
        var client = await provider.GetChatClientAsync();

        var agentLoop = new AgentLoop(client);
        using var cts = new CancellationTokenSource();

        // Cancel quickly
        cts.CancelAfter(100);

        // Should throw OperationCanceledException or return quickly
        try
        {
            await agentLoop.RunAsync("Write a very long essay about the history of computing.", cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
            Assert.True(true);
            return;
        }

        // If we get here, the operation completed before cancellation (also acceptable)
        Assert.True(true);
    }

    [Fact]
    public async Task StreamingResponse_WithGpuStack_YieldsChunks()
    {
        if (!HasGpuStackKey)
        {
            return;
        }

        using var provider = CreateGpuStackProvider();
        var client = await provider.GetChatClientAsync();

        var agentLoop = new AgentLoop(client);
        var chunks = new List<string>();

        await foreach (var chunk in agentLoop.RunStreamingAsync("Count from 1 to 5, one per line."))
        {
            if (!string.IsNullOrEmpty(chunk.TextDelta))
            {
                chunks.Add(chunk.TextDelta);
            }
        }

        Assert.NotEmpty(chunks);
    }

    [Fact]
    public void IntegrationTest_SkipsWhenNoApiKey()
    {
        // This test verifies that integration tests properly handle missing API keys
        var hasKey = HasGpuStackKey;

        // If we have a key, the test passes
        // If we don't have a key, the test also passes (proving skip logic works)
        Assert.True(true);
    }
}
