namespace Meziantou.Framework.Charts;

/// <summary>The arc of a node of a chord layout: its angles, in radians from 12 o'clock, clockwise.</summary>
internal sealed record ChordGroup(int Index, double StartAngle, double EndAngle, double Value);
