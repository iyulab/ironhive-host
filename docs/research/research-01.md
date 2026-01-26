# ironhive-cli 선행 연구 보고서

## Executive Summary

범용 CLI 에이전트 코어를 설계하기 위해 주요 코딩 에이전트들(Claude Code, OpenHands, Goose, SWE-agent, Aider 등)의 아키텍처를 분석했다. 핵심 발견: **단순함이 답이다.** Claude Code의 성공은 복잡한 멀티에이전트 스웜이 아닌, 단일 스레드 마스터 루프 + 규율 있는 도구 사용에서 나왔다.

---

## 1. 아키텍처 분석

### 1.1 Claude Code 아키텍처

**핵심 설계 철학**: "단순한 단일 스레드 마스터 루프 + 규율 있는 도구와 계획이 제어 가능한 자율성을 제공한다"

```
┌─────────────────────────────────────────────────────┐
│                 User Interface                       │
│            (CLI / VS Code / Web UI)                  │
└──────────────────────┬──────────────────────────────┘
                       ▼
┌─────────────────────────────────────────────────────┐
│              Master Agent Loop ("nO")                │
│                                                      │
│   while (response.has_tool_calls):                  │
│       execute_tool(tool_call)                       │
│       response = llm.generate(history + result)     │
│                                                      │
│   return response.text                              │
└──────────────────────┬──────────────────────────────┘
                       ▼
┌─────────────────────────────────────────────────────┐
│              Real-time Steering ("h2A")             │
│         (비동기 듀얼 버퍼 큐 - 중간 개입 가능)         │
└──────────────────────┬──────────────────────────────┘
                       ▼
┌─────────────────────────────────────────────────────┐
│                   Tool Engine                        │
│  ┌─────────┬─────────┬─────────┬─────────────────┐  │
│  │  View   │  Edit   │  Bash   │  GrepTool       │  │
│  │  LS     │  Write  │  Task   │  TodoWrite      │  │
│  └─────────┴─────────┴─────────┴─────────────────┘  │
└─────────────────────────────────────────────────────┘
```

**주요 특징**:
- **단일 스레드, 평면 히스토리**: 스레드 대화나 복수 에이전트 페르소나 없음
- **Tool call 기반 루프 종료**: 도구 호출이 없으면 루프 종료, 사용자에게 제어권 반환
- **TodoWrite로 계획 관리**: JSON 작업 목록 + 상태 추적 + 우선순위
- **Compressor (92% 임계치)**: 컨텍스트 윈도우 92% 도달 시 자동 요약/압축
- **Sub-agent (I2A/Task)**: 제한된 깊이의 서브에이전트 스폰 (무한 재귀 방지)
- **검색은 ripgrep**: Vector DB 아닌 정규식 기반 검색 (운영 오버헤드 최소화)

**기술 스택**: TypeScript, React, Ink, Yoga, Bun

---

### 1.2 OpenHands (V1) 아키텍처

**핵심 설계 원칙**: "엄격한 관심사 분리 + 이벤트 소싱 + 조합 가능성"

```
┌─────────────────────────────────────────────────────┐
│                  Applications                        │
│              (CLI / GUI / GitHub App)               │
└──────────────────────┬──────────────────────────────┘
                       ▼
┌─────────────────────────────────────────────────────┐
│                   Agent Server                       │
│            (REST/WebSocket 서비스)                   │
└──────────────────────┬──────────────────────────────┘
                       ▼
┌─────────────────────────────────────────────────────┐
│                  SDK (Core)                          │
│  ┌─────────────────────────────────────────────┐   │
│  │  Event-Sourced State  │  LLM Abstraction    │   │
│  │  Tool System          │  Agent Interface    │   │
│  │  Context Management   │  Memory             │   │
│  └─────────────────────────────────────────────┘   │
└──────────────────────┬──────────────────────────────┘
                       ▼
┌─────────────────────────────────────────────────────┐
│                   Workspace                          │
│    (Docker 샌드박스 / Local / Remote)                │
└─────────────────────────────────────────────────────┘
```

**V0 → V1 진화 교훈**:
- 모놀리식 → 모듈화된 패키지 (SDK, Tools, Workspace, Server)
- 연구와 프로덕션 분리
- 이벤트 스트림 기반 상태 관리 (재현성, 디버깅 용이)

**Agent Hub**: CodeActAgent (범용), BrowserAgent (웹 전문), Micro-agents (경량)

---

### 1.3 Goose (Block) 아키텍처

