using IronHive.Cli.Infrastructure;
using IronHive.Host.Config;
using IronHive.Host.Providers;

namespace IronHive.Host.Tests.Infrastructure;

/// <summary>
/// The LMSupply chat provider used to be registered twice — once on the user's config and once, for
/// <c>/model</c> selection, on a fresh <see cref="LMSupplyConfig"/> — and the second registration won.
/// The generator model a user configured was therefore never read, and every local run loaded the
/// class default, which was an alias LMSupply does not register. These facts pin the single
/// registration's inputs and the default itself.
/// </summary>
public class LMSupplyProviderRegistrationTests
{
    [Fact]
    public void SelectableConfig_WhenEnabled_IsTheUsersOwnConfig()
    {
        var configured = new LMSupplyConfig { Enabled = true, GeneratorModel = "gguf:qwen3-default" };

        var selected = ServiceCollectionExtensions.SelectableLMSupplyConfig(configured);

        Assert.Same(configured, selected);
    }

    [Fact]
    public void SelectableConfig_WhenDisabled_ForcesEnabled_ButKeepsTheConfiguredModels()
    {
        var configured = new LMSupplyConfig
        {
            Enabled = false,
            GeneratorModel = "gguf:qwen3-default",
            EmbedderModel = "e",
            RerankerModel = "r",
            MaxContextLength = 4096
        };

        var selected = ServiceCollectionExtensions.SelectableLMSupplyConfig(configured);

        Assert.True(selected.Enabled);
        Assert.Equal("gguf:qwen3-default", selected.GeneratorModel);
        Assert.Equal("e", selected.EmbedderModel);
        Assert.Equal("r", selected.RerankerModel);
        Assert.Equal(4096, selected.MaxContextLength);
    }

    [Fact]
    public async Task Provider_ReportsTheConfiguredGeneratorModel_NotAClassDefault()
    {
        var provider = new LMSupplyChatClientProvider(new LMSupplyConfig { Enabled = true, GeneratorModel = "gguf:phi-4-mini" });

        var models = await provider.GetAvailableModelsAsync(TestContext.Current.CancellationToken);

        var model = Assert.Single(models);
        Assert.Equal("gguf:phi-4-mini", model.ModelId);
    }

    [Fact]
    public void DefaultGeneratorModel_IsAnAliasLMSupplyRegisters()
    {
        // "gguf:default" was the default for months and LMSupply never registered it; a fresh install
        // failed at its first local inference. The default must resolve through the registry.
        var alias = new LMSupplyConfig().GeneratorModel;

        Assert.Equal("gguf:auto", alias);
        Assert.True(LMSupply.Generator.Internal.Llama.GgufModelRegistry.IsAlias(alias));
    }
}
