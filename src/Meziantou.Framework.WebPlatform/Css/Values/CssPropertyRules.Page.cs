using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

// Paged media (https://drafts.csswg.org/css-page-3/)
internal static partial class CssPropertyRules
{
    /// <summary>
    /// Returns the canonical form of the value of a descriptor, or null when its serialization follows the grammar. The value
    /// is valid (it matches the grammar of the descriptor).
    /// </summary>
    public static string? CanonicalizeDescriptor(CssDescriptorDefinition descriptor, List<CssComponentValue> significant)
    {
        return descriptor is { AtRule: "@page", Name: "size" } ? CanonicalizePageSize(significant) : null;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-page-3/#page-size-prop: the keywords are serialized in lowercase, the &lt;page-size&gt;
    /// first, without the default portrait orientation (letter portrait is letter).
    /// </summary>
    private static string? CanonicalizePageSize(List<CssComponentValue> significant)
    {
        if (!significant.TrueForAll(value => value is CssPreservedToken { Type: CssTokenType.Ident }))
            return null;

        var keywords = significant.ConvertAll(value => InfraAscii.ToAsciiLowercase(((CssPreservedToken)value).Token.Value));
        if (keywords is [var first, var second] && first is "portrait" or "landscape")
        {
            keywords = [second, first];
        }

        if (keywords is [_, "portrait"])
        {
            keywords.RemoveAt(1);
        }

        return string.Join(' ', keywords);
    }
}
