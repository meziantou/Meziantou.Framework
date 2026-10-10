using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>Scoped pixel access: indexer, row callbacks, strided copies and raw imports.</summary>
public sealed class PixelAccessTests
{
    private static readonly PixelStorageLayoutOptions SegmentedPadded = new() { RowAlignment = 16, TargetSlabBytes = 64 };

    [Fact]
    public void IndexerReadsAndWritesSinglePixels()
    {
        using var image = new Image<Rgba64>(3, 2);
        var frame = image.Frames[0];
        frame[2, 1] = new Rgba64(1, 2, 3, 4);
        Assert.Equal(new Rgba64(1, 2, 3, 4), frame[2, 1]);
        Assert.Equal(default, frame[1, 1]);

        Assert.Equal("x", Assert.Throws<ArgumentOutOfRangeException>(() => frame[3, 0]).ParamName);
        Assert.Equal("x", Assert.Throws<ArgumentOutOfRangeException>(() => frame[-1, 0]).ParamName);
        Assert.Equal("y", Assert.Throws<ArgumentOutOfRangeException>(() => frame[0, 2] = default).ParamName);
    }

    [Fact]
    public void RowCallbacksExposeExactlyTheVisiblePixels()
    {
        using var image = new Image<Rgb24>(5, 3, configuration: null);
        image.Frames[0].ProcessPixelRows(static pixels =>
        {
            Assert.Equal(5, pixels.Width);
            Assert.Equal(3, pixels.Height);
            for (var y = 0; y < pixels.Height; y++)
            {
                var row = pixels.GetRowSpan(y);
                var length = row.Length;
                Assert.Equal(5, length);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgb24((byte)x, (byte)y, 7);
                }
            }

            var rejected = false;
            try
            {
                _ = pixels.GetRowSpan(3);
            }
            catch (ArgumentOutOfRangeException)
            {
                rejected = true;
            }

            Assert.True(rejected);
        });

        image.Frames[0].ProcessPixelBytes(static pixels =>
        {
            Assert.Equal(PixelFormat.Rgb24, pixels.PixelFormat);
            Assert.Equal(15, pixels.RowLength);
            Assert.Equal(5, pixels.Width);
            Assert.Equal(3, pixels.Height);
            Assert.Equal(new byte[] { 4, 2, 7 }, pixels.GetRowSpan(2)[12..].ToArray());
        });

