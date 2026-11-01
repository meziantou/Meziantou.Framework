namespace Meziantou.Framework.TemporaryContainers;

public partial class TemporaryContainer
{
    /// <summary>Reads a file from the container.</summary>
    /// <param name="path">The path of the file inside the container.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A readable stream positioned at the beginning of the file content. Dispose it once read.</returns>
    /// <remarks>The file is not loaded in memory. The Docker Engine API runtime reads it from the daemon as the stream is read, so the stream cannot seek, but its <see cref="Stream.Length"/> is known; copy it to a <see cref="MemoryStream"/> when you need to seek. The CLI runtimes copy it to a temporary file first, deleted when the stream is disposed.</remarks>
    public async Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        var id = RequireId();
        return await Runtime.OpenReadAsync(id, path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes a file to the container, creating or overwriting it.</summary>
    /// <param name="path">The destination path inside the container.</param>
    /// <param name="content">The content to write. It is read from its current position to its end, and is not disposed.</param>
    /// <remarks>The content is not loaded in memory. The Docker Engine API runtime sends a seekable stream as it reads it, and copies any other stream to a temporary file first, since the archive it sends declares the length of the file before its content.</remarks>
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
