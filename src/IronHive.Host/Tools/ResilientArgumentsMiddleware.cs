using System.Text.Json;
using IronHive.Agent.Invocation;
using Microsoft.Extensions.AI;

namespace IronHive.Host.Tools;

/// <summary>
/// A tool invocation step that turns a call whose arguments do not bind to the tool's parameters into a
/// model-actionable recovery directive instead of a failed call — the <see cref="IToolInvocationMiddleware"/> form of
/// <see cref="ResilientFunctionInvoker"/> (both share this implementation).
/// </summary>
/// <remarks>
/// <para>
/// <b>What it handles:</b> the exceptions M.E.AI's parameter marshaller raises when the tool is invoked with arguments
/// it cannot bind — an <see cref="ArgumentException"/> whose <see cref="ArgumentException.ParamName"/> is
/// <c>"arguments"</c> (a required parameter is missing, typically from an empty arguments object) and a
/// <see cref="JsonException"/> (a value does not convert to the parameter's type). The model receives a numbered
/// procedure naming the missing parameter, where to look for its value, and what to do instead of retrying with the
/// same arguments. Every other exception passes through unchanged. The directive is a plain string result, so a loop
/// reports the call as completed and <see cref="RepeatedCallGuardMiddleware"/> counts it: a model that keeps sending
/// the same empty arguments is refused after that guard's limit rather than looping until the iteration cap.
/// </para>
/// <para>
/// <b>Relation to <see cref="ArgumentParseFailureMiddleware"/>:</b> the two handle different failures and never the
/// same call. <see cref="ArgumentParseFailureMiddleware"/> acts before the tool runs, when the model's arguments text
/// could not be parsed at all (<see cref="FunctionCallContent.Exception"/> is set), and refuses the call. This step acts
/// when the arguments were parsed but do not fit the tool's parameters, which is only discovered when the tool is
/// invoked. Placed after it, this step never sees an unparseable call; without it (or with
/// <see cref="ToolInvocationOptions.RefuseUnparseableArguments"/> off, or under a plain <c>UseFunctionInvocation</c>
/// client), such a call reaches the tool with empty or partial arguments and this step answers the marshaller's error.
/// </para>
/// <para>
/// <b>Placement:</b> register it last, so it sits directly around the tool. It catches what the rest of the pipeline
/// throws below it (later steps and the result stage) as well as the tool itself, and
/// <see cref="RepeatedErrorGuardMiddleware"/> placed before it only counts the errors it lets through.
/// </para>
/// <para>
/// <b>Directive shape:</b> a numbered procedure rather than a single .NET-flavoured retry sentence. A one-sentence
/// retry hint does not change the behaviour of small quantized models, even with strengthened tool descriptions; the
/// procedural form shifts the burden from "interpret a stack-trace fragment" to "follow a procedure," which small
/// instruction-tuned models handle better.
/// </para>
/// <para>
/// <b>Reference:</b> https://learn.microsoft.com/dotnet/ai/how-to/handle-invalid-tool-input (canonical Microsoft
/// pattern).
/// </para>
/// </remarks>
public sealed class ResilientArgumentsMiddleware : IToolInvocationMiddleware
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(FunctionInvocationContext context, ToolInvocationNext next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        try
        {
            return await next(context, cancellationToken);
        }
        catch (ArgumentException ex) when (ex.ParamName == "arguments")
        {
            // Marshaller validation failure. Extract the parameter name when present
            // and synthesize a procedural recovery directive the model can act on.
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
    }

    /// <summary>
    /// Procedural recovery directive for the "required parameter missing" case.
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
    /// Procedural recovery directive for the "marshaller raised but no parameter
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
    /// Format: "The arguments dictionary is missing a value for the required parameter 'X'."
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
