using System.Text;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.XPath;

internal sealed record RewrittenXPathExpression(string Expression, string HtmlPrefix, string AttributeFunctionPrefix);

/// <summary>
/// Rewrites the name tests of an XPath 1.0 expression evaluated against an HTML document, as required by
/// https://html.spec.whatwg.org/multipage/infrastructure.html#interactions-with-xpath-and-xslt: unprefixed element name
/// tests use the HTML namespace as the default element namespace (matched ASCII case-insensitively, as HTML element names
/// are lowercase), and unprefixed attribute name tests match the attributes of HTML elements ASCII case-insensitively.
/// Only the lexical structure is analyzed (https://www.w3.org/TR/xpath-10/#exprlex); System.Xml.XPath does the rest.
/// </summary>
internal static class XPathExpressionRewriter
{
    /// <summary>The prefix bound to the HTML namespace for the rewritten element name tests.</summary>
    public const string HtmlPrefix = "_html-default-element-ns";

    /// <summary>The custom function used for the rewritten attribute name tests.</summary>
    public const string AttributeFunctionPrefix = "_html-xpath-fn";

    public const string AttributeFunctionName = "attribute-name-matches";

    public static string Rewrite(string expression) => RewriteWithNamespaces(expression).Expression;

    public static RewrittenXPathExpression RewriteWithNamespaces(string expression)
    {
        var htmlPrefix = GetUnusedPrefix(expression, HtmlPrefix);
        var attributeFunctionPrefix = GetUnusedPrefix(expression, AttributeFunctionPrefix);
        return new RewrittenXPathExpression(RewriteCore(expression, htmlPrefix, attributeFunctionPrefix), htmlPrefix, attributeFunctionPrefix);
    }

    private enum TokenType
    {
        Other,
        Operator,
        LeftParenthesis,
        LeftBracket,
        Comma,
        At,
        DoubleColon,
        Name,
        Star,
    }

    private static string RewriteCore(string expression, string htmlPrefix, string attributeFunctionPrefix)
    {
        var output = new StringBuilder(expression.Length + 16);
        var previous = TokenType.Operator; // the start of the expression behaves like after an operator
        string? pendingAxis = null;
        var index = 0;
        while (index < expression.Length)
        {
            var c = expression[index];
            if (c is ' ' or '\t' or '\r' or '\n')
            {
                output.Append(c);
                index++;
                continue;
            }

            // Literals
            if (c is '"' or '\'')
            {
                var end = expression.IndexOf(c, index + 1, StringComparison.Ordinal);
                end = end < 0 ? expression.Length : end + 1;
                output.Append(expression, index, end - index);
                index = end;
                previous = TokenType.Other;
                pendingAxis = null;
                continue;
            }

            // Numbers
            if (char.IsAsciiDigit(c) || (c == '.' && index + 1 < expression.Length && char.IsAsciiDigit(expression[index + 1])))
            {
                var start = index;
                while (index < expression.Length && (char.IsAsciiDigit(expression[index]) || expression[index] == '.'))
                {
                    index++;
                }

                output.Append(expression, start, index - start);
                previous = TokenType.Other;
                continue;
            }

            // Variable references
            if (c == '$')
            {
                var start = index++;
                index = ReadQName(expression, index);
                output.Append(expression, start, index - start);
                previous = TokenType.Other;
                continue;
            }

            if (c == '@')
            {
                output.Append(c);
                index++;
                previous = TokenType.At;
                pendingAxis = "attribute";
                continue;
            }

            if (c == ':' && index + 1 < expression.Length && expression[index + 1] == ':')
            {
                output.Append("::");
                index += 2;
                previous = TokenType.DoubleColon;
                continue;
            }

            if (c is '(' or '[' or ',')
            {
                output.Append(c);
                index++;
                previous = c switch { '(' => TokenType.LeftParenthesis, '[' => TokenType.LeftBracket, _ => TokenType.Comma };
                pendingAxis = null;
                continue;
            }

            if (c is ')' or ']')
            {
                output.Append(c);
                index++;
                previous = TokenType.Other;
                pendingAxis = null;
                continue;
            }

            if (c == '.')
            {
                var length = index + 1 < expression.Length && expression[index + 1] == '.' ? 2 : 1;
                output.Append(expression, index, length);
                index += length;
                previous = TokenType.Other;
                pendingAxis = null;
                continue;
            }

            var operatorContext = previous is TokenType.Operator or TokenType.LeftParenthesis or TokenType.LeftBracket or TokenType.Comma or TokenType.At or TokenType.DoubleColon;
            if (c == '*')
            {
                output.Append(c);
                index++;

                // A '*' is a name test when it cannot be the multiply operator
                previous = operatorContext ? TokenType.Star : TokenType.Operator;
                pendingAxis = null;
                continue;
            }

            if (c is '/' or '|' or '+' or '-' or '=' or '!' or '<' or '>')
            {
                var length = (c is '/' && index + 1 < expression.Length && expression[index + 1] == '/') || (c is '!' or '<' or '>' && index + 1 < expression.Length && expression[index + 1] == '=') ? 2 : 1;
                output.Append(expression, index, length);
                index += length;
                previous = TokenType.Operator;
                pendingAxis = null;
                continue;
            }

            if (IsNameStart(c))
            {
                var start = index;
                var end = ReadQName(expression, index);
                var name = expression[start..end];
                index = end;

                // An NCName after an operand is an operator name (and, or, mod, div)
                if (!operatorContext)
                {
                    output.Append(name);
                    previous = TokenType.Operator;
                    continue;
                }

                var next = SkipWhitespace(expression, index);
                if (next + 1 < expression.Length && expression[next] == ':' && expression[next + 1] == ':')
                {
                    // Axis name
                    output.Append(name);
                    pendingAxis = name;
                    previous = TokenType.Other;
                    continue;
                }

                if (next < expression.Length && expression[next] == '(')
                {
                    // Function name or node type
                    output.Append(name);
                    previous = TokenType.Other;
                    pendingAxis = null;
                    continue;
                }

                // "prefix:*" is a name test with a prefix
                if (index < expression.Length && expression[index] == ':' && index + 1 < expression.Length && expression[index + 1] == '*')
                {
                    output.Append(name).Append(":*");
                    index += 2;
                    previous = TokenType.Other;
                    pendingAxis = null;
                    continue;
                }

                output.Append(RewriteNameTest(name, pendingAxis ?? "child", htmlPrefix, attributeFunctionPrefix));
                previous = TokenType.Other;
                pendingAxis = null;
                continue;
            }

            output.Append(c);
            index++;
            previous = TokenType.Other;
        }

        return output.ToString();
    }

