namespace IronHive.Cli.Core.Tools;

/// <summary>
/// Optional service that an inner <see cref="Microsoft.Extensions.AI.IChatClient"/>
/// may expose via <c>GetService(typeof(IContextSizeProvider))</c> so a wrapping
/// decorator (notably <see cref="TokenBudgetChatClient"/>) can size its budget
/// to the model's actual context window instead of a fixed default.
/// </summary>
/// <remarks>
/// Introduced for ecosystem ISSUE Option D-2 (2026-04-30) — different LMSupply
/// models have wildly different context windows (1024 for some Phi-3 GGUFs,
/// 32K+ for newer models). Hard-coding 4096 in the decorator would either
/// over-budget small models (no protection) or starve large ones.
/// </remarks>
public interface IContextSizeProvider
{
    /// <summary>
    /// Maximum number of tokens the inner model can accept in its context window.
    /// </summary>
    int MaxContextTokens { get; }
}
