using System.Text.Json;

namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>
/// Represents a node whose type is not part of the supported schema. Atlassian regularly adds
/// node types, and documents returned by the APIs contain nodes such as <c>unsupportedBlock</c>,
/// so parsing never fails on an unknown type. The original JSON is preserved so the node
/// round-trips unchanged.
/// </summary>
public sealed class AdfUnknownNode : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.Unknown;

    /// <summary>Gets the value of the <c>type</c> property of the node.</summary>
    public required string TypeName { get; init; }

    /// <summary>Gets the original JSON of the node.</summary>
    public required JsonElement RawJson { get; init; }
}
