using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Media;
using Meziantou.Framework.WebPlatform.WebCodecs;

namespace Meziantou.Framework.WebPlatform.Fuzz;

/// <summary>
/// The target of <c>VideoFrame.copyTo()</c> and <c>allocationSize()</c>: a frame of random pixels with a rotation and a flip,
/// copied with the rectangle, the layout, the format and the color space of the input. The options are invalid (a
/// <c>TypeError</c> or a <c>NotSupportedError</c>), or the copy writes the rows of the rectangle at the offsets of the layout,
/// within the allocation size, and leaves the other bytes of the destination unchanged; the rendered frame has the display size.
/// </summary>
public static partial class WebPlatformFuzzTargets
{
    private static readonly VideoPixelFormat?[] VideoFrameFormats = [null, VideoPixelFormat.RGBA, VideoPixelFormat.RGBX, VideoPixelFormat.BGRA, VideoPixelFormat.BGRX, VideoPixelFormat.I420, VideoPixelFormat.NV12];

    private static readonly PredefinedColorSpace?[] VideoFrameColorSpaces = [null, PredefinedColorSpace.Srgb, PredefinedColorSpace.DisplayP3, PredefinedColorSpace.SrgbLinear];

    /// <summary>The coordinates of the rectangles: in and out of the frame, negative, fractional and not finite.</summary>
    private static readonly double[] VideoFrameCoordinates = [0, 1, 2, 3, 4, 7, 8, 0.5, 1.75, -1, -0.25, 9, 100, double.NaN, double.PositiveInfinity, uint.MaxValue];

    /// <summary>Runs <c>copyTo()</c> on a frame with the options of the input, and checks the bytes it writes.</summary>
    public static void VideoFrameCopyTo(ReadOnlySpan<byte> data)
    {
        var position = 0;
        var input = data.ToArray();
        byte Next() => position < input.Length ? input[position++] : (byte)0;

        // The frame: 1 to 8 pixels in each direction, its rotation and its flip
        var width = (Next() % 8) + 1;
        var height = (Next() % 8) + 1;
        var orientation = Next();
        var rotation = (orientation & 3) * 90;
        var flip = (orientation & 4) != 0;
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(i * 31);
        }

        var frame = new VideoFrame(new BitmapPixels(width, height, pixels), timestamp: 0, duration: null, rotation, flip);

        // The rendered frame has the display size
        var rendered = frame.GetRenderedPixels()!;
        FuzzAssert.Equal(frame.DisplayWidth, (uint)rendered.Width, "the width of the rendered frame");
        FuzzAssert.Equal(frame.DisplayHeight, (uint)rendered.Height, "the height of the rendered frame");

        // The options
        var flags = Next();
        var rect = (flags & 1) != 0
            ? new DomRectInit { X = Coordinate(), Y = Coordinate(), Width = Coordinate(), Height = Coordinate() }
            : null;
        var layout = (flags & 2) != 0
            ? Enumerable.Range(0, (Next() % 3)).Select(_ => new PlaneLayout { Offset = Next(), Stride = Next() }).ToArray()
            : null;
        var format = VideoFrameFormats[Next() % VideoFrameFormats.Length];
        var colorSpace = VideoFrameColorSpaces[Next() % VideoFrameColorSpaces.Length];
        var options = new VideoFrameCopyToOptions { Rect = rect, Layout = layout, Format = format, ColorSpace = colorSpace };
        var destination = new byte[Next() * 2];
        Array.Fill(destination, (byte)0xA5);

        uint allocationSize;
        try
        {
            allocationSize = frame.AllocationSize(options);
        }
        catch (Exception ex) when (ex is TypeErrorException or DomException)
        {
            // The same options are rejected by copyTo()
            var rejected = frame.CopyTo(destination, options);
            FuzzAssert.True(rejected.IsFaulted && rejected.Exception!.InnerException!.GetType() == ex.GetType(), "copyTo() rejects the options allocationSize() rejects");
            return;
        }

        var task = frame.CopyTo(destination, options);
        if (destination.Length < allocationSize)
        {
            FuzzAssert.True(task.IsFaulted && task.Exception!.InnerException is TypeErrorException, "copyTo() rejects a destination smaller than the allocation size");
            return;
        }

        FuzzAssert.True(task.IsCompletedSuccessfully, "copyTo() succeeds with the options allocationSize() accepts");
        var planeLayout = task.Result is [var single] ? single : throw new FuzzAssertionException("copyTo() returns the layout of one plane");

        // The bytes of the rows of the rectangle are written at the offsets of the layout; the other bytes are unchanged
        var (x, y, copyWidth, copyHeight) = rect is null ? (0, 0, width, height) : ((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);
        var written = new bool[destination.Length];
        for (var row = 0; row < copyHeight; row++)
        {
            for (var column = 0; column < copyWidth * 4; column++)
            {
                var offset = (int)(planeLayout.Offset + (row * planeLayout.Stride) + column);
                FuzzAssert.True(offset < allocationSize, "the copy is within the allocation size");
                written[offset] = true;

                // Without a format, the pixels are copied as they are (RGBA)
                if (format is null)
                {
                    FuzzAssert.Equal(pixels[(((y + row) * width) + x) * 4 + column], destination[offset], "the copied byte");
                }
                else if (format is VideoPixelFormat.RGBX or VideoPixelFormat.BGRX && column % 4 == 3)
                {
                    FuzzAssert.Equal((byte)255, destination[offset], "the padding of the opaque formats");
                }
            }
        }

        for (var i = 0; i < destination.Length; i++)
        {
            if (!written[i])
            {
                FuzzAssert.Equal((byte)0xA5, destination[i], "the bytes outside of the copied rows are unchanged");
            }
        }

        frame.Close();
        FuzzAssert.True(frame.CopyTo(destination, options).IsFaulted, "copyTo() rejects a closed frame");
        FuzzAssert.Equal(0u, frame.DisplayWidth, "a closed frame has no size");

        double Coordinate() => VideoFrameCoordinates[Next() % VideoFrameCoordinates.Length];
    }

