# ironhive-cli

> 범용 CLI 에이전트 코어

Claude Code, Codex CLI 같은 에이전트 도구. 코딩뿐 아니라 범용 작업을 위한 기반 도구.

## 특징

- **gpustack/로컬 모델 지원** - GpuStack, OpenAI 호환 API
- **MCP 플러그인** - Model Context Protocol 기반 도구 확장
- **Plan/Work/HITL 모드** - 계획 → 실행 → 사람 개입
- **세션 관리** - 대화 재개, 포크, 컨텍스트 복원
- **컨텍스트 관리** - 자동 압축, 목표 상기
- **웹훅 지원** - 외부 시스템 연동
- **비용/토큰 제한** - 세션당 한도 설정

## 철학

**하나만 잘한다.** 명령을 받고, 계획하고, 수행한다.

무중단 자동 재개, 칸반 보드, 복잡한 오케스트레이션은 하지 않는다. 그건 이 CLI를 호출하는 2차 프로그램의 몫이다.

```
┌─────────────────────────────────────────┐
│          외부 시스템 (웹훅 소비자)         │
│   CI/CD, 스케줄러, 칸반, 워크플로우 엔진    │
└────────────────────┬────────────────────┘
                     │ 호출 + 웹훅 수신
                     ▼
┌─────────────────────────────────────────┐
│             ironhive-cli                │
│                                         │
│   명령 → 모드 선택 → 플랜 → 실행 → 결과   │
│                                         │
└────────────────────┬────────────────────┘
                     │ MCP
                     ▼
┌─────────────────────────────────────────┐
│              플러그인 (MCP 서버)          │
│   code-beaker, memory-indexer, ...      │
└─────────────────────────────────────────┘
```

## 설치

```bash
# dotnet tool로 설치 (예정)
dotnet tool install -g ironhive

# 또는 소스에서 빌드
git clone https://github.com/iyulab/ironhive-cli
cd ironhive-cli
git submodule update --init --recursive
dotnet build
```

## 사용법

### 기본 대화

```bash
# 대화형 모드
ironhive

# 단일 명령 실행
ironhive -p "이 프로젝트의 README를 작성해줘"
ironhive run "파일 목록을 보여줘"
```

### 모드 선택

```bash
# Plan 모드 (읽기 전용 탐색)
ironhive --plan "리팩토링 계획 세워줘"

# Dry-run (실제 실행 없이 계획만)
ironhive --dry-run "테스트 파일 정리해줘"
```

### 세션 관리

```bash
# 최근 세션 계속
ironhive -c
ironhive --continue

# 특정 세션 재개
ironhive -r <session-id>
ironhive --resume <session-id>

# 세션 포크 (분기)
ironhive -r <session-id> --fork

# 세션 목록
ironhive sessions
ironhive sessions --project <path>
```

### 모델 설정

```bash
# gpustack 모델 사용
ironhive --model gpustack/qwen2.5-coder

# 환경 변수로 설정
export GPUSTACK_ENDPOINT=http://localhost:8080
export GPUSTACK_API_KEY=your-key
export GPUSTACK_MODEL=gpt-4o-mini
```

### 웹훅 설정

```bash
# CLI 옵션
ironhive --webhook http://localhost:8080/events

# 설정 파일 (.ironhive/config.yaml)
webhook:
  endpoints:
    - url: https://example.com/webhook
      secret: your-secret
      eventFilter: [SessionStarted, ToolCompleted]
```

### 설정 파일

설정 파일은 다음 순서로 병합됩니다:

1. **글로벌**: `~/.ironhive/config.yaml`
2. **프로젝트**: `.ironhive/config.yaml`
3. **환경 변수**: `IRONHIVE_*`, `GPUSTACK_*`
4. **.env 파일**: 프로젝트 루트의 `.env`

```yaml
# .ironhive/config.yaml 예시
limits:
  maxSessionTokens: 100000
  maxSessionCost: 10.00
  warningThreshold: 0.8
  stopOnLimit: true

context:
  compactionThreshold: 0.92
  goalReminderEnabled: true

session:
  autoSave: true
  maxSessions: 100
```

### CLAUDE.md 지원

프로젝트 루트에 `CLAUDE.md` 파일을 배치하면 에이전트가 자동으로 로드합니다:

```markdown
# CLAUDE.md

이 프로젝트는 Python Flask 웹 애플리케이션입니다.

## 코딩 스타일
- PEP 8 준수
- 타입 힌트 필수
- docstring 작성

## 금지 사항
- print() 대신 logging 사용
```

## 아키텍처

### 핵심 컴포넌트

| 컴포넌트 | 설명 |
|----------|------|
| **AgentLoop** | 단일 스레드 마스터 루프 |
| **ModeManager** | Plan/Work/HITL 모드 전환 |
| **SessionManager** | JSONL 트랜스크립트 관리 |
| **ContextManager** | 토큰 카운팅, 압축, 목표 상기 |
| **McpPluginManager** | MCP 서버 연결/관리 |

### 내장 도구

| 도구 | 설명 |
|------|------|
| Read | 파일 읽기 |
| Write | 파일 쓰기 (diff 표시) |
| Shell | 명령 실행 |
| Glob | 파일 패턴 검색 |
| Grep | 내용 검색 |
| Todo | 작업 목록 관리 |

## 개발

### 요구 사항

- .NET 10 SDK
- Git (서브모듈 포함)

### 빌드

```bash
# 서브모듈 초기화
git submodule update --init --recursive

# 빌드
dotnet build

# 테스트
dotnet test

# 포맷 검사
dotnet format --verify-no-changes
```

### 테스트

```bash
# 전체 테스트
dotnet test

# 특정 카테고리
dotnet test --filter "Category=Unit"
dotnet test --filter "Category=Integration"

# LLM 통합 테스트 (API 키 필요)
GPUSTACK_API_KEY=your-key dotnet test --filter "Category=Integration"
```

### 프로젝트 구조

```
ironhive-cli/
├── src/
│   ├── IronHive.Cli.Core/       # 핵심 에이전트 로직
│   │   ├── Agent/               # AgentLoop, 모드 시스템
│   │   ├── Config/              # 설정 관리
│   │   ├── Context/             # 컨텍스트 관리
│   │   ├── Mcp/                 # MCP 플러그인
│   │   ├── Providers/           # LLM 프로바이더
│   │   ├── Session/             # 세션 관리
│   │   ├── Tools/               # 내장 도구
│   │   └── Webhook/             # 웹훅
│   └── IronHive.Cli/            # CLI 애플리케이션
├── tests/
│   └── IronHive.Cli.Tests/      # 단위/통합 테스트
├── submodules/
│   ├── TokenMeter/              # 토큰 카운팅
│   └── ToolCallParser/          # tool_call 파싱
└── docs/
    ├── ROADMAP.md               # 개발 로드맵
    └── research/                # 연구 문서
```

## 관련 프로젝트

- [ironhive](https://github.com/iyulab/ironhive) - LLM 추상화
- [ironbees](https://github.com/iyulab/ironbees) - 멀티에이전트 관리
- [memory-indexer](https://github.com/iyulab/memory-indexer) - 시맨틱 메모리 MCP
- [code-beaker](https://github.com/iyulab/code-beaker) - 코드 실행 플랫폼

## 라이선스

MIT
