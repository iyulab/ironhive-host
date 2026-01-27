using System.Reflection;
using IronHive.Cli.Commands;
using IronHive.Cli.Infrastructure;
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

        config.AddCommand<ConfigCommand>("config")
            .WithDescription("Manage configuration")
            .WithExample("config", "show");

        config.AddCommand<UpdateCommand>("update")
            .WithDescription("Check for and install updates")
            .WithExample("update")
            .WithExample("update", "--check");
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
