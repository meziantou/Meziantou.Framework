namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents struck-through text.</summary>
public sealed class AdfStrikeMark : AdfMark
{
    /// <inheritdoc />
    public override AdfMarkKind Kind => AdfMarkKind.Strike;
}
