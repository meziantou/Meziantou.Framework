using System.Text.Json;

namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>
/// Represents a mark whose type is not part of the supported schema. The original JSON is
/// preserved so the mark round-trips unchanged.
/// </summary>
public sealed class AdfUnknownMark : AdfMark
{
    /// <inheritdoc />
    public override AdfMarkKind Kind => AdfMarkKind.Unknown;

    /// <summary>Gets the value of the <c>type</c> property of the mark.</summary>
    public required string TypeName { get; init; }

    /// <summary>Gets the original JSON of the mark.</summary>
    public required JsonElement RawJson { get; init; }
}
