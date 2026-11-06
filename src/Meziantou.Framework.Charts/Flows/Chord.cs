namespace Meziantou.Framework.Charts;

/// <summary>A chord between two nodes: the source is the end of the larger of the two flows between them.</summary>
internal sealed class Chord
{
    public ChordSubgroup? Source { get; set; }

    public ChordSubgroup? Target { get; set; }
}
