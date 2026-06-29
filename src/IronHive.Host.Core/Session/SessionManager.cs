using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace IronHive.Host.Core.Session;

/// <summary>
/// Default implementation of session manager with JSONL transcript persistence.
/// </summary>
public class SessionManager : ISessionManager
{
    private readonly string _baseDirectory;
    private readonly JsonSerializerOptions _jsonOptions;
    private static readonly string DefaultBaseDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".ironhive");

    /// <summary>
    /// Creates a new session manager with the default base directory (~/.ironhive).
    /// </summary>
    public SessionManager() : this(DefaultBaseDirectory)
    {
    }

    /// <summary>
    /// Creates a new session manager with a custom base directory.
    /// </summary>
    /// <param name="baseDirectory">Base directory for session storage</param>
    public SessionManager(string baseDirectory)
    {
        _baseDirectory = baseDirectory;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true
        };
    }

    /// <inheritdoc />
    public async Task<Session> CreateSessionAsync(string projectPath, string model)
    {
        var projectHash = ComputeProjectHash(projectPath);
        var sessionId = GenerateSessionId();
        var projectDir = GetProjectDirectory(projectHash);

        Directory.CreateDirectory(projectDir);

        var transcriptPath = Path.Combine(projectDir, $"{sessionId}.jsonl");

        var session = new Session
        {
            Id = sessionId,
            ProjectPath = projectPath,
            TranscriptPath = transcriptPath,
            Model = model,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = SessionStatus.Active,
            ProjectHash = projectHash
        };

        // Write session start entry
        var startEntry = new SessionStartEntry
        {
            Timestamp = session.CreatedAt,
            SessionId = sessionId,
            Model = model,
            ProjectPath = projectPath,
            Cwd = Directory.GetCurrentDirectory(),
            Version = GetVersion()
        };

        await AppendEntryAsync(transcriptPath, startEntry);

        return session;
    }

    /// <inheritdoc />
    public async Task<Session?> LoadSessionAsync(string sessionId)
    {
        // Search all project directories for the session
        var projectsDir = Path.Combine(_baseDirectory, "projects");
        if (!Directory.Exists(projectsDir))
        {
            return null;
        }

        foreach (var projectDir in Directory.GetDirectories(projectsDir))
        {
            var transcriptPath = Path.Combine(projectDir, $"{sessionId}.jsonl");
            if (File.Exists(transcriptPath))
            {
                return await LoadSessionFromTranscriptAsync(transcriptPath, Path.GetFileName(projectDir));
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<Session?> GetLatestSessionAsync(string projectPath)
    {
        var projectHash = ComputeProjectHash(projectPath);
        var projectDir = GetProjectDirectory(projectHash);

        if (!Directory.Exists(projectDir))
        {
            return null;
        }

        var transcriptFiles = Directory.GetFiles(projectDir, "*.jsonl")
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
            .ToList();

        if (transcriptFiles.Count == 0)
        {
            return null;
        }

        return await LoadSessionFromTranscriptAsync(transcriptFiles[0], projectHash);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionSummary>> ListSessionsAsync(string projectPath, int limit = 10)
    {
        var projectHash = ComputeProjectHash(projectPath);
        var projectDir = GetProjectDirectory(projectHash);

        if (!Directory.Exists(projectDir))
        {
            return [];
        }

        var transcriptFiles = Directory.GetFiles(projectDir, "*.jsonl")
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
            .Take(limit)
            .ToList();

        var summaries = new List<SessionSummary>();

        foreach (var file in transcriptFiles)
        {
            var summary = await GetSessionSummaryAsync(file);
            if (summary != null)
            {
                summaries.Add(summary);
            }
        }

        return summaries;
    }

    /// <inheritdoc />
    public async Task SaveUserMessageAsync(Session session, string content)
    {
        var entry = new UserMessageEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Content = content
        };
        await AppendEntryAsync(session.TranscriptPath, entry);
    }

    /// <inheritdoc />
    public async Task SaveAssistantMessageAsync(Session session, string content)
    {
        var entry = new AssistantMessageEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Content = content
        };
        await AppendEntryAsync(session.TranscriptPath, entry);
    }

    /// <inheritdoc />
    public async Task SaveToolUseAsync(Session session, string tool, object input, string toolUseId)
    {
        var entry = new ToolUseEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Tool = tool,
            Input = input,
            ToolUseId = toolUseId
        };
        await AppendEntryAsync(session.TranscriptPath, entry);
    }

    /// <inheritdoc />
    public async Task SaveToolResultAsync(Session session, string toolUseId, string output, bool isError = false)
    {
        var entry = new ToolResultEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            ToolUseId = toolUseId,
            Output = output,
            IsError = isError
        };
        await AppendEntryAsync(session.TranscriptPath, entry);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChatMessage>> RestoreContextAsync(Session session)
    {
        var entries = await ReadEntriesAsync(session.TranscriptPath);
        var messages = new List<ChatMessage>();

        foreach (var entry in entries)
        {
            switch (entry)
            {
                case UserMessageEntry userMsg:
                    messages.Add(new ChatMessage(ChatRole.User, userMsg.Content));
                    break;

                case AssistantMessageEntry assistantMsg:
                    messages.Add(new ChatMessage(ChatRole.Assistant, assistantMsg.Content));
                    break;

                case ToolUseEntry toolUse:
                    // Tool uses are typically embedded in assistant messages in the actual API
                    // For restoration, we note them but the actual handling depends on the provider
                    break;

                case ToolResultEntry toolResult:
                    // Tool results are typically embedded in the conversation flow
                    // For restoration, we note them but the actual handling depends on the provider
                    break;
            }
        }

        return messages;
    }

    /// <inheritdoc />
    public async Task<Session> ForkSessionAsync(Session session, int? forkPoint = null)
    {
        var entries = await ReadEntriesAsync(session.TranscriptPath);

        // Determine fork point
        var entriesToCopy = forkPoint.HasValue
            ? entries.Take(forkPoint.Value + 1).ToList()
            : entries;

        // Create new session
        var newSessionId = GenerateSessionId();
        var newTranscriptPath = Path.Combine(
            Path.GetDirectoryName(session.TranscriptPath)!,
            $"{newSessionId}.jsonl");

        var newSession = session with
        {
            Id = newSessionId,
            TranscriptPath = newTranscriptPath,
            CreatedAt = DateTimeOffset.UtcNow,
            ResumedAt = DateTimeOffset.UtcNow,
            Status = SessionStatus.Active
        };

        // Write entries to new transcript
        foreach (var entry in entriesToCopy)
        {
            await AppendEntryAsync(newTranscriptPath, entry);
        }

        // Add resume entry
        var resumeEntry = new SessionResumeEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            SessionId = newSessionId,
            ForkedFrom = session.Id
        };
        await AppendEntryAsync(newTranscriptPath, resumeEntry);

        // Mark original session as forked
        await EndSessionAsync(session, "forked");

        return newSession;
    }

    /// <inheritdoc />
    public async Task EndSessionAsync(Session session, string reason)
    {
        var entry = new SessionEndEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Reason = reason
        };
        await AppendEntryAsync(session.TranscriptPath, entry);
    }

    /// <inheritdoc />
    public Task DeleteSessionAsync(string sessionId)
    {
        var projectsDir = Path.Combine(_baseDirectory, "projects");
        if (!Directory.Exists(projectsDir))
        {
            return Task.CompletedTask;
        }

        foreach (var projectDir in Directory.GetDirectories(projectsDir))
        {
            var transcriptPath = Path.Combine(projectDir, $"{sessionId}.jsonl");
            if (File.Exists(transcriptPath))
            {
                File.Delete(transcriptPath);

                // Also delete subagent directory if exists
                var subagentDir = Path.Combine(projectDir, sessionId, "subagents");
                if (Directory.Exists(subagentDir))
                {
                    Directory.Delete(subagentDir, true);
                }

                // Delete session directory if empty
                var sessionDir = Path.Combine(projectDir, sessionId);
                if (Directory.Exists(sessionDir) && !Directory.EnumerateFileSystemEntries(sessionDir).Any())
                {
                    Directory.Delete(sessionDir);
                }

                break;
            }
        }

        return Task.CompletedTask;
    }

    private async Task AppendEntryAsync(string transcriptPath, TranscriptEntry entry)
    {
        // Serialize as base type to include polymorphic type discriminator
        var json = JsonSerializer.Serialize<TranscriptEntry>(entry, _jsonOptions);
        await File.AppendAllTextAsync(transcriptPath, json + Environment.NewLine);
    }

    private async Task<List<TranscriptEntry>> ReadEntriesAsync(string transcriptPath)
    {
        var entries = new List<TranscriptEntry>();

        if (!File.Exists(transcriptPath))
        {
            return entries;
        }

        var lines = await File.ReadAllLinesAsync(transcriptPath);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var entry = JsonSerializer.Deserialize<TranscriptEntry>(line, _jsonOptions);
                if (entry != null)
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException)
            {
                // Skip malformed entries
            }
        }

        return entries;
    }

    private async Task<Session?> LoadSessionFromTranscriptAsync(string transcriptPath, string projectHash)
    {
        var entries = await ReadEntriesAsync(transcriptPath);

        var startEntry = entries.OfType<SessionStartEntry>().FirstOrDefault();
        if (startEntry == null)
        {
            return null;
        }

        var endEntry = entries.OfType<SessionEndEntry>().LastOrDefault();
        var resumeEntry = entries.OfType<SessionResumeEntry>().LastOrDefault();

        var status = endEntry?.Reason switch
        {
            "user_exit" or "completed" => SessionStatus.Ended,
            "error" => SessionStatus.Error,
            "forked" => SessionStatus.Forked,
            _ => SessionStatus.Active
        };

        return new Session
        {
            Id = startEntry.SessionId,
            ProjectPath = startEntry.ProjectPath,
            TranscriptPath = transcriptPath,
            Model = startEntry.Model,
            CreatedAt = startEntry.Timestamp,
            ResumedAt = resumeEntry?.Timestamp,
            Status = status,
            ProjectHash = projectHash
        };
    }

    private async Task<SessionSummary?> GetSessionSummaryAsync(string transcriptPath)
    {
        var entries = await ReadEntriesAsync(transcriptPath);

        var startEntry = entries.OfType<SessionStartEntry>().FirstOrDefault();
        if (startEntry == null)
        {
            return null;
        }

        var endEntry = entries.OfType<SessionEndEntry>().LastOrDefault();
        var lastEntry = entries.LastOrDefault();
        var firstUserMsg = entries.OfType<UserMessageEntry>().FirstOrDefault();

        var status = endEntry?.Reason switch
        {
            "user_exit" or "completed" => SessionStatus.Ended,
            "error" => SessionStatus.Error,
            "forked" => SessionStatus.Forked,
            _ => SessionStatus.Active
        };

        var messageCount = entries.Count(e => e is UserMessageEntry or AssistantMessageEntry);

        return new SessionSummary
        {
            Id = startEntry.SessionId,
            CreatedAt = startEntry.Timestamp,
            LastActiveAt = lastEntry?.Timestamp ?? startEntry.Timestamp,
            Status = status,
            Model = startEntry.Model,
            MessageCount = messageCount,
            FirstUserMessage = TruncateMessage(firstUserMsg?.Content, 100)
        };
    }

    private string GetProjectDirectory(string projectHash)
    {
        return Path.Combine(_baseDirectory, "projects", projectHash);
    }

    private static string ComputeProjectHash(string projectPath)
    {
        // Normalize path
        var normalizedPath = Path.GetFullPath(projectPath).ToLowerInvariant();

        var bytes = Encoding.UTF8.GetBytes(normalizedPath);
        var hash = SHA256.HashData(bytes);

        // Use first 8 bytes (16 hex chars) for shorter, readable hash
        return Convert.ToHexString(hash[..8]).ToLowerInvariant();
    }

    private static string GenerateSessionId()
    {
        // Generate a URL-safe, sortable session ID
        // Format: timestamp-random (sortable by creation time)
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var random = Guid.NewGuid().ToString("N")[..8];
        return $"{timestamp:x}-{random}";
    }

    private static string? TruncateMessage(string? message, int maxLength)
    {
        if (string.IsNullOrEmpty(message))
        {
            return null;
        }

        if (message.Length <= maxLength)
        {
            return message;
        }

        return message[..(maxLength - 3)] + "...";
    }

    private static string? GetVersion()
    {
        var assembly = typeof(SessionManager).Assembly;
        var version = assembly.GetName().Version;
        return version?.ToString();
    }
}
