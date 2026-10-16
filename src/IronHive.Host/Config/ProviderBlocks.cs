namespace IronHive.Host.Config;

/// <summary>One provider block of <see cref="IronHiveConfig"/>: its <c>--provider</c> name, aliases and config section.</summary>
/// <param name="Name">The provider name (<c>--provider</c>, <c>advisor.provider</c>, …).</param>
/// <param name="Section">The <c>config.yaml</c> section that configures it.</param>
/// <param name="Aliases">Other names it answers to.</param>
/// <param name="IsConfigured">Whether the block is complete enough to register the provider.</param>
/// <param name="Model">The block's model.</param>
internal sealed record ProviderBlock(
    string Name,
    string Section,
    IReadOnlyList<string> Aliases,
    Func<IronHiveConfig, bool> IsConfigured,
    Func<IronHiveConfig, string?> Model);

/// <summary>
/// The remote providers an <see cref="IronHiveConfig"/> can configure, in the order the default provider is chosen (the
/// first configured one). The one list the registration, the «is any provider configured» checks, <c>doctor</c> and
/// <c>models</c> read — so a new provider is added here once. LMSupply (local, in-process) is not one of them.
/// </summary>
internal static class ProviderBlocks
{
    public static IReadOnlyList<ProviderBlock> All { get; } =
    [
        new("gpustack", "gpuStack", [], c => c.GpuStack.IsConfigured, c => c.GpuStack.Model),
        new("openaiCompatible", "openaiCompatible", ["openai-compatible"], c => c.OpenAICompatible.IsConfigured, c => c.OpenAICompatible.Model),
        new("openai", "openai", [], c => c.OpenAI.IsConfigured, c => c.OpenAI.Model),
        new("anthropic", "anthropic", ["claude"], c => c.Anthropic.IsConfigured, c => c.Anthropic.Model),
        new("google", "googleai", ["gemini"], c => c.GoogleAI.IsConfigured, c => c.GoogleAI.Model),
        new("xai", "xai", ["grok"], c => c.Xai.IsConfigured, c => c.Xai.Model),
        new("ollama", "ollama", [], c => c.Ollama.IsConfigured, c => c.Ollama.Model),
        new("lmstudio", "lmstudio", [], c => c.LMStudio.IsConfigured, c => c.LMStudio.Model),
    ];

    /// <summary>The configured blocks, in default order.</summary>
    public static IEnumerable<ProviderBlock> Configured(IronHiveConfig config) => All.Where(p => p.IsConfigured(config));

    /// <summary>The block a name or alias names, or <c>null</c>.</summary>
    public static ProviderBlock? Find(string name) => All.FirstOrDefault(p =>
        string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)
        || p.Aliases.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase)));
}
