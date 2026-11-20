using System.Buffers.Binary;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.JavaScript;
using Meziantou.Framework.WebPlatform.Media;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;
using Meziantou.Framework.WebPlatform.WebCodecs;

namespace Meziantou.Framework.WebPlatform.Tests.JavaScript;

/// <summary>
/// WebCodecs (https://w3c.github.io/webcodecs/): <see cref="VideoFrame"/>, <see cref="AudioData"/>, the encoded chunks, the
/// decoders and the encoders on the codecs of a <see cref="MediaCodecBackend"/>, <see cref="MediaCodecString"/> and the
/// WebM muxer of <c>MediaRecorder</c>.
/// </summary>
public sealed class WebCodecsTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly ScriptEngine _engine = TestScriptEngines.Create();

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    /// <summary>Runs a script and returns the value it assigns to <c>document.title</c> (after the event loop is idle).</summary>
    private string Run(string script, MediaCodecBackend? codecs = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            MediaCodecs = codecs,
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });

        var document = context.Navigate("https://example.com/", _ => "<script>" + script + "</script>");
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_errors.Select(error => error.Message));
        return document.Title;
    }

    /// <summary>A frame of a format whose bytes are their index.</summary>
    private static VideoFrame CreateFrame(VideoPixelFormat format, uint width, uint height, int length = 64)
    {
        var bytes = new byte[length];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)i;
        }

        return new VideoFrame(bytes, new VideoFrameBufferInit { Format = format, CodedWidth = width, CodedHeight = height, Timestamp = 0 });
    }

    private static byte[] CopyPixels(VideoFrame frame, VideoFrameCopyToOptions? options = null)
    {
        var bytes = new byte[frame.AllocationSize(options)];
        frame.CopyPixelsTo(bytes, options);
        return bytes;
    }

    // VideoFrame

    [Fact]
    public void VideoFrame_FromBuffer_CopiesThePixels()
    {
        #region WebPlatform_VideoFrameFromBuffer
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
        #endregion

        Assert.Null(frame.Format);
    }

    [Theory]
    [InlineData(VideoPixelFormat.I420, 12, 3)]
    [InlineData(VideoPixelFormat.I420P10, 24, 3)]
    [InlineData(VideoPixelFormat.I420P12, 24, 3)]
    [InlineData(VideoPixelFormat.I420A, 20, 4)]
    [InlineData(VideoPixelFormat.I420AP10, 40, 4)]
    [InlineData(VideoPixelFormat.I420AP12, 40, 4)]
    [InlineData(VideoPixelFormat.I422, 16, 3)]
    [InlineData(VideoPixelFormat.I422P10, 32, 3)]
    [InlineData(VideoPixelFormat.I422P12, 32, 3)]
    [InlineData(VideoPixelFormat.I422A, 24, 4)]
    [InlineData(VideoPixelFormat.I422AP10, 48, 4)]
    [InlineData(VideoPixelFormat.I422AP12, 48, 4)]
    [InlineData(VideoPixelFormat.I444, 24, 3)]
    [InlineData(VideoPixelFormat.I444P10, 48, 3)]
    [InlineData(VideoPixelFormat.I444P12, 48, 3)]
    [InlineData(VideoPixelFormat.I444A, 32, 4)]
    [InlineData(VideoPixelFormat.I444AP10, 64, 4)]
    [InlineData(VideoPixelFormat.I444AP12, 64, 4)]
    [InlineData(VideoPixelFormat.NV12, 12, 2)]
    [InlineData(VideoPixelFormat.RGBA, 32, 1)]
    [InlineData(VideoPixelFormat.RGBX, 32, 1)]
    [InlineData(VideoPixelFormat.BGRA, 32, 1)]
    [InlineData(VideoPixelFormat.BGRX, 32, 1)]
    public void VideoFrame_PixelFormats_HaveTheirPlanes(VideoPixelFormat format, int allocationSize, int planeCount)
    {
        var frame = CreateFrame(format, 4, 2);
        Assert.Equal(format, frame.Format);
        Assert.Equal((uint)allocationSize, frame.AllocationSize());

        var bytes = new byte[allocationSize];
        var layout = frame.CopyPixelsTo(bytes);
        Assert.Equal(planeCount, layout.Count);
        Assert.Equal(0u, layout[0].Offset);

        // The planes are tightly packed: the copy is the input, except the padding of the opaque formats that is opaque
        for (var i = 0; i < allocationSize; i++)
        {
            var expected = format is VideoPixelFormat.RGBX or VideoPixelFormat.BGRX && i % 4 == 3 ? (byte)255 : (byte)i;
            Assert.Equal(expected, bytes[i]);
        }

        // The YUV formats are converted to RGB; the opaque formats are opaque
        var rgba = CopyPixels(frame, new VideoFrameCopyToOptions { Format = VideoPixelFormat.RGBA });
        Assert.Equal(32, rgba.Length);
        if (format is VideoPixelFormat.I420 or VideoPixelFormat.I422 or VideoPixelFormat.I444 or VideoPixelFormat.NV12 or VideoPixelFormat.RGBX or VideoPixelFormat.BGRX)
        {
            Assert.Equal(255, rgba[3]);
        }
    }

    [Fact]
    public void VideoFrame_FromBuffer_DefaultColorSpace()
    {
        var yuv = CreateFrame(VideoPixelFormat.I420, 2, 2);
        Assert.Equal(VideoMatrixCoefficients.Bt709, yuv.ColorSpace.Matrix);
        Assert.Equal(VideoColorPrimaries.Bt709, yuv.ColorSpace.Primaries);
        Assert.Equal(false, yuv.ColorSpace.FullRange);

        var rgb = CreateFrame(VideoPixelFormat.RGBA, 2, 2);
        Assert.Equal(VideoMatrixCoefficients.Rgb, rgb.ColorSpace.Matrix);
        Assert.Equal(true, rgb.ColorSpace.FullRange);
    }

    [Fact]
    public void VideoFrame_CopyTo_ConvertsTheYuvFormatsToRgb()
    {
        // Limited range (BT.709): Y = 16 is black, Y = 235 is white, U = V = 128 is gray
        byte[] i420 = [16, 235, 16, 235, 128, 128];
        var limited = new VideoFrame(i420, new VideoFrameBufferInit { Format = VideoPixelFormat.I420, CodedWidth = 2, CodedHeight = 2, Timestamp = 0 });
        Assert.Equal([0, 0, 0, 255, 255, 255, 255, 255, 0, 0, 0, 255, 255, 255, 255, 255], CopyPixels(limited, new VideoFrameCopyToOptions { Format = VideoPixelFormat.RGBA }));

        // Full range: Y = 255 is white
        byte[] fullRangeBytes = [255, 255, 255, 255, 128, 128];
        var fullRange = new VideoFrame(fullRangeBytes, new VideoFrameBufferInit { Format = VideoPixelFormat.I420, CodedWidth = 2, CodedHeight = 2, Timestamp = 0, ColorSpace = new VideoColorSpaceInit { FullRange = true, Matrix = VideoMatrixCoefficients.Bt709 } });
        Assert.Equal([255, 255, 255, 255], CopyPixels(fullRange, new VideoFrameCopyToOptions { Format = VideoPixelFormat.RGBA })[..4]);

        // The RGB matrix: the planes are G, B and R
        byte[] gbr = [10, 20, 30];
        var rgbMatrix = new VideoFrame(gbr, new VideoFrameBufferInit { Format = VideoPixelFormat.I444, CodedWidth = 1, CodedHeight = 1, Timestamp = 0, ColorSpace = new VideoColorSpaceInit { Matrix = VideoMatrixCoefficients.Rgb, FullRange = true } });
        Assert.Equal([30, 10, 20, 255], CopyPixels(rgbMatrix, new VideoFrameCopyToOptions { Format = VideoPixelFormat.RGBA }));
        Assert.Equal([20, 10, 30, 255], CopyPixels(rgbMatrix, new VideoFrameCopyToOptions { Format = VideoPixelFormat.BGRX }));

        // 10-bit samples (little-endian): Y = 940 is white in the limited range
        var p10 = new byte[12];
        BinaryPrimitives.WriteUInt16LittleEndian(p10, 940);
        BinaryPrimitives.WriteUInt16LittleEndian(p10.AsSpan(2), 940);
        BinaryPrimitives.WriteUInt16LittleEndian(p10.AsSpan(4), 64);
        BinaryPrimitives.WriteUInt16LittleEndian(p10.AsSpan(6), 64);
        BinaryPrimitives.WriteUInt16LittleEndian(p10.AsSpan(8), 512);
        BinaryPrimitives.WriteUInt16LittleEndian(p10.AsSpan(10), 512);
        var tenBits = new VideoFrame(p10, new VideoFrameBufferInit { Format = VideoPixelFormat.I420P10, CodedWidth = 2, CodedHeight = 2, Timestamp = 0 });
        Assert.Equal([255, 255, 255, 255, 255, 255, 255, 255, 0, 0, 0, 255, 0, 0, 0, 255], CopyPixels(tenBits, new VideoFrameCopyToOptions { Format = VideoPixelFormat.RGBA }));

        // NV12: the U and V samples are interleaved
        byte[] nv12 = [235, 235, 235, 235, 128, 128];
        var interleaved = new VideoFrame(nv12, new VideoFrameBufferInit { Format = VideoPixelFormat.NV12, CodedWidth = 2, CodedHeight = 2, Timestamp = 0 });
        Assert.Equal([255, 255, 255, 255], CopyPixels(interleaved, new VideoFrameCopyToOptions { Format = VideoPixelFormat.RGBA })[..4]);

        // The alpha plane, and its removal by an opaque format
        byte[] i420a = [235, 235, 235, 235, 128, 128, 0, 64, 128, 255];
        var alpha = new VideoFrame(i420a, new VideoFrameBufferInit { Format = VideoPixelFormat.I420A, CodedWidth = 2, CodedHeight = 2, Timestamp = 0 });
        Assert.Equal([0, 64, 128, 255], CopyPixels(alpha, new VideoFrameCopyToOptions { Format = VideoPixelFormat.RGBA }).Where((_, i) => i % 4 == 3).ToArray());
        Assert.Equal([255, 255, 255, 255], CopyPixels(alpha, new VideoFrameCopyToOptions { Format = VideoPixelFormat.RGBX }).Where((_, i) => i % 4 == 3).ToArray());
        var discarded = new VideoFrame(alpha, new VideoFrameInit { Alpha = AlphaOption.Discard });
        Assert.Equal(VideoPixelFormat.I420, discarded.Format);
        Assert.Equal(6u, discarded.AllocationSize());

        // Only the RGB formats are destination formats
        Assert.Equal("NotSupportedError", Assert.Throws<DomException>(() => limited.AllocationSize(new VideoFrameCopyToOptions { Format = VideoPixelFormat.I420 })).Name);
    }

    [Fact]
    public void VideoFrame_CopyTo_ConvertsTheColorSpace()
    {
        byte[] red = [255, 0, 0, 255];
        var frame = new VideoFrame(red, new VideoFrameBufferInit { Format = VideoPixelFormat.RGBA, CodedWidth = 1, CodedHeight = 1, Timestamp = 0 });

        Assert.Equal([255, 0, 0, 255], CopyPixels(frame, new VideoFrameCopyToOptions { Format = VideoPixelFormat.RGBA, ColorSpace = PredefinedColorSpace.Srgb }));

        // The red of sRGB is inside of the gamut of Display P3
        var p3 = CopyPixels(frame, new VideoFrameCopyToOptions { Format = VideoPixelFormat.RGBA, ColorSpace = PredefinedColorSpace.DisplayP3 });
        Assert.True(p3[0] < 255);
        Assert.True(p3[1] > 0);
        Assert.Equal(255, p3[3]);
    }

    [Fact]
    public void VideoFrame_FromBuffer_LayoutAndVisibleRect()
    {
        // 2x2 RGBA pixels at offset 4, rows of 12 bytes (4 bytes of padding)
        var bytes = new byte[28];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)i;
        }

        var padded = new VideoFrame(bytes, new VideoFrameBufferInit { Format = VideoPixelFormat.RGBA, CodedWidth = 2, CodedHeight = 2, Timestamp = 0, Layout = [new PlaneLayout { Offset = 4, Stride = 12 }] });
        Assert.Equal([4, 5, 6, 7, 8, 9, 10, 11, 16, 17, 18, 19, 20, 21, 22, 23], CopyPixels(padded));

        // Only the visible pixels are copied: the coded size of the frame is the visible size
        var visible = new VideoFrame(bytes, new VideoFrameBufferInit { Format = VideoPixelFormat.RGBA, CodedWidth = 2, CodedHeight = 2, Timestamp = 0, Layout = [new PlaneLayout { Offset = 4, Stride = 12 }], VisibleRect = new DomRectInit { X = 1, Y = 0, Width = 1, Height = 2 } });
        Assert.Equal((1u, 2u), (visible.CodedWidth, visible.CodedHeight));
        Assert.Equal((0d, 0d, 1d, 2d), (visible.VisibleRect!.X, visible.VisibleRect.Y, visible.VisibleRect.Width, visible.VisibleRect.Height));
        Assert.Equal((1u, 2u), (visible.DisplayWidth, visible.DisplayHeight));
        Assert.Equal([8, 9, 10, 11, 20, 21, 22, 23], CopyPixels(visible));

        // The rectangle and the layout of the destination
        var frame = CreateFrame(VideoPixelFormat.RGBA, 4, 2);
        var options = new VideoFrameCopyToOptions { Rect = new DomRectInit { X = 2, Y = 1, Width = 2, Height = 1 }, Layout = [new PlaneLayout { Offset = 2, Stride = 8 }] };
        Assert.Equal(10u, frame.AllocationSize(options));
        var destination = new byte[10];
        var layout = frame.CopyPixelsTo(destination, options);
        Assert.Equal((2u, 8u), (layout[0].Offset, layout[0].Stride));
        Assert.Equal([0, 0, 24, 25, 26, 27, 28, 29, 30, 31], destination);
        Assert.Throws<TypeErrorException>(() => frame.CopyPixelsTo(new byte[9], options));

        // The I420 planes of a rectangle: the rows of the chroma planes are half as large
        var i420 = CreateFrame(VideoPixelFormat.I420, 4, 4);
        var i420Options = new VideoFrameCopyToOptions { Rect = new DomRectInit { X = 2, Y = 2, Width = 2, Height = 2 } };
        Assert.Equal([10, 11, 14, 15, 19, 23], CopyPixels(i420, i420Options));
    }

    [Fact]
    public void VideoFrame_FromBuffer_ValidatesTheInit()
    {
        VideoFrame Create(VideoFrameBufferInit init, int length = 64) => new(new byte[length], init);

        Assert.Throws<TypeErrorException>(() => Create(new VideoFrameBufferInit { Format = VideoPixelFormat.RGBA, CodedWidth = 0, CodedHeight = 1, Timestamp = 0 }));
        Assert.Throws<TypeErrorException>(() => Create(new VideoFrameBufferInit { Format = VideoPixelFormat.RGBA, CodedWidth = 16385, CodedHeight = 1, Timestamp = 0 }));
        Assert.Throws<TypeErrorException>(() => Create(new VideoFrameBufferInit { Format = VideoPixelFormat.RGBA, CodedWidth = 4, CodedHeight = 4, Timestamp = 0 }, length: 63));
        Assert.Throws<TypeErrorException>(() => Create(new VideoFrameBufferInit { Format = VideoPixelFormat.I420, CodedWidth = 4, CodedHeight = 4, Timestamp = 0, VisibleRect = new DomRectInit { X = 1, Y = 0, Width = 2, Height = 2 } }));
        Assert.Throws<TypeErrorException>(() => Create(new VideoFrameBufferInit { Format = VideoPixelFormat.RGBA, CodedWidth = 2, CodedHeight = 2, Timestamp = 0, VisibleRect = new DomRectInit { X = 1, Y = 0, Width = 2, Height = 2 } }));
        Assert.Throws<TypeErrorException>(() => Create(new VideoFrameBufferInit { Format = VideoPixelFormat.RGBA, CodedWidth = 2, CodedHeight = 2, Timestamp = 0, VisibleRect = new DomRectInit { X = 0, Y = 0, Width = 0, Height = 2 } }));
        Assert.Throws<TypeErrorException>(() => Create(new VideoFrameBufferInit { Format = VideoPixelFormat.RGBA, CodedWidth = 2, CodedHeight = 2, Timestamp = 0, VisibleRect = new DomRectInit { X = double.NaN, Y = 0, Width = 1, Height = 1 } }));
        Assert.Throws<TypeErrorException>(() => Create(new VideoFrameBufferInit { Format = VideoPixelFormat.RGBA, CodedWidth = 2, CodedHeight = 2, Timestamp = 0, DisplayWidth = 4 }));
        Assert.Throws<TypeErrorException>(() => Create(new VideoFrameBufferInit { Format = VideoPixelFormat.RGBA, CodedWidth = 2, CodedHeight = 2, Timestamp = 0, DisplayWidth = 0, DisplayHeight = 4 }));
        Assert.Throws<TypeErrorException>(() => Create(new VideoFrameBufferInit { Format = VideoPixelFormat.RGBA, CodedWidth = 2, CodedHeight = 2, Timestamp = 0, Rotation = double.PositiveInfinity }));
        Assert.Throws<TypeErrorException>(() => Create(new VideoFrameBufferInit { Format = VideoPixelFormat.I420, CodedWidth = 2, CodedHeight = 2, Timestamp = 0, Layout = [new PlaneLayout { Offset = 0, Stride = 2 }] }));
        Assert.Throws<TypeErrorException>(() => Create(new VideoFrameBufferInit { Format = VideoPixelFormat.RGBA, CodedWidth = 2, CodedHeight = 2, Timestamp = 0, Layout = [new PlaneLayout { Offset = 0, Stride = 4 }] }));
        Assert.Throws<TypeErrorException>(() => Create(new VideoFrameBufferInit { Format = VideoPixelFormat.NV12, CodedWidth = 2, CodedHeight = 2, Timestamp = 0, Layout = [new PlaneLayout { Offset = 0, Stride = 2 }, new PlaneLayout { Offset = 2, Stride = 2 }] }));
        Assert.Throws<TypeErrorException>(() => new VideoFrame("pixels", new VideoFrameBufferInit { Format = VideoPixelFormat.RGBA, CodedWidth = 1, CodedHeight = 1, Timestamp = 0 }));
    }

    [Fact]
    public void VideoFrame_RotationFlipAndDisplaySize()
    {
        // 2x1 pixels: red, green
        byte[] pixels = [255, 0, 0, 255, 0, 255, 0, 255];
        VideoFrame Create(double rotation, bool flip = false) => new(pixels, new VideoFrameBufferInit { Format = VideoPixelFormat.RGBA, CodedWidth = 2, CodedHeight = 1, Timestamp = 0, Rotation = rotation, Flip = flip });

        // The rotation is rounded to the nearest multiple of 90 degrees, in [0, 360)
        Assert.Equal(90d, Create(45).Rotation);
        Assert.Equal(0d, Create(44.9).Rotation);
        Assert.Equal(270d, Create(-90).Rotation);
        Assert.Equal(90d, Create(450).Rotation);
        Assert.Equal(0d, Create(-360).Rotation);

        // A rotation of 90 or 270 degrees swaps the display size; the rendered frame is rotated, then flipped
        var rotated = Create(90);
        Assert.Equal((2u, 1u), (rotated.CodedWidth, rotated.CodedHeight));
        Assert.Equal((1u, 2u), (rotated.DisplayWidth, rotated.DisplayHeight));
        var rendered = rotated.GetRenderedPixels()!;
        Assert.Equal((1, 2), (rendered.Width, rendered.Height));
        Assert.Equal([255, 0, 0, 255, 0, 255, 0, 255], rendered.Rgba);

        var flipped = Create(0, flip: true).GetRenderedPixels()!;
        Assert.Equal([0, 255, 0, 255, 255, 0, 0, 255], flipped.Rgba);

        // The rotation of a frame created from a flipped frame is subtracted
        var source = Create(90, flip: true);
        var combined = new VideoFrame(source, new VideoFrameInit { Rotation = 90 });
        Assert.Equal(0d, combined.Rotation);
        Assert.True(combined.Flip);
        Assert.Equal((2u, 1u), (combined.DisplayWidth, combined.DisplayHeight));

        var unflipped = new VideoFrame(source, new VideoFrameInit { Flip = true });
        Assert.Equal(90d, unflipped.Rotation);
        Assert.False(unflipped.Flip);

        // An explicit display size scales the rendered frame
        var scaled = new VideoFrame(pixels, new VideoFrameBufferInit { Format = VideoPixelFormat.RGBA, CodedWidth = 2, CodedHeight = 1, Timestamp = 0, DisplayWidth = 4, DisplayHeight = 3 });
        Assert.Equal((4u, 3u), (scaled.DisplayWidth, scaled.DisplayHeight));
        Assert.Equal((4, 3), (scaled.GetRenderedPixels()!.Width, scaled.GetRenderedPixels()!.Height));
    }

    [Fact]
    public void VideoFrame_FromFrame_SharesThePixels()
    {
        var frame = new VideoFrame(new byte[32], new VideoFrameBufferInit { Format = VideoPixelFormat.RGBA, CodedWidth = 4, CodedHeight = 2, Timestamp = 10, Duration = 20 });

        // A visible rectangle of the other frame: the coded size is the one of the other frame
        var cropped = new VideoFrame(frame, new VideoFrameInit { VisibleRect = new DomRectInit { X = 2, Y = 0, Width = 2, Height = 2 }, Timestamp = 30 });
        Assert.Equal((4u, 2u), (cropped.CodedWidth, cropped.CodedHeight));
        Assert.Equal((2d, 2d), (cropped.VisibleRect!.X, cropped.VisibleRect.Width));
        Assert.Equal((2u, 2u), (cropped.DisplayWidth, cropped.DisplayHeight));
        Assert.Equal(30L, cropped.Timestamp);
        Assert.Equal(20UL, cropped.Duration);
        Assert.Equal(16u, cropped.AllocationSize());

        // The default display size keeps the scale of the other frame
        var stretched = new VideoFrame(frame, new VideoFrameInit { DisplayWidth = 8, DisplayHeight = 2 });
        var half = new VideoFrame(stretched, new VideoFrameInit { VisibleRect = new DomRectInit { X = 0, Y = 0, Width = 2, Height = 2 } });
        Assert.Equal((4u, 2u), (half.DisplayWidth, half.DisplayHeight));

        Assert.Equal(VideoPixelFormat.RGBX, new VideoFrame(frame, new VideoFrameInit { Alpha = AlphaOption.Discard }).Format);
        Assert.Throws<TypeErrorException>(() => new VideoFrame(frame, new VideoFrameInit { VisibleRect = new DomRectInit { X = 3, Y = 0, Width = 2, Height = 2 } }));
        Assert.Throws<TypeErrorException>(() => new VideoFrame(frame, new VideoFrameInit { DisplayHeight = 2 }));
    }

    [Fact]
    public async Task VideoFrame_CloseAndClone()
    {
        var frame = CreateFrame(VideoPixelFormat.I420, 2, 2);
        var clone = frame.Clone();
        frame.Close();

        Assert.Null(frame.Format);
        Assert.Equal((0u, 0u, 0u, 0u), (frame.CodedWidth, frame.CodedHeight, frame.DisplayWidth, frame.DisplayHeight));
        Assert.Null(frame.VisibleRect);
        Assert.Null(frame.CodedRect);
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => frame.AllocationSize()).Name);
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => frame.CopyPixelsTo(new byte[6])).Name);
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => frame.Clone()).Name);
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => frame.Metadata()).Name);
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => new VideoFrame(frame)).Name);
        Assert.True(frame.CopyTo(new byte[6]).IsFaulted);
        Assert.Null(frame.GetRenderedPixels());

        // The clone keeps the pixels
        Assert.Equal(VideoPixelFormat.I420, clone.Format);
        Assert.Equal([0, 1, 2, 3, 4, 5], CopyPixels(clone));
        Assert.NotNull(clone.Metadata());

        // Without an event loop, CopyTo completes at once
        var copy = clone.CopyTo(new byte[6]);
        Assert.True(copy.IsCompletedSuccessfully);
        Assert.Equal(3, (await copy).Count);
    }

    [Fact]
    public void VideoFrame_Script()
    {
        var result = Run("""
            (async () => {
              const results = [];
              const data = new Uint8Array([235, 235, 235, 235, 235, 235, 235, 235, 128, 128, 128, 128]);
              const frame = new VideoFrame(data, { format: 'I420', codedWidth: 4, codedHeight: 2, timestamp: -5, duration: 10, transfer: [data.buffer] });
              results.push(data.byteLength, frame.format, frame.codedWidth, frame.codedHeight, frame.timestamp, frame.duration, frame.colorSpace.matrix, frame.colorSpace.fullRange, frame.allocationSize());
              const layout = await frame.copyTo(new Uint8Array(12));
              results.push(layout.map(plane => plane.offset + ':' + plane.stride).join(' '));
              const rgba = new Uint8Array(32);
              await frame.copyTo(rgba, { format: 'RGBA', rect: { x: 0, y: 0, width: 4, height: 2 } });
              results.push(rgba.slice(0, 4).join(' '));
              try { await frame.copyTo(new Uint8Array(4)); } catch (e) { results.push(e.name); }
              try { new VideoFrame(new Uint8Array(4), { format: 'I420', codedWidth: 4, codedHeight: 2, timestamp: 0 }); } catch (e) { results.push(e.name); }

              const canvas = document.createElement('canvas');
              canvas.width = 3;
              canvas.height = 2;
              try { new VideoFrame(canvas); } catch (e) { results.push(e.name); }
              const fromCanvas = new VideoFrame(canvas, { timestamp: 1, alpha: 'discard' });
              results.push(fromCanvas.format, fromCanvas.codedWidth, fromCanvas.displayHeight, fromCanvas.colorSpace.primaries);

              const clone = structuredClone(fromCanvas);
              results.push(clone instanceof VideoFrame, clone.codedWidth, fromCanvas.format);
              const transferred = structuredClone(fromCanvas, { transfer: [fromCanvas] });
              results.push(transferred.codedWidth, fromCanvas.format);
              try { structuredClone(fromCanvas); } catch (e) { results.push(e.name); }

              frame.close();
              try { frame.allocationSize(); } catch (e) { results.push(e.name); }
              document.title = results.join();
            })();
            """);

        Assert.Equal("0,I420,4,2,-5,10,bt709,false,12,0:4 8:2 10:2,255 255 255 255,TypeError,TypeError,TypeError,RGBX,3,2,bt709,true,3,RGBX,3,,DataCloneError,InvalidStateError", result);
    }

    // AudioData

    [Fact]
    public void AudioData_ConvertsTheSampleFormats()
    {
        // 3 frames of 2 channels, signed 16-bit, interleaved
        var samples = new byte[12];
        short[] values = [0, 16384, -32768, 32767, 8192, -8192];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(samples.AsSpan(i * 2), values[i]);
        }

        var data = new AudioData(new AudioDataInit { Format = AudioSampleFormat.S16, SampleRate = 8000, NumberOfChannels = 2, NumberOfFrames = 3, Timestamp = 100, Data = samples });
        Assert.Equal(AudioSampleFormat.S16, data.Format);
        Assert.Equal((8000f, 2u, 3u, 100L), (data.SampleRate, data.NumberOfChannels, data.NumberOfFrames, data.Timestamp));
        Assert.Equal(375UL, data.Duration);

        // A plane of a planar format is a channel
        var planeOptions = new AudioDataCopyToOptions { PlaneIndex = 1, Format = AudioSampleFormat.F32Planar };
        Assert.Equal(12u, data.AllocationSize(planeOptions));
        var floats = new byte[12];
        data.CopyTo(floats, planeOptions);
        Assert.Equal([0.5f, 32767 / 32768f, -0.25f], [BinaryPrimitives.ReadSingleLittleEndian(floats), BinaryPrimitives.ReadSingleLittleEndian(floats.AsSpan(4)), BinaryPrimitives.ReadSingleLittleEndian(floats.AsSpan(8))]);

        // The integer formats are converted by shifts
        var u8 = new byte[6];
        data.CopyTo(u8, new AudioDataCopyToOptions { PlaneIndex = 0, Format = AudioSampleFormat.U8 });
        Assert.Equal([128, 192, 0, 255, 160, 96], u8);

        var s32 = new byte[8];
        data.CopyTo(s32, new AudioDataCopyToOptions { PlaneIndex = 0, Format = AudioSampleFormat.S32Planar, FrameOffset = 1, FrameCount = 2 });
        Assert.Equal([int.MinValue, 8192 << 16], [BinaryPrimitives.ReadInt32LittleEndian(s32), BinaryPrimitives.ReadInt32LittleEndian(s32.AsSpan(4))]);

        // The frames of an interleaved copy
        var s16 = new byte[4];
        data.CopyTo(s16, new AudioDataCopyToOptions { PlaneIndex = 0, FrameOffset = 2 });
        Assert.Equal([8192, -8192], [BinaryPrimitives.ReadInt16LittleEndian(s16), BinaryPrimitives.ReadInt16LittleEndian(s16.AsSpan(2))]);
    }

    [Fact]
    public void AudioData_ClampsTheFloats()
    {
        var samples = new byte[16];
        float[] values = [1.5f, -2f, 0.5f, -0.5f];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(samples.AsSpan(i * 4), values[i]);
        }

        var data = new AudioData(new AudioDataInit { Format = AudioSampleFormat.F32Planar, SampleRate = 48000, NumberOfChannels = 1, NumberOfFrames = 4, Timestamp = 0, Data = samples });

        var s16 = new byte[8];
        data.CopyTo(s16, new AudioDataCopyToOptions { PlaneIndex = 0, Format = AudioSampleFormat.S16 });
        Assert.Equal([32767, -32768, 16384, -16384], Enumerable.Range(0, 4).Select(i => (int)BinaryPrimitives.ReadInt16LittleEndian(s16.AsSpan(i * 2))).ToArray());

        var u8 = new byte[4];
        data.CopyTo(u8, new AudioDataCopyToOptions { PlaneIndex = 0, Format = AudioSampleFormat.U8Planar });
        Assert.Equal([255, 0, 192, 64], u8);

        // The floats are copied as they are
        var f32 = new byte[16];
        data.CopyTo(f32, new AudioDataCopyToOptions { PlaneIndex = 0, Format = AudioSampleFormat.F32 });
        Assert.Equal(samples, f32);
        Assert.Equal(values, data.GetInterleavedFloats());
    }

    [Fact]
    public void AudioData_ValidatesTheInitAndTheCopies()
    {
        AudioData Create(float sampleRate = 8000, uint frames = 2, uint channels = 2, int length = 8) => new(new AudioDataInit { Format = AudioSampleFormat.S16, SampleRate = sampleRate, NumberOfChannels = channels, NumberOfFrames = frames, Timestamp = 0, Data = new byte[length] });

        Assert.Throws<TypeErrorException>(() => Create(sampleRate: 0));
        Assert.Throws<TypeErrorException>(() => Create(sampleRate: float.NaN));
        Assert.Throws<TypeErrorException>(() => Create(frames: 0));
        Assert.Throws<TypeErrorException>(() => Create(channels: 0));
        Assert.Throws<TypeErrorException>(() => Create(length: 7));
        Assert.Throws<TypeErrorException>(() => new AudioData(new AudioDataInit { Format = AudioSampleFormat.F32, SampleRate = 8000, NumberOfChannels = 1, NumberOfFrames = 1, Timestamp = 0, Data = 42 }));

        var data = Create();
        Assert.Throws<RangeErrorException>(() => data.AllocationSize(new AudioDataCopyToOptions { PlaneIndex = 1 }));
        Assert.Throws<RangeErrorException>(() => data.AllocationSize(new AudioDataCopyToOptions { PlaneIndex = 2, Format = AudioSampleFormat.S16Planar }));
        Assert.Throws<RangeErrorException>(() => data.AllocationSize(new AudioDataCopyToOptions { PlaneIndex = 0, FrameOffset = 2 }));
        Assert.Throws<RangeErrorException>(() => data.AllocationSize(new AudioDataCopyToOptions { PlaneIndex = 0, FrameOffset = 1, FrameCount = 2 }));
        Assert.Throws<RangeErrorException>(() => data.CopyTo(new byte[7], new AudioDataCopyToOptions { PlaneIndex = 0 }));

        var clone = data.Clone();
        data.Close();
        Assert.Null(data.Format);
        Assert.Equal((0f, 0u, 0u, 0UL), (data.SampleRate, data.NumberOfFrames, data.NumberOfChannels, data.Duration));
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => data.AllocationSize(new AudioDataCopyToOptions { PlaneIndex = 0 })).Name);
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => data.CopyTo(new byte[8], new AudioDataCopyToOptions { PlaneIndex = 0 })).Name);
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => data.Clone()).Name);
        Assert.Empty(data.GetInterleavedFloats());
        Assert.Equal(8u, clone.AllocationSize(new AudioDataCopyToOptions { PlaneIndex = 0 }));
    }

    [Fact]
    public void AudioData_Script()
    {
        var result = Run("""
            const results = [];
            const samples = new Float32Array([0, 0.5, -0.5, 1]);
            const data = new AudioData({ format: 'f32', sampleRate: 48000, numberOfFrames: 2, numberOfChannels: 2, timestamp: -1000, data: samples, transfer: [samples.buffer] });
            results.push(samples.byteLength, data.format, data.sampleRate, data.numberOfFrames, data.numberOfChannels, data.timestamp, data.duration);
            const plane = new Float32Array(2);
            data.copyTo(plane, { planeIndex: 1, format: 'f32-planar' });
            results.push(plane.join(' '), data.allocationSize({ planeIndex: 0, format: 's16' }));
            try { data.copyTo(new Float32Array(1), { planeIndex: 0 }); } catch (e) { results.push(e.name); }
            const clone = structuredClone(data);
            results.push(clone.numberOfFrames);
            data.close();
            results.push(data.format, data.numberOfFrames);
            try { data.clone(); } catch (e) { results.push(e.name); }
            try { new AudioData({ format: 'u8', sampleRate: 0, numberOfFrames: 1, numberOfChannels: 1, timestamp: 0, data: new Uint8Array(1) }); } catch (e) { results.push(e.name); }
            document.title = results.join();
            """);

        Assert.Equal("0,f32,48000,2,2,-1000,41,0.5 1,8,RangeError,2,,0,InvalidStateError,TypeError", result);
    }

    // Encoded chunks

    [Fact]
    public void EncodedChunks_Script()
    {
        var result = Run("""
            const results = [];
            const audio = new EncodedAudioChunk({ type: 'key', timestamp: -10, duration: 20, data: new Uint8Array([1, 2, 3]) });
            const copy = new Uint8Array(3);
            audio.copyTo(copy);
            results.push(audio.type, audio.timestamp, audio.duration, audio.byteLength, copy.join(' '));
            try { audio.copyTo(new Uint8Array(2)); } catch (e) { results.push(e.name); }

            const bytes = new Uint8Array([4, 5]);
            const video = new EncodedVideoChunk({ type: 'delta', timestamp: -9007199254740991, data: bytes, transfer: [bytes.buffer] });
            results.push(video.type, video.timestamp, video.duration, video.byteLength, bytes.byteLength);
            const clone = structuredClone(video);
            results.push(clone.byteLength);
            try { new EncodedVideoChunk({ type: 'other', timestamp: 0, data: new Uint8Array(1) }); } catch (e) { results.push(e.name); }
            document.title = results.join();
            """);

        Assert.Equal("key,-10,20,3,1 2 3,TypeError,delta,-9007199254740991,,2,0,2,TypeError", result);
    }

    [Fact]
    public void EncodedChunks_DotNet()
    {
        var chunk = new EncodedVideoChunk(new EncodedVideoChunkInit { Type = EncodedVideoChunkType.Key, Timestamp = 1, Duration = 2, Data = new ReadOnlyMemory<byte>([1, 2, 3]) });
        Assert.Equal([1, 2, 3], chunk.Data.ToArray());
        Assert.Equal(3u, chunk.ByteLength);

        var destination = new byte[4];
        chunk.CopyTo(destination);
        Assert.Equal([1, 2, 3, 0], destination);
        Assert.Throws<TypeErrorException>(() => chunk.CopyTo(new byte[2]));

        var audio = new EncodedAudioChunk(new EncodedAudioChunkInit { Type = EncodedAudioChunkType.Delta, Timestamp = -1, Data = new byte[] { 9 } });
        Assert.Equal((EncodedAudioChunkType.Delta, -1L, (ulong?)null), (audio.Type, audio.Timestamp, audio.Duration));
    }

    // The codecs

    [Fact]
    public async Task Codecs_DecodeTheAudioOfAPage()
    {
        #region WebPlatform_MediaCodecs
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
        #endregion
    }

    [Fact]
    public void AudioDecoder_DecodesTheChunksInOrder()
    {
        var result = Run("""
            const log = [];
            const decoder = new AudioDecoder({
              output: data => {
                const plane = new Float32Array(data.numberOfFrames);
                data.copyTo(plane, { planeIndex: 1, format: 'f32-planar' });
                log.push(['output', data.timestamp, data.numberOfFrames, data.numberOfChannels, data.sampleRate, data.format, Array.from(plane).join(' ')].join(' '));
                data.close();
              },
              error: e => log.push('error ' + e.name),
            });
            decoder.ondequeue = () => log.push('dequeue ' + decoder.decodeQueueSize);
            log.push(decoder.state);
            decoder.configure({ codec: 'pcm-s16', sampleRate: 8000, numberOfChannels: 2 });
            log.push(decoder.state);
            const chunk = (type, timestamp, values) => new EncodedAudioChunk({ type, timestamp, data: new Int16Array(values) });
            try { decoder.decode(chunk('delta', 0, [0, 0])); } catch (e) { log.push(e.name); }
            decoder.decode(chunk('key', 0, [0, 16384, 1, -16384]));
            decoder.decode(chunk('delta', 250, [2, 8192]));
            log.push('queued ' + decoder.decodeQueueSize);
            decoder.flush().then(() => {
              log.push('flushed');

              // A key chunk is required after a flush
              try { decoder.decode(chunk('delta', 500, [0, 0])); } catch (e) { log.push(e.name); }
              document.title = log.join('|');
            });
            """, new TestMediaCodecBackend());

        Assert.Equal("unconfigured|configured|DataError|queued 2|dequeue 1|output 0 2 2 8000 s16 0.5 -0.5|dequeue 0|output 250 1 2 8000 s16 0.25|flushed|DataError", result);
    }

    [Fact]
    public void AudioDecoder_ResetCloseAndErrors()
    {
        var backend = new TestMediaCodecBackend();
        var result = Run("""
            const log = [];
            const chunk = new EncodedAudioChunk({ type: 'key', timestamp: 0, data: new Int16Array([1, 2]) });
            const decoder = new AudioDecoder({ output: () => log.push('output'), error: e => log.push('error ' + e.name) });
            decoder.configure({ codec: 'pcm-s16', sampleRate: 8000, numberOfChannels: 1 });
            decoder.decode(chunk);
            const flush = decoder.flush();
            decoder.reset();
            log.push(decoder.state + ' ' + decoder.decodeQueueSize);
            try { decoder.decode(chunk); } catch (e) { log.push(e.name); }
            decoder.close();
            log.push(decoder.state);
            try { decoder.configure({ codec: 'pcm-s16', sampleRate: 8000, numberOfChannels: 1 }); } catch (e) { log.push(e.name); }
            try { decoder.reset(); } catch (e) { log.push(e.name); }
            try { decoder.close(); } catch (e) { log.push(e.name); }

            // An invalid configuration throws; a configuration that the codecs do not support closes the decoder
            const unsupported = new AudioDecoder({ output: () => log.push('output'), error: e => log.push('unsupported ' + e.name + ' ' + unsupported.state) });
            try { unsupported.configure({ codec: ' ', sampleRate: 8000, numberOfChannels: 1 }); } catch (e) { log.push(e.name); }
            try { unsupported.configure({ codec: 'pcm-s16', sampleRate: 0, numberOfChannels: 1 }); } catch (e) { log.push(e.name); }
            unsupported.configure({ codec: 'mp3', sampleRate: 8000, numberOfChannels: 1 });

            // An exception of the codec closes the decoder with an EncodingError
            const failing = new AudioDecoder({ output: () => log.push('output'), error: e => log.push('failing ' + e.name + ' ' + failing.state) });
            failing.configure({ codec: 'pcm-s16', sampleRate: 8000, numberOfChannels: 1 });
            failing.decode(new EncodedAudioChunk({ type: 'key', timestamp: 0, data: new Uint8Array(1) }));

            // The pending flushes are rejected with the error that closes the decoder
            Promise.all([
              flush.catch(e => log.push('flush ' + e.name)),
              unsupported.flush().catch(e => log.push('unsupported flush ' + e.name)),
              failing.flush().catch(e => log.push('failing flush ' + e.name)),
            ]).then(() => document.title = log.join('|'));
            """, backend);

        Assert.Equal("unconfigured 0|InvalidStateError|closed|InvalidStateError|InvalidStateError|InvalidStateError|TypeError|TypeError|flush AbortError|unsupported NotSupportedError closed|unsupported flush NotSupportedError|failing EncodingError closed|failing flush EncodingError", result);

        // The decoder of the configuration that failed is disposed (the reset decoder was not created yet)
        Assert.Equal(1, backend.DisposedCodecs);
    }

    [Fact]
    public void Codecs_IsConfigSupported()
    {
        var backend = new TestMediaCodecBackend();
        var result = Run("""
            (async () => {
              const results = [];
              const audio = await AudioDecoder.isConfigSupported({ codec: 'pcm-s16', sampleRate: 8000, numberOfChannels: 2, description: new Uint8Array([1, 2]), unknown: 1 });
              results.push(audio.supported, audio.config.codec, audio.config.sampleRate, 'unknown' in audio.config, new Uint8Array(audio.config.description).join(' '));
              results.push((await AudioDecoder.isConfigSupported({ codec: 'mp3', sampleRate: 8000, numberOfChannels: 2 })).supported);
              try { await AudioDecoder.isConfigSupported({ codec: '', sampleRate: 8000, numberOfChannels: 2 }); } catch (e) { results.push(e.name); }

              const video = await VideoDecoder.isConfigSupported({ codec: 'vp8', codedWidth: 2, codedHeight: 2, rotation: 90, flip: true });
              results.push(video.supported, video.config.codedWidth, video.config.rotation, video.config.flip);
              try { await VideoDecoder.isConfigSupported({ codec: 'vp8', codedWidth: 2 }); } catch (e) { results.push(e.name); }

              const encoder = await VideoEncoder.isConfigSupported({ codec: 'vp8', width: 2, height: 2, framerate: 30, bitrate: 1000, scalabilityMode: 'L1T2' });
              results.push(encoder.supported, encoder.config.framerate, encoder.config.scalabilityMode);
              try { await VideoEncoder.isConfigSupported({ codec: 'vp8', width: 0, height: 2 }); } catch (e) { results.push(e.name); }

              const opus = await AudioEncoder.isConfigSupported({ codec: 'opus', sampleRate: 48000, numberOfChannels: 2, opus: { frameDuration: 20000 } });
              results.push(opus.supported, opus.config.opus.complexity, opus.config.opus.frameDuration);
              try { await AudioEncoder.isConfigSupported({ codec: 'opus', sampleRate: 48000, numberOfChannels: 2, opus: { frameDuration: 1000 } }); } catch (e) { results.push(e.name); }
              try { await AudioEncoder.isConfigSupported({ codec: 'opus', sampleRate: 48000, numberOfChannels: 2, bitrate: 1000 }); } catch (e) { results.push(e.name); }
              document.title = results.join();
            })();
            """, backend);

        Assert.Equal("true,pcm-s16,8000,false,1 2,false,TypeError,true,2,90,true,TypeError,true,30,L1T2,TypeError,true,9,20000,TypeError,TypeError", result);

        // The backend gets clones of the configurations: the description is a byte array
        var config = backend.Configs.OfType<AudioDecoderConfig>().First();
        Assert.Equal([1, 2], Assert.IsType<byte[]>(config.Description));
    }

    [Fact]
    public void Codecs_WithoutBackend_AreNotSupported()
    {
        var result = Run("""
            (async () => {
              const results = [];
              results.push((await AudioDecoder.isConfigSupported({ codec: 'pcm-s16', sampleRate: 8000, numberOfChannels: 1 })).supported);
              results.push((await VideoEncoder.isConfigSupported({ codec: 'vp8', width: 2, height: 2 })).supported);
              const decoder = new VideoDecoder({ output: () => {}, error: e => results.push(e.name + ' ' + decoder.state) });
              decoder.configure({ codec: 'vp8' });
              try { await decoder.flush(); } catch (e) { results.push('flush ' + e.name); }
              document.title = results.join();
            })();
            """);

        Assert.Equal("false,false,NotSupportedError closed,flush NotSupportedError", result);
    }

    [Fact]
    public void Codecs_DefaultBackend_SupportsNothing()
    {
        var backend = new EmptyCodecBackend();
        Assert.False(backend.IsConfigSupported(new AudioDecoderConfig { Codec = "opus", SampleRate = 48000, NumberOfChannels = 2 }));
        Assert.False(backend.IsConfigSupported(new VideoDecoderConfig { Codec = "vp8" }));
        Assert.False(backend.IsConfigSupported(new AudioEncoderConfig { Codec = "opus", SampleRate = 48000, NumberOfChannels = 2 }));
        Assert.False(backend.IsConfigSupported(new VideoEncoderConfig { Codec = "vp8", Width = 2, Height = 2 }));
        Assert.Null(backend.CreateAudioDecoder(new AudioDecoderConfig { Codec = "opus", SampleRate = 48000, NumberOfChannels = 2 }));
        Assert.Null(backend.CreateVideoDecoder(new VideoDecoderConfig { Codec = "vp8" }));
        Assert.Null(backend.CreateAudioEncoder(new AudioEncoderConfig { Codec = "opus", SampleRate = 48000, NumberOfChannels = 2 }));
        Assert.Null(backend.CreateVideoEncoder(new VideoEncoderConfig { Codec = "vp8", Width = 2, Height = 2 }));
    }

    [Fact]
    public void VideoDecoder_AppliesTheOrientationAndTheAspectRatioOfTheConfiguration()
    {
        var result = Run("""
            const log = [];
            const frames = [];
            const decoder = new VideoDecoder({ output: frame => frames.push(frame), error: e => log.push('error ' + e.name) });
            decoder.configure({ codec: 'vp8', rotation: 90, flip: true, displayAspectWidth: 2, displayAspectHeight: 1, colorSpace: { primaries: 'bt709', transfer: 'bt709', matrix: 'bt709', fullRange: true } });
            const encoded = (timestamp, width, height) => {
              const bytes = new Uint8Array(4 + width * height * 4).fill(200);
              bytes.set([width, 0, height, 0]);
              return new EncodedVideoChunk({ type: 'key', timestamp, duration: 10, data: bytes });
            };
            decoder.decode(encoded(0, 2, 1));
            decoder.decode(encoded(33, 2, 1));

            // The codec delays the frames by one: the last one is delivered by flush()
            decoder.flush().then(() => {
              const f = frames[0];
              log.push(frames.length, f.timestamp, f.duration, f.format, f.codedWidth, f.codedHeight, f.rotation, f.flip, f.displayWidth, f.displayHeight, f.colorSpace.fullRange, frames[1].timestamp);
              const pixels = new Uint8Array(f.allocationSize());
              f.copyTo(pixels).then(() => {
                log.push(pixels[0]);
                frames.forEach(frame => frame.close());
                document.title = log.join();
              });
            });
            """, new TestMediaCodecBackend());

        Assert.Equal("2,0,10,RGBA,2,1,90,true,1,2,true,33,200", result);
    }

    [Fact]
    public void VideoDecoder_ResetDiscardsThePendingFrames()
    {
        var backend = new TestMediaCodecBackend();
        var result = Run("""
            const log = [];
            const bytes = new Uint8Array([1, 0, 1, 0, 9, 9, 9, 9]);
            const decoder = new VideoDecoder({
              output: frame => {
                log.push('output ' + frame.timestamp);
                frame.close();

                // The second frame is pending in the codec: reset() discards it
                setTimeout(() => {
                  decoder.reset();

                  // A new configuration needs a key chunk again
                  decoder.configure({ codec: 'vp8' });
                  try { decoder.decode(new EncodedVideoChunk({ type: 'delta', timestamp: 2, data: bytes })); } catch (e) { log.push(e.name); }
                  decoder.decode(new EncodedVideoChunk({ type: 'key', timestamp: 3, data: new Uint8Array(2) }));
                  decoder.flush().catch(e => document.title = log.join('|') + '|' + decoder.state + '|' + e.name);
                });
              },
              error: e => log.push('error ' + e.name),
            });
            decoder.configure({ codec: 'vp8' });
            decoder.decode(new EncodedVideoChunk({ type: 'key', timestamp: 0, data: bytes }));
            decoder.decode(new EncodedVideoChunk({ type: 'delta', timestamp: 1, data: bytes }));
            """, backend);

        Assert.Equal("output 0|DataError|error EncodingError|closed|EncodingError", result);
        Assert.Equal(2, backend.DisposedCodecs);
    }

    [Fact]
    public void VideoEncoder_GivesTheDecoderConfigurationWithTheFirstChunk()
    {
        var result = Run("""
            const log = [];
            const encoder = new VideoEncoder({
              output: (chunk, metadata) => {
                const config = metadata.decoderConfig;
                log.push([chunk.type, chunk.timestamp, chunk.byteLength, config ? [config.codec, config.codedWidth, config.codedHeight, config.displayAspectWidth, config.displayAspectHeight, config.rotation, config.flip, config.colorSpace.matrix].join(' ') : 'none'].join(' '));
              },
              error: e => log.push('error ' + e.name),
            });
            encoder.ondequeue = () => log.push('dequeue ' + encoder.encodeQueueSize);
            encoder.configure({ codec: 'vp8', width: 2, height: 2, displayWidth: 4, displayHeight: 2 });
            const frame = new VideoFrame(new Uint8Array(16).fill(255), { format: 'RGBA', codedWidth: 2, codedHeight: 2, timestamp: 0, rotation: 180 });
            encoder.encode(frame, { keyFrame: true });
            encoder.encode(new VideoFrame(frame, { timestamp: 33 }));
            log.push('queued ' + encoder.encodeQueueSize);

            // The frames have the orientation of the first one
            try { encoder.encode(new VideoFrame(frame, { timestamp: 66, rotation: 90 })); } catch (e) { log.push(e.name); }
            frame.close();
            try { encoder.encode(frame); } catch (e) { log.push(e.name); }
            encoder.flush().then(() => document.title = log.join('|'));
            """, new TestMediaCodecBackend());

        Assert.Equal("queued 2|DataError|TypeError|dequeue 1|key 0 20 vp8 2 2 4 2 180 false rgb|dequeue 0|delta 33 20 none", result);
    }

    [Fact]
    public void AudioEncoder_EncodesAClone()
    {
        var result = Run("""
            const log = [];
            const encoder = new AudioEncoder({
              output: (chunk, metadata) => {
                const config = metadata.decoderConfig;
                const samples = new Float32Array(chunk.byteLength / 4);
                chunk.copyTo(samples);
                log.push([chunk.type, chunk.timestamp, samples.join(' '), config ? [config.codec, config.sampleRate, config.numberOfChannels].join(' ') : 'none'].join(' '));
              },
              error: e => log.push('error ' + e.name),
            });
            encoder.configure({ codec: 'opus', sampleRate: 48000, numberOfChannels: 1 });
            const data = new AudioData({ format: 's16', sampleRate: 48000, numberOfFrames: 2, numberOfChannels: 1, timestamp: 0, data: new Int16Array([16384, -16384]) });
            encoder.encode(data);
            log.push(data.format);
            data.close();
            try { encoder.encode(data); } catch (e) { log.push(e.name); }
            encoder.encode(new AudioData({ format: 'f32', sampleRate: 48000, numberOfFrames: 1, numberOfChannels: 1, timestamp: 100, data: new Float32Array([0.25]) }));
            encoder.flush().then(() => {
              encoder.close();
              log.push(encoder.state);
              encoder.flush().catch(e => { log.push(e.name); document.title = log.join('|'); });
            });
            """, new TestMediaCodecBackend());

        Assert.Equal("s16|TypeError|key 0 0.5 -0.5 opus 48000 1|key 100 0.25 none|closed|InvalidStateError", result);
    }

    // MediaCodecString

    [Theory]
    [InlineData("vp8", MediaCodec.Vp8, 0, 0, 8, false)]
    [InlineData("opus", MediaCodec.Opus, 0, 0, 8, false)]
    [InlineData("flac", MediaCodec.Flac, 0, 0, 8, false)]
    [InlineData("ulaw", MediaCodec.Ulaw, 0, 0, 8, false)]
    [InlineData("pcm-f32", MediaCodec.PcmF32, 0, 0, 8, false)]
    [InlineData("mp4a.40.2", MediaCodec.Aac, 2, 0, 8, false)]
    [InlineData("mp4a.40.02", MediaCodec.Aac, 2, 0, 8, false)]
    [InlineData("mp4a.40.29", MediaCodec.Aac, 29, 0, 8, false)]
    [InlineData("mp4a.67", MediaCodec.Aac, 0x67, 0, 8, false)]
    [InlineData("vp09.00.10.08", MediaCodec.Vp9, 0, 10, 8, false)]
    [InlineData("vp09.02.62.12.01.09.16.09.01", MediaCodec.Vp9, 2, 62, 12, false)]
    [InlineData("av01.0.04M.08", MediaCodec.Av1, 0, 4, 8, false)]
    [InlineData("av01.2.31H.12.0.110.01.01.01.0", MediaCodec.Av1, 2, 31, 12, true)]
    [InlineData("avc1.42001E", MediaCodec.Avc, 0x42, 0x1E, 8, false)]
    [InlineData("avc3.640028", MediaCodec.Avc, 0x64, 0x28, 8, false)]
    [InlineData("avc1.6E0028", MediaCodec.Avc, 0x6E, 0x28, 10, false)]
    [InlineData("hvc1.1.6.L93.B0", MediaCodec.Hevc, 1, 93, 8, false)]
    [InlineData("hev1.A2.4.H120.90", MediaCodec.Hevc, 2, 120, 10, true)]
    public void MediaCodecString_ParsesTheRegistryStrings(string value, MediaCodec codec, int profile, int level, int bitDepth, bool isHighTier)
    {
        Assert.True(MediaCodecString.TryParse(value, out var result));
        Assert.Equal(value, result.Value);
        Assert.Equal(value, result.ToString());
        Assert.Equal(codec, result.Codec);
        Assert.Equal(profile, result.Profile);
        Assert.Equal(level, result.Level);
        Assert.Equal(bitDepth, result.BitDepth);
        Assert.Equal(isHighTier, result.IsHighTier);
        Assert.Equal(codec is not (MediaCodec.Vp8 or MediaCodec.Vp9 or MediaCodec.Av1 or MediaCodec.Avc or MediaCodec.Hevc), result.IsAudio);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("vp9")]
    [InlineData(" vp8")]
    [InlineData("VP8")]
    [InlineData("mp4a")]
    [InlineData("mp4a.40")]
    [InlineData("vp09.00.10.10")]
    [InlineData("vp09.02.10.08")]
    [InlineData("vp09.99.99.08")]
    [InlineData("vp09.00.12.08")]
    [InlineData("vp09.00.10.08.1")]
    [InlineData("vp09.0.10.08")]
    [InlineData("av01.3.04M.08")]
    [InlineData("av01.0.24M.08")]
    [InlineData("av01.0.04X.08")]
    [InlineData("av01.0.04M.09")]
    [InlineData("av01.0.04M.08.0.112.01.01.01.0")]
    [InlineData("avc1.42001")]
    [InlineData("avc1.12001E")]
    [InlineData("avc1.420000")]
    [InlineData("avc1.42001E.1")]
    [InlineData("hvc1.12.6.L93")]
    [InlineData("hvc1.1.6.X93")]
    [InlineData("hvc1.1.6.L0")]
    [InlineData("hvc1.1.6")]
    [InlineData("hvc1.1..L93")]
    [InlineData("hvc1.1.6.L93.123")]
    [InlineData("flac.1")]
    public void MediaCodecString_RejectsTheInvalidStrings(string? value)
    {
        Assert.False(MediaCodecString.TryParse(value, out var result));
        Assert.Null(result);
    }

    [Fact]
    public void MediaCodecString_Example()
    {
        #region WebPlatform_MediaCodecString
        // A codec string of the WebCodecs codec registry: VP9, profile 2, level 4.1, 10-bit
        Assert.True(MediaCodecString.TryParse("vp09.02.41.10", out var codec));
        Assert.Equal(MediaCodec.Vp9, codec.Codec);
        Assert.Equal((2, 41, 10), (codec.Profile, codec.Level, codec.BitDepth));
        Assert.False(codec.IsAudio);

        // The strings are exact: an ambiguous or an unknown string is rejected
        Assert.False(MediaCodecString.TryParse("vp9", out _));
        #endregion

        Assert.False(MediaCodecString.TryParse(new string('a', 129), out _));
    }

    // WebMWriter

    [Fact]
    public void WebMWriter_WritesALiveStream()
    {
        var writer = new WebMWriter([new WebMTrack(1, IsVideo: true, "V_VP8", Width: 2, Height: 2), new WebMTrack(2, IsVideo: false, "A_OPUS", [1, 2], SampleRate: 48000, Channels: 1, CodecDelay: 6_500_000)]);

        // The header is written with the first frame, or on demand
        Assert.Equal(0, writer.PendingLength);
        writer.WriteHeader();
        writer.WriteHeader();
        var header = writer.TakeBytes();
        Assert.Equal([0x1A, 0x45, 0xDF, 0xA3], header[..4]);
        Assert.Equal(0, writer.PendingLength);
        Assert.Equal(
            "EBML EBMLVersion EBMLReadVersion EBMLMaxIDLength EBMLMaxSizeLength DocType:webm DocTypeVersion DocTypeReadVersion Segment(unknown) Info TimestampScale MuxingApp WritingApp Tracks TrackEntry TrackNumber TrackUID TrackType CodecID:V_VP8 Video PixelWidth PixelHeight TrackEntry TrackNumber TrackUID TrackType CodecID:A_OPUS CodecPrivate CodecDelay SeekPreRoll Audio SamplingFrequency Channels",
            string.Join(' ', WebMReader.ReadElements(header)));

        // A cluster starts at a video key frame, and when the relative timestamp of a block does not fit in 16 bits
        writer.WriteFrame(1, 0, isKeyFrame: true, [1]);
        writer.WriteFrame(2, 10, isKeyFrame: true, [2]);
        writer.WriteFrame(1, 20, isKeyFrame: false, [3]);
        writer.WriteFrame(1, 40, isKeyFrame: true, [4]);
        writer.WriteFrame(2, 40000, isKeyFrame: true, [5]);
        writer.StartCluster();
        writer.WriteFrame(2, 40010, isKeyFrame: true, [6]);
        var clusters = writer.TakeBytes();
        Assert.Equal(
            "Cluster(unknown) Timecode:0 SimpleBlock:1@0+key SimpleBlock:2@10+key SimpleBlock:1@20 Cluster(unknown) Timecode:40 SimpleBlock:1@0+key Cluster(unknown) Timecode:40000 SimpleBlock:2@0+key Cluster(unknown) Timecode:40010 SimpleBlock:2@0+key",
            string.Join(' ', WebMReader.ReadElements(clusters)));
    }

    [Fact]
    public void WebMWriter_ValidatesItsArguments()
    {
        Assert.Throws<ArgumentException>(() => new WebMWriter([]));
        var writer = new WebMWriter([new WebMTrack(1, IsVideo: true, "V_VP8", Width: 2, Height: 2)], docType: "matroska");
        writer.WriteHeader();
        Assert.Contains("DocType:matroska", WebMReader.ReadElements(writer.TakeBytes()));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteFrame(2, 0, isKeyFrame: true, [1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteFrame(1, -1, isKeyFrame: true, [1]));
    }

    [Theory]
    [InlineData(0, new byte[] { 0x80 })]
    [InlineData(126, new byte[] { 0xFE })]
    [InlineData(127, new byte[] { 0x40, 0x7F })]
    [InlineData(16382, new byte[] { 0x7F, 0xFE })]
    [InlineData(16383, new byte[] { 0x20, 0x3F, 0xFF })]
    public void WebMWriter_EncodeSize_AvoidsTheReservedValues(long size, byte[] expected)
        => Assert.Equal(expected, WebMWriter.EncodeSize(size));

    private sealed class EmptyCodecBackend : MediaCodecBackend
    {
    }
}


