using System.Net;

namespace Meziantou.Framework.Http.Recording;

/// <summary>A delegating handler that records and replays HTTP interactions for testing.</summary>
public sealed class HttpRecordingHandler : DelegatingHandler, IAsyncDisposable
{
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Disposing it would race in-flight requests that are about to release it, throwing ObjectDisposedException from a finally block and replacing the real exception. It never hands out a wait handle, so it holds no unmanaged resource. See Dispose(bool).")]
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private readonly Lock _pendingLock = new();

    private IHttpRecordingStore _store = null!;
    private HttpRecordingMode _mode;
    private HttpRecordingMissBehavior _missBehavior;
    private IHttpRecordingSanitizer[] _sanitizers = [];
    private bool _autoSave;
    private RecordingSession _session = null!;

    private volatile bool _initialized;
    private volatile bool _disposed;
    private volatile bool _autoSaved;
    private int _pendingRecordings;
    private TaskCompletionSource? _recordingsDrained;

    /// <summary>Initializes a new instance of the <see cref="HttpRecordingHandler"/> class without an inner handler.</summary>
    /// <param name="store">The store used to load and save recorded entries.</param>
    /// <param name="options">The recording options. Because there is no inner handler, no real HTTP call can be made, so the mode must be <see cref="HttpRecordingMode.Replay"/> and the miss behavior must not be <see cref="HttpRecordingMissBehavior.Passthrough"/>.</param>
    public HttpRecordingHandler(IHttpRecordingStore store, HttpRecordingOptions? options = null)
    {
        Initialize(store, options, hasInnerHandler: false);
    }

    /// <summary>Initializes a new instance of the <see cref="HttpRecordingHandler"/> class with an inner handler.</summary>
    /// <param name="innerHandler">The inner handler.</param>
    /// <param name="store">The store used to load and save recorded entries.</param>
    /// <param name="options">The recording options.</param>
    public HttpRecordingHandler(HttpMessageHandler innerHandler, IHttpRecordingStore store, HttpRecordingOptions? options = null)
        : base(innerHandler)
    {
        Initialize(store, options, hasInnerHandler: true);
    }

    private void Initialize(IHttpRecordingStore store, HttpRecordingOptions? options, bool hasInnerHandler)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;

        var resolvedOptions = options ?? new HttpRecordingOptions();
        _mode = resolvedOptions.Mode;
        _missBehavior = resolvedOptions.MissBehavior ?? (_mode is HttpRecordingMode.Auto
            ? HttpRecordingMissBehavior.Passthrough
            : HttpRecordingMissBehavior.Throw);
        _sanitizers = [.. resolvedOptions.Sanitizers];
        _autoSave = resolvedOptions.AutoSave;

        if (!hasInnerHandler)
        {
            // Without an inner handler every path that reaches the network throws a DelegatingHandler error that says
            // nothing about the misconfiguration. Reject it here instead, while the caller can still see why.
            if (_mode is not HttpRecordingMode.Replay)
            {
                throw new ArgumentException($"{nameof(HttpRecordingOptions)}.{nameof(HttpRecordingOptions.Mode)} must be {nameof(HttpRecordingMode.Replay)} when no inner handler is provided, because {_mode} performs real HTTP calls. Use the constructor that takes an inner handler.", nameof(options));
            }

            if (_missBehavior is HttpRecordingMissBehavior.Passthrough)
            {
                throw new ArgumentException($"{nameof(HttpRecordingOptions)}.{nameof(HttpRecordingOptions.MissBehavior)} cannot be {nameof(HttpRecordingMissBehavior.Passthrough)} when no inner handler is provided, because there is nothing to forward the request to. Use the constructor that takes an inner handler.", nameof(options));
            }
        }

