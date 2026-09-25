using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Language.Syntax;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

/// <summary>A whole TOML document: its entries and the end of the text.</summary>
internal sealed class TomlDocumentSyntax : TomlSyntaxNode
{
    private readonly GreenNode? _entries;
    private readonly GreenNode _endOfFileToken;

    /// <summary>Creates a document from entries that already end their lines, such as the ones the parser reads.</summary>
    public TomlDocumentSyntax(GreenNode? entries, GreenNode endOfFileToken)
        : this(entries, endOfFileToken, diagnostics: null, annotations: null)
    {
    }

    private TomlDocumentSyntax(GreenNode? entries, GreenNode endOfFileToken, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.TomlDocument, diagnostics, annotations)
    {
        SlotCount = 2;
        AdjustFlagsAndWidth(entries);
        _entries = entries;
        AdjustFlagsAndWidth(endOfFileToken);
        _endOfFileToken = endOfFileToken;
    }

    /// <summary>Creates a document from entries built or edited by hand, ending the line of each one that needs it.</summary>
    public static TomlDocumentSyntax Create(GreenNode? entries, GreenNode endOfFileToken) => new(EndLines(entries), endOfFileToken);

    internal override GreenNode? GetSlot(int index) => index switch
    {
        0 => _entries,
        1 => _endOfFileToken,
        _ => null,
    };

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new TomlDocumentSyntax(EndLines(slots[0]), RequiredSlot(slots[1]), GetDiagnostics(), GetAnnotations());
    internal override bool IsListSlot(int index) => index is 0;
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new TomlDocumentSyntax(_entries, _endOfFileToken, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new TomlDocumentSyntax(_entries, _endOfFileToken, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Toml.TomlDocumentSyntax(this, parent, position);

    /// <summary>Ends the lines that an edit would otherwise join, so that no edit can join two entries on one line.</summary>
    /// <remarks>
    /// <para>
    /// An entry followed by another one gets a line break after it, unless the next one starts on a line of its own.
    /// Only the skipped text the parser keeps after an entry on its line may follow it there, so an entry followed by
    /// skipped text is left as it is. A comment in front of an entry ends its line too, or it would hide the entry.
    /// </para>
    /// <para>
    /// Every document the parser reads meets this already, which keeps an edit from touching any line other than the
    /// ones it changes. The line break added is the first one of the document, or a line feed.
    /// </para>
    /// </remarks>
    internal static GreenNode? EndLines(GreenNode? entries)
    {
        var count = GreenNodeList.Count(entries);
        GreenNode?[]? result = null;
        GreenNode? endOfLine = null;
        for (var i = 0; i < count; i++)
        {
            var entry = GreenNodeList.ElementAt(entries, i);
            if (entry is null)
                continue;

            var edited = entry;
            if (GetOpenCommentIndex((entry.GetFirstTerminal() as GreenToken)?.LeadingTrivia) is var commentIndex and >= 0)
            {
                var lineBreak = endOfLine ??= FindEndOfLine(entries);
                edited = SyntaxNodeRemover.ReplaceEdgeToken(edited, first: true, token => token.WithTrivia(InsertAfter(token.LeadingTrivia, commentIndex, lineBreak), token.TrailingTrivia));
            }

            var next = i + 1 < count ? GreenNodeList.ElementAt(entries, i + 1) : null;
            if (next is not null && next.RawKind != (int)SyntaxKind.TomlSkippedText && !EndsItsLine(entry) && !StartsOnNewLine(next))
            {
                endOfLine ??= FindEndOfLine(entries);
                edited = SyntaxNodeRemover.AppendTrailingTrivia(edited, endOfLine);
            }

            if (!ReferenceEquals(edited, entry))
            {
                result ??= GreenNodeList.ToArray(entries);
                result[i] = edited;
            }
        }

        return result is null ? entries : SyntaxFactory.List(result);
    }

    private static bool EndsItsLine(GreenNode entry)
    {
        var trailing = (entry.GetLastTerminal() as GreenToken)?.TrailingTrivia;
        var count = GreenNodeList.Count(trailing);
        for (var i = 0; i < count; i++)
        {
            if (GreenNodeList.ElementAt(trailing, i)?.RawKind == (int)SyntaxKind.EndOfLineTrivia)
                return true;
        }

        return false;
    }

    /// <summary>Determines whether the trivia in front of <paramref name="entry"/> breaks the line before anything else can join it.</summary>
    private static bool StartsOnNewLine(GreenNode entry)
    {
        var leading = (entry.GetFirstTerminal() as GreenToken)?.LeadingTrivia;
        var count = GreenNodeList.Count(leading);
        for (var i = 0; i < count; i++)
        {
            switch ((SyntaxKind?)GreenNodeList.ElementAt(leading, i)?.RawKind)
            {
                case SyntaxKind.EndOfLineTrivia:
                    return true;
                case SyntaxKind.CommentTrivia:
                    return false;
            }
        }

        return false;
    }

    /// <summary>Gets the index of the last comment in <paramref name="leading"/> when no line break follows it, or -1.</summary>
    private static int GetOpenCommentIndex(GreenNode? leading)
    {
        var index = -1;
        var count = GreenNodeList.Count(leading);
        for (var i = 0; i < count; i++)
        {
            switch ((SyntaxKind?)GreenNodeList.ElementAt(leading, i)?.RawKind)
            {
                case SyntaxKind.CommentTrivia:
                    index = i;
                    break;
                case SyntaxKind.EndOfLineTrivia:
                    index = -1;
                    break;
            }
        }

        return index;
    }

    private static GreenNode? InsertAfter(GreenNode? list, int index, GreenNode item)
        => GreenNodeList.Insert(list, index + 1, [item]);

    private static GreenNode FindEndOfLine(GreenNode? entries)
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

        return SyntaxFactory.Trivia(SyntaxKind.EndOfLineTrivia, "\n");
    }
}
