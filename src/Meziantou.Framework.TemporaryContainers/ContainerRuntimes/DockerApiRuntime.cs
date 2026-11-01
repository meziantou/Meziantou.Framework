using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Meziantou.Framework;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.TemporaryContainers.Internals;

internal sealed class DockerApiRuntime : ContainerRuntime
{
    private static readonly char[] ContainerPathSeparators = ['/', '\\'];

    // A daemon that is reachable answers '/version' at once. The timeout only matters when the socket exists and the
    // daemon behind it never answers, and it has to leave room for a thread pool that is busy starting many tests.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    private readonly DockerRegistryAuthProvider _authProvider;
    private readonly Lock _connectionLock = new();
    private volatile DockerApiConnection? _connection;
    private Task<DockerApiConnection?>? _connectionProbe;
    private volatile string? _unavailableReason;

    internal DockerApiRuntime()
        : base("DockerApi")
    {
        _authProvider = new DockerRegistryAuthProvider();
    }

    public override async Task<bool> IsSupportedAsync(CancellationToken cancellationToken = default)
        => await EnsureConnectionAsync(cancellationToken).ConfigureAwait(false) is not null;

    internal override bool SupportsReaper => true;

    internal override bool SupportsImageCleanup => true;

    internal override bool LogsIncludeTimestamps => true;

    internal override async Task<string> EnsureCreatedAsync(ContainerDefinition definition, CancellationToken cancellationToken)
    {
        if (definition.ReuseId is { } reuseId && await FindReusableContainerAsync(reuseId, cancellationToken).ConfigureAwait(false) is { } reusedContainerId)
            return reusedContainerId;

        var connection = await EnsureConnectionOrThrowAsync(cancellationToken).ConfigureAwait(false);
        if (definition.Logging.Logger is { } logger)
            Log.DockerApiEndpointSelected(logger, connection.Endpoint.DisplayName, connection.Endpoint.Source);

        var imageRef = await PrepareImageAsync(definition, cancellationToken).ConfigureAwait(false);
        var payload = DockerApiCreateRequestBuilder.Build(definition, imageRef, hostIpSupported: !connection.IsWindowsDaemon);
        using var content = CreateJsonContent(payload, DockerApiJsonContext.Default.CreateContainerRequest);
        var endpoint = "/containers/create";

        // A reused container gets a deterministic name so the daemon itself rejects a second creation: two processes
        // that start at the same time would otherwise both find nothing and both create one.
        var name = definition.Name is { Length: > 0 } definitionName
            ? definitionName
            : definition.ReuseId is { } namedReuseId ? ResourceNaming.GetReuseName(namedReuseId) : null;

        if (name is not null)
            endpoint += "?name=" + Uri.EscapeDataString(name);

        using var response = await SendAsync(HttpMethod.Post, endpoint, content, cancellationToken, allowedStatusCodes: definition.ReuseId is null ? null : [HttpStatusCode.Conflict]).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Conflict)
        {
            // Another process created the container between the lookup and the creation. Adopting it is the whole
            // point of a reuse identifier.
            return await ReuseAdoption.FindAsync(ct => FindReusableContainerAsync(definition.ReuseId!, ct), cancellationToken).ConfigureAwait(false)
                ?? throw CreateException($"Unable to create the container: the name '{name}' is already used by a container that does not belong to this library.", HttpStatusCode.Conflict);
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var createResponse = await JsonSerializer.DeserializeAsync(stream, DockerApiJsonContext.Default.CreateContainerResponse, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(createResponse?.Id))
            throw CreateException("Unable to create the container: the Docker API response does not contain an id.", statusCode: null);

        return createResponse.Id;
    }

    internal override async Task StartAsync(string id, CancellationToken cancellationToken)
    {
        // An adopted container is already running, and the daemon reports that with '304 Not Modified' instead of a success status.
        using var response = await SendAsync(HttpMethod.Post, "/containers/" + Uri.EscapeDataString(id) + "/start", content: null, cancellationToken, allowedStatusCodes: [HttpStatusCode.NotModified]).ConfigureAwait(false);
    }

    internal override async Task StopAsync(string id, CancellationToken cancellationToken)
    {
        // Same as StartAsync: a container that already exited is reported with '304 Not Modified'.
        using var response = await SendAsync(HttpMethod.Post, "/containers/" + Uri.EscapeDataString(id) + "/stop", content: null, cancellationToken, allowedStatusCodes: [HttpStatusCode.NotModified]).ConfigureAwait(false);
    }

