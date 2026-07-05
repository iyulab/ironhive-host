using System.Text.Json;
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
/// with an empty Arguments dictionary (or with missing required keys), M.E.AI 10.4.1's
/// reflection-based parameter marshaller throws <see cref="ArgumentException"/> inside
/// the function's invoke path. Without a FunctionInvoker delegate, the exception is
/// captured up to MaximumConsecutiveErrorsPerRequest (default 3) and then rethrown out
/// of GetStreamingResponseAsync, aborting the entire chat stream — the user sees an
/// empty response body. This is Filer's 2026-04-29 chat-rag failure mode (4 stack traces,
/// deterministic).
/// </para>
/// <para>
/// <b>What this does:</b> Wraps every tool invocation in a try/catch that converts
/// the marshaller exception into a procedural recovery directive the model can act on.
/// Because the delegate returns a string instead of throwing, M.E.AI synthesizes a
/// <see cref="FunctionResultContent"/> and feeds it back to the model as the next-turn
/// input. The model gets up to MaximumIterationsPerRequest rounds to self-correct.
/// </para>
/// <para>
/// <b>Phase D-3 (2026-05-01):</b> The directive was rewritten from a single .NET-flavoured
/// retry sentence into a numbered procedure that (1) names the missing parameter, (2) tells
/// the model where to find its value, (3) explicitly forbids the empty-args retry pattern
/// the model gets stuck in, and (4) offers two escape hatches (ask the user, pick a different
/// tool). Filer cycle-699 (2026-05-01 §4.4) showed the prior phrasing did not move the
/// needle on Gemma 4 E4B at gguf:default even with strengthened tool descriptions; the
/// procedural rewrite shifts the cognitive burden from "interpret a stack-trace fragment"
/// to "follow a procedure," which small instruction-tuned models handle better.
/// </para>
/// <para>
/// <b>Reference:</b> https://learn.microsoft.com/dotnet/ai/how-to/handle-invalid-tool-input
/// (canonical Microsoft pattern).
/// </para>
/// </remarks>
public static class ResilientFunctionInvoker
{
    /// <summary>
    /// Creates the FunctionInvoker delegate. Install via
    /// <c>UseFunctionInvocation(configure: c =&gt; c.FunctionInvoker = ResilientFunctionInvoker.Create())</c>.
    /// </summary>
    public static Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> Create()
    {
        return async (context, cancellationToken) =>
        {
            try
            {
                return await context.Function.InvokeAsync(context.Arguments, cancellationToken);
            }
            catch (ArgumentException ex) when (ex.ParamName == "arguments")
            {
                // Marshaller validation failure. Extract the parameter name when present
                // and synthesize a procedural recovery directive (D-3) the model can act on.
                var toolName = context.Function.Name;
                var paramName = TryExtractMissingParameterName(ex.Message);
                return paramName is not null
                    ? BuildMissingParameterDirective(toolName, paramName)
                    : BuildMalformedArgumentsDirective(toolName, ex.Message);
            }
            catch (JsonException ex)
            {
                return $"Tool '{context.Function.Name}' could not parse the arguments JSON: {ex.Message}. " +
                       $"Retry with a valid JSON object whose keys match the parameter schema.";
            }
        };
    }

    /// <summary>
    /// Procedural recovery directive (D-3) for the "required parameter missing" case.
    /// Numbered steps + an explicit "do NOT retry with empty arguments" clause +
    /// two named escape hatches. Optimized for small instruction-tuned models that
    /// respond better to procedures than to stack-trace fragments.
    /// </summary>
    private static string BuildMissingParameterDirective(string toolName, string paramName) =>
        $"Tool '{toolName}' rejected the call because required parameter '{paramName}' was not provided.\n" +
        "\n" +
        "To recover:\n" +
        $"1. Look at the user's most recent message and any earlier context for the value of '{paramName}'. " +
        $"If you find it, repeat the call to '{toolName}' with '{paramName}' set to that value.\n" +
        $"2. If '{paramName}' is genuinely unknown, do NOT call '{toolName}' again with empty arguments. " +
        $"Either ask the user to provide '{paramName}', or pick a different tool that fits the user's intent.\n" +
        "\n" +
        $"Do NOT retry '{toolName}' with the same empty arguments — that will fail again the same way.";

    /// <summary>
    /// Procedural recovery directive (D-3) for the "marshaller raised but no parameter
    /// name was parseable" fallback case. Same procedural shape, scoped to schema review
    /// since the missing key is unknown.
    /// </summary>
    private static string BuildMalformedArgumentsDirective(string toolName, string detail) =>
        $"Tool '{toolName}' rejected the call because the arguments object was malformed. Detail: {detail}\n" +
        "\n" +
        $"Look at the schema for '{toolName}' and provide every required parameter with a non-empty value. " +
        "If you cannot determine a value, ask the user instead of retrying with the same arguments.";

    /// <summary>
    /// Attempts to extract the parameter name from M.E.AI's marshaller error message.
    /// Format (10.4.1, verified via transcript): "The arguments dictionary is missing a value for the required parameter 'X'."
    /// </summary>
    private static string? TryExtractMissingParameterName(string message)
    {
        const string Marker = "required parameter '";
        var start = message.IndexOf(Marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += Marker.Length;
        var end = message.IndexOf('\'', start);
        if (end <= start)
        {
            return null;
        }

        return message[start..end];
    }
}
