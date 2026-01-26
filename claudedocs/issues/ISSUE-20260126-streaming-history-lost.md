# AgentLoop: Streaming 모드에서 대화 히스토리 유실

## 상태
**RESOLVED** (2026-01-26)

## 요약
`RunStreamingAsync()`에서 응답을 스트리밍하지만 히스토리에 추가하지 않음.

## 심각도
**High** - 멀티턴 대화 불가

## 발견 위치
- `src/IronHive.Cli.Core/Agent/AgentLoop.cs:51-93`

## 상세

```csharp
public async IAsyncEnumerable<AgentResponseChunk> RunStreamingAsync(...)
{
    _history.Add(new ChatMessage(ChatRole.User, prompt));

    await foreach (var update in _chatClient.GetStreamingResponseAsync(...))
    {
        yield return new AgentResponseChunk { TextDelta = update.Text };
    }

    // 문제: assistant 응답이 히스토리에 추가되지 않음!
    // 주석에서도 인정: "This is handled by the caller or a wrapper."
    // 하지만 실제로 caller가 처리하지 않음
}
```

### 문제점
1. 스트리밍 응답 전체를 수집하지 않음
2. Assistant 메시지가 `_history`에 추가되지 않음
3. 다음 턴에서 이전 assistant 응답 컨텍스트 손실

## 수정 방안

```csharp
public async IAsyncEnumerable<AgentResponseChunk> RunStreamingAsync(...)
{
    _history.Add(new ChatMessage(ChatRole.User, prompt));

    var responseBuilder = new StringBuilder();

    await foreach (var update in _chatClient.GetStreamingResponseAsync(...))
    {
        if (!string.IsNullOrEmpty(update.Text))
        {
            responseBuilder.Append(update.Text);
            yield return new AgentResponseChunk { TextDelta = update.Text };
        }
    }

    // 히스토리에 전체 응답 추가
    _history.Add(new ChatMessage(ChatRole.Assistant, responseBuilder.ToString()));
}
```

## 관련 파일
- `src/IronHive.Cli.Core/Agent/AgentLoop.cs`
- `src/IronHive.Cli.Core/Agent/ThinkingAgentLoop.cs`

## 우선순위
P1 - UX 문제
