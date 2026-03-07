using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using IronHive.Abstractions.Messages;
using IronHive.Abstractions.Messages.Content;
using IronHive.Abstractions.Messages.Roles;
using IronHive.Abstractions.Tools;
using IronHive.Core.Tools;
using Microsoft.Extensions.AI;

namespace IronHive.Cli.Core.Adapters;

/// <summary>
/// ironhive의 IMessageGenerator를 M.E.AI IChatClient로 변환하는 어댑터입니다.
/// </summary>
public class MessageGeneratorChatClientAdapter : IChatClient
{
    private readonly IMessageGenerator _generator;
    private readonly string _modelId;

    public MessageGeneratorChatClientAdapter(IMessageGenerator generator, string modelId)
    {
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _modelId = modelId ?? throw new ArgumentNullException(nameof(modelId));
    }

    /// <inheritdoc />
    public ChatClientMetadata Metadata => new("IronHive", null, _modelId);

    /// <inheritdoc />
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var request = ConvertToRequest(messages, options);
        var response = await _generator.GenerateMessageAsync(request, cancellationToken);
        return ConvertToResponse(response);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = ConvertToRequest(messages, options);
        string? responseId = null;

        // Buffer tool calls: index → (callId, name, argumentsJson)
        // M.E.AI expects complete FunctionCallContent, not partial argument chunks.
        var toolCallBuffers = new Dictionary<int, (string CallId, string Name, StringBuilder Arguments)>();

