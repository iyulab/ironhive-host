# .NET AI 생태계 조사 보고서

> P0-02, P0-03 조사 결과

## 요약

| 영역 | 선택 | 이유 |
|------|------|------|
| **AI 프레임워크** | Microsoft Agent Framework | 통합된 최신 프레임워크, MS 공식 |
| **기반 추상화** | Microsoft.Extensions.AI | IChatClient 표준 인터페이스 |
| **CLI 파싱** | Spectre.Console.Cli | DI 지원, 클래스 기반, 렌더링 통합 |
| **콘솔 렌더링** | Spectre.Console | 테이블, 진행률, ANSI 지원 |

---

## 1. AI 프레임워크 비교

### 1.1 Microsoft Agent Framework (선택)

**상태**: Preview (2025년 10월 발표)

Microsoft가 Semantic Kernel과 AutoGen을 통합하여 만든 새로운 프레임워크.

**핵심 특징**:
- Microsoft.Extensions.AI 기반
- Graph-based Workflows (스트리밍, 체크포인팅, HITL)
- OpenTelemetry 통합
- MCP (Model Context Protocol) 지원
- A2A (Agent-to-Agent) 메시징
- Python/.NET 모두 지원

**NuGet 패키지**:
```xml
<PackageReference Include="Microsoft.Agents.AI" Version="1.0.0-preview.*" />
<PackageReference Include="Microsoft.Agents.AI.OpenAI" Version="1.0.0-preview.*" />
```

**기본 사용법**:
```csharp
using Microsoft.Agents.AI;

AIAgent agent = new AzureOpenAIClient(
    new Uri("https://your-resource.openai.azure.com/"),
    new AzureCliCredential())
    .GetChatClient("gpt-4o-mini")
    .AsAIAgent(instructions: "You are a helpful assistant.");

var response = await agent.RunAsync("Hello!");
```

