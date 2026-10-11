using System.Globalization;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>
/// The description printed by <c>avifdec --info</c> (libavif's own container parser, after it decoded every frame): the
/// sample format, the color description, the metadata payloads, the transformations and the timing of an image sequence.
/// </summary>
public sealed partial class AvifDecInfo
{
    /// <summary>Gets the width of the image, before any transformation.</summary>
    public int Width { get; private init; }

    /// <summary>Gets the height of the image, before any transformation.</summary>
    public int Height { get; private init; }

    /// <summary>Gets the bit depth of the samples (8, 10 or 12).</summary>
    public int BitDepth { get; private init; }

    /// <summary>Gets the sample format as printed: <c>YUV444</c>, <c>YUV422</c>, <c>YUV420</c> or <c>YUV400</c>.</summary>
    public string Format { get; private init; } = "";

    /// <summary>Gets the alpha description as printed: <c>Absent</c>, <c>Not premultiplied</c> or <c>Premultiplied</c>.</summary>
    public string Alpha { get; private init; } = "";

    /// <summary>Gets a value indicating whether the file has an alpha plane.</summary>
    public bool HasAlpha => Alpha != "Absent";

    /// <summary>Gets a value indicating whether the color samples use the full range.</summary>
    public bool FullRange { get; private init; }

    /// <summary>Gets the color primaries code point (ITU-T H.273).</summary>
    public int ColorPrimaries { get; private init; }

    /// <summary>Gets the transfer characteristics code point (ITU-T H.273).</summary>
    public int TransferCharacteristics { get; private init; }

    /// <summary>Gets the matrix coefficients code point (ITU-T H.273).</summary>
    public int MatrixCoefficients { get; private init; }

    /// <summary>Gets the length of the ICC profile, or <see langword="null"/> when absent.</summary>
    public int? IccProfileLength { get; private init; }

    /// <summary>Gets the length of the XMP payload, or <see langword="null"/> when absent.</summary>
    public int? XmpLength { get; private init; }

    /// <summary>Gets the length of the EXIF payload, or <see langword="null"/> when absent.</summary>
    public int? ExifLength { get; private init; }

    /// <summary>Gets the angle of the <c>irot</c> property (0 to 3, in units of 90 degrees anti-clockwise), or <see langword="null"/> when absent.</summary>
    public int? Rotation { get; private init; }

    /// <summary>Gets the axis of the <c>imir</c> property (0: top and bottom are exchanged; 1: left and right), or <see langword="null"/> when absent.</summary>
    public int? Mirror { get; private init; }

    /// <summary>Gets the crop rectangle libavif derives from a valid <c>clap</c> property, or <see langword="null"/>.</summary>
    public (int X, int Y, int Width, int Height)? CleanApertureCrop { get; private init; }

    /// <summary>Gets a value indicating whether a repetition count is printed, which libavif does for image sequences only.</summary>
    public bool HasRepetitionCount { get; private init; }

    /// <summary>
    /// Gets the repetition count of an image sequence (the number of times it is played again after the first time), or
    /// <see langword="null"/> when it repeats forever, when libavif does not know it
    /// (<see cref="IsRepetitionCountUnknown"/>) or when the file is not an image sequence (<see cref="HasRepetitionCount"/>).
    /// </summary>
    public int? RepetitionCount { get; private init; }

    /// <summary>
    /// Gets a value indicating whether libavif prints that it does not know the repetition count, which it does for an image
    /// sequence whose color track has no edit list.
    /// </summary>
    public bool IsRepetitionCountUnknown { get; private init; }

    /// <summary>Gets the time scale: the number of time units per second.</summary>
    public long TimeScale { get; private init; }

    /// <summary>Gets the number of frames libavif announces.</summary>
    public int FrameCount { get; private init; }

    /// <summary>Gets the frames libavif decoded, in order.</summary>
    public IReadOnlyList<AvifDecFrame> Frames { get; private init; } = [];

