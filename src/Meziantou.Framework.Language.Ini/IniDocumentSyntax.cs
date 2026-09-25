using Meziantou.Framework.Language.InternalSyntax;
using Green = Meziantou.Framework.Language.Ini.Syntax.InternalSyntax;

namespace Meziantou.Framework.Language.Ini;

/// <summary>A whole INI document.</summary>
/// <remarks>
/// The entries are flat, as they are in the text: a section header is followed by the properties under it, not their
/// parent. <see cref="GlobalProperties"/> and <see cref="IniSectionSyntax.Properties"/> group them, and
/// <see cref="GetValue(string?, string, StringComparer?)"/> looks one up.
/// </remarks>
public sealed class IniDocumentSyntax : IniSyntaxNode
{
    private SyntaxNode? _entries;

    internal IniDocumentSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the section, property, and skipped-text entries in source order.</summary>
    public SyntaxList<IniEntrySyntax> Entries => new(GetRedAtZero(ref _entries));

    public SyntaxToken EndOfFileToken => new(this, Green.GetSlot(1), GetChildPosition(1), GetChildIndex(1));

    /// <summary>Gets the properties that come before the first section header.</summary>
    public IReadOnlyList<IniPropertySyntax> GlobalProperties => GetProperties(Entries, 0);

    /// <summary>Gets the section headers, in source order.</summary>
    public IEnumerable<IniSectionSyntax> Sections => Entries.OfType<IniSectionSyntax>();

    /// <summary>Gets the section headers named <paramref name="name"/>, in source order.</summary>
    /// <param name="name">The name of the sections.</param>
    /// <param name="comparer">How names are compared, or <see langword="null"/> for <see cref="StringComparer.OrdinalIgnoreCase"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public IEnumerable<IniSectionSyntax> GetSections(string name, StringComparer? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(name);

        comparer ??= StringComparer.OrdinalIgnoreCase;
        return Sections.Where(section => !section.NameToken.IsMissing && comparer.Equals(section.Name, name));
    }

    /// <summary>Gets the properties with key <paramref name="key"/> in every section named <paramref name="section"/>, in source order.</summary>
    /// <param name="section">The name of the section, or <see langword="null"/> for the properties before the first section header.</param>
    /// <param name="key">The key of the properties.</param>
    /// <param name="comparer">How section names and keys are compared, or <see langword="null"/> for <see cref="StringComparer.OrdinalIgnoreCase"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public IEnumerable<IniPropertySyntax> GetProperties(string? section, string key, StringComparer? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(key);

        comparer ??= StringComparer.OrdinalIgnoreCase;
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
    /// <param name="comparer">How section names and keys are compared, or <see langword="null"/> for <see cref="StringComparer.OrdinalIgnoreCase"/>.</param>
    /// <returns>The value, or <see langword="null"/> when there is no such property.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public string? GetValue(string? section, string key, StringComparer? comparer = null) => GetProperties(section, key, comparer).LastOrDefault()?.Value;

    /// <summary>Returns this document with the given parts, or itself when nothing changed.</summary>
    public IniDocumentSyntax Update(SyntaxList<IniEntrySyntax> entries, SyntaxToken endOfFileToken)
    {
        if (entries.Green == Green.GetSlot(0) && endOfFileToken.Node == Green.GetSlot(1))
            return this;

        return SyntaxFactory.IniDocument(entries, endOfFileToken).WithAnnotationsFrom(this);
    }

    public IniDocumentSyntax WithEntries(SyntaxList<IniEntrySyntax> entries) => Update(entries, EndOfFileToken);
    public IniDocumentSyntax WithEndOfFileToken(SyntaxToken endOfFileToken) => Update(Entries, endOfFileToken);

    /// <summary>Returns this document with <paramref name="items"/> added at the end.</summary>
    /// <remarks>
    /// Every entry has to end its line, so a line break is added after the last entry and after each of
    /// <paramref name="items"/> that does not already end with one. It is the first line break of the document, or a
    /// line feed when it has none.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is <see langword="null"/>.</exception>
    public IniDocumentSyntax AddEntries(params IniEntrySyntax[] items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var endOfLine = DescendantTrivia().FirstOrDefault(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));
        if (endOfLine.RawKind == 0)
        {
            endOfLine = SyntaxFactory.LineFeed;
        }

        var entries = Entries;
        if (entries.Count > 0 && items.Length > 0)
        {
            var last = entries[entries.Count - 1];
            entries = entries.Replace(last, SyntaxFactory.EndLine(last, endOfLine));
        }

        return WithEntries(entries.AddRange(items.Select(item => SyntaxFactory.EndLine(item, endOfLine))));
    }

    internal static IReadOnlyList<IniPropertySyntax> GetProperties(SyntaxList<IniEntrySyntax> entries, int start)
    {
        var result = new List<IniPropertySyntax>();
        for (var i = start; i < entries.Count; i++)
        {
            switch (entries[i])
            {
                case IniSectionSyntax:
                    return result;
                case IniPropertySyntax property:
                    result.Add(property);
                    break;
            }
        }

        return result;
    }

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
}
