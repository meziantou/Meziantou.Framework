namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a collapsible section.</summary>
public sealed class AdfExpand : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.Expand;

    /// <summary>Gets the title shown on the collapsed section.</summary>
    public string? Title { get; init; }
}
