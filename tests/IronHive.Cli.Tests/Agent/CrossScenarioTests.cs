using IronHive.Cli.Core.Agent;
using IronHive.Cli.Tests.Mocks;
using Microsoft.Extensions.AI;

namespace IronHive.Cli.Tests.Agent;

/// <summary>
/// Phase 3: Cross-scenario tests that verify multi-tool chaining patterns.
/// Simulates realistic agent workflows combining different tool types.
/// </summary>
public class CrossScenarioTests
{
    #region Web Search → File Save

    [Fact]
    public async Task Scenario_WebSearchThenFileSave_ChainsToolCalls()
    {
        // Arrange - Agent searches web, then writes results to file
        var mockClient = new MockChatClient()
            .EnqueueToolCallResponse("WebSearch", """{"query": ".NET 10 new features", "maxResults": 5}""",
                "Let me search for the latest .NET 10 features.")
            .EnqueueToolCallResponse("WriteFile", """{"path": "dotnet10-notes.md", "content": "# .NET 10 Features\n- New API\n- Performance"}""",
                "I'll save the results to a file.")
            .EnqueueResponse("I've searched for .NET 10 features and saved the results to dotnet10-notes.md.");

        var agentLoop = new AgentLoop(mockClient, new AgentOptions
        {
            SystemPrompt = "You are a research assistant with web search and file tools."
        });

        // Act
        var r1 = await agentLoop.RunAsync("Search for .NET 10 new features and save to a file");
        Assert.Single(r1.ToolCalls);
        Assert.Equal("WebSearch", r1.ToolCalls[0].ToolName);

        var r2 = await agentLoop.RunAsync("Search results: [1] .NET 10 features - New API, Performance improvements");
        Assert.Single(r2.ToolCalls);
        Assert.Equal("WriteFile", r2.ToolCalls[0].ToolName);

        var r3 = await agentLoop.RunAsync("Successfully wrote to file: dotnet10-notes.md");

        // Assert
        Assert.Contains("dotnet10-notes.md", r3.Content);
        Assert.Empty(r3.ToolCalls);
        Assert.Equal(7, agentLoop.History.Count); // System + 3 User + 3 Assistant
    }

    [Fact]
    public async Task Scenario_ExploreSiteThenWebSearch_ChainsDiscovery()
    {
        // Arrange - Agent explores site structure, then searches specific pages
        var mockClient = new MockChatClient()
            .EnqueueToolCallResponse("ExploreSite", """{"url": "https://docs.python.org"}""",
                "Let me explore the Python docs site structure.")
            .EnqueueToolCallResponse("WebSearch", """{"query": "site:docs.python.org asyncio changes"}""",
                "I found the sitemap. Now let me search for asyncio content.")
            .EnqueueResponse("Based on my research:\n1. asyncio.TaskGroup added in 3.11\n2. Performance improvements in 3.12\n3. New scheduler in 3.13");

        var agentLoop = new AgentLoop(mockClient, new AgentOptions
        {
            SystemPrompt = "You have web search and site exploration tools."
        });

        // Act
        var r1 = await agentLoop.RunAsync("Explore docs.python.org and find asyncio changes");
        Assert.Equal("ExploreSite", r1.ToolCalls[0].ToolName);

        var r2 = await agentLoop.RunAsync("Site has sitemap with /library/asyncio*.html pages");
        Assert.Equal("WebSearch", r2.ToolCalls[0].ToolName);

        var r3 = await agentLoop.RunAsync("Search found 3 relevant pages about asyncio improvements");

        // Assert
        Assert.Contains("asyncio", r3.Content);
        Assert.Empty(r3.ToolCalls);
    }

    #endregion

    #region MCP Tool → Web Search

