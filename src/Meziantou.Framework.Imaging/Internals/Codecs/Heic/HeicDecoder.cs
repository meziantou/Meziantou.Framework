using System.Runtime.ExceptionServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes the image of an HEIC file, or the images of its sequence: decodes the HEVC bitstream of each coded item or
/// sample with the parameter sets of its configuration record, assembles the tiles of a grid, converts the planes to pixels
/// with the alpha plane, and writes the rows that the clean aperture keeps to the frame sink.
/// </summary>
/// <remarks>
/// Memory: the data of the items or of the samples that are not decoded yet (the walker), the decoded picture buffer of the
/// HEVC decoders, the planes of the color image and of the alpha plane, and three rows, all charged to the operation scope.
/// The planes of a grid without alpha plane hold one row of tiles: its rows are converted before the next row is decoded.
/// The decoder of a coded item is released before the next one is decoded; an image sequence keeps one decoder for the color
/// images and one for the alpha planes, and the pictures that they output before their turn: the samples are in decoding
/// order and the images are written in presentation order, so the pictures that wait are bounded by the size of the decoded
/// picture buffer, whatever the number of images.
/// </remarks>
internal sealed class HeicDecoder : HeifDecodeObserver
{
    // The tiles of a grid that is converted one row of tiles at a time are at least as high as the rows that a row of tiles
    // keeps for the next one
    private const int MinBandTileHeight = 4 * HeicPlanarImage.BandOverlap;

    private readonly ImageDecodeRequest _request;
    private readonly ImageCodecContext _context;
    private readonly Queue<HevcPicture> _colorPictures = new();
    private readonly Queue<HevcPicture> _alphaPictures = new();
    private HeifStructureParser? _structure;
    private DecodedFrameSink? _sink;
    private HevcDecoder? _colorDecoder;
    private HevcDecoder? _alphaDecoder;
    private HevcDecoder? _itemTemplate;
    private (int Offset, int Length) _itemTemplateRecord;
    private HeifBitstreamFormat? _identifiedColorFormat;
    private HeifBitstreamFormat? _identifiedAlphaFormat;
    private bool _isMetadataUpdated;
    private int _metadataVersion;

    public HeicDecoder(ImageDecodeRequest request, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        _request = request;
        _context = context;
    }

    public override void OnHeaderComplete(HeifStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        _structure = structure;
        _sink = DecodedFrameSink.Create(_context, _request, ImageFormat.Heic, structure.CanvasSize, structure.PixelFormat, structure.SourcePixelFormat, structure.Metadata.IccProfile);
    }

    public override void OnImageData(HeifStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        var plan = structure.Plan;
        var sink = _sink!;
        sink.BeginFrame(FrameDuration.Zero);
        if (plan.Alpha is null && plan.Color.IsGrid && plan.Color.TileSize.Height >= MinBandTileHeight)
        {
            DecodeGridByTileRows(structure, plan.Color, sink, plan.Crop);
        }
        else
        {
            using var color = DecodeImage(structure, plan.Color);
            structure.SetBitstreamColorDescription(color.Format);
            using var alpha = plan.Alpha is null ? null : DecodeImage(structure, plan.Alpha);
            HeifFrameWriter.WriteFrame(_context, sink, structure, color, alpha, plan.Color.Size, plan.Crop);
        }

        sink.UpdateMetadata(structure.Metadata);
        sink.EndImage();
    }

