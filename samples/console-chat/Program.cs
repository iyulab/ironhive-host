/**
 * Console Chat Sample
 *
 * IronHive.Cli.Core 라이브러리를 직접 통합하여
 * 간단한 대화형 콘솔 채팅을 구현합니다.
 *
 * 목적: Core 라이브러리 API 완성도 검증
 */

using System.Runtime.CompilerServices;
using DotNetEnv;
using IronHive.Cli.Core.Agent;
using IronHive.Cli.Core.Extensions;
using IronHive.Cli.Core.Session;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

Console.WriteLine("=== IronHive Console Chat Sample ===");
Console.WriteLine("Core 라이브러리 통합 검증용 샘플\n");

// .env 파일 로드 (상위 → 현재 순서로 누적 로드, 하위가 오버라이드)
var envFiles = FindAllEnvFiles(Directory.GetCurrentDirectory());
foreach (var envFile in envFiles)
{
    Env.Load(envFile);
    Console.WriteLine($"설정 로드: {envFile}");
}
if (envFiles.Count > 0)
{
    Console.WriteLine();
}

// GPUSTACK_* → OPENAI_* 매핑 (fallback)
MapGpuStackToOpenAI();

static void MapGpuStackToOpenAI()
{
    // OPENAI_API_KEY가 없고 GPUSTACK_API_KEY가 있으면 매핑
    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENAI_API_KEY")))
    {
        var gpuKey = Environment.GetEnvironmentVariable("GPUSTACK_API_KEY");
        if (!string.IsNullOrEmpty(gpuKey))
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", gpuKey);
        }
    }

    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENAI_ENDPOINT")))
    {
        var gpuEndpoint = Environment.GetEnvironmentVariable("GPUSTACK_ENDPOINT");
        if (!string.IsNullOrEmpty(gpuEndpoint))
        {
            Environment.SetEnvironmentVariable("OPENAI_ENDPOINT", gpuEndpoint.TrimEnd('/') + "/v1");
        }
    }

    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENAI_MODEL")))
    {
        var gpuModel = Environment.GetEnvironmentVariable("GPUSTACK_MODEL");
        if (!string.IsNullOrEmpty(gpuModel))
        {
            Environment.SetEnvironmentVariable("OPENAI_MODEL", gpuModel);
        }
    }
}

static List<string> FindAllEnvFiles(string startDir)
{
    var files = new List<string>();
    var dir = new DirectoryInfo(startDir);

    // 루트까지 모든 .env 파일 수집
    while (dir != null)
    {
        var envLocalPath = Path.Combine(dir.FullName, ".env.local");
        var envPath = Path.Combine(dir.FullName, ".env");

        if (File.Exists(envLocalPath))
        {
            files.Add(envLocalPath);
        }
        else if (File.Exists(envPath))
        {
            files.Add(envPath);
        }

        dir = dir.Parent;
    }

    // 상위부터 로드하도록 역순 (상위 설정 → 하위 설정이 오버라이드)
    files.Reverse();
    return files;
}

// DI 컨테이너 설정
var services = new ServiceCollection();

// API 키 확인
var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrEmpty(apiKey))
{
    Console.WriteLine("OPENAI_API_KEY 환경변수가 설정되지 않았습니다.");
    Console.WriteLine("Mock 클라이언트를 사용합니다.\n");

    services.AddIronHive(new MockChatClient(), "You are a helpful assistant.");
}
else
{
    var endpoint = Environment.GetEnvironmentVariable("OPENAI_ENDPOINT");
    var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";

    // CORE-G1 해결: 간편 프로바이더 헬퍼 사용
    if (string.IsNullOrEmpty(endpoint) || endpoint == "https://api.openai.com/v1")
    {
        // OpenAI 공식 API
        Console.WriteLine($"Provider: OpenAI");
        Console.WriteLine($"Model: {model}\n");
        services.AddIronHiveWithOpenAI(apiKey, model, "You are a helpful assistant. Respond concisely.");
    }
    else
    {
        // OpenAI 호환 API (GpuStack, vLLM, LiteLLM 등)
        Console.WriteLine($"Provider: OpenAI Compatible ({endpoint})");
        Console.WriteLine($"Model: {model}\n");
        services.AddIronHiveWithOpenAICompatible(endpoint, apiKey, model, "You are a helpful assistant. Respond concisely.");
    }
}

var serviceProvider = services.BuildServiceProvider();
var agentLoop = serviceProvider.GetRequiredService<IAgentLoop>();
var sessionManager = serviceProvider.GetRequiredService<ISessionManager>();

// CORE-G3 해결: 세션 통합 API 사용 예시
// 최근 세션이 있으면 이어서, 없으면 새로 생성
var currentSession = await agentLoop.LoadOrCreateSessionAsync(
    sessionManager,
    Directory.GetCurrentDirectory(),
    model: "default",
    continueLatest: true);

Console.WriteLine($"세션: {currentSession.Id}");
Console.WriteLine("대화를 시작합니다. 종료하려면 'exit' 또는 'quit'을 입력하세요.");
Console.WriteLine("명령어: /new, /sessions, /continue <id>, /help\n");
Console.WriteLine(new string('-', 50));

var conversationCount = 0;

