---
title: Audio and video codecs
description: Plug the audio and video codecs of WebCodecs and MediaRecorder into the pages, create and copy video frames and audio data, parse codec strings, and record streams to WebM.
---

The pages decode and encode audio and video with [WebCodecs](https://w3c.github.io/webcodecs/) (`AudioDecoder`,
`VideoDecoder`, `AudioEncoder`, `VideoEncoder`), and record streams with `MediaRecorder`. The library implements these
APIs: the states of the codecs, their queues and `dequeue` events, `flush()`, `reset()` and `close()`, the validation and
the cloning of the configurations, the frames, the audio data and the chunks. It has no codec of its own: the codecs come
from a [MediaCodecBackend](cref:Meziantou.Framework.WebPlatform.Media.MediaCodecBackend) that your application sets in
[BrowsingContextOptions.MediaCodecs](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.MediaCodecs):

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/WebCodecsTests.cs;region=WebPlatform_MediaCodecs;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JavaScriptScriptEngine();
using var context = new BrowsingContext(new BrowsingContextOptions
{
    ScriptEngine = engine,

    // The decoders and the encoders of WebCodecs and MediaRecorder
    MediaCodecs = new PcmCodecBackend(),
});

await context.NavigateAsync("https://example.com/", _ => """
    <script>
      const decoder = new AudioDecoder({
        output: data => { document.title = data.numberOfFrames + ' frames at ' + data.timestamp; data.close(); },
        error: e => document.title = e.name,
      });
      decoder.configure({ codec: 'pcm-s16', sampleRate: 8000, numberOfChannels: 1 });
      decoder.decode(new EncodedAudioChunk({ type: 'key', timestamp: 1000, data: new Int16Array([0, 100, 200]) }));
    </script>
    """);

// The codecs run in the tasks of the event loop
await context.EventLoop.RunUntilIdleAsync();
Assert.Equal("3 frames at 1000", context.Document.Title);
```
<!-- endref -->

Without a backend, there is no codec: `isConfigSupported()` resolves with `supported: false`, `configure()` closes the
codec with a `NotSupportedError`, and `MediaRecorder.isTypeSupported()` only supports the empty type. The backend is
shared by the frames and the workers of the browsing context.

## Write a codec backend

A backend tells which configurations it supports, and creates a codec implementation for each configuration that a page
configures. Override the methods of the codecs you have: the others support nothing.

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/WebCodecsTests.cs;region=WebPlatform_MediaCodecBackend;format=md-fenced-code-block;dedent=true -->
```csharp
// A backend that decodes linear PCM (pcm-s16: 16-bit little-endian samples, interleaved)
internal class PcmCodecBackend : MediaCodecBackend
{
    // The configuration is a clone: the codec string, the sample rate, the channels, and the description as a byte[]
    public override bool IsConfigSupported(AudioDecoderConfig config)
        => MediaCodecString.TryParse(config.Codec, out var codec) && codec.Codec == MediaCodec.PcmS16 && config.NumberOfChannels is >= 1 and <= 8;

    public override MediaAudioDecoder? CreateAudioDecoder(AudioDecoderConfig config) => new PcmDecoder(config);

