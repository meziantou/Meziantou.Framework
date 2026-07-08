namespace Meziantou.Framework.TemporaryContainers;

public partial class TemporaryContainer
{
    /// <summary>Reads a file from the container.</summary>
    /// <param name="path">The path of the file inside the container.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A readable, seekable stream positioned at the beginning of the file content.</returns>
    public async Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        var id = RequireId();

        var options = new ExecOptions();
        options.Command.Add("cat");
        options.Command.Add(path);
        var args = Adapter.BuildExecArguments(id, options);

        var stream = new MemoryStream();
        try
        {
            await Cli.RunToStreamAsync(args, stream, cancellationToken).ConfigureAwait(false);
            stream.Position = 0;
            return stream;
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Writes a file to the container, creating or overwriting it.</summary>
    /// <param name="path">The destination path inside the container.</param>
    /// <param name="content">The content to write.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the file is written.</returns>
    public async Task WriteFileAsync(string path, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(content);
        var id = RequireId();

        var tempFile = Path.Combine(Path.GetTempPath(), "MezTC_" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var fileStream = File.Create(tempFile))
                await content.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);

            await Cli.RunBufferedAsync(Adapter.BuildCopyToContainerArguments(id, tempFile, path), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>Copies a file or directory from the host into the container.</summary>
    /// <param name="source">The source path on the host.</param>
    /// <param name="destination">The destination path inside the container.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the copy finishes.</returns>
    public async Task CopyToContainerAsync(string source, string destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        var id = RequireId();

        await Cli.RunBufferedAsync(Adapter.BuildCopyToContainerArguments(id, source, destination), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Copies a file or directory from the container to the host.</summary>
    /// <param name="source">The source path inside the container.</param>
    /// <param name="destination">The destination path on the host.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the copy finishes.</returns>
    public async Task CopyFromContainerAsync(string source, string destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        var id = RequireId();

        await Cli.RunBufferedAsync(Adapter.BuildCopyFromContainerArguments(id, source, destination), cancellationToken).ConfigureAwait(false);
    }
}
