namespace Meziantou.Framework.Charts;

/// <summary>An end of a chord: the part of the arc of a node that the flow from it to another node takes.</summary>
internal sealed record ChordSubgroup(int Index, double StartAngle, double EndAngle, double Value);
