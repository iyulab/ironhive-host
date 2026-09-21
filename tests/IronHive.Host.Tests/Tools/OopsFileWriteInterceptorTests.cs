using IronHive.Host.Oops;
using IronHive.Host.Tools;
using NSubstitute;

namespace IronHive.Host.Tests.Tools;

/// <summary>
/// Versioning used to live inside the host's own copy of <c>WriteFile</c>. It is an interceptor on the
/// agent's tool now; these facts pin the behaviour that moved — order, auto-activation, and what the
/// model is told — so the move cannot have changed it quietly.
/// </summary>
public class OopsFileWriteInterceptorTests
{
    private const string FilePath = "/work/notes.md";

    private readonly IOopsService _oops = Substitute.For<IOopsService>();
    private readonly List<string> _order = [];

    public OopsFileWriteInterceptorTests()
    {
        _oops.When(o => o.RecordEdit(FilePath)).Do(_ => _order.Add("record"));
        _oops.StartAsync(FilePath).Returns(_ =>
        {
            _order.Add("start");
            return new OopsResult { Success = true, Output = "" };
        });
        _oops.SaveAsync(FilePath, Arg.Any<string?>()).Returns(_ =>
        {
            _order.Add("save");
            return new OopsResult { Success = true, Output = "" };
        });
    }

    private Task<string?> Intercept() => new OopsFileWriteInterceptor(_oops).InterceptAsync(
        FilePath,
        () =>
        {
            _order.Add("write");
            return Task.CompletedTask;
        },
        TestContext.Current.CancellationToken);

    [Fact]
    public async Task ATrackedFile_IsSnapshottedAfterTheWrite()
    {
        _oops.IsTracked(FilePath).Returns(true);

        var note = await Intercept();

        Assert.Equal(" (oops snapshot saved)", note);
        Assert.Equal(["record", "write", "save"], _order);
    }

    [Fact]
    public async Task AFileEditedRepeatedly_StartsBeingVersioned_BeforeTheWrite()
    {
        _oops.ShouldAutoActivate(FilePath).Returns(true);
        _oops.IsTracked(FilePath).Returns(false, true); // not tracked when asked first, tracked once started

        var note = await Intercept();

        Assert.Equal(" (oops versioning auto-activated)", note);
        Assert.Equal(["record", "start", "write", "save"], _order);
    }

    [Fact]
    public async Task AnUntrackedFile_IsWrittenAndRecorded_AndNothingElse()
    {
        _oops.IsTracked(FilePath).Returns(false);

        var note = await Intercept();

        Assert.Null(note);
        Assert.Equal(["record", "write"], _order);
    }

    [Fact]
    public async Task AFailedSnapshot_IsNotReportedAsSaved()
    {
        _oops.IsTracked(FilePath).Returns(true);
        _oops.SaveAsync(FilePath, Arg.Any<string?>()).Returns(new OopsResult { Success = false, Output = "" });

        var note = await Intercept();

        Assert.Null(note);
    }
}