        var matcher = resolvedOptions.RequestMatcher ?? DefaultHttpRequestMatcher.Instance;
        _session = new RecordingSession(matcher);
    }

    /// <summary>Loads existing recordings from the store. If not called explicitly, the first <see cref="SendAsync"/> call will call it automatically.</summary>
    /// <remarks>Nothing is loaded in <see cref="HttpRecordingMode.Record"/> mode: saving there replaces the store's content instead of appending to it.</remarks>
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

            if (_mode is not HttpRecordingMode.Record)
            {
                var entries = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
                _session.LoadEntries(entries);
            }

            _initialized = true;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <summary>Saves all recorded entries to the store, waiting for any in-flight recording to complete first.</summary>
    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return SaveCoreAsync(cancellationToken);
    }

    private async Task SaveCoreAsync(CancellationToken cancellationToken)
    {
        // A request that is still being recorded would otherwise be missing from the snapshot, and the store has
        // already replaced the previous content by the time it completes.
        await WaitForPendingRecordingsAsync().WaitAsync(cancellationToken).ConfigureAwait(false);

        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = _session.GetEntriesToPersist();
            await _store.SaveAsync(entries, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        // Loading the store is local I/O that has nothing to do with the remote endpoint. Billing it to the request's
        // token would surface a slow or large recording file as an HttpClient.Timeout against a server never contacted.
        await InitializeAsync(CancellationToken.None).ConfigureAwait(false);

        // Buffer the request body up front so it can be fingerprinted and recorded. Reading it after the inner handler
        // has written it to the wire fails for any content that can only be consumed once.
        if (request.Content is not null)
        {
            await request.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
        }

        return _mode switch
        {
            HttpRecordingMode.Record => await RecordAsync(request, cancellationToken).ConfigureAwait(false),
            HttpRecordingMode.Replay => await ReplayAsync(request, cancellationToken).ConfigureAwait(false),
            HttpRecordingMode.Auto => await AutoAsync(request, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unknown recording mode: {_mode}"),
        };
    }

    private async Task<HttpResponseMessage> RecordAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Counted for the whole exchange, not just the part after the response arrives, so that SaveAsync cannot
        // snapshot the session while this request is still on its way to being recorded.
        BeginRecording();
        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            try
            {
                await RecordEntryAsync(request, response, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // The response never reaches the caller, so nobody else can dispose it. Leaving it alive would hold its
                // pooled connection until finalization.
                response.Dispose();
                throw;
            }

            return response;
        }
        finally
        {
            EndRecording();
        }
    }

    private async Task<HttpResponseMessage> ReplayAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var requestEntry = await CreateLookupEntryAsync(request, cancellationToken).ConfigureAwait(false);

        if (_session.TryGetRecordedResponse(requestEntry, out var match) && match is not null)
        {
            return HttpMessageConverter.ToHttpResponseMessage(match, request);
        }

        if (_missBehavior is HttpRecordingMissBehavior.Passthrough)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        return HandleMiss(request, requestEntry);
    }

    private async Task<HttpResponseMessage> AutoAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var requestEntry = await CreateLookupEntryAsync(request, cancellationToken).ConfigureAwait(false);

        if (_session.TryGetRecordedResponse(requestEntry, out var match) && match is not null)
        {
            return HttpMessageConverter.ToHttpResponseMessage(match, request);
        }

        if (_missBehavior is not HttpRecordingMissBehavior.Passthrough)
        {
            return HandleMiss(request, requestEntry);
        }

        // No match — record the real call
        return await RecordAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpRecordingEntry> CreateLookupEntryAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var entry = await HttpMessageConverter.CreateFromRequestAsync(request, cancellationToken).ConfigureAwait(false);

        // Stored entries were sanitized before being persisted, so the lookup entry must go through the same
        // transformation or a sanitized field the matcher reads could never match again.
        Sanitize(entry);
        return entry;
    }

    private async Task RecordEntryAsync(HttpRequestMessage request, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // Buffering here is what keeps the response the caller receives readable after we have consumed it.
        await response.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);

        var entry = await HttpMessageConverter.CreateFromRequestResponseAsync(request, response, cancellationToken).ConfigureAwait(false);
        Sanitize(entry);
        _session.AddRecordedEntry(entry);
    }

    private void Sanitize(HttpRecordingEntry entry)
    {
        foreach (var sanitizer in _sanitizers)
        {
            sanitizer.Sanitize(entry);
        }
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The response is returned to the caller, which owns it.")]
    private HttpResponseMessage HandleMiss(HttpRequestMessage request, HttpRecordingEntry requestEntry)
    {
        return _missBehavior switch
        {
            HttpRecordingMissBehavior.Throw => throw new HttpRecordingMissException(requestEntry.Method, requestEntry.RequestUri),
            HttpRecordingMissBehavior.ReturnDefault => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(
                    $"No recorded response found for {requestEntry.Method} {HttpRecordingUri.Redact(requestEntry.RequestUri)}.",
                    Encoding.UTF8,
                    "text/plain"),
                RequestMessage = request,
            },
            _ => throw new InvalidOperationException($"Unknown miss behavior: {_missBehavior}"),
        };
    }

    private void BeginRecording()
    {
        lock (_pendingLock)
        {
            _pendingRecordings++;
        }
    }

    private void EndRecording()
    {
        TaskCompletionSource? drained = null;
        lock (_pendingLock)
        {
            if (--_pendingRecordings is 0)
            {
                drained = _recordingsDrained;
                _recordingsDrained = null;
            }
        }

        drained?.TrySetResult();
    }

    private Task WaitForPendingRecordingsAsync()
    {
        lock (_pendingLock)
        {
            if (_pendingRecordings is 0)
            {
                return Task.CompletedTask;
            }

            _recordingsDrained ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return _recordingsDrained.Task;
        }
    }

    /// <summary>Saves the recordings when <see cref="HttpRecordingOptions.AutoSave"/> is enabled, then disposes the handler.</summary>
    /// <remarks>
    /// The save is not skipped when the handler has already been disposed synchronously: an <see cref="HttpClient"/>
    /// owns its handler by default and disposes it first, so by the time this runs the handler is usually disposed
    /// already. The recorded entries are still in memory and are what needs persisting.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (_autoSave && !_autoSaved)
        {
            _autoSaved = true;
            await SaveCoreAsync(CancellationToken.None).ConfigureAwait(false);
        }

        Dispose();
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;

            // _ioLock is deliberately not disposed. A request that is still in flight may be about to release it, and
            // SemaphoreSlim.Dispose racing Release throws ObjectDisposedException from a finally block, which replaces
            // the exception the caller actually needs to see. A SemaphoreSlim that never handed out a wait handle holds
            // no unmanaged resource.
        }

        base.Dispose(disposing);
    }
}
