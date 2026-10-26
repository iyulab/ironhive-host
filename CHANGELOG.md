# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to 0.x pre-1.0 versioning (breaking changes are expected).

## Unreleased

### Added
- **`run --server` writes one line on standard error for each MCP plugin attempt that fails and each plugin that connects**
  (`ironhive: MCP plugin 'name' is not connected: <reason>` / `... connected`), so an operator can see that a server is still not attached.
  Until now the CLI said nothing: the state was visible only on the server's own side.

## 0.72.0 — 2026-10-26

### Added
- **`run --server` connects an MCP server that was not ready at start, or that restarted, by itself, and offers its tools to the running
  session.** A server that failed `initialize` at start used to stay unconnected until the process ended, its tools answering «Requested function
  … not found». Now it is tried again every 30 s on a growing wait (5 s up to 5 min), its tools join the loop's list from the next turn, a
  plugin found unhealthy by the 5-minute check is replaced by a new connection (its tools are swapped, not duplicated), and one that
  disconnects has its tools taken out. A plugin entry with `autoReconnect: false` is left alone (the key was read from `plugins.json` before but
  did nothing). `McpHealthCheckService` takes a `reconnectExpression` (default `@every 30s`); `McpToolSynchronizer` keeps the tool lists in
  step; the loop's tool list is a `LiveToolList`.
- **`tool_end` says when a call named a tool the turn did not offer**: `Refusal: unknown_tool` (`ToolRefusalCodes.UnknownTool`). With
  IronHive.Agent 0.76.0 such calls go through the loop guards, so a model that keeps calling a missing tool is stopped with
  `repeated_error` instead of looping to the step limit (the server would otherwise have thrown on the new refusal kind).

### Dependencies
- `IronHive.Agent` / `IronHive.Agent.Ironbees` / `IronHive.DeepResearch` 0.75.0 -> 0.77.0.
- Re-pinned sibling package(s) `Ironbees.Core` 0.30.0 -> 0.30.1, `TokenMeter` 0.15.0 -> 0.16.0.
- Re-pinned sibling package(s) `LMSupply.Generator` 0.136.0 -> 0.137.0. LMSupply 0.137.0 changes the token estimate it falls back to when a local server reports no usage: Korean text now counts about 2.5x more tokens than before, English slightly fewer, digits more.
- Re-pinned third-party packages: 8 .NET servicing pins (Microsoft.Extensions) 10.0.13 -> 10.0.14 (the 10.0.14 security servicing release); `Spectre.Console` 0.55.2 -> 0.57.2; `Spectre.Console.Cli` 0.55.0 -> 0.57.2.

## 0.71.0 — 2026-10-24

### Changed
- **Breaking: `instructionsAsSystemMessage` (true/false) is now `instructionsPlacement`** on `lmstudio`, `ollama` and
  `openaiCompatible`, and `gpuStack` gains it: `systemAppend` (default) · `separateSystemMessage` · `leadingUserTurn`.
  `leadingUserTurn` puts a turn's own instructions at the head of its last user message, so the prompt, the tools and the
  earlier turns stay in a local server's prefix cache on every chat template (Gemma 3, the DeepSeek-R1 distills and gpt-oss
  included). Migration: `instructionsAsSystemMessage: true` → `instructionsPlacement: separateSystemMessage`. `ironhive set`
  accepts the key on all four.

### Dependencies
- IronHive 0.70.0 -> 0.71.0 (`InstructionsPlacement`).

## 0.70.0 — 2026-10-24

### Added
- **`instructionsAsSystemMessage` on `lmstudio`, `ollama` and `openaiCompatible`** (default off): a turn's own instructions go
  in a second system message after the agent's prompt instead of at its end, so a server whose chat template renders tools
  inside the first system turn (Qwen 2.5/3, Llama 3.x) keeps the tools cached when only the instructions change. Leave it off
  for Gemma 3, the DeepSeek-R1 distills and gpt-oss, whose templates reject or drop a second system message.

