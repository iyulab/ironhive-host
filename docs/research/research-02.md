# **차세대 범용 CLI 에이전트(ironhive-cli) 아키텍처 설계를 위한 심층 분석 보고서**

## **1\. 서론: 자율형 소프트웨어 엔지니어링의 구조적 진화**

소프트웨어 개발 도구의 패러다임이 단순한 코드 자동 완성(Autocomplete)에서 자율적인 문제 해결 능력을 갖춘 에이전트(Autonomous Agent)로 급격히 전환되고 있다. 이러한 변화의 중심에는 대규모 언어 모델(Large Language Model, LLM)이 존재하지만, 단순히 LLM을 CLI(Command Line Interface)에 연결하는 것만으로는 실용적인 엔지니어링 역량을 확보할 수 없다. 본 연구는 ironhive-cli라는 범용 에이전트 코어의 설계를 목적으로, 현재 시장을 선도하는 오픈소스 및 상용 CLI 에이전트들(Claude Code, Codex CLI, Aider, OpenHands, SWE-agent 등)의 내부 아키텍처를 해부하고, 이를 통해 도메인에 구애받지 않는 강건한 프레임워크 구축을 위한 핵심 원칙을 도출한다.

본 보고서는 기존 도구들이 확률론적 모델(Probabilistic Model)인 LLM과 결정론적 시스템(Deterministic System)인 운영체제 간의 불일치를 어떻게 해결하고 있는지에 주목한다. 특히 LLM의 환각(Hallucination)과 논리적 비약, 그리고 제한적인 컨텍스트 윈도우(Context Window) 문제를 극복하기 위해 고안된 아키텍처 패턴, 상태 관리 기법, 그리고 도구 통합 프로토콜(MCP)의 실제 구현 사례를 심층적으로 분석한다. 이는 단순한 기능 나열이 아닌, 각 아키텍처가 선택한 트레이드오프(Trade-off)와 그로 인한 엔지니어링적 합의(Consensus)를 규명하는 과정이다.

## ---

**2\. 아키텍처 분석: 확률과 결정의 경계**

범용 CLI 에이전트를 설계함에 있어 가장 본질적인 질문은 "어디까지가 모델의 영역이고, 어디서부터가 시스템의 영역인가"이다. 기존 에이전트들의 분석 결과, 성공적인 도구들은 LLM을 단순한 텍스트 생성기가 아닌, 거대한 상태 머신(State Machine) 내의 하나의 결정 노드(Decision Node)로 취급하는 경향이 뚜렷하게 나타났다.

### **2.1 내부 아키텍처의 구성 요소와 경계**

#### **오케스트레이션 레이어와 모델의 역할 분담**

분석 대상이 된 주요 에이전트들(OpenHands, SWE-agent, Claude Code 등)은 공통적으로 '팻 호스트(Fat Host)' 아키텍처를 채택하고 있다. 이는 LLM이 전체 실행 흐름을 주도하는 것이 아니라, 무거운 오케스트레이션 레이어가 LLM을 감싸고 있는 형태이다.

가장 대표적인 사례인 \*\*OpenHands(구 OpenDevin)\*\*의 경우, 시스템의 핵심은 LLM이 아닌 AgentController라는 결정론적 컴포넌트이다.1 이 컨트롤러는 에이전트의 생명주기를 관리하며, LLM은 단지 현재의 '상태(State)'와 '이벤트(Event)'를 입력받아 다음 '액션(Action)'을 반환하는 함수처럼 동작한다. AgentController는 LLM이 반환한 액션을 가로채어(Intercept) 실행 가능한지 검증하고, 실제 런타임(Docker 컨테이너 등)에 명령을 전달하며, 그 결과를 다시 관찰(Observation) 형태로 추상화하여 이벤트 스트림에 기록한다.2 이러한 구조에서 LLM 외부 하드코딩된 영역은 샌드박스 환경 관리, 이벤트 직렬화 로직, 그리고 메모리 관리 시스템 전반을 포함한다.

**SWE-agent** 역시 유사한 패턴을 보이는데, run.py 스크립트가 주도하는 ACI(Agent-Computer Interface) 루프가 핵심이다.3 여기서 LLM은 사람이 읽기 좋은 텍스트가 아닌, ACI가 정의한 특수 명령어 포맷(예: edit file.py)을 출력하도록 미세 조정되거나 프롬프팅된다. 특히 린터(Linter) 실행과 같은 검증 로직은 LLM의 추론 과정에 포함되지 않고, 시스템 레벨에서 하드코딩되어 실행된다. 즉, LLM이 파일을 수정하겠다는 의도를 내비치면, 시스템이 이를 받아 수정 후 즉시 린터를 돌리고, 그 결과(성공 또는 에러 메시지)만을 다시 LLM에게 피드백하는 구조이다.4 이는 모델의 인지 부하를 줄이고 시스템의 안정성을 높이는 핵심 기법이다.