    private sealed class PcmDecoder(AudioDecoderConfig config) : MediaAudioDecoder
    {
        // Runs in a task of the event loop of the decoder; an exception closes the decoder with an EncodingError
        public override IReadOnlyList<AudioData> Decode(EncodedAudioChunk chunk)
        {
            var frames = chunk.Data.Length / (2 * (int)config.NumberOfChannels);
            if (frames == 0)
                throw new InvalidDataException("The chunk has no complete frame");

            return
            [
                new AudioData(new AudioDataInit
                {
                    Format = AudioSampleFormat.S16,
                    SampleRate = config.SampleRate,
                    NumberOfChannels = config.NumberOfChannels,
                    NumberOfFrames = (uint)frames,
                    Timestamp = chunk.Timestamp,
                    Data = chunk.Data,
                }),
            ];
        }
    }
}
```
<!-- endref -->

| Codec | Supported when | Created by | Implementation |
| --- | --- | --- | --- |
| `AudioDecoder` | [IsConfigSupported(AudioDecoderConfig)](cref:Meziantou.Framework.WebPlatform.Media.MediaCodecBackend.IsConfigSupported) | [CreateAudioDecoder](cref:Meziantou.Framework.WebPlatform.Media.MediaCodecBackend.CreateAudioDecoder) | [MediaAudioDecoder](cref:Meziantou.Framework.WebPlatform.Media.MediaAudioDecoder) |
| `VideoDecoder` | `IsConfigSupported(VideoDecoderConfig)` | [CreateVideoDecoder](cref:Meziantou.Framework.WebPlatform.Media.MediaCodecBackend.CreateVideoDecoder) | [MediaVideoDecoder](cref:Meziantou.Framework.WebPlatform.Media.MediaVideoDecoder) |
| `AudioEncoder` | `IsConfigSupported(AudioEncoderConfig)` | [CreateAudioEncoder](cref:Meziantou.Framework.WebPlatform.Media.MediaCodecBackend.CreateAudioEncoder) | [MediaAudioEncoder](cref:Meziantou.Framework.WebPlatform.Media.MediaAudioEncoder) |
| `VideoEncoder` | `IsConfigSupported(VideoEncoderConfig)` | [CreateVideoEncoder](cref:Meziantou.Framework.WebPlatform.Media.MediaCodecBackend.CreateVideoEncoder) | [MediaVideoEncoder](cref:Meziantou.Framework.WebPlatform.Media.MediaVideoEncoder) |

- The configurations are clones that only have the members the library knows, already validated: the codec string is
  not empty, but it is case-sensitive and must be exact (no leading or trailing spaces), and the `description` is a
  `byte[]`. A `Create` method that returns null closes the codec with a `NotSupportedError`.
- The methods of a backend may be called from several threads: a backend can be shared by several browsing contexts and
  their workers. A codec implementation is only used by the event loop of its codec, in its tasks, one operation at a
  time: it does not need to be thread-safe.
- `Decode` and `Encode` return the outputs that are ready, possibly none (a decoder that reorders the frames, an encoder
  that waits for a complete frame of the codec). [Flush](cref:Meziantou.Framework.WebPlatform.Media.MediaVideoDecoder.Flush)
  returns the pending outputs for `flush()`. [Reset](cref:Meziantou.Framework.WebPlatform.Media.MediaCodecImplementation.Reset)
  discards them (`reset()`, or a key chunk is required again), and `Dispose` releases the codec once it is reset, closed or
  configured again.
- An exception closes the codec: a [DomException](cref:Meziantou.Framework.WebPlatform.Dom.DomException) is given to the
  error callback of the page as it is, any other exception as an `EncodingError`.
- The timestamps and durations of the decoded outputs are the ones of their chunks. The library applies the rotation, the
  flip, the display aspect ratio and the color space of the configuration to the decoded frames, and closes the outputs
  that a reset discarded.
- An encoder gets a clone of the frame or the audio data, closed after the call. It returns
  [MediaEncodedVideo](cref:Meziantou.Framework.WebPlatform.Media.MediaEncodedVideo) and
  [MediaEncodedAudio](cref:Meziantou.Framework.WebPlatform.Media.MediaEncodedAudio) records: the chunk, and the
  configuration of a decoder of the chunk when it changed (its `description`, for instance), or null. The library gives
  the decoder configuration to the output callback with the first chunk after `configure()` and when it changes.
- [VideoFrame.CopyPixelsTo](cref:Meziantou.Framework.WebPlatform.WebCodecs.VideoFrame.CopyPixelsTo) copies the pixels of a
  frame at once, in its format or converted to RGB, for the encoders. `AudioData.CopyTo` converts the samples to any
  sample format.

## Codec strings

[MediaCodecString](cref:Meziantou.Framework.WebPlatform.Media.MediaCodecString) parses the codec strings of the
[WebCodecs codec registry](https://w3c.github.io/webcodecs/codec_registry.html), to answer the support of the
configurations:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/WebCodecsTests.cs;region=WebPlatform_MediaCodecString;format=md-fenced-code-block;dedent=true -->
```csharp
// A codec string of the WebCodecs codec registry: VP9, profile 2, level 4.1, 10-bit
Assert.True(MediaCodecString.TryParse("vp09.02.41.10", out var codec));
Assert.Equal(MediaCodec.Vp9, codec.Codec);
Assert.Equal((2, 41, 10), (codec.Profile, codec.Level, codec.BitDepth));
Assert.False(codec.IsAudio);

