using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Media;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>
/// The media elements (https://html.spec.whatwg.org/multipage/media.html) with a fake media backend
/// (<see cref="BrowsingContextOptions.MediaBackend"/>): the resources are text files that describe a media resource
/// (<c>MEDIA duration=5 width=320 height=240 audio=1 video=1</c>), played with a fake clock.
/// </summary>
public sealed class MediaElementTests : IDisposable
{
    private static readonly string[] MediaEvents =
    [
        "loadstart", "progress", "suspend", "abort", "error", "emptied", "stalled", "loadedmetadata", "loadeddata", "canplay",
        "canplaythrough", "playing", "waiting", "seeking", "seeked", "ended", "durationchange", "timeupdate", "play", "pause",
        "ratechange", "resize", "volumechange",
    ];

    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly List<string> _requests = [];
    private readonly List<string> _events = [];
    private readonly Dictionary<string, TimeSpan> _delays = new(StringComparer.Ordinal);
    private Channel<byte[]>? _stream;

    public void Dispose() => _contexts.Dispose();

    private Document Load(string html, MediaBackend? backend = null, bool noBackend = false, MediaAutoplayPolicy autoplayPolicy = MediaAutoplayPolicy.Allowed, Action<Document>? setup = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            DocumentLoader = new MediaLoader(this),
            MediaBackend = noBackend ? null : backend ?? new FakeMediaBackend(),
            MediaAutoplayPolicy = autoplayPolicy,
            TimeProvider = _clock,
        });

        var document = context.Navigate("https://example.com/", _ => html);
        foreach (var element in document.QuerySelectorAll("audio, video"))
        {
            Watch((HtmlMediaElement)element);
        }

        setup?.Invoke(document);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    /// <summary>Records the media events fired at the element.</summary>
    private void Watch(HtmlMediaElement element)
    {
        foreach (var type in MediaEvents)
        {
            element.AddEventListener(type, _ => _events.Add(type));
        }
    }

    /// <summary>Runs the tasks and the timers of the event loop for a duration of the fake clock.</summary>
    private void Advance(double seconds)
    {
        var eventLoop = _contexts.EventLoop;
        var remaining = TimeSpan.FromSeconds(seconds);
        while (remaining > TimeSpan.Zero)
        {
            // The tasks may queue timers: the next timer is computed once the loop is idle
            eventLoop.RunUntilIdle();
            var next = eventLoop.PromoteDueTimers();
            if (next is null || next.Value > remaining)
                break;

            if (next.Value > TimeSpan.Zero)
            {
                _clock.Advance(next.Value);
                remaining -= next.Value;
            }
        }

        if (remaining > TimeSpan.Zero)
        {
            _clock.Advance(remaining);
        }

        eventLoop.PromoteDueTimers();
        eventLoop.RunUntilIdle();
    }

    private string[] TakeEvents(params string[] ignored)
    {
        var events = _events.Where(type => !ignored.Contains(type, StringComparer.Ordinal)).ToArray();
        _events.Clear();
        return events;
    }

    private static T Media<T>(Document document) where T : HtmlMediaElement => (T)document.QuerySelector("audio, video")!;

    /// <summary>Serves the media resources described by the query of the URL (<c>movie?duration=5&amp;width=320</c>); the other URLs are text files or errors.</summary>
    private sealed class MediaLoader(MediaElementTests test) : DocumentLoader
    {
        public override async ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            test._requests.Add(request.Destination + " " + request.Url);
            var url = new Uri(request.Url);
            if (test._delays.TryGetValue(url.AbsolutePath, out var delay))
            {
                var completion = new TaskCompletionSource();
                request.Initiator!.OwnerDocument!.DefaultView!.SetTimeout(() => completion.SetResult(), (int)delay.TotalMilliseconds);
                await completion.Task.ConfigureAwait(false);
            }

            // A response that arrives over time: the first bytes do not contain the metadata, the next ones do, and the last
            // ones are sent when the test completes the stream
            if (url.AbsolutePath.StartsWith("/stream", StringComparison.Ordinal))
            {
                test._stream = Channel.CreateUnbounded<byte[]>();
                return new DocumentResource("MED"u8.ToArray()) { RemainingContentBytes = ReadStreamAsync(test._stream.Reader, cancellationToken) };
            }

            // A server that answers the range requests with 16 bytes of the resource at a time
            if (url.AbsolutePath.StartsWith("/ranged", StringComparison.Ordinal))
            {
                var body = Encoding.UTF8.GetBytes("MEDIA duration=6 width=12 height=8 padding=0123456789");
                var range = request.Headers.First(header => header.Key == "Range").Value;
                test._requests[^1] += " " + range;
                var start = int.Parse(range["bytes=".Length..^1], CultureInfo.InvariantCulture);
                var end = Math.Min(start + 16, body.Length);
                return new DocumentResource(body.AsMemory(start, end - start))
                {
                    StatusCode = 206,
                    Headers = [new("Content-Range", string.Create(CultureInfo.InvariantCulture, $"bytes {start}-{end - 1}/{body.Length}"))],
                };
            }

            if (url.AbsolutePath.StartsWith("/missing", StringComparison.Ordinal))
                return new DocumentResource("not found") { StatusCode = 404 };

            if (url.AbsolutePath.StartsWith("/error", StringComparison.Ordinal))
                return null;

            var content = url.AbsolutePath.StartsWith("/movie", StringComparison.Ordinal) || url.AbsolutePath.StartsWith("/sound", StringComparison.Ordinal)
                ? "MEDIA " + url.Query.TrimStart('?').Replace('&', ' ')
                : "not a media resource";
            return new DocumentResource(Encoding.UTF8.GetBytes(content)) { Headers = [new("Content-Type", "application/octet-stream")] };
        }
    }

    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadStreamAsync(ChannelReader<byte[]> reader, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var chunk in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return chunk;
        }
    }

    private sealed class FakeMediaBackend : MediaBackend
    {
        public List<FakeMediaPlayer> Players { get; } = [];

        public override MediaTypeSupport CanPlayType(string mimeType, IReadOnlyList<string> codecs)
        {
            if (mimeType is not ("video/fake" or "audio/fake"))
                return MediaTypeSupport.NotSupported;

            if (codecs.Count == 0)
                return MediaTypeSupport.Maybe;

            return codecs.All(codec => codec is "f1" or "f2") ? MediaTypeSupport.Probably : MediaTypeSupport.NotSupported;
        }

        public override MediaPlayer? CreatePlayer(MediaResource resource)
        {
            // The metadata is the first line of the resource
            var text = Encoding.UTF8.GetString(resource.Content.Span);
            if (!text.StartsWith("MEDIA", StringComparison.Ordinal) || (!resource.IsComplete && !text.Contains('\n', StringComparison.Ordinal)))
                return null;

            text = text.Split('\n')[0];

            var player = new FakeMediaPlayer(resource, text);
            Players.Add(player);
            return player;
        }
    }

    private sealed class FakeMediaPlayer : MediaPlayer
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public FakeMediaPlayer(MediaResource resource, string description)
            : base(resource.TimeProvider)
        {
            Resource = resource;
            foreach (var part in description.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1))
            {
                var index = part.IndexOf('=', StringComparison.Ordinal);
                if (index > 0)
                {
                    _values[part[..index]] = part[(index + 1)..];
                }
            }

            State = (MediaReadyState)GetInt("state", (int)MediaReadyState.HaveEnoughData);
        }

        public MediaResource Resource { get; }

        public MediaReadyState State { get; set; }

        public double Volume { get; private set; } = double.NaN;

        public bool IsDisposed { get; private set; }

        public MediaPlayerError FakeError { get; set; }

        private int GetInt(string name, int defaultValue = 0) => _values.TryGetValue(name, out var value) ? int.Parse(value, CultureInfo.InvariantCulture) : defaultValue;

        public override double Duration => _values.TryGetValue("duration", out var value) ? double.Parse(value, CultureInfo.InvariantCulture) : 5;

        public override int VideoWidth => GetInt("width");

        public override int VideoHeight => GetInt("height");

        public override IReadOnlyList<MediaTrackInfo> AudioTracks => [.. Enumerable.Range(1, GetInt("audio")).Select(i => new MediaTrackInfo("a" + i.ToString(CultureInfo.InvariantCulture), i == 1 ? "main" : "", "Audio " + i.ToString(CultureInfo.InvariantCulture), "en"))];

        public override IReadOnlyList<MediaTrackInfo> VideoTracks => [.. Enumerable.Range(1, GetInt("video")).Select(i => new MediaTrackInfo("v" + i.ToString(CultureInfo.InvariantCulture), i == 1 ? "main" : ""))];

        public override MediaReadyState ReadyState => State;

        public override MediaPlayerError Error => FakeError;

        public override string ErrorMessage => FakeError == MediaPlayerError.None ? "" : "fake error";

        public override void SetVolume(double volume) => Volume = volume;

        private MediaVideoFrame? _frame;

        public override MediaVideoFrame? GetVideoFrame() => VideoWidth > 0 ? _frame ??= new MediaVideoFrame(1, 1, new byte[] { 0, 128, 0, 255 }) : null;

        public void Notify() => NotifyStateChanged();

        public override void OnDataReceived(ReadOnlyMemory<byte> content, bool isComplete)
        {
            if (isComplete)
            {
                State = MediaReadyState.HaveEnoughData;
            }
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    [Fact]
    public void WithoutBackend_TheElementHasNoMediaResource()
    {
        var document = Load("<video src='/movie?duration=5'></video>", noBackend: true);
        var video = Media<HtmlVideoElement>(document);
        Advance(1);
        Assert.Empty(_requests);
        Assert.Empty(_events);
        Assert.Equal(HtmlMediaElement.NetworkEmpty, video.NetworkState);
        Assert.Equal(HtmlMediaElement.HaveNothing, video.ReadyState);
        Assert.Equal("", video.CanPlayType("video/fake"));
        Assert.True(video.Paused);
        var play = video.Play();
        Assert.Equal("NotSupportedError", Assert.IsType<DomException>(play.Exception!.InnerException).Name);
    }

    [Fact]
    public void Src_LoadsTheResourceAndFiresTheEventsInOrder()
    {
        var document = Load("<video src='/movie?duration=5&width=320&height=240&audio=1&video=1'></video>");
        var video = Media<HtmlVideoElement>(document);
        Advance(0.1);
        Assert.Equal(["loadstart", "durationchange", "resize", "loadedmetadata", "loadeddata", "canplay", "canplaythrough", "progress", "suspend"], TakeEvents());
        Assert.Equal(["Video https://example.com/movie?duration=5&width=320&height=240&audio=1&video=1"], _requests);
        Assert.Equal("https://example.com/movie?duration=5&width=320&height=240&audio=1&video=1", video.CurrentSrc);
        Assert.Equal(HtmlMediaElement.NetworkIdle, video.NetworkState);
        Assert.Equal(HtmlMediaElement.HaveEnoughData, video.ReadyState);
        Assert.Equal(5, video.Duration);
        Assert.Equal((320u, 240u), (video.VideoWidth, video.VideoHeight));
        Assert.Null(video.Error);
        Assert.True(video.Paused);
        Assert.Equal(1u, video.Buffered.Length);
        Assert.Equal((0, 5), (video.Seekable.Start(0), video.Seekable.End(0)));
        Assert.Equal(0u, video.Played.Length);

        // The tracks of the resource: the first audio track is enabled, the first video track is selected
        Assert.Equal(["a1"], video.AudioTracks.Select(track => track.Id));
        Assert.True(video.AudioTracks[0].Enabled);
        Assert.Equal(("main", "Audio 1", "en"), (video.AudioTracks[0].Kind, video.AudioTracks[0].Label, video.AudioTracks[0].Language));
        Assert.Equal(0, video.VideoTracks.SelectedIndex);
        Assert.Same(video.VideoTracks[0], video.VideoTracks.GetTrackById("v1"));
    }

    [Fact]
    public void Audio_UsesTheAudioDestination()
    {
        var document = Load("<audio src='/sound?duration=2&audio=1'></audio>");
        Advance(0.1);
        Assert.Equal(["Audio https://example.com/sound?duration=2&audio=1"], _requests);

        // An audio element has no dimensions: no resize event
        Assert.DoesNotContain("resize", TakeEvents());
        Assert.Equal(2, Media<HtmlAudioElement>(document).Duration);
    }

    [Theory]
    [InlineData("/missing.fake")]
    [InlineData("/error.fake")]
    [InlineData("/text.txt")]
    [InlineData("")]
    public void Src_FailureSetsTheErrorAndRejectsPlay(string src)
    {
        var document = Load("<video></video>");
        var video = Media<HtmlVideoElement>(document);
        video.Src = src;
        var play = video.Play();
        Advance(0.1);

        // The events of play() are queued before the ones of the resource selection algorithm, which starts at the next stable state
        Assert.Equal(["play", "waiting", "loadstart", "error"], TakeEvents());
        Assert.Equal(MediaError.MediaErrSrcNotSupported, video.Error!.Code);
        Assert.Equal(HtmlMediaElement.NetworkNoSource, video.NetworkState);
        Assert.Equal("NotSupportedError", Assert.IsType<DomException>(play.Exception!.InnerException).Name);

        // play() is rejected until the load algorithm clears the error
        Assert.True(video.Play().IsFaulted);
        video.Src = "/movie?duration=1";
        Assert.Null(video.Error);
        Advance(0.1);
        Assert.Equal(HtmlMediaElement.HaveEnoughData, video.ReadyState);
    }

    [Fact]
    public void SourceElements_AreTriedInOrder()
    {
        var errors = new List<string>();
        var document = Load("""
            <video>
              <source id=a src='/a.fake' type='video/unknown'>
              <source id=b src='/b.fake' media='(min-width: 100000px)'>
              <source id=c src='/text.txt'>
              <source id=d src='/movie?duration=3' type='video/fake; codecs="f1"'>
              <source id=e src='/movie?duration=4'>
            </video>
            """,
            setup: document =>
            {
                foreach (var source in document.QuerySelectorAll("source"))
                {
                    source.AddEventListener("error", _ => errors.Add(((Element)source).Id));
                }
            });
        var video = Media<HtmlVideoElement>(document);
        Advance(0.1);

        // The types and the media queries are checked without fetching; the resources of the other candidates are sniffed
        Assert.Equal(["Video https://example.com/text.txt", "Video https://example.com/movie?duration=3"], _requests);
        Assert.Equal(["a", "b", "c"], errors);
        Assert.Equal("https://example.com/movie?duration=3", video.CurrentSrc);
        Assert.Equal(3, video.Duration);
        Assert.Null(video.Error);
        Assert.Equal(1, TakeEvents().Count(type => type == "loadstart"));
    }

    [Fact]
    public void SourceElements_WaitForANewSourceAfterTheLastOne()
    {
        var document = Load("<video><source src='/text.txt'></video>");
        var video = Media<HtmlVideoElement>(document);
        Advance(0.1);
        Assert.Equal(HtmlMediaElement.NetworkNoSource, video.NetworkState);
        Assert.Null(video.Error);
        Assert.Equal(["loadstart"], TakeEvents());

        // A source element inserted after the pointer is the next candidate
        var source = document.CreateElement("source");
        source.SetAttribute("src", "/movie?duration=2");
        video.AppendChild(source);
        Advance(0.1);
        Assert.Equal(2, video.Duration);
        Assert.Equal(HtmlMediaElement.NetworkIdle, video.NetworkState);
        Assert.DoesNotContain("loadstart", TakeEvents());
    }

    [Fact]
    public void InsertingASource_InvokesTheResourceSelection()
    {
        var document = Load("<video></video>");
        var video = Media<HtmlVideoElement>(document);
        Assert.Equal(HtmlMediaElement.NetworkEmpty, video.NetworkState);
        var source = document.CreateElement("source");
        source.SetAttribute("src", "/movie?duration=2");
        video.AppendChild(source);
        Assert.Equal(HtmlMediaElement.NetworkNoSource, video.NetworkState);
        Advance(0.1);
        Assert.Equal(2, video.Duration);
    }

    [Fact]
    public void CanPlayType_AsksTheBackend()
    {
        var video = Media<HtmlVideoElement>(Load("<video></video>"));
        Assert.Equal("maybe", video.CanPlayType("video/fake"));
        Assert.Equal("probably", video.CanPlayType("VIDEO/Fake; codecs=\"f1, f2\""));
        Assert.Equal("", video.CanPlayType("video/fake; codecs=\"f1, other\""));
        Assert.Equal("", video.CanPlayType("video/other"));
        Assert.Equal("", video.CanPlayType("application/octet-stream"));
        Assert.Equal("", video.CanPlayType("not a type"));
    }

    [Fact]
    public async Task Play_AdvancesThePositionUntilTheEnd()
    {
        var document = Load("<video src='/movie?duration=2&width=4&height=2'></video>");
        var video = Media<HtmlVideoElement>(document);
        Advance(0.1);
        TakeEvents();
        var play = video.Play();
        Assert.False(video.Paused);
        Assert.False(play.IsCompleted);
        Advance(1);
        await play;
        Assert.Equal(["play", "playing"], TakeEvents("timeupdate"));
        Assert.InRange(video.CurrentTime, 0.9, 1.1);
        Assert.False(video.Ended);
        Advance(1.5);

        // At the end of the resource, the element is paused and the ended event is fired
        Assert.Equal(["pause", "ended"], TakeEvents("timeupdate"));
        Assert.Equal(2, video.CurrentTime);
        Assert.True(video.Ended);
        Assert.True(video.Paused);
        Assert.Equal((1u, 0d, 2d), (video.Played.Length, video.Played.Start(0), video.Played.End(0)));

        // Playing again restarts from the beginning
        _ = video.Play();
        Advance(0.5);
        Assert.Equal(["seeking", "play", "playing", "seeked"], TakeEvents("timeupdate"));
        Assert.InRange(video.CurrentTime, 0.4, 0.6);
    }

    [Fact]
    public void Pause_RejectsThePendingPlayPromises()
    {
        var document = Load("<video></video>");
        var video = Media<HtmlVideoElement>(document);
        _delays["/movie"] = TimeSpan.FromSeconds(1);
        video.Src = "/movie?duration=2";
        var play = video.Play();
        video.Pause();
        Advance(0.1);
        Assert.Equal("AbortError", Assert.IsType<DomException>(play.Exception!.InnerException).Name);
        Assert.Equal(["play", "waiting", "timeupdate", "pause", "loadstart"], TakeEvents());
        Assert.True(video.Paused);
    }

    [Fact]
    public void Load_AbortsTheCurrentResource()
    {
        var backend = new FakeMediaBackend();
        var document = Load("<video src='/movie?duration=2'></video>", backend);
        var video = Media<HtmlVideoElement>(document);
        Advance(0.1);
        var play = video.Play();
        Advance(0.5);
        TakeEvents();
        video.Src = "/movie?duration=3";

        // The promise of a pending task is settled at once, and the element is reset
        Assert.True(play.IsCompletedSuccessfully);
        Assert.True(video.Paused);
        Assert.Equal(HtmlMediaElement.HaveNothing, video.ReadyState);
        Assert.True(double.IsNaN(video.Duration));
        Assert.True(backend.Players[0].IsDisposed);
        Advance(0.1);
        Assert.Equal(["abort", "emptied", "timeupdate", "loadstart", "durationchange", "resize", "loadedmetadata", "loadeddata", "canplay", "canplaythrough", "progress", "suspend"], TakeEvents());
        Assert.Equal(3, video.Duration);
        Assert.Equal(0, video.CurrentTime);
    }

    [Fact]
    public void CurrentTime_Seeks()
    {
        var document = Load("<video src='/movie?duration=10'></video>");
        var video = Media<HtmlVideoElement>(document);

        // Without media data, the value is the default playback start position, used when the metadata is loaded
        video.CurrentTime = 4;
        Assert.Equal(4, video.CurrentTime);
        Advance(0.1);
        Assert.Equal(4, video.CurrentTime);
        Assert.Contains("seeked", TakeEvents());

        video.CurrentTime = 100;
        Assert.True(video.Seeking);
        Advance(0.1);
        Assert.False(video.Seeking);
        Assert.Equal(["seeking", "timeupdate", "seeked"], TakeEvents());
        Assert.Equal(10, video.CurrentTime);
        Assert.True(video.Ended);

        video.CurrentTime = -5;
        Advance(0.1);
        Assert.Equal(0, video.CurrentTime);
    }

    [Fact]
    public void MediaFragment_SetsTheInitialPosition()
    {
        var document = Load("<video src='/movie?duration=10#t=2.5'></video>");
        Advance(0.1);
        Assert.Equal(2.5, Media<HtmlVideoElement>(document).CurrentTime);
    }

    [Fact]
    public void Loop_SeeksToTheStartAtTheEnd()
    {
        var document = Load("<video loop src='/movie?duration=1'></video>");
        var video = Media<HtmlVideoElement>(document);
        Advance(0.1);
        _ = video.Play();
        Advance(2.5);
        var events = TakeEvents("timeupdate");
        Assert.DoesNotContain("ended", events);
        Assert.Equal(2, events.Count(type => type == "seeked"));
        Assert.False(video.Paused);
        Assert.False(video.Ended);
        Assert.InRange(video.CurrentTime, 0, 1);
    }

    [Fact]
    public void Autoplay_StartsThePlayback()
    {
        var document = Load("<video autoplay src='/movie?duration=2'></video>");
        var video = Media<HtmlVideoElement>(document);
        Advance(0.1);
        Assert.False(video.Paused);
        Assert.Equal(["loadstart", "durationchange", "resize", "loadedmetadata", "loadeddata", "canplay", "canplaythrough", "play", "playing", "progress", "suspend"], TakeEvents("timeupdate"));
    }

    [Fact]
    public void AutoplayPolicy_RequiresAUserGestureUnlessMuted()
    {
        var document = Load("<video autoplay src='/movie?duration=2'></video><audio muted autoplay src='/sound?duration=2'></audio>", autoplayPolicy: MediaAutoplayPolicy.UserActivationRequired);
        var video = (HtmlVideoElement)document.QuerySelector("video")!;
        var audio = (HtmlAudioElement)document.QuerySelector("audio")!;
        Advance(0.1);
        Assert.True(video.Paused);
        Assert.False(audio.Paused);
        Assert.Equal("NotAllowedError", Assert.IsType<DomException>(video.Play().Exception!.InnerException).Name);
    }

    [Fact]
    public void PreloadNone_DefersTheFetchUntilPlay()
    {
        var document = Load("<video preload=none src='/movie?duration=2'></video>");
        var video = Media<HtmlVideoElement>(document);
        Advance(0.1);
        Assert.Empty(_requests);
        Assert.Equal(["loadstart", "suspend"], TakeEvents());
        Assert.Equal(HtmlMediaElement.NetworkIdle, video.NetworkState);
        _ = video.Play();
        Advance(0.1);
        Assert.Single(_requests);
        Assert.False(video.Paused);
        Assert.Contains("playing", TakeEvents());
    }

    [Fact]
    public void Stalled_IsFiredWhenNoDataIsReceived()
    {
        var document = Load("<video></video><style>video:stalled { color: red }</style>");
        var video = Media<HtmlVideoElement>(document);
        _delays["/movie"] = TimeSpan.FromSeconds(10);
        video.Src = "/movie?duration=2";
        _ = video.Play();
        Advance(4);
        Assert.Equal(["play", "waiting", "loadstart", "stalled"], TakeEvents());
        Assert.True(video.Matches(":buffering"));
        Assert.True(video.Matches(":stalled"));
        Assert.True(video.Matches(":playing"));
        Advance(7);
        Assert.False(video.Matches(":buffering"));
        Assert.False(video.Matches(":stalled"));
        Assert.Contains("playing", TakeEvents());
    }

    [Fact]
    public void PseudoClasses_FollowTheState()
    {
        var document = Load("<video src='/movie?duration=10'></video>");
        var video = Media<HtmlVideoElement>(document);
        Advance(0.1);
        Assert.True(video.Matches(":paused"));
        Assert.False(video.Matches(":playing"));
        Assert.False(video.Matches(":muted"));
        _ = video.Play();
        Assert.True(video.Matches(":playing"));
        video.CurrentTime = 5;
        Assert.True(video.Matches(":seeking"));
        Advance(0.1);
        Assert.False(video.Matches(":seeking"));
        video.Muted = true;
        Assert.True(video.Matches(":muted"));
    }

    [Fact]
    public void VolumeAndRate_FireEventsAndAreGivenToThePlayer()
    {
        var backend = new FakeMediaBackend();
        var document = Load("<video src='/movie?duration=10'></video>", backend);
        var video = Media<HtmlVideoElement>(document);
        Advance(0.1);
        TakeEvents();
        var player = backend.Players[0];
        Assert.Equal(1, player.Volume);
        video.Volume = 0.5;
        Assert.Equal(0.5, player.Volume);
        video.Muted = true;
        Assert.Equal(0, player.Volume);
        video.PlaybackRate = 2;
        video.DefaultPlaybackRate = 2;
        Advance(0.1);
        Assert.Equal(["volumechange", "volumechange", "ratechange", "ratechange"], TakeEvents());
        Assert.Throws<DomException>(() => video.Volume = 2);

        // The position advances at the playback rate
        _ = video.Play();
        Advance(2);
        Assert.InRange(video.CurrentTime, 3.8, 4.2);
    }

    [Fact]
    public void PlayerNotifications_UpdateTheReadyStateAndTheErrors()
    {
        var backend = new FakeMediaBackend();
        var document = Load("<video src='/movie?duration=10&state=1'></video>", backend);
        var video = Media<HtmlVideoElement>(document);
        Advance(0.1);
        Assert.Equal(HtmlMediaElement.HaveMetadata, video.ReadyState);
        Assert.Equal(["loadstart", "durationchange", "resize", "loadedmetadata", "progress", "suspend"], TakeEvents());
        var player = backend.Players[0];
        _ = video.Play();
        player.State = MediaReadyState.HaveFutureData;
        player.Notify();
        Advance(0.1);
        Assert.Equal(["play", "waiting", "loadeddata", "canplay", "playing"], TakeEvents("timeupdate"));

        // The player waits for data
        player.State = MediaReadyState.HaveCurrentData;
        player.Notify();
        Advance(0.1);
        Assert.Equal(["waiting"], TakeEvents("timeupdate"));
        var position = video.CurrentTime;
        Advance(1);
        Assert.Equal(position, video.CurrentTime);

        player.FakeError = MediaPlayerError.Decode;
        player.Notify();
        Advance(0.1);
        Assert.Equal(["error"], TakeEvents());
        Assert.Equal((MediaError.MediaErrDecode, "fake error"), (video.Error!.Code, video.Error.Message));
    }

    [Fact]
    public async Task StreamedResponse_IsProcessedAsItsChunksArrive()
    {
        var document = Load("<video></video>");
        var video = Media<HtmlVideoElement>(document);
        video.Src = "/stream";
        _ = video.Play();
        Advance(0.1);
        Assert.Equal(["play", "waiting", "loadstart"], TakeEvents());
        Assert.Equal(HtmlMediaElement.HaveNothing, video.ReadyState);

        // The metadata arrives: the player only has the metadata until the resource is complete
        await SendAsync("IA duration=4 width=10 height=20 state=1\n");
        Assert.Equal(["durationchange", "resize", "loadedmetadata"], TakeEvents());
        Assert.Equal((HtmlMediaElement.HaveMetadata, HtmlMediaElement.NetworkLoading, 4d), (video.ReadyState, video.NetworkState, video.Duration));
        Assert.True(video.Matches(":buffering"));

        // No data for 3 seconds: the element is stalled
        Advance(3.5);
        Assert.Equal(["stalled"], TakeEvents());
        Assert.True(video.Matches(":stalled"));
        await SendAsync("data");
        Assert.Equal(["progress"], TakeEvents());
        Assert.False(video.Matches(":stalled"));

        // The end of the response
        _stream!.Writer.Complete();
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Advance(0.1);
        Assert.Equal(["loadeddata", "canplay", "playing", "canplaythrough", "progress", "suspend"], TakeEvents("timeupdate"));
        Assert.Equal((HtmlMediaElement.HaveEnoughData, HtmlMediaElement.NetworkIdle), (video.ReadyState, video.NetworkState));

        // The chunk is processed by a task queued from another thread; the event loop is not idle while the response is not complete
        async Task SendAsync(string chunk)
        {
            var count = _events.Count;
            await _stream!.Writer.WriteAsync(Encoding.UTF8.GetBytes(chunk), TestContext.Current.CancellationToken);
            for (var i = 0; i < 1000 && _events.Count == count; i++)
            {
                await Task.Delay(5, TestContext.Current.CancellationToken);
                _contexts.EventLoop.RunUntilIdle();
            }
        }
    }

    [Fact]
    public void PartialResponses_AreCompletedWithRangeRequests()
    {
        var document = Load("<video src='/ranged'></video>");
        var video = Media<HtmlVideoElement>(document);
        Advance(0.1);
        Assert.Equal(["Video https://example.com/ranged bytes=0-", "Video https://example.com/ranged bytes=16-", "Video https://example.com/ranged bytes=32-", "Video https://example.com/ranged bytes=48-"], _requests);
        Assert.Equal((6d, 12u, HtmlMediaElement.HaveEnoughData, HtmlMediaElement.NetworkIdle), (video.Duration, video.VideoWidth, video.ReadyState, video.NetworkState));
        Assert.Null(video.Error);
    }

    [Fact]
    public void MediaDocument_ContainsAVideoElementThatUsesTheResponseOfTheNavigation()
    {
        var context = _contexts.Create(new BrowsingContextOptions { DocumentLoader = new MediaLoader(this), MediaBackend = new FakeMediaBackend(), TimeProvider = _clock });
        var document = context.Navigate("https://example.com/movie", _ => new DocumentResource(Encoding.UTF8.GetBytes("MEDIA duration=3 width=8 height=4")) { Headers = [new("Content-Type", "video/fake")] });
        Advance(0.5);
        var video = Assert.IsType<HtmlVideoElement>(Assert.Single(document.Body!.ChildNodes));
        Assert.Equal("video/fake", document.ContentType);
        Assert.Equal("CSS1Compat", document.CompatMode);
        Assert.Equal("https://example.com/movie", video.CurrentSrc);
        Assert.Equal((3d, 8u), (video.Duration, video.VideoWidth));
        Assert.False(video.Paused);

        // The resource is not fetched again
        Assert.Empty(_requests);
    }

    [Fact]
    public void SrcObject_PlaysABlob()
    {
        var document = Load("<video></video>");
        var video = Media<HtmlVideoElement>(document);
        video.SrcObject = new Blob([Encoding.UTF8.GetBytes("MEDIA duration=7 width=10 height=20")], new BlobPropertyBag { Type = "video/fake" });
        Advance(0.1);
        Assert.Empty(_requests);
        Assert.Equal("", video.CurrentSrc);
        Assert.Equal(7, video.Duration);
        Assert.Equal((10u, 20u), (video.VideoWidth, video.VideoHeight));
    }

    [Fact]
    public void RemovedFromTheDocument_IsPaused()
    {
        var document = Load("<video src='/movie?duration=10'></video>");
        var video = Media<HtmlVideoElement>(document);
        Advance(0.1);
        _ = video.Play();
        Advance(0.5);
        TakeEvents();
        video.Remove();
        Assert.False(video.Paused);
        Advance(0.1);
        Assert.True(video.Paused);
        Assert.Equal(["timeupdate", "pause"], TakeEvents());
    }

    [Fact]
    public void DetachedDocument_LoadsAndPlays()
    {
        var document = Load("<p>");
        var detached = document.Implementation.CreateHtmlDocument("");
        var video = (HtmlVideoElement)detached.CreateElement("video");
        Watch(video);
        detached.Body!.AppendChild(video);
        video.Src = "https://example.com/movie?duration=10";
        _ = video.Play();
        Advance(1);
        Assert.False(video.Paused);
        Assert.InRange(video.CurrentTime, 0.5, 1.1);
    }

    [Fact]
    public void Video_NaturalSizeIsTheOneOfTheVideo()
    {
        var document = Load("<video></video>");
        var video = Media<HtmlVideoElement>(document);
        var window = document.DefaultView!;
        video.Src = "/movie?duration=10&width=320&height=240";
        Assert.Equal("300px", window.GetComputedStyle(video).GetPropertyValue("width"));
        Advance(0.1);
        Assert.Equal("320px", window.GetComputedStyle(video).GetPropertyValue("width"));
        Assert.Equal("240px", window.GetComputedStyle(video).GetPropertyValue("height"));
        video.RemoveAttribute("src");
        video.Load();
        Assert.Equal("150px", window.GetComputedStyle(video).GetPropertyValue("height"));
    }

    [Fact]
    public void AudioTrack_EnabledFiresChange()
    {
        var document = Load("<video src='/movie?duration=10&audio=2&video=2'></video>");
        var video = Media<HtmlVideoElement>(document);
        var changes = new List<string>();
        video.AudioTracks.AddEventListener("change", _ => changes.Add("audio"));
        video.VideoTracks.AddEventListener("change", _ => changes.Add("video"));
        Advance(0.1);
        Assert.Equal([true, false], video.AudioTracks.Select(track => track.Enabled));
        video.AudioTracks[1].Enabled = true;
        video.VideoTracks[1].Selected = true;
        Advance(0.1);
        Assert.Equal(["audio", "video"], changes);
        Assert.Equal([true, true], video.AudioTracks.Select(track => track.Enabled));
        Assert.Equal(1, video.VideoTracks.SelectedIndex);
    }
}
