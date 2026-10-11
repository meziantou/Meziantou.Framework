using System.Globalization;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>
/// The description printed by <c>jxlinfo -v</c> (libjxl's own parser): the boxes of the container, the fields of the image
/// header, the color encoding and the frames of an animation or of a layered image.
/// </summary>
public sealed partial class JxlInfo
{
    /// <summary>Gets a value indicating whether the file is a container (ISO/IEC 18181-2) and not a bare codestream.</summary>
    public bool IsContainer { get; private init; }

    /// <summary>Gets the boxes of the container, in file order (empty for a bare codestream).</summary>
    public IReadOnlyList<JxlInfoBox> Boxes { get; private init; } = [];

    /// <summary>Gets the metadata boxes jxlinfo recognized (<c>Exif</c>, <c>xml </c>, <c>jumb</c>), in file order.</summary>
    public IReadOnlyList<JxlInfoMetadata> Metadata { get; private init; } = [];

    /// <summary>Gets a value indicating whether the file holds JPEG bitstream reconstruction data (<c>jbrd</c>).</summary>
    public bool HasJpegReconstruction { get; private init; }

    /// <summary>Gets a value indicating whether the image header declares an animation.</summary>
    public bool IsAnimation { get; private init; }

    /// <summary>Gets the width of the image, before the orientation is applied.</summary>
    public int Width { get; private init; }

    /// <summary>Gets the height of the image, before the orientation is applied.</summary>
    public int Height { get; private init; }

    /// <summary>Gets a value indicating whether the image is stored in the XYB color space, which jxlinfo prints as <c>lossy</c>.</summary>
    public bool IsXybEncoded { get; private init; }

    /// <summary>Gets the bit depth of the color samples.</summary>
    public int BitsPerSample { get; private init; }

    /// <summary>Gets the number of exponent bits of floating-point samples, or 0 for integer samples.</summary>
    public int ExponentBitsPerSample { get; private init; }

    /// <summary>Gets the number of color channels: 1 (grayscale) or 3.</summary>
    public int ColorChannelCount { get; private init; }

    /// <summary>Gets the extra channels, in order.</summary>
    public IReadOnlyList<JxlInfoExtraChannel> ExtraChannels { get; private init; } = [];

    /// <summary>Gets a value indicating whether the image has an alpha channel.</summary>
    public bool HasAlpha => ExtraChannels.Any(channel => channel.Type == "Alpha");

    /// <summary>Gets the size of the preview image, or <see langword="null"/> when the file has none.</summary>
    public (int Width, int Height)? Preview { get; private init; }

    /// <summary>Gets the orientation (1 to 8, as in EXIF).</summary>
    public int Orientation { get; private init; }

    /// <summary>Gets the numerator of the number of ticks per second of an animation.</summary>
    public long TicksPerSecondNumerator { get; private init; }

    /// <summary>Gets the denominator of the number of ticks per second of an animation.</summary>
    public long TicksPerSecondDenominator { get; private init; }

    /// <summary>Gets the <c>num_loops</c> field of an animation (0: the animation repeats forever).</summary>
    public long LoopCount { get; private init; }

    /// <summary>Gets the length of the embedded ICC profile, or <see langword="null"/> when the color encoding is enumerated.</summary>
    public long? IccProfileLength { get; private init; }

    /// <summary>Gets the color space as printed (<c>RGB</c>, <c>Grayscale</c>, <c>XYB</c>, <c>Unknown</c>), or <see langword="null"/> with an ICC profile.</summary>
    public string? ColorSpace { get; private init; }

    /// <summary>Gets the white point as printed (<c>D65</c>, <c>Custom</c>, <c>E</c>, <c>P3</c>), or <see langword="null"/> with an ICC profile.</summary>
    public string? WhitePoint { get; private init; }

    /// <summary>Gets the primaries as printed (<c>sRGB</c>, <c>Custom</c>, <c>Rec.2100</c>, <c>P3</c>), or <see langword="null"/> for grayscale images and ICC profiles.</summary>
    public string? Primaries { get; private init; }

