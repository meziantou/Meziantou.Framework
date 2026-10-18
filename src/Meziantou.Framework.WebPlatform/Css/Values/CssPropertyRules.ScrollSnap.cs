using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

// Scroll snap (https://drafts.csswg.org/css-scroll-snap-1/)
internal static partial class CssPropertyRules
{
    /// <summary>https://drafts.csswg.org/css-scroll-snap-1/#scroll-snap-type: the default proximity strictness is omitted (x proximity is x).</summary>
    private static string? CanonicalizeScrollSnapType(List<CssComponentValue> significant)
        => significant is [var axis, var strictness] && IsIdent(strictness, "proximity") ? InfraAscii.ToAsciiLowercase(Serialize([axis], separator: " ")) : null;

    /// <summary>https://drafts.csswg.org/css-scroll-snap-1/#scroll-snap-align: the omitted inline alignment is the block alignment (start start is start).</summary>
    private static string? CanonicalizeScrollSnapAlign(List<CssComponentValue> significant)
        => significant is [CssPreservedToken { Type: CssTokenType.Ident } block, CssPreservedToken { Type: CssTokenType.Ident } inline] && string.Equals(block.Token.Value, inline.Token.Value, StringComparison.OrdinalIgnoreCase)
            ? InfraAscii.ToAsciiLowercase(block.Token.Value)
            : null;
}
