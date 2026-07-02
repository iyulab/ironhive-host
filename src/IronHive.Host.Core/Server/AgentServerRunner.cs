using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;
using IronHive.Host.Protocol;
using Microsoft.Extensions.Logging;

namespace IronHive.Host.Core.Server;

/// <summary>
/// Reads JSON Lines from stdin, dispatches to an agent processing delegate, and writes
/// server-sent events as JSON Lines to stdout.
/// </summary>
/// <remarks>
/// A background task continuously reads stdin into a bounded channel, allowing
/// <see cref="CancelRequest"/> messages to be received and acted upon while a
/// <see cref="UserMessageRequest"/> is still being processed.
/// </remarks>
public partial class AgentServerRunner
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Agent processing error")]
    private partial void LogAgentProcessingError(Exception ex);

    internal static readonly JsonSerializerOptions DefaultJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly Func<UserMessageRequest, CancellationToken, IAsyncEnumerable<ServerEvent>> _processMessage;
    private readonly ILogger<AgentServerRunner> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    private string? _workingPath;

    /// <summary>
    /// Optional callback invoked when a context update is received.
    /// </summary>
    public Action<ContextUpdateRequest>? OnContextUpdate { get; set; }

    /// <summary>
    /// When true, <see cref="BuildContextualContent"/> becomes a pass-through.
    /// Set this when an external orchestrator handles context injection itself.
    /// </summary>
    public bool SkipContextEnrichment { get; set; }

    /// <param name="processMessage">Processes a user message and yields server events.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="jsonOptions">Custom JSON options. Defaults to snake_case.</param>
    /// <param name="typeInfoModifiers">
    /// Optional modifiers applied to <see cref="System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver"/>
    /// to extend or override polymorphic type registrations (e.g. adding custom <see cref="ServerRequest"/> or
    /// <see cref="ServerEvent"/> derived types). Each modifier is appended to the resolver's modifier chain.
    /// When provided, a new <see cref="JsonSerializerOptions"/> is created from <paramref name="jsonOptions"/>
    /// with the modifiers applied — the original options object is not mutated.
    /// </param>
    public AgentServerRunner(
        Func<UserMessageRequest, CancellationToken, IAsyncEnumerable<ServerEvent>> processMessage,
        ILogger<AgentServerRunner> logger,
        JsonSerializerOptions? jsonOptions = null,
        Action<JsonTypeInfo>[]? typeInfoModifiers = null)
    {
        _processMessage = processMessage;
        _logger = logger;
        _jsonOptions = ApplyModifiers(jsonOptions ?? DefaultJsonOpts, typeInfoModifiers);
    }

    internal static JsonSerializerOptions ApplyModifiers(
        JsonSerializerOptions baseOptions,
        Action<JsonTypeInfo>[]? modifiers)
    {
        if (modifiers is null or { Length: 0 })
        {
            return baseOptions;
        }

        var opts = new JsonSerializerOptions(baseOptions);
        var resolver = new DefaultJsonTypeInfoResolver();
        foreach (var modifier in modifiers)
        {
            resolver.Modifiers.Add(modifier);
        }

        opts.TypeInfoResolver = opts.TypeInfoResolver is null
            ? resolver
            : JsonTypeInfoResolver.Combine(opts.TypeInfoResolver, resolver);

        return opts;
    }

    /// <summary>
    /// Runs the server loop using Console.In/Out.
    /// </summary>
    public Task RunAsync(CancellationToken ct = default)
        => RunAsync(Console.In, Console.Out, ct);

    /// <summary>
    /// Runs the server loop with explicit I/O (testable).
    /// </summary>
    /// <remarks>
    /// Stdin is read on a background task into a bounded channel so that
    /// <see cref="CancelRequest"/> can interrupt the in-flight message handler
    /// without requiring the entire session to restart.
    /// </remarks>
    public async Task RunAsync(TextReader input, TextWriter output, CancellationToken ct)
    {
        var channel = Channel.CreateBounded<ServerRequest>(capacity: 16);
        using var readerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var readerTask = ReadStdinIntoChannelAsync(input, channel.Writer, readerCts.Token);

        CancellationTokenSource? messageCts = null;
        Task? handleTask = null;

        try
        {
            await foreach (var request in channel.Reader.ReadAllAsync(ct))
            {
                if (request is ShutdownRequest)
                {
                    break;
                }

                if (request is CancelRequest)
                {
                    messageCts?.Cancel();
                    continue;
                }

                if (request is ContextUpdateRequest ctx)
                {
                    _workingPath = ctx.WorkingPath;
                    OnContextUpdate?.Invoke(ctx);
                    continue;
                }

                if (request is UserMessageRequest msg)
                {
                    // Ensure the previous message has fully completed (TurnEndEvent written)
                    // before starting a new one. HandleMessageAsync never throws.
                    if (handleTask is not null)
                    {
                        await handleTask;
                    }

                    messageCts?.Dispose();
                    messageCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    handleTask = HandleMessageAsync(
                        msg with { Content = BuildContextualContent(msg.Content) }, output, messageCts.Token);

                    // Fire-and-forget: keep draining the channel so CancelRequest
                    // can be processed while handleTask runs concurrently.
                }
            }
        }
        finally
        {
            // Stop background stdin reader.
            await readerCts.CancelAsync();

            // Drain any in-flight message so TurnEndEvent is always written.
            if (handleTask is not null)
            {
                await handleTask;
            }

            messageCts?.Dispose();

            // Wait for the reader task to exit cleanly.
            await readerTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private string BuildContextualContent(string userContent)
    {
        if (SkipContextEnrichment || _workingPath is null)
        {
            return userContent;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"[WorkingPath: {_workingPath}]");
        sb.AppendLine();
        sb.Append(userContent);
        return sb.ToString();
    }

    private async Task HandleMessageAsync(
        UserMessageRequest msg, TextWriter output, CancellationToken ct)
    {
        try
        {
            await foreach (var evt in _processMessage(msg, ct))
            {
                await WriteEventAsync(output, evt, _jsonOptions);
            }
        }
        catch (OperationCanceledException)
        {
            // Intentional cancellation via CancelRequest — TurnEndEvent written in finally.
        }
        catch (Exception ex)
        {
            LogAgentProcessingError(ex);
            await WriteEventAsync(output, new ErrorEvent(ex.Message), _jsonOptions);
        }
        finally
        {
            await WriteEventAsync(output, new TurnEndEvent(), _jsonOptions);
        }
    }

    private async Task ReadStdinIntoChannelAsync(
        TextReader reader,
        ChannelWriter<ServerRequest> writer,
        CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var request = await ReadNextRequestAsync(reader, _jsonOptions, ct);
                if (request is null or ShutdownRequest)
                {
                    await writer.WriteAsync(new ShutdownRequest(), CancellationToken.None);
                    break;
                }

                await writer.WriteAsync(request, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation path — session is shutting down.
        }
        finally
        {
            writer.TryComplete();
        }
    }

    /// <summary>
    /// Reads one JSON Line and deserializes it as a ServerRequest.
    /// </summary>
    public static async Task<ServerRequest?> ReadNextRequestAsync(
        TextReader reader, JsonSerializerOptions options, CancellationToken ct)
    {
        var line = await reader.ReadLineAsync(ct);
        if (string.IsNullOrWhiteSpace(line))
        {
            return new ShutdownRequest();
        }

        try
        {
            return JsonSerializer.Deserialize<ServerRequest>(line, options);
        }
        catch
        {
            return new ShutdownRequest();
        }
    }

    /// <summary>
    /// Serializes an event as a single JSON Line and flushes.
    /// </summary>
    public static async Task WriteEventAsync(
        TextWriter output, ServerEvent evt, JsonSerializerOptions? opts = null)
    {
        var json = JsonSerializer.Serialize<ServerEvent>(evt, opts ?? DefaultJsonOpts);
        await output.WriteLineAsync(json);
        await output.FlushAsync();
    }
}
