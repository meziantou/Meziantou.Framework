using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

// Internal entry points of the custom operations of ImageProcessingExtensions, called after argument validation
public abstract partial class Image
{
    /// <summary>Runs a row operation on every displayed frame and the poster, in place, by bands of rows.</summary>
    internal void ApplyRowOperationFrames<TPixel, TOperation>(TOperation operation, CancellationToken cancellationToken)
        where TPixel : unmanaged
        where TOperation : IPixelRowOperation<TPixel>
    {
        var working = GetWorkingFormat<TPixel>();
        Owner.EnsureCanModify("apply a row operation");
        cancellationToken.ThrowIfCancellationRequested();
        var format = PixelFormat;
        var workers = RowBands.GetWorkerCount(_size, _configuration.MaxDegreeOfParallelism);
        using var scratch = RentWorkingRows(working, workers);
        _metadata.RemoveStaleThumbnail();
        foreach (var frame in GetAllFrames())
        {
            using var lease = frame.GetStorage().AcquireLease();
            var storage = lease.Storage;
            RunBands(workers, storage.Height, (band, start, end) =>
            {
                var rowBuffer = scratch?.GetRow(band);
                for (var y = start; y < end; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var bytes = storage.GetRowSpanCore(y);
                    if (rowBuffer is null)
                    {
                        operation.Invoke(y, unsafe(MemoryMarshal.Cast<byte, TPixel>(bytes)));
                        continue;
                    }

                    var row = rowBuffer.Value.Span;
                    PixelConverter.ConvertRow(format, bytes, working, row);
                    operation.Invoke(y, unsafe(MemoryMarshal.Cast<byte, TPixel>(row)));
                    StoreWorkingRow(working, row, format, bytes);
                }
            });
        }
    }

    /// <summary>
    /// Runs a neighborhood operation on every displayed frame and the poster: each frame is copied (in the working format)
    /// to a scratch storage charged to the scope of the image, then every row is computed from the copy, by bands of rows.
    /// </summary>
    internal void ApplyNeighborhoodOperationFrames<TPixel, TOperation>(TOperation operation, CancellationToken cancellationToken)
        where TPixel : unmanaged
        where TOperation : IPixelNeighborhoodOperation<TPixel>
    {
        var working = GetWorkingFormat<TPixel>();
        Owner.EnsureCanModify("apply a neighborhood operation");
        cancellationToken.ThrowIfCancellationRequested();
        var format = PixelFormat;
        var workers = RowBands.GetWorkerCount(_size, _configuration.MaxDegreeOfParallelism);
        var source = Owner.Allocate(_size.Width, _size.Height, PixelFormats.GetBytesPerPixel(working), options: null, AllocationKind.Temporary);
        try
        {
            using var scratch = RentWorkingRows(working, workers);
            _metadata.RemoveStaleThumbnail();
            foreach (var frame in GetAllFrames())
            {
                using var leases = PixelLeasePair.Acquire(frame.GetStorage(), source);
                var storage = leases.First.Storage;
                var copy = leases.Second.Storage;
                var copyToken = leases.Second.Token;
                for (var y = 0; y < storage.Height; y++)
                {
                    PixelConverter.ConvertRow(format, storage.GetRowSpanCore(y), working, copy.GetRowSpanCore(y));
                }

                RunBands(workers, storage.Height, (band, start, end) =>
                {
                    var rows = new PixelRowSource<TPixel>(copy, copyToken);
                    var rowBuffer = scratch?.GetRow(band);
                    for (var y = start; y < end; y++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var bytes = storage.GetRowSpanCore(y);
                        if (rowBuffer is null)
                        {
                            operation.Invoke(y, rows, unsafe(MemoryMarshal.Cast<byte, TPixel>(bytes)));
                            continue;
                        }

                        // The rented row holds stale pool bytes: the operation receives the current pixels instead, like a row of the image
                        var row = rowBuffer.Value.Span;
                        copy.GetRowSpanCore(y).CopyTo(row);
                        operation.Invoke(y, rows, unsafe(MemoryMarshal.Cast<byte, TPixel>(row)));
                        StoreWorkingRow(working, row, format, bytes);
                    }
                });
            }
        }
        finally
        {
            source.Dispose();
        }
    }

    /// <summary>
    /// Gets the pixel format of the rows an operation of <typeparamref name="TPixel"/> receives, validating that the rows of
    /// the image convert to it without loss: the format of the image, <see cref="Rgba64"/> for an integer format, or
    /// <see cref="RgbaFloat"/>.
    /// </summary>
    /// <exception cref="NotSupportedException"><typeparamref name="TPixel"/> cannot hold the samples of the image.</exception>
    private PixelFormat GetWorkingFormat<TPixel>()
        where TPixel : unmanaged
    {
        var working = PixelFormats.GetPixelFormat<TPixel>();
        var format = PixelFormat;
        if (working == format || working == PixelFormat.RgbaFloat || (working == PixelFormat.Rgba64 && !PixelFormats.IsFloatingPoint(format)))
            return working;

        throw new NotSupportedException($"An operation on {typeof(TPixel).Name} rows cannot process {format} pixels without a loss: use {format} rows, Rgba64 rows (integer formats) or RgbaFloat rows.");
    }

    /// <summary>Rents one row of the working format per worker when it differs from the format of the image, or <see langword="null"/>.</summary>
    private WorkingRows? RentWorkingRows(PixelFormat working, int workers)
        => working == PixelFormat ? null : new WorkingRows(Owner.Scope, CheckedSizes.GetRowLength(_size.Width, PixelFormats.GetBytesPerPixel(working)), workers);

    /// <summary>Stores a row of the working format in a row of the image: out-of-range floating-point samples are clipped.</summary>
    private static void StoreWorkingRow(PixelFormat working, ReadOnlySpan<byte> row, PixelFormat format, Span<byte> destination)
        => PixelConverter.ConvertRow(working, row, format, destination, floats: FloatSampleConversion.Clipping);

    /// <summary>Runs the rows of a frame: once with one worker, else one band per worker; the exception is the one of the first band that failed.</summary>
    private static void RunBands(int workers, int height, Action<int, int, int> rows)
    {
        if (workers is 1)
        {
            rows(0, 0, height);
            return;
        }

        RowBands.RunParallelInBandOrder(workers, band => rows(band, RowBands.GetBandStart(height, band, workers), RowBands.GetBandStart(height, band + 1, workers)));
    }

    /// <summary>The row buffers of the workers of an operation in a working format, charged to the scope of the image.</summary>
    private sealed class WorkingRows : IDisposable
    {
        private readonly PooledBuffer[] _rows;

        public WorkingRows(AllocationScope scope, long rowLength, int workers)
        {
            if (rowLength > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(scope.Limits);

            _rows = new PooledBuffer[workers];
            try
            {
                for (var i = 0; i < workers; i++)
                {
                    _rows[i] = scope.Rent((int)rowLength, AllocationKind.Temporary, clear: false);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public Memory<byte> GetRow(int worker) => _rows[worker].Memory;

        public void Dispose()
        {
            foreach (var row in _rows)
            {
                row?.Dispose();
            }
        }
    }
}
