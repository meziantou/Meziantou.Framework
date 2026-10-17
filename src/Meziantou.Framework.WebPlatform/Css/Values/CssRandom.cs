using System.Text;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

/// <summary>
/// The &lt;random-value-sharing&gt; of random() (https://drafts.csswg.org/css-values-5/#random): the random caching key is made
/// of the &lt;dashed-ident&gt;, the user agent &lt;random-ua-ident&gt; (ua-*) and the element when element-scoped is specified.
/// auto (the default), property-scoped and property-index-scoped are replaced at parse time by a &lt;random-ua-ident&gt; made
/// of the name of the property and the index of the random() function in the value (e.g. element-scoped ua-width-1).
/// </summary>
internal sealed record CssRandomSharing(string? DashedIdent, bool ElementScoped, string? UaIdent, string? ScopeKeyword)
{
    /// <summary>The default sharing (auto), before the &lt;random-ua-ident&gt; is assigned.</summary>
    public static CssRandomSharing Auto { get; } = new(DashedIdent: null, ElementScoped: false, UaIdent: null, ScopeKeyword: "auto");

    /// <summary>The random caching key, without the element.</summary>
    public string CachingKey => (DashedIdent ?? "") + " " + (UaIdent ?? ScopeKeyword ?? "");

    /// <summary>Serializes the sharing in the canonical order: the &lt;dashed-ident&gt;, element-scoped, then the &lt;random-ua-ident&gt;.</summary>
    public string Serialize()
    {
        var builder = new StringBuilder();
        if (DashedIdent is not null)
        {
            builder.Append(CssSerializer.SerializeIdentifier(DashedIdent));
        }

        if (ElementScoped)
        {
            builder.Append(builder.Length > 0 ? " " : "").Append("element-scoped");
        }

        if ((UaIdent ?? ScopeKeyword) is { } scope && (UaIdent is not null || ScopeKeyword != "auto" || builder.Length == 0))
        {
            builder.Append(builder.Length > 0 ? " " : "").Append(CssSerializer.SerializeIdentifier(scope));
        }

        return builder.ToString();
    }
}

internal static class CssRandom
{
    /// <summary>
    /// Parses a &lt;random-value-sharing&gt; other than fixed: [ auto | [ &lt;dashed-ident&gt; || element-scoped ||
    /// [ property-scoped | property-index-scoped | &lt;random-ua-ident&gt; ] ] ]. Returns null when it is invalid.
    /// </summary>
    public static CssRandomSharing? ParseSharing(List<CssComponentValue> significant)
    {
        if (significant.Count == 0)
            return null;

        if (significant is [CssPreservedToken { Type: CssTokenType.Ident } single] && single.Token.IsIdent("auto"))
            return CssRandomSharing.Auto;

        string? dashedIdent = null;
        var elementScoped = false;
        string? uaIdent = null;
        string? scopeKeyword = null;
        foreach (var value in significant)
        {
            if (value is not CssPreservedToken { Type: CssTokenType.Ident } ident)
                return null;

            var name = ident.Token.Value;
            var lower = InfraAscii.ToAsciiLowercase(name);
            if (name.StartsWith("--", StringComparison.Ordinal))
            {
                if (dashedIdent is not null)
                    return null;

                dashedIdent = name;
            }
            else if (lower == "element-scoped")
            {
                if (elementScoped)
                    return null;

                elementScoped = true;
            }
            else if (lower is "property-scoped" or "property-index-scoped" || (lower.StartsWith("ua-", StringComparison.Ordinal) && lower.Length > 3))
            {
                if (uaIdent is not null || scopeKeyword is not null)
                    return null;

                if (lower.StartsWith("ua-", StringComparison.Ordinal))
                {
                    uaIdent = name;
                }
                else
                {
                    scopeKeyword = lower;
                }
            }
            else
            {
                return null;
            }
        }

        return new CssRandomSharing(dashedIdent, elementScoped, uaIdent, scopeKeyword);
    }

    /// <summary>Returns whether the first argument of random() is a &lt;random-value-sharing&gt; (it starts with a keyword that is not a &lt;calc-keyword&gt;).</summary>
    public static bool IsSharingArgument(List<CssComponentValue> significant) =>
        significant is [CssPreservedToken { Type: CssTokenType.Ident } first, ..] && InfraAscii.ToAsciiLowercase(first.Token.Value) is not ("e" or "pi" or "infinity" or "-infinity" or "nan");

