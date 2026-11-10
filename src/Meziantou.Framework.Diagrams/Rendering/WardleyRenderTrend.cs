namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>A movement of a node of a Wardley map, in percent.</summary>
internal sealed record WardleyRenderTrend(string NodeId, double TargetX, double TargetY);
