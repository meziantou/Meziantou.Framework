using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Formatting;

/// <summary>
/// Writes the text of a style sheet, a rule or a declaration block. The default implementation is the CSSOM serialization
/// (https://drafts.csswg.org/cssom/#serialize-a-css-rule); a derived class customizes the output by overriding the method of
/// each part of a rule.
/// </summary>
/// <remarks>
/// <para>
/// A rule is written as its prelude (<see cref="WritePrelude"/>) followed by a semicolon (<see cref="WriteStatementEnd"/>) for
/// the statement at-rules (@import, @namespace and the @layer statements), or by a block: <see cref="WriteBlockStart"/>, the
/// declarations or descriptors (<see cref="WriteDeclaration"/>), the child rules and <see cref="WriteBlockEnd"/>. The nested
/// declarations (<see cref="CssNestedDeclarations"/>) have no prelude and no block: only their declarations are written.
/// </para>
/// <para>
/// The default implementation of <see cref="WritePrelude"/> and <see cref="WriteDeclaration"/> writes the whitespace that
/// precedes the rule or the declaration in the CSSOM serialization. An instance writes one style sheet, rule or declaration
/// block: create a new writer for each call (see <see cref="CssFormatter.CreateWriter"/>).
/// </para>
/// </remarks>
public class CssWriter
{
    /// <summary>The blocks being written, innermost on top, to write the separators of the CSSOM serialization.</summary>
    private readonly Stack<BlockState> _blocks = new();

    /// <summary>Creates a writer that emits CSS to the given output.</summary>
    public CssWriter(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        Output = output;
    }

    /// <summary>The writer the text is written to.</summary>
    protected TextWriter Output { get; }

    /// <summary>
    /// The number of blocks, between the root and the rule or the declaration being written, whose start is written: 0 for
    /// the rules of a style sheet, the rule passed to <see cref="Write(CssRule)"/> and the declarations of a declaration
    /// block; the items of a block are at the depth of its rule plus 1.
    /// </summary>
    protected int Depth { get; private set; }

    /// <summary>Writes the rules of <paramref name="styleSheet"/>.</summary>
    /// <exception cref="DomException">The style sheet is imported from another origin (SecurityError, see <see cref="CssStyleSheet.CssRules"/>).</exception>
    public virtual void Write(CssStyleSheet styleSheet)
    {
        ArgumentNullException.ThrowIfNull(styleSheet);
        _blocks.Push(new BlockState(BlockKind.Root));
        foreach (var rule in styleSheet.CssRules)
        {
            WriteRule(rule);
        }

        _blocks.Pop();
    }

    /// <summary>Writes <paramref name="rule"/>, with its prelude, its declarations and its child rules (as the <see cref="CssRule.CssText"/> getter).</summary>
    public virtual void Write(CssRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _blocks.Push(new BlockState(BlockKind.Root));
        WriteRule(rule);
        _blocks.Pop();
    }

    /// <summary>
    /// Writes the declarations of <paramref name="declarations"/> (as the <see cref="CssStyleDeclaration.CssText"/> getter):
    /// nothing for a computed style.
    /// </summary>
    public virtual void Write(CssStyleDeclaration declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);

        // The serialization of a computed style is the empty string (https://drafts.csswg.org/cssom/#dom-cssstyledeclaration-csstext)
        if (declarations.Computed is not null)
            return;

