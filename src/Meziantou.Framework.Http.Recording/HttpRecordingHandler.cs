using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;

namespace Meziantou.Framework.Http.Recording;

/// <summary>A delegating handler that records and replays HTTP interactions for testing.</summary>
public sealed class HttpRecordingHandler : DelegatingHandler
{
    private readonly IHttpRecordingStore _store;
    private readonly HttpRecordingMode _mode;
    private readonly HttpRecordingMissBehavior _missBehavior;
    private readonly IHttpRecordingSanitizer? _sanitizer;
    private readonly RecordingSession _session;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private volatile bool _initialized;

    /// <summary>Initializes a new instance of the <see cref="HttpRecordingHandler"/> class.</summary>
    /// <param name="store">The store used to load and save recorded entries.</param>
    /// <param name="options">The recording options.</param>
    public HttpRecordingHandler(IHttpRecordingStore store, HttpRecordingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;

        var resolvedOptions = options ?? new HttpRecordingOptions();
        _mode = resolvedOptions.Mode;
        _missBehavior = resolvedOptions.MissBehavior;
        _sanitizer = resolvedOptions.Sanitizer;

        var matcher = resolvedOptions.RequestMatcher ?? DefaultHttpRequestMatcher.Instance;
        _session = new RecordingSession(matcher);
    }

    /// <summary>Initializes a new instance of the <see cref="HttpRecordingHandler"/> class with an inner handler.</summary>
    /// <param name="innerHandler">The inner handler.</param>
    /// <param name="store">The store used to load and save recorded entries.</param>
    /// <param name="options">The recording options.</param>
    public HttpRecordingHandler(HttpMessageHandler innerHandler, IHttpRecordingStore store, HttpRecordingOptions? options = null)
        : base(innerHandler)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;

        var resolvedOptions = options ?? new HttpRecordingOptions();
        _mode = resolvedOptions.Mode;
        _missBehavior = resolvedOptions.MissBehavior;
        _sanitizer = resolvedOptions.Sanitizer;

        var matcher = resolvedOptions.RequestMatcher ?? DefaultHttpRequestMatcher.Instance;
        _session = new RecordingSession(matcher);
    }

    /// <summary>Loads existing recordings from the store. If not called explicitly, the first <see cref="SendAsync"/> call will call it automatically.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            var entries = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            _session.LoadEntries(entries);
            _initialized = true;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <summary>Saves all recorded entries to the store.</summary>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = _session.GetAllEntries();
            await _store.SaveAsync(entries, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <inheritdoc />
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope")]
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        return _mode switch
        {
            HttpRecordingMode.Record => await RecordAsync(request, cancellationToken).ConfigureAwait(false),
            HttpRecordingMode.Replay => Replay(request),
            HttpRecordingMode.Auto => await AutoAsync(request, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unknown recording mode: {_mode}"),
        };
    }

    private async Task<HttpResponseMessage> RecordAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await RecordEntryAsync(request, response, cancellationToken).ConfigureAwait(false);
        return response;
    }

    private HttpResponseMessage Replay(HttpRequestMessage request)
    {
        var requestEntry = HttpMessageConverter.CreateFromRequest(request);

        if (_session.TryGetRecordedResponse(requestEntry, out var match) && match is not null)
        {
            return HttpMessageConverter.ToHttpResponseMessage(match);
        }

        return HandleMiss(request, requestEntry);
    }

    private async Task<HttpResponseMessage> AutoAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var requestEntry = HttpMessageConverter.CreateFromRequest(request);

        if (_session.TryGetRecordedResponse(requestEntry, out var match) && match is not null)
        {
            return HttpMessageConverter.ToHttpResponseMessage(match);
        }

        // No match — record the real call
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await RecordEntryAsync(request, response, cancellationToken).ConfigureAwait(false);
        return response;
    }

    private async Task RecordEntryAsync(HttpRequestMessage request, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await response.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);

        var entry = await HttpMessageConverter.CreateFromRequestResponseAsync(request, response, cancellationToken).ConfigureAwait(false);
        _sanitizer?.Sanitize(entry);
        _session.AddRecordedEntry(entry);
    }

    private HttpResponseMessage HandleMiss(HttpRequestMessage request, HttpRecordingEntry requestEntry)
    {
        return _missBehavior switch
        {
            HttpRecordingMissBehavior.Throw => throw new HttpRecordingMissException(requestEntry.Method, requestEntry.RequestUri),
            HttpRecordingMissBehavior.ReturnDefault => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(
                    $"No recorded response found for {requestEntry.Method} {requestEntry.RequestUri}.",
                    Encoding.UTF8,
                    "text/plain"),
                RequestMessage = request,
            },
            HttpRecordingMissBehavior.Passthrough => throw new InvalidOperationException(
                "Passthrough miss behavior is not supported in Replay mode. Use Auto mode instead."),
            _ => throw new InvalidOperationException($"Unknown miss behavior: {_missBehavior}"),
        };
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _ioLock.Dispose();
        }

        base.Dispose(disposing);
    }
}
