# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to 0.x pre-1.0 versioning (breaking changes are expected).

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
  re-consumption of already-consumed sibling packages (`check-pin-drift.ps1 -Strict` flagged
  this as threshold-exceeding drift, minor gap 12; cold-GPU-kernel-hang protection propagated
  to all ONNX-backed lm-supply modules). No source changes.

## 0.20.1

### Changed
- Re-pinned `IronHive.Agent`/`.DeepResearch` from `0.9.5` to `0.9.6` — patch re-consumption of
  an already-consumed sibling package (`check-pin-drift.ps1` flagged this as drift, within
  threshold, right after `ironhive-agent` 0.9.6 was published). No source changes.

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

Dependency realignment (umbrella DF-1): host had been pinned to `IronHive.* 0.8.2` while the core
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

D14: dedupe host's forked copies of three `IronHive.Agent` clusters (SubAgent, Tools, Ironbees) that had silently diverged since the 0.16.0 rename — host now consumes the canonical `IronHive.Agent` types directly.

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
