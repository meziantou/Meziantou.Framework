using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>Segmented pooled pixel storage: checked layout, accounting, zeroing and release.</summary>
public sealed class PixelStorageTests
{
    // 7 x 10 pixels of 3 bytes, rows padded to 24 bytes, at most 50 bytes per slab: 2 rows (24 + 21 = 45 bytes, a 64-byte
    // size class) per slab, 5 slabs, 320 charged bytes
    private static readonly PixelStorageLayoutOptions SegmentedOptions = new() { RowAlignment = 4, TargetSlabBytes = 50 };

    private static StorageOwner CreateOwner(long limit = long.MaxValue, SlabPool? pool = null)
        => new(new AllocationScope(new ImageResourceLimits { MaxLiveAllocationBytes = limit }, pool ?? new SlabPool()));

    [Fact]
    public void DefaultLayoutIsTightlyPackedInOneSlab()
    {
        var layout = PixelStorageLayout.Create(10, 10, 4);
        Assert.Equal(40, layout.RowLength);
        Assert.Equal(40, layout.Stride);
        Assert.Equal(10, layout.RowsPerSlab);
        Assert.Equal(1, layout.SlabCount);
        Assert.Equal(400, layout.FullSlabLength);
        Assert.Equal(400, layout.LastSlabLength);
        Assert.True(layout.TryGetCapacity(new SlabPool(), out var capacity));
        Assert.Equal(448, capacity);
    }

    [Fact]
    public void SegmentedLayoutKeepsRowsWholeAndOmitsTrailingPadding()
    {
        var layout = PixelStorageLayout.Create(7, 10, 3, SegmentedOptions);
        Assert.Equal(21, layout.RowLength);
        Assert.Equal(24, layout.Stride);
        Assert.Equal(2, layout.RowsPerSlab);
        Assert.Equal(5, layout.SlabCount);
        Assert.Equal(45, layout.FullSlabLength);
        Assert.Equal(2, layout.LastSlabRows);

        var odd = PixelStorageLayout.Create(7, 11, 3, SegmentedOptions);
        Assert.Equal(6, odd.SlabCount);
        Assert.Equal(1, odd.LastSlabRows);
        Assert.Equal(21, odd.LastSlabLength);

        // A row larger than the target gets a slab of its own
        var wide = PixelStorageLayout.Create(100, 3, 4, new PixelStorageLayoutOptions { TargetSlabBytes = 10 });
        Assert.Equal(1, wide.RowsPerSlab);
        Assert.Equal(3, wide.SlabCount);
        Assert.Equal(400, wide.FullSlabLength);
    }

    [Fact]
    public void LayoutArgumentsAreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PixelStorageLayout.Create(0, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PixelStorageLayout.Create(1, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PixelStorageLayout.Create(1, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelStorageLayoutOptions { RowAlignment = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelStorageLayoutOptions { TargetSlabBytes = 0 });
    }

    [Fact]
    public void StorageIsChargedAtTheActualCapacityOfItsSlabs()
    {
        var owner = CreateOwner();
        var storage = owner.Allocate(7, 10, 3, SegmentedOptions);
        Assert.Equal(5, storage.SlabCount);
        Assert.Equal([64, 64, 64, 64, 64], storage.GetSlabCapacities());
        Assert.Equal(320, storage.CapacityBytes);
        Assert.Equal(320, owner.Scope.LiveBytes);
        Assert.Equal(320, owner.Scope.GetLiveBytes(AllocationKind.ImagePixels));
        Assert.Equal(5, owner.Scope.GetDiagnostics().LiveAllocations);

        storage.Dispose();
        storage.Dispose();
        Assert.True(storage.IsDisposed);
        Assert.Equal(0, owner.Scope.LiveBytes);
        Assert.Equal(0, owner.LiveStorageCount);
    }

    [Fact]
    public void StorageAtTheLimitSucceedsAndOneByteLessFails()
    {
        var pool = new SlabPool();
        var atLimit = CreateOwner(320, pool);
        atLimit.Allocate(7, 10, 3, SegmentedOptions).Dispose();

        var overLimit = CreateOwner(319, pool);
        var before = pool.GetDiagnostics();
        var exception = Assert.Throws<ImageResourceLimitException>(() => overLimit.Allocate(7, 10, 3, SegmentedOptions));
        Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, exception.Kind);
        Assert.Equal(319, exception.Limit);
        Assert.Equal(320, exception.Requested); // the visible bytes are only 210
        Assert.Equal(before, pool.GetDiagnostics()); // the whole storage is charged before any slab is rented
        Assert.Equal(0, overLimit.Scope.GetDiagnostics().LiveBytes);
        Assert.Equal(0, overLimit.LiveStorageCount);
    }

    [Fact]
    public void LargeRequestsAreRejectedWithTheirActualCapacityBeforeAllocating()
    {
        // 20000 x 20000 Rgba64: 769 slabs of 26 rows (4,160,000 bytes in the 4 MiB class) and 1 slab of 6 rows (960,000
        // bytes in the 1 MiB class)
        var owner = new StorageOwner(AllocationScope.Create(configuration: null));
        var exception = Assert.Throws<ImageResourceLimitException>(() => owner.Allocate(20_000, 20_000, 8));
        Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, exception.Kind);
        Assert.Equal(ImageResourceLimits.DefaultMaxLiveAllocationBytes, exception.Limit);
        Assert.Equal((769L * 4_194_304) + 1_048_576, exception.Requested);
        Assert.Equal(0, owner.Scope.LiveBytes);
    }