    /// <summary>Gets the transfer function as printed (<c>sRGB</c>, <c>Linear</c>, <c>PQ</c>, <c>gamma(0.454550)</c>, ...), or <see langword="null"/> with an ICC profile.</summary>
    public string? TransferFunction { get; private init; }

    /// <summary>
    /// Gets the frames jxlinfo lists, in order: every frame of an animation and every layer. A still image made of one
    /// frame of the size of the image has none.
    /// </summary>
    public IReadOnlyList<JxlInfoFrame> Frames { get; private init; } = [];

    /// <summary>Parses the standard output of <c>jxlinfo -v</c>.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The description.</returns>
    /// <exception cref="FormatException">The text does not have the expected form.</exception>
    public static JxlInfo Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var header = HeaderRegex().Match(text);
        if (!header.Success)
            throw new FormatException($"jxlinfo printed no image description: {text}");

        var boxes = new List<JxlInfoBox>();
        foreach (Match box in BoxRegex().Matches(text))
        {
            boxes.Add(new JxlInfoBox(box.Groups["type"].Value, ParseInt64(box, "size")));
        }

        var metadata = new List<JxlInfoMetadata>();
        foreach (Match item in MetadataRegex().Matches(text))
        {
            metadata.Add(new JxlInfoMetadata(item.Groups["type"].Value, item.Groups["kind"].Value == "Brotli-compressed", ParseInt64(item, "length")));
        }

        var extraChannels = new List<JxlInfoExtraChannel>();
        foreach (Match channel in ExtraChannelRegex().Matches(text))
        {
            extraChannels.Add(new JxlInfoExtraChannel(
                channel.Groups["type"].Value,
                (int)ParseInt64(channel, "bits"),
                channel.Groups["premultiplied"].Success ? channel.Groups["premultiplied"].Value == "1" : null));
        }

        var frames = new List<JxlInfoFrame>();
        foreach (Match frame in FrameRegex().Matches(text))
        {
            var isFullSize = !frame.Groups["width"].Success;
            frames.Add(new JxlInfoFrame(
                IsLayer: frame.Groups["kind"].Value == "Layer",
                IsFullSize: isFullSize,
                Width: isFullSize ? (int)ParseInt64(header, "width") : (int)ParseInt64(frame, "width"),
                Height: isFullSize ? (int)ParseInt64(header, "height") : (int)ParseInt64(frame, "height"),
                X: isFullSize ? 0 : (int)ParseInt64(frame, "x"),
                Y: isFullSize ? 0 : (int)ParseInt64(frame, "y"),
                DurationMilliseconds: frame.Groups["duration"].Success ? double.Parse(frame.Groups["duration"].Value, CultureInfo.InvariantCulture) : null,
                Name: frame.Groups["name"].Success ? frame.Groups["name"].Value : null));
        }

