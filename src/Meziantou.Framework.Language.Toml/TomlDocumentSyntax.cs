using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml;

/// <summary>A whole TOML document.</summary>
/// <remarks>
/// The entries are flat, as they are in the text: a table header is followed by the key/value pairs under it, not
/// their parent. <see cref="RootProperties"/> and <see cref="TomlTableSyntax.Properties"/> group them.
/// </remarks>
public sealed class TomlDocumentSyntax : TomlSyntaxNode
{
    private SyntaxNode? _entries;

    internal TomlDocumentSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the table headers, key/value pairs, and skipped lines, in source order.</summary>
    public SyntaxList<TomlEntrySyntax> Entries => new(GetRedAtZero(ref _entries));

    public SyntaxToken EndOfFileToken => new(this, Green.GetSlot(1), GetChildPosition(1), GetChildIndex(1));

    /// <summary>Gets the key/value pairs of the root table: the ones before the first table header.</summary>
    public IReadOnlyList<TomlPropertySyntax> RootProperties => GetProperties(Entries, 0);

    /// <summary>Gets the table and array-of-tables headers, in source order.</summary>
    public IEnumerable<TomlTableSyntax> Tables => Entries.OfType<TomlTableSyntax>();

    /// <summary>Returns this document with the given parts, or itself when nothing changed.</summary>
    public TomlDocumentSyntax Update(SyntaxList<TomlEntrySyntax> entries, SyntaxToken endOfFileToken)
    {
        if (entries.Green == Green.GetSlot(0) && endOfFileToken.Node == Green.GetSlot(1))
            return this;

        return SyntaxFactory.TomlDocument(entries, endOfFileToken).WithAnnotationsFrom(this);
    }

    public TomlDocumentSyntax WithEntries(SyntaxList<TomlEntrySyntax> entries) => Update(entries, EndOfFileToken);
    public TomlDocumentSyntax WithEndOfFileToken(SyntaxToken endOfFileToken) => Update(Entries, endOfFileToken);

    /// <summary>Returns this document with <paramref name="items"/> added at the end.</summary>
    /// <remarks>
    /// <para>
    /// Every entry has to end its line, so a line feed is added after the last entry and after each of
    /// <paramref name="items"/> that does not already end with a line break.
    /// </para>
    /// <para>
    /// Comments after the last entry belong to the end of the document, so the new entries go before them. In a
    /// document that has only comments, such as a header, they go after them instead.
    /// </para>
    /// </remarks>
    public TomlDocumentSyntax AddEntries(params TomlEntrySyntax[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Length == 0)
            return this;

        var entries = Entries;
        var endOfFileToken = EndOfFileToken;
        var added = items.Select(SyntaxFactory.EndLine).ToArray();
        if (entries.Count > 0)
        {
            var last = entries[entries.Count - 1];
            entries = entries.Replace(last, SyntaxFactory.EndLine(last));
        }
        else if (endOfFileToken.LeadingTrivia.Any(trivia => trivia.IsKind(SyntaxKind.CommentTrivia)))
        {
            // The comments move to the first new entry, and end their line if the text they came from did not.
            SyntaxTrivia[] comments = endOfFileToken.LeadingTrivia.Last().IsKind(SyntaxKind.EndOfLineTrivia) ? [.. endOfFileToken.LeadingTrivia] : [.. endOfFileToken.LeadingTrivia, SyntaxFactory.LineFeed];
            added[0] = added[0].WithLeadingTrivia([.. comments, .. added[0].GetLeadingTrivia()]);
            endOfFileToken = endOfFileToken.WithLeadingTrivia(default(SyntaxTriviaList));
        }

        return Update(entries.AddRange(added), endOfFileToken);
    }

    internal static IReadOnlyList<TomlPropertySyntax> GetProperties(SyntaxList<TomlEntrySyntax> entries, int start)
    {
        var result = new List<TomlPropertySyntax>();
        for (var i = start; i < entries.Count; i++)
        {
            switch (entries[i])
            {
                case TomlTableSyntax:
                    return result;
                case TomlPropertySyntax property:
                    result.Add(property);
                    break;
            }
        }

        return result;
    }

    internal override SyntaxNode? GetNodeSlot(int index) => index == 0 ? GetRedAtZero(ref _entries) : null;
    internal override SyntaxNode? GetCachedSlot(int index) => index == 0 ? _entries : null;

    public override void Accept(TomlSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitTomlDocument(this);
    }

    public override TResult? Accept<TResult>(TomlSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitTomlDocument(this);
    }
}
