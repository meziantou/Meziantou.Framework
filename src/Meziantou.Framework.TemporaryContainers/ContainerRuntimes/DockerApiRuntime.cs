using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Meziantou.Framework;

namespace Meziantou.Framework.TemporaryContainers.Internals;

internal sealed class DockerApiRuntime : ContainerRuntime
{
    private readonly HttpClient _httpClient;
    private readonly string _apiVersion;
    private readonly DockerContainerRuntime? _cliFallback;
    private readonly DockerRegistryAuthProvider _authProvider;

    private DockerApiRuntime(HttpClient httpClient, string apiVersion, DockerContainerRuntime? cliFallback)
        : base("DockerApi")
    {
        _httpClient = httpClient;
        _apiVersion = apiVersion;
        _cliFallback = cliFallback;
        _authProvider = new DockerRegistryAuthProvider();
    }

    public static bool TryCreate(out ContainerRuntime runtime)
    {
        foreach (var endpoint in DockerApiTransport.GetEndpoints())
        {
            HttpClient? client = null;
            try
            {
                client = DockerApiTransport.CreateClient(endpoint);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var versionResponse = client.GetAsync("/version", cts.Token).ConfigureAwait(false).GetAwaiter().GetResult();
                if (!versionResponse.IsSuccessStatusCode)
                    continue;

                using var stream = versionResponse.Content.ReadAsStream();
                var version = JsonSerializer.Deserialize(stream, DockerApiJsonContext.Default.Version);
                if (string.IsNullOrWhiteSpace(version?.ApiVersion))
                    continue;

                runtime = new DockerApiRuntime(client, version.ApiVersion, GetDockerCliFallback());
                client = null;
                return true;
            }
            catch (OperationCanceledException)
            {
                continue;
            }
            catch (HttpRequestException)
            {
                continue;
            }
            catch (SocketException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }
            finally
            {
                client?.Dispose();
            }
        }

        runtime = null!;
        return false;
    }

    internal static bool TryProbe()
    {
        foreach (var endpoint in DockerApiTransport.GetEndpoints())
        {
            HttpClient? client = null;
            try
            {
                client = DockerApiTransport.CreateClient(endpoint);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var response = client.GetAsync("/version", cts.Token).ConfigureAwait(false).GetAwaiter().GetResult();
                if (response.IsSuccessStatusCode)
                    return true;
            }
            catch (OperationCanceledException) { }
            catch (HttpRequestException) { }
            catch (SocketException) { }
            catch (IOException) { }
            finally
            {
                client?.Dispose();
            }
        }

        return false;
    }

    internal override bool IsSupportedCore() => TryProbe();

    internal override ContainerRuntime? TryResolve()
        => TryCreate(out var runtime) ? runtime : null;

    internal override bool SupportsPause => true;

    internal override bool SupportsRestart => true;

    internal override async Task<string> EnsureCreatedAsync(ContainerDefinition definition, CancellationToken cancellationToken)
    {
        if (definition.ReuseId is { } reuseId && await FindReusableContainerAsync(reuseId, cancellationToken).ConfigureAwait(false) is { } reusedContainerId)
            return reusedContainerId;

        var imageRef = await PrepareImageAsync(definition.Image, definition.PullPolicy, cancellationToken).ConfigureAwait(false);
        var payload = DockerApiCreateRequestBuilder.Build(definition, imageRef);
        using var content = CreateJsonContent(payload, DockerApiJsonContext.Default.CreateContainerRequest);
        var endpoint = "/containers/create";
        if (!string.IsNullOrEmpty(definition.Name))
            endpoint += "?name=" + Uri.EscapeDataString(definition.Name);

        using var response = await SendAsync(HttpMethod.Post, endpoint, content, cancellationToken).ConfigureAwait(false);
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var createResponse = JsonSerializer.Deserialize(stream, DockerApiJsonContext.Default.CreateContainerResponse);
        if (string.IsNullOrWhiteSpace(createResponse?.Id))
            throw new InvalidOperationException("Unable to create the container: the Docker API response does not contain an id.");

        return createResponse.Id;
    }

