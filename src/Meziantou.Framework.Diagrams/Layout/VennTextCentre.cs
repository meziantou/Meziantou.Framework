namespace Meziantou.Framework.Diagrams.Layout;

/// <summary>The center of the label of an area of a Venn diagram.</summary>
/// <param name="X">The horizontal position.</param>
/// <param name="Y">The vertical position.</param>
/// <param name="Disjoint">Whether the area has no surface: its sets do not overlap, and the label is not drawn.</param>
internal sealed record VennTextCentre(double X, double Y, bool Disjoint);
