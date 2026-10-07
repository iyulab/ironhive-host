using System.Runtime.CompilerServices;
using System.Text.Json;
using IronHive.Agent.Invocation;
using IronHive.Agent.Loop;
using IronHive.Agent.Mode;
using IronHive.Host.Protocol;
using IronHive.Host.Server;
using Microsoft.Extensions.AI;

namespace IronHive.Host.Session;

/// <summary>
/// Finishes a turn that a previous process left waiting for approval. Each logged wait is offered to the client again with
/// its original request id (<see cref="HitlBridge.ReofferAsync"/>); the answer is given to the bridge in advance and the call
/// runs through the loop's tool pipeline, so the gate, the guards and the tool behave as they do in a live turn. The results
/// are written to the session, appended to the loop's history, and the loop continues the turn
/// (<see cref="IAgentLoop.ContinueStreamingAsync(CancellationToken)"/>) - recorded like any other.
/// </summary>
public static class SuspendedTurnResumer
{
    /// <summary>
    /// Resumes the suspended turn. The loop's history must be the session's restored history, which ends with the waiting
    /// calls (<see cref="ISessionManager.RestoreContextAsync"/>).
    /// </summary>
    /// <param name="loop">The session's loop, its history restored.</param>
    /// <param name="waits">The session's unsettled waits (<see cref="ISessionManager.GetPendingApprovalsAsync"/>).</param>
    /// <param name="bridge">The approval bridge, attached to the client.</param>
    /// <param name="recorder">The session's recorder.</param>
    /// <param name="tools">The loop's tools, to find each waiting call's function.</param>
    /// <param name="pipeline">The loop's tool pipeline; without one, an approved call runs its function directly.</param>
    /// <param name="cancellationToken">Cancels the resumed turn; a wait not answered yet stays on record.</param>
    public static async IAsyncEnumerable<AgentResponseChunk> ResumeAsync(
        IAgentLoop loop,
        IReadOnlyList<ApprovalWaitEntry> waits,
        HitlBridge bridge,
        SessionTurnRecorder recorder,
        IReadOnlyList<AITool> tools,
        ToolInvocationPipeline? pipeline,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(waits);
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(tools);

        if (waits.Count == 0)
        {
            yield break;
        }

        var results = new List<AIContent>();
        foreach (var wait in waits)
        {
            var answer = await bridge.ReofferAsync(wait, cancellationToken);
            var (result, output, isError) = await RunAsync(wait, answer, bridge, tools, pipeline, cancellationToken);
            await recorder.WriteResumedResultAsync(wait, output, isError, cancellationToken);
            results.Add(new FunctionResultContent(wait.ToolUseId, result));
        }

        var history = (await loop.GetHistoryAsync(cancellationToken)).ToList();
        history.Add(new ChatMessage(ChatRole.Tool, results));
        await loop.InitializeHistoryAsync(history, cancellationToken);

        await foreach (var chunk in recorder.RecordContinuationAsync(loop.ContinueStreamingAsync(cancellationToken), cancellationToken))
        {
            yield return chunk;
        }
    }

    private static async Task<(object? Result, string Output, bool IsError)> RunAsync(
        ApprovalWaitEntry wait,
        HitlResponseRequest answer,
        HitlBridge bridge,
        IReadOnlyList<AITool> tools,
        ToolInvocationPipeline? pipeline,
        CancellationToken cancellationToken)
    {
        var function = tools.OfType<AIFunction>().FirstOrDefault(t => string.Equals(t.Name, wait.Tool, StringComparison.Ordinal));
        if (function is null)
        {
            var missing = $"Error: the tool '{wait.Tool}' is not available after the restart; the call did not run.";
            return (missing, missing, true);
        }

        var arguments = Arguments(wait.Arguments);
        try
        {
            object? result;
            if (pipeline is not null)
            {
                // The gate asks the bridge as in a live turn and gets the answer the client gave to the re-offered request
                using var known = bridge.Preanswer(wait.ToolUseId, answer);
                var context = new FunctionInvocationContext
                {
                    Function = function,
                    Arguments = new AIFunctionArguments(arguments),
                    CallContent = new FunctionCallContent(wait.ToolUseId, wait.Tool, arguments),
                };
                result = await pipeline.InvokeAsync(context, cancellationToken);
            }
            else if (answer.Approved)
            {
                var approved = answer.ModifiedArguments is { Count: > 0 } edited
                    ? edited.ToDictionary(kv => kv.Key, kv => (object?)kv.Value, StringComparer.Ordinal)
                    : arguments;
                result = await function.InvokeAsync(new AIFunctionArguments(approved), cancellationToken);
            }
            else
            {
                result = new ToolCallRefusal(ToolCallRefusalKind.Rejected, answer.Reason ?? "the operator declined");
            }

            return (result, Describe(result), result is ToolCallRefusal);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var failed = $"Error: {ex.Message}";
            return (failed, failed, true);
        }
    }

    private static Dictionary<string, object?> Arguments(JsonElement? arguments)
    {
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (arguments is { ValueKind: JsonValueKind.Object } obj)
        {
            foreach (var property in obj.EnumerateObject())
            {
                map[property.Name] = property.Value.Clone();
            }
        }

        return map;
    }

    private static string Describe(object? result) => result switch
    {
        null => string.Empty,
        string text => text,
        ToolCallRefusal refusal => refusal.ToString(),
        JsonElement element => element.ToString(),
        _ => JsonSerializer.Serialize(result),
    };
}
