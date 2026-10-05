using IronHive.Agent.Loop;

namespace IronHive.Host.Server;

/// <summary>
/// Records tool execution steps to a log for traceability and debugging.
/// </summary>
public interface IExecutionLogger
{
    int TurnCount { get; }
    int TotalSteps { get; }

    void Initialize(string logFilePath);
    Task BeginTurnAsync(string userPrompt, CancellationToken cancellationToken = default);
    Task ProcessChunkAsync(AgentResponseChunk chunk, CancellationToken cancellationToken = default);
    Task EndTurnAsync(int? responseLength = null, CancellationToken cancellationToken = default);
}
