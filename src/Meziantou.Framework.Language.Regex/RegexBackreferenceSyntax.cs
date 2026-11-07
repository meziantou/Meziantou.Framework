using System.Globalization;

namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a numbered backreference such as <c>\1</c>.</summary>
public sealed partial class RegexBackreferenceSyntax : RegexAtomSyntax
{
    /// <summary>The group number the reference names.</summary>
    public int Number => int.TryParse(BackreferenceToken.ValueText, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;
}
