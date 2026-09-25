using Meziantou.Framework.Language.InternalSyntax;
using Green = Meziantou.Framework.Language.Ini.Syntax.InternalSyntax;

namespace Meziantou.Framework.Language.Ini;

/// <summary>A whole INI document.</summary>
/// <remarks>
/// <para>
/// The entries are flat, as they are in the text: a section header is followed by the properties under it, not their
/// parent. <see cref="GlobalProperties"/> and <see cref="IniSectionSyntax.Properties"/> group them,
/// <see cref="GetValue(string?, string, StringComparer?)"/> looks one up, and
/// <see cref="SetValue(string?, string, string, StringComparer?)"/>, <see cref="RemoveProperties"/>, and
/// <see cref="RemoveSections"/> edit them.
/// </para>
/// <para>
/// Every entry ends its line. However a document is edited, an entry that another one follows is given a line break when
/// it has none, so two entries never run into each other.
/// </para>
/// </remarks>
public sealed class IniDocumentSyntax : IniSyntaxNode
{
    private SyntaxNode? _entries;
    private SectionIndex? _index;

    internal IniDocumentSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the section, property, and skipped-text entries in source order.</summary>
    public SyntaxList<IniEntrySyntax> Entries => new(GetRedAtZero(ref _entries));

    public SyntaxToken EndOfFileToken => new(this, Green.GetSlot(1), GetChildPosition(1), GetChildIndex(1));

    /// <summary>Gets the options this document is read with.</summary>
    /// <remarks>
    /// They are the options it was parsed with, or the ones given to <see cref="IniSyntaxTree.Create(IniDocumentSyntax, IniParseOptions?, string?)"/>,
    /// and every document an edit makes from this one keeps them. A document built with <see cref="SyntaxFactory"/> has
    /// <see cref="IniParseOptions.Default"/>. They decide how <see cref="IniPropertySyntax.WithValue(string)"/> writes a
    /// value, and how names are compared when no comparer is given.
    /// </remarks>
    public IniParseOptions Options => ((Green.IniDocumentSyntax)Green).Options;

    /// <summary>Gets the properties that come before the first section header.</summary>
    public IReadOnlyList<IniPropertySyntax> GlobalProperties => Index.GlobalProperties;

    /// <summary>Gets the section headers, in source order.</summary>
    public IEnumerable<IniSectionSyntax> Sections => Index.Sections;

    private SectionIndex Index
    {
        get
        {
            var index = Volatile.Read(ref _index);
            if (index is null)
            {
                index = new SectionIndex(Entries);
                index = Interlocked.CompareExchange(ref _index, index, comparand: null) ?? index;
            }

            return index;
        }
    }

    /// <summary>Gets the section headers named <paramref name="name"/>, in source order.</summary>
    /// <param name="name">The name of the sections.</param>
    /// <param name="comparer">How names are compared, or <see langword="null"/> for the <see cref="IniParseOptions.NameComparer"/> of <see cref="Options"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public IEnumerable<IniSectionSyntax> GetSections(string name, StringComparer? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(name);

