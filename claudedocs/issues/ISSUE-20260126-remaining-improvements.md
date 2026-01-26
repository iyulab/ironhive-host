# 남은 개선 사항

## 상태
**OPEN** (2026-01-26)

## 심각도
**Low** - 기능적 영향 없음, 향후 개선 사항

---

## 1. 사용되지 않는 ISessionMemoryService 등록

### 위치
- `src/IronHive.Cli.Core/Memory/MemoryServiceExtensions.cs`
- `src/IronHive.Cli.Core/Memory/ISessionMemoryService.cs`
- `src/IronHive.Cli.Core/Memory/SessionMemoryService.cs`

### 설명
ISessionMemoryService가 DI 컨테이너에 등록되지만 실제로 사용되지 않음.
MemoryIndexer 통합을 위해 준비된 것으로 보이나, 현재 코드에서 참조 없음.

### 권장
- Phase 2 MemoryIndexer 통합 시 활용 또는
- 불필요 시 제거하여 코드 정리

---

## 2. ThinkingChatClientOptions 구성 불가

### 위치
- `src/IronHive.Cli.Core/Agent/ThinkingAgentLoop.cs:31`
- `src/IronHive.Cli/Infrastructure/AgentLoopFactory.cs`

### 설명
ThinkingChatClientOptions가 항상 기본값으로 생성됨.
사용자가 thinking 추출 동작을 커스터마이징할 수 없음.

```csharp
// 현재 코드
_thinkingClient = new ThinkingChatClient(
    chatClient,
    turnManager,
    thinkingOptions ?? new ThinkingChatClientOptions());  // 항상 기본값
```

### 권장
AgentLoopFactoryOptions에 ThinkingChatClientOptions 추가:
```csharp
public record AgentLoopFactoryOptions
{
    // ... existing properties
    public ThinkingChatClientOptions? ThinkingOptions { get; init; }
}
```

---

## 3. Fallback Provider 테스트 임베드 중복

### 설명
FallbackEmbeddingProvider 및 FallbackRerankProvider가 CheckHealthAsync에서
테스트 임베드/리랭크를 수행하여 불필요한 리소스 소비 가능.

### 권장
간단한 ping/endpoint 확인으로 대체하거나, 캐시된 결과 사용

---

## 우선순위
P3 - 향후 개선

## 참고
- 이 이슈들은 기능적 버그가 아닌 코드 품질 개선 사항
- Critical/High 이슈들은 `ISSUE-20260126-provider-thread-safety.md`에서 해결됨
