using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using IronHive.Cli.Core.Providers;
using LMSupply.Generator;
using LMSupply.Generator.Abstractions;
using Microsoft.Extensions.AI;
using Xunit.Abstractions;
using MeaiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace IronHive.Cli.Tests.Providers;

/// <summary>
/// Decisive faithful replay for Filer ISSUE-219 (umbrella 2026-06-17).
///
/// Filer captured the byte-exact call-3 payload (env-gated FILER_DEBUG_CHAT_PAYLOAD=1):
/// the final-answer turn arrives as a SINGLE text delta (rawTextDeltas=1, firstTextMs=8636,
/// finishReason=stop) on llama-server b9605. Reproduced 3x on Filer's box.
///
/// The prior umbrella experiment (StreamingToolBufferingExperimentTests, lm-supply) hit the
/// RAW generator with a HAND-REBUILT approximation and streamed normally (63/115 chunks).
/// That reconstruction was unfaithful; this suite replays the exact captured payload THROUGH
/// THE REAL ADAPTER (LMSupplyChatClient), the one path segment Filer cannot observe.
///
/// Findings so far (2026-06-17, same machine as Filer's dev-repro):
///   - Through the adapter, faithful call-3 streams 100+ deltas on BOTH b9672 and b9605
///     (Filer's exact build) -> version axis EXCLUDED, adapter EXCLUDED, request shape EXCLUDED.
///   - ConvertOptions adds no hidden genOption (only Temperature/MaxTokens/Tools; rest = defaults).
///
/// Remaining gaps these tests close (per advisor) before escalating to Filer:
///   * AutoPath: Filer loads via gguf:auto (VRAM-sensitive: AdjustedContextLength / partial
///     GpuLayers offload), not the explicit alias. This test loads via gguf:auto and logs the
///     selection diagnostics so a degraded/VRAM-capped config can be spotted or ruled out.
///   * PersistentSequence: Filer's agentic loop runs call1->call2->call3 on ONE warm server
///     with a growing prefix (slot/KV continuation). The 5x call-3 reuse a warm server but send
///     the same standalone prompt; this test replays the real 3-call sequence on one generator.
///
/// Run: dotnet test --filter "FullyQualifiedName~Filer219Repro"
/// Requires a provisioned GGUF runtime + GPU (Category=Integration).
/// </summary>
[Trait("Category", "Integration")]
public class LMSupplyChatClientFiler219ReproTests
{
    private readonly ITestOutputHelper _output;

    public LMSupplyChatClientFiler219ReproTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Explicit-alias control: faithful call-3 through the adapter, 5x. Establishes the
    /// per-token-streaming baseline on a deterministic load path. (Run on b9672 and, via a
    /// state-file installedPath swap, on b9605 — both streamed 100+ deltas.)
    /// </summary>
    [Fact]
    public async Task Call3_FaithfulReplay_ThroughAdapter_TextDeltaCount()
    {
        await using var generator = await LocalGenerator.LoadAsync(
            "gguf:qwen3-fast",
            new GeneratorOptions { MaxContextLength = 16384 });

        LogModel(generator);
        var client = new LMSupplyChatClient(generator);
        var messages = LoadCallMessages(3);
        var options = BuildFilerOptions();

        var deltaCounts = new List<int>();
        for (var run = 1; run <= 5; run++)
        {
            deltaCounts.Add((await StreamOnceAsync(client, messages, options, $"run {run}")).TextDeltas);
        }
        Verdict("gguf:qwen3-fast", deltaCounts);

        Assert.Equal(6, messages.Count);
    }

    /// <summary>
    /// AUTO PATH (Filer's actual entrypoint). gguf:auto is VRAM-sensitive — it may cap context
    /// or partially offload to CPU based on free VRAM at load time, which the explicit alias on a
    /// free GPU cannot exhibit. Logs GpuLayers / AdjustedContextLength / IsGpuActive so a degraded
    /// config (the likeliest live-vs-isolated divergence under the SAME binary) is visible.
    /// </summary>
    [Fact]
    public async Task Call3_FaithfulReplay_AutoPath_LogsSelectionDiagnostics()
    {
        await using var generator = await LocalGenerator.LoadAsync(
            "gguf:auto",
            new GeneratorOptions { MaxContextLength = 16384 });

        LogModel(generator);
        var client = new LMSupplyChatClient(generator);
        var messages = LoadCallMessages(3);
        var options = BuildFilerOptions();

        var deltaCounts = new List<int>();
        for (var run = 1; run <= 5; run++)
        {
            deltaCounts.Add((await StreamOnceAsync(client, messages, options, $"run {run}")).TextDeltas);
        }
        Verdict("gguf:auto", deltaCounts);

        Assert.NotEmpty(deltaCounts);
    }

