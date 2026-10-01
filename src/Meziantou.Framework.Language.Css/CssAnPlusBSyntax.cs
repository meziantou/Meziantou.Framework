using Meziantou.Framework.Language.Css.Internals;

namespace Meziantou.Framework.Language.Css;

/// <summary>Represents an An+B expression, such as <c>2n+1</c> or <c>odd</c>, which selects every A-th element starting with the B-th.</summary>
/// <remarks>The tokens are kept as the tokenizer split them, which is not where a reader would: <c>2n-1</c> is a single token.</remarks>
public sealed partial class CssAnPlusBSyntax
{
    /// <summary>Gets the step, A. It is zero for a plain index such as <c>3</c>.</summary>
    public int A => Read().A;

    /// <summary>Gets the offset, B.</summary>
    public int B => Read().B;

    /// <summary>Determines whether the tokens are a valid An+B expression; when they are not, <see cref="A"/> and <see cref="B"/> are zero.</summary>
    public bool IsValid => Read().IsValid;

    private (int A, int B, bool IsValid) Read()
    {
        var tokens = new AnPlusB.Token[Tokens.Count];
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = Tokens[i];
            var whitespaceBefore = i > 0 && (HasWhitespace(Tokens[i - 1].TrailingTrivia) || HasWhitespace(token.LeadingTrivia));
            tokens[i] = AnPlusB.CreateToken(token.Kind(), token.Text, token.ValueText, token.Value, whitespaceBefore);
        }

        var isValid = AnPlusB.TryParse(tokens, out var a, out var b);
        return (a, b, isValid);

        static bool HasWhitespace(SyntaxTriviaList trivia) => trivia.Any(item => item.Kind() is SyntaxKind.WhitespaceTrivia or SyntaxKind.EndOfLineTrivia);
    }
}
