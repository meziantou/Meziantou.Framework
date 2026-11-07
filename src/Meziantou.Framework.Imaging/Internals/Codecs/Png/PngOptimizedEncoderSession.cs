using System.IO.Compression;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The encoding state of a static PNG or an APNG written with <see cref="PngOptimization.MinimumSize"/>: the candidate
/// encodings are compressed to a byte counter (<see cref="PngImageDataEncoder.StartSource"/>) and the smallest is written.
/// It also writes the still images of <see cref="PngOptimization.Layout"/>, which only chooses the layout, from the analysis
/// alone: the candidate with the smallest unfiltered data, compressed once with the filter strategy and the level of the
/// encoder.
/// </summary>
/// <remarks>
/// <para>
/// <b>Layout.</b> <see cref="ValidateFrame"/> analyzes every image given before the header (<see cref="PngLayoutAnalysis"/>):
/// every image of an eager save, the first image of a sequential writer. The first operation then compresses every known
/// image (<see cref="PlanFrames"/> gives the images of an eager save) with each candidate layout, with the filter strategy
/// that suits the layout, and keeps the layout of the smallest total (its <c>PLTE</c> and <c>tRNS</c> chunks included).
/// The frames of an eager save are compressed as the regions that differ from the previous frame, with SOURCE and, when
/// the layout has a transparent value and it is exact, OVER; the smallest counts. This is how the layouts that only add a
/// transparent value for the OVER frames (an unused color key, a transparent palette entry) are weighed.
/// Later frames are checked against it (<see cref="PngOptimizedLayout.FindMismatch"/>).
/// </para>
/// <para>
/// <b>Images.</b> A still image, the poster and frame zero without a poster cover the canvas: their filter strategy is the
/// smallest of the strategies allowed by <see cref="PngEncoder.Filter"/>. An APNG frame is a region of the canvas, and the
/// region, its blending and the disposal of the previous frame are chosen first: for each disposal (NONE, BACKGROUND, and
/// PREVIOUS unless the previous frame is frame zero, for which the specification makes it BACKGROUND), the region is the
/// bounding box of the pixels that differ from the canvas the disposal leaves, and OVER blending is possible when every
/// such pixel is opaque, or not fully transparent on a fully transparent canvas pixel (both give the source exactly), and
/// the layout has a transparent value for the unchanged pixels. The variants are compressed with the filter strategy of
/// the layout, then the other strategies with the smallest variant. Frame zero after a poster is drawn on the transparent
/// black canvas. An unchanged frame is a 1x1 region.
/// </para>
/// <para>
/// <b>Deferral.</b> The <c>fcTL</c> of a frame holds its disposal, which is chosen with the next frame: a frame that is not
/// the last one is written during the operation of the next frame, from private copies of wide pixels (<see cref="PngCanvas"/>):
/// the displayed frame, and the canvas it is drawn on (needed by PREVIOUS and by OVER). The last frame, the poster and a still
/// image are written in their own operation.
/// </para>
/// <para>
/// Every step does a bounded amount of work per <see cref="Encode"/> call: a band of scanlines of one candidate, of the
/// comparison of a frame with the canvas, or of a copy, so that the writer checks cancellation between them.
/// </para>
/// </remarks>
internal sealed class PngOptimizedEncoderSession : ImageEncoderSession
{
    private static readonly PngFilter[] AllFilters = [PngFilter.None, PngFilter.Sub, PngFilter.Up, PngFilter.Average, PngFilter.Paeth, PngFilter.Adaptive];

    // The zlib levels of the last search stage: the deflater of a lower level is sometimes smaller (other match strategies)
    private static readonly CompressionLevel[] Levels = [CompressionLevel.SmallestSize, CompressionLevel.Optimal, CompressionLevel.Fastest];

    private readonly PngEncoder _encoder;
    private readonly bool _layoutOnly;
    private readonly bool _animated;
    private readonly int _expectedFrameCount;
    private readonly bool _isWide16;
    private readonly PixelFormat _wideFormat;
    private readonly Rectangle _canvasRegion;
    private readonly PngFilter[] _filters;
    private readonly PngMetadataWriter _metadata;
    private readonly List<Trial> _trials = [];
    private readonly Diff[] _diffs = new Diff[3];
    private PngLayoutAnalysis? _analysis;
    private PngIndexedRows? _palette;
    private PngFrameRows? _rows;
    private PngFrameRows? _referenceRows;
    private (Rectangle Region, bool OverExact)[]? _plannedRegions;
    private PngRegionScanlines? _scanlines;
    private PngImageDataEncoder? _data;
    private PngCanvas? _displayed;
    private PngCanvas? _canvas;
    private PngOptimizedLayout? _layout;
    private List<PngOptimizedLayout>? _candidates;
    private ImageFrame? _plannedPoster;
    private IReadOnlyList<ImageFrame>? _plannedFrames;

