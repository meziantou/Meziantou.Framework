namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>A subtable of an item variation store: the regions its items have deltas for, and a row of deltas per item.</summary>
internal sealed class FontVariationSubtable
{
    public required int[] Regions { get; init; }

    public List<int[]> Items { get; } = [];

    /// <summary>Gets or sets a value indicating whether the deltas are 32 and 16 bits instead of 16 and 8 bits.</summary>
    public bool UseLongDeltas { get; init; }
}
