# ironhive-host

[![NuGet: IronHive.Host](https://img.shields.io/nuget/v/IronHive.Host.svg?label=IronHive.Host)](https://www.nuget.org/packages/IronHive.Host)
[![NuGet: IronHive.Cli](https://img.shields.io/nuget/v/IronHive.Cli.svg?label=IronHive.Cli)](https://www.nuget.org/packages/IronHive.Cli)
[![NuGet: IronHive.Host.Protocol](https://img.shields.io/nuget/v/IronHive.Host.Protocol.svg?label=IronHive.Host.Protocol)](https://www.nuget.org/packages/IronHive.Host.Protocol)
[![CI](https://github.com/iyulab/ironhive-host/actions/workflows/ci.yml/badge.svg)](https://github.com/iyulab/ironhive-host/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](./LICENSE)

> Universal Agent Host — CLI · server · embedded

A foundation tool for AI-powered automation—not just coding, but any task that benefits from intelligent command execution. One agent core (plan → execute → tool-call), exposed as an installable CLI, an embeddable .NET SDK, or a long-running server (stdio/JSON-Lines or HTTP/SSE) — pick the surface that fits, without re-implementing the agent loop.

The agent loop, context/compaction, mode system, MCP plugins, and permission engine itself live in
[`IronHive.Agent`](https://www.nuget.org/packages/IronHive.Agent) (from [`ironhive-agent`](https://github.com/iyulab/ironhive-agent)),
which this package consumes as a `PackageReference`. `IronHive.Host` adds the hosting surface on top:
CLI/server/embed entry points, the `IronHive.Host.Protocol` turn-stream contract, layered config,
provider adapters, and session/execution-log/memory integration. The agent loop runs on
any injected `IChatClient` (`UseChatClient` / `UseChatClientFactory`), and the server surfaces
expose a generic turn-stream rather than an application-specific wire format.

## Features

- **Three surfaces, one core** — CLI (`ironhive`), embeddable SDK (`IronHive.Host`), and server runners (stdio or HTTP/SSE) all drive the same `IronHive.Agent` loop.
- **Approvals on every surface** — one tool-call policy (`IToolCallPolicy`) judges each call; an `Ask` is answered on the terminal, or — in `run --server` and by any host that passes a `HitlBridge` to its runner — by the client over the wire (`hitl_request` → `hitl_response`). See [Human approval over the wire](#human-approval-over-the-wire).
- **MCP-native tooling** — plugs into MCP servers (memory, code execution, custom tools) instead of hardcoding a tool set.
- **Multi-provider out of the box** — OpenAI, Anthropic, GoogleAI, Azure OpenAI, xAI, Ollama, LM Studio, GPUStack, and local inference via `LMSUPPLY_ENABLED`.
- **Context-window safe by default** — automatic history compaction (`ContextManager`, wired on every surface including `AddIronHive`) prevents silent context overflows, including on small quantized models. The CLI and `run --server` additionally install a hard-backstop `TokenBudgetChatClient`; library embedders wrap their own `IChatClient` with it (see [TokenBudgetChatClient](#tokenbudgetchatclient)).
- **Resilient tool-calling** — `ResilientArgumentsMiddleware` (a tool invocation pipeline step) and `ResilientFunctionInvoker` (the same behaviour as a plain `FunctionInvoker`) turn tool-call arguments that do not fit the tool into model-actionable recovery hints instead of aborting the stream. Installed by the CLI and `run --server` (after the loop guards and the permission gate); `AddIronHive`/`AddIronHiveWithOpenAI` do not decorate the client, so embedders install it themselves (see [ResilientFunctionInvoker](#resilientfunctioninvoker)).
- **Advisor** — set `advisor.model` and every CLI / `run --server` session gets an `advisor` tool (the library `AddIronHive` path has no advisor option): the working model can send the conversation so far to a stronger model and read its review (before committing to an approach, when stuck, before declaring done). It appears on the wire as an ordinary `tool_start`/`tool_end`.
- **Layered configuration** — global → project → environment → `.env`, with automatic migration from legacy `settings.json`.

<details>
<summary><strong>⚠️ 0.16.0 breaking repackage</strong> (upgrading from an older version? read this)</summary>

SDK 라이브러리 `IronHive.Host.Core` → **`IronHive.Host`**(SDK가 top-level 이름을 소유,
네임스페이스 `IronHive.Host.Core.*` → `IronHive.Host.*`). CLI tool 패키지 `IronHive.Host` → **`IronHive.Cli`**
(실행 명령은 `ironhive` 그대로). turn-stream 프로토콜 계약은 별도 thin 패키지 **`IronHive.Host.Protocol`**(무의존)로 분리 유지.
옛 `IronHive.Host` tool 패키지는 배포 중단(unlist 아님 — 기존 복원 계속 동작); 신규 설치는 `dotnet tool install -g IronHive.Cli`.
릴리스는 이제 `iyulab/ironhive-host`에서 self-host — 옛 `ironhive-cli-releases`는 archive(read-only, 기존 v0.11-0.15 다운로드 URL 유지).

</details>

## Contents

- [Philosophy](#philosophy)
- [As an SDK (IronHive.Host)](#as-an-sdk-ironhivehost)
- [As a CLI (IronHive.Cli)](#as-a-cli-ironhivecli)
- [Quick Start](#quick-start)
- [Configuration](#configuration)
- [Core Library Integration](#core-library-integration)
- [Samples](#samples)
- [Development](#development)
- [Contributing](#contributing)
- [Related Projects](#related-projects)
- [License](#license)

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

## As an SDK (IronHive.Host)

```bash
dotnet add package IronHive.Host
```
Build reusable agent hosts (agent loop, tools, session, providers). The CLI below is one consumption surface of this SDK.

## As a CLI (IronHive.Cli)

```bash
dotnet tool install -g IronHive.Cli
ironhive
```

Or build from source:

```bash
git clone https://github.com/iyulab/ironhive-host
cd ironhive-host
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

### Without an API key

`ironhive` does not need a cloud provider to be useful. Local inference is **on by default**
(`lmsupply.enabled: true`; `LMSUPPLY_ENABLED=false` turns it off): with no other provider
configured, the first run downloads a GGUF model (cached under the LMSupply model cache) and runs
it in-process through [LMSupply](https://github.com/iyulab/lm-supply) — no key, no server:

```bash
ironhive -p "Summarize this directory"   # works with an empty config
ironhive doctor                          # shows which providers are configured and reachable
```

`doctor` reports `Using local inference only (lmsupply)` on such a setup and suggests a remote
provider only as a performance option — that is the expected state, not a warning to fix. The first
run is slower (model download + load); later runs start from the cache.

### JSON Output Schema

`--output json`/`jsonl` is meant for programmatic consumption — piping into `jq`, another process,
or a script. Fields that would otherwise serialize as `null` (`sessionId`, `usage`, `thinking`,
`toolCalls`) are **omitted from the object entirely** rather than written as `null`.

**`--output json`** (single response, `-p`/`--prompt`):

```jsonc
{
  "content": "string",                 // always present — the assistant's text reply
  "sessionId": "string",               // omitted if no session is active
  "usage": {                           // omitted if usage wasn't reported
    "inputTokens": 0,
    "outputTokens": 0,
    "totalTokens": 0
  },
  "thinking": {                        // present only with --show-thinking AND the model returned thinking content
    "content": "string",
    "tokenCount": 0
  },
  "toolCalls": [                       // omitted if no tools were called
    {
      "name": "string",
      "arguments": "string",           // JSON-encoded arguments, as a string
      "result": "string",
      "success": true                  // true | false | null — null means the outcome is unknown
                                        // (unset unless the underlying IChatClient has
                                        // Microsoft.Extensions.AI function-invocation middleware)
    }
  ]
}
```

On cancellation (Ctrl+C) or an unhandled error, the object is replaced with an error shape instead:
`{ "error": "cancelled", "code": 130 }` or `{ "error": "<message>", "code": 1 }`.

**`--output jsonl`** (streaming JSON Lines — one object per line, discriminated by `type`):

| `type` | Fields | When |
|---|---|---|
| `start` | `sessionId` | First line, always |
| `thinking` | `content` | Only with `--show-thinking`, once per thinking delta |
| `text` | `content` | Once per text delta |
| `tool_call` | `id`, `name`, `arguments` | Once per tool-call delta (`arguments` is JSON-encoded) |
| `tool_result` | `id`, `name`, `success`, `result` | When a tool finishes (`id` pairs it with its `tool_call`; `success` is `null` when the outcome is unknown). Only when the chat client invokes tools |
| `done` | `sessionId` | Last line on success |
| `error` | `error` | Instead of `done`, on cancellation or an unhandled exception |

**`sessions list --output json`** — an array (`[]` if empty), one object per session:

```jsonc
[
  {
    "id": "string",
    "status": "string",        // lowercased, e.g. "active", "completed"
    "model": "string",
    "created": "2026-08-31T00:00:00.0000000Z",  // ISO 8601 (round-trip "o" format)
    "messageCount": 0,
    "firstMessage": "string"
  }
]
```

**`sessions delete <id> --output json`**:

- Success: `{ "deleted": "<id>", "success": true }`
- Missing `<id>` or session not found: `{ "error": "string", "code": 1 }`

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

### CLI Reference

| Command | Purpose |
|---------|---------|
| `ironhive` | Interactive mode (or one prompt with `-p`) |
| `ironhive run [PROMPT]` | Run a single prompt and exit (`--server` for JSON Lines server mode) |
| `ironhive set <key> <value>` | Set a configuration value, e.g. `ironhive set openai.apiKey <key>` |
| `ironhive get [key]` | Get a configuration value (all values without a key) |
| `ironhive unset <key>` | Remove a configuration value |
| `ironhive config [show\|path]` | Show all configuration, or the config file path |
| `ironhive models` | List available models from configured providers (`-p/--provider`, `--json`) |
| `ironhive update` | Check for and install updates (`--check` only checks, `--force`) |
| `ironhive doctor` | Diagnose configuration and connectivity (`--verbose`, `--fix`) |
| `ironhive sessions [list\|delete <id>]` | List and manage sessions (`-n/--limit`, default 10; `-o/--output json`) |

Options of the default command (`ironhive` / `ironhive -p`):

| Flag | Purpose |
|------|---------|
| `-m, --model <MODEL>` | Model to use |
| `--provider <PROVIDER>` | Provider to use |
| `--show-tokens` | Show token usage statistics |
| `--show-thinking` | Show the model's thinking/reasoning content |
| `--no-stream` | Wait for the complete response instead of streaming |
| `--plan` | Start in planning mode (read-only exploration) |
| `--dry-run` | Show what would be done without executing |
| `-c, --continue` / `-r, --resume <ID>` | Continue the most recent session / resume a specific one |
| `--fork` | Fork the resumed session into a new session |
| `-o, --output <FORMAT>` / `--plain` | `text`, `json`, `jsonl` / no ANSI formatting |

`run` takes `-p/--prompt`, `-m/--model`, `--provider`, `--show-tokens`, `--show-thinking`, plus:

| Flag | Purpose |
|------|---------|
| `--json` | Output the response as JSON |
| `--server` | Server mode (JSON Lines on stdin/stdout — see [AgentServerRunner](#agentserverrunner--agenthttprunner)) |
| `--session-id <ID>` | Session ID for server mode |
| `--auto-commit` | Commit changes after a successful run |
| `--commit-message <MESSAGE>` | Commit message for `--auto-commit` (default: generated from the prompt) |

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
3. **Environment**: exactly these variables are read (there is no `IRONHIVE_*` variable):
   - `GPUSTACK_ENDPOINT`, `GPUSTACK_API_KEY`, `GPUSTACK_MODEL`, `GPUSTACK_EMBEDDING_MODEL`, `GPUSTACK_RERANK_MODEL`
   - `OPENAI_API_KEY`, `OPENAI_MODEL`, `OPENAI_ENDPOINT`
   - `ANTHROPIC_API_KEY`, `ANTHROPIC_MODEL`
   - `GOOGLEAI_API_KEY` (or `GOOGLE_API_KEY`), `GOOGLEAI_MODEL`
   - `XAI_API_KEY`, `XAI_MODEL`, `XAI_ENDPOINT`
   - `AZURE_OPENAI_ENDPOINT`, `AZURE_OPENAI_API_KEY`, `AZURE_OPENAI_DEPLOYMENT`
   - `OLLAMA_ENDPOINT`, `OLLAMA_MODEL`, `OLLAMA_ENABLED`
   - `LMSTUDIO_ENDPOINT`, `LMSTUDIO_MODEL`, `LMSTUDIO_ENABLED`
   - `LMSUPPLY_ENABLED`
4. **.env file**: Project root `.env`

> On first run, a legacy `~/.ironhive/settings.json` (from earlier versions) is automatically migrated to `config.yaml`.

### Config keys

The loader accepts these top-level keys in `config.yaml`. Acronym provider sections use **lowercase** keys; unknown top-level keys are ignored with a logged warning.

| Key | Notes |
|-----|-------|
| `gpuStack` | camelCase |
| `openai` | lowercase (acronym) |
| `anthropic` | |
| `googleai` | lowercase (acronym) |
| `azureopenai` | lowercase (acronym) |
| `xai` | |
| `ollama` | |
| `lmstudio` | lowercase (acronym) |
| `lmsupply` | lowercase (acronym) |
| `permissions` | |
| `compaction` | |
| `webSearch` | camelCase |
| `deepResearch` | camelCase |
| `chatBehavior` | camelCase — CLI / `run --server` loops only |
| `advisor` | `provider`, `model`, `maxCalls` (per session, default 5) — off until `model` is set; CLI / `run --server` loops only |
| `budget` | `maxSessionTokens`, `maxSessionCost` (USD, TokenMeter catalog prices), `warningThreshold` (default 0.8), `stopOnLimit` (default true) — per session (one CLI session, one `run --server` session), checked before every model call including each tool round; 0 = no limit (default); a call past it ends the turn with `UsageLimitExceededException` |
| `agentsMd` | `enabled` (default on), `maxCharacters` (default 32,000) — the [AGENTS.md](https://agents.md) files from the repository root (the directory with `.git`) down to the session's working directory join the system instructions, root first so the nearest wins; nothing above the repository is read; CLI / `run --server` loops only |
| `toolRetrieval` | `enabled` (default off), `maxTools` (default 10), `minRelevanceScore` (default 0.3), `minScoredSlots`, `alwaysInclude`, `stickyToolLimit` (0 = off; above 0 sends the tools a conversation already sent again unchanged until a request needs one they lack — keeps a prefix-cached server's prompt cache), `stickyChangeScore` (default 0.7 — the score a scored tool the held set lacks must reach to change it; pins, exact names and aliases always do) — when on, each request sends the model only the tools most relevant to the user's request (aliases and companion tools a tool declares count) instead of every built-in and MCP tool; CLI / `run --server` loops only |
| `delegation` | `agentsDirectory` (default `agents`, relative to the working directory), `agents` (list of `name` · `provider` · `model` · `description` · `maxToolTurns`; empty = off), `maxDepth` (default 2), `maxConcurrent` (default 3) — each listed Ironbees agent (`agents/<name>/agent.yaml` + `system-prompt.md`) becomes a tool the working model can hand a task to. A delegated run calls its tools through the session's own approval path (permission rules, planning mode, the approval prompt) and spends the session budget; delegation tools are left out in planning mode; CLI / `run --server` loops only |
| `skills` | `roots`, `enabled`, `exclude`, `maxMetadataCharacters` (default 12,000), `acceptUnknownFields` — off until `roots` names a directory (see **Skills**) |

```yaml
# ~/.ironhive/config.yaml
openai:
  apiKey: sk-...
  model: gpt-4o-mini
gpuStack:
  endpoint: http://localhost:8080
  apiKey: ...
  model: gpt-4o-mini
```

```yaml
# .ironhive/config.yaml

# Context compaction is active by default — long sessions compact history
# instead of overflowing. Tune via the compaction section:
compaction:
  useTokenBasedCompaction: true
  protectRecentTokens: 40000     # most-recent tokens always kept
  minimumPruneTokens: 20000      # only compact when at least this much is prunable
  targetRatio: 0.70              # compact down to ~70% of the context window
  maxContextTokens: 65536        # the model's window, when the catalog does not know the model
  compactOnOverflow: true        # window unknown: compact when the server reports an overflow, not against a guess

# A stronger model the working model can consult through the `advisor` tool:
advisor:
  provider: anthropic
  model: claude-opus-5
  maxCalls: 5

# A usage budget per session (tokens and/or USD):
budget:
  maxSessionTokens: 500000
  maxSessionCost: 2.00
```

## Core Library Integration

Use `IronHive.Host` for direct .NET integration:

```csharp
// Add to your project
<PackageReference Include="IronHive.Host" />

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

Controls how `FunctionInvokingChatClient` orchestrates the tool-call iteration loop. Exposed in `IronHiveConfig.ChatBehavior` so you can tune per-model without forking the source. It applies to the loops the CLI and `run --server` build; the `AddIronHive` DI path has no ChatBehavior option — embedders set these caps on their own `UseFunctionInvocation` client.

| Property | Default | Notes |
|----------|---------|-------|
| `MaximumIterationsPerRequest` | 10 | Lower (5–7) for small 4K-window models; raise (15–20) for large-context models |
| `MaximumConsecutiveErrorsPerRequest` | 3 | Backstop on back-to-back tool errors; rarely hit in the CLI, where argument errors become recovery hints and the same error three times in a row ends the turn first |

```yaml
# .ironhive/config.yaml
chatBehavior:
  maximumIterationsPerRequest: 7      # tune down for small/quantized models
  maximumConsecutiveErrorsPerRequest: 3
```

### Context Compaction

Long sessions are kept within the model's context window automatically. When the agent loop is built
(via the CLI, the server runners, or `IronHive.Host` DI), a `ContextManager` is wired from the
`compaction` config so older history is compacted (token-based, protecting the most recent turns and
important tool outputs) instead of silently overflowing.

- Enabled by default; tune via the `compaction:` config section (see Configuration above)
- Model-aware — the context window is sized from the active model
- Embedded consumers can set `options.Compaction` on `AddIronHive(...)`; manual loop builders can wire it
  via `HostContextManagerFactory.Create(compactionConfig, modelName)` and pass the result to `AgentLoop`/`ThinkingAgentLoop`
- Tool results: `enableToolResultCompaction` (default on, results over `maxToolResultChars` = 30,000 keep head and tail) and `enableObservationMasking` (default on, results older than `observationMaskingProtectedTurns` = 3 user turns become placeholders; `observationMaskingProtectedRounds` also masks older tool rounds inside one turn). Only what is sent is reduced; history keeps full results
- Complements (does not replace) `TokenBudgetChatClient`, which remains the hard backstop against per-request overflow — installed by the CLI and `run --server`; `AddIronHive` embedders add it by wrapping their client (below)

### TokenBudgetChatClient

`IChatClient` decorator that short-circuits streaming calls when the accumulated message-history size would exceed a configurable fraction of the model's context window. Prevents context-overflow silent failures on small quantized models (e.g. 4K-window Gemma E4B).

The CLI and `run --server` wrap every provider client in it (under a tool invocation pipeline with the loop guards, the permission gate and `ResilientArgumentsMiddleware`). `AddIronHive(...)` / `AddIronHiveWithOpenAI(...)` register the provider client as-is — to get the same backstop in an embedded host, wrap the client yourself (constructor below, then function invocation as shown under **ResilientFunctionInvoker**) and pass the result to `AddIronHive(chatClient)` / `options.UseChatClient(...)`.

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

Converts the marshaller-level `ArgumentException` (missing/malformed tool arguments) and `JsonException` a tool invocation raises into model-actionable procedural error strings, enabling small quantized models to self-correct without aborting the stream. It comes in two forms with one implementation.

With `IronHive.Agent`'s tool invocation pipeline, add `ResilientArgumentsMiddleware` as the last step, so it sits directly around the tool (this is what the CLI does):

```csharp
var pipeline = new ToolInvocationPipeline(
[
    new ArgumentParseFailureMiddleware(),          // unparseable arguments: refused before the tool runs
    new RepeatedCallGuardMiddleware(),
    new RepeatedErrorGuardMiddleware(),
    new ApprovalGateMiddleware(modeToolFilter, approvalService),
    new ResilientArgumentsMiddleware(),            // parsed arguments that do not fit the tool: recovery directive
]);
var client = new TokenBudgetChatClient(innerClient)
    .AsBuilder()
    .UseToolInvocationPipeline(pipeline)
    .Build();
```

With DI, `services.AddToolInvocationMiddleware<ResilientArgumentsMiddleware>()` registered after the other steps does the same for `UseToolInvocationPipeline()`.

With a plain `UseFunctionInvocation` client, set it as the `FunctionInvoker`:

```csharp
var client = innerClient
    .AsBuilder()
    .UseFunctionInvocation(configure: c => c.FunctionInvoker = ResilientFunctionInvoker.Create())
    .Build();
```

When the model sends a tool call with a missing required parameter, instead of throwing and aborting, the step returns a numbered recovery directive telling the model exactly what is missing, where to find the value, and explicitly forbidding the empty-args retry pattern. It does not overlap with `ArgumentParseFailureMiddleware`: that step refuses a call whose arguments text could not be parsed at all, before the tool runs; this one answers arguments that were parsed but do not fit the tool's parameters, which only shows when the tool is invoked. Behind a `RepeatedCallGuardMiddleware`, a model that keeps sending the same empty arguments is refused after three directives.

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
var runner = new AgentServerRunner(ProcessMessage, logger, hitlBridge: hitlBridge);
await runner.RunAsync(ct);

// HTTP/SSE (host spawns the agent and communicates via REST)
var httpRunner = new AgentHttpRunner("http://localhost:5100", sessionId, ProcessMessage, logger, hitlBridge: hitlBridge);
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

`AgentHttpRunner` additionally exposes `PublishEvent` for out-of-band event delivery (e.g. provider fallback notices).

#### Human approval over the wire

`HitlBridge` is an `IHumanApprovalService` (the approver `IronHive.Agent`'s approval gate asks on an `Ask` verdict) whose
human is the client. Give the same instance to the gate and to the runner:

```csharp
var hitlBridge = new HitlBridge();                    // timeout: 5 minutes by default
var pipeline = new ToolInvocationPipeline([new ApprovalGateMiddleware(policy, hitlBridge)]);
// ... build the loop's chat client with UseToolInvocationPipeline(pipeline) ...
var runner = new AgentServerRunner(ProcessMessage, logger, hitlBridge: hitlBridge);
```

While the runner runs, each asked call is sent as a `hitl_request` (between the call's `tool_start` and `tool_end`; its
`call_id` matches the `tool_start`) and the call waits. The client answers with a `hitl_response` carrying the request's
`id`: `approved: true` runs the call (with `modified_arguments` when the person edited them), `approved: false` returns
`reason` to the model as the tool's result. Several requests can wait at once; an answer without an `id` is accepted only
while exactly one waits. No answer within the timeout, a cancelled turn, a runner that stops, or no runner at all —
each is a rejection, never a pass. Session rules of your own (trust lists, "already denied this session") fit as an
`IHumanApprovalService` that decorates the bridge. `ironhive run --server` does this for you; in an interactive CLI
session the terminal answers.

**Skills.** `skills:` in `config.yaml` (`roots: [~/.ironhive/skills, .ironhive/skills]`, optional `enabled`, `exclude`, `maxMetadataCharacters`, `acceptUnknownFields`) loads Agent Skills (`SKILL.md` bundles per the [specification](https://agentskills.io/specification)): every skill's name and description is in the system instructions, and the model calls `load_skill` for a body — only from inside the skill's directory. Skills the specification's validator rejects are not loaded (`acceptUnknownFields: true` admits bundles carrying another client's frontmatter keys). The `load_skill` tool is added by the CLI / `run --server` loop factory. The library `AddIronHive(...)` path does not add it — its loop gets only `options.Tools`. `services.AddAgentSkills(new SkillsConfig { Roots = [...] })` registers the `SkillsLoader` and puts the skills' metadata in the instructions, but an embedder must put `SkillsLoader.LoadTool` in its loop's tools itself (e.g. build the loader with `SkillsLoader.Create(config)` and add `loader.LoadTool` to `options.Tools`).

**Permissions.** Every tool call runs through the permission rules (`IronHive.Agent`'s `ApprovalGateMiddleware`, a step of the tool invocation pipeline installed on the chat client): `Allow` runs the tool, `Deny` returns the reason to the model, `Ask` prompts on the console — or, in `run --server`, is sent to the client as a `hitl_request` (see [Human approval over the wire](#human-approval-over-the-wire)). Planning mode is enforced at the same gate: while the session is planning, only read-only tools run. A console prompt needs a terminal — a piped one-shot (stdin or stdout redirected, no server client) rejects an `Ask` verdict with a reason instead, so a prompt never lands in an output stream. Rules come from the project's `.ironhive/permissions.yaml` (or `.yml` / `.json`; keys `read`, `edit`, `bash`, `external_directory`, `mcp_tools`, `tools`, `read_only_tools`, `ask_before_delete`, `default_action`) when it exists; otherwise from the `permissions` section of `config.yaml` — global `~/.ironhive/config.yaml`, then the project's, camelCase keys like the rest of the file (`externalDirectory`, `mcpTools`, `readOnlyTools`, `askBeforeDelete`, `defaultAction`); otherwise the built-in defaults (before 0.29.4 the `config.yaml` section was read and then discarded). `tools` matches by tool name any tool with no dedicated category, and an unmatched tool falls to the default action (`ask` by default — so an unknown tool is asked about, not run). `ask_before_delete` (`true` by default) asks before deleting a file even where the `edit` rules allow it; `false` lets the `edit` rules decide deletes.

**Protocol types** (`ServerRequest` → agent, `ServerEvent` → host):

| Type | Discriminator | Key fields |
|------|---------------|-----------|
| `UserMessageRequest` | `user_message` | `Content`, `Model?`, `Options?` (`TurnOptions`: `tool_names`, `tool_mode`, `reasoning_effort`, `temperature`, `max_output_tokens` — see below) |
| `ContextUpdateRequest` | `context_update` | `WorkingPath?`, `SelectedItems?` |
| `HitlResponseRequest` | `hitl_response` | `Approved`, `Reason?`, `Id?` (the request being answered), `ModifiedArguments?`, `AlwaysApprove?` |
| `CancelRequest` | `cancel` | — |
| `ShutdownRequest` | `shutdown` | — |
| `SessionStartedEvent` | `session_started` | `SessionId` — first line `run --server` writes, before any request is read (`--session-id` value, or a generated id) |
| `ToolStartEvent` | `tool_start` | `Tool`, `Input?`, `CallId?` |
| `HitlRequestEvent` | `hitl_request` | `Id`, `Action`, `Target`, `Description`, `ToolName?`, `Arguments?`, `CallId?` (matches the `tool_start`), `Level?` — a call waiting for approval; answer with `hitl_response` |
| `ToolEndEvent` | `tool_end` | `Tool`, `Success`, `Output?` (≤ 8 KB), `CallId?` — one per tool call once its outcome is known, before `turn_end`; `CallId` matches the `tool_start`. A call the permission gate refused arrives with `Success: false` and the refusal as `Output` (`Permission denied: …` / `Approval rejected: …`) |
| `ThinkingDeltaEvent` | `thinking_delta` | `Content` — extended-thinking text, its own stream, never folded into `text_delta` |
| `FallbackServerEvent` | `fallback` | `Kind` (`retry`\|`fallback`\|`exhausted`), `Category`, `Message`, `ProviderIndex`, `TotalProviders`, `Attempt`, `MaxAttempts` |
| `TextDeltaEvent` | `text_delta` | `Content` |
| `AddendumEvent` | `addendum` | `Content` — a turn observer's note, at most once per turn, after the last `text_delta` and before `turn_end`; not the model's words, never in history |
| `TurnEndEvent` | `turn_end` | `InputTokens?`, `OutputTokens?`, `TotalTokens?` (summed across every model round-trip in the turn; null when the provider reported no usage), `CachedInputTokens?`, `StopReason?` (`completed` · `output_limit` · `content_filter` · `tool_terminated` · `awaiting_host_tools` · `step_limit`; null when the turn failed or was cancelled), `DurationMs?` |
| `ErrorEvent` | `error` | `Message` |

`IronHive.Host.Protocol` also declares `agent_selected` (`AgentSelectedEvent`), `plan_created`, `plan_step_started`,
`plan_step_completed` and `plan_completed` (`Plan*ServerEvent`). No runner emits them today — they are reserved; a client
may accept them but should not wait for them.

**Per-turn options.** `UserMessageRequest.Options` narrows or tunes one turn: `tool_names` (a subset of
the agent's registered tools; `[]` = no tools this turn), `tool_mode` (`auto` | `none` | `require_any` |
`require:<tool>`), `reasoning_effort` (`none` | `low` | `medium` | `high` | `extra_high`),
`temperature`, `max_output_tokens`. The merge is field by field: a set field replaces the agent's
configured value for this turn only, an unset field keeps it, and the next request without `options`
is back on the agent's configuration — nothing is remembered across turns. A tool name that is not
registered, or an unknown mode/effort, is answered with an `error` event, never silently dropped. A
request without `options` is byte-identical to the pre-0.21.0 wire form.

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
- Git

### Build & Test

```bash
dotnet build
dotnet test
```

### Project Structure

```
ironhive-host/
├── src/
│   ├── IronHive.Host.Protocol/  # Thin turn-stream contracts (zero-dep NuGet)
│   ├── IronHive.Host/           # SDK / core library (NuGet)
│   │   ├── Config/              # Configuration classes
│   │   ├── Extensions/          # DI helpers
│   │   ├── Providers/           # LMSupply, IronHive chat client providers
│   │   ├── Server/              # AgentServerRunner, AgentHttpRunner (references Host.Protocol)
│   │   ├── Session/             # Session management
│   │   └── Tools/               # Built-in tools, ResilientArgumentsMiddleware/ResilientFunctionInvoker, TokenBudgetChatClient
│   └── IronHive.Cli/            # CLI application (tool command: ironhive)
├── samples/
│   ├── console-chat/            # .NET Core integration
│   └── web-ai-chat/             # Next.js + subprocess
└── tests/
```

## Contributing

Issues and pull requests are welcome. This project is pre-1.0 (`0.x`) — breaking changes land
freely for structural correctness. The scope is the hosting surface described in the introduction
above; please open an issue to discuss a feature before sending a PR. `dotnet build && dotnet test` (see [Development](#development)) should
pass before opening a PR.

## Related Projects

- [ironhive](https://github.com/iyulab/ironhive) — LLM abstraction
- [ironbees](https://github.com/iyulab/ironbees) — Multi-agent management
- [memory-indexer](https://github.com/iyulab/memory-indexer) — Semantic memory MCP
- [code-beaker](https://github.com/iyulab/code-beaker) — Code execution platform

## License

MIT
