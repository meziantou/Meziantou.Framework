using System.Globalization;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>
/// The description printed by <c>heif-info</c> (libheif's own container parser): the brands, the primary image (size,
/// sample format, color description, alpha plane, metadata items, transformations) and the tracks of an image sequence.
/// </summary>
public sealed partial class HeifInfo
{
    /// <summary>Gets the MIME type libheif gives to the file: <c>image/heic</c>, <c>image/heic-sequence</c>...</summary>
    public string MimeType { get; private init; } = "";

    /// <summary>Gets the major brand.</summary>
    public string MajorBrand { get; private init; } = "";

    /// <summary>Gets the compatible brands, in file order.</summary>
    public IReadOnlyList<string> CompatibleBrands { get; private init; } = [];

    /// <summary>Gets a value indicating whether heif-info describes a primary image; a file that only has tracks has none.</summary>
    public bool HasPrimaryImage { get; private init; }

    /// <summary>Gets the width of the primary image, after its transformations (<c>clap</c>, <c>irot</c>, <c>imir</c>).</summary>
    public int Width { get; private init; }

    /// <summary>Gets the height of the primary image, after its transformations (<c>clap</c>, <c>irot</c>, <c>imir</c>).</summary>
    public int Height { get; private init; }

    /// <summary>Gets the grid of the primary image (columns, rows, and the size of a tile), or <see langword="null"/> when it is not a grid.</summary>
    public (int Columns, int Rows, int TileWidth, int TileHeight)? Tiles { get; private init; }

    /// <summary>Gets the sample format as printed: <c>RGB</c> (identity matrix), <c>YCbCr, 4:2:0</c>, <c>YCbCr, 4:2:2</c>, <c>YCbCr, 4:4:4</c> or <c>monochrome</c>.</summary>
    public string ColorSpace { get; private init; } = "";

    /// <summary>Gets a value indicating whether the primary image has one plane.</summary>
    public bool IsMonochrome => ColorSpace == "monochrome";

    /// <summary>Gets the bit depth of the luma samples of the primary image (8, 10 or 12); 0 without primary image.</summary>
    public int BitDepth { get; private init; }

    /// <summary>Gets the size of the thumbnail of the primary image, or <see langword="null"/> when it has none.</summary>
    public (int Width, int Height)? Thumbnail { get; private init; }

    /// <summary>Gets the kinds of the color profiles of the primary image, as printed: <c>nclx</c>, <c>prof</c>, <c>rICC</c>.</summary>
    public IReadOnlyList<string> ColorProfiles { get; private init; } = [];

    /// <summary>Gets the color primaries code point (ITU-T H.273) of the <c>nclx</c> profile, or <see langword="null"/> when the image has none.</summary>
    public int? ColorPrimaries { get; private init; }

    /// <summary>Gets the transfer characteristics code point (ITU-T H.273) of the <c>nclx</c> profile, or <see langword="null"/> when the image has none.</summary>
    public int? TransferCharacteristics { get; private init; }

    /// <summary>Gets the matrix coefficients code point (ITU-T H.273) of the <c>nclx</c> profile, or <see langword="null"/> when the image has none.</summary>
    public int? MatrixCoefficients { get; private init; }

    /// <summary>Gets the full range flag of the <c>nclx</c> profile, or <see langword="null"/> when the image has none.</summary>
    public bool? FullRange { get; private init; }

    /// <summary>Gets a value indicating whether the primary image has an alpha plane.</summary>
    public bool HasAlpha { get; private init; }

    /// <summary>Gets a value indicating whether the color samples are premultiplied by the alpha plane.</summary>
    public bool IsPremultiplied { get; private init; }

    /// <summary>Gets the length of the EXIF item as printed (it includes the four bytes of the offset that precedes the TIFF structure), or <see langword="null"/> when absent.</summary>
    public int? ExifLength { get; private init; }

    /// <summary>Gets the length of the XMP item, or <see langword="null"/> when absent.</summary>
    public int? XmpLength { get; private init; }

    /// <summary>Gets the angle of the <c>irot</c> property in degrees anti-clockwise (0, 90, 180 or 270), or <see langword="null"/> when absent.</summary>
    public int? RotationAngle { get; private init; }

    /// <summary>Gets the axis of the <c>imir</c> property as printed (<c>horizontal</c>: left and right are exchanged; <c>vertical</c>: top and bottom), or <see langword="null"/> when absent.</summary>
    public string? Mirror { get; private init; }

    /// <summary>Gets the number of columns and rows that the <c>clap</c> property removes on each side, or <see langword="null"/> when absent.</summary>
    public (int Left, int Top, int Right, int Bottom)? Crop { get; private init; }

    /// <summary>Gets the time scale of the movie (the number of time units per second), or <see langword="null"/> when the file has no track.</summary>
    public long? MovieTimeScale { get; private init; }

    /// <summary>Gets the tracks of the file.</summary>
    public IReadOnlyList<HeifInfoTrack> Tracks { get; private init; } = [];