**핵심 설계 철학**: "MCP 기반 확장성 + 컨텍스트 리비전"

```
┌─────────────────────────────────────────────────────┐
│              Interface (CLI / Desktop)              │
└──────────────────────┬──────────────────────────────┘
                       ▼
┌─────────────────────────────────────────────────────┐
│                     Agent                            │
│                                                      │
│   1. Human Request                                  │
│   2. Provider Chat (tools 목록 포함)                │
│   3. Tool Call → Extension 실행                     │
│   4. Response to Model                              │
│   5. Context Revision (오래된/무관한 정보 제거)      │
│   6. 반복 또는 종료                                  │
│                                                      │
└──────────────────────┬──────────────────────────────┘
                       ▼
┌─────────────────────────────────────────────────────┐
│              Extensions (MCP Servers)               │
│    Developer │ Memory │ Web │ Automation │ ...      │
└─────────────────────────────────────────────────────┘
```

**주요 특징**:
- **Rust 기반**: 성능과 안전성
- **MCP 네이티브**: 확장은 모두 MCP 서버로 구현
- **Context Revision**: 매 턴마다 컨텍스트 정리 (토큰 관리)
- **터미널 통합 모드**: `@goose "do this"` - 세션 없이 한 번 실행
- **ACP (Agent Client Protocol) 채택 예정**: 클라이언트 인터페이스 표준화

---

### 1.4 SWE-agent 아키텍처

**핵심 개념**: Agent-Computer Interface (ACI)

```
┌─────────────────────────────────────────────────────┐
│                   sweagent CLI                       │
└──────────────────────┬──────────────────────────────┘
                       ▼
┌───────────────────────────────────────────────────────┐
│                      Agent                            │
│                                                       │
│   forward():                                         │
│       history = compress(history)  # HistoryProcessor│
│       response = model.generate(history)             │
│       action = parse(response)                       │
│       result = env.execute(action)                   │
│       return result                                  │
│                                                       │
└──────────────────────┬───────────────────────────────┘
                       ▼
┌───────────────────────────────────────────────────────┐
│                    SWEEnv                             │
│              (SWE-ReX 패키지 래퍼)                     │
│                                                       │
│   Deployment: Docker (local) / Modal / AWS (remote)  │
│   Shell Session: 명령 실행                            │
│   ACI Tools: find_file, search_file, edit, etc.      │
└───────────────────────────────────────────────────────┘
```

**ACI 도구 설계 원칙**:
- LM 친화적인 최소 액션 세트
- 검색: `find_file`, `search_file`, `search_dir`
- 편집: `edit` (외과적 패치), `write` (전체 파일)
- 탐색: `scroll_up`, `scroll_down`

**일반적인 궤적 패턴**:
1. 로컬라이제이션 (파일 찾기)
2. 재현 스크립트 실행
3. Edit → Execute 루프 반복
4. Submit

---

## 2. 모드 전환 패턴

### 2.1 Plan-mode vs Work-mode

| 에이전트 | Plan-mode | Work-mode | 전환 메커니즘 |
|---------|-----------|-----------|--------------|
| Claude Code | Shift+Tab×2 (read-only) | 기본 | 사용자 토글 |
| OpenHands (Open SWE) | Manager → Planner | Programmer + Reviewer | 시퀀스 기반 |
| Goose | 암묵적 | 암묵적 | LLM 자체 판단 |
| SWE-agent | 암묵적 (thought) | 암묵적 (action) | ReAct 패턴 |

**Claude Code Plan-mode 특징**:
- 코드베이스 탐색만 가능 (파일 수정 불가)
- 아키텍처 이해 후 계획 수립
- 계획을 버전 관리 가능한 파일로 저장

### 2.2 Human-in-the-Loop (HITL) 진입 조건

**공통 패턴**:
1. **위험 작업 감지**: 파일 삭제, 외부 API 호출, sudo 명령
2. **불확실성 임계치**: 모델이 확신이 낮을 때
3. **명시적 승인 게이트**: 쓰기 작업, 위험한 bash 명령

**Claude Code 권한 시스템**:
- 쓰기 작업: allow/deny 결정 필요
- Bash 명령: 위험 수준 분류 + 안전 노트 주입
- 화이트리스트 구성 가능

**Goose 권한 모드**:
- Autonomous: 모든 작업 자동 승인
- Manual Approval: 모든 도구 사용 전 확인

### 2.3 재계획 (Re-planning) 전략

