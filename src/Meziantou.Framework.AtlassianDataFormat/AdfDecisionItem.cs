namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a single decision.</summary>
public sealed class AdfDecisionItem : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.DecisionItem;

    /// <summary>Gets the state of the decision, usually <c>DECIDED</c>.</summary>
    public string? State { get; init; }
}