    [Fact]
    public async Task Scenario_McpScreenCaptureThenWebSearch_ErrorDiagnostics()
    {
        // Arrange - Agent captures screen (MCP), then searches for error solution
        var mockClient = new MockChatClient()
            .EnqueueToolCallResponse("mcp__system-harness_do",
                """{"category": "screen", "action": "capture", "parameters": {}}""",
                "I'll capture the current screen to see the error.")
            .EnqueueToolCallResponse("WebSearch",
                """{"query": "NullReferenceException System.Collections.Generic.List"}""",
                "I can see a NullReferenceException. Let me search for solutions.")
            .EnqueueResponse("The error is a NullReferenceException. Solution: Initialize the list before use.\n```csharp\nvar items = new List<string>();\n```");

        var agentLoop = new AgentLoop(mockClient, new AgentOptions
        {
            SystemPrompt = "You can capture screens and search the web."
        });

        // Act
        var r1 = await agentLoop.RunAsync("Capture the screen and help me fix the error shown");
        Assert.Equal("mcp__system-harness_do", r1.ToolCalls[0].ToolName);

        var r2 = await agentLoop.RunAsync("Screen captured: NullReferenceException at Line 42");
        Assert.Equal("WebSearch", r2.ToolCalls[0].ToolName);

        var r3 = await agentLoop.RunAsync("Found: Initialize collections before use");

        // Assert
        Assert.Contains("NullReferenceException", r3.Content);
        Assert.Contains("List<string>", r3.Content);
    }

    [Fact]
    public async Task Scenario_McpSystemInfoThenFileSave_Report()
    {
        // Arrange - Agent gets system info (MCP), writes report
        var mockClient = new MockChatClient()
            .EnqueueToolCallResponse("mcp__system-harness_get",
                """{"category": "system", "action": "info", "parameters": {}}""",
                "Let me gather system information.")
            .EnqueueToolCallResponse("WriteFile",
                """{"path": "system-report.md", "content": "# System Report\n- OS: Windows 11\n- CPU: 8 cores\n- RAM: 16GB"}""",
                "Now I'll save the report.")
            .EnqueueResponse("System report has been saved to system-report.md with OS, CPU, and RAM details.");

        var agentLoop = new AgentLoop(mockClient, new AgentOptions
        {
            SystemPrompt = "You can access system tools and file tools."
        });

        // Act
        var r1 = await agentLoop.RunAsync("Collect system info and save as a report");
        Assert.Equal("mcp__system-harness_get", r1.ToolCalls[0].ToolName);

        var r2 = await agentLoop.RunAsync("System info: Windows 11, 8 cores, 16GB RAM");
        Assert.Equal("WriteFile", r2.ToolCalls[0].ToolName);

        var r3 = await agentLoop.RunAsync("Successfully wrote to file: system-report.md");

        // Assert
        Assert.Contains("system-report.md", r3.Content);
    }

    #endregion

    #region Multi-Tool Chain

    [Fact]
    public async Task Scenario_SearchAnalyzeWrite_ThreeStepChain()
    {
        // Arrange - Search → Read existing → Write merged result
        var mockClient = new MockChatClient()
            .EnqueueToolCallResponse("WebSearch",
                """{"query": "React vs Vue vs Svelte SSR comparison 2026"}""",
                "Let me search for framework comparisons.")
            .EnqueueToolCallResponse("ReadFile",
                """{"path": "existing-notes.md"}""",
                "Let me check if there are existing notes.")
            .EnqueueToolCallResponse("WriteFile",
                """{"path": "framework-comparison.md", "content": "# SSR Comparison\n| Framework | SSR | Hydration |\n|---|---|---|\n| React | RSC | Selective |\n| Vue | Nuxt | Islands |\n| Svelte | SvelteKit | Zero-JS |"}""",
                "I'll merge the research with existing notes.")
            .EnqueueResponse("Created framework-comparison.md with a comparison table of React, Vue, and Svelte SSR capabilities.");

        var agentLoop = new AgentLoop(mockClient, new AgentOptions
        {
            SystemPrompt = "You can search the web, read files, and write files."
        });

        // Act - 4-step conversation
        var r1 = await agentLoop.RunAsync("Compare React, Vue, Svelte SSR and save to a comparison file");
        Assert.Equal("WebSearch", r1.ToolCalls[0].ToolName);

        var r2 = await agentLoop.RunAsync("Search results: React has RSC, Vue uses Nuxt, Svelte has SvelteKit");
        Assert.Equal("ReadFile", r2.ToolCalls[0].ToolName);

        var r3 = await agentLoop.RunAsync("File not found: existing-notes.md");
        Assert.Equal("WriteFile", r3.ToolCalls[0].ToolName);

        var r4 = await agentLoop.RunAsync("Successfully wrote to file: framework-comparison.md");

        // Assert
        Assert.Contains("framework-comparison.md", r4.Content);
        Assert.Equal(9, agentLoop.History.Count); // System + 4 User + 4 Assistant
    }

