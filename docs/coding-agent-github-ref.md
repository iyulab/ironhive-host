# 오픈소스 코딩 에이전트 GitHub 저장소 목록

## 주요 CLI 에이전트 (오픈소스)

| 프로젝트 | GitHub URL | 언어 | Stars | 특징 |
|----------|------------|------|-------|------|
| **Claude Code** | https://github.com/anthropics/claude-code | TypeScript | - | Anthropic 공식, 단일 스레드 마스터 루프 |
| **OpenAI Codex CLI** | https://github.com/openai/codex | Rust | 30k+ | OpenAI 공식, 로컬 실행 |
| **Gemini CLI** | https://github.com/google-gemini/gemini-cli | TypeScript | 92k+ | Google 공식, MCP 지원, Apache 2.0 |
| **Goose (Block)** | https://github.com/block/goose | Rust | - | Block(Square) 개발, MCP 네이티브, 범용 에이전트 |
| **OpenHands** | https://github.com/All-Hands-AI/OpenHands | Python | 64k+ | 이전 OpenDevin, 샌드박스 환경, SDK 제공 |
| **SWE-agent** | https://github.com/SWE-agent/SWE-agent | Python | - | Princeton/Stanford, ACI 개념, 연구용 |
| **Aider** | https://github.com/Aider-AI/aider | Python | 25k+ | Git 통합, pair programming, 다양한 LLM 지원 |
| **Cline** | https://github.com/cline/cline | TypeScript | 48k+ | VS Code 확장, Plan/Act 모드, HITL |
| **Continue** | https://github.com/continuedev/continue | TypeScript | 20k+ | IDE 확장, 커스텀 에이전트, 오픈소스 |
| **OpenCode** | https://github.com/opencode-ai/opencode | Go | 70k+ | TUI 기반, 멀티 프로바이더, LSP 통합 |
| **OpenCode (SST)** | https://github.com/sst/opencode | TypeScript | - | 클라이언트/서버 아키텍처 |

---

## SDK / 프레임워크

| 프로젝트 | GitHub URL | 언어 | 특징 |
|----------|------------|------|------|
| **OpenHands SDK** | https://github.com/OpenHands/software-agent-sdk | Python | 이벤트 소싱, 샌드박스, MIT |
| **OpenAI Agents SDK** | https://github.com/openai/openai-agents-python | Python | ReAct 패턴, MCP 지원 |
| **LangChain** | https://github.com/langchain-ai/langchain | Python | 에이전트 프레임워크, 체인 |
| **LangGraph** | https://github.com/langchain-ai/langgraph | Python | 그래프 기반 워크플로우 |
| **CrewAI** | https://github.com/crewAIInc/crewAI | Python | 멀티에이전트 오케스트레이션 |
| **AutoGen** | https://github.com/microsoft/autogen | Python | Microsoft, 멀티에이전트 |

---

## MCP (Model Context Protocol) 관련

| 프로젝트 | GitHub URL | 특징 |
|----------|------------|------|
| **MCP Specification** | https://github.com/modelcontextprotocol/modelcontextprotocol | 프로토콜 스펙 |
| **MCP Python SDK** | https://github.com/modelcontextprotocol/python-sdk | Python SDK, FastMCP |
| **MCP TypeScript SDK** | https://github.com/modelcontextprotocol/typescript-sdk | TypeScript SDK |

---

## 특화 에이전트

| 프로젝트 | GitHub URL | 특징 |
|----------|------------|------|
| **mini-SWE-agent** | https://github.com/SWE-agent/SWE-agent | 100줄 Python으로 65% SWE-bench |
| **Open SWE** | https://github.com/langchain-ai/open-swe | LangGraph 기반, Manager-Planner-Programmer |
| **Mentat** | https://github.com/AbanteAI/mentat | Python, 코드베이스 이해 |
| **GPT Engineer** | https://github.com/gpt-engineer-org/gpt-engineer | Python, 전체 프로젝트 생성 |
| **Sweep** | https://github.com/sweepai/sweep | Python, GitHub 이슈 자동 해결 |
| **Devon** | https://github.com/entropy-research/devon | Python, 오픈소스 Devin 대안 |

---

## IDE 확장/통합

| 프로젝트 | GitHub URL | IDE | 특징 |
|----------|------------|-----|------|
| **Cline** | https://github.com/cline/cline | VS Code | Plan/Act, MCP, 브라우저 |
| **Continue** | https://github.com/continuedev/continue | VS Code, JetBrains | 커스텀 어시스턴트 |
| **Aider (VS Code)** | https://github.com/Aider-AI/aider | VS Code (통합) | Git 연동 |
| **Cursor** | (closed source) | 자체 IDE | VS Code fork |
| **Windsurf** | (closed source) | 자체 IDE | Codeium |

---

## 연구/실험용

| 프로젝트 | GitHub URL | 특징 |
|----------|------------|------|
| **BabyAGI** | https://github.com/yoheinakajima/babyagi | 작업 관리 에이전트 |
| **AutoGPT** | https://github.com/Significant-Gravitas/AutoGPT | 자율 에이전트 |
| **AgentGPT** | https://github.com/reworkd/AgentGPT | 웹 기반 자율 에이전트 |
| **MetaGPT** | https://github.com/geekan/MetaGPT | 멀티에이전트 소프트웨어 회사 |
| **ChatDev** | https://github.com/OpenBMB/ChatDev | 가상 소프트웨어 회사 |

---

## 벤치마크/평가

| 프로젝트 | GitHub URL | 용도 |
|----------|------------|------|
| **SWE-bench** | https://github.com/princeton-nlp/SWE-bench | 코딩 에이전트 벤치마크 |
| **GAIA** | https://github.com/gaia-benchmark/GAIA | 범용 AI 어시스턴트 벤치마크 |
| **HumanEval** | https://github.com/openai/human-eval | 코드 생성 벤치마크 |

---

## 추천 참고 순서 (ironhive-cli 개발용)

### 1. 아키텍처 참고
1. **Claude Code** - 단순한 마스터 루프의 모범 사례
2. **OpenHands SDK** - 이벤트 소싱, 모듈화된 설계
3. **Goose** - Rust 기반, MCP 네이티브

### 2. 컨텍스트 관리 참고
1. **Aider** - Git 통합, 코드베이스 맵
2. **SWE-agent** - ACI, HistoryProcessor

### 3. MCP 통합 참고
1. **MCP Python SDK** - FastMCP
2. **Gemini CLI** - MCP 확장 시스템

### 4. HITL/모드 전환 참고
1. **Cline** - Plan/Act 모드
2. **Open SWE** - Manager-Planner-Programmer 시퀀스

---

## 비공개 (참고용)

| 서비스 | 특징 |
|--------|------|
| **Devin** (Cognition) | 최초의 AI 소프트웨어 엔지니어 |
| **Cursor** | VS Code fork, 에이전트 모드 |
| **GitHub Copilot** | Agent Mode, Copilot Spaces |
| **Amazon Q Developer** | AWS 통합, SWE-bench SOTA |
| **Amazon Kiro** | Claude 4 기반, Specs/Hooks |
| **Windsurf** | Codeium, Cascade 에이전트 |