# CLI 옵션 --model, --provider 무시됨

## 요약
커맨드에서 `--model`과 `--provider` 옵션을 정의하지만 실제로 적용되지 않음.

## 심각도
**Medium** - UX 문제

## 발견 위치
- `src/IronHive.Cli/Commands/DefaultCommand.cs:26-32`
- `src/IronHive.Cli/Commands/RunCommand.cs:33-35`

## 상세

```csharp
public class Settings : CommandSettings
{
    [CommandOption("-m|--model <MODEL>")]
    [Description("Model to use (e.g., gpt-4o-mini, llama3.2)")]
    public string? Model { get; init; }  // 정의만 존재

    [CommandOption("--provider <PROVIDER>")]
    [Description("Provider (openai, ollama, gpustack)")]
    public string? Provider { get; init; }  // 사용되지 않음
}
```

### 문제점
1. Settings에 옵션 정의됨
2. ExecuteAsync에서 settings 객체 접근 가능
3. 하지만 IAgentLoop 생성 시 반영되지 않음

## 예상 동작
```bash
ironhive --model gpt-4 --provider openai "Hello"
# → gpt-4 모델로 openai 통해 요청

ironhive --model llama3.2 --provider ollama "Hello"
# → llama3.2 모델로 ollama 통해 요청
```

## 수정 방안
1. Config에 런타임 오버라이드 메커니즘 추가
2. 또는 Settings 기반으로 새 IChatClient 생성

```csharp
public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
{
    var config = /* base config */;

    if (settings.Model is not null)
        config.Model = settings.Model;

    if (settings.Provider is not null)
        config.Provider = settings.Provider;

    var agentLoop = CreateAgentLoop(config);
    // ...
}
```

## 우선순위
P2 - 사용성 개선
