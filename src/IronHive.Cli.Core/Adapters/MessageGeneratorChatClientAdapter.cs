using System.Runtime.CompilerServices;
using System.Text.Json;
using IronHive.Abstractions.Messages;
using IronHive.Abstractions.Messages.Content;
using IronHive.Abstractions.Messages.Roles;
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

        await foreach (var chunk in _generator.GenerateStreamingMessageAsync(request, cancellationToken))
        {
            var update = ConvertToUpdate(chunk, ref responseId);
            if (update is not null)
            {
                yield return update;
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

        foreach (var msg in chatMessages)
        {
            if (msg.Role == ChatRole.System)
            {
                request.SystemPrompt = msg.Text;
            }
            else
            {
                var converted = ConvertMessage(msg);
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
        }

        return request;
    }

    private static Message? ConvertMessage(ChatMessage chatMessage)
    {
        if (chatMessage.Role == ChatRole.User)
        {
            var userMessage = new UserMessage();

            foreach (var content in chatMessage.Contents)
            {
                switch (content)
                {
                    case TextContent textContent:
                        userMessage.Content.Add(new TextMessageContent
                        {
                            Value = textContent.Text ?? string.Empty
                        });
                        break;

                    case FunctionResultContent functionResult:
                        // Tool result 처리는 별도로 필요
                        break;
                }
            }

            if (userMessage.Content.Count == 0 && !string.IsNullOrEmpty(chatMessage.Text))
            {
                userMessage.Content.Add(new TextMessageContent
                {
                    Value = chatMessage.Text
                });
            }

            return userMessage;
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
                        assistantMessage.Content.Add(new ToolMessageContent
                        {
                            Id = functionCall.CallId ?? Guid.NewGuid().ToString(),
                            Name = functionCall.Name,
                            Input = functionCall.Arguments is not null
                                ? JsonSerializer.Serialize(functionCall.Arguments)
                                : null,
                            IsApproved = true
                        });
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

            case StreamingContentDeltaResponse delta:
                return delta.Delta switch
                {
                    TextDeltaContent textDelta => new ChatResponseUpdate
                    {
                        ResponseId = responseId,
                        Contents = [new TextContent(textDelta.Value)]
                    },

                    ToolDeltaContent toolDelta => new ChatResponseUpdate
                    {
                        ResponseId = responseId,
                        RawRepresentation = toolDelta
                    },

                    _ => null
                };

            case StreamingContentAddedResponse added:
                return added.Content switch
                {
                    ToolMessageContent toolContent => new ChatResponseUpdate
                    {
                        ResponseId = responseId,
                        Contents = [new FunctionCallContent(
                            callId: toolContent.Id,
                            name: toolContent.Name,
                            arguments: null)]
                    },

                    _ => null
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
}