// The strings are exact: an ambiguous or an unknown string is rejected
Assert.False(MediaCodecString.TryParse("vp9", out _));
```
<!-- endref -->

The [Codec](cref:Meziantou.Framework.WebPlatform.Media.MediaCodecString.Codec) is a
[MediaCodec](cref:Meziantou.Framework.WebPlatform.Media.MediaCodec): FLAC, MP3, AAC, Opus, Vorbis, G.711 (`ulaw`, `alaw`),
linear PCM (`pcm-u8`, `pcm-s16`, `pcm-s24`, `pcm-s32`, `pcm-f32`), AV1, AVC, HEVC, VP8 and VP9. The
[Profile](cref:Meziantou.Framework.WebPlatform.Media.MediaCodecString.Profile), the
[Level](cref:Meziantou.Framework.WebPlatform.Media.MediaCodecString.Level), the
[BitDepth](cref:Meziantou.Framework.WebPlatform.Media.MediaCodecString.BitDepth) and
[IsHighTier](cref:Meziantou.Framework.WebPlatform.Media.MediaCodecString.IsHighTier) come from the strings of the video
codecs (`vp09.PP.LL.DD`, `av01.P.LLT.DD`, `avc1.PPCCLL`, `hvc1.P.C.TLL`) and the object type of AAC. A string is rejected
when it is ambiguous (`vp9`), has other cases or spaces, or has a profile, a level or a bit depth that the codec does
not define.

## Video frames and audio data

A [VideoFrame](cref:Meziantou.Framework.WebPlatform.WebCodecs.VideoFrame) is created from a buffer of pixels, from an
image or another frame, or by a decoder. .NET can create and copy them too:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/WebCodecsTests.cs;region=WebPlatform_VideoFrameFromBuffer;format=md-fenced-code-block;dedent=true -->
```csharp
// A 2x2 frame of BGRA pixels: blue, green, red, white
byte[] bgra = [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255];
var frame = new VideoFrame(bgra, new VideoFrameBufferInit
{
    Format = VideoPixelFormat.BGRA,
    CodedWidth = 2,
    CodedHeight = 2,
    Timestamp = 40_000, // in microseconds
});

// The pixels are converted to RGBA by the copy, synchronously
var options = new VideoFrameCopyToOptions { Format = VideoPixelFormat.RGBA };
var rgba = new byte[frame.AllocationSize(options)];
var layout = frame.CopyPixelsTo(rgba, options);

