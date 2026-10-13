using System.Text;
using IronHive.Agent.Loop;

namespace IronHive.Cli.Commands;

/// <summary>
/// What a streamed <c>ironhive run</c> turn has done so far — its text, the tool calls it started and the outcomes that
/// came back — so a run cut short (the timeout, the caller, an error) still reports the work it did, not only that it
/// ended. The final chunk's <see cref="TurnRecord"/> replaces the running record when the turn completes.
/// </summary>
internal sealed class RunProgress
{
    private readonly StringBuilder _text = new();
    private readonly StringBuilder _thinking = new();
    private readonly List<(string Id, StringBuilder Name, StringBuilder Arguments)> _started = [];
    private readonly Dictionary<string, ToolCallResult> _finished = new(StringComparer.Ordinal);
    private readonly List<ToolCallResult> _finishedWithoutId = [];
    private TokenUsage? _usage;

    /// <summary>The completed turn, once the final chunk arrived; <c>null</c> while the turn is running or after it was cut.</summary>
    public RunReport? Completed { get; private set; }

    public void Add(AgentResponseChunk chunk)
    {
        if (chunk.TextDelta is { } text)
        {
            _text.Append(text);
        }

        if (chunk.ThinkingDelta is { } thinking)
        {
            _thinking.Append(thinking);
        }

        if (chunk.ToolCallDelta is { } call)
        {
            var index = _started.FindIndex(s => s.Id == call.Id);
            if (index < 0)
            {
                _started.Add((call.Id, new StringBuilder(), new StringBuilder()));
                index = _started.Count - 1;
            }

            var (_, name, arguments) = _started[index];
            if (call.IsComplete)
            {
                // A complete chunk carries the whole name and arguments, not a fragment to append.
                name.Clear().Append(call.NameDelta);
                arguments.Clear().Append(call.ArgumentsDelta);
            }
            else
            {
                name.Append(call.NameDelta);
                arguments.Append(call.ArgumentsDelta);
            }
        }

        if (chunk.ToolResult is { } result)
        {
            if (result.CallId is { } id)
            {
                _finished[id] = result;
            }
            else
            {
                _finishedWithoutId.Add(result);
            }
        }

        if (chunk.Usage is { } usage)
        {
            _usage = usage;
        }

        if (chunk.Turn is { } turn)
        {
            Completed = new RunReport(
                chunk.Addendum is { } addendum ? turn.Content + "\n\n" + addendum : turn.Content,
                turn.StopReason,
                turn.ToolCalls,
                turn.ThinkingContent,
                turn.Usage ?? _usage);
        }
    }

    /// <summary>
    /// The turn as far as it got: the text streamed so far, every call that started in the order it started — with its
    /// outcome when one came back, <c>unfinished</c> otherwise — and the usage if the provider reported any.
    /// </summary>
    public RunReport SoFar()
    {
        var calls = new List<ToolCallResult>();
        foreach (var (id, name, arguments) in _started)
        {
            calls.Add(_finished.TryGetValue(id, out var finished)
                ? finished
                : new ToolCallResult { CallId = id, ToolName = name.ToString(), Arguments = arguments.ToString(), Result = string.Empty });
        }

        // A result the stream reported without an id (or for a call it never announced) is kept rather than dropped.
        calls.AddRange(_finishedWithoutId);
        calls.AddRange(_finished.Values.Where(r => _started.All(s => s.Id != r.CallId)));

        var thinking = _thinking.Length > 0 ? new ThinkingContent { Content = _thinking.ToString() } : null;
        return new RunReport(_text.ToString(), StopReason: null, calls, thinking, _usage, Unfinished: [.. _started.Select(s => s.Id).Where(id => !_finished.ContainsKey(id))]);
    }
}

/// <summary>
/// One <c>ironhive run</c> turn for <see cref="RunOutcome"/>: completed (<see cref="StopReason"/> set) or cut short
/// (<c>null</c>, with <see cref="Unfinished"/> naming the calls that started and never reported back).
/// </summary>
internal sealed record RunReport(
    string Content,
    TurnStopReason? StopReason,
    IReadOnlyList<ToolCallResult> ToolCalls,
    ThinkingContent? ThinkingContent,
    TokenUsage? Usage,
    IReadOnlyCollection<string>? Unfinished = null);
