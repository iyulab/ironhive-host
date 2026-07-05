# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to 0.x pre-1.0 versioning (breaking changes are expected).

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
