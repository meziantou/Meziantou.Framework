using System.Globalization;
using Meziantou.Framework.Language.InternalSyntax;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Regex.Syntax.InternalSyntax;

/// <remarks>The parser needs the bounds while it is still building the tree, so they are worked out here too.</remarks>
internal sealed partial class RegexRangeQuantifierSyntax
{
    public int MinCount => ParseBound(GetSlot(1)) ?? 0;

    public int? MaxCount => GetSlot(2) is null ? MinCount : ParseBound(GetSlot(3));

    private static int? ParseBound(GreenNode? token)
    {
        if (token is not GreenToken { ValueText.Length: > 0 } value)
            return null;

        // The parser clamps a bound that does not fit, and still consumes every digit, so the text can be longer
        // than an int. ValueText carries the clamped value.
        return int.TryParse(value.ValueText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : int.MaxValue;
    }
}
