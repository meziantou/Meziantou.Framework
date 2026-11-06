using System.Collections.Concurrent;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The correction of the coverage of the glyph masks (<see cref="Typography.GlyphRunOptions.Contrast"/>,
/// <see cref="Typography.GlyphRunOptions.Gamma"/>): the lookup table of Skia (<c>SkTMaskGamma_build_correcting_lut</c>) for a
/// black text on a white background, whatever the color of the text: the coverage c is raised by the contrast to
/// <c>s = c + contrast × c × (1 - c)</c>, then to <c>1 - (1 - s)^(1 / gamma)</c>.
/// </summary>
internal static class GlyphCoverageCorrection
{
    /// <summary>The number of intervals of the table: the coverage is interpolated between its entries.</summary>
    private const int Steps = 256;

    private static readonly ConcurrentDictionary<(double Contrast, double Gamma), uint[]> Tables = new();

    /// <summary>Returns the table of a correction (its 257 entries map the coverage <c>i / 256</c> to the corrected one, in units of <see cref="CoverageRasterizer.FullCoverage"/>).</summary>
    public static uint[] GetTable(double contrast, double gamma) => Tables.GetOrAdd((contrast, gamma), static key => Build(key.Contrast, key.Gamma));

    private static uint[] Build(double contrast, double gamma)
    {
        var table = new uint[Steps + 1];
        for (var i = 0; i <= Steps; i++)
        {
            var coverage = (double)i / Steps;
            var raised = coverage + ((1 - coverage) * contrast * coverage);
            var result = 1 - Math.Pow(1 - raised, 1 / gamma);
            table[i] = (uint)Math.Round(Math.Clamp(result, 0, 1) * CoverageRasterizer.FullCoverage, MidpointRounding.AwayFromZero);
        }

        return table;
    }

    /// <summary>Writes the corrected coverages of a span (interpolated between the entries of the table).</summary>
    public static void Apply(uint[] table, ReadOnlySpan<uint> coverage, Span<uint> destination)
    {
        for (var i = 0; i < coverage.Length; i++)
        {
            var value = Math.Min(coverage[i], CoverageRasterizer.FullCoverage);
            var index = (int)(value >> 8);
            if (index >= Steps)
            {
                destination[i] = table[Steps];
                continue;
            }

            var low = table[index];
            var high = table[index + 1];
            destination[i] = (uint)(low + ((((long)high - low) * (value & 0xFF)) >> 8));
        }
    }
}
