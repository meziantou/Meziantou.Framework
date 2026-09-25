using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Language.Syntax;
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
        => new(EndLines(entries), endOfFileToken, options);

    public IniDocumentSyntax WithOptions(IniParseOptions options)
        => ReferenceEquals(options, Options) ? this : new IniDocumentSyntax(_entries, _endOfFileToken, options, GetDiagnostics(), GetAnnotations());

    internal override GreenNode? GetSlot(int index) => index switch
    {
        0 => _entries,
        1 => _endOfFileToken,
        _ => null,
    };

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new IniDocumentSyntax(EndLines(slots[0]), RequiredSlot(slots[1]), Options, GetDiagnostics(), GetAnnotations());
    internal override bool IsListSlot(int index) => index is 0;
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new IniDocumentSyntax(_entries, _endOfFileToken, Options, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new IniDocumentSyntax(_entries, _endOfFileToken, Options, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Ini.IniDocumentSyntax(this, parent, position);

    /// <summary>Ends the line of every entry that is followed by another one, so that no edit can join two lines.</summary>
    /// <remarks>
    /// An entry is followed on its line by nothing but the skipped text the parser keeps there, so an entry followed by
    /// skipped text is left as it is. Every entry the parser reads meets this already, which keeps an edit from touching
    /// any line other than the ones it changes. The line break added is the first one of the document, or a line feed.
    /// </remarks>
    internal static GreenNode? EndLines(GreenNode? entries)
    {
        var count = GreenNodeList.Count(entries);
        GreenNode?[]? result = null;
        GreenNode? endOfLine = null;
        for (var i = 0; i < count - 1; i++)
        {
            var entry = GreenNodeList.ElementAt(entries, i);
            var next = GreenNodeList.ElementAt(entries, i + 1);
            if (entry is null || next is null || next.RawKind == (int)SyntaxKind.IniSkippedText || EndsItsLine(entry) || StartsOnNewLine(next))
                continue;

            endOfLine ??= FindEndOfLine(entries) ?? SyntaxFactory.Trivia(SyntaxKind.EndOfLineTrivia, "\n");
            result ??= GreenNodeList.ToArray(entries);
            result[i] = SyntaxNodeRemover.AppendTrailingTrivia(entry, endOfLine);
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
