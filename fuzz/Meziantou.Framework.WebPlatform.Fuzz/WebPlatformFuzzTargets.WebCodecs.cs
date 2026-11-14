using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;
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
            yield return new FuzzSeed("videoframe-copyto-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), scenarios[i]);
        }
    }
}