    private Step _step;
    private ImageFrame? _frame;
    private bool _isPoster;
    private int _index;
    private bool _lastImage;
    private (ushort Numerator, ushort Denominator)? _delay;
    private int _metadataIndex;
    private bool _headerWritten;
    private bool _posterWritten;
    private uint _sequenceNumber;
    private int _trialIndex;
    private bool _trialStarted;
    private int _diffCount;
    private int _row;
    private bool _writeStarted;
    private FramePlan _plan;
    private PendingFrame? _pending;

    public PngOptimizedEncoderSession(ImageEncoderSessionOptions options)
        : base(options)
    {
        _encoder = (PngEncoder)options.Encoder;
        _animated = options.Capabilities.IsAnimated;
        _layoutOnly = _encoder.Optimization == PngOptimization.Layout;
        if (_layoutOnly && _animated)
            throw new ArgumentException("PngOptimization.Layout only optimizes still images.", nameof(options));

        _expectedFrameCount = options.ExpectedFrameCount ?? throw new ArgumentException("PNG output requires an expected frame count.", nameof(options));
        if (!_animated && _expectedFrameCount != 1)
            throw new ArgumentException("A static PNG has a single frame.", nameof(options));

        _isWide16 = PixelFormats.GetBitsPerComponent(options.PixelFormat) == 16;
        _wideFormat = _isWide16 ? PixelFormat.Rgba64 : PixelFormat.Rgba32;
        _canvasRegion = new Rectangle(0, 0, options.CanvasSize.Width, options.CanvasSize.Height);
        _filters = _encoder.Filter == PngFilter.Adaptive ? AllFilters : [_encoder.Filter];
        _metadata = new PngMetadataWriter(options.Metadata, _layoutOnly ? _encoder.CompressionLevel : CompressionLevel.SmallestSize, options.Scope);
        var width = options.CanvasSize.Width;
        try
        {
            // The colors of the opaque pixels serve the tRNS color key, which the layout analysis alone does not weigh
            var trackOpaqueColors = !_layoutOnly && (PixelFormats.HasAlpha(options.PixelFormat) || (_animated && _expectedFrameCount > 1));
            _analysis = new PngLayoutAnalysis(options.Scope, _isWide16, width, trackOpaqueColors, PixelFormats.HasAlpha(options.PixelFormat));
            _rows = new PngFrameRows(options.Scope, _wideFormat, width);
            if (_animated && _expectedFrameCount > 1)
            {
                _referenceRows = new PngFrameRows(options.Scope, _wideFormat, width);
            }
            _scanlines = new PngRegionScanlines(options.Scope, width);

            // The longest scanline is the one of a full row of RGBA samples of the wide bit depth
            var maxScanlineLength = (long)width * PixelFormats.GetBytesPerPixel(_wideFormat);
            if (maxScanlineLength + 1 > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(options.Scope.Limits);

            _data = new PngImageDataEncoder(options.Scope, (int)maxScanlineLength, _encoder.Interlaced);
        }
        catch
        {
            DisposeState();
            throw;
        }
    }

    private enum Step
    {
        None,
        LayoutTrial,
        Header,
        Metadata,
        Compare,
        VariantTrials,
        FilterTrials,
        WritePending,
        WriteImage,
        Copy,
        Trailer,
    }

    public override void ValidateFrame(ImageFrame frame, bool isPoster)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (_animated && !isPoster)
        {
            _ = AnimationTiming.ToApngDelay(frame.Metadata.Duration, _encoder.DurationRounding);
        }

        var rows = _rows ?? throw new InvalidOperationException("Every image of the PNG output was already written.");
        rows.SetFrame(frame);
        try
        {
            if (_layout is null)
            {
                var status = _analysis!.Add(rows, _encoder.PixelLayout == PngPixelLayout.Indexed, out var x, out var y);
                if (status != PngIndexedRowStatus.Ok)
                    throw CreateColorException(status, x, y);

                return;
            }

            for (var y = 0; y < rows.Height; y++)
            {
                var mismatch = _layout.FindMismatch(rows.GetRow(y), out var x);
                if (mismatch != PngLayoutMismatch.None)
                    throw CreateMismatchException(mismatch, x, y);
            }
        }
        finally
        {
            rows.SetFrame(null);
        }
    }