/// <summary>A small EBML reader: the names of the elements of a WebM stream, in document order.</summary>
internal static class WebMReader
{
    private static readonly Dictionary<uint, string> Names = new()
    {
        [0x1A45DFA3] = "EBML", [0x4286] = "EBMLVersion", [0x42F7] = "EBMLReadVersion", [0x42F2] = "EBMLMaxIDLength", [0x42F3] = "EBMLMaxSizeLength",
        [0x4282] = "DocType", [0x4287] = "DocTypeVersion", [0x4285] = "DocTypeReadVersion", [0x18538067] = "Segment", [0x1549A966] = "Info",
        [0x2AD7B1] = "TimestampScale", [0x4D80] = "MuxingApp", [0x5741] = "WritingApp", [0x1654AE6B] = "Tracks", [0xAE] = "TrackEntry",
        [0xD7] = "TrackNumber", [0x73C5] = "TrackUID", [0x83] = "TrackType", [0x86] = "CodecID", [0x63A2] = "CodecPrivate", [0x56AA] = "CodecDelay",
        [0x56BB] = "SeekPreRoll", [0xE0] = "Video", [0xB0] = "PixelWidth", [0xBA] = "PixelHeight", [0xE1] = "Audio", [0xB5] = "SamplingFrequency",
        [0x9F] = "Channels", [0x1F43B675] = "Cluster", [0xE7] = "Timecode", [0xA3] = "SimpleBlock",
    };

