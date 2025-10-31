namespace Meziantou.Framework.DependencyScanning;

/// <summary>Represents the location of a dependency value in a source file, providing the ability to update the value in place.</summary>
public abstract class Location
{
    protected IFileSystem FileSystem { get; }

    protected Location(IFileSystem fileSystem, string filePath)
    {
        FileSystem = fileSystem;
        FilePath = filePath;
    }

    /// <summary>Gets the path of the file containing the dependency.</summary>
    public string FilePath { get; }

    /// <summary>Gets a value indicating whether this location can be updated.</summary>
    public abstract bool IsUpdatable { get; }

    /// <summary>Updates the value at this location in the source file.</summary>
    /// <param name="oldValue">The expected current value, used for validation.</param>
    /// <param name="newValue">The new value to write.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">Thrown when the location is not updatable.</exception>
    public async Task UpdateAsync(string? oldValue, string newValue, CancellationToken cancellationToken = default)
    {
        EnsureUpdatable();
        await UpdateCoreAsync(oldValue, newValue, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Updates the value at this location in the source file.</summary>
    /// <param name="newValue">The new value to write.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">Thrown when the location is not updatable.</exception>
    public Task UpdateAsync(string newValue, CancellationToken cancellationToken = default)
    {
        return UpdateAsync(oldValue: null, newValue, cancellationToken);
    }

    /// <summary>When overridden in a derived class, performs the actual update operation.</summary>
    /// <param name="oldValue">The expected current value, used for validation.</param>
    /// <param name="newValue">The new value to write.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    protected internal abstract Task UpdateCoreAsync(string? oldValue, string newValue, CancellationToken cancellationToken);

    private void EnsureUpdatable()
    {
        if (!IsUpdatable)
            throw new InvalidOperationException("Location is not updatable");
    }
}
