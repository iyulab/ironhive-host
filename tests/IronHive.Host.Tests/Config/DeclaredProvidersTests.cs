using AwesomeAssertions;
using IronHive.Host.Config;
using IronHive.Host.Providers;
using IronHive.Providers.Declarations;
using Microsoft.Extensions.AI;
using CliServices = IronHive.Cli.Infrastructure.ServiceCollectionExtensions;

namespace IronHive.Host.Tests.Config;

/// <summary>
/// The <c>providers:</c> list: providers declared as data, any number of them, named by id. They load from
/// <c>config.yaml</c>, follow the named blocks in the default order, build a client per model, and a list that cannot be
/// registered says so by entry instead of dropping the entry.
/// </summary>
public class DeclaredProvidersTests
{
    private const string Yaml = """
        providers:
          - id: corp-gateway
            wire: OpenAIChatCompletions
            baseUrl: https://gateway.example.com/v1
            apiKey: k-1
            keyHeader: api-key
            model: llama-3.3
            headers:
              X-Route: blue
            models:
              claude-x:
                wire: Anthropic
                baseUrl: https://gateway.example.com/anthropic
          - id: lab
            wire: GoogleAI
            baseUrl: https://lab.example.com
            model: gemini-x
        """;

    private static IronHiveConfig Load(string yaml) => YamlConfigSerializer.Deserialize<IronHiveConfig>(yaml)!;

    [Fact]
    public void The_list_loads_with_the_wire_by_name_and_the_per_model_overrides()
    {
        var config = Load(Yaml);

        config.Providers.Should().HaveCount(2);
        var gateway = config.Providers[0];
        gateway.Id.Should().Be("corp-gateway");
        gateway.Wire.Should().Be(WireFormat.OpenAIChatCompletions);
        gateway.KeyHeader.Should().Be("api-key");
        gateway.Headers.Should().ContainKey("X-Route");
        gateway.Models!["claude-x"].Wire.Should().Be(WireFormat.Anthropic);
        gateway.ToDeclaration().BaseUrl.Should().Be("https://gateway.example.com/v1");
        config.Providers[1].ApiKey.Should().BeNull();
    }

    [Fact]
    public void Every_key_of_the_list_is_a_key_the_loader_reads_and_a_misspelled_one_is_reported()
    {
        ConfigurationManager.FindUnknownKeys(Yaml).Should().BeEmpty();
        ConfigurationManager.FindUnknownKeys(Yaml.Replace("baseUrl: https://lab", "baseUrll: https://lab")).Should().Equal("providers[1].baseUrll");
    }

    [Fact]
    public void Declared_providers_follow_the_named_blocks_in_the_default_order()
    {
        var config = Load(Yaml);
        config.OpenAI.ApiKey = "k";
        config.OpenAI.Model = "m";

        ProviderBlocks.Configured(config).Select(p => p.Name).Should().Equal("openai", "corp-gateway", "lab");
        ProviderBlocks.Configured(Load(Yaml)).Select(p => p.Model(Load(Yaml))).Should().Equal("llama-3.3", "gemini-x");
    }

    [Fact]
    public void A_list_with_no_problem_reports_none()
    {
        ProviderBlocks.Problems(Load(Yaml)).Should().BeEmpty();
        ProviderBlocks.Problems(new IronHiveConfig()).Should().BeEmpty();
    }

    [Fact]
    public void Each_problem_of_the_list_is_named_by_entry()
    {
        var config = Load("""
            providers:
              - id: a
                wire: Anthropic
                baseUrl: https://a.example.com
              - id: b
                baseUrl: https://b.example.com
                model: m
              - id: B
                wire: GoogleAI
                baseUrl: https://b.example.com
                model: m
              - id: claude
                wire: Anthropic
                baseUrl: https://c.example.com
                model: m
              - id: lmsupply
                wire: Anthropic
                baseUrl: https://d.example.com
                model: m
            """);

        var problems = ProviderBlocks.Problems(config);

        problems.Should().Contain(p => p.StartsWith("providers[0] needs id, baseUrl and model", StringComparison.Ordinal));
        problems.Should().Contain(p => p.StartsWith("providers[1].wire is missing", StringComparison.Ordinal));
        problems.Should().Contain("providers[2].id 'B' is used twice");
        problems.Should().Contain("providers[3].id 'claude' is the name of a built-in provider");
        problems.Should().Contain("providers[4].id 'lmsupply' is the name of a built-in provider");
    }

    [Fact]
    public async Task Every_declared_provider_has_a_registration_and_builds_a_client_per_model()
    {
        var factories = CliServices.ProviderFactories(Load(Yaml));

        factories.Keys.Should().Contain(["corp-gateway", "lab"]);
        await using var provider = (DeclaredChatClientProvider)factories["corp-gateway"]();
        provider.ProviderName.Should().Be("corp-gateway");

        var plain = await provider.GetChatClientAsync(null, TestContext.Current.CancellationToken);
        var overridden = await provider.GetChatClientAsync("claude-x", TestContext.Current.CancellationToken);

        plain.GetService<ChatClientMetadata>()!.ProviderName.Should().Be("corp-gateway");
        plain.GetService<ChatClientMetadata>()!.DefaultModelId.Should().Be("llama-3.3");
        overridden.GetService<ChatClientMetadata>()!.DefaultModelId.Should().Be("claude-x");
    }

    [Fact]
    public async Task The_models_a_declaration_names_are_the_models_it_lists()
    {
        var provider = new DeclaredChatClientProvider(Load(Yaml).Providers[0].ToDeclaration(), "llama-3.3");

        var models = await provider.GetAvailableModelsAsync(TestContext.Current.CancellationToken);

        models.Select(m => m.ModelId).Should().Equal("llama-3.3", "claude-x");
        models.Single(m => m.IsDefault).ModelId.Should().Be("llama-3.3");
        await provider.DisposeAsync();
    }
}