    public override void PlanFrames(ImageFrame? poster, IReadOnlyList<ImageFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (_layout is not null)
            return;

        _plannedPoster = poster;
        _plannedFrames = frames;
        if (_referenceRows is not { } previous || frames.Count < 2)
            return;

        // The layout trial compresses the frames as the regions that differ from the previous frame (the canvas the NONE
        // disposal leaves), with SOURCE and, where it is exact, OVER: what the frames cost with each candidate, the color key
        // or the transparent palette entry that only OVER frames use included
        var rows = _rows!;
        _plannedRegions = new (Rectangle Region, bool OverExact)[frames.Count];
        try
        {
            for (var i = 0; i < frames.Count; i++)
            {
                if (i == 0 && poster is null)
                {
                    _plannedRegions[i] = (_canvasRegion, false);
                    continue;
                }

                rows.SetFrame(frames[i]);
                previous.SetFrame(i == 0 ? null : frames[i - 1]);
                var diff = new Diff(new PngCanvasReference(i == 0 ? null : previous, Rectangle.Empty), PngFrameControl.DisposeNone);
                for (var y = 0; y < rows.Height; y++)
                {
                    CompareRow(rows.GetRow(y), y, ref diff);
                }

                _plannedRegions[i] = (diff.GetRegion(), diff.OverExact && i > 0);
            }
        }
        finally
        {
            rows.SetFrame(null);
            previous.SetFrame(null);
        }
    }

    public override void BeginPosterFrame(ImageFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!_animated)
            throw new InvalidOperationException("A static PNG has no poster frame.");

        if (_posterWritten || _headerWritten)
            throw new InvalidOperationException("The APNG poster frame is written once, before frame zero.");

