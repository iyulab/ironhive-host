# IronHive CLI Scenarios

system-harness와 WebLookup을 활용하는 CLI 시나리오 모음.

## Setup

### 1. Settings (WebLookup)

WebLookup 웹 검색은 기본 활성화 (DuckDuckGo, API 키 불필요):

```bash
# 기본 설정 확인
ironhive config get webSearch

# 지역 설정 변경
ironhive set webSearch.duckDuckGoRegion kr-kr

# Tavily 추가 (선택, API 키 필요)
ironhive set webSearch.tavilyApiKey tvly-xxxxx
```

### 2. MCP Plugins (system-harness)

`.ironhive/plugins.yaml`에 system-harness MCP 서버 등록:

```yaml
plugins:
  system-harness:
    transport: stdio
    command: dotnet
    arguments:
      - run
      - --project
      - path/to/system-harness/src/SystemHarness.Mcp
```

## Scenarios

### A. Web Research

```bash
# 단일 검색 쿼리
ironhive -p "최신 .NET 10 변경사항을 검색해서 요약해줘"

# 사이트 탐색
ironhive -p "https://learn.microsoft.com 의 sitemap을 분석해서 .NET 관련 최신 문서를 찾아줘"

# 주제 조사
ironhive -p "Rust vs Go 성능 비교를 웹에서 조사하고 표로 정리해줘"
```

### B. Desktop Automation (system-harness MCP)

```bash
# 파일 탐색
ironhive -p "현재 디렉토리의 .cs 파일 목록을 보여줘"

# 화면 캡처 (system-harness)
ironhive -p "현재 화면을 캡처해서 보여줘"

# 앱 제어 (system-harness)
ironhive -p "메모장을 열고 'Hello IronHive!' 라고 입력해줘"
```

### C. Combined Scenarios

```bash
# 웹 검색 → 파일 저장
ironhive -p "Python FastAPI 최신 릴리즈 노트를 검색해서 fastapi-notes.md 파일로 저장해줘"

# 웹 조사 → 데스크톱 작업 (system-harness + WebLookup)
ironhive -p "최신 .NET 10 breaking changes를 검색하고, 결과를 메모장에 붙여넣어줘"
```

### D. Deep Research

```bash
# 주제 심층 조사 (standard depth, 3-5분)
ironhive -p "양자 컴퓨팅의 최신 발전 현황을 심층 조사해줘"

# 빠른 조사 (quick depth, 1-2분)
ironhive -p "DeepResearch quick depth로 Rust의 async runtime 비교를 조사해줘"

# 포괄적 조사 (comprehensive depth, 10-15분)
ironhive -p "comprehensive depth로 2026년 AI 코딩 어시스턴트 시장 분석을 해줘"
```

> **Note**: DeepResearch 사용 시 `deepResearch.enabled = true` 설정과 Tavily API 키가 필요합니다.

### E. Information Gathering

```bash
# 사이트 구조 탐색 후 특정 콘텐츠 검색
ironhive -p "https://docs.python.org 의 sitemap을 탐색하고, asyncio 관련 최신 페이지를 찾아서 핵심 변경사항을 정리해줘"

# 복수 소스 비교 조사
ironhive -p "React, Vue, Svelte 최신 버전의 SSR 지원을 각각 검색하고 비교표를 만들어줘"
```

### F. Desktop Reporting (system-harness + WebLookup)

```bash
# 화면 분석 + 웹 조사
ironhive -p "현재 화면에 표시된 에러 메시지를 캡처하고, 해당 에러의 해결 방법을 웹에서 검색해줘"

# 시스템 정보 수집 + 보고서
ironhive -p "시스템 정보를 수집하고 실행 중인 프로세스 목록을 정리해서 system-report.md 파일로 저장해줘"
```

## Permissions

MCP 도구는 기본적으로 사용자 승인이 필요합니다. `.ironhive/permissions.yaml`로 규칙을 커스터마이즈할 수 있습니다:

