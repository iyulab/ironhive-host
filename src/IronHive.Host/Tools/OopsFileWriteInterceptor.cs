using IronHive.Agent.Tools;
using IronHive.Host.Oops;

namespace IronHive.Host.Tools;

/// <summary>
/// Versions the files the agent writes: records each edit, starts tracking a file that keeps being
/// edited, and saves a snapshot after every write to a tracked file.
/// </summary>
public sealed class OopsFileWriteInterceptor : IFileWriteInterceptor
{
    private readonly IOopsService _oopsService;

    /// <param name="oopsService">The versioning service.</param>
    public OopsFileWriteInterceptor(IOopsService oopsService)
    {
        ArgumentNullException.ThrowIfNull(oopsService);
        _oopsService = oopsService;
    }

    /// <inheritdoc />
    public async Task<string?> InterceptAsync(string fullPath, Func<Task> write, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(write);

        // Track the edit, and auto-activate versioning for a file that has been edited repeatedly.
        _oopsService.RecordEdit(fullPath);

        var activated = false;
        if (_oopsService.ShouldAutoActivate(fullPath) && !_oopsService.IsTracked(fullPath))
        {
            await _oopsService.StartAsync(fullPath);
            activated = true;
        }

        await write();

        if (!_oopsService.IsTracked(fullPath))
        {
            return null;
        }

        var saved = await _oopsService.SaveAsync(fullPath);
        if (!saved.Success)
        {
            return null;
        }

        return activated
            ? " (oops versioning auto-activated)"
            : " (oops snapshot saved)";
    }
}