반면 **Claude Code**와 **Codex CLI**는 '클라이언트-서버' 모델에 가깝게 동작한다. 여기서 CLI 도구는 '하네스(Harness)' 역할을 수행하며, 로컬 파일 시스템과 원격의 추론 엔진 사이를 중개한다.5 이들은 CLAUDE.md나 config.toml과 같은 정적 설정 파일을 통해 프로젝트별 컨텍스트를 주입하는데, 이 파일들의 파싱과 적용 로직은 전적으로 LLM 외부의 클라이언트 코드에 하드코딩되어 있다. 이는 모델이 프로젝트의 규칙을 '기억'하는 것이 아니라, 시스템이 매 턴마다 규칙을 '주입'하는 형태임을 시사한다.

#### **상태 머신(State Machine) 대 자유 형식 루프(Free-form Loop)**

초기 에이전트(AutoGPT 등)들이 LLM의 출력에 전적으로 의존하는 '자유 형식 루프'를 채택하여 무한 루프나 논리적 오류에 취약했던 반면, 최신 아키텍처들은 엄격한 \*\*유한 상태 머신(Finite State Machine, FSM)\*\*을 도입하고 있다.

**OpenHands**와 **LangGraph** 기반의 에이전트들은 명시적인 상태 전이 그래프를 정의한다.7 예를 들어, 에이전트는 Research 상태에서 필수 정보를 모두 수집했다는 조건이 충족되어야만 Coding 상태로 전이할 수 있다. 이러한 전이 조건(Transition Guard)은 LLM의 확률적 판단이 아닌, 파이썬 코드 레벨의 결정론적 로직(예: "특정 파일이 생성되었는가?")으로 제어된다. 이는 에이전트가 불필요하게 이전 단계로 회귀하거나, 준비되지 않은 상태에서 코드를 실행하는 것을 원천적으로 차단한다.

반면 **Aider**는 상대적으로 유연한 루프를 유지하되, 내부적으로는 'Architect' 모드와 'Editor' 모드 간의 암묵적인 상태 전이를 활용한다.9 사용자가 /architect 명령어를 사용하거나 대화의 맥락이 설계 논의로 흐를 때, 시스템은 추론 전용 모델(Reasoning Model)을 호출하여 계획을 수립하고, 이후 구현 단계에서는 코딩 전용 모델로 전환한다. 이는 엄격한 FSM보다는 유연하지만, 여전히 '계획(Plan)'과 '실행(Execute)'이라는 두 가지 거시적 상태를 구분하여 운영하고 있음을 보여준다.

### **2.2 실행 모델: 동기식 차단과 비동기식 이벤트 스트림**

실행 모델의 진화는 사용자 경험(UX)과 직결된다. **Codex CLI**나 초기 버전의 **Aider**는 동기식(Synchronous) 실행 모델을 따르며, LLM이 응답을 생성하고 도구를 실행하는 동안 사용자의 터미널 입력이 차단된다.10 이는 단기 작업에는 적합하지만, 복잡한 리팩토링이나 장기 작업 시 생산성을 저해한다.

이에 대한 대안으로 **OpenHands**는 완전한 **비동기 이벤트 스트림(Asynchronous Event Stream)** 아키텍처를 구현하였다.1 사용자, 에이전트, 그리고 시스템(런타임)은 모두 동일한 이벤트 버스(Event Bus)에 메시지를 발행(Publish)하는 주체일 뿐이다. 따라서 에이전트가 긴 작업을 수행하는 도중에도 사용자는 "잠깐, 그 파일이 아니야\!"와 같은 수정 명령을 이벤트로 발행할 수 있으며, AgentController는 이를 즉시 감지하여 현재 작업을 중단하거나 큐에 추가할 수 있다. 이는 멀티 에이전트 협업이나 백그라운드 작업을 지원해야 하는 ironhive-cli 설계에 있어 필수적인 참조 모델이다.

| 특성 | Aider / Codex CLI | OpenHands / Open SWE | Ironhive-cli 설계 시사점 |
| :---- | :---- | :---- | :---- |
| **제어 주체** | 대화 루프 (Conversation Loop) | 이벤트 기반 상태 머신 (Event-driven FSM) | 결정론적 FSM 도입으로 안정성 확보 필요 |
| **실행 모델** | 동기식 (Blocking) | 비동기식 (Non-blocking Event Stream) | 사용자 개입(Interrupt)이 가능한 비동기 버스 채택 |
| **샌드박스** | 로컬 시스템 직접 제어 (위험) | Docker 컨테이너 격리 (안전) | 기본적으로 컨테이너 기반 런타임 제공 |
| **컨텍스트** | Repo Map (AST 기반) | Event History (시간 기반) | AST 기반 정적 분석과 이벤트 로그의 결합 필요 |

## ---

**3\. 모드 전환: 계획과 실행의 인지적 분리**

인간 엔지니어가 설계를 마친 후 코딩에 들어가는 것처럼, 고성능 에이전트는 '계획(Plan)'과 '작업(Work)'을 분리하는 추세를 보인다. 이는 단순한 절차적 분리가 아니라, 각 단계에 최적화된 모델과 컨텍스트를 사용하기 위한 전략적 선택이다.

