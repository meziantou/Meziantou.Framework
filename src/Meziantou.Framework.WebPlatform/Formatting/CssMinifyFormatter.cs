using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Formatting;

/// <summary>
/// Writes a minified CSS text: the whitespace that is not needed is removed, as well as the semicolon of the last declaration
/// of a block. The style sheet is not modified.
/// </summary>
/// <remarks>
/// The values of the custom properties are written as is: they are token sequences whose text can be read by scripts.
/// </remarks>
public sealed partial class CssMinifyFormatter : CssFormatter
{
    /// <summary>
    /// Whether the rules that have no effect are removed: the style rules without declarations (and whose nested rules are
    /// removed), and the @media, @supports, @container, @scope and @starting-style rules whose child rules are removed. The
    /// empty @layer and @keyframes rules are kept: they declare a layer or replace the keyframes of the same name. The default
    /// is true.
    /// </summary>
    public bool RemoveEmptyRules { get; init; } = true;

    /// <summary>
    /// Whether the opaque <c>rgb()</c> colors whose components are integers are written in their hexadecimal notation
    /// (<c>rgb(255, 0, 0)</c> is written <c>#f00</c>). The default is true.
    /// </summary>
    public bool ShortenColors { get; init; } = true;

    /// <summary>Whether the leading zero of the numbers of the values is removed (<c>0.5s</c> is written <c>.5s</c>). The default is true.</summary>
    public bool RemoveLeadingZeros { get; init; } = true;

    /// <summary>Creates a writer that emits minified CSS to the given output.</summary>
    protected override CssWriter CreateWriter(TextWriter output) => new MinifyWriter(this, output);

    private sealed class MinifyWriter(CssMinifyFormatter formatter, TextWriter output) : CssWriter(output)
    {
        private readonly CssMinifyFormatter _formatter = formatter;

        /// <summary>Whether the semicolon that ends the last declaration is not written yet: it is not needed before the end of a block.</summary>
        private bool _pendingSemicolon;

        protected override bool ShouldWrite(CssRule rule) => !_formatter.RemoveEmptyRules || !IsEmpty(rule);

        protected override void WritePrelude(CssRule rule, string prelude)
        {
            WritePendingSemicolon();
            var kind = rule switch
            {
                CssStyleRule => CssTextKind.Selector,
                _ => CssTextKind.Prelude,
            };

            WriteCompacted(Output, prelude, kind, shortenColors: false, removeLeadingZeros: false);
        }

        protected override void WriteBlockStart(CssRule rule) => Output.Write('{');

        protected override void WriteBlockEnd(CssRule rule)
        {
            _pendingSemicolon = false;
            Output.Write('}');
        }

        protected override void WriteDeclaration(CssRule? rule, string name, string value, bool important)
        {
            WritePendingSemicolon();
            Output.Write(name);
            Output.Write(':');
            if (CssPropertyRegistry.IsCustomProperty(name))
            {
                Output.Write(value);
            }
            else
            {
                WriteCompacted(Output, value, CssTextKind.Value, _formatter.ShortenColors, _formatter.RemoveLeadingZeros);
            }

            if (important)
            {
                Output.Write("!important");
            }

            _pendingSemicolon = true;
        }

        private void WritePendingSemicolon()
        {
            if (_pendingSemicolon)
            {
                Output.Write(';');
                _pendingSemicolon = false;
            }
        }

        /// <summary>Whether the rule has no effect: it has no declarations, and its child rules have no effect.</summary>
        private static bool IsEmpty(CssRule rule)
        {
            return rule switch
            {
                CssStyleRule styleRule => styleRule.Style.Declarations.Count == 0 && AreEmpty(styleRule.ChildRules),
                CssNestedDeclarations nested => nested.Style.Declarations.Count == 0,
                CssMediaRule or CssSupportsRule or CssContainerRule or CssScopeRule or CssStartingStyleRule => AreEmpty(rule.ChildRules!),
                _ => false,
            };

            static bool AreEmpty(List<CssRule> rules) => rules.TrueForAll(IsEmpty);
        }
    }
}
