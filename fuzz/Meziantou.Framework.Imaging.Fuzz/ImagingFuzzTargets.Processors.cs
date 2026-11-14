using System.Runtime.InteropServices;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Imaging.Fuzz;

public static partial class ImagingFuzzTargets
{
    /// <summary>
    /// <see cref="ImageProcessingExtensions.ApplyProcessor{TPixel, TProcessor}(Image, TProcessor, CancellationToken)"/> with a
    /// processor that crops and mirrors every frame, in the pixel type of the image or in a working type, and that may fail
    /// on a frame or return an empty size. The input is an image (<see cref="CreateImage"/>) preceded by a byte of working
    /// type, two bytes of output size, a byte of failing frame and a byte of hotspot. A processor that fails, a hotspot cut
    /// away, a pixel the format cannot store or an unsupported working type leaves the image exactly as it was; a processor
    /// that succeeds gives frames of its size, the cropped and mirrored pixels when it works in the pixel type of the image.
    /// </summary>
    private static void ProcessorTarget(ReadOnlySpan<byte> data)
    {
        var working = FuzzInput.ReadByte(ref data) % 3;
        var outputWidth = FuzzInput.ReadByte(ref data) % 20;
        var outputHeight = FuzzInput.ReadByte(ref data) % 20;
        var throwAt = FuzzInput.ReadByte(ref data) % 5;
        var hotspot = FuzzInput.ReadByte(ref data);
        using var image = CreateImage(ref data, maxSide: 12);
        if (hotspot != 0)
        {
            image.Frames[0].Metadata.Hotspot = new Point(hotspot % 16 % image.Width, hotspot / 16 % image.Height);
        }

        var before = Snapshot(image);
        var size = new Size(outputWidth, outputHeight);
        Exception? failure = null;
        try
        {
            switch (working)
            {
                case 0:
                    ApplyInImageFormat(image, size, throwAt);
                    break;
                case 1:
                    image.ApplyProcessor<Rgba64, MirrorProcessor<Rgba64>>(new MirrorProcessor<Rgba64>(size, throwAt));
                    break;
                default:
                    image.ApplyProcessor<RgbaFloat, MirrorProcessor<RgbaFloat>>(new MirrorProcessor<RgbaFloat>(size, throwAt));
                    break;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or UnsupportedImageFeatureException)
        {
            failure = ex;
        }

        if (failure is not null)
        {
            FuzzAssert.Equal(before, Snapshot(image), $"A failed processor changed the image: {failure.Message}");
            return;
        }

        FuzzAssert.False(size.IsEmpty, "A processor that returns an empty size must fail");
        FuzzAssert.True(throwAt >= image.Frames.Count + (image.PosterFrame is null ? 0 : 1), "A processor that throws must fail");
        FuzzAssert.Equal(size, image.Size, "The image does not have the size of the processor");
        if (image.Frames[0].Metadata.Hotspot is { } moved)
        {
            FuzzAssert.True(moved.X < size.Width && moved.Y < size.Height, $"The hotspot {moved} is outside the {size} canvas");
        }
    }

    private static void ApplyInImageFormat(Image image, Size size, int throwAt)
    {
        switch (image)
        {
            case Image<Rgba32> typed:
                ApplyAndCheck(typed, size, throwAt);
                break;
            case Image<Gray16> typed:
                ApplyAndCheck(typed, size, throwAt);
                break;
            case Image<Bgr565> typed:
                ApplyAndCheck(typed, size, throwAt);
                break;
            default:
                image.ApplyProcessor<Rgba64, MirrorProcessor<Rgba64>>(new MirrorProcessor<Rgba64>(size, throwAt));
                break;
        }
    }

    /// <summary>In the pixel type of the image, the result is the mirrored crop, bit for bit, and the pixels beyond the source are cleared.</summary>
    private static void ApplyAndCheck<TPixel>(Image<TPixel> image, Size size, int throwAt)
        where TPixel : unmanaged, IEquatable<TPixel>
    {
        using var original = image.Clone();
        var sourceSize = image.Size;
        image.ApplyProcessor<TPixel, MirrorProcessor<TPixel>>(new MirrorProcessor<TPixel>(size, throwAt));
        for (var i = 0; i < image.Frames.Count; i++)
        {
            for (var y = 0; y < size.Height; y++)
            {
                for (var x = 0; x < size.Width; x++)
                {
                    var sourceX = size.Width - 1 - x;
                    var expected = sourceX < sourceSize.Width && y < sourceSize.Height ? original.Frames[i][sourceX, y] : default;
                    FuzzAssert.Equal(expected, image.Frames[i][x, y], $"Pixel ({x}, {y}) of frame {i}");
                }
            }
        }
    }

    private static IEnumerable<FuzzSeed> GetProcessorSeeds()
    {
        // The cases of the unit tests: a transposed gray image, a working format over an opaque format, a failing frame,
        // an empty size, a cut hotspot, and a format without alpha given a transparent pixel
        yield return new FuzzSeed("transpose-gray", (byte[])[0, 2, 3, 9, 0, 2, 2, 1, 0, 1, 2, 3, 4, 5, 6]);
        yield return new FuzzSeed("working-rgba64", (byte[])[1, 2, 3, 9, 0x21, 1, 2, 1, 0x81, 10, 20, 30]);
        yield return new FuzzSeed("throwing-frame", (byte[])[0, 3, 3, 1, 0, 0, 3, 2, 1, 1, 2, 3, 4]);
        yield return new FuzzSeed("empty-size", (byte[])[2, 0, 4, 9, 0, 0, 3, 2, 0, 1, 2, 3, 4]);
        yield return new FuzzSeed("cut-hotspot", (byte[])[0, 2, 3, 9, 0x03, 0, 3, 2, 1, 1, 2, 3, 4]);
        yield return new FuzzSeed("float-nan", (byte[])[2, 5, 5, 9, 0, 6, 2, 2, 0x82, 0xFF, 0xFF, 0xFF, 0x7F, 0, 0, 0x80, 0x3F]);
        yield return new FuzzSeed("packed", (byte[])[0, 9, 9, 9, 0x44, 8, 4, 4, 0, 0x1F, 0xF8, 0xE0, 0x07]);
    }

    /// <summary>Mirrors the top-left corner of the frame horizontally into a canvas of a given size; fails on one frame.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly struct MirrorProcessor<TPixel>(Size size, int throwAt) : IImageProcessor<TPixel>
        where TPixel : unmanaged
    {
        public Size GetOutputSize(Size sourceSize) => size;

        public void Process(int frameIndex, PixelRowSource<TPixel> source, PixelAccessor<TPixel> destination, CancellationToken cancellationToken)
        {
            if (frameIndex == throwAt)
                throw new InvalidOperationException("The processor failed.");

            for (var y = 0; y < destination.Height && y < source.Height; y++)
            {
                var row = destination.GetRowSpan(y);
                var sourceRow = source.GetRow(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var sourceX = row.Length - 1 - x;
                    if (sourceX < sourceRow.Length)
                    {
                        row[x] = sourceRow[sourceX];
                    }
                }
            }
        }
    }
}