    private static readonly HashSet<uint> Masters = [0x1A45DFA3, 0x18538067, 0x1654AE6B, 0xAE, 0xE0, 0xE1, 0x1F43B675, 0x1549A966];

    /// <summary>Returns the track number and the frame of each <c>SimpleBlock</c>, in document order.</summary>
    public static List<(int Track, byte[] Frame)> ReadBlocks(ReadOnlySpan<byte> data)
    {
        var result = new List<(int Track, byte[] Frame)>();
        var position = 0;
        while (position < data.Length)
        {
            var id = ReadVariableInteger(data, ref position, keepMarker: true);
            var size = ReadVariableInteger(data, ref position, keepMarker: false);
            if (Masters.Contains((uint)id))
                continue;

            var payload = data.Slice(position, (int)size);
            position += (int)size;
            if (id == 0xA3)
                result.Add((payload[0] & 0x7F, payload[4..].ToArray()));
        }

        return result;
    }

    public static List<string> ReadElements(ReadOnlySpan<byte> data)
    {
        var result = new List<string>();
        var position = 0;
        while (position < data.Length)
        {
            var id = ReadVariableInteger(data, ref position, keepMarker: true);
            var size = ReadVariableInteger(data, ref position, keepMarker: false);
            var name = Names[(uint)id];
            if (Masters.Contains((uint)id))
            {
                // The children follow: an element of unknown size ends with the stream
                result.Add(size == ulong.MaxValue ? name + "(unknown)" : name);
                continue;
            }

            var payload = data.Slice(position, (int)size);
            position += (int)size;
            result.Add(name switch
            {
                "DocType" or "CodecID" => name + ":" + System.Text.Encoding.ASCII.GetString(payload),
                "Timecode" => name + ":" + ReadUnsigned(payload).ToString(System.Globalization.CultureInfo.InvariantCulture),
                "SimpleBlock" => FormattableString.Invariant($"{name}:{payload[0] & 0x7F}@{BinaryPrimitives.ReadInt16BigEndian(payload[1..])}{((payload[3] & 0x80) != 0 ? "+key" : "")}"),
                _ => name,
            });
        }

        return result;
    }

