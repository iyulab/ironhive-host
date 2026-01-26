# 남은 개선 사항

## 상태
**PARTIALLY RESOLVED** (2026-01-26)

## 심각도
**Low** - 기능적 영향 없음, 향후 개선 사항

---

## 1. 사용되지 않는 ISessionMemoryService 등록
**상태: DEFERRED** - Phase 2 MemoryIndexer 통합까지 유지

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
**상태: RESOLVED** (2026-01-26)

### 위치
- `src/IronHive.Cli.Core/Agent/IAgentLoopFactory.cs`
- `src/IronHive.Cli/Infrastructure/AgentLoopFactory.cs`

### 해결
AgentLoopFactoryOptions에 ThinkingOptions 속성 추가:
```csharp
public record AgentLoopFactoryOptions
{
    // ... existing properties
    public ThinkingChatClientOptions? ThinkingOptions { get; init; }
}
```

---

## 3. Fallback Provider 테스트 임베드
**상태: WON'T FIX** - 분석 결과 현재 구현 유지

### 분석 결과
- `_initialized` 캐시로 한 번만 실행됨
- 테스트 텍스트("test")가 짧아 오버헤드 미미
- provider 실제 동작 확인이 가장 확실한 방법

### 향후 계획
Phase 3+ 인터페이스 통합 시 CheckHealthAsync 일원화 검토

---

## 요약

| 이슈 | 상태 | 비고 |
|------|------|------|
| ISessionMemoryService | DEFERRED | Phase 2 통합까지 유지 |
| ThinkingChatClientOptions | RESOLVED | AgentLoopFactoryOptions 확장 |
| Fallback Provider 테스트 | WON'T FIX | 현재 구현 적절 |

## 참고
- Critical/High 이슈들은 `ISSUE-20260126-provider-thread-safety.md`에서 해결됨
