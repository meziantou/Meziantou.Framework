namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a heading.</summary>
public sealed class AdfHeading : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.Heading;

    /// <summary>Gets the heading level, between 1 and 6.</summary>
    public required int Level { get; init; }
}
