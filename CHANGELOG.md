# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to 0.x pre-1.0 versioning (breaking changes are expected).

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