        WriteDeclarationBlock(declarations.ParentRule, declarations.Declarations);
    }

    /// <summary>Writes the declarations of a declaration block (https://drafts.csswg.org/cssom/#serialize-a-css-declaration-block).</summary>
    internal void WriteDeclarationBlock(CssRule? parentRule, List<CssDeclarationEntry> declarations)
    {
        _blocks.Push(new BlockState(BlockKind.Root));
        foreach (var declaration in GetDeclarationsToWrite(CssStyleDeclaration.GetSerializedDeclarations(declarations)))
        {
            WriteDeclaration(parentRule, declaration.Name, declaration.Value, declaration.Important);
        }

        _blocks.Pop();
    }

    /// <summary>Returns whether <paramref name="rule"/> and its child rules are written. Returns true by default.</summary>
    protected virtual bool ShouldWrite(CssRule rule) => true;

    /// <summary>
    /// Writes the prelude of a rule: the selectors of a style rule, the key text of a keyframe, the at-keyword and the prelude
    /// of an at-rule (e.g. <c>@media screen</c>). It is not called for the nested declarations.
    /// </summary>
    /// <param name="rule">The rule.</param>
    /// <param name="prelude">The serialized prelude of the rule.</param>
    protected virtual void WritePrelude(CssRule rule, string prelude)
    {
        ArgumentNullException.ThrowIfNull(prelude);
        WriteSeparator(isDeclaration: false);
        Output.Write(prelude);
    }

    /// <summary>Writes the semicolon that ends a statement at-rule (@import, @namespace and the @layer statements).</summary>
    protected virtual void WriteStatementEnd(CssRule rule) => Output.Write(';');

    /// <summary>Writes the start of the block of a rule: a space and <c>{</c>.</summary>
    protected virtual void WriteBlockStart(CssRule rule) => Output.Write(" {");

    /// <summary>
    /// Writes the end of the block of a rule: <c>}</c>, on its own line for the rules that contain rules, preceded by a
    /// space for the rules that only contain declarations.
    /// </summary>
    protected virtual void WriteBlockEnd(CssRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        Output.Write(rule.HasMultilineBlock ? "\n}" : " }");
    }

    /// <summary>Writes a declaration or a descriptor: <c>name: value;</c>, with <c>!important</c> before the semicolon when it is important.</summary>
    /// <param name="rule">The rule that contains the declaration, or null for the declarations of a style attribute.</param>
    /// <param name="name">The serialized property name: a shorthand when the longhands are written together.</param>
    /// <param name="value">The serialized value.</param>
    /// <param name="important">Whether the declaration is important.</param>
    protected virtual void WriteDeclaration(CssRule? rule, string name, string value, bool important)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        WriteSeparator(isDeclaration: true);
        Output.Write(name);
        // The value follows ": ", even when it is empty (the longhands of a shorthand whose value has a var() reference,
        // https://drafts.csswg.org/css-variables/#variables-in-shorthands, are serialized as "margin-right: ;")
        Output.Write(": ");
        Output.Write(value);

        if (important)
        {
            Output.Write(" !important");
        }

        Output.Write(';');
    }

    private void WriteRule(CssRule rule)
    {
        if (!ShouldWrite(rule))
            return;

        if (rule is CssNestedDeclarations or CssFunctionDeclarations)
        {
            // The nested declarations are an item of the block that contains them: their first declaration starts a line
            if (_blocks.TryPeek(out var parent))
            {
                parent.LastItemIsDeclaration = false;
            }

            WriteDeclarations(rule);
            return;
        }

        WritePrelude(rule, rule.GetPrelude());
        if (rule.IsStatement)
        {
            WriteStatementEnd(rule);
            return;
        }

        WriteBlockStart(rule);
        Depth++;
        _blocks.Push(new BlockState(rule.HasMultilineBlock ? BlockKind.Multiline : BlockKind.SingleLine));
        if (MergeAdjacentDeclarations && rule.ChildRules is { } nestedRules)
        {
            WriteItemsMergingDeclarations(rule, nestedRules);
        }
        else
        {
            WriteDeclarations(rule);
            if (rule.ChildRules is { } children)
            {
                foreach (var child in children)
                {
                    WriteRule(child);
                }
            }
        }

        _blocks.Pop();
        Depth--;
        WriteBlockEnd(rule);
    }

    /// <summary>
    /// Whether the declarations that are written without a rule between them (the declarations of a style rule and its nested
    /// declarations, separated by the rules that are not written) are written as one declaration block: the parser reads them
    /// as one block, so the declarations of a property are merged, as the parser does (the last one replaces the others).
    /// </summary>
    internal virtual bool MergeAdjacentDeclarations => false;

    private void WriteItemsMergingDeclarations(CssRule rule, List<CssRule> children)
    {
        // The declaration blocks written since the last rule: the declarations of the style rule, and the nested declarations
        // (the declarations of the other rules, such as the descriptors of @page, are not merged with nested declarations)
        var pending = new List<CssRule>();
        if (rule is CssStyleRule)
        {
            pending.Add(rule);
        }
        else
        {
            WriteDeclarations(rule);
        }

        foreach (var child in children)
        {
            if (!ShouldWrite(child))
                continue;

            if (child is CssNestedDeclarations)
            {
                pending.Add(child);
                continue;
            }

            WritePendingDeclarations(rule, pending);
            WriteRule(child);
        }

        WritePendingDeclarations(rule, pending);
    }

    private void WritePendingDeclarations(CssRule rule, List<CssRule> pending)
    {
        switch (pending)
        {
            case []:
                return;

            case [var single]:
                if (ReferenceEquals(single, rule))
                {
                    WriteDeclarations(rule);
                }
                else
                {
                    WriteRule(single);
                }

                break;

            default:
                var merged = new List<CssDeclarationEntry>();
                foreach (var source in pending)
                {
                    var declarations = source switch
                    {
                        CssStyleRule styleRule => styleRule.Style.Declarations,
                        CssNestedDeclarations nested => nested.Style.Declarations,
                        _ => throw new InvalidOperationException("Unexpected declaration block"),
                    };

                    foreach (var declaration in declarations)
                    {
                        CssStyleDeclaration.AddDeclaration(merged, declaration);
                    }
                }

                foreach (var declaration in GetDeclarationsToWrite(CssStyleDeclaration.GetSerializedDeclarations(merged)))
                {
                    WriteDeclaration(rule, declaration.Name, declaration.Value, declaration.Important);
                }

                break;
        }

        pending.Clear();
    }

    private void WriteDeclarations(CssRule rule)
    {
        foreach (var declaration in GetDeclarationsToWrite(rule.GetDeclarations()))
        {
            WriteDeclaration(rule, declaration.Name, declaration.Value, declaration.Important);
        }
    }

    /// <summary>
    /// Whether the longhands whose value is only known through the shorthand that set them (e.g. the longhands of
    /// <c>border: var(--width) solid</c> followed by <c>border-right-color: red</c>) are written as that shorthand, at the
    /// position of the first of them, instead of a declaration with an empty value: the CSSOM serialization writes
    /// <c>border-top-width: ;</c>, as the browsers, which is invalid and is not parsed again.
    /// </summary>
    internal virtual bool WritePendingLonghandsAsShorthand => false;

    private List<CssSerializedDeclaration> GetDeclarationsToWrite(List<CssSerializedDeclaration> declarations)
    {
        if (!WritePendingLonghandsAsShorthand || !declarations.Exists(declaration => declaration.PendingShorthandName is not null))
            return declarations;

        // Parsing the shorthand sets its longhands again, followed by the declarations that override some of them: they are
        // after the longhands set by the shorthand, as the parser moves the declaration of a property to the end of the block
        var result = new List<CssSerializedDeclaration>(declarations.Count);
        var writtenShorthands = new HashSet<(string Name, string Value, bool Important)>();
        foreach (var declaration in declarations)
        {
            if (declaration is { PendingShorthandName: { } name, PendingShorthandValue: { } value })
            {
                if (writtenShorthands.Add((name, value, declaration.Important)))
                {
                    result.Add(new CssSerializedDeclaration(name, value, declaration.Important));
                }

                continue;
            }

            result.Add(declaration);
        }

        return result;
    }

    /// <summary>
    /// Writes the separator of the CSSOM serialization before an item: a line feed between the rules of a style sheet, a line
    /// feed and two spaces before the rules of a block that contains rules, a space otherwise.
    /// </summary>
    private void WriteSeparator(bool isDeclaration)
    {
        if (!_blocks.TryPeek(out var block))
            return;

        var separator = block.Kind switch
        {
            _ when block.HasItems && isDeclaration && block.LastItemIsDeclaration => " ",
            BlockKind.Root => block.HasItems ? "\n" : "",
            BlockKind.Multiline => "\n  ",
            _ => " ",
        };

        Output.Write(separator);
        block.HasItems = true;
        block.LastItemIsDeclaration = isDeclaration;
    }

    private enum BlockKind
    {
        /// <summary>The style sheet, the rule or the declaration block passed to Write.</summary>
        Root,

        /// <summary>A block whose items are written on the line of the prelude.</summary>
        SingleLine,

        /// <summary>A block whose items are written on their own line (the consecutive declarations are written on the same line).</summary>
        Multiline,
    }

    private sealed class BlockState(BlockKind kind)
    {
        public BlockKind Kind { get; } = kind;

        public bool HasItems { get; set; }

        public bool LastItemIsDeclaration { get; set; }
    }
}
