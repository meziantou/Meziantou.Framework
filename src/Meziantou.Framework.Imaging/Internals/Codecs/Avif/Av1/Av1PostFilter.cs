namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The post-processing filters of the decode frame wrapup process (AV1 specification, section 7.4): the loop filter, CDEF,
/// the super-resolution upscaling and the loop restoration, applied in place to the planes of a frame. The filters that read
/// the frame of the previous stage do so from copies of a few rows, so that a frame is never duplicated.
/// </summary>
internal sealed class Av1PostFilter : IDisposable
{
    private const int BoundaryRows = 4;

    private Av1PooledArray<ushort>? _rows;
    private Av1PooledArray<ushort>? _boundaries;
    private Av1PooledArray<int>? _scratch;

    /// <summary>Applies the filters that the header of <paramref name="frame"/> enables.</summary>
    /// <exception cref="ImageResourceLimitException">The working buffers exceed the live-allocation limit.</exception>
    public void Apply(AllocationScope scope, Av1FrameBuffer frame, Av1FrameState state, Av1InLoopFilters filters, CancellationToken cancellationToken)
    {
        var header = frame.Header;
        var sequence = frame.Sequence;
        if (filters.HasFlag(Av1InLoopFilters.Deblock) && (header.LoopFilterLevel[0] is not 0 || header.LoopFilterLevel[1] is not 0))
            Av1LoopFilter.Apply(frame, state, cancellationToken);

        var useCdef = filters.HasFlag(Av1InLoopFilters.Cdef) && !header.CodedLossless && !header.AllowIntrabc && sequence.EnableCdef;
        var useRestoration = filters.HasFlag(Av1InLoopFilters.Restoration) && header.UsesLr;
        if (!useCdef && !header.UseSuperres && !useRestoration)
            return;

        var lumaStride = frame.GetPlane(0).Stride;
        var stripeLength = (Av1LoopRestoration.MaxBlockHeight + (2 * Av1LoopRestoration.Padding)) * (lumaStride + (2 * Av1LoopRestoration.Padding));
        var rowsLength = Math.Max(3 * Av1Cdef.SourceRows * lumaStride, stripeLength);
        if (_rows is null || _rows.Length < rowsLength)
        {
            _rows?.Dispose();
            _rows = null;
            _rows = Av1PooledArray<ushort>.Rent(scope, rowsLength, clear: false);
        }

        if (useRestoration)
            SaveBoundaries(scope, frame);

        if (useCdef)
            Av1Cdef.Apply(frame, state, _rows.Span, cancellationToken);

        if (header.UseSuperres)
            Upscale(frame, useRestoration, cancellationToken);

        if (useRestoration)
        {
            _scratch ??= Av1PooledArray<int>.Rent(scope, Av1LoopRestoration.ScratchLength, clear: false);
            Restore(frame, state, cancellationToken);
        }
    }

    private static int GetStripeCount(int planeHeight, int subY) => ((((planeHeight - 1) << subY) + 8) / 64) + 1;

    // The rows of the deblocked frame around each stripe boundary: the loop restoration of a stripe reads them instead of the
    // rows that CDEF modified (section 7.17.6)
    private void SaveBoundaries(AllocationScope scope, Av1FrameBuffer frame)
    {
        long length = 0;
        for (var plane = 0; plane < frame.PlaneCount; plane++)
        {
            var buffer = frame.GetPlane(plane);
            length += (long)GetStripeCount(buffer.Height, plane > 0 ? frame.Sequence.SubsamplingY : 0) * BoundaryRows * buffer.Stride;
        }

        if (_boundaries is null || _boundaries.Length < length)
        {
            _boundaries?.Dispose();
            _boundaries = null;
            _boundaries = Av1PooledArray<ushort>.Rent(scope, length, clear: false);
        }

        var boundaries = _boundaries.Span;
        var offset = 0;
        for (var plane = 0; plane < frame.PlaneCount; plane++)
        {
            var subY = plane > 0 ? frame.Sequence.SubsamplingY : 0;
            var buffer = frame.GetPlane(plane);
            var stride = buffer.Stride;
            var samples = buffer.Samples;
            var stripeCount = GetStripeCount(buffer.Height, subY);
            for (var stripe = 1; stripe < stripeCount; stripe++)
            {
                var boundaryY = (-8 + (64 * stripe)) >> subY;
                for (var i = 0; i < BoundaryRows; i++)
                {
                    var y = Math.Min(boundaryY - 2 + i, buffer.Height - 1);
                    samples.Slice(y * stride, stride).CopyTo(boundaries[(offset + (((stripe * BoundaryRows) + i) * stride))..]);
                }
            }

            offset += stripeCount * BoundaryRows * stride;
        }
    }

    // Section 7.16 "Upscaling process", for the frame and for the saved rows of the deblocked frame
    private void Upscale(Av1FrameBuffer frame, bool upscaleBoundaries, CancellationToken cancellationToken)
    {
        var header = frame.Header;
        var sequence = frame.Sequence;
        var scratch = _rows!.Span;
        var boundaryOffset = 0;
        for (var plane = 0; plane < frame.PlaneCount; plane++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var subX = plane > 0 ? sequence.SubsamplingX : 0;
            var subY = plane > 0 ? sequence.SubsamplingY : 0;
            var buffer = frame.GetPlane(plane);
            var stride = buffer.Stride;
            var downscaledWidth = (header.FrameWidth + subX) >> subX;
            var upscaledWidth = (header.UpscaledWidth + subX) >> subX;
            var maxX = ((header.MiCols >> subX) * Av1Constants.MiSize) - 1;
            var samples = buffer.Samples;
            var row = scratch[..stride];
            for (var y = 0; y < buffer.Height; y++)
            {
                var destination = samples.Slice(y * stride, stride);
                destination.CopyTo(row);
                Av1SuperRes.UpscaleRow(row, destination, downscaledWidth, upscaledWidth, maxX, sequence.BitDepth);
            }

            if (upscaleBoundaries)
            {
                var stripeCount = GetStripeCount(buffer.Height, subY);
                var boundaries = _boundaries!.Span.Slice(boundaryOffset, stripeCount * BoundaryRows * stride);
                for (var i = BoundaryRows; i < stripeCount * BoundaryRows; i++)
                {
                    var destination = boundaries.Slice(i * stride, stride);
                    destination.CopyTo(row);
                    Av1SuperRes.UpscaleRow(row, destination, downscaledWidth, upscaledWidth, maxX, sequence.BitDepth);
                }

                boundaryOffset += stripeCount * BoundaryRows * stride;
            }
        }
    }

