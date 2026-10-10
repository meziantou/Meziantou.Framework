namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes the image of an AVIF file, or the images of its sequence: decodes the AV1 bitstream of each coded item or sample,
/// assembles the tiles of a grid, applies the film grain, converts the planes to pixels with the alpha plane, and writes the
/// rows that the clean aperture keeps to the frame sink.
/// </summary>
/// <remarks>
/// Memory: the data of the items or of the samples that are not decoded yet (the walker), the reference state of the AV1
/// decoders, the planes of the color image and of the alpha plane, and three rows, all charged to the operation scope. The
/// decoder of a coded item is released before the next one is decoded; an image sequence keeps one decoder for the color
/// images and one for the alpha planes, whose state does not depend on the number of images.
/// </remarks>
internal sealed class AvifDecoder : AvifDecodeObserver
{
    private readonly ImageDecodeRequest _request;
    private readonly ImageCodecContext _context;
    private AvifStructureParser? _structure;
    private DecodedFrameSink? _sink;
    private Av1Decoder? _colorDecoder;
    private Av1Decoder? _alphaDecoder;
    private bool _isMetadataUpdated;
    private int _metadataVersion;

    public AvifDecoder(ImageDecodeRequest request, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        _request = request;
        _context = context;
    }

    public override void OnHeaderComplete(AvifStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        _structure = structure;
        _sink = DecodedFrameSink.Create(_context, _request, ImageFormat.Avif, structure.CanvasSize, structure.PixelFormat, structure.PixelFormat, structure.Metadata.IccProfile);
    }

    public override void OnImageData(AvifStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        var plan = structure.Plan;
        var sink = _sink!;
        sink.BeginFrame(FrameDuration.Zero);
        using var color = DecodeImage(structure, plan.Color);
        structure.SetBitstreamColorDescription(color.Sequence);
        using var alpha = plan.Alpha is null ? null : DecodeImage(structure, plan.Alpha);
        WriteFrame(structure, color, alpha, plan.Color.Size, plan.Crop);
        sink.UpdateMetadata(structure.Metadata);
        sink.EndImage();
    }

    public override bool OnSequenceFrame(AvifStructureParser structure, WebPPayloadBuffer color, WebPPayloadBuffer? alpha, FrameDuration duration)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(color);
        var movie = structure.Movie ?? throw new InvalidOperationException("The AVIF file is not an image sequence.");
        var sink = _sink!;
        sink.BeginFrame(duration);
        _colorDecoder ??= new Av1Decoder(_context.Scope, _context.Tracker);
        using var colorImage = DecodeSample(_colorDecoder, color, movie.Color);
        structure.SetBitstreamColorDescription(colorImage.Sequence);
        AvifPlanarImage? alphaImage = null;
        try
        {
            if (alpha is not null)
            {
                _alphaDecoder ??= new Av1Decoder(_context.Scope, _context.Tracker);
                alphaImage = DecodeSample(_alphaDecoder, alpha, movie.Alpha!);
            }

            var size = movie.Color.Size;
            WriteFrame(structure, colorImage, alphaImage, size, new Rectangle(0, 0, size.Width, size.Height));
        }
        finally
        {
            alphaImage?.Dispose();
        }

        // The color description of the bitstream is known once the first image is decoded, and the metadata items once
        // their data went by
        if (!_isMetadataUpdated || _metadataVersion != structure.MetadataVersion)
        {
            sink.UpdateMetadata(structure.Metadata);
            _isMetadataUpdated = true;
            _metadataVersion = structure.MetadataVersion;
        }

