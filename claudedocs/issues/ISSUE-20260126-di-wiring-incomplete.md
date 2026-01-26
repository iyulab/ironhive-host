# DI 배선 불완전: Provider/Memory 시스템 비활성 상태

## 상태
**RESOLVED** (2026-01-26)

## 요약
현재 ironhive-cli의 DI(Dependency Injection) 배선이 불완전하여 대부분의 핵심 기능이 비활성 상태입니다.

## 심각도
**Critical** - 기본 기능 작동 불가

## 발견 위치
- `src/IronHive.Cli/Infrastructure/ServiceCollectionExtensions.cs`

## 상세 문제점

### 1. 이중 IronHiveConfig 충돌
두 개의 `IronHiveConfig` 클래스가 존재하며 서로 호환되지 않음:
- `ServiceCollectionExtensions.cs:47-95` - Provider, Model, ApiKey 등
- `Core/Config/IronHiveConfig.cs` - GpuStack, LMSupply 중첩 설정

**영향:** `.env` 파일 로딩 불가, Provider 초기화 불가

### 2. Provider 미등록
다음 인터페이스가 DI에 등록되지 않음:
- `IChatClientProvider`
- `IEmbeddingProvider`
- `IRerankProvider`

대신 구식 `IChatClientFactory`를 사용하여 전체 fallback 시스템 우회

**영향:** GpuStack → LMSupply 자동 전환 불가

### 3. Memory 서비스 미연결
`AddIronHiveMemory()` 확장 메서드가 구현되어 있으나 `AddIronHiveServices()`에서 호출하지 않음

**영향:** 대화 메모리, 세션 관리 기능 비활성

### 4. ThinkingAgentLoop 접근 불가
`IAgentLoop`으로 `AgentLoop`만 등록됨. `ThinkingAgentLoop`는 구현만 존재

**영향:** `--show-thinking` 옵션 무효

## 수정 방향
```csharp
public static IServiceCollection AddIronHiveServices(this IServiceCollection services)
{
    // 1. Core config 사용
    var config = EnvConfigLoader.Load();
    services.AddSingleton(config);

    // 2. Provider 등록
    services.AddSingleton<IChatClientProvider, FallbackChatClientProvider>();
    services.AddSingleton<IEmbeddingProvider, FallbackEmbeddingProvider>();

    // 3. Memory 서비스
    services.AddIronHiveMemory();

    // 4. ThinkingAgentLoop 사용
    services.AddTransient<IAgentLoop, ThinkingAgentLoop>();

    return services;
}
```

## 관련 파일
- `src/IronHive.Cli.Core/Config/EnvConfigLoader.cs`
- `src/IronHive.Cli.Core/Providers/*.cs`
- `src/IronHive.Cli.Core/Memory/MemoryServiceExtensions.cs`
- `src/IronHive.Cli.Core/Agent/ThinkingAgentLoop.cs`

## 우선순위
P0 - 블로커

## 해결 상태

### ✅ 해결됨
1. **이중 IronHiveConfig 충돌** - Core의 IronHiveConfig만 사용하도록 수정
2. **Provider 미등록** - IChatClientProvider, IEmbeddingProvider, IRerankProvider 모두 등록 완료
3. **ThinkingAgentLoop 접근 불가** - IAgentLoop으로 ThinkingAgentLoop 등록 완료

### ✅ 해결됨 (추가)
4. **Memory 서비스 미연결** - AddIronHiveMemory() 호출 추가 완료