        _posterWritten = true;
        Begin(frame, isPoster: true, index: -1, delay: null, lastImage: false);
    }

    public override void BeginFrame(ImageFrame frame, int index)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (index < 0 || index >= _expectedFrameCount)
            throw new InvalidOperationException(_animated ? "The frame index exceeds the expected frame count." : "A static PNG has a single frame.");

        var delay = _animated ? AnimationTiming.ToApngDelay(frame.Metadata.Duration, _encoder.DurationRounding) : ((ushort, ushort)?)null;
        Begin(frame, isPoster: false, index, delay, lastImage: index == _expectedFrameCount - 1);
    }

    public override void BeginComplete(int frameCount)
    {
        if (frameCount != _expectedFrameCount)
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"{frameCount} frame(s) were written but the output declares {_expectedFrameCount}."));

        _step = Step.Trailer;
    }

    public override bool Encode(ImageOutputBuffer output)
    {
        ArgumentNullException.ThrowIfNull(output);
        switch (_step)
        {
            case Step.LayoutTrial:
                if (!RunLayoutTrial())
                    return false;

                _step = Step.Header;
                return false;

            case Step.Header:
                PngChunkWriter.WriteSignature(output);
                PngEncoderCodec.WriteHeader(output, Options.CanvasSize, _layout!.BitDepth, _layout.ColorType, _encoder.Interlaced);
                if (_animated)
                {
                    PngEncoderCodec.WriteAnimationControl(output, _expectedFrameCount, Options.Animation?.TotalPlays);
                }

                _headerWritten = true;
                _step = Step.Metadata;
                return false;

            case Step.Metadata:
                if (_metadataIndex < _metadata.Count)
                {
                    _metadata.Write(_metadataIndex++, output);
                    return false;
                }

                // PLTE and tRNS follow every metadata chunk (iCCP must precede PLTE) and precede the first image
                _layout!.WriteAncillaryChunks(output);
                StartPlan();
                return false;

            case Step.Compare:
                if (!RunCompare())
                    return false;

                StartVariantTrials();
                return false;

            case Step.VariantTrials:
                if (!RunTrials())
                    return false;

                StartFilterTrials();
                return false;

            case Step.FilterTrials:
                if (!RunTrials())
                    return false;

                EndPlan();
                return false;

            case Step.WritePending:
                if (!WritePending(output))
                    return false;

                _step = WritesNow ? Step.WriteImage : Step.Copy;
                return false;

            case Step.WriteImage:
                if (!WriteImage(output))
                    return false;

                EndOperation();
                if (_lastImage)
                {
                    DisposeState();
                }

                return true;

            case Step.Copy:
                if (!Copy())
                    return false;

                EndOperation();
                return true;

            case Step.Trailer:
                PngChunkWriter.Write(output, PngChunkWriter.Iend, []);
                _step = Step.None;
                return true;

            default:
                throw new InvalidOperationException("No PNG encoding operation is in progress.");
        }
    }

    protected override void Dispose(bool disposing)
    {
        DisposeState();
        base.Dispose(disposing);
    }

    /// <summary>Gets a value indicating whether the image of the operation is written in its own operation (not deferred).</summary>
    private bool WritesNow => !_animated || _isPoster || _lastImage;

    /// <summary>Gets the region of the deferred frame, or an empty rectangle.</summary>
    private Rectangle PreviousRegion => _pending?.Region ?? Rectangle.Empty;

    private static PngFilter GetNaturalFilter(PngOptimizedLayout layout, PngFilter[] filters)
        => filters.Length == 1 ? filters[0] : layout.SuitsAdaptiveFilter ? PngFilter.Adaptive : PngFilter.None;

    private void Begin(ImageFrame frame, bool isPoster, int index, (ushort Numerator, ushort Denominator)? delay, bool lastImage)
    {
        if (_data is null)
            throw new InvalidOperationException("Every image of the PNG output was already written.");

        _frame = frame;
        _isPoster = isPoster;
        _index = index;
        _delay = delay;
        _lastImage = lastImage;
        _step = Step.LayoutTrial;
        if (_headerWritten)
        {
            StartPlan();
        }
    }

    private void EndOperation()
    {
        _frame = null;
        _rows?.SetFrame(null);
        _step = Step.None;
    }

    /// <summary>Compresses the known images with every candidate layout, one band per call, and keeps the smallest.</summary>
    /// <returns><see langword="true"/> once the layout is chosen.</returns>
    private bool RunLayoutTrial()
    {
        if (_candidates is null)
        {
            var paletteOnly = _encoder.PixelLayout == PngPixelLayout.Indexed;
            var addTransparency = _animated && _expectedFrameCount > 1;
            var profile = Options.Metadata.IccProfile;
            var allowGray = profile is null || ColorProfileCompatibility.IsCompatible(profile, PixelFormat.Gray8);
            var allowColor = profile is null || ColorProfileCompatibility.IsCompatible(profile, PixelFormat.Rgb24);
            // The frames of an animation are regions of the canvas, whose packed samples some decoders (FFmpeg, for one)
            // misplace: an animation uses 8-bit samples at least
            _candidates = _analysis!.CreateCandidates(paletteOnly, addTransparency, allImagesKnown: _plannedFrames is not null, allowGray, allowColor, allowPackedSamples: !addTransparency);
            if (_layoutOnly)
            {
                _candidates = [GetSmallestUnfiltered(_candidates)];
            }

            // The layout of the pixel format, which the encoder writes without optimization: with the filter strategy and
            // the level of the default encoder among the trials, a still image is never larger than that output
            var unoptimized = PngOptimizedLayout.ForPixelFormat(Options.PixelFormat);
            if (!paletteOnly && !_layoutOnly && !_candidates.Exists(candidate => candidate.IsSameAs(unoptimized)))
            {
                _candidates.Add(unoptimized);
            }

            if (_candidates.Count > 1)
            {
                _trials.Clear();
                for (var candidate = 0; candidate < _candidates.Count; candidate++)
                {
                    AddLayoutTrials(candidate);
                }

                _trialIndex = 0;
            }
        }

        if (_candidates.Count > 1 && !RunTrials())
            return false;

        // The total of each candidate: its chunks, and the smallest trial of each image
        var best = 0;
        var bestSize = long.MaxValue;
        for (var candidate = 0; candidate < _candidates.Count && _candidates.Count > 1; candidate++)
        {
            var size = _candidates[candidate].GetAncillaryBytes();
            var group = -1;
            var groupSize = 0L;
            foreach (var trial in _trials)
            {
                if (trial.Tag != candidate)
                    continue;

                if (trial.Group != group)
                {
                    size += group < 0 ? 0 : groupSize;
                    (group, groupSize) = (trial.Group, trial.Size);
                }
                else
                {
                    groupSize = Math.Min(groupSize, trial.Size);
                }
            }

            size += groupSize;
            if (size < bestSize)
            {
                (best, bestSize) = (candidate, size);
            }
        }

        _layout = _candidates[best];
        _palette = _analysis!.TakePalette(_layout.Palette);
        _candidates = null;
        _plannedPoster = null;
        _plannedFrames = null;
        _plannedRegions = null;
        _trials.Clear();
        _analysis.Dispose();
        _analysis = null;
        _referenceRows?.Dispose();
        _referenceRows = null;
        return true;
    }

    /// <summary>
    /// Gets the candidate of <see cref="PngOptimization.Layout"/>: the smallest unfiltered data (a filter type byte and
    /// the samples of each row) with the <c>PLTE</c> and <c>tRNS</c> chunks it adds; the first one on a tie, which puts
    /// the layouts without a palette and the smaller bit depths first.
    /// </summary>
    private PngOptimizedLayout GetSmallestUnfiltered(List<PngOptimizedLayout> candidates)
    {
        var size = Options.CanvasSize;
        var best = candidates[0];
        var bestBytes = long.MaxValue;
        foreach (var candidate in candidates)
        {
            var bytes = ((long)size.Height * (1 + candidate.GetScanlineLength(size.Width))) + candidate.GetAncillaryBytes();
            if (bytes < bestBytes)
            {
                (best, bestBytes) = (candidate, bytes);
            }
        }

        return best;
    }

    /// <summary>Adds the trials of a candidate layout: one group per known image, whose smallest trial counts.</summary>
    private void AddLayoutTrials(int candidate)
    {
        var layout = _candidates![candidate];
        var filter = GetNaturalFilter(layout, _filters);
        if (_plannedFrames is null)
        {
            AddLevels(new Trial(layout, _frame!, _canvasRegion, over: null, filter, tag: candidate) { Group = 0 });
            return;
        }

        var group = 0;
        if (_plannedPoster is not null)
        {
            AddLevels(new Trial(layout, _plannedPoster, _canvasRegion, over: null, filter, tag: candidate) { Group = group++ });
        }

        for (var i = 0; i < _plannedFrames.Count; i++)
        {
            var (region, overExact) = _plannedRegions?[i] ?? (_canvasRegion, false);
            AddLevels(new Trial(layout, _plannedFrames[i], region, over: null, filter, tag: candidate) { Group = group });
            if (overExact && layout.HasTransparentValue)
            {
                _trials.Add(new Trial(layout, _plannedFrames[i], region, over: null, filter, tag: candidate) { Group = group, PreviousImage = _plannedFrames[i - 1] });
            }

            group++;
        }
    }

    /// <summary>Adds a layout trial at SmallestSize, and again at Optimal, the level of the default encoder.</summary>
    private void AddLevels(Trial trial)
    {
        _trials.Add(trial);
        _trials.Add(new Trial(trial.Layout, trial.Image, trial.Region, trial.Over, trial.Filter, trial.Tag, CompressionLevel.Optimal) { Group = trial.Group, PreviousImage = trial.PreviousImage });
    }

    /// <summary>Starts the choice of the encoding of the image of the operation.</summary>
    private void StartPlan()
    {
        _rows!.SetFrame(_frame);
        var coversCanvas = !_animated || _isPoster || (_index == 0 && !_posterWritten);
        if (coversCanvas)
        {
            _plan = new FramePlan(_canvasRegion, Over: null, GetNaturalFilter(_layout!, _filters), PngFrameControl.DisposeNone, Rectangle.Empty, HasSize: false, Size: 0);
            if (_layoutOnly)
            {
                // Compressed once, with the settings of the encoder
                _plan = _plan with { Level = _encoder.CompressionLevel };
                EndPlan();
                return;
            }

            StartFilterTrials();
            return;
        }

        // The canvases the disposal of the previous frame can leave
        _diffCount = 0;
        if (_pending is not { } pending)
        {
            // Frame zero after a poster: the canvas is transparent black
            _diffs[_diffCount++] = new Diff(new PngCanvasReference(Canvas: null, Rectangle.Empty), PngFrameControl.DisposeNone);
        }
        else
        {
            _diffs[_diffCount++] = new Diff(new PngCanvasReference(_displayed, Rectangle.Empty), PngFrameControl.DisposeNone);
            _diffs[_diffCount++] = new Diff(new PngCanvasReference(_displayed, pending.Region), PngFrameControl.DisposeBackground);
            if (pending.Index > 0)
            {
                _diffs[_diffCount++] = new Diff(new PngCanvasReference(_canvas, Rectangle.Empty), PngFrameControl.DisposePrevious);
            }
        }

        _row = 0;
        _step = Step.Compare;
    }

    /// <summary>Compares a band of rows of the frame with each canvas: the bounding box of the pixels that differ, and whether OVER is exact.</summary>
    /// <returns><see langword="true"/> once every row is compared.</returns>
    private bool RunCompare()
    {
        var rows = _rows!;
        long processed = 0;
        while (_row < rows.Height)
        {
            if (processed >= PngImageDataEncoder.BandBytes)
                return false;

            var y = _row++;
            var row = rows.GetRow(y);
            processed += row.Length;
            for (var d = 0; d < _diffCount; d++)
            {
                CompareRow(row, y, ref _diffs[d]);
            }
        }

        return true;
    }

    /// <summary>Compares a row of a frame with the same row of a canvas: the pixels that differ, and whether OVER stays exact.</summary>
    private void CompareRow(ReadOnlySpan<byte> row, int y, ref Diff diff)
    {
        var bytesPerPixel = PixelFormats.GetBytesPerPixel(_wideFormat);
        var alphaOffset = _isWide16 ? 6 : 3;
        var alphaBytes = _isWide16 ? 2 : 1;
        var reference = diff.Reference.GetRow(y, out var clearedStart, out var clearedEnd);
        var width = row.Length / bytesPerPixel;
        for (var x = 0; x < width; x++)
        {
            var pixel = row.Slice(x * bytesPerPixel, bytesPerPixel);
            var transparentCanvas = x >= clearedStart && x < clearedEnd;
            var canvasPixel = transparentCanvas ? default : reference.Slice(x * bytesPerPixel, bytesPerPixel);
            if (transparentCanvas ? !pixel.ContainsAnyExcept((byte)0) : pixel.SequenceEqual(canvasPixel))
                continue;

            diff.Include(x, y);
            if (!diff.OverExact)
                continue;

            // OVER gives the source exactly for an opaque source, or for a visible source on a transparent canvas
            var alpha = pixel.Slice(alphaOffset, alphaBytes);
            var opaque = !alpha.ContainsAnyExcept(byte.MaxValue);
            var visible = alpha.ContainsAnyExcept((byte)0);
            var canvasTransparent = transparentCanvas || !canvasPixel.Slice(alphaOffset, alphaBytes).ContainsAnyExcept((byte)0);
            if (!opaque && !(visible && canvasTransparent))
            {
                diff.OverExact = false;
            }
        }
    }

    private void StartVariantTrials()
    {
        var layout = _layout!;
        var filter = GetNaturalFilter(layout, _filters);
        var firstFrame = _pending is null;
        _trials.Clear();
        for (var d = 0; d < _diffCount; d++)
        {
            var diff = _diffs[d];
            var region = diff.GetRegion();
            _trials.Add(new Trial(layout, _frame!, region, over: null, filter, tag: d));

            // The first frame is composited with SOURCE whatever its blend_op
            if (diff.OverExact && layout.HasTransparentValue && !firstFrame)
            {
                _trials.Add(new Trial(layout, _frame!, region, diff.Reference, filter, tag: d));
            }
        }

        _trialIndex = 0;
        if (_trials.Count == 1)
        {
            var only = _trials[0];
            _plan = new FramePlan(only.Region, only.Over, filter, _diffs[only.Tag].DisposeOp, PreviousRegion, HasSize: false, Size: 0);
            StartFilterTrials();
            return;
        }

        _step = Step.VariantTrials;
    }

    /// <summary>Chooses the variant (when variants were compressed), then starts the compression of the other filter strategies.</summary>
    private void StartFilterTrials()
    {
        if (_step == Step.VariantTrials)
        {
            var best = _trials[0];
            foreach (var trial in _trials)
            {
                if (trial.Size < best.Size)
                {
                    best = trial;
                }
            }

            _plan = new FramePlan(best.Region, best.Over, best.Filter, _diffs[best.Tag].DisposeOp, PreviousRegion, HasSize: true, best.Size);
        }

        _trials.Clear();
        foreach (var filter in _filters)
        {
            foreach (var level in Levels)
            {
                if (!_plan.HasSize || filter != _plan.Filter || level != _plan.Level)
                {
                    _trials.Add(new Trial(_layout!, _frame!, _plan.Region, _plan.Over, filter, tag: 0, level));
                }
            }
        }

        _trialIndex = 0;
        if (_trials.Count <= 1 && !_plan.HasSize)
        {
            // A single candidate: nothing to compare
            _trials.Clear();
            EndPlan();
            return;
        }

        _step = Step.FilterTrials;
    }

    private void EndPlan()
    {
        foreach (var trial in _trials)
        {
            if (!_plan.HasSize || trial.Size < _plan.Size)
            {
                _plan = _plan with { Filter = trial.Filter, Level = trial.Level, HasSize = true, Size = trial.Size };
            }
        }

        _trials.Clear();
        _writeStarted = false;
        _row = 0;
        _step = _pending is not null ? Step.WritePending : WritesNow ? Step.WriteImage : Step.Copy;
    }

    /// <summary>Runs one band of the candidate encodings.</summary>
    /// <returns><see langword="true"/> once every candidate is counted.</returns>
    private bool RunTrials()
    {
        if (_trialIndex >= _trials.Count)
            return true;

        var trial = _trials[_trialIndex];
        var data = _data!;
        if (!_trialStarted)
        {
            _rows!.SetFrame(trial.Image);
            var over = trial.Over;
            if (trial.PreviousImage is { } previous)
            {
                _referenceRows!.SetFrame(previous);
                over = new PngCanvasReference(_referenceRows, Rectangle.Empty);
            }

            _scanlines!.Set(trial.Layout, _rows, trial.Region, over);
            data.StartSource(_scanlines, trial.Filter, trial.Level, frameData: false, sequenceNumber: 0, count: true);
            _trialStarted = true;
        }

        if (!data.EncodeSource(output: null))
            return false;

        trial.Size = data.CountedBytes;
        _trialStarted = false;
        _trialIndex++;
        if (_trialIndex == _trials.Count)
        {
            // The rows of the image of the operation are read next
            _rows!.SetFrame(_frame);
        }

        return _trialIndex == _trials.Count;
    }

    /// <summary>Writes the deferred frame: its <c>fcTL</c> with the disposal chosen for this frame, then its data.</summary>
    private bool WritePending(ImageOutputBuffer output)
    {
        var pending = _pending!;
        var data = _data!;
        if (!_writeStarted)
        {
            PngEncoderCodec.WriteFrameControl(output, _sequenceNumber++, pending.Region, pending.Delay, _plan.PreviousDispose, pending.Over ? PngFrameControl.BlendOver : PngFrameControl.BlendSource);
            _scanlines!.Set(_layout!, _displayed!, pending.Region, pending.Over ? new PngCanvasReference(_canvas, Rectangle.Empty) : null);
            StartData(data, pending.FrameData, pending.Filter, pending.Level);
            _writeStarted = true;
        }

        if (!data.EncodeSource(output))
            return false;

        EndData(data, pending.FrameData);
        return true;
    }

    /// <summary>Writes the image of the operation from its frame.</summary>
    private bool WriteImage(ImageOutputBuffer output)
    {
        var data = _data!;
        var frameData = _animated && !_isPoster && (_index > 0 || _posterWritten);
        if (!_writeStarted)
        {
            if (_delay is { } delay)
            {
                // The last frame: its disposal does not matter
                PngEncoderCodec.WriteFrameControl(output, _sequenceNumber++, _plan.Region, delay, PngFrameControl.DisposeNone, _plan.Over is null ? PngFrameControl.BlendSource : PngFrameControl.BlendOver);
            }

            _rows!.SetFrame(_frame);
            _scanlines!.Set(_layout!, _rows, _plan.Region, _plan.Over);
            StartData(data, frameData, _plan.Filter, _plan.Level);
            _writeStarted = true;
        }

        if (!data.EncodeSource(output))
            return false;

        EndData(data, frameData);
        return true;
    }

    private void StartData(PngImageDataEncoder data, bool frameData, PngFilter filter, CompressionLevel level)
    {
        if (frameData)
        {
            PngEncoderCodec.EnsureSequenceNumber(_sequenceNumber);
        }

        data.StartSource(_scanlines!, filter, level, frameData, _sequenceNumber, count: false);
    }

    private void EndData(PngImageDataEncoder data, bool frameData)
    {
        if (frameData)
        {
            _sequenceNumber = data.NextSequenceNumber;
        }

        _writeStarted = false;
        _pending = null;
    }

    /// <summary>
    /// Applies the disposal chosen for the previous frame to the private canvas, then copies the frame of the operation, a
    /// band of rows per call, and defers it.
    /// </summary>
    private bool Copy()
    {
        if (_row == 0)
        {
            _displayed ??= new PngCanvas(Options.Scope, _wideFormat, _canvasRegion.Width, _canvasRegion.Height);
            _canvas ??= new PngCanvas(Options.Scope, _wideFormat, _canvasRegion.Width, _canvasRegion.Height);
            if (_index > 0)
            {
                // The canvas of this frame: the previous frame (NONE), the previous frame with its region cleared
                // (BACKGROUND), or the canvas of the previous frame (PREVIOUS); frame zero is drawn on transparent black
                switch (_plan.PreviousDispose)
                {
                    case PngFrameControl.DisposeNone:
                        (_displayed, _canvas) = (_canvas, _displayed);
                        break;

                    case PngFrameControl.DisposeBackground:
                        _displayed.Clear(_plan.PreviousRegion);
                        (_displayed, _canvas) = (_canvas, _displayed);
                        break;
                }
            }
        }

        var rows = _rows!;
        long processed = 0;
        while (_row < rows.Height)
        {
            if (processed >= PngImageDataEncoder.BandBytes)
                return false;

            var source = rows.GetRow(_row);
            source.CopyTo(_displayed!.GetRowSpan(_row));
            processed += source.Length;
            _row++;
        }

        var frameData = _index > 0 || _posterWritten;
        _pending = new PendingFrame(_plan.Region, _plan.Over is not null, _plan.Filter, _plan.Level, _delay!.Value, frameData, _index);
        return true;
    }

    private UnsupportedImageFeatureException CreateColorException(PngIndexedRowStatus status, int x, int y) => status switch
    {
        PngIndexedRowStatus.NotEightBit => new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"A PNG palette stores 8-bit samples, and the {Options.PixelFormat} pixel ({x}, {y}) has a 16-bit sample that 8 bits cannot represent exactly. PngPixelLayout.Indexed never reduces precision: convert the image explicitly with CloneAs, or select PngPixelLayout.Auto."), ImageFormat.Png, "Bit depth reduction"),
        _ => new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"The image has more than {ExactPaletteCollector.MaxColors} distinct colors, the most a PNG palette can hold; the pixel ({x}, {y}) has one more. A color is a combination of red, green, blue and alpha samples (fully transparent pixels of different colors are different entries), and the frames and the poster of an animation share one palette. PngPixelLayout.Indexed never reduces colors: reduce the colors of the image first, or select PngPixelLayout.Auto."), ImageFormat.Png, "Color reduction"),
    };

    private UnsupportedImageFeatureException CreateMismatchException(PngLayoutMismatch mismatch, int x, int y)
    {
        var (what, feature) = mismatch switch
        {
            PngLayoutMismatch.Alpha => ("is not opaque, and the layout has no alpha", "Alpha removal"),
            PngLayoutMismatch.ColorKey => ("is transparent with another color than the tRNS color key, or opaque with the color of the key", "Alpha removal"),
            PngLayoutMismatch.Color => (_layout!.IsIndexed ? "has a color that is not in the palette" : "is not gray", "Color reduction"),
            _ => ("has a sample that needs more bits than the layout stores", "Bit depth reduction"),
        };

        return new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"The pixel ({x}, {y}) {what}: the layout of the PNG file ({_layout}) was chosen by PngOptimization.MinimumSize from the images known before its header, the first image for a sequential writer. Save the whole image at once, write a first image that needs every color, alpha value and bit of the animation, or select PngOptimization.None."), ImageFormat.Png, feature);
    }

    private void DisposeState()
    {
        _frame = null;
        _plannedPoster = null;
        _plannedFrames = null;
        _trials.Clear();
        _data?.Dispose();
        _data = null;
        _scanlines?.Dispose();
        _scanlines = null;
        _rows?.Dispose();
        _rows = null;
        _referenceRows?.Dispose();
        _referenceRows = null;
        _displayed?.Dispose();
        _displayed = null;
        _canvas?.Dispose();
        _canvas = null;
        _analysis?.Dispose();
        _analysis = null;
        _palette?.Dispose();
        _palette = null;
    }

    /// <summary>A candidate encoding of an image, compressed to a counter.</summary>
    private sealed class Trial(PngOptimizedLayout layout, ImageFrame image, Rectangle region, PngCanvasReference? over, PngFilter filter, int tag, CompressionLevel level = CompressionLevel.SmallestSize)
    {
        public PngOptimizedLayout Layout { get; } = layout;

        public ImageFrame Image { get; } = image;

        public Rectangle Region { get; } = region;

        public PngCanvasReference? Over { get; } = over;

        public PngFilter Filter { get; } = filter;

        public CompressionLevel Level { get; } = level;

        /// <summary>Gets the image of the layout trial that the trial measures; the smallest trial of an image counts.</summary>
        public int Group { get; init; }

        /// <summary>Gets the previous frame of an OVER layout trial, the canvas of the frame, or <see langword="null"/>.</summary>
        public ImageFrame? PreviousImage { get; init; }

        /// <summary>Gets the candidate layout, or the variant, the trial measures.</summary>
        public int Tag { get; } = tag;

        public long Size { get; set; }
    }

    /// <summary>The encoding chosen for the image of the operation.</summary>
    /// <param name="Region">The region of the canvas that is written.</param>
    /// <param name="Over">The canvas of an OVER frame, or <see langword="null"/> for SOURCE.</param>
    /// <param name="Filter">The filter strategy.</param>
    /// <param name="PreviousDispose">The disposal of the previous frame that leaves the canvas of this frame.</param>
    /// <param name="PreviousRegion">The region of the previous frame, which BACKGROUND clears.</param>
    /// <param name="HasSize">Whether <paramref name="Size"/> was measured.</param>
    /// <param name="Size">The bytes of the image data.</param>
    /// <param name="Level">The zlib compression level.</param>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct FramePlan(Rectangle Region, PngCanvasReference? Over, PngFilter Filter, byte PreviousDispose, Rectangle PreviousRegion, bool HasSize, long Size, CompressionLevel Level = CompressionLevel.SmallestSize);

    /// <summary>A frame written once the next one chose the disposal of this one.</summary>
    private sealed record PendingFrame(Rectangle Region, bool Over, PngFilter Filter, CompressionLevel Level, (ushort Numerator, ushort Denominator) Delay, bool FrameData, int Index);

    /// <summary>The comparison of a frame with one canvas.</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct Diff(PngCanvasReference reference, byte dispose)
    {
        private int _left = int.MaxValue;
        private int _top = int.MaxValue;
        private int _right = -1;
        private int _bottom = -1;

        public PngCanvasReference Reference { get; } = reference;

        public byte DisposeOp { get; } = dispose;

        public bool OverExact { get; set; } = true;

        public void Include(int x, int y)
        {
            _left = Math.Min(_left, x);
            _top = Math.Min(_top, y);
            _right = Math.Max(_right, x);
            _bottom = Math.Max(_bottom, y);
        }

        /// <summary>Gets the bounding box of the pixels that differ, or a 1x1 region when none does.</summary>
        public readonly Rectangle GetRegion() => _right < 0 ? new Rectangle(0, 0, 1, 1) : Rectangle.FromLTRB(_left, _top, _right + 1, _bottom + 1);
    }
}