    private static ulong ReadVariableInteger(ReadOnlySpan<byte> data, ref int position, bool keepMarker)
    {
        var first = data[position];
        var length = System.Numerics.BitOperations.LeadingZeroCount((uint)first) - 23;
        var value = keepMarker ? first : (ulong)(first & (0xFF >> length));
        var allOnes = value == (ulong)(0xFF >> length);
        for (var i = 1; i < length; i++)
        {
            var next = data[position + i];
            allOnes &= next == 0xFF;
            value = (value << 8) | next;
        }

        position += length;
        return !keepMarker && allOnes ? ulong.MaxValue : value;
    }

    private static ulong ReadUnsigned(ReadOnlySpan<byte> data)
    {
        var value = 0UL;
        foreach (var b in data)
        {
            value = (value << 8) | b;
        }

        return value;
    }
}

#region WebPlatform_MediaCodecBackend
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
#endregion

/// <summary>
/// The codecs of the tests: the PCM decoder, and stand-ins for VP8 and Opus whose bitstreams are not compressed (the tests
/// check the processing model of the codecs and the container, not the compression). A <c>vp8</c> chunk is the width and
/// the height of the frame (16-bit little-endian) and its RGBA pixels; the decoder delays the frames by one, as a decoder
/// that reorders them. An <c>opus</c> chunk is the samples as 32-bit floats, interleaved.
/// </summary>
internal sealed class TestMediaCodecBackend : PcmCodecBackend
{
    /// <summary>Gets the configurations given to <c>IsConfigSupported</c>.</summary>
    public List<object> Configs { get; } = [];

