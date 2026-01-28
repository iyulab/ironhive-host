# Claude Code 호환성 설계

> ironhive-cli를 Claude Code 플러그인 시스템 및 세션 관리와 100% 호환되도록 설계

## 1. 목표

1. **Claude Code 플러그인 호환**: `/plugin install` 가능한 플러그인 소비 및 생성
2. **세션 영속성**: JSONL 기반 트랜스크립트로 `--resume`/`--continue` 지원
3. **Hooks 시스템**: 12개 이벤트 라이프사이클 완전 지원
4. **MCP 서버**: Model Context Protocol 서버 동적 로딩

## 2. Claude Code 플러그인 시스템 사양

### 2.1 플러그인 디렉토리 구조

```
plugin-name/
├── .claude-plugin/
│   └── plugin.json          # 매니페스트 (필수)
├── skills/                  # Agent Skills
│   └── skill-name/
│       └── SKILL.md
├── commands/                # 레거시 커맨드
│   └── command.md
├── agents/                  # 서브에이전트 정의
│   └── agent.md
├── hooks/
│   └── hooks.json           # Hook 설정
├── .mcp.json                # MCP 서버 설정
├── .lsp.json                # LSP 서버 설정
└── README.md
```

### 2.2 plugin.json 스키마

```json
{
  "name": "plugin-name",           // 필수: 고유 식별자 (kebab-case)
  "version": "1.0.0",              // 시맨틱 버전
  "description": "설명",
  "author": {
    "name": "Author",
    "email": "email@example.com",
    "url": "https://github.com/author"
  },
  "homepage": "https://docs.example.com",
  "repository": "https://github.com/user/plugin",
  "license": "MIT",
  "keywords": ["keyword1", "keyword2"],

  // 컴포넌트 경로 (기본 디렉토리 외 추가)
  "commands": ["./custom/commands/"],
  "agents": "./custom/agents/",
  "skills": "./custom/skills/",
  "hooks": "./config/hooks.json",
  "mcpServers": "./mcp-config.json",
  "lspServers": "./.lsp.json"
}
```

### 2.3 Skills (SKILL.md)

```yaml
---
name: skill-name
description: 스킬 설명. 자동 호출 조건.
disable-model-invocation: false  # true면 수동 호출만
hooks:
  PreToolUse:
    - matcher: "Bash"
      hooks:
        - type: command
          command: "./scripts/check.sh"
---

스킬 실행 시 Claude에게 전달되는 프롬프트 내용.
$ARGUMENTS 플레이스홀더로 사용자 입력 캡처.
```

### 2.4 Hooks 시스템

#### 이벤트 라이프사이클

```
SessionStart ─→ UserPromptSubmit ─→ PreToolUse ─→ [Tool Execution]
                                         │              │
                                         ↓              ↓
                                  PermissionRequest  PostToolUse
                                         │              │
                                         └──────┬───────┘
                                                ↓
                                    SubagentStart ─→ SubagentStop
                                                ↓
                                              Stop
                                                ↓
                                           SessionEnd
```

#### 지원 이벤트 (12개)

| 이벤트 | 발생 시점 | 제어 가능 |
|--------|----------|-----------|
| `SessionStart` | 세션 시작/재개 | context 추가 |
| `UserPromptSubmit` | 사용자 입력 제출 | block/allow |
| `PreToolUse` | 도구 실행 전 | allow/deny/ask |
| `PermissionRequest` | 권한 대화상자 | allow/deny |
| `PostToolUse` | 도구 성공 후 | context 추가 |
| `PostToolUseFailure` | 도구 실패 후 | - |
| `SubagentStart` | 서브에이전트 시작 | - |
| `SubagentStop` | 서브에이전트 종료 | block/allow |
| `Stop` | Claude 응답 완료 | block/allow |
| `PreCompact` | 컨텍스트 압축 전 | - |
| `Setup` | --init/--maintenance | context 추가 |
| `SessionEnd` | 세션 종료 | - |
| `Notification` | 알림 전송 | - |

#### hooks.json 형식

```json
{
  "hooks": {
    "PostToolUse": [
      {
        "matcher": "Write|Edit",
        "hooks": [
          {
            "type": "command",
            "command": "${CLAUDE_PLUGIN_ROOT}/scripts/format.sh",
            "timeout": 30
          }
        ]
      }
    ],
    "Stop": [
      {
        "hooks": [
          {
            "type": "prompt",
            "prompt": "작업이 완료되었는지 평가: $ARGUMENTS"
          }
        ]
      }
    ]
  }
}
```

#### Hook 타입

| 타입 | 설명 |
|------|------|
| `command` | Shell 명령 실행 |
| `prompt` | LLM 기반 평가 (Haiku) |
| `agent` | 에이전틱 검증기 |

#### Hook 입력 (stdin JSON)

```json
{
  "session_id": "abc123",
  "transcript_path": "~/.claude/projects/.../session.jsonl",
  "cwd": "/path/to/project",
  "permission_mode": "default",
  "hook_event_name": "PreToolUse",
  "tool_name": "Bash",
  "tool_input": {
    "command": "npm test",
    "description": "Run tests"
  },
  "tool_use_id": "toolu_01ABC..."
}
```

