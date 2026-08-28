namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents an explicit line break.</summary>
public sealed class AdfHardBreak : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.HardBreak;
}