    [Fact]
    public async Task Scenario_McpHelpThenDo_DiscoverAndExecute()
    {
        // Arrange - Use MCP help to discover commands, then execute
        var mockClient = new MockChatClient()
            .EnqueueToolCallResponse("mcp__system-harness_help",
                """{"category": "app"}""",
                "Let me check what app commands are available.")
            .EnqueueToolCallResponse("mcp__system-harness_do",
                """{"category": "app", "action": "open", "parameters": {"name": "notepad"}}""",
                "Found the open command. Let me open Notepad.")
            .EnqueueToolCallResponse("mcp__system-harness_do",
                """{"category": "keyboard", "action": "type", "parameters": {"text": "Hello IronHive!"}}""",
                "Notepad is open. Now I'll type the text.")
            .EnqueueResponse("Done! I opened Notepad and typed 'Hello IronHive!' as requested.");

        var agentLoop = new AgentLoop(mockClient, new AgentOptions
        {
            SystemPrompt = "You have desktop automation tools."
        });

        // Act
        var r1 = await agentLoop.RunAsync("Open notepad and type 'Hello IronHive!'");
        Assert.Equal("mcp__system-harness_help", r1.ToolCalls[0].ToolName);

        var r2 = await agentLoop.RunAsync("Available app commands: open, close, list, focus");
        Assert.Equal("mcp__system-harness_do", r2.ToolCalls[0].ToolName);

        var r3 = await agentLoop.RunAsync("Notepad opened successfully");
        Assert.Equal("mcp__system-harness_do", r3.ToolCalls[0].ToolName);

        var r4 = await agentLoop.RunAsync("Text typed successfully");

        // Assert
        Assert.Contains("Hello IronHive!", r4.Content);
        Assert.Empty(r4.ToolCalls);
    }

    #endregion

    #region Streaming Cross-Scenarios

    [Fact]
    public async Task Scenario_StreamingWebSearchWorkflow_YieldsToolCallsAndText()
    {
        // Arrange - Streaming response with tool call followed by text
        var mockClient = new MockChatClient()
            .EnqueueToolCallResponse("WebSearch",
                """{"query": "Rust vs Go performance"}""",
                "Searching for performance comparison...")
            .EnqueueResponse("Based on my research:\n- Rust: Zero-cost abstractions, no GC\n- Go: Fast compilation, goroutines");

        var agentLoop = new AgentLoop(mockClient, new AgentOptions
        {
            SystemPrompt = "Research assistant with web search."
        });

        // Act - First turn streaming
        var textChunks = new List<string>();
        var toolCalls = new List<ToolCallChunk>();

        await foreach (var chunk in agentLoop.RunStreamingAsync("Compare Rust and Go performance"))
        {
            if (!string.IsNullOrEmpty(chunk.TextDelta))
            {
                textChunks.Add(chunk.TextDelta);
            }
            if (chunk.ToolCallDelta is not null)
            {
                toolCalls.Add(chunk.ToolCallDelta);
            }
        }

        // Assert - First turn has tool call
        Assert.Single(toolCalls);
        Assert.Equal("WebSearch", toolCalls[0].NameDelta);

        // Act - Second turn with search results
        var resultChunks = new List<string>();
        await foreach (var chunk in agentLoop.RunStreamingAsync("Results: Rust is faster, Go is simpler"))
        {
            if (!string.IsNullOrEmpty(chunk.TextDelta))
            {
                resultChunks.Add(chunk.TextDelta);
            }
        }

        // Assert - Second turn has text response
        var fullResponse = string.Join("", resultChunks);
        Assert.Contains("Rust", fullResponse);
        Assert.Contains("Go", fullResponse);
    }

