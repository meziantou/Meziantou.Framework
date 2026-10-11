namespace Meziantou.Framework.Imaging.Tests.Codecs;

/// <summary>A progressive scan written by <see cref="JpegTestImage.Encode"/> (ITU-T T.81 G.1.1.1 scan parameters).</summary>
/// <param name="Components">The component indices of the scan (several only for DC scans).</param>
/// <param name="Start">The first coefficient of the band, zig-zag order (<c>Ss</c>).</param>
/// <param name="End">The last coefficient of the band (<c>Se</c>).</param>
/// <param name="High">The bit position of the previous scan of the band (<c>Ah</c>), 0 for a first scan.</param>
/// <param name="Low">The bit position of this scan (<c>Al</c>).</param>
internal sealed record JpegTestProgressiveScan(int[] Components, int Start, int End, int High, int Low)
{
    /// <summary>Gets a scan from the cjpeg scan-script notation: <c>"0,1,2: 0 0 0 1"</c>.</summary>
    public static JpegTestProgressiveScan Parse(string text)
    {
        var parts = text.Split(':');
        var components = parts[0].Split(',').Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        var values = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        return new JpegTestProgressiveScan(components, values[0], values[1], values[2], values[3]);
    }

    /// <summary>Gets scans from a script of scans separated by semicolons.</summary>
    public static JpegTestProgressiveScan[] ParseScript(string script)
        => [.. script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Parse)];

    public override string ToString() => string.Join(',', Components) + ": " + string.Create(CultureInfo.InvariantCulture, $"{Start} {End} {High} {Low}");
}
