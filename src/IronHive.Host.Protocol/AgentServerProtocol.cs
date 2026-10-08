using System.Text.Json;
using System.Text.Json.Serialization;

namespace IronHive.Host.Protocol;

// ── Requests (stdin → agent) ────────────────────────────────────────

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(UserMessageRequest), "user_message")]
[JsonDerivedType(typeof(HitlResponseRequest), "hitl_response")]
[JsonDerivedType(typeof(ShutdownRequest), "shutdown")]
[JsonDerivedType(typeof(ContextUpdateRequest), "context_update")]
[JsonDerivedType(typeof(CancelRequest), "cancel")]
public abstract record ServerRequest;

/// <summary>
/// A user turn. <paramref name="Options"/> narrows or tunes this turn only; a request without it runs
/// the agent exactly as configured.
/// </summary>
public record UserMessageRequest(
    string Content,
    string? Model = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TurnOptions? Options = null) : ServerRequest;

/// <summary>
/// Per-turn override a client sends with a <see cref="UserMessageRequest"/>. Every field is optional
/// and the merge is field by field: a set field replaces the agent's configured value for this turn
/// only, an unset (<c>null</c>) field keeps the agent's value, and the next request without options
/// is back on the agent's configuration. Nothing here is remembered across turns.
/// </summary>
/// <param name="ToolNames">Names of the tools this turn may use — a subset of the tools registered on
/// the agent. An empty array means no tools this turn. A name that is not registered is rejected with
/// an <see cref="ErrorEvent"/> rather than dropped, so a client cannot believe a turn was restricted
/// when it was not.</param>
/// <param name="ToolMode"><c>auto</c> (the model decides), <c>none</c> (no tool call this turn),
/// <c>require_any</c> (the model must call some tool) or <c>require:&lt;tool name&gt;</c> (it must
/// call that registered tool).</param>
/// <param name="ReasoningEffort"><c>none</c>, <c>low</c>, <c>medium</c>, <c>high</c> or
/// <c>extra_high</c>, for models that expose a reasoning effort.</param>
/// <param name="Temperature">Sampling temperature for this turn.</param>
/// <param name="MaxOutputTokens">Output token cap for this turn.</param>
/// <param name="Instructions">System instructions for this turn, kept apart from the user's text: the client's own
/// directives (an operator's standing instructions, rules it compiled) beside <see cref="UserMessageRequest.Content"/>
/// that may come from someone untrusted. They reach the model as Microsoft.Extensions.AI's
/// <c>ChatOptions.Instructions</c> — the providers send them as system text alongside the agent's system prompt — and
/// are never added to the conversation history, so a later turn without them does not carry them and a guard that reads
/// the user's text does not read them. Send them again with every turn that should have them.</param>
public record TurnOptions(
    string[]? ToolNames = null,
    string? ToolMode = null,
    string? ReasoningEffort = null,
    float? Temperature = null,
    int? MaxOutputTokens = null,
    string? Instructions = null);

/// <summary>
/// A client's answer to a <see cref="HitlRequestEvent"/>.
/// </summary>
/// <param name="Approved">Run the call.</param>
/// <param name="Reason">Why it was declined; the model reads it.</param>
/// <param name="Id">The <see cref="HitlRequestEvent.Id"/> being answered. Without it the answer resolves the request only
/// when exactly one is waiting.</param>
/// <param name="ModifiedArguments">Arguments to run the call with instead of the model's, when the person edited them.</param>
/// <param name="AlwaysApprove">The person asked to approve this kind of call from now on (the approver decides what that
/// covers).</param>
public record HitlResponseRequest(
    bool Approved,
    string? Reason = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Id = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Dictionary<string, JsonElement>? ModifiedArguments = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool AlwaysApprove = false) : ServerRequest;

public record ShutdownRequest() : ServerRequest;

public record ContextUpdateRequest(string? WorkingPath, string[]? SelectedItems = null) : ServerRequest;

/// <summary>
/// Requests cancellation of the currently processing message.
/// The session remains alive; subsequent messages are accepted normally.
/// </summary>
public record CancelRequest() : ServerRequest;

// ── Events (agent → stdout) ─────────────────────────────────────────

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SessionStartedEvent), "session_started")]
[JsonDerivedType(typeof(TextDeltaEvent), "text_delta")]
[JsonDerivedType(typeof(AddendumEvent), "addendum")]
[JsonDerivedType(typeof(ToolStartEvent), "tool_start")]
[JsonDerivedType(typeof(ToolEndEvent), "tool_end")]
[JsonDerivedType(typeof(HitlRequestEvent), "hitl_request")]
[JsonDerivedType(typeof(TurnEndEvent), "turn_end")]
[JsonDerivedType(typeof(AgentSelectedEvent), "agent_selected")]
[JsonDerivedType(typeof(ErrorEvent), "error")]
[JsonDerivedType(typeof(ThinkingDeltaEvent), "thinking_delta")]
[JsonDerivedType(typeof(PlanCreatedServerEvent), "plan_created")]
[JsonDerivedType(typeof(PlanStepStartedServerEvent), "plan_step_started")]
[JsonDerivedType(typeof(PlanStepCompletedServerEvent), "plan_step_completed")]
[JsonDerivedType(typeof(PlanCompletedServerEvent), "plan_completed")]
[JsonDerivedType(typeof(FallbackServerEvent), "fallback")]
public abstract record ServerEvent;

