using IronHive.Host.Protocol;
using Microsoft.Extensions.AI;

namespace IronHive.Host.Server;

/// <summary>
/// Converts the wire-level <see cref="TurnOptions"/> of a <see cref="UserMessageRequest"/> into the
/// per-turn <see cref="ChatOptions"/> override the agent loop accepts.
/// </summary>
/// <remarks>
/// The override is applied by the loop field by field: a set field replaces the loop's configured
/// value for that turn only, an unset (<c>null</c>) field keeps the loop's value, and the next turn
/// without options is back on the loop's configuration. Tool names are resolved against the tools
/// the host registered on the loop; a name that is not registered is an error, not a silent drop,
/// so a client cannot believe a turn was restricted when it was not.
/// </remarks>
public static class TurnOptionsMapper
{
    /// <summary>
    /// Builds the per-turn override for <paramref name="options"/>, or <c>null</c> when the request
    /// carried none (the loop then runs on its own configuration, exactly as before options existed).
    /// </summary>
    /// <param name="options">The request's turn options, or <c>null</c>.</param>
    /// <param name="registeredTools">The tools the host registered on the loop; the universe
    /// <see cref="TurnOptions.ToolNames"/> and a <c>require:&lt;name&gt;</c> tool mode select from.</param>
    /// <exception cref="ArgumentException">
    /// A tool name is not registered, or <see cref="TurnOptions.ToolMode"/> /
    /// <see cref="TurnOptions.ReasoningEffort"/> is not one of the documented values.
    /// </exception>
    public static ChatOptions? ToChatOptions(TurnOptions? options, IReadOnlyCollection<AITool> registeredTools)
    {
        ArgumentNullException.ThrowIfNull(registeredTools);

        if (options is null)
        {
            return null;
        }

        var chatOptions = new ChatOptions
        {
            Temperature = options.Temperature,
            MaxOutputTokens = options.MaxOutputTokens
        };

        if (options.ToolNames is not null)
        {
            var byName = registeredTools.ToDictionary(t => t.Name, StringComparer.Ordinal);
            var unknown = options.ToolNames.Where(n => !byName.ContainsKey(n)).Distinct(StringComparer.Ordinal).ToList();
            if (unknown.Count > 0)
            {
                throw new ArgumentException(
                    $"Turn options name tool(s) that are not registered on this agent: {string.Join(", ", unknown)}. " +
                    $"Registered tools: {string.Join(", ", byName.Keys.Order(StringComparer.Ordinal))}.",
                    nameof(options));
            }

            chatOptions.Tools = options.ToolNames.Distinct(StringComparer.Ordinal).Select(n => byName[n]).ToList();
        }

        if (options.ToolMode is not null)
        {
            chatOptions.ToolMode = ParseToolMode(options.ToolMode, registeredTools);
        }

        if (options.ReasoningEffort is not null)
        {
            chatOptions.Reasoning = new ReasoningOptions { Effort = ParseReasoningEffort(options.ReasoningEffort) };
        }

        return chatOptions;
    }

    private static ChatToolMode ParseToolMode(string mode, IReadOnlyCollection<AITool> registeredTools)
    {
        const string requirePrefix = "require:";

        if (mode.StartsWith(requirePrefix, StringComparison.Ordinal))
        {
            var name = mode[requirePrefix.Length..];
            if (!registeredTools.Any(t => string.Equals(t.Name, name, StringComparison.Ordinal)))
            {
                throw new ArgumentException(
                    $"Turn options require tool '{name}', which is not registered on this agent.", nameof(mode));
            }

            return ChatToolMode.RequireSpecific(name);
        }

        return mode switch
        {
            "auto" => ChatToolMode.Auto,
            "none" => ChatToolMode.None,
            "require_any" => ChatToolMode.RequireAny,
            _ => throw new ArgumentException(
                $"Unknown tool mode '{mode}'. Expected auto, none, require_any or require:<tool name>.", nameof(mode))
        };
    }

    private static ReasoningEffort ParseReasoningEffort(string effort) => effort switch
    {
        "none" => ReasoningEffort.None,
        "low" => ReasoningEffort.Low,
        "medium" => ReasoningEffort.Medium,
        "high" => ReasoningEffort.High,
        "extra_high" => ReasoningEffort.ExtraHigh,
        _ => throw new ArgumentException(
            $"Unknown reasoning effort '{effort}'. Expected none, low, medium, high or extra_high.", nameof(effort))
    };
}
