using System.Text.Json;
using FluentAssertions;
using IronHive.Host.Protocol;
using IronHive.Host.Server;

namespace IronHive.Host.Tests.Server;

public class AgentServerProtocolTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    // ── ServerRequest serialization ─────────────────────────────────

    [Fact]
    public void Serialize_UserMessageRequest_HasTypeDiscriminator()
    {
        ServerRequest request = new UserMessageRequest("hello");
        var json = JsonSerializer.Serialize(request, Options);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("type").GetString().Should().Be("user_message");
        doc.RootElement.GetProperty("content").GetString().Should().Be("hello");
    }

    [Fact]
    public void Serialize_ShutdownRequest_HasTypeDiscriminator()
    {
        ServerRequest request = new ShutdownRequest();
        var json = JsonSerializer.Serialize(request, Options);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("type").GetString().Should().Be("shutdown");
    }

    [Fact]
    public void Serialize_HitlResponseRequest_WithReason()
    {
        ServerRequest request = new HitlResponseRequest(true, "looks good");
        var json = JsonSerializer.Serialize(request, Options);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("type").GetString().Should().Be("hitl_response");
        doc.RootElement.GetProperty("approved").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("reason").GetString().Should().Be("looks good");
    }

    [Fact]
    public void Serialize_HitlResponseRequest_WithoutReason()
    {
        ServerRequest request = new HitlResponseRequest(false);
        var json = JsonSerializer.Serialize(request, Options);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("type").GetString().Should().Be("hitl_response");
        doc.RootElement.GetProperty("approved").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("reason").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void Serialize_ContextUpdateRequest_WithWorkingPath()
    {
        ServerRequest request = new ContextUpdateRequest("/home/user/docs");
        var json = JsonSerializer.Serialize(request, Options);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("type").GetString().Should().Be("context_update");
        doc.RootElement.GetProperty("working_path").GetString().Should().Be("/home/user/docs");
    }

    // ── ServerRequest roundtrip ─────────────────────────────────────

    [Fact]
    public void Roundtrip_UserMessageRequest()
    {
        ServerRequest original = new UserMessageRequest("test content");
        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<ServerRequest>(json, Options);

        deserialized.Should().BeOfType<UserMessageRequest>()
            .Which.Content.Should().Be("test content");
    }

    [Fact]
    public void Roundtrip_ContextUpdateRequest_NullWorkingPath()
    {
        ServerRequest original = new ContextUpdateRequest(null);
        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<ServerRequest>(json, Options);

        deserialized.Should().BeOfType<ContextUpdateRequest>()
            .Which.WorkingPath.Should().BeNull();
    }

    [Fact]
    public void Serialize_CancelRequest_HasTypeDiscriminator()
    {
        ServerRequest request = new CancelRequest();
        var json = JsonSerializer.Serialize(request, Options);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("type").GetString().Should().Be("cancel");
    }

    [Fact]
    public void Roundtrip_CancelRequest()
    {
        ServerRequest original = new CancelRequest();
        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<ServerRequest>(json, Options);

        deserialized.Should().BeOfType<CancelRequest>();
    }

    // ── ServerEvent serialization ───────────────────────────────────

    [Fact]
    public void Serialize_SessionStartedEvent_HasTypeDiscriminator()
    {
        ServerEvent evt = new SessionStartedEvent("sess-001");
        var json = JsonSerializer.Serialize(evt, Options);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("type").GetString().Should().Be("session_started");
        doc.RootElement.GetProperty("session_id").GetString().Should().Be("sess-001");
    }

    // ── ServerEvent roundtrips ──────────────────────────────────────

    [Fact]
    public void Roundtrip_TextDeltaEvent()
    {
        ServerEvent original = new TextDeltaEvent("chunk");
        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<ServerEvent>(json, Options);

        deserialized.Should().BeOfType<TextDeltaEvent>()
            .Which.Content.Should().Be("chunk");
    }

    [Fact]
    public void Roundtrip_ToolStartEvent_WithInput()
    {
        var input = JsonSerializer.SerializeToElement(new { path = "/tmp" });
        ServerEvent original = new ToolStartEvent("read_file", input);
        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<ServerEvent>(json, Options);

        var result = deserialized.Should().BeOfType<ToolStartEvent>().Subject;
        result.Tool.Should().Be("read_file");
        result.Input.Should().NotBeNull();
        result.Input!.Value.GetProperty("path").GetString().Should().Be("/tmp");
    }

    [Fact]
    public void Roundtrip_ToolStartEvent_WithoutInput()
    {
        ServerEvent original = new ToolStartEvent("list_files");
        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<ServerEvent>(json, Options);

        var result = deserialized.Should().BeOfType<ToolStartEvent>().Subject;
        result.Tool.Should().Be("list_files");
        result.Input.Should().BeNull();
    }

    [Fact]
    public void Roundtrip_ToolEndEvent_Success()
    {
        ServerEvent original = new ToolEndEvent("read_file", true);
        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<ServerEvent>(json, Options);

        var result = deserialized.Should().BeOfType<ToolEndEvent>().Subject;
        result.Tool.Should().Be("read_file");
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Roundtrip_ToolEndEvent_Failure()
    {
        ServerEvent original = new ToolEndEvent("write_file", false);
        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<ServerEvent>(json, Options);

        var result = deserialized.Should().BeOfType<ToolEndEvent>().Subject;
        result.Tool.Should().Be("write_file");
        result.Success.Should().BeFalse();
    }

    [Fact]
    public void Roundtrip_HitlRequestEvent()
    {
        ServerEvent original = new HitlRequestEvent("h-1", "delete", "file.txt", "Delete this file?");
        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<ServerEvent>(json, Options);

        var result = deserialized.Should().BeOfType<HitlRequestEvent>().Subject;
        result.Id.Should().Be("h-1");
        result.Action.Should().Be("delete");
        result.Target.Should().Be("file.txt");
        result.Description.Should().Be("Delete this file?");
    }

    [Fact]
    public void Roundtrip_AgentSelectedEvent()
    {
        ServerEvent original = new AgentSelectedEvent("FileAgent", 0.95);
        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<ServerEvent>(json, Options);

        var result = deserialized.Should().BeOfType<AgentSelectedEvent>().Subject;
        result.AgentName.Should().Be("FileAgent");
        result.Confidence.Should().Be(0.95);
    }

    [Fact]
    public void Roundtrip_TurnEndEvent()
    {
        ServerEvent original = new TurnEndEvent();
        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<ServerEvent>(json, Options);

        deserialized.Should().BeOfType<TurnEndEvent>();
    }

    [Fact]
    public void Roundtrip_ErrorEvent()
    {
        ServerEvent original = new ErrorEvent("something went wrong");
        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<ServerEvent>(json, Options);

        deserialized.Should().BeOfType<ErrorEvent>()
            .Which.Message.Should().Be("something went wrong");
    }
}
