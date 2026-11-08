namespace Meziantou.Framework.Fonts;

/// <summary>
/// The feature name table of Apple Advanced Typography (<c>feat</c>): Apple TrueType Reference Manual, "The 'feat'
/// table". It lists the features of the font, their settings, and whether the settings of a feature exclude each other.
/// </summary>
internal sealed class FeatTable
{
    private readonly Dictionary<int, Feature> _features;

    private FeatTable(Dictionary<int, Feature> features)
    {
        _features = features;
    }

    /// <exception cref="InvalidFontException">The table is malformed.</exception>
    public static FeatTable Read(ReadOnlySpan<byte> table)
    {
        if (FontBinary.UInt32(table, 0) != 0x00010000)
            throw FontErrors.InvalidTable(FontTags.Feat, "the version is not supported.");

        var count = FontBinary.UInt16(table, 4);
        var features = new Dictionary<int, Feature>(count);
        for (var i = 0; i < count; i++)
        {
            var record = 12 + (i * 12);
            var type = FontBinary.UInt16(table, record);
            var settingCount = FontBinary.UInt16(table, record + 2);
            var settingTable = FontBinary.Offset32(table, record + 4);
            var flags = FontBinary.UInt16(table, record + 8);
            FontBinary.EnsureRange(table, settingTable, (long)settingCount * 4);
            var settings = new int[settingCount];
            for (var j = 0; j < settingCount; j++)
            {
                settings[j] = FontBinary.UInt16(table, settingTable + (j * 4));
            }

            // "If set, then bits 0-15 of this featureFlags field contain the index of the setting which is to be taken as
            // the default"; else it is the first one.
            var isExclusive = (flags & 0x8000) != 0;
            var defaultIndex = (flags & 0x4000) != 0 ? flags & 0xFF : 0;
            var defaultSetting = defaultIndex < settingCount ? settings[defaultIndex] : 0;
            features.TryAdd(type, new Feature(isExclusive, defaultSetting, settings));
        }

        return new FeatTable(features);
    }

    /// <summary>Gets a feature of the font.</summary>
    public bool TryGetFeature(int type, out Feature feature) => _features.TryGetValue(type, out feature!);

    /// <summary>A feature of the font.</summary>
    /// <param name="IsExclusive">The settings exclude each other: one of them is on at a time.</param>
    /// <param name="DefaultSetting">The setting that is on when none is selected, for an exclusive feature.</param>
    /// <param name="Settings">The settings, in increasing order.</param>
    internal sealed record Feature(bool IsExclusive, int DefaultSetting, int[] Settings);
}