    [Fact]
    public void OverflowingCapacityIsReportedWithoutWrappingAround()
    {
        var owner = CreateOwner();
        var exception = Assert.Throws<ImageResourceLimitException>(() => owner.Allocate(int.MaxValue, int.MaxValue, 8));
        Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, exception.Kind);
        Assert.Null(exception.Requested);
        Assert.Equal(0, owner.Scope.GetDiagnostics().LiveBytes);
        Assert.Equal(0, owner.Scope.GetDiagnostics().ReservedBytes);
    }

    [Fact]
    public void RowsLongerThanOneBufferAreRejectedAndReleaseTheReservation()
    {
        var owner = CreateOwner();
        var exception = Assert.Throws<ImageResourceLimitException>(() => owner.Allocate(int.MaxValue, 1, 4));
        Assert.Equal(ImageResourceLimitKind.Width, exception.Kind);
        Assert.Equal(Array.MaxLength / 4, exception.Limit);
        Assert.Equal(int.MaxValue, exception.Requested);
        Assert.Equal(0, owner.Scope.GetDiagnostics().LiveBytes);
    }

    [Fact]
    public void CheckedSizesReportOverflow()
    {
        Assert.True(CheckedSizes.TryMultiply(int.MaxValue, int.MaxValue, out var product));
        Assert.Equal((long)int.MaxValue * int.MaxValue, product);
        Assert.False(CheckedSizes.TryMultiply(long.MaxValue / 2, 3, out _));
        Assert.True(CheckedSizes.TryMultiply(0, long.MaxValue, out product));
        Assert.Equal(0, product);
        Assert.False(CheckedSizes.TryAdd(long.MaxValue, 1, out _));
        Assert.True(CheckedSizes.TryAlignUp(13, 8, out var aligned));
        Assert.Equal(16, aligned);
        Assert.False(CheckedSizes.TryAlignUp(long.MaxValue - 1, 8, out _));
        Assert.Equal(8L * int.MaxValue, CheckedSizes.GetRowLength(int.MaxValue, 8));
        Assert.Equal((long)int.MaxValue * int.MaxValue, CheckedSizes.GetPixelCount(int.MaxValue, int.MaxValue));
        Assert.False(CheckedSizes.TryGetVisibleByteCount(int.MaxValue, int.MaxValue, 8, out _));
        Assert.True(CheckedSizes.TryGetVisibleByteCount(3, 2, 4, out var visible));
        Assert.Equal(24, visible);
    }

    [Fact]
    public void NewStorageIsZeroedEvenWhenSlabsAreReused()
    {
        var pool = new SlabPool();
        var owner = CreateOwner(pool: pool);

        // Dirty the pool with buffers of every size class the storage uses
        var dirty = owner.Allocate(7, 10, 3, SegmentedOptions);
        using (var lease = dirty.AcquireLease())
        {
            for (var y = 0; y < lease.Height; y++)
            {
                lease.GetRowBytes(y).Fill(0xFF);
            }
        }

        foreach (var buffer in Enumerable.Range(0, 5).Select(_ => pool.Rent(64, clear: false)).ToArray())
        {
            buffer.AsSpan().Fill(0xEE);
            pool.Return(buffer);
        }

        dirty.Dispose();
        var reusedBefore = pool.GetDiagnostics().ReusedRentals;

        var storage = owner.Allocate(7, 10, 3, SegmentedOptions);
        Assert.True(pool.GetDiagnostics().ReusedRentals > reusedBefore);
        using (var lease = storage.AcquireLease())
        {
            for (var y = 0; y < lease.Height; y++)
            {
                Assert.Equal(new byte[21], lease.GetRowBytes(y).ToArray());
            }
        }
    }

    public static TheoryData<int, int> Layouts => new()
    {
        { 1, PixelStorageLayoutOptions.DefaultTargetSlabBytes },
        { 1, 1 },
        { 3, 1 },
        { 16, 1 },
        { 64, 200 },
        { 7, 333 },
        { 4096, 5000 },
    };

    [Theory]
    [MemberData(nameof(Layouts))]
    public void SegmentationAndPaddingDoNotChangeVisibleRows(int rowAlignment, int targetSlabBytes)
    {
        const int Width = 13; // odd width: 39-byte rows of Rgb24
        const int Height = 17;
        var options = new PixelStorageLayoutOptions { RowAlignment = rowAlignment, TargetSlabBytes = targetSlabBytes };
        var owner = CreateOwner();
        var storage = owner.Allocate(Width, Height, 3, options);
        Assert.Equal(storage.GetSlabCapacities().Sum(c => (long)c), storage.CapacityBytes);

        using (var lease = storage.AcquireLease())
        {
            Assert.Equal(Width * 3, lease.RowLength);
            for (var y = 0; y < Height; y++)
            {
                var row = lease.GetRow<Rgb24>(y);
                Assert.HasCount(Width, row.ToArray());
                for (var x = 0; x < Width; x++)
                {
                    row[x] = new Rgb24((byte)x, (byte)y, (byte)(x ^ (y * 7)));
                }
            }

            // Every row is read back exactly after all rows were written: rows never overlap and padding is never visible
            for (var y = 0; y < Height; y++)
            {
                Assert.Equal(CreateExpectedRow(Width, y), lease.GetRowBytes(y).ToArray());
            }
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => GetRowLength(storage, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => GetRowLength(storage, Height));
    }

    [Fact]
    public void AllPixelTypesMapOntoStorageRows()
    {
        AssertRoundTrip(new Rgba32(1, 2, 3, 4), [1, 2, 3, 4]);
        AssertRoundTrip(new Bgra32(1, 2, 3, 4), [3, 2, 1, 4]);
        AssertRoundTrip(new Rgb24(1, 2, 3), [1, 2, 3]);
        AssertRoundTrip(new Rgba64(0x0102, 0x0304, 0x0506, 0x0708), unsafe(MemoryMarshal.AsBytes<ushort>([0x0102, 0x0304, 0x0506, 0x0708])).ToArray());
        AssertRoundTrip(new Gray8(9), [9]);
        AssertRoundTrip(new Gray16(0x0A0B), unsafe(MemoryMarshal.AsBytes<ushort>([0x0A0B])).ToArray());

        static void AssertRoundTrip<TPixel>(TPixel pixel, byte[] expectedBytes)
            where TPixel : unmanaged
        {
            var owner = CreateOwner();
            var storage = owner.Allocate(5, 3, PixelFormats.GetBytesPerPixel(PixelFormats.GetPixelFormat<TPixel>()), new PixelStorageLayoutOptions { RowAlignment = 8, TargetSlabBytes = 1 });
            using var lease = storage.AcquireLease();
            lease.GetRow<TPixel>(1)[4] = pixel;
            Assert.Equal(expectedBytes, lease.GetRowBytes(1)[^expectedBytes.Length..].ToArray());
            Assert.Equal(new byte[expectedBytes.Length * 5], lease.GetRowBytes(0).ToArray());
            Assert.Equal(new byte[expectedBytes.Length * 5], lease.GetRowBytes(2).ToArray());
        }
    }

    [Fact]
    public void MismatchedPixelTypeIsRejected()
    {
        var storage = CreateOwner().Allocate(2, 2, 3);
        var exception = Assert.Throws<InvalidOperationException>(() => GetRowAs<Rgba32>(storage));
        Assert.Contains("Rgba32", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedSlabRentalReleasesEveryNewlyAcquiredResource()
    {
        var rentals = 0;
        var pool = new SlabPool { RentFailureInjector = _ => ++rentals == 3 ? new InjectedAllocationFailureException() : null };
        var owner = CreateOwner(pool: pool);

        Assert.Throws<InjectedAllocationFailureException>(() => owner.Allocate(7, 10, 3, SegmentedOptions));
        var diagnostics = owner.Scope.GetDiagnostics();
        Assert.Equal(0, diagnostics.LiveBytes);
        Assert.Equal(0, diagnostics.ReservedBytes);
        Assert.Equal(0, diagnostics.LiveAllocations);
        Assert.Equal(0, owner.LiveStorageCount);
        Assert.Equal(2, pool.GetDiagnostics().RetainedBuffers); // the two slabs rented before the failure went back

        pool.RentFailureInjector = null;
        Assert.Equal(320, owner.Allocate(7, 10, 3, SegmentedOptions).CapacityBytes);
    }

    [Fact]
    public void OwnerDisposalReleasesEveryStorageOnce()
    {
        var pool = new SlabPool();
        var owner = CreateOwner(pool: pool);
        var frame0 = owner.Allocate(4, 4, 4);
        var frame1 = owner.Allocate(4, 4, 4);
        _ = owner.Allocate(7, 10, 3, SegmentedOptions);
        frame1.Dispose();
        Assert.Equal(2, owner.LiveStorageCount);

        Assert.True(owner.Dispose());
        Assert.False(owner.Dispose());
        Assert.True(owner.IsDisposed);
        Assert.True(frame0.IsDisposed);
        Assert.Equal(0, owner.Scope.LiveBytes);
        Assert.Equal(0, owner.LiveStorageCount);
        Assert.Equal(7, pool.GetDiagnostics().RetainedReturns);

        frame0.Dispose(); // idempotent after the owner released it
        Assert.Throws<ObjectDisposedException>(() => owner.Allocate(1, 1, 1));
        Assert.Throws<ObjectDisposedException>(() => AcquireAndRelease(frame0));
        Assert.Throws<ObjectDisposedException>(owner.ThrowIfDisposed);
        Assert.Throws<ObjectDisposedException>(() => owner.EnsureCanModify("remove a frame"));
        Assert.Equal(0, owner.Scope.LiveBytes);
    }

    [Fact]
    public void ReaderReturnedImagesOutliveTheReader()
    {
        // A reader scope is shared by the reader working state and the images it returns
        var scope = new AllocationScope(ImageResourceLimits.Default, new SlabPool(), "reader");
        var reader = new StorageOwner(scope, "ImageReader");
        var compositor = reader.Allocate(8, 8, 4, kind: AllocationKind.CompositorState);
        var image = new StorageOwner(scope);
        var frame = image.Allocate(8, 8, 4);
        using (var lease = PixelLeasePair.Acquire(compositor, frame))
        {
            lease.First.GetRow<Rgba32>(3)[5] = new Rgba32(10, 20, 30, 40);
            lease.First.CopyTo(lease.Second);
        }

        Assert.Equal(compositor.CapacityBytes + frame.CapacityBytes, scope.LiveBytes);
        Assert.True(reader.Dispose());

        // The returned image stays valid and charged to the reader scope until it is disposed
        Assert.Equal(frame.CapacityBytes, scope.LiveBytes);
        Assert.Equal(0, scope.GetLiveBytes(AllocationKind.CompositorState));
        using (var lease = frame.AcquireLease())
        {
            Assert.Equal(new Rgba32(10, 20, 30, 40), lease.GetRow<Rgba32>(3)[5]);
        }

        Assert.True(image.Dispose());
        Assert.Equal(0, scope.LiveBytes);
    }

    [Fact]
    public void IndependentImagesHaveIndependentScopes()
    {
        var limits = new ImageResourceLimits { MaxLiveAllocationBytes = 448 };
        var first = new StorageOwner(new AllocationScope(limits));
        var second = new StorageOwner(new AllocationScope(limits));
        first.Allocate(10, 10, 4);
        second.Allocate(10, 10, 4); // each scope holds 448 bytes; a shared scope would reject this
        Assert.Throws<ImageResourceLimitException>(() => first.Allocate(1, 1, 1));
        first.Dispose();
        second.Dispose();
    }

    private static byte[] CreateExpectedRow(int width, int y)
    {
        var row = new byte[width * 3];
        for (var x = 0; x < width; x++)
        {
            row[(x * 3) + 0] = (byte)x;
            row[(x * 3) + 1] = (byte)y;
            row[(x * 3) + 2] = (byte)(x ^ (y * 7));
        }

        return row;
    }

    private static int GetRowLength(PixelStorage storage, int y)
    {
        using var lease = storage.AcquireLease();
        return lease.GetRowBytes(y).Length;
    }

    private static int GetRowAs<TPixel>(PixelStorage storage)
        where TPixel : unmanaged
    {
        using var lease = storage.AcquireLease();
        return lease.GetRow<TPixel>(0).Length;
    }

    private static void AcquireAndRelease(PixelStorage storage)
    {
        using var lease = storage.AcquireLease();
    }
}
