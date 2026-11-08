namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>
/// Writes OpenType Layout tables of one lookup: OpenType specification, "OpenType Layout Common Table Formats", "GSUB"
/// and "GPOS". They are what a font needs to tell which table a shaper uses, not a general writer.
/// </summary>
internal static class FontLayoutWriter
{
    /// <summary>Writes a <c>GSUB</c> table whose feature replaces a glyph by another one (single substitution, format 2).</summary>
    /// <param name="script">The script of the table, or <see langword="null"/> for a table without script.</param>
    /// <param name="feature">The feature of the default language system of the script.</param>
    /// <param name="from">The glyph replaced.</param>
    /// <param name="to">The glyph that replaces it.</param>
    public static byte[] WriteSingleSubstitution(string? script, string feature, int from, int to)
    {
        var subtable = new FontWriter();
        subtable.UInt16(2);
        subtable.UInt16(8);
        subtable.UInt16(1);
        subtable.UInt16(to);
        subtable.UInt16(1);
        subtable.UInt16(1);
        subtable.UInt16(from);
        return WriteTable(script, feature, lookupType: 1, subtable.ToArray());
    }

    /// <summary>Writes a <c>GPOS</c> table whose feature adjusts the advance of the first glyph of a pair (pair adjustment, format 1).</summary>
    public static byte[] WritePairAdjustment(string? script, string feature, int first, int second, int advance)
    {
        var subtable = new FontWriter();
        subtable.UInt16(1);
        subtable.UInt16(18);
        subtable.UInt16(4);
        subtable.UInt16(0);
        subtable.UInt16(1);
        subtable.UInt16(12);
        subtable.UInt16(1);
        subtable.UInt16(second);
        subtable.Int16(advance);
        subtable.UInt16(1);
        subtable.UInt16(1);
        subtable.UInt16(first);
        return WriteTable(script, feature, lookupType: 2, subtable.ToArray());
    }

    /// <summary>Writes a layout table: a script whose default language system has one feature, which has one lookup of one subtable.</summary>
    private static byte[] WriteTable(string? script, string feature, int lookupType, byte[] subtable)
    {
        const int ScriptList = 10;

        var scriptCount = script is null ? 0 : 1;
        var featureList = ScriptList + 2 + (scriptCount * (6 + 4 + 6 + 2));
        var lookupList = featureList + 2 + 6 + 6;
        var writer = new FontWriter();
        writer.UInt16(1);
        writer.UInt16(0);
        writer.UInt16(ScriptList);
        writer.UInt16(featureList);
        writer.UInt16(lookupList);

        // The script list: the script and its default language system, which has the feature 0.
        writer.UInt16(scriptCount);
        if (script is not null)
        {
            writer.Tag(script);
            writer.UInt16(8);
            writer.UInt16(4);
            writer.UInt16(0);
            writer.UInt16(0);
            writer.UInt16(0xFFFF);
            writer.UInt16(1);
            writer.UInt16(0);
        }

        // The feature list: the feature and its lookup 0.
        writer.UInt16(1);
        writer.Tag(feature);
        writer.UInt16(8);
        writer.UInt16(0);
        writer.UInt16(1);
        writer.UInt16(0);

        // The lookup list: the lookup and its subtable.
        writer.UInt16(1);
        writer.UInt16(4);
        writer.UInt16(lookupType);
        writer.UInt16(0);
        writer.UInt16(1);
        writer.UInt16(8);
        writer.Bytes(subtable);
        return writer.ToArray();
    }
}
