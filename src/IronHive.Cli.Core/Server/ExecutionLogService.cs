using System.Text;
using System.Text.Json;
using IronHive.Agent.Loop;

namespace IronHive.Cli.Core.Server;

/// <summary>
/// Records tool execution steps to a JSONL log file for traceability.
/// Captures tool calls from streaming chunks and writes structured entries
/// to a separate execution log ({sessionId}.execlog.jsonl).
/// </summary>
public sealed class ExecutionLogService : IExecutionLogger, IAsyncDisposable
{
    private StreamWriter? _writer;
    private int _turnNumber;
    private int _stepNumber;

    private string? _pendingToolId;
    private string? _pendingToolName;
    private readonly StringBuilder _pendingArgs = new();

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public void Initialize(string logFilePath)
    {
        var dir = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var stream = new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream, new UTF8Encoding(false))
        {
            AutoFlush = true
        };
    }

    public async Task BeginTurnAsync(string userPrompt)
    {
        _turnNumber++;
        _stepNumber = 0;
        await WriteEntryAsync(new
        {
            type = "turn_start",
            turn = _turnNumber,
            prompt = Truncate(userPrompt, 500),
            timestamp = DateTimeOffset.UtcNow
        });
    }

    public async Task ProcessChunkAsync(AgentResponseChunk chunk)
    {
        if (chunk.ToolCallDelta is null)
        {
            return;
        }

        var tc = chunk.ToolCallDelta;

        if (tc.NameDelta is not null)
        {
            await FlushPendingToolCallAsync();
            _pendingToolId = tc.Id;
            _pendingToolName = tc.NameDelta;
            _pendingArgs.Clear();
        }

        if (tc.ArgumentsDelta is not null)
        {
            _pendingArgs.Append(tc.ArgumentsDelta);
        }
    }

    public async Task EndTurnAsync(int? responseLength = null)
    {
        await FlushPendingToolCallAsync();

        if (_stepNumber > 0)
        {
            await WriteEntryAsync(new
            {
                type = "turn_end",
                turn = _turnNumber,
                steps = _stepNumber,
                responseChars = responseLength,
                timestamp = DateTimeOffset.UtcNow
            });
        }
    }

    public int TotalSteps { get; private set; }
    public int TurnCount => _turnNumber;

    private async Task FlushPendingToolCallAsync()
    {
        if (_pendingToolName is null)
        {
            return;
        }

        _stepNumber++;
        TotalSteps++;

        await WriteEntryAsync(new
        {
            type = "tool_call",
            turn = _turnNumber,
            step = _stepNumber,
            id = _pendingToolId,
            tool = _pendingToolName,
            arguments = _pendingArgs.ToString(),
            timestamp = DateTimeOffset.UtcNow
        });

        _pendingToolId = null;
        _pendingToolName = null;
        _pendingArgs.Clear();
    }

    private async Task WriteEntryAsync(object entry)
    {
        if (_writer is null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(entry, s_jsonOptions);
        await _writer.WriteLineAsync(json);
    }

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : string.Concat(text.AsSpan(0, maxLength), "...");

    public async ValueTask DisposeAsync()
    {
        if (_writer is not null)
        {
            await _writer.DisposeAsync();
            _writer = null;
        }
    }
}
