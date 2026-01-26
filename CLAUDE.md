# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

ironhive-cli는 Claude Code, Codex CLI 같은 범용 CLI 에이전트 코어입니다. 코딩뿐 아니라 범용 작업을 위한 기반 도구를 목표로 합니다.

## Core Philosophy

**하나만 잘한다.** 명령을 받고, 계획하고, 수행한다.

무중단 자동 재개, 칸반 보드, 복잡한 오케스트레이션은 하지 않습니다. 그것은 이 CLI를 호출하는 2차 프로그램의 몫입니다.

## Architecture Design Principles

이 프로젝트는 Claude Code, OpenHands, Goose, SWE-agent, Aider 등의 연구를 기반으로 설계됩니다:

### 1. 단순한 단일 스레드 마스터 루프
- 복잡한 멀티에이전트 스웜이 아닌, 단일 스레드 마스터 루프 + 규율 있는 도구 사용
- LLM을 거대한 상태 머신 내의 하나의 결정 노드로 취급

### 2. Plan/Work/HITL 모드
- **Plan-mode**: 읽기 전용 탐색, 계획 수립
- **Work-mode**: 실제 명령 실행
- **HITL-mode**: 위험 작업 시 사용자 승인 요청

### 3. MCP 플러그인 기반 확장
- 도구 확장은 모두 MCP(Model Context Protocol) 서버로 구현
- 핵심 기능은 최소화하고, 확장은 동적으로 로딩

### 4. 컨텍스트 관리
- Compaction (92% 임계치 도달 시 자동 압축)
- 목표를 컨텍스트 끝에 유지 ("lost-in-the-middle" 방지)

## Project Status: v0.1.0-alpha + LMSupply Integration

### 완료된 기능
- 솔루션 구조 (`IronHive.Cli.Core`, `IronHive.Cli`, `IronHive.Cli.Tests`)
- Microsoft.Extensions.AI 기반 IChatClient 연동
- Spectre.Console.Cli 기반 CLI 스켈레톤
- MockChatClient 테스트 인프라
- GitHub Actions CI/CD
- **.env 파일 지원** (DotNetEnv)
- **LMSupply 로컬 추론 fallback** (Embedder, Reranker, Generator)
- **GpuStack/OpenAI 호환 API 지원**
- **자동 Fallback 프로바이더 체인**

### 다음 단계: Phase 1 (v0.2.0) - 기본 에이전트 루프
- 대화 세션 관리
- 내장 도구 (Read, Write, Shell, Glob/Grep)
- 스트리밍 출력
- 인터럽트 처리

## Technology Stack

| 항목 | 선택 | 패키지 |
|------|------|--------|
| **언어** | C# | - |
| **런타임** | .NET 10 | - |
| **AI 추상화** | Microsoft.Extensions.AI | `Microsoft.Extensions.AI 10.2.0` |
| **로컬 추론** | LMSupply | `LMSupply.Embedder/Reranker/Generator 0.10.0` |
| **CLI** | Spectre.Console.Cli | `Spectre.Console.Cli 0.50.0` |
| **Diff** | DiffPlex | `DiffPlex 1.9.0` |
| **Glob** | MS.Ext.FileSystemGlobbing | `Microsoft.Extensions.FileSystemGlobbing 10.0.0` |
| **Config** | MS.Ext.Configuration | + `NetEscapades.Configuration.Yaml` |
| **.env** | DotNetEnv | `DotNetEnv 3.1.1` |

## Project Structure

