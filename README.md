# ironhive-cli

> 범용 CLI 에이전트 코어

Claude Code, Codex CLI 같은 에이전트 도구. 코딩뿐 아니라 범용 작업을 위한 기반 도구.

## 특징

- **gpustack/로컬 모델 지원** - ironhive 기반 멀티 프로바이더
- **MCP 플러그인** - 도구 확장은 MCP 서버로
- **Plan/Work/HITL 모드** - 계획 → 실행 → 사람 개입
- **웹훅 지원** - 외부 시스템 연동

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

## 사용 예시

```bash
# 기본 대화
ironhive

# 단일 명령 실행
ironhive -p "이 프로젝트의 README를 작성해줘"

# Plan 모드 (읽기 전용 탐색)
ironhive --plan "리팩토링 계획 세워줘"

# gpustack 모델 사용
ironhive --model gpustack/qwen2.5-coder

# 웹훅 설정
ironhive --webhook http://localhost:8080/events
```

## 관련 프로젝트

- [ironhive](https://github.com/iyulab/ironhive) - LLM 추상화
- [ironbees](https://github.com/iyulab/ironbees) - 멀티에이전트 관리
- [memory-indexer](https://github.com/iyulab/memory-indexer) - 시맨틱 메모리 MCP
- [code-beaker](https://github.com/iyulab/code-beaker) - 코드 실행 플랫폼

## 라이선스

MIT