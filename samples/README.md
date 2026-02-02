# IronHive CLI Samples

ironhive-cli 통합 방식별 샘플 프로젝트입니다.

## 샘플 목록

| 샘플 | 경로 | 검증 대상 | 기술 스택 |
|------|------|-----------|-----------|
| **Web Chat** | `web-ai-chat/` | CLI 인터페이스 (subprocess) | Next.js + TypeScript |
| **Console Chat** | `console-chat/` | Core 라이브러리 (직접 통합) | .NET 10 Console |

## 샘플 주도 개발 사이클

이 샘플들은 **"샘플 주도 개발"** 방식으로 라이브러리 완성도를 검증합니다:

1. 샘플 구현 시도
2. 미비점(Gap) 발견
3. 라이브러리/CLI 개선
4. 샘플 완성

### 발견된 Gap 및 해결 현황

#### CLI 인터페이스 (web-ai-chat)

| Gap | 설명 | 상태 |
|-----|------|------|
| CLI-G1 | `--output json` | ✅ 구현됨 |
| CLI-G2 | `sessions list --output json` | ✅ 구현됨 |
| CLI-G3 | `--output jsonl` (스트리밍) | ✅ 구현됨 |
| CLI-G5 | `--plain` 플래그 | 🔲 대기 |

#### Core 라이브러리 (console-chat)

| Gap | 설명 | 상태 |
|-----|------|------|
| CORE-G1 | 프로바이더 팩토리 헬퍼 | 🔲 대기 |
| CORE-G2 | `ThinkingDelta` 스트리밍 | 🔲 대기 |
| CORE-G3 | 세션-AgentLoop 통합 API | 🔲 대기 |

## 빠른 시작

### Web Chat (Next.js)

```bash
cd samples/web-ai-chat
npm install
npm run dev
# http://localhost:3000
```

### Console Chat (.NET)

```bash
cd samples/console-chat
dotnet run
```

## 자세한 내용

- [샘플 주도 개발 사이클 문서](../dev-docs/dev-cycles/sample-driven-web-chat-cycle.md)
