# AgentLoop: Tool 실행 로직 누락

## 상태
**RESOLVED** (2026-01-26)

## 요약
`AgentLoop.ExtractToolCalls()`가 응답에서 tool call을 추출하지만 실제 실행은 하지 않음.

## 심각도
**Critical** - Agent가 도구를 사용할 수 없음

## 발견 위치
- `src/IronHive.Cli.Core/Agent/AgentLoop.cs:105-124`

## 상세

```csharp
// 현재 구현 (문제)
private static List<ToolCallResult> ExtractToolCalls(ChatResponse response)
{
    // ...
    results.Add(new ToolCallResult
    {
        ToolName = content.Name,
        Arguments = content.Arguments?.ToString() ?? "{}",
        Result = string.Empty,  // ← 항상 빈 문자열
        Success = true
    });
}
```

### 누락된 기능
1. Tool call 추출 후 실제 실행
2. 실행 결과를 대화 히스토리에 주입
3. Tool 결과를 바탕으로 대화 계속

## 예상 구현

```csharp
public async Task<AgentResponse> RunAsync(string prompt, CancellationToken ct)
{
    _history.Add(new ChatMessage(ChatRole.User, prompt));

    while (true)
    {
        var response = await _chatClient.GetResponseAsync(_history, options, ct);
        _history.AddRange(response.Messages);

        var toolCalls = ExtractToolCalls(response);
        if (toolCalls.Count == 0)
        {
            // 대화 종료
            return new AgentResponse { Content = response.Text, ... };
        }

        // Tool 실행 및 결과 주입
        foreach (var call in toolCalls)
        {
            var result = await _toolExecutor.ExecuteAsync(call, ct);
            _history.Add(new ChatMessage(ChatRole.Tool, result));
        }
        // 루프 계속 - LLM이 tool 결과 기반 응답 생성
    }
}
```

## 관련 파일
- `src/IronHive.Cli.Core/Agent/AgentLoop.cs`
- `src/IronHive.Cli.Core/Agent/ThinkingAgentLoop.cs`

## 우선순위
P0 - Core agent 기능

## 해결 방법

직접 구현 대신 Microsoft.Extensions.AI의 `FunctionInvokingChatClient` 데코레이터 패턴 활용:

```csharp
// ServiceCollectionExtensions.cs
services.AddSingleton<IChatClient>(sp =>
{
    var provider = sp.GetRequiredService<IChatClientProvider>();
    var innerClient = provider.GetChatClient();

    return new FunctionInvokingChatClient(innerClient)
    {
        MaximumIterationsPerRequest = 10,
        MaximumConsecutiveErrorsPerRequest = 3,
        IncludeDetailedErrors = true
    };
});
```

**이점**:
- 공식 Microsoft 패턴 사용
- 자동 tool 실행 루프
- 동시성, 에러 핸들링, 루프 제한 기본 제공
- AgentLoop 코드 단순성 유지