    /// <summary>
    /// PERSISTENT SEQUENCE: replay the real call1 -> call2 -> call3 prompts in order on ONE
    /// generator/server (warm slot, growing prefix), matching the agentic loop's KV continuation.
    /// Uses gguf:auto to mirror Filer. The call-3 generation here runs against a slot that just
    /// processed call-1 and call-2 — the one runtime condition the standalone 5x runs omit.
    /// </summary>
    [Fact]
    public async Task FullSequence_Call1Call2Call3_OnPersistentGenerator()
    {
        await using var generator = await LocalGenerator.LoadAsync(
            "gguf:auto",
            new GeneratorOptions { MaxContextLength = 16384 });

        LogModel(generator);
        var client = new LMSupplyChatClient(generator);
        var options = BuildFilerOptions();

        // Replay the captured prompts in conversational order on the same warm server.
        var call1 = await StreamOnceAsync(client, LoadCallMessages(1), options, "call1");
        var call2 = await StreamOnceAsync(client, LoadCallMessages(2), options, "call2");
        var call3 = await StreamOnceAsync(client, LoadCallMessages(3), options, "call3");

        _output.WriteLine("");
        _output.WriteLine("=== VERDICT (persistent sequence) ===");
        _output.WriteLine(call3.TextDeltas <= 1
            ? $"REPRODUCED on warm slot: call-3 collapsed to {call3.TextDeltas} text delta after the " +
              "call1/call2 continuation -> slot/KV-reuse is the trigger; isolate server launch/slot config."
            : $"NOT reproduced on warm slot: call-3 streamed {call3.TextDeltas} deltas " +
              $"(call1={call1.TextDeltas}, call2={call2.TextDeltas}) -> KV continuation is not the trigger.");

        Assert.True(call1.TotalUpdates + call2.TotalUpdates + call3.TotalUpdates > 0);
    }

    // ------------------------------------------------------------------ helpers

    private static ChatOptions BuildFilerOptions() => new()
    {
        // Verbatim Filer call-3 boundary ChatOptions.
        MaxOutputTokens = 4096,
        Temperature = 0.7f,
        Tools = BuildFilerTools(),
    };

    private void LogModel(ITextGenerator generator)
    {
        var m = generator as IGeneratorModel;
        var info = m?.GetModelInfo();
        _output.WriteLine($"model={generator.ModelId} runtime={info?.RuntimeVersion ?? "?"} " +
                          $"ep={info?.ExecutionProvider ?? "?"} gpu={m?.IsGpuActive} " +
                          $"ctx={info?.MaxContextLength} " +
                          $"adjustedCtx={info?.AdjustedContextLength?.ToString(CultureInfo.InvariantCulture) ?? "-"} " +
                          $"gpuLayers={info?.GpuLayers?.ToString(CultureInfo.InvariantCulture) ?? "-"}/" +
                          $"{info?.TotalLayers?.ToString(CultureInfo.InvariantCulture) ?? "-"} " +
                          $"vram={info?.EstimatedVramBytes?.ToString(CultureInfo.InvariantCulture) ?? "-"} " +
                          $"ram={info?.EstimatedRamBytes?.ToString(CultureInfo.InvariantCulture) ?? "-"} " +
                          $"diag={info?.Diagnostics?.ToString() ?? "-"}");
        if (info?.AdjustedContextLength is not null || info?.GpuLayers is not null)
        {
            _output.WriteLine("  *** DEGRADED CONFIG: context was VRAM-capped and/or partial CPU offload " +
                              "(this is the candidate live-vs-isolated divergence). ***");
        }
    }

    private async Task<StreamStats> StreamOnceAsync(
        LMSupplyChatClient client, IReadOnlyList<MeaiChatMessage> messages, ChatOptions options, string label)
    {
        var s = new StreamStats();
        long firstTextMs = -1;
        var sw = Stopwatch.StartNew();
        await foreach (var upd in client.GetStreamingResponseAsync(messages, options))
        {
            s.TotalUpdates++;
            var text = string.Concat(upd.Contents.OfType<TextContent>().Select(t => t.Text));
            if (!string.IsNullOrEmpty(text))
            {
                if (firstTextMs < 0)
                {
                    firstTextMs = sw.ElapsedMilliseconds;
                }
                s.TextDeltas++;
                s.Chars += text.Length;
                s.Preview ??= text.Length > 60 ? text[..60] : text;
            }
            if (upd.FinishReason is not null)
            {
                s.Finish = upd.FinishReason.ToString();
            }
        }
        sw.Stop();
        s.FirstTextMs = firstTextMs;
        _output.WriteLine($"[{label}] textDeltas={s.TextDeltas} totalUpdates={s.TotalUpdates} " +
                          $"chars={s.Chars} firstTextMs={s.FirstTextMs} finish={s.Finish} " +
                          $"totalMs={sw.ElapsedMilliseconds} firstDelta=\"{s.Preview}\"");
        return s;
    }

