using Iyu.Conventions.Testing;
using Xunit;

namespace IronHive.Host.Tests;

/// <summary>
/// The public surface follows the two API rules of the ecosystem: every public async method takes a
/// <see cref="CancellationToken"/>, and failure is reported by an exception rather than by a returned object carrying a
/// success flag and an error. The scans are <c>Iyu.Conventions.Testing</c>'s, over the same assemblies as the
/// operational-language scan.
/// </summary>
/// <remarks>
/// The rosters are the methods that break a rule today. Shrink them; never grow them silently. A change to a listed
/// method's parameters changes its entry, which is a roster change on purpose.
/// </remarks>
public class PublicApiConventionTests
{
    private static readonly string[] KnownUncancellable =
    [
        "IronHive.Cli.Commands.UpdateChecker.WaitForCheckAsync(Nullable<TimeSpan>)",
        "IronHive.Host.Extensions.AgentLoopSessionExtensions.LoadOrCreateSessionAsync(IAgentLoop, ISessionManager, String, String, Boolean)",
        "IronHive.Host.Extensions.AgentLoopSessionExtensions.LoadSessionAsync(IAgentLoop, ISessionManager, String)",
        "IronHive.Host.Extensions.AgentLoopSessionExtensions.SaveTurnAsync(ISessionManager, Session, String, AgentResponse)",
        "IronHive.Host.Oops.IOopsService.BackAsync(String, Int32)",
        "IronHive.Host.Oops.IOopsService.ChangesAsync(String, Nullable<Int32>, Nullable<Int32>)",
        "IronHive.Host.Oops.IOopsService.CleanupAsync(Boolean)",
        "IronHive.Host.Oops.IOopsService.HistoryAsync(String)",
        "IronHive.Host.Oops.IOopsService.SaveAsync(String, String)",
        "IronHive.Host.Oops.IOopsService.StartAsync(String)",
        "IronHive.Host.Oops.IOopsService.StatusAsync(String)",
        "IronHive.Host.Oops.IOopsService.StopAsync(String)",
        "IronHive.Host.Oops.IOopsService.UndoAsync(String)",
        "IronHive.Host.Server.AgentServerRunner.WriteEventAsync(TextWriter, ServerEvent, JsonSerializerOptions)",
        "IronHive.Host.Server.IExecutionLogger.BeginTurnAsync(String)",
        "IronHive.Host.Server.IExecutionLogger.EndTurnAsync(Nullable<Int32>)",
        "IronHive.Host.Server.IExecutionLogger.ProcessChunkAsync(AgentResponseChunk)",
        "IronHive.Host.Session.ISessionManager.CreateSessionAsync(String, String)",
        "IronHive.Host.Session.ISessionManager.DeleteSessionAsync(String)",
        "IronHive.Host.Session.ISessionManager.EndSessionAsync(Session, String)",
        "IronHive.Host.Session.ISessionManager.ForkSessionAsync(Session, Nullable<Int32>)",
        "IronHive.Host.Session.ISessionManager.GetLatestSessionAsync(String)",
        "IronHive.Host.Session.ISessionManager.ListSessionsAsync(String, Int32)",
        "IronHive.Host.Session.ISessionManager.LoadSessionAsync(String)",
        "IronHive.Host.Session.ISessionManager.RestoreContextAsync(Session)",
        "IronHive.Host.Session.ISessionManager.SaveAssistantMessageAsync(Session, String)",
        "IronHive.Host.Session.ISessionManager.SaveToolResultAsync(Session, String, String, Boolean)",
        "IronHive.Host.Session.ISessionManager.SaveToolUseAsync(Session, String, Object, String)",
        "IronHive.Host.Session.ISessionManager.SaveUserMessageAsync(Session, String)",
    ];

    private static readonly string[] KnownResultReturns =
    [
        "IronHive.Host.Integration.ICodeExecutionProvider.CreateSessionAsync(String, CancellationToken)",
        "IronHive.Host.Integration.ICodeExecutionProvider.DestroySessionAsync(String, CancellationToken)",
        "IronHive.Host.Integration.ICodeExecutionProvider.ExecuteAsync(String, String, String, Int32, CancellationToken)",
        "IronHive.Host.Integration.ICodeExecutionProvider.InstallPackagesAsync(String, String[], CancellationToken)",
        "IronHive.Host.Integration.ICodeExecutionProvider.ListSessionsAsync(CancellationToken)",
        "IronHive.Host.Integration.IMemoryToolsProvider.ForgetAsync(String, String, CancellationToken)",
        "IronHive.Host.Integration.IMemoryToolsProvider.RecallAsync(String, String, Int32, CancellationToken)",
        "IronHive.Host.Integration.IMemoryToolsProvider.SearchAsync(String, MemorySearchOptions, CancellationToken)",
        "IronHive.Host.Integration.IMemoryToolsProvider.StoreAsync(String, String, Single, String, CancellationToken)",
        "IronHive.Host.Oops.IOopsService.BackAsync(String, Int32)",
        "IronHive.Host.Oops.IOopsService.ChangesAsync(String, Nullable<Int32>, Nullable<Int32>)",
        "IronHive.Host.Oops.IOopsService.CleanupAsync(Boolean)",
        "IronHive.Host.Oops.IOopsService.HistoryAsync(String)",
        "IronHive.Host.Oops.IOopsService.SaveAsync(String, String)",
        "IronHive.Host.Oops.IOopsService.StartAsync(String)",
        "IronHive.Host.Oops.IOopsService.StatusAsync(String)",
        "IronHive.Host.Oops.IOopsService.StopAsync(String)",
        "IronHive.Host.Oops.IOopsService.UndoAsync(String)",
        "IronHive.Host.Update.IUpdateService.UpdateAsync(IProgress<UpdateProgress>, CancellationToken)",
    ];

    [Fact]
    public void PublicAsyncMethods_TakeACancellationToken() =>
        AsyncCancellation.Scan(OptionsReachabilityRosterTests.Libraries).ShouldMatchRoster(KnownUncancellable);

    [Fact]
    public void PublicMethods_DoNotReturnResultObjects() =>
        ResultReturns.Scan(OptionsReachabilityRosterTests.Libraries).ShouldMatchRoster(KnownResultReturns);

    // Positive control: an empty roster would also pass if the scan saw no public method at all.
    [Fact]
    public void Scan_SeesThePublicSurface() =>
        Assert.True(ResultReturns.Scan(OptionsReachabilityRosterTests.Libraries).MembersRead > 0, "the scan read too few public methods");
}
