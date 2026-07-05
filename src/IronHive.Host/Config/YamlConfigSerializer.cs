using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace IronHive.Host.Config;

/// <summary>Shared YAML read/write for host config (round-trip symmetric).</summary>
public static class YamlConfigSerializer
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public static T? Deserialize<T>(string yaml) => Deserializer.Deserialize<T>(yaml);
    public static string Serialize<T>(T value) => Serializer.Serialize(value!);
}
