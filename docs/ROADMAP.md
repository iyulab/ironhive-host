# ironhive-cli 개발 로드맵

> 범용 CLI 에이전트 코어 개발 계획

## 기술 스택 (확정)

| 항목 | 선택 | 비고 |
|------|------|------|
| **언어** | C# | - |
| **런타임** | .NET 10 | LTS, AOT 지원 |
| **에이전트 프레임워크** | Microsoft Agent Framework | Microsoft.Extensions.AI 기반 |
| **CLI 라이브러리** | System.CommandLine / Spectre.Console | - |
| **UI** | 터미널 전용 | 웹 UI 없음 |

---

## 라이브러리 결정 현황

**"직접 구현보다 검증된 오픈소스 우선"** → 📄 [dev-docs/ref.md](../dev-docs/ref.md)

### 확정된 결정

| 기능 | 결정 | 선택 | 참조 |
|------|------|------|------|
| token-counter | ✅ 사용 | `Microsoft.ML.Tokenizers` | [ref.md#1](../dev-docs/ref.md) |
| diff-formatter | ✅ 사용 | `DiffPlex` | [ref.md#2](../dev-docs/ref.md) |
| glob-matcher | ✅ 사용 | `Microsoft.Extensions.FileSystemGlobbing` | [ref.md#3](../dev-docs/ref.md) |
| config-merger | ✅ 사용 | `Microsoft.Extensions.Configuration` + YAML | [ref.md#4](../dev-docs/ref.md) |
| cost-calculator | 📦 서브모듈 | [iyulab/TokenMeter](https://github.com/iyulab/TokenMeter) | [ref.md#5](../dev-docs/ref.md) |
| tool-call-parser | 📦 서브모듈 | [iyulab/ToolCallParser](https://github.com/iyulab/ToolCallParser) | [ref.md#6](../dev-docs/ref.md) |
| tree-indexer | ⏸️ 보류 | Future로 이동 (C# 바인딩 미성숙) | [ref.md#7](../dev-docs/ref.md) |

### 서브모듈 (병행 개발)

```
submodules/
├── TokenMeter/       # 토큰 카운팅 + 비용 계산 → NuGet 배포 예정
└── ToolCallParser/   # 멀티 프로바이더 tool_call 파싱 → NuGet 배포 예정
```

#### 서브모듈 활용 계획

| 서브모듈 | 용도 | 통합 Phase | 통합 방식 |
|----------|------|------------|-----------|
| **TokenMeter** | 토큰 카운팅, 비용 계산, 사용량 추적 | P1-10 | 프로젝트 참조 → NuGet 전환 |
| **ToolCallParser** | 멀티 프로바이더 tool_call 파싱 | P1-02 | 프로젝트 참조 → NuGet 전환 |

**TokenMeter 활용**:
- `ITokenCounter`: 실시간 토큰 카운팅
- `ICostCalculator`: 모델별 비용 계산
- `IUsageTracker`: 세션/일/월 사용량 추적
- Phase 1-10 (진행률 표시)에서 통합

**ToolCallParser 활용**:
- `IToolCallParser`: OpenAI/Anthropic/Azure 등 멀티 프로바이더 tool_call 파싱
- `IToolCallNormalizer`: 표준 형식으로 정규화
- Phase 1-02 (Agent 기본 루프)에서 통합 예정

**개발 순서**:
1. 서브모듈 초기 구조 개발 (별도 진행)
2. ironhive-cli에서 프로젝트 참조로 통합
3. 안정화 후 NuGet 패키지로 배포
4. 프로젝트 참조 → NuGet 참조로 전환

### 조사 필요

| 영역 | 조사 항목 | 후보 |
|------|----------|------|
| MCP 클라이언트 | .NET용 MCP SDK | mcpdotnet, 공식 SDK |
| 상태 머신 | FSM 라이브러리 | Stateless, Automatonymous |

---

## 테스트 전략

**"LLM 의존성을 격리하여 결정론적 테스트 보장"**

### 테스트 레벨

| 레벨 | 목적 | 도구 |
|------|------|------|
| **Unit** | 개별 컴포넌트 검증 | xUnit, NSubstitute |
| **Integration** | 컴포넌트 간 상호작용 | TestContainers |
| **E2E** | 전체 시나리오 검증 | 실제 CLI 실행 |
| **Simulation** | LLM 응답 시뮬레이션 | Mock IChatClient |

### LLM 시뮬레이션 전략

```
┌─────────────────────────────────────────┐
│           Production Mode               │
│   IChatClient → OpenAI/Azure/GPUStack   │
└─────────────────────────────────────────┘

┌─────────────────────────────────────────┐
│             Test Mode                   │
│   IChatClient → MockChatClient          │
│   - 사전 정의된 응답 시퀀스             │
│   - 도구 호출 시나리오 재현             │
│   - 에러/타임아웃 시뮬레이션            │
└─────────────────────────────────────────┘
```

---

## 참조 문서

| 문서 | 경로 | 내용 |
|------|------|------|
| **연구 보고서 1** | [research-01.md](./research/research-01.md) | 주요 에이전트 아키텍처 분석 (Claude Code, OpenHands, Goose, SWE-agent) |
| **연구 보고서 2** | [research-02.md](./research/research-02.md) | 심층 분석 (FSM, 컨텍스트 관리, MCP) |
| **GitHub 레퍼런스** | [coding-agent-github-ref.md](./coding-agent-github-ref.md) | 오픈소스 코딩 에이전트 저장소 목록 |
| **라이브러리 결정** | [ref.md](../dev-docs/ref.md) | 라이브러리 사용 vs 구현 결정 |

---

## 버전 정책

- 현재: **0.x.x** 단계 (pre-release)
- 마이너 버전(0.X.0): 새 기능 추가, 하위 호환 유지
- 패치 버전(0.0.X): 버그 수정, 문서 수정, 소규모 개선
- 메이저 버전 변경은 커뮤니티/팀 승인 후 수동 작업으로만 진행

---

## Phase 0: 프로젝트 초기화 (v0.1.0)

### 목표
프로젝트 기반 구조 설정 및 개발 환경 구축

### 태스크

| ID | 태스크 | 설명 | 의존성 | 참조 |
|----|--------|------|--------|------|
| P0-01 | ~~기술 스택 결정~~ | ✅ C# / .NET 10 / Microsoft Agent Framework 확정 | - | - |
| P0-02 | 🔍 .NET AI 생태계 조사 | Microsoft.Extensions.AI, Semantic Kernel, AutoGen.NET 비교 | P0-01 | [research-01](./research/research-01.md) |
| P0-03 | 🔍 CLI 라이브러리 조사 | System.CommandLine vs Spectre.Console.Cli 비교 | P0-01 | - |
| P0-04 | 솔루션 구조 생성 | `dotnet new sln`, 프로젝트 분리 (Core, CLI, Tests) | P0-02, P0-03 | - |
| P0-05 | 빌드 파이프라인 구성 | GitHub Actions, dotnet format, xUnit/NUnit | P0-04 | - |
| P0-06 | Microsoft.Extensions.AI 연동 | IChatClient, IEmbeddingGenerator 통합 | P0-04 | [research-01#1.1](./research/research-01.md) |
| P0-07 | 기본 CLI 스켈레톤 | 조사 결과 기반 CLI 구현 | P0-04 | - |
| P0-08 | Native AOT 설정 | 단일 실행 파일 배포, AOT 호환성 검증 | P0-04 | - |
| P0-09 | 🧪 테스트 인프라 구축 | xUnit, NSubstitute, Coverlet 설정 | P0-05 | - |
| P0-10 | 🧪 MockChatClient 구현 | LLM 시뮬레이션용 모의 클라이언트 | P0-06, P0-09 | [research-01#5.2](./research/research-01.md) |

### 산출물
- **조사 보고서**: `docs/research/dotnet-ecosystem.md`
- 빌드 가능한 .NET 10 솔루션 구조
- `ironhive --version`, `ironhive --help` 동작
- Native AOT 빌드 지원
- **테스트 인프라 및 MockChatClient 기반**

---

## Phase 1: 기본 에이전트 루프 (v0.2.0)

### 목표
"nO" 스타일 단일 스레드 마스터 루프 구현

### 아키텍처
```
User Input → Agent Loop → LLM → Tool Call → Result → LLM → ... → Response
```

### 태스크

| ID | 태스크 | 설명 | 의존성 | 참조 |
|----|--------|------|--------|------|
| P1-01 | ~~대화 세션 관리~~ | ✅ ChatHistory, 세션 생성/종료 | P0-06 | [research-01#1.1](./research/research-01.md) |
| P1-02 | ~~Agent 기본 루프~~ | ✅ "nO" 스타일 단일 스레드 마스터 루프 | P1-01 | [research-01#1.1](./research/research-01.md), [research-02#2.1](./research/research-02.md) |
| P1-03 | ~~gpustack 연동 검증~~ | ✅ gpustack/로컬 모델 연동 테스트 | P1-01 | - |
| P1-04 | ~~AIFunction 도구 시스템~~ | ✅ AIFunctionFactory 기반 도구 등록/호출 | P1-02 | [research-01#4](./research/research-01.md) |
| P1-05 | ~~내장 도구: Read~~ | ✅ 파일 읽기 도구 | P1-04 | [research-01#1.4](./research/research-01.md) |
| P1-06 | ~~내장 도구: Write~~ | ✅ 파일 쓰기 + **DiffPlex 컬러 diff** | P1-04 | [ref.md#2](../dev-docs/ref.md) |
| P1-07 | ~~내장 도구: Shell~~ | ✅ 명령 실행 (Process 기반, 샌드박싱) | P1-04 | [research-02#6.1](./research/research-02.md) |
| P1-08 | ~~내장 도구: Glob/Grep~~ | ✅ **MS.Ext.FileSystemGlobbing** 사용 | P1-04 | [ref.md#3](../dev-docs/ref.md) |
| P1-09 | ~~스트리밍 출력~~ | ✅ IAsyncEnumerable + Console.Write 실시간 출력 | P1-02 | - |
| P1-10 | ~~진행률 표시~~ | ✅ UsageTracker로 토큰 사용량, 예상 비용 표시 (7 tests) | P1-09 | - |
| P1-11 | ~~인터럽트 처리~~ | ✅ Ctrl+C graceful shutdown, per-request cancellation | P1-02 | [research-01#2.2](./research/research-01.md) |
| P1-12 | ~~단일 명령 모드~~ | ✅ `ironhive -p "prompt"` 지원 | P1-02 | - |
| P1-13 | ~~🧪 에이전트 루프 단위 테스트~~ | ✅ MockChatClient로 도구 호출/스트리밍 검증 (14 tests) | P0-10, P1-02 | - |
| P1-14 | ~~🧪 도구 실행 테스트~~ | ✅ 각 내장 도구의 정상/에러 케이스 (11 tests) | P1-05~P1-08 | - |
| P1-15 | ~~🧪 시뮬레이션 시나리오~~ | ✅ 멀티턴 대화, 에러 복구, 도구 체인 테스트 (11 tests) | P1-13, P1-14 | - |
| P1-16 | ~~📦 TokenMeter 서브모듈 개발~~ | ✅ ITokenCounter, ICostCalculator, IUsageTracker 구현 (32 tests) | - | [TokenMeter](https://github.com/iyulab/TokenMeter) |
| P1-17 | ~~📦 ToolCallParser 서브모듈 개발~~ | ✅ 멀티 프로바이더 tool_call 파싱, 정규화 (51 tests) | - | [ToolCallParser](https://github.com/iyulab/ToolCallParser) |
| P1-18 | ~~📦 서브모듈 통합~~ | ✅ TokenMeter, ToolCallParser를 ironhive-cli에 프로젝트 참조로 통합 | P1-16, P1-17 | - |

### 산출물
- ✅ 기본적인 대화형 에이전트 동작
- ✅ 파일 읽기/쓰기/검색, 명령 실행 가능
- ✅ **gpustack/로컬 모델 연동 확인**
- ✅ **ironbees 멀티에이전트 통합** (선행 완료)
- ⏳ 파일 변경 시 컬러 diff 표시
- ✅ 에이전트 루프 테스트 커버리지 80%+ (40 tests)
- ⏳ **TokenMeter/ToolCallParser 서브모듈 통합**

### 진행 상황
- **Phase 1 완료**: P1-01~P1-18 (18/18 태스크, 100%) ✅
- **테스트 현황**: 전체 155개 (ironhive-cli) + 32개 (TokenMeter) + 51개 (ToolCallParser) = **238개 테스트 통과**
  - AgentLoopTests: 14 tests (도구 호출, 스트리밍, 취소 등)
  - AgentLoopScenarioTests: 11 tests (멀티턴, 에러 복구, 장기 대화)
  - BuiltInToolsTests: 11 tests (Read, Write, Shell, Glob, Grep)
  - ChatClientFrameworkAdapterTests: 4 tests (Ironbees 통합)
  - UsageTrackerTests: 7 tests (세션 토큰 추적)
  - **TokenMeter 서브모듈**: 32 tests (TokenCounter, CostCalculator, UsageTracker)
  - **ToolCallParser 서브모듈**: 51 tests (OpenAI, Anthropic 파서)
- **서브모듈 현황**:
  - ✅ P1-16: TokenMeter 완료 (net8.0/net9.0 멀티타겟, Microsoft.ML.Tokenizers 기반)
  - ✅ P1-17: ToolCallParser 완료 (net8.0/net9.0 멀티타겟, 멀티 프로바이더 지원)
  - ✅ P1-18: 서브모듈 ironhive-cli 통합 완료
- **Phase 2로 진행 가능**

---

## Phase 2: 모드 시스템 및 HITL (v0.3.0)

### 목표
Plan/Work/HITL 모드 전환 및 안전 메커니즘 구현

### 상태 머신
```
IDLE → PLANNING ──┬── PLAN_MODE (read-only)
                  ├── WORK_MODE (execution)
                  └── HITL_MODE (user approval)
```

### 태스크

| ID | 태스크 | 설명 | 의존성 | 참조 |
|----|--------|------|--------|------|
| P2-01 | ~~모드 라우터 설계~~ | ✅ FSM 기반 ModeManager, ModeToolFilter (32 tests) | P1-02 | - |
| P2-02 | ~~Plan-mode 구현~~ | ✅ 읽기 전용 탐색, 쓰기 도구 비활성화 | P2-01 | - |
| P2-03 | ~~Work-mode 구현~~ | ✅ 전체 도구 활성화 | P2-01 | - |
| P2-04 | ~~HITL 트리거 정의~~ | ✅ RiskAssessment, 위험 쉘 명령 감지 | P2-01 | - |
| P2-05 | ~~HITL 승인 UI~~ | ✅ ConsoleApprovalService (Spectre.Console) | P2-04 | - |
| P2-06 | ~~권한 화이트리스트~~ | ✅ ApprovalConfig, 도구/명령/경로 패턴 매칭 | P2-05 | [ref.md#4](../dev-docs/ref.md) |
| P2-07 | ~~`--plan` 플래그~~ | ✅ CLI에서 Plan-mode 진입 | P2-02 | - |
| P2-08 | ~~`--dry-run` 플래그~~ | ✅ 실제 실행 없이 계획만 출력 | P2-02 | - |
| P2-09 | ~~TodoWrite 도구~~ | ✅ TodoTool, JSON 기반 작업 관리 (.ironhive/todo.json) | P1-04 | [research-01#1.1](./research/research-01.md) |
| P2-10 | ~~재계획 메커니즘~~ | ✅ ReplanningService, 실패 추적, ReplanRequested 트리거 | P2-09 | [research-01#2.3](./research/research-01.md), [research-02#3.3](./research/research-02.md) |
| P2-11 | ~~🔍 FSM 라이브러리 조사~~ | ✅ Stateless, Automatonymous 비교 → 직접 구현 결정 | P2-01 | - |
| P2-12 | ~~🧪 모드 전환 테스트~~ | ✅ Plan→Work→HITL 상태 전이 검증 (9 tests) | P2-01~P2-03 | - |
| P2-13 | ~~🧪 HITL 시나리오 테스트~~ | ✅ 위험 작업 감지 및 승인 흐름 (12 tests) | P2-04, P2-05 | - |
| P2-14 | ~~🧪 재계획 시뮬레이션~~ | ✅ 실패→롤백→재시도 시나리오 검증 (8 tests) | P2-10 | - |

### 조사 산출물
- **FSM 라이브러리 비교표**: 직접 구현으로 결정 (Stateless 불필요)

### 산출물
- ✅ `ironhive --plan "..."` 동작
- ✅ 위험 작업 시 사용자 승인 요청
- ✅ 작업 목록 기반 진행 상황 추적 (TodoTool)
- ✅ **모드 전환 테스트 커버리지** (183 tests)
- ✅ **권한 화이트리스트** (YAML/JSON 설정 파일)
- ✅ **재계획 메커니즘** (실패 추적 + 자동 롤백)
- ✅ **통합 테스트** (복합 워크플로우, HITL 시나리오, 재계획 시뮬레이션)

### 진행 상황
- **Phase 2 완료**: P2-01~P2-14 (14/14 태스크, 100%) ✅
- **테스트 현황**: 183개 테스트 통과
  - ModeManagerTests: 17 tests
  - ModeToolFilterTests: 45 tests (화이트리스트 포함)
  - HumanApprovalTests: 8 tests
  - TodoToolTests: 20 tests
  - ReplanningServiceTests: 17 tests
  - **ModeTransitionIntegrationTests**: 9 tests (복합 워크플로우)
  - **HITLScenarioTests**: 12 tests (위험 감지 및 승인)
  - **ReplanningSimulationTests**: 8 tests (실패 추적 및 재계획)
- **Phase 3으로 진행 가능**

---

## Phase 3: MCP 플러그인 시스템 (v0.4.0)

### 목표
MCP(Model Context Protocol) 기반 도구 확장 시스템

### 아키텍처
```
ironhive-cli ←─ MCP ─→ memory-indexer
                   ─→ code-beaker
                   ─→ custom plugins
```

### 태스크

| ID | 태스크 | 설명 | 의존성 | 참조 |
|----|--------|------|--------|------|
| P3-01 | ~~🔍 MCP .NET SDK 조사~~ | ✅ 공식 C# SDK 선택 (ModelContextProtocol 0.6.0) | P1-04 | [mcp-sdk-comparison](./research/mcp-sdk-comparison.md) |
| P3-02 | ~~🔍 MCP 스펙 분석~~ | ✅ 2025-06-18 스펙 정리 완료 | P3-01 | [mcp-spec-summary](./research/mcp-spec-summary.md) |
| P3-03 | ~~MCP 클라이언트 구현/통합~~ | ✅ McpPluginManager, StdioClientTransport | P3-01, P3-02 | - |
| P3-04 | ~~도구 동적 로딩~~ | ✅ ListToolsAsync → AITool 변환 | P3-03 | - |
| P3-05 | ~~도구 호출 라우팅~~ | ✅ CallToolAsync → MCP 서버 전달 | P3-04 | - |
| P3-06 | ~~플러그인 설정 파일~~ | ✅ YAML/JSON 로딩 (McpPluginsConfigLoader) | P3-03 | - |
| P3-07 | ~~플러그인 핫 리로드~~ | ✅ McpPluginHotReloader (FileSystemWatcher, 런타임 연결/해제) | P3-04 | [research-02#4.2](./research/research-02.md) |
| P3-08 | ~~계층적 도구 발견~~ | ✅ McpToolDiscovery (메타 도구, 지연 로딩) | P3-04 | [research-02#4.1](./research/research-02.md) |
| P3-09 | ~~memory-indexer 통합~~ | ✅ SDK 방식 통합 (MemoryIndexerTools, IMemoryToolsProvider) | P3-03 | [github-ref](./coding-agent-github-ref.md) |
| P3-10 | ~~code-beaker 통합~~ | ✅ SDK 방식 통합 (CodeBeakerTools, ICodeExecutionProvider) | P3-03 | [github-ref](./coding-agent-github-ref.md) |
| P3-11 | ~~🧪 McpPluginManager 테스트~~ | ✅ 단위 테스트 26개 | P3-03 | - |
| P3-12 | 🧪 MCP 통합 테스트 | 도구 발견/호출/결과 흐름 검증 (실제 서버 필요) | P3-11, P3-04, P3-05 | - |
| P3-13 | ~~🧪 플러그인 로드/언로드 테스트~~ | ✅ HotReloader, ToolDiscovery 테스트 (33개) | P3-07, P3-11 | - |

### 조사 산출물
- **MCP SDK 비교표**: `docs/research/mcp-sdk-comparison.md`
- **MCP 스펙 요약**: `docs/research/mcp-spec-summary.md`

### 산출물
- ✅ MCP 서버 자동 연결/해제 (McpPluginManager)
- ✅ 외부 플러그인으로 도구 확장 가능
- ✅ 플러그인 설정 파일 (YAML/JSON)
- ✅ 핫 리로드 (McpPluginHotReloader)
- ✅ 계층적 도구 발견 (McpToolDiscovery)
- ✅ memory-indexer SDK 통합 (MemoryIndexerTools)
- ✅ code-beaker SDK 통합 (CodeBeakerTools)

### 진행 상황
- **완료**: P3-01~P3-11, P3-13 (12/13 태스크, 92%)
- **테스트 현황**: 299개 테스트 통과
  - McpPluginManagerTests: 20 tests
  - McpPluginsConfigLoaderTests: 6 tests
  - McpPluginHotReloaderTests: 15 tests
  - McpToolDiscoveryTests: 15 tests
  - MemoryIndexerToolsTests: 18 tests
  - CodeBeakerToolsTests: 24 tests
  - **McpIntegrationTests: 21 tests** (통합 테스트 추가)
- **검증 리포트**: [phase3-verification-report.md](./reports/phase3-verification-report.md)
- **남은 작업**:
  - P3-12: 실제 MCP 서버 E2E 테스트 (E2E 단계로 이동)

---

## Phase 3.5: Claude Code 호환성 - 세션 관리 (v0.4.1)

### 목표
Claude Code 호환 세션 관리 시스템 구현

### 태스크

| ID | 태스크 | 설명 | 의존성 | 참조 |
|----|--------|------|--------|------|
| P3.5-01 | ~~JSONL 트랜스크립트~~ | ✅ 세션별 JSONL 저장/로드 | P1-01 | [claude-code-compatibility](./design/claude-code-compatibility.md) |
| P3.5-02 | ~~ISessionManager 인터페이스~~ | ✅ 세션 CRUD 및 컨텍스트 복원 | P3.5-01 | - |
| P3.5-03 | ~~`--continue` 플래그~~ | ✅ 최근 세션 계속 | P3.5-02 | - |
| P3.5-04 | ~~`--resume` 플래그~~ | ✅ 특정 세션 재개 | P3.5-02 | - |
| P3.5-05 | ~~sessions 명령~~ | ✅ 세션 목록/삭제 관리 | P3.5-02 | - |
| P3.5-06 | ~~🧪 SessionManager 테스트~~ | ✅ 21개 테스트 통과 | P3.5-02 | - |
| P3.5-07 | ~~세션 포크~~ | ✅ `--fork` 플래그로 기존 세션 분기 | P3.5-02 | - |
| P3.5-08 | ~~컨텍스트 주입~~ | ✅ IAgentLoop.InitializeHistory() 구현 | P3.5-02 | - |

### 산출물
- ✅ JSONL 트랜스크립트 저장/로드
- ✅ `ironhive -c` / `ironhive -r <id>` 동작
- ✅ `ironhive sessions` 명령
- ✅ `ironhive -r <id> --fork` 세션 포크
- ✅ `IAgentLoop.InitializeHistory()` 컨텍스트 주입
- ✅ 338개 테스트 통과 (세션 21개 + InitializeHistory 4개)

### 진행 상황
- **Phase 3.5 완료**: P3.5-01~P3.5-08 (8/8 태스크, 100%) ✅
- **테스트 현황**: 338개 테스트 통과
- **Phase 4로 진행 가능**

---

## Phase 4: 컨텍스트 관리 고도화 (v0.5.0)

### 목표
토큰 효율성 및 장기 메모리 관리

### 태스크

| ID | 태스크 | 설명 | 의존성 | 참조 |
|----|--------|------|--------|------|
| P4-01 | ~~🔍 토크나이저 조사~~ | ✅ **Microsoft.ML.Tokenizers** 확정 | P1-01 | [ref.md#1](../dev-docs/ref.md) |
| P4-02 | 토큰 카운터 | Microsoft.ML.Tokenizers 기반 실시간 추적 | P4-01 | [ref.md#1](../dev-docs/ref.md) |
| P4-03 | Compaction 트리거 | 92% 임계치 도달 시 압축 시작 | P4-02 | [research-01#3.1](./research/research-01.md) |
| P4-04 | 히스토리 압축기 | Head/Middle/Tail 분리, Middle 요약 | P4-03 | [research-01#3.1](./research/research-01.md), [research-02#5.2](./research/research-02.md) |
| P4-05 | 목표 상기 주입 | 매 턴 목표를 컨텍스트 끝에 추가 | P4-04 | [research-01#2.3](./research/research-01.md) |
| P4-06 | 프롬프트 캐싱 | Anthropic prefix caching 등 활용 | P4-04 | [research-01#3.2](./research/research-01.md) |
| P4-07 | 장기 메모리 저장 | 세션 간 유지되는 프로젝트 메모리 | P3-09 | [research-01#3.2](./research/research-01.md), [research-02#5.3](./research/research-02.md) |
| P4-08 | 장기 메모리 검색 | 관련 메모리 자동 로드 | P4-07 | [research-02#5.3](./research/research-02.md) |
| P4-09 | 🧪 토큰 카운팅 정확도 테스트 | 실제 모델 토큰과 비교 검증 | P4-02 | - |
| P4-10 | 🧪 압축 품질 테스트 | 압축 전후 정보 손실 측정 | P4-04 | - |
| P4-11 | 🧪 장기 세션 시뮬레이션 | 100+ 턴 대화에서 컨텍스트 관리 검증 | P4-04, P4-05 | - |

### 참고
- 토크나이저 결정 완료: `Microsoft.ML.Tokenizers` (공식, 최고 성능)

### 산출물
- 장시간 작업 시 컨텍스트 자동 압축
- 세션 간 학습 내용 유지
- **프롬프트 캐싱으로 비용/지연 최적화**
- **장기 세션 안정성 검증 완료**

---

## Phase 5a: 안정화 (v0.6.0)

### 목표
프로덕션 준비 및 핵심 기능 안정화

### 태스크

| ID | 태스크 | 설명 | 의존성 | 참조 |
|----|--------|------|--------|------|
| P5a-01 | 웹훅 시스템 | 이벤트 발생 시 외부 시스템 알림 | P2-01 | - |
| P5a-02 | 설정 파일 체계 | **MS.Ext.Configuration** 계층적 병합 | P3-06 | [ref.md#4](../dev-docs/ref.md) |
| P5a-03 | CLAUDE.md 지원 | 프로젝트별 지시사항 자동 로드 | P5a-02 | [github-ref](./coding-agent-github-ref.md) |
| P5a-04 | 에러 복구 고도화 | 반복 탐지, 자동 에스컬레이션 | P2-10 | [research-01#5.2](./research/research-01.md), [research-02#6.2](./research/research-02.md) |
| P5a-05 | 비용/토큰 제한 | 세션당 한도 설정 및 경고 | P4-02 | [ref.md#5](../dev-docs/ref.md) |
| P5a-06 | 크로스 플랫폼 테스트 | Windows, macOS, Linux 검증 | P0-05 | - |
| P5a-07 | 🧪 E2E 테스트 스위트 | 실제 CLI 실행 기반 전체 시나리오 | P5a-06 | - |
| P5a-08 | 🧪 성능 벤치마크 | 응답 지연, 메모리 사용량 측정 | P5a-07 | - |

### 산출물
- 안정적인 v0.6.0 릴리스
- 웹훅, 설정 체계, 에러 복구 완비
- **E2E 테스트 스위트 (20+ 시나리오)**
- **성능 벤치마크 베이스라인**

---

## Phase 5b: 생태계 통합 (v0.7.0)

### 목표
iyulab 생태계 완전 통합 및 릴리스 자동화

### 태스크

| ID | 태스크 | 설명 | 의존성 | 참조 |
|----|--------|------|--------|------|
| P5b-01 | ~~ironbees 통합~~ | ✅ 멀티에이전트 관리 연동 (선행 완료) | P3-03 | [github-ref](./coding-agent-github-ref.md) |
| P5b-02 | 🧪 실제 LLM 통합 테스트 | OpenAI/Azure/gpustack 실제 연동 검증 | P5a-07 | - |
| P5b-03 | 🧪 회귀 테스트 자동화 | CI에서 전체 테스트 스위트 실행 | P5a-07, P0-05 | - |
| P5b-04 | 문서화 | 사용자 가이드, API 문서, 플러그인 개발 가이드 | - | - |
| P5b-05 | ~~릴리스 자동화~~ | ✅ GitHub Release CI/CD (태그 푸시 시 자동 배포) | P0-05 | - |
| P5b-06 | ~~자동 업데이트~~ | ✅ `ironhive update`, `--update` 플래그 | P5b-05 | - |

### 산출물
- ✅ ironbees 기반 멀티에이전트 지원 (선행 완료)
- ✅ **자동화된 릴리스 파이프라인** (GitHub Releases)
- ✅ **Self-update 기능** (`ironhive update`)
- ⏳ 완전한 문서화
- ⏳ **dotnet tool 배포**: `dotnet tool install -g ironhive`

### 진행 상황
- **완료**: P5b-01 (ironbees 통합), P5b-05 (릴리스 자동화), P5b-06 (자동 업데이트)
- **테스트 현황**: 313개 테스트 통과
- **남은 작업**: P5b-02~P5b-04 (테스트, 문서화)

---

## Future (v0.8.0+)

연구 문서에서 명시적으로 "후속 버전으로 미룸"으로 분류된 기능:

| 기능 | 설명 | 우선순위 | 비고 | 참조 |
|------|------|----------|------|------|
| **Repo Map (tree-indexer)** | AST 기반 코드 구조 추출 | Medium | C# 바인딩 미성숙, MCP 플러그인 분리 권장 | [ref.md#7](../dev-docs/ref.md) |
| **관련성 스코어링** | PageRank 기반 중요 파일 식별 | Medium | Repo Map 의존 | [research-02#5.1](./research/research-02.md) |
| Sub-agent 아키텍처 | 제한된 깊이의 서브에이전트 스폰 | Medium | - | [research-01#3.1](./research/research-01.md) |
| 고급 컨텍스트 선택 | Personalized PageRank | Low | - | [research-02#5.1](./research/research-02.md) |
| 분산 실행 | 원격 런타임, 컨테이너 오케스트레이션 | Low | - | [research-02#2.2](./research/research-02.md) |
| ACP 지원 | Agent Client Protocol 표준 채택 | Low | - | [research-01#1.3](./research/research-01.md) |
| ~~웹 UI~~ | ~~브라우저 기반 인터페이스~~ | **제외** | 철학적 결정 | - |

> **Note**: Repo Map(tree-indexer)은 C# tree-sitter 바인딩이 미성숙하고 "범용 에이전트" 철학과 충돌할 수 있어 선택적 MCP 플러그인으로 분리 권장 → [ref.md#7](../dev-docs/ref.md)

---

## 마일스톤 요약

```
v0.1.0 ─── Phase 0: 프로젝트 초기화 + 테스트 인프라     ✅ 완료
   │
v0.2.0 ─── Phase 1: 기본 에이전트 루프 + gpustack 연동  ✅ 완료 (100%)
   │       └── P1-01~P1-18 완료 (18/18)
   │       └── P1-16 TokenMeter (32 tests)
   │       └── P1-17 ToolCallParser (51 tests)
   │       └── P1-18 서브모듈 통합 완료
   │
v0.3.0 ─── Phase 2: 모드 시스템 및 HITL + --dry-run     ✅ 완료 (100%)
   │       └── P2-01~P2-14 완료 (14/14)
   │       └── 183 tests 통과
   │
v0.4.0 ─── Phase 3: MCP 플러그인 시스템                 🔄 진행중 (92%)
   │       └── P3-01~P3-11, P3-13 완료 (12/13)
   │       └── 공식 C# SDK 통합 (ModelContextProtocol 0.6.0)
   │       └── HotReloader, ToolDiscovery 구현
   │       └── memory-indexer, code-beaker SDK 통합
   │       └── 299 tests 통과 (통합 테스트 21개 포함)
   │
v0.4.1 ─── Phase 3.5: Claude Code 호환 세션 관리        ✅ 완료 (100%)
   │       └── P3.5-01~P3.5-08 완료 (8/8)
   │       └── JSONL 트랜스크립트, --continue/--resume/--fork
   │       └── sessions 명령, IAgentLoop.InitializeHistory()
   │       └── 338 tests 통과 (세션 21개 + InitializeHistory 4개)
   │
v0.5.0 ─── Phase 4: 컨텍스트 관리 + 프롬프트 캐싱       ⏳ 대기
   │
v0.6.0 ─── Phase 5a: 안정화 + E2E 테스트               ⏳ 대기
   │
v0.7.0 ─── Phase 5b: 생태계 통합 + 릴리스 자동화        🔄 부분 완료
   │       └── P5b-01 ironbees 통합 선행 완료
   │
v0.8.0+ ── Future: Repo Map, Sub-agent, 분산 실행
```

---

## 의존성 그래프

```
Phase 0 (기반)
    │
    ▼
Phase 1 (에이전트 루프 + gpustack) ←──────────┐
    │                                         │
    ├───────────────┬─────────────────┐       │
    ▼               ▼                 ▼       │
Phase 2         Phase 3           Phase 4     │
(모드/HITL)     (MCP 플러그인)    (컨텍스트)   │
    │               │                 │       │
    └───────────────┴─────────────────┘       │
                    │                         │
                    ▼                         │
              Phase 5a (안정화) ───────────────┘
                    │
                    ▼
              Phase 5b (생태계 통합)
```

Phase 2, 3, 4는 Phase 1 완료 후 병렬 진행 가능