    /// <summary>Parses the output of <c>heif-info</c>.</summary>
    /// <param name="text">The standard output.</param>
    /// <returns>The description.</returns>
    /// <exception cref="FormatException">The output describes neither a primary image nor a track.</exception>
    public static HeifInfo Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // The block of the primary image ends where the next image, or the first section that is not indented, starts
        var image = PrimaryImageRegex().Match(text);
        var block = image.Groups["block"].Value;
        var tiles = TilesRegex().Match(block);
        var thumbnail = ThumbnailRegex().Match(block);
        var alpha = AlphaRegex().Match(block);
        var crop = CropRegex().Match(text);
        var tracks = new List<HeifInfoTrack>();
        foreach (Match track in TrackRegex().Matches(text))
        {
            var trackBlock = track.Groups["block"].Value;
            var resolution = ResolutionRegex().Match(trackBlock);
            var repetitions = ReadText(trackBlock, "repetitions");
            var hasUnsupportedEditList = repetitions is not null && repetitions.StartsWith("unsupported", StringComparison.Ordinal);
            tracks.Add(new HeifInfoTrack(
                ParseInt(track.Groups["id"].Value),
                HandlerRegex().Match(trackBlock).Groups["handler"].Value,
                resolution.Success ? (ParseInt(resolution.Groups["width"].Value), ParseInt(resolution.Groups["height"].Value)) : null,
                repetitions is null || hasUnsupportedEditList || repetitions.StartsWith("infinite", StringComparison.Ordinal) ? null : ParseInt(LeadingNumberRegex().Match(repetitions).Value),
                ReadText(trackBlock, "sample entry type") ?? "")
            {
                HasUnsupportedEditList = hasUnsupportedEditList,
            });
        }

        if (!image.Success && tracks.Count == 0)
            throw new FormatException($"heif-info printed no primary image and no track: {text}");

        return new HeifInfo
        {
            HasPrimaryImage = image.Success,
            MimeType = ReadText(text, "MIME type") ?? "",
            MajorBrand = ReadText(text, "main brand") ?? "",
            CompatibleBrands = [.. (ReadText(text, "compatible brands") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
            Width = image.Success ? ParseInt(image.Groups["width"].Value) : 0,
            Height = image.Success ? ParseInt(image.Groups["height"].Value) : 0,
            Tiles = tiles.Success ? (ParseInt(tiles.Groups["columns"].Value), ParseInt(tiles.Groups["rows"].Value), ParseInt(tiles.Groups["width"].Value), ParseInt(tiles.Groups["height"].Value)) : null,
            ColorSpace = ReadText(block, "colorspace") ?? "",
            BitDepth = ReadNumber(block, "bit depth") ?? (image.Success ? throw new FormatException($"heif-info printed no bit depth: {text}") : 0),
            Thumbnail = thumbnail.Success ? (ParseInt(thumbnail.Groups["width"].Value), ParseInt(thumbnail.Groups["height"].Value)) : null,
            ColorProfiles = [.. ColorProfileRegex().Matches(block).Select(match => match.Groups["kind"].Value)],
            ColorPrimaries = ReadNumber(block, "colour primaries"),
            TransferCharacteristics = ReadNumber(block, "transfer characteristics"),
            MatrixCoefficients = ReadNumber(block, "matrix coefficients"),
            FullRange = ReadNumber(block, "full range flag") is { } fullRange ? fullRange != 0 : null,
            HasAlpha = alpha.Success && alpha.Groups["value"].Value == "yes",
            IsPremultiplied = alpha.Success && alpha.Groups["premultiplied"].Success,
            ExifLength = ReadNumber(text, "Exif"),
            XmpLength = ReadNumber(text, "XMP"),
            RotationAngle = ReadNumber(text, @"angle \(ccw\)"),
            Mirror = ReadText(text, "mirror"),
            Crop = crop.Success ? (ParseInt(crop.Groups["left"].Value), ParseInt(crop.Groups["top"].Value), ParseInt(crop.Groups["right"].Value), ParseInt(crop.Groups["bottom"].Value)) : null,
            MovieTimeScale = ReadNumber(text, "sequence time scale"),
            Tracks = tracks,
        };
    }

    private static int ParseInt(string value) => int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);

    // The text that follows "label:" on its line
    private static string? ReadText(string text, string label)
    {
        var match = Regex.Match(text, $@"^\s*{label}:[ \t]*(?<value>\S.*?)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));
        return match.Success ? match.Groups["value"].Value : null;
    }

    // The number that follows "label:" on its line
    private static int? ReadNumber(string text, string label)
        => ReadText(text, label) is { } value && LeadingNumberRegex().Match(value) is { Success: true } number ? ParseInt(number.Value) : null;

    [GeneratedRegex(@"^image: (?<width>\d+)x(?<height>\d+) \(id=\d+\), primary[^\n]*\n(?<block>(?:[ \t]+[^\n]*\n?)*)", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PrimaryImageRegex();

    [GeneratedRegex(@"^\s*tiles: (?<columns>\d+)x(?<rows>\d+), tile size: (?<width>\d+)x(?<height>\d+)", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TilesRegex();

    [GeneratedRegex(@"^\s*thumbnail: (?<width>\d+)x(?<height>\d+)", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ThumbnailRegex();

    [GeneratedRegex(@"^\s*color profile: (?<kind>\w+)", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ColorProfileRegex();

    [GeneratedRegex(@"^\s*alpha channel: (?<value>yes|no)\s*(?<premultiplied>\(premultiplied\))?", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AlphaRegex();

    [GeneratedRegex(@"^\s*crop: left=(?<left>\d+) top=(?<top>\d+) right=(?<right>\d+) bottom=(?<bottom>\d+)", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CropRegex();

    [GeneratedRegex(@"^track (?<id>\d+)[^\n]*\n(?<block>(?:[ \t]+[^\n]*\n?)*)", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TrackRegex();

    [GeneratedRegex(@"handler: '(?<handler>[^']*)'", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HandlerRegex();

    [GeneratedRegex(@"resolution: (?<width>\d+)x(?<height>\d+)", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ResolutionRegex();

    [GeneratedRegex(@"^\d+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LeadingNumberRegex();
}
