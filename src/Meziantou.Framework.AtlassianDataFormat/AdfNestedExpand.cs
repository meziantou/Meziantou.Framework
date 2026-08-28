namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a collapsible section nested inside another node.</summary>
public sealed class AdfNestedExpand : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.NestedExpand;

    /// <summary>Gets the title shown on the collapsed section.</summary>
    public string? Title { get; init; }
}