    /// <summary>Gets the number of codec implementations that were disposed.</summary>
    public int DisposedCodecs { get; private set; }

    public override bool IsConfigSupported(AudioDecoderConfig config)
    {
        Configs.Add(config);
        return base.IsConfigSupported(config);
    }

    public override MediaAudioDecoder? CreateAudioDecoder(AudioDecoderConfig config) => new DisposeCounter(this, base.CreateAudioDecoder(config)!);

    public override bool IsConfigSupported(VideoDecoderConfig config)
    {
        Configs.Add(config);
        return config.Codec == "vp8";
    }

    public override MediaVideoDecoder? CreateVideoDecoder(VideoDecoderConfig config) => new RawVideoDecoder(this);

    public override bool IsConfigSupported(AudioEncoderConfig config)
    {
        Configs.Add(config);
        return config.Codec == "opus" && config.NumberOfChannels is 1 or 2;
    }

    public override MediaAudioEncoder? CreateAudioEncoder(AudioEncoderConfig config) => new RawAudioEncoder(this);

    public override bool IsConfigSupported(VideoEncoderConfig config)
    {
        Configs.Add(config);
        return config.Codec == "vp8" && config.Width <= 4096 && config.Height <= 4096;
    }

    public override MediaVideoEncoder? CreateVideoEncoder(VideoEncoderConfig config) => new RawVideoEncoder(this);

