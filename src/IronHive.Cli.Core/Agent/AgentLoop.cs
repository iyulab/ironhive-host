using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace IronHive.Cli.Core.Agent;

/// <summary>
/// Default implementation of the agent loop using Microsoft.Extensions.AI.
/// Implements "nO" style single-threaded master loop pattern.
/// </summary>
public class AgentLoop : IAgentLoop
{
    private readonly IChatClient _chatClient;
    private readonly AgentOptions _options;
    private readonly List<ChatMessage> _history = [];

    public AgentLoop(IChatClient chatClient, AgentOptions? options = null)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
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
        var response = await _chatClient.GetResponseAsync(_history, chatOptions, cancellationToken);

        // Add assistant response to history
        _history.AddRange(response.Messages);

        var toolCalls = ExtractToolCalls(response);

        return new AgentResponse
        {
            Content = response.Text ?? string.Empty,
            ToolCalls = toolCalls,
            Usage = MapUsage(response.Usage)
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
        var responseBuilder = new StringBuilder();
        var toolCalls = new List<FunctionCallContent>();

        await foreach (var update in _chatClient.GetStreamingResponseAsync(_history, chatOptions, cancellationToken))
        {
            // Yield and collect text content
            if (!string.IsNullOrEmpty(update.Text))
            {
                responseBuilder.Append(update.Text);
                yield return new AgentResponseChunk
                {
                    TextDelta = update.Text
                };
            }

            // Yield and collect tool call updates
            if (update.Contents.OfType<FunctionCallContent>().Any())
            {
                foreach (var functionCall in update.Contents.OfType<FunctionCallContent>())
                {
                    toolCalls.Add(functionCall);
                    yield return new AgentResponseChunk
                    {
                        ToolCallDelta = new ToolCallChunk
                        {
                            Id = functionCall.CallId,
                            NameDelta = functionCall.Name,
                            ArgumentsDelta = functionCall.Arguments is not null
                                ? JsonSerializer.Serialize(functionCall.Arguments)
                                : null
                        }
                    };
                }
            }
        }

        // Add complete assistant response to history for multi-turn conversations
        var assistantMessage = new ChatMessage(ChatRole.Assistant, responseBuilder.ToString());
        if (toolCalls.Count > 0)
        {
            foreach (var toolCall in toolCalls)
            {
                assistantMessage.Contents.Add(toolCall);
            }
        }
        _history.Add(assistantMessage);
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
                    Arguments = content.Arguments is not null
                        ? JsonSerializer.Serialize(content.Arguments)
                        : "{}",
                    Result = string.Empty, // Will be filled after execution
                    Success = true
                });
            }
        }

        return results;
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

/// <summary>
/// Options for configuring the agent loop.
/// </summary>
public class AgentOptions
{
    /// <summary>
    /// System prompt to initialize the agent.
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Temperature for response generation.
    /// </summary>
    public float? Temperature { get; set; }

    /// <summary>
    /// Maximum tokens for response generation.
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    /// Available tools for the agent.
    /// </summary>
    public IList<AITool>? Tools { get; set; }
}
