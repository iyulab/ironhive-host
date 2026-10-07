using System.Text.Json.Serialization;

namespace IronHive.Host.Session;

/// <summary>
/// Base class for JSONL transcript entries.
/// Compatible with Claude Code transcript format.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SessionStartEntry), "session_start")]
[JsonDerivedType(typeof(SessionEndEntry), "session_end")]
[JsonDerivedType(typeof(SessionResumeEntry), "session_resume")]
[JsonDerivedType(typeof(UserMessageEntry), "user_message")]
[JsonDerivedType(typeof(AssistantMessageEntry), "assistant_message")]
[JsonDerivedType(typeof(ToolUseEntry), "tool_use")]
[JsonDerivedType(typeof(ToolResultEntry), "tool_result")]
[JsonDerivedType(typeof(ApprovalWaitEntry), "approval_wait")]
[JsonDerivedType(typeof(ApprovalClosedEntry), "approval_closed")]
public abstract record TranscriptEntry
{
    /// <summary>
    /// When this entry was recorded.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public required DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Session start entry.
/// </summary>
public record SessionStartEntry : TranscriptEntry
{
    /// <summary>
    /// The session ID.
    /// </summary>
    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }

    /// <summary>
    /// The model being used.
    /// </summary>
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    /// <summary>
    /// The project path.
    /// </summary>
    [JsonPropertyName("project_path")]
    public required string ProjectPath { get; init; }

    /// <summary>
    /// The current working directory.
    /// </summary>
    [JsonPropertyName("cwd")]
    public string? Cwd { get; init; }

    /// <summary>
    /// CLI version.
    /// </summary>
    [JsonPropertyName("version")]
    public string? Version { get; init; }
}

/// <summary>
/// Session end entry.
/// </summary>
public record SessionEndEntry : TranscriptEntry
{
    /// <summary>
    /// Reason for session end.
    /// </summary>
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }
}

/// <summary>
/// Session resume entry (when continuing a previous session).
/// </summary>
public record SessionResumeEntry : TranscriptEntry
{
    /// <summary>
    /// The session ID being resumed.
    /// </summary>
    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }

    /// <summary>
    /// Previous session ID if this is a fork.
    /// </summary>
    [JsonPropertyName("forked_from")]
    public string? ForkedFrom { get; init; }
}

/// <summary>
/// User message entry.
/// </summary>
public record UserMessageEntry : TranscriptEntry
{
    /// <summary>
    /// The message content.
    /// </summary>
    [JsonPropertyName("content")]
    public required string Content { get; init; }
}

/// <summary>
/// Assistant message entry.
/// </summary>
public record AssistantMessageEntry : TranscriptEntry
{
    /// <summary>
    /// The message content.
    /// </summary>
    [JsonPropertyName("content")]
    public required string Content { get; init; }
}

/// <summary>
/// Tool use entry.
/// </summary>
public record ToolUseEntry : TranscriptEntry
{
    /// <summary>
    /// The tool name.
    /// </summary>
    [JsonPropertyName("tool")]
    public required string Tool { get; init; }

    /// <summary>
    /// The tool input/arguments.
    /// </summary>
    [JsonPropertyName("input")]
    public required object Input { get; init; }

    /// <summary>
    /// Unique ID for this tool use.
    /// </summary>
    [JsonPropertyName("tool_use_id")]
    public required string ToolUseId { get; init; }
}

/// <summary>
/// Tool result entry.
/// </summary>
public record ToolResultEntry : TranscriptEntry
{
    /// <summary>
    /// The tool use ID this result corresponds to.
    /// </summary>
    [JsonPropertyName("tool_use_id")]
    public required string ToolUseId { get; init; }

    /// <summary>
    /// The output from the tool.
    /// </summary>
    [JsonPropertyName("output")]
    public required string Output { get; init; }

    /// <summary>
    /// Whether this is an error result.
    /// </summary>
    [JsonPropertyName("is_error")]
    public bool IsError { get; init; }
}

/// <summary>
/// A tool call waiting for a human answer: the approval request as it was sent to the client. Written when the request
/// is published, so a process that restarts before the answer can offer it again. Closed by
/// <see cref="ApprovalClosedEntry"/> when the answer (or its timeout) settles the call.
/// </summary>
public record ApprovalWaitEntry : TranscriptEntry
{
    /// <summary>The request id the client answers with.</summary>
    [JsonPropertyName("request_id")]
    public required string RequestId { get; init; }

    /// <summary>The tool call the request is about (its <see cref="ToolUseEntry.ToolUseId"/>).</summary>
    [JsonPropertyName("tool_use_id")]
    public required string ToolUseId { get; init; }

    /// <summary>The tool name.</summary>
    [JsonPropertyName("tool")]
    public required string Tool { get; init; }

    /// <summary>The call's arguments, as sent.</summary>
    [JsonPropertyName("arguments")]
    public System.Text.Json.JsonElement? Arguments { get; init; }

    /// <summary>What the request asks to allow.</summary>
    [JsonPropertyName("action")]
    public string Action { get; init; } = string.Empty;

    /// <summary>The call's primary target (a path, a command, a URL).</summary>
    [JsonPropertyName("target")]
    public string Target { get; init; } = string.Empty;

    /// <summary>The prompt shown to the human.</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>The risk level, lower case.</summary>
    [JsonPropertyName("level")]
    public string? Level { get; init; }
}

/// <summary>
/// The approval request <see cref="RequestId"/> was settled: answered, rejected or timed out. Its call's result follows
/// as a <see cref="ToolResultEntry"/>.
/// </summary>
public record ApprovalClosedEntry : TranscriptEntry
{
    /// <summary>The request id of the settled <see cref="ApprovalWaitEntry"/>.</summary>
    [JsonPropertyName("request_id")]
    public required string RequestId { get; init; }
}
