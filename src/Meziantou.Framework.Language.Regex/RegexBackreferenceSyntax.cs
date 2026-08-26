using System.Globalization;

namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a numbered backreference such as <c>\1</c>.</summary>
public sealed class RegexBackreferenceSyntax : RegexAtomSyntax
{
    public RegexBackreferenceSyntax(RegexSyntaxToken backreferenceToken)
        : base(RegexSyntaxKind.Backreference, [backreferenceToken], Part(backreferenceToken))
    {
        BackreferenceToken = backreferenceToken;
    }

    public RegexSyntaxToken BackreferenceToken { get; }

    /// <summary>The group number the reference names.</summary>
    public int Number => int.TryParse(BackreferenceToken.ValueText, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitBackreference(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitBackreference(this);
}
