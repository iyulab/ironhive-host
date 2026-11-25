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
    public static async Task<(object? Result, string Output, bool IsError, bool AwaitingInput)> RunAsync(
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
            return (missing, missing, true, false);
        }

        var arguments = Arguments(wait.Arguments);
        var context = new FunctionInvocationContext
        {
            Function = function,
            Arguments = new AIFunctionArguments(arguments),
            CallContent = new FunctionCallContent(wait.ToolUseId, wait.Tool, arguments),
        };
        try
        {
            object? result;
            if (pipeline is not null)
            {
                // The gate asks the bridge as in a live turn and gets the answer the client gave to the re-offered request
                using var known = bridge.Preanswer(wait.ToolUseId, answer);
                // ... and the tool sees this call as a live one: an MCP call that is released while its server's question is
                // open is then known by the model's call id
                using var live = LiveInvocation.Enter(context);
                result = await pipeline.InvokeAsync(context, cancellationToken);
            }
            else if (answer.Approved)
            {
                var approved = answer.ModifiedArguments is { Count: > 0 } edited
                    ? edited.ToDictionary(kv => kv.Key, kv => (object?)kv.Value, StringComparer.Ordinal)
                    : arguments;
                context.Arguments = new AIFunctionArguments(approved);
                using var live = LiveInvocation.Enter(context);
                result = await function.InvokeAsync(context.Arguments, cancellationToken);
            }
            else
            {
                result = new ToolCallRefusal(ToolCallRefusalKind.Rejected, answer.Reason ?? "the operator declined");
            }

            // The call may have been released while the server's question to the person was open: what came back is then a
            // placeholder, not the tool's result
            var released = bridge.IsAwaitingInput(wait.ToolUseId);
            return (result, Describe(result), released || result is ToolCallRefusal, released);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var failed = $"Error: {ex.Message}";
            return (failed, failed, true, false);
        }
    }

    // FunctionInvokingChatClient.CurrentContext is what tools read to learn which model call they serve. A call that runs
    // here is outside that client's loop, so it is set for the length of the call. Only a derived type may set it; this one is
    // never constructed.
    private sealed class LiveInvocation : FunctionInvokingChatClient
    {
        private LiveInvocation(IChatClient innerClient)
            : base(innerClient)
        {
        }

        public static IDisposable Enter(FunctionInvocationContext context)
        {
            var previous = CurrentContext;
            CurrentContext = context;
            return new Restore(previous);
        }

        private sealed class Restore(FunctionInvocationContext? previous) : IDisposable
        {
            public void Dispose() => CurrentContext = previous;
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