    /// <summary>
    /// Replaces the auto, property-scoped and property-index-scoped sharing of the random() functions of the value of a property by
    /// a &lt;random-ua-ident&gt; (https://drafts.csswg.org/css-values-5/#random-caching-key): ua-&lt;property&gt; for property-scoped,
    /// ua-&lt;property&gt;-&lt;index&gt; for property-index-scoped and element-scoped ua-&lt;property&gt;-&lt;index&gt; for auto, where the
    /// index is the position of the random() function among the random() functions of the value (from 1).
    /// Returns the values unchanged when they do not contain random().
    /// </summary>
    /// <param name="property">The name of the property, or of the descriptor of a custom function (e.g. --f-result).</param>
    /// <param name="values">The value.</param>
    /// <param name="invocation">
    /// In a custom function, the identifier of its invocation: the element-scoped random() functions are scoped to the
    /// "hypothetical element" of the invocation, so the identifier is added to their &lt;random-ua-ident&gt;.
    /// </param>
    public static List<CssComponentValue> AssignCachingKeys(string property, List<CssComponentValue> values, string? invocation = null)
    {
        if (!ContainsRandom(values))
            return values;

        var index = 0;
        return Assign(property, values, invocation, ref index, AssignMode.Rewrite, splices: null);
    }

    /// <summary>
    /// Assigns the caching keys of the random() functions of the conditions of the if() functions of a declared value (they are
    /// evaluated in the property, https://github.com/w3c/csswg-drafts/issues/13662), whose index is their position among all the
    /// random() functions of the value. The other random() functions are left as they are: they are evaluated where they are
    /// substituted. Returns the text unchanged when there is no such random() function.
    /// </summary>
    public static string AssignIfConditionCachingKeys(string property, string text)
    {
        if (!text.Contains("random(", StringComparison.OrdinalIgnoreCase) || !text.Contains("if(", StringComparison.OrdinalIgnoreCase))
            return text;

        var values = CssParser.ParseListOfComponentValues(text, out var source);
        var splices = new List<(int Start, int End, string Text)>();
        var index = 0;
        Assign(property, values, invocation: null, ref index, AssignMode.Count, splices);
        if (splices.Count == 0)
            return text;

        // The rewritten random() functions replace their source: the rest of the value is unchanged
        var builder = new StringBuilder(source.Length);
        var position = 0;
        foreach (var (start, end, replacement) in splices)
        {
            builder.Append(source, position, start - position).Append(replacement);
            position = end;
        }

        return builder.Append(source, position, source.Length - position).ToString();
    }

