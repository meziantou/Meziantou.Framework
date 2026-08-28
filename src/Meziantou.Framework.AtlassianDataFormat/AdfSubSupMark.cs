namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents subscript or superscript text.</summary>
public sealed class AdfSubSupMark : AdfMark
{
    /// <inheritdoc />
    public override AdfMarkKind Kind => AdfMarkKind.SubSup;

    /// <summary>Gets whether the text is subscript or superscript.</summary>
    public required AdfSubSupType Type { get; init; }
}
