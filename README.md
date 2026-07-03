# ironhive-host

> Universal Agent Host — CLI · server · embedded
>
> **역할·범위·기능 명세(미들웨어 정체성)는 [`CHARTER.md`](./CHARTER.md) 참조** — product-middleware §⑤ 에이전트 호스트.
>
> **⚠️ 0.13.0 breaking rename**: 내부 패키지가 `IronHive.Cli*` → `IronHive.Host*`로 변경되었습니다.
> NuGet 라이브러리 `IronHive.Cli.Core` → **`IronHive.Host.Core`**, dotnet tool `IronHive.Cli` → **`IronHive.Host`**
> (실행 명령은 `ironhive` 그대로). turn-stream 프로토콜 계약은 새 thin 패키지 **`IronHive.Host.Protocol`**(무의존)로 분리.
> 소비자는 PackageReference + `using IronHive.Cli.* → IronHive.Host.*` 교체로 마이그레이션. 0.12.x `IronHive.Cli*`는 배포 중단(unlist 아님 — 기존 복원 계속 동작).

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
│             ironhive-host                │
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
# Install as dotnet tool (package id IronHive.Host; the command stays `ironhive`)
dotnet tool install -g IronHive.Host

# Or build from source
git clone https://github.com/iyulab/ironhive-host
cd ironhive-host
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

# Context compaction is active by default — long sessions compact history
# instead of overflowing. Tune via the compaction section:
compaction:
  useTokenBasedCompaction: true
  protectRecentTokens: 40000     # most-recent tokens always kept
  minimumPruneTokens: 20000      # only compact when at least this much is prunable
  targetRatio: 0.70              # compact down to ~70% of the context window
```

### CLAUDE.md Support

Place a `CLAUDE.md` file in your project root for automatic agent instructions.

## Core Library Integration

Use `IronHive.Host.Core` for direct .NET integration:

```csharp
// Add to your project
<PackageReference Include="IronHive.Host.Core" />

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

### ChatBehaviorConfig

Controls how `FunctionInvokingChatClient` orchestrates the tool-call iteration loop. Exposed in `IronHiveConfig.ChatBehavior` so you can tune per-model without forking the source.

| Property | Default | Notes |
|----------|---------|-------|
| `MaximumIterationsPerRequest` | 10 | Lower (5–7) for small 4K-window models; raise (15–20) for large-context models |
| `MaximumConsecutiveErrorsPerRequest` | 3 | Backstop on back-to-back marshaller errors; rarely hit when `ResilientFunctionInvoker` is installed |

```yaml
# .ironhive/config.yaml
chatBehavior:
  maximumIterationsPerRequest: 7      # tune down for small/quantized models
  maximumConsecutiveErrorsPerRequest: 3
```

### Context Compaction

Long sessions are kept within the model's context window automatically. When the agent loop is built
(via the CLI, the server runners, or `IronHive.Host.Core` DI), a `ContextManager` is wired from the
`compaction` config so older history is compacted (token-based, protecting the most recent turns and
important tool outputs) instead of silently overflowing.

- Enabled by default; tune via the `compaction:` config section (see Configuration above)
- Model-aware — the context window is sized from the active model
- Embedded consumers can set `options.Compaction` on `AddIronHive(...)`; manual loop builders can wire it
  via `HostContextManagerFactory.Create(compactionConfig, modelName)` and pass the result to `AgentLoop`/`ThinkingAgentLoop`
- Complements (does not replace) `TokenBudgetChatClient`, which remains the hard backstop against per-request overflow

### TokenBudgetChatClient

`IChatClient` decorator that short-circuits streaming calls when the accumulated message-history size would exceed a configurable fraction of the model's context window. Prevents context-overflow silent failures on small quantized models (e.g. 4K-window Gemma E4B).

- Sits between `FunctionInvokingChatClient` and the underlying provider
- Estimates tokens as `total-chars ÷ 4` (conservative upper bound)
- When the estimate exceeds `maxContextTokens × threshold`, emits a graceful `ChatFinishReason.Length` response instead of letting the model error silently
- Context window auto-detected via `IContextSizeProvider` if the inner client exposes it; otherwise falls back to `defaultMaxContextTokens`

```csharp
var client = new TokenBudgetChatClient(
    inner: innerClient,
    defaultMaxContextTokens: 4096,
    threshold: 0.8);   // trigger at 80 % of context window
```

### ResilientFunctionInvoker

Factory for the M.E.AI `FunctionInvoker` delegate that converts marshaller-level `ArgumentException` (missing/malformed tool arguments) into model-actionable procedural error strings, enabling small quantized models to self-correct without aborting the stream.

