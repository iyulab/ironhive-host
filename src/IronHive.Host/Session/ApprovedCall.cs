using System.Text.Json;
using IronHive.Agent.Invocation;
using IronHive.Agent.Mode;
using IronHive.Host.Protocol;
using IronHive.Host.Server;
using Microsoft.Extensions.AI;

namespace IronHive.Host.Session;

/// <summary>
/// Runs a call whose approval was answered outside its turn - a wait a previous process left (<see cref="SuspendedTurnResumer"/>)
/// or a deferred one (<see cref="DeferredApprovalTurn"/>) - through the loop's tool pipeline, with the answer given to the
/// bridge in advance so the gate, the guards and the tool behave as in a live turn.
/// </summary>
internal static class ApprovedCall
{
    public static async Task<(object? Result, string Output, bool IsError)> RunAsync(
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
            var missing = $"Error: the tool '{wait.Tool}' is not available here; the call did not run.";
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
