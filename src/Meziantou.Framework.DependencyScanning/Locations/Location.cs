namespace Meziantou.Framework.DependencyScanning;

/// <summary>Represents the location of a dependency value in a source file, providing the ability to update the value in place.</summary>
/// <remarks>
/// A location remembers the value it holds: the value reported with it when the dependency was scanned, then the
/// last value it wrote. An update checks that the file still holds that value before writing, even when no expected
/// value is given, so a file modified since the scan is never overwritten blindly.
/// </remarks>
public abstract class Location
{
    private string? _currentValue;
    private Location? _sibling;

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

    /// <summary>Gets the value the location holds: the value it was scanned with, or the last value it wrote. <see langword="null"/> when it is unknown.</summary>
    internal string? CurrentValue => Volatile.Read(ref _currentValue);

    /// <summary>Gets whether this location is the one that tracks the written value, rather than <see cref="UpdateAsync(string?, string, CancellationToken)"/>.</summary>
    private protected virtual bool TracksWrittenValue => false;

    /// <summary>Records the value found at this location during the scan, unless one is already known.</summary>
    internal void InitializeCurrentValue(string? value)
    {
        if (value is not null)
        {
            Interlocked.CompareExchange(ref _currentValue, value, comparand: null);
        }
    }

    /// <summary>Links this location to the other location of its dependency, so that it follows the shifts an update of the other one causes.</summary>
    internal void LinkSibling(Location sibling)
    {
        if (!ReferenceEquals(sibling, this))
        {
            Interlocked.CompareExchange(ref _sibling, sibling, comparand: null);
        }
    }

    /// <summary>Tells the other location of the dependency that this location replaced <paramref name="oldLength"/> characters at <paramref name="start"/> with <paramref name="newLength"/> characters.</summary>
    /// <remarks>The name and the version of a dependency often share a line or a value, such as <c>FROM node:18</c>, so updating the first one moves the second one.</remarks>
    private protected void NotifyValueReplaced(int start, int oldLength, int newLength)
    {
        Volatile.Read(ref _sibling)?.OnSiblingValueReplaced(this, start, oldLength, newLength);
    }

    /// <summary>Moves this location when the other location of its dependency replaced a value before it, in the same line or value.</summary>
    private protected virtual void OnSiblingValueReplaced(Location sibling, int start, int oldLength, int newLength)
    {
    }

    /// <summary>Records the value written at this location, so that the next update expects it.</summary>
    private protected void SetCurrentValue(string value)
    {
        Volatile.Write(ref _currentValue, value);
    }

    /// <summary>Updates the value at this location in the source file.</summary>
    /// <param name="oldValue">
    /// The expected current value, used for validation. When <see langword="null"/>, the value the location holds is
    /// expected: the value reported by the scan, or the last value written through this location.
    /// </param>
    /// <param name="newValue">The new value to write.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">Thrown when the location is not updatable.</exception>
    /// <exception cref="DependencyScannerException">The file does not hold the expected value at this location anymore.</exception>
    public async Task UpdateAsync(string? oldValue, string newValue, CancellationToken cancellationToken = default)
    {
        EnsureUpdatable();
        await UpdateCoreAsync(oldValue ?? CurrentValue, newValue, cancellationToken).ConfigureAwait(false);
        if (!TracksWrittenValue)
        {
            SetCurrentValue(newValue);
        }
    }

    /// <summary>Updates the value at this location in the source file, after checking that it still holds the value the location holds.</summary>
    /// <param name="newValue">The new value to write.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">Thrown when the location is not updatable.</exception>
    /// <exception cref="DependencyScannerException">The file does not hold the expected value at this location anymore.</exception>
    public Task UpdateAsync(string newValue, CancellationToken cancellationToken = default)
    {
        return UpdateAsync(oldValue: null, newValue, cancellationToken);
    }

    /// <summary>When overridden in a derived class, performs the actual update operation.</summary>
    /// <param name="oldValue">
    /// The expected current value, used for validation. It is <see langword="null"/> only when the caller gave no
    /// expected value and the location does not know the value it holds.
    /// </param>
    /// <param name="newValue">The new value to write.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    protected internal abstract Task UpdateCoreAsync(string? oldValue, string newValue, CancellationToken cancellationToken);

    /// <summary>Gets whether the file system can write the file, which it cannot when the file was scanned from in-memory content.</summary>
    private protected bool CanWriteFile => FileSystem is not Internals.SingleFileInMemoryFileSystem;

    private void EnsureUpdatable()
    {
        if (!IsUpdatable)
            throw new InvalidOperationException("Location is not updatable");
    }
}