public record SessionStartedEvent(string SessionId) : ServerEvent;

public record TextDeltaEvent(string Content) : ServerEvent;

/// <summary>
/// Text a turn observer appended after the turn's output — a note about the turn, not the model's
/// words. Emitted at most once per turn, after the last <see cref="TextDeltaEvent"/> and before
/// <see cref="TurnEndEvent"/>, and only when an observer appended something. It is never part of
/// the conversation history; a client renders it apart from the assistant text (and may leave it
/// out of what it aggregates or persists as the model's answer).
/// </summary>
public record AddendumEvent(string Content) : ServerEvent;

/// <summary>
/// Thinking/reasoning content delta from extended-thinking models.
/// </summary>
public record ThinkingDeltaEvent(string Content) : ServerEvent;

public record ToolStartEvent(string Tool, JsonElement? Input = null, string? CallId = null) : ServerEvent;

/// <summary>
/// Signals completion of a tool call.
/// <see cref="Output"/> carries the stringified return value (capped at 8 KB); null when the tool returned null/empty.
/// On failure, <see cref="Output"/> carries "{ExceptionType}: {message}".
/// <see cref="CallId"/> matches the corresponding <see cref="ToolStartEvent.CallId"/> for start/end pairing.
/// </summary>
public record ToolEndEvent(string Tool, bool Success, string? Output = null, string? CallId = null) : ServerEvent;

/// <summary>
/// A tool call that needs a person's approval before it runs. The call waits; answer with a
/// <see cref="HitlResponseRequest"/> carrying this <see cref="Id"/>. Several requests can wait at once.
/// </summary>
/// <param name="Id">Identifies this request; echo it in the answer.</param>
/// <param name="Action">What the call would do (the verdict's reason, or the tool name).</param>
/// <param name="Target">The call's primary argument (a path, command, url or query), or empty.</param>
/// <param name="Description">The question to show the person.</param>
/// <param name="ToolName">The tool being called.</param>
/// <param name="Arguments">The call's arguments.</param>
/// <param name="CallId">The call's id — the same as the <see cref="ToolStartEvent.CallId"/> of the call it is about.</param>
/// <param name="Level">Risk level: <c>low</c>, <c>medium</c>, <c>high</c> or <c>critical</c>.</param>
public record HitlRequestEvent(
    string Id,
    string Action,
    string Target,
    string Description,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ToolName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Arguments = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CallId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Level = null) : ServerEvent;

public record AgentSelectedEvent(string AgentName, double Confidence) : ServerEvent;

/// <summary>
/// Signals completion of a turn. <see cref="InputTokens"/>/<see cref="OutputTokens"/>, when
/// present, are summed across every model round-trip within the turn (a single turn commonly
/// makes several via tool-calling) — null when the underlying provider reported no usage data
/// for any round-trip in the turn.
/// </summary>
public record TurnEndEvent(long? InputTokens = null, long? OutputTokens = null) : ServerEvent
{
    /// <summary>
    /// <see cref="InputTokens"/> + <see cref="OutputTokens"/>; null when both are null.
    /// </summary>
    public long? TotalTokens => InputTokens is null && OutputTokens is null
        ? null
        : (InputTokens ?? 0) + (OutputTokens ?? 0);

    /// <summary>
    /// How many of <see cref="InputTokens"/> the provider read from its prompt cache; null when it reported none.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? CachedInputTokens { get; init; }

    /// <summary>
    /// Why the turn ended, when it ended without an error: <c>completed</c>, <c>output_limit</c>, <c>content_filter</c>,
    /// <c>tool_terminated</c>, <c>awaiting_host_tools</c> or <c>step_limit</c>. Null when the turn failed or was cancelled
    /// (an <see cref="ErrorEvent"/> says why), or the turn produced no record.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StopReason { get; init; }

    /// <summary>Wall-clock time of the turn in milliseconds, from its first event to this one; null when unknown.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? DurationMs { get; init; }
}

public record ErrorEvent(string Message) : ServerEvent;

/// <summary>
/// LLM provider retry / fallback / exhaustion notice.
/// Kind: "retry" | "fallback" | "exhausted".
/// Category: "transient" | "rate_limit" | "auth" | "fatal".
/// </summary>
public sealed record FallbackServerEvent(
    string Kind,
    int ProviderIndex,
    int TotalProviders,
    string Category,
    string Message,
    int Attempt = 0,
    int MaxAttempts = 0) : ServerEvent;

public record PlanCreatedServerEvent(string PlanId, int StepCount, string[] StepDescriptions) : ServerEvent;

public record PlanStepStartedServerEvent(string PlanId, int StepIndex, string Description) : ServerEvent;

public record PlanStepCompletedServerEvent(string PlanId, int StepIndex, bool Success, string? Summary) : ServerEvent;

public record PlanCompletedServerEvent(string PlanId, bool Success, string Summary) : ServerEvent;
