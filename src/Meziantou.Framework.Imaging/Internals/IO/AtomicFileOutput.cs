namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A path output published atomically: bytes go to a new temporary file in the destination
/// directory, which replaces the destination (a same-directory rename) only after the output completed successfully.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>The temporary file is created with <see cref="FileMode.CreateNew"/> (an existing file is never reused or truncated) and a random name, so it is owned by this output.</description></item>
/// <item><description>On failure, cancellation or abort, the temporary file is closed and deleted, and an existing destination is left untouched. Only the owned temporary file is ever deleted.</description></item>
/// <item><description>No crash-durability guarantee is made: the data is not flushed to the storage device (no fsync) before the rename.</description></item>
/// </list>
/// </remarks>
internal sealed class AtomicFileOutput
{
    private FileStream? _stream;

    private AtomicFileOutput(string destinationPath, string temporaryPath, FileStream stream)
    {
        DestinationPath = destinationPath;
        TemporaryPath = temporaryPath;
        _stream = stream;
    }

    /// <summary>Gets the full destination path.</summary>
    public string DestinationPath { get; }

    /// <summary>Gets the full path of the owned temporary file.</summary>
    public string TemporaryPath { get; }

    /// <summary>Gets the stream of the temporary file.</summary>
    /// <exception cref="ObjectDisposedException">The output was published or aborted.</exception>
    public FileStream Stream => _stream ?? throw new ObjectDisposedException(nameof(AtomicFileOutput));

    /// <summary>Gets a value indicating whether the destination was replaced.</summary>
    public bool IsPublished { get; private set; }

    /// <summary>Creates the temporary file next to <paramref name="path"/>.</summary>
    /// <param name="path">The destination path.</param>
    /// <param name="asynchronous">Whether the file is opened for asynchronous I/O.</param>
    /// <returns>The output.</returns>
    /// <exception cref="IOException">The directory does not exist or the file cannot be created.</exception>
    public static AtomicFileOutput Create(string path, bool asynchronous)
    {
        var destination = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(destination) ?? throw new ArgumentException($"The path '{path}' has no directory.", nameof(path));
        var name = Path.GetFileName(destination);
        if (name.Length == 0)
            throw new ArgumentException($"The path '{path}' does not name a file.", nameof(path));

        var attempts = 0;
        while (true)
        {
            var temporary = Path.Combine(directory, "." + name + "." + Guid.NewGuid().ToString("N")[..12] + ".tmp");
            try
            {
                var stream = new FileStream(temporary, new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    BufferSize = 0,
                    Options = asynchronous ? FileOptions.Asynchronous : FileOptions.None,
                });
                return new AtomicFileOutput(destination, temporary, stream);
            }
            catch (IOException) when (++attempts < 5 && File.Exists(temporary))
            {
                // Name collision with a file we do not own: never touch it, try another name
            }
        }
    }

    /// <summary>Closes the temporary file and atomically replaces the destination with it.</summary>
    public void Publish()
    {
        var stream = Stream;
        stream.Flush();
        stream.Dispose();
        _stream = null;
        File.Move(TemporaryPath, DestinationPath, overwrite: true);
        IsPublished = true;
    }

    /// <summary>Asynchronously flushes and closes the temporary file, then atomically replaces the destination with it.</summary>
    public async ValueTask PublishAsync(CancellationToken cancellationToken)
    {
        var stream = Stream;
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        await stream.DisposeAsync().ConfigureAwait(false);
        _stream = null;
        cancellationToken.ThrowIfCancellationRequested();
        File.Move(TemporaryPath, DestinationPath, overwrite: true);
        IsPublished = true;
    }

    /// <summary>Closes and deletes the temporary file, leaving the destination untouched. Best effort: never throws.</summary>
    public void Abort()
    {
        if (IsPublished)
            return;

        try
        {
            _stream?.Dispose();
        }
        catch (IOException)
        {
            // Closing may flush buffered bytes to a failing device; the file is deleted anyway
        }

        _stream = null;
        DeleteTemporaryFile();
    }

    /// <summary>Asynchronously closes and deletes the temporary file. Best effort: never throws.</summary>
    public async ValueTask AbortAsync()
    {
        if (IsPublished)
            return;

        try
        {
            if (_stream is not null)
            {
                await _stream.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
            // See Abort
        }

        _stream = null;
        DeleteTemporaryFile();
    }

    private void DeleteTemporaryFile()
    {
        try
        {
            File.Delete(TemporaryPath);
        }
        catch (IOException)
        {
            // Best effort: the temporary file is hidden and named after the destination
        }
        catch (UnauthorizedAccessException)
        {
            // Same
        }
    }
}
