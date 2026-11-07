using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Language.Syntax;
using GreenList = Meziantou.Framework.Language.InternalSyntax.SyntaxList;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Ini.Syntax.InternalSyntax;

/// <summary>A whole INI document: its entries, the end of the text, and the options it is read with.</summary>
/// <remarks>
/// The options travel with the document through every edit, because an edit has to write values the way the document
/// reads them, and an edited document belongs to no <see cref="IniSyntaxTree"/> that could say.
/// </remarks>
internal sealed class IniDocumentSyntax : IniSyntaxNode
{
    private readonly GreenNode? _entries;
    private readonly GreenNode _endOfFileToken;

    /// <summary>Creates a document from entries that already end their lines, such as the ones the parser reads.</summary>
    public IniDocumentSyntax(GreenNode? entries, GreenNode endOfFileToken, IniParseOptions options)
        : this(entries, endOfFileToken, options, diagnostics: null, annotations: null)
    {
    }

    private IniDocumentSyntax(GreenNode? entries, GreenNode endOfFileToken, IniParseOptions options, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.IniDocument, diagnostics, annotations)
    {
        SlotCount = 2;
        AdjustFlagsAndWidth(entries);
        _entries = entries;
        AdjustFlagsAndWidth(endOfFileToken);
        _endOfFileToken = endOfFileToken;
        Options = options;
    }

    public IniParseOptions Options { get; }

    /// <summary>Creates a document from entries built or edited by hand, ending the line of each one that needs it.</summary>
    public static IniDocumentSyntax Create(GreenNode? entries, GreenNode endOfFileToken, IniParseOptions options)
        => new(EndLines(entries, endOfFileToken, options), endOfFileToken, options);

    public IniDocumentSyntax WithOptions(IniParseOptions options)
        => ReferenceEquals(options, Options) ? this : new IniDocumentSyntax(_entries, _endOfFileToken, options, GetDiagnostics(), GetAnnotations());

