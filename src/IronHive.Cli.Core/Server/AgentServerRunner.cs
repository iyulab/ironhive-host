using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace IronHive.Cli.Core.Server;

/// <summary>
/// Reads JSON Lines from stdin, dispatches to an agent processing delegate, and writes
/// server-sent events as JSON Lines to stdout.
/// </summary>
public partial class AgentServerRunner
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Agent processing error")]
    private partial void LogAgentProcessingError(Exception ex);

    internal static readonly JsonSerializerOptions DefaultJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly Func<string, CancellationToken, IAsyncEnumerable<ServerEvent>> _processMessage;
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

    public AgentServerRunner(
        Func<string, CancellationToken, IAsyncEnumerable<ServerEvent>> processMessage,
        ILogger<AgentServerRunner> logger,
        JsonSerializerOptions? jsonOptions = null)
    {
        _processMessage = processMessage;
        _logger = logger;
        _jsonOptions = jsonOptions ?? DefaultJsonOpts;
    }

    /// <summary>
    /// Runs the server loop using Console.In/Out.
    /// </summary>
    public Task RunAsync(CancellationToken ct = default)
        => RunAsync(Console.In, Console.Out, ct);

    /// <summary>
    /// Runs the server loop with explicit I/O (testable).
    /// </summary>
    public async Task RunAsync(TextReader input, TextWriter output, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var request = await ReadNextRequestAsync(input, _jsonOptions, ct);
            if (request is null or ShutdownRequest)
            {
                break;
            }

            if (request is UserMessageRequest msg)
            {
                var content = BuildContextualContent(msg.Content);
                await HandleMessageAsync(content, output, ct);
            }
            else if (request is ContextUpdateRequest ctx)
            {
                _workingPath = ctx.WorkingPath;
                OnContextUpdate?.Invoke(ctx);
            }
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
        string content, TextWriter output, CancellationToken ct)
    {
        try
        {
            await foreach (var evt in _processMessage(content, ct))
            {
                await WriteEventAsync(output, evt, _jsonOptions);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
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
