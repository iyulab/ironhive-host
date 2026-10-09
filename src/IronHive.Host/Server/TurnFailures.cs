using IronHive.Agent.ErrorRecovery;
using IronHive.Host.Protocol;

namespace IronHive.Host.Server;

/// <summary>
/// How a server runner reports a turn that ended in an exception — one rule for the stdio and the HTTP runner.
/// </summary>
internal static class TurnFailures
{
    /// <summary>
    /// The wire event for a turn that threw. The class comes from the agent's failure classifier, so the wire and error
    /// recovery read a failure the same way.
    /// </summary>
    /// <param name="exception">What the turn threw.</param>
    /// <param name="classifier">The agent's failure classifier.</param>
    public static ErrorEvent ToErrorEvent(Exception exception, IErrorRecoveryService classifier)
    {
        var category = classifier.AnalyzeException(exception).Error.Category;

        // A cancellation reaches here only when the client did not ask for it (the runner handles the client's cancel
        // apart): something stopped the turn on a clock — an HTTP client's timeout, a stream's, a provider SDK's own.
        if (exception is OperationCanceledException && category == ErrorCategory.Unknown)
        {
            category = ErrorCategory.Timeout;
        }

        return new ErrorEvent(exception.Message) { Code = WireName(category) };
    }

    /// <summary>The protocol spelling of a failure class (<see cref="ErrorCodes"/>).</summary>
    internal static string WireName(ErrorCategory category) => category switch
    {
        ErrorCategory.ContextLimit => ErrorCodes.ContextLimit,
        ErrorCategory.RateLimit => ErrorCodes.RateLimit,
        ErrorCategory.Billing => ErrorCodes.Billing,
        ErrorCategory.Authentication => ErrorCodes.Auth,
        ErrorCategory.Network => ErrorCodes.Network,
        ErrorCategory.Timeout => ErrorCodes.Timeout,
        ErrorCategory.InvalidInput => ErrorCodes.RequestRejected,
        ErrorCategory.ToolExecution => ErrorCodes.ToolExecution,
        ErrorCategory.FileSystem => ErrorCodes.FileSystem,
        ErrorCategory.Internal => ErrorCodes.Internal,
        ErrorCategory.Unknown => ErrorCodes.Unknown,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "A failure class without a wire name."),
    };
}