```yaml
# MCP 도구 권한 설정 예시
mcpTools:
  # help/get 명령은 읽기 전용이므로 자동 허용
  - pattern: "*_help"
    action: allow
    reason: "Read-only help command"
  - pattern: "*_get"
    action: allow
    reason: "Read-only query command"
  # do 명령은 승인 필요 (기본값)
  # 특정 위험 도구 차단
  # - pattern: "*_dangerous_tool"
  #   action: deny
  #   reason: "Blocked for safety"

defaultAction: ask
```

## Test Coverage

각 시나리오 카테고리의 자동화 테스트 현황:

| Scenario | Test Class | Tests | Status |
|----------|-----------|-------|--------|
| **A. Web Research** | `WebSearchToolTests` | 24 | WebSearch, ExploreSite 단위 테스트 |
| **B. Desktop Automation** | `ModeToolFilterTests` | 10 | MCP 도구 모드 필터링 (Planning/Working) |
| **C. Combined (웹→파일)** | `CrossScenarioTests` | 12 | WebSearch→WriteFile, MCP→WebSearch 등 |
| **D. Deep Research** | `BuiltInToolsTests` | 2 | 도구 등록 (10개 전체, 8개 DeepResearch만) |
| **E. Information Gathering** | `CrossScenarioTests` | - | ExploreSite→WebSearch 체인 포함 |
| **F. Desktop Reporting** | `CrossScenarioTests` | - | MCP screen capture→WebSearch 포함 |
| **Permissions** | `PermissionEvaluatorTests` | 49 | MCP 도구 권한 규칙, glob 패턴 매칭 |
| **MCP Integration** | `AgentLoopFactoryTests` | 11 | 도구 조합, 설정 로딩, 에러 복원력 |
| **Error Recovery** | `CrossScenarioTests` | 3 | 도구 실패→대안, 부분 실패 처리 |

### 시나리오-테스트 매핑 상세

**Web Search → File Save** (`CrossScenarioTests`):
- `Scenario_WebSearchThenFileSave_ChainsToolCalls` — A→C 교차
- `Scenario_SearchAnalyzeWrite_ThreeStepChain` — A→ReadFile→C 교차

**MCP → Web Search** (`CrossScenarioTests`):
- `Scenario_McpScreenCaptureThenWebSearch_ErrorDiagnostics` — B→A 교차 (E)
- `Scenario_McpSystemInfoThenFileSave_Report` — B→C 교차 (E)

**MCP Help → Do** (`CrossScenarioTests`):
- `Scenario_McpHelpThenDo_DiscoverAndExecute` — B 시나리오 자동화

**Error Recovery** (`CrossScenarioTests`):
- `Scenario_ToolFailure_AgentFallsBackToAlternative` — 웹 검색 실패 → Grep
- `Scenario_McpToolUnavailable_AgentUsesBuiltinAlternative` — MCP 실패 → 내장 도구
- `Scenario_PartialFailure_AgentCompletesWhatItCan` — 부분 데이터로 완료

## Configuration Reference

| Setting | Default | Description |
|---------|---------|-------------|
| `webSearch.enabled` | `true` | 웹 검색 도구 활성화 |
| `webSearch.defaultMaxResults` | `10` | 기본 검색 결과 수 |
| `webSearch.maxSitemapEntries` | `50` | 최대 sitemap 항목 수 |
| `webSearch.duckDuckGoRegion` | `null` | DuckDuckGo 지역 코드 |
| `webSearch.tavilyApiKey` | `null` | Tavily API 키 |
| `webSearch.searchApiKey` | `null` | SearchApi API 키 |
| `webSearch.searchApiEngine` | `"google"` | SearchApi 엔진 |
| `deepResearch.enabled` | `false` | DeepResearch 도구 활성화 |
| `deepResearch.tavilyApiKey` | `null` | Tavily API 키 (미설정 시 webSearch.tavilyApiKey 사용) |
| `deepResearch.maxIterations` | `5` | 최대 연구 반복 횟수 |
| `deepResearch.provider` | `null` | 연구용 LLM 프로바이더 (미설정 시 기본 프로바이더 사용) |
| `deepResearch.model` | `null` | 연구용 LLM 모델 |