        Assert.Equal(new Rgb24(4, 2, 7), image.Frames[0][4, 2]);
    }

    [Fact]
    public void StateOverloadsPassStateIncludingRefStructs()
    {
        using var image = new Image<Gray8>(4, 2);
        Span<int> counter = [0];
        image.Frames[0].ProcessPixelRows(new Counter(counter), static (pixels, state) =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                pixels.GetRowSpan(y).Fill(new Gray8(9));
                state.Increment();
            }
        });

        image.Frames[0].ProcessPixelBytes(new Counter(counter), static (pixels, state) =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                Assert.Equal(new byte[] { 9, 9, 9, 9 }, pixels.GetRowSpan(y).ToArray());
                state.Increment();
            }
        });

        Assert.Equal(4, counter[0]);
    }

    [Fact]
    public void DefaultAccessorsHaveNoRows()
    {
        Assert.Equal(0, default(PixelAccessor<Rgba32>).Width);
        Assert.Equal(0, default(PixelAccessor<Rgba32>).Height);
        Assert.Throws<InvalidOperationException>(() => default(PixelAccessor<Rgba32>).GetRowSpan(0));
        Assert.Equal(0, default(PixelBytesAccessor).RowLength);
        Assert.Throws<InvalidOperationException>(() => default(PixelBytesAccessor).GetRowSpan(0));
    }

    [Fact]
    public void LeasesAreReleasedWhenCallbacksThrow()
    {
        using var image = new Image<Rgba32>(2, 2);
        var frame = image.Frames[0];
        Assert.Throws<FormatException>(() => frame.ProcessPixelRows(static _ => throw new FormatException()));
        Assert.Throws<FormatException>(() => frame.ProcessPixelBytes(static _ => throw new FormatException()));
        Assert.Throws<FormatException>(() => frame.ProcessPixelRows(0, static (_, _) => throw new FormatException()));
        Assert.Equal(0, image.Owner.ActiveLeaseCount);
        Assert.False(frame.Storage.IsLeased);

        // The image is still fully usable
        frame[1, 1] = new Rgba32(1, 2, 3);
        image.AppendFrame();
        image.Dispose();
    }

    [Fact]
    public void CallbacksRejectReentrantStorageChangesAndConflictingAccess()
    {
        using var image = new Image<Rgba32>(2, 2);
        image.AppendFrame();
        var frame = image.Frames[0];
        var other = image.Frames[1];
        var checks = 0;

        frame.ProcessPixelRows(pixels =>
        {
            Assert.Throws<InvalidOperationException>(() => image.Dispose());
            Assert.Throws<InvalidOperationException>(() => image.AppendFrame());
            Assert.Throws<InvalidOperationException>(() => image.AppendFrame(other));
            Assert.Throws<InvalidOperationException>(() => image.InsertFrame(0, other));
            Assert.Throws<InvalidOperationException>(() => image.RemoveFrame(1));
            Assert.Throws<InvalidOperationException>(() => image.MoveFrame(0, 1));
            Assert.Throws<InvalidOperationException>(() => image.SetPosterFrame(other));
            Assert.Throws<InvalidOperationException>(() => image.RemovePosterFrame());
            Assert.Throws<InvalidOperationException>(() => frame.ProcessPixelRows(static _ => { }));
            Assert.Throws<InvalidOperationException>(() => frame.ProcessPixelBytes(static _ => { }));
            Assert.Throws<InvalidOperationException>(() => frame[0, 0]);
            Assert.Throws<InvalidOperationException>(() => frame.CopyPixelDataTo(new Rgba32[4]));
            Assert.Throws<InvalidOperationException>(() => frame.CopyPixelBytesTo(new byte[16]));
            Assert.Throws<InvalidOperationException>(() => image.Clone());

            // Another frame of the same image can be accessed, and the leased rows stay writable
            other[1, 1] = new Rgba32(4, 5, 6);
            using var copy = image.CloneFrame(1);
            pixels.GetRowSpan(0)[0] = new Rgba32(7, 8, 9);
            checks++;
        });

        Assert.Equal(1, checks);
        Assert.Equal(new Rgba32(7, 8, 9), frame[0, 0]);
        Assert.Equal(new Rgba32(4, 5, 6), other[1, 1]);
        Assert.Equal(2, image.Frames.Count);
    }

    [Fact]
    public void StaticRowCallbacksDoNotAllocateAfterWarmup()
    {
        using var image = new Image<Rgba32>(64, 64);
        var frame = image.Frames[0];
        var destination = new Rgba32[64 * 64];
        var bytes = new byte[64 * 64 * 4];
        var box = new StrongBox<long>();
        Span<long> total = [0];

        for (var i = 0; i < 3; i++)
        {
            Run(frame, box, new Total(total), destination, bytes);
        }

        // Tiered JIT compilation / OSR running concurrently can occasionally attribute a few bytes
        // to this thread, so take the best of several measurement rounds.
        var allocated = long.MaxValue;
        for (var round = 0; round < 5 && allocated != 0; round++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++)
            {
                Run(frame, box, new Total(total), destination, bytes);
            }

            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Assert.Equal(0, allocated);
        Assert.True(box.Value > 0);
        Assert.True(total[0] > 0);

        static void Run(ImageFrame<Rgba32> frame, StrongBox<long> box, Total total, Rgba32[] destination, byte[] bytes)
        {
            frame.ProcessPixelRows(static pixels =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    pixels.GetRowSpan(y)[0].R++;
                }
            });

            frame.ProcessPixelRows(box, static (pixels, state) =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    state.Value += pixels.GetRowSpan(y)[0].R;
                }
            });

            frame.ProcessPixelRows(total, static (pixels, state) =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    state.Add(pixels.GetRowSpan(y).Length);
                }
            });

            frame.ProcessPixelBytes(total, static (pixels, state) =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    state.Add(pixels.GetRowSpan(y).Length);
                }
            });

            frame[3, 3] = frame[2, 2];
            frame.CopyPixelDataTo(destination);
            frame.CopyPixelBytesTo(bytes);
        }
    }

    public static TheoryData<string, bool> PixelTypesAndLayouts()
    {
        var data = new TheoryData<string, bool>();
        foreach (var format in PixelFormats.All.Select(format => format.ToString()))
        {
            data.Add(format, false);
            data.Add(format, true);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PixelTypesAndLayouts))]
    public void CopyInAndOutPreservesVisibleBytes(string format, bool segmented)
    {
        switch (Enum.Parse<PixelFormat>(format))
        {
            case PixelFormat.Rgba32: CopyRoundTrip<Rgba32>(segmented); break;
            case PixelFormat.Bgra32: CopyRoundTrip<Bgra32>(segmented); break;
            case PixelFormat.Rgb24: CopyRoundTrip<Rgb24>(segmented); break;
            case PixelFormat.Rgba64: CopyRoundTrip<Rgba64>(segmented); break;
            case PixelFormat.Gray8: CopyRoundTrip<Gray8>(segmented); break;
            case PixelFormat.Gray16: CopyRoundTrip<Gray16>(segmented); break;
            default: throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    [Fact]
    public void ImportValidatesStridesAndLengths()
    {
        // Zero stride means tightly packed; the last row only needs the visible length
        using (var image = Image.ImportPixelData<Gray8>(new Gray8[6], 3, 2))
        {
            Assert.Equal(new Size(3, 2), image.Size);
        }

        using (var image = Image.ImportPixelData<Gray8>(new Gray8[8], 3, 2, strideInPixels: 5))
        {
            Assert.Equal(new Size(3, 2), image.Size);
        }

        Assert.Equal("strideInPixels", Assert.Throws<ArgumentOutOfRangeException>(() => Image.ImportPixelData<Gray8>(new Gray8[8], 3, 2, strideInPixels: 2)).ParamName);
        Assert.Equal("strideInPixels", Assert.Throws<ArgumentOutOfRangeException>(() => Image.ImportPixelData<Gray8>(new Gray8[8], 3, 2, strideInPixels: -1)).ParamName);
        Assert.Equal("source", Assert.Throws<ArgumentException>(() => Image.ImportPixelData<Gray8>(new Gray8[7], 3, 2, strideInPixels: 5)).ParamName);
        Assert.Equal("source", Assert.Throws<ArgumentException>(() => Image.ImportPixelData<Gray8>(new Gray8[5], 3, 2)).ParamName);

        // Byte strides do not need to be a multiple of the pixel size
        using (var image = Image.ImportPixelBytes<Rgb24>(new byte[(10 * 2) + 9], 3, 3, strideInBytes: 10))
        {
            Assert.Equal(new Size(3, 3), image.Size);
        }

        Assert.Equal("strideInBytes", Assert.Throws<ArgumentOutOfRangeException>(() => Image.ImportPixelBytes<Rgb24>(new byte[100], 3, 3, strideInBytes: 8)).ParamName);
        Assert.Contains("29", Assert.Throws<ArgumentException>(() => Image.ImportPixelBytes<Rgb24>(new byte[28], 3, 3, strideInBytes: 10)).Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => Image.ImportPixelBytes<Rgba64>(new byte[15], 2, 1));
    }

    [Fact]
    public void ImportCopiesCallerMemory()
    {
        var source = new Rgba32[] { new(1, 2, 3), new(4, 5, 6) };
        using var image = Image.ImportPixelData<Rgba32>(source, 2, 1);
        source[0] = default;
        Assert.Equal(new Rgba32(1, 2, 3), image.Frames[0][0, 0]);

        var bytes = new byte[] { 1, 2, 3, 4 };
        using var gray = Image.ImportPixelBytes<Gray16>(bytes, 2, 1);
        bytes.AsSpan().Clear();
        Assert.Equal(new Gray16(BitConverter.ToUInt16([1, 2])), gray.Frames[0][0, 0]);
        Assert.Null(gray.Animation);
        Assert.Single(gray.Frames);
    }

    [Fact]
    public void CopyOutValidatesStridesAndLeavesPaddingUntouched()
    {
        using var image = Image.ImportPixelBytes<Rgb24>([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12], 2, 2);
        var frame = image.Frames[0];
        Assert.Equal("strideInBytes", Assert.Throws<ArgumentOutOfRangeException>(() => frame.CopyPixelBytesTo(new byte[20], 5)).ParamName);
        Assert.Equal("strideInBytes", Assert.Throws<ArgumentOutOfRangeException>(() => frame.CopyPixelBytesTo(new byte[20], -1)).ParamName);
        Assert.Equal("destination", Assert.Throws<ArgumentException>(() => frame.CopyPixelBytesTo(new byte[11])).ParamName);
        Assert.Equal("destination", Assert.Throws<ArgumentException>(() => frame.CopyPixelBytesTo(new byte[13], 8)).ParamName);
        Assert.Equal("strideInPixels", Assert.Throws<ArgumentOutOfRangeException>(() => frame.CopyPixelDataTo(new Rgb24[8], 1)).ParamName);
        Assert.Equal("destination", Assert.Throws<ArgumentException>(() => frame.CopyPixelDataTo(new Rgb24[4], 3)).ParamName);

        var buffer = Enumerable.Repeat((byte)0xEE, 16).ToArray();
        frame.CopyPixelBytesTo(buffer.AsSpan(0, 14), 8); // the last row only needs the visible bytes
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 0xEE, 0xEE, 7, 8, 9, 10, 11, 12, 0xEE, 0xEE }, buffer);

        var pixels = Enumerable.Repeat(new Rgb24(0xEE, 0xEE, 0xEE), 6).ToArray();
        frame.CopyPixelDataTo(pixels, 3);
        Assert.Equal(new Rgb24(0xEE, 0xEE, 0xEE), pixels[2]);
        Assert.Equal(new Rgb24(10, 11, 12), pixels[4]);
        Assert.Equal(new Rgb24(0xEE, 0xEE, 0xEE), pixels[5]);
    }

    [Fact]
    public void SegmentedAndPaddedStorageIsInvisible()
    {
        using var image = Image.ImportPixelBytesCore<Rgb24>(Enumerable.Range(0, 7 * 9 * 3).Select(i => (byte)i).ToArray(), 7, 9, 21, ImageConfiguration.Default, SegmentedPadded);
        var storage = image.Frames[0].Storage;
        Assert.True(storage.SlabCount > 1);
        Assert.True(storage.Layout.Stride > storage.RowLength);

        // Clones and appended frames keep the layout, and every visible row is intact
        using var clone = image.Clone();
        clone.AppendFrame(clone.Frames[0]);
        Assert.Equal(storage.SlabCount, clone.Frames[1].Storage.SlabCount);
        var bytes = new byte[7 * 9 * 3];
        clone.Frames[1].CopyPixelBytesTo(bytes);
        Assert.Equal(Enumerable.Range(0, 7 * 9 * 3).Select(i => (byte)i), bytes);
        clone.Frames[1].ProcessPixelBytes(static pixels =>
        {
            var length = pixels.GetRowSpan(8).Length;
            Assert.Equal(21, length);
        });
    }

    private static void CopyRoundTrip<TPixel>(bool segmented)
        where TPixel : unmanaged
    {
        const int Width = 5; // odd width
        const int Height = 7;
        var bytesPerPixel = Unsafe.SizeOf<TPixel>();
        var rowBytes = Width * bytesPerPixel;
        var strideInBytes = rowBytes + 3; // not a multiple of the pixel size
        var source = new byte[(strideInBytes * (Height - 1)) + rowBytes];
        for (var i = 0; i < source.Length; i++)
        {
            source[i] = (byte)((i * 37) + 11);
        }

        var options = segmented ? SegmentedPadded : null;
        using var image = Image.ImportPixelBytesCore<TPixel>(source, Width, Height, strideInBytes, ImageConfiguration.Default, options);
        if (segmented)
        {
            Assert.True(image.Frames[0].Storage.SlabCount > 1);
        }

        // Byte export with a stride: visible bytes copied, padding untouched
        var exported = Enumerable.Repeat((byte)0x5A, source.Length + 5).ToArray();
        image.Frames[0].CopyPixelBytesTo(exported, strideInBytes);
        for (var i = 0; i < exported.Length; i++)
        {
            var visible = i < source.Length && i % strideInBytes < rowBytes;
            Assert.Equal(visible ? source[i] : (byte)0x5A, exported[i]);
        }

        // Typed import/export with a pixel stride
        var typed = new TPixel[(Width + 2) * Height];
        image.Frames[0].CopyPixelDataTo(typed, Width + 2);
        using var reimported = Image.ImportPixelData<TPixel>(typed, Width, Height, Width + 2);
        var tight = new byte[rowBytes * Height];
        reimported.Frames[0].CopyPixelBytesTo(tight);
        for (var y = 0; y < Height; y++)
        {
            Assert.Equal(source.AsSpan(y * strideInBytes, rowBytes).ToArray(), tight.AsSpan(y * rowBytes, rowBytes).ToArray());
        }

        // Typed and untyped row views see the same bytes
        image.Frames[0].ProcessPixelRows(tight, static (pixels, expected) =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                var row = unsafe(MemoryMarshal.AsBytes(pixels.GetRowSpan(y)));
                Assert.Equal(expected.AsSpan(y * row.Length, row.Length).ToArray(), row.ToArray());
            }
        });
    }

    private readonly ref struct Counter(Span<int> value)
    {
        private readonly Span<int> _value = value;

        public void Increment() => _value[0]++;
    }

    private readonly ref struct Total(Span<long> value)
    {
        private readonly Span<long> _value = value;

        public void Add(long amount) => _value[0] += amount;
    }
}
