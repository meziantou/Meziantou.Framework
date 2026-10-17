using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.WebVtt;

namespace Meziantou.Framework.WebPlatform.Dom;

// The track element (https://html.spec.whatwg.org/multipage/media.html#the-track-element): its text track, which is in the
// list of text tracks of its parent media element, and the track processing model
// (https://html.spec.whatwg.org/multipage/media.html#start-the-track-processing-model), which fetches the WebVTT file of the
// element when the mode of the track is not disabled and parses it into the list of cues of the track.
public partial class HtmlTrackElement
{
    /// <summary>The steps of the track processing model that are running for the element.</summary>
    private enum ProcessingState
    {
        /// <summary>The algorithm is not running.</summary>
        Idle,

        /// <summary>The algorithm awaits a stable state.</summary>
        AwaitingStableState,

        /// <summary>The resource is being fetched and parsed: the readiness state of the track is loading.</summary>
        Loading,

        /// <summary>The algorithm waits until the track URL is no longer equal to the URL that was fetched, while the mode of the track is hidden or showing.</summary>
        WaitingForUrlChange,
    }

    private TextTrack? _track;
    private ProcessingState _processingState;
    private CancellationTokenSource? _fetchCancellation;
    private int _loadGeneration;

    /// <summary>The track URL of the element (https://html.spec.whatwg.org/multipage/media.html#track-url).</summary>
    private string _trackUrl = "";

    /// <summary>The track URL the track processing model fetched last.</summary>
    private string _loadedUrl = "";

    /// <summary>The media element whose list of text tracks contains the text track of the element: its parent, or null.</summary>
    private HtmlMediaElement? _mediaElement;

    partial void OnCreated() => _track = new TextTrack(this);

    /// <summary>Gets the text track of the element (https://html.spec.whatwg.org/multipage/media.html#dom-track-track).</summary>
    [IdlMember("track", "https://html.spec.whatwg.org/multipage/media.html#dom-track-track")]
    public TextTrack Track => _track!;

    /// <summary>
    /// Gets the readiness state of the text track of the element (https://html.spec.whatwg.org/multipage/media.html#dom-track-readystate):
    /// <see cref="None"/>, <see cref="Loading"/>, <see cref="Loaded"/> or <see cref="Error"/>.
    /// </summary>
    [IdlMember("readyState", "https://html.spec.whatwg.org/multipage/media.html#dom-track-readystate")]
    public ushort ReadyState => (ushort)Track.ReadinessState;

