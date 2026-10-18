using IronHive.Agent.Loop;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace IronHive.Cli.Infrastructure;

/// <summary>
/// Exports the process's spans over OTLP when the standard OpenTelemetry environment asks for it: the agent turn
/// (<see cref="AgentTelemetry.SourceName"/>) and each model call under it (<see cref="ChatSourceName"/>).
/// The libraries only emit through <c>ActivitySource</c>; the exporter belongs to the executable, so an embedder of the
/// libraries chooses its own.
/// </summary>
internal static class OtlpTracing
{
    /// <summary>The source name of the model-call spans the CLI's chat pipeline emits (M.E.AI <c>UseOpenTelemetry</c>).</summary>
    public const string ChatSourceName = "IronHive.Cli";

    /// <summary>
    /// Starts the exporter when <c>OTEL_EXPORTER_OTLP_TRACES_ENDPOINT</c> or <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set and
    /// <c>OTEL_SDK_DISABLED</c> is not <c>true</c>; null otherwise. The protocol is
    /// <c>OTEL_EXPORTER_OTLP_TRACES_PROTOCOL</c> / <c>OTEL_EXPORTER_OTLP_PROTOCOL</c> (<c>grpc</c> unless <c>http/protobuf</c>;
    /// over HTTP a base endpoint gets <c>/v1/traces</c>, as the specification says); headers and timeouts are read by the
    /// exporter from the standard variables. The service is named by <c>OTEL_SERVICE_NAME</c>, else <c>ironhive</c>.
    /// Disposing the provider flushes the spans not yet sent.
    /// </summary>
    public static TracerProvider? StartFromEnvironment(string serviceVersion, Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        var tracesEndpoint = environment("OTEL_EXPORTER_OTLP_TRACES_ENDPOINT");
        var baseEndpoint = environment("OTEL_EXPORTER_OTLP_ENDPOINT");
        if (string.Equals(environment("OTEL_SDK_DISABLED"), "true", StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrWhiteSpace(tracesEndpoint) && string.IsNullOrWhiteSpace(baseEndpoint)))
        {
            return null;
        }

        var http = string.Equals(
            environment("OTEL_EXPORTER_OTLP_TRACES_PROTOCOL") ?? environment("OTEL_EXPORTER_OTLP_PROTOCOL"),
            "http/protobuf", StringComparison.OrdinalIgnoreCase);
        var endpoint = !string.IsNullOrWhiteSpace(tracesEndpoint)
            ? tracesEndpoint
            : http ? baseEndpoint!.TrimEnd('/') + "/v1/traces" : baseEndpoint!;

        var serviceName = environment("OTEL_SERVICE_NAME") is { Length: > 0 } name ? name : "ironhive";
        return Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName, serviceVersion: serviceVersion))
            .AddSource(AgentTelemetry.SourceName, ChatSourceName)
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(endpoint);
                options.Protocol = http ? OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf : OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
            })
            .Build();
    }
}