    internal override async Task RestartAsync(string id, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, "/containers/" + Uri.EscapeDataString(id) + "/restart", content: null, cancellationToken).ConfigureAwait(false);
    }

    internal override async Task PauseAsync(string id, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, "/containers/" + Uri.EscapeDataString(id) + "/pause", content: null, cancellationToken).ConfigureAwait(false);
    }

    internal override async Task UnpauseAsync(string id, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, "/containers/" + Uri.EscapeDataString(id) + "/unpause", content: null, cancellationToken).ConfigureAwait(false);
    }

    internal override async Task KillAsync(string id, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, "/containers/" + Uri.EscapeDataString(id) + "/kill", content: null, cancellationToken).ConfigureAwait(false);
    }

    internal override async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        // 'v=1' removes the anonymous volumes the image declared, which would otherwise pile up after every run. Named
        // volumes are never touched by it.
        using var response = await SendAsync(HttpMethod.Delete, "/containers/" + Uri.EscapeDataString(id) + "?force=1&v=1", content: null, cancellationToken, allowedStatusCodes: [HttpStatusCode.NotFound]).ConfigureAwait(false);
    }

    internal override async Task<bool> ExistsAsync(string id, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, "/containers/" + Uri.EscapeDataString(id) + "/json", content: null, cancellationToken, allowedStatusCodes: [HttpStatusCode.NotFound]).ConfigureAwait(false);
        return response.StatusCode != HttpStatusCode.NotFound;
    }

    internal override async Task CreateVolumeAsync(VolumeDefinition definition, string name, string instanceId, CancellationToken cancellationToken)
    {
        var labels = ResourceLabels.Build(definition.Labels, definition.ReuseId, sessionOwned: true, definition.Identity);
        labels[ResourceLabels.Instance] = instanceId;

        var payload = new DockerApiModels.VolumeCreateRequest
        {
            Name = name,
            Driver = definition.Driver,
            DriverOpts = definition.DriverOptions.Count > 0 ? definition.DriverOptions.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal) : null,
            Labels = labels,
        };

        using var content = CreateJsonContent(payload, DockerApiJsonContext.Default.VolumeCreateRequest);
        using var response = await SendAsync(HttpMethod.Post, "/volumes/create", content, cancellationToken).ConfigureAwait(false);
    }

    internal override async Task DeleteVolumeAsync(string name, CancellationToken cancellationToken)
    {
        // A volume still used by a container answers 409, which the caller detects by probing the volume again rather
        // than by an exception, so removal behaves like the CLI runtimes.
        using var response = await SendAsync(HttpMethod.Delete, "/volumes/" + Uri.EscapeDataString(name) + "?force=1", content: null, cancellationToken, allowedStatusCodes: [HttpStatusCode.NotFound, HttpStatusCode.Conflict]).ConfigureAwait(false);
    }

    internal override async Task<bool> VolumeExistsAsync(string name, CancellationToken cancellationToken)
        => await GetVolumeLabelsAsync(name, cancellationToken).ConfigureAwait(false) is not null;

    internal override async Task<IReadOnlyDictionary<string, string>?> GetVolumeLabelsAsync(string name, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, "/volumes/" + Uri.EscapeDataString(name), content: null, cancellationToken, allowedStatusCodes: [HttpStatusCode.NotFound]).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var volume = await JsonSerializer.DeserializeAsync(stream, DockerApiJsonContext.Default.VolumeInspectResponse, cancellationToken).ConfigureAwait(false);
        return volume?.Labels ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    internal override async Task DeleteImageAsync(string image, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Delete, "/images/" + Uri.EscapeDataString(image), content: null, cancellationToken, allowedStatusCodes: [HttpStatusCode.NotFound]).ConfigureAwait(false);
    }

    internal override async Task<ContainerInfo> InspectAsync(string id, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, "/containers/" + Uri.EscapeDataString(id) + "/json", content: null, cancellationToken).ConfigureAwait(false);
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var result = await JsonSerializer.DeserializeAsync(stream, DockerApiJsonContext.Default.DockerInspectResult, cancellationToken).ConfigureAwait(false)
            ?? throw CreateException("Unable to inspect the container: the Docker API response is empty.", statusCode: null);

        return DockerContainerInfoParser.ParseInspectResult(result);
    }

    internal override async IAsyncEnumerable<LogEntry> GetLogsAsync(string id, ContainerLogsOptions options, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var endpoint = BuildLogsEndpoint(id, options);
        using var response = await SendAsync(HttpMethod.Get, endpoint, content: null, cancellationToken, completionOption: HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var entry in ReadMultiplexedLogsAsync(stream, cancellationToken).ConfigureAwait(false))
            yield return entry;
    }

    internal static string BuildLogsEndpoint(string id, ContainerLogsOptions options)
    {
        var endpoint = new StringBuilder();
        endpoint.Append("/containers/").Append(Uri.EscapeDataString(id)).Append("/logs?timestamps=1");
        endpoint.Append("&stdout=").Append(options.IncludeStandardOutput ? '1' : '0');
        endpoint.Append("&stderr=").Append(options.IncludeStandardError ? '1' : '0');
        endpoint.Append("&follow=").Append(options.Follow ? '1' : '0');
        if (options.Since is { } since)
            endpoint.Append("&since=").Append(LogTimestamps.FormatUnix(since));

        if (options.Until is { } until)
            endpoint.Append("&until=").Append(LogTimestamps.FormatUnix(until));

        if (options.Tail is { } tail)
            endpoint.Append("&tail=").Append(tail.ToString(CultureInfo.InvariantCulture));

        return endpoint.ToString();
    }

    internal override async Task<ExecResult> ExecAsync(string id, ExecOptions options, CancellationToken cancellationToken)
    {
        var createExecRequest = new DockerApiModels.ExecCreateRequest
        {
            AttachStdout = !options.Detach,
            AttachStderr = !options.Detach,
            AttachStdin = options.StandardInput is not null,
            Tty = options.Tty,
            Privileged = options.Privileged,
            Cmd = [.. options.Command],
            Env = [.. options.Environment.Select(static pair => pair.Key + "=" + pair.Value)],
            User = options.User,
            WorkingDir = options.WorkingDirectory,
        };

        using var createExecContent = CreateJsonContent(createExecRequest, DockerApiJsonContext.Default.ExecCreateRequest);
        using var createExecResponse = await SendAsync(HttpMethod.Post, "/containers/" + Uri.EscapeDataString(id) + "/exec", createExecContent, cancellationToken).ConfigureAwait(false);
        using var createExecStream = await createExecResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var createExecResult = await JsonSerializer.DeserializeAsync(createExecStream, DockerApiJsonContext.Default.ExecCreateResponse, cancellationToken).ConfigureAwait(false);
        var execId = createExecResult?.Id ?? throw CreateException("Unable to create exec command: missing exec id.", statusCode: null);

        var startExecRequest = JsonSerializer.Serialize(new DockerApiModels.ExecStartRequest { Detach = options.Detach, Tty = options.Tty }, DockerApiJsonContext.Default.ExecStartRequest);
        var startEndpoint = "/exec/" + Uri.EscapeDataString(execId) + "/start";

        if (options.Detach)
        {
            using var detachedContent = new StringContent(startExecRequest, Encoding.UTF8, "application/json");
            using var detachedResponse = await SendAsync(HttpMethod.Post, startEndpoint, detachedContent, cancellationToken).ConfigureAwait(false);
            return new ExecResult(0, "", "");
        }

        string standardOutput;
        string standardError;
        if (options.StandardInput is { } standardInput)
        {
            (standardOutput, standardError) = await StartExecWithStandardInputAsync(startEndpoint, startExecRequest, standardInput, options, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            using var startExecContent = new StringContent(startExecRequest, Encoding.UTF8, "application/json");
            using var startExecResponse = await SendAsync(HttpMethod.Post, startEndpoint, startExecContent, cancellationToken, completionOption: HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            await using var startExecStream = await startExecResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            (standardOutput, standardError) = await ReadExecOutputAsync(startExecStream, options, cancellationToken).ConfigureAwait(false);
        }

        var exitCode = await WaitForExecExitCodeAsync(execId, cancellationToken).ConfigureAwait(false);
        return new ExecResult(exitCode, standardOutput, standardError);
    }

    /// <summary>Starts an exec over a connection the daemon takes over, sends the standard input while the output is read, and closes the input side once it is sent, which is how the process sees the end of its input.</summary>
    private async Task<(string StandardOutput, string StandardError)> StartExecWithStandardInputAsync(string endpoint, string body, InputSource standardInput, ExecOptions options, CancellationToken cancellationToken)
    {
        var connection = await EnsureConnectionOrThrowAsync(cancellationToken).ConfigureAwait(false);
        var (upgraded, statusCode, errorBody) = await DockerApiTransport.SendUpgradeRequestAsync(connection.Endpoint, connection.Tls, BuildEndpoint(connection, endpoint), body, cancellationToken).ConfigureAwait(false);
        if (upgraded is null)
            throw CreateException("Docker API request POST " + endpoint + " failed with status " + (int)statusCode + " (" + statusCode.ToString() + "): " + errorBody, statusCode);

        await using (upgraded.ConfigureAwait(false))
        {
            // The output is read while the input is written: a process that echoes its input would otherwise fill the
            // buffers of the connection and never read the rest.
            var output = ReadExecOutputAsync(upgraded.Stream, options, cancellationToken);
            await WriteStandardInputAsync(upgraded, standardInput, cancellationToken).ConfigureAwait(false);
            return await output.ConfigureAwait(false);
        }
    }

    /// <summary>Copies the standard input to the exec. The source is read asynchronously, so a slow source does not hold a thread of the pool while it is pumped.</summary>
    private static async Task WriteStandardInputAsync(DockerApiTransport.UpgradedConnection connection, InputSource standardInput, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            while (true)
            {
                var read = await standardInput.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                    break;

                await connection.Stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            await connection.Stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            await connection.CloseWriteAsync().ConfigureAwait(false);
        }
        catch (IOException)
        {
            // A process that exits without reading its whole input closes the connection under the writer. What it
            // printed and its exit code are still what the caller asked for.
        }
        catch (SocketException)
        {
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Reads the output of an exec: multiplexed frames, or the raw bytes of the terminal when the exec has one. The output goes to the streams of the options when they are set, and is decoded as text otherwise.</summary>
    internal static async Task<(string StandardOutput, string StandardError)> ReadExecOutputAsync(Stream stream, ExecOptions options, CancellationToken cancellationToken)
    {
        if (options.Tty)
        {
            // A terminal has a single output, which carries both streams.
            if (options.StandardOutput is { } target)
            {
                await stream.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                return ("", "");
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            return (await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false), "");
        }

        if (options.StandardOutput is null && options.StandardError is null)
            return await ReadMultiplexedTextAsync(stream, cancellationToken).ConfigureAwait(false);

        var standardOutputDecoder = Encoding.UTF8.GetDecoder();
        var standardErrorDecoder = Encoding.UTF8.GetDecoder();
        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();
        var header = new byte[8];
        while (await FillBufferAsync(stream, header, cancellationToken).ConfigureAwait(false))
        {
            var isStandardError = header[0] is 2;
            var payloadLength = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4));
            if (payloadLength <= 0)
                continue;

            var rented = ArrayPool<byte>.Shared.Rent(payloadLength);
            try
            {
                var payload = rented.AsMemory(0, payloadLength);
                await ReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false);
                if ((isStandardError ? options.StandardError : options.StandardOutput) is { } target)
                    await target.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
                else
                    (isStandardError ? standardError : standardOutput).Append(Decode(isStandardError ? standardErrorDecoder : standardOutputDecoder, payload.Span, flush: false));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        standardOutput.Append(Decode(standardOutputDecoder, [], flush: true));
        standardError.Append(Decode(standardErrorDecoder, [], flush: true));
        return (standardOutput.ToString(), standardError.ToString());
    }

    /// <summary>Reads the exit code of an exec. The output stream ends when the process closes it, which is not necessarily when the process exits, and the daemon records the exit asynchronously, so the exec is polled until the daemon reports it as done.</summary>
    private async Task<int> WaitForExecExitCodeAsync(string execId, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromMilliseconds(10);
        while (true)
        {
            using (var response = await SendAsync(HttpMethod.Get, "/exec/" + Uri.EscapeDataString(execId) + "/json", content: null, cancellationToken).ConfigureAwait(false))
            {
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var result = await JsonSerializer.DeserializeAsync(stream, DockerApiJsonContext.Default.ExecInspectResponse, cancellationToken).ConfigureAwait(false)
                    ?? throw CreateException("Unable to inspect exec command result.", statusCode: null);

                if (!result.Running && result.ExitCode is { } exitCode)
                    return exitCode;
            }

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 500));
        }
    }

    internal override async Task<Stream> OpenReadAsync(string id, string path, CancellationToken cancellationToken)
    {
        var (resolvedPath, _) = await ResolvePathAsync(id, path, cancellationToken).ConfigureAwait(false);
        return await OpenArchiveFileAsync(id, resolvedPath, path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Opens the file the archive endpoint returns for a path. The returned stream reads the file from the response as it is read, and owns the response.</summary>
    private async Task<Stream> OpenArchiveFileAsync(string id, string resolvedPath, string requestedPath, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get, BuildArchiveEndpoint(id, resolvedPath), content: null, cancellationToken, completionOption: HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        try
        {
            var archive = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await DockerApiTarArchive.OpenSingleFileAsync(archive, requestedPath, response, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    internal override async Task WriteFileAsync(string id, string path, Stream content, CancellationToken cancellationToken)
    {
        // A stream whose length is unknown is copied to a temporary file first; any other one is sent as it is read.
        var seekableContent = await DockerApiTarArchive.EnsureSeekableAsync(content, cancellationToken).ConfigureAwait(false);
        try
        {
            var entryName = GetContainerPathName(path);
            await PutArchiveAsync(id, GetContainerParentPath(path), (destination, ct) => DockerApiTarArchive.WriteFileAsync(destination, entryName, seekableContent, DockerApiTarArchive.RegularFileMode, ct), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (!ReferenceEquals(seekableContent, content))
                await seekableContent.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal override async Task CopyToContainerAsync(string id, string source, string destination, CancellationToken cancellationToken)
    {
        // 'docker cp' copies into an existing directory under the name of the source, and renames the source to the destination otherwise.
        var destinationIsDirectory = await StatPathAsync(id, destination, cancellationToken).ConfigureAwait(false) is { IsDirectory: true };
        var targetDirectory = destinationIsDirectory ? destination : GetContainerParentPath(destination);
        var entryName = GetContainerPathName(destinationIsDirectory ? source : destination);

        if (Directory.Exists(source))
        {
            await PutArchiveAsync(id, targetDirectory, (stream, ct) => DockerApiTarArchive.WriteDirectoryAsync(stream, source, entryName + "/", additionalFile: null, ct), cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var file = File.OpenRead(source);
        var mode = DockerApiTarArchive.GetFileMode(source);
        await PutArchiveAsync(id, targetDirectory, (stream, ct) => DockerApiTarArchive.WriteFileAsync(stream, entryName, file, mode, ct), cancellationToken).ConfigureAwait(false);
    }

    internal override async Task CopyFromContainerAsync(string id, string source, string destination, CancellationToken cancellationToken)
    {
        var (resolvedSource, sourceStat) = await ResolvePathAsync(id, source, cancellationToken).ConfigureAwait(false);
        if (sourceStat is { IsDirectory: true })
        {
            using var response = await SendAsync(HttpMethod.Get, BuildArchiveEndpoint(id, resolvedSource), content: null, cancellationToken, completionOption: HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            await using var archive = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            // A destination directory that does not exist yet takes the place of the copied one, so the archive's own top-level directory is dropped.
            var destinationExists = Directory.Exists(destination);
            Directory.CreateDirectory(destination);
            await DockerApiTarArchive.ExtractToDirectoryAsync(archive, destination, stripFirstSegment: !destinationExists, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Same as CopyToContainerAsync, the other way around: an existing directory receives the file under its own name.
        var targetFile = Directory.Exists(destination) ? Path.Combine(destination, GetContainerPathName(source)) : destination;
        if (Path.GetDirectoryName(Path.GetFullPath(targetFile)) is { } parent)
            Directory.CreateDirectory(parent);

        await using var content = await OpenArchiveFileAsync(id, resolvedSource, source, cancellationToken).ConfigureAwait(false);
        await using var file = File.Create(targetFile);
        await content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces a path that is a symbolic link with the path it points to.</summary>
    /// <remarks>The archive endpoint resolves every link of a path but the last one, and archives a last one that is a link as a link, without content. Reading the path through the container (<c>cat</c>) follows the link, so the path is resolved first to behave the same. The daemon reports the target of a link fully resolved, relative to the root of the container.</remarks>
    private async Task<(string Path, DockerApiModels.ContainerPathStat? Stat)> ResolvePathAsync(string id, string path, CancellationToken cancellationToken)
    {
        var stat = await StatPathAsync(id, path, cancellationToken).ConfigureAwait(false);
        if (stat is { IsSymbolicLink: true, LinkTarget: { Length: > 0 } linkTarget })
            return (linkTarget, await StatPathAsync(id, linkTarget, cancellationToken).ConfigureAwait(false));

        return (path, stat);
    }

    /// <summary>Extracts an archive in a directory of the container. The archive is written into the body of the request as it is sent.</summary>
    private async Task PutArchiveAsync(string id, string directory, Func<Stream, CancellationToken, Task> writeArchive, CancellationToken cancellationToken)
    {
        using var content = DockerApiTarArchive.CreateContent(writeArchive);
        using var response = await SendAsync(HttpMethod.Put, BuildArchiveEndpoint(id, directory), content, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the metadata the daemon reports for a container path. Returns <see langword="null"/> when the path does not exist.</summary>
    private async Task<DockerApiModels.ContainerPathStat?> StatPathAsync(string id, string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Head, BuildArchiveEndpoint(id, path), content: null, cancellationToken, allowedStatusCodes: [HttpStatusCode.NotFound]).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!response.Headers.TryGetValues("X-Docker-Container-Path-Stat", out var values) || values.FirstOrDefault() is not { Length: > 0 } encoded)
            return null;

        var buffer = new byte[encoded.Length];
        if (!Convert.TryFromBase64String(encoded, buffer, out var written))
            return null;

        return JsonSerializer.Deserialize(buffer.AsSpan(0, written), DockerApiJsonContext.Default.ContainerPathStat);
    }

    private static string BuildArchiveEndpoint(string id, string path)
        => "/containers/" + Uri.EscapeDataString(id) + "/archive?path=" + Uri.EscapeDataString(path);

    /// <summary>The last segment of a container path. <see cref="Path"/> cannot do it: it splits on the separators of the host, and the path of a Windows container reaches a Linux host unchanged.</summary>
    private static string GetContainerPathName(string path)
    {
        var trimmed = path.TrimEnd(ContainerPathSeparators);
        var separatorIndex = trimmed.LastIndexOfAny(ContainerPathSeparators);
        return separatorIndex < 0 ? trimmed : trimmed[(separatorIndex + 1)..];
    }

    /// <summary>The directory a container path lives in. See <see cref="GetContainerPathName"/> for why <see cref="Path"/> is not used.</summary>
    private static string GetContainerParentPath(string path)
    {
        var trimmed = path.TrimEnd(ContainerPathSeparators);
        return trimmed.LastIndexOfAny(ContainerPathSeparators) switch
        {
            < 0 => ".",
            0 => "/",
            var separatorIndex => trimmed[..separatorIndex],
        };
    }

    internal override async Task<string?> GetRemoteDaemonHostAsync(CancellationToken cancellationToken)
    {
        var connection = await EnsureConnectionOrThrowAsync(cancellationToken).ConfigureAwait(false);
        return HostAddress.GetRemoteHost(connection.Endpoint);
    }

    internal override IReadOnlyDictionary<int, int> ResolvePortMap(ContainerInfo info, ContainerDefinition definition)
    {
        return info.Ports;
    }

    internal override async Task<IReadOnlyList<ManagedResource>> ListManagedContainersAsync(CancellationToken cancellationToken)
        => await ListManagedResourcesAsync("/containers/json?all=1&filters=", cancellationToken).ConfigureAwait(false);

    internal override async Task<IReadOnlyList<ManagedResource>> ListManagedImagesAsync(CancellationToken cancellationToken)
        => await ListManagedResourcesAsync("/images/json?filters=", cancellationToken).ConfigureAwait(false);

    private async Task<IReadOnlyList<ManagedResource>> ListManagedResourcesAsync(string endpointPrefix, CancellationToken cancellationToken)
    {
        var endpoint = endpointPrefix + Uri.EscapeDataString(BuildManagedLabelFilter());
        using var response = await SendAsync(HttpMethod.Get, endpoint, content: null, cancellationToken).ConfigureAwait(false);
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var items = await JsonSerializer.DeserializeAsync(stream, DockerApiJsonContext.Default.ContainerSummaryArray, cancellationToken).ConfigureAwait(false);
        if (items is null)
            return [];

        var resources = new List<ManagedResource>(items.Length);
        foreach (var item in items)
        {
            if (!string.IsNullOrEmpty(item.Id))
                resources.Add(new ManagedResource(item.Id, item.Labels ?? new Dictionary<string, string>(StringComparer.Ordinal)));
        }

        return resources;
    }

    internal override async Task<IReadOnlyList<ManagedResource>> ListManagedVolumesAsync(CancellationToken cancellationToken)
    {
        var endpoint = "/volumes?filters=" + Uri.EscapeDataString(BuildManagedLabelFilter());
        using var response = await SendAsync(HttpMethod.Get, endpoint, content: null, cancellationToken).ConfigureAwait(false);
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var volumes = await JsonSerializer.DeserializeAsync(stream, DockerApiJsonContext.Default.VolumeListResponse, cancellationToken).ConfigureAwait(false);
        if (volumes?.Volumes is null)
            return [];

        var resources = new List<ManagedResource>(volumes.Volumes.Count);
        foreach (var volume in volumes.Volumes)
        {
            if (!string.IsNullOrEmpty(volume.Name))
                resources.Add(new ManagedResource(volume.Name, volume.Labels ?? new Dictionary<string, string>(StringComparer.Ordinal)));
        }

        return resources;
    }

    private static string BuildManagedLabelFilter()
        => "{\"label\":[\"" + ResourceLabels.Managed + "\"]}";

    /// <summary>The path of the daemon socket, as the containers of the daemon see it.</summary>
    /// <remarks>On Linux, the daemon runs on the machine, so the socket this runtime connects to is the one to mount, rootless daemon included. On macOS and Windows, the daemon runs in a virtual machine: the socket this runtime connects to is a forwarding on the host, and the daemon's own socket is at the default path inside the machine.</remarks>
    internal override async Task<string> GetReaperSocketPathAsync(CancellationToken cancellationToken)
    {
        var connection = await EnsureConnectionOrThrowAsync(cancellationToken).ConfigureAwait(false);
        if (OperatingSystem.IsLinux() && connection.Endpoint.UnixSocketPath is { } socketPath)
            return socketPath.Value;

        return DefaultReaperSocketPath;
    }

    private async Task<string?> FindReusableContainerAsync(string reuseId, CancellationToken cancellationToken)
    {
        var labelFilter = JsonEncodedText.Encode(ResourceLabels.ReuseId + "=" + reuseId).ToString();
        var filters = "{\"label\":[\"" + labelFilter + "\"]}";

        var endpoint = "/containers/json?all=1&filters=" + Uri.EscapeDataString(filters);
        using var response = await SendAsync(HttpMethod.Get, endpoint, content: null, cancellationToken).ConfigureAwait(false);
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var containers = await JsonSerializer.DeserializeAsync(stream, DockerApiJsonContext.Default.ContainerSummaryArray, cancellationToken).ConfigureAwait(false);
        if (containers is null)
            return null;

        foreach (var container in containers)
        {
            if (!string.IsNullOrWhiteSpace(container.Id))
                return container.Id;
        }

        return null;
    }

    private async Task<string> PrepareImageAsync(ContainerDefinition definition, CancellationToken cancellationToken)
    {
        var logger = definition.Logging.Logger;
        switch (definition.Image)
        {
            case RegistryImage registry:
                // The Engine API pulls every tag of a reference that has none, where the CLI pulls 'latest'.
                var imageName = ImageReference.WithDefaultTag(registry.Name);
                if (definition.PullPolicy is PullPolicy.Always || definition.PullPolicy is PullPolicy.IfMissing && !await ImageExistsAsync(imageName, logger, cancellationToken).ConfigureAwait(false))
                    await PullImageAsync(imageName, logger, cancellationToken).ConfigureAwait(false);

                return imageName;

            case ExistingImage existing:
                return existing.ImageId;

            case DockerfileImage dockerfile:
                return await BuildImageAsync(dockerfile, ResourceLabels.BuildForImage(definition), cancellationToken).ConfigureAwait(false);

            case ArchiveImage archive:
                return await LoadImageAsync(archive, cancellationToken).ConfigureAwait(false);

            default:
                throw new NotSupportedException($"Image source '{definition.Image.GetType()}' is not supported.");
        }
    }

    private async Task<string> BuildImageAsync(DockerfileImage dockerfile, Dictionary<string, string> labels, CancellationToken cancellationToken)
    {
        var tag = ResourceNaming.BuiltImagePrefix + Guid.NewGuid().ToString("N") + ":latest";
        var contextDirectory = Path.GetFullPath(dockerfile.ContextDirectory);
        var dockerfileName = Path.GetRelativePath(contextDirectory, Path.GetFullPath(dockerfile.DockerfilePath)).Replace('\\', '/');

        // The daemon only ever sees the build context, so a Dockerfile stored outside of it is added to the archive under a name of our own.
        (string SourcePath, string EntryName)? additionalFile = null;
        if (Path.IsPathRooted(dockerfileName) || dockerfileName.StartsWith("../", StringComparison.Ordinal))
        {
            dockerfileName = "Dockerfile.meziantou-tc-" + Guid.NewGuid().ToString("N");
            additionalFile = (dockerfile.DockerfilePath, dockerfileName);
        }

        // The context is archived into the body of the request as it is sent, rather than into a temporary file first.
        using var content = DockerApiTarArchive.CreateContent((stream, ct) => DockerApiTarArchive.WriteDirectoryAsync(stream, contextDirectory, entryPrefix: "", additionalFile, ct));

        // The labels are what the cleanup and the reaper find a built image by.
        var endpoint = "/build?rm=1&dockerfile=" + Uri.EscapeDataString(dockerfileName) + "&t=" + Uri.EscapeDataString(tag) +
            "&labels=" + Uri.EscapeDataString(JsonSerializer.Serialize(labels, DockerApiJsonContext.Default.DictionaryStringString));
        using var response = await SendAsync(HttpMethod.Post, endpoint, content, cancellationToken, completionOption: HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

        // The daemon answers as soon as the build starts, so a failure is only reported in the stream of messages that follows.
        await ReadProgressAsync(response, "Unable to build the image from '" + dockerfile.DockerfilePath + "': ", cancellationToken).ConfigureAwait(false);
        return tag;
    }

    private async Task<string> LoadImageAsync(ArchiveImage archive, CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(archive.ArchivePath);
        using var content = new StreamContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-tar");

        using var response = await SendAsync(HttpMethod.Post, "/images/load", content, cancellationToken, completionOption: HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        var output = await ReadProgressAsync(response, "Unable to load the image archive '" + archive.ArchivePath + "': ", cancellationToken).ConfigureAwait(false);
        return ContainerImageOutputParser.TryParseLoadedImage(output)
            ?? throw CreateException("Unable to determine the image reference from the load output: " + output, statusCode: null);
    }

    /// <summary>Reads the stream of JSON messages the daemon answers a build or a load with, and returns the text they carry. A failure is only ever reported there.</summary>
    private async Task<string> ReadProgressAsync(HttpResponseMessage response, string errorPrefix, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.Length is 0)
                continue;

            var progress = JsonSerializer.Deserialize(line, DockerApiJsonContext.Default.PullProgress);
            var errorMessage = progress?.ErrorDetail?.Message ?? progress?.Error;
            if (!string.IsNullOrEmpty(errorMessage))
                throw CreateException(errorPrefix + errorMessage, statusCode: null);

            output.Append(progress?.Stream);
        }

        return output.ToString();
    }

    private Task<bool> ImageExistsAsync(string imageName, ILogger? logger, CancellationToken cancellationToken)
    {
        return RetryStrategy.ExecuteAsync(
            ct => ImageExistsCoreAsync(imageName, ct),
            TransientError.IsTransient,
            Log.CreateImageLookupRetryCallback(logger, imageName),
            RetryStrategy.DefaultBaseDelay,
            cancellationToken);
    }

    private async Task<bool> ImageExistsCoreAsync(string imageName, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, "/images/" + Uri.EscapeDataString(imageName) + "/json", content: null, cancellationToken, allowedStatusCodes: [HttpStatusCode.NotFound]).ConfigureAwait(false);
        return response.StatusCode != HttpStatusCode.NotFound;
    }

    /// <summary>Pulls the image, running the whole pull again when the registry fails for a reason that a second attempt can resolve.</summary>
    /// <remarks>A pull is idempotent, and the layers that were already fetched are cached by the daemon, so an attempt that follows a failure resumes instead of downloading everything again.</remarks>
    private Task PullImageAsync(string imageName, ILogger? logger, CancellationToken cancellationToken)
    {
        return RetryStrategy.ExecuteAsync(
            ct => PullImageCoreAsync(imageName, ct),
            TransientError.IsTransient,
            Log.CreateImagePullRetryCallback(logger, imageName),
            RetryStrategy.DefaultBaseDelay,
            cancellationToken);
    }

    private async Task PullImageCoreAsync(string imageName, CancellationToken cancellationToken)
    {
        var connection = await EnsureConnectionOrThrowAsync(cancellationToken).ConfigureAwait(false);
        var endpoint = "/images/create?fromImage=" + Uri.EscapeDataString(imageName);
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint(connection, endpoint));
        var registryAuth = await _authProvider.GetRegistryAuthHeaderValueAsync(imageName, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(registryAuth))
            request.Headers.TryAddWithoutValidation("X-Registry-Auth", registryAuth);

        using var response = await connection.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw await CreateRequestExceptionAsync(response, request.Method.Method, request.RequestUri?.AbsolutePath ?? endpoint, cancellationToken).ConfigureAwait(false);

        // The daemon answers a pull with a success as soon as it starts, so a failure is only ever reported in the body.
        // It carries no status code, which is why the classification of a transient failure falls back to the text.
        await ReadProgressAsync(response, "Unable to pull image '" + imageName + "': ", cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string endpoint, HttpContent? content, CancellationToken cancellationToken, HttpStatusCode[]? allowedStatusCodes = null, HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
    {
        var connection = await EnsureConnectionOrThrowAsync(cancellationToken).ConfigureAwait(false);
        using var request = new HttpRequestMessage(method, BuildEndpoint(connection, endpoint))
        {
            Content = content,
        };

        var response = await connection.HttpClient.SendAsync(request, completionOption, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode || allowedStatusCodes is not null && Array.IndexOf(allowedStatusCodes, response.StatusCode) >= 0)
            return response;

        using (response)
        {
            throw await CreateRequestExceptionAsync(response, method.Method, request.RequestUri?.AbsolutePath ?? endpoint, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string BuildEndpoint(DockerApiConnection connection, string endpoint)
    {
        if (!endpoint.StartsWith('/', StringComparison.Ordinal))
            endpoint = "/" + endpoint;

        return "/v" + connection.ApiVersion + endpoint;
    }

    private async Task<Exception> CreateRequestExceptionAsync(HttpResponseMessage response, string method, string endpoint, CancellationToken cancellationToken)
    {
        string? daemonMessage = null;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var error = await JsonSerializer.DeserializeAsync(stream, DockerApiJsonContext.Default.ErrorResponse, cancellationToken).ConfigureAwait(false);
            daemonMessage = error?.Message;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The body only adds detail to the status: a body that cannot be read must not replace the failure it describes.
        }

        var message = "Docker API request " + method + " " + endpoint + " failed with status " + (int)response.StatusCode + " (" + response.StatusCode.ToString() + ")";
        if (!string.IsNullOrEmpty(daemonMessage))
            message += ": " + daemonMessage;

        return CreateException(message, response.StatusCode);
    }

    private ContainerRuntimeException CreateException(string message, HttpStatusCode? statusCode) => new(message, this, statusCode);

    internal static async IAsyncEnumerable<LogEntry> ReadMultiplexedLogsAsync(Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new DockerMultiplexedStreamReader(stream);
        using var standardOutput = new DockerStreamTextBuffer();
        using var standardError = new DockerStreamTextBuffer();
        while (await reader.ReadFrameAsync(cancellationToken).ConfigureAwait(false))
        {
            var logStream = reader.CurrentStream;
            var buffer = logStream is LogStream.Stderr ? standardError : standardOutput;
            buffer.Append(reader.CurrentPayload, flush: false);
            while (TryReadLogLine(buffer, logStream, out var entry))
            {
                if (entry is not null)
                    yield return entry;
            }
        }

        // The stream ended. Whatever is left was never terminated by a newline, but it is still a log line: a process
        // that writes its readiness marker with 'printf' and no '\n', or that dies mid-line, would otherwise never be
        // reported and every wait strategy watching for it would time out. A stream that ends in the middle of a
        // character still reports what it received.
        foreach (var (logStream, buffer) in new[] { (LogStream.Stdout, standardOutput), (LogStream.Stderr, standardError) })
        {
            buffer.Append([], flush: true);
            while (TryReadLogLine(buffer, logStream, out var entry))
            {
                if (entry is not null)
                    yield return entry;
            }

            if (TryReadTrailingLogLine(buffer, logStream) is { } trailing)
                yield return trailing;
        }
    }

    internal static async Task<(string StandardOutput, string StandardError)> ReadMultiplexedTextAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new DockerMultiplexedStreamReader(stream);
        using var standardOutput = new DockerStreamTextBuffer();
        using var standardError = new DockerStreamTextBuffer();
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        while (await reader.ReadFrameAsync(cancellationToken).ConfigureAwait(false))
        {
            var (buffer, builder) = reader.CurrentStream is LogStream.Stderr ? (standardError, stderr) : (standardOutput, stdout);
            AppendText(buffer, builder, reader.CurrentPayload, flush: false);
        }

        AppendText(standardOutput, stdout, [], flush: true);
        AppendText(standardError, stderr, [], flush: true);
        return (stdout.ToString(), stderr.ToString());

        static void AppendText(DockerStreamTextBuffer buffer, StringBuilder builder, ReadOnlySpan<byte> bytes, bool flush)
        {
            buffer.Append(bytes, flush);
            builder.Append(buffer.Pending);
            buffer.Clear();
        }
    }

    /// <summary>Reads the next complete line of a stream. The entry is <see langword="null"/> for an empty line.</summary>
    private static bool TryReadLogLine(DockerStreamTextBuffer buffer, LogStream stream, out LogEntry? entry)
    {
        if (!buffer.TryReadLine(out var line))
        {
            entry = null;
            return false;
        }

        entry = ParseLogLine(stream, line);
        return true;
    }

    /// <summary>Reads what is left of a stream that ended without a newline. Trimming the carriage returns mirrors what <see cref="DockerStreamTextBuffer.TryReadLine"/> does for every other line, which matters when the stream stops between the CR and the LF of a CRLF ending.</summary>
    private static LogEntry? TryReadTrailingLogLine(DockerStreamTextBuffer buffer, LogStream stream)
    {
        var line = buffer.Pending.TrimEnd('\r');
        var entry = buffer.Pending.IsEmpty ? null : ParseLogLine(stream, line);
        buffer.Clear();
        return entry;
    }

    private static LogEntry? ParseLogLine(LogStream stream, ReadOnlySpan<char> line)
    {
        var spaceIndex = line.IndexOf(' ');
        if (spaceIndex > 0 &&
            DateTimeOffset.TryParse(line[..spaceIndex], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
        {
            return new LogEntry(stream, line[(spaceIndex + 1)..].ToString(), timestamp);
        }

        if (line.IsEmpty)
            return null;

        return new LogEntry(stream, line.ToString(), Timestamp: null);
    }

    private static StringContent CreateJsonContent<T>(T payload, JsonTypeInfo<T> typeInfo)
    {
        var json = JsonSerializer.Serialize(payload, typeInfo);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private async Task<DockerApiConnection> EnsureConnectionOrThrowAsync(CancellationToken cancellationToken)
    {
        return await EnsureConnectionAsync(cancellationToken).ConfigureAwait(false) ?? throw CreateUnavailableRuntimeException(this);
    }

    private async Task<DockerApiConnection?> EnsureConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { } connection)
            return connection;

        Task<DockerApiConnection?> probe;
        lock (_connectionLock)
        {
            if (_connection is { } publishedConnection)
                return publishedConnection;

            // Concurrent callers share the probe in flight, so a burst of tests starting at once opens one connection
            // instead of one each. Only a success is kept: a probe that completed without publishing a connection
            // failed, and a daemon started after it is still detected by the next caller.
            if (_connectionProbe is null || _connectionProbe.IsCompleted)
                _connectionProbe = ProbeConnectionAsync();

            probe = _connectionProbe;
        }

        return await probe.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The certificates are disposed in the finally block, unless the published connection owns them.")]
    private async Task<DockerApiConnection?> ProbeConnectionAsync()
    {
        var discovery = DockerEndpointDiscovery.CreateForCurrentProcess().Discover();
        var failures = new List<string>(discovery.Diagnostics);
        foreach (var endpoint in discovery.Endpoints)
        {
            DockerApiTransport.TlsCredentials? tls = null;
            try
            {
                if (endpoint.Tls is { } tlsSettings)
                    tls = DockerApiTransport.TlsCredentials.Load(tlsSettings);

                var (connection, failure) = await ProbeEndpointAsync(endpoint, tls).ConfigureAwait(false);
                if (connection is not null)
                {
                    // The connection lives as long as the runtime, and keeps the certificates its client uses.
                    tls = null;
                    _unavailableReason = null;
                    _connection = connection;
                    return connection;
                }

                failures.Add($"{endpoint}: {failure}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
            {
                failures.Add($"{endpoint}: cannot load the TLS certificates: {ex.Message}");
            }
            finally
            {
                tls?.Dispose();
            }
        }

        _unavailableReason = failures.Count > 0 ? string.Join(" ", failures) : "No Docker endpoint was found.";
        return null;
    }

    private static async Task<(DockerApiConnection? Connection, string? Failure)> ProbeEndpointAsync(DockerApiTransport.Endpoint endpoint, DockerApiTransport.TlsCredentials? tls)
    {
        HttpClient? candidateClient = null;
        try
        {
            candidateClient = DockerApiTransport.CreateClient(endpoint, tls);
            using var cts = new CancellationTokenSource(ProbeTimeout);
            using var versionResponse = await candidateClient.GetAsync("/version", cts.Token).ConfigureAwait(false);
            if (!versionResponse.IsSuccessStatusCode)
                return (null, string.Create(CultureInfo.InvariantCulture, $"GET /version returned {(int)versionResponse.StatusCode}."));

            await using var stream = await versionResponse.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            var version = await JsonSerializer.DeserializeAsync(stream, DockerApiJsonContext.Default.Version, cts.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(version?.ApiVersion))
                return (null, "GET /version did not return an API version.");

            // The client and the API version are published together, so a caller that sees the connection sees both.
            var connection = new DockerApiConnection(candidateClient, tls, version.ApiVersion, endpoint, string.Equals(version.Os, "windows", StringComparison.OrdinalIgnoreCase));
            candidateClient = null;
            return (connection, null);
        }
        catch (OperationCanceledException)
        {
            return (null, "the daemon did not answer within " + ProbeTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture) + " seconds.");
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or IOException or JsonException)
        {
            return (null, GetInnermostMessage(ex));
        }
        finally
        {
            candidateClient?.Dispose();
        }
    }

    private static string GetInnermostMessage(Exception exception)
    {
        while (exception.InnerException is { } inner)
            exception = inner;

        return exception.Message;
    }

    /// <summary>Why the last probe did not find a daemon, listing every endpoint it tried; <see langword="null"/> once a daemon answered.</summary>
    internal override string? UnavailableReason => _unavailableReason;

    /// <summary>The daemon the runtime talks to, connecting to it first if needed.</summary>
    internal async Task<DockerApiTransport.Endpoint> GetEndpointAsync(CancellationToken cancellationToken)
    {
        var connection = await EnsureConnectionOrThrowAsync(cancellationToken).ConfigureAwait(false);
        return connection.Endpoint;
    }

    /// <summary>The endpoint the runtime talks to, published as a single value so that a caller never sees a client without its API version.</summary>
    /// <param name="IsWindowsDaemon">Whether the daemon runs Windows containers, which cannot publish a port on a specific host address.</param>
    /// <param name="Tls">The certificates of the TLS connections to the daemon, shared by <paramref name="HttpClient"/> and the connections of the standard input of an exec.</param>
    private sealed record DockerApiConnection(HttpClient HttpClient, DockerApiTransport.TlsCredentials? Tls, string ApiVersion, DockerApiTransport.Endpoint Endpoint, bool IsWindowsDaemon);
}
