# ironhive-cli

> Universal CLI Agent Core

A foundation tool for AI-powered automation—not just coding, but any task that benefits from intelligent command execution.

## Philosophy

**Do one thing well.**

Receive a command. Plan. Execute. Return.

```
┌─────────────────────────────────────────┐
│          External Systems               │
│   CI/CD · Schedulers · Orchestrators    │
└────────────────────┬────────────────────┘
                     │ invoke
                     ▼
┌─────────────────────────────────────────┐
│             ironhive-cli                │
│   Command → Plan → Execute → Done       │
└────────────────────┬────────────────────┘
                     │ MCP
                     ▼
┌─────────────────────────────────────────┐
│           Plugins (MCP Servers)         │
│   code-beaker · memory-indexer · ...    │
└─────────────────────────────────────────┘
```

## Installation

```bash
# Install as dotnet tool
dotnet tool install -g IronHive.Cli

# Or build from source
git clone https://github.com/iyulab/ironhive-cli
cd ironhive-cli
git submodule update --init --recursive
dotnet build
```

## Quick Start

```bash
# Interactive mode
ironhive

# Single command
ironhive -p "Write a README for this project"

# JSON output (for programmatic use)
ironhive -p "Hello" --output json

# Streaming JSON Lines
ironhive -p "Hello" --output jsonl

# Plain text (no ANSI)
ironhive -p "Hello" --plain
```

### Session Management

```bash
# Continue most recent session
ironhive -c

# Resume specific session
ironhive -r <session-id>

# List sessions
ironhive sessions list
ironhive sessions list --output json
```

### Model Configuration

```bash
# Environment variables
export GPUSTACK_ENDPOINT=http://localhost:8080/v1
export GPUSTACK_API_KEY=your-key
export GPUSTACK_MODEL=gpt-4o-mini

# Or OpenAI
export OPENAI_API_KEY=sk-xxx
```

## Configuration

Configuration is merged in order (later overrides earlier):

1. **Global**: `~/.ironhive/config.yaml`
2. **Project**: `.ironhive/config.yaml`
3. **Environment**: `IRONHIVE_*`, `GPUSTACK_*`, `OPENAI_*`
4. **.env file**: Project root `.env`

```yaml
# .ironhive/config.yaml
limits:
  maxSessionTokens: 100000
  maxSessionCost: 10.00

context:
  compactionThreshold: 0.92
  goalReminderEnabled: true
```

### CLAUDE.md Support

Place a `CLAUDE.md` file in your project root for automatic agent instructions.

## Core Library Integration

Use `IronHive.Cli.Core` for direct .NET integration:

```csharp
// Add to your project
<PackageReference Include="IronHive.Cli.Core" />

// Configure with DI
services.AddIronHiveWithOpenAI(apiKey, "gpt-4o-mini");
// Or
services.AddIronHiveWithOllama("llama3.2");

// Use
var agentLoop = serviceProvider.GetRequiredService<IAgentLoop>();
var response = await agentLoop.RunAsync("Hello");

// Streaming
await foreach (var chunk in agentLoop.RunStreamingAsync("Hello"))
{
    Console.Write(chunk.TextDelta);
}
```

See `samples/console-chat` for a complete example.

## Samples

| Sample | Path | Description |
|--------|------|-------------|
| Console Chat | `samples/console-chat/` | Core library direct integration |
| Web Chat | `samples/web-ai-chat/` | Next.js + CLI subprocess |

```bash
# Console Chat
cd samples/console-chat
dotnet run

# Web Chat
cd samples/web-ai-chat
npm install && npm run dev
```

## Development

### Requirements

- .NET 10 SDK
- Git (with submodules)

### Build & Test

```bash
git submodule update --init --recursive
dotnet build
dotnet test
```

### Project Structure

```
ironhive-cli/
├── src/
│   ├── IronHive.Cli.Core/       # Core library (NuGet)
│   │   ├── Agent/               # AgentLoop, modes
│   │   ├── Extensions/          # DI helpers
│   │   ├── Session/             # Session management
│   │   └── Tools/               # Built-in tools
│   └── IronHive.Cli/            # CLI application
├── samples/
│   ├── console-chat/            # .NET Core integration
│   └── web-ai-chat/             # Next.js + subprocess
├── tests/
└── submodules/
    ├── TokenMeter/              # Token counting
    └── ToolCallParser/          # tool_call parsing
```

## Related Projects

- [ironhive](https://github.com/iyulab/ironhive) — LLM abstraction
- [ironbees](https://github.com/iyulab/ironbees) — Multi-agent management
- [memory-indexer](https://github.com/iyulab/memory-indexer) — Semantic memory MCP
- [code-beaker](https://github.com/iyulab/code-beaker) — Code execution platform

## License

MIT
