# MCP 스펙 요약 (P3-02)

> Protocol Revision: 2025-06-18

## 개요

Model Context Protocol (MCP)은 LLM 애플리케이션과 외부 데이터 소스/도구 간의
표준화된 통합 프로토콜입니다.

## 핵심 기능

| 기능 | 제어 주체 | 설명 |
|------|-----------|------|
| **Tools** | Model-controlled | LLM이 자동으로 발견/호출하는 도구 |
| **Resources** | Application-driven | 컨텍스트 제공용 데이터 (파일, DB 스키마 등) |
| **Prompts** | User-controlled | 사용자가 선택하는 템플릿 |
| **Sampling** | Server-initiated | 서버가 LLM 요청을 시작 |
| **Roots** | Client-defined | 작업 디렉토리 정의 |
| **Elicitation** | Server-initiated | 서버가 추가 정보 요청 |

## 프로토콜 구조

- **전송**: JSON-RPC 2.0
- **연결**: Stdio (프로세스), HTTP/SSE, WebSocket

## Tools API

### tools/list

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/list",
  "params": { "cursor": "optional" }
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "tools": [
      {
        "name": "get_weather",
        "title": "Weather Provider",
        "description": "Get weather for a location",
        "inputSchema": {
          "type": "object",
          "properties": {
            "location": { "type": "string" }
          },
          "required": ["location"]
        },
        "outputSchema": { ... }  // optional
      }
    ],
    "nextCursor": "next-page"
  }
}
```

### tools/call

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "get_weather",
    "arguments": { "location": "Seoul" }
  }
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "result": {
    "content": [
      { "type": "text", "text": "Temperature: 15°C" }
    ],
    "structuredContent": { ... },  // optional
    "isError": false
  }
}
```

### Content Types

| Type | 설명 |
|------|------|
| `text` | 텍스트 콘텐츠 |
| `image` | Base64 이미지 |
| `audio` | Base64 오디오 |
| `resource_link` | 리소스 URI 링크 |
| `resource` | 임베디드 리소스 |

## Resources API

### resources/list

```json
{
  "method": "resources/list",
  "params": { "cursor": "optional" }
}
```

### resources/read

```json
{
  "method": "resources/read",
  "params": { "uri": "file:///path/to/file" }
}
```

## Prompts API

### prompts/list

```json
{
  "method": "prompts/list",
  "params": { "cursor": "optional" }
}
```

### prompts/get

```json
{
  "method": "prompts/get",
  "params": {
    "name": "code_review",
    "arguments": { "code": "..." }
  }
}
```

## Capabilities 협상

초기화 시 서버가 지원하는 기능 선언:

```json
{
  "capabilities": {
    "tools": { "listChanged": true },
    "resources": { "subscribe": true, "listChanged": true },
    "prompts": { "listChanged": true }
  }
}
```

## 알림 (Notifications)

| 메서드 | 설명 |
|--------|------|
| `notifications/tools/list_changed` | 도구 목록 변경 |
| `notifications/resources/list_changed` | 리소스 목록 변경 |
| `notifications/prompts/list_changed` | 프롬프트 목록 변경 |

## 에러 처리

1. **프로토콜 에러**: JSON-RPC 표준 에러
   - `-32602`: Invalid params (알 수 없는 도구, 잘못된 인자)
   - `-32603`: Internal error

2. **도구 실행 에러**: `isError: true`로 결과 반환
   - API 실패, 비즈니스 로직 에러

## 보안 고려사항

- 모든 입력 검증 필수
- 민감한 작업은 사용자 확인 (HITL)
- 도구 호출 Rate limiting
- 출력 sanitization

## 참고 링크

- [공식 스펙](https://modelcontextprotocol.io/specification/2025-06-18)
- [GitHub](https://github.com/modelcontextprotocol/modelcontextprotocol)
