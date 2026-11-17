using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>Scoped pixel access: indexer, row callbacks, strided copies and raw imports.</summary>
public sealed class PixelAccessTests
{
    private static readonly PixelStorageLayoutOptions SegmentedPadded = new() { RowAlignment = 16, TargetSlabBytes = 64 };

    [Fact]
    public void CustomPixelsAreImportedCopiedAndProcessedThroughTheirStorageFormat()
    {
        #region ICustomPixel_Usage
        // Bgr555 converts from and to Rgb24: an image of Rgb24 pixels is made from its pixels
        Bgr555[] source = [Bgr555.FromColor(31, 0, 0), Bgr555.FromColor(0, 31, 0), Bgr555.FromColor(0, 0, 31), Bgr555.FromColor(31, 31, 31)];
        using var image = Image.ImportCustomPixelData<Bgr555>(source, width: 2, height: 2);
        Assert.Equal(PixelFormat.Rgb24, image.PixelFormat);

        // The pixels of a frame of any format are converted to the storage format, then to the custom pixels
        using var gray = image.CloneAs<Gray16>();
        var copy = new Bgr555[4];
        gray.Frames[0].CopyCustomPixelDataTo<Bgr555>(copy);

        // A frame of the storage format is edited in place, row by row
        image.Frames[0].ProcessCustomPixelRows<Bgr555>(static (row, y) =>
        {
            foreach (ref var pixel in row)
            {
                pixel = Bgr555.FromColor(pixel.Blue, pixel.Green, pixel.Red);
            }
        });
        #endregion

        var rgb = new Rgb24[4];
        ((Image<Rgb24>)image).Frames[0].CopyPixelDataTo(rgb);
        Assert.Equal([new Rgb24(0, 0, 255), new Rgb24(0, 255, 0), new Rgb24(255, 0, 0), new Rgb24(255, 255, 255)], rgb);

        // The copy of the gray frame is its conversion to Rgb24, then to Bgr555
        using var grayRgb = gray.CloneAs<Rgb24>();
        var bytes = new byte[12];
        grayRgb.Frames[0].CopyPixelBytesTo(bytes);
        var expected = new Bgr555[4];
        Bgr555.FromStorage(bytes, expected);
        Assert.Equal(expected, copy);
        Assert.All(copy, pixel => Assert.Equal((pixel.Red, pixel.Red), (pixel.Green, pixel.Blue)));
    }

