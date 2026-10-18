using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Css.Selectors;

/// <summary>Serializes selectors (https://drafts.csswg.org/cssom/#serializing-selectors).</summary>
internal static class SelectorSerializer
{
    /// <summary>https://drafts.csswg.org/cssom/#serialize-a-group-of-selectors</summary>
    public static string Serialize(SelectorList list)
    {
        var builder = new StringBuilder();
        AppendList(builder, list);
        return builder.ToString();
    }

    /// <summary>https://drafts.csswg.org/cssom/#serialize-a-selector</summary>
    public static string Serialize(ComplexSelector selector)
    {
        var builder = new StringBuilder();
        AppendComplex(builder, selector);
        return builder.ToString();
    }

    public static string Serialize(CompoundSelector selector)
    {
        var builder = new StringBuilder();
        AppendCompound(builder, selector);
        return builder.ToString();
    }

    /// <summary>https://drafts.csswg.org/cssom/#serialize-a-simple-selector</summary>
    public static string Serialize(SimpleSelector selector)
    {
        var builder = new StringBuilder();
        AppendSimple(builder, selector);
        return builder.ToString();
    }

    private static void AppendList(StringBuilder builder, SelectorList list)
    {
        // The invalid selectors of a forgiving selector list are serialized as written, at their position
        var unparsed = list.UnparsedSelectors;
        var count = list.Selectors.Length + unparsed.Length;
        var selectorIndex = 0;
        var unparsedIndex = 0;
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            if (unparsedIndex < unparsed.Length && unparsed[unparsedIndex].Index == i)
            {
                builder.Append(unparsed[unparsedIndex++].Text);
            }
            else
            {
                AppendComplex(builder, list.Selectors[selectorIndex++]);
            }
        }
    }

    /// <summary>https://drafts.csswg.org/cssom/#serialize-a-selector</summary>
    private static void AppendComplex(StringBuilder builder, ComplexSelector selector)
    {
        if (selector.RelativeCombinator is { } relative && relative != Combinator.Descendant)
        {
            builder.Append(GetCombinator(relative).TrimStart());
        }

        // The nesting selector implied by a scoped relative selector is not serialized: "&amp; > .a" is "> .a"
        var first = 0;
        if (selector.Compounds is [{ Selectors: [NestingSelector { Implicit: true }] }, _, ..])
        {
            if (selector.Combinators[0] != Combinator.Descendant)
            {
                builder.Append(GetCombinator(selector.Combinators[0]).TrimStart());
            }

            first = 1;
        }

        for (var i = first; i < selector.Compounds.Length; i++)
        {
            if (i > first)
            {
                builder.Append(GetCombinator(selector.Combinators[i - 1]));
            }

            AppendCompound(builder, selector.Compounds[i]);
        }
    }

    private static string GetCombinator(Combinator combinator) => combinator switch
    {
        Combinator.Child => " > ",
        Combinator.NextSibling => " + ",
        Combinator.SubsequentSibling => " ~ ",
        _ => " ",
    };

    private static void AppendCompound(StringBuilder builder, CompoundSelector compound)
    {
        // The universal selector is omitted when it is not the only simple selector and has no significant namespace prefix
        var selectors = compound.Selectors;
        for (var i = 0; i < selectors.Length; i++)
        {
            var selector = selectors[i];
            if (selector is TypeSelector { LocalName: null } universal && selectors.Length > 1 && (universal.Prefix is null || universal.IsDefaultNamespace))
                continue;

            AppendSimple(builder, selector);
        }
    }

    private static void AppendNamespacePrefix(StringBuilder builder, string? prefix, bool isDefaultNamespace)
    {
        if (prefix is null || isDefaultNamespace)
            return;

        if (prefix.Length == 0)
        {
            builder.Append('|');
        }
        else if (prefix == "*")
        {
            builder.Append("*|");
        }
        else
        {
            builder.Append(CssSerializer.SerializeIdentifier(prefix)).Append('|');
        }
    }

    /// <summary>https://drafts.csswg.org/cssom/#serialize-a-simple-selector</summary>
    private static void AppendSimple(StringBuilder builder, SimpleSelector selector)
    {
        switch (selector)
        {
            case TypeSelector type:
                AppendNamespacePrefix(builder, type.Prefix, type.IsDefaultNamespace);
                builder.Append(type.LocalName is null ? "*" : CssSerializer.SerializeIdentifier(type.LocalName));
                break;

            case IdSelector id:
                builder.Append('#').Append(CssSerializer.SerializeIdentifier(id.Id));
                break;

            case ClassSelector classSelector:
                builder.Append('.').Append(CssSerializer.SerializeIdentifier(classSelector.ClassName));
                break;

            case ClassPrefixSelector classPrefix:
                builder.Append('.').Append(CssSerializer.SerializeIdentifier(classPrefix.Prefix)).Append('*');
                break;

            case NestingSelector:
                builder.Append('&');
                break;

            case AttributeSelector attribute:
                builder.Append('[');

                // The attributes in no namespace are serialized without prefix ([|att] is [att])
                if (attribute.Prefix is not "")
                {
                    AppendNamespacePrefix(builder, attribute.Prefix, isDefaultNamespace: false);
                }

                builder.Append(CssSerializer.SerializeIdentifier(attribute.Name));
                if (attribute.Operator != AttributeOperator.Exists)
                {
                    builder.Append(attribute.Operator switch
                    {
                        AttributeOperator.Includes => "~=",
                        AttributeOperator.DashMatch => "|=",
                        AttributeOperator.Prefix => "^=",
                        AttributeOperator.Suffix => "$=",
                        AttributeOperator.Substring => "*=",
                        _ => "=",
                    });
                    builder.Append(CssSerializer.SerializeString(attribute.Value));
                    if (attribute.CaseSensitivity == AttributeCaseSensitivity.Insensitive)
                    {
                        builder.Append(" i");
                    }
                    else if (attribute.CaseSensitivity == AttributeCaseSensitivity.Sensitive)
                    {
                        builder.Append(" s");
                    }
                }

                builder.Append(']');
                break;

            case PseudoClassSelector pseudoClass:
                AppendPseudoClass(builder, pseudoClass);
                break;

            case PseudoElementSelector pseudoElement:
                builder.Append("::").Append(pseudoElement.Name);
                if (pseudoElement.IsFunction)
                {
                    builder.Append('(');
                    if (pseudoElement.Argument is { } argument)
                    {
                        AppendList(builder, argument);
                    }
                    else
                    {
                        builder.Append(pseudoElement.ArgumentText);
                    }

                    builder.Append(')');
                }

                break;
        }
    }

    private static void AppendPseudoClass(StringBuilder builder, PseudoClassSelector selector)
    {
        builder.Append(':').Append(selector.Name);
        switch (selector.Kind)
        {
            case PseudoClass.Is or PseudoClass.Where or PseudoClass.Not or PseudoClass.Has or PseudoClass.HostFunction or PseudoClass.HostContext:
                builder.Append('(');
                AppendList(builder, selector.Argument!);
                builder.Append(')');
                break;

            case PseudoClass.NthChild or PseudoClass.NthLastChild or PseudoClass.NthOfType or PseudoClass.NthLastOfType:
                builder.Append('(').Append(SerializeAnB(selector.Nth));
                if (selector.Argument is { } of)
                {
                    builder.Append(" of ");
                    AppendList(builder, of);
                }

                builder.Append(')');
                break;

            case PseudoClass.Lang:
                builder.Append('(').Append(selector.ArgumentText).Append(')');
                break;

            case PseudoClass.Dir or PseudoClass.State:
                builder.Append('(').Append(CssSerializer.SerializeIdentifier(selector.Strings[0])).Append(')');
                break;

            case PseudoClass.ActiveViewTransitionType:
                builder.Append('(').Append(string.Join(", ", selector.Strings.Select(CssSerializer.SerializeIdentifier))).Append(')');
                break;

            case PseudoClass.HeadingFunction:
                builder.Append('(').Append(string.Join(", ", selector.Levels.Select(l => l.ToString(CultureInfo.InvariantCulture)))).Append(')');
                break;
        }
    }

    /// <summary>https://drafts.csswg.org/css-syntax-3/#serializing-anb</summary>
    public static string SerializeAnB(AnB value)
    {
        if (value.A == 0)
            return value.B.ToString(CultureInfo.InvariantCulture);

        var builder = new StringBuilder();
        builder.Append(value.A switch
        {
            1 => "n",
            -1 => "-n",
            _ => value.A.ToString(CultureInfo.InvariantCulture) + "n",
        });

        if (value.B > 0)
        {
            builder.Append('+').Append(value.B.ToString(CultureInfo.InvariantCulture));
        }
        else if (value.B < 0)
        {
            builder.Append(value.B.ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
