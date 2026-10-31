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
/// <item><description>
/// A scaled decode (<see cref="ImageDecodeRequest.ScaleDenominator"/>) discards the highest resolution levels: their packets
/// are read, their code-blocks are not decoded, and the image has the size of the lower resolution level on the reference
/// grid (B-14), <c>ceil(x1 / 2^r) - ceil(x0 / 2^r)</c>. The highest denominator is 2 to the power of the decomposition
/// levels of the main header; a tile with fewer is an error.
/// </description></item>
/// <item><description>
/// A region (<see cref="ImageDecodeRequest.Region"/>) is an area of the reference grid at full resolution: the tiles it
/// does not meet are not decoded, and in the others only the code-blocks whose coefficients reach it.
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

    // The resolution levels that are discarded, the area of the reference grid at full resolution that is decoded (null
    // for the whole image), and that area at the decoded resolution: the frame
    private int _reduction;
    private Jpeg2000Area? _region;
    private Jpeg2000Area _window;

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

        // The resolution levels to discard: at most the decomposition levels of every component of the main header
        var siz = structure.Siz;
        if (_request.IsScaled)
        {
            var levels = Jpeg2000TileCoding.Resolve(siz, structure.MainHeader, tile: null, structure.Capabilities).Styles.Min(style => style.DecompositionLevels);
            var denominator = _request.GetScaleDenominator(structure.Size, 1 << Math.Min(levels, 30), ImageFormat.Jpeg2000, string.Create(CultureInfo.InvariantCulture, $"the codestream has {levels} decomposition levels"));
            _reduction = System.Numerics.BitOperations.Log2((uint)denominator);
        }

        var area = new Jpeg2000Area(siz.XOffset, siz.YOffset, siz.XSize, siz.YSize);
        if (_request.Region is { } region)
        {
            var requested = new Jpeg2000Area(siz.XOffset + region.X, siz.YOffset + region.Y, siz.XOffset + (long)region.X + region.Width, siz.YOffset + (long)region.Y + region.Height);
            area = area.Intersect(requested);
            if (area.IsEmpty)
                throw Jpeg2000Format.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The region {region} does not meet the image of {structure.Size.Width}x{structure.Size.Height} pixels."), "Region decoding");
        }

        // A region narrower than a sample of the decoded resolution keeps the sample that covers it
        var image = new Jpeg2000Area(siz.XOffset, siz.YOffset, siz.XSize, siz.YSize).Reduce(_reduction);
        var window = area.Reduce(_reduction);
        var windowX0 = window.X1 > window.X0 ? window.X0 : Math.Min(window.X0, image.X1 - 1);
        var windowY0 = window.Y1 > window.Y0 ? window.Y0 : Math.Min(window.Y0, image.Y1 - 1);
        _window = new Jpeg2000Area(windowX0, windowY0, Math.Max(window.X1, windowX0 + 1), Math.Max(window.Y1, windowY0 + 1));
        if (_request.Region is not null)
        {
            // The samples of the window cover this area of the full resolution: the tiles and the code-blocks to decode
            _region = new Jpeg2000Area(_window.X0 << _reduction, _window.Y0 << _reduction, _window.X1 << _reduction, _window.Y1 << _reduction).Intersect(new Jpeg2000Area(siz.XOffset, siz.YOffset, siz.XSize, siz.YSize));
        }
        var size = new Size((int)(_window.X1 - _window.X0), (int)(_window.Y1 - _window.Y0));
        if (size != structure.Size)
        {
            _context.Sequential?.ScaleHeader(size);
        }

        // The rows are written as RGBA for the gray formats with alpha and 16-bit RGB
        var format = layout.DefaultPixelFormat;
        _sink = DecodedFrameSink.Create(_context, _request, ImageFormat.Jpeg2000, size, format, PixelFormatWidening.GetWidenedFormat(format), profile);
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

        // A tile that the region does not meet is not decoded
        siz.GetTileBounds(tile.Index, out var x0, out var y0, out var x1, out var y1);
        if (_region is { } region && new Jpeg2000Area(x0, y0, x1, y1).Intersect(region).IsEmpty)
            return;

        var coding = Jpeg2000TileCoding.Resolve(siz, structure.MainHeader, tile.Headers, structure.Capabilities);
        using var geometry = Jpeg2000TileGeometry.Create(_context.Scope, siz, coding, tile.Index, _reduction);
        _tileDecoder ??= new Jpeg2000TileDecoder(_context, _context.Configuration.MaxDegreeOfParallelism);
        structure.AddTools(_tileDecoder.Decode(siz, coding, geometry, tile, _region));
        _rowWriter ??= new Jpeg2000RowWriter(_context, siz, structure.Layout, sink, _inkConversion, _window);
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
