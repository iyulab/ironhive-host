# ironhive-host — Charter

> **한 줄 정체성**: 에이전트를 **어떤 표면(CLI · 서버 · 임베드)으로든 호스팅**하는 중립 호스트.
> "위(③②)도 아래(⑥)도 모르는" 에이전트 런타임 미들웨어.
>
> 권위: 조직 전략 `dev-works/org/docs/product-middleware.md` §⑤ · umbrella 실행 `ironhive-umbrella/docs/MIDDLEWARE-ALIGNMENT.md` M1.
> 본 문서는 이 repo의 **정체성(불변)**을 명문화한다. 실행 태스크·일정은 MIDDLEWARE-ALIGNMENT.md M1이 권위(앵커 §끝).

---

## 위상

| 항목 | 값 |
|---|---|
| 층 | **low 라이브러리** (provider/도메인 중립 — IChatClient 주입) |
| repo 판정 | **기존 repo** — GitHub rename `ironhive-cli → ironhive-host` 완료(`03c331f`). ⚠️ 내부 네이밍(`IronHive.Cli*`)은 부채로 잔존 |
| 배포 위상 | **인프로세스 SDK** (NuGet `dotnet tool` + DI) — **SDK 확정** |
| umbrella 페이즈 | **M1** |

> rename 자체가 "**CLI 앱 → 재사용 호스트 미들웨어**" 승격 선언이다. "cli"는 배포형태 오표기였다 — 실제로는 CLI·서버·임베드를 다 한다.

---

## Charter 5항목 (존재 이유)

### 1. 책임
에이전트를 어떤 표면으로든 호스팅한다 — **루프 · 모드 · MCP · 권한 · 세션 · compaction · HITL.**

### 2. 경계
- **도메인 모름** — 파일도 재고도 모른다 (그건 소비 앱).
- **provider 모름** — `IChatClient`를 **주입받는다**. vendor(OpenAI/Anthropic/Ollama/Gemini) 분기 0. 어느 모델로 갈지 + 안전하게 갈지는 ⑥ `iron-prow`의 책임.
- **앱 IPC wire 아님** — host는 **generic turn-stream 표면**만 제공. 앱별 wire 타입은 앱이 소유 (CLAUDE.md 업스트림 §3 — 라이브러리 schema ≠ 앱 wire).

### 3. 소비자 (rule-of-two 실증 = 3/4)
- ③ HoneAI · ② Formbase(대화형) · **Filer**(filer-ai, `IronHive.Cli.Core` 0.11.1 채택) · **SMI.AIMS**(ChatOrchestrator) · **vault-ai**(session+compaction) · web-chat 샘플.

### 4. 의존 (without-ironhive)
- `Microsoft.Extensions.AI.IChatClient` 표준 (주입: `UseChatClient` / `UseChatClientFactory`).
- ironhive = **기본 provider 어댑터** — `IChatClient`만 있으면 ironhive 없이도 돈다.

### 5. 추출조건
- 이미 repo. **신규 개발 아님** — product-middleware §⑤ "지난 설계의 Agent Runtime 그 자체."
- 잔존 작업 = 정체성 정렬(내부 rename) + 표면 정제(thin protocol 추출, primitive 통합).

---

## 기능 표면

> API 시그니처는 README가 권위. 본 절은 **표면의 범위**를 정체성 차원에서 명세한다.

### 현재 (실측 — 성숙)

**3표면:**
| 표면 | 진입점 | 출력 |
|---|---|---|
| **CLI** | `ironhive` / `ironhive -p "..."` | text · `--output json` · `--output jsonl` · `--plain` |
| **서버** | `AgentServerRunner`(stdin/stdout JSON Lines) · `AgentHttpRunner`(HTTP/SSE) | `ServerEvent` 스트림 |
| **임베드** | `IronHive.Host` NuGet + DI (`IAgentLoop`) | `RunAsync` · `RunStreamingAsync` |

