namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents inline code.</summary>
public sealed class AdfCodeMark : AdfMark
{
    /// <inheritdoc />
    public override AdfMarkKind Kind => AdfMarkKind.Code;
}