**참조**:
- [Quick Start](https://learn.microsoft.com/en-us/agent-framework/tutorials/quick-start)
- [GitHub](https://github.com/microsoft/agent-framework)
- [NuGet Profile](https://www.nuget.org/profiles/MicrosoftAgentFramework)

### 1.2 Microsoft.Extensions.AI (기반 레이어)

모든 AI 추상화의 기반. Microsoft Agent Framework가 이 위에 구축됨.

**핵심 인터페이스**:
- `IChatClient`: LLM 채팅 추상화
- `IEmbeddingGenerator<TInput, TEmbedding>`: 임베딩 추상화

**패키지 구조**:
```
Microsoft.Extensions.AI.Abstractions  // 인터페이스 정의
Microsoft.Extensions.AI               // 미들웨어, DI 통합
```

**프로바이더 독립적**:
```csharp
// OpenAI
IChatClient client = new OpenAIClient(apiKey).AsChatClient("gpt-4o-mini");

// Azure OpenAI
IChatClient client = new AzureOpenAIClient(endpoint, credential).AsChatClient("gpt-4o-mini");

// Ollama (로컬)
IChatClient client = new OllamaApiClient(new Uri("http://localhost:11434/"), "phi3:mini");
```

**참조**:
- [Microsoft.Extensions.AI 개요](https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai)
- [IChatClient 사용법](https://learn.microsoft.com/en-us/dotnet/ai/ichatclient)

### 1.3 Semantic Kernel (레거시)

Microsoft Agent Framework 이전의 SDK. 현재는 Agent Framework로 마이그레이션 권장.

**마이그레이션 경로**: Kernel + Plugin → Agent + Tool

### 1.4 AutoGen.NET (연구용)

Microsoft Research AI Frontiers Lab 개발. 멀티에이전트 대화 특화.

**마이그레이션 경로**: AssistantAgent → ChatAgent

---

## 2. CLI 라이브러리 비교

### 2.1 Spectre.Console.Cli (선택)

**다운로드**: 7.7M+ (NuGet)

**장점**:
- 클래스 기반 명령 정의 (테스트 용이)
- Microsoft.Extensions.DependencyInjection 통합
- Spectre.Console 렌더링과 자연스러운 통합
- 업계 표준 CLI 규칙 준수
- 활발한 유지보수 (2025년 12월 최신 커밋)

**기본 구조**:
```csharp
// Settings
public class GreetSettings : CommandSettings
{
    [CommandArgument(0, "<name>")]
    public required string Name { get; init; }

    [CommandOption("-c|--count")]
    public int Count { get; init; } = 1;
}

// Command
public class GreetCommand : Command<GreetSettings>
{
    public override int Execute(CommandContext context, GreetSettings settings)
    {
        for (int i = 0; i < settings.Count; i++)
            AnsiConsole.MarkupLine($"Hello, [green]{settings.Name}[/]!");
        return 0;
    }
}

// Program
var app = new CommandApp<GreetCommand>();
return app.Run(args);
```

**DI 통합**:
```csharp
var services = new ServiceCollection();
services.AddSingleton<IMyService, MyService>();

var app = new CommandApp(new TypeRegistrar(services));
app.Configure(config =>
{
    config.AddCommand<MyCommand>("mycommand");
});
```

**참조**:
- [Spectre.Console.Cli 문서](https://spectreconsole.net/cli)
- [GitHub](https://github.com/spectreconsole/spectre.console.cli)

### 2.2 System.CommandLine (대안)

.NET Foundation 프로젝트. 파싱 전용.

**장점**:
- 미래 .NET BCL 편입 가능성
- 순수 파싱에 집중

**단점**:
- 렌더링 기능 없음 (별도로 Spectre.Console 필요)
- 클래스 기반 명령이 아닌 함수 기반

**결론**: Spectre.Console.Cli가 파싱 + 렌더링 통합으로 더 적합

### 2.3 벤치마크 참조

- [콘솔 라이브러리 3종 벤치마크](https://forum.dotnetdev.kr/t/3-system-commandline-spectre-console-cli-clifx/13490) (2025.08)

---

## 3. 최종 패키지 목록

### 핵심 패키지
```xml
<!-- AI Framework -->
<PackageReference Include="Microsoft.Extensions.AI" Version="10.0.0-*" />
<PackageReference Include="Microsoft.Agents.AI" Version="1.0.0-preview.*" />
<PackageReference Include="Microsoft.Agents.AI.OpenAI" Version="1.0.0-preview.*" />

<!-- CLI -->
<PackageReference Include="Spectre.Console" Version="0.53.*" />
<PackageReference Include="Spectre.Console.Cli" Version="0.53.*" />

<!-- DI -->
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.0" />
<PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.0" />
```

### 로컬 모델 지원 (gpustack/Ollama)
```xml
<PackageReference Include="OllamaSharp" Version="*" />
```

---

## 4. 아키텍처 권장사항

### 4.1 프로바이더 추상화

```
┌─────────────────────────────────────────────┐
│              Application Layer              │
│   (Commands, Services, Agent Loop)          │
└──────────────────┬──────────────────────────┘
                   │ IChatClient
┌──────────────────┴──────────────────────────┐
│       Microsoft.Extensions.AI               │
│   (Middleware: Logging, Caching, Tools)     │
└──────────────────┬──────────────────────────┘
                   │
    ┌──────────────┼──────────────┐
    ▼              ▼              ▼
┌────────┐   ┌──────────┐   ┌─────────┐
│ OpenAI │   │  Azure   │   │ Ollama  │
│        │   │ OpenAI   │   │/gpustack│
└────────┘   └──────────┘   └─────────┘
```

### 4.2 CLI 구조

```
ironhive
├── (default) → 대화형 모드
├── run -p "prompt" → 단일 명령
├── plan "task" → Plan 모드
└── config → 설정 관리
```

---

## 5. 참조 링크

### 공식 문서
- [Microsoft.Extensions.AI](https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai)
- [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/)
- [Spectre.Console](https://spectreconsole.net/)

### 튜토리얼
- [IChatClient 사용법](https://learn.microsoft.com/en-us/dotnet/ai/ichatclient)
- [Agent Framework Quick Start](https://learn.microsoft.com/en-us/agent-framework/tutorials/quick-start)
- [Spectre.Console.Cli 튜토리얼](https://spectreconsole.net/cli)

### 블로그/아티클
- [Microsoft Agent Framework 소개](https://devblogs.microsoft.com/dotnet/introducing-microsoft-agent-framework-preview/)
- [Semantic Kernel vs AutoGen](https://medium.com/microsoftazure/semantic-kernel-vs-autogen-which-microsoft-ai-framework-fits-your-needs-a4de90ef4045)
- [MS.Extensions.AI 멀티 프로바이더 설정](https://weblog.west-wind.com/posts/2025/May/30/Configuring-MicrosoftAIExtension-with-multiple-providers)
