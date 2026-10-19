using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Media;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

// Loading the media resource (https://html.spec.whatwg.org/multipage/media.html#loading-the-media-resource)
public partial class HtmlMediaElement
{
    /// <summary>https://html.spec.whatwg.org/multipage/media.html#media-element-stall-timeout: about three seconds.</summary>
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(3);

    /// <summary>The shortest time between two <c>progress</c> events while the media data is received (the specification requires 350ms ±200ms).</summary>
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(350);

    private bool _delayingTheLoadEvent;
    private Document? _delayedDocument;
    private bool _canAutoplay = true;
    private bool _isCurrentlyStalled;
    private bool _showPoster = true;

    /// <summary>The node document of the element when its src attribute was last changed: the base of its URL.</summary>
    private Document? _srcDocument;

    /// <summary>The instance of the resource selection algorithm: its steps do nothing once another instance is invoked, or the algorithm is aborted.</summary>
    private int _selectionGeneration;

    // The pointer of the resource selection algorithm in the children mode: a position between two adjacent children, where
    // null is the start of the list (before) or the end of the list (after)
    private bool _hasSourcePointer;
    private Node? _pointerBefore;
    private Node? _pointerAfter;

    /// <summary>Whether the resource selection algorithm waits for a node after the pointer (a new <c>source</c> element).</summary>
    private bool _waitingForSource;

    private CancellationTokenSource? _fetchCancellation;
    private int _stallGeneration;

    /// <summary>The rest of the resource fetch algorithm when the fetch waits for the playback to be requested (<c>preload="none"</c>), or null.</summary>
    private Action? _deferredFetch;

    /// <summary>Whether the lazy load resumption steps of the element ran: the element "has started loading while in the Lazy state".</summary>
    private bool _lazyLoadStarted;

    /// <summary>Whether the loadstart event of the resource selection algorithm waits for the lazy load resumption steps.</summary>
    private bool _loadStartDeferred;

    /// <summary>Whether the element is currently stalled (https://html.spec.whatwg.org/multipage/media.html#is-currently-stalled), for <c>:stalled</c>.</summary>
    internal bool IsCurrentlyStalled => _isCurrentlyStalled;

    /// <summary>
    /// Whether the element matches <c>:buffering</c> (https://html.spec.whatwg.org/multipage/semantics-other.html#selector-buffering):
    /// it is not paused, its network state is <c>NETWORK_LOADING</c> and its ready state is <c>HAVE_CURRENT_DATA</c> or less.
    /// </summary>
    internal bool IsBuffering => !_paused && _networkState == NetworkLoading && _readyState <= HaveCurrentData;

    /// <summary>The show poster flag (https://html.spec.whatwg.org/multipage/media.html#show-poster-flag): a video element shows its poster frame instead of the video.</summary>
    internal bool ShowPoster => _showPoster;

    private void SetShowPoster(bool value)
    {
        if (_showPoster == value)
            return;

        _showPoster = value;
        OnRenderingChanged();
    }

    /// <summary>What the element represents changed (the poster or the frame of a video, its natural dimensions).</summary>
    private protected virtual void OnRenderingChanged()
    {
    }

    /// <summary>https://html.spec.whatwg.org/multipage/media.html#dom-media-load</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/media.html#dom-media-load"/>
    [IdlMember("load", "https://html.spec.whatwg.org/multipage/media.html#dom-media-load")]
    public void Load()
    {
        if (Backend is null)
        {
            // There is no media resource to select or fetch
            _defaultPlaybackStartPosition = 0;
            _playbackRate = _defaultPlaybackRate;
            NodeDocument.IncrementElementStateVersion();
            return;
        }

        RunLazyLoadResumptionSteps();
        RunLoadAlgorithm();
    }

    /// <summary>Runs the lazy load resumption steps of the element, if any (<c>load()</c> and <c>play()</c> start the fetch of a lazy element).</summary>
    private void RunLazyLoadResumptionSteps()
    {
        if (RareDataOrNull is not ElementRareData { LazyLoadResumptionSteps: { } resumptionSteps } rareData)
            return;

        rareData.LazyLoadResumptionSteps = null;
        LazyLoading.StopIntersectionObserving(this);
        resumptionSteps();
    }

    /// <summary>https://html.spec.whatwg.org/multipage/media.html#media-element-load-algorithm</summary>
    private void RunLoadAlgorithm()
    {
        _isCurrentlyStalled = false;
        AbortResourceSelection();
        RemovePendingMediaTasks();

        // The attached media source is detached before the element transitions to NETWORK_EMPTY
        DetachMediaSource();
        if (_networkState is NetworkLoading or NetworkIdle)
        {
            QueueMediaEvent("abort");
        }

        if (_networkState != NetworkEmpty)
        {
            QueueMediaEvent("emptied");
            ForgetMediaResourceSpecificTracks();

            // The official playback position is the position the playback reached
            var position = OfficialPlaybackPosition;
            ReleasePlayer();
            _readyState = HaveNothing;
            ClearActiveCues();
            if (!_paused)
            {
                _paused = true;
                RejectPendingPlayPromises(TakePendingPlayPromises(), "AbortError");
            }

            _seeking = false;
            _seekGeneration++;
            _currentPlaybackPosition = 0;
            _officialPlaybackPosition = 0;
            if (position != 0)
            {
                QueueMediaEvent("timeupdate");
            }

            // The duration is reset without a durationchange event
            _duration = double.NaN;
            _playedRanges = null;
            OnRenderingChanged();
        }

        if (_playbackRate != _defaultPlaybackRate)
        {
            SetPlaybackRate(_defaultPlaybackRate);
            QueueRateChange();
        }

        _error = null;
        _canAutoplay = true;
        NodeDocument.IncrementElementStateVersion();
        InvokeResourceSelection();
    }

