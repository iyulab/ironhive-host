using System.Reflection;
using IronHive.Host.Commands;
using IronHive.Host.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

try
{
    var services = new ServiceCollection();
    services.AddIronHiveServices();

    var registrar = new TypeRegistrar(services);
    var app = new CommandApp<DefaultCommand>(registrar);

    // Get version from assembly (set by Directory.Build.props)
    var version = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "0.0.0";

    app.Configure(config =>
    {
        config.SetApplicationName("ironhive");
        config.SetApplicationVersion(version);

        config.AddCommand<RunCommand>("run")
            .WithDescription("Run a single prompt and exit")
            .WithExample("run", "-p", "\"List all files in the current directory\"");

        config.AddCommand<SetCommand>("set")
            .WithDescription("Set a configuration value")
            .WithExample("set", "openai.apiKey", "sk-xxx")
            .WithExample("set", "anthropic.model", "claude-sonnet-4-20250514");

        config.AddCommand<GetCommand>("get")
            .WithDescription("Get a configuration value")
            .WithExample("get", "openai.apiKey")
            .WithExample("get");

        config.AddCommand<UnsetCommand>("unset")
            .WithDescription("Remove a configuration value")
            .WithExample("unset", "openai.apiKey");

        config.AddCommand<ConfigCommand>("config")
            .WithDescription("Show all configuration")
            .WithExample("config", "show")
            .WithExample("config", "path");

        config.AddCommand<ModelsCommand>("models")
            .WithDescription("List available models from configured providers")
            .WithExample("models")
            .WithExample("models", "--provider", "openai")
            .WithExample("models", "--json");

        config.AddCommand<DoctorCommand>("doctor")
            .WithDescription("Diagnose configuration and connectivity issues")
            .WithExample("doctor")
            .WithExample("doctor", "--verbose");

        config.AddCommand<UpdateCommand>("update")
            .WithDescription("Check for and install updates")
            .WithExample("update")
            .WithExample("update", "--check");

        config.AddCommand<SessionsCommand>("sessions")
            .WithDescription("List and manage conversation sessions")
            .WithExample("sessions")
            .WithExample("sessions", "list")
            .WithExample("sessions", "delete", "<session-id>");
    });

    return await app.RunAsync(args);
}
catch (InvalidOperationException ex) when (ex.Message.Contains("provider configured"))
{
    // Configuration error - show user-friendly message
    AnsiConsole.MarkupLine("[red]Configuration Error[/]");
    AnsiConsole.WriteLine();
    AnsiConsole.WriteLine(ex.Message);
    return 1;
}
catch (Exception ex)
{
    // Unexpected error - show details for debugging
    AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
#if DEBUG
    AnsiConsole.WriteException(ex);
#endif
    return 1;
}
