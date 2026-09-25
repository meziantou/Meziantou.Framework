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

    /// <summary>Gets every key/value pair of the document, in source order, with the full key each one defines.</summary>
    /// <remarks>
    /// <para>
    /// A key/value pair whose value is an inline table comes first, followed by the pairs of that inline table, each
    /// with the full key it defines. The values in an array are not visited, as they have no key of their own.
    /// </para>
    /// <para>
    /// The pairs are read as they are written, whatever the diagnostics of the document: a key defined twice comes
    /// twice.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// foreach (var pair in document.GetKeyValues())
    /// {
    ///     if (pair.Names is ["dependencies", var name] &amp;&amp; pair.Value is TomlStringSyntax version)
    ///         Console.WriteLine($"{name} {version.Value}");
    /// }
    /// </code>
    /// </example>
    public IEnumerable<TomlKeyValue> GetKeyValues()
    {
        TomlTableSyntax? table = null;
        IReadOnlyList<string> headerNames = [];
        IReadOnlyList<SyntaxToken> headerParts = [];
        var pending = new Stack<(TomlPropertySyntax Property, IReadOnlyList<string> Names, IReadOnlyList<SyntaxToken> Parts)>();
        foreach (var entry in Entries)
        {
            switch (entry)
            {
                case TomlTableSyntax header:
                    table = header;
                    headerNames = header.Key.Names;
                    headerParts = header.Key.Parts;
                    break;

                case TomlPropertySyntax property:
                    // The pairs of inline tables are walked with a stack rather than by recursion, so a document built by
                    // hand as deep as memory allows does not overflow the stack.
                    pending.Push((property, headerNames, headerParts));
                    while (pending.TryPop(out var item))
                    {
                        var names = new TomlKeyValue.Concatenation<string>(item.Names, item.Property.Key.Names);
                        var parts = new TomlKeyValue.Concatenation<SyntaxToken>(item.Parts, item.Property.Key.Parts);
                        yield return new TomlKeyValue(table, names, parts, item.Property);

                        if (item.Property.Value is TomlInlineTableSyntax inlineTable)
                        {
                            var properties = inlineTable.Properties;
                            for (var i = properties.Count - 1; i >= 0; i--)
                            {
                                pending.Push((properties[i], names, parts));
                            }
                        }
                    }

                    break;
            }
        }
    }

    /// <summary>Gets the value of the key/value pair that defines the full key <paramref name="names"/>.</summary>
    /// <remarks>
    /// The key is looked for wherever it can be written: under a header, as a dotted key, or in an inline table, so
    /// <c>GetValue("server", "port")</c> finds the port in <c>[server]</c>, in <c>server.port = 80</c>, and in
    /// <c>server = { port = 80 }</c>. Names are compared as they are, case included, as TOML does. A table a header
    /// defines is not a value. When the key is defined more than once, as it is in each table of an array of tables,
    /// the first one is returned.
    /// </remarks>
    /// <param name="names">The name of each part of the key, unquoted.</param>
    /// <returns>The value, or <see langword="null"/> when no key/value pair defines that key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="names"/> or one of its items is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="names"/> is empty.</exception>
    public TomlValueSyntax? GetValue(params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (names.Length == 0)
            throw new ArgumentException("A key has at least one part.", nameof(names));

        if (Array.Exists(names, name => name is null))
            throw new ArgumentNullException(nameof(names), "A key cannot have a null part.");

        return GetKeyValues().FirstOrDefault(pair => pair.Names.SequenceEqual(names, StringComparer.Ordinal))?.Value;
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
