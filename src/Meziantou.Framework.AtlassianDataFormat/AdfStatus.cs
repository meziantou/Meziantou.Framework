namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a status lozenge.</summary>
public sealed class AdfStatus : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.Status;

    /// <summary>Gets the text of the status lozenge.</summary>
    public required string Text { get; init; }

    /// <summary>Gets the color of the status lozenge.</summary>
    public string? Color { get; init; }

    /// <summary>Gets the style of the status lozenge.</summary>
    public string? Style { get; init; }
}
