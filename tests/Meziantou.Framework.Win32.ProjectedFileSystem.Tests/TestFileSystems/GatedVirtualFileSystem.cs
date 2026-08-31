namespace Meziantou.Framework.Win32.ProjectedFileSystem;

/// <summary>
/// Test VFS whose OpenReadAsync blocks until the test releases it, so a file data command stays
/// outstanding for as long as the test needs. Stands in for a provider waiting on a slow network.
/// </summary>
internal sealed class GatedVirtualFileSystem : ProjectedFileSystemBase
{
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public GatedVirtualFileSystem(string rootFolder) : base(rootFolder) { }

    /// <summary>Completes once the provider has been asked for the file content.</summary>
    public Task Entered => _entered.Task;

    /// <summary>Lets the pending OpenReadAsync call finish.</summary>
    public void Release() => _release.TrySetResult();

    protected override ValueTask<IEnumerable<ProjectedFileSystemEntry>> GetEntriesAsync(string path)
    {
        if (AreFileNamesEqual(path, ""))
            return ValueTask.FromResult<IEnumerable<ProjectedFileSystemEntry>>([ProjectedFileSystemEntry.File("gated.bin", 4)]);

        return ValueTask.FromResult<IEnumerable<ProjectedFileSystemEntry>>([]);
    }

    protected override async ValueTask<Stream?> OpenReadAsync(string path)
    {
        if (!AreFileNamesEqual(path, "gated.bin"))
            return null;

        _entered.TrySetResult();
        await _release.Task.ConfigureAwait(false);
        return new MemoryStream([1, 2, 3, 4]);
    }
}
