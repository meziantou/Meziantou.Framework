using System.Runtime.CompilerServices;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Memory contracts of the library: static row callbacks allocate nothing per row in steady state,
/// and sequential processing keeps the live pixel storage independent of the total frame count when results are disposed.
/// These are deterministic (allocation counts and accounted bytes), unlike timings, which are measured by the benchmarks.
/// </summary>
public sealed class PerformanceContractTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(1)]
    [InlineData(4096)]
    public void StaticRowCallbacksAllocateNothingWhateverTheRowCount(int height)
    {
        using var image = new Image<Rgba32>(16, height);
        using var decoded = Image.Load<Rgba32>(EncodePng(16, height));
        var box = new StrongBox<long>();
        foreach (var frame in new[] { image.Frames[0], decoded.Frames[0] })
        {
            Assert.Equal(0, MeasureSteadyStateAllocations(() => Run(frame, box)));
        }

        Assert.True(box.Value > 0);

        static void Run(ImageFrame<Rgba32> frame, StrongBox<long> box)
        {
            frame.ProcessPixelRows(static pixels =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    foreach (ref var pixel in pixels.GetRowSpan(y))
                    {
                        pixel = new Rgba32((byte)(255 - pixel.R), pixel.G, pixel.B, pixel.A);
                    }
                }
            });

            frame.ProcessPixelRows(box, static (pixels, state) =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    state.Value += pixels.GetRowSpan(y)[0].R + 1;
                }
            });

            frame.ProcessPixelBytes(box, static (pixels, state) =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    state.Value += pixels.GetRowSpan(y)[1];
                }
            });
        }
    }

    public static TheoryData<string> AnimationFormats() => ["apng", "gif"];

    [Theory]
    [MemberData(nameof(AnimationFormats))]
    public void SequentialTransformsKeepLivePixelStorageIndependentOfTheFrameCount(string format)
    {
        // Read a frame, resize it, write it, dispose it: the accounted peak (reader, returned images, resize scratch and writer)
        // must be the same for 4 and 40 frames
        var few = MeasureSequentialPeak(CreateAnimation(format, frames: 4), format, reuseDestination: false);
        var many = MeasureSequentialPeak(CreateAnimation(format, frames: 40), format, reuseDestination: false);
        Assert.Equal(few, many);

        // Same with one reused destination image (ReadFrameInto, processed in place)
        Assert.Equal(MeasureSequentialPeak(CreateAnimation(format, frames: 4), format, reuseDestination: true), MeasureSequentialPeak(CreateAnimation(format, frames: 40), format, reuseDestination: true));

        // The measure is meaningful: eager decoding retains every frame, so its peak grows with the frame count
        Assert.True(MeasureEagerPeak(CreateAnimation(format, frames: 40)) > 5 * MeasureEagerPeak(CreateAnimation(format, frames: 4)));
    }

    private static long MeasureSteadyStateAllocations(Action action)
    {
        for (var i = 0; i < 3; i++)
        {
            action();
        }

        // Tiered compilation running concurrently can occasionally attribute a few bytes to this thread: best of several rounds
        var allocated = long.MaxValue;
        for (var round = 0; round < 5 && allocated != 0; round++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 20; i++)
            {
                action();
            }

            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        return allocated;
    }

    private static long MeasureSequentialPeak(byte[] input, string format, bool reuseDestination)
    {
        using var tracking = new AllocationTracking();
        using (var source = new MemoryStream(input, writable: false))
        using (var output = new MemoryStream())
        using (var reader = Image.OpenReader<Rgba32>(source))
        {
            // The images returned by the reader share its scope; the reused destination has its own (one scope per role, so
            // the sum of the scope peaks bounds the simultaneous live bytes)
            var info = reader.Info;
            var size = reuseDestination ? info.Size : new Size(info.Width / 2, info.Height / 2);
            using var writer = Image.CreateWriter<Rgba32>(output, new ImageWriterOptions(size)
            {
                Encoder = format == "gif" ? new GifEncoder() : new PngEncoder { AnimationMode = PngAnimationMode.Animated },
                Animation = info.Animation,
                ExpectedFrameCount = info.FrameCount,
            });

            if (reuseDestination)
            {
                using var destination = new Image<Rgba32>(info.Width, info.Height);
                while (reader.ReadFrameInto(destination))
                {
                    destination.Grayscale(Ct);
                    writer.WriteFrame(destination.Frames[0]);
                }
            }
            else
            {
                var resize = new ResizeOptions(size) { Mode = ResizeMode.Stretch };
                while (reader.ReadFrame() is { } frame)
                {
                    using (frame)
                    {
                        frame.Resize(resize, Ct);
                        writer.WriteFrame(frame.Frames[0]);
                    }
                }
            }

            writer.Complete();
        }

        // Everything was disposed: every scope is back to zero
        Assert.Equal(0, tracking.LiveBytes);
        return tracking.Scopes.Sum(scope => scope.GetDiagnostics().PeakLiveBytes);
    }

    private static long MeasureEagerPeak(byte[] input)
    {
        using var tracking = new AllocationTracking();
        using (var image = Image.Load<Rgba32>(input))
        {
            Assert.True(image.Frames.Count > 1);
        }

        return tracking.Scopes.Sum(scope => scope.GetDiagnostics().PeakLiveBytes);
    }

    private static byte[] CreateAnimation(string format, int frames)
    {
        using var image = new Image<Rgba32>(64, 48);
        for (var i = 0; i < frames; i++)
        {
            var frame = i == 0 ? image.Frames[0] : image.AppendFrame();
            frame.Metadata.Duration = FrameDuration.FromMilliseconds(40);
            frame.ProcessPixelRows(i, static (pixels, index) =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    var row = pixels.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        row[x] = new Rgba32((byte)((x * 4) + index), (byte)(y * 5), (byte)(index * 9));
                    }
                }
            });
        }

        using var stream = new MemoryStream();
        image.Save(stream, format == "gif" ? new GifEncoder() : new PngEncoder { AnimationMode = PngAnimationMode.Animated });
        return stream.ToArray();
    }

    private static byte[] EncodePng(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(10, 20, 30));
        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        return stream.ToArray();
    }
}