### **3.1 Plan-mode와 Work-mode의 전환 메커니즘**

**Aider**의 **Architect-Editor 패턴**은 이 분야의 선도적인 사례이다.12 이 아키텍처는 인지적 능력(Reasoning)과 코딩 능력(Coding)을 분리하여 처리한다.

* **Plan Mode (Architect):** 이 단계에서는 OpenAI의 o1-preview나 Claude 3.5 Sonnet과 같은 고성능 추론 모델이 사용된다. 이 모델은 코드를 직접 작성하지 않고, 자연어로 된 '구현 계획서'나 '변경 제안서'를 작성한다. 이때의 결정 단위(Granularity)는 '기능(Feature)'이나 '이슈(Issue)' 단위이다.  
* **Work Mode (Editor):** 계획이 확정되면, 시스템은 GPT-4o나 DeepSeek와 같은 빠르고 저렴하며 코딩 문법에 강한 모델로 전환된다. Editor 모델은 Architect가 작성한 명세서를 바탕으로 실제 파일의 Diff를 생성하는 데 집중한다.12

전환의 결정은 **하이브리드 방식**을 취한다. LLM이 자체적으로 계획 수립이 완료되었다고 판단할 수도 있지만, **Aider**는 사용자가 명시적으로 /code 명령을 내리거나, Architect의 제안에 대해 "진행해(Go ahead)"라고 승인하는 암묵적 게이트를 통과해야만 모드가 전환된다.9

**SWE-agent**와 **Open SWE**는 이를 더욱 공식화하여, 작업을 시작하기 전에 반드시 'Planner' 에이전트가 저장소를 탐색(Research)하고 단계별 계획을 수립하도록 강제한다.14 이 계획은 사용자에게 UI를 통해 제시되며, 사용자가 승인 버튼을 누르거나 수정 의견을 제시하기 전까지는 코드를 한 줄도 작성하지 않는 '엄격한 게이트(Hard Gate)' 방식을 사용한다. 이는 에이전트가 잘못된 방향으로 코드를 대량 생산하여 되돌리기 어려운 상태가 되는 것을 방지한다.

### **3.2 Human-in-the-Loop (HITL) 진입 전략**

자율 에이전트의 위험성을 통제하기 위한 HITL 전략은 단순한 '승인 요청'을 넘어, 상황에 따른 '적응형 개입(Adaptive Intervention)'으로 진화하고 있다.

**Codex CLI**는 승인 정책을 세 가지 레벨로 분류하여 관리한다.15

1. **Read-Only:** 파일 시스템이나 네트워크에 부작용(Side-effect)을 일으키는 모든 작업을 원천 차단한다.  
2. **Auto (Untrusted):** ls, grep과 같은 읽기 작업은 자동 승인하되, write, delete, push와 같은 위험 작업 감지 시에만 실행을 일시 중지하고 사용자 승인을 요청한다.  
3. **Full Access:** 모든 작업을 사용자 승인 없이 수행한다 (샌드박스 내부 등에서 사용).

**OpenHands**는 이를 \*\*보안 분석기(Security Analyzer)\*\*와 \*\*확인 정책(Confirmation Policy)\*\*으로 추상화하였다.17 에이전트가 도구를 호출할 때마다 보안 분석기가 해당 호출의 위험도를 'Low/Medium/High'로 평가한다. 설정된 정책보다 위험도가 높은 작업이 감지되면, 에이전트의 상태는 WAITING\_FOR\_CONFIRMATION으로 자동 전이된다. 이 상태에서는 사용자가 명시적인 UserConfirmAction 이벤트를 발생시키기 전까지 모든 작업이 중단된다.

특히 주목할 점은 \*\*불확실성 임계치(Uncertainty Threshold)\*\*의 활용이다. 일부 연구에서는 LLM이 생성한 액션의 로그 확률(Log-probability)이 낮거나, 계획 단계에서 모호성이 감지될 경우 위험한 작업이 아니더라도 선제적으로 HITL 모드로 진입하여 사용자에게 명확화를 요청하는 패턴을 제안하고 있다.

### **3.3 실패 시 재계획(Re-planning) 전략**

계획대로 작업이 진행되지 않을 때, 에이전트는 어떻게 반응해야 하는가?

**SWE-agent**는 \*\*사후 피드백(Hindsight Feedback)\*\*과 **궤적(Trajectory) 분석**을 활용한다.18 에이전트가 특정 테스트 케이스를 통과하지 못해 반복적으로 실패할 경우, 시스템은 현재의 '실행(Action)' 상태를 폐기하고 이전의 '계획(Plan)' 단계로 롤백(Rollback)한다. 이때 단순한 재시도가 아니라, 실패한 시도에서 얻은 에러 로그와 원인 분석을 새로운 컨텍스트로 주입하여, Planner가 이전과 다른 경로를 모색하도록 유도한다. 이는 부분 실패(Syntax Error 등)는 린터 피드백 루프를 통해 즉시 수정하고, 전체 실패(로직 오류, 설계 결함)는 상위 계획을 수정하는 **계층적 복구 전략**이다.

## ---

