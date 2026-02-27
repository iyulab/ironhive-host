using System.Text.Json;
using System.Text.Json.Serialization;

namespace IronHive.Cli.Core.Server;

// ── Requests (stdin → agent) ────────────────────────────────────────

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(UserMessageRequest), "user_message")]
[JsonDerivedType(typeof(HitlResponseRequest), "hitl_response")]
[JsonDerivedType(typeof(ShutdownRequest), "shutdown")]
[JsonDerivedType(typeof(ContextUpdateRequest), "context_update")]
public abstract record ServerRequest;

public record UserMessageRequest(string Content) : ServerRequest;

public record HitlResponseRequest(bool Approved, string? Reason = null) : ServerRequest;

public record ShutdownRequest() : ServerRequest;

public record ContextUpdateRequest(string? WorkingPath) : ServerRequest;

// ── Events (agent → stdout) ─────────────────────────────────────────

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SessionStartedEvent), "session_started")]
[JsonDerivedType(typeof(TextDeltaEvent), "text_delta")]
[JsonDerivedType(typeof(ToolStartEvent), "tool_start")]
[JsonDerivedType(typeof(ToolEndEvent), "tool_end")]
[JsonDerivedType(typeof(HitlRequestEvent), "hitl_request")]
[JsonDerivedType(typeof(TurnEndEvent), "turn_end")]
[JsonDerivedType(typeof(AgentSelectedEvent), "agent_selected")]
[JsonDerivedType(typeof(ErrorEvent), "error")]
[JsonDerivedType(typeof(PlanCreatedServerEvent), "plan_created")]
[JsonDerivedType(typeof(PlanStepStartedServerEvent), "plan_step_started")]
[JsonDerivedType(typeof(PlanStepCompletedServerEvent), "plan_step_completed")]
[JsonDerivedType(typeof(PlanCompletedServerEvent), "plan_completed")]
public abstract record ServerEvent;

public record SessionStartedEvent(string SessionId) : ServerEvent;

public record TextDeltaEvent(string Content) : ServerEvent;

public record ToolStartEvent(string Tool, JsonElement? Input = null) : ServerEvent;

public record ToolEndEvent(string Tool, bool Success) : ServerEvent;

public record HitlRequestEvent(string Id, string Action, string Target, string Description) : ServerEvent;

public record AgentSelectedEvent(string AgentName, double Confidence) : ServerEvent;

public record TurnEndEvent() : ServerEvent;

public record ErrorEvent(string Message) : ServerEvent;

public record PlanCreatedServerEvent(string PlanId, int StepCount, string[] StepDescriptions) : ServerEvent;

public record PlanStepStartedServerEvent(string PlanId, int StepIndex, string Description) : ServerEvent;

public record PlanStepCompletedServerEvent(string PlanId, int StepIndex, bool Success, string? Summary) : ServerEvent;

public record PlanCompletedServerEvent(string PlanId, bool Success, string Summary) : ServerEvent;
