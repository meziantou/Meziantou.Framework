using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>
/// The extended glyph metamorphosis table of Apple Advanced Typography (<c>morx</c>): Apple TrueType Reference Manual,
/// "The 'morx' table". Its chains of subtables replace, merge, insert and reorder glyphs, each subtable when the
/// features that select it are on.
/// </summary>
/// <remarks>
/// Versions 2 and 3 are read; the coverage bit fields of version 3 only tell which subtables a text can skip, and are
/// not needed. The headers of the chains and of the subtables are validated when the table is read; the content of a
/// subtable is read when it is applied, through <see cref="FontBinary"/>.
/// </remarks>
internal sealed class MorxTable
{
    /// <summary>The subtables of a chain that a text can have, together: far more than any font has.</summary>
    private const int MaxSubtables = 4096;

    private MorxTable(Chain[] chains)
    {
        Chains = chains;
    }

    /// <summary>Gets the chains, in the order they are applied.</summary>
    public Chain[] Chains { get; }

    /// <exception cref="InvalidFontException">The table is malformed.</exception>
    public static MorxTable Read(ReadOnlySpan<byte> table)
    {
        var version = FontBinary.UInt16(table, 0);
        if (version is not (2 or 3))
            throw FontErrors.InvalidTable(FontTags.Morx, $"the version {version} is not supported.");

        var chainCount = FontBinary.Offset32(table, 4);
        var chains = new List<Chain>();
        var subtableCount = 0;
        var offset = 8;
        for (var i = 0; i < chainCount; i++)
        {
            var defaultFlags = FontBinary.UInt32(table, offset);
            var length = FontBinary.Offset32(table, offset + 4);
            var featureCount = FontBinary.Offset32(table, offset + 8);
            var count = FontBinary.Offset32(table, offset + 12);
            FontBinary.EnsureRange(table, offset, length);
            var chain = table.Slice(offset, length);
            subtableCount += count;
            if (subtableCount > MaxSubtables || (long)featureCount * 12 > length)
                throw FontErrors.InvalidTable(FontTags.Morx, "a chain has more features or subtables than it can hold.");

            var features = new FeatureEntry[featureCount];
            for (var j = 0; j < featureCount; j++)
            {
                var entry = 16 + (j * 12);
                features[j] = new FeatureEntry(FontBinary.UInt16(chain, entry), FontBinary.UInt16(chain, entry + 2), FontBinary.UInt32(chain, entry + 4), FontBinary.UInt32(chain, entry + 8));
            }

            var subtables = new Subtable[count];
            var position = 16 + (featureCount * 12);
            for (var j = 0; j < count; j++)
            {
                var subtableLength = FontBinary.Offset32(chain, position);
                if (subtableLength < 12)
                    throw FontErrors.InvalidTable(FontTags.Morx, "a subtable is shorter than its header.");

                FontBinary.EnsureRange(chain, position, subtableLength);
                var coverage = FontBinary.UInt32(chain, position + 4);
                subtables[j] = new Subtable(offset + position + 12, subtableLength - 12, coverage, FontBinary.UInt32(chain, position + 8));
                position += subtableLength;
            }

            chains.Add(new Chain(defaultFlags, features, subtables));
            offset = FontBinary.AddOffset(offset, length);
            if (length == 0)
                break;
        }

        return new MorxTable([.. chains]);
    }

    /// <summary>Gets the flags of the subtables of a chain that are on for the features of a text.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="isSelected">Tells whether a setting (feature type and selector) of the chain is selected.</param>
    public static uint GetFlags(Chain chain, Func<int, int, bool> isSelected)
    {
        // "The result is initialized to the chain's defaultFlags. The entries in the feature table are processed in
        // order. If the setting is in the requested list, the result is ANDed with the entry's disableFlags and ORed
        // with the entry's enableFlags."
        var flags = chain.DefaultFlags;
        foreach (var feature in chain.Features)
        {
            if (isSelected(feature.Type, feature.Setting))
            {
                flags = (flags & feature.DisableFlags) | feature.EnableFlags;
            }
        }

        return flags;
    }

    /// <summary>A chain of subtables.</summary>
    /// <param name="DefaultFlags">The flags of the subtables that are on when no feature is selected.</param>
    /// <param name="Features">The settings that turn subtables on and off.</param>
    /// <param name="Subtables">The subtables, in the order they are applied.</param>
    internal sealed record Chain(uint DefaultFlags, FeatureEntry[] Features, Subtable[] Subtables);

    /// <summary>A setting of a feature and the subtables it turns on and off.</summary>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct FeatureEntry(int Type, int Setting, uint EnableFlags, uint DisableFlags);

    /// <summary>A subtable: where its content is in the table, how it applies, and the flags that select it.</summary>
    /// <param name="Offset">The offset of its content, after its header, from the start of the table.</param>
    /// <param name="Length">The length of its content.</param>
    /// <param name="Coverage">The orientation, the order of the glyphs and the type of the subtable.</param>
    /// <param name="Flags">The flags of the subtable: it applies when one of them is on.</param>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct Subtable(int Offset, int Length, uint Coverage, uint Flags)
    {
        /// <summary>Gets the type of the subtable.</summary>
        public int Type => (int)(Coverage & 0xFF);

        /// <summary>Gets a value indicating whether the subtable applies to vertical text only (unless <see cref="IsAnyOrientation"/>).</summary>
        public bool IsVertical => (Coverage & 0x80000000) != 0;

        /// <summary>Gets a value indicating whether the subtable processes the glyphs from the last one.</summary>
        public bool IsDescending => (Coverage & 0x40000000) != 0;

        /// <summary>Gets a value indicating whether the subtable applies to both orientations.</summary>
        public bool IsAnyOrientation => (Coverage & 0x20000000) != 0;

        /// <summary>Gets a value indicating whether the subtable processes the glyphs in the order of the characters rather than in the order they are drawn.</summary>
        public bool IsLogicalOrder => (Coverage & 0x10000000) != 0;
    }
}