    /// <summary>Counts the disposal of a decoder of the base backend.</summary>
    private sealed class DisposeCounter(TestMediaCodecBackend backend, MediaAudioDecoder inner) : MediaAudioDecoder
    {
        public override IReadOnlyList<AudioData> Decode(EncodedAudioChunk chunk) => inner.Decode(chunk);

        protected override void Dispose(bool disposing)
        {
            backend.DisposedCodecs++;
            inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class RawVideoDecoder(TestMediaCodecBackend backend) : MediaVideoDecoder
    {
        private VideoFrame? _pending;

        public override IReadOnlyList<VideoFrame> Decode(EncodedVideoChunk chunk)
        {
            var data = chunk.Data.Span;
            if (data.Length < 4)
                throw new InvalidDataException("The chunk has no header");

            var width = BinaryPrimitives.ReadUInt16LittleEndian(data);
            var height = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
            var frame = new VideoFrame(chunk.Data[4..], new VideoFrameBufferInit { Format = VideoPixelFormat.RGBA, CodedWidth = width, CodedHeight = height, Timestamp = chunk.Timestamp, Duration = chunk.Duration });
            var previous = _pending;
            _pending = frame;
            return previous is null ? [] : [previous];
        }

        public override IReadOnlyList<VideoFrame> Flush()
        {
            var pending = _pending;
            _pending = null;
            return pending is null ? [] : [pending];
        }

        public override void Reset()
        {
            _pending?.Close();
            _pending = null;
        }

        protected override void Dispose(bool disposing)
        {
            backend.DisposedCodecs++;
            Reset();
            base.Dispose(disposing);
        }
    }

    private sealed class RawVideoEncoder(TestMediaCodecBackend backend) : MediaVideoEncoder
    {
        private bool _keyFrameEncoded;

        public override IReadOnlyList<MediaEncodedVideo> Encode(VideoFrame frame, VideoEncoderEncodeOptions options)
        {
            var copyOptions = new VideoFrameCopyToOptions { Format = VideoPixelFormat.RGBA };
            var bytes = new byte[4 + frame.AllocationSize(copyOptions)];
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)frame.VisibleRect!.Width);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), (ushort)frame.VisibleRect.Height);
            frame.CopyPixelsTo(bytes.AsSpan(4), copyOptions);
            var type = options.KeyFrame || !_keyFrameEncoded ? EncodedVideoChunkType.Key : EncodedVideoChunkType.Delta;
            _keyFrameEncoded = true;
            return [new MediaEncodedVideo(new EncodedVideoChunk(new EncodedVideoChunkInit { Type = type, Timestamp = frame.Timestamp, Duration = frame.Duration, Data = bytes }))];
        }

        protected override void Dispose(bool disposing)
        {
            backend.DisposedCodecs++;
            base.Dispose(disposing);
        }
    }

    private sealed class RawAudioEncoder(TestMediaCodecBackend backend) : MediaAudioEncoder
    {
        public override IReadOnlyList<MediaEncodedAudio> Encode(AudioData data)
        {
            var options = new AudioDataCopyToOptions { PlaneIndex = 0, Format = AudioSampleFormat.F32 };
            var bytes = new byte[data.AllocationSize(options)];
            data.CopyTo(bytes, options);
            return [new MediaEncodedAudio(new EncodedAudioChunk(new EncodedAudioChunkInit { Type = EncodedAudioChunkType.Key, Timestamp = data.Timestamp, Duration = data.Duration, Data = bytes }))];
        }

        protected override void Dispose(bool disposing)
        {
            backend.DisposedCodecs++;
            base.Dispose(disposing);
        }
    }
}
