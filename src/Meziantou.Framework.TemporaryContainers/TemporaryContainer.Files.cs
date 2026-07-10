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

        try
        {
            return await OpenReadAsync(id, ["cat", path], cancellationToken).ConfigureAwait(false);
        }
        catch (ProcessExecutionException)
        {
            try
            {
                return await OpenReadAsync(id,
                [
                    "powershell",
                    "-NoProfile",
                    "-Command",
                    "$bytes=[System.IO.File]::ReadAllBytes('" + EscapePowerShellSingleQuotedString(path) + "'); [Console]::OpenStandardOutput().Write($bytes, 0, $bytes.Length)",
                ], cancellationToken).ConfigureAwait(false);
            }
            catch (ProcessExecutionException)
            {
                return await OpenReadUsingCopyAsync(id, path, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<Stream> OpenReadAsync(string id, IReadOnlyList<string> command, CancellationToken cancellationToken)
    {
        var options = new ExecOptions();
        foreach (var item in command)
            options.Command.Add(item);

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

    private async Task<Stream> OpenReadUsingCopyAsync(string id, string path, CancellationToken cancellationToken)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "MezTC_" + Guid.NewGuid().ToString("N"));
        try
        {
            await Cli.RunBufferedAsync(Adapter.BuildCopyFromContainerArguments(id, path, tempFile), cancellationToken).ConfigureAwait(false);
            return new Internals.TemporaryFileStream(tempFile);
        }
        catch
        {
            File.Delete(tempFile);
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

        if (Runtime is ContainerRuntime.Wslc)
        {
            var result = await ExecAsync(options =>
            {
                options.StandardInput = InputSource.FromStream(content);
                options.Command.Add("sh");
                options.Command.Add("-c");
                options.Command.Add("cat > " + QuoteShellArgument(path));
            }, cancellationToken).ConfigureAwait(false);

            if (result.ExitCode != 0)
                throw new InvalidOperationException("Unable to write file to the container. " + result.StandardError);

            return;
        }

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

        if (Runtime is ContainerRuntime.Wslc)
        {
            await using var stream = File.OpenRead(source);
            await WriteFileAsync(destination, stream, cancellationToken).ConfigureAwait(false);
            return;
        }

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

        if (Runtime is ContainerRuntime.Wslc)
        {
            await using var stream = await OpenReadAsync(source, cancellationToken).ConfigureAwait(false);
            await using var fileStream = File.Create(destination);
            await stream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            return;
        }

        await Cli.RunBufferedAsync(Adapter.BuildCopyFromContainerArguments(id, source, destination), cancellationToken).ConfigureAwait(false);
    }

    private static string QuoteShellArgument(string value)
    {
        return "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    private static string EscapePowerShellSingleQuotedString(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}
