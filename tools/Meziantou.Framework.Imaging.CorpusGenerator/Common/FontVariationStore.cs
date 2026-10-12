namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>An item variation store.</summary>
internal sealed class FontVariationStore
{
    public List<FontVariationRegion> Regions { get; } = [];

    public List<FontVariationSubtable> Subtables { get; } = [];
}