**4\. 플러그인 및 에이전트 생태계: MCP를 통한 확장성**

범용 에이전트인 ironhive-cli는 특정 도구나 API에 종속되어서는 안 된다. 이를 위해 \*\*Model Context Protocol (MCP)\*\*이 핵심 통합 표준으로 부상하고 있다.

### **4.1 MCP 기반 도구 통합의 구현 패턴**

**Goose**의 사례는 대규모 도구 생태계를 효율적으로 관리하기 위한 **계층적 도구 발견(Layered Tool Discovery)** 패턴을 보여준다.19 수백 개의 API가 존재하는 엔터프라이즈 환경에서 모든 도구의 명세를 시스템 프롬프트에 넣는 것은 불가능하다.

1. **발견(Discovery):** 에이전트는 처음에 list\_tools 또는 search\_services라는 메타 도구만을 가지고 시작한다.  
2. **선택(Selection):** 사용자의 요청(예: "Jira 티켓 확인해줘")을 받으면, 에이전트는 메타 도구를 사용해 'Jira' 관련 도구가 있는지 검색한다. 이때 검색 결과로 해당 도구의 간략한 설명과 스키마를 동적으로 로딩한다.  
3. **오케스트레이션(Orchestration):** 필요한 도구 스키마를 컨텍스트에 적재한 후, 실제 호출을 수행한다.

이 과정에서 도구의 선택은 \*\*라우터 에이전트(Router Agent)\*\*가 담당하기도 한다. **Cline**의 제안된 멀티 에이전트 아키텍처에서는 '메인 프록시 에이전트'가 사용자 요청을 분석하여 가장 적합한 API 제공자나 MCP 서버를 선택하고, 작업을 위임하는 방식을 취한다.20

### **4.2 동적 로딩과 런타임 관리**

플러그인(MCP 서버)의 동적 로딩은 에이전트의 유연성을 결정짓는다.

* **파일 시스템 컨벤션 (OpenHands/Ironbees):** .openhands/skills/ 디렉토리나 특정 설정 파일(Markdown, YAML)에 에이전트의 능력(Skill)을 정의해두면, 런타임 시 이를 파싱하여 프롬프트에 주입하는 방식이다.17 이는 핫 리로드(Hot-reload)를 지원하여, 사용자가 에이전트 실행 중에 스킬 파일을 수정하면 즉시 반영될 수 있다.  
* **레지스트리 기반 (Goose/MCP):** registry.modelcontextprotocol.io와 같은 중앙 레지스트리를 통해 에이전트가 스스로 필요한 기능을 검색하고 설치할 수 있는 구조이다.21 이는 npm이나 pip처럼 에이전트의 기능을 패키지화하여 배포하고 관리할 수 있게 한다.  
* **런타임 연결:** **Claude Code**와 **Goose**는 실행 중인 세션에서 동적으로 MCP 서버에 연결(Connect)하거나 연결을 해제(Disconnect)할 수 있다. 이는 stdio 통신을 기반으로 하위 프로세스를 생성하고 관리하는 호스트 로직에 의해 구현된다.22

### **4.3 멀티 에이전트 협업과 통신**

단일 에이전트의 한계를 넘기 위해 여러 에이전트가 협업할 때, **LangGraph**와 **OpenHands**는 서로 다른 통신 패턴을 보여준다.

* **메시지 패싱과 공유 상태 (LangGraph):** LangGraph는 에이전트 간의 통신을 AgentState라는 공유 데이터 구조(JSON 스키마)를 통해 처리한다.8 예를 들어, 'Researcher' 에이전트가 조사 결과를 state\['documents'\]에 기록하면, 'Writer' 에이전트가 이를 읽어 사용하는 방식이다. 이는 직접적인 호출보다는 상태를 매개로 한 느슨한 결합(Loose Coupling)을 지향한다.  
* **위임(Delegation) (OpenHands):** 상위 에이전트가 하위 에이전트를 함수 호출하듯이 실행하는 방식이다. AgentDelegateAction 도구를 사용하여 하위 에이전트에게 "이 작업을 수행하라"는 명령과 함께 입력을 전달하면, 하위 에이전트는 독립적인 컨테이너나 스레드에서 작업을 수행하고 그 결과(Observation)를 반환한다.25 이 과정은 상위 에이전트 입장에서는 동기적인 도구 호출처럼 보이지만, 실제로는 별도의 자율 루프가 실행되는 것이다.

## ---

**5\. 컨텍스트 관리: 토큰 경제학의 최적화**

제한된 토큰 예산 내에서 방대한 코드베이스와 긴 대화 기록을 관리하는 것은 CLI 에이전트의 성능을 좌우하는 핵심 기술이다.

### **5.1 토큰 버짓 내 최적 컨텍스트 선택 알고리즘**

**Aider**가 도입한 **저장소 지도(Repo Map)** 기술은 이 분야의 표준으로 자리 잡았다.26 단순히 파일을 텍스트로 읽는 것이 아니라, 코드의 구조적 의미를 파악하여 가장 중요한 부분만을 선별한다.

