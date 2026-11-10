namespace Meziantou.Framework.Diagrams.Layout;

/// <summary>An input of the layout of a Venn diagram: a set, or an overlap of sets, with its size.</summary>
/// <param name="Sets">The identifiers of the sets.</param>
/// <param name="Size">The area of the set, or of the overlap.</param>
/// <param name="Weight">The weight of the area in the loss of the layout.</param>
internal sealed record VennLayoutArea(IReadOnlyList<string> Sets, double Size, double Weight = 1);