**표면 컴포넌트:**
- **`AgentServerProtocol`** (thin `IronHive.Host.Protocol` 패키지, 무의존) — 완전한 generic turn-stream: requests(`user_message`·`hitl_response`·`context_update`·`cancel`·`shutdown`) / events(`session_started`·`text_delta`·`thinking_delta`·`tool_start`·`tool_end`·`hitl_request`·`turn_end`·`agent_selected`·`error`·`fallback`·`plan_*`).
- **세션 관리** (`-c`/`-r`, `sessions list`), **config 머지**(global→project→env→.env, `config.yaml` 4-scope + settings.json 자동 마이그레이션).
- **`ChatBehaviorConfig`** (iteration/error 루프 튜닝), **`TokenBudgetChatClient`**(context-overflow 차단), **`ResilientFunctionInvoker`**(tool-arg 자가교정).
- **provider 주입** (`UseChatClient`/`UseChatClientFactory`).

### 목표 (M1 — 정체성 정렬 + 표면 정제)

- **M1-1 내부 rename** `IronHive.Cli*` → `IronHive.Host*` (패키지 ID·네임스페이스·slnx·tool명). **✅ 완료 (0.13.0, D3 hard-cut)** — 실행 명령 `ironhive`는 유지.
- **M1-3 thin `IronHive.Host.Protocol`** contracts 패키지 추출 — 경량 client가 heavy Core 의존/hand-mirror 없이 프로토콜 채택. **✅ 완료 (0.13.0, D3 통합 breaking)**.
- **M1-4 primitive 통합 표면** — session · compaction · MCP-client · loop-guard · HITL (소비자 재구현 + vault-ai broken stub 흡수).
- **M1-2 provider 중립 완결** — chat은 PASS. embedding/rerank GpuStack coupling 제거(주입 추상화).
- **M1-5** 3표면(CLI·서버·임베드) 문서화.

---

## ⑥ iron-prow 와의 경계 분담 (중요)

host와 iron-prow는 **한 쌍**이며 책임이 인접해 혼동되기 쉽다. 선을 명확히 한다:

| 책임 | 주인 | 근거 |
|---|---|---|
| **여러 번의 호출을 엮는 것** — 루프·모드·세션·MCP·권한·HITL·compaction | ⑤ host | 에이전트 런타임 |
| **한 번의 안전한 호출** — provider 선택 + guardrail + resilience + length-bound | ⑥ iron-prow | 안전 추론 |

- host는 iron-prow의 **guarded `IChatClient`를 `UseChatClient`로 주입**받는다 (M2-5). host는 provider 중립 유지.
- **이관 후보**: 현재 host에 baked-in된 `TokenBudgetChatClient`(length-bounding) · `ResilientFunctionInvoker`(error-recovery)는 "안전 추론" 책임 → **iron-prow로 수렴이 정석**. 구현 게이트 통과 시 M2-2/M2-4에서 이관 검토(지금은 경계 선언만).

---

## 현재 상태 & 부채

- **네이밍 부채 ✅ 해소 (0.13.0)**: `IronHive.Cli*` → `IronHive.Host*` rename + thin `IronHive.Host.Protocol` 추출 + dual-`CompactionConfig` dedup(agent 단일 소스)을 D3 통합 breaking 1회로 정리. 실행 명령 `ironhive` 유지.
- **마이그레이션**: 0.12.x `IronHive.Cli`(3,491 dl)·`IronHive.Cli.Core`(1,310 dl)는 배포 중단(unlist 아님). 소비자(Filer filer-ai)는 `IronHive.Host.Core` 재참조 + `using` 교체로 이전 — Filer inline-append 검증 대기.
- **provider 중립**: chat ✅ PASS / embedding·rerank GpuStack coupling 잔존 (M2 비차단, provider-격리 후속).
- 초안: `claudedocs/issues/ISSUE-ironhive-host-20260629-005241-...rename...md` · `...-m13-thin-protocol-...md` · `...-m12-provider-neutral-verdict.md`.

---

## 로드맵 앵커

실행 태스크·일정·게이트 상태의 권위는 **`ironhive-umbrella/docs/MIDDLEWARE-ALIGNMENT.md`**:

- **M1** — ironhive-host 정체성 확립: M1-1(rename) · M1-2(provider 중립) · M1-3(thin protocol) · M1-4(primitive) · M1-5(문서화).
- **D3**(결정 레지스터) — host breaking: M1-1+M1-3 통합 1회 breaking 권장 (owner 승인 대기, 소비자 Filer 영향 큼 → 별도 계획+이슈 필수).