    /// <summary>Aborts the running instance of the resource selection algorithm, and its fetch.</summary>
    private void AbortResourceSelection()
    {
        _selectionGeneration++;
        _hasSourcePointer = false;
        _pointerBefore = null;
        _pointerAfter = null;
        _waitingForSource = false;
        _deferredFetch = null;
        _stallGeneration++;
        LazyLoading.ClearResumptionSteps(this);
        if (_fetchCancellation is { } cancellation)
        {
            _fetchCancellation = null;
            cancellation.Cancel();
            cancellation.Dispose();
        }
    }

    /// <summary>The player of the previous media resource is released: its playback stops.</summary>
    private void ReleasePlayer()
    {
        _playbackTimerGeneration++;
        _isAdvancing = false;
        if (_player is { } player)
        {
            _player = null;
            player.Dispose();
        }

        if (_playerDocument is { } document)
        {
            _playerDocument = null;
            document.MediaElementsWithPlayer?.Remove(this);
        }
    }

    /// <summary>The document whose destruction releases the player of the element: the document that fetched the media resource.</summary>
    private Document? _playerDocument;

    /// <summary>
    /// The document of the element is destroyed (it is no longer fully active, e.g. its navigable navigates to another
    /// document or its <c>iframe</c> is removed): the fetch of the media resource is aborted and its player is released at
    /// once, so the playback stops (no event is fired).
    /// </summary>
    internal void OnDocumentDestroyed()
    {
        AbortResourceSelection();
        RemovePendingMediaTasks();
        DetachMediaSource();
        ReleasePlayer();
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The delaying-the-load-event flag
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>While the delaying-the-load-event flag is true, the element delays the load event of its document.</summary>
    private void SetDelayingTheLoadEvent(bool value)
    {
        if (_delayingTheLoadEvent == value)
            return;

        _delayingTheLoadEvent = value;
        if (value)
        {
            _delayedDocument = NodeDocument;
            _delayedDocument.EmbeddedContentDelayingTheLoadEvent++;
            return;
        }

        var document = _delayedDocument ?? NodeDocument;
        _delayedDocument = null;
        document.EmbeddedContentDelayingTheLoadEvent--;
        document.TryQueueLoadEvent();
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Resource selection algorithm (https://html.spec.whatwg.org/multipage/media.html#concept-media-load-algorithm)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>A <c>source</c> element was inserted in the element: the resource selection algorithm is invoked when the element has no src attribute and its network state is <c>NETWORK_EMPTY</c>.</summary>
    internal void OnSourceElementInserted()
    {
        if (_networkState == NetworkEmpty && !HasAttributeWithLocalName("src") && Backend is not null)
        {
            InvokeResourceSelection();
        }
    }

    private void InvokeResourceSelection()
    {
        var generation = ++_selectionGeneration;
        SetNetworkState(NetworkNoSource);
        SetShowPoster(true);

        // The synchronous section: the rest of the algorithm until it fetches a resource
        var awaited = AwaitStableState(() =>
        {
            if (generation == _selectionGeneration)
            {
                SelectResource(generation);
            }
        });

        if (!awaited)
        {
            // A document without event loop: nothing is selected
            SetNetworkState(NetworkEmpty);
        }
    }

    private void SelectResource(int generation)
    {
        var document = NodeDocument;
        SetDelayingTheLoadEvent(!LazyLoading.IsLazy(GetAttributeValue("loading")) || !document.ScriptingEnabled);
        if (!_blockedOnParser)
        {
            PopulatePendingTextTracks();
        }

        HtmlSourceElement? candidate = null;
        var provider = _srcObject;
        var hasSrc = HasAttributeWithLocalName("src");
        if (provider is null && !hasSrc)
        {
            for (var child = FirstChild; child is not null; child = child.NextSibling)
            {
                if (child is HtmlSourceElement source)
                {
                    candidate = source;
                    break;
                }
            }

            if (candidate is null)
            {
                SetNetworkState(NetworkEmpty);
                SetDelayingTheLoadEvent(false);
                return;
            }
        }

        // The loadstart event of an element whose fetch is deferred by the lazy loading attribute is fired when the fetch
        // starts: as in the browsers (and the tests), a lazy element fires no event until it is about to be visible
        SetNetworkState(NetworkLoading);
        _loadStartDeferred = LazyLoading.WillLazyLoad(this);
        if (!_loadStartDeferred)
        {
            QueueMediaEvent("loadstart");
        }

        if (provider is not null)
        {
            // Mode object
            _currentSrc = "";
            FetchResource(generation, url: null, provider, FailWithMediaProviderOrAttribute);
        }
        else if (hasSrc)
        {
            // Mode attribute
            var src = GetAttributeValue("src")!;
            if (src.Length == 0)
            {
                FailWithMediaProviderOrAttribute();
                return;
            }

            var url = Reflection.EncodingParseUrl(_srcDocument ?? document, src);
            if (url is null)
            {
                FailWithMediaProviderOrAttribute();
                return;
            }

            // A blob: URL of a media source (resolved when the attribute was set) is a media provider object
            _currentSrc = url.Serialize();
            FetchResource(generation, url, provider: _srcMediaSource, FailWithMediaProviderOrAttribute);
        }
        else
        {
            // Mode children: the pointer is between the candidate and the next node
            _hasSourcePointer = true;
            _pointerBefore = candidate;
            _pointerAfter = candidate!.NextSibling;
            ProcessCandidate(generation, candidate);
        }
    }

    /// <summary>
    /// The "failed with media provider" and "failed with attribute" steps: the dedicated media source failure steps
    /// (https://html.spec.whatwg.org/multipage/media.html#dedicated-media-source-failure-steps), and the algorithm ends.
    /// The specification runs all the steps in a media element task; as in the browsers, the state of the element changes
    /// at once (the error attribute, the network state), and the task only fires the <c>error</c> event and rejects the
    /// pending play promises (whatwg/html#11155, WPT error-sequence.html).
    /// </summary>
    private void FailWithMediaProviderOrAttribute()
    {
        var promises = TakePendingPlayPromises();
        _selectionGeneration++;
        DetachMediaSource();
        _error = new MediaError(MediaError.MediaErrSrcNotSupported, "The media resource is not suitable");
        ForgetMediaResourceSpecificTracks();
        SetNetworkState(NetworkNoSource);
        SetShowPoster(true);
        QueueMediaElementTask(
            () =>
            {
                FireEvent(new Event("error"));
                RejectPendingPlayPromises(promises, "NotSupportedError");
                SetDelayingTheLoadEvent(false);
            },
            () => RejectPendingPlayPromises(promises, "NotSupportedError"));
    }

    /// <summary>The "process candidate" steps of the children mode.</summary>
    private void ProcessCandidate(int generation, HtmlSourceElement candidate)
    {
        var src = candidate.GetAttributeValue("src");
        if (string.IsNullOrEmpty(src)
            || (candidate.GetAttributeValue("media") is { } media && !MediaQueryEvaluator.Matches(media, StyleEngine.CreateEnvironment(NodeDocument)))
            || Reflection.EncodingParseUrl(candidate.NodeDocument, src) is not { } url
            || (candidate.GetAttributeValue("type") is { } type && IsTypeKnownToBeUnsupported(type)))
        {
            FailWithElements(generation, candidate);
            return;
        }

        _currentSrc = url.Serialize();
        FetchResource(generation, url, provider: ResolveMediaSourceUrl(url), () => FailWithElements(generation, candidate));
    }

    /// <summary>The "failed with elements" steps: an <c>error</c> event is fired at the candidate, and the next candidate is searched at the next stable state.</summary>
    private void FailWithElements(int generation, HtmlSourceElement candidate)
    {
        DetachMediaSource();
        QueueMediaElementTask(() => candidate.FireEvent(new Event("error")));
        AwaitStableState(() =>
        {
            if (generation != _selectionGeneration)
                return;

            ForgetMediaResourceSpecificTracks();
            FindNextCandidate(generation);
        });
    }

    /// <summary>The "find next candidate" steps: the pointer advances to the next <c>source</c> element, or the algorithm waits for one.</summary>
    private void FindNextCandidate(int generation)
    {
        HtmlSourceElement? candidate = null;
        while (candidate is null)
        {
            var after = _pointerAfter;
            if (after is null)
            {
                // Waiting
                SetNetworkState(NetworkNoSource);
                SetShowPoster(true);
                QueueMediaElementTask(() => SetDelayingTheLoadEvent(false));
                _waitingForSource = true;
                return;
            }

            candidate = after as HtmlSourceElement;
            _pointerBefore = after;
            _pointerAfter = after.NextSibling;
        }

        ProcessCandidate(generation, candidate);
    }

    /// <summary>
    /// Updates the pointer of the resource selection algorithm after a child was inserted, removed or moved: an insertion at
    /// the pointer goes after the pointer, and the pointer does not move relative to the remaining nodes when one of its
    /// nodes is removed. The children are removed one by one, so at most one node of the pointer was removed.
    /// </summary>
    internal void UpdateSourcePointer()
    {
        if (!_hasSourcePointer)
            return;

        if (_pointerBefore is { } before && !ReferenceEquals(before.ParentNode, this))
        {
            _pointerBefore = _pointerAfter is { } after ? after.PreviousSibling : LastChild;
        }
        else
        {
            // The node after the pointer was removed, or a node was inserted between the two nodes of the pointer (it is the
            // node after the pointer): the node after the pointer is the one that follows the node before the pointer
            _pointerAfter = _pointerBefore is { } remaining ? remaining.NextSibling : FirstChild;
        }

        if (_waitingForSource && _pointerAfter is not null)
        {
            // The node after the pointer is no longer the end of the list
            _waitingForSource = false;
            var generation = _selectionGeneration;
            AwaitStableState(() =>
            {
                if (generation != _selectionGeneration)
                    return;

                SetDelayingTheLoadEvent(true);
                SetNetworkState(NetworkLoading);
                FindNextCandidate(generation);
            });
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Resource fetch algorithm (https://html.spec.whatwg.org/multipage/media.html#concept-media-load-resource)
    // ----------------------------------------------------------------------------------------------------------------

    private void FetchResource(int generation, UrlRecord? url, object? provider, Action onFailure)
    {
        // The lazy loading attribute defers the fetch until the element intersects the viewport (or load() or play() is called)
        if (LazyLoading.WillLazyLoad(this))
        {
            LazyLoading.Defer(this, () =>
            {
                _lazyLoadStarted = true;
                if (generation == _selectionGeneration)
                {
                    QueueDeferredLoadStart();
                    StartFetch(generation, url, provider, onFailure);
                }
            });
            return;
        }

        QueueDeferredLoadStart();
        StartFetch(generation, url, provider, onFailure);
    }

    /// <summary>Fires the loadstart event that the lazy loading attribute deferred, if any.</summary>
    private void QueueDeferredLoadStart()
    {
        if (_loadStartDeferred)
        {
            _loadStartDeferred = false;
            QueueMediaEvent("loadstart");
        }
    }

    private void StartFetch(int generation, UrlRecord? url, object? provider, Action onFailure)
    {
        // Local mode: a media source (or its handle, for a source constructed in a dedicated worker) is attached to the
        // element (https://w3c.github.io/media-source/#mediasource-attach); a handle that was transferred cannot be used
        if (provider is MediaSource or MediaSourceHandle)
        {
            if (provider is MediaSourceHandle { IsDetached: true })
            {
                QueueMediaElementTask(() =>
                {
                    if (generation == _selectionGeneration)
                    {
                        onFailure();
                    }
                });
                return;
            }

            AttachMediaSource(generation, provider as MediaSource ?? ((MediaSourceHandle)provider).Source, byHandle: provider is MediaSourceHandle, onFailure);
            return;
        }

        if (provider is Blob blob)
        {
            // Local mode: the media provider object contains the media data
            var type = blob.Type.Length == 0 ? null : blob.Type;
            var data = new MediaData(requestUrl: null, onFailure) { ContentType = type, IsComplete = true };
            data.Write(0, blob.Data.Span);
            QueueMediaElementTask(() => ProcessMediaData(generation, data));
            return;
        }

        // The user agent does not fetch the resource until the user requests it (preload="none"): the element is suspended
        if (IsFetchDeferredByPreload())
        {
            SetNetworkState(NetworkIdle);
            QueueMediaEvent("suspend");
            QueueMediaElementTask(() => SetDelayingTheLoadEvent(false));
            _deferredFetch = () =>
            {
                SetDelayingTheLoadEvent(true);
                SetNetworkState(NetworkLoading);
                FetchRemoteResource(generation, url!, onFailure);
            };
            return;
        }

        FetchRemoteResource(generation, url!, onFailure);
    }

    /// <summary>
    /// Whether the fetch of the resource waits until the playback is requested: the <c>preload</c> attribute is in the None
    /// state, unless the <c>autoplay</c> attribute overrides it.
    /// </summary>
    private bool IsFetchDeferredByPreload()
        => GetAttributeValue("preload") is { } preload && InfraAscii.EqualsIgnoreAsciiCase(preload, "none") && !HasAttributeWithLocalName("autoplay");

    /// <summary>Starts the fetch that <c>preload="none"</c> deferred (the playback is requested, or the hint changed).</summary>
    private void ResumeDeferredFetch()
    {
        if (_deferredFetch is not { } fetch)
            return;

        _deferredFetch = null;
        fetch();
    }

    private void FetchRemoteResource(int generation, UrlRecord url, Action onFailure)
    {
        var document = NodeDocument;
        var cancellation = new CancellationTokenSource();
        _fetchCancellation = cancellation;
        var data = new MediaData(url, onFailure);

        // The media resource of a media document is the response of the navigation: it is not fetched again
        if (document.TakeMediaDocumentResource(url) is { } navigationResponse)
        {
            QueueMediaElementTask(() => OnResponse(generation, cancellation, data, navigationResponse));
            return;
        }

        FetchRange(generation, cancellation, data);
    }

    /// <summary>
    /// Fetches the media resource from the first byte that is missing: as the browsers do, the request has a <c>Range</c>
    /// header (<c>bytes=0-</c> for the first request), so a server can answer with a part of the resource (a <c>206</c>
    /// response); the rest is then requested the same way.
    /// </summary>
    private void FetchRange(int generation, CancellationTokenSource cancellation, MediaData data)
    {
        // The request is created for the document of the element; a document without browsing context (e.g.
        // createHTMLDocument()) uses the environment of the document that created it
        var document = NodeDocument;
        var client = document.BrowsingContext is null && document.OriginDocument is { } origin ? origin : document;
        var destination = this is HtmlAudioElement ? RequestDestination.Audio : RequestDestination.Video;
        var request = SubresourceFetching.CreateRequest(client, data.RequestUrl!, destination, cors: HasAttributeWithLocalName("crossorigin"), referrerPolicy: "", this, useCredentials: SubresourceFetching.UsesCredentials(this));
        request.HeaderList.Add(new("Range", string.Create(System.Globalization.CultureInfo.InvariantCulture, $"bytes={data.Length}-")));

        // The media data is given to the player as it arrives
        request.StreamsBody = true;
        var started = SubresourceFetching.Fetch(client, request, resource => OnResponse(generation, cancellation, data, resource), initiatorType: this is HtmlAudioElement ? "audio" : "video", cancellation.Token);
        if (!started)
        {
            // Nothing can be fetched (there is no loader): the media data cannot be fetched at all
            QueueMediaElementTask(() => OnResponse(generation, cancellation, data, resource: null));
            return;
        }

        StartStallTimer(generation);
    }

    /// <summary>
    /// The element is actively attempting to obtain media data: when it has received no data for the media element stall
    /// timeout, a <c>stalled</c> event is fired.
    /// </summary>
    private void StartStallTimer(int generation)
    {
        if (NodeDocument.EventLoop is not { } eventLoop)
            return;

        var stallGeneration = ++_stallGeneration;
        eventLoop.QueueTimer(StallTimeout, () =>
        {
            if (stallGeneration != _stallGeneration || generation != _selectionGeneration || _networkState != NetworkLoading)
                return;

            QueueMediaElementTask(() =>
            {
                _isCurrentlyStalled = true;
                NodeDocument.IncrementElementStateVersion();
                FireEvent(new Event("stalled"));
            });
        });
    }

    /// <summary>The media data of the resource that is being fetched: the bytes received so far, and the player once the backend could read the metadata.</summary>
    private sealed class MediaData(UrlRecord? requestUrl, Action onFailure)
    {
        private byte[] _bytes = [];

        /// <summary>The URL of the requests, or null for a media provider object.</summary>
        public UrlRecord? RequestUrl { get; } = requestUrl;

        public Action OnFailure { get; } = onFailure;

        /// <summary>The URL and the Content-Type of the first response.</summary>
        public string Url { get; set; } = "";

        public string? ContentType { get; set; }

        /// <summary>Whether all the responses are CORS-same-origin.</summary>
        public bool IsCorsSameOrigin { get; set; } = true;

        /// <summary>
        /// The origin of the resource (https://html.spec.whatwg.org/multipage/media.html#verify-a-media-response): null
        /// ("none"), the serialized origin of the URLs of its partial responses, "rewritten" or "multiple".
        /// </summary>
        public string? Origin { get; set; }

        public int Length { get; private set; }

        /// <summary>The bytes received so far, from the first byte of the resource.</summary>
        public ReadOnlyMemory<byte> Content => _bytes.AsMemory(0, Length);

        public bool IsComplete { get; set; }

        /// <summary>Whether the resource is complete at the end of the body of the current response (else the next range is requested).</summary>
        public bool IsLastResponse { get; set; } = true;

        public MediaPlayer? Player { get; set; }

        public long LastProgressTimestamp { get; set; }

        /// <summary>Writes bytes of the resource at an offset that is not after the bytes received so far.</summary>
        public void Write(int offset, ReadOnlySpan<byte> bytes)
        {
            var end = offset + bytes.Length;
            if (end > _bytes.Length)
            {
                // The buffer is replaced: the memory given to the player stays valid
                var buffer = new byte[Math.Max(end, _bytes.Length * 2)];
                _bytes.AsSpan(0, Length).CopyTo(buffer);
                _bytes = buffer;
            }
            else if (offset < Length)
            {
                // The bytes that are already given to the player are not modified in place
                _bytes = _bytes.AsSpan().ToArray();
            }

            bytes.CopyTo(_bytes.AsSpan(offset));
            Length = Math.Max(Length, end);
        }

        public void Reset() => Length = 0;
    }

    private void OnResponse(int generation, CancellationTokenSource cancellation, MediaData data, DocumentResource? resource)
    {
        if (generation != _selectionGeneration || !ReferenceEquals(_fetchCancellation, cancellation))
        {
            ReleaseFetchCancellation(cancellation);
            return;
        }

        _stallGeneration++;
        if (resource is null || !VerifyMediaResponse(resource, data, out var range))
        {
            // The media data cannot be fetched at all (a network error, an HTTP error), or the connection is interrupted
            ReleaseFetchCancellation(cancellation);
            OnConnectionInterrupted(generation, data);
            return;
        }

        var bytes = resource.ContentBytes ?? System.Text.Encoding.UTF8.GetBytes(resource.Content ?? "");
        if (data.Length == 0 && data.Player is null)
        {
            data.Url = resource.Url ?? _currentSrc;
            data.ContentType = resource.Headers.FirstOrDefault(static header => header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)).Value;
        }

        data.IsCorsSameOrigin &= resource.IsCorsSameOrigin;
        if (range is var (first, last, total))
        {
            // A partial response: its bytes are the ones of its Content-Range (they must follow the bytes received so far);
            // the rest of the resource is requested once its body is received
            if (first > data.Length || first > int.MaxValue - bytes.Length)
            {
                ReleaseFetchCancellation(cancellation);
                OnConnectionInterrupted(generation, data);
                return;
            }

            var previousLength = data.Length;
            data.Write((int)first, bytes.Span);
            data.IsLastResponse = total is null || last + 1 >= total || data.Length <= previousLength || bytes.Length == 0;
        }
        else
        {
            // The entire resource
            data.Reset();
            data.Write(0, bytes.Span);
            data.IsLastResponse = true;
        }

        if (resource.RemainingContentBytes is { } remaining && NodeDocument.EventLoop is { } eventLoop)
        {
            // A response that arrives over time: the media data is processed as its chunks arrive
            QueueMediaElementTask(() => ProcessMediaData(generation, data));
            StartStallTimer(generation);
            ReceiveNextChunk(generation, cancellation, data, eventLoop, remaining.GetAsyncEnumerator(cancellation.Token));
            return;
        }

        QueueMediaElementTask(() => OnResponseBodyReceived(generation, cancellation, data));
    }

    /// <summary>The body of a response is received: the resource is complete, or its next range is requested.</summary>
    private void OnResponseBodyReceived(int generation, CancellationTokenSource cancellation, MediaData data)
    {
        if (generation != _selectionGeneration || !ReferenceEquals(_fetchCancellation, cancellation))
        {
            ReleaseFetchCancellation(cancellation);
            return;
        }

        if (data.IsLastResponse)
        {
            ReleaseFetchCancellation(cancellation);
            data.IsComplete = true;
            ProcessMediaData(generation, data);
            return;
        }

        ProcessMediaData(generation, data);
        if (generation == _selectionGeneration && ReferenceEquals(_fetchCancellation, cancellation))
        {
            FetchRange(generation, cancellation, data);
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/media.html#verify-a-media-response for a request of the rest of the resource
    /// from an offset: a <c>200</c> response is the entire resource; a <c>206</c> response is a part of the resource given by its
    /// <c>Content-Range</c> (<paramref name="range"/>), which cannot be patched together with the responses of other
    /// origins when it is opaque.
    /// </summary>
    private static bool VerifyMediaResponse(DocumentResource resource, MediaData data, out (long First, long Last, long? Total)? range)
    {
        range = null;
        if (resource.StatusCode == 200)
            return true;

        if (resource.StatusCode != 206 || HeaderList.Get(resource.Headers, "Content-Range") is not { } contentRange || ParseContentRange(contentRange) is not { } values)
            return false;

        range = values;
        var origin = resource.Url is null || UrlParser.Parse(resource.Url, baseUrl: null) is not { } url ? "rewritten" : OriginRecord.FromUrl(url).Serialize();
        if (data.Origin is null || data.Origin == origin)
        {
            data.Origin = origin;
        }
        else if (!resource.IsCorsSameOrigin || !data.IsCorsSameOrigin)
        {
            // The responses of two origins cannot be patched together when one of them is opaque (the response, or a
            // previous response of the resource)
            return false;
        }
        else
        {
            data.Origin = "multiple";
        }

        return true;
    }

    /// <summary>https://fetch.spec.whatwg.org/#extract-content-range-values: <c>bytes first-last/complete</c>, where the complete length can be <c>*</c>.</summary>
    private static (long First, long Last, long? Total)? ParseContentRange(string value)
    {
        const string Unit = "bytes ";
        if (!value.StartsWith(Unit, StringComparison.Ordinal))
            return null;

        var dash = value.IndexOf('-', StringComparison.Ordinal);
        var slash = value.IndexOf('/', StringComparison.Ordinal);
        if (dash < 0 || slash < dash
            || !long.TryParse(value.AsSpan(Unit.Length, dash - Unit.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var first)
            || !long.TryParse(value.AsSpan(dash + 1, slash - dash - 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var last))
        {
            return null;
        }

        var complete = value.AsSpan(slash + 1);
        if (complete is "*")
            return (first, last, null);

        return long.TryParse(complete, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var total) ? (first, last, total) : null;
    }

    private void ReleaseFetchCancellation(CancellationTokenSource cancellation)
    {
        if (ReferenceEquals(_fetchCancellation, cancellation))
        {
            _fetchCancellation = null;
        }

        cancellation.Dispose();
    }

    /// <summary>Waits for the next chunk of a response that arrives over time, and processes it from a task of the event loop.</summary>
    private void ReceiveNextChunk(int generation, CancellationTokenSource cancellation, MediaData data, DomEventLoop eventLoop, IAsyncEnumerator<ReadOnlyMemory<byte>> chunks)
    {
        ValueTask<bool> next;
        try
        {
            next = chunks.MoveNextAsync();
        }
#pragma warning disable CA1031 // An exception of the response is an interrupted connection
        catch (Exception ex)
#pragma warning restore CA1031
        {
            next = ValueTask.FromException<bool>(ex);
        }

        eventLoop.BeginOperation();
        _ = next.AsTask().ContinueWith(
            completed =>
            {
                eventLoop.QueueTask(() =>
                {
                    // The fetch was aborted (e.g. the element loads another resource): the rest of the response is ignored
                    if (generation != _selectionGeneration || !ReferenceEquals(_fetchCancellation, cancellation))
                    {
                        _ = chunks.DisposeAsync().AsTask();
                        return;
                    }

                    if (completed.IsCompletedSuccessfully && completed.Result)
                    {
                        data.Write(data.Length, chunks.Current.Span);
                        ProcessMediaData(generation, data);
                        ReceiveNextChunk(generation, cancellation, data, eventLoop, chunks);
                        return;
                    }

                    _ = chunks.DisposeAsync().AsTask();
                    if (completed.IsCompletedSuccessfully)
                    {
                        OnResponseBodyReceived(generation, cancellation, data);
                    }
                    else
                    {
                        ReleaseFetchCancellation(cancellation);
                        OnConnectionInterrupted(generation, data);
                    }
                });
                eventLoop.EndOperation();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// The connection is interrupted: before the resource is established to be usable, the media data cannot be fetched at
    /// all; afterwards, it is a fatal network error (<c>MEDIA_ERR_NETWORK</c>).
    /// </summary>
    private void OnConnectionInterrupted(int generation, MediaData data)
    {
        _stallGeneration++;
        if (data.Player is null || !ReferenceEquals(_player, data.Player))
        {
            data.OnFailure();
            return;
        }

        _error = new MediaError(MediaError.MediaErrNetwork, "The connection was interrupted");
        if (generation == _selectionGeneration)
        {
            _selectionGeneration++;
        }

        SetNetworkState(NetworkIdle);
        SetDelayingTheLoadEvent(false);
        FireEvent(new Event("error"));
        UpdatePlayback();
    }

    /// <summary>Whether the media data is CORS-same-origin: a canvas is tainted when a CORS-cross-origin video is drawn on it.</summary>
    internal bool IsMediaDataCorsSameOrigin { get; private set; } = true;

    /// <summary>
    /// The media data processing steps (https://html.spec.whatwg.org/multipage/media.html#media-data-processing-steps-list),
    /// run when the response arrives and each time more bytes of a response that arrives over time are received: the media
    /// backend opens the resource; once its metadata is known, the element has the tracks, the duration and the dimensions of
    /// the resource, then the data of the current playback position. Once the entire resource is fetched, the element is
    /// suspended.
    /// </summary>
    private void ProcessMediaData(int generation, MediaData data)
    {
        if (generation != _selectionGeneration || Backend is not { } backend || NodeDocument.EventLoop is not { } eventLoop)
            return;

        if (!data.IsComplete)
        {
            // Media data was received: the element is no longer stalled, and a progress event is fired every 350ms (±200ms)
            // or for every byte received, whichever is least frequent
            if (_isCurrentlyStalled)
            {
                _isCurrentlyStalled = false;
                NodeDocument.IncrementElementStateVersion();
            }

            StartStallTimer(generation);
            var now = eventLoop.TimeProvider.GetTimestamp();
            if (data.Player is not null && eventLoop.TimeProvider.GetElapsedTime(data.LastProgressTimestamp, now) >= ProgressInterval)
            {
                data.LastProgressTimestamp = now;
                QueueMediaEvent("progress");
            }
        }
        else
        {
            _stallGeneration++;
            _isCurrentlyStalled = false;
            NodeDocument.IncrementElementStateVersion();
        }

        if (data.Player is { } current)
        {
            if (!ReferenceEquals(_player, current))
                return;

            // More bytes of the resource of the player
            current.OnDataReceived(data.Content, data.IsComplete);
            OnPlayerStateChanged();
            if (data.IsComplete)
            {
                QueueMediaElementTask(() => CompleteFetch(current));
            }

            return;
        }

        var player = backend.CreatePlayer(new MediaResource(data.Url, data.ContentType, data.Content, data.IsComplete, this is HtmlVideoElement, eventLoop.TimeProvider));
        if (player is null)
        {
            // The metadata cannot be read yet; once the resource is complete, the media data is in an unsupported format, or
            // cannot be rendered at all
            if (data.IsComplete)
            {
                data.OnFailure();
            }

            return;
        }

        data.Player = player;
        data.LastProgressTimestamp = eventLoop.TimeProvider.GetTimestamp();
        EstablishMediaResource(player, eventLoop, data.IsCorsSameOrigin);

        // The URL of the resource indicates a start time: a media fragment (https://www.w3.org/TR/media-frags/#naming-time)
        if (!_seeking && MediaFragment.GetStartTime(_currentSrc) is { } startTime and > 0)
        {
            Seek(startTime);
        }

        // The data of the current playback position is available in a later task (the events of the metadata are fired
        // first); once the entire resource is fetched, the element is suspended and the algorithm ends
        QueueMediaElementTask(() =>
        {
            if (!ReferenceEquals(_player, player))
                return;

            UpdateReadyStateFromPlayer();
            if (data.IsComplete)
            {
                QueueMediaElementTask(() => CompleteFetch(player));
            }
        });
    }

    /// <summary>
    /// The steps of the media data processing steps list once enough of the media data has been fetched to determine the
    /// duration of the media resource, its dimensions and other metadata: the element uses the player of the resource, has
    /// its tracks, its duration and its dimensions, and its ready state is <c>HAVE_METADATA</c>.
    /// </summary>
    private void EstablishMediaResource(MediaPlayer player, DomEventLoop eventLoop, bool isCorsSameOrigin)
    {
        ReleasePlayer();
        _player = player;

        // The player is released when the document that plays the resource is destroyed
        _playerDocument = NodeDocument.BrowsingContext is null && NodeDocument.OriginDocument is { } originDocument ? originDocument : NodeDocument;
        (_playerDocument.MediaElementsWithPlayer ??= new(ReferenceEqualityComparer.Instance)).Add(this);
        IsMediaDataCorsSameOrigin = isCorsSameOrigin;
        player.PlaybackRate = _playbackRate;
        player.SetPreservesPitch(_preservesPitch);
        UpdatePlayerVolume();
        player.SetStateChangedCallback(() => eventLoop.QueueTask(() =>
        {
            if (ReferenceEquals(_player, player))
            {
                OnPlayerStateChanged();
            }
        }));

        // The audio and video tracks of the resource: the first audio track is enabled, the first video track is selected
        // (the tracks of a media source are created by its source buffers)
        foreach (var info in player.AudioTracks)
        {
            AudioTracks.Add(new AudioTrack(info));
        }

        foreach (var info in player.VideoTracks)
        {
            VideoTracks.Add(new VideoTrack(info));
        }

        ExposeMediaResourceSpecificTextTracks(player);

        // Once enough of the media data has been fetched to determine the duration of the media resource, its dimensions
        // and other metadata
        _currentPlaybackPosition = 0;
        _officialPlaybackPosition = 0;
        var duration = player.Duration;
        duration = double.IsNaN(duration) ? double.PositiveInfinity : duration;
        if (!duration.Equals(_duration))
        {
            // The duration of an attached media source can already be known
            _duration = duration;
            QueueMediaEvent("durationchange");
        }

        SetVideoDimensions(player.VideoWidth, player.VideoHeight);
        if (this is HtmlVideoElement)
        {
            QueueMediaEvent("resize");
        }

        SetReadyState(HaveMetadata);
        if (_defaultPlaybackStartPosition > 0)
        {
            Seek(_defaultPlaybackStartPosition);
        }

        _defaultPlaybackStartPosition = 0;
    }

    /// <summary>The entire media resource has been fetched: a last progress event is fired, and the element is suspended.</summary>
    private void CompleteFetch(MediaPlayer player)
    {
        if (!ReferenceEquals(_player, player) || _networkState != NetworkLoading)
            return;

        FireEvent(new Event("progress"));
        SetNetworkState(NetworkIdle);
        FireEvent(new Event("suspend"));
    }

    /// <summary>The state of the player changed (see <see cref="MediaPlayer.NotifyStateChanged"/>): the element fires the events of the changes.</summary>
    private void OnPlayerStateChanged()
    {
        if (_player is not { } player)
            return;

        // A fatal error after the resource was established to be usable
        if (player.Error != MediaPlayerError.None && _error is null)
        {
            _error = new MediaError(player.Error == MediaPlayerError.Network ? MediaError.MediaErrNetwork : MediaError.MediaErrDecode, player.ErrorMessage);
            _selectionGeneration++;
            SetNetworkState(NetworkIdle);
            SetDelayingTheLoadEvent(false);
            FireEvent(new Event("error"));
            UpdatePlayback();
            return;
        }

        var wasAdvancing = _isAdvancing;
        var duration = player.Duration;
        duration = double.IsNaN(duration) ? double.PositiveInfinity : duration;
        if (duration != _duration)
        {
            // The length of the media resource changes to a known value
            _duration = duration;
            QueueMediaEvent("durationchange");
            if (CurrentPlaybackPosition > duration)
            {
                Seek(duration);
            }
        }

        UpdateVideoDimensions();
        UpdateReadyStateFromPlayer(canRise: player is not MediaSourcePlayer || _isMonitoringMediaSource);
        if (_seeking && !player.IsSeeking)
        {
            if (player is MediaSourcePlayer)
            {
                // The media data of the new playback position was appended to the media source: the seek algorithm
                // resumes from a task (the scripts that append the data or end the stream still see the element seeking)
                var generation = _seekGeneration;
                QueueMediaElementTask(() =>
                {
                    if (generation == _seekGeneration && _player is { IsSeeking: false })
                    {
                        CompleteSeek(generation);
                    }
                });
            }
            else
            {
                CompleteSeek(_seekGeneration);
            }
        }

        // The position of the player may have changed in a discontinuous way; the position of an element attached to a
        // media source only advances with the clock (the timer of the playback reports it)
        UpdatePlayback();
        if (wasAdvancing && player is not MediaSourcePlayer)
        {
            OnPlaybackPositionChanged();
        }
    }
}

/// <summary>The temporal dimension of the media fragments (https://www.w3.org/TR/media-frags/#naming-time): <c>#t=10</c>, <c>#t=npt:10,20</c>, <c>#t=1:02:03.5</c>.</summary>
internal static class MediaFragment
{
    /// <summary>Returns the start time, in seconds, of the temporal dimension of the fragment of a URL, or null.</summary>
    public static double? GetStartTime(string url)
    {
        var hash = url.IndexOf('#', StringComparison.Ordinal);
        if (hash < 0)
            return null;

        double? result = null;
        foreach (var dimension in url[(hash + 1)..].Split('&'))
        {
            // The last valid occurrence of a dimension wins
            if (!dimension.StartsWith("t=", StringComparison.Ordinal))
                continue;

            var value = Uri.UnescapeDataString(dimension[2..]);
            if (value.StartsWith("npt:", StringComparison.Ordinal))
            {
                value = value[4..];
            }

            var comma = value.IndexOf(',', StringComparison.Ordinal);
            var start = comma < 0 ? value : value[..comma];
            if (start.Length == 0)
            {
                // "t=,10" starts at 0
                if (comma >= 0 && TryParseNptTime(value[(comma + 1)..], out _))
                {
                    result = 0;
                }

                continue;
            }

            if (TryParseNptTime(start, out var time) && (comma < 0 || (TryParseNptTime(value[(comma + 1)..], out var end) && end > time)))
            {
                result = time;
            }
        }

        return result;
    }

    /// <summary>Parses a normal play time: seconds (<c>12.5</c>), <c>mm:ss</c> or <c>hh:mm:ss</c>, with an optional fraction.</summary>
    private static bool TryParseNptTime(string value, out double seconds)
    {
        seconds = 0;
        var parts = value.Split(':');
        if (parts.Length is 0 or > 3)
            return false;

        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var isLast = i == parts.Length - 1;
            if (part.Length == 0 || !IsDigitsWithOptionalFraction(part, allowFraction: isLast))
                return false;

            // The minutes and the seconds of a clock time have two digits
            if (parts.Length > 1 && i > 0 && part.Split('.')[0].Length != 2)
                return false;

            var number = double.Parse(part, System.Globalization.CultureInfo.InvariantCulture);
            if (parts.Length > 1 && i > 0 && number >= 60)
                return false;

            seconds = (seconds * 60) + number;
        }

        return true;
    }

    private static bool IsDigitsWithOptionalFraction(string value, bool allowFraction)
    {
        var seenDot = false;
        var digits = 0;
        foreach (var c in value)
        {
            if (c == '.' && allowFraction && !seenDot && digits > 0)
            {
                seenDot = true;
            }
            else if (char.IsAsciiDigit(c))
            {
                digits++;
            }
            else
            {
                return false;
            }
        }

        return digits > 0 && value[^1] != '.';
    }
}
