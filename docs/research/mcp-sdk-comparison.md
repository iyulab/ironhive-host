# MCP .NET SDK 비교 (P3-01)

> 조사일: 2026-01-27

## 후보 라이브러리

| 라이브러리 | 버전 | 유지관리 | 특징 |
|------------|------|----------|------|
| **ModelContextProtocol** | 0.6.0-preview.1 | Microsoft + 공식 | 공식 SDK, AIFunction 통합 |
| mcpdotnet | - | PederHP | 커뮤니티, 스펙 준수 목표 |

## 결정: 공식 C# SDK 사용

### 선택 이유

1. **공식 지원**: Microsoft와 협력하여 유지 관리
2. **Microsoft.Extensions.AI 통합**: `McpClientTool`이 `AIFunction` 상속
3. **활발한 개발**: 2025-06-18 스펙 지원, 1.0.0 안정 버전 목표
4. **완전한 기능**: Client/Server, Stdio/HTTP, DI 지원

### 설치

```bash
dotnet add package ModelContextProtocol --prerelease
```

### 핵심 API

```csharp
// 1. Client 생성 (Stdio)
var transport = new StdioClientTransport(new StdioClientTransportOptions
{
    Name = "my-server",
    Command = "npx",
    Arguments = ["-y", "@modelcontextprotocol/server-everything"],
});
var client = await McpClient.CreateAsync(transport);

// 2. 도구 목록 조회
foreach (var tool in await client.ListToolsAsync())
{
    Console.WriteLine($"{tool.Name}: {tool.Description}");
}

// 3. 도구 호출
var result = await client.CallToolAsync(
    "echo",
    new Dictionary<string, object?> { ["message"] = "Hello!" });

// 4. AIFunction으로 변환 (Microsoft.Extensions.AI 통합)
IList<AITool> tools = await client.GetAIToolsAsync();
```

### 패키지 구조

| 패키지 | 용도 |
|--------|------|
| `ModelContextProtocol` | 메인 (DI 확장 포함) |
| `ModelContextProtocol.Core` | 최소 의존성 (Client/Server 핵심) |
| `ModelContextProtocol.AspNetCore` | HTTP 서버 지원 |

### 의존성

- .NET 8.0+ (net8.0, net9.0 지원)
- Microsoft.Extensions.DependencyInjection
- System.Text.Json

## 참고 링크

- [GitHub](https://github.com/modelcontextprotocol/csharp-sdk)
- [NuGet](https://www.nuget.org/packages/ModelContextProtocol/)
- [문서](https://modelcontextprotocol.github.io/csharp-sdk/)
