namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The JPEG 2000 sample decoder: decodes each tile that <see cref="Jpeg2000StructureParser"/> hands over and writes its
/// pixels to a <see cref="DecodedFrameSink"/>, in the default pixel format of the image.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// The tiles are decoded in the order the codestream completes them and each one writes its own area of the image: the
/// state of a tile is released before the next one is decoded.
/// </description></item>
/// <item><description>
/// The image ends with the input, not with its codestream: metadata boxes may follow the codestream box.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class Jpeg2000Decoder : Jpeg2000DecodeObserver
{
    private readonly ImageDecodeRequest _request;
    private readonly ImageCodecContext _context;
    private Jpeg2000StructureParser? _structure;
    private DecodedFrameSink? _sink;
    private Jpeg2000TileDecoder? _tileDecoder;
    private Jpeg2000RowWriter? _rowWriter;
    private IccPipeline? _inkConversion;
    private bool _framePending;

    public Jpeg2000Decoder(ImageDecodeRequest request, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        _request = request;
        _context = context;
    }

    public override void OnHeaderComplete(Jpeg2000StructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        _structure = structure;
        var layout = structure.Layout;
        var profile = structure.Metadata.IccProfile;
        if (layout.Kind == Jpeg2000ColorKind.Cmyk)
        {
            // Only a CMYK profile is adopted for a CMYK image. The rows are converted to the destination profile, which is
            // the one that labels the image
            var conversion = _request.CmykConversion ?? throw Jpeg2000Format.Unsupported("CMYK JPEG 2000 images are only loaded with a color conversion: set the CmykConversion property of the decode or reader options.", "JPEG 2000 CMYK");
            var transform = conversion.CreateTransform(profile) ?? throw Jpeg2000Format.Unsupported("The CMYK JPEG 2000 image has no CMYK color profile and CmykConversionOptions.AssumedSourceProfile is null.", "JPEG 2000 CMYK without color profile");
            _inkConversion = transform.Pipeline;
            profile = conversion.DestinationProfile;
        }

        // The rows are written as RGBA for the gray formats with alpha and 16-bit RGB
        var format = layout.DefaultPixelFormat;
        _sink = DecodedFrameSink.Create(_context, _request, ImageFormat.Jpeg2000, structure.Size, format, PixelFormatWidening.GetWidenedFormat(format), profile);
    }

    public override void OnTile(Jpeg2000StructureParser structure, Jpeg2000Tile tile)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(tile);
        var sink = _sink ?? throw new InvalidOperationException("The JPEG 2000 header was not parsed.");
        if (!_framePending)
        {
            sink.BeginFrame(FrameDuration.Zero);
            _framePending = true;
        }

        var siz = structure.Siz;
        var coding = Jpeg2000TileCoding.Resolve(siz, structure.MainHeader, tile.Headers, structure.Capabilities);
        using var geometry = Jpeg2000TileGeometry.Create(_context.Scope, siz, coding, tile.Index);
        _tileDecoder ??= new Jpeg2000TileDecoder(_context);
        structure.AddTools(_tileDecoder.Decode(siz, coding, geometry, tile));
        _rowWriter ??= new Jpeg2000RowWriter(_context, siz, structure.Layout, sink, _inkConversion);
        _rowWriter.WriteTile(geometry);
    }

    public override void OnEnd(Jpeg2000StructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        if (_framePending)
        {
            _framePending = false;
            var sink = _sink!;
            sink.UpdateMetadata(structure.Metadata);
            sink.EndImage();
        }
    }

    public override Image GetResult()
    {
        var structure = _structure ?? throw new InvalidOperationException("The JPEG 2000 file was not decoded.");
        return _sink!.Build(structure.Metadata, animation: null);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tileDecoder?.Dispose();
            _tileDecoder = null;
            _rowWriter?.Dispose();
            _rowWriter = null;
            _sink?.Dispose();
            _sink = null;
        }

        base.Dispose(disposing);
    }
}