    [Fact]
    public void CustomPixelsKeepTheStrideOfTheCallerAndCheckTheirArguments()
    {
        // A stride of 3 pixels for rows of 2: the third pixel of each row is padding, never read nor written
        var source = new Bgr555[] { Bgr555.FromColor(1, 2, 3), Bgr555.FromColor(4, 5, 6), new(0x7FFF), Bgr555.FromColor(7, 8, 9), Bgr555.FromColor(10, 11, 12) };
        using var image = Image.ImportCustomPixelData<Bgr555>(source, 2, 2, strideInPixels: 3);
        var copy = new Bgr555[5];
        copy.AsSpan().Fill(new Bgr555(0x1234));
        image.Frames[0].CopyCustomPixelDataTo<Bgr555>(copy, strideInPixels: 3);
        Assert.Equal([Bgr555.FromColor(1, 2, 3), Bgr555.FromColor(4, 5, 6), new Bgr555(0x1234), Bgr555.FromColor(7, 8, 9), Bgr555.FromColor(10, 11, 12)], copy);

        Assert.Throws<ArgumentException>("source", () => Image.ImportCustomPixelData<Bgr555>(source.AsSpan(0, 4), 2, 2, strideInPixels: 3));
        Assert.Throws<ArgumentOutOfRangeException>("strideInPixels", () => Image.ImportCustomPixelData<Bgr555>(source, 2, 2, strideInPixels: 1));
        Assert.Throws<ArgumentOutOfRangeException>("width", () => Image.ImportCustomPixelData<Bgr555>(source, 0, 2));
        Assert.Throws<ArgumentException>("destination", () => image.Frames[0].CopyCustomPixelDataTo<Bgr555>(new Bgr555[3]));
        Assert.Throws<ArgumentNullException>("action", () => image.Frames[0].ProcessCustomPixelRows<Bgr555>(null!));
        Assert.Throws<NotSupportedException>(() => Image.ImportCustomPixelData<UnknownStoragePixel>([default], 1, 1));
        Assert.Throws<ImageResourceLimitException>(() => Image.ImportCustomPixelData<Bgr555>(source, 2, 2, configuration: new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = 1 } }));
    }

    [Fact]
    public void ProcessingCustomPixelsNeedsTheStorageFormat()
    {
        using var image = new Image<Rgba32>(2, 1);
        var exception = Assert.Throws<InvalidOperationException>(() => image.Frames[0].ProcessCustomPixelRows<Bgr555>(static (row, y) => { }));
        Assert.Contains("Rgb24", exception.Message);
    }

    [Fact]
    public void CopyingCustomPixelsFollowsTheConversionRules()
    {
        // Removing alpha without a background is rejected before any pixel is written, as CloneAs does
        using var image = new Image<Rgba32>(2, 1);
        image.Frames[0][1, 0] = new Rgba32(255, 0, 0, 128);
        var copy = new Bgr555[2];
        copy.AsSpan().Fill(new Bgr555(0x1234));
        Assert.Throws<UnsupportedImageFeatureException>(() => image.Frames[0].CopyCustomPixelDataTo<Bgr555>(copy));
        Assert.All(copy, pixel => Assert.Equal(new Bgr555(0x1234), pixel));

        image.Frames[0].CopyCustomPixelDataTo<Bgr555>(copy, options: new PixelConversionOptions { BackgroundColor = new Rgba64(0, 0, 0) });
        Assert.Equal([Bgr555.FromColor(0, 0, 0), Bgr555.FromColor(16, 0, 0)], copy);
    }

    [Fact]
    public void AFailingCustomConversionReleasesTheImage()
    {
        using var tracking = new AllocationTracking();
        Assert.Throws<InvalidOperationException>(() => Image.ImportCustomPixelData<FailingPixel>(new FailingPixel[4], 2, 2));
        Assert.Equal(0, tracking.LiveBytes);
    }

    [Fact]
    public void CustomPixelCopiesConvertCmykThroughTheEmbeddedProfileLikeCloneAs()
    {
        // The embedded profile converts the inks, not the device formulas: the same colors as CloneAs with the same options
        using var image = new Image<Cmyk32>(2, 1, new Cmyk32(10, 200, 40, 30));
        image.Frames[0][1, 0] = new Cmyk32(220, 15, 90, 5);
        image.Metadata.IccProfile = Metadata.IccProfile.Cgats001CompatibleCmyk;
        var options = new PixelConversionOptions { CmykConversion = CmykPixelConversionOptions.Default };
        using var rgb = image.CloneAs<Rgb24>(options);
        var expected = new Bgr555[2];
        rgb.Frames[0].CopyCustomPixelDataTo<Bgr555>(expected);
        var actual = new Bgr555[2];
        image.Frames[0].CopyCustomPixelDataTo<Bgr555>(actual, options: options);
        Assert.Equal(expected, actual);
    }

    #region ICustomPixel_Bgr555
    /// <summary>A pixel of 5-bit blue, green and red components packed in 16 bits (blue in the low bits), stored as <see cref="Rgb24"/>.</summary>
    private readonly record struct Bgr555(ushort Packed) : ICustomPixel<Bgr555>
    {
        public static PixelFormat StorageFormat => PixelFormat.Rgb24;

        public int Blue => Packed & 31;

        public int Green => (Packed >> 5) & 31;

        public int Red => (Packed >> 10) & 31;

        public static Bgr555 FromColor(int red, int green, int blue) => new((ushort)((red << 10) | (green << 5) | blue));

        public static void FromStorage(ReadOnlySpan<byte> source, Span<Bgr555> destination)
        {
            for (var i = 0; i < destination.Length; i++)
            {
                // The nearest 5-bit component of each 8-bit sample
                destination[i] = FromColor(((source[3 * i] * 31) + 127) / 255, ((source[(3 * i) + 1] * 31) + 127) / 255, ((source[(3 * i) + 2] * 31) + 127) / 255);
            }
        }

        public static void ToStorage(ReadOnlySpan<Bgr555> source, Span<byte> destination)
        {
            for (var i = 0; i < source.Length; i++)
            {
                destination[3 * i] = (byte)(((source[i].Red * 255) + 15) / 31);
                destination[(3 * i) + 1] = (byte)(((source[i].Green * 255) + 15) / 31);
                destination[(3 * i) + 2] = (byte)(((source[i].Blue * 255) + 15) / 31);
            }
        }
    }
    #endregion

    private readonly struct UnknownStoragePixel : ICustomPixel<UnknownStoragePixel>
    {
        public static PixelFormat StorageFormat => PixelFormat.Unknown;

        public static void FromStorage(ReadOnlySpan<byte> source, Span<UnknownStoragePixel> destination) => throw new InvalidOperationException();

        public static void ToStorage(ReadOnlySpan<UnknownStoragePixel> source, Span<byte> destination) => throw new InvalidOperationException();
    }

    private readonly struct FailingPixel : ICustomPixel<FailingPixel>
    {
        public static PixelFormat StorageFormat => PixelFormat.Gray8;

        public static void FromStorage(ReadOnlySpan<byte> source, Span<FailingPixel> destination) => throw new InvalidOperationException();

        public static void ToStorage(ReadOnlySpan<FailingPixel> source, Span<byte> destination) => throw new InvalidOperationException();
    }

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
            case PixelFormat.RgbaFloat: CopyRoundTrip<RgbaFloat>(segmented); break;
            case PixelFormat.RgbaHalf: CopyRoundTrip<RgbaHalf>(segmented); break;
            case PixelFormat.La16: CopyRoundTrip<La16>(segmented); break;
            case PixelFormat.La32: CopyRoundTrip<La32>(segmented); break;
            case PixelFormat.Rgb48: CopyRoundTrip<Rgb48>(segmented); break;
            case PixelFormat.A8: CopyRoundTrip<A8>(segmented); break;
            case PixelFormat.Argb32: CopyRoundTrip<Argb32>(segmented); break;
            case PixelFormat.Abgr32: CopyRoundTrip<Abgr32>(segmented); break;
            case PixelFormat.Bgr565: CopyRoundTrip<Bgr565>(segmented); break;
            case PixelFormat.Rgba1010102: CopyRoundTrip<Rgba1010102>(segmented); break;
            case PixelFormat.Cmyk32: CopyRoundTrip<Cmyk32>(segmented); break;
            case PixelFormat.Cmyk64: CopyRoundTrip<Cmyk64>(segmented); break;
            case PixelFormat.GrayFloat: CopyRoundTrip<GrayFloat>(segmented); break;
            case PixelFormat.GrayHalf: CopyRoundTrip<GrayHalf>(segmented); break;
            case PixelFormat.LaFloat: CopyRoundTrip<LaFloat>(segmented); break;
            case PixelFormat.LaHalf: CopyRoundTrip<LaHalf>(segmented); break;
            default: throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    [Fact]
    public void WrappedMemoryIsReadAndWrittenInPlace()
    {
        // Two rows of three Rgb24 pixels, a stride of 10 bytes: the padding is never touched
        var memory = new byte[19];
        memory.AsSpan().Fill(0xAA);
        for (var i = 0; i < 9; i++)
        {
            memory[i] = (byte)i;
            memory[10 + i] = (byte)(100 + i);
        }

        var releases = 0;
        var image = Image.WrapMemory<Rgb24>(memory, 3, 2, strideInBytes: 10, release: () => releases++);
        Assert.Equal(new Size(3, 2), image.Size);
        Assert.True(image.Frames[0].GetStorage().IsExternal);
        Assert.Equal(new Rgb24(3, 4, 5), image.Frames[0][1, 0]);
        Assert.Equal(new Rgb24(106, 107, 108), image.Frames[0][2, 1]);

        image.Frames[0][0, 1] = new Rgb24(7, 8, 9);
        Assert.Equal(new byte[] { 7, 8, 9 }, memory.AsSpan(10, 3).ToArray());
        image.Flip(FlipMode.Horizontal);
        Assert.Equal(new byte[] { 6, 7, 8, 3, 4, 5, 0, 1, 2, 0xAA }, memory.AsSpan(0, 10).ToArray());
        Assert.Equal(0xAA, memory[9]);

        // A copy, a clone and a new frame are memory of the library
        using (var clone = image.Clone())
        {
            Assert.False(clone.Frames[0].GetStorage().IsExternal);
        }

        Assert.False(image.AppendFrame().GetStorage().IsExternal);
        Assert.Equal(0, releases);
        image.Dispose();
        Assert.Equal(1, releases);
        image.Dispose();
        Assert.Equal(1, releases);
        Assert.Equal(new byte[] { 6, 7, 8 }, memory.AsSpan(0, 3).ToArray());
    }

    [Fact]
    public void GeometryChangesReleaseTheWrappedMemory()
    {
        var memory = new byte[4 * 4 * 4];
        var releases = 0;
        using var image = Image.WrapMemory(PixelFormat.Rgba32, memory, 4, 4, release: () => releases++);
        var typed = Assert.IsType<Image<Rgba32>>(image);
        typed.Frames[0][3, 3] = new Rgba32(1, 2, 3, 4);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, memory.AsSpan(60).ToArray());

        image.Crop(new Rectangle(2, 2, 2, 2));
        Assert.Equal(1, releases);
        Assert.False(image.Frames[0].GetStorage().IsExternal);
        Assert.Equal(new Rgba32(1, 2, 3, 4), typed.Frames[0][1, 1]);

        // The memory is no longer used
        typed.Frames[0][1, 1] = default;
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, memory.AsSpan(60).ToArray());
    }

    [Fact]
    public void NativeMemoryCanBeWrapped()
    {
        const int Stride = 9;
        var freed = false;
        unsafe
        {
            var pointer = (byte*)NativeMemory.AllocZeroed(Stride * 2);
            var address = (nint)pointer;
            using (var image = Image.WrapMemory<Gray16>(pointer, 4, 2, Stride, release: () =>
            {
                unsafe
                {
                    NativeMemory.Free((void*)address);
                }

                freed = true;
            }))
            {
                image.Frames[0][3, 1] = new Gray16(0x1234);
                Assert.Equal(new Gray16(0x1234), image.Frames[0][3, 1]);
                Assert.Equal(BitConverter.IsLittleEndian ? (byte)0x34 : (byte)0x12, pointer[Stride + 6]);
                using var untyped = Image.WrapMemory(PixelFormat.Gray16, pointer, 4, 2, Stride);
                Assert.Equal(new Gray16(0x1234), Assert.IsType<Image<Gray16>>(untyped).Frames[0][3, 1]);
                Assert.False(freed);
            }
        }

        Assert.True(freed);
    }

    [Fact]
    public void WrapValidatesItsArgumentsBeforeTakingTheMemory()
    {
        var released = false;
        void Release() => released = true;
        var memory = new byte[12];
        Assert.Throws<NotSupportedException>(() => Image.WrapMemory<int>(memory, 1, 1, release: Release));
        Assert.Throws<ArgumentOutOfRangeException>("pixelFormat", () => Image.WrapMemory((PixelFormat)999, memory, 1, 1, release: Release));
        Assert.Throws<ArgumentOutOfRangeException>("width", () => Image.WrapMemory<Rgba32>(memory, 0, 1, release: Release));
        Assert.Throws<ArgumentOutOfRangeException>("height", () => Image.WrapMemory<Rgba32>(memory, 1, 0, release: Release));
        Assert.Throws<ArgumentOutOfRangeException>("strideInBytes", () => Image.WrapMemory<Rgba32>(memory, 2, 1, strideInBytes: 7, release: Release));
        Assert.Throws<ArgumentException>("memory", () => Image.WrapMemory<Rgba32>(memory, 2, 2, release: Release));
        Assert.Throws<ArgumentNullException>("address", () =>
        {
            unsafe
            {
                _ = Image.WrapMemory<Rgba32>((void*)null, 1, 1, release: Release);
            }
        });
        var limits = new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = 2 } };
        Assert.Throws<ImageResourceLimitException>(() => Image.WrapMemory<Rgb24>(memory, 3, 1, configuration: limits, release: Release));
        Assert.False(released);

        // The last row only needs its visible bytes, and the memory is not charged to the allocation limit
        var tiny = new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = 1 } };
        using var image = Image.WrapMemory<Rgba32>(memory, 1, 2, strideInBytes: 8, configuration: tiny);
        Assert.Same(tiny, image.Configuration);
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
