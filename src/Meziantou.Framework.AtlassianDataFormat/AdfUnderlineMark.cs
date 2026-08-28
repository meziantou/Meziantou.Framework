namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents underlined text.</summary>
public sealed class AdfUnderlineMark : AdfMark
{
    /// <inheritdoc />
    public override AdfMarkKind Kind => AdfMarkKind.Underline;
}