    internal override GreenNode? GetSlot(int index) => index switch
    {
        0 => _entries,
        1 => _endOfFileToken,
        _ => null,
    };

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots)
    {
        var endOfFileToken = RequiredSlot(slots[1]);

        return new IniDocumentSyntax(EndLines(slots[0], endOfFileToken, Options), endOfFileToken, Options, GetDiagnostics(), GetAnnotations());
    }

    internal override bool IsListSlot(int index) => index is 0;
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new IniDocumentSyntax(_entries, _endOfFileToken, Options, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new IniDocumentSyntax(_entries, _endOfFileToken, Options, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Ini.IniDocumentSyntax(this, parent, position);

    /// <summary>Keeps every entry of an edited document on lines of its own, so that it reads back as the same entries.</summary>
    /// <remarks>
    /// <para>
    /// Every entry the parser reads meets these rules already, which keeps an edit from touching any line other than the
    /// ones it changes:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// An entry ends its line when another entry follows it, or when a comment follows it at the end of the document. The
    /// line break added is the first one of the document, or a line feed.
    /// </item>
    /// <item>
    /// The lines of a value end before the lines it continues on.
    /// </item>
    /// <item>
    /// Skipped text stays on the line of the section header it follows. Skipped text that no longer follows its header is
    /// moved back after it, or removed with it when the header is gone.
    /// </item>
    /// <item>
    /// With <see cref="IniParseOptions.AllowMultilineValues"/>, an entry after a property is indented no more than its key,
    /// so that it does not continue its value, and the lines a value continues on are indented more than its key.
    /// </item>
    /// </list>
    /// </remarks>
    internal static GreenNode? EndLines(GreenNode? entries, GreenNode endOfFileToken, IniParseOptions options)
    {
        var items = GreenNodeList.ToArray(entries);
        var changed = KeepSkippedTextWithItsHeader(ref items);
        GreenNode? endOfLine = null;
        for (var i = 0; i < items.Length; i++)
        {
            var entry = items[i];
            if (entry is null)
                continue;

            if (entry.RawKind == (int)SyntaxKind.IniProperty)
            {
                entry = EndValueLines(entry, ref endOfLine, entries);
            }

            var next = i + 1 < items.Length ? items[i + 1] : null;
            var needsEndOfLine = next is null ? StartsWithComment((endOfFileToken as GreenToken)?.LeadingTrivia) : next.RawKind != (int)SyntaxKind.IniSkippedText;
            if (needsEndOfLine && !EndsItsLine(entry))
            {
                entry = SyntaxNodeRemover.AppendTrailingTrivia(entry, GetEndOfLine(ref endOfLine, entries));
            }

            if (!ReferenceEquals(entry, items[i]))
            {
                items[i] = entry;
                changed = true;
            }
        }

        if (options.AllowMultilineValues)
        {
            changed |= KeepEntriesOutOfValues(items);
        }

        return changed ? SyntaxFactory.List(items) : entries;
    }

    /// <summary>Moves skipped text back after the section header whose line it is on, or removes it when that header is gone.</summary>
    /// <returns>Whether anything was moved or removed.</returns>
    private static bool KeepSkippedTextWithItsHeader(ref GreenNode?[] items)
    {
        var hasStrayText = false;
        for (var i = 0; i < items.Length; i++)
        {
            if (items[i]?.RawKind == (int)SyntaxKind.IniSkippedText && !OwnsSkippedText(i > 0 ? items[i - 1] : null))
            {
                hasStrayText = true;
                break;
            }
        }

        if (!hasStrayText)
            return false;

        var result = new List<GreenNode?>(items.Length);

        // A header that does not end its line, and that no skipped text follows yet: the text was on its line.
        var openHeader = -1;
        foreach (var item in items)
        {
            if (item?.RawKind == (int)SyntaxKind.IniSkippedText)
            {
                if (result.Count > 0 && OwnsSkippedText(result[^1]))
                {
                    result.Add(item);
                }
                else if (openHeader >= 0)
                {
                    result.Insert(openHeader + 1, item);
                }

                openHeader = -1;
                continue;
            }

            result.Add(item);
            if (item?.RawKind == (int)SyntaxKind.IniSection)
            {
                openHeader = EndsItsLine(item) ? -1 : result.Count - 1;
            }
        }

        items = [.. result];
        return true;

        // The parser keeps skipped text only after a section header, on the line of the header.
        static bool OwnsSkippedText(GreenNode? previous) => previous?.RawKind == (int)SyntaxKind.IniSection && !EndsItsLine(previous);
    }

    /// <summary>Ends the lines of a value that continues on the lines below it, but the last one.</summary>
    private static GreenNode EndValueLines(GreenNode property, ref GreenNode? endOfLine, GreenNode? entries)
    {
        var continuations = property.GetSlot(3);
        var count = GreenNodeList.Count(continuations);
        if (count == 0)
            return property;

        var value = property.GetSlot(2) as GreenToken;
        if (value is not null && !HasEndOfLine(value.TrailingTrivia))
        {
            value = value.WithTrivia(value.LeadingTrivia, GreenList.Concat(value.TrailingTrivia, GetEndOfLine(ref endOfLine, entries)));
        }

        GreenNode?[]? lines = null;
        for (var i = 0; i < count - 1; i++)
        {
            if (GreenNodeList.ElementAt(continuations, i) is GreenToken line && !HasEndOfLine(line.TrailingTrivia))
            {
                lines ??= GreenNodeList.ToArray(continuations);
                lines[i] = line.WithTrivia(line.LeadingTrivia, GreenList.Concat(line.TrailingTrivia, GetEndOfLine(ref endOfLine, entries)));
            }
        }

        if (ReferenceEquals(value, property.GetSlot(2)) && lines is null)
            return property;

        return property.WithSlots([property.GetSlot(0), property.GetSlot(1), value, lines is null ? continuations : SyntaxFactory.List(lines)])!;
    }

    /// <summary>Indents the lines of a document read with multiline values so that each one is read as it was written.</summary>
    /// <returns>Whether any line was indented again.</returns>
    private static bool KeepEntriesOutOfValues(GreenNode?[] items)
    {
        var changed = false;

        // The indentation of the key of the property the next line would continue, if it were indented more.
        string? keyIndentation = null;
        for (var i = 0; i < items.Length; i++)
        {
            var entry = items[i];
            if (entry is null || entry.RawKind == (int)SyntaxKind.IniSkippedText)
                continue;

            if (keyIndentation is not null && FirstToken(entry) is { } first && GetIndentation(first.LeadingTrivia).Length > keyIndentation.Length)
            {
                entry = ReplaceToken(entry, first, WithIndentation(first, keyIndentation));
            }

            keyIndentation = null;
            if (entry.RawKind == (int)SyntaxKind.IniProperty && entry.GetSlot(1) is { IsMissing: false })
            {
                keyIndentation = FirstToken(entry) is { } key ? GetIndentation(key.LeadingTrivia) : "";
                entry = IndentValueLines(entry, keyIndentation);
            }

            if (!ReferenceEquals(entry, items[i]))
            {
                items[i] = entry;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>Indents the lines a value continues on more than its key, which they would not continue otherwise.</summary>
    private static GreenNode IndentValueLines(GreenNode property, string keyIndentation)
    {
        var continuations = property.GetSlot(3);
        var count = GreenNodeList.Count(continuations);
        GreenNode?[]? lines = null;
        for (var i = 0; i < count; i++)
        {
            if (GreenNodeList.ElementAt(continuations, i) is GreenToken line && GetIndentation(line.LeadingTrivia).Length <= keyIndentation.Length)
            {
                lines ??= GreenNodeList.ToArray(continuations);
                lines[i] = WithIndentation(line, keyIndentation + "    ");
            }
        }

        return lines is null ? property : property.WithSlots([property.GetSlot(0), property.GetSlot(1), property.GetSlot(2), SyntaxFactory.List(lines)])!;
    }

    /// <summary>Gets the first token of an entry that has text or trivia, which is where its line starts.</summary>
    private static GreenToken? FirstToken(GreenNode entry)
    {
        for (var i = 0; i < entry.SlotCount; i++)
        {
            var slot = entry.GetSlot(i);
            var count = GreenNodeList.Count(slot);
            for (var j = 0; j < count; j++)
            {
                if (GreenNodeList.ElementAt(slot, j) is GreenToken { FullWidth: > 0 } token)
                    return token;
            }
        }

        return null;
    }

    /// <summary>Returns <paramref name="entry"/> with <paramref name="oldToken"/>, one of the tokens in its slots, replaced.</summary>
    private static GreenNode ReplaceToken(GreenNode entry, GreenToken oldToken, GreenToken newToken)
    {
        var slots = new GreenNode?[entry.SlotCount];
        for (var i = 0; i < slots.Length; i++)
        {
            var slot = entry.GetSlot(i);
            if (ReferenceEquals(slot, oldToken))
            {
                slot = newToken;
            }
            else if (slot is { IsList: true })
            {
                var items = GreenNodeList.ToArray(slot);
                var index = Array.IndexOf(items, oldToken);
                if (index >= 0)
                {
                    items[index] = newToken;
                    slot = SyntaxFactory.List(items);
                }
            }

            slots[i] = slot;
        }

        return entry.WithSlots(slots)!;
    }

    /// <summary>Gets the whitespace at the start of the line of a token whose leading trivia is <paramref name="trivia"/>.</summary>
    /// <remarks>A byte order mark does not indent a line, as the lexer does not count it.</remarks>
    private static string GetIndentation(GreenNode? trivia)
    {
        var indentation = "";
        var count = GreenNodeList.Count(trivia);
        for (var i = 0; i < count; i++)
        {
            var item = GreenNodeList.ElementAt(trivia, i);
            indentation = item?.RawKind == (int)SyntaxKind.WhitespaceTrivia ? indentation + item.ToFullString() : "";
        }

        return indentation.Replace("\ufeff", "", StringComparison.Ordinal);
    }

    /// <summary>Returns <paramref name="token"/> indented by <paramref name="indentation"/> on its line.</summary>
    private static GreenToken WithIndentation(GreenToken token, string indentation)
    {
        var trivia = GreenNodeList.ToArray(token.LeadingTrivia);
        var lineStart = 0;
        for (var i = 0; i < trivia.Length; i++)
        {
            if (trivia[i]?.RawKind is (int)SyntaxKind.EndOfLineTrivia or (int)SyntaxKind.CommentTrivia || trivia[i]?.ToFullString() is "\ufeff")
            {
                lineStart = i + 1;
            }
        }

        GreenNode?[] leading = indentation.Length == 0 ? trivia[..lineStart] : [.. trivia[..lineStart], SyntaxFactory.Trivia(SyntaxKind.WhitespaceTrivia, indentation)];
        return token.WithTrivia(SyntaxFactory.List(leading), token.TrailingTrivia);
    }

    private static bool EndsItsLine(GreenNode entry) => HasEndOfLine((entry.GetLastTerminal() as GreenToken)?.TrailingTrivia);

    private static bool HasEndOfLine(GreenNode? trivia)
    {
        var count = GreenNodeList.Count(trivia);
        for (var i = 0; i < count; i++)
        {
            if (GreenNodeList.ElementAt(trivia, i)?.RawKind == (int)SyntaxKind.EndOfLineTrivia)
                return true;
        }

        return false;
    }

    /// <summary>Determines whether a comment comes before the first line break of <paramref name="trivia"/>, so it would join the line before it.</summary>
    private static bool StartsWithComment(GreenNode? trivia)
    {
        var count = GreenNodeList.Count(trivia);
        for (var i = 0; i < count; i++)
        {
            switch ((SyntaxKind?)GreenNodeList.ElementAt(trivia, i)?.RawKind)
            {
                case SyntaxKind.EndOfLineTrivia:
                    return false;
                case SyntaxKind.CommentTrivia:
                    return true;
            }
        }

        return false;
    }

    private static GreenNode GetEndOfLine(ref GreenNode? endOfLine, GreenNode? entries)
        => endOfLine ??= FindEndOfLine(entries) ?? SyntaxFactory.Trivia(SyntaxKind.EndOfLineTrivia, "\n");

    private static GreenNode? FindEndOfLine(GreenNode? entries)
    {
        var count = GreenNodeList.Count(entries);
        for (var i = 0; i < count; i++)
        {
            var trailing = (GreenNodeList.ElementAt(entries, i)?.GetLastTerminal() as GreenToken)?.TrailingTrivia;
            var triviaCount = GreenNodeList.Count(trailing);
            for (var j = 0; j < triviaCount; j++)
            {
                var trivia = GreenNodeList.ElementAt(trailing, j);
                if (trivia?.RawKind == (int)SyntaxKind.EndOfLineTrivia)
                    return trivia;
            }
        }

        return null;
    }
}
