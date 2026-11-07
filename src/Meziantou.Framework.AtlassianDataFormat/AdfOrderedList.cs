namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents an ordered list.</summary>
public sealed class AdfOrderedList : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.OrderedList;

    /// <summary>Gets the number the list starts at, or <see langword="null"/> to start at 1.</summary>
    public int? Order { get; init; }
}
