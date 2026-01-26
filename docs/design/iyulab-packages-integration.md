# iyulab 패키지 통합 설계

## 개요

ironhive-cli는 iyulab의 다양한 오픈소스 패키지를 활용하여 기능을 확장합니다.
"하나만 잘한다" 철학에 따라 단계별로 패키지를 통합합니다.

## 패키지 분석 요약

| 패키지 | 버전 | 용도 | 우선순위 |
|--------|------|------|----------|
| IndexThinking | 0.15.0 | 토큰 관리, 추론 추출 | 🔴 필수 |
| memory-indexer | 0.13.0 | 대화 메모리, 세션 관리 | 🟠 높음 |
| FileFlux | 0.9.8 | 문서 처리, 청킹 | 🟡 중간 |
| FluxIndex | 0.5.8 | 시맨틱 검색, RAG | 🟡 중간 |
| WebFlux | 0.1.9 | 웹 크롤링 | 🟢 낮음 |

## 통합 아키텍처

```
┌─────────────────────────────────────────────────────────────────────┐
│                         IronHive CLI                                 │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐                 │
│  │ IndexThinking│  │   Memory    │  │  FluxIndex  │                 │
│  │             │  │   Indexer   │  │             │                 │
│  │ - 토큰 관리  │  │ - 세션 메모리│  │ - 시맨틱 검색│                 │
│  │ - 추론 추출  │  │ - 컨텍스트   │  │ - RAG       │                 │
│  │ - 잘림 감지  │  │ - 장기 기억  │  │ - 그래프    │                 │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘                 │
│         │                │                │                         │
│         └────────────────┼────────────────┘                         │
│                          │                                          │
│                          ▼                                          │
│              ┌──────────────────────┐                               │
│              │   Shared Services    │                               │
│              │  - IEmbeddingProvider│◄─── LMSupply / GpuStack       │
│              │  - IChatClient       │                               │
│              └──────────────────────┘                               │
│                                                                     │
├─────────────────────────────────────────────────────────────────────┤
│                       도구 (Tools)                                   │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐            │
│  │ FileFlux │  │ WebFlux  │  │   Read   │  │  Write   │            │
│  │ 문서 처리 │  │ 웹 크롤링 │  │ 파일 읽기 │  │ 파일 쓰기 │            │
│  └──────────┘  └──────────┘  └──────────┘  └──────────┘            │
└─────────────────────────────────────────────────────────────────────┘
```

## Phase 1: IndexThinking 통합 (v0.2.0)

### 목적
- 토큰 예산 관리
- 응답 잘림 감지 및 자동 재개
- 추론 과정 추출 (thinking blocks)

### 인터페이스

```csharp
// IndexThinking에서 제공하는 핵심 기능
IContextTracker      // 세션별 컨텍스트 추적
ITokenCounter        // 토큰 카운트
ITruncationDetector  // 잘림 감지
IReasoningParser     // 추론 추출
```

### 구현 방식

```csharp
// AgentLoop에 IndexThinking 래퍼 적용
services.AddSingleton<IChatClient>(sp =>
{
    var baseClient = sp.GetRequiredService<IChatClientProvider>().GetChatClient();
    return ChatClientBuilder
        .UseIndexThinking()
        .Build(sp, baseClient);
});
```

### 태스크

| ID | 태스크 | 설명 | 상태 |
|----|--------|------|------|
| IT-01 | IndexThinking 패키지 추가 | nuget 참조 | ✅ 완료 |
| IT-02 | ChatClient 래퍼 통합 | ThinkingAgentLoop 구현 | ✅ 완료 |
| IT-03 | 토큰 사용량 표시 | CLI에 토큰 정보 출력 | ⏳ 대기 |
| IT-04 | 추론 추출 옵션 | --show-thinking 플래그 | ⏳ 대기 |

## Phase 2: Memory Indexer 통합 (v0.3.0)

### 목적
- 대화 세션 관리
- 장기 기억 저장
- 컨텍스트 자동 압축

### 메모리 계층

```
┌─────────────────────────────────────────┐
│              Memory Tiers               │
├─────────────────────────────────────────┤
│  T0: Buffer    │ 현재 턴 (즉시 접근)    │
│  T1: Short     │ 현재 세션 (빠른 검색)  │
│  T2: Long      │ 사용자별 (시맨틱 검색) │
│  T3: Archive   │ 압축 저장 (희귀 접근)  │
└─────────────────────────────────────────┘
```

### 태스크

| ID | 태스크 | 설명 |
|----|--------|------|
| MI-01 | memory-indexer 패키지 추가 | nuget 참조 |
| MI-02 | IMemoryService DI 등록 | 서비스 설정 |
| MI-03 | 세션 관리 구현 | 시작/종료/재개 |
| MI-04 | 컨텍스트 압축 | 92% 임계치 자동 압축 |
| MI-05 | ~/.ironhive/memory.db | SQLite 저장소 |

## Phase 3: FileFlux 통합 (v0.4.0)

### 목적
- 문서 읽기 도구 확장
- RAG용 청크 생성
- 다양한 포맷 지원

### 지원 포맷

