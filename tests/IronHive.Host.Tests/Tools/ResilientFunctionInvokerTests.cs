using System.Text.Json;
using AwesomeAssertions;
using IronHive.Host.Tools;
using Microsoft.Extensions.AI;

namespace IronHive.Host.Tests.Tools;

/// <summary>
/// Unit tests for <see cref="ResilientFunctionInvoker"/> — the model-actionable
/// recovery directive.
///
/// <para>
/// A short directive ("Retry the call with ALL required parameters specified") is too
/// .NET-jargon-flavoured for a small/quantized model to act on: such models keep retrying
/// with empty arguments and never reach a non-empty answer, even with strengthened tool
/// descriptions and system prompt. The directive is therefore procedural language that
/// names a recovery procedure and explicitly forbids the empty-args retry pattern.
/// </para>
/// </summary>
public class ResilientFunctionInvokerTests
{
    private const string MarshallerMissingPathMessage =
        "The arguments dictionary is missing a value for the required parameter 'path'.";

    /// <summary>
    /// The parameter name the M.E.AI marshaller stamps on its ArgumentException — a
    /// literal string, not a C# parameter reference. Hoisted to a const so CA1507's
    /// nameof refactor doesn't fire on every test that needs to reproduce the shape.
    /// </summary>
    private const string MarshallerParamName = "arguments";

    /// <summary>
    /// AIFunction whose body unconditionally throws the exception M.E.AI's reflection
    /// marshaller raises when a required key is missing from the arguments dictionary.
    /// Reproduces the exact failure mode <see cref="ResilientFunctionInvoker"/> rescues.
    /// </summary>
    private sealed class MissingPathFunction : AIFunction
    {
        public override string Name => "read_file";

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            throw new ArgumentException(MarshallerMissingPathMessage, paramName: MarshallerParamName);
        }
    }

    private sealed class JsonFailureFunction : AIFunction
    {
        public override string Name => "edit_file";

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            throw new JsonException("Unexpected character at position 3.");
        }
    }

    private static FunctionInvocationContext ContextFor(AIFunction function) =>
        new()
        {
            Function = function,
            Arguments = new AIFunctionArguments()
        };

    private static async Task<string> InvokeAndGetDirectiveAsync(AIFunction function)
    {
        var invoker = ResilientFunctionInvoker.Create();
        var result = await invoker(ContextFor(function), CancellationToken.None);
        result.Should().BeOfType<string>(
            because: "marshaller failures must be rendered as a model-actionable string, not rethrown");
        return (string)result!;
    }

    [Fact]
    public async Task MissingRequiredParameter_DirectiveNamesToolAndParameter()
    {
        var directive = await InvokeAndGetDirectiveAsync(new MissingPathFunction());

        directive.Should().Contain("read_file",
            because: "the model needs to know which tool the rejection refers to");
        directive.Should().Contain("'path'",
            because: "the missing parameter name must be quoted explicitly so the model can locate it in its schema");
    }

    [Fact]
    public async Task MissingRequiredParameter_DirectiveUsesProceduralRecoveryLanguage()
    {
        // Contract: directive must read as a procedure, not a stack-trace fragment.
        // Each clause below is load-bearing for small-model self-correction (a short
        // directive is insufficient on Gemma 4 E4B at gguf:default, even with
        // strengthened tool descriptions).
        var directive = await InvokeAndGetDirectiveAsync(new MissingPathFunction());

        directive.Should().Contain("rejected",
            because: "the model must understand the previous attempt did not run");
        directive.Should().Contain("Look at",
            because: "small models benefit from an explicit instruction to re-read the user's message for the parameter value");
        directive.Should().Contain("Do NOT",
            because: "the empty-args retry pattern must be forbidden in language the model can act on (uppercase NOT is a Gemma/Llama-family attention anchor)");
        directive.Should().Contain("empty",
            because: "the failure mode being prevented (empty arguments) must be named so the model can recognize it");
        directive.Should().Contain("different tool",
            because: "the model must be given an alternative escape hatch — pick another tool — so it does not have to retry the same one");
    }

    [Fact]
    public async Task MissingRequiredParameter_DirectiveDoesNotEchoNetJargon()
    {
        // Negative regression — the prior directive used phrasing like "Retry the call
        // with ALL required parameters specified" which empirically failed to drive
        // self-correction on small models. Make sure no rewrite accidentally restores it.
        var directive = await InvokeAndGetDirectiveAsync(new MissingPathFunction());

        directive.Should().NotContain("ALL required parameters specified",
            because: "the .NET-jargon phrasing does not drive self-correction on small models");
    }

    [Fact]
    public async Task ArgumentExceptionWithoutParameterName_FallsBackToGenericDirective()
    {
        // ParamName is the marshaller-emitted literal "arguments" (not a code symbol),
        // routed through a const so CA1507 doesn't flag it as a refactor target.
        var fn = new ThrowingFunction("read_file",
            new ArgumentException("invalid argument shape", paramName: MarshallerParamName));
        var directive = await InvokeAndGetDirectiveAsync(fn);

        directive.Should().Contain("read_file");
        directive.Should().Contain("invalid argument shape",
            because: "when no parameter name is parseable the underlying detail must still reach the model");
    }

    [Fact]
    public async Task JsonException_DirectiveExplainsParseFailureWithToolName()
    {
        var directive = await InvokeAndGetDirectiveAsync(new JsonFailureFunction());

        directive.Should().Contain("edit_file");
        directive.Should().Contain("JSON",
            because: "the parse-failure case must name the failure type so the model picks the right corrective action");
    }

    [Fact]
    public async Task UnrelatedArgumentException_IsNotSwallowed()
    {
        // ResilientFunctionInvoker is scoped to ParamName=="arguments". Any other
        // ArgumentException must propagate so consumer code can observe genuine bugs.
        var fn = new ThrowingFunction("foo",
            new ArgumentException("not the marshaller", paramName: "somethingElse"));
        var invoker = ResilientFunctionInvoker.Create();

        Func<Task> action = async () => await invoker(ContextFor(fn), CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .Where(ex => ex.ParamName == "somethingElse");
    }

    [Fact]
    public async Task MiddlewareForm_WhenTheRestOfThePipelineThrowsTheMarshallerError_ReturnsTheSameDirective()
    {
        var middleware = new ResilientArgumentsMiddleware();
        var fn = new MissingPathFunction();

        var result = await middleware.InvokeAsync(
            ContextFor(fn),
            (context, ct) => context.Function.InvokeAsync(context.Arguments, ct),
            CancellationToken.None);

        result.Should().Be(await InvokeAndGetDirectiveAsync(fn),
            because: "the delegate and the middleware share one implementation");
    }

    [Fact]
    public async Task MiddlewareForm_PassesAResultThrough_AndLetsUnrelatedExceptionsPropagate()
    {
        var middleware = new ResilientArgumentsMiddleware();
        var context = ContextFor(new MissingPathFunction());

        var passed = await middleware.InvokeAsync(context, (_, _) => new ValueTask<object?>("tool output"), CancellationToken.None);
        passed.Should().Be("tool output");

        Func<Task> action = async () => await middleware.InvokeAsync(
            context,
            (_, _) => throw new InvalidOperationException("tool bug"),
            CancellationToken.None);
        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class ThrowingFunction : AIFunction
    {
        private readonly string _name;
        private readonly Exception _toThrow;

        public ThrowingFunction(string name, Exception toThrow)
        {
            _name = name;
            _toThrow = toThrow;
        }

        public override string Name => _name;

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            throw _toThrow;
        }
    }
}
