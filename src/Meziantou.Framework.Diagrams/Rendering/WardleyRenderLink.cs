namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>A link of a Wardley map, its ends resolved to the identifiers of nodes.</summary>
internal sealed record WardleyRenderLink(string SourceId, string TargetId, bool IsDashed, WardleyFlow Flow, string? Label);