Assert.Equal(8u, Assert.Single(layout).Stride);
Assert.Equal([0, 0, 255, 255, 0, 255, 0, 255], rgba[..8]);
frame.Close();
```
<!-- endref -->

- The formats of [VideoPixelFormat](cref:Meziantou.Framework.WebPlatform.WebCodecs.VideoPixelFormat) are supported: the
  planar YUV formats (`I420`, `I422`, `I444`, with an alpha plane, in 8, 10 and 12 bits), `NV12`, and the RGB formats
  (`RGBA`, `RGBX`, `BGRA`, `BGRX`).
- The buffer is a `byte[]` or a `ReadOnlyMemory<byte>` from .NET, a buffer source from the scripts. The `layout` gives
  the offset and the stride of each plane in the buffer. Only the pixels of the visible rectangle are copied: as in
  Chromium, the coded size of the frame is the size of its visible rectangle.
- A frame created from an image (a canvas, an `img` element, an `ImageBitmap`, an `OffscreenCanvas`) needs a timestamp;
  a frame created from a `video` element has its current frame. A frame created from another frame shares its pixels,
  with a new visible rectangle, rotation, flip or display size.
- The rotation is rounded to a multiple of 90 degrees. The display size is the visible size, swapped by a rotation of
  90 or 270 degrees, unless the init gives one. `drawImage()` and `createImageBitmap()` draw the visible rectangle at
  the display size, rotated, then flipped.
- `copyTo()` copies the planes of the frame, or converts the pixels to an RGB format (`RGBA`, `RGBX`, `BGRA`, `BGRX`)
  in sRGB or Display P3: the YUV formats are converted with the matrix and the range of the color space of the frame.
  [CopyPixelsTo](cref:Meziantou.Framework.WebPlatform.WebCodecs.VideoFrame.CopyPixelsTo) is its synchronous form, and
  [AllocationSize](cref:Meziantou.Framework.WebPlatform.WebCodecs.VideoFrame.AllocationSize) the size of the buffer.
- [AudioData](cref:Meziantou.Framework.WebPlatform.WebCodecs.AudioData) holds samples of any
  [AudioSampleFormat](cref:Meziantou.Framework.WebPlatform.WebCodecs.AudioSampleFormat) (8, 16 and 32-bit integers, 32-bit
  floats, interleaved or planar), and copies a plane or a range of frames in any format: the integers are converted by
  shifts, the floats are scaled and clamped.
- [EncodedVideoChunk](cref:Meziantou.Framework.WebPlatform.WebCodecs.EncodedVideoChunk) and
  [EncodedAudioChunk](cref:Meziantou.Framework.WebPlatform.WebCodecs.EncodedAudioChunk) hold the encoded bytes; .NET reads
  them with their `Data` property.
- `close()` releases the pixels or the samples; the clones share them. The frames and the audio data can be posted to the
  frames and the workers of the same agent cluster, serialized or transferred.

## Record a stream

`MediaRecorder` records the tracks of a `MediaStream`: a camera or a microphone of the
[media capture backend](08-PermissionsAndDevices.md#camera-microphone-and-screen-capture), a canvas, a media element or
a Web Audio graph. The video is encoded in VP8 and the audio in Opus by the codecs of the backend, and muxed into a WebM
stream (`video/webm`, `audio/webm`), or a Matroska stream (`video/x-matroska`):

- The backend must support a `VideoEncoderConfig` of the codec `vp8` and an `AudioEncoderConfig` of the codec `opus`.
  `MediaRecorder.isTypeSupported()` and the constructor accept the types whose codecs the backend can encode; a type
  without codecs needs one of them.
- The video frames of the tracks are sampled at the frame rate of the track (30 frames per second by default, at most
  60) on the clock of the event loop: with a fake time provider, advance the clock to record (see
  [Time and the event loop](07-TimeAndTheEventLoop.md#control-the-clock)).
- `start(timeslice)` delivers a `dataavailable` event every `timeslice` milliseconds, `requestData()` and `stop()` deliver
  the data recorded since the previous event. The first blob starts with the header of the stream, and each blob starts a
  new cluster: the blobs of a recording are concatenated into a WebM file.
- The recording stops when all its tracks end. A track added to or removed from the stream stops it with an
  `InvalidModificationError`, and a failure of the codecs with an `UnknownError`.

## Limitations

- The codecs (`AudioDecoder`, `VideoDecoder`, `AudioEncoder`, `VideoEncoder`) and `MediaRecorder` use the codecs of the
  global object of the scripts that create them: create them from the scripts of a page or a worker.
- `MediaRecorder` only records VP8 and Opus. `videoKeyFrameIntervalDuration` and `videoKeyFrameIntervalCount` request
  the key frames from the encoder.
- The conversion of a frame to RGB applies the matrix and the range of its color space, but not its transfer
  characteristics (PQ, HLG), and the colors of the primaries other than Display P3 (BT.2020) are kept as sRGB colors.
  `copyTo()` only converts to the RGB formats.
