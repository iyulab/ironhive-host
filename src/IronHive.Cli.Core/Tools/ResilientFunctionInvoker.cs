using System.Text.Json;
using Microsoft.Extensions.AI;

namespace IronHive.Cli.Core.Tools;

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
/// the .NET-jargon-heavy exception into a directive sentence the model can act on
/// ("missing required parameter 'X' — retry with all required parameters"). Because
/// the delegate returns a string instead of throwing, M.E.AI synthesizes a
/// <see cref="FunctionResultContent"/> and feeds it back to the model as the next-turn
/// input. The model gets up to MaximumIterationsPerRequest rounds to self-correct.
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
                // and reflect it back to the model with an actionable instruction.
                var paramName = TryExtractMissingParameterName(ex.Message);
                return paramName is not null
                    ? $"Tool '{context.Function.Name}' rejected the call: required parameter '{paramName}' was missing. " +
                      $"Retry the call with ALL required parameters specified, including '{paramName}'."
                    : $"Tool '{context.Function.Name}' rejected the call due to invalid arguments. " +
                      $"Verify each required parameter is present and retry. Detail: {ex.Message}";
            }
            catch (JsonException ex)
            {
                return $"Tool '{context.Function.Name}' could not parse the arguments JSON: {ex.Message}. " +
                       $"Retry with a valid JSON object whose keys match the parameter schema.";
            }
        };
    }

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