#### Hook 출력 (stdout JSON)

```json
{
  "hookSpecificOutput": {
    "hookEventName": "PreToolUse",
    "permissionDecision": "allow",  // allow | deny | ask
    "permissionDecisionReason": "Approved by policy",
    "updatedInput": { "command": "npm test --silent" },
    "additionalContext": "추가 컨텍스트"
  },
  "continue": true,
  "suppressOutput": false
}
```

## 3. 세션 관리 사양

### 3.1 세션 저장 구조

```
~/.ironhive/
├── projects/
│   └── <project-hash>/
│       ├── <session-id>.jsonl       # 메인 트랜스크립트
│       └── <session-id>/
│           └── subagents/
│               └── agent-<id>.jsonl # 서브에이전트 트랜스크립트
├── settings.json                    # 사용자 설정
└── plugins/                         # 설치된 플러그인 캐시
```

### 3.2 JSONL 트랜스크립트 형식

```jsonl
{"type":"session_start","timestamp":"2026-01-28T10:00:00Z","session_id":"abc123","model":"gpt-4o"}
{"type":"user_message","timestamp":"2026-01-28T10:00:01Z","content":"파일 목록 보여줘"}
{"type":"assistant_message","timestamp":"2026-01-28T10:00:02Z","content":"네, 확인하겠습니다."}
{"type":"tool_use","timestamp":"2026-01-28T10:00:03Z","tool":"Bash","input":{"command":"ls -la"}}
{"type":"tool_result","timestamp":"2026-01-28T10:00:04Z","tool_use_id":"toolu_01","output":"..."}
{"type":"session_end","timestamp":"2026-01-28T10:05:00Z","reason":"user_exit"}
```

### 3.3 세션 CLI 인터페이스

```bash
# 새 세션 시작
ironhive

# 가장 최근 세션 계속
ironhive --continue
ironhive -c

# 특정 세션 재개
ironhive --resume <session-id>
ironhive -r <session-id>

# 세션 목록 보기
ironhive sessions list

# 세션 포크 (분기)
ironhive --resume <session-id> --fork
```

### 3.4 세션 상태 복원

```csharp
public interface ISessionManager
{
    // 세션 생성/로드
    Task<Session> CreateSessionAsync(string projectPath);
    Task<Session> LoadSessionAsync(string sessionId);
    Task<Session> GetLatestSessionAsync(string projectPath);

    // 세션 저장
    Task SaveMessageAsync(Session session, ChatMessage message);
    Task SaveToolUseAsync(Session session, ToolUse toolUse);

    // 세션 재개
    Task<IReadOnlyList<ChatMessage>> RestoreContextAsync(Session session);

    // 세션 포크
    Task<Session> ForkSessionAsync(Session session);
}

public record Session
{
    public string Id { get; init; }
    public string ProjectPath { get; init; }
    public string TranscriptPath { get; init; }
    public string Model { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ResumedAt { get; init; }
    public SessionStatus Status { get; init; }
}
```

## 4. MCP 서버 통합

### 4.1 MCP 설정 (.mcp.json)

```json
{
  "mcpServers": {
    "memory-indexer": {
      "command": "memory-indexer-mcp",
      "args": ["--port", "3000"],
      "env": {
        "INDEX_PATH": "${CLAUDE_PROJECT_DIR}/.memory"
      }
    },
    "code-beaker": {
      "command": "npx",
      "args": ["@iyulab/code-beaker-mcp"],
      "cwd": "${CLAUDE_PROJECT_DIR}"
    }
  }
}
```

### 4.2 MCP 도구 네이밍

```
mcp__<server>__<tool>

예시:
mcp__memory-indexer__search
mcp__code-beaker__execute
```

## 5. 구현 로드맵

### Phase 1: 세션 관리 (v0.3.0)
- [ ] JSONL 트랜스크립트 저장/로드
- [ ] `--continue`, `--resume` 플래그
- [ ] 컨텍스트 복원 로직
- [ ] 세션 목록/관리 명령

### Phase 2: Hooks 시스템 (v0.4.0)
- [ ] Hook 설정 로더 (`settings.json`, `hooks.json`)
- [ ] 12개 이벤트 라이프사이클
- [ ] Command/Prompt/Agent hook 타입
- [ ] Hook 입출력 JSON 처리

### Phase 3: 플러그인 시스템 (v0.5.0)
- [ ] plugin.json 파서
- [ ] Skills/Commands 로더
- [ ] MCP 서버 동적 로딩
- [ ] `/plugin install` 명령

### Phase 4: 완전 호환 (v0.6.0)
- [ ] LSP 서버 통합
- [ ] 플러그인 마켓플레이스 연동
- [ ] Claude Code 플러그인 상호 운용성 테스트

## 6. 참조 문서

- [Claude Code Plugins](https://code.claude.com/docs/en/plugins)
- [Plugins Reference](https://code.claude.com/docs/en/plugins-reference)
- [Hooks Reference](https://code.claude.com/docs/en/hooks)
- [MCP Documentation](https://modelcontextprotocol.io/)
- [Agent SDK Sessions](https://platform.claude.com/docs/en/agent-sdk/sessions)

---

Last Updated: 2026-01-28
