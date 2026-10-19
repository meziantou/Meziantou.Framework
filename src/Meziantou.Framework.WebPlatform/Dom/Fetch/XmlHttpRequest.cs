using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Encodings;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// Sends HTTP requests from a document (https://xhr.spec.whatwg.org/#interface-xmlhttprequest). The requests are fetched
/// with the fetch algorithm of the library (https://fetch.spec.whatwg.org/#fetching: the CORS protocol, the redirects, the
/// <c>blob:</c> and <c>data:</c> URLs), whose HTTP requests are sent by the <see cref="DocumentLoader"/> of the browsing
/// context of the window that created the object. The events are fired by the tasks of the event loop of the window.
/// </summary>
/// <remarks>
/// The response body is received at once: the <c>progress</c> event is fired once, when the body is received. A
/// synchronous request blocks the event loop until the loader completes. An object created without window (e.g. from .NET
/// without script engine) cannot send requests: they are network errors.
/// </remarks>
/// <seealso href="https://xhr.spec.whatwg.org/#interface-xmlhttprequest"/>
[IdlInterface("XMLHttpRequest", "https://xhr.spec.whatwg.org/#interface-xmlhttprequest")]
public sealed class XmlHttpRequest : XmlHttpRequestEventTarget
{
    /// <summary>The relevant global object when it is a window (the synchronous requests of a window have restrictions).</summary>
    private Window? RelevantWindow => RelevantGlobal as Window;

    /// <summary>The document that represents the relevant settings object: the document of a window, or the document of a worker.</summary>
    private Document? RelevantEnvironment => RelevantGlobal?.EnvironmentDocument;

    /// <summary>The state of a request that is created but not opened yet (<c>UNSENT</c>).</summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-xmlhttprequest-unsent"/>
    [IdlMember("UNSENT", "https://xhr.spec.whatwg.org/#dom-xmlhttprequest-unsent")]
    public const ushort Unsent = 0;

    /// <summary>The state of a request whose <c>open()</c> method has been called (<c>OPENED</c>).</summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-xmlhttprequest-opened"/>
    [IdlMember("OPENED", "https://xhr.spec.whatwg.org/#dom-xmlhttprequest-opened")]
    public const ushort Opened = 1;

    /// <summary>The state of a request whose response headers have been received (<c>HEADERS_RECEIVED</c>).</summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-xmlhttprequest-headers_received"/>
    [IdlMember("HEADERS_RECEIVED", "https://xhr.spec.whatwg.org/#dom-xmlhttprequest-headers_received")]
    public const ushort HeadersReceived = 2;

    /// <summary>The state of a request whose response body is being received (<c>LOADING</c>).</summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-xmlhttprequest-loading"/>
    [IdlMember("LOADING", "https://xhr.spec.whatwg.org/#dom-xmlhttprequest-loading")]
    public const ushort Loading = 3;

    /// <summary>The state of a request whose operation is complete (<c>DONE</c>).</summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-xmlhttprequest-done"/>
    [IdlMember("DONE", "https://xhr.spec.whatwg.org/#dom-xmlhttprequest-done")]
    public const ushort Done = 4;

    private bool _sendFlag;
    private uint _timeout;
    private bool _crossOriginCredentials;
    private string _requestMethod = "GET";
    private UrlRecord? _requestUrl;
    private BlobUrlEntry? _requestBlobUrlEntry;
    private List<KeyValuePair<string, string>> _authorRequestHeaders = [];
    private byte[]? _requestBody;
    private bool _synchronous;
    private bool _uploadComplete;
    private bool _uploadListener;
    private bool _timedOut;
    private FetchResponse _response = FetchResponse.NetworkError();
    private byte[] _receivedBytes = [];
    private XmlHttpRequestResponseType _responseType;
    private object? _responseObject;
    private bool _responseObjectFailure;
    private MimeType? _overrideMimeType;

    // Incremented when the fetch is terminated (open(), abort(), the timeout): the tasks of the previous fetch do not run
    private int _fetchId;
    private CancellationTokenSource? _fetchController;
    private IDisposable? _fetchGroupRecord;
    private long _fetchStart;
    private long _lastProgressLoaded = -1;

    /// <summary>Creates a request object in the <see cref="Unsent"/> state.</summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-xmlhttprequest"/>
    [IdlMember("constructor", "https://xhr.spec.whatwg.org/#dom-xmlhttprequest")]
    public XmlHttpRequest()
    {
        Upload = new XmlHttpRequestUpload();
    }

    /// <summary>Gets or sets the event handler of the <c>readystatechange</c> event.</summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#handler-xhr-onreadystatechange"/>
    [IdlMember("onreadystatechange", "https://xhr.spec.whatwg.org/#handler-xhr-onreadystatechange")]
    public Action<Event>? OnReadyStateChange
    {
        get => GetEventHandler("readystatechange");
        set => SetEventHandler("readystatechange", value);
    }

