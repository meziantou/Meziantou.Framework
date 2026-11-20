using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>
/// The private properties (https://drafts.csswg.org/css-mixins-1/#private) visible in a lexical scope: a style rule, the body of
/// an application of a mixin, or a contents block. A private property is a custom property with a hidden name, unique to the
/// scope that declares it: the @private rules of the scope declare it, and the declarations and the var() references of the
/// scope and of its nested rules that use its name are renamed to the hidden name before the cascade. So a private property
/// resolves on its declaring element and inherits like a custom property, but only the rules of its scope can reference it.
/// </summary>
/// <remarks>
/// The initial values of the @private rules only see the private properties of their own scope (a nested scope does not
/// capture the private properties of the enclosing ones), while the other declarations see the private properties of the
/// enclosing scopes. The hidden names are not exposed by the computed styles (see <see cref="IsHiddenName"/>).
/// </remarks>
internal sealed class CssPrivateScope
{
    /// <summary>The prefix of the hidden names: a character of a private use area (an ident code point) that no author style sheet contains in practice.</summary>
    private const string HiddenPrefix = "--\U0010FFFD";

    private static long s_nextId;

    private CssPrivateScope(Dictionary<string, string> own, Dictionary<string, string> visible)
    {
        Own = own;
        Visible = visible;
    }

    /// <summary>The private properties declared by the scope: the hidden name by name.</summary>
    public Dictionary<string, string> Own { get; }

    /// <summary>The private properties visible in the scope (those of the scope and of the enclosing scopes): the hidden name by name.</summary>
    public Dictionary<string, string> Visible { get; }

    /// <summary>Returns whether a custom property name is the hidden name of a private property.</summary>
    public static bool IsHiddenName(string name) => name.StartsWith(HiddenPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Returns the scope of rules (the child rules of a style rule, a mixin body, a contents block): the private properties
    /// of its @private rules (in it or in its conditional group rules, whatever their conditions) are added to those of the
    /// enclosing scope. Returns the enclosing scope when the rules declare none.
    /// </summary>
    public static CssPrivateScope? Create(List<CssRule> rules, CssPrivateScope? enclosing)
    {
        Dictionary<string, string>? own = null;
        CollectNames(rules, ref own);
        if (own is null)
            return enclosing;

        var visible = enclosing is null ? new Dictionary<string, string>(StringComparer.Ordinal) : new Dictionary<string, string>(enclosing.Visible, StringComparer.Ordinal);
        foreach (var (name, hiddenName) in own)
        {
            visible[name] = hiddenName;
        }

        return new CssPrivateScope(own, visible);
    }

    private static void CollectNames(List<CssRule> rules, ref Dictionary<string, string>? names)
    {
        foreach (var rule in rules)
        {
            switch (rule)
            {
                case CssPrivateRule privateRule:
                    foreach (var declaration in privateRule.Declarations)
                    {
                        names ??= new(StringComparer.Ordinal);
                        if (!names.ContainsKey(declaration.Name))
                        {
                            var id = Interlocked.Increment(ref s_nextId).ToString(CultureInfo.InvariantCulture);
                            names[declaration.Name] = HiddenPrefix + id + declaration.Name;
                        }
                    }

                    break;

                case CssGroupingRule group when group is CssMediaRule or CssSupportsRule or CssContainerRule or CssLayerBlockRule or CssStartingStyleRule:
                    CollectNames(group.ChildRules, ref names);
                    break;
            }
        }
    }

    /// <summary>Returns the declarations of a @private rule of the scope: custom properties with hidden names, whose values only see the private properties of the scope.</summary>
    public List<CssDeclarationEntry> GetPrivateDeclarations(CssPrivateRule rule)
    {
        var result = new List<CssDeclarationEntry>(rule.Declarations.Count);
        foreach (var declaration in rule.Declarations)
        {
            if (Own.TryGetValue(declaration.Name, out var hiddenName))
            {
                result.Add(new CssDeclarationEntry(new CssLonghandValue(hiddenName, RewriteReferences(declaration.Value, Own)), important: false));
            }
        }

        return result;
    }

    /// <summary>Returns the declarations with the private properties of the scope renamed (the declarations themselves when they use none).</summary>
    public List<CssDeclarationEntry> Rewrite(List<CssDeclarationEntry> declarations)
    {
        List<CssDeclarationEntry>? result = null;
        for (var i = 0; i < declarations.Count; i++)
        {
            var declaration = declarations[i];
            var value = declaration.Value;
            var name = Visible.TryGetValue(value.Name, out var hiddenName) ? hiddenName : value.Name;
            var rewritten = value with
            {
                Name = name,
                Value = RewriteReferences(value.Value),
                ShorthandValue = value.ShorthandValue is null ? null : RewriteReferences(value.ShorthandValue),
                ComputationValue = value.ComputationValue is null ? null : RewriteReferences(value.ComputationValue),
                TypedValue = value.TypedValue is null ? null : RewriteReferences(value.TypedValue),
            };

            if (result is null)
            {
                if (rewritten == value)
                    continue;

                result = new List<CssDeclarationEntry>(declarations.Count);
                result.AddRange(declarations.Take(i));
            }

            result.Add(rewritten == value ? declaration : CopyDeclaration(declaration, rewritten));
        }

        return result ?? declarations;
    }

    private static CssDeclarationEntry CopyDeclaration(CssDeclarationEntry declaration, CssLonghandValue value)
    {
        var copy = new CssDeclarationEntry(value, declaration.Important);
        copy.SetBaseUrl(declaration.BaseUrlDocument, declaration.BaseUrl);
        return copy;
    }

    /// <summary>Renames the var() references to the private properties visible in the scope.</summary>
    public string RewriteReferences(string text) => RewriteReferences(text, Visible);

    private static string RewriteReferences(string text, Dictionary<string, string> names)
    {
        if (names.Count == 0 || !text.Contains("var(", StringComparison.OrdinalIgnoreCase))
            return text;

        var values = CssParser.ParseListOfComponentValues(text, out var source);
        List<(int Start, int End, string HiddenName)>? replacements = null;
        CollectReferences(values, names, ref replacements);
        if (replacements is null)
            return text;

        var builder = new StringBuilder(source.Length + (replacements.Count * 16));
        var position = 0;
        foreach (var (start, end, hiddenName) in replacements.OrderBy(r => r.Start))
        {
            builder.Append(source, position, start - position).Append(CssSerializer.SerializeIdentifier(hiddenName));
            position = end;
        }

        return builder.Append(source, position, source.Length - position).ToString();
    }

    private static void CollectReferences(List<CssComponentValue> values, Dictionary<string, string> names, ref List<(int Start, int End, string HiddenName)>? replacements)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case CssFunction function:
                    if (function.Name.Equals("var", StringComparison.OrdinalIgnoreCase)
                        && function.Value.Find(v => v is not CssPreservedToken { Type: CssTokenType.Whitespace }) is CssPreservedToken { Type: CssTokenType.Ident } ident
                        && names.TryGetValue(ident.Value, out var hiddenName))
                    {
                        (replacements ??= []).Add((ident.Start, ident.End, hiddenName));
                    }

                    CollectReferences(function.Value, names, ref replacements);
                    break;

                case CssSimpleBlock block:
                    CollectReferences(block.Value, names, ref replacements);
                    break;
            }
        }
    }
}