| 포맷 | 확장자 | 기능 |
|------|--------|------|
| PDF | .pdf | 텍스트 + 이미지 추출 |
| Word | .docx | 스타일/구조 보존 |
| Excel | .xlsx | 다중 시트, 테이블 |
| PowerPoint | .pptx | 슬라이드 + 노트 |
| Markdown | .md | 구조 보존 |
| HTML | .html | DOM 파싱 |
| 코드 | .* | 언어별 파싱 |

### 태스크

| ID | 태스크 | 설명 |
|----|--------|------|
| FF-01 | FileFlux 패키지 추가 | nuget 참조 |
| FF-02 | ReadTool 확장 | 문서 포맷 지원 |
| FF-03 | ChunkTool 구현 | 청킹 도구 |
| FF-04 | 메타데이터 추출 | 제목, 작성자 등 |

## Phase 4: FluxIndex 통합 (v0.5.0)

### 목적
- 코드베이스 인덱싱
- 시맨틱 검색
- RAG 파이프라인

### 검색 전략

```
┌─────────────────────────────────────────┐
│           Hybrid Search                 │
├─────────────────────────────────────────┤
│  Vector Search │ 의미적 유사도          │
│  BM25 Search   │ 키워드 매칭            │
│  Graph Search  │ 관계 기반 탐색         │
└─────────────────────────────────────────┘
```

### 태스크

| ID | 태스크 | 설명 |
|----|--------|------|
| FI-01 | FluxIndex 패키지 추가 | nuget 참조 |
| FI-02 | 프로젝트 인덱싱 | 초기 코드베이스 스캔 |
| FI-03 | SearchTool 구현 | 시맨틱 검색 도구 |
| FI-04 | 증분 업데이트 | 파일 변경 감지 |

## Phase 5: WebFlux 통합 (v0.6.0 - 선택적)

### 목적
- 웹 페이지 읽기
- 문서 크롤링
- API 문서 수집

### 태스크

| ID | 태스크 | 설명 |
|----|--------|------|
| WF-01 | WebFlux 패키지 추가 | nuget 참조 |
| WF-02 | FetchTool 구현 | URL 내용 가져오기 |
| WF-03 | robots.txt 준수 | 크롤링 규칙 |

## 공유 서비스 어댑터

### IEmbeddingService 브릿지

모든 iyulab 패키지는 `IEmbeddingService`를 요구합니다.
기존 `IEmbeddingProvider`와 브릿지를 만듭니다.

```csharp
// IronHive의 IEmbeddingProvider → iyulab의 IEmbeddingService
public class EmbeddingServiceAdapter : IEmbeddingService
{
    private readonly IEmbeddingProvider _provider;

    public int Dimensions => _provider.Dimensions;

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct)
    {
        return await _provider.EmbedAsync(text, ct);
    }

    public async Task<float[][]> GenerateBatchEmbeddingsAsync(
        IEnumerable<string> texts, CancellationToken ct)
    {
        return await _provider.EmbedBatchAsync(texts.ToList(), ct);
    }
}
```

### ITextCompletionService 브릿지

```csharp
// IChatClient → ITextCompletionService
public class TextCompletionServiceAdapter : ITextCompletionService
{
    private readonly IChatClient _chatClient;

    public async Task<string> CompleteAsync(string prompt, CancellationToken ct)
    {
        var response = await _chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            cancellationToken: ct);
        return response.Text ?? string.Empty;
    }
}
```

## NuGet 패키지 버전

```xml
<!-- Phase 1 -->
<PackageVersion Include="IndexThinking" Version="0.15.0" />

<!-- Phase 2 -->
<PackageVersion Include="MemoryIndexer" Version="0.13.0" />

<!-- Phase 3 -->
<PackageVersion Include="FileFlux" Version="0.9.8" />

<!-- Phase 4 -->
<PackageVersion Include="FluxIndex" Version="0.5.8" />

<!-- Phase 5 (선택적) -->
<PackageVersion Include="WebFlux" Version="0.1.9" />
```

## 의존성 그래프

```
IndexThinking (0.15.0)
├── Microsoft.Extensions.AI
├── Microsoft.ML.Tokenizers
└── Microsoft.Data.Sqlite

MemoryIndexer (0.13.0)
├── Microsoft.Data.Sqlite
├── ModelContextProtocol
└── OpenTelemetry

FileFlux (0.9.8)
├── NTextCat
├── FluxCurator (선택적)
└── FluxImprover (선택적)

FluxIndex (0.5.8)
├── FileFlux
├── WebFlux
├── Microsoft.Extensions.Caching
└── 저장소 (SQLite/PostgreSQL/Qdrant)

WebFlux (0.1.9)
├── Microsoft.Playwright
├── HtmlAgilityPack
├── Polly
└── Markdig
```

## 마일스톤

| 버전 | 패키지 | 주요 기능 |
|------|--------|-----------|
| v0.2.0 | IndexThinking | 토큰 관리, 추론 추출 |
| v0.3.0 | memory-indexer | 세션 메모리, 장기 기억 |
| v0.4.0 | FileFlux | 문서 처리 도구 |
| v0.5.0 | FluxIndex | 시맨틱 검색 |
| v0.6.0 | WebFlux | 웹 크롤링 (선택적) |
