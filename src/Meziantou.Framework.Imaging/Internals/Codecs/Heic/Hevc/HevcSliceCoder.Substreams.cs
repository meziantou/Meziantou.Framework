using System.Runtime.ExceptionServices;

namespace Meziantou.Framework.Imaging.Internals;

// The substreams of a slice segment decoded by several workers. A substream (a tile, or a row of coding tree blocks of the
// wavefront in a tile) starts with context variables that do not depend on the bins of the substream before it: they are
// initialized (clause 9.3.2.2), or synchronized with the ones that the row above stored (clause 9.3.2.4), and the first
// quantization group is predicted from SliceQpY (clause 8.6.1). A block reads no block of another tile (clause 6.4.1), and a
// row of the wavefront reads no block of the row above to the right of the next column, so a worker writes the state of
// the picture in its substream only, and reads it in its substream and in the rows above it that are already decoded. The
// entry points give where each substream starts; the checks of the sequential decoding are made afterwards, in the order
// of the substreams, so the state of the picture, the pictures and the errors do not depend on the number of workers.
internal ref partial struct HevcSliceCoder
{
    // Decodes concurrently the substreams of the slice segment that start at an entry point inside its data, from the first
    // one: returns HevcSubstreamOutcome.EndOfSliceSegment when the slice segment ends in one of them, EndOfSubset when the
    // next substream is to be decoded by the caller from next (its entry point is not checked yet), and None when there is
    // nothing to decode concurrently (fewer than two substreams).
    private HevcSubstreamOutcome DecodeSubstreamsConcurrently(int position, out int next)
    {
        next = 0;
        var count = CountSubstreams(position);
        if (count < 2)
            return HevcSubstreamOutcome.None;

        var workerCount = Math.Min(_maxWorkers, count);
        var workers = _workers!;

        // Everything a worker uses is rented on the calling thread, before the workers start
        workers.Prepare(workerCount, inter: _refs is not null);
        using var job = new HevcSubstreamJob(workers.Scope, count);
        FillSubstreams(job, position);
        job.ResetProgress();
        var context = new SubstreamContext(job, workers, _layout, _header, _picture, _state, _rbsp, _refs, _cancellationToken);
        RowBands.RunParallel(workerCount, context.Run);

        // A cancellation first, then the checks of the sequential decoding, in the order of the substreams
        var canceled = Array.Find(job.Errors, error => error is OperationCanceledException);
        if (canceled is not null)
        {
            ExceptionDispatchInfo.Throw(canceled);
        }

        var lastStored = -1;
        for (var k = 0; k < count; k++)
        {
            if (k > 0)
            {
                CheckEntryPoint(job.EndPositions[k - 1]);
            }

            if (job.Errors[k] is { } error)
            {
                ExceptionDispatchInfo.Throw(error);
            }

            _tools |= job.Tools[k];
            if (job.HasStoredWppContexts[k])
            {
                lastStored = k;
            }

            if (job.Outcomes[k] is HevcSubstreamOutcome.EndOfSliceSegment)
            {
                StoreWppContexts(job, lastStored);
                FinishConcurrentSliceSegment(job, k);
                return HevcSubstreamOutcome.EndOfSliceSegment;
            }

            if (job.Outcomes[k] is not HevcSubstreamOutcome.EndOfSubset)
                throw new InvalidOperationException("A substream was not decoded although the ones before it continue.");
        }

        // The substreams that follow are decoded on the calling thread, as they are when there is a single worker
        StoreWppContexts(job, lastStored);
        _ctbAddrInTs = job.End[count - 1];
        _ctbAddrInRs = _ctbAddrTsToRs[_ctbAddrInTs];
        next = job.EndPositions[count - 1];
        return HevcSubstreamOutcome.EndOfSubset;
    }

    // The number of substreams from the first one whose entry points are inside the data, as the sequential decoding
    // checks them: up to the last entry point, the end of the picture, or an entry point beyond the data
    private readonly int CountSubstreams(int position) => VisitSubstreams(job: null, position);

    private readonly void FillSubstreams(HevcSubstreamJob job, int position) => VisitSubstreams(job, position);

    private readonly int VisitSubstreams(HevcSubstreamJob? job, int position)
    {
        var entryPoints = _header.EntryPointOffsetMinus1;
        var picSizeInCtbsY = _sps.PicSizeInCtbsY;
        long sourceOffset = _rbsp.ToSourceOffset(_header.DataOffset);
        var ctbAddrInTs = _ctbAddrInTs;
        var count = 0;
        while (true)
        {
            var first = ctbAddrInTs;
            ctbAddrInTs = GetSubstreamEnd(ctbAddrInTs);
            if (job is not null)
            {
                job.First[count] = first;
                job.End[count] = ctbAddrInTs;
                job.Start[count] = position;
                job.DependsOnPrevious[count] = count > 0 && _pps.EntropyCodingSyncEnabledFlag && _tileId[first] == _tileId[first - 1];
            }

            count++;
            if (count > entryPoints.Length || ctbAddrInTs >= picSizeInCtbsY)
                return count;

            // CheckEntryPoint: the substream that follows starts at its entry point
            sourceOffset += entryPoints[count - 1] + 1L;
            if (sourceOffset > _rbsp.Length + _rbsp.EmulationPreventionByteCount)
                return count;

            position = _rbsp.Offset + _rbsp.FromSourceOffset((int)sourceOffset);
            if (position >= _end)
                return count;
        }
    }

    // The address in tile scan of the coding tree block that starts the next tile, or the next row of the wavefront
    private readonly int GetSubstreamEnd(int ctbAddrInTs)
    {
        var picSizeInCtbsY = _sps.PicSizeInCtbsY;
        var tileId = _tileId[ctbAddrInTs];
        while (++ctbAddrInTs < picSizeInCtbsY)
        {
            if (_tileId[ctbAddrInTs] != tileId)
                break;

            if (_pps.EntropyCodingSyncEnabledFlag)
            {
                var ctbAddrInRs = _ctbAddrTsToRs[ctbAddrInTs];
                if (ctbAddrInRs % _picWidthInCtbsY is 0 || _tileId[_ctbAddrRsToTs[ctbAddrInRs - 1]] != tileId)
                    break;
            }
        }

        return ctbAddrInTs;
    }

    // The context variables of the wavefront that the last substream to store them stored, for the slice segments and the
    // substreams that follow
    private readonly void StoreWppContexts(HevcSubstreamJob job, int substream)
    {
        if (substream < 0)
            return;

        var slot = job.GetWppSlot(substream);
        slot[..HevcContextLayout.Count].CopyTo(_state.ContextsWpp);
        slot[HevcContextLayout.Count..].CopyTo(_state.StatCoeffWpp);
    }

    // The end of a slice segment that ends in the substream k: the state that the slice segments that follow read, and the
    // state of the coding tree blocks that the substreams after k decoded, which are not part of the slice segment, as
    // before they were decoded
    private readonly void FinishConcurrentSliceSegment(HevcSubstreamJob job, int k)
    {
        // The substream stops after the coding tree block that ends the slice segment
        _state.NextCtbAddrInTs = job.EndCtbAddrInTs[k];
        _state.LastQpY = job.EndQpY[k];
        if (_pps.DependentSliceSegmentsEnabledFlag)
        {
            var slot = job.GetEndSlot(k);
            slot[..HevcContextLayout.Count].CopyTo(_state.ContextsDs);
            slot[HevcContextLayout.Count..].CopyTo(_state.StatCoeffDs);
        }

        if (k < job.Count - 1)
        {
            _state.ClearCodingTreeBlocks(_layout, job.End[k], job.End[job.Count - 1]);
        }
    }

    // Decodes the substream k on a worker, from its entry point, with the arithmetic decoder and the buffers of the worker
    private void DecodeSubstream(HevcSubstreamJob job, int k)
    {
        _ctbAddrInTs = job.First[k];
        _ctbAddrInRs = _ctbAddrTsToRs[_ctbAddrInTs];
        var dependsOnPrevious = job.DependsOnPrevious[k];
        var rx0 = _ctbAddrInRs % _picWidthInCtbsY;

        // The columns of the tile of a row of the wavefront, and the address of the first block of the row above
        var tileColumn = 0;
        while (_layout.ColBd[tileColumn + 1] <= rx0)
        {
            tileColumn++;
        }

        var tileRight = _layout.ColBd[tileColumn + 1];
        var aboveFirst = job.First[k] - (tileRight - rx0);
        if (k is 0)
        {
            InitializeSubstream(job.Start[0], isSliceSegmentStart: true, _state.ContextsWpp, _state.StatCoeffWpp);
        }
        else
        {
            ReadOnlySpan<byte> contexts = _state.ContextsWpp;
            ReadOnlySpan<byte> statCoeff = _state.StatCoeffWpp;
            if (dependsOnPrevious)
            {
                // The block above and to the right is decoded, and the row above stored its context variables after it,
                // when the substream before this one decoded it
                job.WaitFor(k - 1, aboveFirst + Math.Min(1, tileRight - rx0 - 1));
                if (aboveFirst + 1 >= job.First[k - 1])
                {
                    var slot = job.GetWppSlot(k - 1);
                    contexts = slot[..HevcContextLayout.Count];
                    statCoeff = slot[HevcContextLayout.Count..];
                }
            }

            InitializeSubstream(job.Start[k], isSliceSegmentStart: false, contexts, statCoeff);
        }

        var picSizeInCtbsY = _sps.PicSizeInCtbsY;
        var end = job.End[k];
        while (true)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (job.IsAbandoned(k))
                return;

            if (dependsOnPrevious)
            {
                // The block above and to the right, or the last block of the row above in the tile
                var rx = _ctbAddrInRs % _picWidthInCtbsY;
                job.WaitFor(k - 1, aboveFirst + Math.Min(rx + 1, tileRight - 1) - rx0);
            }

            DecodeCodingTreeUnit();
            var endOfSliceSegmentFlag = _cabac.DecodeTerminate() is not 0;

            // Clause 9.3.1: the storage process after the second coding tree block of a row of a tile
            if (_pps.EntropyCodingSyncEnabledFlag
                && (_ctbAddrInRs % _picWidthInCtbsY is 1 || (_ctbAddrInRs > 1 && _tileId[_ctbAddrInTs] != _tileId[_ctbAddrRsToTs[_ctbAddrInRs - 2]])))
            {
                var slot = job.GetWppSlot(k);
                _cabac.SaveContexts(slot[..HevcContextLayout.Count]);
                StoreStatCoeff(slot[HevcContextLayout.Count..]);
                job.HasStoredWppContexts[k] = true;
            }

            _ctbAddrInTs++;
            job.Publish(k, _ctbAddrInTs);
            if (endOfSliceSegmentFlag)
            {
                job.StopAfter(k);
                job.Tools[k] = _tools;
                job.EndCtbAddrInTs[k] = _ctbAddrInTs;
                job.EndQpY[k] = _qpY;
                if (_pps.DependentSliceSegmentsEnabledFlag)
                {
                    // Clause 9.3.1: the storage process at the end of a slice segment
                    var slot = job.GetEndSlot(k);
                    _cabac.SaveContexts(slot[..HevcContextLayout.Count]);
                    StoreStatCoeff(slot[HevcContextLayout.Count..]);
                }

                // rbsp_slice_segment_trailing_bits( ), as DecodeSliceSegment reads them
                var trailing = ReadAlignmentZeroBits();
                if (_rbsp.Array.AsSpan(trailing, _end - trailing).ContainsAnyExcept((byte)0))
                    throw HevcErrors.Invalid("data follow the trailing bits of a slice segment.");

                job.Outcomes[k] = HevcSubstreamOutcome.EndOfSliceSegment;
                return;
            }

            if (_ctbAddrInTs >= picSizeInCtbsY)
                throw HevcErrors.Invalid("a slice segment goes beyond the last coding tree block of the picture.");

            _ctbAddrInRs = _ctbAddrTsToRs[_ctbAddrInTs];
            if (_ctbAddrInTs == end)
            {
                // end_of_subset_one_bit, then byte_alignment( )
                if (_cabac.DecodeTerminate() is 0)
                    throw HevcErrors.Invalid("end_of_subset_one_bit is not 1.");

                job.EndPositions[k] = ReadAlignmentZeroBits();
                job.Tools[k] = _tools;
                job.Outcomes[k] = HevcSubstreamOutcome.EndOfSubset;
                return;
            }
        }
    }

    // What the workers share: they take the substreams in order, and decode each one with a coder of their own
    private sealed class SubstreamContext(HevcSubstreamJob job, HevcSubstreamWorkers workers, HevcPictureLayout layout, HevcSliceSegmentHeader header, HevcPicture picture, HevcPictureState state, HevcRbspBuffer rbsp, HevcReferencePictureLists? references, CancellationToken cancellationToken)
    {
        public void Run(int worker)
        {
            var cabac = workers.GetCabac(worker);
            var scratch = workers.GetScratch(worker);
            while (job.TakeNext() is >= 0 and var k)
            {
                try
                {
                    var coder = new HevcSliceCoder(layout, header, picture, state, cabac, scratch, rbsp, references, cancellationToken);
                    coder.DecodeSubstream(job, k);
                }
                catch (Exception exception)
                {
                    job.Errors[k] = exception;
                    job.Outcomes[k] = HevcSubstreamOutcome.Failed;
                    job.StopAfter(k);
                }
                finally
                {
                    // The substreams that wait for this one go on: their decoding is not used when this one failed
                    job.Publish(k, int.MaxValue);
                }
            }
        }
    }
}