    #endregion

    #region History Consistency

    [Fact]
    public async Task Scenario_CrossToolHistory_MaintainsCorrectMessageCounts()
    {
        // Arrange - Verify history growth across tool types
        var mockClient = new MockChatClient()
            .EnqueueToolCallResponse("WebSearch", """{"query": "test"}""")
            .EnqueueToolCallResponse("mcp__system-harness_get", """{"category": "system", "action": "time"}""")
            .EnqueueToolCallResponse("WriteFile", """{"path": "out.txt", "content": "result"}""")
            .EnqueueResponse("All done.");

        var agentLoop = new AgentLoop(mockClient, new AgentOptions
        {
            SystemPrompt = "Multi-tool agent."
        });

        // Act
        await agentLoop.RunAsync("Do a complex task");
        // System(1) + User(1) + Assistant(1) = 3 messages after first call
        Assert.Equal(3, agentLoop.History.Count);

        await agentLoop.RunAsync("Search result: OK");
        Assert.Equal(5, agentLoop.History.Count); // +User +Assistant

        await agentLoop.RunAsync("System time: 14:30");
        Assert.Equal(7, agentLoop.History.Count);

        await agentLoop.RunAsync("File written");
        Assert.Equal(9, agentLoop.History.Count);

        // Assert - MockClient received correct message counts
        Assert.Equal(2, mockClient.ReceivedMessages[0].Count);  // System + User1
        Assert.Equal(4, mockClient.ReceivedMessages[1].Count);  // +Asst1 +User2
        Assert.Equal(6, mockClient.ReceivedMessages[2].Count);  // +Asst2 +User3
        Assert.Equal(8, mockClient.ReceivedMessages[3].Count);  // +Asst3 +User4
    }

    [Fact]
    public async Task Scenario_ToolCallArguments_PreserveJsonStructure()
    {
        // Arrange - Verify complex arguments in tool calls
        var mockClient = new MockChatClient()
            .EnqueueToolCallResponse("mcp__system-harness_do",
                """{"category": "keyboard", "action": "type", "parameters": {"text": "Hello \"World\"", "delay": 50}}""");

        var agentLoop = new AgentLoop(mockClient);

        // Act
        var response = await agentLoop.RunAsync("Type some text with quotes");

        // Assert
        Assert.Single(response.ToolCalls);
        Assert.Equal("mcp__system-harness_do", response.ToolCalls[0].ToolName);
        Assert.Contains("keyboard", response.ToolCalls[0].Arguments);
        Assert.Contains("Hello", response.ToolCalls[0].Arguments);
    }

    #endregion

    #region Error Recovery Cross-Scenarios

