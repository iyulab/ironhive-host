using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace IronHive.Cli.Core.Integration;

/// <summary>
/// Provides AI tools that integrate with CodeBeaker for code execution.
/// </summary>
public class CodeBeakerTools : IAsyncDisposable
{
    private readonly ICodeExecutionProvider _provider;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Creates code execution tools with the given provider.
    /// </summary>
    /// <param name="provider">Code execution provider</param>
    public CodeBeakerTools(ICodeExecutionProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <summary>
    /// Creates code execution tools with the default WebSocket provider.
    /// </summary>
    /// <param name="endpoint">WebSocket endpoint (e.g., ws://localhost:5039/ws/jsonrpc)</param>
    public CodeBeakerTools(string endpoint = "ws://localhost:5039/ws/jsonrpc")
    {
        _provider = new WebSocketCodeExecutionProvider(endpoint);
    }

    /// <summary>
    /// Gets all code execution AI tools.
    /// </summary>
    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            CreateExecuteTool(),
            CreateSessionTool(),
            CreateInstallPackagesTool()
        ];
    }

    /// <summary>
    /// Creates a tool for executing code.
    /// </summary>
    private AIFunction CreateExecuteTool()
    {
        return AIFunctionFactory.Create(
            async (string code, string language, string? sessionId, int timeoutSeconds) =>
            {
                try
                {
                    var result = await _provider.ExecuteAsync(
                        code,
                        language,
                        sessionId,
                        timeoutSeconds > 0 ? timeoutSeconds : 30);

                    return JsonSerializer.Serialize(result, JsonOptions);
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new ExecutionResult
                    {
                        Success = false,
                        Error = ex.Message
                    }, JsonOptions);
                }
            },
            name: "code_execute",
            description: "Executes code in a sandboxed environment. Supports: javascript, typescript, python, csharp. Returns stdout, stderr, and execution result.");
    }

    /// <summary>
    /// Creates a tool for managing execution sessions.
    /// </summary>
    private AIFunction CreateSessionTool()
    {
        return AIFunctionFactory.Create(
            async (string action, string? sessionId, string? language) =>
            {
                try
                {
                    return action.ToLowerInvariant() switch
                    {
                        "create" => JsonSerializer.Serialize(
                            await _provider.CreateSessionAsync(language ?? "javascript"),
                            JsonOptions),
                        "destroy" when sessionId != null => JsonSerializer.Serialize(
                            await _provider.DestroySessionAsync(sessionId),
                            JsonOptions),
                        "list" => JsonSerializer.Serialize(
                            await _provider.ListSessionsAsync(),
                            JsonOptions),
                        _ => JsonSerializer.Serialize(new SessionResult
                        {
                            Success = false,
                            Error = "Invalid action. Use: create, destroy, or list"
                        }, JsonOptions)
                    };
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new SessionResult
                    {
                        Success = false,
                        Error = ex.Message
                    }, JsonOptions);
                }
            },
            name: "code_session",
            description: "Manages code execution sessions. Actions: 'create' (new session), 'destroy' (end session), 'list' (show active sessions). Sessions maintain state between executions.");
    }

    /// <summary>
    /// Creates a tool for installing packages in a session.
    /// </summary>
    private AIFunction CreateInstallPackagesTool()
    {
        return AIFunctionFactory.Create(
            async (string sessionId, string[] packages) =>
            {
                try
                {
                    var result = await _provider.InstallPackagesAsync(sessionId, packages);
                    return JsonSerializer.Serialize(result, JsonOptions);
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new PackageInstallResult
                    {
                        Success = false,
                        Error = ex.Message
                    }, JsonOptions);
                }
            },
            name: "code_install_packages",
            description: "Installs packages in an execution session. Provide package names as an array (e.g., ['lodash', 'axios'] for JavaScript).");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_provider is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Interface for code execution providers.
