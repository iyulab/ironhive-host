using IronHive.Cli.Core.Agent;
using IronHive.Cli.Core.Extensions;
using IronHive.Cli.Core.Session;
using IronHive.Cli.Tests.Mocks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace IronHive.Cli.Tests.Extensions;

/// <summary>
/// Tests for IronHiveServiceCollectionExtensions.
/// Validates provider helper methods and DI registration.
/// </summary>
public class IronHiveServiceCollectionExtensionsTests
{
    [Fact]
    public void AddIronHive_WithChatClient_RegistersServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockClient = new MockChatClient();

        // Act
        services.AddIronHive(mockClient, "Test system prompt");
        var provider = services.BuildServiceProvider();

        // Assert
        var agentLoop = provider.GetService<IAgentLoop>();
        var sessionManager = provider.GetService<ISessionManager>();

        Assert.NotNull(agentLoop);
        Assert.NotNull(sessionManager);
    }

    [Fact]
    public void AddIronHive_WithOptions_ConfiguresCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockClient = new MockChatClient();

        // Act
        services.AddIronHive(options =>
        {
            options.UseChatClient(mockClient);
            options.SystemPrompt = "Custom system prompt";
            options.DefaultModel = "test-model";
            options.MaxTokens = 1000;
            options.Temperature = 0.7f;
        });

        var provider = services.BuildServiceProvider();

        // Assert
        var agentOptions = provider.GetService<AgentOptions>();
        Assert.NotNull(agentOptions);
        Assert.Equal("Custom system prompt", agentOptions.SystemPrompt);
        Assert.Equal("test-model", agentOptions.ModelId);
        Assert.Equal(1000, agentOptions.MaxTokens);
        Assert.Equal(0.7f, agentOptions.Temperature);
    }

    [Fact]
    public void AddIronHive_WithoutChatClient_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            services.AddIronHive(options =>
            {
                // Not setting chat client
                options.SystemPrompt = "Test";
            });
        });

        Assert.Contains("ChatClient", exception.Message);
    }

    [Fact]
    public void AddIronHiveWithOpenAI_ThrowsOnNullApiKey()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert - ArgumentNullException is a subclass of ArgumentException
        Assert.ThrowsAny<ArgumentException>(() =>
        {
            services.AddIronHiveWithOpenAI(null!);
        });
    }

    [Fact]
    public void AddIronHiveWithOpenAI_ThrowsOnEmptyApiKey()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(() =>
        {
            services.AddIronHiveWithOpenAI("");
        });
    }

    [Fact]
    public void AddIronHiveWithOpenAICompatible_ThrowsOnNullEndpoint()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(() =>
        {
            services.AddIronHiveWithOpenAICompatible(null!, "key", "model");
        });
    }

    [Fact]
    public void AddIronHiveWithOpenAICompatible_ThrowsOnNullModel()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(() =>
        {
            services.AddIronHiveWithOpenAICompatible("http://localhost", "key", null!);
        });
    }

    [Fact]
    public void AddIronHiveWithOllama_UsesDefaultValues()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act - Should not throw (defaults are provided)
        // Note: This will create a client pointing to localhost:11434
        // which may not be running, but that's OK for registration test
        services.AddIronHiveWithOllama();

        // Assert - Services should be registered
        var provider = services.BuildServiceProvider();
        var agentLoop = provider.GetService<IAgentLoop>();
        Assert.NotNull(agentLoop);
    }

    [Fact]
    public void AddIronHive_AgentLoopHasSystemPromptInHistory()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockClient = new MockChatClient();
        mockClient.EnqueueResponse("Hello!");

        services.AddIronHive(mockClient, "You are a helpful assistant.");
        var provider = services.BuildServiceProvider();

        // Act
        var agentLoop = provider.GetRequiredService<IAgentLoop>();

        // Assert - History should contain system prompt
        Assert.Single(agentLoop.History);
        Assert.Equal(ChatRole.System, agentLoop.History[0].Role);
        Assert.Contains("helpful assistant", agentLoop.History[0].Text);
    }

    [Fact]
    public async Task AddIronHive_AgentLoopCanProcessPrompt()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockClient = new MockChatClient();
        mockClient.EnqueueResponse("Hello! How can I help you?");

        services.AddIronHive(mockClient, "You are a helpful assistant.");
        var provider = services.BuildServiceProvider();
        var agentLoop = provider.GetRequiredService<IAgentLoop>();

        // Act
        var response = await agentLoop.RunAsync("Hi there");

        // Assert
        Assert.Equal("Hello! How can I help you?", response.Content);
        Assert.Equal(3, agentLoop.History.Count); // System + User + Assistant
    }
}
