using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.Selectors;

namespace Meziantou.Framework.WebPlatform.Formatting;

/// <summary>
/// Writes an indented CSS text, to read or compare style sheets: each rule and each declaration is written on its own line,
/// indented by its depth, and each selector of the selector list of a style rule is written on its own line.
/// </summary>
/// <remarks>An empty block is written on the line of its prelude (<c>a {}</c>).</remarks>
public sealed class CssPrettyFormatter : CssFormatter
{
    /// <summary>The string written for each level of indentation. The default is two spaces.</summary>
    public string Indentation
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = "  ";

    /// <summary>
    /// The maximum number of indentation levels: the content of the deeper blocks is indented as the content at this depth.
    /// The default is 64, which bounds the size of the output of deeply nested input (the indentation of each line would
    /// grow with the depth).
    /// </summary>
    public int MaxIndentationDepth
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = 64;

    /// <summary>The line separator. The default is <c>"\n"</c>.</summary>
    public string NewLine
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = "\n";

    /// <summary>Creates a writer that emits indented CSS to the given output.</summary>
    protected override CssWriter CreateWriter(TextWriter output) => new PrettyWriter(this, output);

    private sealed class PrettyWriter(CssPrettyFormatter formatter, TextWriter output) : CssWriter(output)
    {
        private readonly CssPrettyFormatter _formatter = formatter;

        /// <summary>For each open block, whether an item is written in it.</summary>
        private readonly Stack<bool> _blockHasItems = new();

        private bool _isFirstLine = true;

        protected override void WritePrelude(CssRule rule, string prelude)
        {
            StartItem();
            if (rule is CssStyleRule { Selectors.Selectors: { Length: > 1 } selectors })
            {
                for (var i = 0; i < selectors.Length; i++)
                {
                    if (i > 0)
                    {
                        Output.Write(',');
                        StartLine();
                    }

                    Output.Write(SelectorSerializer.Serialize(selectors[i]));
                }

                return;
            }

            Output.Write(prelude);
        }

        protected override void WriteBlockStart(CssRule rule)
        {
            Output.Write(" {");
            _blockHasItems.Push(false);
        }

        protected override void WriteBlockEnd(CssRule rule)
        {
            if (_blockHasItems.Pop())
            {
                StartLine();
            }

            Output.Write('}');
        }

        protected override void WriteDeclaration(CssRule? rule, string name, string value, bool important)
        {
            StartItem();
            Output.Write(name);
            Output.Write(':');
            if (value.Length > 0)
            {
                Output.Write(' ');
                Output.Write(value);
            }

            if (important)
            {
                Output.Write(" !important");
            }

            Output.Write(';');
        }

        private void StartItem()
        {
            if (_blockHasItems.Count > 0 && !_blockHasItems.Peek())
            {
                _blockHasItems.Pop();
                _blockHasItems.Push(true);
            }

            StartLine();
        }

        private void StartLine()
        {
            if (_isFirstLine)
            {
                _isFirstLine = false;
            }
            else
            {
                Output.Write(_formatter.NewLine);
            }

            if (_formatter.Indentation.Length > 0)
            {
                for (var i = Math.Min(Depth, _formatter.MaxIndentationDepth); i > 0; i--)
                {
                    Output.Write(_formatter.Indentation);
                }
            }
        }
    }
}