**Manus 접근법**:
- TodoWrite로 매 턴 목표 재작성
- "lost-in-the-middle" 방지 (목표를 컨텍스트 끝에 유지)
- 평균 50개 도구 호출 → 지속적인 목표 상기 필요

**Open SWE 접근법**:
- Reviewer가 코드 검토 후 문제 발견 시 Programmer에게 피드백
- Action-Review 루프 반복

---

## 3. 컨텍스트 관리 전략

### 3.1 Anthropic의 Context Engineering 원칙

**세 가지 주요 기법**:

#### 3.1.1 Compaction (압축)
```
컨텍스트 92% 도달 시:
1. 대화 히스토리를 LLM에 전달하여 요약
2. 아키텍처 결정, 미해결 버그, 구현 세부사항 보존
3. 중복 도구 출력, 메시지 폐기
4. 요약 + 최근 5개 파일로 재시작
```

**Claude Code 예시**:
- 요약 프롬프트: recall 최대화 → precision 향상 반복
- 복잡한 에이전트 트레이스에서 튜닝

#### 3.1.2 Structured Note-taking (구조화된 메모)
```
Memory Tool (Sonnet 4.5):
- 파일 기반 외부 메모리
- 프로젝트 상태 세션 간 유지
- 이전 작업 참조 (컨텍스트 내 유지 없이)
```

#### 3.1.3 Sub-agent Architecture (서브에이전트)
```
메인 에이전트: 고수준 계획 조율
서브에이전트: 집중된 기술 작업 수행
  - 수만 토큰 탐색 가능
  - 1,000-2,000 토큰 요약만 반환
  - 깊이 제한으로 무한 스폰 방지
```

### 3.2 Google ADK 컨텍스트 아키텍처

**핵심 개념**: "컨텍스트는 더 풍부한 상태 시스템 위의 컴파일된 뷰"

```
Sources (소스)           Pipeline (파이프라인)      Output (출력)
─────────────────────────────────────────────────────────────────
Sessions                 Flows                     Working Context
Memory                → Processors              →  (LLM에 전달)
Artifacts                                         
```

**Artifact 패턴**:
- 대용량 데이터(5MB CSV, JSON 등)는 아티팩트 스토어에 저장
- 컨텍스트에는 핸들(참조)만 포함
- "context dumping" 함정 방지

**Prefix Caching 최적화**:
```
┌────────────────────────────────────────────────────┐
│  Stable Prefix (캐시 가능)                          │
│  - 시스템 지시사항                                   │
│  - 에이전트 정체성                                   │
│  - 장기 메모리                                       │
├────────────────────────────────────────────────────┤
│  Dynamic Suffix (변동)                              │
│  - 최근 메시지                                       │
│  - 현재 작업 컨텍스트                                │
└────────────────────────────────────────────────────┘
```

### 3.3 GCC (Git Context Controller)

**혁신적 접근**: 버전 관리 시맨틱을 에이전트 메모리에 적용

```
GCC Operations:
- COMMIT: 마일스톤 체크포인트
- BRANCH: 대안 탐색
- MERGE: 결과 통합
- CONTEXT: 구조화된 반영
```

**성과**: SWE-Bench에서 48.00% 해결률 (26개 시스템 대비 SOTA)

---

## 4. 플러그인/도구 시스템

### 4.1 MCP (Model Context Protocol) 핵심

**프로토콜 구조**:
```
Client (LLM App)              Server (Tool Provider)
─────────────────────────────────────────────────────
tools/list     ──────────►    도구 목록 반환
tools/call     ──────────►    도구 실행 + 결과 반환
resources/list ──────────►    리소스 목록 반환
prompts/list   ──────────►    프롬프트 템플릿 목록
```

**도구 정의 예시**:
```json
{
  "name": "get_weather",
  "description": "Get weather for a location",
  "inputSchema": {
    "type": "object",
    "properties": {
      "location": { "type": "string" }
    },
    "required": ["location"]
  }
}
```

**HITL 권고사항**:
> "신뢰와 안전, 보안을 위해 도구 호출을 거부할 수 있는 인간이 항상 루프에 있어야 한다"

### 4.2 도구 선택 (Tool Selection)

**LLM vs 라우터**:

| 접근법 | 장점 | 단점 |
|--------|------|------|
| LLM 자체 선택 | 유연성, 컨텍스트 이해 | 비용, 할루시네이션 |
| 규칙 기반 라우터 | 빠름, 예측 가능 | 유연성 부족 |
| 하이브리드 | 균형 | 복잡성 |

