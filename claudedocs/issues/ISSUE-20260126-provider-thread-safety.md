# Provider Thread-Safety 및 초기화 문제

## 상태
**RESOLVED** (2026-01-26)

## 요약
Provider 클래스들의 thread-safety 및 초기화 관련 여러 Critical 이슈 수정

## 심각도
**Critical** - 런타임 크래시 및 동시성 문제

## 발견된 문제들

### 1. LMSupplyChatClientProvider 초기화 크래시
- **문제**: `GetChatClient()` 호출 전 `CheckHealthAsync()` 미호출 시 `InvalidOperationException` 발생
- **해결**: Lazy initialization 지원 추가

### 2. LMSupply 모델 오버라이드 실패
- **문제**: `GetChatClient(modelOverride)` 호출 시 기본 모델 외 다른 모델은 항상 예외 발생
- **해결**: 동적 모델 로딩 지원 (LoadModelSync)

### 3. Provider 캐시 Thread-Safety
- **문제**: `Dictionary<string, IChatClient>` 동시 접근 시 데이터 경합
- **해결**: `ConcurrentDictionary` + `GetOrAdd` 패턴 사용

### 4. FallbackChatClientProvider 자동 초기화 미지원
- **문제**: `GetChatClient()` 호출 시 `_activeProvider == null`이면 초기화 없이 예외
- **해결**: 자동 초기화 로직 추가 (lock + double-check)

### 5. AgentLoop Dispose 누락
- **문제**: DefaultCommand, RunCommand에서 IAgentLoop 생성 후 dispose 안 함
- **해결**: try-finally로 IAsyncDisposable dispose 추가

## 수정된 파일

```
src/IronHive.Cli.Core/Providers/LMSupplyChatClientProvider.cs
- ConcurrentDictionary 사용
- SemaphoreSlim으로 초기화 동시성 제어
- Lazy initialization 및 동적 모델 로딩

src/IronHive.Cli.Core/Providers/GpuStackChatClientProvider.cs
- ConcurrentDictionary.GetOrAdd 패턴 적용

src/IronHive.Cli.Core/Providers/FallbackChatClientProvider.cs
- volatile _activeProvider
- lock으로 자동 초기화 동시성 제어

src/IronHive.Cli/Commands/DefaultCommand.cs
src/IronHive.Cli/Commands/RunCommand.cs
- AgentLoop dispose finally 블록 추가
```

## 테스트 결과
- 빌드: 성공 (0 errors, 0 warnings)
- 테스트: 7/7 통과
