namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The decoded planes of an HEIC image, at their output size: the planes of a decoded HEVC picture whose conformance window
/// starts at its first sample, planes that hold the conformance window of a picture or the tiles of a grid, or a band of
/// planes that holds one row of tiles of a grid at a time.
/// </summary>
internal sealed class HeicPlanarImage : HeifPlanarImage
{
    /// <summary>The rows of each plane of a band that precede its row of tiles: the last rows of the row of tiles above.</summary>
    public const int BandOverlap = 2;

    private readonly Av1Plane?[] _planes = new Av1Plane?[3];
    private readonly int[] _rowOrigins = new int[3];
    private HevcPicture? _picture;

    private HeicPlanarImage(in HeifBitstreamFormat format, int width, int height)
    {
        Format = format;
        Width = width;
        Height = height;
    }

    public override HeifBitstreamFormat Format { get; }

    public override int Width { get; }

    public override int Height { get; }

    /// <summary>
    /// Gives the planes of the conformance window of a decoded picture, which is released with the image or by this method:
    /// the planes of the picture itself when the window starts at its first sample, otherwise a copy of the window.
    /// </summary>
    /// <exception cref="ImageResourceLimitException">The copy exceeds the live-allocation limit.</exception>
    public static HeicPlanarImage FromPicture(AllocationScope scope, HevcPicture picture, in HeifBitstreamFormat format)
    {
        var sps = picture.Sps;
        if (sps.OutputLeft is 0 && sps.OutputTop is 0)
            return new HeicPlanarImage(format, sps.OutputWidth, sps.OutputHeight) { _picture = picture };

        try
        {
            var image = Create(scope, format, sps.OutputWidth, sps.OutputHeight);
            image.CopyPicture(picture, 0, 0);
            return image;
        }
        finally
        {
            picture.Release();
        }
    }

    /// <summary>Rents the planes of an image of <paramref name="width"/> x <paramref name="height"/> luma samples.</summary>
    /// <exception cref="ImageResourceLimitException">The planes exceed the live-allocation limit.</exception>
    public static HeicPlanarImage Create(AllocationScope scope, in HeifBitstreamFormat format, int width, int height)
    {
        var image = new HeicPlanarImage(format, width, height);
        try
        {
            for (var plane = 0; plane < format.PlaneCount; plane++)
            {
                var subX = plane > 0 ? format.SubsamplingX : 0;
                var subY = plane > 0 ? format.SubsamplingY : 0;
                image._planes[plane] = Av1Plane.Create(scope, (width + subX) >> subX, (height + subY) >> subY, alignment: 1);
            }
        }
        catch
        {
            image.Dispose();
            throw;
        }

        return image;
    }

    /// <summary>
    /// Rents the planes of a band of an image of <paramref name="width"/> x <paramref name="height"/> luma samples, which holds
    /// a row of tiles of <paramref name="tileHeight"/> luma samples and the <see cref="BandOverlap"/> rows of each plane above
    /// it (<see cref="BeginTileRow"/>).
    /// </summary>
    /// <exception cref="ImageResourceLimitException">The planes exceed the live-allocation limit.</exception>
    public static HeicPlanarImage CreateBand(AllocationScope scope, in HeifBitstreamFormat format, int width, int height, int tileHeight)
    {
        var image = new HeicPlanarImage(format, width, height);
        try
        {
            for (var plane = 0; plane < format.PlaneCount; plane++)
            {
                var subX = plane > 0 ? format.SubsamplingX : 0;
                var subY = plane > 0 ? format.SubsamplingY : 0;
                image._planes[plane] = Av1Plane.Create(scope, (width + subX) >> subX, BandOverlap + ((tileHeight + subY) >> subY), alignment: 1);
                image._rowOrigins[plane] = -BandOverlap;
            }
        }
        catch
        {
            image.Dispose();
            throw;
        }

        return image;
    }

    /// <summary>
    /// Moves a band to the row of tiles whose first luma row is <paramref name="top"/>: the last <see cref="BandOverlap"/> rows
    /// of each plane are kept above it, the other rows are the ones of the new row of tiles.
    /// </summary>
    public void BeginTileRow(int top)
    {
        for (var plane = 0; plane < PlaneCount; plane++)
        {
            var subY = plane > 0 ? Format.SubsamplingY : 0;
            var target = GetPlane(plane);
            for (var row = 0; row < BandOverlap; row++)
            {
                target.GetRow(target.Height - BandOverlap + row).CopyTo(target.GetRow(row));
            }

            _rowOrigins[plane] = (top >> subY) - BandOverlap;
        }
    }

    /// <summary>Copies the conformance window of a decoded picture to the planes of the image, at a position in luma samples.</summary>
    public void CopyPicture(HevcPicture picture, int x, int y)
    {
        var sps = picture.Sps;
        for (var plane = 0; plane < picture.PlaneCount; plane++)
        {
            var subX = plane > 0 ? Format.SubsamplingX : 0;
            var subY = plane > 0 ? Format.SubsamplingY : 0;
            var source = picture.GetPlane(plane);
            var target = GetPlane(plane);
            var left = sps.OutputLeft >> subX;
            var top = sps.OutputTop >> subY;
            var width = (sps.OutputWidth + subX) >> subX;
            var height = (sps.OutputHeight + subY) >> subY;
            for (var row = 0; row < height; row++)
            {
                source.Samples.Slice(((top + row) * source.Stride) + left, width).CopyTo(target.Samples[((((y >> subY) - _rowOrigins[plane] + row) * target.Stride) + (x >> subX))..]);
            }
        }
    }

    public override Av1Plane GetPlane(int plane) => _picture?.GetPlane(plane) ?? _planes[plane] ?? throw new ObjectDisposedException(nameof(HeicPlanarImage));

    public override Span<ushort> GetRow(int plane, int y) => GetPlane(plane).GetRow(y - _rowOrigins[plane]);

    protected override void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        _picture?.Release();
        _picture = null;
        for (var i = 0; i < _planes.Length; i++)
        {
            _planes[i]?.Dispose();
            _planes[i] = null;
        }
    }
}
