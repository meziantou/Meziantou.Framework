using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Meziantou.Framework.WasmEngine.Components;
using static Meziantou.Framework.WasmEngine.Wasi.Components.WasiComponentValues;

namespace Meziantou.Framework.WasmEngine.Wasi.Components;

/// <summary>
/// The interfaces <c>wasi:http/types</c> and <c>wasi:http/client</c> of WASI 0.3, over a <see cref="WasiHttpClient"/>,
/// and the calls of the export <c>wasi:http/handler</c> of a component (<see cref="HandleAsync"/>).
/// </summary>
internal sealed class WasiHttp
{
    private const int BufferSize = 64 * 1024;

    private static readonly HashSet<string> ContentHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "allow", "content-disposition", "content-encoding", "content-language", "content-length", "content-location", "content-md5", "content-range", "content-type", "expires", "last-modified",
    };

    private readonly WasiHttpClient? _client;
    private readonly ComponentResourceType _fields = new("fields");
    private readonly ComponentResourceType _request = new("request", resource => DisposeUnconsumed(((Request)resource.Representation!).Body));
    private readonly ComponentResourceType _response = new("response", resource => DisposeUnconsumed(((Response)resource.Representation!).Body));
    private readonly ComponentResourceType _requestOptions = new("request-options");

    public WasiHttp(WasiHttpClient? client)
    {
        _client = client;
    }

    /// <summary>
    /// Releases a body that the component dropped without consuming it: the content of the host (such as the connection of
    /// a response), or the stream and the future of a component, whose writer sees that the reader dropped them.
    /// </summary>
    private static void DisposeUnconsumed(Body body)
    {
        if (!body.IsConsumed)
        {
            body.IsConsumed = true;
            body.HostContent?.Dispose();
            body.Contents?.AsStreamReader().Dispose();
            body.Trailers?.AsFutureReader().Dispose();
        }
    }

    /// <summary>Converts a duration in nanoseconds to a timeout of a <see cref="CancellationTokenSource"/> (at most about 24 days).</summary>
    private static TimeSpan ToTimeout(ulong nanoseconds) => TimeSpan.FromTicks((long)Math.Min(nanoseconds / 100, (ulong)int.MaxValue * 10_000));

    // ------------------------------------------------------------------------------------------------------------
    // The state of the resources
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>The fields of a request or a response: the names are in lower case (as HTTP/2 sends them), the values are bytes.</summary>
    private sealed class Fields(List<(string Name, byte[] Value)> entries, bool isImmutable)
    {
        public List<(string Name, byte[] Value)> Entries { get; } = entries;

        public bool IsImmutable { get; } = isImmutable;

        public Fields Clone(bool isImmutable) => new([.. Entries], isImmutable);
    }

    private sealed class RequestOptions(bool isImmutable)
    {
        public bool IsImmutable { get; } = isImmutable;

        public ulong? ConnectTimeout { get; set; }

        public ulong? FirstByteTimeout { get; set; }

        public ulong? BetweenBytesTimeout { get; set; }

        public RequestOptions Clone(bool isImmutable) => new(isImmutable) { ConnectTimeout = ConnectTimeout, FirstByteTimeout = FirstByteTimeout, BetweenBytesTimeout = BetweenBytesTimeout };
    }

    /// <summary>
    /// A body: the stream and the future of trailers of a component, or a content of the host whose stream and future are
    /// created when the component consumes the body.
    /// </summary>
    private sealed class Body
    {
        public ComponentValue? Contents { get; init; }

        public ComponentValue? Trailers { get; init; }

        public HttpContent? HostContent { get; init; }

        public Func<HttpHeaders?>? HostTrailers { get; init; }

        public bool IsConsumed { get; set; }

        /// <summary>Reports the result of the transmission to the producer of a body of a component (the future returned by <c>new</c>).</summary>
        public Action<ComponentValue>? Transmit { get; set; }

        /// <summary>The time without data after which the reading of a body of the host fails (<c>between-bytes-timeout</c>).</summary>
        public TimeSpan? BetweenBytesTimeout { get; init; }
    }

    private sealed class Request
    {
        public string Method { get; set; } = "GET";

        public string? PathWithQuery { get; set; }

        public string? Scheme { get; set; }

        public string? Authority { get; set; }

        public required Fields Headers { get; init; }

        public required Body Body { get; init; }

        public RequestOptions? Options { get; init; }

        public ComponentFutureWriter? Transmission { get; init; }

        public bool IsTransmitted { get; set; }

        /// <summary>Writes the result of the transmission of the request, once.</summary>
        public void WriteTransmission(ComponentValue status)
        {
            if (!IsTransmitted)
            {
                IsTransmitted = true;
                Transmission?.Write(status);
            }
        }
    }

    private sealed class Response
    {
        public int StatusCode { get; set; } = 200;

        public required Fields Headers { get; init; }

        public required Body Body { get; init; }

        public ComponentFutureWriter? Transmission { get; init; }
    }

    // ------------------------------------------------------------------------------------------------------------
    // Values
    // ------------------------------------------------------------------------------------------------------------

    private static ComponentFunction Sync(Func<ComponentCallContext, ComponentValue[], ComponentValue?> body) => Function((context, arguments) => body(context, arguments.ToArray()));

    private static ComponentValue HeaderError(string code) => ComponentValue.Error(ComponentValue.FromCase(code));

    private static ComponentValue Method(string method) => method switch
    {
        "GET" => ComponentValue.FromCase("get"),
        "HEAD" => ComponentValue.FromCase("head"),
        "POST" => ComponentValue.FromCase("post"),
        "PUT" => ComponentValue.FromCase("put"),
        "DELETE" => ComponentValue.FromCase("delete"),
        "CONNECT" => ComponentValue.FromCase("connect"),
        "OPTIONS" => ComponentValue.FromCase("options"),
        "TRACE" => ComponentValue.FromCase("trace"),
        "PATCH" => ComponentValue.FromCase("patch"),
        _ => ComponentValue.FromCase("other", String(method)),
    };

    private static string? ToMethod(ComponentValue value) => value.CaseName switch
    {
        "other" => value.Payload!.Value.AsString() is var other && WasiHttpSyntax.IsToken(other) ? other : null,
        var name => name!.ToUpperInvariant(),
    };

    private static ComponentValue Scheme(string scheme) => scheme.ToUpperInvariant() switch
    {
        "HTTP" => ComponentValue.FromCase("HTTP"),
        "HTTPS" => ComponentValue.FromCase("HTTPS"),
        _ => ComponentValue.FromCase("other", String(scheme)),
    };

    private static string? ToScheme(ComponentValue value) => value.CaseName switch
    {
        "other" => value.Payload!.Value.AsString() is var other && WasiHttpSyntax.IsValidScheme(other) ? other : null,
        var name => name,
    };

    private static ComponentValue? OptionalString(string? value) => value is null ? ComponentValue.NoneValue : ComponentValue.Some(String(value));

    private static ComponentValue OptionalDuration(ulong? value) => value is { } duration ? ComponentValue.Some(ComponentValue.FromU64(duration)) : ComponentValue.NoneValue;

    private static ulong? ToOptionalDuration(ComponentValue value) => value.IsCase("some") ? value.Payload!.Value.AsU64() : null;

    private static ComponentValue FieldEntries(Fields fields) => ComponentValue.FromList(fields.Entries.Select(entry => ComponentValue.FromTuple(String(entry.Name), ComponentValue.FromBytes(entry.Value))));

    private static IEnumerable<byte[]> GetValues(Fields fields, string name) => fields.Entries.Where(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase)).Select(entry => entry.Value);

    /// <summary>Checks a field: <c>invalid-syntax</c>, or <c>forbidden</c> for the headers of the connection.</summary>
    private static string? CheckField(string name, ReadOnlySpan<byte> value)
    {
        if (!WasiHttpSyntax.IsValidFieldName(name) || !WasiHttpSyntax.IsValidFieldValue(value))
            return "invalid-syntax";

        return WasiHttpSyntax.IsForbiddenHeader(name) ? "forbidden" : null;
    }

    /// <summary>The fields of .NET headers: the values are their bytes (ISO-8859-1, as HTTP/1.1 sends them).</summary>
    private static Fields ToFields(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers, bool isImmutable)
    {
        var entries = new List<(string, byte[])>();
        foreach (var (name, values) in headers)
        {
            foreach (var value in values)
            {
                entries.Add((name.ToLowerInvariant(), Encoding.Latin1.GetBytes(value)));
            }
        }

        return new Fields(entries, isImmutable);
    }

    private static void AddHeaders(Fields fields, HttpHeaders headers, HttpContent? content)
    {
        foreach (var (name, value) in fields.Entries)
        {
            if (WasiHttpSyntax.IsForbiddenHeader(name))
                continue;

            var text = Encoding.Latin1.GetString(value);
            if (ContentHeaders.Contains(name))
            {
                content?.Headers.TryAddWithoutValidation(name, text);
            }
            else
            {
                headers.TryAddWithoutValidation(name, text);
            }
        }
    }

    private static ComponentValue ErrorCode(Exception exception)
    {
        var socketError = (exception as SocketException ?? exception.InnerException as SocketException ?? exception.InnerException?.InnerException as SocketException)?.SocketErrorCode;
        return exception switch
        {
            UnauthorizedAccessException => ComponentValue.FromCase("HTTP-request-denied"),
            TimeoutException or TaskCanceledException => ComponentValue.FromCase("connection-timeout"),
            _ when socketError is SocketError.ConnectionRefused => ComponentValue.FromCase("connection-refused"),
            _ when socketError is SocketError.AccessDenied => ComponentValue.FromCase("destination-IP-prohibited"),
            _ when socketError is SocketError.HostUnreachable or SocketError.NetworkUnreachable => ComponentValue.FromCase("destination-IP-unroutable"),
            _ when socketError is SocketError.TimedOut => ComponentValue.FromCase("connection-timeout"),
            _ when socketError is SocketError.ConnectionReset or SocketError.ConnectionAborted => ComponentValue.FromCase("connection-terminated"),
            HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError } => ComponentValue.FromCase("DNS-error", ComponentValue.FromRecord(ComponentValue.Some(String("address not available")), ComponentValue.NoneValue)),
            HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } => ComponentValue.FromCase("TLS-protocol-error"),
            HttpRequestException { HttpRequestError: HttpRequestError.ResponseEnded } or IOException => ComponentValue.FromCase("HTTP-response-incomplete"),
            HttpRequestException { HttpRequestError: HttpRequestError.InvalidResponse } => ComponentValue.FromCase("HTTP-protocol-error"),
            _ => ComponentValue.FromCase("internal-error", ComponentValue.Some(String(exception.Message))),
        };
    }

    private static bool IsError(Exception exception) => exception is HttpRequestException or IOException or SocketException or UnauthorizedAccessException or TimeoutException or OperationCanceledException or InvalidOperationException;

    // ------------------------------------------------------------------------------------------------------------
    // The interfaces
    // ------------------------------------------------------------------------------------------------------------

    public void Define(ComponentImports imports, Func<string, string> name)
    {
        var types = new ComponentImports()
            .Define("fields", _fields)
            .Define("headers", _fields)
            .Define("trailers", _fields)
            .Define("request", _request)
            .Define("response", _response)
            .Define("request-options", _requestOptions);
        DefineFields(types);
        DefineRequest(types);
        DefineRequestOptions(types);
        DefineResponse(types);
        imports.Define(name("wasi:http/types"), types);
        imports.Define(name("wasi:http/client"), new ComponentImports()
            .Define("send", AsyncFunction(async (context, arguments) => await SendAsync(context, Get<Request>(arguments[0])).ConfigureAwait(true))));
    }

    private void DefineFields(ComponentImports types)
    {
        types
            .Define("[constructor]fields", Sync((context, arguments) => Resource(_fields, new Fields([], isImmutable: false))))
            .Define("[static]fields.from-list", Sync((context, arguments) =>
            {
                var entries = new List<(string, byte[])>();
                foreach (var entry in arguments[0].AsList())
                {
                    var parts = entry.AsRecord();
                    var (name, value) = (parts[0].AsString(), parts[1].AsBytes());
                    if (CheckField(name, value) is { } error)
                        return HeaderError(error);

                    entries.Add((name.ToLowerInvariant(), value));
                }

                return ComponentValue.Ok(Resource(_fields, new Fields(entries, isImmutable: false)));
            }))
            .Define("[method]fields.get", Sync((context, arguments) => ComponentValue.FromList(GetValues(Get<Fields>(arguments[0]), arguments[1].AsString()).Select(value => ComponentValue.FromBytes(value)))))
            .Define("[method]fields.has", Sync((context, arguments) => ComponentValue.FromBool(GetValues(Get<Fields>(arguments[0]), arguments[1].AsString()).Any())))
            .Define("[method]fields.set", Sync((context, arguments) =>
            {
                var fields = Get<Fields>(arguments[0]);
                var name = arguments[1].AsString();
                var values = arguments[2].AsList().Select(value => value.AsBytes()).ToArray();
                if (fields.IsImmutable)
                    return HeaderError("immutable");

                if (CheckField(name, []) is { } nameError)
                    return HeaderError(nameError);

                foreach (var value in values)
                {
                    if (CheckField(name, value) is { } error)
                        return HeaderError(error);
                }

                fields.Entries.RemoveAll(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
                fields.Entries.AddRange(values.Select(value => (name.ToLowerInvariant(), value)));
                return ComponentValue.Ok();
            }))
            .Define("[method]fields.delete", Sync((context, arguments) =>
            {
                var fields = Get<Fields>(arguments[0]);
                var name = arguments[1].AsString();
                if (fields.IsImmutable)
                    return HeaderError("immutable");

                if (CheckField(name, []) is { } error)
                    return HeaderError(error);

                fields.Entries.RemoveAll(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
                return ComponentValue.Ok();
            }))
            .Define("[method]fields.get-and-delete", Sync((context, arguments) =>
            {
                var fields = Get<Fields>(arguments[0]);
                var name = arguments[1].AsString();
                if (fields.IsImmutable)
                    return HeaderError("immutable");

                if (CheckField(name, []) is { } error)
                    return HeaderError(error);

                var values = GetValues(fields, name).ToArray();
                fields.Entries.RemoveAll(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
                return ComponentValue.Ok(ComponentValue.FromList(values.Select(value => ComponentValue.FromBytes(value))));
            }))
            .Define("[method]fields.append", Sync((context, arguments) =>
            {
                var fields = Get<Fields>(arguments[0]);
                var name = arguments[1].AsString();
                var value = arguments[2].AsBytes();
                if (fields.IsImmutable)
                    return HeaderError("immutable");

                if (CheckField(name, value) is { } error)
                    return HeaderError(error);

                fields.Entries.Add((name.ToLowerInvariant(), value));
                return ComponentValue.Ok();
            }))
            .Define("[method]fields.copy-all", Sync((context, arguments) => FieldEntries(Get<Fields>(arguments[0]))))
            .Define("[method]fields.clone", Sync((context, arguments) => Resource(_fields, Get<Fields>(arguments[0]).Clone(isImmutable: false))));
    }

    private void DefineRequest(ComponentImports types)
    {
        types
            .Define("[static]request.new", Sync((context, arguments) =>
            {
                var transmission = ComponentFutureWriter.Create(GetTransmissionType(context.Type), out var future);
                var request = new Request
                {
                    Headers = Get<Fields>(arguments[0]).Clone(isImmutable: true),
                    Body = new Body { Contents = arguments[1].IsCase("some") ? arguments[1].Payload : null, Trailers = arguments[2] },
                    Options = arguments[3].IsCase("some") ? Get<RequestOptions>(arguments[3].Payload!.Value).Clone(isImmutable: true) : null,
                    Transmission = transmission,
                };

                request.Body.Transmit = request.WriteTransmission;
                return ComponentValue.FromTuple(Resource(_request, request), future);
            }))
            .Define("[method]request.get-method", Sync((context, arguments) => Method(Get<Request>(arguments[0]).Method)))
            .Define("[method]request.set-method", Sync((context, arguments) =>
            {
                if (ToMethod(arguments[1]) is not { } method)
                    return ComponentValue.Error();

                Get<Request>(arguments[0]).Method = method;
                return ComponentValue.Ok();
            }))
            .Define("[method]request.get-path-with-query", Sync((context, arguments) => OptionalString(Get<Request>(arguments[0]).PathWithQuery)))
            .Define("[method]request.set-path-with-query", Sync((context, arguments) =>
            {
                var value = arguments[1].IsCase("some") ? arguments[1].Payload!.Value.AsString() : null;
                if (value is not null && !WasiHttpSyntax.IsValidPathWithQuery(value))
                    return ComponentValue.Error();

                // An empty path is the root, and the fragment is not part of the path
                if (value is not null)
                {
                    value = WasiHttpSyntax.RemoveFragment(value);
                    if (value.Length == 0)
                    {
                        value = "/";
                    }
                }

                Get<Request>(arguments[0]).PathWithQuery = value;
                return ComponentValue.Ok();
            }))
            .Define("[method]request.get-scheme", Sync((context, arguments) => Get<Request>(arguments[0]).Scheme is { } scheme ? ComponentValue.Some(Scheme(scheme)) : ComponentValue.NoneValue))
            .Define("[method]request.set-scheme", Sync((context, arguments) =>
            {
                string? scheme = null;
                if (arguments[1].IsCase("some") && (scheme = ToScheme(arguments[1].Payload!.Value)) is null)
                    return ComponentValue.Error();

                Get<Request>(arguments[0]).Scheme = scheme;
                return ComponentValue.Ok();
            }))
            .Define("[method]request.get-authority", Sync((context, arguments) => OptionalString(Get<Request>(arguments[0]).Authority)))
            .Define("[method]request.set-authority", Sync((context, arguments) =>
            {
                var value = arguments[1].IsCase("some") ? arguments[1].Payload!.Value.AsString() : null;
                if (value is not null && !WasiHttpSyntax.IsValidAuthority(value))
                    return ComponentValue.Error();

                Get<Request>(arguments[0]).Authority = value;
                return ComponentValue.Ok();
            }))
            .Define("[method]request.get-options", Sync((context, arguments) => Get<Request>(arguments[0]).Options is { } options ? ComponentValue.Some(Resource(_requestOptions, options.Clone(isImmutable: true))) : ComponentValue.NoneValue))
            .Define("[method]request.get-headers", Sync((context, arguments) => Resource(_fields, Get<Request>(arguments[0]).Headers)))
            .Define("[static]request.consume-body", Sync((context, arguments) => ConsumeBody(context, Get<Request>(arguments[0]).Body, arguments[1])));
    }

    private void DefineRequestOptions(ComponentImports types)
    {
        static ComponentValue Set(ComponentValue options, Action<RequestOptions> set)
        {
            var value = Get<RequestOptions>(options);
            if (value.IsImmutable)
                return ComponentValue.Error(ComponentValue.FromCase("immutable"));

            set(value);
            return ComponentValue.Ok();
        }

        types
            .Define("[constructor]request-options", Sync((context, arguments) => Resource(_requestOptions, new RequestOptions(isImmutable: false))))
            .Define("[method]request-options.get-connect-timeout", Sync((context, arguments) => OptionalDuration(Get<RequestOptions>(arguments[0]).ConnectTimeout)))
            .Define("[method]request-options.set-connect-timeout", Sync((context, arguments) => Set(arguments[0], options => options.ConnectTimeout = ToOptionalDuration(arguments[1]))))
            .Define("[method]request-options.get-first-byte-timeout", Sync((context, arguments) => OptionalDuration(Get<RequestOptions>(arguments[0]).FirstByteTimeout)))
            .Define("[method]request-options.set-first-byte-timeout", Sync((context, arguments) => Set(arguments[0], options => options.FirstByteTimeout = ToOptionalDuration(arguments[1]))))
            .Define("[method]request-options.get-between-bytes-timeout", Sync((context, arguments) => OptionalDuration(Get<RequestOptions>(arguments[0]).BetweenBytesTimeout)))
            .Define("[method]request-options.set-between-bytes-timeout", Sync((context, arguments) => Set(arguments[0], options => options.BetweenBytesTimeout = ToOptionalDuration(arguments[1]))))
            .Define("[method]request-options.clone", Sync((context, arguments) => Resource(_requestOptions, Get<RequestOptions>(arguments[0]).Clone(isImmutable: false))));
    }

    private void DefineResponse(ComponentImports types)
    {
        types
            .Define("[static]response.new", Sync((context, arguments) =>
            {
                var transmission = ComponentFutureWriter.Create(GetTransmissionType(context.Type), out var future);
                var response = new Response
                {
                    Headers = Get<Fields>(arguments[0]).Clone(isImmutable: true),
                    Body = new Body { Contents = arguments[1].IsCase("some") ? arguments[1].Payload : null, Trailers = arguments[2] },
                    Transmission = transmission,
                };

                response.Body.Transmit = status => transmission.Write(status);
                return ComponentValue.FromTuple(Resource(_response, response), future);
            }))
            .Define("[method]response.get-status-code", Sync((context, arguments) => ComponentValue.FromU16((ushort)Get<Response>(arguments[0]).StatusCode)))
            .Define("[method]response.set-status-code", Sync((context, arguments) =>
            {
                var statusCode = arguments[1].AsU16();
                if (!WasiHttpSyntax.IsValidStatusCode(statusCode))
                    return ComponentValue.Error();

                Get<Response>(arguments[0]).StatusCode = statusCode;
                return ComponentValue.Ok();
            }))
            .Define("[method]response.get-headers", Sync((context, arguments) => Resource(_fields, Get<Response>(arguments[0]).Headers)))
            .Define("[static]response.consume-body", Sync((context, arguments) => ConsumeBody(context, Get<Response>(arguments[0]).Body, arguments[1])));
    }

    /// <summary>The type of the result of the transmission (<c>future&lt;result&lt;_, error-code&gt;&gt;</c>) of the tuple returned by <c>new</c>.</summary>
    private static ComponentValueType? GetTransmissionType(ComponentFunctionType type) => type.Result is ComponentTupleType { Types: [_, ComponentFutureType future] } ? future.ElementType : null;

    /// <summary>Gives a body to the component: its stream and its future of trailers.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The background copy disposes the writer")]
    private ComponentValue ConsumeBody(ComponentCallContext context, Body body, ComponentValue result)
    {
        // The result of the processing of the body is the result of the transmission for the producer of a body of a
        // component; the host does not wait for it
        var processed = result.AsFutureReader();
        if (body.Transmit is { } transmit && !body.IsConsumed)
        {
            context.RunInBackground(async () =>
            {
                using (processed)
                {
                    transmit(await processed.ReadAsync().ConfigureAwait(true) ?? ComponentValue.Ok());
                }
            });
        }
        else
        {
            processed.Dispose();
        }

        var (streamType, futureType) = context.Type.Result is ComponentTupleType { Types: [ComponentStreamType stream, ComponentFutureType future] } ? (stream.ElementType, future.ElementType) : (null, null);
        if (body.IsConsumed)
            throw new InvalidOperationException("The body is already consumed");

        body.IsConsumed = true;
        if (body.HostContent is null)
        {
            var contents = body.Contents;
            if (contents is null)
            {
                ComponentStreamWriter.Create(streamType, out var empty).Dispose();
                contents = empty;
            }

            if (body.Trailers is { } trailers)
                return ComponentValue.FromTuple(contents.Value, trailers);

            var none = ComponentFutureWriter.Create(futureType, out var noTrailers);
            none.Write(ComponentValue.Ok(ComponentValue.NoneValue));
            return ComponentValue.FromTuple(contents.Value, noTrailers);
        }

        var writer = ComponentStreamWriter.Create(streamType, out var readable);
        var trailersWriter = ComponentFutureWriter.Create(futureType, out var trailersFuture);
        var content = body.HostContent;
        context.RunInBackground(async () =>
        {
            ComponentValue status;
            var timedOut = false;
            try
            {
                var stream = await content.ReadAsStreamAsync(writer.ReaderDroppedToken).ConfigureAwait(true);
                var buffer = new byte[BufferSize];
                while (!writer.IsReaderDropped)
                {
                    int read;
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(writer.ReaderDroppedToken))
                    {
                        if (body.BetweenBytesTimeout is { } betweenBytes)
                        {
                            timeout.CancelAfter(betweenBytes);
                        }

                        try
                        {
                            read = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(true);
                        }
                        catch (OperationCanceledException) when (!writer.IsReaderDropped)
                        {
                            // No data was received in the between-bytes timeout
                            timedOut = true;
                            throw;
                        }
                    }

                    if (read == 0)
                        break;

                    await writer.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(true);
                }

                status = body.HostTrailers?.Invoke() is { } headers && headers.Any()
                    ? ComponentValue.Ok(ComponentValue.Some(Resource(_fields, ToFields(headers, isImmutable: true))))
                    : ComponentValue.Ok(ComponentValue.NoneValue);
            }
            catch (OperationCanceledException) when (writer.IsReaderDropped)
            {
                status = ComponentValue.Ok(ComponentValue.NoneValue);
            }
            catch (Exception ex) when (IsError(ex))
            {
                status = ComponentValue.Error(timedOut ? ComponentValue.FromCase("HTTP-response-timeout") : ErrorCode(ex));
            }
            finally
            {
                writer.Dispose();
                content.Dispose();
            }

            trailersWriter.Write(status);
        });

        return ComponentValue.FromTuple(readable, trailersFuture);
    }

    // ------------------------------------------------------------------------------------------------------------
    // wasi:http/client
    // ------------------------------------------------------------------------------------------------------------

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The response owns the request")]
    private async ValueTask<ComponentValue?> SendAsync(ComponentCallContext context, Request request)
    {
        var bodyStarted = false;
        var scheme = request.Scheme ?? "HTTP";
        if (!string.Equals(scheme, "HTTP", StringComparison.OrdinalIgnoreCase) && !string.Equals(scheme, "HTTPS", StringComparison.OrdinalIgnoreCase))
            return Fail(ComponentValue.FromCase("HTTP-protocol-error"));

        // The userinfo of the authority is not sent: the Host header and the URI are the host and the port. .NET does not
        // send the asterisk form (OPTIONS *)
        var hostPort = request.Authority?[(request.Authority.LastIndexOf('@', StringComparison.Ordinal) + 1)..];
        if (hostPort is null || request.PathWithQuery == "*" || !Uri.TryCreate(scheme.ToLowerInvariant() + "://" + hostPort + (request.PathWithQuery ?? "/"), UriKind.Absolute, out var uri))
            return Fail(ComponentValue.FromCase("HTTP-request-URI-invalid"));

        if (_client is null)
            return Fail(ComponentValue.FromCase("HTTP-request-denied"));

        var message = new HttpRequestMessage(new HttpMethod(request.Method), uri);
        message.Headers.TryAddWithoutValidation("Host", hostPort);
        var pipe = request.Body.Contents is not null ? new Pipe(new PipeOptions(useSynchronizationContext: true)) : null;
        if (pipe is not null)
        {
            message.Content = new StreamContent(pipe.Reader.AsStream());
        }
        else if (request.Body is { HostContent: { } hostContent, IsConsumed: false })
        {
            // A request of the host that the component forwards (a proxy): its content is the body, and its headers are the fields
            request.Body.IsConsumed = true;
            hostContent.Headers.Clear();
            message.Content = hostContent;
        }

        if (request.Options?.ConnectTimeout is { } connectTimeout)
        {
            message.Options.Set(WasiHttpClient.ConnectTimeoutOption, ToTimeout(connectTimeout));
        }

        AddHeaders(request.Headers, message.Headers, message.Content);

        // A body of unknown length is sent in chunks, which can carry trailers
        if (message.Content is { Headers.ContentLength: null })
        {
            message.Headers.TransferEncodingChunked = true;
        }

        var trailers = new TaskCompletionSource<HttpHeaders?>(TaskCreationOptions.RunContinuationsAsynchronously);
        message.Options.Set(WasiHttpClient.RequestTrailers, trailers.Task);
        var transmitted = StartRequestBody(context, request, pipe, trailers);
        bodyStarted = true;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        if (request.Options?.FirstByteTimeout is { } firstByte)
        {
            timeout.CancelAfter(ToTimeout(firstByte));
        }

        HttpResponseMessage response;
        try
        {
            response = await _client.SendAsync(message, timeout.Token).ConfigureAwait(true);
        }
        catch (Exception ex) when (IsError(ex) && !context.CancellationToken.IsCancellationRequested)
        {
            message.Dispose();
            return Fail(ex is OperationCanceledException && request.Options?.FirstByteTimeout is not null ? ComponentValue.FromCase("HTTP-response-timeout") : ErrorCode(ex));
        }
        catch (OperationCanceledException)
        {
            // The component cancelled the request: the body is not sent, and the transmission fails
            message.Dispose();
            request.WriteTransmission(ComponentValue.Error(ComponentValue.FromCase("internal-error", ComponentValue.Some(String("The request was cancelled")))));
            throw;
        }

        // The request is transmitted once the server answered and the body is sent
        context.RunInBackground(async () => request.WriteTransmission(await transmitted.ConfigureAwait(true)));
        var headers = ToFields(response.Headers.Concat(response.Content.Headers), isImmutable: true);
        var result = new Response
        {
            StatusCode = (int)response.StatusCode,
            Headers = headers,
            Body = new Body
            {
                HostContent = response.Content,
                HostTrailers = () => response.TrailingHeaders,
                BetweenBytesTimeout = request.Options?.BetweenBytesTimeout is { } betweenBytes ? ToTimeout(betweenBytes) : null,
            },
        };

        return ComponentValue.Ok(Resource(_response, result));

        ComponentValue Fail(ComponentValue error)
        {
            // A body that is not read yet is dropped: the component that writes it does not wait for the host
            if (!bodyStarted && !request.Body.IsConsumed)
            {
                request.Body.IsConsumed = true;
                request.Body.Contents?.AsStreamReader().Dispose();
                request.Body.Trailers?.AsFutureReader().Dispose();
            }

            request.WriteTransmission(ComponentValue.Error(error));
            return ComponentValue.Error(error);
        }
    }

    /// <summary>Copies the body of a request of the component to the content of the .NET request, and reports the end of the transmission.</summary>
    private static Task<ComponentValue> StartRequestBody(ComponentCallContext context, Request request, Pipe? pipe, TaskCompletionSource<HttpHeaders?> requestTrailers)
    {
        var contents = request.Body.Contents;
        var trailers = request.Body.Trailers;
        request.Body.IsConsumed = true;
        var transmitted = new TaskCompletionSource<ComponentValue>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.RunInBackground(async () =>
        {
            ComponentValue status = ComponentValue.Ok();
            try
            {
                if (contents is { } value && pipe is not null)
                {
                    using var reader = value.AsStreamReader();
                    var buffer = new byte[BufferSize];
                    while (true)
                    {
                        var read = await reader.ReadAsync(buffer).ConfigureAwait(true);
                        if (read == 0 && reader.IsCompleted)
                            break;

                        if (read > 0)
                        {
                            var flush = await pipe.Writer.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(true);
                            if (flush.IsCompleted)
                                break;
                        }
                    }
                }

                // HTTP/1.1 requests of .NET have no trailers: an error of the component aborts the request
                if (trailers is { } future)
                {
                    using var trailersReader = future.AsFutureReader();
                    var trailersResult = await trailersReader.ReadAsync().ConfigureAwait(true);
                    if (trailersResult is { } ok && ok.IsCase("ok") && ok.Payload is { } option && option.IsCase("some"))
                    {
                        var headers = new HttpRequestMessage().Headers;
                        foreach (var (fieldName, fieldValue) in Get<Fields>(option.Payload!.Value).Entries)
                        {
                            headers.TryAddWithoutValidation(fieldName, Encoding.Latin1.GetString(fieldValue));
                        }

                        requestTrailers.TrySetResult(headers);
                    }

                    if (trailersResult is { } error && error.IsCase("error"))
                    {
                        trailersResult = error;
                        status = error;
                        if (pipe is not null)
                        {
                            await pipe.Writer.CompleteAsync(new IOException("The component reported an error in the body of the request")).ConfigureAwait(true);
                            pipe = null;
                        }
                    }
                }

                requestTrailers.TrySetResult(null);
                if (pipe is not null)
                {
                    await pipe.Writer.CompleteAsync().ConfigureAwait(true);
                }
            }
            catch (Exception ex) when (IsError(ex))
            {
                requestTrailers.TrySetResult(null);
                status = ComponentValue.Error(ErrorCode(ex));
                if (pipe is not null)
                {
                    await pipe.Writer.CompleteAsync(ex).ConfigureAwait(true);
                }
            }

            transmitted.SetResult(status);
        });

        return transmitted.Task;
    }

    // ------------------------------------------------------------------------------------------------------------
    // wasi:http/handler
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>Calls the export <c>handle</c> of a component with a request of the host, and returns the response with its body.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The component consumes the body of the request, which disposes its content; the caller disposes the response")]
    public async Task<HttpResponseMessage> HandleAsync(ComponentFunction handle, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw new ArgumentException("The request has no URI", nameof(request));
        var headers = ToFields(request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>()), isImmutable: true);
        var incoming = new Request
        {
            Method = request.Method.Method,
            PathWithQuery = uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString,
            Scheme = uri.IsAbsoluteUri ? uri.Scheme.ToUpperInvariant() : "HTTP",
            Authority = uri.IsAbsoluteUri ? uri.Authority : request.Headers.Host,
            Headers = headers,
            // A request without content has an empty body, and is forwarded without content
            Body = new Body { HostContent = request.Content, HostTrailers = () => null },
        };

        var result = handle.Call(Resource(_request, incoming)) ?? throw new InvalidOperationException("The handler returned no result");
        if (result.IsCase("error"))
            return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new ByteArrayContent([]) };

        var response = Get<Response>(result.Payload!.Value);
        var (body, trailers) = await ReadBodyAsync(response.Body, cancellationToken).ConfigureAwait(true);
        response.Transmission?.Write(ComponentValue.Ok());

        var message = new HttpResponseMessage((HttpStatusCode)response.StatusCode) { Content = new ByteArrayContent(body) };
        AddHeaders(response.Headers, message.Headers, message.Content);
        if (trailers is not null)
        {
            foreach (var (name, value) in trailers.Entries)
            {
                message.TrailingHeaders.TryAddWithoutValidation(name, Encoding.Latin1.GetString(value));
            }
        }

        return message;
    }

    /// <summary>Reads a body: its bytes and its trailers.</summary>
    private static async Task<(byte[] Body, Fields? Trailers)> ReadBodyAsync(Body body, CancellationToken cancellationToken)
    {
        body.IsConsumed = true;
        if (body.HostContent is { } content)
            return (await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(true), body.HostTrailers?.Invoke() is { } hostTrailers ? ToFields(hostTrailers, isImmutable: true) : null);

        using var output = new MemoryStream();
        if (body.Contents is { } contents)
        {
            using var reader = contents.AsStreamReader();
            var buffer = new byte[BufferSize];
            while (true)
            {
                var read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(true);
                if (read == 0 && reader.IsCompleted)
                    break;

                output.Write(buffer, 0, read);
            }
        }

        Fields? trailers = null;
        if (body.Trailers is { } future)
        {
            using var reader = future.AsFutureReader();
            var value = await reader.ReadAsync(cancellationToken).ConfigureAwait(true);
            if (value is { } trailersResult && trailersResult.IsCase("ok") && trailersResult.Payload is { } option && option.IsCase("some"))
            {
                trailers = Get<Fields>(option.Payload!.Value);
            }
        }

        return (output.ToArray(), trailers);
    }
}