/// </summary>
public interface ICodeExecutionProvider
{
    /// <summary>
    /// Executes code.
    /// </summary>
    Task<ExecutionResult> ExecuteAsync(
        string code,
        string language,
        string? sessionId,
        int timeoutSeconds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an execution session.
    /// </summary>
    Task<SessionResult> CreateSessionAsync(
        string language,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Destroys an execution session.
    /// </summary>
    Task<SessionResult> DestroySessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists active sessions.
    /// </summary>
    Task<SessionListResult> ListSessionsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Installs packages in a session.
    /// </summary>
    Task<PackageInstallResult> InstallPackagesAsync(
        string sessionId,
        string[] packages,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// WebSocket JSON-RPC provider for CodeBeaker.
/// </summary>
public class WebSocketCodeExecutionProvider : ICodeExecutionProvider, IAsyncDisposable
{
    private readonly string _endpoint;
    private ClientWebSocket? _webSocket;
    private int _requestId;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public WebSocketCodeExecutionProvider(string endpoint)
    {
        _endpoint = endpoint;
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken = default)
    {
        if (_webSocket?.State == WebSocketState.Open)
        {
            return;
        }

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_webSocket?.State == WebSocketState.Open)
            {
                return;
            }

            _webSocket?.Dispose();
            _webSocket = new ClientWebSocket();
            await _webSocket.ConnectAsync(new Uri(_endpoint), cancellationToken);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task<JsonDocument> SendRequestAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);

        var request = new
        {
            jsonrpc = "2.0",
            id = Interlocked.Increment(ref _requestId),
            method,
            @params = parameters
        };

        var requestJson = JsonSerializer.Serialize(request, JsonOptions);
        var requestBytes = Encoding.UTF8.GetBytes(requestJson);

        await _webSocket!.SendAsync(
            new ArraySegment<byte>(requestBytes),
            WebSocketMessageType.Text,
            true,
            cancellationToken);

        var buffer = new byte[8192];
        var result = await _webSocket.ReceiveAsync(
            new ArraySegment<byte>(buffer),
            cancellationToken);

        var responseJson = Encoding.UTF8.GetString(buffer, 0, result.Count);
        return JsonDocument.Parse(responseJson);
    }

    public async Task<ExecutionResult> ExecuteAsync(
        string code,
        string language,
        string? sessionId,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("session.execute", new
        {
            sessionId,
            language,
            code,
            timeout = timeoutSeconds * 1000
        }, cancellationToken);

        var root = response.RootElement;

        if (root.TryGetProperty("error", out var error))
        {
            return new ExecutionResult
            {
                Success = false,
                Error = error.GetProperty("message").GetString()
            };
        }

        var result = root.GetProperty("result");
        return new ExecutionResult
        {
            Success = true,
            Stdout = result.TryGetProperty("stdout", out var stdout) ? stdout.GetString() : null,
            Stderr = result.TryGetProperty("stderr", out var stderr) ? stderr.GetString() : null,
            Result = result.TryGetProperty("result", out var res) ? res.ToString() : null,
            ExecutionTimeMs = result.TryGetProperty("executionTimeMs", out var time) ? time.GetInt32() : 0
        };
    }

    public async Task<SessionResult> CreateSessionAsync(
        string language,
        CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("session.create", new
        {
            language,
            runtimePreference = "Balanced"
        }, cancellationToken);

        var root = response.RootElement;

        if (root.TryGetProperty("error", out var error))
        {
            return new SessionResult
            {
                Success = false,
                Error = error.GetProperty("message").GetString()
            };
        }

        var result = root.GetProperty("result");
        return new SessionResult
        {
            Success = true,
            SessionId = result.TryGetProperty("sessionId", out var id) ? id.GetString() : null,
            Language = language
        };
    }

    public async Task<SessionResult> DestroySessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("session.destroy", new
        {
            sessionId
        }, cancellationToken);

        var root = response.RootElement;

        if (root.TryGetProperty("error", out var error))
        {
            return new SessionResult
            {
                Success = false,
                Error = error.GetProperty("message").GetString()
            };
        }

        return new SessionResult
        {
            Success = true,
            SessionId = sessionId
        };
    }

    public async Task<SessionListResult> ListSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("session.list", null, cancellationToken);

        var root = response.RootElement;

        if (root.TryGetProperty("error", out var error))
        {
            return new SessionListResult
            {
                Success = false,
                Error = error.GetProperty("message").GetString()
            };
        }

        var result = root.GetProperty("result");
        var sessions = new List<SessionInfo>();

        if (result.TryGetProperty("sessions", out var sessionsArray))
        {
            foreach (var session in sessionsArray.EnumerateArray())
            {
                sessions.Add(new SessionInfo
                {
                    SessionId = session.TryGetProperty("sessionId", out var id) ? id.GetString() : null,
                    Language = session.TryGetProperty("language", out var lang) ? lang.GetString() : null,
                    CreatedAt = session.TryGetProperty("createdAt", out var created) ? created.GetString() : null
                });
            }
        }

        return new SessionListResult
        {
            Success = true,
            Sessions = sessions
        };
    }

    public async Task<PackageInstallResult> InstallPackagesAsync(
        string sessionId,
        string[] packages,
        CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("session.installPackages", new
        {
            sessionId,
            packages
        }, cancellationToken);

        var root = response.RootElement;

        if (root.TryGetProperty("error", out var error))
        {
            return new PackageInstallResult
            {
                Success = false,
                Error = error.GetProperty("message").GetString()
            };
        }

        var result = root.GetProperty("result");
        return new PackageInstallResult
        {
            Success = true,
            InstalledPackages = packages.ToList()
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_webSocket != null)
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Closing",
                    CancellationToken.None);
            }
            _webSocket.Dispose();
        }
        _connectionLock.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Result of code execution.