    private static IEnumerable<FuzzSeed> GetVideoFrameCopyToSeeds()
    {
        byte[][] scenarios =
        [
            // The whole frame, without options
            [3, 2, 0, 0, 0, 0, 255],

            // A rectangle, a padded layout, BGRA in Display P3, a rotation and a flip
            [7, 7, 5, 3, 1, 1, 2, 2, 1, 4, 40, 3, 2, 200],

            // An invalid rectangle, a layout of two planes, a YUV format, a destination too small
            [4, 4, 1, 3, 13, 1, 9, 2, 2, 0, 16, 1, 4, 5, 0, 1],

            // An opaque format, a rectangle at the edge, a stride smaller than the row
            [8, 8, 2, 3, 6, 6, 1, 1, 1, 0, 1, 2, 0, 40],
        ];

        for (var i = 0; i < scenarios.Length; i++)
        {
            yield return new FuzzSeed("videoframe-copyto-" + i.ToString(CultureInfo.InvariantCulture), scenarios[i]);
        }
    }

    /// <summary>Returns the exception an action throws, or null.</summary>
    private static Exception? CatchException(Action action)
    {
        try
        {
            action();
            return null;
        }
#pragma warning disable CA1031 // The exception is checked by the caller
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return ex;
        }
    }

    // MediaCodecString

    /// <summary>The tokens of the codec strings of the WebCodecs codec registry.</summary>
    private static string[] MediaCodecStringTokens =>
    [
        "vp8", "vp09", "av01", "avc1", "avc3", "hev1", "hvc1", "mp4a", "mp4a.40.", "mp4a.67", "opus", "flac", "mp3", "vorbis", "ulaw", "alaw",
        "pcm-", "u8", "s16", "s24", "s32", "f32", ".", "..", "00", "01", "02", "08", "10", "12", "31", "62", "M", "H", "L", "A", "B", "C",
        "42001E", "640028", "6E", "F4", "B0", "90", "110",
    ];

    /// <summary>The levels of VP9 (https://www.webmproject.org/vp9/mp4/#codecs-parameter-string).</summary>
    private static readonly int[] FuzzVp9Levels = [10, 11, 20, 21, 30, 31, 40, 41, 50, 51, 52, 60, 61, 62];

    /// <summary>
    /// Parses a codec string: a parsed string has the input as its value, and the profile, the level and the bit depth are in
    /// the ranges of its codec; the result does not depend on the run.
    /// </summary>
    public static void MediaCodecStringTarget(string value)
    {
        if (!MediaCodecString.TryParse(value, out var codec))
        {
            FuzzAssert.False(MediaCodecString.TryParse(value, out _), "the parsing is deterministic");
            return;
        }

        FuzzAssert.Equal(value, codec.Value, "the value of the codec string");
        FuzzAssert.Equal(value, codec.ToString(), "the string of the codec string");
        FuzzAssert.True(value.Length is > 0 and <= 128, "a codec string has 1 to 128 characters");
        FuzzAssert.True(value.All(char.IsAscii) && !value.Contains(' ', StringComparison.Ordinal), "a codec string is ASCII without spaces");
        FuzzAssert.True(MediaCodecString.TryParse(value, out var again) && again.Codec == codec.Codec && again.Profile == codec.Profile && again.Level == codec.Level, "the parsing is deterministic");

        var isVideo = codec.Codec is MediaCodec.Vp8 or MediaCodec.Vp9 or MediaCodec.Av1 or MediaCodec.Avc or MediaCodec.Hevc;
        FuzzAssert.Equal(!isVideo, codec.IsAudio, "an audio codec");
        switch (codec.Codec)
        {
            case MediaCodec.Vp9:
                FuzzAssert.True(codec.Profile is >= 0 and <= 3, "the profile of VP9");
                FuzzAssert.True(Array.IndexOf(FuzzVp9Levels, codec.Level) >= 0, "the level of VP9");
                FuzzAssert.True(codec.BitDepth is 8 or 10 or 12, "the bit depth of VP9");
                FuzzAssert.Equal(codec.Profile < 2, codec.BitDepth == 8, "the profiles 0 and 1 of VP9 are 8-bit");
                break;

            case MediaCodec.Av1:
                FuzzAssert.True(codec.Profile is >= 0 and <= 2, "the profile of AV1");
                FuzzAssert.True(codec.Level is >= 0 and <= 23 or 31, "the level of AV1");
                FuzzAssert.True(codec.BitDepth is 8 or 10 or 12, "the bit depth of AV1");
                break;

            case MediaCodec.Avc:
                FuzzAssert.True(codec.Profile is > 0 and <= 0xFF, "the profile of AVC");
                FuzzAssert.True(codec.Level is >= 1 and <= 62, "the level of AVC");
                FuzzAssert.True(codec.BitDepth is 8 or 10, "the bit depth of AVC");
                FuzzAssert.False(codec.IsHighTier, "AVC has no tier");
                break;

            case MediaCodec.Hevc:
                FuzzAssert.True(codec.Profile is >= 1 and <= 11, "the profile of HEVC");
                FuzzAssert.True(codec.Level is >= 1 and <= 255, "the level of HEVC");
                FuzzAssert.True(codec.BitDepth is 8 or 10, "the bit depth of HEVC");
                break;

            case MediaCodec.Aac:
                FuzzAssert.True(codec.Profile is 2 or 5 or 29 or 0x67, "the object type of AAC");
                FuzzAssert.Equal(0, codec.Level, "AAC has no level");
                break;

            default:
                FuzzAssert.Equal((0, 0, 8, false), (codec.Profile, codec.Level, codec.BitDepth, codec.IsHighTier), "a codec without profile");
                break;
        }
    }

    private static IEnumerable<FuzzSeed> GetMediaCodecStringSeeds()
    {
        // The strings of the unit tests (WebCodecsTests.MediaCodecString_*)
        string[] values =
        [
            "vp8", "opus", "flac", "ulaw", "pcm-f32", "pcm-s16", "mp4a.40.2", "mp4a.40.02", "mp4a.40.29", "mp4a.67", "vp09.00.10.08", "vp09.02.62.12.01.09.16.09.01",
            "vp09.02.41.10", "av01.0.04M.08", "av01.2.31H.12.0.110.01.01.01.0", "avc1.42001E", "avc3.640028", "avc1.6E0028", "hvc1.1.6.L93.B0", "hev1.A2.4.H120.90",
            "", "vp9", " vp8", "VP8", "mp4a", "mp4a.40", "vp09.00.10.10", "vp09.02.10.08", "vp09.99.99.08", "vp09.00.12.08", "vp09.00.10.08.1", "vp09.0.10.08",
            "av01.3.04M.08", "av01.0.24M.08", "av01.0.04X.08", "av01.0.04M.09", "av01.0.04M.08.0.112.01.01.01.0", "avc1.42001", "avc1.12001E", "avc1.420000",
            "avc1.42001E.1", "hvc1.12.6.L93", "hvc1.1.6.X93", "hvc1.1.6.L0", "hvc1.1.6", "hvc1.1..L93", "hvc1.1.6.L93.123", "flac.1",
        ];

        for (var i = 0; i < values.Length; i++)
        {
            yield return FuzzSeed.FromText("media-codec-string-" + i.ToString(CultureInfo.InvariantCulture), values[i]);
        }
    }

    // VideoFrame created from a buffer

    private static readonly VideoPixelFormat[] AllVideoPixelFormats = Enum.GetValues<VideoPixelFormat>();

    private static readonly double[] VideoFrameRotations = [0, 90, 180, 270, 45, -90, 359.9, 1e300, double.NaN];

    private static readonly VideoMatrixCoefficients[] VideoFrameMatrices = Enum.GetValues<VideoMatrixCoefficients>();

    /// <summary>
    /// Creates a frame from a buffer with the format, the coded size, the layout, the visible rectangle, the display size,
    /// the rotation and the color space of the input, then copies it with the options of the input. The init is invalid (a
    /// <c>TypeError</c>), or the coded size of the frame is its visible size within the coded size of the init; a copy writes
    /// no byte past the allocation size, a copy of the planes creates the same frame again, and the opaque formats are opaque
    /// once converted to RGB.
    /// </summary>
    public static void VideoFrameBuffer(ReadOnlySpan<byte> data)
    {
        var position = 0;
        var input = data.ToArray();
        byte Next() => position < input.Length ? input[position++] : (byte)0;
        double Coordinate() => VideoFrameCoordinates[Next() % VideoFrameCoordinates.Length];

        // The init: 255 is a coded width larger than the largest one
        var format = AllVideoPixelFormats[Next() % AllVideoPixelFormats.Length];
        var widthByte = Next();
        var codedWidth = widthByte == 255 ? 16385u : (uint)(widthByte % 17);
        var codedHeight = (uint)(Next() % 17);
        var flags = Next();
        var layout = (flags & 1) != 0 ? Enumerable.Range(0, Next() % 5).Select(_ => new PlaneLayout { Offset = (uint)(Next() * 2), Stride = Next() }).ToArray() : null;
        var visibleRect = (flags & 2) != 0 ? new DomRectInit { X = Coordinate(), Y = Coordinate(), Width = Coordinate(), Height = Coordinate() } : null;
        uint? displayWidth = (flags & 4) != 0 ? (uint)(Next() % 33) : null;
        uint? displayHeight = (flags & 8) != 0 ? (uint)(Next() % 33) : null;
        var rotation = VideoFrameRotations[Next() % VideoFrameRotations.Length];
        var flip = (flags & 16) != 0;
        var colorSpace = (flags & 32) != 0 ? new VideoColorSpaceInit { Matrix = VideoFrameMatrices[Next() % VideoFrameMatrices.Length], FullRange = (Next() & 1) != 0 } : null;
        var length = ((Next() << 8) | Next()) % 2200;
        var pixels = new byte[length];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)((i * 37) + length);
        }

        VideoFrame frame;
        try
        {
            frame = new VideoFrame(pixels, new VideoFrameBufferInit
            {
                Format = format,
                CodedWidth = codedWidth,
                CodedHeight = codedHeight,
                Timestamp = -1,
                Layout = layout,
                VisibleRect = visibleRect,
                DisplayWidth = displayWidth,
                DisplayHeight = displayHeight,
                Rotation = rotation,
                Flip = flip,
                ColorSpace = colorSpace,
            });
        }
        catch (TypeErrorException)
        {
            return;
        }

        // The visible pixels are copied: the coded size of the frame is the visible size
        FuzzAssert.Equal(format, frame.Format, "the format of the frame");
        FuzzAssert.True(frame.CodedWidth is > 0 && frame.CodedWidth <= codedWidth && frame.CodedHeight > 0 && frame.CodedHeight <= codedHeight, "the coded size is within the coded size of the init");
        if (visibleRect is not null)
        {
            FuzzAssert.Equal(((uint)visibleRect.Width, (uint)visibleRect.Height), (frame.CodedWidth, frame.CodedHeight), "the coded size is the size of the visible rectangle");
        }

        FuzzAssert.Equal((0d, 0d, (double)frame.CodedWidth, (double)frame.CodedHeight), (frame.VisibleRect!.X, frame.VisibleRect.Y, frame.VisibleRect.Width, frame.VisibleRect.Height), "the visible rectangle is the coded rectangle");
        FuzzAssert.True(frame.Rotation is 0 or 90 or 180 or 270, "the rotation is a multiple of 90 degrees");
        var expectedDisplay = displayWidth is { } width && displayHeight is { } height ? (width, height) : frame.Rotation is 90 or 270 ? (frame.CodedHeight, frame.CodedWidth) : (frame.CodedWidth, frame.CodedHeight);
        FuzzAssert.Equal(expectedDisplay, (frame.DisplayWidth, frame.DisplayHeight), "the display size");
        FuzzAssert.Equal(-1L, frame.Timestamp, "the timestamp");

        // The planes are tightly packed; a copy creates the same frame again
        var planes = VideoPixelFormats.GetPlanes(format);
        var expectedSize = planes.Sum(plane => plane.GetRowBytes(frame.CodedWidth) * plane.GetRows(frame.CodedHeight));
        var allocationSize = frame.AllocationSize();
        FuzzAssert.Equal(expectedSize, (long)allocationSize, "the allocation size of the planes");
        var copy = new byte[allocationSize];
        FuzzAssert.Equal(planes.Length, frame.CopyPixelsTo(copy).Count, "the layout has a plane per plane of the format");
        var again = new VideoFrame(copy, new VideoFrameBufferInit { Format = format, CodedWidth = frame.CodedWidth, CodedHeight = frame.CodedHeight, Timestamp = 0 });
        var copyAgain = new byte[allocationSize];
        again.CopyPixelsTo(copyAgain);
        FuzzAssert.True(copy.AsSpan().SequenceEqual(copyAgain), "a copy of the planes creates the same frame");

        // The rendered frame has the display size
        if ((long)frame.DisplayWidth * frame.DisplayHeight <= 4096)
        {
            var rendered = frame.GetRenderedPixels()!;
            FuzzAssert.Equal(((int)frame.DisplayWidth, (int)frame.DisplayHeight), (rendered.Width, rendered.Height), "the size of the rendered frame");
        }

        // The copies with the options of the input
        while (position < input.Length)
        {
            var copyFlags = Next();
            var rect = (copyFlags & 1) != 0 ? new DomRectInit { X = Coordinate(), Y = Coordinate(), Width = Coordinate(), Height = Coordinate() } : null;
            var copyLayout = (copyFlags & 2) != 0 ? Enumerable.Range(0, Next() % 5).Select(_ => new PlaneLayout { Offset = Next(), Stride = Next() }).ToArray() : null;
            var destinationFormat = VideoFrameFormats[Next() % VideoFrameFormats.Length];
            var options = new VideoFrameCopyToOptions { Rect = rect, Layout = copyLayout, Format = destinationFormat, ColorSpace = VideoFrameColorSpaces[Next() % VideoFrameColorSpaces.Length] };
            var extra = Next() % 16;

            uint size;
            try
            {
                size = frame.AllocationSize(options);
            }
            catch (Exception ex) when (ex is TypeErrorException or DomException)
            {
                var rejected = CatchException(() => frame.CopyPixelsTo(new byte[64], options));
                FuzzAssert.True(rejected?.GetType() == ex.GetType(), "CopyPixelsTo rejects the options AllocationSize rejects");
                continue;
            }

            if (size > 0 && (copyFlags & 4) != 0)
            {
                FuzzAssert.True(CatchException(() => frame.CopyPixelsTo(new byte[size - 1], options)) is TypeErrorException, "CopyPixelsTo rejects a destination smaller than the allocation size");
            }

            var destination = new byte[size + extra];
            Array.Fill(destination, (byte)0xA5);
            var planeLayouts = frame.CopyPixelsTo(destination, options);
            FuzzAssert.Equal(destinationFormat is null ? planes.Length : 1, planeLayouts.Count, "the layout has a plane per plane of the destination format");
            for (var i = (int)size; i < destination.Length; i++)
            {
                FuzzAssert.Equal((byte)0xA5, destination[i], "CopyPixelsTo writes no byte past the allocation size");
            }

            // An opaque frame converted to RGBA is opaque
            if (destinationFormat is VideoPixelFormat.RGBA && !VideoPixelFormats.HasAlpha(format) && copyLayout is null)
            {
                for (var i = 3; i < size; i += 4)
                {
                    FuzzAssert.Equal((byte)255, destination[i], "the alpha of an opaque frame");
                }
            }
        }

        var clone = frame.Clone();
        frame.Close();
        FuzzAssert.True(CatchException(() => frame.AllocationSize()) is DomException { Name: "InvalidStateError" }, "a closed frame has no allocation size");
        FuzzAssert.Equal(allocationSize, clone.AllocationSize(), "the clone keeps the pixels");
    }

    private static IEnumerable<FuzzSeed> GetVideoFrameBufferSeeds()
    {
        // The inits of the unit tests (WebCodecsTests.VideoFrame_*): format, coded width, coded height, flags, [layout],
        // [visible rectangle], [display width], [display height], rotation, [color space], length (2 bytes), then the copies
        byte[][] scenarios =
        [
            // I420 4x2, copied as it is, then to RGBA
            [0, 4, 2, 0, 0, 0, 12, 0, 0, 0, 0, 0, 1, 0, 0],

            // RGBA 2x2 at offset 4 with rows of 12 bytes
            [19, 2, 2, 1, 1, 2, 12, 0, 0, 28, 0, 0, 0, 0],

            // RGBA 2x2 with a visible rectangle (1, 0, 1, 2), copied with a rectangle and a layout
            [19, 2, 2, 3, 1, 2, 12, 1, 0, 1, 2, 0, 0, 28, 3, 0, 0, 1, 1, 1, 2, 8, 0, 0, 0],

            // I420A 2x2 to RGBA and RGBX; NV12 4x4; BGRA rotated and flipped with a display size
            [3, 2, 2, 0, 0, 0, 10, 0, 0, 1, 0, 0, 0, 2, 0, 0],
            [18, 4, 4, 32, 0, 3, 1, 0, 24, 0, 0, 1, 3, 0],
            [21, 2, 1, 28, 4, 3, 1, 0, 8, 0, 0, 4, 0, 0],

            // 10-bit 4:4:4 with alpha; an unaligned rectangle of I420; a zero coded width; a coded width too large
            [16, 3, 3, 0, 0, 0, 72, 0, 0, 1, 0, 0],
            [0, 4, 4, 2, 1, 0, 2, 2, 0, 0, 24],
            [19, 0, 2, 0, 0, 0, 16],
            [19, 255, 1, 0, 0, 255, 255],
        ];

        for (var i = 0; i < scenarios.Length; i++)
        {
            yield return new FuzzSeed("videoframe-buffer-" + i.ToString(CultureInfo.InvariantCulture), scenarios[i]);
        }
    }

    // AudioData

    private static readonly float[] AudioDataSampleRates = [8000, 44100, 48000, 0, -1, float.NaN, float.PositiveInfinity, 1e-30f, 3e38f];

    /// <summary>
    /// Creates audio data with the format, the channels, the frames, the sample rate and the samples of the input, then
    /// copies it with the options of the input. The init is invalid (a <c>TypeError</c>), a copy is out of range (a
    /// <c>RangeError</c>, for <c>AllocationSize</c> and <c>CopyTo</c> alike), or the copy writes no byte past the
    /// allocation size; a copy of floats through <c>f32-planar</c>, and a copy of integers through <c>s32</c>, give the
    /// samples back.
    /// </summary>
    public static void AudioDataTarget(ReadOnlySpan<byte> data)
    {
        var position = 0;
        var input = data.ToArray();
        byte Next() => position < input.Length ? input[position++] : (byte)0;

        var format = (AudioSampleFormat)(Next() % 8);
        var channels = (uint)(Next() % 5);
        var frames = (uint)(Next() % 9);
        var sampleRate = AudioDataSampleRates[Next() % AudioDataSampleRates.Length];
        var timestamp = (sbyte)Next() * 1000L;
        var samples = new byte[Next() % 160];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = Next();
        }

        AudioData audio;
        try
        {
            audio = new AudioData(new AudioDataInit { Format = format, SampleRate = sampleRate, NumberOfChannels = channels, NumberOfFrames = frames, Timestamp = timestamp, Data = samples });
        }
        catch (TypeErrorException)
        {
            return;
        }

        FuzzAssert.Equal((format, channels, frames, timestamp), (audio.Format!.Value, audio.NumberOfChannels, audio.NumberOfFrames, audio.Timestamp), "the description of the data");
        FuzzAssert.True(sampleRate > 0 && float.IsFinite(sampleRate), "the sample rate is positive");
        FuzzAssert.Equal((int)(channels * frames), audio.GetInterleavedFloats().Length, "a float per sample");
        var size = (int)(channels * frames) * AudioData.GetBytesPerSample(format);

        // The round trips: the floats through f32-planar, the integers through s32 (the conversions of the integers are shifts)
        var through = format is AudioSampleFormat.F32 or AudioSampleFormat.F32Planar ? AudioSampleFormat.F32Planar : AudioData.IsPlanar(format) ? AudioSampleFormat.S32Planar : AudioSampleFormat.S32;
        var intermediate = new AudioData(new AudioDataInit { Format = through, SampleRate = sampleRate, NumberOfChannels = channels, NumberOfFrames = frames, Timestamp = 0, Data = CopyAllSamples(audio, through) });
        var roundTrip = CopyAllSamples(intermediate, format);
        FuzzAssert.Equal(size, roundTrip.Length, "the size of the round trip");
        for (var i = 0; i < size; i += AudioData.GetBytesPerSample(format))
        {
            if (format is AudioSampleFormat.F32 or AudioSampleFormat.F32Planar)
            {
                var expected = BinaryPrimitives.ReadSingleLittleEndian(samples.AsSpan(i));
                var actual = BinaryPrimitives.ReadSingleLittleEndian(roundTrip.AsSpan(i));
                FuzzAssert.True(float.IsNaN(expected) ? float.IsNaN(actual) : BitConverter.SingleToInt32Bits(expected) == BitConverter.SingleToInt32Bits(actual), "a float sample is copied exactly");
            }
            else
            {
                FuzzAssert.True(samples.AsSpan(i, AudioData.GetBytesPerSample(format)).SequenceEqual(roundTrip.AsSpan(i, AudioData.GetBytesPerSample(format))), "an integer sample is copied exactly");
            }
        }

        // The copies with the options of the input: plane, frame offset, frame count (0 for none), format (0 for none), extra bytes
        while (position < input.Length)
        {
            var countByte = Next();
            var formatByte = Next();
            var options = new AudioDataCopyToOptions
            {
                PlaneIndex = (uint)(Next() % 5),
                FrameOffset = (uint)(Next() % 10),
                FrameCount = countByte == 0 ? null : (uint)(countByte - 1),
                Format = formatByte == 0 ? null : (AudioSampleFormat)((formatByte - 1) % 8),
            };
            var extra = Next() % 8;

            uint allocationSize;
            try
            {
                allocationSize = audio.AllocationSize(options);
            }
            catch (RangeErrorException)
            {
                FuzzAssert.True(CatchException(() => audio.CopyTo(new byte[1024], options)) is RangeErrorException, "CopyTo rejects the options AllocationSize rejects");
                continue;
            }

            var destinationFormat = options.Format ?? format;
            var frameCount = options.FrameCount ?? (frames - options.FrameOffset);
            FuzzAssert.Equal((long)frameCount * (AudioData.IsPlanar(destinationFormat) ? 1 : channels) * AudioData.GetBytesPerSample(destinationFormat), (long)allocationSize, "the allocation size of the copied samples");

            var destination = new byte[allocationSize + extra];
            Array.Fill(destination, (byte)0xA5);
            audio.CopyTo(destination, options);
            for (var i = (int)allocationSize; i < destination.Length; i++)
            {
                FuzzAssert.Equal((byte)0xA5, destination[i], "CopyTo writes no byte past the allocation size");
            }

            if (allocationSize > 0)
            {
                FuzzAssert.True(CatchException(() => audio.CopyTo(new byte[allocationSize - 1], options)) is RangeErrorException, "CopyTo rejects a destination smaller than the allocation size");
            }
        }

        var clone = audio.Clone();
        audio.Close();
        FuzzAssert.True(CatchException(() => audio.AllocationSize(new AudioDataCopyToOptions { PlaneIndex = 0 })) is DomException { Name: "InvalidStateError" }, "closed data has no allocation size");
        FuzzAssert.Equal(frames, clone.NumberOfFrames, "the clone keeps the samples");
    }

    /// <summary>Copies all the samples of the data in a format: the planes one after the other for a planar format.</summary>
    private static byte[] CopyAllSamples(AudioData audio, AudioSampleFormat format)
    {
        if (!AudioData.IsPlanar(format))
        {
            var options = new AudioDataCopyToOptions { PlaneIndex = 0, Format = format };
            var bytes = new byte[audio.AllocationSize(options)];
            audio.CopyTo(bytes, options);
            return bytes;
        }

        var result = new List<byte>();
        for (var plane = 0u; plane < audio.NumberOfChannels; plane++)
        {
            var options = new AudioDataCopyToOptions { PlaneIndex = plane, Format = format };
            var bytes = new byte[audio.AllocationSize(options)];
            audio.CopyTo(bytes, options);
            result.AddRange(bytes);
        }

        return [.. result];
    }

    private static IEnumerable<FuzzSeed> GetAudioDataSeeds()
    {
        // The data of the unit tests (WebCodecsTests.AudioData_*): format, channels, frames, sample rate, timestamp, length,
        // the samples, then the copies (frame count + 1, format + 1, plane, frame offset, extra bytes)
        byte[][] scenarios =
        [
            // s16, 2 channels, 3 frames: plane 1 as f32-planar, u8, frames 1 and 2 of plane 0 as s32-planar
            [1, 2, 3, 0, 100, 12, 0, 0, 0, 64, 0, 128, 255, 127, 0, 32, 0, 224, 0, 8, 1, 0, 0, 0, 1, 0, 0, 0, 3, 7, 0, 1, 0],

            // f32-planar, 1 channel, 4 frames (1.5, -2, 0.5, -0.5): as s16, u8-planar and f32
            [7, 1, 4, 2, 0, 16, 0, 0, 192, 63, 0, 0, 0, 192, 0, 0, 0, 63, 0, 0, 0, 191, 0, 2, 0, 0, 0, 0, 5, 0, 0, 0, 0, 4, 0, 0, 0],

            // f32 with a NaN and an infinity; u8; out of range copies
            [3, 1, 2, 1, 0, 8, 0, 0, 192, 127, 0, 0, 128, 127, 0, 1, 1, 0, 0],
            [0, 2, 2, 1, 0, 4, 0, 128, 255, 127, 0, 0, 1, 0, 0, 0, 0, 0, 2, 0],

            // An invalid sample rate; no frame; a buffer too small
            [1, 1, 1, 3, 0, 2, 0, 0],
            [1, 1, 0, 0, 0, 2, 0, 0],
            [1, 2, 2, 0, 0, 7, 0, 0, 0, 0, 0, 0, 0],
        ];

        for (var i = 0; i < scenarios.Length; i++)
        {
            yield return new FuzzSeed("audiodata-" + i.ToString(CultureInfo.InvariantCulture), scenarios[i]);
        }
    }

    // WebMWriter

    private static readonly string[] WebMCodecIds = ["V_VP8", "A_OPUS", "V_VP9", "A_VORBIS", ""];

    /// <summary>
    /// Writes a WebM stream with the tracks and the frames of the input, taking the bytes and starting clusters at the
    /// points of the input. The output parses as EBML (the IDs and the sizes are valid, the elements are within their
    /// parents, the unknown sizes are the ones of the segment and the clusters), and its blocks are the frames, in order,
    /// with their tracks, timestamps, key flags and data.
    /// </summary>
    public static void WebMWriterTarget(ReadOnlySpan<byte> data)
    {
        var position = 0;
        var input = data.ToArray();
        byte Next() => position < input.Length ? input[position++] : (byte)0;

        // The tracks: number, video, codec, private data, width, height, sample rate (kHz), channels, codec delay (100 µs)
        var tracks = new List<WebMTrack>();
        var trackCount = (Next() % 4) + 1;
        for (var i = 0; i < trackCount; i++)
        {
            var number = (Next() % 126) + 1;
            var isVideo = (Next() & 1) != 0;
            var codecId = WebMCodecIds[Next() % WebMCodecIds.Length];
            var privateLength = Next() % 5;
            byte[]? codecPrivate = privateLength == 0 ? null : [.. Enumerable.Range(0, privateLength - 1).Select(_ => Next())];
            tracks.Add(new WebMTrack(number, isVideo, codecId, codecPrivate, Width: Next(), Height: Next(), SampleRate: Next() * 1000, Channels: Next() % 3, CodecDelay: Next() * 100_000L));
        }

        var docType = (Next() & 1) == 0 ? "webm" : "matroska";
        var writer = new WebMWriter(tracks, docType);
        var output = new List<byte>();
        var frames = new List<(int Track, long Timestamp, bool Key, byte[] Data)>();
        var time = 0L;
        while (position < input.Length)
        {
            var operation = Next();
            switch (operation % 8)
            {
                case 0:
                    writer.StartCluster();
                    break;

                case 1:
                    var pending = writer.PendingLength;
                    var taken = writer.TakeBytes();
                    FuzzAssert.Equal(pending, (long)taken.Length, "TakeBytes returns the pending bytes");
                    output.AddRange(taken);
                    break;

                case 2:
                    FuzzAssert.True(CatchException(() => writer.WriteFrame(127, 0, isKeyFrame: true, [])) is ArgumentOutOfRangeException, "an unknown track is rejected");
                    FuzzAssert.True(CatchException(() => writer.WriteFrame(tracks[0].Number, -1, isKeyFrame: true, [])) is ArgumentOutOfRangeException, "a negative timestamp is rejected");
                    break;

                default:
                    // A step of the timestamp (in seconds for the operations above 127: the relative timestamps overflow), backwards too
                    var track = tracks[Next() % tracks.Count];
                    time = Math.Max(0, time + ((sbyte)Next() * (operation >= 128 ? 1000L : 1L)));
                    var isKeyFrame = (Next() & 1) != 0;
                    var frameData = Enumerable.Range(0, Next() % 8).Select(_ => Next()).ToArray();
                    writer.WriteFrame(track.Number, time, isKeyFrame, frameData);

                    // The first track of a number is the track of its blocks; an audio block is a key frame
                    frames.Add((track.Number, time, isKeyFrame || !tracks.First(t => t.Number == track.Number).IsVideo, frameData));
                    break;
            }
        }

        // (the header is written with the first frame, or by WriteHeader: a stream without frames has one too)
        writer.WriteHeader();
        output.AddRange(writer.TakeBytes());
        FuzzAssert.Equal(0L, writer.PendingLength, "TakeBytes clears the pending bytes");

        var stream = EbmlStream.Read([.. output]);
        FuzzAssert.Equal(docType, stream.DocType, "the document type");
        FuzzAssert.Equal(tracks.Count, stream.TrackCount, "a track entry per track");
        FuzzAssert.Equal(frames.Count, stream.Blocks.Count, "a block per frame");
        for (var i = 0; i < frames.Count; i++)
        {
            var (track, timestamp, key, frameData) = frames[i];
            var block = stream.Blocks[i];
            FuzzAssert.Equal((track, timestamp, key), (block.Track, block.Timestamp, block.Key), "the track, the timestamp and the key flag of a block");
            FuzzAssert.True(frameData.AsSpan().SequenceEqual(block.Data), "the data of a block");
        }
    }

    private static IEnumerable<FuzzSeed> GetWebMWriterSeeds()
    {
        // The streams of the unit tests (WebCodecsTests.WebMWriter_*): track count - 1, the tracks (number - 1, video, codec,
        // private length + 1, private, width, height, sample rate, channels, codec delay), the document type, then the
        // operations (3 to 7: a frame of a track, a step, a key flag, a length and the data; 0: a new cluster; 1: take the
        // bytes; 2: invalid frames; above 127: a step in seconds)
        byte[][] scenarios =
        [
            [
                1, 0, 1, 0, 0, 2, 2, 0, 0, 0, 1, 0, 1, 3, 1, 2, 0, 0, 48, 1, 65, 0,
                1, 3, 0, 0, 1, 1, 1, 3, 1, 10, 1, 1, 2, 3, 0, 10, 0, 1, 3, 3, 0, 20, 1, 1, 4, 131, 1, 40, 1, 1, 5, 0, 3, 1, 0, 1, 1, 6, 1,
            ],
            [0, 0, 1, 0, 0, 2, 2, 0, 0, 0, 1, 2, 3, 0, 1, 1, 9, 2],
        ];

        for (var i = 0; i < scenarios.Length; i++)
        {
            yield return new FuzzSeed("webm-writer-" + i.ToString(CultureInfo.InvariantCulture), scenarios[i]);
        }
    }

    /// <summary>
    /// A small EBML reader of the streams of <see cref="WebMWriter"/>: it checks the IDs and the sizes of the elements, that
    /// the elements are within their parents and that only the segment and the clusters have an unknown size, and returns
    /// the document type, the number of track entries and the blocks with their absolute timestamps.
    /// </summary>
    private sealed class EbmlStream
    {
        private const uint EbmlId = 0x1A45DFA3;
        private const uint SegmentId = 0x18538067;
        private const uint ClusterId = 0x1F43B675;
        private const uint TrackEntryId = 0xAE;
        private const uint DocTypeId = 0x4282;
        private const uint TimecodeId = 0xE7;
        private const uint SimpleBlockId = 0xA3;

        private static readonly HashSet<uint> MasterIds = [EbmlId, SegmentId, 0x1549A966, 0x1654AE6B, TrackEntryId, 0xE0, 0xE1, ClusterId];

        private static readonly HashSet<uint> LeafIds =
        [
            0x4286, 0x42F7, 0x42F2, 0x42F3, DocTypeId, 0x4287, 0x4285, 0x2AD7B1, 0x4D80, 0x5741, 0xD7, 0x73C5, 0x83, 0x86, 0x63A2, 0x56AA, 0x56BB,
            0xB0, 0xBA, 0xB5, 0x9F, TimecodeId, SimpleBlockId,
        ];

        public string? DocType { get; private set; }

        public int TrackCount { get; private set; }

        public List<(int Track, long Timestamp, bool Key, byte[] Data)> Blocks { get; } = [];

        public static EbmlStream Read(byte[] data)
        {
            var result = new EbmlStream();
            var parents = new Stack<(uint Id, long End)>();
            long? clusterTimecode = null;
            var position = 0;
            FuzzAssert.True(data.Length >= 4 && BinaryPrimitives.ReadUInt32BigEndian(data) == EbmlId, "the stream starts with the EBML header");
            while (position < data.Length)
            {
                // The elements of known size end where their parent ends; an element of unknown size has the rest of the stream
                while (parents.Count > 0 && parents.Peek().End == position)
                {
                    parents.Pop();
                }

                var id = (uint)ReadVariableInteger(data, ref position, maxLength: 4, keepMarker: true, out _);
                var size = ReadVariableInteger(data, ref position, maxLength: 8, keepMarker: false, out var unknownSize);
                if (id == ClusterId)
                {
                    // A cluster of unknown size ends at the next cluster
                    while (parents.Count > 0 && parents.Peek().Id != SegmentId)
                    {
                        parents.Pop();
                    }

                    clusterTimecode = null;
                }

                var parentEnd = parents.Count > 0 ? parents.Peek().End : data.Length;
                FuzzAssert.True(unknownSize ? id is SegmentId or ClusterId : position + (long)size <= parentEnd, "an element is within its parent");
                if (MasterIds.Contains(id))
                {
                    if (id == TrackEntryId)
                    {
                        result.TrackCount++;
                    }

                    parents.Push((id, unknownSize ? data.Length : position + (long)size));
                    continue;
                }

                FuzzAssert.True(LeafIds.Contains(id), "the element ID is one of the writer");
                var payload = data.AsSpan(position, (int)size);
                position += (int)size;
                switch (id)
                {
                    case DocTypeId:
                        result.DocType = Encoding.ASCII.GetString(payload);
                        break;

                    case TimecodeId:
                        FuzzAssert.True(payload.Length is > 0 and <= 8, "the size of the timecode");
                        clusterTimecode = 0;
                        foreach (var b in payload)
                        {
                            clusterTimecode = (clusterTimecode << 8) | b;
                        }

                        break;

                    case SimpleBlockId:
                        FuzzAssert.True(clusterTimecode is not null, "a block follows the timecode of its cluster");
                        FuzzAssert.True(payload.Length >= 4 && (payload[0] & 0x80) != 0, "a block starts with a track number of one byte");
                        result.Blocks.Add((payload[0] & 0x7F, clusterTimecode.GetValueOrDefault() + BinaryPrimitives.ReadInt16BigEndian(payload[1..]), (payload[3] & 0x80) != 0, payload[4..].ToArray()));
                        break;
                }
            }

            return result;
        }

        /// <summary>Reads a variable-size integer (https://www.rfc-editor.org/rfc/rfc8794#section-4).</summary>
        private static ulong ReadVariableInteger(byte[] data, ref int position, int maxLength, bool keepMarker, out bool allOnes)
        {
            FuzzAssert.True(position < data.Length, "the variable-size integer is within the stream");
            var first = data[position];
            var length = BitOperations.LeadingZeroCount((uint)first) - 23;
            FuzzAssert.True(length >= 1 && length <= maxLength && position + length <= data.Length, "the length of the variable-size integer");
            var marker = 0xFF >> length;
            var value = keepMarker ? first : (ulong)(first & marker);
            allOnes = (first & marker) == marker;
            for (var i = 1; i < length; i++)
            {
                var next = data[position + i];
                allOnes &= next == 0xFF;
                value = (value << 8) | next;
            }

            position += length;
            return value;
        }
    }
}