    /// <summary>Gets the state of the request: <see cref="Unsent"/>, <see cref="Opened"/>, <see cref="HeadersReceived"/>, <see cref="Loading"/> or <see cref="Done"/>.</summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-xmlhttprequest-readystate"/>
    [IdlMember("readyState", "https://xhr.spec.whatwg.org/#dom-xmlhttprequest-readystate")]
    public ushort ReadyState { get; private set; }

    /// <summary>Gets the target of the progress events of the request body.</summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-xmlhttprequest-upload"/>
    [IdlMember("upload", "https://xhr.spec.whatwg.org/#dom-xmlhttprequest-upload")]
    public XmlHttpRequestUpload Upload { get; }

    /// <summary>Gets or sets the timeout of the request, in milliseconds (0 for no timeout).</summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-xmlhttprequest-timeout"/>
    [IdlMember("timeout", "https://xhr.spec.whatwg.org/#dom-xmlhttprequest-timeout")]
    public uint Timeout
    {
        get => _timeout;
        set
        {
            if (RelevantWindow is not null && _synchronous)
                throw new DomException("The timeout of a synchronous request cannot be set", "InvalidAccessError");

            _timeout = value;

            // The timeout is relative to the start of the fetch
            if (_sendFlag && !_synchronous)
            {
                ScheduleTimeout(_fetchId);
            }
        }
    }

    /// <summary>Gets or sets whether the credentials are included in a cross-origin request.</summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-xmlhttprequest-withcredentials"/>
    [IdlMember("withCredentials", "https://xhr.spec.whatwg.org/#dom-xmlhttprequest-withcredentials")]
    public bool WithCredentials
    {
        get => _crossOriginCredentials;
        set
        {
            if (ReadyState is not (Unsent or Opened) || _sendFlag)
                throw DomException.InvalidState("The withCredentials attribute cannot be set after the request is sent");

            _crossOriginCredentials = value;
        }
    }

    /// <summary>Initializes an asynchronous request (https://xhr.spec.whatwg.org/#dom-xmlhttprequest-open).</summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-xmlhttprequest-open"/>
    [IdlMember("open", "https://xhr.spec.whatwg.org/#dom-xmlhttprequest-open")]
    public void Open(string method, string url) => OpenCore(method, url, async: true, username: null, password: null, setCredentials: false);

    /// <summary>Initializes a request (https://xhr.spec.whatwg.org/#dom-xmlhttprequest-open).</summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-xmlhttprequest-open"/>
    [IdlMember("open", "https://xhr.spec.whatwg.org/#dom-xmlhttprequest-open")]
    public void Open(string method, string url, bool async, string? username = null, string? password = null) => OpenCore(method, url, async, username, password, setCredentials: true);

    private void OpenCore(string method, string url, bool async, string? username, string? password, bool setCredentials)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(url);
        var window = RelevantWindow;
        if (window is not null && !window.Document.IsFullyActive)
            throw DomException.InvalidState("The document is not fully active");

        if (!FetchHeaders.IsMethod(method))
            throw DomException.Syntax($"'{method}' is not a valid method");

        if (FetchHeaders.IsForbiddenMethod(method))
            throw new DomException($"The method '{method}' is forbidden", "SecurityError");

        method = FetchHeaders.NormalizeMethod(method);
        var environment = RelevantEnvironment;
        var parsedUrl = (environment is null ? UrlParser.Parse(url, baseUrl: null) : Html.Reflection.EncodingParseUrl(environment, url))
            ?? throw DomException.Syntax($"The URL '{url}' is invalid");

        if (setCredentials && parsedUrl.Host is not null)
        {
            if (username is not null)
            {
                parsedUrl.Username = PercentEncoding.Utf8PercentEncode(username, PercentEncodeSet.Userinfo);
            }

            if (password is not null)
            {
                parsedUrl.Password = PercentEncoding.Utf8PercentEncode(password, PercentEncodeSet.Userinfo);
            }
        }

        if (!async && window is not null && (_timeout != 0 || _responseType != XmlHttpRequestResponseType.Default))
            throw new DomException("A synchronous request cannot have a timeout or a response type", "InvalidAccessError");

        TerminateFetch();
        _sendFlag = false;
        _uploadListener = false;
        _requestMethod = method;
        _requestUrl = parsedUrl;