1. **AST 파싱:** Tree-sitter를 사용하여 소스 코드를 추상 구문 트리(AST)로 파싱하고, 함수와 클래스 정의, 그리고 호출 관계를 추출한다.  
2. **참조 그래프 구축:** 추출된 정보를 바탕으로 파일 간의 의존성 그래프(Dependency Graph)를 생성한다.  
3. **PageRank 알고리즘 적용:** 그래프 상에서 네트워크 분석 기법인 PageRank를 실행하여, 코드베이스 내에서 가장 많이 참조되고 중요한 파일이나 함수를 식별한다.28  
4. **관련성 스코어링:** 사용자의 현재 질문이나 열려 있는 파일과 연관된 노드에 가중치를 부여(Personalized PageRank)하여, 현재 작업과 가장 관련성이 높은 코드 스니펫만을 컨텍스트 윈도우에 채운다.

이 방식은 단순한 키워드 검색(RAG)보다 훨씬 높은 정확도로 코드의 문맥을 LLM에 전달할 수 있게 한다.

### **5.2 대화 히스토리 압축 및 요약 전략**

장시간 작업 시 대화 기록이 토큰 한도를 초과하는 문제를 해결하기 위해, **OpenHands**는 \*\*컨텍스트 콘덴서(Context Condenser)\*\*를 도입했다.30