    /// <summary>Parses the output.</summary>
    /// <param name="text">The standard output of <c>avifdec --info</c>.</param>
    /// <returns>The description.</returns>
    /// <exception cref="FormatException">A mandatory line is missing.</exception>
    public static AvifDecInfo Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // " * Name : value" lines; the first occurrence of a name is the one of the image
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in FieldRegex().Matches(text))
        {
            fields.TryAdd(match.Groups["name"].Value, match.Groups["value"].Value);
        }

        var resolution = Required(ResolutionRegex(), text, "Resolution");
        var timing = Required(TimingRegex(), text, "timescales per second");
        var crop = CropRegex().Match(text);
        var frames = new List<AvifDecFrame>();
        foreach (Match match in FrameRegex().Matches(text))
        {
            frames.Add(new AvifDecFrame(Int(match, "index"), long.Parse(match.Groups["pts"].Value, CultureInfo.InvariantCulture), long.Parse(match.Groups["duration"].Value, CultureInfo.InvariantCulture), Int(match, "width"), Int(match, "height")));
        }

        var hasRepetitionCount = fields.TryGetValue("Repeat Count", out var repetition);
        return new AvifDecInfo
        {
            Width = Int(resolution, "width"),
            Height = Int(resolution, "height"),
            BitDepth = Number(fields, text, "Bit Depth"),
            Format = Field(fields, text, "Format"),
            Alpha = Field(fields, text, "Alpha"),
            FullRange = Field(fields, text, "Range") switch
            {
                "Full" => true,
                "Limited" => false,
                var value => throw new FormatException($"Unexpected range '{value}' in the output of avifdec --info."),
            },
            ColorPrimaries = Number(fields, text, "Color Primaries"),
            TransferCharacteristics = Number(fields, text, "Transfer Char."),
            MatrixCoefficients = Number(fields, text, "Matrix Coeffs."),
            IccProfileLength = PayloadLength(fields, text, "ICC Profile"),
            XmpLength = PayloadLength(fields, text, "XMP Metadata"),
            ExifLength = PayloadLength(fields, text, "Exif Metadata"),
            Rotation = OptionalInt(RotationRegex().Match(text)),
            Mirror = OptionalInt(MirrorRegex().Match(text)),
            CleanApertureCrop = crop.Success ? (Int(crop, "x"), Int(crop, "y"), Int(crop, "width"), Int(crop, "height")) : null,
            HasRepetitionCount = hasRepetitionCount,
            RepetitionCount = hasRepetitionCount && repetition is not ("Infinite" or "Unknown") ? int.Parse(repetition!, CultureInfo.InvariantCulture) : null,
            IsRepetitionCountUnknown = hasRepetitionCount && repetition is "Unknown",
            TimeScale = long.Parse(timing.Groups["timescale"].Value, CultureInfo.InvariantCulture),
            FrameCount = Int(timing, "frames"),
            Frames = frames,
        };
    }

    private static string Field(Dictionary<string, string> fields, string text, string name)
        => fields.TryGetValue(name, out var value) ? value : throw new FormatException($"The output of avifdec --info has no '{name}' line:\n{text}");

    private static int Number(Dictionary<string, string> fields, string text, string name) => int.Parse(Field(fields, text, name), CultureInfo.InvariantCulture);

    private static int? PayloadLength(Dictionary<string, string> fields, string text, string name)
    {
        var value = Field(fields, text, name);
        if (value == "Absent")
            return null;

        var match = PresentRegex().Match(value);
        if (!match.Success)
            throw new FormatException($"Unexpected value '{value}' for '{name}' in the output of avifdec --info.");

        return Int(match, "length");
    }

    private static Match Required(Regex regex, string text, string name)
    {
        var match = regex.Match(text);
        return match.Success ? match : throw new FormatException($"The output of avifdec --info has no '{name}' line:\n{text}");
    }

    private static int Int(Match match, string group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

    private static int? OptionalInt(Match match) => match.Success ? Int(match, "value") : null;

    [GeneratedRegex(@"^ \* (?<name>[^:\r\n]+?)\s*: (?<value>[^\r\n]*?)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FieldRegex();

    [GeneratedRegex(@"^ \* Resolution\s*: (?<width>\d+)x(?<height>\d+)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ResolutionRegex();

    [GeneratedRegex(@"^Present \((?<length>\d+) bytes\)$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PresentRegex();

    [GeneratedRegex(@"^\s+\* irot \(Rotation\)\s*: (?<value>\d+)", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RotationRegex();

    [GeneratedRegex(@"^\s+\* imir \(Mirror\)\s*: (?<value>\d+)", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MirrorRegex();

    [GeneratedRegex(@"^\s+\* Valid, derived crop rect: X: (?<x>\d+), Y: (?<y>\d+), W: (?<width>\d+), H: (?<height>\d+)", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CropRegex();

    [GeneratedRegex(@"^ \* (?<timescale>\d+) timescales per second, [^\r\n]*, (?<frames>\d+) frames?\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TimingRegex();

    [GeneratedRegex(@"^\s+\* Decoded frame \[(?<index>\d+)\] \[pts [^\(]*\((?<pts>\d+) timescales\)\] \[duration [^\(]*\((?<duration>\d+) timescales\)\] \[(?<width>\d+)x(?<height>\d+)\]", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FrameRegex();
}
