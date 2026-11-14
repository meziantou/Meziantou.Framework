namespace Meziantou.AspNetCore.Testing.Fuzz.Components;

/// <summary>A region (<c>OpenRegion</c>): its children are siblings of the nodes around it in the DOM.</summary>
internal sealed class ModelRegion(bool inSvg) : ModelContainer(inSvg);