    [Fact]
    public async Task Scenario_ToolFailure_AgentFallsBackToAlternative()
    {
        // Arrange - First tool fails, agent tries alternative
        var mockClient = new MockChatClient()
            .EnqueueToolCallResponse("WebSearch", """{"query": "error fix"}""",
                "Let me search for a solution.")
            .EnqueueToolCallResponse("GrepFiles",
                """{"pattern": "NullReferenceException", "filePattern": "**/*.log"}""",
                "Web search failed. Let me check local logs instead.")
            .EnqueueResponse("Found the error in local logs. The fix is to initialize the variable on line 42.");

        var agentLoop = new AgentLoop(mockClient, new AgentOptions
        {
            SystemPrompt = "You can search web and grep files."
        });

        // Act - First tool (web search)
        var r1 = await agentLoop.RunAsync("Find the cause of the NullReferenceException");
        Assert.Equal("WebSearch", r1.ToolCalls[0].ToolName);

        // Simulate web search failure
        var r2 = await agentLoop.RunAsync("Error: Web search timed out");
        Assert.Equal("GrepFiles", r2.ToolCalls[0].ToolName);

        // Grep succeeds
        var r3 = await agentLoop.RunAsync("Found match in app.log: line 42 NullReferenceException");

        // Assert - Agent recovered and provided solution
        Assert.Contains("line 42", r3.Content);
        Assert.Empty(r3.ToolCalls);
    }

    [Fact]
    public async Task Scenario_McpToolUnavailable_AgentUsesBuiltinAlternative()
    {
        // Arrange - MCP tool fails, agent uses built-in file tool
        var mockClient = new MockChatClient()
            .EnqueueToolCallResponse("mcp__system-harness_get",
                """{"category": "file", "action": "read", "parameters": {"path": "config.json"}}""",
                "Let me read the config using system-harness.")
            .EnqueueToolCallResponse("ReadFile", """{"path": "config.json"}""",
                "System-harness failed. I'll use the built-in ReadFile tool.")
            .EnqueueResponse("The config.json contains: database=localhost, port=5432");

        var agentLoop = new AgentLoop(mockClient, new AgentOptions
        {
            SystemPrompt = "You have MCP and built-in file tools."
        });

        // Act
        var r1 = await agentLoop.RunAsync("Read config.json");
        Assert.Equal("mcp__system-harness_get", r1.ToolCalls[0].ToolName);

        var r2 = await agentLoop.RunAsync("Error: MCP plugin 'system-harness' is not connected");
        Assert.Equal("ReadFile", r2.ToolCalls[0].ToolName);

        var r3 = await agentLoop.RunAsync("""{"database": "localhost", "port": 5432}""");

        // Assert
        Assert.Contains("localhost", r3.Content);
        Assert.Contains("5432", r3.Content);
    }

    [Fact]
    public async Task Scenario_PartialFailure_AgentCompletesWhatItCan()
    {
        // Arrange - Multi-step task, one step fails, agent completes remaining
        var mockClient = new MockChatClient()
            .EnqueueToolCallResponse("WebSearch", """{"query": "React latest version"}""")
            .EnqueueToolCallResponse("WebSearch", """{"query": "Vue latest version"}""")
            .EnqueueToolCallResponse("WriteFile",
                """{"path": "comparison.md", "content": "# Framework Versions\n- React: 19.1\n- Vue: (search failed)"}""",
                "React search succeeded, Vue search failed. I'll note the partial result.")
            .EnqueueResponse("Created comparison.md. Note: Vue version could not be retrieved due to search timeout.");

        var agentLoop = new AgentLoop(mockClient);

        // Act
        var r1 = await agentLoop.RunAsync("Compare React and Vue latest versions");
        Assert.Equal("WebSearch", r1.ToolCalls[0].ToolName);

        var r2 = await agentLoop.RunAsync("React 19.1 released in 2026");
        Assert.Equal("WebSearch", r2.ToolCalls[0].ToolName);

        var r3 = await agentLoop.RunAsync("Error: Search timeout for Vue query");
        Assert.Equal("WriteFile", r3.ToolCalls[0].ToolName);

        var r4 = await agentLoop.RunAsync("Successfully wrote to file: comparison.md");

        // Assert - Agent completed with partial data
        Assert.Contains("comparison.md", r4.Content);
        Assert.Contains("could not be retrieved", r4.Content);
    }

    #endregion
}
