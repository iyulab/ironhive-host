using Microsoft.Extensions.AI;

namespace IronHive.Host.Tools;

/// <summary>
/// Factory for the canonical M.E.AI <c>FunctionInvoker</c> delegate that converts
/// marshaller-level exceptions into model-actionable error strings, enabling small/quantized
/// local models to self-correct empty-args / malformed-args tool calls.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists:</b> When a local model emits a <see cref="FunctionCallContent"/>
/// with an empty Arguments dictionary (or with missing required keys), M.E.AI's
/// reflection-based parameter marshaller throws <see cref="ArgumentException"/> inside
/// the function's invoke path. Without a FunctionInvoker delegate, the exception is
/// captured up to MaximumConsecutiveErrorsPerRequest (default 3) and then rethrown out
/// of GetStreamingResponseAsync, aborting the entire chat stream — the user sees an
/// empty response body, deterministically, whenever a model keeps emitting empty arguments.
/// </para>
/// <para>
/// <b>What this does:</b> Runs every tool invocation through <see cref="ResilientArgumentsMiddleware"/>,
/// which converts the marshaller exception into a procedural recovery directive the model can act on.
/// Because the delegate returns a string instead of throwing, M.E.AI synthesizes a
/// <see cref="FunctionResultContent"/> and feeds it back to the model as the next-turn
/// input. The model gets up to MaximumIterationsPerRequest rounds to self-correct.
/// </para>
/// <para>
/// <b>Which form to use:</b> this delegate is for a plain <c>UseFunctionInvocation</c> client, where it is the
/// whole <c>FunctionInvoker</c>. A client built with <c>UseToolInvocationPipeline</c> owns its
/// <c>FunctionInvoker</c>; add <see cref="ResilientArgumentsMiddleware"/> to that pipeline instead (last, so it sits
/// directly around the tool). The behaviour is the same.
/// </para>
/// </remarks>
public static class ResilientFunctionInvoker
{
    private static readonly ResilientArgumentsMiddleware Middleware = new();

    /// <summary>
    /// Creates the FunctionInvoker delegate. Install via
    /// <c>UseFunctionInvocation(configure: c =&gt; c.FunctionInvoker = ResilientFunctionInvoker.Create())</c>.
    /// </summary>
    public static Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> Create() =>
        (context, cancellationToken) => Middleware.InvokeAsync(context, InvokeToolAsync, cancellationToken);

    private static ValueTask<object?> InvokeToolAsync(FunctionInvocationContext context, CancellationToken cancellationToken) =>
        context.Function.InvokeAsync(context.Arguments, cancellationToken);
}
