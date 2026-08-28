namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a callout panel.</summary>
public sealed class AdfPanel : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.Panel;

    /// <summary>Gets the kind of panel.</summary>
    public required AdfPanelType PanelType { get; init; }

    /// <summary>Gets the icon of a custom panel.</summary>
    public string? PanelIcon { get; init; }

    /// <summary>Gets the icon text of a custom panel.</summary>
    public string? PanelIconText { get; init; }

    /// <summary>Gets the color of a custom panel.</summary>
    public string? PanelColor { get; init; }
}