while (true)
{
    Console.Write("\nYou: ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
    {
        continue;
    }

    if (input.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("quit", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("\n대화를 종료합니다.");
        break;
    }

    // 명령어 처리
    if (input.StartsWith('/'))
    {
        currentSession = await HandleCommand(input, agentLoop, sessionManager, currentSession);
        continue;
    }

    try
    {
        Console.Write("\nIronHive: ");
        conversationCount++;

        // 스트리밍 응답
        var hasThinking = false;
        await foreach (var chunk in agentLoop.RunStreamingAsync(input))
        {
            // CORE-G2 해결: ThinkingDelta 지원됨
            if (!string.IsNullOrEmpty(chunk.ThinkingDelta))
            {
                if (!hasThinking)
                {
                    hasThinking = true;
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.Write("[Thinking] ");
                }
                Console.Write(chunk.ThinkingDelta);
            }

            if (!string.IsNullOrEmpty(chunk.TextDelta))
            {
                if (hasThinking)
                {
                    Console.ResetColor();
                    Console.WriteLine();
                    hasThinking = false;
                }
                Console.Write(chunk.TextDelta);
            }
        }
        Console.ResetColor();

        Console.WriteLine();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\nError: {ex.Message}");
    }
}

Console.WriteLine($"\n총 {conversationCount}번의 대화가 있었습니다.");

// 정리
if (serviceProvider is IAsyncDisposable asyncDisposable)
{
    await asyncDisposable.DisposeAsync();
}

static async Task<IronHive.Cli.Core.Session.Session> HandleCommand(
    string command,
    IAgentLoop agentLoop,
    ISessionManager sessionManager,
    IronHive.Cli.Core.Session.Session currentSession)
{
    var parts = command.Split(' ', 2);
    var cmd = parts[0].ToLowerInvariant();

    switch (cmd)
    {
        case "/sessions":
            var sessions = await sessionManager.ListSessionsAsync(Directory.GetCurrentDirectory(), 10);
            Console.WriteLine($"\n세션 목록 ({sessions.Count}개):");
            foreach (var s in sessions)
            {
                var marker = s.Id == currentSession.Id ? " *현재*" : "";
                Console.WriteLine($"  - {s.Id} ({s.CreatedAt:yyyy-MM-dd HH:mm}){marker}");
            }
            break;

        case "/new":
            var newSession = await sessionManager.CreateSessionAsync(Directory.GetCurrentDirectory(), "default");
            agentLoop.ClearHistory();
            Console.WriteLine($"\n새 세션 생성됨: {newSession.Id}");
            return newSession;

        case "/continue":
            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
            {
                Console.WriteLine("\n사용법: /continue <session-id>");
                break;
            }
            try
            {
                // CORE-G3 해결: 통합 세션 로드 API
                var loadedSession = await agentLoop.LoadSessionAsync(sessionManager, parts[1].Trim());
                Console.WriteLine($"\n세션 복원됨: {loadedSession.Id}");
                Console.WriteLine($"이전 대화 {agentLoop.History.Count}개 메시지 로드됨");
                return loadedSession;
            }
            catch (SessionNotFoundException)
            {
                Console.WriteLine($"\n세션을 찾을 수 없습니다: {parts[1]}");
            }
            break;

        case "/help":
            Console.WriteLine("\n명령어:");
            Console.WriteLine("  /sessions       - 세션 목록");
            Console.WriteLine("  /new            - 새 세션 생성");
            Console.WriteLine("  /continue <id>  - 기존 세션 이어하기");
            Console.WriteLine("  /help           - 도움말");
            Console.WriteLine("  exit            - 종료");
            break;

        default:
            Console.WriteLine($"\n알 수 없는 명령어: {cmd}. /help로 도움말 확인");
            break;
    }

    return currentSession;
}

/**
 * 발견된 Core 라이브러리 Gap:
 *
 * Gap CORE-G1: ✅ 해결됨 - AddIronHiveWithOpenAI/AddIronHiveWithOpenAICompatible/AddIronHiveWithOllama
 * Gap CORE-G2: ✅ 해결됨 - AgentResponseChunk.ThinkingDelta 추가
 * Gap CORE-G3: ✅ 해결됨 - agentLoop.LoadSessionAsync() / LoadOrCreateSessionAsync() 통합 API
 *
 * Gap CORE-G4: 사용량 통계 접근 API
 *   - 현재 세션의 총 토큰 사용량을 쉽게 조회하는 방법
 */

// Mock client for testing without API key
sealed class MockChatClient : IChatClient
{
    public ChatClientMetadata Metadata => new("mock", new Uri("http://localhost"), "mock");

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var lastMsg = messages.LastOrDefault()?.Text ?? "";
        var response = $"[Mock] 입력 받음: \"{lastMsg}\"\n이것은 테스트 응답입니다. API 키를 설정하면 실제 LLM 응답을 받을 수 있습니다.";
        return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, response)]));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        var text = response.Messages.FirstOrDefault()?.Text ?? "";

        foreach (var word in text.Split(' '))
        {
            await Task.Delay(50, cancellationToken);
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = [new TextContent(word + " ")]
            };
        }
    }

    public void Dispose() { }
    public object? GetService(Type serviceType, object? key = null) => null;
}
