# ironhive-cli

> Universal CLI Agent Core

A foundation tool for AI-powered automation—not just coding, but any task that benefits from intelligent command execution.

## Philosophy

**Do one thing well.**

Receive a command. Plan. Execute. Return.

This CLI doesn't manage workflows, run schedulers, or orchestrate complex pipelines. That responsibility belongs to the systems that invoke it—CI/CD tools, workflow engines, or your own automation layer. By staying focused, ironhive-cli remains composable, predictable, and easy to integrate.

```
┌─────────────────────────────────────────┐
│          External Systems               │
│   CI/CD · Schedulers · Orchestrators    │
└────────────────────┬────────────────────┘
                     │ invoke + receive webhooks
                     ▼
┌─────────────────────────────────────────┐
│             ironhive-cli                │
│                                         │
│   Command → Mode → Plan → Execute → Done│
│                                         │
└────────────────────┬────────────────────┘
                     │ MCP
                     ▼
┌─────────────────────────────────────────┐
│           Plugins (MCP Servers)         │
│   code-beaker · memory-indexer · ...    │
└─────────────────────────────────────────┘
```

## Features

- **Local & Cloud Models** — GpuStack, OpenAI-compatible APIs
- **MCP Plugins** — Extend capabilities via Model Context Protocol
- **Plan/Work/HITL Modes** — Plan → Execute → Human approval when needed
- **Session Management** — Resume, fork, restore context
- **Context Management** — Auto-compaction, goal reminders
- **Webhooks** — Integrate with external systems
- **Usage Limits** — Token and cost caps per session

## Installation

```bash
# Install as dotnet tool
dotnet tool install -g IronHive.Cli --version 0.2.0-alpha

# Or build from source
git clone https://github.com/iyulab/ironhive-cli
cd ironhive-cli
git submodule update --init --recursive
dotnet build
```

## Quick Start

### Basic Usage

```bash
# Interactive mode
ironhive

# Single command
ironhive -p "Write a README for this project"
ironhive run "Show me the file structure"
```

### Modes

```bash
# Plan mode (read-only exploration)
ironhive --plan "Plan a refactoring strategy"

# Dry-run (plan only, no execution)
ironhive --dry-run "Clean up test files"
```

### Session Management

```bash
# Continue most recent session
ironhive -c
ironhive --continue

# Resume specific session
ironhive -r <session-id>
ironhive --resume <session-id>

# Fork a session (branch off)
ironhive -r <session-id> --fork

# List sessions
ironhive sessions
ironhive sessions --project <path>
```

### Model Configuration

```bash
# Use gpustack model
ironhive --model gpustack/qwen2.5-coder

# Environment variables
export GPUSTACK_ENDPOINT=http://localhost:8080
export GPUSTACK_API_KEY=your-key
export GPUSTACK_MODEL=gpt-4o-mini
```

### Webhooks

```bash
# CLI option
ironhive --webhook http://localhost:8080/events

# Configuration file (.ironhive/config.yaml)
webhook:
  endpoints:
    - url: https://example.com/webhook
      secret: your-secret
      eventFilter: [SessionStarted, ToolCompleted]
```

## Configuration

Configuration is merged in order (later overrides earlier):

1. **Global**: `~/.ironhive/config.yaml`
2. **Project**: `.ironhive/config.yaml`
3. **Environment**: `IRONHIVE_*`, `GPUSTACK_*`
4. **.env file**: Project root `.env`

```yaml
# .ironhive/config.yaml
limits:
  maxSessionTokens: 100000
  maxSessionCost: 10.00
  warningThreshold: 0.8
  stopOnLimit: true

context:
  compactionThreshold: 0.92
  goalReminderEnabled: true

session:
  autoSave: true
  maxSessions: 100
```

### CLAUDE.md Support

Place a `CLAUDE.md` file in your project root for automatic agent instructions:

```markdown
# CLAUDE.md

This is a Python Flask web application.

## Coding Style
- Follow PEP 8
- Use type hints
- Write docstrings

## Rules
- Use logging instead of print()
```

## Architecture

### Core Components

| Component | Purpose |
|-----------|---------|
| **AgentLoop** | Single-threaded master loop with context management |
| **ModeManager** | Plan/Work/HITL mode transitions |
| **SessionManager** | JSONL transcript persistence |
| **ContextManager** | Token counting, compaction, goal reminders |
| **McpPluginManager** | MCP server connections |

### Built-in Tools

| Tool | Description |
|------|-------------|
| Read | Read files |
| Write | Write files (with diff preview) |
| Shell | Execute commands |
| Glob | Pattern-based file search |
| Grep | Content search |
| Todo | Task list management |

## Development

### Requirements

- .NET 10 SDK
- Git (with submodules)

### Build

```bash
# Initialize submodules
git submodule update --init --recursive

# Build
dotnet build

# Test
dotnet test

# Format check
dotnet format --verify-no-changes

# Create NuGet package
dotnet pack src/IronHive.Cli -c Release
```

### Testing

```bash
# All tests
dotnet test

# By category
dotnet test --filter "Category=Unit"
dotnet test --filter "Category=Integration"
dotnet test --filter "Category=E2E"

# MCP E2E tests (requires Node.js + environment variable)
IRONHIVE_MCP_E2E_ENABLED=true dotnet test --filter "Category=MCP"

# LLM integration tests (requires API key)
GPUSTACK_API_KEY=your-key dotnet test --filter "Category=Integration"
```

### Project Structure

```
ironhive-cli/
├── src/
│   ├── IronHive.Cli.Core/       # Core agent logic
│   │   ├── Agent/               # AgentLoop, modes, MCP
│   │   ├── Config/              # Configuration management
│   │   ├── Context/             # Context management
│   │   ├── Providers/           # LLM providers
│   │   ├── Session/             # Session management
│   │   ├── Tools/               # Built-in tools
│   │   └── Webhook/             # Webhook system
│   └── IronHive.Cli/            # CLI application
├── tests/
│   └── IronHive.Cli.Tests/      # Unit/Integration/E2E tests
├── submodules/
│   ├── TokenMeter/              # Token counting & cost
│   └── ToolCallParser/          # tool_call parsing
└── docs/
    ├── ROADMAP.md               # Development roadmap
    └── research/                # Research documents
```

## Related Projects

- [ironhive](https://github.com/iyulab/ironhive) — LLM abstraction
- [ironbees](https://github.com/iyulab/ironbees) — Multi-agent management
- [memory-indexer](https://github.com/iyulab/memory-indexer) — Semantic memory MCP
- [code-beaker](https://github.com/iyulab/code-beaker) — Code execution platform

## License

MIT
