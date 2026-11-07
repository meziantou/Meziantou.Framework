namespace Meziantou.Framework.WebPlatform.Css.Syntax;

/// <summary>A component value (https://drafts.csswg.org/css-syntax-3/#component-value): a preserved token, a function or a simple block.</summary>
internal abstract class CssComponentValue
{
    /// <summary>The position of the component value in the preprocessed input.</summary>
    public int Start { get; init; }

    public int End { get; set; }

    /// <summary>Whether the function or the simple block is closed by the end of the input instead of its ending token (a parse error).</summary>
    public bool Unterminated { get; set; }
}

/// <summary>https://drafts.csswg.org/css-syntax-3/#preserved-tokens</summary>
internal sealed class CssPreservedToken(CssToken token) : CssComponentValue
{
    public CssToken Token { get; } = token;

    public CssTokenType Type => Token.Type;

    public override string ToString() => Token.ToString();
}

/// <summary>https://drafts.csswg.org/css-syntax-3/#function</summary>
internal sealed class CssFunction(string name) : CssComponentValue
{
    public string Name { get; } = name;

    public List<CssComponentValue> Value { get; } = [];

    public override string ToString() => Name + "(" + string.Concat(Value) + ")";
}

/// <summary>https://drafts.csswg.org/css-syntax-3/#simple-block</summary>
internal sealed class CssSimpleBlock(CssTokenType associatedToken) : CssComponentValue
{
    /// <summary>The token that opened the block ([, ( or {).</summary>
    public CssTokenType AssociatedToken { get; } = associatedToken;

    public List<CssComponentValue> Value { get; } = [];
}

/// <summary>The entry points of the CSS parser (https://drafts.csswg.org/css-syntax-3/#parser-entry-points).</summary>
internal static partial class CssParser
{
    /// <summary>https://drafts.csswg.org/css-syntax-3/#parse-a-list-of-component-values</summary>
    public static List<CssComponentValue> ParseListOfComponentValues(string input) => ParseListOfComponentValues(input, out _);

    /// <summary>Parses a list of component values and returns the preprocessed input (the positions of the component values are relative to it).</summary>
    public static List<CssComponentValue> ParseListOfComponentValues(string input, out string preprocessedInput)
    {
        var tokenizer = new CssTokenizer(input);
        preprocessedInput = tokenizer.Input;
        var result = new List<CssComponentValue>();
        while (true)
        {
            var token = tokenizer.ConsumeToken();
            if (token.Type == CssTokenType.EndOfFile)
                return result;

            result.Add(ConsumeComponentValue(tokenizer, token));
        }
    }

    /// <summary>
    /// The maximum nesting depth of the blocks and functions. The syntax has no limit, but the algorithms that process the
    /// component values (rules, selectors, values) are recursive: the content of the deeper blocks is consumed without being
    /// kept, so pathological input (e.g. 100,000 nested parentheses) cannot exhaust the stack. Browsers have similar limits.
    /// </summary>
    internal const int MaxNestingDepth = 256;

    /// <summary>https://drafts.csswg.org/css-syntax-3/#consume-component-value</summary>
    internal static CssComponentValue ConsumeComponentValue(CssTokenizer tokenizer, CssToken token) => ConsumeComponentValue(tokenizer, token, depth: 0);

    private static CssComponentValue ConsumeComponentValue(CssTokenizer tokenizer, CssToken token, int depth)
    {
        switch (token.Type)
        {
            case CssTokenType.LeftCurlyBracket or CssTokenType.LeftSquareBracket or CssTokenType.LeftParenthesis:
            {
                var block = new CssSimpleBlock(token.Type) { Start = token.Start };
                var ending = token.Type switch
                {
                    CssTokenType.LeftCurlyBracket => CssTokenType.RightCurlyBracket,
                    CssTokenType.LeftSquareBracket => CssTokenType.RightSquareBracket,
                    _ => CssTokenType.RightParenthesis,
                };

                ConsumeUntil(tokenizer, block.Value, ending, block, depth + 1);
                return block;
            }

            case CssTokenType.Function:
            {
                var function = new CssFunction(token.Value) { Start = token.Start };
                ConsumeUntil(tokenizer, function.Value, CssTokenType.RightParenthesis, function, depth + 1);
                return function;
            }

            default:
                return new CssPreservedToken(token) { Start = token.Start, End = token.End };
        }
    }

    private static void ConsumeUntil(CssTokenizer tokenizer, List<CssComponentValue> values, CssTokenType ending, CssComponentValue owner, int depth)
    {
        if (depth > MaxNestingDepth)
        {
            SkipUntil(tokenizer, ending, owner);
            return;
        }

        while (true)
        {
            var next = tokenizer.ConsumeToken();
            if (next.Type == ending || next.Type == CssTokenType.EndOfFile)
            {
                // EOF is a parse error; the block/function is closed anyway
                owner.End = next.End;
                owner.Unterminated = next.Type == CssTokenType.EndOfFile;
                return;
            }

            values.Add(ConsumeComponentValue(tokenizer, next, depth));
        }
    }

    /// <summary>Consumes the content of a block or a function without creating its component values, with the same block matching as <see cref="ConsumeUntil"/>.</summary>
    private static void SkipUntil(CssTokenizer tokenizer, CssTokenType ending, CssComponentValue owner)
    {
        var endings = new Stack<CssTokenType>();
        endings.Push(ending);
        while (true)
        {
            var next = tokenizer.ConsumeToken();
            if (next.Type == CssTokenType.EndOfFile)
            {
                owner.End = next.End;
                owner.Unterminated = true;
                return;
            }

            if (next.Type == endings.Peek())
            {
                endings.Pop();
                if (endings.Count == 0)
                {
                    owner.End = next.End;
                    return;
                }

                continue;
            }

            switch (next.Type)
            {
                case CssTokenType.LeftCurlyBracket:
                    endings.Push(CssTokenType.RightCurlyBracket);
                    break;
                case CssTokenType.LeftSquareBracket:
                    endings.Push(CssTokenType.RightSquareBracket);
                    break;
                case CssTokenType.LeftParenthesis or CssTokenType.Function:
                    endings.Push(CssTokenType.RightParenthesis);
                    break;
            }
        }
    }
}
