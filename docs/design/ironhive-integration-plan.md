# ironhive 통합 계획

## 개요

ironhive-cli가 별도로 프로바이더를 구현하는 대신, iyulab/ironhive 라이브러리를 직접 활용하여 코드 중복을 제거하고 유지보수성을 높입니다.

## 현황 분석

### ironhive-cli 현재 구조
```
src/IronHive.Cli.Core/Providers/
├── IChatClientProvider.cs          # M.E.AI IChatClient 래퍼
├── IChatClientFactory.cs           # 프로바이더 팩토리
├── GpuStackChatClientProvider.cs   # GpuStack/OpenAI 호환 API
├── LMSupplyChatClientProvider.cs   # 로컬 추론 (LMSupply)
├── FallbackChatClientProvider.cs   # Fallback 체인
└── ...Embedding/Rerank...          # 임베딩/리랭크 프로바이더
```

### ironhive 라이브러리 구조
```
IronHive.Abstractions/
├── IMessageGenerator               # LLM 메시지 생성 인터페이스
├── IEmbeddingGenerator             # 임베딩 생성 인터페이스
├── IHiveService                    # 통합 서비스 인터페이스
└── IHiveServiceBuilder             # 빌더 패턴

IronHive.Providers.*/
├── OpenAI                          # OpenAI, GpuStack, Xai 호환
├── Anthropic                       # Claude 모델
├── GoogleAI                        # Gemini 모델
└── Ollama                          # 로컬 Ollama
```

## 핵심 차이점

| 항목 | ironhive-cli (현재) | ironhive |
|------|---------------------|----------|
| Chat 인터페이스 | `IChatClient` (M.E.AI) | `IMessageGenerator` |
| 프로바이더 관리 | 개별 등록 | `IProviderRegistry` |
| 빌더 패턴 | 없음 (DI 직접) | `IHiveServiceBuilder` |

## 통합 전략

### 방안 1: 어댑터 패턴 (권장)

ironhive에 M.E.AI 호환 어댑터를 추가하여 기존 AgentLoop 구조를 유지합니다.

```
ironhive-cli AgentLoop
    ↓ (IChatClient)
IMessageGeneratorChatClientAdapter (ironhive에 추가)
    ↓ (IMessageGenerator)
IronHive.Providers.* (OpenAI, Anthropic, GoogleAI, ...)
```

**장점:**
- 기존 AgentLoop 코드 변경 최소화
- M.E.AI 생태계 호환성 유지
- 어댑터는 ironhive에서 유지 → 다른 프로젝트도 활용 가능

**ironhive에 추가할 코드:**
```csharp
// IronHive.Core/Adapters/MessageGeneratorChatClientAdapter.cs
public class MessageGeneratorChatClientAdapter : IChatClient
{
    private readonly IMessageGenerator _generator;
    private readonly string _modelId;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        var request = ConvertToMessageRequest(messages, options);
        var response = await _generator.GenerateMessageAsync(request, cancellationToken);
        return ConvertToChatResponse(response);
    }

    // ... streaming 메서드 등
}
```

### 방안 2: 직접 전환

AgentLoop이 IMessageGenerator를 직접 사용하도록 수정합니다.

**장점:**
- M.E.AI 의존성 제거
- ironhive와 완전 통합

**단점:**
- AgentLoop 전체 재작성 필요
- M.E.AI 기반 도구들 (FunctionInvokingChatClient 등) 사용 불가

## 권장 통합 계획

### Phase 1: ironhive 개선 (Dogfooding)

**ironhive 이슈 생성:**
1. `M.E.AI IChatClient 어댑터 추가` - IMessageGenerator → IChatClient
2. `Azure OpenAI 프로바이더 추가` - AzureOpenAIConfig, AzureOpenAIMessageGenerator
3. `Xai (Grok) 프로바이더 추가` - OpenAI 호환 API 기반

### Phase 2: ironhive-cli 패키지 교체

**의존성 변경:**
```xml
<!-- 제거 -->
<PackageReference Include="Anthropic.SDK" />
<PackageReference Include="Google_GenerativeAI.Microsoft" />
<PackageReference Include="Azure.AI.OpenAI" />

<!-- 추가 (로컬 참조 또는 NuGet) -->
<PackageReference Include="IronHive.Core" />
<PackageReference Include="IronHive.Providers.OpenAI" />
<PackageReference Include="IronHive.Providers.Anthropic" />
<PackageReference Include="IronHive.Providers.GoogleAI" />
<PackageReference Include="IronHive.Providers.Ollama" />
```

### Phase 3: 프로바이더 코드 제거