### Fixed
- **The agent's system prompt reaches the model when the turn also carries AGENTS.md, skill sections, a scratchpad or a compaction
  summary.** Those blocks travel as further system messages, and the provider path (IronHive 0.70.0's `AsChatClient()`) kept only the
  last one, so the model saw the block instead of the prompt. Now every block follows the prompt.

### Changed
- **Breaking: a turn's own instructions (`TurnOptions.Instructions`) come after the agent's system prompt instead of before it**
  (IronHive 0.70.0). The prompt stays the leading text, so a provider's prefix cache keeps it when only the instructions change.
  Migration: text that must frame the prompt belongs in the prompt.

### Dependencies
- Re-pinned sibling package(s) `IronHive.Abstractions` 0.69.0 -> 0.70.0, `IronHive.Core` 0.69.0 -> 0.70.0, `IronHive.Extensions.AI` 0.69.0 -> 0.70.0, `IronHive.Providers.Anthropic` 0.69.0 -> 0.70.0, `IronHive.Providers.GoogleAI` 0.69.0 -> 0.70.0, `IronHive.Providers.OpenAI` 0.69.0 -> 0.70.0, `IronHive.Providers.OpenAI.Compatible` 0.69.0 -> 0.70.0.
- Re-pinned sibling package(s) `IronHive.Agent` 0.74.0 -> 0.75.0, `IronHive.Agent.Ironbees` 0.74.0 -> 0.75.0, `IronHive.DeepResearch` 0.74.0 -> 0.75.0.

## 0.69.0 — 2026-10-23

### Changed
- **Breaking: the commands the model runs through the shell tool no longer see the provider API keys the host reads.**
  `GPUSTACK_API_KEY`, `OPENAI_API_KEY`, `ANTHROPIC_API_KEY`, `GOOGLEAI_API_KEY`, `GOOGLE_API_KEY`, `XAI_API_KEY` and
  `OPENAI_COMPATIBLE_API_KEY` are removed from every `ExecuteCommand` environment, so `env` or `printenv` no longer prints them
  into the tool result (and from there into the transcript and the logs). Everything else is still inherited. New `shell`
  section in `config.yaml`: `environment.remove` hides more variables, `environment.passthrough` keeps a listed one
  (`ShellHostConfig`, `ConfigurationManager.ProviderCredentialVariables`; `BuiltInTools.GetAll` takes the shell tool options).
  Migration: a task whose commands call the same provider with the host's key lists that variable under
  `shell.environment.passthrough`.

### Dependencies
- `IronHive.Agent` / `.Ironbees` / `IronHive.DeepResearch` 0.73.0 -> 0.74.0: a turn whose provider call fails after a tool ran is no
  longer sent again from the start (the tools would run twice) — the per-call retry this host already installs still retries a failed
  model call inside the turn; a call the model made at the step limit to a wrap-up finishing tool runs.

## 0.68.0 — 2026-10-23

### Added
- **An MCP server that needs a person before a tool call can finish now asks one, instead of the call failing.** A server that
  answers `tools/call` with `input_required` (a URL-mode elicitation and a `requestState`) or sends `elicitation/create`
  reaches a person: the terminal shows the server's message and link and asks «Done» / «Decline»; `run --server` sends an
  `elicitation_request` event (`id`, `plugin`, `mode`, `message`, `url`, `elicitation_id`) and waits for the client's
  `elicitation_response` (`id`, `action`: `accept` · `decline` · `cancel`). On `accept` the same call is sent again with the
  server's state and completes. The wait stays inside the turn and ends as `cancel` after `approval.timeoutSeconds`, when no
  client is attached, or when the client disconnects; a form-mode request is declined. `HitlBridge` implements the agent's
  `IMcpElicitationHandler` for hosts that embed the runners (`ResolveElicitation` delivers the client's answer).
- **`wrapped_up` stop reason.** A turn the agent closed at its step limit with a wrap-up request
  (`AgentOptions.WrapUpAtStepLimit`) ends with `stop_reason` `wrapped_up` (`StopReasons.WrappedUp`); `ironhive run` exits 2 for
  it, as for `step_limit`.

### Fixed
- **`ToolRefusalCodes.InvalidArguments` documents both causes** — arguments that did not parse, or a required parameter left
  out (the second since IronHive.Agent 0.72.0). Documentation only; the code value is unchanged.

### Dependencies
- `IronHive.Agent` / `.Ironbees` / `IronHive.DeepResearch` 0.72.0 -> 0.73.0 (MCP elicitation handler, wrap-up at the step limit).
- `ModelContextProtocol` 2.2.0 -> 2.3.0, `Microsoft.Extensions.AI.OpenAI` 10.10.1 -> 10.11.0, `OpenTelemetry` / `.Exporter.OpenTelemetryProtocol` 1.20.0 -> 1.20.1. Test tooling: xunit.v3 4.0.2, MTP HangDump 2.5.1, coverlet.MTP 10.1.0, `Microsoft.Extensions.TimeProvider.Testing` 10.11.0.

## 0.67.1 — 2026-10-23

### Fixed
- **A local LMSupply model configured by an alias answers again.** In 0.67.0 every request through the LMSupply provider failed
  with «This client answers with model '…'; the request names 'gguf:…'» when the model was configured by an alias, because the
  agent sends the configured id as `ChatOptions.ModelId` and LMSupply's chat client accepts only the model's own id. The provider
  now passes a request naming the id it loaded the model under on to the model as the model's own id. A request naming another
  model is still refused.

## 0.67.0 — 2026-10-23

### Added
- **A transient provider failure on one model call no longer ends the turn.** The chat pipeline tries a model call again once
  when it fails before any of it reached the caller with a 502/504 from the gateway, a refused or reset connection, or a
  response stream that ended before it began (`TransientRetryChatClient`, around the provider client). A failure after part of a
  streamed answer was shown, and any refusal, still ends the turn as before.
- **`ironhive run --json` says whose failure an error was: `error_kind` is `"provider"` or `"run"`.** `"provider"` covers no
  response (connection, TLS, a body cut short), a 5xx / 408 / 429 status, a rate limit or billing refusal and an error the
  server sent inside the stream — running again later can succeed; everything else (configuration, another 4xx, a context
  overflow, a defect) is `"run"`. `null` when the run did not stop with an error. A script or benchmark harness no longer has
  to read the `error` message to tell an unreachable model from a failed run.

### Changed
- **Breaking: the local LMSupply provider runs on LMSupply's own chat client** (`generator.AsChatClient()`, LMSupply.Generator
  0.135.0+). The public `LMSupplyChatClient` is removed; the provider still answers the model's context window to the token
  budget. Behaviour is the same (roles, `Instructions`, tools, streaming, usage), with three differences.
  - `MaxOutputTokens` now caps the generated tokens (`MaxNewTokens`) rather than prompt plus output.
  - A call without `ChatOptions` sends LMSupply's default options instead of none.
  - The repetition penalty and min-p no longer come from `ChatOptions.AdditionalProperties` (`repetition_penalty`, `min_p`).

  Migration: `new LMSupplyChatClient(generator)` → `generator.AsChatClient()` (`using LMSupply.Generator;`). For the two sampler
  values, set `ChatOptions.RawRepresentationFactory = _ => new GenerationOptions { RepetitionPenalty = …, MinP = … }`
  (`LMSupply.Generator.Models`); the standard options are applied over it.

### Fixed
- **`typeInfoModifiers` on `AgentServerRunner` / `AgentHttpRunner` take effect when the JSON options already carry a resolver.**
  The modifiers were combined behind the options' resolver, and the first resolver that answers for a type wins — so with a
  source-generated context, or with options that had been used once (the runner's own defaults after any earlier serialization
  in the process), they never ran: an event type a host had registered was written as an `error` event (`Runtime type … is not
  supported by polymorphic type 'ServerEvent'`). They now wrap the existing resolver. The README shows a complete modifier that
  adds an event type of your own to the stream.

### Dependencies
- Re-pinned sibling package(s) `LMSupply.Generator` 0.131.0 -> 0.132.0, `TokenMeter` 0.14.0 -> 0.15.0.
- Re-pinned sibling package(s) `LMSupply.Generator` 0.132.0 -> 0.133.0.
- Re-pinned sibling package(s) `LMSupply.Generator` 0.133.0 -> 0.134.0.
- Re-pinned sibling package(s) `LMSupply.Generator` 0.134.0 -> 0.136.0.
- Re-pinned sibling package(s) `Ironbees.Core` 0.29.1 -> 0.30.0, `IronHive.Abstractions` 0.68.0 -> 0.69.0, `IronHive.Agent` 0.72.0-r186.1 -> 0.72.0, `IronHive.Agent.Ironbees` 0.72.0-r186.1 -> 0.72.0, `IronHive.Core` 0.68.0 -> 0.69.0, `IronHive.DeepResearch` 0.72.0-r186.1 -> 0.72.0, `IronHive.Extensions.AI` 0.68.0 -> 0.69.0, `IronHive.Providers.Anthropic` 0.68.0 -> 0.69.0, `IronHive.Providers.GoogleAI` 0.68.0 -> 0.69.0, `IronHive.Providers.OpenAI` 0.68.0 -> 0.69.0, `IronHive.Providers.OpenAI.Compatible` 0.68.0 -> 0.69.0.

## 0.66.0 — 2026-10-20

### Changed
- **Breaking** — **MCP tools are judged by the `mcp_tools` permission rules** (IronHive.Agent 0.71.0). MCP tools keep the names
  their servers give them, so an `mcp_tools` rule never matched them and every MCP tool fell to `tools` / the default action; the
  CLI's policy now asks the MCP plugin manager which names a connected server listed. Migration: a rule written under `tools`
  for an MCP tool (the workaround) no longer applies — move it to `mcp_tools` (`mcpTools` in `config.yaml`).
- **Breaking** — **the `gpustack` provider calls GPUStack's standard `/v1/chat/completions`** (IronHive 0.68.0), not `/v1-openai/`. GPUStack
  answers both the same; the same configuration now also reaches an OpenAI-compatible gateway in front of GPUStack. A GPUStack 0.x
  server with nothing at `/v1/`: use the `openaiCompatible` provider with `{host}/v1-openai`.

### Fixed
- **A server that stops while a recorded session's turn is about to wait for approval suspends that turn instead of
  denying it.** The stop checked «no wait yet» and then denied every wait; a wait that registered between the two was denied
  («the server stopped before the approval request was answered») where a restarted host should have offered it again.
- **`ironhive get` says which file it read.** It reads the global `config.yaml` only, so a key set in the project's
  `.ironhive/config.yaml` read «Key not found» although it was in effect; the message now names the global file and, when a
  project file exists, says its settings still apply.
- **`compaction.maxContextTokens` sets the token budget guard's window too, for a provider that reports none.** The CLI
  passed it only to compaction, so a gateway model's window could not be given to the guard. `HostChatPipeline.Decorate`
  gains an overload taking the declared window (`declaredContextWindow`; 0 = none — then the server enforces its window).
- **The README shows `.ironhive/permissions.yaml` in the shape the loader reads** — the rules under a top-level `permissions:`
  section (it listed the keys as if they sat at the top level, which the loader refuses), with a minimal «allow these tools,
  deny the rest» example for unattended runs.
- **A model whose server does not report its context window is no longer cut off after a few tool results.** The token
  budget guard (`TokenBudgetChatClient`) assumed a 4,096-token window for such a provider — every remote OpenAI-compatible
  server (`gpustack`, `openaiCompatible`, …) — and since the agent counts tool calls and results in full (IronHive.Agent
  0.64.0), a run reached that budget within a handful of tool calls and ended with «[partial response: token budget
  exhausted …]» and stop reason `output_limit`, with most of the real window unused. With no reported window there is now
  no budget and the server enforces its own; a provider that reports its window (the local LMSupply provider) is guarded as
  before. `TokenBudgetChatClient`'s `defaultMaxContextTokens` now defaults to 0 (unknown).
- **Breaking — the token budget guard answers the same way whether the call streams or not.** `GetResponseAsync` (the agent
  loop's `RunAsync` path) was never guarded; over budget it now returns the same explanation with finish reason `Length`
  without calling the model, as the streaming call does — where the model's server used to fail the call. Migration: a caller
  that caught that failure on a local model reads `FinishReason == Length` instead.

### Dependencies
- .NET runtime packages 10.0.12 -> 10.0.13 (the 10.0 servicing band; `Microsoft.Extensions.*` and their siblings move together).
- Re-pinned sibling package(s) `LMSupply.Generator` 0.128.0 -> 0.129.0.
- Re-pinned sibling package(s) `IronHive.Abstractions` 0.67.0 -> 0.68.0, `IronHive.Core` 0.67.0 -> 0.68.0, `IronHive.Extensions.AI` 0.67.0 -> 0.68.0, `IronHive.Providers.Anthropic` 0.67.0 -> 0.68.0, `IronHive.Providers.GoogleAI` 0.67.0 -> 0.68.0, `IronHive.Providers.OpenAI` 0.67.0 -> 0.68.0, `IronHive.Providers.OpenAI.Compatible` 0.67.0 -> 0.68.0.
- Re-pinned sibling package(s) `LMSupply.Generator` 0.129.0 -> 0.131.0.
- Re-pinned sibling package(s) `TokenMeter` 0.13.1 -> 0.14.0.
- Re-pinned sibling package(s) `IronHive.Agent` 0.70.1 -> 0.71.0, `IronHive.Agent.Ironbees` 0.70.1 -> 0.71.0, `IronHive.DeepResearch` 0.70.1 -> 0.71.0.

## 0.65.0 — 2026-10-19

### Added
- **An embedding host runs model and tool calls as the CLI does: `HostChatPipeline`.** `HostChatPipeline.Decorate(client,
  chatBehavior, tools, telemetrySourceName?)` wraps a provider's client with the CLI's decorators (function invocation with
  text tool-call recovery, the per-round context and usage-limit hooks the agent loop binds, the context-window guard), and
  `HostChatPipeline.CreateToolInvocationPipeline(policy, …)` builds the CLI's tool-call steps; a null policy leaves the
  approval gate out for an unattended host. The CLI and `run --server` call these two methods, so the composition has one
  definition.
- **`turn_end` says what the turn cost.** `TurnEndEvent.CostUsd` prices each model round-trip of the turn at the model's
  TokenMeter catalog rates (cached input at the cache-read rate) and sums them; it is absent when the model has no catalog
  price (a self-hosted model) or the provider reported no usage. `run --server` prices with the same catalog lookup its usage
  budget uses. An embedder mapping a loop's chunks itself passes the price with `ToServerEvents(pricing, …)`
  (`TokenMeter.ModelCatalog.FindModel(modelId)`); `CreatedAgentLoop.ModelId` names the model a host-created loop calls.
- **The CLI exports traces over OTLP.** With the standard `OTEL_EXPORTER_OTLP_ENDPOINT` set (`OTEL_EXPORTER_OTLP_PROTOCOL` picks
  `grpc` or `http/protobuf`), `ironhive` — `run --server` included — sends a span per agent turn (source `IronHive.Agent`) with a
  child per model call (source `IronHive.Cli`, message content not recorded). Nothing is exported when the variable is unset or `OTEL_SDK_DISABLED=true`. The exporter is in the CLI only; the
  `IronHive.Host` library gains no dependency.

### Fixed
- **`TokenBudgetChatClient.GetService` answers for itself.** It answered only `typeof(IChatClient)`, so
  `client.GetService<TokenBudgetChatClient>()` on a decorated pipeline returned null; it now returns itself for any type it is
  (unkeyed), as M.E.AI's `DelegatingChatClient` does.

### Dependencies
- `IronHive.Cli` adds `OpenTelemetry` and `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.20.0.
- Re-pinned sibling package(s) `IronHive.Agent` 0.70.0 -> 0.70.1, `IronHive.Agent.Ironbees` 0.70.0 -> 0.70.1, `IronHive.DeepResearch` 0.70.0 -> 0.70.1, `LMSupply.Generator` 0.127.0 -> 0.128.0.

## 0.64.0 — 2026-10-18

### Added
- **An approval says which delegated agent asked.** A `delegation:` agent's tool calls go through the session's approval path;
  the console prompt now shows «Requested by: delegated agent '<name>'», and the server's `hitl_request` carries
  `delegated_by` (`HitlRequestEvent.DelegatedBy`, omitted for the session's own calls). Read from
  `ApprovalRequest.DelegatedBy` (IronHive.Agent 0.70.0). **Breaking** for code that constructs `HitlRequestEvent` positionally
  from a compiled assembly: the record gained a trailing optional parameter — recompile; source is unchanged.

### Dependencies
- Re-pinned sibling package(s) `IronHive.Agent` 0.66.0 -> 0.67.0, `IronHive.Agent.Ironbees` 0.66.0 -> 0.67.0, `IronHive.DeepResearch` 0.66.0 -> 0.67.0, `LMSupply.Generator` 0.125.0 -> 0.126.0.
- Re-pinned sibling package(s) `IronHive.Agent` 0.67.0 -> 0.67.1, `IronHive.Agent.Ironbees` 0.67.0 -> 0.67.1, `IronHive.DeepResearch` 0.67.0 -> 0.67.1.
- Re-pinned sibling package(s) `IronHive.Abstractions` 0.66.1 -> 0.67.0, `IronHive.Core` 0.66.1 -> 0.67.0, `IronHive.Extensions.AI` 0.66.1 -> 0.67.0, `IronHive.Providers.Anthropic` 0.66.1 -> 0.67.0, `IronHive.Providers.GoogleAI` 0.66.1 -> 0.67.0, `IronHive.Providers.OpenAI` 0.66.1 -> 0.67.0, `IronHive.Providers.OpenAI.Compatible` 0.66.1 -> 0.67.0, `LMSupply.Generator` 0.126.0 -> 0.127.0.
- Re-pinned sibling package(s) `IronHive.Agent` 0.67.1 -> 0.68.0, `IronHive.Agent.Ironbees` 0.67.1 -> 0.68.0, `IronHive.DeepResearch` 0.67.1 -> 0.68.0.
- Re-pinned sibling package(s) `IronHive.Agent` 0.68.0 -> 0.69.0, `IronHive.Agent.Ironbees` 0.68.0 -> 0.69.0, `IronHive.DeepResearch` 0.68.0 -> 0.69.0.
- Re-pinned sibling package(s) `IronHive.Agent` 0.69.0 -> 0.70.0, `IronHive.Agent.Ironbees` 0.69.0 -> 0.70.0, `IronHive.DeepResearch` 0.69.0 -> 0.70.0.

## 0.63.0 — 2026-10-17

### Changed
- **Old tool results are masked in batches, so the prompt start — and a provider's prompt cache — stays the same between
  them** (IronHive.Agent 0.66.0). `compaction: observationMaskingMinimumClearTokens` (default 2,000; 0 = mask at every turn,
  the previous behaviour) waits until that many tokens of results sit past the protected turns. The anchored compactor also
  keeps the paths, URLs and IDs the session read, word for word.
- **`ironhive doctor` and `ironhive models` list providers in the order the default provider is chosen** (gpustack,
  openaiCompatible, openai, anthropic, google, xai, ollama, lmstudio, then lmsupply). The CLI now keeps that list in one
  place, so the registration, the default choice, doctor and models cannot disagree about which providers exist.

### Dependencies
- Re-pinned sibling package(s) `IronHive.Agent` 0.64.0 -> 0.65.0, `IronHive.Agent.Ironbees` 0.64.0 -> 0.65.0, `IronHive.DeepResearch` 0.64.0 -> 0.65.0.
- Re-pinned sibling package(s) `LMSupply.Generator` 0.123.0 -> 0.124.0.
- Re-pinned sibling package(s) `LMSupply.Generator` 0.124.0 -> 0.125.0.
- Re-pinned sibling package(s) `IronHive.Agent` 0.65.0 -> 0.66.0, `IronHive.Agent.Ironbees` 0.65.0 -> 0.66.0, `IronHive.DeepResearch` 0.65.0 -> 0.66.0.

## 0.62.0 — 2026-10-17

### Added
- **A provider for any OpenAI-compatible Chat Completions server: `openaiCompatible`.** An organization's model gateway,
  vLLM, llama-server or a cloud provider's compatible endpoint is configured by function rather than as a product:
  `openaiCompatible: { endpoint, model, apiKey }` or `OPENAI_COMPATIBLE_ENDPOINT` / `OPENAI_COMPATIBLE_MODEL` /
  `OPENAI_COMPATIBLE_API_KEY`, chosen with `--provider openaiCompatible` (alias `openai-compatible`). Requests go to
  `{endpoint}/chat/completions` with the endpoint as given (no `/v1` appended), and the key is optional — with none, no
  `Authorization` header is sent, for servers that need none or sit behind a proxy that adds it. `carryToolImages` and
  `streamIdleTimeoutSeconds` work as on the other blocks. The `openai` provider still speaks the Responses API. README
  lists which wire API each provider speaks and the order in which the default provider is chosen.

- **Compaction can summarize instead of omit: `historySummary`.** With `historySummary: { provider, model }` set, the
  messages a compaction drops are summarized by that model (tool calls and results included) instead of being replaced by
  an omission note — one model call per compaction, sent as a plain request (no tools, no token budget guard). Unset (the
  default) keeps the deterministic compaction with no model call. Library: `IronHiveOptions.HistorySummarizer` for
  `AddIronHive`, and `HostContextManagerFactory.Create(…, summarizer)`.

- **A masked or shortened tool result can be read back: `chatBehavior.retrieveToolResults`.** When on, CLI and
  `run --server` loops offer `retrieve_tool_result` (IronHive.Agent `CompactedResultTool`): the placeholder of a result
  compaction hid names an id, and the model reads the original back instead of repeating the call — which a tool with side
  effects cannot. Off by default (no tool, placeholders unchanged). Needs IronHive.Agent 0.64.0.

### Changed
- **The token budget guard counts the way IronHive.Agent 0.64.0 does.** `TokenBudgetChatClient` (installed by the CLI and
  `run --server`) estimates with the agent's `ContextTokenCounter`, which now uses IndexThinking's ratios fitted to o200k:
  Latin text counts about seven letters per token instead of four, Korean about a quarter more than before, and tool calls
  and results are counted in full. For English-heavy histories the guard trips later — at a size closer to what the server
  actually counts — and for Korean ones earlier.

### Fixed
- **`ironhive doctor` no longer reports an unreachable endpoint as reachable.** A provider whose model-list request fails
  falls back to its static catalog (or the configured model), and doctor counted that list as a reachable endpoint; it now
  reports «model list unavailable» with the recommendation to check the endpoint and key.

### Dependencies
- Re-pinned sibling package(s) `IronHive.Agent` 0.63.0 -> 0.64.0, `IronHive.Agent.Ironbees` 0.63.0 -> 0.64.0, `IronHive.DeepResearch` 0.63.0 -> 0.64.0.

## 0.61.0 — 2026-10-16

### Added
- **An approval wait no longer holds the session: `approval.deferAfterSeconds`.** A request not answered within that time
  is deferred — the call does not run, its `tool_end` carries `Refusal: awaiting_approval`, the model reads that the call
  waits for approval, and the turn ends so the next `user_message` is answered. The answer (or `approval.timeoutSeconds`,
  counted from the first send) opens a turn of the server's own: the call runs once through the session's tool pipeline, a
  second `tool_end` for the same `CallId` carries the outcome, and the model is told what became of it. With a recorded
  session a deferred request survives a restart (sent again with its id, holding no turn). Unset (the default) keeps the
  request in its turn. Library: `HitlBridge(timeout, deferAfter: …)`, `AgentServerRunner.DeferredAnswerTurn` /
  `AgentHttpRunner.DeferredAnswerTurn` with `DeferredApprovalTurn.RunAsync`, `HitlBridge.RestoreDeferred` /
  `ReofferOrDeferAsync`, `ApprovalWaitEntry.Deferred`; protocol `ToolRefusalCodes.AwaitingApproval`,
  `StopReasons.Suspended`, `ErrorCodes.NotProcessed`. Needs IronHive.Agent 0.63.0 (`ApprovalResult.Defer`).

### Changed
- **Breaking: a server stopped while a turn waits for an approval no longer waits for the approval timeout.** With a
  recorded session (`--session-id`) the turn ends with `stop_reason: suspended` and the wait stays on record — the next
  process with the same session offers it again and finishes the turn; messages queued behind it are not run and each gets
  an `error` with `Code: not_processed`. Without a session the wait is denied at once. Before, a `shutdown` request or the
  end of the input held the process until `approval.timeoutSeconds` and then closed the wait as a denial. A client that
  waited for that denial now reads `suspended` (or the denial right away).

### Dependencies
- Re-pinned sibling package(s) `IronHive.Agent` 0.62.0 -> 0.63.0, `IronHive.Agent.Ironbees` 0.62.0 -> 0.63.0.
- Re-pinned sibling package(s) `Ironbees.Core` 0.28.1 -> 0.29.0.
- Re-pinned sibling package(s) `LMSupply.Generator` 0.120.0 -> 0.121.0.
- `Microsoft.Extensions.AI` / `.Abstractions` 10.10.x -> 10.11.0.
- Re-pinned sibling package(s) `LMSupply.Generator` 0.121.0 -> 0.122.0.
- Re-pinned sibling package(s) `IronHive.Abstractions` 0.65.1 -> 0.66.0, `IronHive.Core` 0.65.1 -> 0.66.0, `IronHive.Extensions.AI` 0.65.1 -> 0.66.0, `IronHive.Providers.Anthropic` 0.65.1 -> 0.66.0, `IronHive.Providers.GoogleAI` 0.65.1 -> 0.66.0, `IronHive.Providers.OpenAI` 0.65.1 -> 0.66.0, `IronHive.Providers.OpenAI.Compatible` 0.65.1 -> 0.66.0.
- Re-pinned sibling package(s) `Cronex.Net` 0.7.0 -> 0.7.1, `Cronex.Net.Hosting` 0.7.0 -> 0.7.1, `IronHive.Abstractions` 0.66.0 -> 0.66.1, `IronHive.Core` 0.66.0 -> 0.66.1, `IronHive.Extensions.AI` 0.66.0 -> 0.66.1, `IronHive.Providers.Anthropic` 0.66.0 -> 0.66.1, `IronHive.Providers.GoogleAI` 0.66.0 -> 0.66.1, `IronHive.Providers.OpenAI` 0.66.0 -> 0.66.1, `IronHive.Providers.OpenAI.Compatible` 0.66.0 -> 0.66.1, `LMSupply.Generator` 0.122.0 -> 0.123.0.
- Re-pinned sibling package(s) `Ironbees.Core` 0.29.0 -> 0.29.1.
- Re-pinned sibling package(s) `IronHive.DeepResearch` 0.62.0 -> 0.63.0.

## 0.60.1 — 2026-10-14

### Fixed
- **A turn that allows no tool call no longer runs one the model wrote as text.** With `toolMode: "none"` (or
  `ChatToolMode.None` in-process) the tools are still listed, and when a local model wrote a call in its reply text the
  text-call recovery turned it into a call that ran on the round meant to end the turn. Now no call is made and the answer
  is the text before the call markup; a reply that ends inside a call block also no longer shows the markup (IronHive.Agent
  0.62.0).

### Dependencies
- Re-pinned sibling package(s) `TokenMeter` 0.13.0 -> 0.13.1.
- Re-pinned sibling package(s) `IronHive.Agent` 0.61.0 -> 0.62.0, `IronHive.Agent.Ironbees` 0.61.0 -> 0.62.0, `IronHive.DeepResearch` 0.61.0 -> 0.62.0.

## 0.60.0 — 2026-10-13

### Added
- **`ironhive run --json` keeps the work a run did when it is cut short.** A run past `--timeout`, cancelled, or failed
  mid-turn used to print only `{ error, stop_reason, duration_ms }`, so nothing it did before the cut could be read. It now
  prints the full document — the text streamed so far, `tool_calls` and the `calls` list — with `error` set; a call that
  started and never reported back has `outcome: "unfinished"`. A turn that ended carries `"error": null`. The run is
  streamed to get there; what a completed run reports is unchanged.
- **A tool call a local model writes as text is run instead of ending the turn.** When a model behind an
  OpenAI-compatible server (GPUStack, llama-server, vLLM) emits its call in its template's markup and the server's parser
  misses it — Hermes / Qwen `<tool_call>{json}</tool_call>`, Qwen3-Coder `<function=…><parameter=…>` — the reply used to
  end the turn as `completed` with the work undone. The CLI and `run --server` pipeline now has IronHive.Agent's
  `UseTextToolCallRecovery()` directly under the function invocation: a complete block that names an offered tool becomes
  that call, which goes through the same loop guards and permission gate as any other. Delegated agents (their own tool
  loop) are not covered.

### Dependencies
- Re-pinned sibling package(s) `LMSupply.Generator` 0.118.0 -> 0.119.0.
- Re-pinned sibling package(s) `IronHive.Abstractions` 0.64.0 -> 0.65.0, `IronHive.Core` 0.64.0 -> 0.65.0, `IronHive.Extensions.AI` 0.64.0 -> 0.65.0, `IronHive.Providers.Anthropic` 0.64.0 -> 0.65.0, `IronHive.Providers.GoogleAI` 0.64.0 -> 0.65.0, `IronHive.Providers.OpenAI` 0.64.0 -> 0.65.0, `IronHive.Providers.OpenAI.Compatible` 0.64.0 -> 0.65.0, `Iyu.Conventions.Testing` 0.9.0 -> 0.10.0, `LMSupply.Generator` 0.119.0 -> 0.120.0.
- Re-pinned sibling package(s) `Iyu.Conventions.Testing` 0.10.1 -> 0.11.0, `TokenMeter` 0.12.0 -> 0.13.0.
- Re-pinned sibling package(s) `Ironbees.Core` 0.28.0 -> 0.28.1, `IronHive.Abstractions` 0.65.0 -> 0.65.1, `IronHive.Core` 0.65.0 -> 0.65.1, `IronHive.Extensions.AI` 0.65.0 -> 0.65.1, `IronHive.Providers.Anthropic` 0.65.0 -> 0.65.1, `IronHive.Providers.GoogleAI` 0.65.0 -> 0.65.1, `IronHive.Providers.OpenAI` 0.65.0 -> 0.65.1, `IronHive.Providers.OpenAI.Compatible` 0.65.0 -> 0.65.1.
- Re-pinned sibling package(s) `IronHive.Agent` 0.60.2 -> 0.61.0, `IronHive.Agent.Ironbees` 0.60.2 -> 0.61.0, `IronHive.DeepResearch` 0.60.2 -> 0.61.0.

## 0.59.0 — 2026-10-12

### Added
- **A runner handler can take the context and receive the user's text as sent.** `AgentServerRunner` and `AgentHttpRunner`
  have a constructor whose handler is `(UserMessageRequest, ContextUpdateRequest?, CancellationToken)`. It gets the last
  `context_update` read before the message (null before any), and the runner puts nothing in front of the content. A host
  can then check the user's own words (input rules, a classifier) before the working path and selected items go in.
- **`ContextEnrichment.Apply(ContextUpdateRequest?, string)` is public**, so such a handler applies the runners' own
  `[WorkingPath: …]` / `[Selected: a, b]` format instead of keeping a copy of it.

### Removed
- **Breaking: `SkipContextEnrichment` is gone from both runners.** Migration: construct the runner with a handler that takes
  the context, and leave the context out (or apply it with `ContextEnrichment.Apply` where you choose). The existing
  two-argument handler still receives the enriched message, as before.

### Dependencies
- Re-pinned sibling package(s) `IronHive.Abstractions` 0.62.0 -> 0.63.0, `IronHive.Core` 0.62.0 -> 0.63.0, `IronHive.Extensions.AI` 0.62.0 -> 0.63.0, `IronHive.Providers.Anthropic` 0.62.0 -> 0.63.0, `IronHive.Providers.GoogleAI` 0.62.0 -> 0.63.0, `IronHive.Providers.OpenAI` 0.62.0 -> 0.63.0, `IronHive.Providers.OpenAI.Compatible` 0.62.0 -> 0.63.0.
- Re-pinned sibling package(s) `IronHive.Abstractions` 0.63.0 -> 0.64.0, `IronHive.Core` 0.63.0 -> 0.64.0, `IronHive.Extensions.AI` 0.63.0 -> 0.64.0, `IronHive.Providers.Anthropic` 0.63.0 -> 0.64.0, `IronHive.Providers.GoogleAI` 0.63.0 -> 0.64.0, `IronHive.Providers.OpenAI` 0.63.0 -> 0.64.0, `IronHive.Providers.OpenAI.Compatible` 0.63.0 -> 0.64.0.
- Re-pinned sibling package(s) `IronHive.Agent` 0.60.1 -> 0.60.2, `IronHive.Agent.Ironbees` 0.60.1 -> 0.60.2, `IronHive.DeepResearch` 0.60.1 -> 0.60.2.

## 0.58.0 — 2026-10-12

### Documentation
- **The README says how a host of your own resumes a session**: `OpenSessionAsync` with the session id, `RestoreContextAsync`
  into the loop, and `SessionTurnRecorder.RecordAsync` around each turn's stream. Tool calls and results are restored too.
  These are the same pieces `run --server --session-id` uses; before, the README described the recorder only for approvals.

### Changed
- **The runners put the selected items in front of a message too.** `AgentServerRunner` and `AgentHttpRunner` prefixed each
  `user_message` with `[WorkingPath: …]` from the last `context_update` and dropped its `selected_items`. They now add
  `[Selected: a, b]` after the working path when the update named items (and either line alone when only one is set). A host
  that built this prefix itself and left `SkipContextEnrichment` off would now get the items twice: set it, or drop your own
  builder.
- **The protocol's plan events say what `stepIndex` is**: the step's 1-based number, its position in the last
  `plan_created`'s `stepDescriptions` plus one (the planner in IronHive.Agent numbers steps by position).
- **The CLI resolves a turn's tool names against the loop's own catalog** (`IAgentLoop.Tools`, IronHive.Agent 0.58.0) instead of a copy its loop factory kept. No behaviour change. In the tool package's internals, `CreatedAgentLoop` drops `Tools` and `CreateWithToolsAsync` is now `CreateWithPipelineAsync`.
- **The nuget.org «Release Notes» of every package link to the GitHub release of its version**, whose notes are this CHANGELOG's section for that version.

### Dependencies
- Re-pinned sibling package(s) `LMSupply.Generator` 0.115.1 -> 0.116.0.
- Re-pinned sibling package(s) `IronHive.Abstractions` 0.61.0 -> 0.62.0, `IronHive.Core` 0.61.0 -> 0.62.0, `IronHive.Extensions.AI` 0.61.0 -> 0.62.0, `IronHive.Providers.Anthropic` 0.61.0 -> 0.62.0, `IronHive.Providers.GoogleAI` 0.61.0 -> 0.62.0, `IronHive.Providers.OpenAI` 0.61.0 -> 0.62.0, `IronHive.Providers.OpenAI.Compatible` 0.61.0 -> 0.62.0.
- Re-pinned sibling package(s) `IronHive.Agent` 0.57.0 -> 0.58.0, `IronHive.Agent.Ironbees` 0.57.0 -> 0.58.0, `IronHive.DeepResearch` 0.57.0 -> 0.58.0.
- Re-pinned sibling package(s) `IronHive.Agent` 0.58.0 -> 0.59.0, `IronHive.Agent.Ironbees` 0.58.0 -> 0.59.0, `IronHive.DeepResearch` 0.58.0 -> 0.59.0.
- Re-pinned sibling package(s) `Ironbees.Core` 0.27.0 -> 0.28.0, `LMSupply.Generator` 0.116.0 -> 0.117.0.
- Re-pinned sibling package(s) `IronHive.Agent` 0.59.0 -> 0.60.0, `IronHive.Agent.Ironbees` 0.59.0 -> 0.60.0, `IronHive.DeepResearch` 0.59.0 -> 0.60.0.
- Re-pinned sibling package(s) `LMSupply.Generator` 0.117.0 -> 0.117.1.
- Re-pinned sibling package(s) `LMSupply.Generator` 0.117.1 -> 0.118.0, `TokenMeter` 0.11.0 -> 0.12.0.
- Re-pinned sibling package(s) `IronHive.Agent` 0.60.0 -> 0.60.1, `IronHive.Agent.Ironbees` 0.60.0 -> 0.60.1, `IronHive.DeepResearch` 0.60.0 -> 0.60.1.

## 0.57.0 — 2026-10-11

### Added
- **`tool_end` carries what the tool reported about its own work.** `ToolEndEvent.Stats` (optional, omitted when empty) holds the
  values a tool reported through IronHive.Agent's `ToolCallStats` during the call — items visited, bytes read, why it stopped —
  on both the arrived result and the turn's consolidated calls. They are never in `Output` and never sent to the model.
- **Rejected MCP plugin entries are logged at start-up.** An entry in `.ironhive/plugins.*` that cannot connect (no `command`/`url`,
  both without a `type`, an unknown `type`) is a warning naming the entry; an ignored key is an information line. The file may
  also be the `mcpServers` snippet an MCP server's documentation gives.

### Fixed
- **A tool call reported as it arrived is not relayed again from the turn record when the two records differ by value.** The
  server matches them by call id.

### Changed
- **The packages from this repository depend on each other at exactly the same version** (`[x.y.z]`), not a floor.
  A consumer that moves one of them while a family member it depends on resolves at an older version now gets restore warning NU1608 naming
  the pair (an error where warnings are errors) — before, the mixed versions restored silently and could fail at run time.
  A member that reaches you only through another package's floor is not covered: pin every member you load and move them together.

### Dependencies
- Re-pinned sibling package(s) `IronHive.Agent` 0.56.0 -> 0.57.0, `IronHive.Agent.Ironbees` 0.56.0 -> 0.57.0, `IronHive.DeepResearch` 0.56.0 -> 0.57.0.

## 0.56.0 — 2026-10-10

### Removed
- **Breaking: the `IronHive.Host.Exceptions` namespace is gone.** `IronHiveException`, `ConfigurationException`,
  `ProviderException`, `McpPluginException` and `SessionException` were never thrown by the host, and nothing used them.
  Migration: catch the exceptions the host actually throws (standard .NET exceptions such as `InvalidOperationException`),
  or define your own type if you threw these yourself.

### Changed
- **Breaking: a cancelled `run` exits 130 with `stop_reason` `cancelled` instead of exit 1 with `stop_reason` `error`.**
  The cancellation was reported as a failed run carrying the cancellation's message. The `--timeout` result (exit 2,
  `timeout`) is unchanged. Migration: a script that treated exit 1 as «cancelled» checks for exit 130 or `cancelled`.
- **Ctrl+C stops `run` and `run --server` instead of killing the process.** The first Ctrl+C cancels the run the way a
  caller's token does — a single run exits 130 (`cancelled`), a server stops reading and ends the turn in progress; a second
  Ctrl+C ends the process as before.

### Dependencies
- Re-pinned sibling package(s) `Ironbees.Core` 0.25.0 -> 0.26.0, `IronHive.Abstractions` 0.59.1 -> 0.60.0, `IronHive.Core` 0.59.1 -> 0.60.0, `IronHive.Extensions.AI` 0.59.1 -> 0.60.0, `IronHive.Providers.Anthropic` 0.59.1 -> 0.60.0, `IronHive.Providers.GoogleAI` 0.59.1 -> 0.60.0, `IronHive.Providers.OpenAI` 0.59.1 -> 0.60.0, `IronHive.Providers.OpenAI.Compatible` 0.59.1 -> 0.60.0.
- Re-pinned sibling package(s) `TokenMeter` 0.8.0 -> 0.9.0.
- Re-pinned sibling package(s) `TokenMeter` 0.9.0 -> 0.10.0.
- Re-pinned sibling package(s) `TokenMeter` 0.10.0 -> 0.11.0.
- Re-pinned sibling package(s) `Ironbees.Core` 0.26.0 -> 0.27.0, `IronHive.Abstractions` 0.60.0 -> 0.61.0, `IronHive.Agent` 0.55.1 -> 0.56.0, `IronHive.Agent.Ironbees` 0.55.1 -> 0.56.0, `IronHive.Core` 0.60.0 -> 0.61.0, `IronHive.DeepResearch` 0.55.1 -> 0.56.0, `IronHive.Extensions.AI` 0.60.0 -> 0.61.0, `IronHive.Providers.Anthropic` 0.60.0 -> 0.61.0, `IronHive.Providers.GoogleAI` 0.60.0 -> 0.61.0, `IronHive.Providers.OpenAI` 0.60.0 -> 0.61.0, `IronHive.Providers.OpenAI.Compatible` 0.60.0 -> 0.61.0.

## 0.55.0 — 2026-10-09

### Added
- **A failed turn says what kind of failure it was.** `ErrorEvent.Code` carries one of `ErrorCodes` (`context_limit`,
  `rate_limit`, `billing`, `auth`, `network`, `timeout`, `request_rejected`, `tool_execution`, `file_system`, `internal`,
  `unknown`; `unreadable_request` for a skipped request line) beside `Message`, so a client can retry, suggest a smaller
  request or count failures by cause without matching the sentence. The runners take the class from the agent's
  `IErrorRecoveryService`; set `ErrorClassifier` to pass the application's own (`run --server` passes its registered one).
  It is omitted from the JSON when null.
- **`StopReasons`** names the `TurnEndEvent.StopReason` values as constants, the way `ErrorCodes` does for errors.
- **A refused tool call says why.** `ToolEndEvent.Refusal` carries one of `ToolRefusalCodes` (`denied`, `rejected`,
  `timed_out`, …) for a call the agent refused or whose result it withheld, and is null when the tool ran.

### Changed
- **Breaking** — **a cancellation the client did not ask for is now a `timeout` error, and a client's cancel ends the turn
  with `StopReason` `cancelled`.** Before, both ended the same way: no `error` event and a `turn_end` with no stop reason,
  so a provider or stream timeout looked like the client's own cancel. Migration: a client that read «`turn_end` without
  `stop_reason` and no `error`» as «cancelled» reads `stop_reason == "cancelled"` instead.

### Dependencies
- IronHive.Agent / .Ironbees / IronHive.DeepResearch 0.55.0 → 0.55.1.

## 0.54.0 — 2026-10-09

### Added
- **An operator can bound one tool call: `chatBehavior.toolCallTimeoutSeconds`, or `run --tool-timeout <SECONDS>` for
  one run.** Past the limit the call is stopped and the model reads which tool ran out of time (so it can ask for less);
  the same slow call three times in a row ends the turn. An approval prompt in front of the call is not timed. Unset (the
  default) keeps no limit; zero or a negative value is refused when the client is created. Applies to the CLI and
  `run --server`.

### Dependencies
- Re-pinned sibling package(s) `IronHive.Abstractions` 0.58.0 -> 0.59.0, `IronHive.Agent` 0.53.0 -> 0.54.0, `IronHive.Agent.Ironbees` 0.53.0 -> 0.54.0, `IronHive.Core` 0.58.0 -> 0.59.0, `IronHive.DeepResearch` 0.53.0 -> 0.54.0, `IronHive.Extensions.AI` 0.58.0 -> 0.59.0, `IronHive.Providers.Anthropic` 0.58.0 -> 0.59.0, `IronHive.Providers.GoogleAI` 0.58.0 -> 0.59.0, `IronHive.Providers.OpenAI` 0.58.0 -> 0.59.0, `IronHive.Providers.OpenAI.Compatible` 0.58.0 -> 0.59.0.
- Re-pinned sibling package(s) `TokenMeter` 0.7.10 -> 0.8.0.
- Re-pinned sibling package(s) `IronHive.Abstractions` 0.59.0 -> 0.59.1, `IronHive.Core` 0.59.0 -> 0.59.1, `IronHive.Extensions.AI` 0.59.0 -> 0.59.1, `IronHive.Providers.Anthropic` 0.59.0 -> 0.59.1, `IronHive.Providers.GoogleAI` 0.59.0 -> 0.59.1, `IronHive.Providers.OpenAI` 0.59.0 -> 0.59.1, `IronHive.Providers.OpenAI.Compatible` 0.59.0 -> 0.59.1.
- Re-pinned sibling package(s) `IronHive.Agent` 0.54.0 -> 0.55.0, `IronHive.Agent.Ironbees` 0.54.0 -> 0.55.0, `IronHive.DeepResearch` 0.54.0 -> 0.55.0.

## 0.53.0 — 2026-10-09

### Added
- **A turn can carry system instructions apart from the user's text: `TurnOptions.Instructions` (`instructions` on the
  wire).** A client that sends its own directives beside text from someone untrusted no longer has to concatenate them
  into `Content`. They reach the model as `ChatOptions.Instructions` for that turn only — sent as system text with the
  agent's system prompt, never stored in the conversation history, not carried to the next turn. Blank instructions are
  the same as none. Existing clients are unaffected.

### Dependencies
- Re-pinned sibling package(s) `LMSupply.Generator` 0.113.0 -> 0.115.1.

## 0.52.0 — 2026-10-09

### Fixed
- **Settings in `config.yaml` are no longer lost or reset between the global and the project file.** The two files are now
  laid over each other as YAML trees and read once: a key the project file sets wins, a key it does not set keeps the
  global value. Before, a field-by-field merge reset every number and switch the project file did not mention (for example
  `compaction.targetRatio`, `ollama.enabled`) to its default, never read the `approval` section at all
  (`approval.timeoutSeconds` was always 300), and ignored a `skills` section without `roots`. `permissions`, `budget` and
  `skills` still replace as a whole when a file writes them. A key written with an empty value now counts as set.
- **`ironhive get` reads keys exactly as the loader does.** It matched case-insensitively, so `get openai.model` showed the
  value of a wrong-case `openAI:` section that the loader ignores.
- **`get`/`set`/`config`/`doctor` log config warnings through the application's logging.** They held a logger from the
  startup logger factory, which is disposed once the services are built.

### Changed
- **Loading warns about a config key the host does not read at any depth, not only a misspelled section.** A member such as
  `lmsupply.embedderModel` or `delegation.agents[0].bogus` was dropped without a word because the YAML reader ignores unmatched
  members; it is now logged as `Unknown config key '<path>' … ignored`. The keys of dictionary sections are data and are not
  checked. **Breaking**: `ConfigurationManager.FindUnknownTopLevelKeys` is renamed `FindUnknownKeys` and returns dotted paths.

### Removed
- **Breaking: the embedding and rerank providers, which nothing in the host used, are gone.** The CLI registered an
  `IEmbeddingProvider` and an `IRerankProvider` backed by LMSupply, but no tool, retriever or command ever resolved them —
  tool retrieval is keyword-based — and the GpuStack variants were never registered. Removed: `LMSupplyEmbeddingProvider`,
  `LMSupplyRerankProvider`, `GpuStackEmbeddingProvider`, `GpuStackRerankProvider`, the config keys
  `lmsupply.embedderModel`, `lmsupply.rerankerModel`, `gpuStack.embeddingModel`, `gpuStack.rerankModel` and the
  environment variables `GPUSTACK_EMBEDDING_MODEL` and `GPUSTACK_RERANK_MODEL`; the package no longer depends on
  `LMSupply.Embedder` or `LMSupply.Reranker`. Migration: delete those keys from `config.yaml` — loading now warns about each
  one (below); `ironhive set` rejects them.
- **Breaking: `ConfigWriter` is removed.** It wrote `.env` lines for dotted keys, but `ironhive set` writes `config.yaml`
  and no code called it.

### Dependencies
- Re-pinned sibling package(s) `LMSupply.Generator` 0.111.0 -> 0.113.0, `IronHive.Agent` 0.52.7 -> 0.53.0, `IronHive.Agent.Ironbees` 0.52.7 -> 0.53.0, `IronHive.DeepResearch` 0.52.7 -> 0.53.0, `Ironbees.Core` 0.24.6 -> 0.25.0.

## 0.51.2 — 2026-10-08

### Fixed
- **`run --server` answers while the client keeps its input open.** The stdio server handled no message until its standard
  input closed: `Console.In` reads synchronously even through `ReadLineAsync`, and the read loop ran inline, so after the
  first line it waited for the next one before any message was handled. A client that writes a message and waits for the
  answer — every interactive client — got none. The read loop now runs on its own task, and stopping the server no longer
  waits for a line that may never come.
- **A request may name its `type` after its other fields.** `{"id":"…","approved":true,"type":"hitl_response"}` was
  skipped as unreadable; JSON members are unordered, and a client writing from a dictionary sends them in any order.

## 0.51.1 — 2026-10-08

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.24.5 -> 0.24.6, `IronHive.Agent` 0.52.6 -> 0.52.7, `IronHive.Agent.Ironbees` 0.52.6 -> 0.52.7, `IronHive.DeepResearch` 0.52.6 -> 0.52.7, `TokenMeter` 0.7.9 -> 0.7.10. No source changes.

## 0.51.0 — 2026-10-08

### Added
- **The HTTP runner resumes a suspended approval turn too.** `AgentHttpRunner.ResumeTurn` runs before the inbox's
  messages, like `AgentServerRunner.ResumeTurn`, so a host that embeds the HTTP surface wires the same
  `SuspendedTurnResumer` (see «A wait outlives the process» in the README).

### Changed
- `ResumeTurn` runs once on both runners: the property is cleared when a run takes it, so a second `RunAsync` does not
  offer the settled waits again.
- Re-pinned sibling package(s) `IronHive.Agent` 0.52.5 -> 0.52.6, `IronHive.Agent.Ironbees` 0.52.5 -> 0.52.6, `IronHive.DeepResearch` 0.52.5 -> 0.52.6.

## 0.50.0 — 2026-10-08

### Added
- **A tool call waiting for a human approval survives a server restart.** With a recorded session
  (`run --server --session-id <ID>`) each asked call is written to the session before its `hitl_request` is sent (the call
  and an `approval_wait` entry) and settled when answered (`approval_closed`). A process that stops while a call waits
  leaves the turn suspended, not interrupted; the next `run --server` with the same `--session-id` sends the same
  `hitl_request` again (same `id`) before reading any message, runs the call through the session's tool pipeline once the
  client answers — the same gate, guards and tool as in a live turn — and the model finishes the turn. The approval timeout
  counts from the first request: an older wait resumes as a denial without asking.
- Library surface for hosts of their own: `HitlBridge.WaitLog` (`IApprovalWaitLog`, implemented by `SessionTurnRecorder`),
  `HitlBridge.ReofferAsync`, `HitlBridge.Preanswer`, `SessionTurnRecorder.RecordContinuationAsync` /
  `WriteResumedResultAsync`, `SuspendedTurnResumer.ResumeAsync`, `AgentServerRunner.ResumeTurn`,
  `CreatedAgentLoop.Pipeline`, and the transcript entries `ApprovalWaitEntry` / `ApprovalClosedEntry`.

### Fixed
- **Each tool call is written to a recorded session once.** The loop reports a call when its result arrives and again in the
  turn's final record (a different object), and the recorder wrote both — every call of a `run --server` session appeared
  twice in the transcript and in a restored history (since 0.49.0). It now goes by the call id.

### Changed
- **Breaking** — `ISessionManager` has three more members: `SaveApprovalWaitAsync`, `SaveApprovalClosedAsync`,
  `GetPendingApprovalsAsync`. Migration: an implementation of your own adds them (`SessionManager` has them).
- Restoring a session joins tool calls written one after another without results between them into one assistant message,
  so a batch that waited for approval is answered together.
- **Server mode logs an unreadable request line by its error type, not its parser message.** A JSON parser message can
  quote what the sender wrote; the server's log now says only `JsonException`, and the sender still receives the full
  message on its own channel (the `error` event). A test over every shipped log template keeps user and model text out
  of log messages.
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.110.0 -> 0.111.0, `LMSupply.Generator` 0.110.0 -> 0.111.0, `LMSupply.Reranker` 0.110.0 -> 0.111.0.
- Re-pinned sibling package(s) `IronHive.Abstractions` 0.57.0 -> 0.58.0, `IronHive.Core` 0.57.0 -> 0.58.0, `IronHive.Extensions.AI` 0.57.0 -> 0.58.0, `IronHive.Providers.Anthropic` 0.57.0 -> 0.58.0, `IronHive.Providers.GoogleAI` 0.57.0 -> 0.58.0, `IronHive.Providers.OpenAI` 0.57.0 -> 0.58.0, `IronHive.Providers.OpenAI.Compatible` 0.57.0 -> 0.58.0.
- Re-pinned sibling package(s) `Ironbees.Core` 0.24.4 -> 0.24.5.
- Re-pinned sibling package(s) `IronHive.Agent` 0.52.4 -> 0.52.5, `IronHive.Agent.Ironbees` 0.52.4 -> 0.52.5, `IronHive.DeepResearch` 0.52.4 -> 0.52.5.

## 0.49.0 — 2026-10-07

### Changed
- **Breaking** — **a server-mode request line that is not a request no longer ends the server.** The stdio runner
  (`ironhive run --server`, `AgentServerRunner`) skips blank lines and answers an unreadable line (malformed JSON, unknown
  `type`, `null`) with an `error` event, then reads on; it ends only on a `shutdown` request or the end of its input.
  `AgentServerRunner.ReadNextRequestAsync` returns `ServerRequest` (not nullable), skips blank lines, and throws
  `JsonException` for an unreadable line. Migration: a client that ended a session by sending an empty line sends
  `{"type":"shutdown"}` or closes stdin.
- **Breaking** — **`AgentHttpRunner` reconnects when the inbox stream ends** without a `shutdown` request or the host is
  briefly unreachable (5xx, 408, 429, connection errors): exponential backoff (`InitialReconnectDelay`, `MaxReconnectDelay`),
  `Last-Event-ID` resume when the host sends `id:` lines, and a turn in progress keeps running. After
  `MaxReconnectAttempts` consecutive failures (default 10, about three minutes; `null` = never give up) `RunAsync` throws
  `HttpRequestException`; a host that refuses the session fails at once. Before, a dropped stream ended `RunAsync` as if the
  session were over. Migration: a host that ends a session — or stops — by closing the inbox stream now sends a `shutdown`
  request first (or answers the reconnect with 404/410); otherwise the agent keeps retrying until the limit.
- An unreadable inbox message is posted back as an `error` event instead of being written to stderr only.
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.109.0 -> 0.110.0, `LMSupply.Generator` 0.109.0 -> 0.110.0, `LMSupply.Reranker` 0.109.0 -> 0.110.0.
- Re-pinned sibling package(s) `IronHive.Agent` 0.52.3 -> 0.52.4, `IronHive.Agent.Ironbees` 0.52.3 -> 0.52.4, `IronHive.DeepResearch` 0.52.3 -> 0.52.4.

### Added
- **`run --server --session-id <id>` resumes the session**: started again with the same ID (a restart, a moved
  container), the server restores the conversation and continues it. Each turn is written to the session transcript
  as it happens — the user message when the turn starts, each tool call when its result arrives, the assistant text
  when it ends; a turn cut short by an error or cancellation keeps what it wrote and is marked
  `[turn interrupted before it finished]`, so the resumed model knows it did not finish. Session IDs are letters,
  digits, `-` and `_` (at most 128).
- `ISessionManager.OpenSessionAsync(sessionId, projectPath, model)` — load-or-create under a given ID (**Breaking** for
  implementers of the interface). `SessionTurnRecorder` writes a streaming or completed turn to a session;
  `SaveTurnAsync` now uses it (tool calls keep their call IDs).
- `approval.timeoutSeconds` (default 300) — how long `run --server` waits for the client's `hitl_response`.
- `AgentHttpRunner` takes an optional `httpHandler` (`HttpMessageHandler`) for the connection to the host.

### Fixed
- **`--continue` and `--resume` restore the conversation.** The interactive CLI created a session per run but never
  wrote a turn to it, so resuming restored nothing; every turn (interactive, single prompt, JSON output) is now
  written. `SessionManager.LoadSessionAsync`/`DeleteSessionAsync` refuse an ID that is not a file-name segment
  (`../x`) with `ArgumentException`.
- **A turn waiting for approval no longer blocks the session when another message arrives first.** Both server runners
  waited for the running turn before reading past a second `user_message`, so the `hitl_response` behind it was never read
  and the approval timed out as a denial (and nothing else, `cancel` included, was read until then). Turns now run in
  order on their own task while the request loop keeps reading.

## 0.48.11 — 2026-10-07

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.24.3 -> 0.24.4, `IronHive.Abstractions` 0.56.0 -> 0.57.0, `IronHive.Agent` 0.52.2 -> 0.52.3, `IronHive.Agent.Ironbees` 0.52.2 -> 0.52.3, `IronHive.Core` 0.56.0 -> 0.57.0, `IronHive.DeepResearch` 0.52.2 -> 0.52.3, `IronHive.Extensions.AI` 0.56.0 -> 0.57.0, `IronHive.Providers.Anthropic` 0.56.0 -> 0.57.0, `IronHive.Providers.GoogleAI` 0.56.0 -> 0.57.0, `IronHive.Providers.OpenAI` 0.56.0 -> 0.57.0, `IronHive.Providers.OpenAI.Compatible` 0.56.0 -> 0.57.0. No source changes.

## 0.48.10 — 2026-10-07

### Fixed
- **A tool call whose arguments stream is started and logged once.** When the agent loop streams tool arguments
  (`AgentOptions.StreamToolArguments`), the server's `tool_start` event, the execution log and the CLI's tool lines now
  act on the complete call only; the argument fragments before it (`ToolCallChunk.IsComplete = false`) named the tool too,
  so each call was started twice and logged once with its arguments cut short.

## 0.48.9 — 2026-10-07

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.52.1 -> 0.52.2, `IronHive.Agent.Ironbees` 0.52.1 -> 0.52.2, `IronHive.DeepResearch` 0.52.1 -> 0.52.2. No source changes.

## 0.48.8 — 2026-10-07

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.24.2 -> 0.24.3, `IronHive.Abstractions` 0.55.1 -> 0.56.0, `IronHive.Agent` 0.51.1 -> 0.52.1, `IronHive.Agent.Ironbees` 0.51.1 -> 0.52.1, `IronHive.Core` 0.55.1 -> 0.56.0, `IronHive.DeepResearch` 0.51.1 -> 0.52.1, `IronHive.Extensions.AI` 0.55.1 -> 0.56.0, `IronHive.Providers.Anthropic` 0.55.1 -> 0.56.0, `IronHive.Providers.GoogleAI` 0.55.1 -> 0.56.0, `IronHive.Providers.OpenAI` 0.55.1 -> 0.56.0, `IronHive.Providers.OpenAI.Compatible` 0.55.1 -> 0.56.0, `LMSupply.Embedder` 0.108.0 -> 0.109.0, `LMSupply.Generator` 0.108.0 -> 0.109.0, `LMSupply.Reranker` 0.108.0 -> 0.109.0. No source changes.

## 0.48.7 — 2026-10-06

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.24.1 -> 0.24.2, `IronHive.Abstractions` 0.55.0 -> 0.55.1, `IronHive.Agent` 0.51.0 -> 0.51.1, `IronHive.Agent.Ironbees` 0.51.0 -> 0.51.1, `IronHive.Core` 0.55.0 -> 0.55.1, `IronHive.DeepResearch` 0.51.0 -> 0.51.1, `IronHive.Extensions.AI` 0.55.0 -> 0.55.1, `IronHive.Providers.Anthropic` 0.55.0 -> 0.55.1, `IronHive.Providers.GoogleAI` 0.55.0 -> 0.55.1, `IronHive.Providers.OpenAI` 0.55.0 -> 0.55.1, `IronHive.Providers.OpenAI.Compatible` 0.55.0 -> 0.55.1. No source changes.

## 0.48.6 — 2026-10-06

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.24.0 -> 0.24.1, `IronHive.Abstractions` 0.54.0 -> 0.55.0, `IronHive.Agent` 0.50.2 -> 0.51.0, `IronHive.Agent.Ironbees` 0.50.2 -> 0.51.0, `IronHive.Core` 0.54.0 -> 0.55.0, `IronHive.DeepResearch` 0.50.2 -> 0.51.0, `IronHive.Extensions.AI` 0.54.0 -> 0.55.0, `IronHive.Providers.Anthropic` 0.54.0 -> 0.55.0, `IronHive.Providers.GoogleAI` 0.54.0 -> 0.55.0, `IronHive.Providers.OpenAI` 0.54.0 -> 0.55.0, `IronHive.Providers.OpenAI.Compatible` 0.54.0 -> 0.55.0. No source changes.

## 0.48.5 — 2026-10-06

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.50.1 -> 0.50.2, `IronHive.Agent.Ironbees` 0.50.1 -> 0.50.2, `IronHive.DeepResearch` 0.50.1 -> 0.50.2, `LMSupply.Embedder` 0.107.0 -> 0.108.0, `LMSupply.Generator` 0.107.0 -> 0.108.0, `LMSupply.Reranker` 0.107.0 -> 0.108.0. No source changes.

## 0.48.4 — 2026-10-06

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.23.0 -> 0.24.0, `IronHive.Agent` 0.50.0 -> 0.50.1, `IronHive.Agent.Ironbees` 0.50.0 -> 0.50.1, `IronHive.DeepResearch` 0.50.0 -> 0.50.1. No source changes.

## 0.48.3 — 2026-10-06

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.22.1 -> 0.23.0, `IronHive.Abstractions` 0.53.1 -> 0.54.0, `IronHive.Agent` 0.49.2 -> 0.50.0, `IronHive.Agent.Ironbees` 0.49.2 -> 0.50.0, `IronHive.Core` 0.53.1 -> 0.54.0, `IronHive.DeepResearch` 0.49.2 -> 0.50.0, `IronHive.Extensions.AI` 0.53.1 -> 0.54.0, `IronHive.Providers.Anthropic` 0.53.1 -> 0.54.0, `IronHive.Providers.GoogleAI` 0.53.1 -> 0.54.0, `IronHive.Providers.OpenAI` 0.53.1 -> 0.54.0, `IronHive.Providers.OpenAI.Compatible` 0.53.1 -> 0.54.0, `LMSupply.Embedder` 0.106.1 -> 0.107.0, `LMSupply.Generator` 0.106.1 -> 0.107.0, `LMSupply.Reranker` 0.106.1 -> 0.107.0. No source changes.

## 0.48.2 — 2026-10-06

### Changed
- Re-pinned sibling package(s) `IronHive.Abstractions` 0.53.0 -> 0.53.1, `IronHive.Core` 0.53.0 -> 0.53.1, `IronHive.Extensions.AI` 0.53.0 -> 0.53.1, `IronHive.Providers.Anthropic` 0.53.0 -> 0.53.1, `IronHive.Providers.GoogleAI` 0.53.0 -> 0.53.1, `IronHive.Providers.OpenAI` 0.53.0 -> 0.53.1, `IronHive.Providers.OpenAI.Compatible` 0.53.0 -> 0.53.1, `LMSupply.Embedder` 0.106.0 -> 0.106.1, `LMSupply.Generator` 0.106.0 -> 0.106.1, `LMSupply.Reranker` 0.106.0 -> 0.106.1.
- Re-pinned sibling package(s) `Ironbees.Core` 0.22.0 -> 0.22.1.
- Re-pinned sibling package(s) `IronHive.Agent` 0.49.1 -> 0.49.2, `IronHive.Agent.Ironbees` 0.49.1 -> 0.49.2, `IronHive.DeepResearch` 0.49.1 -> 0.49.2.

### Fixed
- **Breaking** (released as a patch) — **cancelling a call now cancels it.** 4 method(s) that take a `CancellationToken` caught every exception to
  return a fallback (`null`, an empty result, a failure value) or to log and continue, and treated the caller's own
  cancellation the same way. They now let the caller's `OperationCanceledException` through; other failures behave
  as before. Affected: `doctor`, `update`, the Oops service and the chat client provider.
  Migration: code that relied on a cancelled call returning `null`, an empty result or a failure value now
  receives `OperationCanceledException` — catch it where a cancellation is expected.

## 0.48.1 — 2026-10-06

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.49.0 -> 0.49.1, `IronHive.Agent.Ironbees` 0.49.0 -> 0.49.1, `IronHive.DeepResearch` 0.49.0 -> 0.49.1. No source changes.

## 0.48.0 — 2026-10-06

### Changed
- **Breaking: `IUpdateService` reports a failed check or update by throwing.** Before, `CheckForUpdateAsync` returned
  `null` for a network error, an error status and an unusable release alike, and `UpdateAsync` caught every exception —
  cancellation included — into `UpdateResult { Success = false, Error = … }`. Now `CheckForUpdateAsync` returns the
  latest `UpdateInfo` or throws (`HttpRequestException` with the status, `InvalidOperationException` when no usable
  version is published), and `UpdateAsync` returns only what it installed; a cancelled update stops instead of reading
  as a failed one. `UpdateResult.Success` and `UpdateResult.Error` are removed, `UpdatedVersion` is now required, and the
  "already up to date" case is `UpdateResult.AlreadyUpToDate` (it was a success carrying the text in `Error`).
  Migration: wrap the calls in `try`/`catch` instead of testing `Success` or `null`.
- `ironhive update` and `ironhive --update` pass the command's cancellation to the update check and the install, and
  print the reason a check failed.

### Removed
- **Breaking:** the in-process long-term memory wiring, which nothing in the host ever read: `LongTermMemoryManager`,
  `LongTermMemoryOptions` and `AddIronHiveMemory()` (the CLI registered the MemoryIndexer stack with it, but no code
  resolved `ISessionMemoryService` or `IMemoryService`). The package no longer depends on `MemoryIndexer`,
  `MemoryIndexer.Sdk` or `IronHive.Agent.Memory`. Long-term memory for an agent comes from a memory MCP server; an
  embedder that wants it in process composes `IronHive.Agent.Memory` itself.

## 0.47.0 — 2026-10-06

### Removed
- **Breaking:** `IronHive.Host.Integration` — `CodeBeakerTools`, `ICodeExecutionProvider` (with `WebSocketCodeExecutionProvider`,
  `InMemoryCodeExecutionProvider` and their result types) and `MemoryIndexerTools`, `IMemoryToolsProvider` (with
  `InMemoryToolsProvider`, `MemorySearchOptions` and their result types). Nothing in the host registered or used them, no
  documentation described them, and the memory tools had no implementation beyond an in-memory stand-in. Memory for an
  agent is `IronHive.Agent.Memory`.

## 0.46.0 — 2026-10-06

### Fixed
- **oops snapshot commands no longer block a thread and can be cancelled.** `IOopsService.StartAsync`, `SaveAsync`,
  `UndoAsync`, `BackAsync`, `HistoryAsync`, `ChangesAsync`, `StatusAsync`, `StopAsync` and `CleanupAsync` returned a
  completed task after running the `oops` process synchronously, so a hung `oops` held the caller (an agent's file
  write) indefinitely. They now await the process and kill it when the token is cancelled.

### Changed
- **Breaking:** `IOopsService` (the methods above), `ISessionManager` (every method), `IExecutionLogger`
  (`BeginTurnAsync`, `ProcessChunkAsync`, `EndTurnAsync`), the `AgentLoopSessionExtensions` methods,
  `AgentServerRunner.WriteEventAsync` and `UpdateChecker.WaitForCheckAsync` take an optional `CancellationToken`, and the
  host passes the turn's token through. Callers compile unchanged; implementations of the interfaces add the parameter.

## 0.45.5 — 2026-10-06

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.21.18 -> 0.22.0, `IronHive.Abstractions` 0.52.0 -> 0.53.0, `IronHive.Agent` 0.48.4 -> 0.49.0, `IronHive.Agent.Ironbees` 0.48.4 -> 0.49.0, `IronHive.Agent.Memory` 0.48.4 -> 0.49.0, `IronHive.Core` 0.52.0 -> 0.53.0, `IronHive.DeepResearch` 0.48.4 -> 0.49.0, `IronHive.Extensions.AI` 0.52.0 -> 0.53.0, `IronHive.Providers.Anthropic` 0.52.0 -> 0.53.0, `IronHive.Providers.GoogleAI` 0.52.0 -> 0.53.0, `IronHive.Providers.OpenAI` 0.52.0 -> 0.53.0, `IronHive.Providers.OpenAI.Compatible` 0.52.0 -> 0.53.0. No source changes.

## 0.45.4 — 2026-10-05

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.48.3 -> 0.48.4, `IronHive.Agent.Ironbees` 0.48.3 -> 0.48.4, `IronHive.Agent.Memory` 0.48.3 -> 0.48.4, `IronHive.DeepResearch` 0.48.3 -> 0.48.4, `LMSupply.Embedder` 0.105.2 -> 0.106.0, `LMSupply.Generator` 0.105.2 -> 0.106.0, `LMSupply.Reranker` 0.105.2 -> 0.106.0, `MemoryIndexer` 0.24.0 -> 0.25.0, `MemoryIndexer.Sdk` 0.24.0 -> 0.25.0. No source changes.

## 0.45.3 — 2026-10-05

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.21.17 -> 0.21.18, `IronHive.Abstractions` 0.51.1 -> 0.52.0, `IronHive.Agent` 0.48.2 -> 0.48.3, `IronHive.Agent.Ironbees` 0.48.2 -> 0.48.3, `IronHive.Agent.Memory` 0.48.2 -> 0.48.3, `IronHive.Core` 0.51.1 -> 0.52.0, `IronHive.DeepResearch` 0.48.2 -> 0.48.3, `IronHive.Extensions.AI` 0.51.1 -> 0.52.0, `IronHive.Providers.Anthropic` 0.51.1 -> 0.52.0, `IronHive.Providers.GoogleAI` 0.51.1 -> 0.52.0, `IronHive.Providers.OpenAI` 0.51.1 -> 0.52.0, `IronHive.Providers.OpenAI.Compatible` 0.51.1 -> 0.52.0, `MemoryIndexer` 0.23.2 -> 0.24.0, `MemoryIndexer.Sdk` 0.23.2 -> 0.24.0. No source changes.

## 0.45.2 — 2026-10-05

### Changed
- Re-pinned sibling package(s) `Cronex.Net` 0.6.2 -> 0.7.0, `Cronex.Net.Hosting` 0.6.2 -> 0.7.0. No source changes.

## 0.45.1 — 2026-10-05

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.21.16 -> 0.21.17, `IronHive.Abstractions` 0.51.0 -> 0.51.1, `IronHive.Agent` 0.48.0 -> 0.48.2, `IronHive.Agent.Ironbees` 0.48.0 -> 0.48.2, `IronHive.Agent.Memory` 0.48.0 -> 0.48.2, `IronHive.Core` 0.51.0 -> 0.51.1, `IronHive.DeepResearch` 0.48.0 -> 0.48.2, `IronHive.Extensions.AI` 0.51.0 -> 0.51.1, `IronHive.Providers.Anthropic` 0.51.0 -> 0.51.1, `IronHive.Providers.GoogleAI` 0.51.0 -> 0.51.1, `IronHive.Providers.OpenAI` 0.51.0 -> 0.51.1, `IronHive.Providers.OpenAI.Compatible` 0.51.0 -> 0.51.1, `LMSupply.Embedder` 0.105.1 -> 0.105.2, `LMSupply.Generator` 0.105.1 -> 0.105.2, `LMSupply.Reranker` 0.105.1 -> 0.105.2, `MemoryIndexer` 0.23.1 -> 0.23.2, `MemoryIndexer.Sdk` 0.23.1 -> 0.23.2. No source changes.

## 0.45.0 — 2026-10-05

### Added
- **The `ollama` provider works.** Enabling it (`ollama.enabled: true` + `ollama.model`, or `OLLAMA_ENABLED`/`OLLAMA_MODEL`) used to stop the CLI with «temporarily unsupported»; it now registers on Ollama's OpenAI-compatible `/v1` surface (the endpoint may be given with or without `/v1`) and takes `carryToolImages` like `lmstudio`.
- **`streamIdleTimeoutSeconds` in every provider block the CLI wires** (`gpuStack`, `openai`, `anthropic`, `googleai`, `xai`, `ollama`, `lmstudio`) — how long a streaming answer may stay silent before the request ends with a «stream idle timeout». Unset keeps the previous behaviour (no limit); zero or negative is refused naming the key. Settable with `ironhive set <section>.streamIdleTimeoutSeconds <seconds>`; a project `config.yaml` overrides the global value per provider.

### Removed
- **Breaking: the `azureopenai` config section and the `AZURE_OPENAI_*` variables.** No Azure provider was ever registered from them — a config that set only Azure ran on the local LMSupply fallback without saying so, or stopped with «No API provider configured» when that was off. A config that still has the section now gets the unknown-key warning.
  Migration for code that reads the config: `IronHiveConfig.AzureOpenAI` and `AzureOpenAIConfig` are gone — delete the references (no provider was ever built from it, so a check on it never selected one).

## 0.44.2 — 2026-10-05

### Changed
- Re-pinned sibling package(s) `IronHive.Abstractions` 0.50.0 -> 0.51.0, `IronHive.Core` 0.50.0 -> 0.51.0, `IronHive.Extensions.AI` 0.50.0 -> 0.51.0, `IronHive.Providers.Anthropic` 0.50.0 -> 0.51.0, `IronHive.Providers.GoogleAI` 0.50.0 -> 0.51.0, `IronHive.Providers.OpenAI` 0.50.0 -> 0.51.0, `IronHive.Providers.OpenAI.Compatible` 0.50.0 -> 0.51.0, `LMSupply.Embedder` 0.105.0 -> 0.105.1, `LMSupply.Generator` 0.105.0 -> 0.105.1, `LMSupply.Reranker` 0.105.0 -> 0.105.1, `TokenMeter` 0.7.8 -> 0.7.9.
- Re-pinned sibling package(s) `Ironbees.Core` 0.21.15 -> 0.21.16, `MemoryIndexer` 0.23.0 -> 0.23.1, `MemoryIndexer.Sdk` 0.23.0 -> 0.23.1.
- Re-pinned sibling package(s) `IronHive.Agent` 0.47.0 -> 0.48.0, `IronHive.Agent.Ironbees` 0.47.0 -> 0.48.0, `IronHive.Agent.Memory` 0.47.0 -> 0.48.0, `IronHive.DeepResearch` 0.47.0 -> 0.48.0.

### Dependencies
- Microsoft.Extensions.AI 10.10.0, Microsoft.Extensions.AI.Abstractions 10.10.1, Microsoft.Extensions.AI.OpenAI 10.10.1, OpenAI 2.14.0.

## 0.44.1 — 2026-10-04

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.46.0 -> 0.47.0, `IronHive.Agent.Ironbees` 0.46.0 -> 0.47.0, `IronHive.Agent.Memory` 0.46.0 -> 0.47.0, `IronHive.DeepResearch` 0.46.0 -> 0.47.0, `MemoryIndexer` 0.22.0 -> 0.23.0, `MemoryIndexer.Sdk` 0.22.0 -> 0.23.0. No source changes.

## 0.44.0 — 2026-10-04

### Added
- **`run --json` lists every tool call with its outcome (`calls`).** Each entry has `tool`, `outcome` (`ok`, `failed`,
  `refused` or `unknown`), `refusal_kind` and the refusal's message, and the call's `arguments` cut to 300 characters.
  The tool's own result is not included. A run that ends as `tool_terminated` now says which call a guard refused and
  why. Before, it reported only a count.

## 0.43.0 — 2026-10-04

### Added
- **`chatBehavior.reasoningEffort` and `run --reasoning-effort`: how much the model reasons on every call.** `none`,
  `low`, `medium`, `high` or `extra_high`; unset sends nothing (the model's own default, which for a reasoning model can
  be tens of thousands of thinking tokens on one call). `none` turns thinking off; the other levels are requests a server
  may hold loosely. A server turn's `reasoning_effort` still replaces it for that turn. An unknown value fails when the
  loop is created and names the setting.

## 0.42.2 — 2026-10-04

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.45.1 -> 0.46.0, `IronHive.Agent.Ironbees` 0.45.1 -> 0.46.0, `IronHive.Agent.Memory` 0.45.1 -> 0.46.0, `IronHive.DeepResearch` 0.45.1 -> 0.46.0. No source changes.

## 0.42.1 — 2026-10-04

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.44.0 -> 0.45.1, `IronHive.Agent.Ironbees` 0.44.0 -> 0.45.1, `IronHive.Agent.Memory` 0.44.0 -> 0.45.1, `IronHive.DeepResearch` 0.44.0 -> 0.45.1, `LMSupply.Embedder` 0.104.0 -> 0.105.0, `LMSupply.Generator` 0.104.0 -> 0.105.0, `LMSupply.Reranker` 0.104.0 -> 0.105.0. No source changes.

## 0.42.0 — 2026-10-04

### Added
- **`chatBehavior.maxOutputTokens` and `run --max-output-tokens <N>`: the output-token cap per model call is configurable.**
  It was a fixed 4096 in the CLI. A reasoning model spends part of the cap on its thinking, so a turn could end at
  `output_limit` with no answer; raise it (e.g. 16384) within what the server allows. The default stays 4096.

## 0.41.0 — 2026-10-04

### Added
- **`ironhive run --json` says how the turn ended: `stop_reason`, `duration_ms`, `tool_calls`, `refused_tool_calls`,
  `failed_tool_calls`**, beside `content`, `thinking` and `usage`. A failure — also one before the turn starts, such as
  a provider that cannot be created — is a JSON document (`error`, `stop_reason: "error"`) instead of plain text.
- **`run --max-iterations <N>` and `run --timeout <SECONDS>`.** The cap overrides `chatBehavior.maximumIterationsPerRequest`
  for the run; a run past its timeout stops with `stop_reason: "timeout"`.

### Changed
- **Breaking (scripts that read `run`'s exit code):** a turn that did not complete now exits **2** (iteration cap, output
  limit, a guard that ended the turn, or `--timeout`), and **3** when the content filter stopped it. Both were 0. An error
  is still 1.
- Configuration warnings logged while the CLI starts go to stderr, so they no longer land in the middle of `run --json`
  or `run --server` output.

### Fixed
- **A `run` cut off at its iteration cap reports `step_limit` (exit 2) and the tool calls that ran.** It read as
  `completed` with no tool calls: at the cap the model is asked once more without tools, and an answer that was then
  continued lost the turn's tool rounds — and the continuation offered the tools again, so more calls ran unrecorded
  past the cap (IronHive.Agent 0.44.0, IndexThinking 0.24.1).

### Dependencies
- IronHive.Agent family 0.43.1 -> 0.44.0 (`ToolCallResult.RefusalKind`, step-limit classification); IndexThinking
  0.24.1 through it.

## 0.40.1 — 2026-10-04

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.21.14 -> 0.21.15, `IronHive.Abstractions` 0.49.0 -> 0.50.0, `IronHive.Agent` 0.43.0 -> 0.43.1, `IronHive.Agent.Ironbees` 0.43.0 -> 0.43.1, `IronHive.Agent.Memory` 0.43.0 -> 0.43.1, `IronHive.Core` 0.49.0 -> 0.50.0, `IronHive.DeepResearch` 0.43.0 -> 0.43.1, `IronHive.Extensions.AI` 0.49.0 -> 0.50.0, `IronHive.Providers.Anthropic` 0.49.0 -> 0.50.0, `IronHive.Providers.GoogleAI` 0.49.0 -> 0.50.0, `IronHive.Providers.OpenAI` 0.49.0 -> 0.50.0, `IronHive.Providers.OpenAI.Compatible` 0.49.0 -> 0.50.0, `LMSupply.Embedder` 0.103.0 -> 0.104.0, `LMSupply.Generator` 0.103.0 -> 0.104.0, `LMSupply.Reranker` 0.103.0 -> 0.104.0. No source changes.

## 0.40.0 — 2026-10-04

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.102.0 -> 0.103.0, `LMSupply.Generator` 0.102.0 -> 0.103.0, `LMSupply.Reranker` 0.102.0 -> 0.103.0.
- Re-pinned sibling package(s) `Ironbees.Core` 0.21.13 -> 0.21.14, `MemoryIndexer` 0.21.0 -> 0.22.0, `MemoryIndexer.Sdk` 0.21.0 -> 0.22.0.

### Added
- **`lmstudio.carryToolImages` and `gpuStack.carryToolImages`** (default off): an image a tool returns reaches a vision
  model behind a Chat Completions endpoint (IronHive 0.49.0 `CarryImageToolResultsAsUserMessage`). A Chat Completions
  tool message holds text only, so the image was replaced by a note; with the key on, the tool message names it and a
  user message after the tool results carries it. `ironhive set lmstudio.carryToolImages true`.

### Fixed
- **A tool result that is content (an MCP tool returning an image) reaches the local LMSupply model as its text with
  the image named**, not as the type name of a list.

### Dependencies
- IronHive 0.49.0, IronHive.Agent 0.43.0 (content tool results are counted, masked and recorded as text with the
  image named; `McpToolResult.Images`).

## 0.39.7 — 2026-10-04

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.42.4 -> 0.42.5, `IronHive.Agent.Ironbees` 0.42.4 -> 0.42.5, `IronHive.Agent.Memory` 0.42.4 -> 0.42.5, `IronHive.DeepResearch` 0.42.4 -> 0.42.5, `LMSupply.Embedder` 0.101.0 -> 0.102.0, `LMSupply.Generator` 0.101.0 -> 0.102.0, `LMSupply.Reranker` 0.101.0 -> 0.102.0. No source changes.

## 0.39.6 — 2026-10-03

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.42.3 -> 0.42.4, `IronHive.Agent.Ironbees` 0.42.3 -> 0.42.4, `IronHive.Agent.Memory` 0.42.3 -> 0.42.4, `IronHive.DeepResearch` 0.42.3 -> 0.42.4, `LMSupply.Embedder` 0.100.0 -> 0.101.0, `LMSupply.Generator` 0.100.0 -> 0.101.0, `LMSupply.Reranker` 0.100.0 -> 0.101.0. No source changes.

## 0.39.5 — 2026-10-03

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.42.2 -> 0.42.3, `IronHive.Agent.Ironbees` 0.42.2 -> 0.42.3, `IronHive.Agent.Memory` 0.42.2 -> 0.42.3, `IronHive.DeepResearch` 0.42.2 -> 0.42.3, `LMSupply.Embedder` 0.99.0 -> 0.100.0, `LMSupply.Generator` 0.99.0 -> 0.100.0, `LMSupply.Reranker` 0.99.0 -> 0.100.0. No source changes.

## 0.39.4 — 2026-10-03

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.42.1 -> 0.42.2, `IronHive.Agent.Ironbees` 0.42.1 -> 0.42.2, `IronHive.Agent.Memory` 0.42.1 -> 0.42.2, `IronHive.DeepResearch` 0.42.1 -> 0.42.2. No source changes.

## 0.39.3 — 2026-10-03

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.98.2 -> 0.99.0, `LMSupply.Generator` 0.98.2 -> 0.99.0, `LMSupply.Reranker` 0.98.2 -> 0.99.0. No source changes.

## 0.39.2 — 2026-10-03

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.42.0 -> 0.42.1, `IronHive.Agent.Ironbees` 0.42.0 -> 0.42.1, `IronHive.Agent.Memory` 0.42.0 -> 0.42.1, `IronHive.DeepResearch` 0.42.0 -> 0.42.1. No source changes.

## 0.39.1 — 2026-10-03

### Fixed
- **An embedder that registers skills gets the tool to read them.** With `services.AddAgentSkills(...)` and `AddIronHive(...)`, the skills' names and descriptions reached the instructions but the loop had no `load_skill` tool, so the model was told about skills it could not open (the CLI loop factory added the tool; the library path did not). `AddIronHive` now adds `SkillsLoader.LoadTool` to `options.Tools` when skills are registered and at least one was found, and leaves the list alone when it already carries `load_skill`.

## 0.39.0 — 2026-10-03

### Changed
- **A CLI or server turn ends when the model keeps re-reading content it already has** (IronHive.Agent 0.42.0 `RepeatedResultGuardMiddleware`, in the CLI's tool pipeline): the same tool returning the same result on a third separate visit ends the turn on a `RepeatedResult` refusal that names the cause — usually a working set larger than `compaction.observationMaskingProtectedTokens` — instead of re-reading in rotation until the step limit or answering from memory.
- **`compaction.observationMaskingProtectedRounds` is replaced by `observationMaskingProtectedTokens`** (IronHive.Agent 0.42.0): masking inside one turn now keeps the recent tool results that fit a token budget instead of the last N rounds, so short results (a write answering "ok") no longer push out the reads before them, and a masked result names the call that produced it so the model can call it again. **Breaking** — **Migration:** replace `observationMaskingProtectedRounds: N` with `observationMaskingProtectedTokens:` set to the room you want for recent results (for example a quarter of the model's window, or 8000).
- **The CLI's context-window guard measures Korean, Japanese and Chinese text at its own density.** `TokenBudgetChatClient` estimated four characters per token for every script and counted a message's text twice; it now uses the agent's `ContextTokenCounter`, so a CJK history that would overflow the window is stopped before the request instead of slipping past the guard.
- **A built-in tool that fails now ends a request that keeps failing the same way** (IronHive.Agent 0.42.0): the agent's file, shell and todo tools, and the host's `WebSearch`, `ExploreSite` and `DeepResearch`, throw on failure instead of returning "Error…" text, so the repeated-error guard sees the failure. The model still reads why: the agent's `UseToolInvocationPipeline` now sends a failing tool's message by default, so the CLI no longer sets `IncludeDetailedErrors` itself. **Breaking** for code that calls `WebSearchTool`/`DeepResearchTool` directly and reads an "Error…" result — catch the exception instead.
- README: a delegated agent's `agent.yaml` needs a `model:` section; leaving `deployment` out runs it on the session's model.
- Re-pinned sibling package(s) `IronHive.Agent`, `IronHive.Agent.Ironbees`, `IronHive.Agent.Memory`, `IronHive.DeepResearch` 0.41.0 -> 0.42.0.
- Re-pinned sibling package(s) `IronHive.Abstractions` 0.47.0 -> 0.48.0, `IronHive.Core` 0.47.0 -> 0.48.0, `IronHive.Extensions.AI` 0.47.0 -> 0.48.0, `IronHive.Providers.Anthropic` 0.47.0 -> 0.48.0, `IronHive.Providers.GoogleAI` 0.47.0 -> 0.48.0, `IronHive.Providers.OpenAI` 0.47.0 -> 0.48.0, `IronHive.Providers.OpenAI.Compatible` 0.47.0 -> 0.48.0.
- Re-pinned sibling package(s) `Ironbees.Core` 0.21.12 -> 0.21.13.

## 0.38.3 — 2026-10-03

### Changed
- **A CLI turn stuck re-issuing a refused tool call now ends** (IronHive.Agent 0.41.0 `MaxRefusedRepeats`, default 2): the second refusal in a row of the same call ends the request on that refusal instead of running to the step limit.
- Re-pinned sibling package(s) `IronHive.Agent` 0.40.0 -> 0.41.0, `IronHive.Agent.Ironbees` 0.40.0 -> 0.41.0, `IronHive.Agent.Memory` 0.40.0 -> 0.41.0, `IronHive.DeepResearch` 0.40.0 -> 0.41.0. No source changes.

## 0.38.2 — 2026-10-02

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.39.1 -> 0.40.0, `IronHive.Agent.Ironbees` 0.39.1 -> 0.40.0, `IronHive.Agent.Memory` 0.39.1 -> 0.40.0, `IronHive.DeepResearch` 0.39.1 -> 0.40.0. No source changes.

## 0.38.1 — 2026-10-02

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.21.11 -> 0.21.12, `IronHive.Abstractions` 0.46.1 -> 0.47.0, `IronHive.Agent` 0.39.0 -> 0.39.1, `IronHive.Agent.Ironbees` 0.39.0 -> 0.39.1, `IronHive.Agent.Memory` 0.39.0 -> 0.39.1, `IronHive.Core` 0.46.1 -> 0.47.0, `IronHive.DeepResearch` 0.39.0 -> 0.39.1, `IronHive.Extensions.AI` 0.46.1 -> 0.47.0, `IronHive.Providers.Anthropic` 0.46.1 -> 0.47.0, `IronHive.Providers.GoogleAI` 0.46.1 -> 0.47.0, `IronHive.Providers.OpenAI` 0.46.1 -> 0.47.0, `IronHive.Providers.OpenAI.Compatible` 0.46.1 -> 0.47.0, `MemoryIndexer` 0.20.3 -> 0.21.0, `MemoryIndexer.Sdk` 0.20.3 -> 0.21.0. No source changes.

## 0.38.0 — 2026-10-02

### Added
- **`toolRetrieval.stickyChangeScore`** — with `stickyToolLimit` on, the score a scored tool the held set lacks must
  reach before it may change the set (unset = the IronHive.Agent 0.39.0 default, 0.7). A generic follow-up no longer
  changes the set — and re-reads the whole prompt on a hybrid-memory local model — for its best but weak match.

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` · `IronHive.Agent.Ironbees` · `IronHive.Agent.Memory` · `IronHive.DeepResearch` 0.38.0 -> 0.39.0.

## 0.37.0 — 2026-10-02

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.37.0 -> 0.38.0, `IronHive.Agent.Ironbees` 0.37.0 -> 0.38.0, `IronHive.Agent.Memory` 0.37.0 -> 0.38.0, `IronHive.DeepResearch` 0.37.0 -> 0.38.0.

### Added
- **`delegation:` — Ironbees named agents as tools.** Each agent listed under `delegation.agents` (an
  `agents/<name>/agent.yaml` + `system-prompt.md` under `delegation.agentsDirectory`, default `agents` in the working
  directory) becomes a tool the working model can hand a sub-task to (`IronHive.Agent.Ironbees` `DelegationTools`).
  A delegated run calls its tools through the **session's own** tool pipeline — the same permission rules, planning mode
  and approval prompt, once per call — and its usage is charged to the session budget. `provider` / `model` choose the
  agent's model (unset: the model in its `agent.yaml`, else the session's); `maxDepth` / `maxConcurrent` bound nesting
  and fan-out. An agent that is not in the directory fails when the session starts, naming the path.

### Removed
- **Breaking: `IronbeesIntegrationExtensions.AddIronbeesOrchestration`.** It built a second service provider inside a
  factory, ran delegated tools through the container's pipeline instead of the session's (so a host's approval prompt
  was not asked), and blocked on asynchronous plugin loading. Use `delegation:` in `config.yaml`; an embedder that wants
  Ironbees in its own container calls `IronHive.Agent.Ironbees`'s `AddIronbees` directly.

## 0.36.3 — 2026-10-02

### Changed
- **`toolRetrieval.stickyToolLimit` now holds the tool set instead of growing it** (IronHive.Agent 0.37.0): a later
  request sends the same tools unchanged unless it needs one they lack, so the prompt cache survives; the README and
  the option's documentation say so.
- Re-pinned sibling package(s) `IronHive.Abstractions` 0.46.0 -> 0.46.1, `IronHive.Core` 0.46.0 -> 0.46.1, `IronHive.Extensions.AI` 0.46.0 -> 0.46.1, `IronHive.Providers.Anthropic` 0.46.0 -> 0.46.1, `IronHive.Providers.GoogleAI` 0.46.0 -> 0.46.1, `IronHive.Providers.OpenAI` 0.46.0 -> 0.46.1, `IronHive.Providers.OpenAI.Compatible` 0.46.0 -> 0.46.1.
- Re-pinned sibling package(s) `Ironbees.Core` 0.21.10 -> 0.21.11.
- Re-pinned sibling package(s) `IronHive.Agent` 0.36.2 -> 0.37.0, `IronHive.Agent.Ironbees` 0.36.2 -> 0.37.0, `IronHive.Agent.Memory` 0.36.2 -> 0.37.0, `IronHive.DeepResearch` 0.36.2 -> 0.37.0.

## 0.36.2 — 2026-10-02

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.98.1 -> 0.98.2, `LMSupply.Generator` 0.98.1 -> 0.98.2, `LMSupply.Reranker` 0.98.1 -> 0.98.2. No source changes.
- Re-pinned sibling package(s) `MemoryIndexer` 0.20.2 -> 0.20.3, `MemoryIndexer.Sdk` 0.20.2 -> 0.20.3.
- Re-pinned sibling package(s) `IronHive.Agent` 0.36.1 -> 0.36.2, `IronHive.Agent.Ironbees` 0.36.1 -> 0.36.2, `IronHive.Agent.Memory` 0.36.1 -> 0.36.2, `IronHive.DeepResearch` 0.36.1 -> 0.36.2.

## 0.36.1 — 2026-10-02

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.36.0 -> 0.36.1, `IronHive.Agent.Ironbees` 0.36.0 -> 0.36.1, `IronHive.Agent.Memory` 0.36.0 -> 0.36.1, `IronHive.DeepResearch` 0.36.0 -> 0.36.1. No source changes.

## 0.36.0 — 2026-10-02

### Added
- **`toolRetrieval.stickyToolLimit` — keep a local server's prompt cache across messages.** When above 0, tools a
  conversation already sent stay selected and go first in later requests, with newly selected tools after them, while
  the whole set fits the limit (over it the selection starts over). A prefix-cached server (llama-server, vLLM) then
  re-reads from the first new tool instead of the whole prompt on every message. Off by default.
- **`DeleteFile` and `MoveFile` in the built-in tool set** (from `IronHive.Agent` 0.36.0). An agent renaming a file no
  longer empties the old one with `WriteFile`. The default permissions ask before either (a move is judged as deleting
  its source and writing its destination); Planning mode leaves both out.

### Changed
- **Selected tools are sent ordinal by name** (from `IronHive.Agent` 0.36.0), so the same set always serialises the same
  way. Re-pinned `IronHive.Agent`, `IronHive.Agent.Ironbees`, `IronHive.Agent.Memory`, `IronHive.DeepResearch` 0.35.1 -> 0.36.0.

## 0.35.1 — 2026-10-02

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.21.9 -> 0.21.10, `IronHive.Abstractions` 0.45.4 -> 0.46.0, `IronHive.Agent` 0.34.2 -> 0.35.1, `IronHive.Agent.Ironbees` 0.34.2 -> 0.35.1, `IronHive.Agent.Memory` 0.34.2 -> 0.35.1, `IronHive.Core` 0.45.4 -> 0.46.0, `IronHive.DeepResearch` 0.34.2 -> 0.35.1, `IronHive.Extensions.AI` 0.45.4 -> 0.46.0, `IronHive.Providers.Anthropic` 0.45.4 -> 0.46.0, `IronHive.Providers.GoogleAI` 0.45.4 -> 0.46.0, `IronHive.Providers.OpenAI` 0.45.4 -> 0.46.0, `IronHive.Providers.OpenAI.Compatible` 0.45.4 -> 0.46.0, `LMSupply.Embedder` 0.98.0 -> 0.98.1, `LMSupply.Generator` 0.98.0 -> 0.98.1, `LMSupply.Reranker` 0.98.0 -> 0.98.1. No source changes.

## 0.35.0 — 2026-10-02

### Added
- **`toolRetrieval:` — send the model only the tools a request needs.** With `enabled: true`, CLI / `run --server` loops
  connect the agent's `KeywordToolRetriever`: each request carries the tools most relevant to the user's request (up to
  `maxTools`, plus `alwaysInclude`), instead of every built-in and MCP tool. Also `minRelevanceScore` and
  `minScoredSlots`. Off by default — turning it on changes which tools the model sees.

## 0.34.2 — 2026-10-02

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.34.1 -> 0.34.2, `IronHive.Agent.Ironbees` 0.34.1 -> 0.34.2, `IronHive.Agent.Memory` 0.34.1 -> 0.34.2, `IronHive.DeepResearch` 0.34.1 -> 0.34.2. No source changes.

## 0.34.1 — 2026-10-01

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.34.0 -> 0.34.1, `IronHive.Agent.Ironbees` 0.34.0 -> 0.34.1, `IronHive.Agent.Memory` 0.34.0 -> 0.34.1, `IronHive.DeepResearch` 0.34.0 -> 0.34.1. No source changes.

## 0.34.0 — 2026-10-01

### Added
- **AGENTS.md is read.** CLI and `run --server` loops add the AGENTS.md files that apply to their working directory —
  from the repository root down, root first so the nearest file wins — to the system instructions, re-read every turn.
  On by default; `agentsMd: { enabled: false }` turns it off, `maxCharacters` bounds it. Each server session reads its
  own working directory. Nothing above the repository root is read.

### Dependencies
- `IronHive.Agent` 0.33.0 -> 0.34.0 (`AgentsMdInstructions`).

## 0.33.0 — 2026-10-01

### Added
- **A usage budget per session**: the `budget` section of `config.yaml` (`maxSessionTokens`, `maxSessionCost` in USD priced
  from the TokenMeter catalog, `warningThreshold`, `stopOnLimit`). Each loop the host builds — one CLI session, one
  `run --server` session — gets its own limit, checked before every model call including each tool round; the call past
  it ends the turn with `UsageLimitExceededException`. Before, the host registered no limiter at all, so a limit could not
  be set. Loops now also carry the model id, so usage is priced.

### Dependencies
- `IronHive.Agent` 0.32.0 -> 0.33.0 (`UsageLimitChatClient`).

## 0.32.0 — 2026-10-01

### Added
- **`turn_end` says why the turn ended and how long it took**: `TurnEndEvent.StopReason` (`completed`, `output_limit`,
  `content_filter`, `tool_terminated`, `awaiting_host_tools`, `step_limit` — null when the turn failed or was
  cancelled), `DurationMs`, and `CachedInputTokens` (prompt-cache reads, when the provider reported them). Additive:
  existing fields and clients are unchanged.

### Changed
- Re-pinned `IronHive.Agent` / `.Agent.Ironbees` / `.Agent.Memory` / `IronHive.DeepResearch` 0.31.0 -> 0.32.0 (turn stop
  reasons, `MaxTurnDuration`).

## 0.31.3 — 2026-10-01

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.30.0 -> 0.31.0, `IronHive.Agent.Ironbees` 0.30.0 -> 0.31.0, `IronHive.Agent.Memory` 0.30.0 -> 0.31.0, `IronHive.DeepResearch` 0.30.0 -> 0.31.0. No source changes.

## 0.31.2 — 2026-10-01

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.29.0 -> 0.30.0, `IronHive.Agent.Ironbees` 0.29.0 -> 0.30.0, `IronHive.Agent.Memory` 0.29.0 -> 0.30.0, `IronHive.DeepResearch` 0.29.0 -> 0.30.0. No source changes.

## 0.31.1 — 2026-10-01

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.97.0 -> 0.98.0, `LMSupply.Generator` 0.97.0 -> 0.98.0, `LMSupply.Reranker` 0.97.0 -> 0.98.0. No source changes.

## 0.31.0 — 2026-10-01

### Added
- **`run --server` asks the client before an asked tool call runs.** An `Ask` verdict is sent as a `hitl_request` and the
  call waits for the matching `hitl_response` (before, it was always rejected because there was no terminal). The
  approver is `HitlBridge` (`IronHive.Host.Server`), an `IHumanApprovalService` that `AgentServerRunner` and
  `AgentHttpRunner` drive when given one (`hitlBridge:`): several requests can wait at once, each answered by its `id`;
  no answer within the timeout (5 minutes by default), a cancelled turn, a stopped runner or no runner at all is a
  rejection. The interactive CLI keeps asking on the terminal.
- **Protocol (additive):** `hitl_request` carries `tool_name`, `arguments`, `call_id` (matches the `tool_start`) and
  `level`; `hitl_response` carries `id`, `modified_arguments` and `always_approve`. A response without `id` still answers
  the only waiting request.

### Changed
- **Breaking: `AgentHttpRunner.WaitForHitlResponseAsync` and `ResolveHitl` are removed.** The runner routes every
  `hitl_response` to the `HitlBridge` it was given. Migration: pass `hitlBridge:` to the runner and give the same bridge
  to the approval gate as its approver, instead of wiring a wait/resolve pair yourself.
- **Planning mode is enforced in the CLI and `run --server`:** while planning, only read-only tools run
  (`IronHive.Agent` 0.29.0's gate).
- `IronHive.Agent` 0.28.0 → 0.29.0 (the tool-call policy; see its changelog).
- Re-pinned sibling package(s) `Ironbees.Core` 0.21.8 -> 0.21.9, `IronHive.Abstractions` 0.45.3 -> 0.45.4, `IronHive.Core` 0.45.3 -> 0.45.4, `IronHive.DeepResearch` 0.28.0 -> 0.29.0, `IronHive.Extensions.AI` 0.45.3 -> 0.45.4, `IronHive.Providers.Anthropic` 0.45.3 -> 0.45.4, `IronHive.Providers.GoogleAI` 0.45.3 -> 0.45.4, `IronHive.Providers.OpenAI` 0.45.3 -> 0.45.4, `IronHive.Providers.OpenAI.Compatible` 0.45.3 -> 0.45.4.

## 0.30.3 — 2026-10-01

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.96.0 -> 0.97.0, `LMSupply.Generator` 0.96.0 -> 0.97.0, `LMSupply.Reranker` 0.96.0 -> 0.97.0. No source changes.

## 0.30.2 — 2026-10-01

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.27.0 -> 0.28.0, `IronHive.Agent.Ironbees` 0.27.0 -> 0.28.0, `IronHive.Agent.Memory` 0.27.0 -> 0.28.0, `IronHive.DeepResearch` 0.27.0 -> 0.28.0. No source changes.

## 0.30.1 — 2026-09-30

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.95.0 -> 0.96.0, `LMSupply.Generator` 0.95.0 -> 0.96.0, `LMSupply.Reranker` 0.95.0 -> 0.96.0. No source changes.

## 0.30.0 — 2026-09-30

### Added
- **`ResilientArgumentsMiddleware`: the resilient tool-calling behaviour as a tool invocation pipeline step.** Add it
  last to a `ToolInvocationPipeline` (or register it with `AddToolInvocationMiddleware<ResilientArgumentsMiddleware>()`)
  and a call whose arguments do not fit the tool's parameters returns the same recovery directive
  `ResilientFunctionInvoker` returns. `ResilientFunctionInvoker.Create()` is unchanged for plain `UseFunctionInvocation`
  clients; both forms share one implementation.

### Changed
- **The CLI's and `run --server`'s tool calls run through IronHive.Agent's tool invocation pipeline**, in this order:
  the default loop guards, the permission gate (same rules and prompt as before), then the resilient-arguments step.
  What changes for the user:
  - a tool call whose arguments could not be parsed from the model's response is not run; the model reads the parse
    error and can re-issue it, and the user is not asked to approve it;
  - a fourth identical call in a row to the same tool (same arguments, the previous three succeeded) is not run; the
    model is told it already has that result. This also stops a model that keeps retrying with the same empty
    arguments;
  - the same tool failing with the same error three times in a row ends the turn with a readable message, instead of
    the model retrying until the turn aborts with an error.
- Re-pinned sibling package(s) `Cronex.Net` 0.6.1 -> 0.6.2, `Cronex.Net.Hosting` 0.6.1 -> 0.6.2, `Ironbees.Core` 0.21.7 -> 0.21.8, `IronHive.Abstractions` 0.45.2 -> 0.45.3, `IronHive.Core` 0.45.2 -> 0.45.3, `IronHive.Extensions.AI` 0.45.2 -> 0.45.3, `IronHive.Providers.Anthropic` 0.45.2 -> 0.45.3, `IronHive.Providers.GoogleAI` 0.45.2 -> 0.45.3, `IronHive.Providers.OpenAI` 0.45.2 -> 0.45.3, `IronHive.Providers.OpenAI.Compatible` 0.45.2 -> 0.45.3, `LMSupply.Embedder` 0.94.0 -> 0.95.0, `LMSupply.Generator` 0.94.0 -> 0.95.0, `LMSupply.Reranker` 0.94.0 -> 0.95.0, `MemoryIndexer` 0.20.1 -> 0.20.2, `MemoryIndexer.Sdk` 0.20.1 -> 0.20.2.

  `MaximumIterationsPerRequest`, `MaximumConsecutiveErrorsPerRequest` and detailed errors behave as before.

### Dependencies
- `IronHive.Agent`, `IronHive.Agent.Ironbees`, `IronHive.Agent.Memory`, `IronHive.DeepResearch` 0.26.0 -> 0.27.0.

## 0.29.15 — 2026-09-30

### Changed
- **Documentation: comments and XML docs describe behaviour without internal references.** The
  internal planning documents `CHARTER.md` and `docs/design/ironhive-integration-plan.md` were
  removed from the repository; the README describes the host's scope.
- Re-pinned sibling package(s) `IronHive.Abstractions` 0.45.1 -> 0.45.2, `IronHive.Core` 0.45.1 -> 0.45.2, `IronHive.Extensions.AI` 0.45.1 -> 0.45.2, `IronHive.Providers.Anthropic` 0.45.1 -> 0.45.2, `IronHive.Providers.GoogleAI` 0.45.1 -> 0.45.2, `IronHive.Providers.OpenAI` 0.45.1 -> 0.45.2, `IronHive.Providers.OpenAI.Compatible` 0.45.1 -> 0.45.2, `LMSupply.Embedder` 0.93.1 -> 0.94.0, `LMSupply.Generator` 0.93.1 -> 0.94.0, `LMSupply.Reranker` 0.93.1 -> 0.94.0, `WebLookup` 0.2.4 -> 0.3.0.
- Re-pinned sibling package(s) `Ironbees.Core` 0.21.6 -> 0.21.7.
- Re-pinned sibling package(s) `IronHive.Agent` 0.25.6 -> 0.26.0, `IronHive.Agent.Ironbees` 0.25.6 -> 0.26.0, `IronHive.Agent.Memory` 0.25.6 -> 0.26.0, `IronHive.DeepResearch` 0.25.6 -> 0.26.0.

## 0.29.14 — 2026-09-30

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.25.5 -> 0.25.6, `IronHive.Agent.Ironbees` 0.25.5 -> 0.25.6, `IronHive.Agent.Memory` 0.25.5 -> 0.25.6, `IronHive.DeepResearch` 0.25.5 -> 0.25.6, `WebLookup` 0.2.3 -> 0.2.4 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.29.13 — 2026-09-30

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.21.5 -> 0.21.6, `LMSupply.Embedder` 0.93.0 -> 0.93.1, `LMSupply.Generator` 0.93.0 -> 0.93.1, `LMSupply.Reranker` 0.93.0 -> 0.93.1, `MemoryIndexer` 0.20.0 -> 0.20.1, `MemoryIndexer.Sdk` 0.20.0 -> 0.20.1 — re-consumption of already-consumed iyulab packages. No source changes.
- Re-pinned sibling package(s) `IronHive.Agent` 0.25.4 -> 0.25.5, `IronHive.Agent.Ironbees` 0.25.4 -> 0.25.5, `IronHive.Agent.Memory` 0.25.4 -> 0.25.5, `IronHive.DeepResearch` 0.25.4 -> 0.25.5 — re-consumption of already-consumed iyulab packages.

## 0.29.12 — 2026-09-29

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.25.3 -> 0.25.4, `IronHive.Agent.Ironbees` 0.25.3 -> 0.25.4, `IronHive.Agent.Memory` 0.25.3 -> 0.25.4, `IronHive.DeepResearch` 0.25.3 -> 0.25.4 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.29.11 — 2026-09-29

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.92.1 -> 0.93.0, `LMSupply.Generator` 0.92.1 -> 0.93.0, `LMSupply.Reranker` 0.92.1 -> 0.93.0 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.29.10 — 2026-09-29

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.25.2 -> 0.25.3, `IronHive.Agent.Ironbees` 0.25.2 -> 0.25.3, `IronHive.Agent.Memory` 0.25.2 -> 0.25.3, `IronHive.DeepResearch` 0.25.2 -> 0.25.3, `LMSupply.Embedder` 0.92.0 -> 0.92.1, `LMSupply.Generator` 0.92.0 -> 0.92.1, `LMSupply.Reranker` 0.92.0 -> 0.92.1 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.29.9 — 2026-09-29

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.21.4 -> 0.21.5, `IronHive.Abstractions` 0.45.0 -> 0.45.1, `IronHive.Core` 0.45.0 -> 0.45.1, `IronHive.Extensions.AI` 0.45.0 -> 0.45.1, `IronHive.Providers.Anthropic` 0.45.0 -> 0.45.1, `IronHive.Providers.GoogleAI` 0.45.0 -> 0.45.1, `IronHive.Providers.OpenAI` 0.45.0 -> 0.45.1, `IronHive.Providers.OpenAI.Compatible` 0.45.0 -> 0.45.1, `LMSupply.Embedder` 0.91.0 -> 0.92.0, `LMSupply.Generator` 0.91.0 -> 0.92.0, `LMSupply.Reranker` 0.91.0 -> 0.92.0 — re-consumption of already-consumed iyulab packages. No source changes.
- Re-pinned sibling package(s) `IronHive.Agent` 0.25.1 -> 0.25.2, `IronHive.Agent.Ironbees` 0.25.1 -> 0.25.2, `IronHive.Agent.Memory` 0.25.1 -> 0.25.2, `IronHive.DeepResearch` 0.25.1 -> 0.25.2 — re-consumption of already-consumed iyulab packages.

## 0.29.8 — 2026-09-29

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.25.0 -> 0.25.1, `IronHive.Agent.Ironbees` 0.25.0 -> 0.25.1, `IronHive.Agent.Memory` 0.25.0 -> 0.25.1, `IronHive.DeepResearch` 0.25.0 -> 0.25.1 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.29.7 — 2026-09-29

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.90.0 -> 0.91.0, `LMSupply.Generator` 0.90.0 -> 0.91.0, `LMSupply.Reranker` 0.90.0 -> 0.91.0 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.29.6 — 2026-09-29

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.89.0 -> 0.90.0, `LMSupply.Generator` 0.89.0 -> 0.90.0, `LMSupply.Reranker` 0.89.0 -> 0.90.0 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.29.5 — 2026-09-29

### Changed
- Re-pinned sibling package(s) `IronHive.DeepResearch` 0.24.1 -> 0.25.0 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.29.4 — 2026-09-29

### Fixed

- **The `permissions` section of `config.yaml` applies.** It was merged and then replaced by the permission-file lookup even when
  the project had no permission file, so its rules never took effect. Now: the project's `.ironhive/permissions.*` when it exists,
  otherwise the `config.yaml` rules (global, then project), otherwise the built-in defaults. A section that sets only
  `defaultAction` or `readOnlyTools` now counts too.
- **`LMSUPPLY_ENABLED=false` keeps local inference off.** With no remote provider configured, the loader turned it back on.
- **A skills root written `~/…` is under the home directory.** It resolved to a directory literally named `~` under the working
  directory, so no global skills loaded.
- **`doctor` reports local-only inference as a passing check**, as the README describes, instead of a warning.
- **README:** the environment variables actually read, which surfaces install the token budget / resilient tool-calling /
  permission gate (CLI and `run --server`), `load_skill` for embedders, the `skills` config key, a CLI reference, and the
  `session_started` event.

### Dependencies

- IronHive.Agent 0.25.0.

## 0.29.3 — 2026-09-28

### Changed
- Re-pinned sibling package(s) `IronHive.Abstractions` 0.44.0 -> 0.45.0, `IronHive.Core` 0.44.0 -> 0.45.0, `IronHive.Extensions.AI` 0.44.0 -> 0.45.0, `IronHive.Providers.Anthropic` 0.44.0 -> 0.45.0, `IronHive.Providers.GoogleAI` 0.44.0 -> 0.45.0, `IronHive.Providers.OpenAI` 0.44.0 -> 0.45.0, `IronHive.Providers.OpenAI.Compatible` 0.44.0 -> 0.45.0 — re-consumption of already-consumed iyulab packages.
- Re-pinned sibling package(s) `Ironbees.Core` 0.21.3 -> 0.21.4 — re-consumption of already-consumed iyulab packages.
- Re-pinned sibling package(s) `IronHive.Agent` 0.24.0 -> 0.24.1, `IronHive.Agent.Ironbees` 0.24.0 -> 0.24.1, `IronHive.Agent.Memory` 0.24.0 -> 0.24.1, `IronHive.DeepResearch` 0.24.0 -> 0.24.1 — re-consumption of already-consumed iyulab packages.

### Fixed
- **The packages carry the README**, so their nuget.org page shows it.

## 0.29.2 — 2026-09-28

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.88.0 -> 0.89.0, `LMSupply.Generator` 0.88.0 -> 0.89.0, `LMSupply.Reranker` 0.88.0 -> 0.89.0 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.29.1 — 2026-09-28

### Changed
- Re-pinned sibling package(s) `MemoryIndexer` 0.19.17 -> 0.20.0, `MemoryIndexer.Sdk` 0.19.17 -> 0.20.0 — re-consumption of already-consumed iyulab packages.
- Re-pinned sibling package(s) `IronHive.Agent` 0.23.0 -> 0.24.0, `IronHive.Agent.Ironbees` 0.23.0 -> 0.24.0, `IronHive.Agent.Memory` 0.23.0 -> 0.24.0, `IronHive.DeepResearch` 0.23.0 -> 0.24.0 — re-consumption of already-consumed iyulab packages.

- **`compaction.toolSchemaCompression` is no longer read from the config file.** It never had an effect: the agent's
  `CompactionConfig` carried it and nothing applied it, and IronHive.Agent 0.24.0 removes it. A config file that still sets
  it loads as before; the key is ignored.

## 0.29.0 — 2026-09-28

### Changed
- Re-pinned sibling package(s) `IronHive.Abstractions` 0.43.1 -> 0.44.0, `IronHive.Core` 0.43.1 -> 0.44.0, `IronHive.Extensions.AI` 0.43.1 -> 0.44.0, `IronHive.Providers.Anthropic` 0.43.1 -> 0.44.0, `IronHive.Providers.GoogleAI` 0.43.1 -> 0.44.0, `IronHive.Providers.OpenAI` 0.43.1 -> 0.44.0, `IronHive.Providers.OpenAI.Compatible` 0.43.1 -> 0.44.0 — re-consumption of already-consumed iyulab packages.
- Re-pinned sibling package(s) `Ironbees.Core` 0.21.2 -> 0.21.3 — re-consumption of already-consumed iyulab packages.

### Fixed

- **`compaction.maxContextTokens` sizes the context window.** The host built its context manager by hand from the model
  name alone, so the setting was read from the config file and never used: a model the catalog does not know ran on an
  8192-token guess. The host now builds the manager with the agent's own builder, so the rest of the section applies too:
  `targetRatio`, `useAnchoredCompaction` / `useTokenBasedCompaction` / `thresholdPercentage`, and budgets clamped to the
  window. Without that clamp, the 40k `protectRecentTokens` default kept compaction from ever firing on a small window.

### Added

- **`compaction.compactOnOverflow`** (default `true`, from IronHive.Agent 0.23.0). When the window is not known, the host's
  loops compact when the server reports an overflow, and learn the window from it, instead of compacting against the guess.

## 0.28.2 — 2026-09-28

### Added

- **The CLI masks and compacts tool results on every tool round of a turn, not only before the first model call.** Its
  chat client pipeline now carries IronHive.Agent's `ToolRoundContextChatClient` inside function invocation; the loop
  binds its own `ContextManager` to it. With `compaction.observationMaskingProtectedRounds` set, a long single-message
  task keeps only its recent rounds' results at full size.
- `compaction.goalReminder` reaches host-built loops (`HostContextManagerFactory` and the config merge), for example to
  turn the reminder off.

## 0.28.1 — 2026-09-28

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.21.1 -> 0.21.2, `IronHive.Abstractions` 0.43.0 -> 0.43.1, `IronHive.Agent` 0.21.0 -> 0.22.0, `IronHive.Agent.Ironbees` 0.21.0 -> 0.22.0, `IronHive.Agent.Memory` 0.21.0 -> 0.22.0, `IronHive.Core` 0.43.0 -> 0.43.1, `IronHive.DeepResearch` 0.21.0 -> 0.22.0, `IronHive.Extensions.AI` 0.43.0 -> 0.43.1, `IronHive.Providers.Anthropic` 0.43.0 -> 0.43.1, `IronHive.Providers.GoogleAI` 0.43.0 -> 0.43.1, `IronHive.Providers.OpenAI` 0.43.0 -> 0.43.1, `IronHive.Providers.OpenAI.Compatible` 0.43.0 -> 0.43.1 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.28.0 — 2026-09-28

### Changed

- **Behaviour change — the `compaction` settings for tool results now take effect in host-built loops, with their
  documented defaults.** `HostContextManagerFactory` (used by the CLI and by `AddIronHive…` loop registration) built the
  loop's `ContextManager` without a tool-result compactor or an observation masker, so `EnableToolResultCompaction`,
  `MaxToolResultChars`, `EnableObservationMasking` and `ObservationMaskingProtectedTurns` were accepted and never applied.
  Both are now built from the config. Their defaults are on: a tool result over 30,000 characters is cut to its head
  and tail, and results longer than 200 characters older than the last 3 user turns are sent as placeholders. `History`
  keeps them in full. To keep the old behaviour, set `compaction.enableToolResultCompaction: false` and
  `compaction.enableObservationMasking: false`. The config merge also carries the new `ObservationMaskingProtectedRounds`
  (IronHive.Agent 0.21.0). Masking inside one turn also needs `ToolRoundContextChatClient` inside function invocation,
  which the CLI pipeline does not add yet.

## 0.27.10 — 2026-09-28

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.20.0 -> 0.21.0, `IronHive.Agent.Ironbees` 0.20.0 -> 0.21.0, `IronHive.Agent.Memory` 0.20.0 -> 0.21.0, `IronHive.DeepResearch` 0.20.0 -> 0.21.0 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.27.9 — 2026-09-28

### Changed
- Re-pinned sibling package(s) `IronHive.Abstractions` 0.42.0 -> 0.43.0, `IronHive.Core` 0.42.0 -> 0.43.0, `IronHive.Extensions.AI` 0.42.0 -> 0.43.0, `IronHive.Providers.Anthropic` 0.42.0 -> 0.43.0, `IronHive.Providers.GoogleAI` 0.42.0 -> 0.43.0, `IronHive.Providers.OpenAI` 0.42.0 -> 0.43.0, `IronHive.Providers.OpenAI.Compatible` 0.42.0 -> 0.43.0 — re-consumption of already-consumed iyulab packages.
- Re-pinned sibling package(s) `Ironbees.Core` 0.21.0 -> 0.21.1 — re-consumption of already-consumed iyulab packages.
- Re-pinned sibling package(s) `IronHive.Agent` 0.19.13 -> 0.20.0, `IronHive.Agent.Ironbees` 0.19.13 -> 0.20.0, `IronHive.Agent.Memory` 0.19.13 -> 0.20.0, `IronHive.DeepResearch` 0.19.13 -> 0.20.0 — re-consumption of already-consumed iyulab packages.

### Fixed

- **`LMSupplyChatClient` offers declaration-only tools to the model.** A tool created with
  `AIFunctionFactory.CreateDeclaration` (the host runs it) was dropped from the tool list sent to the local model.
  Every `AIFunctionDeclaration` in `ChatOptions.Tools` is now sent with its name, description and schema.

## 0.27.8

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.19.12 -> 0.19.13, `IronHive.Agent.Ironbees` 0.19.12 -> 0.19.13, `IronHive.Agent.Memory` 0.19.12 -> 0.19.13, `IronHive.DeepResearch` 0.19.12 -> 0.19.13, `LMSupply.Embedder` 0.87.0 -> 0.88.0, `LMSupply.Generator` 0.87.0 -> 0.88.0, `LMSupply.Reranker` 0.87.0 -> 0.88.0, `MemoryIndexer` 0.19.16 -> 0.19.17, `MemoryIndexer.Sdk` 0.19.16 -> 0.19.17 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.27.7

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.19.11 -> 0.19.12, `IronHive.Agent.Ironbees` 0.19.11 -> 0.19.12, `IronHive.Agent.Memory` 0.19.11 -> 0.19.12, `IronHive.DeepResearch` 0.19.11 -> 0.19.12, `MemoryIndexer` 0.19.15 -> 0.19.16, `MemoryIndexer.Sdk` 0.19.15 -> 0.19.16 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.27.6

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.86.0 -> 0.87.0, `LMSupply.Generator` 0.86.0 -> 0.87.0, `LMSupply.Reranker` 0.86.0 -> 0.87.0 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.27.5

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.19.10 -> 0.19.11, `IronHive.Agent.Ironbees` 0.19.10 -> 0.19.11, `IronHive.Agent.Memory` 0.19.10 -> 0.19.11, `IronHive.DeepResearch` 0.19.10 -> 0.19.11, `LMSupply.Embedder` 0.85.0 -> 0.86.0, `LMSupply.Generator` 0.85.0 -> 0.86.0, `LMSupply.Reranker` 0.85.0 -> 0.86.0, `MemoryIndexer` 0.19.14 -> 0.19.15, `MemoryIndexer.Sdk` 0.19.14 -> 0.19.15 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.27.4

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.19.9 -> 0.19.10, `IronHive.Agent.Ironbees` 0.19.9 -> 0.19.10, `IronHive.Agent.Memory` 0.19.9 -> 0.19.10, `IronHive.DeepResearch` 0.19.9 -> 0.19.10, `LMSupply.Embedder` 0.84.0 -> 0.85.0, `LMSupply.Generator` 0.84.0 -> 0.85.0, `LMSupply.Reranker` 0.84.0 -> 0.85.0, `MemoryIndexer` 0.19.13 -> 0.19.14, `MemoryIndexer.Sdk` 0.19.13 -> 0.19.14 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.27.3

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.19.8 -> 0.19.9, `IronHive.Agent.Ironbees` 0.19.8 -> 0.19.9, `IronHive.Agent.Memory` 0.19.8 -> 0.19.9, `IronHive.DeepResearch` 0.19.8 -> 0.19.9, `LMSupply.Embedder` 0.83.0 -> 0.84.0, `LMSupply.Generator` 0.83.0 -> 0.84.0, `LMSupply.Reranker` 0.83.0 -> 0.84.0, `MemoryIndexer` 0.19.12 -> 0.19.13, `MemoryIndexer.Sdk` 0.19.12 -> 0.19.13 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.27.2

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.19.7 -> 0.19.8, `IronHive.Agent.Ironbees` 0.19.7 -> 0.19.8, `IronHive.Agent.Memory` 0.19.7 -> 0.19.8, `IronHive.DeepResearch` 0.19.7 -> 0.19.8, `LMSupply.Embedder` 0.81.1 -> 0.83.0, `LMSupply.Generator` 0.81.1 -> 0.83.0, `LMSupply.Reranker` 0.81.1 -> 0.83.0, `MemoryIndexer` 0.19.11 -> 0.19.12, `MemoryIndexer.Sdk` 0.19.11 -> 0.19.12 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.27.1

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.20.4 -> 0.21.0, `IronHive.Agent` 0.19.6 -> 0.19.7, `IronHive.Agent.Ironbees` 0.19.6 -> 0.19.7, `IronHive.Agent.Memory` 0.19.6 -> 0.19.7, `IronHive.DeepResearch` 0.19.6 -> 0.19.7 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.27.0

### Dependencies
- IronHive 0.41.0 -> 0.42.0. `IronHive.Host` now also references `IronHive.Extensions.AI`, where IronHive 0.42.0 moved
  `ChatClientAdapter` (namespace `IronHive.Extensions.AI`). A host that used `IronHive.Core.Microsoft` types through
  this package's transitive `IronHive.Core` reference must switch that `using` — see IronHive 0.42.0's migration note.

## 0.26.4

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.19.5 -> 0.19.6, `IronHive.Agent.Ironbees` 0.19.5 -> 0.19.6, `IronHive.Agent.Memory` 0.19.5 -> 0.19.6, `IronHive.DeepResearch` 0.19.5 -> 0.19.6, `LMSupply.Embedder` 0.80.0 -> 0.81.1, `LMSupply.Generator` 0.80.0 -> 0.81.1, `LMSupply.Reranker` 0.80.0 -> 0.81.1, `MemoryIndexer` 0.19.10 -> 0.19.11, `MemoryIndexer.Sdk` 0.19.10 -> 0.19.11 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.26.3

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.19.4 -> 0.19.5, `IronHive.Agent.Ironbees` 0.19.4 -> 0.19.5, `IronHive.Agent.Memory` 0.19.4 -> 0.19.5, `IronHive.DeepResearch` 0.19.4 -> 0.19.5, `MemoryIndexer` 0.19.9 -> 0.19.10, `MemoryIndexer.Sdk` 0.19.9 -> 0.19.10 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.26.2

### Added
- **Usage from the local LMSupply model reports prompt tokens served from the server's cache.** `LMSupplyChatClient` sets `UsageDetails.CachedInputTokenCount`, on both the response and the streamed final update. The value comes from llama-server's timings, which LMSupply 0.80.0 exposes. Those tokens are still counted in `InputTokenCount`; they were not evaluated. The value is null when the backend reports no timings.

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.79.1 -> 0.80.0, `LMSupply.Generator` 0.79.1 -> 0.80.0, `LMSupply.Reranker` 0.79.1 -> 0.80.0.

## 0.26.1

### Changed
- Re-pinned sibling package(s) `Cronex.Net` 0.6.0 -> 0.6.1, `Cronex.Net.Hosting` 0.6.0 -> 0.6.1, `IronHive.Abstractions` 0.40.0 -> 0.41.0, `IronHive.Core` 0.40.0 -> 0.41.0, `IronHive.Providers.Anthropic` 0.40.0 -> 0.41.0, `IronHive.Providers.GoogleAI` 0.40.0 -> 0.41.0, `IronHive.Providers.OpenAI` 0.40.0 -> 0.41.0, `IronHive.Providers.OpenAI.Compatible` 0.40.0 -> 0.41.0, `LMSupply.Embedder` 0.79.0 -> 0.79.1, `LMSupply.Generator` 0.79.0 -> 0.79.1, `LMSupply.Reranker` 0.79.0 -> 0.79.1 — re-consumption of already-consumed iyulab packages.
- Re-pinned sibling package(s) `Ironbees.Core` 0.20.3 -> 0.20.4, `MemoryIndexer` 0.19.8 -> 0.19.9, `MemoryIndexer.Sdk` 0.19.8 -> 0.19.9 — re-consumption of already-consumed iyulab packages.
- Re-pinned sibling package(s) `IronHive.Agent` 0.19.3 -> 0.19.4, `IronHive.Agent.Ironbees` 0.19.3 -> 0.19.4, `IronHive.Agent.Memory` 0.19.3 -> 0.19.4, `IronHive.DeepResearch` 0.19.3 -> 0.19.4 — re-consumption of already-consumed iyulab packages.

### Fixed
- **A service provider disposed with `Dispose()` no longer throws when DeepResearch is enabled.** `DeepResearchTool` (a singleton) implemented only `IAsyncDisposable`, and a container or scope disposed with `Dispose()` throws on such a service ("type only implements IAsyncDisposable"); it now implements `IDisposable` too.

## 0.26.0

### Added
- **Streamed responses from a local LMSupply model report token usage.** The final update carries the backend's count (LMSupply 0.79.0, reasoning included) as a `UsageContent`, so `ToChatResponse()` has a `Usage`.

### Fixed
- **`LMSupplyChatClient.GetResponseAsync` dropped the backend's token usage and the model id.** The response now carries `Usage` (prompt, completion and total tokens) and `ModelId`; before, both were always null, so cost and budget tracking saw zero for local turns.

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.78.0 -> 0.79.0, `LMSupply.Generator` 0.78.0 -> 0.79.0, `LMSupply.Reranker` 0.78.0 -> 0.79.0.
- Re-pinned sibling package(s) `MemoryIndexer` 0.19.7 -> 0.19.8, `MemoryIndexer.Sdk` 0.19.7 -> 0.19.8 — re-consumption of already-consumed iyulab packages.
- Re-pinned sibling package(s) `IronHive.Agent` 0.19.2 -> 0.19.3, `IronHive.Agent.Ironbees` 0.19.2 -> 0.19.3, `IronHive.Agent.Memory` 0.19.2 -> 0.19.3, `IronHive.DeepResearch` 0.19.2 -> 0.19.3 — re-consumption of already-consumed iyulab packages.

## 0.25.4

### Changed
- Re-pinned sibling package(s) `IronHive.Abstractions` 0.39.0 -> 0.40.0, `IronHive.Core` 0.39.0 -> 0.40.0, `IronHive.Providers.Anthropic` 0.39.0 -> 0.40.0, `IronHive.Providers.GoogleAI` 0.39.0 -> 0.40.0, `IronHive.Providers.OpenAI` 0.39.0 -> 0.40.0, `IronHive.Providers.OpenAI.Compatible` 0.39.0 -> 0.40.0, `LMSupply.Embedder` 0.77.0 -> 0.78.0, `LMSupply.Generator` 0.77.0 -> 0.78.0, `LMSupply.Reranker` 0.77.0 -> 0.78.0 — re-consumption of already-consumed iyulab packages.
- Re-pinned sibling package(s) `Ironbees.Core` 0.20.2 -> 0.20.3, `MemoryIndexer` 0.19.6 -> 0.19.7, `MemoryIndexer.Sdk` 0.19.6 -> 0.19.7 — re-consumption of already-consumed iyulab packages.
- Re-pinned sibling package(s) `IronHive.Agent` 0.19.1 -> 0.19.2, `IronHive.Agent.Ironbees` 0.19.1 -> 0.19.2, `IronHive.Agent.Memory` 0.19.1 -> 0.19.2, `IronHive.DeepResearch` 0.19.1 -> 0.19.2 — re-consumption of already-consumed iyulab packages.

### Fixed
- **`ConfigurationManager(projectRoot:)` now reads `.ironhive/permissions.{yaml,yml,json}` from `projectRoot`**, like
  `.ironhive/config.yaml` and `.env`. It used to read the permission file from the process working directory, so a
  host that pinned `projectRoot` still picked up (or, since 0.25.2, failed to start on) a permission file wherever the
  process happened to run. Without `projectRoot` the working directory is still the project scope. The loaded
  `PermissionConfig.WorkingDirectory` is now `projectRoot`.

## 0.25.3

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.20.1 -> 0.20.2, `IronHive.Abstractions` 0.38.0 -> 0.39.0, `IronHive.Agent` 0.19.0 -> 0.19.1, `IronHive.Agent.Ironbees` 0.19.0 -> 0.19.1, `IronHive.Agent.Memory` 0.19.0 -> 0.19.1, `IronHive.Core` 0.38.0 -> 0.39.0, `IronHive.DeepResearch` 0.19.0 -> 0.19.1, `IronHive.Providers.Anthropic` 0.38.0 -> 0.39.0, `IronHive.Providers.GoogleAI` 0.38.0 -> 0.39.0, `IronHive.Providers.OpenAI` 0.38.0 -> 0.39.0, `IronHive.Providers.OpenAI.Compatible` 0.38.0 -> 0.39.0, `LMSupply.Embedder` 0.76.0 -> 0.77.0, `LMSupply.Generator` 0.76.0 -> 0.77.0, `LMSupply.Reranker` 0.76.0 -> 0.77.0, `MemoryIndexer` 0.19.5 -> 0.19.6, `MemoryIndexer.Sdk` 0.19.5 -> 0.19.6 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.25.2

### Changed
- **A permission file that cannot be read now stops the host at start**, naming the file (IronHive.Agent 0.19.0):
  `.ironhive/permissions.{yaml,yml,json}` that is malformed, misspells a section or key, or has an action other than
  `allow`/`deny`/`ask` used to fall back silently to the default rules, which allow more than a restrictive file would.
- References the new `IronHive.Agent.Memory` and `IronHive.Agent.Ironbees` packages (IronHive.Agent 0.19.0 split them out).
- Re-pinned sibling package(s) `Ironbees.Core` 0.19.4 -> 0.20.1, `IronHive.Abstractions` 0.37.0 -> 0.38.0, `IronHive.Agent` 0.17.1 -> 0.19.0, `IronHive.Core` 0.37.0 -> 0.38.0, `IronHive.DeepResearch` 0.17.1 -> 0.19.0, `IronHive.Providers.Anthropic` 0.37.0 -> 0.38.0, `IronHive.Providers.GoogleAI` 0.37.0 -> 0.38.0, `IronHive.Providers.OpenAI` 0.37.0 -> 0.38.0, `IronHive.Providers.OpenAI.Compatible` 0.37.0 -> 0.38.0, `LMSupply.Embedder` 0.75.0 -> 0.76.0, `LMSupply.Generator` 0.75.0 -> 0.76.0, `LMSupply.Reranker` 0.75.0 -> 0.76.0, `MemoryIndexer` 0.19.4 -> 0.19.5, `MemoryIndexer.Sdk` 0.19.4 -> 0.19.5 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.25.1

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.19.3 -> 0.19.4, `IronHive.Abstractions` 0.36.0 -> 0.37.0, `IronHive.Agent` 0.16.1 -> 0.17.1, `IronHive.Core` 0.36.0 -> 0.37.0, `IronHive.DeepResearch` 0.16.1 -> 0.17.1, `IronHive.Providers.Anthropic` 0.36.0 -> 0.37.0, `IronHive.Providers.GoogleAI` 0.36.0 -> 0.37.0, `IronHive.Providers.OpenAI` 0.36.0 -> 0.37.0, `IronHive.Providers.OpenAI.Compatible` 0.36.0 -> 0.37.0 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.25.0

### Added
- **`--output jsonl` writes a `tool_result` record when a tool finishes** (`id`, `name`, `success`, `result`; `id`
  pairs it with the `tool_call` record). Before, a JSON Lines reader saw a tool start and never what it returned.

### Changed
- **The server relays each tool's `tool_end` event as the tool finishes**, not when the turn ends. An outcome that
  never arrived on its own chunk (a chat client without function invocation, a permission refusal) is still relayed
  from the final turn record, once.
- Re-pinned sibling package(s) `Ironbees.Core` 0.19.2 -> 0.19.3, `IronHive.Abstractions` 0.35.0 -> 0.36.0, `IronHive.Agent` 0.15.10 -> 0.16.1, `IronHive.Core` 0.35.0 -> 0.36.0, `IronHive.DeepResearch` 0.15.10 -> 0.16.1, `IronHive.Providers.Anthropic` 0.35.0 -> 0.36.0, `IronHive.Providers.GoogleAI` 0.35.0 -> 0.36.0, `IronHive.Providers.OpenAI` 0.35.0 -> 0.36.0, `IronHive.Providers.OpenAI.Compatible` 0.35.0 -> 0.36.0, `LMSupply.Embedder` 0.74.0 -> 0.75.0, `LMSupply.Generator` 0.74.0 -> 0.75.0, `LMSupply.Reranker` 0.74.0 -> 0.75.0, `MemoryIndexer` 0.19.3 -> 0.19.4, `MemoryIndexer.Sdk` 0.19.3 -> 0.19.4 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.24.9

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.19.1 -> 0.19.2, `IronHive.Abstractions` 0.34.0 -> 0.35.0, `IronHive.Agent` 0.15.9 -> 0.15.10, `IronHive.Core` 0.34.0 -> 0.35.0, `IronHive.DeepResearch` 0.15.9 -> 0.15.10, `IronHive.Providers.Anthropic` 0.34.0 -> 0.35.0, `IronHive.Providers.GoogleAI` 0.34.0 -> 0.35.0, `IronHive.Providers.OpenAI` 0.34.0 -> 0.35.0, `IronHive.Providers.OpenAI.Compatible` 0.34.0 -> 0.35.0, `LMSupply.Embedder` 0.73.0 -> 0.74.0, `LMSupply.Generator` 0.73.0 -> 0.74.0, `LMSupply.Reranker` 0.73.0 -> 0.74.0, `MemoryIndexer` 0.19.2 -> 0.19.3, `MemoryIndexer.Sdk` 0.19.2 -> 0.19.3, `TokenMeter` 0.7.7 -> 0.7.8 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.24.8

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.19.0 -> 0.19.1, `IronHive.Agent` 0.15.8 -> 0.15.9, `IronHive.DeepResearch` 0.15.8 -> 0.15.9, `TokenMeter` 0.7.6 -> 0.7.7 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.24.7

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.18.0 -> 0.19.0, `IronHive.Abstractions` 0.33.1 -> 0.34.0, `IronHive.Agent` 0.15.7 -> 0.15.8, `IronHive.Core` 0.33.1 -> 0.34.0, `IronHive.DeepResearch` 0.15.7 -> 0.15.8, `IronHive.Providers.Anthropic` 0.33.1 -> 0.34.0, `IronHive.Providers.GoogleAI` 0.33.1 -> 0.34.0, `IronHive.Providers.OpenAI` 0.33.1 -> 0.34.0, `IronHive.Providers.OpenAI.Compatible` 0.33.1 -> 0.34.0, `LMSupply.Embedder` 0.72.1 -> 0.73.0, `LMSupply.Generator` 0.72.1 -> 0.73.0, `LMSupply.Reranker` 0.72.1 -> 0.73.0, `MemoryIndexer` 0.19.0 -> 0.19.2, `MemoryIndexer.Sdk` 0.19.0 -> 0.19.2, `TokenMeter` 0.7.5 -> 0.7.6 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.24.6

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.17.3 -> 0.18.0, `IronHive.Agent` 0.15.6 -> 0.15.7, `IronHive.DeepResearch` 0.15.6 -> 0.15.7, `MemoryIndexer` 0.18.5 -> 0.19.0, `MemoryIndexer.Sdk` 0.18.5 -> 0.19.0 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.24.5

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.15.4 -> 0.15.6, `IronHive.DeepResearch` 0.15.4 -> 0.15.6, `LMSupply.Embedder` 0.72.0 -> 0.72.1, `LMSupply.Generator` 0.72.0 -> 0.72.1, `LMSupply.Reranker` 0.72.0 -> 0.72.1, `MemoryIndexer` 0.18.4 -> 0.18.5, `MemoryIndexer.Sdk` 0.18.4 -> 0.18.5 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.24.4

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.17.2 -> 0.17.3, `IronHive.Abstractions` 0.33.0 -> 0.33.1, `IronHive.Agent` 0.15.3 -> 0.15.4, `IronHive.Core` 0.33.0 -> 0.33.1, `IronHive.DeepResearch` 0.15.3 -> 0.15.4, `IronHive.Providers.Anthropic` 0.33.0 -> 0.33.1, `IronHive.Providers.GoogleAI` 0.33.0 -> 0.33.1, `IronHive.Providers.OpenAI` 0.33.0 -> 0.33.1, `IronHive.Providers.OpenAI.Compatible` 0.33.0 -> 0.33.1 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.24.3

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.15.2 -> 0.15.3, `IronHive.DeepResearch` 0.15.2 -> 0.15.3, `LMSupply.Embedder` 0.71.0 -> 0.72.0, `LMSupply.Generator` 0.71.0 -> 0.72.0, `LMSupply.Reranker` 0.71.0 -> 0.72.0, `MemoryIndexer` 0.18.3 -> 0.18.4, `MemoryIndexer.Sdk` 0.18.3 -> 0.18.4 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.24.2

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.15.1 -> 0.15.2, `IronHive.DeepResearch` 0.15.1 -> 0.15.2 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.24.1

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.15.0 -> 0.15.1, `IronHive.DeepResearch` 0.15.0 -> 0.15.1, `LMSupply.Embedder` 0.70.0 -> 0.71.0, `LMSupply.Generator` 0.70.0 -> 0.71.0, `LMSupply.Reranker` 0.70.0 -> 0.71.0, `MemoryIndexer` 0.18.2 -> 0.18.3, `MemoryIndexer.Sdk` 0.18.2 -> 0.18.3 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.24.0

### Removed
- **Breaking — `IronHive.Host.Tools.ToolProvider`.** It was a second copy of the agent's file, search
  and shell tools that differed only in versioning its writes. The host now uses
  `IronHive.Agent.Tools.ToolProvider` and attaches versioning through the new
  `OopsFileWriteInterceptor`. Migration: construct `IronHive.Agent.Tools.ToolProvider(workingDirectory,
  new OopsFileWriteInterceptor(oopsService))`, or keep calling `BuiltInTools.GetAll(...)`, whose
  overloads, tool names, result messages and tool counts are unchanged.

### Added
- **Agent Skills in the host.** A `skills:` section in `config.yaml` (`roots`, `enabled`, `exclude`,
  `maxMetadataCharacters`, `acceptUnknownFields`) registers `IronHive.Agent`'s skills loader: the skills'
  name + description go into the system instructions of every loop the factory builds and a `load_skill`
  tool returns a body on demand, confined to the skill's directory. Relative roots resolve against the
  process working directory. An embedder registers the loader itself with `AddAgentSkills(config)`; the
  factory picks it up from the container. Nothing changes while no root is configured.
- **`FileToolOptions` reaches the built-in tools from the container.** Register one (`AllowedRoots`, and
  optionally a write interceptor) and `AgentLoopFactory` passes it to `BuiltInTools.GetAll`; versioning
  through the oops service stays attached unless the options bring their own interceptor. New overload
  `BuiltInTools.GetAll(workingDirectory, oopsService, webSearchTool, deepResearchTool, fileToolOptions)`.
- **The tools and the permission rules must share a working directory.** `AgentLoopFactory` now takes the
  `PermissionConfig` and refuses to build a loop whose tools work in one directory while the rules judge
  paths against another (`InvalidOperationException` naming both). Until now such a loop was built and a
  rule like `read: src/**` was matched against a path the tool never opened. The CLI's two directories are
  both the process working directory, so it is unaffected; an embedder that passed
  `AgentLoopFactoryOptions.WorkingDirectory` without moving `PermissionConfig.WorkingDirectory` now hears about it.
- **Instruction contributors reach the host's loops.** An `ISystemInstructionContributor` registered in
  the container adds its section after the system prompt — the host's default prompt stays, so adding
  one paragraph no longer means copying and maintaining all of it. Wired on both paths: the embedded
  `IAgentLoop` (`AddIronHive`) and the loops `AgentLoopFactory` builds. `HostContextManagerFactory.Create`
  takes the contributors as an optional third argument.
- `OopsFileWriteInterceptor` — records each edit, starts versioning a file that keeps being edited, and
  saves a snapshot after every write to a tracked file. `BuiltInTools.GetAll(workingDirectory, oopsService)`
  wires it up.

### Changed
- Re-pinned `IronHive.Agent` / `IronHive.DeepResearch` 0.14.6 -> 0.15.0 (the write interceptor lives there).
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.69.0 -> 0.70.0, `LMSupply.Generator` 0.69.0 -> 0.70.0, `LMSupply.Reranker` 0.69.0 -> 0.70.0 — re-consumption of already-consumed iyulab packages. No source changes.
- Re-pinned sibling package(s) `MemoryIndexer` 0.18.1 -> 0.18.2, `MemoryIndexer.Sdk` 0.18.1 -> 0.18.2 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.23.7

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.14.5 -> 0.14.6, `IronHive.DeepResearch` 0.14.5 -> 0.14.6, `LMSupply.Embedder` 0.68.3 -> 0.69.0, `LMSupply.Generator` 0.68.3 -> 0.69.0, `LMSupply.Reranker` 0.68.3 -> 0.69.0, `MemoryIndexer` 0.18.0 -> 0.18.1, `MemoryIndexer.Sdk` 0.18.0 -> 0.18.1 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.23.6

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.14.4 -> 0.14.5, `IronHive.DeepResearch` 0.14.4 -> 0.14.5, `MemoryIndexer` 0.17.16 -> 0.18.0, `MemoryIndexer.Sdk` 0.17.16 -> 0.18.0 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.23.5

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.14.3 -> 0.14.4, `IronHive.DeepResearch` 0.14.3 -> 0.14.4 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.23.4

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.14.2 -> 0.14.3, `IronHive.DeepResearch` 0.14.2 -> 0.14.3 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.23.3

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.17.1 -> 0.17.2, `IronHive.Abstractions` 0.32.0 -> 0.33.0, `IronHive.Agent` 0.14.1 -> 0.14.2, `IronHive.Core` 0.32.0 -> 0.33.0, `IronHive.DeepResearch` 0.14.1 -> 0.14.2, `IronHive.Providers.Anthropic` 0.32.0 -> 0.33.0, `IronHive.Providers.GoogleAI` 0.32.0 -> 0.33.0, `IronHive.Providers.OpenAI` 0.32.0 -> 0.33.0, `IronHive.Providers.OpenAI.Compatible` 0.32.0 -> 0.33.0 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.23.2

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.16.0 -> 0.17.1, `IronHive.Abstractions` 0.31.0 -> 0.32.0, `IronHive.Agent` 0.14.0 -> 0.14.1, `IronHive.Core` 0.31.0 -> 0.32.0, `IronHive.DeepResearch` 0.14.0 -> 0.14.1, `IronHive.Providers.Anthropic` 0.31.0 -> 0.32.0, `IronHive.Providers.GoogleAI` 0.31.0 -> 0.32.0, `IronHive.Providers.OpenAI` 0.31.0 -> 0.32.0, `IronHive.Providers.OpenAI.Compatible` 0.31.0 -> 0.32.0 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.23.1

### Changed
- **The `deep_research` tool honours the host's `SufficiencyThreshold` and `MinSourcesBeforeReport`**, and its streaming run retries
  a search that found nothing (IronHive.DeepResearch 0.14.0). The tool no longer sets `ResearchRequest.OutputFormat`, which was
  removed because reports are always Markdown.
- Re-pinned sibling package(s) `Ironbees.Core` 0.15.0 -> 0.16.0 — re-consumption of already-consumed iyulab packages. No source changes.
- Re-pinned sibling package(s) `IronHive.Abstractions` 0.30.0 -> 0.31.0, `IronHive.Core` 0.30.0 -> 0.31.0, `IronHive.Providers.Anthropic` 0.30.0 -> 0.31.0, `IronHive.Providers.GoogleAI` 0.30.0 -> 0.31.0, `IronHive.Providers.OpenAI` 0.30.0 -> 0.31.0, `IronHive.Providers.OpenAI.Compatible` 0.30.0 -> 0.31.0 — re-consumption of already-consumed iyulab packages. No source changes.

### Fixed
- **`DeepResearch.MaxIterations` in the host config limits research iterations.** It was passed to
  `DeepResearchOptions.DefaultMaxIterations`, which nothing read, so every query ran up to the request default of 5 whatever the
  config said. It now sets the request's `MaxIterations`, which the depth still caps. The default (5) is unchanged.

### Dependencies
- `IronHive.Agent` 0.13.0 -> 0.14.0, `IronHive.DeepResearch` 0.13.0 -> 0.14.0.

## 0.23.0

### Fixed
- **A transient provider failure is retried once, and a usage limit registered as `IUsageLimiter` is enforced**,
  on every agent loop the host builds. The thinking loop the CLI always uses had neither. The CLI now registers
  a default `IErrorRecoveryService` (an embedder's own registration wins).
- **Anthropic calls from the CLI no longer fail with 404.** The provider was configured with a base URL that already
  ended in `/v1/`, and the SDK adds the versioned path itself, so every request went to `/v1/v1/messages`.

### Added
- **`advisor` config section** (`provider`, `model`, `maxCalls`). With a model set, every session's tools include an
  `advisor` tool that sends the conversation so far to that model and returns its review; it is allowed by the default
  permission rules (read-only) and shows up as an ordinary `tool_start`/`tool_end` on the server wire.

### Changed
- Re-pinned `IronHive.Agent` and `IronHive.DeepResearch` 0.12.12 -> 0.13.0, `Ironbees.Core` 0.14.12 -> 0.15.0.
- Re-pinned sibling package(s) `IronHive.Abstractions` 0.29.1 -> 0.29.2, `IronHive.Core` 0.29.1 -> 0.29.2, `IronHive.Providers.Anthropic` 0.29.1 -> 0.29.2, `IronHive.Providers.GoogleAI` 0.29.1 -> 0.29.2, `IronHive.Providers.OpenAI` 0.29.1 -> 0.29.2, `IronHive.Providers.OpenAI.Compatible` 0.29.1 -> 0.29.2 — re-consumption of already-consumed iyulab packages. No source changes.
- Re-pinned sibling package(s) `IronHive.Abstractions` 0.29.2 -> 0.30.0, `IronHive.Core` 0.29.2 -> 0.30.0, `IronHive.Providers.Anthropic` 0.29.2 -> 0.30.0, `IronHive.Providers.GoogleAI` 0.29.2 -> 0.30.0, `IronHive.Providers.OpenAI` 0.29.2 -> 0.30.0, `IronHive.Providers.OpenAI.Compatible` 0.29.2 -> 0.30.0 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.22.13

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.12.11 -> 0.12.12, `IronHive.DeepResearch` 0.12.11 -> 0.12.12, `LMSupply.Embedder` 0.68.2 -> 0.68.3, `LMSupply.Generator` 0.68.2 -> 0.68.3, `LMSupply.Reranker` 0.68.2 -> 0.68.3, `MemoryIndexer` 0.17.15 -> 0.17.16, `MemoryIndexer.Sdk` 0.17.15 -> 0.17.16 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.22.12

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.14.10 -> 0.14.12, `IronHive.Abstractions` 0.28.4 -> 0.29.1, `IronHive.Agent` 0.12.9 -> 0.12.11, `IronHive.Core` 0.28.4 -> 0.29.1, `IronHive.DeepResearch` 0.12.9 -> 0.12.11, `IronHive.Providers.Anthropic` 0.28.4 -> 0.29.1, `IronHive.Providers.GoogleAI` 0.28.4 -> 0.29.1, `IronHive.Providers.OpenAI` 0.28.4 -> 0.29.1, `IronHive.Providers.OpenAI.Compatible` 0.28.4 -> 0.29.1, `LMSupply.Embedder` 0.68.0 -> 0.68.2, `LMSupply.Generator` 0.68.0 -> 0.68.2, `LMSupply.Reranker` 0.68.0 -> 0.68.2, `MemoryIndexer` 0.17.13 -> 0.17.15, `MemoryIndexer.Sdk` 0.17.13 -> 0.17.15 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.22.11

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.14.9 -> 0.14.10, `IronHive.Abstractions` 0.28.2 -> 0.28.4, `IronHive.Agent` 0.12.8 -> 0.12.9, `IronHive.Core` 0.28.2 -> 0.28.4, `IronHive.DeepResearch` 0.12.8 -> 0.12.9, `IronHive.Providers.Anthropic` 0.28.2 -> 0.28.4, `IronHive.Providers.GoogleAI` 0.28.2 -> 0.28.4, `IronHive.Providers.OpenAI` 0.28.2 -> 0.28.4, `IronHive.Providers.OpenAI.Compatible` 0.28.2 -> 0.28.4, `LMSupply.Embedder` 0.67.0 -> 0.68.0, `LMSupply.Generator` 0.67.0 -> 0.68.0, `LMSupply.Reranker` 0.67.0 -> 0.68.0, `MemoryIndexer` 0.17.12 -> 0.17.13, `MemoryIndexer.Sdk` 0.17.12 -> 0.17.13 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.22.10

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.14.8 -> 0.14.9, `IronHive.Abstractions` 0.28.0 -> 0.28.2, `IronHive.Agent` 0.12.6 -> 0.12.8, `IronHive.Core` 0.28.0 -> 0.28.2, `IronHive.DeepResearch` 0.12.6 -> 0.12.8, `IronHive.Providers.Anthropic` 0.28.0 -> 0.28.2, `IronHive.Providers.GoogleAI` 0.28.0 -> 0.28.2, `IronHive.Providers.OpenAI` 0.28.0 -> 0.28.2, `IronHive.Providers.OpenAI.Compatible` 0.28.0 -> 0.28.2, `LMSupply.Embedder` 0.66.1 -> 0.67.0, `LMSupply.Generator` 0.66.1 -> 0.67.0, `LMSupply.Reranker` 0.66.1 -> 0.67.0, `MemoryIndexer` 0.17.11 -> 0.17.12, `MemoryIndexer.Sdk` 0.17.11 -> 0.17.12, `WebLookup` 0.2.2 -> 0.2.3 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.22.9

### Fixed
- The packages now ship their XML documentation file (`IronHive.Host`, `IronHive.Host.Protocol`, `IronHive.Cli`). `GenerateDocumentationFile` was never set, so every `///` doc was written but never delivered to a consumer. Generating it surfaced two `cref`s in `ChatBehaviorConfig` that did not resolve (`ResilientFunctionInvoker` lives in `IronHive.Host.Tools`); fixed. No code changes.

## 0.22.8

### Changed
- Re-pinned sibling package(s) `IronHive.Abstractions` 0.27.0 -> 0.28.0, `IronHive.Agent` 0.12.5 -> 0.12.6, `IronHive.Core` 0.27.0 -> 0.28.0, `IronHive.DeepResearch` 0.12.5 -> 0.12.6, `IronHive.Providers.Anthropic` 0.27.0 -> 0.28.0, `IronHive.Providers.GoogleAI` 0.27.0 -> 0.28.0, `IronHive.Providers.OpenAI` 0.27.0 -> 0.28.0, `IronHive.Providers.OpenAI.Compatible` 0.27.0 -> 0.28.0, `MemoryIndexer` 0.17.10 -> 0.17.11, `MemoryIndexer.Sdk` 0.17.10 -> 0.17.11, `WebLookup` 0.2.1 -> 0.2.2 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.22.7

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.14.6 -> 0.14.8, `IronHive.Agent` 0.12.4 -> 0.12.5, `IronHive.DeepResearch` 0.12.4 -> 0.12.5, `LMSupply.Embedder` 0.66.0 -> 0.66.1, `LMSupply.Generator` 0.66.0 -> 0.66.1, `LMSupply.Reranker` 0.66.0 -> 0.66.1, `MemoryIndexer` 0.17.9 -> 0.17.10, `MemoryIndexer.Sdk` 0.17.9 -> 0.17.10 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.22.6

### Changed
- Re-pinned sibling package(s) `IronHive.Abstractions` 0.26.3 -> 0.27.0, `IronHive.Core` 0.26.3 -> 0.27.0, `IronHive.Providers.Anthropic` 0.26.3 -> 0.27.0, `IronHive.Providers.GoogleAI` 0.26.3 -> 0.27.0, `IronHive.Providers.OpenAI` 0.26.3 -> 0.27.0, `IronHive.Providers.OpenAI.Compatible` 0.26.3 -> 0.27.0, `LMSupply.Embedder` 0.65.1 -> 0.66.0, `LMSupply.Generator` 0.65.1 -> 0.66.0, `LMSupply.Reranker` 0.65.1 -> 0.66.0, `TokenMeter` 0.7.4 -> 0.7.5 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.22.5

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.12.3 -> 0.12.4, `IronHive.DeepResearch` 0.12.3 -> 0.12.4 — re-consumption of already-consumed iyulab packages. No source changes.
- Raised `Microsoft.Extensions.*` package references to 10.0.12 (latest servicing release). The re-pinned sibling releases declare `Microsoft.Extensions.*` floors above the previous references.

## 0.22.4

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.14.5 -> 0.14.6, `IronHive.Agent` 0.12.1 -> 0.12.3, `IronHive.DeepResearch` 0.12.1 -> 0.12.3 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.22.3

### Changed
- Re-pinned sibling package(s) `IronHive.Abstractions` 0.26.2 -> 0.26.3, `IronHive.Core` 0.26.2 -> 0.26.3, `IronHive.Providers.Anthropic` 0.26.2 -> 0.26.3, `IronHive.Providers.GoogleAI` 0.26.2 -> 0.26.3, `IronHive.Providers.OpenAI` 0.26.2 -> 0.26.3, `IronHive.Providers.OpenAI.Compatible` 0.26.2 -> 0.26.3 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.22.2

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.12.0 -> 0.12.1, `IronHive.DeepResearch` 0.12.0 -> 0.12.1 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.22.1

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.14.4 -> 0.14.5, `MemoryIndexer` 0.17.8 -> 0.17.9, `MemoryIndexer.Sdk` 0.17.8 -> 0.17.9 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.22.0

### Added
- The permission rules now gate every tool call, and the console approval prompt fires for the
  first time. `ApprovalGatedFunctionInvoker` (IronHive.Agent 0.12.0) sits on top of the resilient
  invoker in the chat client chain: an `Allow` verdict runs the tool, `Deny` returns the reason to the
  model, `Ask` (for example editing `*.json` or `.env*`, or any tool no rule names) shows the
  Spectre.Console approval prompt and runs the tool only on approval. Until now
  `ConsoleApprovalService` was registered and never called, so every `Ask` rule ran unasked.
- `permissions.tools` in `config.yaml`: rules matched by tool name for tools with no dedicated
  category (`WebSearch`, MCP-less plugins, ...). A merge that defines only this section is applied
  like the others.

- `tool_end` events on the server wire. The protocol declared `ToolEndEvent` from the start and
  nothing emitted it: a client saw a tool start and never what it returned. The final chunk's
  consolidated tool outcomes are now relayed as one `tool_end` per call — `success`, `output`, and
  `call_id` matching the `tool_start` — so a client sees a permission-gate refusal
  (`success: false`, `output: "Approval rejected: ..."`) instead of only that the tool started.

- `thinking_delta` events on the server wire: the loop streamed extended-thinking content on its own
  chunk field and the protocol declared the event, but the mapper never connected them, so a client
  with a thinking pane got nothing.
- A test now pins every declared protocol event to a construction site in host source (and every
  request to a consumer), with the still-unemitted ones listed by name and reason — so a declared
  type nothing sends cannot stay silent again.

### Fixed
- The configured `lmsupply.generatorModel` was never read. The LMSupply chat provider was
  registered twice — once on the user's config and once, "for `/model` selection", on a fresh
  `LMSupplyConfig` — and the second registration won, so every local run loaded the class default
  regardless of config or `--model`. Registered once, on the configured models (with `Enabled`
  forced on when LMSupply is not the enabled fallback).
- That class default was `gguf:default`, an alias LMSupply does not register: a fresh install
  failed at its first local inference with "not a registered GGUF alias". The default is now
  `gguf:auto`.
- `--model` no longer loads the configured default model first. The provider initialized the
  default before honouring an override, so a broken default failed an explicit model and a working
  one cost a second model load.

### Changed
- `ConsoleApprovalService` rejects an approval request with a reason when stdin or stdout is
  redirected — `run --server` speaks JSON Lines on those streams, so a prompt must never land in
  them, and a piped one-shot run has nobody to answer. Such a call reaches the model as
  `Approval rejected: ...`; allow the tool by rule if it should run unattended.
- Re-pinned `IronHive.Agent`, `IronHive.DeepResearch` 0.11.1 -> 0.12.0.

## 0.21.2

### Changed
- Re-pinned sibling package(s) `IronHive.Abstractions` 0.26.1 -> 0.26.2, `IronHive.Agent` 0.11.0 -> 0.11.1, `IronHive.Core` 0.26.1 -> 0.26.2, `IronHive.DeepResearch` 0.11.0 -> 0.11.1, `IronHive.Providers.Anthropic` 0.26.1 -> 0.26.2, `IronHive.Providers.GoogleAI` 0.26.1 -> 0.26.2, `IronHive.Providers.OpenAI` 0.26.1 -> 0.26.2, `IronHive.Providers.OpenAI.Compatible` 0.26.1 -> 0.26.2, `LMSupply.Embedder` 0.65.0 -> 0.65.1, `LMSupply.Generator` 0.65.0 -> 0.65.1, `LMSupply.Reranker` 0.65.0 -> 0.65.1 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.21.1

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.14.3 -> 0.14.4, `MemoryIndexer` 0.17.7 -> 0.17.8, `MemoryIndexer.Sdk` 0.17.7 -> 0.17.8 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.21.0

### Added
- `AddendumEvent` (`addendum`) in `IronHive.Host.Protocol`: the text a turn observer appended after a turn now leaves the host as its own event — at most once per turn, after the last `text_delta` and before `turn_end`, only when an observer appended something. Until now it was relayed as one more `text_delta` (the agent carried it as the final chunk's text delta), so a client could not tell the note from the model's words without re-deriving it from the concatenated text. `AgentResponseMapper` maps `AgentResponseChunk.Addendum` (IronHive.Agent 0.11.0) to it.

- `UserMessageRequest.Options` (`TurnOptions`) in `IronHive.Host.Protocol`: a client can narrow or tune one turn — `tool_names` (subset of the agent's registered tools), `tool_mode` (`auto`/`none`/`require_any`/`require:<tool>`), `reasoning_effort`, `temperature`, `max_output_tokens`. The host maps it to the agent loop's per-turn `ChatOptions` override (`TurnOptionsMapper`), field by field: set fields replace the agent's configuration for that turn only, unset fields keep it, nothing persists to the next turn. A tool name that is not registered, or an unknown mode/effort, is answered with an `error` event rather than dropped. A request without `options` serializes exactly as before.
- The CLI's streaming outputs (`--format jsonl` as `{"type":"addendum"}`, and the console paths after the model's text) render a turn observer's note, which IronHive.Agent 0.11.0 no longer delivers as a text delta.

### Changed
- Re-pinned `IronHive.Agent` / `IronHive.DeepResearch` 0.10.5 -> 0.11.0.
- A client that concatenated every `text_delta` into the assistant's answer no longer sees an observer's note in that answer; render `addendum` separately.

## 0.20.15

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.14.2 -> 0.14.3, `IronHive.Abstractions` 0.26.0 -> 0.26.1, `IronHive.Agent` 0.10.4 -> 0.10.5, `IronHive.Core` 0.26.0 -> 0.26.1, `IronHive.DeepResearch` 0.10.4 -> 0.10.5, `IronHive.Providers.Anthropic` 0.26.0 -> 0.26.1, `IronHive.Providers.GoogleAI` 0.26.0 -> 0.26.1, `IronHive.Providers.OpenAI` 0.26.0 -> 0.26.1, `IronHive.Providers.OpenAI.Compatible` 0.26.0 -> 0.26.1, `LMSupply.Embedder` 0.64.0 -> 0.65.0, `LMSupply.Generator` 0.64.0 -> 0.65.0, `LMSupply.Reranker` 0.64.0 -> 0.65.0, `MemoryIndexer` 0.17.6 -> 0.17.7, `MemoryIndexer.Sdk` 0.17.6 -> 0.17.7 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.20.14

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.14.1 -> 0.14.2, `IronHive.Abstractions` 0.25.0 -> 0.26.0, `IronHive.Agent` 0.10.3 -> 0.10.4, `IronHive.Core` 0.25.0 -> 0.26.0, `IronHive.DeepResearch` 0.10.3 -> 0.10.4, `IronHive.Providers.Anthropic` 0.25.0 -> 0.26.0, `IronHive.Providers.GoogleAI` 0.25.0 -> 0.26.0, `IronHive.Providers.OpenAI` 0.25.0 -> 0.26.0, `IronHive.Providers.OpenAI.Compatible` 0.25.0 -> 0.26.0, `LMSupply.Embedder` 0.63.0 -> 0.64.0, `LMSupply.Generator` 0.63.0 -> 0.64.0, `LMSupply.Reranker` 0.63.0 -> 0.64.0, `MemoryIndexer` 0.17.5 -> 0.17.6, `MemoryIndexer.Sdk` 0.17.5 -> 0.17.6 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.20.13

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.13.6 -> 0.14.1, `IronHive.Abstractions` 0.24.1 -> 0.25.0, `IronHive.Agent` 0.10.1 -> 0.10.3, `IronHive.Core` 0.24.1 -> 0.25.0, `IronHive.DeepResearch` 0.10.1 -> 0.10.3, `IronHive.Providers.Anthropic` 0.24.1 -> 0.25.0, `IronHive.Providers.GoogleAI` 0.24.1 -> 0.25.0, `IronHive.Providers.OpenAI` 0.24.1 -> 0.25.0, `IronHive.Providers.OpenAI.Compatible` 0.24.1 -> 0.25.0, `LMSupply.Embedder` 0.62.0 -> 0.63.0, `LMSupply.Generator` 0.62.0 -> 0.63.0, `LMSupply.Reranker` 0.62.0 -> 0.63.0, `MemoryIndexer` 0.17.4 -> 0.17.5, `MemoryIndexer.Sdk` 0.17.4 -> 0.17.5 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.20.12

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.13.5 -> 0.13.6, `IronHive.Agent` 0.10.0 -> 0.10.1, `IronHive.DeepResearch` 0.10.0 -> 0.10.1, `LMSupply.Embedder` 0.61.0 -> 0.62.0, `LMSupply.Generator` 0.61.0 -> 0.62.0, `LMSupply.Reranker` 0.61.0 -> 0.62.0, `MemoryIndexer` 0.17.3 -> 0.17.4, `MemoryIndexer.Sdk` 0.17.3 -> 0.17.4 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.20.11

### Changed
- Re-pinned sibling package(s) `IronHive.Abstractions` 0.24.0 -> 0.24.1, `IronHive.Agent` 0.9.10 -> 0.10.0, `IronHive.Core` 0.24.0 -> 0.24.1, `IronHive.DeepResearch` 0.9.10 -> 0.10.0, `IronHive.Providers.Anthropic` 0.24.0 -> 0.24.1, `IronHive.Providers.GoogleAI` 0.24.0 -> 0.24.1, `IronHive.Providers.OpenAI` 0.24.0 -> 0.24.1, `IronHive.Providers.OpenAI.Compatible` 0.24.0 -> 0.24.1, `LMSupply.Embedder` 0.60.0 -> 0.61.0, `LMSupply.Generator` 0.60.0 -> 0.61.0, `LMSupply.Reranker` 0.60.0 -> 0.61.0, `MemoryIndexer` 0.17.2 -> 0.17.3, `MemoryIndexer.Sdk` 0.17.2 -> 0.17.3, `TokenMeter` 0.7.3 -> 0.7.4 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.20.10

### Changed
- Re-pinned sibling package(s) `Ironbees.Core` 0.13.4 -> 0.13.5, `IronHive.Agent` 0.9.9 -> 0.9.10, `IronHive.DeepResearch` 0.9.9 -> 0.9.10, `LMSupply.Embedder` 0.59.1 -> 0.60.0, `LMSupply.Generator` 0.59.1 -> 0.60.0, `LMSupply.Reranker` 0.59.1 -> 0.60.0 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.20.9

### Changed
- Re-pinned `Ironbees.Core` 0.13.3 -> 0.13.4, `IronHive.Abstractions` 0.22.2 -> 0.24.0, `IronHive.Agent` 0.9.8 -> 0.9.9, `IronHive.Core` 0.22.2 -> 0.24.0, `IronHive.DeepResearch` 0.9.8 -> 0.9.9, `IronHive.Providers.Anthropic` 0.22.2 -> 0.24.0, `IronHive.Providers.GoogleAI` 0.22.2 -> 0.24.0, `IronHive.Providers.OpenAI` 0.22.2 -> 0.24.0, `IronHive.Providers.OpenAI.Compatible` 0.22.2 -> 0.24.0, `LMSupply.Embedder` 0.58.0 -> 0.59.1, `LMSupply.Generator` 0.58.0 -> 0.59.1, `LMSupply.Reranker` 0.58.0 -> 0.59.1, `MemoryIndexer` 0.16.13 -> 0.17.2, `MemoryIndexer.Sdk` 0.16.13 -> 0.17.2.
- GPUStack provider wiring adapted to IronHive 0.23.0/0.24.0: `GpuStackMessageGenerator` was folded into `OpenAICompatibleMessageGenerator`; the host now builds it from `GpuStackConfig.ToOpenAICompatible()` (public since 0.24.0). No behaviour change.

## 0.20.8

### Changed
- Re-pinned sibling package(s) `IronHive.Abstractions` 0.22.1 -> 0.22.2, `IronHive.Agent` 0.9.7 -> 0.9.8, `IronHive.Core` 0.22.1 -> 0.22.2, `IronHive.DeepResearch` 0.9.7 -> 0.9.8, `IronHive.Providers.Anthropic` 0.22.1 -> 0.22.2, `IronHive.Providers.GoogleAI` 0.22.1 -> 0.22.2, `IronHive.Providers.OpenAI` 0.22.1 -> 0.22.2, `IronHive.Providers.OpenAI.Compatible` 0.22.1 -> 0.22.2, `LMSupply.Embedder` 0.57.0 -> 0.58.0, `LMSupply.Generator` 0.57.0 -> 0.58.0, `LMSupply.Reranker` 0.57.0 -> 0.58.0, `MemoryIndexer` 0.16.12 -> 0.16.13, `MemoryIndexer.Sdk` 0.16.12 -> 0.16.13 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.20.7

### Changed
- Re-pinned sibling package(s) `IronHive.Agent` 0.9.6 -> 0.9.7, `IronHive.DeepResearch` 0.9.6 -> 0.9.7, `LMSupply.Embedder` 0.55.4 -> 0.57.0, `LMSupply.Generator` 0.55.4 -> 0.57.0, `LMSupply.Reranker` 0.55.4 -> 0.57.0, `MemoryIndexer` 0.16.11 -> 0.16.12, `MemoryIndexer.Sdk` 0.16.11 -> 0.16.12 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.20.6

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.55.0 -> 0.55.4, `LMSupply.Generator` 0.55.0 -> 0.55.4, `LMSupply.Reranker` 0.55.0 -> 0.55.4, `MemoryIndexer` 0.16.8 -> 0.16.11, `MemoryIndexer.Sdk` 0.16.8 -> 0.16.11 — re-consumption of already-consumed iyulab packages. No source changes.

## 0.20.5

### Changed
- GpuStack embedding/rerank provider failures now say what to change: a missing model names the
  `gpustack.embedding_model` / `gpustack.rerank_model` config keys and their `GPUSTACK_*` environment
  variables (and the endpoint/key/model settings when GpuStack is not configured at all), and a
  malformed response names the endpoint path and model that answered. Regression tests pin the
  handles each message must contain.

## 0.20.4

### Fixed
- Re-pinned `LMSupply.Embedder`/`.Reranker`/`.Generator` from `0.54.0` to `0.55.0` — `0.54.0` was
  never actually published to nuget.org for these packages (their published history jumps
  `0.45.0` -> `0.55.0`), so `0.20.3`'s restore failed with NU1603 escalated to error and no nupkg
  for `0.20.3` was ever produced.

## 0.20.3

### Fixed
- The CLI no longer prints an `InvalidOperationException` ("... type only implements
  IAsyncDisposable. Use DisposeAsync to dispose the container.") after every command exits.
  `TypeResolver.Dispose()` was unconditionally calling the DI container's synchronous
  `IDisposable.Dispose()`, which throws whenever any registered service implements only
  `IAsyncDisposable` (e.g. the MCP plugin manager). It now disposes through
  `IAsyncDisposable.DisposeAsync()` when the container supports it, falling back to synchronous
  disposal otherwise.

## 0.20.2

### Changed
- Re-pinned `LMSupply.Embedder`/`.Reranker`/`.Generator` from `0.42.10` to `0.54.0` — patch
  re-consumption of already-consumed sibling packages (a minor gap of 12; cold-GPU-kernel-hang
  protection propagated to all ONNX-backed lm-supply modules). No source changes.

## 0.20.1

### Changed
- Re-pinned `IronHive.Agent`/`.DeepResearch` from `0.9.5` to `0.9.6` — patch re-consumption of
  an already-consumed sibling package, right after `ironhive-agent` 0.9.6 was published. No
  source changes.

## 0.20.0

### Added
- `TurnEndEvent` now carries `InputTokens`/`OutputTokens`/`TotalTokens` (`long?`), summed across
  every model round-trip within the turn. `AgentResponseChunk.Usage` was already being handed to
  `AgentResponseMapper.ToServerEvents` and silently discarded — no new instrumentation needed on
  the agent side. Purely additive (`init`-only properties on an existing record); both server
  transports (`AgentServerRunner`, `AgentHttpRunner`) forward the mapper's own populated
  `TurnEndEvent` and only fall back to a bare one when the turn ends via exception/cancellation
  before the mapper reaches it.

## 0.19.13

### Changed
- Re-pinned `Ironbees.Core` from `0.13.2` to `0.13.3` — patch re-consumption of an already-consumed
  sibling package (lowers the `Microsoft.ML.OnnxRuntime` transitive floor to avoid a known DirectML
  crash). No source changes.

## 0.19.12

### Changed
- Re-pinned `IronHive.Agent`/`.DeepResearch` from `0.9.4` to `0.9.5` — patch re-consumption of
  an already-consumed sibling package. No source changes.

## 0.19.11

### Changed
- Re-pinned already-consumed sibling packages to their latest patch: `Ironbees.Core`
  (`0.13.1`→`0.13.2`), `MemoryIndexer`/`.Sdk` (`0.16.7`→`0.16.8`). No source changes.

## 0.19.10

### Changed
- Re-pinned already-consumed sibling packages to their latest patch/minor: `Cronex.Net`/
  `.Hosting` (`0.4.1`→`0.6.0`), `IronHive.Agent`/`.DeepResearch` (`0.9.1`→`0.9.4`),
  `LMSupply.Embedder`/`.Reranker`/`.Generator` (`0.42.2`→`0.42.10`), `MemoryIndexer`/`.Sdk`
  (`0.16.5`→`0.16.7`), `TokenMeter` (`0.7.0`→`0.7.3`). No source changes.

## 0.19.9

### Documentation
- `README.md` documents the actual response schema for `-o json`/`jsonl` and `sessions
  list`/`delete --output json` for the first time — the "Quick Start" section previously showed
  only invocation examples, never the shape of what came back. Includes the `toolCalls[].success`
  3-state (`true`/`false`/`null`) semantics and the "fields serializing to `null` are omitted, not
  written as `null`" behavior of the shared `JsonSerializerOptions`.

## 0.19.8

### Changed
- Repinned `IronHive.Agent`/`IronHive.DeepResearch` 0.7.2 → 0.9.1 and `IronHive.Abstractions`/
  `.Core`/`.Providers.*` 0.20.0 → 0.22.1 to clear a pin-drift gap that had opened since 2026-08-07.

### Fixed
- `AgentLoopSessionExtensions.SaveTurnAsync` treated `ToolCallResult.Success == null` (outcome
  unknown — no function-invocation middleware in the pipeline) as an error when negating it
  directly. `IronHive.Agent` 0.9.0 made `Success` nullable for exactly this case; the negation now
  only reports an error on an explicit `false`, leaving unknown outcomes unmarked. Regression test:
  `AgentLoopSessionExtensionsTests.SaveTurnAsync_UnknownToolCallOutcome_IsNotReportedAsError`.

## 0.19.7

### Fixed
- `GitHubUpdateServiceTests.UpdateAsync_ReportsProgress` asserted on `IProgress<T>` reports
  immediately after `await`, with no wait for delivery. Without a captured `SynchronizationContext`
  (the case for this test host), `Progress<T>.Report()` dispatches through the ThreadPool and does
  not block the caller, so the callback could still be pending when the assertion ran — observed as
  an intermittent `Collection was empty` CI failure after the xUnit v3/MTP migration. The test now
  gates on the callback actually firing before asserting. Test-only; `GitHubUpdateService` itself
  is unchanged.

### Changed
- Fixed a `dotnet format` whitespace violation in `McpServerE2ETests.cs` (over-indented
  `Dictionary` initializer) flagged by CI's `format` job.

## 0.19.6

### Changed
- Updated `ModelContextProtocol` to 2.2.0 (previously 1.3.0). No public API changes — this
  package has no MCP wiring of its own (only test code exercises basic tool listing/calling via
  `IronHive.Agent`'s MCP plugin manager), so it is unaffected by the capabilities the 2.0
  protocol revision deprecated (roots, sampling, logging).
- Aligned the `Microsoft.Extensions.*` 10.0.9 family to 10.0.11 and the
  `Microsoft.Extensions.AI`/`.AI.Abstractions`/`.AI.OpenAI` trio to 10.9.0 — transitive floors
  raised by the `ModelContextProtocol` update.

## 0.19.5

### Changed
- Bumped `Ironbees.Core` 0.12.1 → 0.13.1, `IronHive.Agent`/`.DeepResearch` 0.7.1 → 0.7.2,
  `IronHive.Abstractions`/`.Core`/`.Providers.*` 0.19.1 → 0.20.0 (dependency freshness, no known
  breaking changes consumed — 860/860 tests unchanged).

## 0.19.4

### Documentation
- CHARTER.md/README.md never stated that `IronHive.Host` is built on top of the separate
  `IronHive.Agent` package (PackageReference; the agent loop, context/compaction, mode system,
  MCP plugins, and permission engine live there) — CHARTER.md's own responsibility line even
  described host as owning the runtime "그 자체", and README's Philosophy section/feature list
  implied host owns the agent loop directly. An external consumer evaluating `ironhive-host` vs
  `ironhive-agent` for an integration point had no documented way to tell they aren't competing
  implementations. Both docs now name the dependency explicitly; no code changed.

## 0.19.3

### Changed
- Bumped `Ironbees.Core` 0.10.0 → 0.12.1, `IronHive.*` 0.14.0 → 0.19.1, `IronHive.Agent`/
  `.DeepResearch` 0.5.0 → 0.7.1, `LMSupply.*` 0.37.1 → 0.42.2, `MemoryIndexer`/`.Sdk` 0.16.1 →
  0.16.5, `TokenMeter` 0.6.2 → 0.7.0 (dependency freshness, no known breaking changes consumed —
  full test suite unchanged at 860/860, one flaky wall-clock benchmark test observed under
  concurrent-build load and confirmed passing in isolation).

## 0.19.2

### Changed — MCP plugin health checks now run on Cronex's hosted scheduler

`McpHealthCheckService` used to build its own `CronexScheduler` and drive it directly. It now wires
the same periodic health check through `Cronex.Net.Hosting`'s `ICronexHandler`/`CronexBackgroundService`,
so the scheduling and background-execution logic is exercised through the shared library surface
instead of being reimplemented locally. `Start()` is now `StartAsync(CancellationToken)` — the one
caller in `RunCommand` has been updated; no other observable behavior changes (same expression,
same event, same disposal semantics).

## 0.19.1

### Fixed — resumed sessions lost their tool-call history

`SessionManager.RestoreContextAsync` silently dropped tool-use and tool-result transcript entries
when rebuilding a session's context, so a resumed session lost every tool call it had ever made
with no trace it happened. Both entry kinds are now mapped onto the same
`FunctionCallContent`/`FunctionResultContent` types the rest of the `Microsoft.Extensions.AI`
surface already uses, so tool history survives a restore like the rest of the transcript does.

## 0.19.0

### Changed — the browser runtime leaves the dependency graph

`IronHive.Agent` and `IronHive.DeepResearch` move to 0.5.0, which take WebFlux 0.7.0. WebFlux no
longer carries `Microsoft.Playwright`; dynamic rendering now lives in a separate `WebFlux.Playwright`
package that only consumers who crawl JavaScript-rendered pages install.

Host is where the DeepResearch reference lives, so without this hop the payload reached every Host
consumer regardless of what the layers below it did. A clean rebuild with the previous pins produced
a `.playwright` directory, a Playwright assembly and a bundled node runtime; with these it produces
none of them. In a self-contained single-RID publish the same reference had been observed pulling in
platform-specific node runtimes for platforms that were not the publish target.

Host does not use dynamic rendering, so `WebFlux.Playwright` is deliberately not referenced. A
consumer that needs it adds the package directly; the namespace and registration call are unchanged.

The `IronHive.Abstractions`/`IronHive.Core` pins are unchanged at 0.14.0. That drift predates this
release and spans a breaking version, so advancing it is a separate decision.

## 0.18.0

Dependency realignment: host had been pinned to `IronHive.* 0.8.2` while the core
moved to 0.14.0 (six minors), with the rest of the iyulab set drifting alongside it. This lifts the
whole set and drops the NU1903 suppression that the stale pins had made necessary.

### Changed (breaking, 0.x)
- **OpenAI-compatible providers moved to `IronHive.Providers.OpenAI.Compatible`.** IronHive 0.14.0
  removed `OpenAIConfig.Api`/`OpenAIApiSurface`, making the provider-isolation split explicit:
  GPUStack now uses `GpuStackConfig` + `GpuStackMessageGenerator`, and xAI / LM Studio use
  `OpenAICompatibleConfig` + `OpenAICompatibleMessageGenerator`. Model listing still goes through
  `OpenAIModelFinder`, built from each config's `ToOpenAI()` view — the wiring upstream's own
  `AddGpuStackProviders` uses. Behavior is preserved; only the types behind the registration changed.
- **`ITextCompletionService` for memory services is now `MemoryIndexer.Interfaces.ITextCompletionService`.**
  MemoryIndexer 0.16.0 dropped its `Flux.Abstractions` edge and owns the contract itself, so host no
  longer pulls flux transitively.

### Fixed
- **NU1903 suppression removed.** The `NoWarn` entry hid transitive high-severity advisories rather
  than being inert; with the pins current, no vulnerable transitive remains and a future one will fail
  the build instead of being silently absorbed. `NU1902` (OpenTelemetry/MessagePack, no fix published)
  and `NU5104` (MCP prerelease) keep their suppressions — those premises still hold.

### Dependencies
- `IronHive.*` 0.8.2 → **0.14.0** · `IronHive.Agent`/`.DeepResearch` 0.2.18 → **0.4.0** ·
  `Ironbees.Core` 0.6.4 → **0.10.0** · `LMSupply.*` 0.34.17 → **0.37.1** ·
  `MemoryIndexer(.Sdk)` 0.15.2 → **0.16.1** · `TokenMeter` 0.4.0 → **0.6.2** ·
  `ToolCallParser` 0.2.1 → **0.4.0** · `OpenAI` 2.11.0 → **2.12.0**.
- Added `IronHive.Providers.OpenAI.Compatible`.

## 0.17.0

Dedupe host's forked copies of three `IronHive.Agent` clusters (SubAgent, Tools, Ironbees) that had silently diverged since the 0.16.0 rename — host now consumes the canonical `IronHive.Agent` types directly.

### Fixed
- Ironbees multi-agent orchestration (`AddIronbeesOrchestration`) previously executed **zero tools** — host's forked `ChatClientFrameworkAdapter` had no tool-execution loop at all. It now uses `IronHive.Agent.Ironbees.ChatClientFrameworkAdapter`, which supports up to `MaxToolTurns` (default 20) tool-call iterations, permission checks, and dynamic tool/MCP provisioning via `IronbeesOptions.EnableToolExecution` + `WorkingDirectory`.
- The `"orchestrated"` keyed `IAgentLoop` (`AddIronbeesOrchestration`) previously never wired `IConversationStore` into `OrchestratedAgentLoop`, so `IronbeesOptions.ConversationsDirectory` was silently ignored and history/clear were permanent no-ops. Conversation persistence now works end-to-end on this path. `IronbeesOptions.DefaultAgentName` is now honored on this path as well (previously ignored).

### Removed (BREAKING)
- Removed the unused sub-agent-spawning feature: `IronHive.Host.Agent.SubAgent.*` (`ISubAgentService`, `SubAgentContext`, `SubAgentResult`, `SubAgentService`, `SubAgentType`), `IronHive.Host.Tools.SubAgentTool`, and config types `SubAgentConfig`/`ExploreAgentConfig`/`GeneralAgentConfig`. None of this was reachable from any host entry point (CLI/server/embed) — the production tool-list builder (`AgentLoopFactory`) never used the `ISubAgentService`-accepting `BuiltInTools.GetAll` overloads.
- Removed the `subAgent` top-level `config.yaml` key (`IronHiveConfig.SubAgent`) as a consequence — it configured the now-removed feature. Existing `subAgent.*` entries in `config.yaml` are ignored with a logged warning (unknown-key handling introduced in 0.15.0), not an error.
- Removed duplicate `IronHive.Host.Tools.TodoTool` (byte-identical to `IronHive.Agent.Tools.TodoTool`) in favor of the canonical type.
- Removed duplicate `IronHive.Host.Ironbees.*` (`ChatClientFrameworkAdapter`, `OrchestratedAgentLoop`, `IronbeesServiceCollectionExtensions`) in favor of `IronHive.Agent.Ironbees.*`.

## 0.16.0

- BREAKING: SDK package renamed `IronHive.Host.Core` -> `IronHive.Host`; namespaces `IronHive.Host.Core.*` -> `IronHive.Host.*`.
- BREAKING: CLI tool package renamed `IronHive.Host` -> `IronHive.Cli` (tool command `ironhive` unchanged). Old `IronHive.Host` tool package is deprecated; install via `dotnet tool install -g IronHive.Cli`.
- Releases now self-hosted on `iyulab/ironhive-host`; `ironhive-cli-releases` archived (existing v0.11-0.15 download URLs preserved).

## [0.15.0]

### Changed (BREAKING)
- Host configuration now loads from `~/.ironhive/config.yaml` and `./.ironhive/config.yaml` (YAML, 4-scope merge: global < project < .env < environment), matching the long-documented behavior. Previously the runtime only read `~/.ironhive/settings.json` (JSON) and silently ignored config.yaml.
- A legacy `~/.ironhive/settings.json` is automatically migrated to `config.yaml` on first run.
- `ironhive get`/`set`/`unset` now read and write `config.yaml` (previously settings.json).
- Acronym config sections use lowercase YAML keys (`openai`, `googleai`, `azureopenai`, `lmsupply`, `lmstudio`); unknown top-level keys now log a warning instead of being silently dropped.

### Removed (BREAKING)
- Removed unused public types from `IronHive.Host.Core`: `MergedConfig`, `ContextConfig`, `SessionConfig`, `ClaudeMdConfig`, and `SettingsManager`. These had no runtime consumers; external consumers referencing them must migrate to `ConfigurationManager` / `IronHiveConfig`.
- Removed the config-layer CLAUDE.md aggregation (`LoadClaudeMd`/`GetMergedClaudeMdContent`) — it was never wired into the live path.
