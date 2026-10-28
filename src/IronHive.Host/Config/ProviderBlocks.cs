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

    /// <summary>The configured blocks, in default order: the named blocks, then the declared providers in list order.</summary>
    public static IEnumerable<ProviderBlock> Configured(IronHiveConfig config) => All.Where(p => p.IsConfigured(config)).Concat(Declared(config));

    /// <summary>
    /// The complete entries of the <c>providers:</c> list, each as a block named by its id. An entry <see cref="Problems"/> reports
    /// is still listed here when complete - the registration refuses the whole list, so nothing is dropped silently.
    /// </summary>
    public static IEnumerable<ProviderBlock> Declared(IronHiveConfig config) =>
        config.Providers.Where(e => e.IsConfigured).Select(e => new ProviderBlock(e.Id!.Trim(), "providers", [], _ => true, _ => e.Model)).ToList();

    /// <summary>The entries of the <c>providers:</c> list as configured, for the registration to build from.</summary>
    internal static IReadOnlyList<DeclaredProviderConfig> DeclaredEntries(IronHiveConfig config) => config.Providers;

    /// <summary>
    /// What is wrong with the <c>providers:</c> list, by entry: an incomplete entry (id, baseUrl and model are needed), an
    /// id used twice, an id that is a built-in block's name or alias (or <c>lmsupply</c> / <c>local</c>), an unknown wire. Nothing is dropped silently.
    /// </summary>
    public static IReadOnlyList<string> Problems(IronHiveConfig config)
    {
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < config.Providers.Count; i++)
        {
            var entry = config.Providers[i];
            if (!entry.IsConfigured)
            {
                problems.Add($"providers[{i}] needs id, baseUrl and model");
                continue;
            }

            var id = entry.Id!.Trim();
            if (Find(id) is not null || id.Equals("lmsupply", StringComparison.OrdinalIgnoreCase) || id.Equals("local", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"providers[{i}].id '{id}' is the name of a built-in provider");
            }
            else if (!seen.Add(id))
            {
                problems.Add($"providers[{i}].id '{id}' is used twice");
            }

            if (!Enum.IsDefined(entry.Wire))
            {
                problems.Add($"providers[{i}].wire is missing or not a wire format ({string.Join(", ", Enum.GetNames<IronHive.Providers.Declarations.WireFormat>())})");
            }
        }

        return problems;
    }

    /// <summary>The block a name or alias names, or <c>null</c>.</summary>
    public static ProviderBlock? Find(string name) => All.FirstOrDefault(p =>
        string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)
        || p.Aliases.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase)));
}