        await foreach (var chunk in _generator.GenerateStreamingMessageAsync(request, cancellationToken))
        {
            switch (chunk)
            {
                case StreamingContentAddedResponse added when added.Content is ToolMessageContent tool:
                    // Start buffering a new tool call
                    toolCallBuffers[added.Index] = (
                        tool.Id ?? Guid.NewGuid().ToString(),
                        tool.Name ?? string.Empty,
                        new StringBuilder());
                    break;

                case StreamingContentDeltaResponse delta when delta.Delta is ToolDeltaContent toolDelta:
                    // Accumulate argument chunks
                    if (toolCallBuffers.TryGetValue(delta.Index, out var buffer))
                    {
                        buffer.Arguments.Append(toolDelta.Input);
                    }
                    break;

                case StreamingContentCompletedResponse completed:
                    // Emit the complete FunctionCallContent
                    if (toolCallBuffers.TryGetValue(completed.Index, out var completedTool))
                    {
                        var argsJson = completedTool.Arguments.ToString();

                        // Parse JSON args, converting JsonElement values to CLR types
                        // so FunctionInvokingChatClient can match them to method parameters.
                        Dictionary<string, object?>? arguments = null;
                        if (!string.IsNullOrEmpty(argsJson))
                        {
                            var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argsJson);
                            if (raw is not null)
                            {
                                arguments = new Dictionary<string, object?>();
                                foreach (var kvp in raw)
                                {
                                    arguments[kvp.Key] = ConvertJsonElement(kvp.Value);
                                }
                            }
                        }

                        yield return new ChatResponseUpdate
                        {
                            ResponseId = responseId,
                            Contents = [new FunctionCallContent(
                                callId: completedTool.CallId,
                                name: completedTool.Name,
                                arguments: arguments)]
                        };
                        toolCallBuffers.Remove(completed.Index);
                    }
                    break;

                default:
                    // Non-tool updates pass through normally
                    var update = ConvertToUpdate(chunk, ref responseId);
                    if (update is not null)
                    {
                        yield return update;
                    }
                    break;
            }
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(IMessageGenerator))
        {
            return _generator;
        }
        return null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    private MessageGenerationRequest ConvertToRequest(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options)
    {
        var request = new MessageGenerationRequest
        {
            Model = options?.ModelId ?? _modelId,
            Messages = []
        };

        // Collect tool results keyed by callId for merging into assistant messages
        var toolResults = new Dictionary<string, string>();
        foreach (var msg in chatMessages)
        {
            foreach (var content in msg.Contents)
            {
                if (content is FunctionResultContent result && result.CallId is not null)
                {
                    toolResults[result.CallId] = result.Result?.ToString() ?? string.Empty;
                }
            }
        }

        foreach (var msg in chatMessages)
        {
            if (msg.Role == ChatRole.System)
            {
                request.System = msg.Text;
            }
            else
            {
                var converted = ConvertMessage(msg, toolResults);
                if (converted is not null)
                {
                    request.Messages.Add(converted);
                }
            }
        }

        if (options is not null)
        {
            request.Temperature = options.Temperature;
            request.MaxTokens = options.MaxOutputTokens;
            request.TopP = options.TopP;
            request.StopSequences = options.StopSequences?.ToList();

            if (options.Tools is { Count: > 0 })
            {
                var adapted = options.Tools.Select(t => (ITool)new AIToolAdapter(t));
                request.Tools = new ToolCollection(adapted);
            }
        }

        return request;
    }

    private static Message? ConvertMessage(ChatMessage chatMessage, Dictionary<string, string> toolResults)
    {
        if (chatMessage.Role == ChatRole.User)
        {
            var userMessage = new UserMessage();

            foreach (var content in chatMessage.Contents)
            {
                if (content is TextContent textContent)
                {
                    userMessage.Content.Add(new TextMessageContent
                    {
                        Value = textContent.Text ?? string.Empty
                    });
                }
                // FunctionResultContent is handled via toolResults merge — skip here
            }

            if (userMessage.Content.Count == 0 && !string.IsNullOrEmpty(chatMessage.Text))
            {
                userMessage.Content.Add(new TextMessageContent
                {
                    Value = chatMessage.Text
                });
            }

            return userMessage.Content.Count > 0 ? userMessage : null;
        }
        else if (chatMessage.Role == ChatRole.Assistant)
        {
            var assistantMessage = new AssistantMessage();

            foreach (var content in chatMessage.Contents)
            {
                switch (content)
                {
                    case TextContent textContent:
                        assistantMessage.Content.Add(new TextMessageContent
                        {
                            Value = textContent.Text ?? string.Empty
                        });
                        break;

                    case FunctionCallContent functionCall:
                        var callId = functionCall.CallId ?? Guid.NewGuid().ToString();
                        var toolMsg = new ToolMessageContent
                        {
                            Id = callId,
                            Name = functionCall.Name,
                            Input = functionCall.Arguments is not null
                                ? JsonSerializer.Serialize(functionCall.Arguments)
                                : "{}",
                            IsApproved = true
                        };

                        // Merge tool result if available
                        if (toolResults.TryGetValue(callId, out var result))
                        {
                            toolMsg.Output = new ToolOutput(true, result);
                        }

                        assistantMessage.Content.Add(toolMsg);
                        break;
                }
            }

            if (assistantMessage.Content.Count == 0 && !string.IsNullOrEmpty(chatMessage.Text))
            {
                assistantMessage.Content.Add(new TextMessageContent
                {
                    Value = chatMessage.Text
                });
            }

            return assistantMessage;
        }

        // Skip ChatRole.Tool messages — results are merged into assistant messages above
        return null;
    }

    private ChatResponse ConvertToResponse(MessageResponse response)
    {
        var chatMessage = new ChatMessage(ChatRole.Assistant, []);

        foreach (var content in response.Message.Content)
        {
            switch (content)
            {
                case TextMessageContent textContent:
                    chatMessage.Contents.Add(new TextContent(textContent.Value));
                    break;

                case ToolMessageContent toolContent:
                    var arguments = !string.IsNullOrEmpty(toolContent.Input)
                        ? JsonSerializer.Deserialize<Dictionary<string, object?>>(toolContent.Input)
                        : null;

                    chatMessage.Contents.Add(new FunctionCallContent(
                        callId: toolContent.Id,
                        name: toolContent.Name,
                        arguments: arguments));
                    break;
            }
        }

        var finishReason = response.DoneReason switch
        {
            MessageDoneReason.EndTurn => ChatFinishReason.Stop,
            MessageDoneReason.MaxTokens => ChatFinishReason.Length,
            MessageDoneReason.ToolCall => ChatFinishReason.ToolCalls,
            _ => ChatFinishReason.Stop
        };

        UsageDetails? usage = null;
        if (response.TokenUsage is not null)
        {
            usage = new UsageDetails
            {
                InputTokenCount = response.TokenUsage.InputTokens,
                OutputTokenCount = response.TokenUsage.OutputTokens,
                TotalTokenCount = response.TokenUsage.TotalTokens
            };
        }

        return new ChatResponse(chatMessage)
        {
            ResponseId = response.Id,
            ModelId = response.Message.Model ?? _modelId,
            FinishReason = finishReason,
            Usage = usage,
            CreatedAt = response.Timestamp
        };
    }

    private static ChatResponseUpdate? ConvertToUpdate(
        StreamingMessageResponse chunk,
        ref string? responseId)
    {
        switch (chunk)
        {
            case StreamingMessageBeginResponse begin:
                responseId = begin.Id;
                return new ChatResponseUpdate
                {
                    ResponseId = begin.Id,
                    Role = ChatRole.Assistant
                };

            case StreamingContentDeltaResponse delta when delta.Delta is TextDeltaContent textDelta:
                return new ChatResponseUpdate
                {
                    ResponseId = responseId,
                    Contents = [new TextContent(textDelta.Value)]
                };

            case StreamingMessageDoneResponse done:
                var finishReason = done.DoneReason switch
                {
                    MessageDoneReason.EndTurn => ChatFinishReason.Stop,
                    MessageDoneReason.MaxTokens => ChatFinishReason.Length,
                    MessageDoneReason.ToolCall => ChatFinishReason.ToolCalls,
                    _ => ChatFinishReason.Stop
                };

                return new ChatResponseUpdate
                {
                    ResponseId = done.Id,
                    ModelId = done.Model,
                    FinishReason = finishReason,
                    CreatedAt = done.Timestamp
                };

            case StreamingMessageErrorResponse error:
                throw new InvalidOperationException(
                    $"Streaming error: {error.Code} - {error.Message}");

            default:
                return null;
        }
    }

    private static object? ConvertJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => element
    };
}
