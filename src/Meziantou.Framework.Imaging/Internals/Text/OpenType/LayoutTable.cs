namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The structure the GSUB and GPOS tables share: scripts, language systems, features and lookups. OpenType
/// specification, "OpenType Layout Common Table Formats".
/// </summary>
/// <remarks>
/// The header is validated when the table is read; everything else is validated when it is used, and a reference
/// outside the table is an <see cref="InvalidImageContentException"/>.
/// </remarks>
internal sealed class LayoutTable
{
    /// <summary>The index of a feature that is not required: a language system that has no required feature.</summary>
    public const int NoFeature = 0xFFFF;

    private readonly int _scripts;
    private readonly int _features;
    private readonly int _lookups;
    private readonly int _featureVariations;

    private LayoutTable(int scripts, int features, int lookups, int featureVariations)
    {
        _scripts = scripts;
        _features = features;
        _lookups = lookups;
        _featureVariations = featureVariations;
    }

    /// <summary>Reads the header of a GSUB or GPOS table.</summary>
    /// <returns>The table, or <see langword="null"/> when its version is not known or it has no lookup: the font is then used without it.</returns>
    public static LayoutTable? Read(ReadOnlySpan<byte> table)
    {
        if (table.Length < 10 || FontBinary.UInt16(table, 0) != 1)
            return null;

        var scripts = FontBinary.UInt16(table, 4);
        var features = FontBinary.UInt16(table, 6);
        var lookups = FontBinary.UInt16(table, 8);
        if (scripts == 0 || features == 0 || lookups == 0 || Math.Max(scripts, Math.Max(features, lookups)) > table.Length - 2)
            return null;

        // Version 1.1 adds the feature variations: features that are replaced in a part of the variation space.
        var featureVariations = 0;
        if (FontBinary.UInt16(table, 2) >= 1 && table.Length >= 14)
        {
            var offset = FontBinary.UInt32(table, 10);
            featureVariations = offset != 0 && offset <= table.Length - 8 && FontBinary.UInt16(table, (int)offset) == 1 ? (int)offset : 0;
        }

        return new LayoutTable(scripts, features, lookups, featureVariations);
    }

    /// <summary>
    /// Finds the feature substitutions that apply to an instance of a variable font: "FeatureVariations table". The
    /// first record whose conditions all hold is the one that applies.
    /// </summary>
    /// <param name="table">The table.</param>
    /// <param name="coordinates">The normalized coordinates of the instance, as 2.14 fixed-point numbers; an axis without coordinate is at its default.</param>
    /// <returns>The position of the feature table substitution, or 0 when no feature is replaced.</returns>
    public int FindFeatureSubstitutions(ReadOnlySpan<byte> table, ReadOnlySpan<int> coordinates)
    {
        if (_featureVariations == 0)
            return 0;

        var count = FontBinary.UInt32(table, _featureVariations + 4);
        var records = FontBinary.Slice(table, _featureVariations + 8, (long)count * 8);
        for (var i = 0; i < (int)count; i++)
        {
            var conditions = FontBinary.Offset32(records, i * 8);
            if (conditions != 0 && !MatchesConditions(table, FontBinary.AddOffset(_featureVariations, conditions), coordinates))
                continue;

            var substitutions = FontBinary.Offset32(records, (i * 8) + 4);
            return substitutions == 0 ? 0 : FontBinary.AddOffset(_featureVariations, substitutions);
        }

        return 0;
    }

    /// <summary>Tells whether every condition of a set holds: each one is a range of the normalized value of an axis.</summary>
    private static bool MatchesConditions(ReadOnlySpan<byte> table, int set, ReadOnlySpan<int> coordinates)
    {
        var count = FontBinary.UInt16(table, set);
        for (var i = 0; i < count; i++)
        {
            var condition = FontBinary.AddOffset(set, FontBinary.Offset32(table, set + 2 + (i * 4)));

            // A condition of a format that is not known does not hold.
            if (FontBinary.UInt16(table, condition) != 1)
                return false;

            var axis = FontBinary.UInt16(table, condition + 2);
            var coordinate = axis < coordinates.Length ? coordinates[axis] : 0;
            if (coordinate < FontBinary.Int16(table, condition + 4) || coordinate > FontBinary.Int16(table, condition + 6))
                return false;
        }

        return true;
    }

    /// <summary>Gets the number of lookups.</summary>
    public int GetLookupCount(ReadOnlySpan<byte> table) => FontBinary.UInt16(table, _lookups);

    /// <summary>Gets a lookup.</summary>
    public LayoutLookup GetLookup(ReadOnlySpan<byte> table, int index)
    {
        if ((uint)index >= (uint)GetLookupCount(table))
            throw FontErrors.Invalid("a layout table refers to a lookup that does not exist.");

        var offset = _lookups + FontBinary.UInt16(table, _lookups + 2 + (index * 2));
        var flag = FontBinary.UInt16(table, offset + 2);
        var count = FontBinary.UInt16(table, offset + 4);
        var set = (flag & LayoutLookup.UseMarkFilteringSet) != 0 ? FontBinary.UInt16(table, offset + 6 + (count * 2)) : 0;
        return new LayoutLookup(offset, FontBinary.UInt16(table, offset), flag, count, set);
    }

