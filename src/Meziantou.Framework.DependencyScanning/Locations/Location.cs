namespace Meziantou.Framework.DependencyScanning;

public abstract class Location
{
    protected IFileSystem FileSystem { get; }

    protected Location(IFileSystem fileSystem, string filePath)
    {
        FileSystem = fileSystem;
        FilePath = filePath;
    }

    public string FilePath { get; }

    public abstract bool IsUpdatable { get; }

    public async Task UpdateAsync(string? oldValue, string newValue, CancellationToken cancellationToken = default)
    {
        EnsureUpdatable();
        await UpdateCoreAsync(oldValue, newValue, cancellationToken).ConfigureAwait(false);
    }

    public Task UpdateAsync(string newValue, CancellationToken cancellationToken = default)
    {
        return UpdateAsync(oldValue: null, newValue, cancellationToken);
    }

    protected internal abstract Task UpdateCoreAsync(string? oldValue, string newValue, CancellationToken cancellationToken);

    private void EnsureUpdatable()
    {
        if (!IsUpdatable)
            throw new InvalidOperationException("Location is not updatable");
    }
}
