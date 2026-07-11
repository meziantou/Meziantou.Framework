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
        return await Runtime.OpenReadAsync(id, path, cancellationToken).ConfigureAwait(false);
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
        await Runtime.WriteFileAsync(id, path, content, cancellationToken).ConfigureAwait(false);
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

        await Runtime.CopyToContainerAsync(id, source, destination, cancellationToken).ConfigureAwait(false);
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

        await Runtime.CopyFromContainerAsync(id, source, destination, cancellationToken).ConfigureAwait(false);
    }
}