    internal override async Task StartAsync(string id, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, "/containers/" + Uri.EscapeDataString(id) + "/start", content: null, cancellationToken).ConfigureAwait(false);
    }

    internal override async Task StopAsync(string id, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, "/containers/" + Uri.EscapeDataString(id) + "/stop", content: null, cancellationToken).ConfigureAwait(false);
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
        using var response = await SendAsync(HttpMethod.Delete, "/containers/" + Uri.EscapeDataString(id) + "?force=1", content: null, cancellationToken, allowNotFound: true).ConfigureAwait(false);
    }

    internal override async Task<bool> ExistsAsync(string id, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, "/containers/" + Uri.EscapeDataString(id) + "/json", content: null, cancellationToken, allowNotFound: true).ConfigureAwait(false);
        return response.StatusCode != HttpStatusCode.NotFound;
    }

    internal override async Task<ContainerInfo> InspectAsync(string id, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, "/containers/" + Uri.EscapeDataString(id) + "/json", content: null, cancellationToken).ConfigureAwait(false);
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize(stream, DockerApiJsonContext.Default.DockerInspectResult);
        if (result is null)
            throw new InvalidOperationException("Unable to inspect the container: the Docker API response is empty.");

        return DockerContainerInfoParser.ParseInspectResult(result);
    }

    internal override async IAsyncEnumerable<LogEntry> GetLogsAsync(string id, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var endpoint = "/containers/" + Uri.EscapeDataString(id) + "/logs?stdout=1&stderr=1&follow=1&timestamps=1";
        using var response = await SendAsync(HttpMethod.Get, endpoint, content: null, cancellationToken, completionOption: HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var entry in ReadMultiplexedLogsAsync(stream, cancellationToken).ConfigureAwait(false))
            yield return entry;
    }

    internal override async Task<ExecResult> ExecAsync(string id, ExecOptions options, CancellationToken cancellationToken)
    {
        if (options.StandardInput is not null)
        {
            if (_cliFallback is null)
                throw new NotSupportedException("The Docker API runtime does not support stdin for exec operations without docker CLI fallback.");

            return await _cliFallback.ExecAsync(id, options, cancellationToken).ConfigureAwait(false);
        }

        var createExecRequest = new DockerApiModels.ExecCreateRequest
        {
            AttachStdout = true,
            AttachStderr = true,
            AttachStdin = false,
            Tty = false,
            Cmd = [.. options.Command],
            Env = [.. options.Environment.Select(static pair => pair.Key + "=" + pair.Value)],
            User = options.User,
            WorkingDir = options.WorkingDirectory,
        };

        using var createExecContent = CreateJsonContent(createExecRequest, DockerApiJsonContext.Default.ExecCreateRequest);
        using var createExecResponse = await SendAsync(HttpMethod.Post, "/containers/" + Uri.EscapeDataString(id) + "/exec", createExecContent, cancellationToken).ConfigureAwait(false);
        using var createExecStream = await createExecResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var createExecResult = JsonSerializer.Deserialize(createExecStream, DockerApiJsonContext.Default.ExecCreateResponse);
        var execId = createExecResult?.Id ?? throw new InvalidOperationException("Unable to create exec command: missing exec id.");

        var startExecRequest = new DockerApiModels.ExecStartRequest
        {
            Detach = false,
            Tty = false,
        };

        using var startExecContent = CreateJsonContent(startExecRequest, DockerApiJsonContext.Default.ExecStartRequest);
        using var startExecResponse = await SendAsync(HttpMethod.Post, "/exec/" + Uri.EscapeDataString(execId) + "/start", startExecContent, cancellationToken, completionOption: HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        await using var startExecStream = await startExecResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var (standardOutput, standardError) = await ReadMultiplexedTextAsync(startExecStream, cancellationToken).ConfigureAwait(false);

        using var inspectExecResponse = await SendAsync(HttpMethod.Get, "/exec/" + Uri.EscapeDataString(execId) + "/json", content: null, cancellationToken).ConfigureAwait(false);
        using var inspectExecStream = await inspectExecResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var inspectExecResult = JsonSerializer.Deserialize(inspectExecStream, DockerApiJsonContext.Default.ExecInspectResponse)
            ?? throw new InvalidOperationException("Unable to inspect exec command result.");

        return new ExecResult(inspectExecResult.ExitCode, standardOutput, standardError);
    }

    internal override Task<Stream> OpenReadAsync(string id, string path, CancellationToken cancellationToken)
    {
        return ExecuteUsingFallbackAsync(runtime => runtime.OpenReadAsync(id, path, cancellationToken));
    }

    internal override async Task WriteFileAsync(string id, string path, Stream content, CancellationToken cancellationToken)
    {
        await ExecuteUsingFallbackAsync(async runtime =>
        {
            await runtime.WriteFileAsync(id, path, content, cancellationToken).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
    }

    internal override async Task CopyToContainerAsync(string id, string source, string destination, CancellationToken cancellationToken)
    {
        await ExecuteUsingFallbackAsync(async runtime =>
        {
            await runtime.CopyToContainerAsync(id, source, destination, cancellationToken).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
    }

    internal override async Task CopyFromContainerAsync(string id, string source, string destination, CancellationToken cancellationToken)
    {
        await ExecuteUsingFallbackAsync(async runtime =>
        {
            await runtime.CopyFromContainerAsync(id, source, destination, cancellationToken).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
    }

    internal override IReadOnlyDictionary<int, int> ResolvePortMap(ContainerInfo info, ContainerDefinition definition)
    {
        return info.Ports;
    }

    private static DockerContainerRuntime? GetDockerCliFallback()
    {
        return (DockerContainerRuntime?)ContainerRuntime.Docker.TryResolve();
    }

    private async Task<string?> FindReusableContainerAsync(string reuseId, CancellationToken cancellationToken)
    {
        var labelFilter = JsonEncodedText.Encode(DockerCreateArgumentBuilder.ReuseLabel + "=" + reuseId).ToString();
        var filters = "{\"label\":[\"" + labelFilter + "\"]}";

        var endpoint = "/containers/json?all=1&filters=" + Uri.EscapeDataString(filters);
        using var response = await SendAsync(HttpMethod.Get, endpoint, content: null, cancellationToken).ConfigureAwait(false);
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var containers = JsonSerializer.Deserialize(stream, DockerApiJsonContext.Default.ContainerSummaryArray);
        if (containers is null)
            return null;

        foreach (var container in containers)
        {
            if (!string.IsNullOrWhiteSpace(container.Id))
                return container.Id;
        }

        return null;
    }

    private async Task<string> PrepareImageAsync(ImageSource source, PullPolicy pullPolicy, CancellationToken cancellationToken)
    {
        switch (source)
        {
            case RegistryImage registry:
                if (pullPolicy is PullPolicy.Always || pullPolicy is PullPolicy.IfMissing && !await ImageExistsAsync(registry.Name, cancellationToken).ConfigureAwait(false))
                    await PullImageAsync(registry.Name, cancellationToken).ConfigureAwait(false);

                return registry.Name;

            case ExistingImage existing:
                return existing.ImageId;

            case DockerfileImage dockerfile:
                if (_cliFallback is null)
                    throw new NotSupportedException("The Docker API runtime does not support Dockerfile image builds without docker CLI fallback.");

                return await _cliFallback.PrepareImageAsync(dockerfile, pullPolicy, cancellationToken).ConfigureAwait(false);

            case ArchiveImage archive:
                if (_cliFallback is null)
                    throw new NotSupportedException("The Docker API runtime does not support image archive loading without docker CLI fallback.");

                return await _cliFallback.PrepareImageAsync(archive, pullPolicy, cancellationToken).ConfigureAwait(false);

            default:
                throw new NotSupportedException($"Image source '{source.GetType()}' is not supported.");
        }
    }

    private async Task<bool> ImageExistsAsync(string imageName, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, "/images/" + Uri.EscapeDataString(imageName) + "/json", content: null, cancellationToken, allowNotFound: true).ConfigureAwait(false);
        return response.StatusCode != HttpStatusCode.NotFound;
    }

    private async Task PullImageAsync(string imageName, CancellationToken cancellationToken)
    {
        var endpoint = "/images/create?fromImage=" + Uri.EscapeDataString(imageName);
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint(endpoint));
        var registryAuth = await _authProvider.GetRegistryAuthHeaderValueAsync(imageName, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(registryAuth))
            request.Headers.TryAddWithoutValidation("X-Registry-Auth", registryAuth);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw await CreateRequestExceptionAsync(response, request.Method.Method, request.RequestUri?.AbsolutePath ?? endpoint, cancellationToken).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { Length: > 0 } line)
        {
            var progress = JsonSerializer.Deserialize(line, DockerApiJsonContext.Default.PullProgress);
            var errorMessage = progress?.ErrorDetail?.Message ?? progress?.Error;
            if (!string.IsNullOrEmpty(errorMessage))
                throw new InvalidOperationException("Unable to pull image '" + imageName + "': " + errorMessage);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string endpoint, HttpContent? content, CancellationToken cancellationToken, bool allowNotFound = false, HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
    {
        using var request = new HttpRequestMessage(method, BuildEndpoint(endpoint))
        {
            Content = content,
        };

        var response = await _httpClient.SendAsync(request, completionOption, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode || allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            return response;

        throw await CreateRequestExceptionAsync(response, method.Method, request.RequestUri?.AbsolutePath ?? endpoint, cancellationToken).ConfigureAwait(false);
    }

    private string BuildEndpoint(string endpoint)
    {
        if (!endpoint.StartsWith('/', StringComparison.Ordinal))
            endpoint = "/" + endpoint;

        return "/v" + _apiVersion + endpoint;
    }

    private static async Task<Exception> CreateRequestExceptionAsync(HttpResponseMessage response, string method, string endpoint, CancellationToken cancellationToken)
    {
        string? daemonMessage = null;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var error = JsonSerializer.Deserialize(stream, DockerApiJsonContext.Default.ErrorResponse);
            daemonMessage = error?.Message;
        }
        catch (JsonException)
        {
        }

        var message = "Docker API request " + method + " " + endpoint + " failed with status " + (int)response.StatusCode + " (" + response.StatusCode.ToString() + ")";
        if (!string.IsNullOrEmpty(daemonMessage))
            message += ": " + daemonMessage;

        return new InvalidOperationException(message);
    }

    private static async IAsyncEnumerable<LogEntry> ReadMultiplexedLogsAsync(Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffers = new Dictionary<LogStream, StringBuilder>
        {
            [LogStream.Stdout] = new StringBuilder(),
            [LogStream.Stderr] = new StringBuilder(),
        };

        await foreach (var (logStream, text) in ReadMultiplexedFramesAsync(stream, cancellationToken).ConfigureAwait(false))
        {
            var buffer = buffers[logStream];
            buffer.Append(text);

            while (TryReadLine(buffer, out var line))
            {
                var parsed = ParseLogLine(logStream, line);
                if (parsed is not null)
                    yield return parsed;
            }
        }
    }

    private static async Task<(string StandardOutput, string StandardError)> ReadMultiplexedTextAsync(Stream stream, CancellationToken cancellationToken)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        await foreach (var (logStream, text) in ReadMultiplexedFramesAsync(stream, cancellationToken).ConfigureAwait(false))
        {
            if (logStream is LogStream.Stdout)
                stdout.Append(text);
            else
                stderr.Append(text);
        }

        return (stdout.ToString(), stderr.ToString());
    }

    private static async IAsyncEnumerable<(LogStream Stream, string Text)> ReadMultiplexedFramesAsync(Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var header = new byte[8];
        while (await FillBufferAsync(stream, header, cancellationToken).ConfigureAwait(false))
        {
            var streamKind = header[0] switch
            {
                2 => LogStream.Stderr,
                _ => LogStream.Stdout,
            };

            var payloadLength = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4));
            if (payloadLength <= 0)
                continue;

            var rented = ArrayPool<byte>.Shared.Rent(payloadLength);
            try
            {
                await ReadExactlyAsync(stream, rented.AsMemory(0, payloadLength), cancellationToken).ConfigureAwait(false);
                yield return (streamKind, Encoding.UTF8.GetString(rented, 0, payloadLength));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    private static async ValueTask<bool> FillBufferAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return offset != 0 ? throw new EndOfStreamException("Unexpected end of stream while reading Docker API logs.") : false;

            offset += read;
        }

        return true;
    }

    private static async ValueTask ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("Unexpected end of stream while reading Docker API payload.");

            offset += read;
        }
    }

    private static bool TryReadLine(StringBuilder builder, out string line)
    {
        for (var i = 0; i < builder.Length; i++)
        {
            if (builder[i] is '\n')
            {
                line = builder.ToString(0, i).TrimEnd('\r');
                builder.Remove(0, i + 1);
                return true;
            }
        }

        line = string.Empty;
        return false;
    }

    private static LogEntry? ParseLogLine(LogStream stream, string line)
    {
        var spaceIndex = line.IndexOf(' ', StringComparison.Ordinal);
        if (spaceIndex > 0 &&
            DateTimeOffset.TryParse(line[..spaceIndex], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
        {
            return new LogEntry(stream, line[(spaceIndex + 1)..], timestamp);
        }

        if (line.Length == 0)
            return null;

        return new LogEntry(stream, line, Timestamp: null);
    }

    private static StringContent CreateJsonContent<T>(T payload, JsonTypeInfo<T> typeInfo)
    {
        var json = JsonSerializer.Serialize(payload, typeInfo);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private async Task<T> ExecuteUsingFallbackAsync<T>(Func<DockerContainerRuntime, Task<T>> callback)
    {
        if (_cliFallback is null)
            throw new NotSupportedException("The Docker API runtime does not support this operation without docker CLI fallback.");

        return await callback(_cliFallback).ConfigureAwait(false);
    }

}
