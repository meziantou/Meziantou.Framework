namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents strong emphasis.</summary>
public sealed class AdfStrongMark : AdfMark
{
    /// <inheritdoc />
    public override AdfMarkKind Kind => AdfMarkKind.Strong;
}
