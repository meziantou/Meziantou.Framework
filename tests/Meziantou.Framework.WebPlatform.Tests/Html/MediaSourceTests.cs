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
/// frames are always key frames). For Encrypted Media Extensions, <c>keyids=0101...</c> adds a <c>keyids</c>
/// Initialization Data to an initialization segment, and <c>e=0101...</c> encrypts a frame with the key ID (<c>e=0101...!</c>
/// gives it subsamples that do not match its data).
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

    private Document Load(string script, MediaBackend? backend = null, WebPlatformFeatures? features = null, bool prelude = true, string url = "https://example.com/", MediaKeysStorageBackend? mediaKeysStorage = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            MediaBackend = backend ?? new FakeBackend(),
            MediaKeysStorage = mediaKeysStorage,
            TimeProvider = _clock,
            Features = features ?? WebPlatformFeatures.Default,
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });
        var document = context.Navigate(url, _ => (prelude ? Prelude : "") + "<script>(async () => {" + script + "})().catch(e => document.title = 'FAILED: ' + e);</script>");
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

    private const string EncryptedMediaPrelude = """
        const keyId = new Uint8Array(16).fill(1);
        const key = new Uint8Array(16).fill(2);
        const base64url = bytes => btoa(String.fromCharCode(...bytes)).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_');
        const decode = buffer => new TextDecoder().decode(buffer);
        const keyIdsInitData = encode(JSON.stringify({ kids: [base64url(keyId)] }));
        const license = type => encode(JSON.stringify({ keys: [{ kty: 'oct', kid: base64url(keyId), k: base64url(key) }], type }));
        const configuration = { initDataTypes: ['keyids'], videoCapabilities: [{ contentType: 'video/mp4; codecs="avc1.4d401e"' }] };
        """;

    [Fact]
    public void EncryptedMedia_RequestMediaKeySystemAccess_SelectsTheFirstSupportedConfiguration()
    {
        var document = Load(EncryptedMediaPrelude + """
            for (const [keySystem, configurations] of [['', [configuration]], ['org.w3.clearkey', []], ['com.widevine.alpha', [configuration]], ['ORG.W3.CLEARKEY', [configuration]], ['org.w3.clearkey', [{}]]]) {
              try { await navigator.requestMediaKeySystemAccess(keySystem, configurations); } catch (e) { log.push(e.name); }
            }

            const access = await navigator.requestMediaKeySystemAccess('org.w3.clearkey', [
              { initDataTypes: ['fake'], videoCapabilities: configuration.videoCapabilities },
              { label: 'second', initDataTypes: ['webm', 'keyids'], videoCapabilities: [{ contentType: 'video/mp4; codecs="AVC1.4D401E"' }, { contentType: 'video/mp4; codecs="avc1.4d401e"', encryptionScheme: 'cbcs' }], sessionTypes: ['temporary', 'persistent-license'] },
            ]);
            const result = access.getConfiguration();
            log.push(access.keySystem, result.label, result.initDataTypes.join(','), result.videoCapabilities.map(c => c.contentType + ':' + c.encryptionScheme).join(','), result.persistentState, result.distinctiveIdentifier, result.sessionTypes.join(','));
            log.push(access.getConfiguration() !== result);
            done();
            """);

        Assert.Equal("TypeError|TypeError|NotSupportedError|NotSupportedError|NotSupportedError|org.w3.clearkey|second|webm,keyids|video/mp4; codecs=\"avc1.4d401e\":cbcs|required|not-allowed|temporary,persistent-license|true", document.Title);
    }

    [Fact]
    public void EncryptedMedia_ClearKeySession_SendsTheLicenseRequestAndUsesTheKeysOfTheLicense()
    {
        var document = Load(EncryptedMediaPrelude + """
            const access = await navigator.requestMediaKeySystemAccess('org.w3.clearkey', [configuration]);
            const keys = await access.createMediaKeys();
            try { keys.createSession('persistent-license'); } catch (e) { log.push(e.name); }
            log.push(await keys.setServerCertificate(new Uint8Array(4)), await keys.getStatusForPolicy({ minHdcpVersion: '1.0' }));

            const session = keys.createSession();
            try { await session.update(license()); } catch (e) { log.push(e.name); }
            try { await session.generateRequest('keyids', encode('{"kids":["not base64url!"]}')); } catch (e) { log.push(e.name); }

            const other = keys.createSession();
            other.generateRequest('keyids', keyIdsInitData);
            const message = await event(other, 'message');
            log.push(message.messageType, decode(message.message), /^[0-9]+$/.test(other.sessionId), String(other.expiration));
            try { await other.update(encode('{"keys":[{"kty":"oct","kid":"AQ","k":"AQ"}]}')); } catch (e) { log.push(e.name); }

            const statuses = event(other, 'keystatuseschange');
            await other.update(license());
            await statuses;
            log.push(other.keyStatuses.size, other.keyStatuses.get(keyId), other.keyStatuses.has(new Uint8Array(16)), [...other.keyStatuses.keys()].map(k => base64url(new Uint8Array(k))).join(','));

            await other.close();
            log.push(await other.closed, other.keyStatuses.size);
            try { await other.update(license()); } catch (e) { log.push(e.name); }
            done();
            """);

        Assert.Equal("NotSupportedError|false|usable|InvalidStateError|TypeError|license-request|{\"kids\":[\"AQEBAQEBAQEBAQEBAQEBAQ\"],\"type\":\"temporary\"}|true|NaN|TypeError|1|usable|false|AQEBAQEBAQEBAQEBAQEBAQ|closed-by-application|0|InvalidStateError", document.Title);
    }

    [Fact]
    public void EncryptedMedia_Playback_WaitsForTheKeyOfTheEncryptedFrames()
    {
        var document = Load(EncryptedMediaPrelude + """
            const keys = await (await navigator.requestMediaKeySystemAccess('org.w3.clearkey', [configuration])).createMediaKeys();
            await video.setMediaKeys(keys);
            log.push(video.mediaKeys === keys);
            video.onencrypted = e => log.push('encrypted:' + e.initDataType + ':' + decode(e.initData));
            video.onwaitingforkey = () => log.push('waitingforkey:' + video.currentTime + ':' + video.readyState);
            await open();
            const buffer = source.addSourceBuffer('video/mp4; codecs="avc1.4d401e"');
            await append(buffer, 'init duration=3 v=1:10x10 keyids=01010101010101010101010101010101\nmedia 1:0:1:k 1:1:1:k:e=01010101010101010101010101010101 1:2:1:e=01010101010101010101010101010101\n');
            source.endOfStream();
            video.play();
            await event(video, 'waitingforkey');
            log.push('buffered=' + ranges(video.buffered));
            const session = keys.createSession();
            session.generateRequest('keyids', keyIdsInitData);
            await event(session, 'message');
            await session.update(license());
            await event(video, 'ended');
            log.push('t=' + video.currentTime);
            done();
            """);

        Advance(5);

        // The playback stops at the first encrypted frame, with the data of the current position (HAVE_CURRENT_DATA), and
        // resumes once a session has the key
        Assert.Equal("true|durationchange|loadedmetadata|encrypted:keyids:{\"kids\":[\"AQEBAQEBAQEBAQEBAQEBAQ\"]}|waiting|loadeddata|canplay|playing|canplaythrough|waitingforkey:1:2|buffered=[0,3)|waiting|canplay|playing|canplaythrough|ended|t=3", document.Title);
    }

    [Fact]
    public void EncryptedMedia_FrameThatCannotBeDecrypted_IsADecodeError()
    {
        var document = Load(EncryptedMediaPrelude + """
            const keys = await (await navigator.requestMediaKeySystemAccess('org.w3.clearkey', [configuration])).createMediaKeys();
            const session = keys.createSession();
            session.generateRequest('keyids', keyIdsInitData);
            await event(session, 'message');
            await session.update(license());
            await video.setMediaKeys(keys);
            await open();
            const buffer = source.addSourceBuffer('video/mp4; codecs="avc1.4d401e"');
            const error = event(video, 'error');
            await append(buffer, 'init duration=2 v=1:10x10\nmedia 1:0:1:k:e=01010101010101010101010101010101!\n');
            await error;
            log.push('code=' + video.error.code);
            done();
            """);

        Assert.Equal("durationchange|loadedmetadata|error|code=3", document.Title);
    }

    [Fact]
    public void EncryptedMedia_PersistentLicense_IsOnlyLoadedInItsStoragePartition()
    {
        #region WebPlatform_EncryptedMediaStorage
        // The persistent-license sessions of Clear Key, shared by the browsing contexts (e.g. a database to keep them between runs)
        var storage = new InMemoryMediaKeysStorageBackend();
        var options = new BrowsingContextOptions
        {
            MediaKeysStorage = storage,
        };
        #endregion

        Assert.Same(storage, options.MediaKeysStorage);

        const string PersistentPrelude = EncryptedMediaPrelude + """
            const persistentConfiguration = { ...configuration, persistentState: 'required', sessionTypes: ['persistent-license'] };
            const keys = await (await navigator.requestMediaKeySystemAccess('org.w3.clearkey', [persistentConfiguration])).createMediaKeys();
            """;
        var first = Load(PersistentPrelude + """
            const session = keys.createSession('persistent-license');
            session.generateRequest('keyids', keyIdsInitData);
            const message = await event(session, 'message');
            log.push(decode(message.message));
            try { await session.update(license('temporary')); } catch (e) { log.push(e.name); }
            await session.update(license('persistent-license'));
            await session.close();
            log.push(session.sessionId);
            done();
            """, mediaKeysStorage: storage);
        var parts = first.Title.Split('|');
        Assert.Equal("{\"kids\":[\"AQEBAQEBAQEBAQEBAQEBAQ\"],\"type\":\"persistent-license\"}|TypeError", string.Join('|', parts[..^1]));
        var sessionId = parts[^1];

        // Another origin cannot load the session
        var other = Load(PersistentPrelude + "log.push(await keys.createSession('persistent-license').load('" + sessionId + "')); done();", url: "https://other.example/", mediaKeysStorage: storage);
        Assert.Equal("false", other.Title);

        // The origin of the session loads it: its key is usable, until remove() destroys the license and the acknowledgement of the release clears the session
        var second = Load(PersistentPrelude + """
            const session = keys.createSession('persistent-license');
            try { await keys.createSession().load('SESSION'); } catch (e) { log.push(e.name); }
            log.push(await session.load('
            """ + sessionId + """
            '), session.keyStatuses.get(keyId));
            await session.remove();
            const message = await event(session, 'message');
            log.push(message.messageType, decode(message.message), session.keyStatuses.size);
            await session.update(message.message);
            log.push(await session.closed, await keys.createSession('persistent-license').load(session.sessionId));
            done();
            """, mediaKeysStorage: storage);
        Assert.Equal("NotSupportedError|true|usable|license-release|{\"kids\":[\"AQEBAQEBAQEBAQEBAQEBAQ\"]}|0|release-acknowledged|false", second.Title);
    }

    [Fact]
    public void EncryptedMedia_CodedFrame_DescribesTheCommonEncryption()
    {
        #region WebPlatform_EncryptedMediaFrames
        // A frame of a media segment encrypted with the Common Encryption (cenc): AES-CTR on the bytes after the clear header of
        // each subsample. The library decrypts it with the keys of the sessions of the MediaKeys of the media element, and the
        // playback waits until a session has the key.
        byte[] keyId = [0xad, 0x13, 0xf9, 0xea, 0x2b, 0xe6, 0x98, 0xb8, 0x75, 0xf5, 0x04, 0xa8, 0xe3, 0xcc, 0xea, 0x64];
        byte[] initializationVector = [0, 0, 0, 0, 0, 0, 0, 1];
        byte[] data = new byte[100];
        var frame = new MediaCodedFrame(TrackId: 1, PresentationTimestamp: 0, DecodeTimestamp: 0, Duration: 0.04, IsRandomAccessPoint: true)
        {
            Encryption = new MediaSampleEncryption(MediaEncryptionScheme.Cenc, keyId, initializationVector, [new MediaSubsample(ClearBytes: 5, ProtectedBytes: 95)]),
            Data = data,
        };
        #endregion

        Assert.Equal(MediaEncryptionScheme.Cenc, frame.Encryption.Scheme);
        Assert.Equal(95, frame.Encryption.Subsamples[0].ProtectedBytes);
        Assert.Throws<ArgumentException>(() => new MediaSampleEncryption(MediaEncryptionScheme.Cbcs, keyId, initializationVector));
    }

    [Fact]
    public void EncryptedMedia_SampleDecryptor_DecryptsCencAndCbcs()
    {
        // NIST SP 800-38A F.5.1 (CTR-AES128) and F.2.1 (CBC-AES128)
        var key = Convert.FromHexString("2b7e151628aed2a6abf7158809cf4f3c");
        var keyId = new byte[16];
        var counter = Convert.FromHexString("f0f1f2f3f4f5f6f7f8f9fafbfcfdfeff");
        var ctrCiphertext = Convert.FromHexString("874d6191b620e3261bef6864990db6ce9806f66b7970fdff8617187bb9fffdff");
        var plaintext = Convert.FromHexString("6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e51");

        var whole = ctrCiphertext.ToArray();
        Assert.True(MediaSampleDecryptor.TryDecrypt(new MediaSampleEncryption(MediaEncryptionScheme.Cenc, keyId, counter), key, whole, whole));
        Assert.Equal(plaintext, whole);

        // The protected ranges of the subsamples form one key stream; the clear bytes are kept
        byte[] sample = [0xAA, .. ctrCiphertext[..10], 0xBB, 0xCC, .. ctrCiphertext[10..]];
        var decrypted = new byte[sample.Length];
        Assert.True(MediaSampleDecryptor.TryDecrypt(new MediaSampleEncryption(MediaEncryptionScheme.Cenc, keyId, counter, [new MediaSubsample(1, 10), new MediaSubsample(2, 22)]), key, sample, decrypted));
        Assert.Equal([0xAA, .. plaintext[..10], 0xBB, 0xCC, .. plaintext[10..]], decrypted);
        Assert.False(MediaSampleDecryptor.TryDecrypt(new MediaSampleEncryption(MediaEncryptionScheme.Cenc, keyId, counter, [new MediaSubsample(1, 10)]), key, sample, decrypted));

        // cbcs with the pattern 1:1: the first block is decrypted, the second one is clear, the last partial block is clear
        var iv = Convert.FromHexString("000102030405060708090a0b0c0d0e0f");
        byte[] cbcs = [.. Convert.FromHexString("7649abac8119b246cee98e9b12e9197d"), .. plaintext[16..], 1, 2, 3];
        Assert.True(MediaSampleDecryptor.TryDecrypt(new MediaSampleEncryption(MediaEncryptionScheme.Cbcs, keyId, iv) { CryptByteBlock = 1, SkipByteBlock = 1 }, key, cbcs, cbcs));
        Assert.Equal([.. plaintext, 1, 2, 3], cbcs);
    }

    [Fact]
    public void EncryptedMedia_ClearKeyInitData_IsValidatedAndBounded()
    {
        byte[] commonPssh = [0x00, 0x00, 0x00, 0x34, .. "pssh"u8, 0x01, 0x00, 0x00, 0x00, 0x10, 0x77, 0xEF, 0xEC, 0xC0, 0xB2, 0x4D, 0x02, 0xAC, 0xE3, 0x3C, 0x1E, 0x52, 0xE2, 0xFB, 0x4B, 0x00, 0x00, 0x00, 0x01, .. new byte[16], 0x00, 0x00, 0x00, 0x00];
        Assert.Equal([new byte[16]], ClearKey.ExtractKeyIds("cenc", commonPssh));

        // The pssh box of another key system is valid but gives no key ID; a box whose size does not match is not valid
        byte[] otherPssh = [0x00, 0x00, 0x00, 0x20, .. "pssh"u8, 0x00, 0x00, 0x00, 0x00, .. Enumerable.Repeat((byte)0x55, 16), 0x00, 0x00, 0x00, 0x00];
        Assert.Empty(ClearKey.ExtractKeyIds("cenc", otherPssh)!);
        Assert.Null(ClearKey.ExtractKeyIds("cenc", [0x00, 0x00, 0xFF, 0xFF, .. commonPssh[4..]]));
        Assert.Null(ClearKey.ExtractKeyIds("cenc", [.. commonPssh, 0x00]));

        Assert.Equal([[1, 2]], ClearKey.ExtractKeyIds("keyids", """{"kids":["AQI"]}"""u8));
        Assert.Null(ClearKey.ExtractKeyIds("keyids", """{"kids":["AQI="]}"""u8));
        Assert.Null(ClearKey.ExtractKeyIds("keyids", """{"kids":[]}"""u8));
        Assert.Null(ClearKey.ExtractKeyIds("keyids", Encoding.UTF8.GetBytes("{\"kids\":[\"" + new string('A', 800) + "\"]}")));
        Assert.Equal([[7]], ClearKey.ExtractKeyIds("webm", [7]));
        Assert.Null(ClearKey.ExtractKeyIds("webm", new byte[ClearKey.MaxInitDataLength + 1]));

        Assert.Equal(16, ClearKey.ParseResponse("""{"keys":[{"kty":"oct","kid":"AQI","k":"AAAAAAAAAAAAAAAAAAAAAA"}],"type":"persistent-license"}"""u8)!.Keys![0].Key.Length);
        Assert.Null(ClearKey.ParseResponse("""{"keys":[{"kty":"RSA","kid":"AQI","k":"AAAAAAAAAAAAAAAAAAAAAA"}]}"""u8));
        Assert.Equal([[1, 2]], ClearKey.ParseResponse("""{"kids":["AQI"]}"""u8)!.ReleasedKeyIds);
        Assert.Null(ClearKey.ParseResponse("[]"u8));
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
            if (mimeType is not ("video/fake" or "audio/fake" or "video/mp4" or "audio/mp4"))
                return MediaTypeSupport.NotSupported;

            return codecs.Count == 0 ? MediaTypeSupport.Maybe : MediaTypeSupport.Probably;
        }

        public override MediaPlayer? CreatePlayer(MediaResource resource) => null;

        public override MediaSegmentParser? CreateSegmentParser(string mimeType, IReadOnlyList<string> codecs) => mimeType is "video/fake" or "audio/fake" or "video/mp4" or "audio/mp4" ? new FakeParser() : null;
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
                    var initData = new List<MediaEncryptionInitData>();
                    foreach (var part in parts.Skip(1))
                    {
                        var value = part[(part.IndexOf('=', StringComparison.Ordinal) + 1)..];
                        if (part.StartsWith("keyids=", StringComparison.Ordinal))
                        {
                            initData.Add(new MediaEncryptionInitData("keyids", Encoding.UTF8.GetBytes("{\"kids\":[\"" + System.Buffers.Text.Base64Url.EncodeToString(Convert.FromHexString(value)) + "\"]}")));
                        }
                        else if (part.StartsWith("duration=", StringComparison.Ordinal))
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

                    return MediaSegmentParseResult.Initialization(end + 1, new MediaInitializationSegment(tracks, duration) { InitData = initData });
                }

                case "media":
                {
                    var frames = new List<MediaCodedFrame>();
                    foreach (var part in parts.Skip(1))
                    {
                        var fields = part.Split(':');
                        var track = long.Parse(fields[0], CultureInfo.InvariantCulture);
                        var timestamp = double.Parse(fields[1], CultureInfo.InvariantCulture);
                        var frame = new MediaCodedFrame(track, timestamp, timestamp, double.Parse(fields[2], CultureInfo.InvariantCulture), IsRandomAccessPoint: fields.Contains("k") || _audioTracks.Contains(track));
                        if (fields.FirstOrDefault(field => field.StartsWith("e=", StringComparison.Ordinal)) is { } encrypted)
                        {
                            // A frame of 16 bytes encrypted with AES-CTR; "!" gives subsamples that do not cover the frame
                            var corrupted = encrypted.EndsWith('!', StringComparison.Ordinal);
                            var keyId = Convert.FromHexString(encrypted[2..].TrimEnd('!'));
                            frame = frame with
                            {
                                Encryption = new MediaSampleEncryption(MediaEncryptionScheme.Cenc, keyId, new byte[8], corrupted ? [new MediaSubsample(1, 1)] : null),
                                Data = new byte[16],
                            };
                        }

                        frames.Add(frame);
                    }

                    return MediaSegmentParseResult.Media(end + 1, frames);
                }

                default:
                    return MediaSegmentParseResult.Error("Unknown segment");
            }
        }
    }
}