**삭제할 파일:**
```
src/IronHive.Cli.Core/Providers/
├── GpuStackChatClientProvider.cs   ← IronHive.Providers.OpenAI로 대체
├── GpuStackEmbeddingProvider.cs    ← IronHive.Providers.OpenAI로 대체
├── GpuStackRerankProvider.cs       ← 별도 처리 필요
└── FallbackChatClientProvider.cs   ← IProviderRegistry fallback으로 대체
```

**유지할 파일:**
```
├── IChatClientProvider.cs          ← 어댑터 래퍼로 변경
├── IChatClientFactory.cs           ← IProviderRegistry 기반으로 재구현
├── LMSupplyChatClientProvider.cs   ← LMSupply는 ironhive에 없음, 유지
├── LMSupplyEmbeddingProvider.cs
└── LMSupplyRerankProvider.cs
```

### Phase 4: 서비스 등록 간소화

**현재 (ServiceCollectionExtensions.cs):**
```csharp
if (config.GpuStack.IsConfigured)
{
    services.AddSingleton<GpuStackChatClientProvider>(...);
}
// ... 개별 프로바이더 등록
```

**변경 후:**
```csharp
// IHiveService 빌드
var hiveBuilder = new HiveServiceBuilder(services);

if (config.OpenAI.IsConfigured)
    hiveBuilder.AddOpenAIProviders("openai", config.OpenAI);

if (config.Anthropic.IsConfigured)
    hiveBuilder.AddAnthropicProviders("anthropic", config.Anthropic);

if (config.GoogleAI.IsConfigured)
    hiveBuilder.AddGoogleAIProviders("google", config.GoogleAI);

// ... 등

var hiveService = hiveBuilder.Build();
services.AddSingleton(hiveService);

// IChatClient 어댑터 등록
services.AddSingleton<IChatClient>(sp =>
{
    var hive = sp.GetRequiredService<IHiveService>();
    var generator = hive.Providers.Get<IMessageGenerator>(defaultProvider);
    return new MessageGeneratorChatClientAdapter(generator, defaultModel);
});
```

## 환경 변수 매핑

| 현재 (ironhive-cli) | ironhive Config |
|---------------------|-----------------|
| `GPUSTACK_ENDPOINT` | `OpenAIConfig.BaseUrl` |
| `GPUSTACK_API_KEY` | `OpenAIConfig.ApiKey` |
| `GPUSTACK_MODEL` | 모델 선택 시 지정 |
| `OPENAI_API_KEY` | `OpenAIConfig.ApiKey` |
| `ANTHROPIC_API_KEY` | `AnthropicConfig.ApiKey` |
| `GOOGLE_API_KEY` | `GoogleAIConfig.ApiKey` |

## Fallback 체인 설계

```
Priority Order:
1. GpuStack (자체 인프라)
2. OpenAI
3. Anthropic
4. GoogleAI
5. Xai
6. AzureOpenAI
7. LMSupply (로컬)
```

**구현:**
```csharp
public class FallbackMessageGenerator : IMessageGenerator
{
    private readonly List<(string name, IMessageGenerator generator)> _generators;

    public async Task<MessageResponse> GenerateMessageAsync(...)
    {
        foreach (var (name, generator) in _generators)
        {
            try
            {
                return await generator.GenerateMessageAsync(request, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Provider {Provider} failed: {Error}", name, ex.Message);
                continue;
            }
        }
        throw new InvalidOperationException("All providers failed");
    }
}
```

## 로드맵

### v0.2.1 - ironhive 준비
- [ ] ironhive에 M.E.AI 어댑터 이슈 생성
- [ ] ironhive에 Azure OpenAI 프로바이더 이슈 생성
- [ ] ironhive에 Xai 프로바이더 이슈 생성
- [ ] ironhive 변경사항 구현 및 릴리스

### v0.3.0 - ironhive-cli 통합
- [ ] ironhive 패키지 의존성 추가
- [ ] 기존 프로바이더 코드 제거
- [ ] ServiceCollectionExtensions 리팩토링
- [ ] 환경변수 로더 통합
- [ ] 테스트 업데이트

## Dogfooding 피드백 항목

ironhive 사용 중 발견된 개선점은 `d:/data/ironhive/claudedocs/issues/`에 기록합니다.

**예상 이슈:**
1. `ISSUE-*-meai-adapter.md` - M.E.AI IChatClient 어댑터 필요
2. `ISSUE-*-azure-openai.md` - Azure OpenAI 프로바이더 추가
3. `ISSUE-*-xai-grok.md` - Xai (Grok) 프로바이더 추가
4. `ISSUE-*-fallback-chain.md` - 다중 프로바이더 fallback 지원

## 참고

- [ironhive GitHub](https://github.com/iyulab/ironhive)
- [Microsoft.Extensions.AI](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai)
- [CLAUDE.md dogfooding 규칙](/.claude/CLAUDE.md)