    private void Verdict(string load, IReadOnlyList<int> deltaCounts)
    {
        _output.WriteLine("");
        _output.WriteLine($"=== VERDICT ({load}) ===");
        if (deltaCounts.All(c => c <= 1))
        {
            _output.WriteLine("REPRODUCED: every run collapsed to <=1 text delta.");
        }
        else if (deltaCounts.All(c => c > 1))
        {
            _output.WriteLine($"NOT reproduced: every run streamed >1 delta (max={deltaCounts.Max()}).");
        }
        else
        {
            _output.WriteLine($"MIXED across runs: {string.Join(",", deltaCounts)} (nondeterministic at Temp 0.7).");
        }
    }

    private static List<MeaiChatMessage> LoadCallMessages(int call)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Providers", "Fixtures",
            $"filer-issue219-call{call.ToString(CultureInfo.InvariantCulture)}-messages.json");
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);

        var list = new List<MeaiChatMessage>();
        foreach (var msgEl in doc.RootElement.EnumerateArray())
        {
            var role = msgEl.GetProperty("role").GetString()!;
            var contents = new List<AIContent>();
            foreach (var c in msgEl.GetProperty("contents").EnumerateArray())
            {
                switch (c.GetProperty("type").GetString())
                {
                    case "text":
                        contents.Add(new TextContent(c.GetProperty("text").GetString()));
                        break;
                    case "functionCall":
                        Dictionary<string, object?>? args = null;
                        if (c.TryGetProperty("arguments", out var argEl) &&
                            argEl.ValueKind == JsonValueKind.Object)
                        {
                            args = JsonSerializer.Deserialize<Dictionary<string, object?>>(argEl.GetRawText());
                        }
                        contents.Add(new FunctionCallContent(
                            c.GetProperty("callId").GetString()!,
                            c.GetProperty("name").GetString()!,
                            args));
                        break;
                    case "functionResult":
                        contents.Add(new FunctionResultContent(
                            c.GetProperty("callId").GetString()!,
                            c.GetProperty("result").GetString()));
                        break;
                }
            }

            var chatRole = role switch
            {
                "system" => ChatRole.System,
                "user" => ChatRole.User,
                "assistant" => ChatRole.Assistant,
                "tool" => ChatRole.Tool,
                _ => ChatRole.User
            };
            list.Add(new MeaiChatMessage(chatRole, contents));
        }
        return list;
    }

    // The 8 tools present on Filer's stop turn (post tool-retrieval compression). Names are
    // byte-exact; parameter shapes mirror each tool's real surface. Schemas need not be byte-exact
    // — the single-delta is content-independent per Filer; tool COUNT + names drive rendering.
    private static IList<AITool> BuildFilerTools() =>
    [
        AIFunctionFactory.Create((string path) => string.Empty, "ReadFile", "Read a file's contents."),
        AIFunctionFactory.Create((string path, string content, bool append) => string.Empty, "WriteFile", "Write content to a file."),
        AIFunctionFactory.Create((string path, string content) => string.Empty, "EditFile", "Edit a file's contents."),
        AIFunctionFactory.Create((string path) => string.Empty, "DeleteFile", "Delete a file."),
        AIFunctionFactory.Create((string path, bool force) => string.Empty, "DeleteDirectory", "Delete a directory."),
        AIFunctionFactory.Create((string path, bool recursive) => string.Empty, "ListDirectory", "List a directory's entries."),
        AIFunctionFactory.Create((string pattern) => string.Empty, "GlobFiles", "Find files matching a glob pattern."),
        AIFunctionFactory.Create((string query) => string.Empty, "search_knowledge", "Search the file knowledge base."),
    ];

    private sealed class StreamStats
    {
        public int TextDeltas { get; set; }
        public int TotalUpdates { get; set; }
        public int Chars { get; set; }
        public long FirstTextMs { get; set; }
        public string? Finish { get; set; }
        public string? Preview { get; set; }
    }
}