    /// <summary>
    /// The kind of the text track of the element (https://html.spec.whatwg.org/multipage/media.html#attr-track-kind): the
    /// state of its <c>kind</c> attribute (subtitles when the attribute is missing, metadata when its value is invalid).
    /// </summary>
    internal TextTrackKind TrackKind => Kind switch
    {
        "subtitles" => TextTrackKind.Subtitles,
        "captions" => TextTrackKind.Captions,
        "descriptions" => TextTrackKind.Descriptions,
        "chapters" => TextTrackKind.Chapters,
        _ => TextTrackKind.Metadata,
    };

    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);
        if (@namespace is not null)
            return;

        switch (localName)
        {
            case "src":
                // As in Firefox (and the tests), setting the attribute to its value changes nothing: the specification empties
                // the list of cues, which would stay empty as the track URL does not change
                if (oldValue == value)
                    break;

                // When the src attribute is set, the track URL is the URL of the attribute relative to the document, or the
                // empty string. The specification keeps the track URL when the attribute is removed; an element without
                // attribute has no track URL here, so that the removal aborts the load of the previous URL (WPT
                // track-element-src-change-error.html)
                _trackUrl = string.IsNullOrEmpty(value) ? "" : Reflection.EncodingParseAndSerializeUrl(NodeDocument, value) ?? "";

                // Whenever the src attribute is set, changed, or removed, the list of cues of the text track is emptied
                Track.ClearCues();
                OnTrackUrlOrModeChanged();
                break;

            case "kind":
                // Only the cues of the subtitles and captions tracks are displayed
                if (Track.Mode == TextTrackMode.Showing)
                {
                    _mediaElement?.OnCueRenderingChanged();
                }

                break;
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The parent of the element (https://html.spec.whatwg.org/multipage/media.html#sourcing-out-of-band-text-tracks)
    // ----------------------------------------------------------------------------------------------------------------

    internal override void InsertionSteps()
    {
        base.InsertionSteps();
        UpdateMediaElement();
    }

    internal override void RemovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
        base.RemovingSteps(isSubtreeRoot, oldAncestor);
        UpdateMediaElement();
    }

    internal override void MovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
        base.MovingSteps(isSubtreeRoot, oldAncestor);
        UpdateMediaElement();
    }

    /// <summary>
    /// The parent of the element may have changed: its text track is removed from the list of text tracks of its old media
    /// element parent, and added to the one of its new media element parent, for which the track processing model starts.
    /// </summary>
    private void UpdateMediaElement()
    {
        var parent = ParentNode as HtmlMediaElement;
        if (ReferenceEquals(parent, _mediaElement))
        {
            // The order of the track elements of the parent may have changed
            parent?.TextTracks.InvalidateOrder();
            return;
        }

        if (_mediaElement is { } oldParent)
        {
            _mediaElement = null;
            oldParent.OnTrackElementRemoved(this);
        }

        if (parent is not null)
        {
            _mediaElement = parent;
            parent.OnTrackElementInserted(this);
            StartTrackProcessingModel();
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The track processing model (https://html.spec.whatwg.org/multipage/media.html#start-the-track-processing-model)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The mode of the text track of the element changed.</summary>
    internal void OnTextTrackModeChanged() => OnTrackUrlOrModeChanged();

    private bool IsTrackActive => Track.Mode is TextTrackMode.Hidden or TextTrackMode.Showing;

    /// <summary>Whether the track processing model awaits the stable state in which the readiness state of the text track changes to loading.</summary>
    internal bool IsAboutToLoad => _processingState == ProcessingState.AwaitingStableState;

    private void OnTrackUrlOrModeChanged()
    {
        switch (_processingState)
        {
            case ProcessingState.Idle:
                StartTrackProcessingModel();
                break;

            case ProcessingState.Loading when IsTrackActive && _trackUrl != _loadedUrl:
            {
                // The track URL is no longer equal to the URL that is fetched, while the mode is hidden or showing: the fetch is
                // aborted (no cue is added to the list of cues after the moment the URL changed), then the readiness state
                // changes to failed to load and an error event is fired at the element
                AbortFetch();
                var generation = _loadGeneration;
                NodeDocument.QueueElementTask(() =>
                {
                    if (generation == _loadGeneration)
                    {
                        CompleteLoad(succeeded: false);
                    }
                });
                break;
            }

            case ProcessingState.WaitingForUrlChange when IsTrackActive && _trackUrl != _loadedUrl:
                RunFromTop();
                break;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/media.html#start-the-track-processing-model</summary>
    private void StartTrackProcessingModel()
    {
        // Another occurrence of the algorithm is already running for the text track and its track element
        if (_processingState != ProcessingState.Idle)
            return;

        if (!IsTrackActive || ParentNode is not HtmlMediaElement media || !media.AreTextTracksEnabled)
            return;

        RunFromTop();
    }

    /// <summary>The steps of the algorithm from the step labeled top.</summary>
    private void RunFromTop()
    {
        if (NodeDocument.EventLoop is not { } eventLoop)
        {
            _processingState = ProcessingState.Idle;
            return;
        }

        // Await a stable state: the synchronous section sets the readiness state to loading and reads the track URL and the
        // state of the crossorigin attribute of the parent media element
        _processingState = ProcessingState.AwaitingStableState;
        eventLoop.QueueMicrotask(() =>
        {
            _processingState = ProcessingState.Loading;
            var generation = ++_loadGeneration;
            var track = Track;
            track.ReadinessState = TextTrackReadinessState.Loading;
            _loadedUrl = _trackUrl;
            var media = ParentNode as HtmlMediaElement;
            var document = NodeDocument;
            if (_loadedUrl.Length == 0 || Urls.UrlParser.Parse(_loadedUrl, baseUrl: null) is not { } url || !Fetch(generation, document, url, media))
            {
                // The URL is the empty string, or nothing can be fetched: the readiness state changes to failed to load and
                // an error event is fired at the element, in a task
                document.QueueElementTask(() =>
                {
                    if (generation == _loadGeneration)
                    {
                        CompleteLoad(succeeded: false);
                    }
                });
            }
        });
    }

    /// <summary>
    /// Fetches the resource of the track: a potential-CORS request whose destination is "track", with the state of the
    /// <c>crossorigin</c> attribute of the parent media element and the same-origin fallback flag set (without the
    /// attribute, the resource must be same origin).
    /// </summary>
    private bool Fetch(int generation, Document document, Urls.UrlRecord url, HtmlMediaElement? media)
    {
        var client = document.BrowsingContext is null && document.OriginDocument is { } origin ? origin : document;
        var cors = media is not null && media.HasAttributeWithLocalName("crossorigin");
        var request = SubresourceFetching.CreateRequest(client, url, RequestDestination.Track, cors, referrerPolicy: "", this, useCredentials: cors && SubresourceFetching.UsesCredentials(media!));
        if (!cors)
        {
            request.Mode = RequestMode.SameOrigin;
            request.CredentialsMode = FetchCredentialsMode.SameOrigin;
        }

        // The cues are parsed as the bytes of the resource arrive
        request.StreamsBody = true;

        var cancellation = new CancellationTokenSource();
        _fetchCancellation = cancellation;
        var started = SubresourceFetching.Fetch(client, request, resource => OnResponse(generation, cancellation, resource), initiatorType: "track", cancellation.Token);
        if (!started)
        {
            _fetchCancellation = null;
            cancellation.Dispose();
        }

        return started;
    }

    private void AbortFetch()
    {
        _loadGeneration++;
        if (_fetchCancellation is { } cancellation)
        {
            _fetchCancellation = null;
            cancellation.Cancel();
            cancellation.Dispose();
        }
    }

    private void ReleaseFetchCancellation(CancellationTokenSource cancellation)
    {
        if (ReferenceEquals(_fetchCancellation, cancellation))
        {
            _fetchCancellation = null;
            cancellation.Dispose();
        }
    }

    /// <summary>
    /// The response of the fetch: when the fetch fails (a network error, an HTTP error, the CORS check), the load fails; else
    /// the data of the resource is passed to the WebVTT parser as it is received, whose cues are added to the list of cues of
    /// the text track.
    /// </summary>
    private void OnResponse(int generation, CancellationTokenSource cancellation, DocumentResource? resource)
    {
        if (generation != _loadGeneration)
        {
            ReleaseFetchCancellation(cancellation);
            return;
        }

        if (resource is null || resource.StatusCode is < 200 or > 299)
        {
            ReleaseFetchCancellation(cancellation);
            CompleteLoad(succeeded: false);
            return;
        }

        var track = Track;
        var cues = new List<TextTrackCue>();
        var parser = new WebVttParser(
            cue =>
            {
                track.AddCueCore(cue);
                cues.Add(cue);
            },
            track.Regions,
            text =>
            {
                track.StyleSheets.Add(new CssStyleSheet(text));
                if (text.Contains('(', StringComparison.Ordinal))
                {
                    NodeDocument.CueStyleSheetsMayReferenceImages = true;
                }
            });

        Parse(parser, cues, resource.ContentBytes is { } bytes ? bytes.Span : System.Text.Encoding.UTF8.GetBytes(resource.Content ?? ""));
        if (resource.RemainingContentBytes is { } remaining && NodeDocument.EventLoop is { } eventLoop && !parser.HasFailed)
        {
            // A response that arrives over time: the parser updates the list of cues as the chunks arrive
            ReceiveNextChunk(generation, cancellation, parser, cues, eventLoop, remaining.GetAsyncEnumerator(cancellation.Token));
            return;
        }

        ReleaseFetchCancellation(cancellation);
        CompleteParsing(parser, cues);
    }

    /// <summary>The cues of the bytes are added to the list of cues of the text track; they are newly introduced cues of the media element.</summary>
    private void Parse(WebVttParser parser, List<TextTrackCue> cues, ReadOnlySpan<byte> bytes)
    {
        parser.Append(bytes);
        IntroduceCues(cues);
    }

    private void IntroduceCues(List<TextTrackCue> cues)
    {
        if (cues.Count == 0)
            return;

        var introduced = cues.ToArray();
        cues.Clear();
        Track.MediaElement?.IntroduceCues(introduced);
    }

    private void CompleteParsing(WebVttParser parser, List<TextTrackCue> cues)
    {
        parser.Complete();
        IntroduceCues(cues);

        // A file that does not start with the WebVTT file signature was not successfully processed
        CompleteLoad(succeeded: !parser.HasFailed);
    }

    /// <summary>Waits for the next chunk of a response that arrives over time, and parses it from a task of the event loop.</summary>
    private void ReceiveNextChunk(int generation, CancellationTokenSource cancellation, WebVttParser parser, List<TextTrackCue> cues, DomEventLoop eventLoop, IAsyncEnumerator<ReadOnlyMemory<byte>> chunks)
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
                    // The fetch was aborted (the track URL changed): the rest of the response is ignored
                    if (generation != _loadGeneration)
                    {
                        _ = chunks.DisposeAsync().AsTask();
                        ReleaseFetchCancellation(cancellation);
                        return;
                    }

                    if (completed.IsCompletedSuccessfully && completed.Result)
                    {
                        Parse(parser, cues, chunks.Current.Span);
                        if (!parser.HasFailed)
                        {
                            ReceiveNextChunk(generation, cancellation, parser, cues, eventLoop, chunks);
                            return;
                        }
                    }

                    _ = chunks.DisposeAsync().AsTask();
                    ReleaseFetchCancellation(cancellation);
                    if (completed.IsCompletedSuccessfully)
                    {
                        CompleteParsing(parser, cues);
                    }
                    else
                    {
                        CompleteLoad(succeeded: false);
                    }
                });
                eventLoop.EndOperation();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// The readiness state of the text track changes to loaded or failed to load, and a <c>load</c> or an <c>error</c> event
    /// is fired at the element. The algorithm then waits until the track URL is no longer equal to the URL that was fetched
    /// while the mode of the track is hidden or showing, to load the new resource.
    /// </summary>
    private void CompleteLoad(bool succeeded)
    {
        _loadGeneration++;
        Track.ReadinessState = succeeded ? TextTrackReadinessState.Loaded : TextTrackReadinessState.FailedToLoad;
        _processingState = ProcessingState.WaitingForUrlChange;
        FireEvent(new Event(succeeded ? "load" : "error"));
        if (_processingState == ProcessingState.WaitingForUrlChange && IsTrackActive && _trackUrl != _loadedUrl)
        {
            RunFromTop();
        }
    }
}
