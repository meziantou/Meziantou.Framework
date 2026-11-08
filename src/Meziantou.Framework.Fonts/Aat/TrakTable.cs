namespace Meziantou.Framework.Fonts;

/// <summary>
/// The tracking table of Apple Advanced Typography (<c>trak</c>): Apple TrueType Reference Manual, "The 'trak' table".
/// It gives, for a few sizes, the space added to the advance of every glyph: more space at small sizes, less at large
/// ones.
/// </summary>
/// <remarks>
/// Only the normal track (track 0) is used: it is the spacing of a text whose tracking nobody chose. Between two sizes
/// of the table, the value is interpolated; out of their range, it is the value of the nearest size.
/// </remarks>
internal sealed class TrakTable
{
    private readonly Track? _horizontal;
    private readonly Track? _vertical;

    private TrakTable(Track? horizontal, Track? vertical)
    {
        _horizontal = horizontal;
        _vertical = vertical;
    }

    /// <exception cref="InvalidFontException">The table is malformed.</exception>
    public static TrakTable Read(ReadOnlySpan<byte> table)
    {
        if (FontBinary.UInt32(table, 0) != 0x00010000 || FontBinary.UInt16(table, 4) != 0)
            throw FontErrors.InvalidTable(FontTags.Trak, "the version is not supported.");

        var horizontal = FontBinary.UInt16(table, 6);
        var vertical = FontBinary.UInt16(table, 8);
        return new TrakTable(horizontal == 0 ? null : ReadTrack(table, horizontal), vertical == 0 ? null : ReadTrack(table, vertical));
    }

    /// <summary>Gets the space the normal track adds to the advance of every glyph at a size, in font units.</summary>
    /// <param name="pointSize">The size of the text, in points (the size of an em).</param>
    /// <param name="isVertical"><see langword="true"/> for the advances of a vertical line.</param>
    public int GetTracking(double pointSize, bool isVertical)
    {
        var track = isVertical ? _vertical : _horizontal;
        if (track is null || track.Sizes.Length == 0)
            return 0;

        var sizes = track.Sizes;
        var values = track.Values;
        if (pointSize <= sizes[0])
            return values[0];

        for (var i = 1; i < sizes.Length; i++)
        {
            if (pointSize <= sizes[i])
            {
                var ratio = (pointSize - sizes[i - 1]) / (sizes[i] - sizes[i - 1]);
                return (int)Math.Round(values[i - 1] + (ratio * (values[i] - values[i - 1])), MidpointRounding.AwayFromZero);
            }
        }

        return values[^1];
    }

    /// <summary>Reads the sizes and the values of the normal track of a track data table.</summary>
    private static Track? ReadTrack(ReadOnlySpan<byte> table, int offset)
    {
        var trackCount = FontBinary.UInt16(table, offset);
        var sizeCount = FontBinary.UInt16(table, offset + 2);
        var sizeTable = FontBinary.Offset32(table, offset + 4);
        for (var i = 0; i < trackCount; i++)
        {
            var entry = offset + 8 + (i * 8);
            if (FontBinary.Int32(table, entry) != 0)
                continue;

            var valuesOffset = FontBinary.UInt16(table, entry + 6);
            var sizes = new double[sizeCount];
            var values = new int[sizeCount];
            for (var j = 0; j < sizeCount; j++)
            {
                sizes[j] = FontBinary.Int32(table, sizeTable + (j * 4)) / 65536.0;
                values[j] = FontBinary.Int16(table, valuesOffset + (j * 2));
            }

            // The sizes are in increasing order; a table whose sizes are not has no track that can be used.
            for (var j = 1; j < sizeCount; j++)
            {
                if (sizes[j] <= sizes[j - 1])
                    return null;
            }

            return new Track(sizes, values);
        }

        return null;
    }

    private sealed record Track(double[] Sizes, int[] Values);
}
