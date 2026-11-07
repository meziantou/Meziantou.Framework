using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Media;
using Meziantou.Framework.WebPlatform.WebVtt;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>
/// The text tracks of the media elements (https://html.spec.whatwg.org/multipage/media.html#timed-text-tracks) and WebVTT
/// (https://w3c.github.io/webvtt/): the parsers, the cues, the track elements, the cue events and the rendering of the cues
/// over a video element, with a fake media backend and a fake clock.
/// </summary>
public sealed class TextTrackTests : IDisposable
{
    private const string Captions = """
        WEBVTT

        first
        00:00:01.000 --> 00:00:02.000
        One

        second
        00:00:02.500 --> 00:00:03.000 line:0 align:start
        Two

        """;

    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal) { ["/captions.vtt"] = Captions };
    private readonly List<string> _requests = [];
    private readonly List<string> _events = [];

    public void Dispose() => _contexts.Dispose();

    private Document Load(string html, WebPlatformFeatures? features = null, MediaBackend? backend = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            DocumentLoader = new Loader(this),
            MediaBackend = backend ?? new FakeMediaBackend(),
            TimeProvider = _clock,
            Features = features,
            Viewport = new ViewportOptions { Width = 800, Height = 600 },
        });

        var document = context.Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    /// <summary>Runs the tasks and the timers of the event loop for a duration of the fake clock.</summary>
    private void Advance(double seconds)
    {
        var eventLoop = _contexts.EventLoop;
        var remaining = TimeSpan.FromSeconds(seconds);
        while (remaining > TimeSpan.Zero)
        {
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

    private string[] TakeEvents()
    {
        var events = _events.ToArray();
        _events.Clear();
        return events;
    }

    private void WatchCues(TextTrack track)
    {
        track.AddEventListener("cuechange", _ => _events.Add("cuechange"));
        foreach (var cue in track.CueList)
        {
            cue.AddEventListener("enter", _ => _events.Add("enter " + cue.Id));
            cue.AddEventListener("exit", _ => _events.Add("exit " + cue.Id));
        }
    }

    private static List<VttCue> Parse(string text, List<VttRegion>? regions = null, List<string>? styleSheets = null)
    {
        var cues = new List<VttCue>();
        Assert.True(WebVttParser.Parse(Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n")), cues.Add, regions, styleSheets is null ? null : styleSheets.Add));
        return cues;
    }

    private sealed class Loader(TextTrackTests test) : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            test._requests.Add(request.Destination + " " + request.Mode + " " + request.Url);
            var url = new Uri(request.Url);
            if (url.AbsolutePath.StartsWith("/movie", StringComparison.Ordinal))
                return ValueTask.FromResult<DocumentResource?>(new DocumentResource(Encoding.UTF8.GetBytes("MEDIA " + url.Query.TrimStart('?'))));

            if (test._files.TryGetValue(url.AbsolutePath, out var content))
                return ValueTask.FromResult<DocumentResource?>(new DocumentResource(Encoding.UTF8.GetBytes(content.ReplaceLineEndings("\n"))) { Headers = [new("Content-Type", "text/vtt")] });

            return ValueTask.FromResult<DocumentResource?>(new DocumentResource("not found") { StatusCode = 404 });
        }
    }

    private sealed class FakeMediaBackend : MediaBackend
    {
        public List<FakeMediaPlayer> Players { get; } = [];

        public IReadOnlyList<MediaTextTrackInfo> TextTracks { get; init; } = [];

        public override MediaTypeSupport CanPlayType(string mimeType, IReadOnlyList<string> codecs) => MediaTypeSupport.Maybe;

        public override MediaPlayer? CreatePlayer(MediaResource resource)
        {
            var text = Encoding.UTF8.GetString(resource.Content.Span);
            if (!text.StartsWith("MEDIA", StringComparison.Ordinal))
                return null;

            var player = new FakeMediaPlayer(resource, text, TextTracks);
            Players.Add(player);
            return player;
        }
    }

    private sealed class FakeMediaPlayer(MediaResource resource, string description, IReadOnlyList<MediaTextTrackInfo> textTracks) : MediaPlayer(resource.TimeProvider)
    {
        public bool IsDisposed { get; private set; }

        public override double Duration => double.Parse(description.Split("duration=")[1], CultureInfo.InvariantCulture);

        public override int VideoWidth => 320;

        public override int VideoHeight => 180;

        public override IReadOnlyList<MediaTextTrackInfo> TextTracks => textTracks;

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The WebVTT parser
    // ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Parser_CuesIdentifiersAndSettings()
    {
        var cues = Parse("""
            WEBVTT - a header

            NOTE a comment
            that spans two lines

            1
            00:01.000 --> 00:02.500 line:10%,center position:20%,line-left size:30% align:left vertical:rl
            Hello
            world

            01:00:00.000 --> 01:00:01.000 line:-2 position:auto size:150% unknown:value
            Second

            """);

        Assert.Equal(2, cues.Count);
        var first = cues[0];
        Assert.Equal(("1", 1.0, 2.5, "Hello\nworld"), (first.Id, first.StartTime, first.EndTime, first.Text));
        Assert.Equal((10.0, false, LineAlignSetting.Center), (first.Line.Number, first.SnapToLines, first.LineAlign));
        Assert.Equal((20.0, PositionAlignSetting.LineLeft, 30.0), (first.Position.Number, first.PositionAlign, first.Size));
        Assert.Equal((AlignSetting.Left, DirectionSetting.Rl), (first.Align, first.Vertical));

        // The settings that are not valid are ignored
        var second = cues[1];
        Assert.Equal(("", 3600.0, 3601.0), (second.Id, second.StartTime, second.EndTime));
        Assert.Equal((-2.0, true), (second.Line.Number, second.SnapToLines));
        Assert.True(second.Position.IsAuto);
        Assert.Equal(100, second.Size);
    }

    [Fact]
    public void Parser_RecoversFromInvalidBlocks()
    {
        var cues = Parse("""
            WEBVTT
            this line is a part of the header
            00:00.000 --> 00:01.000
            A
            00:01.000 --> 00:02.000
            B

            not an identifier
            00:02.000 -> 00:03.000
            ignored: the timings are not valid

            00:60.000 --> 00:61.000
            ignored: the seconds are greater than 59

            C
            00:03.000 --> 00:04.000 line:abc
            <b>bold</b> &amp; text
            """);

        Assert.Equal(["A", "B", "<b>bold</b> &amp; text"], cues.Select(cue => cue.Text));
        Assert.Equal(["", "", "C"], cues.Select(cue => cue.Id));
        Assert.True(cues[2].Line.IsAuto);
    }

    [Theory]
    [InlineData("WEBVTT", true)]
    [InlineData("WEBVTT\tcomment\n", true)]
    [InlineData("\U0000FEFFWEBVTT\n\n00:00.000 --> 00:01.000\nA", true)]
    [InlineData("WEBVT", false)]
    [InlineData("WEBVTTT\n", false)]
    [InlineData(" WEBVTT\n", false)]
    [InlineData("", false)]
    public void Parser_FileSignature(string text, bool expected)
    {
        Assert.Equal(expected, WebVttParser.Parse(Encoding.UTF8.GetBytes(text), _ => { }));
    }

    [Fact]
    public void Parser_RegionsAndStyleSheetsPrecedeTheCues()
    {
        var regions = new List<VttRegion>();
        var styleSheets = new List<string>();
        var cues = Parse("""
            WEBVTT

            REGION
            id:fred width:40% lines:5
            regionanchor:10%,20% viewportanchor:30%,40% scroll:up

            STYLE
            ::cue { color: lime }

            00:00.000 --> 00:01.000 region:fred
            In the region

            STYLE
            ::cue { color: red }

            00:01.000 --> 00:02.000 region:fred line:1
            Not in the region: it has a line
            """, regions, styleSheets);

        var region = Assert.Single(regions);
        Assert.Equal(("fred", 40.0, 5u, ScrollSetting.Up), (region.Id, region.Width, region.Lines, region.Scroll));
        Assert.Equal((10.0, 20.0, 30.0, 40.0), (region.RegionAnchorX, region.RegionAnchorY, region.ViewportAnchorX, region.ViewportAnchorY));
        Assert.Equal(["::cue { color: lime }"], styleSheets);

        // A STYLE block that follows a cue is a cue without timings: it is ignored
        Assert.Equal(2, cues.Count);
        Assert.Same(region, cues[0].Region);
        Assert.Null(cues[1].Region);
    }

    [Fact]
    public void Parser_IsIncremental()
    {
        var bytes = Encoding.UTF8.GetBytes("WEBVTT\r\n\r\nid\r00:00.000 --> 00:01.000\r\n\U000000E9t\U000000E9\0\n\n00:01.000 --> 00:02.000\nlast");
        var expected = new List<VttCue>();
        Assert.True(WebVttParser.Parse(bytes, expected.Add));

        // The same cues are parsed when the bytes arrive one at a time (a line terminator and a character may be split)
        var cues = new List<VttCue>();
        var parser = new WebVttParser(cues.Add);
        foreach (var value in bytes)
        {
            parser.Append([value]);
        }

        // The last cue is returned when the stream ends
        Assert.Single(cues);
        parser.Complete();
        Assert.False(parser.HasFailed);
        Assert.Equal(["\U000000E9t\U000000E9\U0000FFFD", "last"], cues.Select(cue => cue.Text));
        Assert.Equal(expected.Select(cue => (cue.Id, cue.Text, cue.StartTime)), cues.Select(cue => (cue.Id, cue.Text, cue.StartTime)));
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The cues
    // ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void GetCueAsHtml_BuildsTheNodesOfTheCueText()
    {
        var document = Load("<p></p>");
        var cue = new VttCue(0, 1, "<v.loud Bob &amp; Al>Hi <i>there</i></v> <00:00:01.500><lang fr><c.a.b>x</c></lang><ruby>base<rt>text</ruby><unknown>&lt;&nbsp;&bogus;");
        var fragment = cue.GetCueAsHtml(document);
        var container = document.CreateElement("div");
        container.AppendChild(fragment);
        Assert.Equal(
            "<span title=\"Bob &amp; Al\" class=\"loud\">Hi <i>there</i></span> <span lang=\"fr\"><span class=\"a b\">x</span></span><ruby>base<rt>text</rt></ruby>&lt;&nbsp;&amp;bogus;",
            container.InnerHtml.Replace("<?timestamp 00:00:01.500?>", "", StringComparison.Ordinal).Replace("<?timestamp 00:00:01.500>", "", StringComparison.Ordinal));

        // A timestamp is a processing instruction
        var timestamp = Assert.IsType<ProcessingInstruction>(container.ChildNodes[2]);
        Assert.Equal(("timestamp", "00:00:01.500"), (timestamp.Target, timestamp.Data));
        Assert.Same(document, container.FirstChild!.OwnerDocument);
    }

    [Fact]
    public void VttCue_ValidatesItsAttributes()
    {
        var cue = new VttCue(1, double.PositiveInfinity, "text");
        Assert.Equal(double.PositiveInfinity, cue.EndTime);
        Assert.True(cue.Line.IsAuto);
        Assert.True(cue.Position.IsAuto);
        Assert.Equal((100.0, AlignSetting.Center, PositionAlignSetting.Auto, true), (cue.Size, cue.Align, cue.PositionAlign, cue.SnapToLines));

        Assert.Throws<ArgumentException>(() => new VttCue(0, double.NaN, ""));
        Assert.Throws<ArgumentException>(() => cue.EndTime = double.NegativeInfinity);
        Assert.Equal("IndexSizeError", Assert.Throws<DomException>(() => cue.Position = 101).Name);
        Assert.Equal("IndexSizeError", Assert.Throws<DomException>(() => cue.Size = -1).Name);

        cue.Line = -1000;
        cue.Position = 25.5;
        cue.Position = AutoKeyword.Auto;
        Assert.Equal(-1000, cue.Line.Number);
        Assert.True(cue.Position.IsAuto);

        var region = new VttRegion();
        Assert.Equal(("", 100.0, 3u, 0.0, 100.0, 0.0, 100.0), (region.Id, region.Width, region.Lines, region.RegionAnchorX, region.RegionAnchorY, region.ViewportAnchorX, region.ViewportAnchorY));
        Assert.Equal("IndexSizeError", Assert.Throws<DomException>(() => region.Width = 100.5).Name);
    }

    [Fact]
    public void VttCue_ComputedValues()
    {
        var cue = new VttCue(0, 1, "");
        Assert.Equal((-1.0, 50.0, PositionAlignSetting.Center), (cue.ComputedLine, cue.ComputedPosition, cue.GetComputedPositionAlign(isLeftToRight: true)));

        cue.Align = AlignSetting.Start;
        Assert.Equal((50.0, PositionAlignSetting.LineLeft, PositionAlignSetting.LineRight), (cue.ComputedPosition, cue.GetComputedPositionAlign(isLeftToRight: true), cue.GetComputedPositionAlign(isLeftToRight: false)));

        cue.Align = AlignSetting.Right;
        Assert.Equal((100.0, PositionAlignSetting.LineRight), (cue.ComputedPosition, cue.GetComputedPositionAlign(isLeftToRight: true)));

        // The line of a cue that does not snap to lines is a percentage: auto and the values out of range are 100
        cue.SnapToLines = false;
        Assert.Equal(100, cue.ComputedLine);
        cue.Line = 120;
        Assert.Equal(100, cue.ComputedLine);
        cue.Line = 30;
        Assert.Equal(30, cue.ComputedLine);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The track elements and the list of text tracks
    // ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Track_IsLoadedWhenItsModeIsNotDisabled()
    {
        var document = Load("<video><track id=t kind=captions srclang=en label=English src='/captions.vtt'></video>");
        var element = (HtmlTrackElement)document.GetElementById("t")!;
        var video = (HtmlVideoElement)document.QuerySelector("video")!;
        var track = element.Track;
        element.AddEventListener("load", _ => _events.Add("load"));
        element.AddEventListener("error", _ => _events.Add("error"));

        // A track that is not a default track is disabled: its resource is not fetched
        Assert.Same(track, Assert.Single(video.TextTracks));
        Assert.Same(track, video.TextTracks.GetTrackById("t"));
        Assert.Equal((TextTrackKind.Captions, "English", "en", TextTrackMode.Disabled), (track.Kind, track.Label, track.Language, track.Mode));
        Assert.Equal(HtmlTrackElement.None, element.ReadyState);
        Assert.Null(track.Cues);
        Assert.Empty(_requests);

        var changes = 0;
        video.TextTracks.AddEventListener("change", _ => changes++);
        track.Mode = TextTrackMode.Hidden;
        Assert.Equal(HtmlTrackElement.None, element.ReadyState);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["load"], TakeEvents());
        Assert.Equal(1, changes);
        Assert.Equal(HtmlTrackElement.Loaded, element.ReadyState);

        // The request is a same-origin request without the crossorigin attribute of the media element
        Assert.Equal(["Track SameOrigin https://example.com/captions.vtt"], _requests);
        Assert.Equal(["first", "second"], track.Cues!.Select(cue => cue.Id));
        Assert.Same(track, track.Cues![0].Track);
        Assert.Same(track.Cues[1], track.Cues.GetCueById("second"));

        // The list of cues is emptied when the src attribute changes, and the new resource is fetched
        element.Src = "/missing.vtt";
        Assert.Empty(track.Cues);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["error"], TakeEvents());
        Assert.Equal(HtmlTrackElement.Error, element.ReadyState);

        // The track is removed from the list when its element is removed from the media element
        var removed = 0;
        video.TextTracks.AddEventListener("removetrack", e => removed += ReferenceEquals(((TrackEvent)e).Track, track) ? 1 : 0);
        element.Remove();
        Assert.Empty(video.TextTracks);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(1, removed);
    }

    [Fact]
    public void DefaultTrack_IsShowingAndUsesTheCorsModeOfTheMediaElement()
    {
        var document = Load("<video crossorigin><track kind=metadata default src='/captions.vtt'><track default src='/captions.vtt'><track default src='/captions.vtt'></video>");
        var video = (HtmlVideoElement)document.QuerySelector("video")!;

        // The first default subtitles track is showing, the default metadata tracks are hidden
        Assert.Equal([TextTrackMode.Hidden, TextTrackMode.Showing, TextTrackMode.Disabled], video.TextTracks.Select(track => track.Mode));
        Assert.Equal(["Track Cors https://example.com/captions.vtt", "Track Cors https://example.com/captions.vtt"], _requests);
        Assert.Equal(2, video.TextTracks[0].Cues!.Count);
    }

    [Fact]
    public void TextTracksEnabled_DisablesTheLoadingOfTheTracks()
    {
        var document = Load("<video><track default src='/captions.vtt'></video>", new WebPlatformFeatures { TextTracksEnabled = false });
        var video = (HtmlVideoElement)document.QuerySelector("video")!;
        Assert.Equal(TextTrackMode.Disabled, video.TextTracks[0].Mode);
        video.TextTracks[0].Mode = TextTrackMode.Showing;
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_requests);
    }

    [Fact]
    public void AddTextTrack_CuesAreInTextTrackCueOrder()
    {
        var document = Load("<video></video>");
        var video = (HtmlVideoElement)document.QuerySelector("video")!;
        var added = new List<object?>();
        video.TextTracks.AddEventListener("addtrack", e => added.Add(((TrackEvent)e).Track));
        var track = video.AddTextTrack(TextTrackKind.Chapters, "Chapters", "fr");
        Assert.Equal((TextTrackKind.Chapters, "Chapters", "fr", TextTrackMode.Hidden), (track.Kind, track.Label, track.Language, track.Mode));
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal([track], added);

        // The cues are sorted by their start time, then by their end time (the latest first), then in the order they were added
        var a = new VttCue(2, 3, "a");
        var b = new VttCue(1, 2, "b");
        var c = new VttCue(1, 5, "c");
        var d = new VttCue(1, 2, "d");
        foreach (var cue in (VttCue[])[a, b, c, d])
        {
            track.AddCue(cue);
        }

        Assert.Equal([c, b, d, a], track.Cues!);
        a.StartTime = 0;
        Assert.Equal([a, c, b, d], track.Cues!);

        // A cue is moved from its track to the track it is added to
        var other = video.AddTextTrack(TextTrackKind.Metadata);
        other.AddCue(b);
        Assert.Equal([a, c, d], track.Cues!);
        Assert.Same(other, b.Track);
        Assert.Equal("NotFoundError", Assert.Throws<DomException>(() => track.RemoveCue(b)).Name);
        track.RemoveCue(c);
        Assert.Null(c.Track);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Time marches on
    // ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Playback_FiresTheCueEventsAtTheTimesOfTheCues()
    {
        var document = Load("<video src='/movie?duration=5'><track default src='/captions.vtt'></video>");
        var video = (HtmlVideoElement)document.QuerySelector("video")!;
        var track = video.TextTracks[0];
        WatchCues(track);
        _ = video.Play();
        Advance(0.9);
        Assert.Empty(TakeEvents());
        Assert.Empty(track.ActiveCues!);

        // The timer of the playback is due when a cue starts or ends
        Advance(0.11);
        Assert.Equal(["enter first", "cuechange"], TakeEvents());
        Assert.Equal(["first"], track.ActiveCues!.Select(cue => cue.Id));
        Advance(1.2);
        Assert.Equal(["exit first", "cuechange"], TakeEvents());
        Assert.Empty(track.ActiveCues!);

        // A seek fires the events of the cues of the new position; a disabled track has no active cue
        video.CurrentTime = 2.75;
        Advance(0.01);
        Assert.Equal(["enter second", "cuechange"], TakeEvents());
        track.Mode = TextTrackMode.Disabled;
        Assert.Null(track.ActiveCues);
        track.Mode = TextTrackMode.Hidden;
        Assert.Empty(track.ActiveCues!);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["second"], track.ActiveCues!.Select(cue => cue.Id));
    }

    [Fact]
    public void Playback_MissedCuesAndPauseOnExit()
    {
        var document = Load("<video src='/movie?duration=5'></video>");
        var video = (HtmlVideoElement)document.QuerySelector("video")!;
        var track = video.AddTextTrack(TextTrackKind.Metadata);

        // A cue that starts and ends at the same position is never active: it is missed by the playback
        track.AddCue(new VttCue(0.5, 0.5, "") { Id = "missed" });
        track.AddCue(new VttCue(1, 2, "") { Id = "pause", PauseOnExit = true });
        WatchCues(track);
        _ = video.Play();
        Advance(1.5);
        Assert.Equal(["enter missed", "exit missed", "cuechange", "enter pause", "cuechange"], TakeEvents());
        Assert.False(video.Paused);

        // The playback pauses when it goes past the end of a cue whose pause-on-exit flag is set
        Advance(1);
        Assert.Equal(["exit pause", "cuechange"], TakeEvents());
        Assert.True(video.Paused);
        Assert.Equal(2, video.CurrentTime, 2);
    }

    [Fact]
    public void MediaResourceTextTracks_AreExposed()
    {
        var backend = new FakeMediaBackend
        {
            TextTracks = [new MediaTextTrackInfo(TextTrackKind.Metadata, [new MediaTextTrackCue(1, 2, "data", "id1", "line:0")], Id: "t1", Label: "Data", Language: "en", InBandMetadataTrackDispatchType: "codec")],
        };

        var document = Load("<video src='/movie?duration=5'></video>", backend: backend);
        var video = (HtmlVideoElement)document.QuerySelector("video")!;
        var track = Assert.Single(video.TextTracks);
        Assert.Equal(("t1", TextTrackKind.Metadata, "Data", "en", "codec", TextTrackMode.Disabled), (track.Id, track.Kind, track.Label, track.Language, track.InBandMetadataTrackDispatchType, track.Mode));
        track.Mode = TextTrackMode.Hidden;
        var cue = Assert.IsType<VttCue>(Assert.Single(track.Cues!));
        Assert.Equal(("id1", 1.0, 2.0, "data", 0.0), (cue.Id, cue.StartTime, cue.EndTime, cue.Text, cue.Line.Number));

        // The tracks of the media resource are forgotten when another resource is loaded
        video.Src = "/missing";
        Assert.Empty(video.TextTracks);
    }

    [Fact]
    public void DestroyedDocument_ReleasesThePlayers()
    {
        var backend = new FakeMediaBackend();
        var document = Load("<iframe srcdoc=\"<video src='/movie?duration=5'></video>\"></iframe>", backend: backend);
        var iframe = (HtmlIFrameElement)document.QuerySelector("iframe")!;
        Advance(0.1);
        var video = (HtmlVideoElement)iframe.ContentDocument!.QuerySelector("video")!;
        _ = video.Play();
        Advance(0.5);
        var player = Assert.Single(backend.Players);
        Assert.False(player.IsDisposed);

        // The playback stops at once when the document of the element is no longer fully active
        iframe.Remove();
        Assert.True(player.IsDisposed);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The rendering of the cues
    // ----------------------------------------------------------------------------------------------------------------

    private (HtmlVideoElement Video, TextTrack Track) LoadVideoWithCues(string style, params VttCue[] cues)
    {
        var document = Load("<!DOCTYPE html><style>body { margin: 0 } video { width: 320px; height: 180px; vertical-align: top } " + style + "</style><video src='/movie?duration=5'></video>");
        var video = (HtmlVideoElement)document.QuerySelector("video")!;
        var track = video.AddTextTrack(TextTrackKind.Subtitles);
        foreach (var cue in cues)
        {
            track.AddCue(cue);
        }

        track.Mode = TextTrackMode.Showing;
        video.CurrentTime = 1;
        Advance(0.1);
        return (video, track);
    }

    /// <summary>Returns the rectangles of the boxes of the cues and of the regions rendered over a video element, relative to the video.</summary>
    private static PhysicalRect[] GetCueRects(HtmlVideoElement video)
    {
        var engine = video.OwnerDocument!.GetLayoutEngine();
        engine.EnsureLayout();
        var origin = engine.GetBorderBoxRect(video)!.Value;
        return [.. video.CueDisplay!.Items.Where(item => item.Cue?.DisplayState?.IsIgnored != true).Select(item => engine.GetBorderBoxRect(item.Element)).Where(rect => rect is not null).Select(rect => rect!.Value.Translate(new PhysicalOffset(-origin.X, -origin.Y)))];
    }

    [Fact]
    public void Rendering_CuesAreStackedFromTheBottomOfTheVideo()
    {
        var (video, track) = LoadVideoWithCues("", new VttCue(0, 5, "first"), new VttCue(0, 5, "second"), new VttCue(2, 5, "not active"), new VttCue(0, 5, ""));
        var rects = GetCueRects(video);

        // The cue without text has no line box: it is ignored
        Assert.Equal(2, rects.Length);
        Assert.Equal((0, 320, 180), (rects[0].X, rects[0].Width, rects[0].Bottom));

        // The font is 5vh of the video viewport: a line is taller than 9px
        Assert.True(rects[0].Height >= 9, rects[0].ToString());

        // The second cue is moved up, line by line, until it does not overlap the first one
        Assert.Equal(rects[0].Top, rects[1].Bottom, 3);

        // The cues keep their position while they are displayed: the second cue does not move when the first one is removed
        track.RemoveCue(track.Cues![0]);
        Assert.Equal([rects[1]], GetCueRects(video));

        // A track that is not showing is not rendered
        track.Mode = TextTrackMode.Hidden;
        Assert.Empty(GetCueRects(video));
    }

    [Fact]
    public void Rendering_SettingsOfTheCues()
    {
        var top = new VttCue(0, 5, "top") { Line = 0, Size = 50, Position = 0, PositionAlign = PositionAlignSetting.LineLeft };
        var percent = new VttCue(0, 5, "percent") { SnapToLines = false, Line = 50, LineAlign = LineAlignSetting.Center, Size = 50, Position = 100, PositionAlign = PositionAlignSetting.LineRight };
        var vertical = new VttCue(0, 5, "vertical") { Vertical = DirectionSetting.Lr, Line = 0, Size = 40, Position = 100, PositionAlign = PositionAlignSetting.LineRight };
        var (video, _) = LoadVideoWithCues("", top, percent, vertical);
        var rects = GetCueRects(video);
        Assert.Equal(3, rects.Length);

        // line:0 is the first line of the video viewport; the size and the position are percentages of its width
        Assert.Equal((0, 0, 160), (rects[0].X, rects[0].Y, rects[0].Width));

        // A cue that does not snap to lines: its line is a percentage, and the box is centered at the line
        Assert.Equal((160, 160), (rects[1].X, rects[1].Width));
        Assert.Equal(90, rects[1].Y + (rects[1].Height / 2), 3);

        // A vertical cue: its size and its position are percentages of the height of the video viewport, its first line is
        // at the left
        Assert.Equal((0, 108, 72), (rects[2].X, rects[2].Y, rects[2].Height));
    }

    [Fact]
    public void Rendering_CueStyles()
    {
        var cue = new VttCue(0, 5, "plain <b>bold</b> <c.red>class</c> <00:00:02.000><i>future</i>") { Id = "cue1" };
        var (video, _) = LoadVideoWithCues("""
            video { color: rgb(1, 2, 3); letter-spacing: 3px }
            ::cue { color: rgb(0, 128, 0); background-color: rgb(9, 9, 9); position: static; font-size: 20px }
            video::cue(b) { color: rgb(0, 0, 255); font-size: 10px }
            ::cue(#cue1) { opacity: 0.5 }
            ::cue(:future) { color: rgb(7, 7, 7); font-size: 30px }
            div::cue(i) { color: red }
            """, cue);
        Assert.Single(GetCueRects(video));
        var engine = video.OwnerDocument!.GetStyleEngine();
        var root = video.CueDisplay!.Items[0].Element;

        // The properties of ::cue apply to the root of the cue, except its background (the cue background box); the other
        // properties are ignored, and the inherited properties are inherited from the video element
        var rootStyle = engine.GetStyle(root);
        Assert.Equal(("rgb(0, 128, 0)", "absolute", "20px", "3px", "0.5"), (rootStyle.GetComputedValue("color"), rootStyle.GetComputedValue("position"), rootStyle.GetComputedValue("font-size"), rootStyle.GetComputedValue("letter-spacing"), rootStyle.GetComputedValue("opacity")));

        // The background is computed on the root, and painted by the cue background box that inherits it
        Assert.Equal("rgb(9, 9, 9)", rootStyle.GetComputedValue("background-color"));
        Assert.Equal("rgb(9, 9, 9)", engine.GetStyle(root, WebVttElement.BackgroundBoxPseudoElement).GetComputedValue("background-color"));

        // ::cue() matches the WebVTT node objects; the default classes set the color
        var elements = root.Children.ToArray();
        Assert.Equal(["b", "c", "i"], elements.Select(element => element.LocalName));
        Assert.Equal(("rgb(0, 0, 255)", "10px", "700"), (engine.GetStyle(elements[0]).GetComputedValue("color"), engine.GetStyle(elements[0]).GetComputedValue("font-size"), engine.GetStyle(elements[0]).GetComputedValue("font-weight")));
        Assert.Equal("rgb(255, 0, 0)", engine.GetStyle(elements[1]).GetComputedValue("color"));

        // The node that follows a timestamp is in the future until the playback reaches the timestamp; the font properties
        // do not apply to a selector with :future
        Assert.Equal(("rgb(7, 7, 7)", "20px", "italic"), (engine.GetStyle(elements[2]).GetComputedValue("color"), engine.GetStyle(elements[2]).GetComputedValue("font-size"), engine.GetStyle(elements[2]).GetComputedValue("font-style")));
        video.CurrentTime = 3;
        Advance(0.1);
        Assert.Equal("rgb(0, 128, 0)", engine.GetStyle(elements[2]).GetComputedValue("color"));

        // The elements of the cues are not exposed: they are not hit
        Assert.Same(video, video.OwnerDocument.ElementFromPoint(160, 170));
    }

    [Fact]
    public void Rendering_StyleBlocksOfTheWebVttFile()
    {
        _files["/styled.vtt"] = """
            WEBVTT

            STYLE
            ::cue { color: rgb(0, 128, 0) }
            video::cue { color: rgb(255, 0, 0) }

            00:00.000 --> 00:05.000
            text
            """;
        var document = Load("<!DOCTYPE html><style>::cue { color: rgb(0, 0, 255); opacity: 0.5 }</style><video src='/movie?duration=5'><track default src='/styled.vtt'></video>");
        var video = (HtmlVideoElement)document.QuerySelector("video")!;
        video.CurrentTime = 1;
        Advance(0.1);
        Assert.Single(GetCueRects(video));

        // The rules of the STYLE blocks come after the rules of the document; their originating element has no name
        var style = document.GetStyleEngine().GetStyle(video.CueDisplay!.Items[0].Element);
        Assert.Equal(("rgb(0, 128, 0)", "0.5"), (style.GetComputedValue("color"), style.GetComputedValue("opacity")));
    }

    [Fact]
    public void Rendering_Regions()
    {
        var region = new VttRegion { Id = "r", Width = 50, Lines = 2, RegionAnchorX = 0, RegionAnchorY = 0, ViewportAnchorX = 25, ViewportAnchorY = 10 };
        var (video, _) = LoadVideoWithCues("", new VttCue(0, 5, "first") { Region = region }, new VttCue(0, 5, "second") { Region = region }, new VttCue(0, 5, "third") { Region = region });
        var rect = Assert.Single(GetCueRects(video));

        // The region is 50% of the width of the video viewport, at most 2 lines of 6vh tall, and anchored at (25%, 10%):
        // its last cues are visible (the box of a region that is not full is aligned with the bottom of the region)
        Assert.Equal((80, 160), (rect.X, rect.Width));
        Assert.Equal(21.6, rect.Height, 3);
        Assert.Equal(18, rect.Y, 3);
    }
}
