# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to 0.x pre-1.0 versioning (breaking changes are expected).

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
