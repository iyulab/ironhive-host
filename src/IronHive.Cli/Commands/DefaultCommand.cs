using System.ComponentModel;
using IronHive.Cli.Core.Agent;
using Spectre.Console;
using Spectre.Console.Cli;

namespace IronHive.Cli.Commands;

/// <summary>
/// Default command - enters interactive chat mode.
/// </summary>
public class DefaultCommand : AsyncCommand<DefaultCommand.Settings>
{
    private readonly IAgentLoop _agentLoop;

    public DefaultCommand(IAgentLoop agentLoop)
    {
        _agentLoop = agentLoop;
    }

    public class Settings : CommandSettings
    {
        [CommandOption("-p|--prompt <PROMPT>")]
        [Description("Run a single prompt and exit")]
        public string? Prompt { get; init; }

        [CommandOption("-m|--model <MODEL>")]
        [Description("Model to use (e.g., gpt-4o-mini, llama3.2)")]
        public string? Model { get; init; }

        [CommandOption("--provider <PROVIDER>")]
        [Description("Provider (openai, ollama, gpustack)")]
        public string? Provider { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        // Single prompt mode
        if (!string.IsNullOrWhiteSpace(settings.Prompt))
        {
            return await RunSinglePromptAsync(settings.Prompt);
        }

        // Interactive mode
        return await RunInteractiveAsync();
    }

    private async Task<int> RunSinglePromptAsync(string prompt)
    {
        try
        {
            AnsiConsole.MarkupLine("[grey]Running prompt...[/]");

            var response = await _agentLoop.RunAsync(prompt);

            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Panel(response.Content)
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Blue));

            if (response.Usage is not null)
            {
                AnsiConsole.MarkupLine($"[grey]Tokens: {response.Usage.InputTokens} in / {response.Usage.OutputTokens} out[/]");
            }

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private async Task<int> RunInteractiveAsync()
    {
        AnsiConsole.Write(new FigletText("IronHive")
            .Color(Color.Yellow));

        AnsiConsole.MarkupLine("[grey]Type 'exit' or press Ctrl+C to quit.[/]");
        AnsiConsole.WriteLine();

        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        while (!cts.Token.IsCancellationRequested)
        {
            var prompt = AnsiConsole.Prompt(
                new TextPrompt<string>("[green]>[/] ")
                    .AllowEmpty());

            if (string.IsNullOrWhiteSpace(prompt))
            {
                continue;
            }

            if (prompt.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                prompt.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            try
            {
                var response = await _agentLoop.RunAsync(prompt, cts.Token);

                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[blue]{Markup.Escape(response.Content)}[/]");
                AnsiConsole.WriteLine();

                if (response.Usage is not null)
                {
                    AnsiConsole.MarkupLine($"[grey]({response.Usage.TotalTokens} tokens)[/]");
                }

                AnsiConsole.WriteLine();
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
                break;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
            }
        }

        AnsiConsole.MarkupLine("[grey]Goodbye![/]");
        return 0;
    }
}
