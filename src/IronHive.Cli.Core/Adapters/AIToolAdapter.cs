using IronHive.Abstractions.Tools;
using Microsoft.Extensions.AI;

namespace IronHive.Cli.Core.Adapters;

/// <summary>
/// M.E.AI의 AITool을 IronHive의 ITool로 변환하는 어댑터입니다.
/// MessageGeneratorChatClientAdapter에서 ChatOptions.Tools를 MessageGenerationRequest.Tools로
/// 전달하기 위해 사용됩니다. 실행(InvokeAsync)은 지원하지 않습니다 — 실행은 FunctionInvokingChatClient가 담당합니다.
/// </summary>
internal sealed class AIToolAdapter : ITool
{
    private readonly AITool _aiTool;

    public AIToolAdapter(AITool aiTool)
    {
        _aiTool = aiTool ?? throw new ArgumentNullException(nameof(aiTool));
    }

    public string UniqueName => _aiTool.Name;

    public string? Description => _aiTool.Description;

    public object? Parameters => _aiTool is AIFunction func ? func.JsonSchema : null;

    public bool RequiresApproval => false;

    public Task<ToolOutput> InvokeAsync(ToolInput input, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "AIToolAdapter is declaration-only. Tool execution is handled by FunctionInvokingChatClient.");
    }
}
