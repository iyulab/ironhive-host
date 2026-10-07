using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;
using IronHive.Host.Protocol;
using Microsoft.Extensions.Logging;

namespace IronHive.Host.Server;

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unreadable request line skipped: {Reason}")]
    private partial void LogUnreadableRequest(string reason);

    internal static readonly JsonSerializerOptions DefaultJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly Func<UserMessageRequest, CancellationToken, IAsyncEnumerable<ServerEvent>> _processMessage;
    private readonly ILogger<AgentServerRunner> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly HitlBridge? _hitl;

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
    /// <param name="hitlBridge">The approver the agent's gate asks: while the runner runs, its approval requests are written
    /// as <see cref="HitlRequestEvent"/> lines and each <see cref="HitlResponseRequest"/> read from the input answers one.
    /// Without it, a <c>hitl_response</c> line is ignored.</param>
    public AgentServerRunner(
        Func<UserMessageRequest, CancellationToken, IAsyncEnumerable<ServerEvent>> processMessage,
        ILogger<AgentServerRunner> logger,
        JsonSerializerOptions? jsonOptions = null,
        Action<JsonTypeInfo>[]? typeInfoModifiers = null,
        HitlBridge? hitlBridge = null)
    {
        _processMessage = processMessage;
        _logger = logger;
        _jsonOptions = ApplyModifiers(jsonOptions ?? DefaultJsonOpts, typeInfoModifiers);
        _hitl = hitlBridge;
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
        // The turn writes its events while an approval request is published from inside a tool call: one line at a time.
        using var writeLock = new SemaphoreSlim(1, 1);
        var readerTask = ReadStdinIntoChannelAsync(
            input, channel.Writer, evt => WriteSerializedAsync(output, writeLock, evt, CancellationToken.None), readerCts.Token);

        CancellationTokenSource? messageCts = null;
        Task? handleTask = null;

        using var hitlAttachment = _hitl?.Attach((evt, token) => WriteSerializedAsync(output, writeLock, evt, token));

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

                if (request is HitlResponseRequest hitl)
                {
                    _hitl?.Resolve(hitl);
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
                        msg with { Content = BuildContextualContent(msg.Content) }, output, writeLock, messageCts.Token);

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
        UserMessageRequest msg, TextWriter output, SemaphoreSlim writeLock, CancellationToken ct)
    {
        var turnEndSent = false;
        try
        {
            await foreach (var evt in _processMessage(msg, ct))
            {
                if (evt is TurnEndEvent)
                {
                    turnEndSent = true;
                }

                await WriteSerializedAsync(output, writeLock, evt, CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            // Intentional cancellation via CancelRequest — fallback TurnEndEvent written in finally.
        }
        catch (Exception ex)
        {
            LogAgentProcessingError(ex);
            await WriteSerializedAsync(output, writeLock, new ErrorEvent(ex.Message), CancellationToken.None);
        }
        finally
        {
            // _processMessage's own pipeline (AgentResponseMapper.ToServerEvents) yields a
            // usage-populated TurnEndEvent as its last event on normal completion — this is
            // only a fallback for the exception/cancellation paths above that abandon the
            // stream before it gets there.
            if (!turnEndSent)
            {
                await WriteSerializedAsync(output, writeLock, new TurnEndEvent(), CancellationToken.None);
            }
        }
    }

    private async Task WriteSerializedAsync(TextWriter output, SemaphoreSlim writeLock, ServerEvent evt, CancellationToken ct)
    {
        await writeLock.WaitAsync(ct);
        try
        {
            await WriteEventAsync(output, evt, _jsonOptions, cancellationToken: ct);
        }
        finally
        {
            writeLock.Release();
        }
    }

    private async Task ReadStdinIntoChannelAsync(
        TextReader reader,
        ChannelWriter<ServerRequest> writer,
        Func<ServerEvent, Task> reportError,
        CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                ServerRequest request;
                try
                {
                    request = await ReadNextRequestAsync(reader, _jsonOptions, ct);
                }
                catch (JsonException ex)
                {
                    // One bad line is the sender's mistake to hear about, not a reason to end the session
                    LogUnreadableRequest(ex.Message);
                    await reportError(new ErrorEvent($"Unreadable request skipped: {ex.Message}"));
                    continue;
                }

                if (request is ShutdownRequest)
                {
                    await writer.WriteAsync(request, CancellationToken.None);
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
    /// Reads the next request line. Blank lines are skipped; the end of the input is a <see cref="ShutdownRequest"/>.
    /// </summary>
    /// <exception cref="JsonException">The line is not a request (malformed JSON, unknown type, or null). The reader is
    /// positioned after it, so the caller can report it and read on.</exception>
    public static async Task<ServerRequest> ReadNextRequestAsync(
        TextReader reader, JsonSerializerOptions options, CancellationToken ct)
    {
        string? line;
        do
        {
            line = await reader.ReadLineAsync(ct);
            if (line is null)
            {
                return new ShutdownRequest();
            }
        }
        while (string.IsNullOrWhiteSpace(line));

        try
        {
            return JsonSerializer.Deserialize<ServerRequest>(line, options)
                ?? throw new JsonException("The line is null, not a request.");
        }
        catch (NotSupportedException ex)
        {
            throw new JsonException(ex.Message, ex);
        }
    }

    /// <summary>
    /// Serializes an event as a single JSON Line and flushes.
    /// </summary>
    public static async Task WriteEventAsync(
        TextWriter output, ServerEvent evt, JsonSerializerOptions? opts = null, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize<ServerEvent>(evt, opts ?? DefaultJsonOpts);
        await output.WriteLineAsync(json);
        await output.FlushAsync(cancellationToken: cancellationToken);
    }
}