**Claude Code 접근법**: LLM이 선택, 도구 정의에 명확한 설명 제공

**Goose 접근법**:
- 키워드, 파일 타입, 컨텍스트 기반 라우팅
- 멀티 모델 구성 (비용/성능 최적화)

### 4.3 도구 실행 패턴

**JSON → 샌드박스 → 텍스트 결과**:
```
1. LLM이 JSON 형식 도구 호출 생성
2. 에이전트가 샌드박스 환경에서 실행
3. 결과를 텍스트로 변환하여 LLM에 반환
```

**에러 처리**:
- MCP 프로토콜 레벨 에러가 아닌 결과 객체 내 에러 보고
- LLM이 에러를 보고 처리 가능하도록

---

## 5. 실행 및 검증

### 5.1 검증 파이프라인

**Open SWE 접근법**:
```
Programmer → Reviewer → (문제 발견) → Programmer
           ↓
        (승인) → PR 생성
```

**Claude Code 접근법**:
- Diffs-first workflow: 컬러화된 diff로 변경 즉시 확인
- 최소 수정 + 쉬운 리뷰/롤백
- TDD 패턴 자연스럽게 촉진

### 5.2 에러 복구 전략

**3단계 복구**:
1. **재시도**: 동일 방법으로 다시 시도
2. **대안**: 다른 방법 시도
3. **에스컬레이션**: HITL로 전환

**Ralph Loop Agent (Vercel)**:
```javascript
verifyCompletion: ({ result }) => ({
  complete: result.text.includes('All files updated'),
  reason: 'Missing file updates'  // 실패 시 피드백
})
```

### 5.3 무한 루프/삽질 탐지

**정지 조건**:
- 반복 횟수 제한: `iterationCountIs(50)`
- 토큰 제한: `tokenCountIs(100_000)`
- 비용 제한: `costIs(5.00)`

**진전(Progress) 측정**:
- TodoWrite 상태 변화 감지
- 동일 에러 반복 패턴 감지
- 파일 변경 없는 연속 턴 감지

---

## 6. 핵심 설계 원칙 도출

### 6.1 아키텍처 원칙

1. **단순함 우선**: 복잡한 오케스트레이션보다 단순한 루프
2. **평면 히스토리**: 스레드 대화 지양, 디버깅 용이성 확보
3. **관심사 분리**: 코어 로직 / 도구 / 실행 환경 분리
4. **이벤트 소싱**: 상태를 이벤트 스트림으로 관리 (재현성)

### 6.2 모드 전환 원칙

1. **명시적 Plan-mode**: 읽기 전용으로 탐색 후 계획
2. **HITL 기본값**: 위험 작업은 기본적으로 승인 요청
3. **실시간 조향**: 중간 개입 가능한 구조

### 6.3 컨텍스트 관리 원칙

1. **Compaction**: 임계치 도달 시 자동 압축
2. **Artifact 분리**: 대용량 데이터는 핸들로 참조
3. **목표 상기**: 매 턴 목표를 컨텍스트 끝에 유지
4. **Prefix Caching**: 안정적 부분은 캐시 활용

### 6.4 도구 관리 원칙

1. **MCP 표준 채택**: 범용 도구 프로토콜
2. **LM 친화적 설계**: 명확한 설명, 최소 액션 세트
3. **에러는 결과로**: 프로토콜 에러 아닌 도구 결과로 보고

---

## 7. ironhive-cli 설계 제안

### 7.1 핵심 컴포넌트

```
┌─────────────────────────────────────────────────────┐
│                    ironhive-cli                      │
│                                                      │
│  ┌─────────────────────────────────────────────┐   │
│  │                    CLI                       │   │
│  │         (Spectre.Console 추천)               │   │
│  └─────────────────────┬───────────────────────┘   │
│                        ▼                            │
│  ┌─────────────────────────────────────────────┐   │
│  │              Mode Router                     │   │
│  │    plan-mode ↔ work-mode ↔ hitl-mode       │   │
│  │         (상태 머신 기반)                      │   │
│  └─────────────────────┬───────────────────────┘   │
│                        ▼                            │
│  ┌─────────────────────────────────────────────┐   │
│  │            Agent Loop ("nO" 스타일)          │   │
│  │                                              │   │
│  │   while (has_tool_calls && !hitl_required): │   │
│  │       result = execute_tool()               │   │
│  │       context = revise_context()            │   │
│  │       response = llm.generate()             │   │
│  │                                              │   │
│  └─────────────────────┬───────────────────────┘   │
│                        ▼                            │
│  ┌─────────────────────────────────────────────┐   │
│  │           Plugin Registry (MCP)              │   │
│  │    load_agents(directory_convention)        │   │
│  │    (ironbees 통합)                           │   │
│  └─────────────────────┬───────────────────────┘   │
│                        ▼                            │
│  ┌─────────────────────────────────────────────┐   │
│  │           Context Manager                    │   │
│  │    - Compaction (압축)                       │   │
│  │    - Selection (선택)                        │   │
│  │    - Revision (정리)                         │   │
│  │    (memory-indexer 통합)                     │   │
│  └─────────────────────────────────────────────┘   │
│                        ▼                            │
│  ┌─────────────────────────────────────────────┐   │
│  │           LLM Abstraction                    │   │
│  │           (ironhive 사용)                    │   │
│  └─────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────┘
```

