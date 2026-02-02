# Web AI Chat Sample (Next.js + subprocess)

ironhive CLI를 subprocess로 호출하여 웹 채팅을 구현하는 샘플입니다.

## 목적

**CLI 인터페이스의 프로그래매틱 사용 완성도**를 검증합니다.

## 구현된 Gap (CLI-G1, G2, G3)

샘플 개발 과정에서 발견된 Gap이 CLI에 구현되었습니다:

```bash
# JSON 출력 (CLI-G1)
ironhive -p "Hello" --output json
# {"content":"Hi!","sessionId":"abc123","usage":{"inputTokens":5,"outputTokens":10}}

# JSON Lines 스트리밍 (CLI-G3)
ironhive -p "Hello" --output jsonl
# {"type":"start","sessionId":"abc123"}
# {"type":"text","content":"Hi"}
# {"type":"text","content":"!"}
# {"type":"done","sessionId":"abc123"}

# 세션 목록 JSON (CLI-G2)
ironhive sessions list --output json
# [{"id":"abc123","status":"active","model":"gpt-4","created":"2024-01-01T00:00:00Z"}]
```

## 남은 Gap

| ID | 설명 | 우선순위 |
|----|------|----------|
| CLI-G5 | `--plain` 플래그 (ANSI 코드 제거) | Medium |
| CLI-G6 | stdin에서 프롬프트 읽기 (`-p -`) | Low |
| CLI-G7 | 종료 코드 문서화 | Low |

## 실행 방법

### 사전 요구사항

```bash
# ironhive CLI 설치
dotnet tool install -g IronHive.Cli

# Node.js 18+ 필요
```

### 실행

```bash
cd samples/web-ai-chat

# 의존성 설치
npm install

# 개발 서버 시작
npm run dev

# 브라우저에서 http://localhost:3000 접속
```

### 환경 변수

```bash
# ironhive 실행 파일 경로 (선택)
export IRONHIVE_PATH=/path/to/ironhive

# 작업 디렉토리 (선택)
export IRONHIVE_CWD=/path/to/project
```

## API 엔드포인트

| Method | Path | 설명 |
|--------|------|------|
| POST | /api/chat | 스트리밍 채팅 (SSE) |
| PUT | /api/chat | 비스트리밍 채팅 (JSON) |
| GET | /api/sessions | 세션 목록 |
| GET | /api/gaps | Gap 현황 |

## 아키텍처

```
┌─────────────┐     HTTP/SSE   ┌─────────────┐    subprocess    ┌──────────────┐
│   Browser   │ ─────────────► │   Next.js   │ ───────────────► │ ironhive CLI │
│   (React)   │ ◄───────────── │   API       │ ◄─────────────── │ --output jsonl│
└─────────────┘                └─────────────┘                  └──────────────┘
```

## 파일 구조

```
web-ai-chat/
├── app/
│   ├── page.tsx              # 채팅 UI
│   ├── layout.tsx
│   └── api/
│       ├── chat/route.ts     # 채팅 API (SSE/JSON)
│       ├── sessions/route.ts # 세션 관리
│       └── gaps/route.ts     # Gap 현황
├── lib/
│   └── ironhive.ts           # CLI 래퍼
├── package.json
└── README.md
```
