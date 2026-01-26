# .env 파일 및 LMSupply 통합 설계

## 개요

ironhive-cli는 다음 전략으로 AI 서비스를 제공합니다:

1. **Primary**: gpustack/OpenAI 호환 API (원격)
2. **Fallback**: LMSupply 로컬 추론

## .env 파일 구조

```env
# Primary: gpustack/OpenAI 호환 API
GPUSTACK_ENDPOINT=http://172.30.1.53:8080
GPUSTACK_API_KEY=gpustack_xxx
GPUSTACK_MODEL=gpt-oss-20b

# Optional: 별도 Embedding/Rerank 모델
GPUSTACK_EMBEDDING_MODEL=qwen3-embedding-0.6b
GPUSTACK_RERANK_MODEL=qwen3-reranker-0.6b

# Fallback: LMSupply 로컬 모델
LMSUPPLY_ENABLED=true
LMSUPPLY_EMBEDDER_MODEL=auto
LMSUPPLY_RERANKER_MODEL=auto
LMSUPPLY_GENERATOR_MODEL=gguf:default
```

## 프로바이더 우선순위

```
┌─────────────────────────────────────────────────────────────────┐
│                        IronHive CLI                              │
└───────────────────────────┬─────────────────────────────────────┘
                            │
              ┌─────────────┴─────────────┐
              ▼                           ▼
     ┌────────────────┐         ┌─────────────────┐
     │   IChatClient  │         │  IEmbedder      │
     │   IEmbedder    │         │  IReranker      │
     └───────┬────────┘         └────────┬────────┘
             │                           │
    ┌────────┴────────┐         ┌────────┴────────┐
    ▼                 ▼         ▼                 ▼
┌─────────┐    ┌──────────┐  ┌─────────┐   ┌──────────┐
│gpustack │    │ LMSupply │  │gpustack │   │ LMSupply │
│(Primary)│    │(Fallback)│  │(Primary)│   │(Fallback)│
└─────────┘    └──────────┘  └─────────┘   └──────────┘
```

## Fallback 시나리오

| 상황 | 동작 |
|------|------|
| gpustack 정상 | gpustack 사용 |
| gpustack 연결 실패 | LMSupply로 fallback |
| gpustack 타임아웃 | LMSupply로 fallback |
| .env 파일 없음 | LMSupply "auto" 모드 사용 |
| LMSUPPLY_ENABLED=false | fallback 비활성화, 오류 발생 |

## 인터페이스 설계

### Core Abstractions (IronHive.Cli.Core)

```csharp
// LLM 채팅
public interface IChatClientProvider
{
    IChatClient GetChatClient();
    bool IsAvailable { get; }
    string ProviderName { get; }
}

// 임베딩
public interface IEmbeddingProvider
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
    Task<float[][]> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct = default);
    bool IsAvailable { get; }
    string ProviderName { get; }
}

// 리랭킹
public interface IRerankProvider
{
    Task<IReadOnlyList<RerankResult>> RerankAsync(
        string query,
        IEnumerable<string> documents,
        int topK = 10,
        CancellationToken ct = default);
    bool IsAvailable { get; }
    string ProviderName { get; }
}
```

### Fallback Composite

```csharp
public class FallbackChatClientProvider : IChatClientProvider
{
    private readonly IChatClientProvider[] _providers;

    public IChatClient GetChatClient()
    {
        foreach (var provider in _providers)
        {
            if (provider.IsAvailable)
                return provider.GetChatClient();
        }
        throw new InvalidOperationException("No available chat client provider");
    }
}
```

## NuGet 패키지 의존성

```xml
<!-- LMSupply packages (fallback) -->
<PackageReference Include="LMSupply.Embedder" Version="*" />
<PackageReference Include="LMSupply.Reranker" Version="*" />
<PackageReference Include="LMSupply.Generator" Version="*" />

<!-- .env support -->
<PackageReference Include="DotNetEnv" Version="3.1.1" />
```

## 설정 로딩 순서

1. `.env` 파일 (프로젝트 루트)
2. 환경 변수
3. `~/.ironhive/config.yaml` (전역)
4. `.ironhive/config.yaml` (프로젝트)
5. CLI 인자

## 구현 태스크

| ID | 태스크 | 설명 | 상태 |
|----|--------|------|------|
| ENV-01 | DotNetEnv 통합 | .env 파일 로딩 | ✅ 완료 |
| ENV-02 | IronHiveConfig 확장 | Embedding, Rerank 설정 추가 | ✅ 완료 |
| ENV-03 | IEmbeddingProvider 정의 | 임베딩 추상화 | ✅ 완료 |
| ENV-04 | IRerankProvider 정의 | 리랭킹 추상화 | ✅ 완료 |
| ENV-05 | GpuStackEmbeddingProvider | gpustack 임베딩 구현 | ✅ 완료 |
| ENV-06 | GpuStackRerankProvider | gpustack 리랭킹 구현 | ✅ 완료 |
| ENV-07 | LMSupplyEmbeddingProvider | LMSupply 임베딩 래퍼 | ✅ 완료 |
| ENV-08 | LMSupplyRerankProvider | LMSupply 리랭킹 래퍼 | ✅ 완료 |
| ENV-09 | LMSupplyChatClientProvider | LMSupply 생성기 래퍼 | ✅ 완료 |
| ENV-10 | FallbackComposite 구현 | 자동 fallback 로직 | ✅ 완료 |
| ENV-11 | 테스트 작성 | 각 프로바이더 테스트 | ⏳ 대기 |

## 구현된 파일

### 설정 (Config/)
- `IronHiveConfig.cs` - 전체 설정 구조
- `EnvConfigLoader.cs` - .env 파일 및 환경변수 로딩

### 프로바이더 인터페이스 (Providers/)
- `IChatClientProvider.cs` - 채팅 클라이언트 프로바이더 인터페이스
- `IEmbeddingProvider.cs` - 임베딩 프로바이더 인터페이스
- `IRerankProvider.cs` - 리랭킹 프로바이더 인터페이스

### GpuStack 프로바이더
- `GpuStackChatClientProvider.cs` - OpenAI 호환 API 채팅
- `GpuStackEmbeddingProvider.cs` - OpenAI 호환 API 임베딩
- `GpuStackRerankProvider.cs` - 리랭킹 API

### LMSupply 프로바이더
- `LMSupplyChatClientProvider.cs` - LMSupply.Generator 래퍼
- `LMSupplyEmbeddingProvider.cs` - LMSupply.Embedder 래퍼
- `LMSupplyRerankProvider.cs` - LMSupply.Reranker 래퍼

### Fallback 컴포지트
- `FallbackChatClientProvider.cs` - 채팅 클라이언트 fallback
- `FallbackEmbeddingProvider.cs` - 임베딩 fallback
- `FallbackRerankProvider.cs` - 리랭킹 fallback