### 7.2 모드 상태 머신

```
                    ┌──────────────┐
                    │   IDLE       │
                    └──────┬───────┘
                           │ user_input
                           ▼
                    ┌──────────────┐
          ┌────────│  PLANNING    │────────┐
          │        └──────┬───────┘        │
          │ complex_task  │ simple_task    │ need_clarification
          ▼               ▼                ▼
   ┌──────────────┐ ┌──────────────┐ ┌──────────────┐
   │  PLAN_MODE   │ │  WORK_MODE   │ │  HITL_MODE   │
   │ (read-only)  │ │ (execution)  │ │ (user input) │
   └──────┬───────┘ └──────┬───────┘ └──────┬───────┘
          │ plan_approved  │ dangerous_op   │ user_response
          └───────►        ◄────────────────┘
                           │
                           ▼
                    ┌──────────────┐
                    │  COMPLETED   │
                    └──────────────┘
```

### 7.3 플러그인 로딩 (ironbees 스타일)

```
.ironhive/
├── agents/
│   ├── code-assistant/
│   │   ├── AGENT.md          # 프롬프트
│   │   ├── tools.json        # MCP 도구 정의
│   │   └── config.yaml       # 설정
│   ├── researcher/
│   └── writer/
├── memory/
│   └── project.md            # 장기 메모리
└── config.yaml               # 전역 설정
```

### 7.4 기존 모듈 통합 계획

| 컴포넌트 | 기존 모듈 | 역할 |
|----------|----------|------|
| LLM Abstraction | ironhive | 멀티 프로바이더 LLM 호출 |
| Multi-agent | ironbees | 파일시스템 기반 에이전트 관리 |
| Memory | memory-indexer | 시맨틱 메모리 저장/검색 |
| Streaming | index-thinking | 스트리밍 + thinking 추출 |
| Execution | code-beaker | 코드 실행 (플러그인으로) |

### 7.5 MVP 범위

**포함**:
- CLI 인터페이스 (입력/출력/승인)
- 모드 라우터 (plan/work/hitl)
- 기본 에이전트 루프
- MCP 플러그인 로더
- 기본 컨텍스트 관리 (토큰 카운트 + 간단한 압축)

**후속 버전으로 미룸**:
- 고급 컨텍스트 선택 알고리즘
- Sub-agent 아키텍처
- 웹 UI
- 분산 실행

---

## 8. 참고 자료

### 논문
- ReAct: Synergizing Reasoning and Acting in Language Models (Yao et al., 2022)
- SWE-agent: Agent-Computer Interfaces Enable Automated Software Engineering (Yang et al., 2024)
- OpenHands Software Agent SDK (Wang et al., 2025)

### 프로젝트
- [Claude Code](https://github.com/anthropics/claude-code)
- [OpenHands](https://github.com/All-Hands-AI/OpenHands)
- [Goose](https://github.com/block/goose)
- [SWE-agent](https://github.com/SWE-agent/SWE-agent)
- [MCP Specification](https://modelcontextprotocol.io)

### 블로그/문서
- [Effective Context Engineering for AI Agents](https://www.anthropic.com/engineering/effective-context-engineering-for-ai-agents)
- [How Claude Code is Built](https://newsletter.pragmaticengineer.com/p/how-claude-code-is-built)
- [Context Engineering for AI Agents: Lessons from Building Manus](https://manus.im/blog/Context-Engineering-for-AI-Agents-Lessons-from-Building-Manus)
- [Architecting Efficient Context-Aware Multi-Agent Framework](https://developers.googleblog.com/architecting-efficient-context-aware-multi-agent-framework-for-production/)