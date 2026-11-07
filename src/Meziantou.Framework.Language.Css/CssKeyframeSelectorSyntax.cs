namespace Meziantou.Framework.Language.Css;

/// <summary>Represents a keyframe selector: <c>from</c>, <c>to</c>, a percentage, or a timeline range such as <c>entry 10%</c>.</summary>
public sealed partial class CssKeyframeSelectorSyntax
{
    /// <summary>Gets the offset as a percentage: 0 for <c>from</c>, 100 for <c>to</c>.</summary>
    public double Offset => OffsetToken.Kind() == SyntaxKind.PercentageToken
        ? (double)OffsetToken.Value!
        : string.Equals(OffsetToken.ValueText, "to", StringComparison.OrdinalIgnoreCase) ? 100 : 0;
}