        var ticks = TicksRegex().Match(text);
        var preview = PreviewRegex().Match(text);
        var icc = IccRegex().Match(text);
        var exponent = ExponentRegex().Match(text);
        return new JxlInfo
        {
            IsContainer = text.Contains("JPEG XL file format container", StringComparison.Ordinal),
            Boxes = boxes,
            Metadata = metadata,
            HasJpegReconstruction = text.Contains("JPEG bitstream reconstruction data available", StringComparison.Ordinal),
            IsAnimation = header.Groups["kind"].Value == "animation",
            Width = (int)ParseInt64(header, "width"),
            Height = (int)ParseInt64(header, "height"),
            IsXybEncoded = header.Groups["coding"].Value == "lossy",
            BitsPerSample = (int)ParseInt64(header, "bits"),
            ExponentBitsPerSample = exponent.Success ? (int)ParseInt64(exponent, "bits") : 0,
            ColorChannelCount = (int)ParseRequired(ColorChannelsRegex(), text, "the number of color channels"),
            ExtraChannels = extraChannels,
            Preview = preview.Success ? ((int)ParseInt64(preview, "width"), (int)ParseInt64(preview, "height")) : null,
            Orientation = (int)ParseRequired(OrientationRegex(), text, "the orientation"),
            TicksPerSecondNumerator = ticks.Success ? ParseInt64(ticks, "numerator") : 0,
            TicksPerSecondDenominator = ticks.Success ? ParseInt64(ticks, "denominator") : 0,
            LoopCount = LoopsRegex().Match(text) is { Success: true } loops ? ParseInt64(loops, "value") : 0,
            IccProfileLength = icc.Success ? ParseInt64(icc, "length") : null,
            ColorSpace = icc.Success ? null : GetOptional(ColorSpaceRegex(), text),
            WhitePoint = GetOptional(WhitePointRegex(), text),
            Primaries = GetOptional(PrimariesRegex(), text),
            TransferFunction = GetOptional(TransferFunctionRegex(), text),
            Frames = frames,
        };
    }

    private static long ParseInt64(Match match, string group) => long.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

    private static long ParseRequired(Regex regex, string text, string description)
    {
        var match = regex.Match(text);
        if (!match.Success)
            throw new FormatException($"jxlinfo did not print {description}: {text}");

        return ParseInt64(match, "value");
    }

    private static string? GetOptional(Regex regex, string text)
    {
        var match = regex.Match(text);
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    [GeneratedRegex(@"^JPEG XL (?<kind>image|animation), (?<width>\d+)x(?<height>\d+), (?<coding>lossy|\(possibly\) lossless), (?<bits>\d+)-bit ", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HeaderRegex();

    [GeneratedRegex(@"^Box:\r?\n  type: ""(?<type>.{4})""\r?\n  size: (?<size>\d+)", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BoxRegex();

    [GeneratedRegex(@"^(?<kind>Uncompressed|Brotli-compressed) (?<type>.{4}) metadata: (?<length>\d+)", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MetadataRegex();

    [GeneratedRegex(@"^Extra channel \d+:\r?\n  type: (?<type>[^\r\n]+)\r?\n  bits per sample: (?<bits>\d+)(\r?\n  (?!alpha premultiplied)[^\r\n]+)*(\r?\n  alpha premultiplied: (?<premultiplied>\d))?", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ExtraChannelRegex();

    [GeneratedRegex(@"^(?<kind>Frame|Layer): ((?<width>\d+)x(?<height>\d+) at position \((?<x>-?\d+),(?<y>-?\d+)\)|Full image size)(, duration: (?<duration>[\d.]+) ms)?(, time code: [0-9A-F]+)?(, name: ""(?<name>[^""]*)"")?\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FrameRegex();

    [GeneratedRegex(@"^Ticks per second \(numerator / denominator\): (?<numerator>\d+) / (?<denominator>\d+)", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TicksRegex();

    [GeneratedRegex(@"^Num loops: (?<value>\d+)", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LoopsRegex();

    [GeneratedRegex(@"^Preview image: (?<width>\d+)x(?<height>\d+)", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PreviewRegex();

    [GeneratedRegex(@"^(?<length>\d+)-byte ICC profile", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex IccRegex();

    [GeneratedRegex(@"-bit float \((?<bits>\d+) exponent bits\)", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ExponentRegex();

    [GeneratedRegex(@"^Number of color channels: (?<value>\d+)", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ColorChannelsRegex();

    [GeneratedRegex(@"^Orientation: (?<value>\d)", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex OrientationRegex();

    [GeneratedRegex(@"^Color space: (?<value>[^\r\n]+)", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ColorSpaceRegex();

    [GeneratedRegex(@"^White point: (?<value>[^\r\n]+)", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WhitePointRegex();

    [GeneratedRegex(@"^Primaries: (?<value>[^\r\n]+)", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PrimariesRegex();

    [GeneratedRegex(@"^Transfer function: (?<value>[^\r\n]+)", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TransferFunctionRegex();
}
