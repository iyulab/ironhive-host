# 라이브러리 사용 vs 구현 결정 표

## 요약

| 기능 | 결정 | 이유 |
|------|------|------|
| token-counter | ✅ **사용** | Microsoft.ML.Tokenizers (공식, 고성능) |
| diff-formatter | ✅ **사용** | DiffPlex (검증됨, 1.9M 다운로드) |
| glob-matcher | ✅ **사용** | Microsoft.Extensions.FileSystemGlobbing (공식) |
| config-merger | ✅ **사용** | Microsoft.Extensions.Configuration (공식) |
| cost-calculator | 📦 **서브모듈** | [iyulab/TokenMeter](https://github.com/iyulab/TokenMeter) |
| tool-call-parser | 📦 **서브모듈** | [iyulab/ToolCallParser](https://github.com/iyulab/ToolCallParser) |
| tree-indexer | ⏸️ **보류** | C# 바인딩 미성숙, Future로 이동 권장 |

### 서브모듈

```
submodules/
├── TokenMeter/       # 토큰 카운팅 + 비용 계산
└── ToolCallParser/   # 멀티 프로바이더 tool_call 파싱
```

---

## 상세 분석

### 1. token-counter ✅ 사용

**선택: `Microsoft.ML.Tokenizers`**

| 라이브러리 | Stars/다운로드 | 특징 | 추천 |
|-----------|---------------|------|------|
| **Microsoft.ML.Tokenizers** | .NET 공식 | .NET 9+ 공식, 최고 성능, 멀티 모델 | ⭐⭐⭐ |
| Tiktoken (tryAGI) | NuGet 인기 | gpt-4o, o200k 지원, 빠름 | ⭐⭐ |
| SharpToken | NuGet 인기 | 메모리 효율, tiktoken 포트 | ⭐⭐ |
| TiktokenSharp | 1.2M | OpenAI Rust 참조 구현 | ⭐ |

```csharp
// Microsoft.ML.Tokenizers 사용 예시
using Microsoft.ML.Tokenizers;

var tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");
int count = tokenizer.CountTokens("Hello world");
```

**이유**: Microsoft 공식, 최고 성능, 지속적 업데이트 보장

---

### 2. diff-formatter ✅ 사용

**선택: `DiffPlex`**

| 라이브러리 | 다운로드 | 특징 | 추천 |
|-----------|---------|------|------|
| **DiffPlex** | 1.9M+ | unified diff, side-by-side, three-way merge | ⭐⭐⭐ |
| TextDiff.Sharp | 신규 | diff 적용 특화 | ⭐ |
| CSharpDiff | 저조 | jsdiff 포트 | ⭐ |

```csharp
// DiffPlex 사용 예시
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.Renderer;

var differ = new Differ();
var diffResult = InlineDiffBuilder.Diff(oldText, newText);

// Unified diff 출력
var unifiedDiff = UnidiffRenderer.Render(oldText, newText);
```

**이유**: 검증됨, unified diff + ANSI 렌더링 모두 지원, 활발한 유지보수

---

### 3. glob-matcher ✅ 사용

**선택: `Microsoft.Extensions.FileSystemGlobbing`**

| 라이브러리 | 다운로드 | 특징 | 추천 |
|-----------|---------|------|------|
| **MS.Ext.FileSystemGlobbing** | 공식 | Microsoft 공식, ** 지원, include/exclude | ⭐⭐⭐ |
| DotNet.Glob | 12M+ | 가장 빠름, Regex 미사용 | ⭐⭐ |
| MAB.DotIgnore | 181K | .gitignore 스타일 전용 | ⭐⭐ |
| Glob (kthompson) | 인기 | 간단한 API | ⭐ |

```csharp
// Microsoft.Extensions.FileSystemGlobbing 사용 예시
using Microsoft.Extensions.FileSystemGlobbing;

var matcher = new Matcher();
matcher.AddInclude("**/*.cs");
matcher.AddExclude("**/bin/**");
matcher.AddExclude("**/obj/**");

var results = matcher.GetResultsInFullPath("./src");
```

**이유**: Microsoft 공식, 충분한 기능, 생태계 통합

**보조 옵션**: gitignore 파싱 필요시 `MAB.DotIgnore` 추가

---

### 4. config-merger ✅ 사용

**선택: `Microsoft.Extensions.Configuration` + `NetEscapades.Configuration.Yaml`**

| 라이브러리 | 다운로드 | 특징 | 추천 |
|-----------|---------|------|------|
| **MS.Ext.Configuration** | 공식 | 표준, 계층적 병합, 환경변수 | ⭐⭐⭐ |
| NetEscapades.Configuration.Yaml | 1M+ | YAML 프로바이더 | ⭐⭐⭐ |
| Config.Net | 인기 | 인터페이스 기반, 강타입 | ⭐⭐ |

```csharp
// Microsoft.Extensions.Configuration 사용 예시
var config = new ConfigurationBuilder()
    .AddYamlFile("~/.ironhive/config.yaml", optional: true)     // 전역
    .AddYamlFile(".ironhive/config.yaml", optional: true)       // 프로젝트
    .AddYamlFile(".ironhive/config.local.yaml", optional: true) // 로컬
    .AddEnvironmentVariables("IRONHIVE_")
    .Build();
```

**이유**: .NET 표준, 계층적 병합 내장, YAML 확장 쉬움

---

### 5. cost-calculator 📦 서브모듈 (TokenMeter)

**서브모듈**: [`submodules/TokenMeter`](https://github.com/iyulab/TokenMeter)

**기능**:
- 토큰 카운팅 (Microsoft.ML.Tokenizers 래핑)
- 비용 계산 (프로바이더별 가격 지원)
- 가격 데이터 외부 관리

**사용법**:
```csharp
// TokenMeter 사용 예시
var meter = new TokenMeter();
var usage = meter.Count("Hello world", "gpt-4o");
var cost = meter.CalculateCost(usage.InputTokens, usage.OutputTokens, "gpt-4o");
```

**개발 계획**: ironhive-cli와 함께 점진적 구현 및 NuGet 배포

---

### 6. tool-call-parser 📦 서브모듈 (ToolCallParser)

**서브모듈**: [`submodules/ToolCallParser`](https://github.com/iyulab/ToolCallParser)

**기능**:
- 멀티 프로바이더 tool_call 파싱 (OpenAI, Anthropic, Google, XML)
- 스트리밍 중 partial tool call 처리
- ironhive 연동

**사용법**:
```csharp
// ToolCallParser 사용 예시
var parser = new ToolCallParser();
var toolCalls = parser.Parse(response, ToolFormat.Auto);

// 스트리밍
await foreach (var toolCall in parser.ParseStreamingAsync(chunks))
{
    // ...
}
```

**개발 계획**: ironhive-cli와 함께 점진적 구현 및 NuGet 배포

---

### 7. tree-indexer ⏸️ 보류 (Future)

**이유**:
- C# tree-sitter 바인딩 미성숙 (실험적)
- 코딩 에이전트 전용 기능 (범용 CLI 우선)
- 대안: Roslyn (C# 전용), esprima-dotnet (JS 전용)

**현재 옵션**:
| 라이브러리 | 특징 | 문제점 |
|-----------|------|--------|
| tree-sitter/csharp-tree-sitter | 공식 C# 바인딩 | 실험적, 문서 부족 |
| Roslyn | C# AST | C# 전용 |
| esprima-dotnet | JS AST | JS/TS 전용 |

**권장**: Phase 4+ 또는 별도 MCP 서버로 분리

---

## 최종 의존성 목록

### NuGet 패키지 (사용)
```xml
<!-- 토큰 카운팅 -->
<PackageReference Include="Microsoft.ML.Tokenizers" Version="1.0.0" />

<!-- Diff -->
<PackageReference Include="DiffPlex" Version="1.9.0" />

<!-- Glob -->
<PackageReference Include="Microsoft.Extensions.FileSystemGlobbing" Version="10.0.0" />

<!-- Config -->
<PackageReference Include="Microsoft.Extensions.Configuration" Version="10.0.0" />
<PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.0.0" />
<PackageReference Include="NetEscapades.Configuration.Yaml" Version="3.1.0" />

<!-- gitignore (선택) -->
<PackageReference Include="MAB.DotIgnore" Version="3.0.2" />
```

### 서브모듈 (iyulab 오픈소스)
```
ironhive-cli/
├── submodules/
│   ├── TokenMeter/              # 토큰 카운팅 + 비용 계산
│   │   └── (github.com/iyulab/TokenMeter)
│   │
│   └── ToolCallParser/          # tool_call 파싱
│       └── (github.com/iyulab/ToolCallParser)
```

**서브모듈 초기화**:
```bash
git submodule update --init --recursive
```

---

## 비용 절감 효과

| 항목 | 직접 구현 시 | 라이브러리 사용 시 | 절감 |
|------|-------------|------------------|------|
| token-counter | 2주 | 0 | 2주 |
| diff-formatter | 1주 | 0 | 1주 |
| glob-matcher | 3일 | 0 | 3일 |
| config-merger | 1주 | 0 | 1주 |
| **합계** | ~5주 | ~0 | **~5주** |

→ **5주 개발 시간 절감**, 검증된 코드 품질 확보