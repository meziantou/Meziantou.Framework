using Meziantou.Framework.Language.InternalSyntax;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Tests.TestLanguage;

/// <summary>Parses the toy language: a value that is either an identifier or a parenthesised list of values.</summary>
/// <remarks>
/// Trivia follows the same rule the real parsers use: whitespace up to and including a line break belongs to the
/// token before it, and everything after belongs to the token that follows.
/// </remarks>
internal static class TestSyntax
{
    public static TestRootSyntax ParseRoot(string text) => (TestRootSyntax)ParseGreen(text).CreateRed();

    public static GreenNode ParseGreen(string text)
    {
        var tokens = Lex(text);
        var parser = new Parser(tokens);

        return parser.ParseRoot();
    }

    public static TestAtomSyntax Atom(string name) => (TestAtomSyntax)new TestGreen.Atom(TestGreen.Token(TestSyntaxKind.IdentifierToken, name)).CreateRed();

    private static List<GreenToken> Lex(string text)
    {
        var tokens = new List<GreenToken>();
        var position = 0;
        while (true)
        {
            var leading = LexTrivia(text, ref position, isTrailing: false);
            var start = position;
            var kind = TestSyntaxKind.EndOfFileToken;
            if (position < text.Length)
            {
                switch (text[position])
                {
                    case '(':
                        kind = TestSyntaxKind.OpenParenToken;
                        position++;
                        break;
                    case ')':
                        kind = TestSyntaxKind.CloseParenToken;
                        position++;
                        break;
                    case ',':
                        kind = TestSyntaxKind.CommaToken;
                        position++;
                        break;
                    default:
                        kind = TestSyntaxKind.IdentifierToken;
                        while (position < text.Length && char.IsAsciiLetterOrDigit(text[position]))
                        {
                            position++;
                        }

                        if (position == start)
                            throw new InvalidOperationException($"The toy language does not accept '{text[position]}'.");

                        break;
                }
            }

            var tokenText = text[start..position];
            var trailing = LexTrivia(text, ref position, isTrailing: true);
            tokens.Add(TestGreen.Token(kind, tokenText, leading, trailing));

            if (kind == TestSyntaxKind.EndOfFileToken)
                return tokens;
        }
    }

    private static GreenNode? LexTrivia(string text, ref int position, bool isTrailing)
    {
        List<GreenNode?>? trivia = null;
        while (position < text.Length)
        {
            var start = position;
            if (text[position] is ' ' or '\t')
            {
                while (position < text.Length && text[position] is ' ' or '\t')
                {
                    position++;
                }

                (trivia ??= []).Add(TestGreen.Trivia(TestSyntaxKind.WhitespaceTrivia, text[start..position]));
                continue;
            }

            if (text[position] is '\r' or '\n')
            {
                position += text[position] == '\r' && position + 1 < text.Length && text[position + 1] == '\n' ? 2 : 1;
                (trivia ??= []).Add(TestGreen.Trivia(TestSyntaxKind.EndOfLineTrivia, text[start..position]));

                // Everything past the end of the line belongs to the next token.
                if (isTrailing)
                    break;

                continue;
            }

            break;
        }

        return trivia is null ? null : InternalSyntax.SyntaxList.List(trivia.ToArray());
    }

    private sealed class Parser(List<GreenToken> tokens)
    {
        private int _index;

        private GreenToken Current => tokens[Math.Min(_index, tokens.Count - 1)];

        public TestGreen.Root ParseRoot()
        {
            var value = Current.RawKind == (int)TestSyntaxKind.EndOfFileToken ? null : ParseValue();
            var endOfFile = Eat(TestSyntaxKind.EndOfFileToken);

            return new TestGreen.Root(value, endOfFile);
        }

        private GreenNode ParseValue()
        {
            if (Current.RawKind == (int)TestSyntaxKind.OpenParenToken)
                return ParseList();

            return new TestGreen.Atom(Eat(TestSyntaxKind.IdentifierToken));
        }

        private TestGreen.List ParseList()
        {
            var open = Eat(TestSyntaxKind.OpenParenToken);
            var items = new List<GreenNode?>();
            while (Current.RawKind is not (int)TestSyntaxKind.CloseParenToken and not (int)TestSyntaxKind.EndOfFileToken)
            {
                items.Add(ParseValue());
                if (Current.RawKind != (int)TestSyntaxKind.CommaToken)
                    break;

                items.Add(Eat(TestSyntaxKind.CommaToken));
            }

            var close = Current.RawKind == (int)TestSyntaxKind.CloseParenToken
                ? Eat(TestSyntaxKind.CloseParenToken)
                : TestGreen.Token(TestSyntaxKind.CloseParenToken, string.Empty, isMissing: true);

            return new TestGreen.List(open, InternalSyntax.SyntaxList.ListNode(items.ToArray()), close);
        }

        private GreenToken Eat(TestSyntaxKind kind)
        {
            var current = Current;
            if (current.RawKind != (int)kind)
                throw new InvalidOperationException($"Expected {kind} but found {(TestSyntaxKind)current.RawKind}.");

            _index++;

            return current;
        }
    }
}
