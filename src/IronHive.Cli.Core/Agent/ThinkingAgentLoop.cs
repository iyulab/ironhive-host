using System.Runtime.CompilerServices;
using IndexThinking.Agents;
using IndexThinking.Client;
using IndexThinking.Core;
using Microsoft.Extensions.AI;

namespace IronHive.Cli.Core.Agent;

/// <summary>
/// Agent loop with IndexThinking integration for token management and reasoning extraction.
/// </summary>
public class ThinkingAgentLoop : IAgentLoop, IAsyncDisposable
{
    private readonly ThinkingChatClient _thinkingClient;
    private readonly AgentOptions _options;
    private readonly List<ChatMessage> _history = [];

    public ThinkingAgentLoop(
        IChatClient chatClient,
        IThinkingTurnManager turnManager,
        AgentOptions? options = null,
        ThinkingChatClientOptions? thinkingOptions = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(turnManager);

        _thinkingClient = new ThinkingChatClient(
            chatClient,
            turnManager,
            thinkingOptions ?? new ThinkingChatClientOptions());

        _options = options ?? new AgentOptions();

        if (!string.IsNullOrWhiteSpace(_options.SystemPrompt))
        {
            _history.Add(new ChatMessage(ChatRole.System, _options.SystemPrompt));
        }
    }

    /// <inheritdoc />
    public async Task<AgentResponse> RunAsync(string prompt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        _history.Add(new ChatMessage(ChatRole.User, prompt));

        var chatOptions = CreateChatOptions();
        var response = await _thinkingClient.GetResponseAsync(_history, chatOptions, cancellationToken);

        // Add assistant response to history
        _history.AddRange(response.Messages);

        var toolCalls = ExtractToolCalls(response);
        var thinkingContent = ExtractThinkingContent(response);

        return new AgentResponse
        {
            Content = response.Text ?? string.Empty,
            ToolCalls = toolCalls,
            Usage = MapUsage(response.Usage),
            ThinkingContent = thinkingContent
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentResponseChunk> RunStreamingAsync(
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        _history.Add(new ChatMessage(ChatRole.User, prompt));

        var chatOptions = CreateChatOptions();

        await foreach (var update in _thinkingClient.GetStreamingResponseAsync(_history, chatOptions, cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return new AgentResponseChunk
                {
                    TextDelta = update.Text
                };
            }

            if (update.Contents.OfType<FunctionCallContent>().Any())
            {
                foreach (var functionCall in update.Contents.OfType<FunctionCallContent>())
                {
                    yield return new AgentResponseChunk
                    {
                        ToolCallDelta = new ToolCallChunk
                        {
                            Id = functionCall.CallId,
                            NameDelta = functionCall.Name,
                            ArgumentsDelta = functionCall.Arguments?.ToString()
                        }
                    };
                }
            }
        }
    }

    private ChatOptions CreateChatOptions()
    {
        return new ChatOptions
        {
            Temperature = _options.Temperature,
            MaxOutputTokens = _options.MaxTokens,
            Tools = _options.Tools
        };
    }

    private static List<ToolCallResult> ExtractToolCalls(ChatResponse response)
    {
        var results = new List<ToolCallResult>();

        foreach (var message in response.Messages)
        {
            foreach (var content in message.Contents.OfType<FunctionCallContent>())
            {
                results.Add(new ToolCallResult
                {
                    ToolName = content.Name,
                    Arguments = content.Arguments?.ToString() ?? "{}",
                    Result = string.Empty,
                    Success = true
                });
            }
        }

        return results;
    }

    private static ThinkingContent? ExtractThinkingContent(ChatResponse response)
    {
        if (response.AdditionalProperties?.TryGetValue(
            ThinkingChatClient.ThinkingContentKey, out var value) == true &&
            value is IndexThinking.Core.ThinkingContent thinking)
        {
            return new ThinkingContent
            {
                Content = thinking.Text,
                TokenCount = thinking.TokenCount
            };
        }

        return null;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _thinkingClient.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private static TokenUsage? MapUsage(UsageDetails? usage)
    {
        if (usage is null)
        {
            return null;
        }

        return new TokenUsage
        {
            InputTokens = usage.InputTokenCount ?? 0,
            OutputTokens = usage.OutputTokenCount ?? 0
        };
    }

    /// <summary>
    /// Clears the conversation history.
    /// </summary>
    public void ClearHistory()
    {
        _history.Clear();

        if (!string.IsNullOrWhiteSpace(_options.SystemPrompt))
        {
            _history.Add(new ChatMessage(ChatRole.System, _options.SystemPrompt));
        }
    }

    /// <summary>
    /// Gets the current conversation history.
    /// </summary>
    public IReadOnlyList<ChatMessage> History => _history.AsReadOnly();
}
