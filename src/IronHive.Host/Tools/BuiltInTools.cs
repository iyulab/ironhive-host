using IronHive.Host.Oops;
using Microsoft.Extensions.AI;
using AgentBuiltInTools = IronHive.Agent.Tools.BuiltInTools;

namespace IronHive.Host.Tools;

/// <summary>
/// The host's tool list: the agent's built-in file, search, shell and todo tools, plus what only a host
/// has — versioned writes, web search and deep research.
/// </summary>
/// <remarks>
/// The file tools themselves live in <c>IronHive.Agent</c>; this type adds to that list and never
/// re-implements it. Versioning attaches to the agent's <c>WriteFile</c> through
/// <see cref="OopsFileWriteInterceptor"/>.
/// </remarks>
public static class BuiltInTools
{
    /// <summary>
    /// Gets all built-in tools as AITool instances.
    /// </summary>
    /// <param name="workingDirectory">Working directory for tools.</param>
    /// <returns>List of AI tools.</returns>
    public static IList<AITool> GetAll(string? workingDirectory = null)
    {
        return GetAll(workingDirectory, oopsService: null);
    }

    /// <summary>
    /// Gets all built-in tools with oops versioning support.
    /// </summary>
    /// <param name="workingDirectory">Working directory for tools.</param>
    /// <param name="oopsService">Optional oops service for file versioning.</param>
    /// <returns>List of AI tools.</returns>
    public static IList<AITool> GetAll(string? workingDirectory, IOopsService? oopsService)
    {
        return GetAll(workingDirectory, oopsService, webSearchTool: null, deepResearchTool: null);
    }

    /// <summary>
    /// Gets all built-in tools with oops versioning and web search support.
    /// </summary>
    /// <param name="workingDirectory">Working directory for tools.</param>
    /// <param name="oopsService">Optional oops service for file versioning.</param>
    /// <param name="webSearchTool">Optional web search tool instance.</param>
    /// <param name="deepResearchTool">Optional deep research tool instance.</param>
    /// <returns>List of AI tools.</returns>
    public static IList<AITool> GetAll(
        string? workingDirectory,
        IOopsService? oopsService,
        WebSearchTool? webSearchTool,
        DeepResearchTool? deepResearchTool = null)
    {
        var toolList = AgentBuiltInTools.GetAll(
            workingDirectory,
            oopsService is null
                ? null
                : new IronHive.Agent.Tools.FileToolOptions { WriteInterceptor = new OopsFileWriteInterceptor(oopsService) });

        if (webSearchTool is not null)
        {
            toolList.Add(AIFunctionFactory.Create(webSearchTool.WebSearch));
            toolList.Add(AIFunctionFactory.Create(webSearchTool.ExploreSite));
        }

        if (deepResearchTool is not null)
        {
            toolList.Add(AIFunctionFactory.Create(deepResearchTool.DeepResearch));
        }

        return toolList;
    }
}
