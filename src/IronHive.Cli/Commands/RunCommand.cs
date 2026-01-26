using System.ComponentModel;
using System.Text.Json;
using IronHive.Cli.Core.Agent;
using Spectre.Console;
using Spectre.Console.Cli;

namespace IronHive.Cli.Commands;

/// <summary>
/// Run command - executes a single prompt and exits.
/// </summary>
public class RunCommand : AsyncCommand<RunCommand.Settings>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IAgentLoop _agentLoop;

    public RunCommand(IAgentLoop agentLoop)
    {
        _agentLoop = agentLoop;
    }

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "[PROMPT]")]
        [Description("The prompt to execute")]
        public string? PromptArg { get; init; }

        [CommandOption("-p|--prompt <PROMPT>")]
        [Description("The prompt to execute (alternative to argument)")]
        public string? PromptOption { get; init; }

        [CommandOption("-m|--model <MODEL>")]
        [Description("Model to use")]
        public string? Model { get; init; }

        [CommandOption("--json")]
        [Description("Output response as JSON")]
        public bool Json { get; init; }

        public string? GetPrompt() => PromptArg ?? PromptOption;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var prompt = settings.GetPrompt();

        if (string.IsNullOrWhiteSpace(prompt))
        {
            AnsiConsole.MarkupLine("[red]Error: Prompt is required.[/]");
            AnsiConsole.MarkupLine("[grey]Usage: ironhive run -p \"your prompt here\"[/]");
            return 1;
        }

        try
        {
            var response = await _agentLoop.RunAsync(prompt);

            if (settings.Json)
            {
                var json = JsonSerializer.Serialize(new
                {
                    content = response.Content,
                    usage = response.Usage is not null ? new
                    {
                        input_tokens = response.Usage.InputTokens,
                        output_tokens = response.Usage.OutputTokens,
                        total_tokens = response.Usage.TotalTokens
                    } : null
                }, JsonOptions);

                Console.WriteLine(json);
            }
            else
            {
                Console.WriteLine(response.Content);
            }

            return 0;
        }
        catch (Exception ex)
        {
            if (settings.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    error = ex.Message
                }, JsonOptions));
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
            }

            return 1;
        }
    }
}
