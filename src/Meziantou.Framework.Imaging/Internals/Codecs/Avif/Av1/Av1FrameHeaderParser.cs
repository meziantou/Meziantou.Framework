namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Parses the uncompressed header of an AV1 frame (AV1 specification, section 5.9) against the sequence header and the
/// reference frames it depends on. The parser follows the syntax tables of the specification, in their order.
/// </summary>
internal ref struct Av1FrameHeaderParser
{
    private const int AllFrames = (1 << Av1Constants.NumRefFrames) - 1;

    private readonly Av1SequenceHeader _sequence;
    private readonly ReadOnlySpan<Av1FrameBuffer?> _slots;
    private readonly Span<bool> _refValid;
    private readonly Av1FrameHeader _header;
    private Av1BitReader _reader;
    private Av1GlobalMotion _previousGlobalMotion;

    private Av1FrameHeaderParser(Av1BitReader reader, Av1SequenceHeader sequence, ReadOnlySpan<Av1FrameBuffer?> slots, Span<bool> refValid)
    {
        _reader = reader;
        _sequence = sequence;
        _slots = slots;
        _refValid = refValid;
        _header = new Av1FrameHeader();
    }

    /// <summary>
    /// Parses <c>uncompressed_header()</c> from <paramref name="reader"/>, which is left after the last bit of the header.
    /// </summary>
    /// <param name="reader">The reader, positioned at the first bit of the header.</param>
    /// <param name="sequence">The active sequence header.</param>
    /// <param name="obu">The header of the OBU that carries the frame header.</param>
    /// <param name="slots">The 8 reference frame slots; an empty slot is <see langword="null"/>.</param>
    /// <param name="refValid"><c>RefValid</c>: updated by the reference frame marking of the header.</param>
    /// <exception cref="InvalidImageContentException">The header is malformed or refers to a missing or incompatible reference frame.</exception>
    public static Av1FrameHeader Parse(ref Av1BitReader reader, Av1SequenceHeader sequence, in Av1ObuHeader obu, ReadOnlySpan<Av1FrameBuffer?> slots, Span<bool> refValid)
    {
        var parser = new Av1FrameHeaderParser(reader, sequence, slots, refValid);
        parser._header.TemporalId = obu.TemporalId;
        parser._header.SpatialId = obu.SpatialId;
        parser.ReadUncompressedHeader();
        reader = parser._reader;
        return parser._header;
    }

    private bool ReadFlag() => _reader.ReadFlag();

    private int ReadBits(int count) => (int)_reader.ReadBits(count);

    private Av1FrameBuffer GetSlot(int index)
    {
        return _slots[index] ?? throw Av1Errors.Invalid("a frame refers to an empty reference slot.");
    }

    // Section 5.9.2 "Uncompressed header syntax"
    private void ReadUncompressedHeader()
    {
        var header = _header;
        var sequence = _sequence;
        var idLength = sequence.FrameIdLength;
        if (sequence.ReducedStillPictureHeader)
        {
            header.FrameType = Av1Constants.KeyFrame;
            header.FrameIsIntra = true;
            header.ShowFrame = true;
            header.ErrorResilientMode = true;
        }
        else
        {
            header.ShowExistingFrame = ReadFlag();
            if (header.ShowExistingFrame)
            {
                ReadShowExistingFrame(idLength);
                return;
            }

            header.FrameType = ReadBits(2);
            header.FrameIsIntra = header.FrameType is Av1Constants.IntraOnlyFrame or Av1Constants.KeyFrame;
            header.ShowFrame = ReadFlag();
            if (header.ShowFrame && sequence.DecoderModelInfoPresent && !sequence.EqualPictureInterval)
                ReadTemporalPointInfo();

            header.ShowableFrame = header.ShowFrame ? header.FrameType is not Av1Constants.KeyFrame : ReadFlag();
            header.ErrorResilientMode = header.FrameType is Av1Constants.SwitchFrame || (header.FrameType is Av1Constants.KeyFrame && header.ShowFrame) || ReadFlag();
        }

        if (header.FrameType is Av1Constants.KeyFrame && header.ShowFrame)
            _refValid.Clear();

        header.DisableCdfUpdate = ReadFlag();
        header.AllowScreenContentTools = sequence.ForceScreenContentTools is Av1Constants.SelectScreenContentTools ? ReadFlag() : sequence.ForceScreenContentTools is not 0;
        if (header.AllowScreenContentTools)
            header.ForceIntegerMv = sequence.ForceIntegerMv is Av1Constants.SelectIntegerMv ? ReadFlag() : sequence.ForceIntegerMv is not 0;

        if (header.FrameIsIntra)
            header.ForceIntegerMv = true;

        if (sequence.FrameIdNumbersPresent)
        {
            header.CurrentFrameId = ReadBits(idLength);
            MarkRefFrames(idLength);
        }

        if (header.FrameType is Av1Constants.SwitchFrame)
        {
            header.FrameSizeOverrideFlag = true;
        }
        else if (!sequence.ReducedStillPictureHeader)
        {
            header.FrameSizeOverrideFlag = ReadFlag();
        }

        header.OrderHint = ReadBits(sequence.OrderHintBits);
        header.PrimaryRefFrame = header.FrameIsIntra || header.ErrorResilientMode ? Av1Constants.PrimaryRefNone : ReadBits(3);
        if (sequence.DecoderModelInfoPresent)
            ReadBufferRemovalTimes();

        header.RefreshFrameFlags = header.FrameType is Av1Constants.SwitchFrame || (header.FrameType is Av1Constants.KeyFrame && header.ShowFrame) ? AllFrames : ReadBits(8);
        if (header.FrameType is Av1Constants.IntraOnlyFrame && header.RefreshFrameFlags is AllFrames)
            throw Av1Errors.Invalid("an intra-only frame refreshes every reference frame.");

        if ((!header.FrameIsIntra || header.RefreshFrameFlags is not AllFrames) && header.ErrorResilientMode && sequence.EnableOrderHint)
        {
            for (var i = 0; i < Av1Constants.NumRefFrames; i++)
            {
                var refOrderHint = ReadBits(sequence.OrderHintBits);
                if (_slots[i] is not { } slot || slot.Header.OrderHint != refOrderHint)
                    _refValid[i] = false;
            }
        }

        if (header.FrameIsIntra)
        {
            ReadFrameSize();
            ReadRenderSize();
            if (header.AllowScreenContentTools && header.UpscaledWidth == header.FrameWidth)
                header.AllowIntrabc = ReadFlag();
        }
        else
        {
            ReadInterFrameSetup(idLength);
        }

        header.DisableFrameEndUpdateCdf = sequence.ReducedStillPictureHeader || header.DisableCdfUpdate || ReadFlag();

        // setup_past_independence() or load_previous(): the state that the syntax below updates
        if (header.PrimaryRefFrame is Av1Constants.PrimaryRefNone)
        {
            header.LoopFilterDeltaEnabled = true;
            header.LoopFilterDeltas = Av1LoopFilterDeltas.Default;
            _previousGlobalMotion = Av1GlobalMotion.Identity;
        }
        else
        {
            var previous = GetSlot(header.RefFrameIdx[header.PrimaryRefFrame]).Header;
            header.LoopFilterDeltas = previous.LoopFilterDeltas;
            header.Segmentation = previous.Segmentation;
            _previousGlobalMotion = previous.GlobalMotion;
        }

        ReadTileInfo();
        ReadQuantizationParams();
        ReadSegmentationParams();
        ReadDeltaQParams();
        ReadDeltaLfParams();
        ComputeLossless();
        ReadLoopFilterParams();
        ReadCdefParams();
        ReadLoopRestorationParams();
        ReadTxMode();
        header.ReferenceSelect = !header.FrameIsIntra && ReadFlag();
        ReadSkipModeParams();
        header.AllowWarpedMotion = !header.FrameIsIntra && !header.ErrorResilientMode && sequence.EnableWarpedMotion && ReadFlag();
        header.ReducedTxSet = ReadFlag();
        ReadGlobalMotionParams();
        ReadFilmGrainParams();
    }

    private void ReadShowExistingFrame(int idLength)
    {
        var header = _header;
        header.FrameToShowMapIdx = ReadBits(3);
        if (_sequence.DecoderModelInfoPresent && !_sequence.EqualPictureInterval)
            ReadTemporalPointInfo();

        var slot = GetSlot(header.FrameToShowMapIdx);
        if (_sequence.FrameIdNumbersPresent)
        {
            var displayFrameId = ReadBits(idLength);
            if (displayFrameId != slot.Header.CurrentFrameId || !_refValid[header.FrameToShowMapIdx])
                throw Av1Errors.Invalid("the identifier of the frame to show does not match its reference slot.");
        }

        if (!slot.Header.ShowableFrame)
            throw Av1Errors.Invalid("a frame that is not showable is shown.");

        header.FrameType = slot.Header.FrameType;
        header.RefreshFrameFlags = header.FrameType is Av1Constants.KeyFrame ? AllFrames : 0;
        if (_sequence.FilmGrainParamsPresent)
            header.FilmGrain = slot.Header.FilmGrain;
    }

    private void ReadTemporalPointInfo() => _reader.Skip(_sequence.FramePresentationTimeLength);

    private void ReadBufferRemovalTimes()
    {
        if (!ReadFlag())
            return;

        for (var i = 0; i < _sequence.OperatingPointCount; i++)
        {
            var operatingPoint = _sequence.GetOperatingPoint(i);
            if (!operatingPoint.DecoderModelPresent)
                continue;

            var inTemporalLayer = ((operatingPoint.Idc >> _header.TemporalId) & 1) is not 0;
            var inSpatialLayer = ((operatingPoint.Idc >> (_header.SpatialId + 8)) & 1) is not 0;
            if (operatingPoint.Idc is 0 || (inTemporalLayer && inSpatialLayer))
                _reader.Skip(_sequence.BufferRemovalTimeLength);
        }
    }

    // Section 5.9.4 "Reference frame marking function"
    private void MarkRefFrames(int idLength)
    {
        var diffLength = _sequence.DeltaFrameIdLength;
        var current = _header.CurrentFrameId;
        for (var i = 0; i < Av1Constants.NumRefFrames; i++)
        {
            if (_slots[i] is not { } slot)
                continue;

            var refFrameId = slot.Header.CurrentFrameId;
            if (current > (1 << diffLength))
            {
                if (refFrameId > current || refFrameId < current - (1 << diffLength))
                    _refValid[i] = false;
            }
            else if (refFrameId > current && refFrameId < (1 << idLength) + current - (1 << diffLength))
            {
                _refValid[i] = false;
            }
        }
    }

    private void ReadInterFrameSetup(int idLength)
    {
        var header = _header;
        var sequence = _sequence;
        var shortSignaling = sequence.EnableOrderHint && ReadFlag();
        if (shortSignaling)
        {
            var lastFrameIdx = ReadBits(3);
            var goldFrameIdx = ReadBits(3);
            SetFrameRefs(lastFrameIdx, goldFrameIdx);
        }

        for (var i = 0; i < Av1Constants.RefsPerFrame; i++)
        {
            if (!shortSignaling)
                header.RefFrameIdx[i] = (sbyte)ReadBits(3);

            var slotIndex = header.RefFrameIdx[i];
            var slot = GetSlot(slotIndex);
            if (!_refValid[slotIndex])
                throw Av1Errors.Invalid("a frame refers to a reference frame that is not valid.");

            if (slot.Sequence.BitDepth != sequence.BitDepth || slot.Sequence.SubsamplingX != sequence.SubsamplingX || slot.Sequence.SubsamplingY != sequence.SubsamplingY || slot.Sequence.Monochrome != sequence.Monochrome)
                throw Av1Errors.Invalid("a reference frame has a different sample format.");

            if (sequence.FrameIdNumbersPresent)
            {
                var deltaFrameId = ReadBits(sequence.DeltaFrameIdLength) + 1;
                var expectedFrameId = (header.CurrentFrameId + (1 << idLength) - deltaFrameId) % (1 << idLength);
                if (expectedFrameId != slot.Header.CurrentFrameId)
                    throw Av1Errors.Invalid("a reference frame does not have the expected identifier.");
            }
        }

        if (header.FrameSizeOverrideFlag && !header.ErrorResilientMode)
        {
            ReadFrameSizeWithRefs();
        }
        else
        {
            ReadFrameSize();
            ReadRenderSize();
        }

        // Section 6.8.6: a reference frame is at most twice as large and sixteen times as small as the frame
        for (var i = 0; i < Av1Constants.RefsPerFrame; i++)
        {
            var reference = GetSlot(header.RefFrameIdx[i]).Header;
            if (2 * header.FrameWidth < reference.UpscaledWidth
                || 2 * header.FrameHeight < reference.FrameHeight
                || header.FrameWidth > 16 * reference.UpscaledWidth
                || header.FrameHeight > 16 * reference.FrameHeight)
            {
                throw Av1Errors.Invalid("the size of a reference frame is outside the scaling limits.");
            }
        }

        header.AllowHighPrecisionMv = !header.ForceIntegerMv && ReadFlag();

        // read_interpolation_filter()
        header.InterpolationFilter = ReadFlag() ? Av1Constants.Switchable : ReadBits(2);
        header.IsMotionModeSwitchable = ReadFlag();
        header.UseRefFrameMvs = !header.ErrorResilientMode && sequence.EnableRefFrameMvs && ReadFlag();
        for (var i = 0; i < Av1Constants.RefsPerFrame; i++)
        {
            var refFrame = Av1Constants.LastFrame + i;
            var hint = GetSlot(header.RefFrameIdx[i]).Header.OrderHint;
            header.OrderHints[refFrame] = hint;
            header.RefFrameSignBias[refFrame] = sequence.EnableOrderHint && GetRelativeDistance(hint, header.OrderHint) > 0;
        }
    }

    /// <summary>Returns <c>get_relative_dist( a, b )</c> (section 5.9.3) for the order hints of the sequence.</summary>
    public static int GetRelativeDistance(Av1SequenceHeader sequence, int a, int b)
    {
        if (!sequence.EnableOrderHint)
            return 0;

        var diff = a - b;
        var m = 1 << (sequence.OrderHintBits - 1);
        return (diff & (m - 1)) - (diff & m);
    }

    private readonly int GetRelativeDistance(int a, int b) => GetRelativeDistance(_sequence, a, b);

    // Section 7.8 "Set frame refs process"
    private void SetFrameRefs(int lastFrameIdx, int goldFrameIdx)
    {
        var header = _header;
        for (var i = 0; i < Av1Constants.RefsPerFrame; i++)
        {
            header.RefFrameIdx[i] = -1;
        }

        header.RefFrameIdx[Av1Constants.LastFrame - Av1Constants.LastFrame] = (sbyte)lastFrameIdx;
        header.RefFrameIdx[Av1Constants.GoldenFrame - Av1Constants.LastFrame] = (sbyte)goldFrameIdx;

        Span<bool> usedFrame = stackalloc bool[Av1Constants.NumRefFrames];
        usedFrame[lastFrameIdx] = true;
        usedFrame[goldFrameIdx] = true;

        var curFrameHint = 1 << (_sequence.OrderHintBits - 1);
        Span<int> shiftedOrderHints = stackalloc int[Av1Constants.NumRefFrames];
        for (var i = 0; i < Av1Constants.NumRefFrames; i++)
        {
            shiftedOrderHints[i] = curFrameHint + GetRelativeDistance(GetSlot(i).Header.OrderHint, header.OrderHint);
        }

        if (shiftedOrderHints[lastFrameIdx] >= curFrameHint || shiftedOrderHints[goldFrameIdx] >= curFrameHint)
            throw Av1Errors.Invalid("the last or golden reference frame is not a forward reference.");

        // ALTREF_FRAME: the backward reference with the highest output order
        var reference = FindLatestBackward(shiftedOrderHints, usedFrame, curFrameHint);
        if (reference >= 0)
        {
            header.RefFrameIdx[Av1Constants.AltrefFrame - Av1Constants.LastFrame] = (sbyte)reference;
            usedFrame[reference] = true;
        }

        // BWDREF_FRAME: the closest backward reference
        reference = FindEarliestBackward(shiftedOrderHints, usedFrame, curFrameHint);
        if (reference >= 0)
        {
            header.RefFrameIdx[Av1Constants.BwdrefFrame - Av1Constants.LastFrame] = (sbyte)reference;
            usedFrame[reference] = true;
        }

        // ALTREF2_FRAME: the next closest backward reference
        reference = FindEarliestBackward(shiftedOrderHints, usedFrame, curFrameHint);
        if (reference >= 0)
        {
            header.RefFrameIdx[Av1Constants.Altref2Frame - Av1Constants.LastFrame] = (sbyte)reference;
            usedFrame[reference] = true;
        }

        // The remaining references are forward references in anti-chronological order
        foreach (var refFrame in Av1Tables.RefFrameList)
        {
            if (header.RefFrameIdx[refFrame - Av1Constants.LastFrame] >= 0)
                continue;

            reference = FindLatestForward(shiftedOrderHints, usedFrame, curFrameHint);
            if (reference >= 0)
            {
                header.RefFrameIdx[refFrame - Av1Constants.LastFrame] = (sbyte)reference;
                usedFrame[reference] = true;
            }
        }

        // Any remaining reference is the frame with the smallest output order
        reference = -1;
        var earliestOrderHint = 0;
        for (var i = 0; i < Av1Constants.NumRefFrames; i++)
        {
            var hint = shiftedOrderHints[i];
            if (reference < 0 || hint < earliestOrderHint)
            {
                reference = i;
                earliestOrderHint = hint;
            }
        }

        for (var i = 0; i < Av1Constants.RefsPerFrame; i++)
        {
            if (header.RefFrameIdx[i] < 0)
                header.RefFrameIdx[i] = (sbyte)reference;
        }
    }

    private static int FindLatestBackward(ReadOnlySpan<int> shiftedOrderHints, ReadOnlySpan<bool> usedFrame, int curFrameHint)
    {
        var reference = -1;
        var latestOrderHint = 0;
        for (var i = 0; i < Av1Constants.NumRefFrames; i++)
        {
            var hint = shiftedOrderHints[i];
            if (!usedFrame[i] && hint >= curFrameHint && (reference < 0 || hint >= latestOrderHint))
            {
                reference = i;
                latestOrderHint = hint;
            }
        }

        return reference;
    }

    private static int FindEarliestBackward(ReadOnlySpan<int> shiftedOrderHints, ReadOnlySpan<bool> usedFrame, int curFrameHint)
    {
        var reference = -1;
        var earliestOrderHint = 0;
        for (var i = 0; i < Av1Constants.NumRefFrames; i++)
        {
            var hint = shiftedOrderHints[i];
            if (!usedFrame[i] && hint >= curFrameHint && (reference < 0 || hint < earliestOrderHint))
            {
                reference = i;
                earliestOrderHint = hint;
            }
        }

        return reference;
    }

    private static int FindLatestForward(ReadOnlySpan<int> shiftedOrderHints, ReadOnlySpan<bool> usedFrame, int curFrameHint)
    {
        var reference = -1;
        var latestOrderHint = 0;
        for (var i = 0; i < Av1Constants.NumRefFrames; i++)
        {
            var hint = shiftedOrderHints[i];
            if (!usedFrame[i] && hint < curFrameHint && (reference < 0 || hint >= latestOrderHint))
            {
                reference = i;
                latestOrderHint = hint;
            }
        }

        return reference;
    }

    // Section 5.9.5 "Frame size syntax"
    private void ReadFrameSize()
    {
        var header = _header;
        if (header.FrameSizeOverrideFlag)
        {
            header.FrameWidth = ReadBits(_sequence.FrameWidthBits) + 1;
            header.FrameHeight = ReadBits(_sequence.FrameHeightBits) + 1;
            if (header.FrameWidth > _sequence.MaxFrameWidth || header.FrameHeight > _sequence.MaxFrameHeight)
                throw Av1Errors.Invalid("a frame is larger than the maximum size of its sequence.");
        }
        else
        {
            header.FrameWidth = _sequence.MaxFrameWidth;
            header.FrameHeight = _sequence.MaxFrameHeight;
        }

        ReadSuperresParams();
        ComputeImageSize();
    }

    // Section 5.9.6 "Render size syntax"
    private void ReadRenderSize()
    {
        var header = _header;
        if (ReadFlag())
        {
            header.RenderWidth = ReadBits(16) + 1;
            header.RenderHeight = ReadBits(16) + 1;
        }
        else
        {
            header.RenderWidth = header.UpscaledWidth;
            header.RenderHeight = header.FrameHeight;
        }
    }

    // Section 5.9.7 "Frame size with refs syntax"
    private void ReadFrameSizeWithRefs()
    {
        var header = _header;
        for (var i = 0; i < Av1Constants.RefsPerFrame; i++)
        {
            if (!ReadFlag())
                continue;

            var reference = GetSlot(header.RefFrameIdx[i]).Header;
            header.UpscaledWidth = reference.UpscaledWidth;
            header.FrameWidth = header.UpscaledWidth;
            header.FrameHeight = reference.FrameHeight;
            header.RenderWidth = reference.RenderWidth;
            header.RenderHeight = reference.RenderHeight;
            ReadSuperresParams();
            ComputeImageSize();
            return;
        }

        ReadFrameSize();
        ReadRenderSize();
    }

    // Section 5.9.8 "Superres params syntax"
    private void ReadSuperresParams()
    {
        var header = _header;
        header.UseSuperres = _sequence.EnableSuperres && ReadFlag();
        header.SuperresDenom = header.UseSuperres ? ReadBits(Av1Constants.SuperresDenomBits) + Av1Constants.SuperresDenomMin : Av1Constants.SuperresNum;
        header.UpscaledWidth = header.FrameWidth;
        header.FrameWidth = ((header.UpscaledWidth * Av1Constants.SuperresNum) + (header.SuperresDenom / 2)) / header.SuperresDenom;
    }

    // Section 5.9.9 "Compute image size function"
    private void ComputeImageSize()
    {
        _header.MiCols = 2 * ((_header.FrameWidth + 7) >> 3);
        _header.MiRows = 2 * ((_header.FrameHeight + 7) >> 3);
    }

    // Section 5.9.15 "Tile info syntax"
    private void ReadTileInfo()
    {
        var header = _header;
        var use128 = _sequence.Use128x128Superblock;
        var sbCols = use128 ? (header.MiCols + 31) >> 5 : (header.MiCols + 15) >> 4;
        var sbRows = use128 ? (header.MiRows + 31) >> 5 : (header.MiRows + 15) >> 4;
        var sbShift = use128 ? 5 : 4;
        var sbSize = sbShift + 2;
        var maxTileWidthSb = Av1Constants.MaxTileWidth >> sbSize;
        var maxTileAreaSb = Av1Constants.MaxTileArea >> (2 * sbSize);
        var minLog2TileCols = TileLog2(maxTileWidthSb, sbCols);
        var maxLog2TileCols = TileLog2(1, Math.Min(sbCols, Av1Constants.MaxTileCols));
        var maxLog2TileRows = TileLog2(1, Math.Min(sbRows, Av1Constants.MaxTileRows));
        var minLog2Tiles = Math.Max(minLog2TileCols, TileLog2(maxTileAreaSb, sbRows * sbCols));

        Span<int> colStarts = stackalloc int[Av1Constants.MaxTileCols + 1];
        Span<int> rowStarts = stackalloc int[Av1Constants.MaxTileRows + 1];
        int tileCols;
        int tileRows;
        if (ReadFlag())
        {
            // uniform_tile_spacing_flag
            header.TileColsLog2 = minLog2TileCols;
            while (header.TileColsLog2 < maxLog2TileCols && ReadFlag())
            {
                header.TileColsLog2++;
            }

            var tileWidthSb = (sbCols + (1 << header.TileColsLog2) - 1) >> header.TileColsLog2;
            tileCols = 0;
            for (var startSb = 0; startSb < sbCols; startSb += tileWidthSb)
            {
                colStarts[tileCols++] = startSb << sbShift;
            }

            var minLog2TileRows = Math.Max(minLog2Tiles - header.TileColsLog2, 0);
            header.TileRowsLog2 = minLog2TileRows;
            while (header.TileRowsLog2 < maxLog2TileRows && ReadFlag())
            {
                header.TileRowsLog2++;
            }

            var tileHeightSb = (sbRows + (1 << header.TileRowsLog2) - 1) >> header.TileRowsLog2;
            tileRows = 0;
            for (var startSb = 0; startSb < sbRows; startSb += tileHeightSb)
            {
                rowStarts[tileRows++] = startSb << sbShift;
            }
        }
        else
        {
            var widestTileSb = 0;
            tileCols = 0;
            for (var startSb = 0; startSb < sbCols;)
            {
                if (tileCols >= Av1Constants.MaxTileCols)
                    throw Av1Errors.Invalid("a frame has more than 64 tile columns.");

                colStarts[tileCols++] = startSb << sbShift;
                var maxWidth = Math.Min(sbCols - startSb, maxTileWidthSb);
                var sizeSb = _reader.ReadNonSymmetric(maxWidth) + 1;
                widestTileSb = Math.Max(sizeSb, widestTileSb);
                startSb += sizeSb;
            }

            header.TileColsLog2 = TileLog2(1, tileCols);
            maxTileAreaSb = minLog2Tiles > 0 ? (sbRows * sbCols) >> (minLog2Tiles + 1) : sbRows * sbCols;
            var maxTileHeightSb = Math.Max(maxTileAreaSb / widestTileSb, 1);
            tileRows = 0;
            for (var startSb = 0; startSb < sbRows;)
            {
                if (tileRows >= Av1Constants.MaxTileRows)
                    throw Av1Errors.Invalid("a frame has more than 64 tile rows.");

                rowStarts[tileRows++] = startSb << sbShift;
                var maxHeight = Math.Min(sbRows - startSb, maxTileHeightSb);
                var sizeSb = _reader.ReadNonSymmetric(maxHeight) + 1;
                startSb += sizeSb;
            }

            header.TileRowsLog2 = TileLog2(1, tileRows);
        }

        colStarts[tileCols] = header.MiCols;
        rowStarts[tileRows] = header.MiRows;
        header.TileCols = tileCols;
        header.TileRows = tileRows;
        header.MiColStarts = colStarts[..(tileCols + 1)].ToArray();
        header.MiRowStarts = rowStarts[..(tileRows + 1)].ToArray();
        if (header.TileColsLog2 > 0 || header.TileRowsLog2 > 0)
        {
            header.ContextUpdateTileId = ReadBits(header.TileRowsLog2 + header.TileColsLog2);
            if (header.ContextUpdateTileId >= tileCols * tileRows)
                throw Av1Errors.Invalid("the tile that updates the contexts does not exist.");

            header.TileSizeBytes = ReadBits(2) + 1;
        }
    }

    // Section 5.9.16: the smallest k such that blkSize << k is greater than or equal to target
    private static int TileLog2(int blockSize, int target)
    {
        var k = 0;
        while ((blockSize << k) < target)
        {
            k++;
        }

        return k;
    }

    // Section 5.9.12 "Quantization params syntax"
    private void ReadQuantizationParams()
    {
        var header = _header;
        header.BaseQIdx = ReadBits(8);
        header.DeltaQYDc = ReadDeltaQ();
        if (_sequence.PlaneCount > 1)
        {
            var diffUvDelta = _sequence.SeparateUvDeltaQ && ReadFlag();
            header.DeltaQUDc = ReadDeltaQ();
            header.DeltaQUAc = ReadDeltaQ();
            if (diffUvDelta)
            {
                header.DeltaQVDc = ReadDeltaQ();
                header.DeltaQVAc = ReadDeltaQ();
            }
            else
            {
                header.DeltaQVDc = header.DeltaQUDc;
                header.DeltaQVAc = header.DeltaQUAc;
            }
        }

        header.UsingQmatrix = ReadFlag();
        if (header.UsingQmatrix)
        {
            header.QmY = ReadBits(4);
            header.QmU = ReadBits(4);
            header.QmV = _sequence.SeparateUvDeltaQ ? ReadBits(4) : header.QmU;
        }
    }

    private int ReadDeltaQ() => ReadFlag() ? _reader.ReadSigned(7) : 0;

    // Section 5.9.14 "Segmentation params syntax"
    private void ReadSegmentationParams()
    {
        var header = _header;
        header.SegmentationEnabled = ReadFlag();
        if (header.SegmentationEnabled)
        {
            if (header.PrimaryRefFrame is Av1Constants.PrimaryRefNone)
            {
                header.SegmentationUpdateMap = true;
                header.SegmentationUpdateData = true;
            }
            else
            {
                header.SegmentationUpdateMap = ReadFlag();
                header.SegmentationTemporalUpdate = header.SegmentationUpdateMap && ReadFlag();
                header.SegmentationUpdateData = ReadFlag();
            }

            if (header.SegmentationUpdateData)
            {
                for (var i = 0; i < Av1Constants.MaxSegments; i++)
                {
                    for (var j = 0; j < Av1Constants.SegLvlMax; j++)
                    {
                        var enabled = ReadFlag();
                        var clippedValue = 0;
                        if (enabled)
                        {
                            var bitsToRead = Av1Tables.SegmentationFeatureBits[j];
                            var limit = Av1Tables.SegmentationFeatureMax[j];
                            clippedValue = Av1Tables.SegmentationFeatureSigned[j] is 1
                                ? Math.Clamp(_reader.ReadSigned(1 + bitsToRead), -limit, limit)
                                : Math.Clamp(ReadBits(bitsToRead), 0, limit);
                        }

                        header.Segmentation.Set(i, j, enabled, clippedValue);
                    }
                }
            }
        }
        else
        {
            header.Segmentation = default;
        }

        for (var i = 0; i < Av1Constants.MaxSegments; i++)
        {
            for (var j = 0; j < Av1Constants.SegLvlMax; j++)
            {
                if (header.Segmentation.IsEnabled(i, j))
                {
                    header.LastActiveSegId = i;
                    if (j >= Av1Constants.SegLvlRefFrame)
                        header.SegIdPreSkip = true;
                }
            }
        }
    }

    // Section 5.9.17 "Quantizer index delta parameters syntax"
    private void ReadDeltaQParams()
    {
        _header.DeltaQPresent = _header.BaseQIdx > 0 && ReadFlag();
        if (_header.DeltaQPresent)
            _header.DeltaQRes = ReadBits(2);
    }

    // Section 5.9.18 "Loop filter delta parameters syntax"
    private void ReadDeltaLfParams()
    {
        var header = _header;
        if (!header.DeltaQPresent)
            return;

        header.DeltaLfPresent = !header.AllowIntrabc && ReadFlag();
        if (header.DeltaLfPresent)
        {
            header.DeltaLfRes = ReadBits(2);
            header.DeltaLfMulti = ReadFlag();
        }
    }

    private void ComputeLossless()
    {
        var header = _header;
        header.CodedLossless = true;
        for (var segmentId = 0; segmentId < Av1Constants.MaxSegments; segmentId++)
        {
            var qindex = header.GetQIndex(segmentId, currentQIndex: -1);
            var lossless = qindex is 0 && header.DeltaQYDc is 0 && header.DeltaQUAc is 0 && header.DeltaQUDc is 0 && header.DeltaQVAc is 0 && header.DeltaQVDc is 0;
            header.LosslessArray[segmentId] = lossless;
            if (!lossless)
                header.CodedLossless = false;
        }

        if (header.CodedLossless && header.DeltaQPresent)
            throw Av1Errors.Invalid("a lossless frame codes quantizer index deltas.");

        header.AllLossless = header.CodedLossless && header.FrameWidth == header.UpscaledWidth;
    }

    // Section 5.9.11 "Loop filter params syntax"
    private void ReadLoopFilterParams()
    {
        var header = _header;
        if (header.CodedLossless || header.AllowIntrabc)
        {
            header.LoopFilterDeltas = Av1LoopFilterDeltas.Default;
            return;
        }

        header.LoopFilterLevel[0] = (byte)ReadBits(6);
        header.LoopFilterLevel[1] = (byte)ReadBits(6);
        if (_sequence.PlaneCount > 1 && (header.LoopFilterLevel[0] is not 0 || header.LoopFilterLevel[1] is not 0))
        {
            header.LoopFilterLevel[2] = (byte)ReadBits(6);
            header.LoopFilterLevel[3] = (byte)ReadBits(6);
        }

        header.LoopFilterSharpness = ReadBits(3);
        header.LoopFilterDeltaEnabled = ReadFlag();
        if (header.LoopFilterDeltaEnabled)
        {
            header.LoopFilterDeltaUpdate = ReadFlag();
            if (header.LoopFilterDeltaUpdate)
            {
                for (var i = 0; i < Av1Constants.TotalRefsPerFrame; i++)
                {
                    if (ReadFlag())
                        header.LoopFilterDeltas.RefDeltas[i] = (sbyte)_reader.ReadSigned(7);
                }

                for (var i = 0; i < 2; i++)
                {
                    if (ReadFlag())
                        header.LoopFilterDeltas.ModeDeltas[i] = (sbyte)_reader.ReadSigned(7);
                }
            }
        }
    }

    // Section 5.9.19 "CDEF params syntax"
    private void ReadCdefParams()
    {
        var header = _header;
        if (header.CodedLossless || header.AllowIntrabc || !_sequence.EnableCdef)
        {
            header.CdefDamping = 3;
            return;
        }

        header.CdefDamping = ReadBits(2) + 3;
        header.CdefBits = ReadBits(2);
        for (var i = 0; i < (1 << header.CdefBits); i++)
        {
            header.CdefYPriStrength[i] = (byte)ReadBits(4);
            var secondary = ReadBits(2);
            header.CdefYSecStrength[i] = (byte)(secondary is 3 ? 4 : secondary);
            if (_sequence.PlaneCount > 1)
            {
                header.CdefUvPriStrength[i] = (byte)ReadBits(4);
                secondary = ReadBits(2);
                header.CdefUvSecStrength[i] = (byte)(secondary is 3 ? 4 : secondary);
            }
        }
    }

    // Section 5.9.20 "Loop restoration params syntax"
    private void ReadLoopRestorationParams()
    {
        var header = _header;
        if (header.AllLossless || header.AllowIntrabc || !_sequence.EnableRestoration)
            return;

        var usesChromaLr = false;
        for (var i = 0; i < _sequence.PlaneCount; i++)
        {
            var type = Av1Tables.RemapLrType[ReadBits(2)];
            header.FrameRestorationType[i] = type;
            if (type is not Av1Constants.RestoreNone)
            {
                header.UsesLr = true;
                if (i > 0)
                    usesChromaLr = true;
            }
        }

        if (!header.UsesLr)
            return;

        var unitShift = ReadBits(1);
        if (_sequence.Use128x128Superblock)
        {
            unitShift++;
        }
        else if (unitShift is not 0)
        {
            unitShift += ReadBits(1);
        }

        header.LoopRestorationSize[0] = Av1Constants.RestorationTilesizeMax >> (2 - unitShift);
        var uvShift = _sequence.SubsamplingX is 1 && _sequence.SubsamplingY is 1 && usesChromaLr ? ReadBits(1) : 0;
        header.LoopRestorationSize[1] = header.LoopRestorationSize[0] >> uvShift;
        header.LoopRestorationSize[2] = header.LoopRestorationSize[0] >> uvShift;
    }

    // Section 5.9.21 "TX mode syntax"
    private void ReadTxMode()
    {
        if (_header.CodedLossless)
        {
            _header.TxMode = Av1Constants.Only4x4;
        }
        else
        {
            _header.TxMode = ReadFlag() ? Av1Constants.TxModeSelect : Av1Constants.TxModeLargest;
        }
    }

    // Section 5.9.22 "Skip mode params syntax"
    private void ReadSkipModeParams()
    {
        var header = _header;
        var skipModeAllowed = false;
        if (!header.FrameIsIntra && header.ReferenceSelect && _sequence.EnableOrderHint)
        {
            var forwardIdx = -1;
            var backwardIdx = -1;
            var forwardHint = 0;
            var backwardHint = 0;
            for (var i = 0; i < Av1Constants.RefsPerFrame; i++)
            {
                var refHint = GetSlot(header.RefFrameIdx[i]).Header.OrderHint;
                if (GetRelativeDistance(refHint, header.OrderHint) < 0)
                {
                    if (forwardIdx < 0 || GetRelativeDistance(refHint, forwardHint) > 0)
                    {
                        forwardIdx = i;
                        forwardHint = refHint;
                    }
                }
                else if (GetRelativeDistance(refHint, header.OrderHint) > 0)
                {
                    if (backwardIdx < 0 || GetRelativeDistance(refHint, backwardHint) < 0)
                    {
                        backwardIdx = i;
                        backwardHint = refHint;
                    }
                }
            }

            if (forwardIdx < 0)
            {
                skipModeAllowed = false;
            }
            else if (backwardIdx >= 0)
            {
                skipModeAllowed = true;
                header.SkipModeFrame[0] = (byte)(Av1Constants.LastFrame + Math.Min(forwardIdx, backwardIdx));
                header.SkipModeFrame[1] = (byte)(Av1Constants.LastFrame + Math.Max(forwardIdx, backwardIdx));
            }
            else
            {
                var secondForwardIdx = -1;
                var secondForwardHint = 0;
                for (var i = 0; i < Av1Constants.RefsPerFrame; i++)
                {
                    var refHint = GetSlot(header.RefFrameIdx[i]).Header.OrderHint;
                    if (GetRelativeDistance(refHint, forwardHint) < 0 && (secondForwardIdx < 0 || GetRelativeDistance(refHint, secondForwardHint) > 0))
                    {
                        secondForwardIdx = i;
                        secondForwardHint = refHint;
                    }
                }

                if (secondForwardIdx >= 0)
                {
                    skipModeAllowed = true;
                    header.SkipModeFrame[0] = (byte)(Av1Constants.LastFrame + Math.Min(forwardIdx, secondForwardIdx));
                    header.SkipModeFrame[1] = (byte)(Av1Constants.LastFrame + Math.Max(forwardIdx, secondForwardIdx));
                }
            }
        }

        header.SkipModePresent = skipModeAllowed && ReadFlag();
    }

    // Section 5.9.24 "Global motion params syntax"
    private void ReadGlobalMotionParams()
    {
        var header = _header;
        header.GlobalMotion = Av1GlobalMotion.Identity;
        if (header.FrameIsIntra)
            return;

        for (var reference = Av1Constants.LastFrame; reference <= Av1Constants.AltrefFrame; reference++)
        {
            int type;
            if (ReadFlag())
            {
                if (ReadFlag())
                {
                    type = Av1Constants.Rotzoom;
                }
                else
                {
                    type = ReadFlag() ? Av1Constants.Translation : Av1Constants.Affine;
                }
            }
            else
            {
                type = Av1Constants.Identity;
            }

            header.GlobalMotion.Types[reference] = (byte)type;
            if (type >= Av1Constants.Rotzoom)
            {
                ReadGlobalParam(type, reference, 2);
                ReadGlobalParam(type, reference, 3);
                if (type is Av1Constants.Affine)
                {
                    ReadGlobalParam(type, reference, 4);
                    ReadGlobalParam(type, reference, 5);
                }
                else
                {
                    header.GlobalMotion.Parameters[reference][4] = -header.GlobalMotion.Parameters[reference][3];
                    header.GlobalMotion.Parameters[reference][5] = header.GlobalMotion.Parameters[reference][2];
                }
            }

            if (type >= Av1Constants.Translation)
            {
                ReadGlobalParam(type, reference, 0);
                ReadGlobalParam(type, reference, 1);
            }
        }
    }

    // Section 5.9.25 "Global param syntax"
    private void ReadGlobalParam(int type, int reference, int index)
    {
        var absBits = Av1Constants.GmAbsAlphaBits;
        var precBits = Av1Constants.GmAlphaPrecBits;
        if (index < 2)
        {
            if (type is Av1Constants.Translation)
            {
                var lowPrecision = _header.AllowHighPrecisionMv ? 0 : 1;
                absBits = Av1Constants.GmAbsTransOnlyBits - lowPrecision;
                precBits = Av1Constants.GmTransOnlyPrecBits - lowPrecision;
            }
            else
            {
                absBits = Av1Constants.GmAbsTransBits;
                precBits = Av1Constants.GmTransPrecBits;
            }
        }

        var precDiff = Av1Constants.WarpedmodelPrecBits - precBits;
        var round = index % 3 is 2 ? 1 << Av1Constants.WarpedmodelPrecBits : 0;
        var sub = index % 3 is 2 ? 1 << precBits : 0;
        var mx = 1 << absBits;
        var r = (_previousGlobalMotion.Parameters[reference][index] >> precDiff) - sub;
        _header.GlobalMotion.Parameters[reference][index] = (DecodeSignedSubexpWithRef(-mx, mx + 1, r) << precDiff) + round;
    }

    // Section 5.9.26 "Decode signed subexp with ref syntax"
    private int DecodeSignedSubexpWithRef(int low, int high, int r)
    {
        var x = DecodeUnsignedSubexpWithRef(high - low, r - low);
        return x + low;
    }

    // Section 5.9.27 "Decode unsigned subexp with ref syntax"
    private int DecodeUnsignedSubexpWithRef(int mx, int r)
    {
        var v = DecodeSubexp(mx);
        if ((r << 1) <= mx)
            return InverseRecenter(r, v);

        return mx - 1 - InverseRecenter(mx - 1 - r, v);
    }

    // Section 5.9.28 "Decode subexp syntax"
    private int DecodeSubexp(int numSyms)
    {
        var i = 0;
        var mk = 0;
        const int K = 3;
        while (true)
        {
            var b2 = i is not 0 ? K + i - 1 : K;
            var a = 1 << b2;
            if (numSyms <= mk + (3 * a))
                return _reader.ReadNonSymmetric(numSyms - mk) + mk;

            if (!ReadFlag())
                return ReadBits(b2) + mk;

            i++;
            mk += a;
        }
    }

    // Section 5.9.29 "Inverse recenter function"
    private static int InverseRecenter(int r, int v)
    {
        if (v > 2 * r)
            return v;

        if ((v & 1) is not 0)
            return r - ((v + 1) >> 1);

        return r + (v >> 1);
    }

    // Section 5.9.30 "Film grain params syntax"
    private void ReadFilmGrainParams()
    {
        var header = _header;
        var sequence = _sequence;
        if (!sequence.FilmGrainParamsPresent || (!header.ShowFrame && !header.ShowableFrame))
            return;

        if (!ReadFlag())
            return;

        var grainSeed = ReadBits(16);
        var updateGrain = header.FrameType is not Av1Constants.InterFrame || ReadFlag();
        if (!updateGrain)
        {
            var refIndex = ReadBits(3);
            var isReference = false;
            for (var i = 0; i < Av1Constants.RefsPerFrame; i++)
            {
                isReference |= header.RefFrameIdx[i] == refIndex;
            }

            if (!isReference)
                throw Av1Errors.Invalid("the film grain parameters are loaded from a frame that is not a reference of the frame.");

            // load_grain_params: every parameter of the reference frame, including the absence of grain
            header.FilmGrain = GetSlot(refIndex).Header.FilmGrain?.WithSeed(grainSeed);
            return;
        }

        var numYPoints = ReadBits(4);
        if (numYPoints > Av1FilmGrainParameters.MaxLumaPoints)
            throw Av1Errors.Invalid("the film grain has more than 14 luma points.");

        ReadGrainPoints(numYPoints, out var pointYValue, out var pointYScaling);
        var chromaScalingFromLuma = !sequence.Monochrome && ReadFlag();
        var numCbPoints = 0;
        var numCrPoints = 0;
        byte[] pointCbValue = [];
        byte[] pointCbScaling = [];
        byte[] pointCrValue = [];
        byte[] pointCrScaling = [];
        var is420 = sequence.SubsamplingX is 1 && sequence.SubsamplingY is 1;
        if (!sequence.Monochrome && !chromaScalingFromLuma && !(is420 && numYPoints is 0))
        {
            numCbPoints = ReadBits(4);
            if (numCbPoints > Av1FilmGrainParameters.MaxChromaPoints)
                throw Av1Errors.Invalid("the film grain has more than 10 chroma points.");

            ReadGrainPoints(numCbPoints, out pointCbValue, out pointCbScaling);
            numCrPoints = ReadBits(4);
            if (numCrPoints > Av1FilmGrainParameters.MaxChromaPoints)
                throw Av1Errors.Invalid("the film grain has more than 10 chroma points.");

            ReadGrainPoints(numCrPoints, out pointCrValue, out pointCrScaling);
            if (is420 && (numCbPoints is 0) != (numCrPoints is 0))
                throw Av1Errors.Invalid("the film grain applies to a single chroma plane of a 4:2:0 frame.");
        }

        var grainScaling = ReadBits(2) + 8;
        var arCoeffLag = ReadBits(2);
        var numPosLuma = 2 * arCoeffLag * (arCoeffLag + 1);
        var numPosChroma = numYPoints is not 0 ? numPosLuma + 1 : numPosLuma;
        var arCoeffsY = numYPoints is not 0 ? ReadGrainCoefficients(numPosLuma) : [];
        var arCoeffsCb = chromaScalingFromLuma || numCbPoints is not 0 ? ReadGrainCoefficients(numPosChroma) : [];
        var arCoeffsCr = chromaScalingFromLuma || numCrPoints is not 0 ? ReadGrainCoefficients(numPosChroma) : [];
        var arCoeffShift = ReadBits(2) + 6;
        var grainScaleShift = ReadBits(2);
        var cbMult = 0;
        var cbLumaMult = 0;
        var cbOffset = 0;
        if (numCbPoints is not 0)
        {
            cbMult = ReadBits(8);
            cbLumaMult = ReadBits(8);
            cbOffset = ReadBits(9);
        }

        var crMult = 0;
        var crLumaMult = 0;
        var crOffset = 0;
        if (numCrPoints is not 0)
        {
            crMult = ReadBits(8);
            crLumaMult = ReadBits(8);
            crOffset = ReadBits(9);
        }

        header.FilmGrain = new Av1FilmGrainParameters
        {
            GrainSeed = grainSeed,
            NumYPoints = numYPoints,
            PointYValue = pointYValue,
            PointYScaling = pointYScaling,
            ChromaScalingFromLuma = chromaScalingFromLuma,
            NumCbPoints = numCbPoints,
            PointCbValue = pointCbValue,
            PointCbScaling = pointCbScaling,
            NumCrPoints = numCrPoints,
            PointCrValue = pointCrValue,
            PointCrScaling = pointCrScaling,
            GrainScaling = grainScaling,
            ArCoeffLag = arCoeffLag,
            ArCoeffsY = arCoeffsY,
            ArCoeffsCb = arCoeffsCb,
            ArCoeffsCr = arCoeffsCr,
            ArCoeffShift = arCoeffShift,
            GrainScaleShift = grainScaleShift,
            CbMult = cbMult,
            CbLumaMult = cbLumaMult,
            CbOffset = cbOffset,
            CrMult = crMult,
            CrLumaMult = crLumaMult,
            CrOffset = crOffset,
            OverlapFlag = ReadFlag(),
            ClipToRestrictedRange = ReadFlag(),
        };
    }

    private void ReadGrainPoints(int count, out byte[] values, out byte[] scalings)
    {
        values = new byte[count];
        scalings = new byte[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = (byte)ReadBits(8);
            if (i > 0 && values[i] <= values[i - 1])
                throw Av1Errors.Invalid("the points of a film grain scaling function are not in increasing order.");

            scalings[i] = (byte)ReadBits(8);
        }
    }

    private sbyte[] ReadGrainCoefficients(int count)
    {
        var coefficients = new sbyte[count];
        for (var i = 0; i < count; i++)
        {
            coefficients[i] = (sbyte)(ReadBits(8) - 128);
        }

        return coefficients;
    }
}
