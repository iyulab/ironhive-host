using IronHive.Cli.Commands;
using IronHive.Cli.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

var services = new ServiceCollection();
services.AddIronHiveServices();

var registrar = new TypeRegistrar(services);
var app = new CommandApp<DefaultCommand>(registrar);

app.Configure(config =>
{
    config.SetApplicationName("ironhive");
    config.SetApplicationVersion("0.1.0-alpha");

    config.AddCommand<RunCommand>("run")
        .WithDescription("Run a single prompt and exit")
        .WithExample("run", "-p", "\"List all files in the current directory\"");

    config.AddCommand<ConfigCommand>("config")
        .WithDescription("Manage configuration")
        .WithExample("config", "show");

    // Future: plan, chat commands
});

return await app.RunAsync(args);
