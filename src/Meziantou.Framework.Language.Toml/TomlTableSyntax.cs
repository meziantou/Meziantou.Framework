using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml;

/// <summary>A table header such as <c>[database]</c>, or an array-of-tables header such as <c>[[products]]</c>.</summary>
/// <remarks>
/// A header is an entry of the document like any other: the key/value pairs under it are the entries that follow it,
/// up to the next header. <see cref="Properties"/> gathers them.
/// </remarks>
public sealed class TomlTableSyntax : TomlEntrySyntax
{
    private TomlKeySyntax? _key;

    internal TomlTableSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets <c>[</c>, or <c>[[</c> for an array of tables.</summary>
    public SyntaxToken OpenBracketToken => new(this, Green.GetSlot(0), Position, GetChildIndex(0));

    /// <summary>Gets the name of the table.</summary>
    public TomlKeySyntax Key => GetRed(ref _key, 1)!;

    /// <summary>Gets <c>]</c>, or <c>]]</c> for an array of tables.</summary>
    public SyntaxToken CloseBracketToken => new(this, Green.GetSlot(2), GetChildPosition(2), GetChildIndex(2));

    /// <summary>Gets a value indicating whether this is an array-of-tables header such as <c>[[products]]</c>.</summary>
    public bool IsArrayOfTables => Kind() == SyntaxKind.TomlArrayOfTables;

    /// <summary>Gets the key/value pairs under this header: the ones that follow it, up to the next header.</summary>
    /// <remarks>A header that is not part of a document has none.</remarks>
    public IReadOnlyList<TomlPropertySyntax> Properties
    {
        get
        {
            if (Parent is not TomlDocumentSyntax document)
                return [];

            var entries = document.Entries;
            var index = IndexOf(entries, this);
            return index < 0 ? [] : TomlDocumentSyntax.GetProperties(entries, index + 1);
        }
    }

    /// <summary>Finds <paramref name="table"/> among <paramref name="entries"/> by its position.</summary>
    /// <remarks>
    /// The entries are in source order, so a binary search finds it without going through the ones before it, which
    /// keeps reading the properties of every table of a document linear rather than quadratic.
    /// </remarks>
    private static int IndexOf(SyntaxList<TomlEntrySyntax> entries, TomlTableSyntax table)
    {
        var low = 0;
        var high = entries.Count;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (entries[middle].Position < table.Position)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        // An entry can be empty, and share its position with the ones around it.
        for (var i = low; i < entries.Count && entries[i].Position == table.Position; i++)
        {
            if (ReferenceEquals(entries[i], table))
                return i;
        }

        return -1;
    }

    /// <summary>Returns this header with the given parts, or itself when nothing changed.</summary>
    /// <remarks>Whether it is an array-of-tables header follows <paramref name="openBracketToken"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public TomlTableSyntax Update(SyntaxToken openBracketToken, TomlKeySyntax key, SyntaxToken closeBracketToken)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (openBracketToken.Node == Green.GetSlot(0) && ReferenceEquals(key.Green, Green.GetSlot(1)) && closeBracketToken.Node == Green.GetSlot(2))
            return this;

        return SyntaxFactory.TomlTable(openBracketToken, key, closeBracketToken).WithAnnotationsFrom(this);
    }

    public TomlTableSyntax WithOpenBracketToken(SyntaxToken openBracketToken) => Update(openBracketToken, Key, CloseBracketToken);
    public TomlTableSyntax WithKey(TomlKeySyntax key) => Update(OpenBracketToken, key, CloseBracketToken);
    public TomlTableSyntax WithCloseBracketToken(SyntaxToken closeBracketToken) => Update(OpenBracketToken, Key, closeBracketToken);

    internal override SyntaxNode? GetNodeSlot(int index) => index == 1 ? GetRed(ref _key, 1) : null;
    internal override SyntaxNode? GetCachedSlot(int index) => index == 1 ? _key : null;

    public override void Accept(TomlSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitTomlTable(this);
    }

    public override TResult? Accept<TResult>(TomlSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitTomlTable(this);
    }
}
