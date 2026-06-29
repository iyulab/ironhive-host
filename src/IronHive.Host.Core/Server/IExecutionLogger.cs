using IronHive.Agent.Loop;

namespace IronHive.Host.Core.Server;

/// <summary>
/// Records tool execution steps to a log for traceability and debugging.
/// </summary>
public interface IExecutionLogger
{
    int TurnCount { get; }
    int TotalSteps { get; }

    void Initialize(string logFilePath);
    Task BeginTurnAsync(string userPrompt);
    Task ProcessChunkAsync(AgentResponseChunk chunk);
    Task EndTurnAsync(int? responseLength = null);
}
