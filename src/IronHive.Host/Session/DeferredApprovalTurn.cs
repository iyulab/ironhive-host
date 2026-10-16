using System.Runtime.CompilerServices;
using System.Text.Json;
using IronHive.Agent.Invocation;
using IronHive.Agent.Loop;
using IronHive.Agent.Mode;
using IronHive.Host.Server;
using Microsoft.Extensions.AI;

namespace IronHive.Host.Session;

/// <summary>
/// The turn a deferred approval's answer opens (<see cref="HitlBridge.DeferAfter"/>). The request is settled first - an
/// answer is used once, even when the process stops before the call finishes - then the call runs through the loop's tool
/// pipeline with the answer given to the bridge in advance (so the gate, the guards and the tool behave as in a live turn),
/// its outcome is relayed as the call's result (a second <c>tool_end</c> for the same call id), and the model is told the
/// outcome in a message and goes on from there.
/// </summary>
public static class DeferredApprovalTurn
{
    /// <summary>
    /// Runs the deferred call and the turn that tells the model its outcome.
    /// </summary>
    /// <param name="loop">The session's loop.</param>
    /// <param name="outcome">The answer (or expiry) the bridge handed to the runner.</param>
    /// <param name="bridge">The approval bridge.</param>
    /// <param name="recorder">The session's recorder, when the session is recorded; the message and the turn are written
    /// like any other.</param>
    /// <param name="tools">The loop's tools, to find the call's function.</param>
    /// <param name="pipeline">The loop's tool pipeline; without one, an approved call runs its function directly.</param>
    /// <param name="cancellationToken">Cancels the turn.</param>
    public static async IAsyncEnumerable<AgentResponseChunk> RunAsync(
        IAgentLoop loop,
        DeferredApproval outcome,
        HitlBridge bridge,
        SessionTurnRecorder? recorder,
        IReadOnlyList<AITool> tools,
        ToolInvocationPipeline? pipeline,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(tools);

        var wait = outcome.Wait;
        if (bridge.WaitLog is { } log)
        {
            await log.SettledAsync(wait.RequestId, CancellationToken.None);
        }

        var (result, output, isError) = await ApprovedCall.RunAsync(wait, outcome.Answer, bridge, tools, pipeline, cancellationToken);
        var refusal = result as ToolCallRefusal;
        yield return new AgentResponseChunk
        {
            ToolResult = new ToolCallResult
            {
                CallId = wait.ToolUseId,
                ToolName = wait.Tool,
                Arguments = wait.Arguments is { } args ? args.GetRawText() : "{}",
                Result = output,
                Success = !isError,
                RefusalKind = refusal?.Kind,
            }
        };

        var note = Note(wait, outcome, output, isError, refusal is not null);
        var turn = loop.RunStreamingAsync(note, cancellationToken);
        if (recorder is not null)
        {
            turn = recorder.RecordAsync(note, turn, cancellationToken);
        }

        await foreach (var chunk in turn.WithCancellation(cancellationToken))
        {
            yield return chunk;
        }
    }

    /// <summary>The message that tells the model what became of its deferred call.</summary>
    internal static string Note(ApprovalWaitEntry wait, DeferredApproval outcome, string output, bool isError, bool notRun)
    {
        var what = outcome.Expired
            ? "No answer came in time, so it did not run."
            : notRun
                ? "It did not run."
                : isError
                    ? "It was approved and ran, and failed."
                    : "It was approved and has now run.";
        return $"[Approval outcome] Your earlier call to {wait.Tool} (call {wait.ToolUseId}) was waiting for approval. {what}\n"
            + $"Result:\n{output}";
    }
}