    private static string GetUnusedPrefix(string expression, string prefix)
    {
        var candidate = prefix;
        var suffix = 0;
        while (expression.Contains(candidate, StringComparison.Ordinal))
        {
            suffix++;
            candidate = prefix + "-" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return candidate;
    }

    private static string RewriteNameTest(string name, string axis, string htmlPrefix, string attributeFunctionPrefix)
    {
        if (name.Contains(':', StringComparison.Ordinal))
            return name; // Prefixed name tests are not affected

        if (axis == "namespace")
            return name;

        if (axis == "attribute")
        {
            // Attribute names of HTML elements are lowercase: a name without uppercase ASCII letters has the same meaning
            if (name.AsSpan().IndexOfAnyInRange('A', 'Z') < 0)
                return name;

            return "*[" + attributeFunctionPrefix + ":" + AttributeFunctionName + "('" + name + "')]";
        }

        // The principal node type of the other axes is element
        return htmlPrefix + ":" + InfraAscii.ToAsciiLowercase(name);
    }

    private static int SkipWhitespace(string expression, int index)
    {
        while (index < expression.Length && expression[index] is ' ' or '\t' or '\r' or '\n')
        {
            index++;
        }

        return index;
    }

    private static bool IsNameStart(char c) => char.IsLetter(c) || c == '_' || c > 0x7F;

    private static bool IsNameChar(char c) => IsNameStart(c) || char.IsAsciiDigit(c) || c is '-' or '.' || c > 0x7F;

    /// <summary>Reads a QName (NCName, optionally followed by ':' NCName).</summary>
    private static int ReadQName(string expression, int index)
    {
        while (index < expression.Length && IsNameChar(expression[index]))
        {
            index++;
        }

        if (index + 1 < expression.Length && expression[index] == ':' && expression[index + 1] != ':' && IsNameStart(expression[index + 1]))
        {
            index++;
            while (index < expression.Length && IsNameChar(expression[index]))
            {
                index++;
            }
        }

        return index;
    }
}
