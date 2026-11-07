using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Media;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>
/// The Media Source Extensions (https://w3c.github.io/media-source/) with a fake media backend whose byte stream format is
/// made of lines: <c>init duration=4 a=1 v=2:320x240</c> is an initialization segment (an audio track 1 and a video track 2),
/// and <c>media 1:0:1:k 2:0.5:0.5</c> is a media segment (track:timestamp:duration, <c>k</c> for a key frame; the audio
/// frames are always key frames).
/// </summary>
public sealed class MediaSourceTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly ScriptEngine _engine = TestScriptEngines.Create();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    private const string Prelude = """
        <video></video>
        <script>
        const log = [];
        const video = document.querySelector('video');
        const source = new MediaSource();
        const encode = text => new TextEncoder().encode(text);
        const ranges = r => { let s = ''; for (let i = 0; i < r.length; i++) s += '[' + r.start(i) + ',' + r.end(i) + ')'; return s; };
        const event = (target, type) => new Promise(resolve => target.addEventListener(type, resolve, { once: true }));
        const append = (buffer, text) => { buffer.appendBuffer(encode(text)); return event(buffer, 'updateend'); };
        const open = () => { video.src = URL.createObjectURL(source); return event(source, 'sourceopen'); };
        for (const type of ['loadedmetadata', 'loadeddata', 'canplay', 'canplaythrough', 'waiting', 'playing', 'seeking', 'seeked', 'ended', 'error', 'durationchange', 'emptied'])
          video.addEventListener(type, () => log.push(type));
        const done = () => document.title = log.join('|');
        </script>
        """;

    private Document Load(string script, MediaBackend? backend = null, WebPlatformFeatures? features = null, bool prelude = true)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            MediaBackend = backend ?? new FakeBackend(),
            TimeProvider = _clock,
            Features = features ?? WebPlatformFeatures.Default,
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });
        var document = context.Navigate("https://example.com/", _ => (prelude ? Prelude : "") + "<script>(async () => {" + script + "})().catch(e => document.title = 'FAILED: ' + e);</script>");
        Advance(0.01);
        Assert.True(_errors.Count == 0, string.Join("\n", _errors.Select(error => error.Message)));
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

    [Fact]
    public void IsTypeSupported_AsksTheBackend()
    {
        var document = Load("""
            log.push(MediaSource.isTypeSupported('video/fake; codecs="f1"'), MediaSource.isTypeSupported('video/fake'), MediaSource.isTypeSupported('video/other; codecs="f1"'), MediaSource.isTypeSupported(''));
            await open();
            try { source.addSourceBuffer('video/other'); } catch (e) { log.push(e.name); }
            try { source.addSourceBuffer(''); } catch (e) { log.push(e.name); }

            // A type without codecs is accepted by addSourceBuffer()
            source.addSourceBuffer('video/fake');
            log.push(source.sourceBuffers.length);
            done();
            """);

        Assert.Equal("true|false|false|false|NotSupportedError|TypeError|1", document.Title);
    }

    [Fact]
    public void WithoutSegmentParser_NoTypeIsSupported()
    {
        var document = Load("log.push(MediaSource.isTypeSupported('video/fake; codecs=\"f1\"')); done();", new BackendWithoutMediaSource());
        Assert.Equal("false", document.Title);
    }

    [Fact]
    public void Attach_OpensTheSourceAndTheSegmentsSetTheReadyState()
    {
        var document = Load("""
            log.push(source.readyState, String(source.duration));
            await open();
            log.push(source.readyState, video.readyState, video.networkState);
            const buffer = source.addSourceBuffer('video/fake; codecs="f1"');
            await append(buffer, 'init duration=4 a=1 v=2:320x240\n');
            log.push('rs=' + video.readyState, 'd=' + video.duration, video.videoWidth + 'x' + video.videoHeight, 'tracks=' + video.audioTracks.length + '/' + video.videoTracks.length, 'active=' + source.activeSourceBuffers.length);
            log.push(buffer.audioTracks[0] === video.audioTracks[0], video.videoTracks[0].sourceBuffer === buffer, video.videoTracks[0].selected);
            await append(buffer, 'media 1:0:1 2:0:1:k 1:1:1 2:1:1\n');
            log.push('buffered=' + ranges(buffer.buffered), 'element=' + ranges(video.buffered), 'seekable=' + ranges(video.seekable));
            await event(video, 'canplaythrough');
            log.push('rs=' + video.readyState);
            done();
            """);

        Assert.Equal("closed|NaN|open|0|2|durationchange|loadedmetadata|rs=1|d=4|320x240|tracks=1/1|active=1|true|true|true|buffered=[0,2)|element=[0,2)|seekable=[0,4)|loadeddata|canplay|canplaythrough|rs=4", document.Title);
    }

    [Fact]
    public void Playback_StopsAtTheEndOfTheBufferedDataAndResumesWhenDataIsAppended()
    {
        var document = Load("""
            await open();
            const buffer = source.addSourceBuffer('video/fake; codecs="f1"');
            await append(buffer, 'init duration=4 v=1:10x10\nmedia 1:0:1:k\n');
            video.play();
            await event(video, 'playing');
            await event(video, 'waiting');
            log.length = 0;
            log.push('t=' + video.currentTime, 'rs=' + video.readyState);
            await append(buffer, 'media 1:1:1:k 1:2:1 1:3:1\n');
            source.endOfStream();
            await event(video, 'ended');
            log.push('t=' + video.currentTime, source.readyState);
            done();
            """);

        Advance(1.5);
        Assert.Equal("", document.Title);
        Advance(5);
        Assert.Equal("t=1|rs=2|canplay|playing|canplaythrough|ended|t=4|ended", document.Title);
    }

    [Fact]
    public void Seek_WaitsForTheDataOfTheNewPosition()
    {
        var document = Load("""
            await open();
            const buffer = source.addSourceBuffer('video/fake; codecs="f1"');
            await append(buffer, 'init duration=10 v=1:10x10\nmedia 1:0:1:k\n');
            await event(video, 'canplaythrough');
            log.length = 0;
            video.currentTime = 5;
            log.push(video.seeking, video.readyState);
            await append(buffer, 'media 1:5:1:k\n');
            log.push(video.seeking);
            await event(video, 'seeked');
            log.push(video.currentTime, ranges(video.buffered));
            done();
            """);

        // The seek completes in a task once the data of the new position is appended
        Assert.Equal("true|1|seeking|false|seeked|5|[0,1)[5,6)", document.Title);
    }

    [Fact]
    public void CodedFrameProcessing_AppliesTheTimestampOffsetTheAppendWindowAndTheMode()
    {
        var document = Load("""
            await open();
            const buffer = source.addSourceBuffer('video/fake; codecs="f1"');
            buffer.timestampOffset = 10;
            buffer.appendWindowStart = 11;
            buffer.appendWindowEnd = 13;
            await append(buffer, 'init a=1\nmedia 1:0:1 1:1:1 1:2:1 1:3:1\n');
            log.push(ranges(buffer.buffered), source.duration);

            // In the sequence mode, the next segment follows the previous one whatever its timestamps
            buffer.abort();
            buffer.mode = 'sequence';
            await append(buffer, 'media 1:100:1 1:101:1\n');
            log.push(ranges(buffer.buffered), buffer.timestampOffset);
            done();
            """);

        Assert.Equal("[11,13)|Infinity|[11,15)|-87", WithoutMediaEvents(document.Title));
    }

    [Fact]
    public void Remove_RemovesTheFramesUpToTheNextKeyFrame()
    {
        var document = Load("""
            await open();
            const buffer = source.addSourceBuffer('video/fake; codecs="f1"');
            await append(buffer, 'init duration=8 v=1:10x10\nmedia 1:0:1:k 1:1:1 1:2:1 1:3:1 1:4:1:k 1:5:1 1:6:1:k 1:7:1\n');
            try { buffer.remove(-1, 2); } catch (e) { log.push(e.name); }
            buffer.remove(1, 3);
            log.push(buffer.updating);
            await event(buffer, 'updateend');
            log.push(ranges(buffer.buffered));
            try { source.duration = 3; } catch (e) { log.push(e.name); }
            buffer.remove(1, Infinity);
            await event(buffer, 'updateend');
            source.duration = 0.5;
            log.push(ranges(buffer.buffered), source.duration, video.duration);
            done();
            """);

        // The frames of the group of pictures that follow the removed frames are removed too; the duration cannot be less than the buffered frames
        Assert.Equal("TypeError|true|[0,1)[4,8)|InvalidStateError|[0,1)|1|1", WithoutMediaEvents(document.Title));
    }

    /// <summary>Removes the events of the media element from the log (their tasks are interleaved with the ones of the script).</summary>
    private static string WithoutMediaEvents(string title)
    {
        string[] events = ["durationchange", "loadedmetadata", "loadeddata", "canplay", "canplaythrough"];
        return string.Join('|', title.Split('|').Where(part => !events.Contains(part, StringComparer.Ordinal)));
    }

    [Fact]
    public void AppendErrors_EndTheStreamWithADecodeError()
    {
        var document = Load("""
            await open();
            const buffer = source.addSourceBuffer('video/fake; codecs="f1"');
            await append(buffer, 'init duration=4 v=1:10x10\n');
            buffer.addEventListener('error', () => log.push('buffer error'));
            await append(buffer, 'garbage\n');
            await event(video, 'error');
            log.push(source.readyState, video.error.code);
            try { buffer.appendBuffer(encode('media 1:0:1:k\n')); } catch (e) { log.push(e.name); }
            done();
            """);

        Assert.Equal("durationchange|loadedmetadata|buffer error|error|ended|3|InvalidStateError", document.Title);
    }

    [Fact]
    public void MediaSegmentBeforeTheInitializationSegment_FailsTheElement()
    {
        var document = Load("""
            await open();
            const buffer = source.addSourceBuffer('video/fake; codecs="f1"');
            await append(buffer, 'media 1:0:1:k\n');
            await event(source, 'sourceclose');
            log.push(source.readyState, source.sourceBuffers.length, video.error.code);
            try { buffer.buffered; } catch (e) { log.push(e.name); }
            done();
            """);

        Assert.Equal("closed|0|4|InvalidStateError", document.Title);
    }

    [Fact]
    public void Load_DetachesTheSource()
    {
        var document = Load("""
            await open();
            const buffer = source.addSourceBuffer('video/fake; codecs="f1"');
            await append(buffer, 'init duration=4 a=1\n');
            video.audioTracks.onremovetrack = () => log.push('removetrack');
            video.removeAttribute('src');
            video.load();
            log.push(source.readyState, String(source.duration), source.sourceBuffers.length, video.audioTracks.length);
            source.onsourceclose = () => log.push('sourceclose');
            await event(video, 'emptied');
            done();
            """);

        Assert.Equal("durationchange|loadedmetadata|closed|NaN|0|0|removetrack|sourceclose|emptied", document.Title);
    }

    [Fact]
    public void RemoveSourceBuffer_AbortsTheAppendAndRemovesTheTracks()
    {
        var document = Load("""
            await open();
            const buffer = source.addSourceBuffer('video/fake; codecs="f1"');
            await append(buffer, 'init duration=4 a=1\n');
            for (const type of ['updatestart', 'update', 'updateend', 'abort']) buffer.addEventListener(type, () => log.push(type));
            buffer.appendBuffer(encode('media 1:0:1\n'));
            source.removeSourceBuffer(buffer);
            log.push(buffer.updating, source.sourceBuffers.length, video.audioTracks.length);
            try { source.removeSourceBuffer(buffer); } catch (e) { log.push(e.name); }
            await event(source.sourceBuffers, 'removesourcebuffer');
            done();
            """);

        Assert.Equal("durationchange|loadedmetadata|false|0|0|NotFoundError|updatestart|abort|updateend", document.Title);
    }

    [Fact]
    public void QuotaExceededError_IsThrownWhenTheBufferIsFull()
    {
        var document = Load("""
            await open();
            const buffer = source.addSourceBuffer('audio/fake; codecs="f1"');
            buffer.mode = 'sequence';
            const segment = 'media 1:0:1 1:1:1' + ' '.repeat(1005) + '\n';
            await append(buffer, 'init a=1\n');
            let count = 0;
            try {
              for (; count < 100; count++) await append(buffer, segment);
            } catch (e) {
              log.push(e.name, e instanceof QuotaExceededError, buffer.updating);
            }

            log.push(count, ranges(buffer.buffered));
            done();
            """);

        // The capacity of the source buffers of the fake backend is 12 KiB, and a segment (2 frames of 1 second) has 1023 bytes
        Assert.Equal("QuotaExceededError|true|false|13|[0,26)", WithoutMediaEvents(document.Title));
    }

    [Fact]
    public void ManagedMediaSource_StreamsUntilTheElementHasEnoughData()
    {
        var document = Load("""
            const managed = new ManagedMediaSource();
            managed.onstartstreaming = () => log.push('startstreaming');
            managed.onendstreaming = () => log.push('endstreaming');
            video.src = URL.createObjectURL(managed);
            await event(managed, 'sourceopen');
            const buffer = managed.addSourceBuffer('video/fake; codecs="f1"');
            log.push(buffer instanceof ManagedSourceBuffer, managed.streaming);
            buffer.onbufferedchange = e => log.push('bufferedchange +' + ranges(e.addedRanges) + ' -' + ranges(e.removedRanges));
            await append(buffer, 'init duration=2 v=1:10x10\nmedia 1:0:1:k 1:1:1:k\n');
            await event(managed, 'endstreaming');
            buffer.remove(1, 2);
            await event(buffer, 'bufferedchange');
            log.push(managed.streaming);
            done();
            """);

        Assert.Equal("true|true|startstreaming|durationchange|loadedmetadata|bufferedchange +[0,2) -|endstreaming|loadeddata|canplay|canplaythrough|bufferedchange + -[1,2)|false", document.Title);
    }

    [Fact]
    public void Feature_HidesTheInterfaces()
    {
        var document = Load("document.title = [typeof MediaSource, typeof SourceBuffer, typeof ManagedMediaSource, typeof MediaSourceHandle, 'sourceBuffer' in AudioTrack.prototype].join('|');", features: WebPlatformFeatures.Default with { MediaSourceEnabled = false }, prelude: false);
        Assert.Equal("undefined|undefined|undefined|undefined|false", document.Title);
    }

    private sealed class BackendWithoutMediaSource : MediaBackend
    {
        public override MediaTypeSupport CanPlayType(string mimeType, IReadOnlyList<string> codecs) => MediaTypeSupport.Probably;

        public override MediaPlayer? CreatePlayer(MediaResource resource) => null;
    }

    private sealed class FakeBackend : MediaBackend
    {
        public override MediaTypeSupport CanPlayType(string mimeType, IReadOnlyList<string> codecs)
        {
            if (mimeType is not ("video/fake" or "audio/fake"))
                return MediaTypeSupport.NotSupported;

            return codecs.Count == 0 ? MediaTypeSupport.Maybe : MediaTypeSupport.Probably;
        }

        public override MediaPlayer? CreatePlayer(MediaResource resource) => null;

        public override MediaSegmentParser? CreateSegmentParser(string mimeType, IReadOnlyList<string> codecs) => mimeType is "video/fake" or "audio/fake" ? new FakeParser() : null;
    }

    /// <summary>Each line of the byte stream is a segment.</summary>
    private sealed class FakeParser : MediaSegmentParser
    {
        private readonly HashSet<long> _audioTracks = [];

        /// <summary>A small capacity, to fill a source buffer with a few appends.</summary>
        public override long? BufferCapacity => 12 * 1024;

        public override MediaSegmentParseResult Parse(ReadOnlyMemory<byte> input)
        {
            var span = input.Span;
            var end = span.IndexOf((byte)'\n');
            if (end < 0)
                return MediaSegmentParseResult.NeedMoreData(isParsingMediaSegment: span.StartsWith("media"u8));

            var parts = Encoding.UTF8.GetString(span[..end]).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (parts.FirstOrDefault())
            {
                case "init":
                {
                    var duration = double.NaN;
                    var tracks = new List<MediaSegmentTrack>();
                    foreach (var part in parts.Skip(1))
                    {
                        var value = part[(part.IndexOf('=', StringComparison.Ordinal) + 1)..];
                        if (part.StartsWith("duration=", StringComparison.Ordinal))
                        {
                            duration = double.Parse(value, CultureInfo.InvariantCulture);
                        }
                        else if (part.StartsWith("a=", StringComparison.Ordinal))
                        {
                            tracks.Add(new MediaSegmentTrack(long.Parse(value, CultureInfo.InvariantCulture), IsVideo: false, "main", Language: "en"));
                            _audioTracks.Add(tracks[^1].Id);
                        }
                        else if (part.StartsWith("v=", StringComparison.Ordinal))
                        {
                            var fields = value.Split(':', 'x');
                            tracks.Add(new MediaSegmentTrack(long.Parse(fields[0], CultureInfo.InvariantCulture), IsVideo: true, "main", VideoWidth: int.Parse(fields[1], CultureInfo.InvariantCulture), VideoHeight: int.Parse(fields[2], CultureInfo.InvariantCulture)));
                        }
                    }

                    return MediaSegmentParseResult.Initialization(end + 1, new MediaInitializationSegment(tracks, duration));
                }

                case "media":
                {
                    var frames = new List<MediaCodedFrame>();
                    foreach (var part in parts.Skip(1))
                    {
                        var fields = part.Split(':');
                        var track = long.Parse(fields[0], CultureInfo.InvariantCulture);
                        var timestamp = double.Parse(fields[1], CultureInfo.InvariantCulture);
                        frames.Add(new MediaCodedFrame(track, timestamp, timestamp, double.Parse(fields[2], CultureInfo.InvariantCulture), IsRandomAccessPoint: fields.Length > 3 || _audioTracks.Contains(track)));
                    }

                    return MediaSegmentParseResult.Media(end + 1, frames);
                }

                default:
                    return MediaSegmentParseResult.Error("Unknown segment");
            }
        }
    }
}