Install via `UseFunctionInvocation`:

```csharp
chatClient.UseFunctionInvocation(configure: c =>
{
    c.FunctionInvoker = ResilientFunctionInvoker.Create();
});
```

When the model sends a tool call with a missing required parameter, instead of throwing and aborting, the invoker returns a numbered recovery directive telling the model exactly what is missing, where to find the value, and explicitly forbidding the empty-args retry pattern.

### AgentServerRunner / AgentHttpRunner

Two runner implementations share the same processor delegate signature, enabling a single agent pipeline to serve either transport:

```csharp
async IAsyncEnumerable<ServerEvent> ProcessMessage(
    UserMessageRequest msg,
    CancellationToken token)
{
    // msg.Model carries the per-message model override (nullable)
    await foreach (var evt in agentLoop.RunStreamingAsync(msg.Content, token)
        .ToServerEvents(executionLog, token))
    {
        yield return evt;
    }
}

// stdin/stdout JSON Lines (used by `ironhive run --server`)
var runner = new AgentServerRunner(ProcessMessage, logger);
await runner.RunAsync(ct);

// HTTP/SSE (host spawns the agent and communicates via REST)
var httpRunner = new AgentHttpRunner("http://localhost:5100", sessionId, ProcessMessage, logger);
await httpRunner.RunAsync(ct);
```

**AgentHttpRunner** expects these host endpoints:

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/agent/{id}/ready` | POST | Signal agent is up |
| `/api/agent/{id}/inbox` | GET (SSE) | Receive `ServerRequest` commands |
| `/api/agent/{id}/events` | POST | Deliver `ServerEvent` batches |

Both runners support `typeInfoModifiers` to extend polymorphic type registrations without mutating the base `JsonSerializerOptions`:

```csharp
var runner = new AgentServerRunner(ProcessMessage, logger,
    typeInfoModifiers: [ApplyCustomPolymorphismOverrides]);
```

`AgentHttpRunner` additionally exposes `WaitForHitlResponseAsync` / `ResolveHitl` for human-in-the-loop flows, and `PublishEvent` for out-of-band event delivery (e.g. provider fallback notices).

**Protocol types** (`ServerRequest` → agent, `ServerEvent` → host):

| Type | Discriminator | Key fields |
|------|---------------|-----------|
| `UserMessageRequest` | `user_message` | `Content`, `Model?` |
| `ContextUpdateRequest` | `context_update` | `WorkingPath?`, `SelectedItems?` |
| `HitlResponseRequest` | `hitl_response` | `Approved`, `Reason?` |
| `CancelRequest` | `cancel` | — |
| `ShutdownRequest` | `shutdown` | — |
| `ToolStartEvent` | `tool_start` | `Tool`, `Input?`, `CallId?` |
| `ToolEndEvent` | `tool_end` | `Tool`, `Success`, `Output?` (≤ 8 KB), `CallId?` |
| `FallbackServerEvent` | `fallback` | `Kind` (`retry`\|`fallback`\|`exhausted`), `Category`, `Message`, `ProviderIndex`, `TotalProviders`, `Attempt`, `MaxAttempts` |
| `TextDeltaEvent` | `text_delta` | `Content` |
| `TurnEndEvent` | `turn_end` | — |
| `ErrorEvent` | `error` | `Message` |

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
ironhive-host/
├── src/
│   ├── IronHive.Host.Protocol/  # Thin turn-stream contracts (zero-dep NuGet)
│   ├── IronHive.Host.Core/      # Core library (NuGet)
│   │   ├── Config/              # Configuration classes
│   │   ├── Extensions/          # DI helpers
│   │   ├── Providers/           # LMSupply, IronHive chat client providers
│   │   ├── Server/              # AgentServerRunner, AgentHttpRunner (references Host.Protocol)
│   │   ├── Session/             # Session management
│   │   └── Tools/               # Built-in tools, ResilientFunctionInvoker, TokenBudgetChatClient
│   └── IronHive.Host/           # CLI application (tool command: ironhive)
├── samples/
│   ├── console-chat/            # .NET Core integration
│   └── web-ai-chat/             # Next.js + subprocess
└── tests/
```

## Related Projects

- [ironhive](https://github.com/iyulab/ironhive) — LLM abstraction
- [ironbees](https://github.com/iyulab/ironbees) — Multi-agent management
- [memory-indexer](https://github.com/iyulab/memory-indexer) — Semantic memory MCP
- [code-beaker](https://github.com/iyulab/code-beaker) — Code execution platform

## License

MIT
