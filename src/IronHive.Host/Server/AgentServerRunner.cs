using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;
using IronHive.Agent.ErrorRecovery;
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
    // How long the loop's end waits for the input reader before leaving it to finish on its own.
    private static readonly TimeSpan ReaderStopGrace = TimeSpan.FromSeconds(1);

    [LoggerMessage(Level = LogLevel.Error, Message = "Agent processing error")]
    private partial void LogAgentProcessingError(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unreadable request line skipped ({ErrorType})")]
    private partial void LogUnreadableRequest(string errorType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A session read failed")]
    private partial void LogSessionReadFailed(Exception ex);

    internal static readonly JsonSerializerOptions DefaultJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        // A JSON object's members are unordered: a client that writes "type" after the other fields (a dictionary,
        // a serializer that sorts keys) sends a valid request.
        AllowOutOfOrderMetadataProperties = true,
    };

    private readonly Func<UserMessageRequest, ContextUpdateRequest?, CancellationToken, IAsyncEnumerable<ServerEvent>> _processMessage;
    private readonly ILogger<AgentServerRunner> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly HitlBridge? _hitl;

    private ContextUpdateRequest? _context;

    /// <summary>
    /// Optional callback invoked when a context update is received.
    /// </summary>
    public Action<ContextUpdateRequest>? OnContextUpdate { get; set; }

    /// <summary>
    /// Answers <see cref="ListSessionsRequest"/> and <see cref="ReadSessionRequest"/> from the session store. The answers are
    /// written as soon as they are ready, without waiting for or disturbing a running turn (they only read). Without it the
    /// server answers those requests with an <see cref="ErrorEvent"/> coded <see cref="ErrorCodes.NotSupported"/>.
    /// </summary>
    public Session.StoredSessionReader? SessionReader { get; set; }

    /// <summary>
    /// A turn to run before any message is read - the turn a previous process left waiting for approval
    /// (<see cref="Session.SuspendedTurnResumer"/>). It runs once the approval bridge is attached, and the messages that
    /// arrive meanwhile queue behind it. It runs once: the property is cleared when a run takes it.
    /// </summary>
    public Func<CancellationToken, IAsyncEnumerable<ServerEvent>>? ResumeTurn { get; set; }

    /// <summary>
    /// Opens the turn a deferred approval's answer (or expiry) asks for (<see cref="HitlBridge.DeferAfter"/>) - typically
    /// <see cref="Session.DeferredApprovalTurn.RunAsync"/>. The turn queues behind the others. Without it the bridge never
    /// defers: a request waits inside its turn.
    /// </summary>
    public Func<DeferredApproval, CancellationToken, IAsyncEnumerable<ServerEvent>>? DeferredAnswerTurn { get; set; }

    // Set when the server stops while a turn waits for an approval the session keeps: that turn ends as suspended
    private volatile bool _suspending;

    /// <summary>
    /// Classifies a failed turn for <see cref="ErrorEvent.Code"/>. Defaults to an <see cref="ErrorRecoveryService"/> with its
    /// built-in HTTP status reader; pass the application's own (with the provider gateways' failure readers) so the wire
    /// and error recovery read a failure the same way.
    /// </summary>
    public IErrorRecoveryService ErrorClassifier
    {
        get => _errorClassifier;
        set => _errorClassifier = value ?? throw new ArgumentNullException(nameof(value));
    }

    private IErrorRecoveryService _errorClassifier = new ErrorRecoveryService();

    /// <param name="processMessage">Processes a user message and yields server events. The message arrives with the last
    /// <see cref="ContextUpdateRequest"/> in front of its content (<see cref="ContextEnrichment"/>); to receive the user's
    /// text and the context apart, use the constructor whose handler takes the context.</param>
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
        : this(Enriching(processMessage), logger, jsonOptions, typeInfoModifiers, hitlBridge)
    {
    }

    /// <param name="processMessage">Processes a user message and yields server events. It receives the message as sent
    /// and the last <see cref="ContextUpdateRequest"/> read before it (null before any), and the runner puts nothing in
    /// front of the content: the handler can check the user's own text, then apply the context with
    /// <see cref="ContextEnrichment.Apply"/> or not at all.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="jsonOptions">Custom JSON options. Defaults to snake_case.</param>
    /// <param name="typeInfoModifiers">Optional modifiers applied to the polymorphic type resolver (see the other
    /// constructor).</param>
    /// <param name="hitlBridge">The approver the agent's gate asks (see the other constructor).</param>
    public AgentServerRunner(
        Func<UserMessageRequest, ContextUpdateRequest?, CancellationToken, IAsyncEnumerable<ServerEvent>> processMessage,
        ILogger<AgentServerRunner> logger,
        JsonSerializerOptions? jsonOptions = null,
        Action<JsonTypeInfo>[]? typeInfoModifiers = null,
        HitlBridge? hitlBridge = null)
    {
        ArgumentNullException.ThrowIfNull(processMessage);
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

        // The modifiers wrap the resolver the options already have. Combining a second resolver behind it does not work:
        // the first resolver that answers for a type wins, so once the options carry one — a source-generated context, or
        // the reflection resolver STJ installs on first use of any options instance — the modifiers never ran.
        var opts = new JsonSerializerOptions(baseOptions);
        var resolver = opts.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver();
        foreach (var modifier in modifiers)
        {
            resolver = resolver.WithAddedModifier(modifier);
        }

        opts.TypeInfoResolver = resolver;
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
    /// <para>
    /// The input is read on a background task into a bounded channel so that
    /// <see cref="CancelRequest"/> can interrupt the in-flight message handler
    /// without requiring the entire session to restart.
    /// </para>
    /// <para>
    /// The reader may block: <see cref="Console.In"/> reads synchronously even through
    /// <see cref="TextReader.ReadLineAsync(CancellationToken)"/>, so the read loop runs on a thread of its own — run
    /// inline it would wait for the next line before this loop ever started, and no message would be handled until
    /// the input closed. For the same reason a read in progress cannot be cancelled: when the loop ends for any reason
    /// other than the input (cancellation, a <see cref="ShutdownRequest"/> already taken), it does not wait on it.
    /// </para>
    /// </remarks>
    public async Task RunAsync(TextReader input, TextWriter output, CancellationToken ct)
    {
        _suspending = false;
        var channel = Channel.CreateBounded<ServerRequest>(capacity: 16);
        using var readerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // The turn writes its events while an approval request is published from inside a tool call: one line at a time.
        using var writeLock = new SemaphoreSlim(1, 1);
        var readerTask = Task.Run(
            () => ReadStdinIntoChannelAsync(
                input, channel.Writer, evt => WriteSerializedAsync(output, writeLock, evt, CancellationToken.None), readerCts.Token),
            CancellationToken.None);

        // Turns run on their own task so this loop keeps reading — a turn waiting for approval gets its hitl_response
        // even when another message arrived first.
        var turns = new TurnQueue(ct);

        var deferredTurn = DeferredAnswerTurn;
        using var hitlAttachment = _hitl?.Attach(
            (evt, token) => WriteSerializedAsync(output, writeLock, evt, token),
            deferredTurn is null
                ? null
                : answer => turns.Enqueue(token => HandleTurnAsync(t => deferredTurn(answer, t), output, writeLock, token)));

        // A turn a previous process left waiting for approval runs first, once the bridge can reach the client
        if (ResumeTurn is { } resume)
        {
            ResumeTurn = null; // once: the waits it resumes are settled by then
            turns.Enqueue(token => HandleTurnAsync(resume, output, writeLock, token));
        }

        // Deferred requests a previous process left (HitlBridge.RestoreDeferred) are sent again; they hold no turn
        if (_hitl is not null)
        {
            await _hitl.ReofferDeferredAsync(ct);
        }

        // Session reads answer beside the turns, not through their queue; the loop waits for the ones still running when it ends.
        var reads = new List<Task>();
        try
        {
            await foreach (var request in channel.Reader.ReadAllAsync(ct))
            {
                if (request is ShutdownRequest)
                {
                    break;
                }

                if (request is ListSessionsRequest || request is ReadSessionRequest)
                {
                    reads.RemoveAll(task => task.IsCompleted);
                    reads.Add(AnswerSessionReadAsync(request, output, writeLock, ct));
                    continue;
                }

                if (request is CancelRequest)
                {
                    turns.CancelCurrent();
                    continue;
                }

                if (request is ContextUpdateRequest ctx)
                {
                    _context = ctx;
                    OnContextUpdate?.Invoke(ctx);
                    continue;
                }

                if (request is HitlResponseRequest hitl)
                {
                    _hitl?.Resolve(hitl);
                    continue;
                }

                if (request is ElicitationResponseRequest elicitation)
                {
                    _hitl?.ResolveElicitation(elicitation);
                    continue;
                }

                if (request is UserMessageRequest msg)
                {
                    // The context in effect when the message arrived. Turns run one at a time, each ending with
                    // its TurnEndEvent before the next starts. HandleMessageAsync never throws.
                    var context = _context;
                    turns.Enqueue(
                        token => HandleTurnAsync(t => _processMessage(msg, context, t), output, writeLock, token),
                        () => WriteNotProcessedAsync(output, writeLock));
                }
            }
        }
        finally
        {
            // Stop background stdin reader.
            await readerCts.CancelAsync();

            // A turn waiting for an approval the session keeps is suspended, not denied: a server started again with the
            // same session offers the request again and finishes it. The messages queued behind it are not run.
            // Nothing keeps the wait without a session: it is denied now instead of holding the stop until the approval
            // timeout. A wait that begins while the remaining turns run is treated the same way.
            StopWaits(_hitl, turns, () => _suspending = true);

            // A read already received is answered before the server stops (it only reads, and ends with the cancellation token).
            await Task.WhenAll(reads);

            // Run the turns already received so each one's TurnEndEvent is written.
            await turns.DisposeAsync();
            if (_hitl is not null)
            {
                _hitl.WaitStarted = null;
            }

            // Let the reader exit when it can: an asynchronous reader stops on the cancellation, a synchronous one
            // blocked on a line cannot be interrupted and ends with the next line or the end of the input.
            await Task.WhenAny(readerTask, Task.Delay(ReaderStopGrace, CancellationToken.None));
        }
    }

    /// <summary>A handler that receives the message with the context already in front of its content.</summary>
    internal static Func<UserMessageRequest, ContextUpdateRequest?, CancellationToken, IAsyncEnumerable<ServerEvent>> Enriching(
        Func<UserMessageRequest, CancellationToken, IAsyncEnumerable<ServerEvent>> processMessage)
    {
        ArgumentNullException.ThrowIfNull(processMessage);
        return (msg, context, ct) => processMessage(msg with { Content = ContextEnrichment.Apply(context, msg.Content) }, ct);
    }

    // Never throws: a failed read is answered as an ErrorEvent, as a failed turn is.
    private async Task AnswerSessionReadAsync(ServerRequest request, TextWriter output, SemaphoreSlim writeLock, CancellationToken ct)
    {
        ServerEvent answer;
        var requestId = request switch
        {
            ListSessionsRequest list => list.Id,
            ReadSessionRequest read => read.Id,
            _ => null,
        };
        try
        {
            answer = SessionReader is not { } reader
                ? new ErrorEvent("This server has no session store to read.") { Code = ErrorCodes.NotSupported, Id = requestId }
                : request switch
                {
                    ListSessionsRequest list => await reader.ListAsync(list, ct),
                    ReadSessionRequest read => await reader.ReadAsync(read, ct),
                    _ => new ErrorEvent("Not a session read.") { Code = ErrorCodes.Internal, Id = requestId },
                };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            LogSessionReadFailed(ex);
            answer = new ErrorEvent("The session store could not be read.") { Code = ErrorCodes.Internal, Id = requestId };
        }

        await WriteSerializedAsync(output, writeLock, answer, CancellationToken.None);
    }

    private async Task HandleTurnAsync(
        Func<CancellationToken, IAsyncEnumerable<ServerEvent>> turn, TextWriter output, SemaphoreSlim writeLock, CancellationToken ct)
    {
        var turnEndSent = false;
        var failed = false;
        try
        {
            await foreach (var evt in turn(ct))
            {
                if (evt is TurnEndEvent)
                {
                    turnEndSent = true;
                }

                await WriteSerializedAsync(output, writeLock, evt, CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Intentional cancellation via CancelRequest — fallback TurnEndEvent (StopReason "cancelled") written in finally.
        }
        catch (Exception ex)
        {
            // Includes a cancellation the client did not ask for — an HTTP or stream timeout is a failure, not a cancel.
            failed = true;
            LogAgentProcessingError(ex);
            await WriteSerializedAsync(output, writeLock, TurnFailures.ToErrorEvent(ex, _errorClassifier), CancellationToken.None);
        }
        finally
        {
            // _processMessage's own pipeline (AgentResponseMapper.ToServerEvents) yields a
            // usage-populated TurnEndEvent as its last event on normal completion — this is
            // only a fallback for the exception/cancellation paths above that abandon the
            // stream before it gets there.
            if (!turnEndSent)
            {
                var stopReason = failed || !ct.IsCancellationRequested ? null
                    : _suspending ? StopReasons.Suspended
                    : StopReasons.Cancelled;
                await WriteSerializedAsync(output, writeLock, new TurnEndEvent { StopReason = stopReason }, CancellationToken.None);
            }
        }
    }

    /// <summary>What the model reads for a call whose approval request was open when the server stopped.</summary>
    internal const string StoppedReason = "the server stopped before the approval request was answered";

    /// <summary>
    /// What a stopping runner does with approval waits, now and for each one that begins while its remaining turns run:
    /// with a session it suspends the waiting turn (and drops the turns behind it), without one it denies the wait.
    /// </summary>
    internal static void StopWaits(HitlBridge? bridge, TurnQueue turns, Action markSuspending)
    {
        if (bridge is null)
        {
            return;
        }

        void Stop()
        {
            if (bridge.WaitLog is null)
            {
                bridge.RejectWaitsInTurn(StoppedReason);
            }
            else if (bridge.HasWaitInTurn)
            {
                markSuspending();
                turns.SuspendCurrent();
            }
            // With a session nothing is denied: a wait that registers after the check above announces itself through
            // WaitStarted and suspends then. Checking «no wait» and then denying «every wait» raced a wait registering in
            // between - it was denied instead of suspended (host CI, 2026-10-20, a 2-vCPU runner).
        }

        bridge.WaitStarted = Stop;
        Stop();
    }

    /// <summary>The events a message gets when the server stops before its turn starts, behind a suspended one.</summary>
    internal static IEnumerable<ServerEvent> NotProcessedEvents() =>
    [
        new ErrorEvent("The server stopped before this message was processed; it was not recorded. Send it again once the server runs.")
        {
            Code = ErrorCodes.NotProcessed,
        },
        new TurnEndEvent(),
    ];

    private async Task WriteNotProcessedAsync(TextWriter output, SemaphoreSlim writeLock)
    {
        foreach (var evt in NotProcessedEvents())
        {
            await WriteSerializedAsync(output, writeLock, evt, CancellationToken.None);
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
                    LogUnreadableRequest(ex.GetType().Name);
                    await reportError(new ErrorEvent($"Unreadable request skipped: {ex.Message}") { Code = ErrorCodes.UnreadableRequest });
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