    // Section 7.17 "Loop restoration process", by stripes of 64 luma rows offset upwards by 8 rows
    private void Restore(Av1FrameBuffer frame, Av1FrameState state, CancellationToken cancellationToken)
    {
        var header = frame.Header;
        var sequence = frame.Sequence;
        var source = _rows!.Span;
        var scratch = _scratch!.Span;
        var boundaryOffset = 0;
        for (var plane = 0; plane < frame.PlaneCount; plane++)
        {
            var subX = plane > 0 ? sequence.SubsamplingX : 0;
            var subY = plane > 0 ? sequence.SubsamplingY : 0;
            var buffer = frame.GetPlane(plane);
            var stride = buffer.Stride;
            var planeHeight = buffer.Height;
            var stripeCount = GetStripeCount(planeHeight, subY);
            var boundaries = _boundaries!.Span.Slice(boundaryOffset, stripeCount * BoundaryRows * stride);
            boundaryOffset += stripeCount * BoundaryRows * stride;
            if (header.FrameRestorationType[plane] is Av1Constants.RestoreNone)
                continue;

            var planeWidth = (header.UpscaledWidth + subX) >> subX;
            var planeEndX = planeWidth - 1;
            var planeEndY = planeHeight - 1;
            var unitSize = header.LoopRestorationSize[plane];
            var unitRows = state.GetLoopRestorationUnitRows(plane);
            var unitCols = state.GetLoopRestorationUnitCols(plane);
            var units = state.GetLoopRestorationUnits(plane);
            var samples = buffer.Samples;
            var sourceStride = planeWidth + (2 * Av1LoopRestoration.Padding);
            for (var stripe = 0; stripe < stripeCount; stripe++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stripeStartY = (-8 + (stripe * 64)) >> subY;
                var stripeEndY = stripeStartY + (64 >> subY) - 1;
                var y0 = Math.Max(0, stripeStartY);
                var y1 = Math.Min(planeEndY, stripeEndY);
                var h = y1 - y0 + 1;
                if (h <= 0)
                    continue;

                // The source of the stripe: section 7.17.6 "Get source sample process" for the rows y0 - 3 to y1 + 3
                for (var i = 0; i < h + (2 * Av1LoopRestoration.Padding); i++)
                {
                    var y = Math.Clamp(y0 - Av1LoopRestoration.Padding + i, 0, planeEndY);
                    ReadOnlySpan<ushort> row;
                    if (y < stripeStartY)
                    {
                        y = Math.Max(stripeStartY - 2, y);
                        row = boundaries.Slice(((stripe * BoundaryRows) + (y - (stripeStartY - 2))) * stride, planeWidth);
                    }
                    else if (y > stripeEndY)
                    {
                        y = Math.Min(stripeEndY + 2, y);
                        row = boundaries.Slice((((stripe + 1) * BoundaryRows) + 2 + (y - (stripeEndY + 1))) * stride, planeWidth);
                    }
                    else
                    {
                        row = samples.Slice(y * stride, planeWidth);
                    }

                    var destination = source.Slice(i * sourceStride, sourceStride);
                    destination[..Av1LoopRestoration.Padding].Fill(row[0]);
                    row.CopyTo(destination[Av1LoopRestoration.Padding..]);
                    destination[(Av1LoopRestoration.Padding + planeWidth)..].Fill(row[planeEndX]);
                }

                var unitRow = Math.Min(unitRows - 1, (((y0 << subY) + 8) >> subY) / unitSize);
                for (var x = 0; x < planeWidth;)
                {
                    var unitCol = Math.Min(unitCols - 1, x / unitSize);
                    var unitEnd = unitCol == unitCols - 1 ? planeWidth : (unitCol + 1) * unitSize;
                    var w = Math.Min(Av1LoopRestoration.MaxBlockWidth, unitEnd - x);
                    var unit = units.Slice(((unitRow * unitCols) + unitCol) * Av1FrameState.LoopRestorationUnitLength, Av1FrameState.LoopRestorationUnitLength);
                    var sourceOffset = (Av1LoopRestoration.Padding * sourceStride) + Av1LoopRestoration.Padding + x;
                    var destination = samples[((y0 * stride) + x)..];
                    if (unit[0] is Av1Constants.RestoreWiener)
                    {
                        Av1LoopRestoration.ApplyWiener(source, sourceStride, sourceOffset, destination, stride, w, h, unit, sequence.BitDepth, scratch);
                    }
                    else if (unit[0] is Av1Constants.RestoreSgrproj)
                    {
                        Av1LoopRestoration.ApplySelfGuided(source, sourceStride, sourceOffset, destination, stride, w, h, unit, sequence.BitDepth, scratch);
                    }

                    x += w;
                }
            }
        }
    }

    public void Dispose()
    {
        _rows?.Dispose();
        _rows = null;
        _boundaries?.Dispose();
        _boundaries = null;
        _scratch?.Dispose();
        _scratch = null;
    }
}
