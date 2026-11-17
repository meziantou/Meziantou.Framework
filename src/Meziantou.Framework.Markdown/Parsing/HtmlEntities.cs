using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>
/// The named character references of HTML, from <see href="https://html.spec.whatwg.org/multipage/named-characters.html"/>:
/// HtmlEntities.g.cs is generated from <c>entities.json</c> by <c>tools/Meziantou.Framework.WebPlatform.SpecGen</c>.
/// </summary>
internal static partial class HtmlEntities
{
    /// <summary>Decodes an entity or a numeric character reference: <c>&amp;amp;</c>, <c>&amp;#35;</c>, <c>&amp;#x22;</c>.</summary>
    public static string Decode(string entity)
    {
        if (entity.StartsWith("&#", StringComparison.Ordinal))
        {
            var isHex = entity[2] is 'x' or 'X';
            var digits = entity.AsSpan(isHex ? 3 : 2, entity.Length - (isHex ? 4 : 3));
            if (!int.TryParse(digits, isHex ? NumberStyles.HexNumber : NumberStyles.None, CultureInfo.InvariantCulture, out var codePoint) || codePoint == 0 || !Rune.IsValid(codePoint))
            {
                return "�";
            }

            return char.ConvertFromUtf32(codePoint);
        }

        // An entity that HTML does not define is text
        return Entities.TryGetValue(entity[1..^1], out var value) ? value : entity;
    }
}
