namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes a page of a TIFF file at a reduced resolution (<see cref="ImageDecodeRequest.ScaleDenominator"/>,
/// <see cref="ImageDecodeRequest.TargetSize"/>) from the reduced-resolution images the file stores for it: a pyramid.
/// </summary>
/// <remarks>
/// <para>
/// The levels of a page are the images marked as reduced-resolution versions of an image (<c>NewSubfileType</c> bit 0, or
/// <c>SubfileType</c> 2), not as transparency masks (<c>NewSubfileType</c> bit 2): the images of its <c>SubIFDs</c> (as Adobe,
/// OME-TIFF and libvips write them), and the images that follow it in the chain of directories until the first one that is
/// not a reduced-resolution image (as GDAL writes the overviews of a Cloud Optimized GeoTIFF file). A level is used when its
/// size is the size of the page divided by a power of two <c>n</c>, rounded down or up in each direction, and its pixels have
/// the format and the color model of the page. A level that cannot be read or decoded is ignored, like any other image
/// that does not fit: the page has the levels it can use.
/// </para>
/// <para>
/// The image is the level, with the metadata of the page (its color profile labels the samples of every level): the metadata
/// of the levels is neither read nor charged to <see cref="ImageResourceLimits.MaxMetadataBytes"/>. A region is
/// in pixels of the page: it becomes the pixels of the level that cover it (left and top edges divided by <c>n</c> and
/// rounded down, right and bottom edges rounded up, at least one pixel).
/// </para>
/// </remarks>
internal static class TiffResolutionLevels
{
    // The largest number of directories read to find the levels of a page: a pyramid halves its size at each level
    private const int MaxLevels = 32;

    /// <summary>Decodes a page for a request that asks for a reduced resolution.</summary>
    /// <param name="source">The input.</param>
    /// <param name="page">The page.</param>
    /// <param name="request">The request, which asks for a reduced resolution.</param>
    /// <param name="context">The operation context.</param>
    /// <returns>The image of the level that the request selects, or of the page when no level fits a target size.</returns>
    /// <exception cref="UnsupportedImageFeatureException">The request asks for an exact scale and the page has no level of that scale, or the region does not meet the page.</exception>
    public static Image Decode(RandomAccessSource source, TiffPage page, ImageDecodeRequest request, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        // The region is checked against the page before any level is read
        var area = request.Region is null ? (Rectangle?)null : request.GetRegion(page.Size, ImageFormat.Tiff);
        var (level, denominator) = Select(source, page, request, context);
        if (level is null)
            return TiffPageDecoder.Decode(source, page, request, context);

        Rectangle? levelArea = null;
        if (area is { } region)
        {
            var size = level.Size;
            var left = Math.Min(region.Left / denominator, size.Width - 1);
            var top = Math.Min(region.Top / denominator, size.Height - 1);
            var right = Math.Clamp(ImageDecodeRequest.GetScaledLength(region.Right, denominator), left + 1, size.Width);
            var bottom = Math.Clamp(ImageDecodeRequest.GetScaledLength(region.Bottom, denominator), top + 1, size.Height);
            levelArea = Rectangle.FromLTRB(left, top, right, bottom);
        }

        return TiffPageDecoder.Decode(source, level, request with { ScaleDenominator = 1, TargetSize = null, Region = levelArea }, context, page.Metadata);
    }

    /// <summary>
    /// Selects the level of a request: the level of <see cref="ImageDecodeRequest.ScaleDenominator"/>, or the smallest level
    /// whose size is still at least <see cref="ImageDecodeRequest.TargetSize"/>.
    /// </summary>
    /// <returns>The level and its scale denominator, or <see langword="null"/> for the page itself.</returns>
    private static (TiffPage? Level, int Denominator) Select(RandomAccessSource source, TiffPage page, ImageDecodeRequest request, ImageCodecContext context)
    {
        var levels = GetLevels(source, page, context);
        if (request.ScaleDenominator > 1)
        {
            foreach (var (level, denominator) in levels)
            {
                if (denominator == request.ScaleDenominator)
                    return (level, denominator);
            }

            throw request.CreateUnsupportedScaleException(ImageFormat.Tiff, levels.Count == 0
                ? "the TIFF page has no reduced-resolution image"
                : string.Create(CultureInfo.InvariantCulture, $"the TIFF page has reduced-resolution images at 1/{string.Join(", 1/", levels.Select(level => level.Denominator).Order())} of its size only"));
        }

        TiffPage? selected = null;
        var selectedDenominator = 1;
        if (request.TargetSize is { } target)
        {
            foreach (var (level, denominator) in levels)
            {
                if (denominator > selectedDenominator && level.Size.Width >= target.Width && level.Size.Height >= target.Height)
                {
                    selected = level;
                    selectedDenominator = denominator;
                }
            }
        }

        return (selected, selectedDenominator);
    }

    /// <summary>Gets the levels of a page that can stand for it at a reduced resolution, with their scale denominators.</summary>
    private static List<(TiffPage Level, int Denominator)> GetLevels(RandomAccessSource source, TiffPage page, ImageCodecContext context)
    {
        var levels = new List<(TiffPage Level, int Denominator)>();
        foreach (var directory in EnumerateCandidates(source, page.Directory))
        {
            TiffPage level;
            try
            {
                // The metadata of a level is not read: the image has the metadata of the page
                level = TiffPage.Resolve(directory, page.Index, context, readMetadata: false);
            }
            catch (Exception exception) when (exception is InvalidImageContentException or UnsupportedImageFeatureException)
            {
                // A level that cannot be decoded is not used: the page itself is still decoded
                continue;
            }

            if (level.DefaultPixelFormat == page.DefaultPixelFormat && level.ColorModel == page.ColorModel && level.HasAlpha == page.HasAlpha
                && level.IsCmyk == page.IsCmyk && level.IsCieLab == page.IsCieLab
                && GetDenominator(page.Size, level.Size) is { } denominator && !levels.Exists(existing => existing.Denominator == denominator))
            {
                levels.Add((level, denominator));
            }
        }

        return levels;
    }

    /// <summary>
    /// Enumerates the reduced-resolution images of a page: those of its <c>SubIFDs</c>, then the ones that follow it in the
    /// chain of directories. A directory that cannot be read ends its list.
    /// </summary>
    private static IEnumerable<TiffDirectory> EnumerateCandidates(RandomAccessSource source, TiffDirectory page)
    {
        var header = page.Header;
        var visited = new HashSet<ulong> { page.Offset };
        ulong[]? subDirectories;
        try
        {
            subDirectories = page.TryReadUnsignedArray(TiffFormat.SubIfdsTag, MaxLevels);
        }
        catch (InvalidImageContentException)
        {
            subDirectories = null;
        }

        foreach (var offset in subDirectories ?? [])
        {
            if (offset != 0 && visited.Add(offset) && TryRead(source, header, offset) is { } directory && IsReducedResolution(directory))
                yield return directory;
        }

        var next = page.NextDirectoryOffset;
        for (var i = 0; i < MaxLevels && next != 0 && visited.Add(next); i++)
        {
            if (TryRead(source, header, next) is not { } directory || !IsReducedResolution(directory))
                yield break;

            yield return directory;
            next = directory.NextDirectoryOffset;
        }
    }

    private static TiffDirectory? TryRead(RandomAccessSource source, TiffHeader header, ulong offset)
    {
        try
        {
            return TiffDirectory.Read(source, header, offset);
        }
        catch (InvalidImageContentException)
        {
            return null;
        }
    }

    /// <summary>Gets a value indicating whether a directory is a reduced-resolution image that is not a transparency mask.</summary>
    private static bool IsReducedResolution(TiffDirectory directory)
    {
        try
        {
            if (directory.TryGetUnsigned(TiffFormat.NewSubfileTypeTag, out var type))
                return (type & 1) != 0 && (type & 4) == 0;

            return directory.TryGetUnsigned(TiffFormat.SubfileTypeTag, out var oldType) && oldType == 2;
        }
        catch (InvalidImageContentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the power of two <c>n</c> by which a level divides the size of its page, each dimension rounded down or up, or
    /// <see langword="null"/> when the level is not such a reduction.
    /// </summary>
    internal static int? GetDenominator(Size page, Size level)
    {
        for (var denominator = 2L; denominator / 2 < Math.Max(page.Width, page.Height); denominator *= 2)
        {
            if (Fits(page.Width, level.Width, denominator) && Fits(page.Height, level.Height, denominator))
                return (int)denominator;
        }

        return null;

        static bool Fits(int length, int reduced, long denominator) => reduced >= 1 && reduced >= length / denominator && reduced <= (length + denominator - 1) / denominator;
    }
}