    /// <summary>Gets the position of a subtable of a lookup.</summary>
    public static int GetSubtable(ReadOnlySpan<byte> table, in LayoutLookup lookup, int index)
        => lookup.Offset + FontBinary.UInt16(table, lookup.Offset + 6 + (index * 2));

    /// <summary>Finds the language system of a script and a language.</summary>
    /// <param name="table">The table.</param>
    /// <param name="scripts">The tags of the script, by order of preference.</param>
    /// <param name="language">The tag of the language, or 0 for the default language system of the script.</param>
    /// <returns>The position of the language system, or 0 when the table has none for the script.</returns>
    public int FindLanguageSystem(ReadOnlySpan<byte> table, ReadOnlySpan<uint> scripts, uint language)
    {
        var script = 0;
        foreach (var tag in scripts)
        {
            script = FindRecord(table, _scripts, tag);
            if (script != 0)
                break;
        }

        if (script == 0)
            return 0;

        // The language systems: the default one, then a sorted list of the others.
        if (language != 0)
        {
            var count = FontBinary.UInt16(table, script + 2);
            for (var i = 0; i < count; i++)
            {
                if (FontBinary.UInt32(table, script + 4 + (i * 6)) == language)
                    return script + FontBinary.UInt16(table, script + 8 + (i * 6));
            }
        }

        var defaultSystem = FontBinary.UInt16(table, script);
        return defaultSystem == 0 ? 0 : script + defaultSystem;
    }

    /// <summary>Gets the feature a language system requires, or <see cref="NoFeature"/>.</summary>
    public static int GetRequiredFeature(ReadOnlySpan<byte> table, int languageSystem) => FontBinary.UInt16(table, languageSystem + 2);

    /// <summary>Gets the number of features of a language system.</summary>
    public static int GetFeatureCount(ReadOnlySpan<byte> table, int languageSystem) => FontBinary.UInt16(table, languageSystem + 4);

    /// <summary>Gets the index, in the feature list, of a feature of a language system.</summary>
    public static int GetFeatureIndex(ReadOnlySpan<byte> table, int languageSystem, int index) => FontBinary.UInt16(table, languageSystem + 6 + (index * 2));

    /// <summary>Gets the tag of a feature of the feature list, or 0 when the index is not one of its features.</summary>
    public uint GetFeatureTag(ReadOnlySpan<byte> table, int feature)
        => (uint)feature < (uint)FontBinary.UInt16(table, _features) ? FontBinary.UInt32(table, _features + 2 + (feature * 6)) : 0;

    /// <summary>Gets the number of lookups of a feature of the feature list.</summary>
    /// <param name="table">The table.</param>
    /// <param name="feature">The index of the feature in the feature list.</param>
    /// <param name="substitutions">The feature substitutions of the instance, from <see cref="FindFeatureSubstitutions"/>, or 0.</param>
    public int GetFeatureLookupCount(ReadOnlySpan<byte> table, int feature, int substitutions = 0) => FontBinary.UInt16(table, GetFeature(table, feature, substitutions) + 2);

    /// <summary>Gets a lookup of a feature of the feature list.</summary>
    public int GetFeatureLookup(ReadOnlySpan<byte> table, int feature, int index, int substitutions = 0) => FontBinary.UInt16(table, GetFeature(table, feature, substitutions) + 4 + (index * 2));

    private int GetFeature(ReadOnlySpan<byte> table, int feature, int substitutions)
    {
        if ((uint)feature >= (uint)FontBinary.UInt16(table, _features))
            throw FontErrors.Invalid("a layout table refers to a feature that does not exist.");

        if (substitutions != 0)
        {
            // The alternate feature tables of the instance, sorted by the index of the feature they replace.
            var count = FontBinary.UInt16(table, substitutions + 4);
            for (var i = 0; i < count; i++)
            {
                var replaced = FontBinary.UInt16(table, substitutions + 6 + (i * 6));
                if (replaced == feature)
                    return FontBinary.AddOffset(substitutions, FontBinary.Offset32(table, substitutions + 8 + (i * 6)));

                if (replaced > feature)
                    break;
            }
        }

        return _features + FontBinary.UInt16(table, _features + 6 + (feature * 6));
    }

    /// <summary>Finds a record by its tag in a list of records made of a tag and a 16-bit offset.</summary>
    private static int FindRecord(ReadOnlySpan<byte> table, int list, uint tag)
    {
        var count = FontBinary.UInt16(table, list);
        for (var i = 0; i < count; i++)
        {
            if (FontBinary.UInt32(table, list + 2 + (i * 6)) == tag)
                return list + FontBinary.UInt16(table, list + 6 + (i * 6));
        }

        return 0;
    }
}