        comparer ??= Options.NameComparer;
        return Index.Sections.Where(section => !section.NameToken.IsMissing && comparer.Equals(section.Name, name));
    }

    /// <summary>Gets the properties with key <paramref name="key"/> in every section named <paramref name="section"/>, in source order.</summary>
    /// <param name="section">The name of the section, or <see langword="null"/> for the properties before the first section header.</param>
    /// <param name="key">The key of the properties.</param>
    /// <param name="comparer">How section names and keys are compared, or <see langword="null"/> for the <see cref="IniParseOptions.NameComparer"/> of <see cref="Options"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public IEnumerable<IniPropertySyntax> GetProperties(string? section, string key, StringComparer? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(key);

        comparer ??= Options.NameComparer;
        var properties = section is null ? GlobalProperties : GetSections(section, comparer).SelectMany(header => header.Properties);
        return properties.Where(property => !property.KeyToken.IsMissing && comparer.Equals(property.Key, key));
    }

    /// <summary>Gets the value of the property with key <paramref name="key"/> in the section named <paramref name="section"/>.</summary>
    /// <remarks>
    /// Sections that share a name are read as one section. When the key is there more than once, the last one wins, as it
    /// does in most readers of INI files.
    /// </remarks>
    /// <param name="section">The name of the section, or <see langword="null"/> for the properties before the first section header.</param>
    /// <param name="key">The key of the property.</param>
    /// <param name="comparer">How section names and keys are compared, or <see langword="null"/> for the <see cref="IniParseOptions.NameComparer"/> of <see cref="Options"/>.</param>
    /// <returns>The value, or <see langword="null"/> when there is no such property.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public string? GetValue(string? section, string key, StringComparer? comparer = null) => GetProperties(section, key, comparer).LastOrDefault()?.Value;

    /// <summary>Returns this document with the property <paramref name="key"/> of <paramref name="section"/> set to <paramref name="value"/>.</summary>
    /// <remarks>
    /// <para>
    /// When the property exists, the one <see cref="GetValue(string?, string, StringComparer?)"/> reads gets the new value, as
    /// <see cref="IniPropertySyntax.WithValue(string)"/> writes it. Otherwise it is added after the last entry of the last
    /// section named <paramref name="section"/>, or of the properties before the first section header, written like the
    /// property before it: same indentation, same separator, same spacing. When there is no such section, it is added at
    /// the end of the document under a new header, after a blank line.
    /// </para>
    /// <para>The value is written the way <see cref="Options"/> read it.</para>
    /// </remarks>
    /// <param name="section">The name of the section, or <see langword="null"/> for the properties before the first section header.</param>
    /// <param name="key">The key of the property.</param>
    /// <param name="value">The value.</param>
    /// <param name="comparer">How section names and keys are compared, or <see langword="null"/> for the <see cref="IniParseOptions.NameComparer"/> of <see cref="Options"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="key"/>, <paramref name="section"/>, or <paramref name="value"/> cannot be written; see
    /// <see cref="SyntaxFactory.Key(string)"/>, <see cref="SyntaxFactory.SectionName(string)"/>, and <see cref="IniPropertySyntax.WithValue(string)"/>.
    /// </exception>
    public IniDocumentSyntax SetValue(string? section, string key, string value, StringComparer? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(value);

        var keyToken = SyntaxFactory.Key(key);
        var existing = GetProperties(section, key, comparer).LastOrDefault();
        if (existing is not null)
            return this.ReplaceNode(existing, existing.WithValue(value));

        var index = Index;
        var entries = Entries;
        int insertAt;
        IniPropertySyntax? sibling;
        var indentation = "";
        if (section is null)
        {
            insertAt = index.Sections.Count > 0 ? index.EntryIndexes[index.Sections[0]] : entries.Count;
            sibling = LastOrNull(index.GlobalProperties);
        }
        else
        {
            var header = GetSections(section, comparer).LastOrDefault();
            if (header is null)
            {
                var newHeader = SyntaxFactory.IniSection(section);
                if (entries.Count > 0)
                {
                    newHeader = newHeader.WithLeadingTrivia(SyntaxFactory.GetEndOfLine(this));
                }

                return AddEntries(newHeader, CreateProperty(keyToken, value, sibling: null, indentation));
            }

            var headerOrdinal = index.SectionOrdinals[header];
            insertAt = headerOrdinal + 1 < index.Sections.Count ? index.EntryIndexes[index.Sections[headerOrdinal + 1]] : entries.Count;
            sibling = LastOrNull(index.Properties[header]);
            indentation = IniPropertySyntax.GetIndentation(header.OpenBracketToken.LeadingTrivia);
        }

        if (sibling is not null)
        {
            indentation = IniPropertySyntax.GetIndentation(sibling.KeyToken.IsMissing ? sibling.SeparatorToken.LeadingTrivia : sibling.KeyToken.LeadingTrivia);
        }

        if (insertAt == entries.Count)
            return AddEntries(CreateProperty(keyToken, value, sibling, indentation));

        // A line indented more than the key before it would continue its value, so the new key is indented at least as
        // much as the line it goes in front of.
        var next = entries[insertAt];
        var nextIndentation = IniPropertySyntax.GetIndentation(next.GetLeadingTrivia());
        if (Options.AllowMultilineValues && nextIndentation.Length > indentation.Length)
        {
            indentation = nextIndentation;
        }

        var property = CreateProperty(keyToken, value, sibling, indentation);
        if (insertAt == 0)
        {
            // What heads the document stays at its head.
            var (head, body) = SplitHead(next.GetLeadingTrivia());
            entries = entries.Replace(next, next.WithLeadingTrivia(body));
            property = property.WithLeadingTrivia([.. head, .. property.GetLeadingTrivia()]);
        }

        return WithEntries(entries.Insert(insertAt, property));
    }

    /// <summary>Returns this document without the properties with key <paramref name="key"/> in the sections named <paramref name="section"/>.</summary>
    /// <remarks>
    /// Every one of them is removed, with the comment lines in front of it and the comment after it, so that
    /// <see cref="GetValue(string?, string, StringComparer?)"/> finds none. What heads the document, up to its last blank
    /// line, stays.
    /// </remarks>
    /// <param name="section">The name of the section, or <see langword="null"/> for the properties before the first section header.</param>
    /// <param name="key">The key of the properties.</param>
    /// <param name="comparer">How section names and keys are compared, or <see langword="null"/> for the <see cref="IniParseOptions.NameComparer"/> of <see cref="Options"/>.</param>
    /// <returns>The document without the properties, or this document when there are none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public IniDocumentSyntax RemoveProperties(string? section, string key, StringComparer? comparer = null)
    {
        var index = Index;
        var removed = GetProperties(section, key, comparer).Select(property => index.EntryIndexes[property]).ToHashSet();

        return RemoveEntries(removed);
    }

    /// <summary>Returns this document without the sections named <paramref name="name"/> and the entries under them.</summary>
    /// <remarks>
    /// Each header is removed with the comment lines in front of it and every entry up to the next header. What heads the
    /// document, up to its last blank line, stays.
    /// </remarks>
    /// <param name="name">The name of the sections.</param>
    /// <param name="comparer">How names are compared, or <see langword="null"/> for the <see cref="IniParseOptions.NameComparer"/> of <see cref="Options"/>.</param>
    /// <returns>The document without the sections, or this document when there are none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public IniDocumentSyntax RemoveSections(string name, StringComparer? comparer = null)
    {
        var index = Index;
        var count = Entries.Count;
        var removed = new HashSet<int>();
        foreach (var header in GetSections(name, comparer))
        {
            var ordinal = index.SectionOrdinals[header];
            var end = ordinal + 1 < index.Sections.Count ? index.EntryIndexes[index.Sections[ordinal + 1]] : count;
            for (var i = index.EntryIndexes[header]; i < end; i++)
            {
                removed.Add(i);
            }
        }

        return RemoveEntries(removed);
    }

    /// <summary>Returns this document with the given parts, or itself when nothing changed.</summary>
    /// <remarks>The new document keeps <see cref="Options"/>.</remarks>
    public IniDocumentSyntax Update(SyntaxList<IniEntrySyntax> entries, SyntaxToken endOfFileToken)
    {
        if (entries.Green == Green.GetSlot(0) && endOfFileToken.Node == Green.GetSlot(1))
            return this;

        return SyntaxFactory.IniDocument(entries, endOfFileToken, Options).WithAnnotationsFrom(this);
    }

    public IniDocumentSyntax WithEntries(SyntaxList<IniEntrySyntax> entries) => Update(entries, EndOfFileToken);
    public IniDocumentSyntax WithEndOfFileToken(SyntaxToken endOfFileToken) => Update(Entries, endOfFileToken);

    /// <summary>Returns this document with <paramref name="items"/> added at the end.</summary>
    /// <remarks>
    /// Every entry has to end its line, so a line break is added after the last entry and after each of
    /// <paramref name="items"/> that does not already end with one. The line breaks of <paramref name="items"/> are those
    /// of the document: its first line break, or a line feed when it has none. In a document holding nothing but trivia,
    /// such as a header comment, <paramref name="items"/> come after it.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is <see langword="null"/>.</exception>
    public IniDocumentSyntax AddEntries(params IniEntrySyntax[] items)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Length == 0)
            return this;

        var endOfLine = SyntaxFactory.GetEndOfLine(this);
        var added = items.Select(item => SyntaxFactory.EndLine(item, endOfLine, replaceLineBreaks: true)).ToArray();
        var entries = Entries;
        var endOfFile = EndOfFileToken;
        if (entries.Count > 0)
        {
            var last = entries[entries.Count - 1];
            entries = entries.Replace(last, SyntaxFactory.EndLine(last, endOfLine, replaceLineBreaks: false));
        }
        else
        {
            // Everything but the whitespace after the last line break stays in front of the new entries.
            var trivia = endOfFile.LeadingTrivia;
            var split = 0;
            for (var i = 0; i < trivia.Count; i++)
            {
                if (!trivia[i].IsKind(SyntaxKind.WhitespaceTrivia) || IsByteOrderMark(trivia[i]))
                {
                    split = i + 1;
                }
            }

            if (split > 0)
            {
                List<SyntaxTrivia> head = [.. trivia.Take(split)];
                if (head[^1].IsKind(SyntaxKind.CommentTrivia))
                {
                    head.Add(endOfLine);
                }

                added[0] = added[0].WithLeadingTrivia([.. head, .. added[0].GetLeadingTrivia()]);
                endOfFile = endOfFile.WithLeadingTrivia(trivia.Skip(split));
            }
        }

        return Update(entries.AddRange(added), endOfFile);
    }

    internal IReadOnlyList<IniPropertySyntax> GetSectionProperties(IniSectionSyntax section)
        => Index.Properties.TryGetValue(section, out var properties) ? properties : [];

    internal override SyntaxNode? GetNodeSlot(int index) => index == 0 ? GetRedAtZero(ref _entries) : null;
    internal override SyntaxNode? GetCachedSlot(int index) => index == 0 ? _entries : null;

    public override void Accept(IniSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitIniDocument(this);
    }

    public override TResult? Accept<TResult>(IniSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitIniDocument(this);
    }

    /// <summary>Builds a property the way <paramref name="sibling"/> is written, or as <c>key=value</c> when there is none.</summary>
    private IniPropertySyntax CreateProperty(SyntaxToken keyToken, string value, IniPropertySyntax? sibling, string indentation)
    {
        var endOfLine = SyntaxFactory.GetEndOfLine(this);
        var (valueToken, continuations) = SyntaxFactory.ValueLines(value, Options, indentation + "    ", endOfLine, nameof(value));
        var separator = SyntaxFactory.Token(SyntaxKind.EqualsToken);
        if (sibling is not null && !sibling.SeparatorToken.IsMissing)
        {
            separator = SyntaxFactory.Token(sibling.SeparatorToken.Kind());
            keyToken = keyToken.WithTrailingTrivia(WhitespaceOnly(sibling.KeyToken.TrailingTrivia));
            valueToken = valueToken.WithLeadingTrivia(WhitespaceOnly(sibling.ValueToken.LeadingTrivia));
        }

        if (indentation.Length > 0)
        {
            keyToken = keyToken.WithLeadingTrivia(SyntaxFactory.Whitespace(indentation));
        }

        return SyntaxFactory.IniProperty(keyToken, separator, valueToken, SyntaxFactory.TokenList(continuations));

        static SyntaxTriviaList WhitespaceOnly(SyntaxTriviaList trivia)
            => trivia.All(item => item.IsKind(SyntaxKind.WhitespaceTrivia)) ? trivia : SyntaxFactory.TriviaList();
    }

    /// <summary>Returns this document without the entries at <paramref name="removed"/>, and the skipped text after them on their lines.</summary>
    private IniDocumentSyntax RemoveEntries(HashSet<int> removed)
    {
        if (removed.Count == 0)
            return this;

        var entries = Entries;
        var kept = new List<IniEntrySyntax>(entries.Count);
        SyntaxTriviaList? head = null;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (removed.Contains(i) || (entry is IniSkippedTextSyntax && removed.Contains(i - 1) && removed.Add(i)))
            {
                if (i == 0)
                {
                    head = SplitHead(entry.GetLeadingTrivia()).Head;
                }

                continue;
            }

            if (head is { } carried)
            {
                entry = entry.WithLeadingTrivia([.. carried, .. SkipBlankLines(entry.GetLeadingTrivia(), carried)]);
                head = null;
            }

            kept.Add(entry);
        }

        var endOfFile = EndOfFileToken;
        if (head is { } rest)
        {
            endOfFile = endOfFile.WithLeadingTrivia([.. rest, .. SkipBlankLines(endOfFile.LeadingTrivia, rest)]);
        }

        return Update(SyntaxFactory.List(kept), endOfFile);

        // The head of the document ends with a blank line when it has one, so the blank lines after it would double it.
        static IEnumerable<SyntaxTrivia> SkipBlankLines(SyntaxTriviaList trivia, SyntaxTriviaList head)
        {
            if (head.Count == 0 || !head[head.Count - 1].IsKind(SyntaxKind.EndOfLineTrivia))
                return trivia;

            var start = 0;
            for (var i = 0; i < trivia.Count; i++)
            {
                if (trivia[i].IsKind(SyntaxKind.EndOfLineTrivia))
                {
                    start = i + 1;
                }
                else if (!trivia[i].IsKind(SyntaxKind.WhitespaceTrivia))
                {
                    break;
                }
            }

            return trivia.Skip(start);
        }
    }

    /// <summary>
    /// Splits the trivia in front of the first entry into what heads the document, up to and including its last blank line,
    /// and what describes the entry.
    /// </summary>
    /// <remarks>A byte order mark always heads the document.</remarks>
    private static (SyntaxTriviaList Head, SyntaxTriviaList Body) SplitHead(SyntaxTriviaList trivia)
    {
        var split = trivia.Count > 0 && IsByteOrderMark(trivia[0]) ? 1 : 0;
        var lineHasComment = false;
        for (var i = 0; i < trivia.Count; i++)
        {
            if (trivia[i].IsKind(SyntaxKind.CommentTrivia))
            {
                lineHasComment = true;
            }
            else if (trivia[i].IsKind(SyntaxKind.EndOfLineTrivia))
            {
                if (!lineHasComment)
                {
                    split = i + 1;
                }

                lineHasComment = false;
            }
        }

        return (SyntaxFactory.TriviaList(trivia.Take(split)), SyntaxFactory.TriviaList(trivia.Skip(split)));
    }

    private static IniPropertySyntax? LastOrNull(IReadOnlyList<IniPropertySyntax> properties) => properties.Count > 0 ? properties[^1] : null;

    private static bool IsByteOrderMark(SyntaxTrivia trivia) => trivia.IsKind(SyntaxKind.WhitespaceTrivia) && trivia.ToFullString() is "﻿";

    /// <summary>Where each section header and property sits, worked out once per document.</summary>
    private sealed class SectionIndex
    {
        public SectionIndex(SyntaxList<IniEntrySyntax> entries)
        {
            List<IniPropertySyntax>? current = [];
            GlobalProperties = current;
            for (var i = 0; i < entries.Count; i++)
            {
                switch (entries[i])
                {
                    case IniSectionSyntax section:
                        current = [];
                        SectionOrdinals.Add(section, Sections.Count);
                        Sections.Add(section);
                        Properties.Add(section, current);
                        EntryIndexes.Add(section, i);
                        break;

                    case IniPropertySyntax property:
                        current.Add(property);
                        EntryIndexes.Add(property, i);
                        break;
                }
            }
        }

        public IReadOnlyList<IniPropertySyntax> GlobalProperties { get; }
        public List<IniSectionSyntax> Sections { get; } = [];
        public Dictionary<IniSectionSyntax, int> SectionOrdinals { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<IniSectionSyntax, IReadOnlyList<IniPropertySyntax>> Properties { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<IniEntrySyntax, int> EntryIndexes { get; } = new(ReferenceEqualityComparer.Instance);
    }
}