* **전략:** 대화 기록을 \[Head\], \[Middle\], \`\`로 구분한다.  
  * Head: 시스템 프롬프트와 초기 작업 목표 등 절대 잊어서는 안 되는 정보를 보존한다.  
  * Tail: 최근 ![][image1]개의 대화 턴(Turn)을 보존하여 즉각적인 문맥을 유지한다.  
  * Middle: 중간의 방대한 기록은 LLM을 사용해 요약(Summarization)하거나 과감히 삭제한다. 요약 시에는 "어떤 파일이 수정되었는지", "어떤 테스트가 통과했는지"와 같은 상태 변화 위주로 정보를 압축한다.  
* **Claude Code**는 **프루닝(Pruning)** 전략을 강조한다. 실패한 시도나 의미 없는 도구 호출 로그는 요약할 가치도 없으므로, 아예 히스토리에서 제거하여 컨텍스트 오염을 방지한다.32

### **5.3 메모리 분리: 장기 기억과 작업 기억**

에이전트의 메모리는 세션 내에서만 유효한 \*\*작업 기억(Working Memory)\*\*과 세션을 넘어서도 유지되는 \*\*장기 기억(Long-term Memory)\*\*으로 나뉜다.

* **자동 추출 및 벡터 저장:** **Goose**와 **Byterover** 같은 시스템은 에이전트가 작업 중에 알게 된 사실(예: "이 프로젝트는 Poetry를 사용함", "API 키는.env에 있음")을 자동으로 추출하여 별도의 메모리 파일이나 벡터 데이터베이스에 저장한다.34 다음 세션이 시작될 때 이 정보를 검색하여 프롬프트에 주입함으로써, 에이전트가 이전의 학습 내용을 기억하는 효과를 낸다.

## ---

**6\. 실행 및 검증: 안전망 구축**

에이전트가 생성한 코드는 항상 오류 가능성을 내포하고 있다. 따라서 실행 결과의 검증과 복구 메커니즘은 선택이 아닌 필수이다.

### **6.1 성공/실패 판정 기준**

단순한 종료 코드(Exit Code) 확인만으로는 충분하지 않다. **SWE-agent**의 연구에 따르면, 도구 실행 결과에 대한 \*\*의미적 검증(Semantic Verification)\*\*이 병행되어야 한다.

* **출력 파싱:** 에이전트 오케스트레이터는 도구의 표준 출력(stdout)과 에러 출력(stderr)을 파싱하여, "Traceback", "Error", "Failed"와 같은 키워드를 감지한다. 종료 코드가 0이라도 에러 메시지가 포함되어 있다면 실패로 간주한다.  
* **전용 검증기:** 코드를 수정한 후에는 반드시 테스트 케이스를 실행하거나 린터를 돌려 그 결과를 판정 기준으로 삼는다. 일부 고급 설정에서는 별도의 'Reviewer LLM'이 생성된 코드의 Diff를 분석하여 로직의 정합성을 평가하기도 한다.14

### **6.2 에러 복구 및 무한 루프 탐지**

에이전트가 스스로 에러를 수정하는 **자기 수정(Self-correction)** 루프는 강력하지만, 무한 루프에 빠질 위험이 있다.

* **패턴 감지(Pattern Detection):** **Jolt**와 같은 시스템은 프로그램의 상태 스냅샷을 비교하여 무한 루프를 탐지한다.36 에이전트의 경우, 연속된 액션 시퀀스의 해시(Hash) 값을 비교하여 동일한 수정과 에러가 반복되는지 감지하는 \*\*반복 탐지기(Repetition Detector)\*\*를 구현해야 한다.  
* **진전(Progress) 측정:** 에이전트가 자원을 소모하며 작업을 수행하고 있지만, 실제로는 문제 해결에 다가가지 못하는 '삽질'을 탐지하기 위해, 해결된 테스트 케이스의 수나 코드 커버리지의 변화와 같은 정량적 지표를 모니터링해야 한다.37 만약 진전이 없다면 시스템은 강제로 작업을 중단하고 사용자에게 개입을 요청(Escalation)해야 한다.  
* **비용 제한:** **Aider**는 세션당 토큰 비용 한도를 설정하여, 에러 복구 루프가 무한정 실행되어 API 비용이 급증하는 것을 막는 안전장치를 두고 있다.38

## ---

**7\. 결론: Ironhive-cli 설계를 위한 제언**

본 연구를 통해 도출된 ironhive-cli의 핵심 설계 원칙은 다음과 같다.

1. **결정론적 코어 위에 확률적 모델을 얹어라:** LLM에 모든 제어권을 넘기지 말고, OpenHands와 같이 엄격한 이벤트 기반 상태 머신(FSM)으로 에이전트의 행동 반경을 제어해야 한다.  
2. **계획과 실행을 분리하라:** Aider의 Architect-Editor 모델을 차용하여, 고비용 추론 모델로 설계를 확정한 후 저비용 코딩 모델로 구현하는 2단계 파이프라인을 구축해야 한다.  
3. **MCP를 통한 극단적 모듈화:** 코어 기능은 최소화하고, 모든 도구와 확장은 MCP 프로토콜을 통해 동적으로 로딩되는 구조를 취해야 한다. 특히 계층적 도구 발견 패턴을 통해 컨텍스트 효율성을 확보해야 한다.  
4. **구조적 컨텍스트 이해:** 단순 텍스트 검색이 아닌, AST와 PageRank 기반의 Repo Map 기술을 도입하여 에이전트가 코드의 의존성을 구조적으로 이해할 수 있도록 지원해야 한다.  
5. **안전 우선 실행:** 모든 실행은 격리된 환경(컨테이너)에서 이루어져야 하며, 의미적 파싱과 반복 탐지 알고리즘을 통해 에이전트의 폭주를 방지하는 안전망이 내장되어야 한다.

이러한 원칙들은 ironhive-cli가 단순한 코딩 도구를 넘어, 신뢰할 수 있는 자율 소프트웨어 엔지니어링 파트너로 기능하기 위한 필수적인 기반이 될 것이다.

| 구분 | 주요 기술 요소 | Ironhive-cli 적용 전략 |
| :---- | :---- | :---- |
| **아키텍처** | Event-driven FSM, Fat Host | 비동기 이벤트 버스 및 엄격한 상태 전이 제어 도입 |
| **모드 전환** | Architect/Editor, Hard Gate | 추론 전용 모드와 구현 모드 분리 및 명시적 승인 절차 |
| **확장성** | MCP, Layered Discovery | MCP 레지스트리 연동 및 계층적 도구 탐색 구현 |
| **컨텍스트** | Repo Map (AST+PageRank), Condenser | Rust 기반 고성능 Repo Mapper 및 지능형 이력 압축 |
| **안전성** | Semantic Verification, Loop Detection | 린터/테스트 기반 자동 검증 루프 및 반복 행동 차단 |

#### **참고 자료**

1. OpenHands Agent Framework \- Emergent Mind, 1월 26, 2026에 액세스, [https://www.emergentmind.com/topics/openhands-agent-framework](https://www.emergentmind.com/topics/openhands-agent-framework)  
2. \[Feature\]: Visualize Agent Loop (User Input, Agent Output, Action-Obs pair) and Record LLM Metrics with OpenTelemetry/Logfire · Issue \#8916 \- GitHub, 1월 26, 2026에 액세스, [https://github.com/All-Hands-AI/OpenHands/issues/8916](https://github.com/All-Hands-AI/OpenHands/issues/8916)  
3. SWE-agent: Agent-Computer Interfaces Enable Automated Software Engineering \- NIPS, 1월 26, 2026에 액세스, [https://proceedings.neurips.cc/paper\_files/paper/2024/file/5a7c947568c1b1328ccc5230172e1e7c-Paper-Conference.pdf](https://proceedings.neurips.cc/paper_files/paper/2024/file/5a7c947568c1b1328ccc5230172e1e7c-Paper-Conference.pdf)  
4. How SWE-Agent uses large language models and Agent-Computer Interfaces to improve software development. \- Devansh, 1월 26, 2026에 액세스, [https://machine-learning-made-simple.medium.com/how-swe-agent-uses-large-language-models-and-agent-computer-interfaces-to-improve-software-c2bccc107673](https://machine-learning-made-simple.medium.com/how-swe-agent-uses-large-language-models-and-agent-computer-interfaces-to-improve-software-c2bccc107673)  
5. Claude Code: Best practices for agentic coding \- Anthropic, 1월 26, 2026에 액세스, [https://www.anthropic.com/engineering/claude-code-best-practices](https://www.anthropic.com/engineering/claude-code-best-practices)  
6. Unrolling the Codex agent loop | OpenAI, 1월 26, 2026에 액세스, [https://openai.com/index/unrolling-the-codex-agent-loop/](https://openai.com/index/unrolling-the-codex-agent-loop/)  
7. I was done with open-ended Loops. I use “State Machines” so that my Agents don't get lost. : r/AgentsOfAI \- Reddit, 1월 26, 2026에 액세스, [https://www.reddit.com/r/AgentsOfAI/comments/1qjlzcj/i\_was\_done\_with\_openended\_loops\_i\_use\_state/](https://www.reddit.com/r/AgentsOfAI/comments/1qjlzcj/i_was_done_with_openended_loops_i_use_state/)  
8. Graph API overview \- Docs by LangChain, 1월 26, 2026에 액세스, [https://docs.langchain.com/oss/python/langgraph/graph-api](https://docs.langchain.com/oss/python/langgraph/graph-api)  
9. Chat modes | aider, 1월 26, 2026에 액세스, [https://aider.chat/docs/usage/modes.html](https://aider.chat/docs/usage/modes.html)  
10. In-chat commands \- Aider, 1월 26, 2026에 액세스, [https://aider.chat/docs/usage/commands.html](https://aider.chat/docs/usage/commands.html)  
11. OpenHands/openhands-feedback · Datasets at Hugging Face, 1월 26, 2026에 액세스, [https://huggingface.co/datasets/OpenHands/openhands-feedback](https://huggingface.co/datasets/OpenHands/openhands-feedback)  
12. Aider's Architect/Editor approach sets new SOTA for AI code editing, achieving 85% pass rate : r/ChatGPTCoding \- Reddit, 1월 26, 2026에 액세스, [https://www.reddit.com/r/ChatGPTCoding/comments/1fshzxl/aiders\_architecteditor\_approach\_sets\_new\_sota\_for/](https://www.reddit.com/r/ChatGPTCoding/comments/1fshzxl/aiders_architecteditor_approach_sets_new_sota_for/)  
13. Separating code reasoning and editing | aider, 1월 26, 2026에 액세스, [https://aider.chat/2024/09/26/architect.html](https://aider.chat/2024/09/26/architect.html)  
14. Introducing Open SWE: An Open-Source Asynchronous Coding Agent \- LangChain Blog, 1월 26, 2026에 액세스, [https://www.blog.langchain.com/introducing-open-swe-an-open-source-asynchronous-coding-agent/](https://www.blog.langchain.com/introducing-open-swe-an-open-source-asynchronous-coding-agent/)  
15. Breaking Out of the Codex Sandbox (While Keeping Approval Controls), 1월 26, 2026에 액세스, [https://www.vincentschmalbach.com/breaking-out-of-the-codex-sandbox-while-keeping-approval-controls/](https://www.vincentschmalbach.com/breaking-out-of-the-codex-sandbox-while-keeping-approval-controls/)  
16. Codex CLI features \- OpenAI for developers, 1월 26, 2026에 액세스, [https://developers.openai.com/codex/cli/features/](https://developers.openai.com/codex/cli/features/)  
17. The OpenHands Software Agent SDK: A Composable and Extensible Foundation for Production Agents \- arXiv, 1월 26, 2026에 액세스, [https://arxiv.org/html/2511.03690v1](https://arxiv.org/html/2511.03690v1)  
18. SWE-SEARCH: ENHANCING SOFTWARE AGENTS WITH MONTE CARLO TREE SEARCH AND ITERATIVE REFINEMENT \- ICLR Proceedings, 1월 26, 2026에 액세스, [https://proceedings.iclr.cc/paper\_files/paper/2025/file/a1e6783e4d739196cad3336f12d402bf-Paper-Conference.pdf](https://proceedings.iclr.cc/paper_files/paper/2025/file/a1e6783e4d739196cad3336f12d402bf-Paper-Conference.pdf)  
19. MCP Night 2.0 Demo Recap: Block's Goose \- The Layered Tool Pattern \- WorkOS, 1월 26, 2026에 액세스, [https://workos.com/blog/mcp-night-block-goose-layered-tool-pattern](https://workos.com/blog/mcp-night-block-goose-layered-tool-pattern)  
20. Feature Request: Multi-Agent Framework with Automated API Provider Selection · cline cline · Discussion \#489 \- GitHub, 1월 26, 2026에 액세스, [https://github.com/cline/cline/discussions/489](https://github.com/cline/cline/discussions/489)  
21. Introducing the MCP Registry | Model Context Protocol Blog, 1월 26, 2026에 액세스, [http://blog.modelcontextprotocol.io/posts/2025-09-08-mcp-registry-preview/](http://blog.modelcontextprotocol.io/posts/2025-09-08-mcp-registry-preview/)  
22. Deep Dive into goose's Extension System and Model Context Protocol (MCP), 1월 26, 2026에 액세스, [https://dev.to/lymah/deep-dive-into-gooses-extension-system-and-model-context-protocol-mcp-3ehl](https://dev.to/lymah/deep-dive-into-gooses-extension-system-and-model-context-protocol-mcp-3ehl)  
23. Model Context Protocol(MCP) with Google Gemini 2.5 Pro — A Deep Dive (Full Code), 1월 26, 2026에 액세스, [https://medium.com/google-cloud/model-context-protocol-mcp-with-google-gemini-llm-a-deep-dive-full-code-ea16e3fac9a3](https://medium.com/google-cloud/model-context-protocol-mcp-with-google-gemini-llm-a-deep-dive-full-code-ea16e3fac9a3)  
24. How to Build LangGraph Agents Hands-On Tutorial \- DataCamp, 1월 26, 2026에 액세스, [https://www.datacamp.com/tutorial/langgraph-agents](https://www.datacamp.com/tutorial/langgraph-agents)  
25. Sub-Agent Delegation \- OpenHands Docs, 1월 26, 2026에 액세스, [https://docs.openhands.dev/sdk/guides/agent-delegation](https://docs.openhands.dev/sdk/guides/agent-delegation)  
26. Repository map \- Aider, 1월 26, 2026에 액세스, [https://aider.chat/docs/repomap.html](https://aider.chat/docs/repomap.html)  
27. Building a better repository map with tree sitter \- Aider, 1월 26, 2026에 액세스, [https://aider.chat/2023/10/22/repomap.html](https://aider.chat/2023/10/22/repomap.html)  
28. RepoMap Graph: use code entities as nodes instead of files? · Issue \#1385 · Aider-AI/aider, 1월 26, 2026에 액세스, [https://github.com/paul-gauthier/aider/issues/1385](https://github.com/paul-gauthier/aider/issues/1385)  
29. An Exploratory Study of Code Retrieval Techniques in Coding Agents \- Preprints.org, 1월 26, 2026에 액세스, [https://www.preprints.org/manuscript/202510.0924](https://www.preprints.org/manuscript/202510.0924)  
30. Context Condenser \- OpenHands Docs, 1월 26, 2026에 액세스, [https://docs.openhands.dev/sdk/guides/context-condenser](https://docs.openhands.dev/sdk/guides/context-condenser)  
31. OpenHands Context Condensensation for More Efficient AI Agents, 1월 26, 2026에 액세스, [https://openhands.dev/blog/openhands-context-condensensation-for-more-efficient-ai-agents](https://openhands.dev/blog/openhands-context-condensensation-for-more-efficient-ai-agents)  
32. Best Practices for Claude Code \- Claude Code Docs, 1월 26, 2026에 액세스, [https://code.claude.com/docs/en/best-practices](https://code.claude.com/docs/en/best-practices)  
33. Feature Request: Add Context Pruning as Alternative to Compacting · Issue \#6390 · anthropics/claude-code \- GitHub, 1월 26, 2026에 액세스, [https://github.com/anthropics/claude-code/issues/6390](https://github.com/anthropics/claude-code/issues/6390)  
34. Advanced Claude Code Context Engineering Strategies (with Examples) \- YouTube, 1월 26, 2026에 액세스, [https://www.youtube.com/watch?v=aHJkc84T9k8](https://www.youtube.com/watch?v=aHJkc84T9k8)  
35. Using Extensions | goose \- GitHub Pages, 1월 26, 2026에 액세스, [https://block.github.io/goose/docs/getting-started/using-extensions/](https://block.github.io/goose/docs/getting-started/using-extensions/)  
36. Detecting and Escaping Infinite Loops with Jolt \- People, 1월 26, 2026에 액세스, [https://people.csail.mit.edu/rinard/paper/ecoop11.pdf](https://people.csail.mit.edu/rinard/paper/ecoop11.pdf)  
37. Huxley-Gödel Machine: Human-Level Coding Agent Development by an Approximation of the Optimal Self-Improving Machine \- arXiv, 1월 26, 2026에 액세스, [https://arxiv.org/html/2510.21614v2](https://arxiv.org/html/2510.21614v2)  
38. BUG (?): v0.58 seems to have gotten caught in a loop, ran up $13 in charges rapidly \#1842 \- GitHub, 1월 26, 2026에 액세스, [https://github.com/paul-gauthier/aider/issues/1842](https://github.com/paul-gauthier/aider/issues/1842)

[image1]: <data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAABIAAAAXCAYAAAAGAx/kAAABGElEQVR4Xu3SMWoCURAG4AkmhWJQK7FTCyEQsPAMgmlSpAiKpa0kpVYStEkhiGkC6cUrpMgBAjlALmBlI9gpaPx/3yxO1pUtLPWHr3De+HZ33hM5mTTUh2pD1KxnYai8nldImp5tWKA7mMMKymb9CnLqC54gAxHT8y816MAMxnBp1q7VG6RMPTBHb3Sh+N0FcZtws1vTc6P64noDwyfQAOJQgTW8mJ4HVTe1vZRUS38n4Bt+Ia21rmLfwXA2dG9qTfiDquxmEzofzobyps6jnsCnuLfgbELnw9l48/HCP/RgIe4Ccjah8/G+3x+eGk+Pl7SoAsOnPsOj8of3iFfhR3Ynuxce5VTcQJdqBDHbJO4qvPtq55zDbAB7gDMYHgeMRgAAAABJRU5ErkJggg==>