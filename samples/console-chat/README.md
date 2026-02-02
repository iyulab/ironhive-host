# Console Chat Sample

IronHive.Cli.Core 라이브러리를 직접 통합하는 콘솔 채팅 샘플입니다.

## 목적

**Core 라이브러리 API 완성도**를 검증합니다.

## 발견된 Gap

| ID | 설명 | 상태 |
|----|------|------|
| CORE-G1 | OpenAI/Anthropic 클라이언트 팩토리 헬퍼 | ✅ 구현됨 |
| CORE-G2 | `AgentResponseChunk.ThinkingDelta` | ✅ 구현됨 |
| CORE-G3 | 세션-AgentLoop 통합 API | ✅ 구현됨 |
| CORE-G4 | 사용량 통계 조회 API | 🔲 대기 |

## 실행 방법

```bash
cd samples/console-chat

# 빌드 및 실행
dotnet run

# 또는 API 키와 함께
OPENAI_API_KEY=sk-xxx dotnet run
```

## 환경 변수

| 변수 | 설명 | 기본값 |
|------|------|--------|
| `OPENAI_API_KEY` | API 키 | (없으면 Mock 사용) |
| `OPENAI_ENDPOINT` | API 엔드포인트 | https://api.openai.com/v1 |
| `OPENAI_MODEL` | 모델 이름 | gpt-4o-mini |

## 사용 예시

```
=== IronHive Console Chat Sample ===
Core 라이브러리 통합 검증용 샘플

Mock 클라이언트를 사용합니다.

대화를 시작합니다. 종료하려면 'exit' 또는 'quit'을 입력하세요.
새 세션을 만들려면 '/new', 세션 목록은 '/sessions'

--------------------------------------------------

You: 안녕하세요

IronHive: [Mock] 입력 받음: "안녕하세요"
이것은 테스트 응답입니다. API 키를 설정하면 실제 LLM 응답을 받을 수 있습니다.

You: /sessions

세션 목록 (2개):
  - abc123 (2024-01-15 10:30)
  - def456 (2024-01-14 15:20)

You: exit

대화를 종료합니다.
총 1번의 대화가 있었습니다.
```

## 구현된 API

```csharp
// CORE-G1 ✅: 간편 프로바이더 설정
services.AddIronHiveWithOpenAI(apiKey, model);
services.AddIronHiveWithOpenAICompatible(endpoint, apiKey, model);  // GpuStack, vLLM 등
services.AddIronHiveWithOllama(model);  // 로컬 Ollama

// CORE-G2 ✅: Thinking 스트리밍
await foreach (var chunk in agent.RunStreamingAsync(prompt))
{
    if (chunk.ThinkingDelta != null)
        Console.Write($"[Thinking] {chunk.ThinkingDelta}");
    if (chunk.TextDelta != null)
        Console.Write(chunk.TextDelta);
}

// CORE-G3 ✅: 세션 통합 API
// 세션 로드 + 컨텍스트 복원 한 번에
var session = await agentLoop.LoadSessionAsync(sessionManager, sessionId);

// 세션 생성/복원 통합 (continueLatest=true면 최근 세션 이어하기)
var session = await agentLoop.LoadOrCreateSessionAsync(
    sessionManager, projectPath, model, continueLatest: true);

// 대화 턴 저장 간소화
await sessionManager.SaveTurnAsync(session, prompt, response);
```

## Gap 해결 후 기대하는 API

```csharp
// CORE-G4: 사용량 조회
var usage = agent.GetSessionUsage();
Console.WriteLine($"Total tokens: {usage.TotalTokens}");
```