        return sink.EndImage();
    }

    // Converts the planes of an image to pixels, and writes the rows of the clean aperture to the sink
    private void WriteFrame(AvifStructureParser structure, AvifPlanarImage color, AvifPlanarImage? alpha, Size codedSize, Rectangle crop)
    {
        var sink = _sink!;

        // A 'colr' box of type 'nclx' describes the colors of the image; otherwise the bitstream does
        var nclx = structure.ContainerColorDescription;
        var matrix = nclx?.MatrixCoefficients ?? color.Sequence.MatrixCoefficients;
        var fullRange = nclx?.FullRange ?? color.Sequence.FullRange;
        var converter = new AvifYuvConverter(color.Sequence, matrix, fullRange, alpha?.Sequence, structure.PixelFormat);
        var width = codedSize.Width;
        var height = codedSize.Height;
        using var rows = Av1PooledArray<ushort>.Rent(_context.Scope, 2L * width, clear: false);
        using var pixels = _context.Scope.Rent(checked(width * converter.BytesPerPixel), AllocationKind.DecoderState, clear: false);
        var u = rows.Span[..width];
        var v = rows.Span.Slice(width, width);
        var pixelRow = pixels.RawBuffer.AsSpan(0, width * converter.BytesPerPixel);
        var hasChroma = color.PlaneCount > 1;
        using var target = sink.LeaseCurrentFrame();
        for (var y = 0; y < crop.Height; y++)
        {
            if ((y & 31) is 0)
                _context.CancellationToken.ThrowIfCancellationRequested();

            var sourceY = crop.Y + y;
            if (hasChroma)
            {
                converter.UpsampleChromaRow(color, 1, sourceY, width, height, u);
                converter.UpsampleChromaRow(color, 2, sourceY, width, height, v);
            }

            var alphaRow = alpha is null ? default : alpha.GetPlane(0).GetRow(sourceY)[..width];
            converter.ConvertRow(color.GetPlane(0).GetRow(sourceY)[..width], u, v, alphaRow, pixelRow);
            sink.WriteRow(target, y, pixelRow.Slice(crop.X * converter.BytesPerPixel, crop.Width * converter.BytesPerPixel));
        }
    }

    public override Image GetResult()
    {
        var structure = _structure ?? throw new InvalidOperationException("The AVIF file was not decoded.");
        return _sink!.Build(structure.Metadata, structure.Animation);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sink?.Dispose();
            _sink = null;
            _colorDecoder?.Dispose();
            _colorDecoder = null;
            _alphaDecoder?.Dispose();
            _alphaDecoder = null;
        }

        base.Dispose(disposing);
    }

    // Decodes the coded items of an image into planes of the size of the image
    private AvifPlanarImage DecodeImage(AvifStructureParser structure, AvifCodedImage image)
    {
        AvifPlanarImage? result = null;
        try
        {
            for (var i = 0; i < image.Tiles.Length; i++)
            {
                _context.CancellationToken.ThrowIfCancellationRequested();
                structure.GetItemBuffer(image.Tiles[i], out var data, out var length);
                var frame = DecodeCodedItem(data, length, image.OperatingPoints[i], image.Layers[i]);
                try
                {
                    var plane = frame.GetPlane(0);
                    AvifStructureParser.EnsureMatchesConfiguration(image, frame.Sequence);
                    if (plane.Width != image.TileSize.Width || plane.Height != image.TileSize.Height)
                        throw AvifFormat.Invalid("the size of an AV1 image does not match its 'ispe' property.");

                    if (result is not null && !result.Sequence.IsSameSequence(frame.Sequence))
                        throw AvifFormat.Invalid("the tiles of a grid do not have the same sequence header.");

                    var filmGrain = frame.Header.FilmGrain;
                    if (!image.IsGrid && filmGrain is null)
                        return AvifPlanarImage.FromFrame(frame);

                    // The planes hold whole tiles: the tiles of the last row and column may exceed the image
                    if (result is null)
                        result = AvifPlanarImage.Create(_context.Scope, frame.Sequence, image.Columns * image.TileSize.Width, image.Rows * image.TileSize.Height);

                    CopyTile(frame, filmGrain, result, i % image.Columns * image.TileSize.Width, i / image.Columns * image.TileSize.Height);
                }
                finally
                {
                    frame.Release();
                }
            }

            var planes = result!;
            result = null;
            return planes;
        }
        finally
        {
            result?.Dispose();
        }
    }

    // Decodes the temporal unit of an image of a sequence: the decoder keeps the frames that the next images refer to
    private AvifPlanarImage DecodeSample(Av1Decoder decoder, WebPPayloadBuffer sample, AvifTrack track)
    {
        decoder.SetInput(sample.Data, 0, sample.Length);
        var frame = decoder.DecodeNextOutputFrame(_context.CancellationToken) ?? throw AvifFormat.Invalid("a sample of the image sequence has no image.");
        try
        {
            if (decoder.DecodeNextOutputFrame(_context.CancellationToken) is { } extra)
            {
                extra.Release();
                throw AvifFormat.Invalid("a sample of the image sequence has several images.");
            }

            AvifStructureParser.EnsureMatchesConfiguration(track, frame.Sequence);
            var plane = frame.GetPlane(0);
            if (plane.Width != track.Size.Width || plane.Height != track.Size.Height)
                throw AvifFormat.Unsupported("An image of the AVIF sequence does not have the size of the sequence.", "image size changes inside a sequence");

            var filmGrain = frame.Header.FilmGrain;
            if (filmGrain is null)
                return AvifPlanarImage.FromFrame(frame);

            AvifPlanarImage? result = null;
            try
            {
                result = AvifPlanarImage.Create(_context.Scope, frame.Sequence, plane.Width, plane.Height);
                CopyTile(frame, filmGrain, result, 0, 0);
                var image = result;
                result = null;
                return image;
            }
            finally
            {
                result?.Dispose();
            }
        }
        finally
        {
            frame.Release();
        }
    }

    // Decodes the frame of a coded item: the reference state of the decoder is released, the frame is kept by the caller.
    // A layered item holds a frame per spatial layer: the frame of the selected layer is the image, or the frame of the
    // highest layer of the operating point, which the lower layers only help to decode or to show progressively
    private Av1FrameBuffer DecodeCodedItem(byte[] data, int length, int operatingPoint, int? layer)
    {
        using var decoder = new Av1Decoder(_context.Scope, _context.Tracker, operatingPoint);
        if (layer is { } selected)
            decoder.MaxSpatialLayer = selected;

        decoder.SetInput(data, 0, length);
        var frame = decoder.DecodeNextOutputFrame(_context.CancellationToken) ?? throw AvifFormat.Invalid("an AV1 image has no frame.");
        if (layer is not null && decoder.OutputSpatialId != layer)
        {
            frame.Release();
            throw AvifFormat.Invalid("the bitstream of an AV1 image does not have the layer that the image selects.");
        }

        return frame;
    }

    private void CopyTile(Av1FrameBuffer frame, Av1FilmGrainParameters? filmGrain, AvifPlanarImage destination, int x, int y)
    {
        var sequence = frame.Sequence;
        var luma = frame.GetPlane(0);
        if (filmGrain is null)
        {
            for (var plane = 0; plane < frame.PlaneCount; plane++)
            {
                var subX = plane > 0 ? sequence.SubsamplingX : 0;
                var subY = plane > 0 ? sequence.SubsamplingY : 0;
                var source = frame.GetPlane(plane);
                var target = destination.GetPlane(plane);
                for (var row = 0; row < source.Height; row++)
                {
                    source.GetRow(row).CopyTo(target.Samples[((((y >> subY) + row) * target.Stride) + (x >> subX))..]);
                }
            }

            return;
        }

        using var grain = new Av1FilmGrain(_context.Scope, sequence, filmGrain, luma.Width, luma.Height);
        var targetY = destination.GetPlane(0);
        for (var stripe = 0; stripe < grain.StripeCount; stripe++)
        {
            var lumaRows = targetY.Samples[(((y + (stripe * Av1FilmGrain.StripeHeight)) * targetY.Stride) + x)..];
            Span<ushort> uRows = default;
            Span<ushort> vRows = default;
            var chromaStride = 0;
            if (frame.PlaneCount > 1)
            {
                var targetU = destination.GetPlane(1);
                var targetV = destination.GetPlane(2);
                chromaStride = targetU.Stride;
                var offset = ((((y >> sequence.SubsamplingY) + (stripe << (5 - sequence.SubsamplingY))) * chromaStride) + (x >> sequence.SubsamplingX));
                uRows = targetU.Samples[offset..];
                vRows = targetV.Samples[offset..];
            }

            grain.ApplyStripe(frame, stripe, lumaRows, targetY.Stride, uRows, vRows, chromaStride);
        }
    }
}
