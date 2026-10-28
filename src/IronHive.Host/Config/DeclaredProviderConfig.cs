using IronHive.Providers.Declarations;

namespace IronHive.Host.Config;

/// <summary>
/// One entry of the <c>providers:</c> list of <see cref="IronHiveConfig"/>: a provider declared as data - any number of them,
/// each named by its <see cref="Id"/> (<c>--provider &lt;id&gt;</c>), none of them tied to a fixed block. The fields are those of
/// <see cref="ProviderDeclaration"/> plus the <see cref="Model"/> the provider answers with by default.
/// </summary>
public class DeclaredProviderConfig
{
    /// <summary>The provider's name: <c>--provider</c>, <c>advisor.provider</c>. Not the name of a built-in block or one of its aliases, and unique in the list.</summary>
    public string? Id { get; set; }

    /// <summary>The wire format the server speaks: <c>OpenAIChatCompletions</c>, <c>OpenAIResponses</c>, <c>Anthropic</c> or <c>GoogleAI</c>.</summary>
    public WireFormat Wire { get; set; }

    /// <summary>The endpoint, as the server documents it, version segment included (<c>https://gateway.example.com/v1</c>).</summary>
    public string? BaseUrl { get; set; }

    /// <summary>The API key; unset for a server that takes none.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The header that carries the key, when it is not the wire's own.</summary>
    public string? KeyHeader { get; set; }

    /// <summary>The word before the key in <see cref="KeyHeader"/>; see <see cref="ProviderDeclaration.KeyScheme"/>.</summary>
    public string? KeyScheme { get; set; }

    /// <summary>Extra headers sent on every request.</summary>
    public Dictionary<string, string>? Headers { get; set; }

    /// <summary>The model used when none is chosen.</summary>
    public string? Model { get; set; }

    /// <summary>Models of this provider that are reached over another wire or endpoint, by model id.</summary>
    public Dictionary<string, ModelDeclaration>? Models { get; set; }

    /// <summary>Whether the entry is complete enough to register the provider.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Id) &&
        !string.IsNullOrWhiteSpace(BaseUrl) &&
        !string.IsNullOrWhiteSpace(Model);

    /// <summary>The library declaration this entry stands for.</summary>
    public ProviderDeclaration ToDeclaration() => new()
    {
        Id = Id ?? string.Empty,
        Wire = Wire,
        BaseUrl = BaseUrl ?? string.Empty,
        ApiKey = ApiKey,
        KeyHeader = KeyHeader,
        KeyScheme = KeyScheme,
        Headers = Headers,
        Models = Models,
    };
}