        // The blob of a blob: URL is resolved when the URL is parsed (https://url.spec.whatwg.org/#concept-url-blob-entry)
        _requestBlobUrlEntry = environment is not null ? BlobUrlStore.Get(environment).Resolve(parsedUrl) : null;
        _synchronous = !async;
        _authorRequestHeaders = [];
        _response = FetchResponse.NetworkError();
        _receivedBytes = [];
        _lastProgressLoaded = -1;
        ResetResponseObject();
        if (ReadyState != Opened)
        {
            ReadyState = Opened;
            FireEvent(new Event("readystatechange"));
        }
    }

    /// <summary>Adds a header to the request (https://xhr.spec.whatwg.org/#dom-xmlhttprequest-setrequestheader).</summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-xmlhttprequest-setrequestheader"/>
    [IdlMember("setRequestHeader", "https://xhr.spec.whatwg.org/#dom-xmlhttprequest-setrequestheader")]
    public void SetRequestHeader(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        if (ReadyState != Opened || _sendFlag)
            throw DomException.InvalidState("The request is not opened, or it is already sent");

        value = FetchHeaders.Normalize(value);
        if (!FetchHeaders.IsHeaderName(name))
            throw DomException.Syntax($"'{name}' is not a valid header name");

        if (!FetchHeaders.IsHeaderValue(value))
            throw DomException.Syntax($"'{value}' is not a valid header value");

        if (FetchHeaders.IsForbiddenRequestHeader(name, value))
            return;

        FetchHeaders.Combine(_authorRequestHeaders, name, value);
    }

    /// <summary>
    /// Sends the request (https://xhr.spec.whatwg.org/#dom-xmlhttprequest-send). The body is null, a <see cref="Dom.Document"/>,
    /// a string, a <see cref="Blob"/>, a <see cref="FormData"/>, a <see cref="UrlSearchParams"/>, or bytes (a
    /// <see cref="byte"/> array or a <see cref="ReadOnlyMemory{T}"/> of bytes).
    /// </summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-xmlhttprequest-send"/>
    [IdlMember("send", "https://xhr.spec.whatwg.org/#dom-xmlhttprequest-send")]
    public void Send(object? body = null)
    {
        if (ReadyState != Opened)
            throw DomException.InvalidState("The request is not opened");

        if (_sendFlag)
            throw DomException.InvalidState("The request is already sent");

        // The "sync-xhr" policy-controlled feature, which XMLHttpRequest does not specify: as in Chromium, a document whose
        // permissions policy disables it cannot send a synchronous request
        if (_synchronous && RelevantWindow is { } window && !PermissionsPolicyAlgorithms.IsFeatureEnabled(PolicyControlledFeature.SyncXhr, window.Document, window.Document.Origin))
            throw new DomException("The permissions policy of the document disables the synchronous requests", "NetworkError");

        if (_requestMethod is "GET" or "HEAD")
        {
            body = null;
        }

        _requestBody = null;
        if (body is not null)
        {
            string? extractedContentType = null;
            if (body is Document document)
            {
                _requestBody = Encoding.UTF8.GetBytes(InfraString.ToScalarValueString(DynamicMarkup.FragmentSerialize(document, requireWellFormed: false)));
            }
            else
            {
                (_requestBody, extractedContentType) = ExtractBody(body);
            }

            var originalAuthorContentType = HeaderList.Get(_authorRequestHeaders, "Content-Type");
            if (originalAuthorContentType is not null)
            {
                if (body is Document or string or UrlSearchParams)
                {
                    var contentTypeRecord = MimeType.Parse(originalAuthorContentType);
                    if (contentTypeRecord?.GetParameter("charset") is { } charset && !InfraAscii.EqualsIgnoreAsciiCase(charset, "UTF-8"))
                    {
                        contentTypeRecord.SetParameter("charset", "UTF-8");
                        FetchHeaders.Set(_authorRequestHeaders, "Content-Type", contentTypeRecord.ToString());
                    }
                }
            }
            else if (body is Document bodyDocument)
            {
                FetchHeaders.Set(_authorRequestHeaders, "Content-Type", bodyDocument.IsHtmlDocument ? "text/html;charset=UTF-8" : "application/xml;charset=UTF-8");
            }
            else if (extractedContentType is not null)
            {
                FetchHeaders.Set(_authorRequestHeaders, "Content-Type", extractedContentType);
            }
        }

        if (Upload.HasEventListeners)
        {
            _uploadListener = true;
        }

        var environment = RelevantEnvironment;
        var request = new FetchRequest(_requestUrl!)
        {
            Method = _requestMethod,
            HeaderList = [.. _authorRequestHeaders],
            UnsafeRequest = true,
            Body = _requestBody,
            Client = environment,
            Origin = environment?.SerializeOrigin() ?? "null",
            Mode = RequestMode.Cors,
            UseCorsPreflight = _uploadListener,
            CredentialsMode = _crossOriginCredentials ? FetchCredentialsMode.Include : FetchCredentialsMode.SameOrigin,
            UseUrlCredentials = _requestUrl!.IncludesCredentials,
            BlobUrlEntry = _requestBlobUrlEntry,
            Synchronous = _synchronous,
        };

        _uploadComplete = false;
        _timedOut = false;
        if (request.Body is null)
        {
            _uploadComplete = true;
        }

        _sendFlag = true;
        if (!_synchronous)
        {
            SendAsynchronously(request);
            return;
        }

        // A synchronous request: the loader is waited for, until the timeout elapses (https://xhr.spec.whatwg.org/#dom-xmlhttprequest-send, step 12.4)
        var fetchId = ++_fetchId;
        using var controller = _timeout != 0 && RelevantGlobal is { } global
            ? new CancellationTokenSource(TimeSpan.FromMilliseconds(_timeout), global.EventLoop.TimeProvider)
            : new CancellationTokenSource();
        if (_timeout != 0)
        {
            // An empty timer at the timeout: a host that advances a fake clock to the next timer of the event loops (see
            // DomEventLoop.PromoteDueTimers) does not advance it past the timeout
            RelevantGlobal?.EventLoop.QueueTimer(TimeSpan.FromMilliseconds(_timeout), static () => { });
        }
        _fetchController = controller;
        FetchResponse response;
        var timing = environment is not null && RelevantGlobal is { } relevantGlobal ? ResourceTiming.Start(relevantGlobal, request, "xmlhttprequest") : null;
        try
        {
            response = environment is null ? FetchResponse.NetworkError() : Fetching.FetchAsync(request, controller.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (controller.IsCancellationRequested)
        {
            response = FetchResponse.NetworkError();
        }

        _fetchController = null;
        if (fetchId != _fetchId)
            return;

        if (_timeout != 0 && controller.IsCancellationRequested)
        {
            _timedOut = true;
            HandleErrors();
            return;
        }

        timing?.Report(response);
        _receivedBytes = response.Body ?? [];
        _response = response;
        HandleResponseEndOfBody();
    }

    private void SendAsynchronously(FetchRequest request)
    {
        // open() terminates the fetch: a call from an event handler stops this send()
        var sendId = _fetchId;
        FireProgressEvent("loadstart", 0, 0);
        var requestBodyLength = request.Body?.Length ?? 0;
        if (!_uploadComplete && _uploadListener)
        {
            Upload.FireProgressEvent("loadstart", 0, requestBodyLength);
        }

        if (ReadyState != Opened || !_sendFlag || sendId != _fetchId)
            return;

        var fetchId = ++_fetchId;
        var controller = new CancellationTokenSource();
        _fetchController = controller;
        _fetchStart = RelevantGlobal?.EventLoop.TimeProvider.GetTimestamp() ?? 0;
        if (_timeout != 0)
        {
            ScheduleTimeout(fetchId);
        }

        if (RelevantEnvironment is not { } environment)
        {
            ProcessResponse(fetchId, FetchResponse.NetworkError());
            return;
        }

        // The fetch is aborted with the document (https://fetch.spec.whatwg.org/#concept-fetch-group): the queued tasks of the
        // fetch are discarded, and the abort is processed by a task, which open() cancels
        _fetchGroupRecord = environment.FetchGroup.Add(() =>
        {
            if (fetchId != _fetchId || !_sendFlag)
                return;

            TerminateFetch();
            QueueFetchTask(_fetchId, () =>
            {
                _response = FetchResponse.NetworkError(aborted: true);
                HandleErrors();
            });
        });

        var timing = RelevantGlobal is { } relevantGlobal ? ResourceTiming.Start(relevantGlobal, request, "xmlhttprequest") : null;
        var task = Fetching.FetchAsync(request, controller.Token);
        _ = task.ContinueWith(
            completed =>
            {
                // The request body is transmitted, then the response is processed, by the tasks of the networking task source
                QueueFetchTask(fetchId, () =>
                {
                    timing?.Report(completed.Result);
                    // The body of a request answered by a service worker is not transmitted: the upload has no progress events
                    if (!_uploadComplete && !completed.Result.IsNetworkError && !request.IsAnsweredByServiceWorker)
                    {
                        _uploadComplete = true;
                        if (_uploadListener)
                        {
                            Upload.FireProgressEvent("progress", requestBodyLength, requestBodyLength);
                            Upload.FireProgressEvent("load", requestBodyLength, requestBodyLength);
                            Upload.FireProgressEvent("loadend", requestBodyLength, requestBodyLength);
                        }
                    }
                });
                QueueFetchTask(fetchId, () => ProcessResponse(fetchId, completed.Result));
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>The processResponse steps of send() (https://xhr.spec.whatwg.org/#dom-xmlhttprequest-send, step 11.9).</summary>
    private void ProcessResponse(int fetchId, FetchResponse response)
    {
        _response = response;
        HandleErrors();
        if (_response.IsNetworkError)
            return;

        ReadyState = HeadersReceived;
        FireEvent(new Event("readystatechange"));
        if (ReadyState != HeadersReceived || fetchId != _fetchId)
            return;

        if (_response.Body is not { } body)
        {
            HandleResponseEndOfBody();
            return;
        }

        var length = GetContentLength(_response.HeaderList);
        if (body.Length == 0)
        {
            QueueFetchTask(fetchId, HandleResponseEndOfBody);
            return;
        }

        // The body is received as a single chunk (https://xhr.spec.whatwg.org/#dom-xmlhttprequest-send, processBodyChunk)
        QueueFetchTask(fetchId, () =>
        {
            _receivedBytes = body;
            if (ReadyState == HeadersReceived)
            {
                ReadyState = Loading;
            }

            FireEvent(new Event("readystatechange"));
            if (fetchId != _fetchId)
                return;

            FireProgressEvent("progress", _receivedBytes.Length, length);
            _lastProgressLoaded = _receivedBytes.Length;
            QueueFetchTask(fetchId, HandleResponseEndOfBody);
        });
    }

    /// <summary>https://xhr.spec.whatwg.org/#handle-response-end-of-body</summary>
    private void HandleResponseEndOfBody()
    {
        HandleErrors();
        if (_response.IsNetworkError)
            return;

        _receivedBytes = _response.Body ?? [];
        var transmitted = _receivedBytes.Length;
        var length = GetContentLength(_response.HeaderList);

        // The body is received at once: the progress event of its chunk was already fired
        if (!_synchronous && transmitted != _lastProgressLoaded)
        {
            FireProgressEvent("progress", transmitted, length);
        }

        ReadyState = Done;
        _sendFlag = false;
        _fetchController = null;
        ReleaseFetchGroupRecord();
        FireEvent(new Event("readystatechange"));
        FireProgressEvent("load", transmitted, length);
        FireProgressEvent("loadend", transmitted, length);
    }

    /// <summary>https://xhr.spec.whatwg.org/#handle-errors</summary>
    private void HandleErrors()
    {
        if (!_sendFlag)
            return;

        if (_timedOut)
        {
            RequestErrorSteps("timeout", new DomException("The request timed out", "TimeoutError"));
        }
        else if (_response.Aborted)
        {
            RequestErrorSteps("abort", new DomException("The request was aborted", "AbortError"));
        }
        else if (_response.IsNetworkError)
        {
            RequestErrorSteps("error", new DomException("A network error occurred", "NetworkError"));
        }
    }

    /// <summary>https://xhr.spec.whatwg.org/#request-error-steps</summary>
    private void RequestErrorSteps(string eventType, DomException? exception)
    {
        ReadyState = Done;
        _sendFlag = false;
        _response = FetchResponse.NetworkError();
        ReleaseFetchGroupRecord();
        if (_synchronous && exception is not null)
            throw exception;

        FireEvent(new Event("readystatechange"));
        if (!_uploadComplete)
        {
            _uploadComplete = true;
            if (_uploadListener)
            {
                Upload.FireProgressEvent(eventType, 0, 0);
                Upload.FireProgressEvent("loadend", 0, 0);
            }
        }

        FireProgressEvent(eventType, 0, 0);
        FireProgressEvent("loadend", 0, 0);
    }

    /// <summary>Cancels the request (https://xhr.spec.whatwg.org/#dom-xmlhttprequest-abort).</summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-xmlhttprequest-abort"/>
    [IdlMember("abort", "https://xhr.spec.whatwg.org/#dom-xmlhttprequest-abort")]
    public void Abort()
    {
        TerminateFetch();
        if ((ReadyState == Opened && _sendFlag) || ReadyState is HeadersReceived or Loading)
        {
            RequestErrorSteps("abort", exception: null);
        }

        if (ReadyState == Done)
        {
            ReadyState = Unsent;
            _response = FetchResponse.NetworkError();
        }
    }

    /// <summary>Gets the URL of the response, without its fragment, or the empty string.</summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-xmlhttprequest-responseurl"/>
    [IdlMember("responseURL", "https://xhr.spec.whatwg.org/#dom-xmlhttprequest-responseurl")]
    public string ResponseUrl => _response.Url?.Serialize(excludeFragment: true) ?? "";

    /// <summary>Gets the status of the response (0 before the response is received, and for a network error).</summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-xmlhttprequest-status"/>
    [IdlMember("status", "https://xhr.spec.whatwg.org/#dom-xmlhttprequest-status")]
    public ushort Status => (ushort)_response.Status;

    /// <summary>Gets the status message of the response.</summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-xmlhttprequest-statustext"/>
    [IdlMember("statusText", "https://xhr.spec.whatwg.org/#dom-xmlhttprequest-statustext")]
    public string StatusText => _response.StatusMessage;

    /// <summary>Returns the values of the response headers with the name, separated by <c>", "</c>, or null.</summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-xmlhttprequest-getresponseheader"/>
    [IdlMember("getResponseHeader", "https://xhr.spec.whatwg.org/#dom-xmlhttprequest-getresponseheader")]
    public string? GetResponseHeader(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return HeaderList.Get(_response.HeaderList, name);
    }

    /// <summary>Returns the response headers, sorted by name (lowercase), one per line (https://xhr.spec.whatwg.org/#dom-xmlhttprequest-getallresponseheaders).</summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-xmlhttprequest-getallresponseheaders"/>
    [IdlMember("getAllResponseHeaders", "https://xhr.spec.whatwg.org/#dom-xmlhttprequest-getallresponseheaders")]
    public string GetAllResponseHeaders()
    {
        // The names are sorted by their legacy-uppercased bytes (e.g. "also-here" before "__custom")
        var headers = FetchHeaders.SortAndCombine(_response.HeaderList).OrderBy(header => InfraAscii.ToAsciiUppercase(header.Key), StringComparer.Ordinal);
        var output = new StringBuilder();
        foreach (var (name, value) in headers)
        {
            output.Append(name).Append(": ").Append(value).Append("\r\n");
        }

        return output.ToString();
    }

    /// <summary>Overrides the MIME type of the response (https://xhr.spec.whatwg.org/#dom-xmlhttprequest-overridemimetype).</summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-xmlhttprequest-overridemimetype"/>
    [IdlMember("overrideMimeType", "https://xhr.spec.whatwg.org/#dom-xmlhttprequest-overridemimetype")]
    public void OverrideMimeType(string mime)
    {
        ArgumentNullException.ThrowIfNull(mime);
        if (ReadyState is Loading or Done)
            throw DomException.InvalidState("The MIME type cannot be overridden once the response is loading");

        _overrideMimeType = MimeType.Parse(mime) ?? MimeType.Parse("application/octet-stream");
    }

    /// <summary>Gets or sets the type of <see cref="Response"/>.</summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-xmlhttprequest-responsetype"/>
    [IdlMember("responseType", "https://xhr.spec.whatwg.org/#dom-xmlhttprequest-responsetype")]
    public XmlHttpRequestResponseType ResponseType
    {
        get => _responseType;
        set
        {
            // https://xhr.spec.whatwg.org/#dom-xmlhttprequest-responsetype: "document" is ignored in the workers
            if (RelevantGlobal is not null and not Window && value == XmlHttpRequestResponseType.Document)
                return;

            if (ReadyState is Loading or Done)
                throw DomException.InvalidState("The response type cannot be set once the response is loading");

            if (RelevantWindow is not null && _synchronous)
                throw new DomException("The response type of a synchronous request cannot be set", "InvalidAccessError");

            _responseType = value;
        }
    }

    /// <summary>
    /// Gets the response (https://xhr.spec.whatwg.org/#dom-xmlhttprequest-response): a string for the text types, else
    /// null until the response is received, then a <see cref="byte"/> array (an <c>ArrayBuffer</c> in JavaScript), a
    /// <see cref="Blob"/>, a <see cref="Dom.Document"/> or the JSON value (parsed by the script engine).
    /// </summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-xmlhttprequest-response"/>
    [IdlMember("response", "https://xhr.spec.whatwg.org/#dom-xmlhttprequest-response")]
    public object? Response
    {
        get
        {
            if (_responseType is XmlHttpRequestResponseType.Default or XmlHttpRequestResponseType.Text)
                return ReadyState is Loading or Done ? GetTextResponse() : "";

            if (ReadyState != Done || _responseObjectFailure)
                return null;

            if (_responseObject is not null)
                return _responseObject;

            switch (_responseType)
            {
                case XmlHttpRequestResponseType.ArrayBuffer:
                    _responseObject = _receivedBytes.ToArray();
                    break;
                case XmlHttpRequestResponseType.Blob:
                    _responseObject = new Blob([_receivedBytes], new BlobPropertyBag { Type = GetFinalMimeType().ToString() });
                    break;
                case XmlHttpRequestResponseType.Document:
                    SetDocumentResponse();
                    break;
                case XmlHttpRequestResponseType.Json:
                    if (_response.Body is null)
                        return null;

                    _responseObject = new Scripting.JsonText(DecodeUtf8WithoutBom(_receivedBytes));
                    break;
            }

            return _responseObject;
        }
    }

    /// <summary>Gets the text of the response (https://xhr.spec.whatwg.org/#dom-xmlhttprequest-responsetext).</summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-xmlhttprequest-responsetext"/>
    [IdlMember("responseText", "https://xhr.spec.whatwg.org/#dom-xmlhttprequest-responsetext")]
    public string ResponseText
    {
        get
        {
            if (_responseType is not (XmlHttpRequestResponseType.Default or XmlHttpRequestResponseType.Text))
                throw DomException.InvalidState("The response type is not text");

            return ReadyState is Loading or Done ? GetTextResponse() : "";
        }
    }

    /// <summary>Gets the document of the response, or null (https://xhr.spec.whatwg.org/#dom-xmlhttprequest-responsexml).</summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-xmlhttprequest-responsexml"/>
    [IdlMember("responseXML", "https://xhr.spec.whatwg.org/#dom-xmlhttprequest-responsexml")]
    public Document? ResponseXml
    {
        get
        {
            if (_responseType is not (XmlHttpRequestResponseType.Default or XmlHttpRequestResponseType.Document))
                throw DomException.InvalidState("The response type is not document");

            if (ReadyState != Done)
                return null;

            if (_responseObject is null && !_responseObjectFailure)
            {
                SetDocumentResponse();
            }

            return _responseObject as Document;
        }
    }

    private void ResetResponseObject()
    {
        _responseObject = null;
        _responseObjectFailure = false;
    }

    /// <summary>https://xhr.spec.whatwg.org/#response-mime-type</summary>
    private MimeType GetResponseMimeType() => HeaderList.ExtractMimeType(_response.HeaderList) ?? MimeType.Parse("text/xml")!;

    /// <summary>https://xhr.spec.whatwg.org/#final-mime-type</summary>
    private MimeType GetFinalMimeType() => _overrideMimeType ?? GetResponseMimeType();

    /// <summary>https://xhr.spec.whatwg.org/#final-charset</summary>
    private string? GetFinalCharset() => _overrideMimeType?.GetParameter("charset") ?? GetResponseMimeType().GetParameter("charset");

    /// <summary>https://xhr.spec.whatwg.org/#text-response</summary>
    private string GetTextResponse()
    {
        if (_response.Body is null || _receivedBytes.Length == 0)
            return "";

        WebEncodingId? encoding = GetFinalCharset() is { } charset && WebEncodingRegistry.TryGetEncoding(charset, out var charsetEncoding) ? charsetEncoding : null;
        if (_responseType == XmlHttpRequestResponseType.Default && encoding is null && GetFinalMimeType().IsXml)
        {
            encoding = GetXmlEncoding(_receivedBytes);
        }

        return Decode(_receivedBytes, encoding ?? WebEncodingId.Utf8);
    }

    /// <summary>https://xhr.spec.whatwg.org/#document-response</summary>
    private void SetDocumentResponse()
    {
        if (_response.Body is null)
            return;

        var finalMimeType = GetFinalMimeType();
        var isHtml = finalMimeType.Essence == "text/html";
        if (!isHtml && !finalMimeType.IsXml)
        {
            _responseObjectFailure = _responseType == XmlHttpRequestResponseType.Default;
            return;
        }

        if (isHtml && _responseType == XmlHttpRequestResponseType.Default)
        {
            _responseObjectFailure = true;
            return;
        }

        var environment = RelevantEnvironment;
        var url = _response.Url?.Serialize();
        Document document;
        var charset = GetFinalCharset();
        if (isHtml)
        {
            // The final charset, else the prescan, else UTF-8. The encoding is tentative, as for a navigation (a meta element of
            // the head after the first 1024 bytes changes it), as in the browsers (tested by WPT html/syntax/charset/xhr.html).
            // The encoding is not detected from the content (https://xhr.spec.whatwg.org/#document-response).
            document = new HtmlDocumentParser(new HtmlParserOptions { Url = url, TransportEncoding = charset, DefaultEncoding = "UTF-8", EncodingDetector = null, AllowDeclarativeShadowRoots = false, OriginDocument = environment }).Parse(_receivedBytes);
        }
        else
        {
            var encoding = charset is not null && WebEncodingRegistry.TryGetEncoding(charset, out var charsetEncoding) ? charsetEncoding : GetXmlEncoding(_receivedBytes) ?? WebEncodingId.Utf8;
            try
            {
                document = XmlDocumentParser.ParseDocument(Decode(_receivedBytes, encoding), new XmlParserOptions { Url = url, ContentType = finalMimeType.Essence });
            }
            catch (System.Xml.XmlException)
            {
                _responseObjectFailure = true;
                return;
            }

            document.Encoding = encoding;
            document.OriginDocument = environment;
        }

        document.ContentType = finalMimeType.Essence;

        // https://html.spec.whatwg.org/multipage/dom.html#dom-document-lastmodified: the Last-Modified header, else the time of the response
        document.LastModifiedTime = (HeaderList.Get(_response.HeaderList, "Last-Modified") is { } lastModified ? HeaderList.ParseHttpDate(lastModified) : null)
            ?? (RelevantGlobal?.EventLoop.TimeProvider ?? TimeProvider.System).GetUtcNow();
        _responseObject = document;
    }

    /// <summary>The encoding of an XML document (https://html.spec.whatwg.org/multipage/parsing.html#xml-encoding): its byte order mark or the encoding of its XML declaration, or null.</summary>
    private static WebEncodingId? GetXmlEncoding(ReadOnlySpan<byte> bytes)
    {
        if (WebEncodingRegistry.SniffBom(bytes, out var bomEncoding) > 0)
            return bomEncoding;

        if (!bytes.StartsWith("<?xml"u8))
            return null;

        var end = bytes.IndexOf("?>"u8);
        if (end < 0)
            return null;

        var declaration = Encoding.ASCII.GetString(bytes[..end]);
        var index = declaration.IndexOf("encoding", StringComparison.Ordinal);
        if (index < 0)
            return null;

        var position = index + "encoding".Length;
        while (position < declaration.Length && declaration[position] is ' ' or '\t' or '\r' or '\n')
        {
            position++;
        }

        if (position >= declaration.Length || declaration[position] != '=')
            return null;

        position++;
        while (position < declaration.Length && declaration[position] is ' ' or '\t' or '\r' or '\n')
        {
            position++;
        }

        if (position >= declaration.Length || declaration[position] is not ('"' or '\''))
            return null;

        var quote = declaration[position];
        var valueEnd = declaration.IndexOf(quote, position + 1, StringComparison.Ordinal);
        if (valueEnd < 0)
            return null;

        return WebEncodingRegistry.TryGetEncoding(declaration.AsSpan()[(position + 1)..valueEnd].ToString(), out var encoding) ? encoding : null;
    }

    /// <summary>https://encoding.spec.whatwg.org/#decode: the byte order mark overrides the encoding.</summary>
    private static string Decode(ReadOnlySpan<byte> bytes, WebEncodingId encoding)
    {
        var bomLength = WebEncodingRegistry.SniffBom(bytes, out var bomEncoding);
        if (bomLength > 0)
        {
            encoding = bomEncoding;
            bytes = bytes[bomLength..];
        }

        return WebEncodingRegistry.CreateDecoder(encoding).DecodeToString(bytes);
    }

    /// <summary>https://encoding.spec.whatwg.org/#utf-8-decode</summary>
    private static string DecodeUtf8WithoutBom(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith("\U0000FEFF"u8))
        {
            bytes = bytes[3..];
        }

        return WebEncodingRegistry.CreateDecoder(WebEncodingId.Utf8).DecodeToString(bytes);
    }

    /// <summary>Whether the body of a response was encoded with a content coding (the loader returns the decoded body).</summary>
    private static bool HasContentCoding(IReadOnlyList<KeyValuePair<string, string>> headers)
        => HeaderList.Get(headers, "Content-Encoding") is { Length: > 0 } contentEncoding && !Infra.InfraAscii.EqualsIgnoreAsciiCase(contentEncoding.Trim(), "identity");

    /// <summary>
    /// https://fetch.spec.whatwg.org/#content-length-header: 0 when the header is missing or invalid. The length of a response
    /// with a content coding is the length of the encoded body, while the received bytes are decoded: as in the browsers, the
    /// length is then not computable (0).
    /// </summary>
    private static long GetContentLength(IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        if (HasContentCoding(headers))
            return 0;

        if (HeaderList.GetDecodeAndSplit(headers, "Content-Length") is not { Count: > 0 } values)
            return 0;

        var first = values[0];
        foreach (var value in values)
        {
            if (value != first)
                return 0;
        }

        return long.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out var length) ? length : 0;
    }

    /// <summary>
    /// https://fetch.spec.whatwg.org/#concept-bodyinit-extract for the types of XMLHttpRequestBodyInit: returns the bytes and the
    /// Content-Type of the body.
    /// </summary>
    internal static (byte[] Body, string? ContentType) ExtractBody(object body)
    {
        switch (body)
        {
            case Blob blob:
                return (blob.Data.ToArray(), blob.Type.Length > 0 ? blob.Type : null);
            case byte[] bytes:
                return (bytes.ToArray(), null);
            case ReadOnlyMemory<byte> memory:
                return (memory.ToArray(), null);
            case Scripting.JsBufferSource bufferSource:
                return (bufferSource.Bytes.ToArray(), null);
            case FormData formData:
            {
                // An empty FormData is an empty body, as in the browsers
                if (formData.Entries.Count == 0)
                    return ([], "multipart/form-data; boundary=" + FormSubmissionEncoding.SerializeMultipart([], WebEncodingId.Utf8).Boundary);

                var (bytes, boundary) = FormSubmissionEncoding.SerializeMultipart(formData.Entries, WebEncodingId.Utf8);
                return (bytes, "multipart/form-data; boundary=" + boundary);
            }

            case UrlSearchParams searchParams:
                return (Encoding.UTF8.GetBytes(searchParams.ToString()), "application/x-www-form-urlencoded;charset=UTF-8");
            default:
                return (Encoding.UTF8.GetBytes(InfraString.ToScalarValueString(body.ToString() ?? "")), "text/plain;charset=UTF-8");
        }
    }

    /// <summary>Terminates the fetch: the tasks of the fetch are not run (https://fetch.spec.whatwg.org/#fetch-controller-terminate).</summary>
    private void TerminateFetch()
    {
        _fetchId++;
        ReleaseFetchGroupRecord();
        if (_fetchController is { } controller)
        {
            _fetchController = null;
            controller.Cancel();
        }
    }

    private void ReleaseFetchGroupRecord()
    {
        _fetchGroupRecord?.Dispose();
        _fetchGroupRecord = null;
    }

    /// <summary>Queues a task of the fetch (https://fetch.spec.whatwg.org/#queue-a-fetch-task), which does not run if the fetch is terminated.</summary>
    private void QueueFetchTask(int fetchId, Action action)
    {
        if (RelevantGlobal is not { } global)
            return;

        global.QueueGlobalTask(() =>
        {
            if (fetchId == _fetchId)
            {
                action();
            }
        });
    }

    /// <summary>
    /// Fires the timeout when the time elapsed since the fetch started reaches the timeout
    /// (https://xhr.spec.whatwg.org/#dom-xmlhttprequest-send, step 11.8).
    /// </summary>
    private void ScheduleTimeout(int fetchId)
    {
        if (RelevantGlobal is not { } window || _timeout == 0)
            return;

        var clock = window.EventLoop.TimeProvider;
        var elapsed = clock.GetElapsedTime(_fetchStart);
        var timeout = _timeout;
        window.EventLoop.QueueTimer(TimeSpan.FromMilliseconds(timeout) - elapsed, () =>
        {
            if (fetchId != _fetchId || !_sendFlag || _timeout != timeout)
                return;

            _timedOut = true;
            TerminateFetch();
            HandleErrors();
        });
    }
}