    // The samples are in decoding order: a picture is output once the pictures that precede it in output order are decoded
    public override void OnSequenceSample(HeifStructureParser structure, WebPPayloadBuffer color, WebPPayloadBuffer? alpha)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(color);
        var movie = structure.Movie ?? throw new InvalidOperationException("The HEIC file is not an image sequence.");
        _colorDecoder ??= CreateDecoder(movie.Color, color, out _identifiedColorFormat);
        DecodeSample(_colorDecoder, color, movie.Color, _colorPictures);
        if (alpha is not null)
        {
            _alphaDecoder ??= CreateDecoder(movie.Alpha!, alpha, out _identifiedAlphaFormat);
            DecodeSample(_alphaDecoder, alpha, movie.Alpha!, _alphaPictures);
        }
    }

    public override void OnSequenceEnd(HeifStructureParser structure)
    {
        if (_colorDecoder is { } color)
        {
            color.EndOfSequence();
            Drain(color, _colorPictures);
        }

        if (_alphaDecoder is { } alpha)
        {
            alpha.EndOfSequence();
            Drain(alpha, _alphaPictures);
        }
    }

    public override bool TryWriteSequenceFrame(HeifStructureParser structure, FrameDuration duration, out bool proceed)
    {
        ArgumentNullException.ThrowIfNull(structure);
        proceed = true;
        var movie = structure.Movie ?? throw new InvalidOperationException("The HEIC file is not an image sequence.");
        if (_colorPictures.Count is 0 || (movie.Alpha is not null && _alphaPictures.Count is 0))
            return false;

        var sink = _sink!;
        sink.BeginFrame(duration);
        using var colorImage = ToSequenceImage(_colorPictures.Dequeue(), movie.Color, ref _identifiedColorFormat);
        structure.SetBitstreamColorDescription(colorImage.Format);
        using var alphaImage = movie.Alpha is null ? null : ToSequenceImage(_alphaPictures.Dequeue(), movie.Alpha, ref _identifiedAlphaFormat);
        HeifFrameWriter.WriteFrame(_context, sink, structure, colorImage, alphaImage, movie.Color.Size, movie.Color.Crop);

        // The color description of the bitstream is known once the first image is decoded, and the metadata items once
        // their data went by
        if (!_isMetadataUpdated || _metadataVersion != structure.MetadataVersion)
        {
            sink.UpdateMetadata(structure.Metadata);
            _isMetadataUpdated = true;
            _metadataVersion = structure.MetadataVersion;
        }

        proceed = sink.EndImage();
        return true;
    }

    public override Image GetResult()
    {
        var structure = _structure ?? throw new InvalidOperationException("The HEIC file was not decoded.");
        return _sink!.Build(structure.Metadata, structure.Animation);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sink?.Dispose();
            _sink = null;
            ReleasePictures(_colorPictures);
            ReleasePictures(_alphaPictures);
            _colorDecoder?.Dispose();
            _colorDecoder = null;
            _alphaDecoder?.Dispose();
            _alphaDecoder = null;
            _itemTemplate?.Dispose();
            _itemTemplate = null;
        }

        base.Dispose(disposing);
    }

    private static void ReleasePictures(Queue<HevcPicture> pictures)
    {
        while (pictures.TryDequeue(out var picture))
        {
            picture.Release();
        }
    }

    // Decodes the coded items of an image into planes of the size of the image
    private HeicPlanarImage DecodeImage(HeifStructureParser structure, HeifCodedImage image)
    {
        HeicPlanarImage? result = null;
        try
        {
            for (var i = 0; i < image.Tiles.Length; i++)
            {
                var picture = DecodeTile(structure, image, i, result?.Format, out var format);
                if (!image.IsGrid)
                    return HeicPlanarImage.FromPicture(_context.Scope, picture, format);

                try
                {
                    // The planes hold whole tiles: the tiles of the last row and column may exceed the image
                    result ??= HeicPlanarImage.Create(_context.Scope, format, image.Columns * image.TileSize.Width, image.Rows * image.TileSize.Height);
                    result.CopyPicture(picture, i % image.Columns * image.TileSize.Width, i / image.Columns * image.TileSize.Height);
                }
                finally
                {
                    picture.Release();
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

    // Decodes the tiles of a grid without alpha plane and writes the rows of the image as soon as the rows of tiles that
    // their conversion reads are decoded: the planes hold one row of tiles, and the last rows of the one above, which the
    // chroma of the last rows of the row above reads. The errors happen in the order of the decoding of the whole image
    // before its conversion: those of the conversion are thrown once every tile is decoded.
    private void DecodeGridByTileRows(HeifStructureParser structure, HeifCodedImage image, DecodedFrameSink sink, Rectangle crop)
    {
        HeicPlanarImage? band = null;
        HeifFrameWriter? writer = null;
        ExceptionDispatchInfo? conversionError = null;
        var tileHeight = image.TileSize.Height;
        var nextRow = 0;
        try
        {
            for (var i = 0; i < image.Tiles.Length; i++)
            {
                var picture = DecodeTile(structure, image, i, band?.Format, out var format);
                var tileRow = i / image.Columns;
                try
                {
                    band ??= HeicPlanarImage.CreateBand(_context.Scope, format, image.Columns * image.TileSize.Width, image.Rows * tileHeight, tileHeight);
                    if (i % image.Columns is 0)
                    {
                        band.BeginTileRow(tileRow * tileHeight);
                    }

                    band.CopyPicture(picture, i % image.Columns * image.TileSize.Width, tileRow * tileHeight);
                }
                finally
                {
                    picture.Release();
                }

                if (i % image.Columns != image.Columns - 1 || conversionError is not null)
                    continue;

                // The chroma of the last two luma rows of a row of tiles may be interpolated with the first chroma row of the
                // next row of tiles, when it is subsampled vertically
                var end = tileRow == image.Rows - 1 ? crop.Height : Math.Min(crop.Height, ((tileRow + 1) * tileHeight) - (format.SubsamplingY is 0 ? 0 : HeicPlanarImage.BandOverlap) - crop.Y);
                if (end <= nextRow)
                    continue;

                try
                {
                    writer ??= new HeifFrameWriter(_context, structure, format, alpha: null, image.Size, crop);
                    writer.WriteRows(sink, band, alpha: null, nextRow, end);
                    nextRow = end;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    conversionError = ExceptionDispatchInfo.Capture(ex);
                }
            }

            structure.SetBitstreamColorDescription(band!.Format);
            conversionError?.Throw();
        }
        finally
        {
            writer?.Dispose();
            band?.Dispose();
        }
    }

    // Decodes a coded item of an image, and checks the picture against the file and against the previous tiles of a grid
    private HevcPicture DecodeTile(HeifStructureParser structure, HeifCodedImage image, int index, HeifBitstreamFormat? previous, out HeifBitstreamFormat format)
    {
        _context.CancellationToken.ThrowIfCancellationRequested();
        structure.GetItemBuffer(image.Tiles[index], out var data, out var length);
        var configuration = structure.GetConfigurationRecord(image, index);

        // What the identification of the file checks: the sequence parameter set of the configuration record, or else the
        // first one of the data. The parameter set of the picture, checked below, is another one when the data replace the
        // one of the record.
        var identified = HeicBinding.Instance.ReadBitstreamFormat(_context.Scope, configuration, data.AsSpan(0, length), image.OperatingPoints[index]);
        HeifStructureParser.EnsureMatchesConfiguration(image, identified);
        var picture = DecodeCodedItem(configuration, image.ConfigurationRecords[index], data, length);
        try
        {
            format = HeicBinding.ToBitstreamFormat(picture.Sps);
            EnsureIsIdentifiedFormat(format, identified);
            HeifStructureParser.EnsureMatchesConfiguration(image, format);
            if (picture.Sps.OutputWidth != image.TileSize.Width || picture.Sps.OutputHeight != image.TileSize.Height)
                throw HeicBinding.Instance.Invalid("the size of an HEVC image does not match its 'ispe' property.");

            if (previous is { } expected && expected != format)
                throw HeicBinding.Instance.Invalid("the tiles of a grid do not have the same sample format.");
        }
        catch
        {
            picture.Release();
            throw;
        }

        return picture;
    }

    // Decodes the picture of a coded item: the decoder is released, the picture is kept by the caller
    private HevcPicture DecodeCodedItem(ReadOnlySpan<byte> configuration, (int Offset, int Length) record, byte[] data, int length)
    {
        // The parameter sets of a configuration record are read once for the items that share it: the tiles of a grid
        // would otherwise each pay for the whole record, whose size is not bounded by the size of a tile
        if (_itemTemplate is null || _itemTemplateRecord != record)
        {
            _itemTemplate?.Dispose();
            _itemTemplate = null;
            _itemTemplate = CreateDecoder(configuration);
            _itemTemplateRecord = record;
        }

        using var decoder = new HevcDecoder(_context.Scope, _context.Tracker);
        decoder.CopyParameterSetsFrom(_itemTemplate);
        decoder.SetInput(data, 0, length, HeicConfigurationRecord.GetNalUnitLengthSize(configuration));
        var picture = decoder.DecodeNextOutputPicture(_context.CancellationToken);
        if (picture is null)
        {
            decoder.EndOfSequence();
            picture = decoder.DecodeNextOutputPicture(_context.CancellationToken) ?? throw HeicBinding.Instance.Invalid("an HEVC image has no picture.");
        }

        HevcPicture? extra;
        try
        {
            extra = decoder.DecodeNextOutputPicture(_context.CancellationToken);
            if (extra is null)
            {
                decoder.EndOfSequence();
                extra = decoder.DecodeNextOutputPicture(_context.CancellationToken);
            }
        }
        catch
        {
            picture.Release();
            throw;
        }

        if (extra is not null)
        {
            extra.Release();
            picture.Release();
            throw HeicBinding.Instance.Invalid("an HEVC image has several pictures.");
        }

        return picture;
    }

    // The decoder of a track, whose first sample is checked as the identification of the file checks it: its sequence
    // parameter set, the one of the configuration record or else the first one of the sample, matches the record
    private HevcDecoder CreateDecoder(HeifTrack track, WebPPayloadBuffer firstSample, out HeifBitstreamFormat? identified)
    {
        var format = HeicBinding.Instance.ReadBitstreamFormat(_context.Scope, track.ConfigurationRecord, firstSample.Data.AsSpan(0, firstSample.Length), operatingPoint: 0);
        HeifStructureParser.EnsureMatchesConfiguration(HeicBinding.Instance, track, format);
        identified = format;
        return CreateDecoder(track.ConfigurationRecord);
    }

    // The identification of a file describes its images with the sequence parameter set of the configuration record, or
    // else with the first one of the data: the picture that is decoded has that sample format and that color description,
    // whichever parameter set its slices refer to
    private static void EnsureIsIdentifiedFormat(in HeifBitstreamFormat format, in HeifBitstreamFormat identified)
    {
        if (format != identified)
            throw HeicBinding.Instance.Invalid("the sequence parameter set of an HEVC picture does not describe it as the one of its configuration record.");
    }

    // A decoder that has the parameter sets and the supplemental information of a configuration record
    private HevcDecoder CreateDecoder(ReadOnlySpan<byte> configuration)
    {
        var decoder = new HevcDecoder(_context.Scope, _context.Tracker);
        try
        {
            var position = default(HeicConfigurationRecord.Position);
            while (HeicConfigurationRecord.TryReadNalUnit(configuration, ref position, out var nalUnit))
            {
                // forbidden_zero_bit, then the six bits of nal_unit_type; the arrays of a record may hold other NAL units,
                // which no decoding process needs
                if (((nalUnit[0] >> 1) & 0x3F) is HevcConstants.NalVps or HevcConstants.NalSps or HevcConstants.NalPps or HevcConstants.NalPrefixSei or HevcConstants.NalSuffixSei)
                    decoder.AddParameterSet(nalUnit);
            }

            return decoder;
        }
        catch
        {
            decoder.Dispose();
            throw;
        }
    }

    // Decodes the access unit of a sample: the decoder keeps the pictures that the next ones refer to, and outputs the
    // pictures whose turn has come
    private void DecodeSample(HevcDecoder decoder, WebPPayloadBuffer sample, HeifTrack track, Queue<HevcPicture> pictures)
    {
        decoder.SetInput(sample.Data, 0, sample.Length, HeicConfigurationRecord.GetNalUnitLengthSize(track.ConfigurationRecord));
        Drain(decoder, pictures);
    }

    private void Drain(HevcDecoder decoder, Queue<HevcPicture> pictures)
    {
        while (decoder.DecodeNextOutputPicture(_context.CancellationToken) is { } picture)
        {
            pictures.Enqueue(picture);
        }
    }

    // The planes of a picture of a sequence, which is released with the image
    private HeicPlanarImage ToSequenceImage(HevcPicture picture, HeifTrack track, ref HeifBitstreamFormat? identified)
    {
        HeifBitstreamFormat format;
        try
        {
            format = HeicBinding.ToBitstreamFormat(picture.Sps);
            if (identified is { } expected)
            {
                // The first picture of the track
                identified = null;
                EnsureIsIdentifiedFormat(format, expected);
            }

            HeifStructureParser.EnsureMatchesConfiguration(HeicBinding.Instance, track, format);
            if (picture.Sps.OutputWidth != track.Size.Width || picture.Sps.OutputHeight != track.Size.Height)
                throw HeicBinding.Instance.Unsupported("An image of the HEIC sequence does not have the size of the sequence.", "image size changes inside a sequence");
        }
        catch
        {
            picture.Release();
            throw;
        }

        return HeicPlanarImage.FromPicture(_context.Scope, picture, format);
    }
}