    private static bool ContainsRandom(List<CssComponentValue> values)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case CssFunction function when InfraAscii.EqualsIgnoreAsciiCase(function.Name, "random") || ContainsRandom(function.Value):
                    return true;
                case CssSimpleBlock block when ContainsRandom(block.Value):
                    return true;
            }
        }

        return false;
    }

    private enum AssignMode
    {
        /// <summary>The random() functions are rewritten with their caching keys.</summary>
        Rewrite,

        /// <summary>The random() functions are counted; the ones of the conditions of the if() functions are spliced.</summary>
        Count,

        /// <summary>The random() functions are spliced (in an if() condition).</summary>
        Splice,
    }

    /// <summary>
    /// Assigns the caching keys of the random() functions of the values (<see cref="AssignMode.Rewrite"/>), or of the conditions
    /// of their if() functions only, the rewritten functions being added to <paramref name="splices"/>; all the random()
    /// functions are counted.
    /// </summary>
    private static List<CssComponentValue> Assign(string property, List<CssComponentValue> values, string? invocation, ref int index, AssignMode mode, List<(int Start, int End, string Text)>? splices)
    {
        var result = new List<CssComponentValue>(values.Count);
        foreach (var value in values)
        {
            switch (value)
            {
                case CssFunction function when mode == AssignMode.Splice && InfraAscii.EqualsIgnoreAsciiCase(function.Name, "random"):
                {
                    var keyed = Assign(property, [function], invocation, ref index, AssignMode.Rewrite, splices: null);
                    splices!.Add((function.Start, function.End, CssSerializer.SerializeValue(keyed, property: null)));
                    result.Add(function);
                    break;
                }

                case CssFunction function when mode == AssignMode.Count && InfraAscii.EqualsIgnoreAsciiCase(function.Name, "random"):
                    // The random() functions of the arguments are counted too
                    index++;
                    Assign(property, function.Value, invocation, ref index, AssignMode.Count, splices: null);
                    result.Add(function);
                    break;

                case CssFunction function when InfraAscii.EqualsIgnoreAsciiCase(function.Name, "random"):
                {
                    index++;

                    var position = index;
                    var arguments = ValueMatcher.SplitAtCommas(function.Value);
                    var copy = new CssFunction(function.Name);
                    var first = ValueMatcher.RemoveWhitespace(arguments[0]);
                    CssRandomSharing? sharing = null;
                    if (IsSharingArgument(first))
                    {
                        if (first[0] is CssPreservedToken firstToken && firstToken.Token.IsIdent("fixed"))
                        {
                            copy.Value.AddRange(Assign(property, arguments[0], invocation, ref index, AssignMode.Rewrite, splices: null));
                            copy.Value.Add(new CssPreservedToken(new CssToken { Type = CssTokenType.Comma }));
                            AppendArguments(property, copy, arguments, 1, invocation, ref index);
                            result.Add(copy);
                            break;
                        }

                        sharing = ParseSharing(first);
                        if (sharing is null)
                        {
                            result.Add(function);
                            break;
                        }

                        arguments.RemoveAt(0);
                    }

                    sharing ??= CssRandomSharing.Auto;
                    sharing = sharing.ScopeKeyword switch
                    {
                        "auto" => sharing with { ElementScoped = true, UaIdent = "ua-" + property + "-" + position, ScopeKeyword = null },
                        "property-scoped" => sharing with { UaIdent = "ua-" + property, ScopeKeyword = null },
                        "property-index-scoped" => sharing with { UaIdent = "ua-" + property + "-" + position, ScopeKeyword = null },
                        _ => sharing,
                    };

                    // In a custom function, the element is the hypothetical element of the invocation
                    if (invocation is not null && sharing.ElementScoped)
                    {
                        sharing = sharing with { UaIdent = (sharing.UaIdent ?? "ua-" + property) + "-" + invocation, ScopeKeyword = null };
                    }

                    copy.Value.AddRange(CssParser.ParseListOfComponentValues(sharing.Serialize()));
                    copy.Value.Add(new CssPreservedToken(new CssToken { Type = CssTokenType.Comma }));
                    AppendArguments(property, copy, arguments, 0, invocation, ref index);
                    result.Add(copy);
                    break;
                }

                case CssFunction function when mode == AssignMode.Count && InfraAscii.EqualsIgnoreAsciiCase(function.Name, "if"):
                {
                    // if( [ <if-condition> : <declaration-value>? ; ]* ): the random() functions of the conditions are spliced
                    var inCondition = true;
                    foreach (var item in function.Value)
                    {
                        if (item is CssPreservedToken { Type: CssTokenType.Semicolon })
                        {
                            inCondition = true;
                        }
                        else if (item is CssPreservedToken { Type: CssTokenType.Colon } && inCondition)
                        {
                            inCondition = false;
                        }
                        else
                        {
                            Assign(property, [item], invocation, ref index, inCondition ? AssignMode.Splice : AssignMode.Count, splices);
                        }
                    }

                    result.Add(function);
                    break;
                }

                case CssFunction function when ContainsRandom(function.Value):
                {
                    var copy = new CssFunction(function.Name);
                    copy.Value.AddRange(Assign(property, function.Value, invocation, ref index, mode, splices));
                    result.Add(copy);
                    break;
                }

                case CssSimpleBlock block when ContainsRandom(block.Value):
                {
                    var copy = new CssSimpleBlock(block.AssociatedToken);
                    copy.Value.AddRange(Assign(property, block.Value, invocation, ref index, mode, splices));
                    result.Add(copy);
                    break;
                }

                default:
                    result.Add(value);
                    break;
            }
        }

        return result;
    }

    private static void AppendArguments(string property, CssFunction function, List<List<CssComponentValue>> arguments, int start, string? invocation, ref int index)
    {
        for (var i = start; i < arguments.Count; i++)
        {
            if (i > start)
            {
                function.Value.Add(new CssPreservedToken(new CssToken { Type = CssTokenType.Comma }));
            }

            function.Value.AddRange(Assign(property, arguments[i], invocation, ref index, AssignMode.Rewrite, splices: null));
        }
    }
}