/// </summary>
public class ExecutionResult
{
    public bool Success { get; set; }
    public string? Stdout { get; set; }
    public string? Stderr { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
    public int ExecutionTimeMs { get; set; }
}

/// <summary>
/// Result of session operations.
/// </summary>
public class SessionResult
{
    public bool Success { get; set; }
    public string? SessionId { get; set; }
    public string? Language { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Result of listing sessions.
/// </summary>
public class SessionListResult
{
    public bool Success { get; set; }
    public List<SessionInfo>? Sessions { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Information about a session.
/// </summary>
public class SessionInfo
{
    public string? SessionId { get; set; }
    public string? Language { get; set; }
    public string? CreatedAt { get; set; }
}

/// <summary>
/// Result of package installation.
/// </summary>
public class PackageInstallResult
{
    public bool Success { get; set; }
    public List<string>? InstalledPackages { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// In-memory code execution provider for testing.
/// Supports basic JavaScript and Python evaluation.
/// </summary>
public class InMemoryCodeExecutionProvider : ICodeExecutionProvider
{
    private readonly Dictionary<string, SessionState> _sessions = new();
    private int _sessionCounter;

    /// <inheritdoc />
    public Task<ExecutionResult> ExecuteAsync(
        string code,
        string language,
        string? sessionId,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Get or create session state
            SessionState? session = null;
            if (sessionId != null && _sessions.TryGetValue(sessionId, out session))
            {
                // Use existing session
            }

            // Simple evaluation for testing
            var result = language.ToLowerInvariant() switch
            {
                "javascript" or "js" => EvaluateJavaScript(code, session),
                "python" or "py" => EvaluatePython(code, session),
                _ => $"Unsupported language: {language}"
            };

            sw.Stop();

            return Task.FromResult(new ExecutionResult
            {
                Success = true,
                Stdout = result,
                ExecutionTimeMs = (int)sw.ElapsedMilliseconds
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Task.FromResult(new ExecutionResult
            {
                Success = false,
                Error = ex.Message,
                ExecutionTimeMs = (int)sw.ElapsedMilliseconds
            });
        }
    }

    /// <inheritdoc />
    public Task<SessionResult> CreateSessionAsync(
        string language,
        CancellationToken cancellationToken = default)
    {
        var sessionId = $"session_{Interlocked.Increment(ref _sessionCounter)}";
        _sessions[sessionId] = new SessionState
        {
            SessionId = sessionId,
            Language = language,
            Variables = new Dictionary<string, object?>(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        return Task.FromResult(new SessionResult
        {
            Success = true,
            SessionId = sessionId,
            Language = language
        });
    }

    /// <inheritdoc />
    public Task<SessionResult> DestroySessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var removed = _sessions.Remove(sessionId);

        return Task.FromResult(new SessionResult
        {
            Success = removed,
            SessionId = sessionId,
            Error = removed ? null : "Session not found"
        });
    }

    /// <inheritdoc />
    public Task<SessionListResult> ListSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        var sessions = _sessions.Values.Select(s => new SessionInfo
        {
            SessionId = s.SessionId,
            Language = s.Language,
            CreatedAt = s.CreatedAt.ToString("O")
        }).ToList();

        return Task.FromResult(new SessionListResult
        {
            Success = true,
            Sessions = sessions
        });
    }

    /// <inheritdoc />
    public Task<PackageInstallResult> InstallPackagesAsync(
        string sessionId,
        string[] packages,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return Task.FromResult(new PackageInstallResult
            {
                Success = false,
                Error = "Session not found"
            });
        }

        session.InstalledPackages.AddRange(packages);

        return Task.FromResult(new PackageInstallResult
        {
            Success = true,
            InstalledPackages = packages.ToList()
        });
    }

    private static string EvaluateJavaScript(string code, SessionState? session)
    {
        // Very simple evaluation for testing - just echo the code
        if (code.StartsWith("console.log(", StringComparison.Ordinal))
        {
            var content = code.Replace("console.log(", "").TrimEnd(')', ';');
            return content.Trim('"', '\'');
        }

        return $"[JS] Executed: {code.Length} chars";
    }

    private static string EvaluatePython(string code, SessionState? session)
    {
        // Very simple evaluation for testing - just echo the code
        if (code.StartsWith("print(", StringComparison.Ordinal))
        {
            var content = code.Replace("print(", "").TrimEnd(')', '\n');
            return content.Trim('"', '\'');
        }

        return $"[Python] Executed: {code.Length} chars";
    }

    private sealed class SessionState
    {
        public string SessionId { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public Dictionary<string, object?> Variables { get; set; } = new();
        public List<string> InstalledPackages { get; set; } = new();
        public DateTimeOffset CreatedAt { get; set; }
    }
}