```
ironhive-cli/
├── src/
│   ├── IronHive.Cli.Core/          # 핵심 에이전트 로직
│   │   ├── Agent/
│   │   │   ├── IAgentLoop.cs       # 에이전트 루프 인터페이스
│   │   │   └── AgentLoop.cs        # 기본 구현
│   │   ├── Config/
│   │   │   ├── IronHiveConfig.cs   # 설정 구조
│   │   │   └── EnvConfigLoader.cs  # .env 파일 로딩
│   │   └── Providers/
│   │       ├── IChatClientProvider.cs
│   │       ├── IEmbeddingProvider.cs
│   │       ├── IRerankProvider.cs
│   │       ├── GpuStack*.cs        # GpuStack/OpenAI 호환 API
│   │       ├── LMSupply*.cs        # LMSupply 로컬 추론
│   │       └── Fallback*.cs        # 자동 fallback 컴포지트
│   └── IronHive.Cli/               # CLI 애플리케이션
│       ├── Commands/               # CLI 명령
│       └── Infrastructure/         # DI, 설정
├── tests/
│   └── IronHive.Cli.Tests/         # 단위 테스트
│       ├── Agent/                  # AgentLoop 테스트
│       └── Mocks/                  # MockChatClient
├── submodules/
│   ├── TokenMeter/                 # 토큰/비용 계산
│   └── ToolCallParser/             # tool_call 파싱
└── docs/
    ├── ROADMAP.md                  # 개발 로드맵
    ├── design/                     # 설계 문서
    │   └── env-lmsupply-integration.md
    └── research/                   # 연구 문서
```

## Build Commands

```bash
# 서브모듈 초기화
git submodule update --init --recursive

# 빌드
dotnet build

# 테스트
dotnet test

# 실행 (대화형)
dotnet run --project src/IronHive.Cli

# 실행 (단일 프롬프트)
dotnet run --project src/IronHive.Cli -- -p "Hello"

# 도움말
dotnet run --project src/IronHive.Cli -- --help

# Native AOT 빌드
dotnet publish src/IronHive.Cli -c Release -r win-x64

# 포맷 검사
dotnet format --verify-no-changes
```

## CLI Usage

```bash
# 버전 확인
ironhive --version

# 도움말
ironhive --help

# 대화형 모드
ironhive

# 단일 프롬프트 실행
ironhive -p "이 프로젝트의 README를 작성해줘"
ironhive run "파일 목록을 보여줘"

# 설정 확인
ironhive config show
ironhive config path
```

## Development Guidelines

### API Key 설정

```bash
# .env 파일 사용 (권장)
cat > .env << EOF
GPUSTACK_ENDPOINT=http://172.30.1.53:8080
GPUSTACK_API_KEY=gpustack_xxx
GPUSTACK_MODEL=gpt-oss-20b
GPUSTACK_EMBEDDING_MODEL=qwen3-embedding-0.6b
GPUSTACK_RERANK_MODEL=qwen3-reranker-0.6b

LMSUPPLY_ENABLED=true
LMSUPPLY_EMBEDDER_MODEL=auto
LMSUPPLY_RERANKER_MODEL=auto
LMSUPPLY_GENERATOR_MODEL=gguf:default
EOF

# 환경 변수로 설정
export OPENAI_API_KEY=sk-...

# 또는 gpustack/ollama 사용
ironhive --provider ollama --model llama3.2
```

### 테스트 작성

```csharp
// MockChatClient 사용
var mockClient = new MockChatClient()
    .EnqueueResponse("Hello!");

var agentLoop = new AgentLoop(mockClient);
var response = await agentLoop.RunAsync("Hi");

Assert.Equal("Hello!", response.Content);
```

## Related Projects

- [ironhive](https://github.com/iyulab/ironhive) - LLM 추상화
- [ironbees](https://github.com/iyulab/ironbees) - 멀티에이전트 관리
- [memory-indexer](https://github.com/iyulab/memory-indexer) - 시맨틱 메모리 MCP
- [code-beaker](https://github.com/iyulab/code-beaker) - 코드 실행 플랫폼

## Reference Documents

- [ROADMAP.md](docs/ROADMAP.md) - 개발 로드맵
- [dotnet-ecosystem.md](docs/research/dotnet-ecosystem.md) - .NET AI 생태계 조사
- [ref.md](dev-docs/ref.md) - 라이브러리 결정
