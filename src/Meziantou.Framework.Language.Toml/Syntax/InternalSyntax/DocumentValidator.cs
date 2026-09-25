using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Language.Toml.Internals;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

/// <summary>Reports what the grammar cannot see: keys defined twice, tables defined twice, and values extended after the fact.</summary>
/// <remarks>
/// <para>
/// The entries are checked in document order against a model of the tables the document has built so far. Only the
/// shape is modelled -- which names are tables, arrays of tables, or values -- never the values themselves.
/// </para>
/// <para>
/// Whether a key is defined twice depends on the whole document, so the result is never stored in the nodes: a node
/// can be moved into another document, or the entry it clashed with removed, and the diagnostic would then be wrong.
/// The tree runs this over its whole root instead, and each diagnostic is at an absolute position.
/// </para>
/// <para>
/// The rules are those of Python's <c>tomllib</c>, which passes the whole <c>toml-test</c> suite. Two flags do most of
/// the work: a table is <em>explicit</em> once a header or a dotted key has defined it, after which no header can
/// define it again; and an inline table or an array is <em>frozen</em>, so nothing can be added to it, nor to anything
/// inside it. A dotted key marks the tables it goes through as explicit only once the next header is reached, which
/// is what lets <c>a.b = 1</c> and <c>a.c = 2</c> share <c>a</c> while stopping <c>[a]</c> from reopening it.
/// </para>
/// <para>
/// Every key is walked once, name by name, from the table of the section it is in: the cost of a key/value pair is
/// the length of its own key, whatever the depth of the header above it.
/// </para>
/// <para>
/// An entry whose key could not be read is not checked, and a table header that is itself in error stops the
/// key/value pairs under it from being checked, so one mistake is reported once rather than again on every line it
/// affects.
/// </para>
/// </remarks>
internal sealed class DocumentValidator
{
    private static readonly object Value = new();

    private readonly Table _root = new();
    private readonly FlagNode _flags = new();
    private readonly List<FlagNode> _pendingExplicit = [];
    private readonly List<SyntaxDiagnosticInfo> _diagnostics = [];

    /// <summary>The names of the header of the section being read.</summary>
    private string[] _header = [];

    /// <summary>The table the key/value pairs being read belong to, or <see langword="null"/> when their header is in error.</summary>
    private Table? _headerTable;

    /// <summary>The flags of <see cref="_headerTable"/>.</summary>
    private FlagNode _headerFlags;

    private DocumentValidator()
    {
        _headerTable = _root;
        _headerFlags = _flags;
    }

    /// <summary>Checks the entries of <paramref name="document"/>.</summary>
    /// <returns>What is wrong, at offsets from the start of the document.</returns>
    public static List<SyntaxDiagnosticInfo> Validate(GreenNode document)
    {
        var validator = new DocumentValidator();
        var entries = document.GetSlot(0);
        var count = GreenNodeList.Count(entries);
        for (var i = 0; i < count; i++)
        {
            var offset = GreenNodeList.OffsetAt(entries, i);
            switch (GreenNodeList.ElementAt(entries, i))
            {
                case TomlTableSyntax table:
                    validator.ValidateHeader(table, offset);
                    break;
                case TomlPropertySyntax property:
                    validator.ValidateProperty(property, offset);
                    break;
            }
        }

        return validator._diagnostics;
    }

    private void ValidateHeader(TomlTableSyntax table, int offset)
    {
        foreach (var flags in _pendingExplicit)
        {
            flags.Explicit = true;
        }

        _pendingExplicit.Clear();
        _headerTable = null;

        var keyNode = table.GetRequiredSlot(1);
        if (GetNames(keyNode) is not { } names)
            return;

        var keyOffset = offset + table.GetSlotOffset(1) + keyNode.GetLeadingTriviaWidth();
        void Report(DiagnosticDescriptor descriptor, IEnumerable<string> key)
            => Add(_diagnostics, keyOffset, keyNode.Width, descriptor, key);

        if (_flags.GetFrozenPrefixLength(names, includeLast: true) is var frozenLength and > 0)
        {
            Report(TomlDiagnosticDescriptors.ImmutableValue, names[..frozenLength]);
            return;
        }

        Table sectionTable;
        FlagNode sectionFlags;
        if (table.Kind == SyntaxKind.TomlArrayOfTables)
        {
            var parent = GetOrCreateTable(_root, names, names.Length - 1, accessArrays: true, out var failedAt);
            if (parent is null)
            {
                Report(TomlDiagnosticDescriptors.NotATable, names[..failedAt]);
                return;
            }

            ArrayOfTables array;
            if (!parent.Children.TryGetValue(names[^1], out var existing))
            {
                array = new ArrayOfTables();
                parent.Children.Add(names[^1], array);
            }
            else if (existing is ArrayOfTables existingArray)
            {
                array = existingArray;
                array.Last = new Table();
            }
            else
            {
                Report(existing is Table ? TomlDiagnosticDescriptors.DuplicateTable : TomlDiagnosticDescriptors.NotATable, names);
                return;
            }

            // Each [[a]] starts a new table, so what the previous one defined is free to be defined again. Only a
            // header that is accepted does that: a rejected one leaves the tables it names as they were.
            _flags.Remove(names);
            sectionFlags = _flags.GetOrAdd(names);
            sectionFlags.Explicit = true;
            sectionTable = array.Last;
        }
        else
        {
            sectionFlags = _flags.GetOrAdd(names);
            if (sectionFlags.Explicit)
            {
                Report(TomlDiagnosticDescriptors.DuplicateTable, names);
                return;
            }

            sectionFlags.Explicit = true;
            if (GetOrCreateTable(_root, names, names.Length, accessArrays: true, out var failedAt) is not { } created)
            {
                Report(TomlDiagnosticDescriptors.NotATable, names[..failedAt]);
                return;
            }

            sectionTable = created;
        }

        _header = names;
        _headerTable = sectionTable;
        _headerFlags = sectionFlags;
    }

    private void ValidateProperty(TomlPropertySyntax property, int offset)
    {
        var value = property.GetRequiredSlot(2);
        var valueOffset = offset + property.GetSlotOffset(2);
        var keyNode = property.GetRequiredSlot(0);
        if (_headerTable is not { } table || GetNames(keyNode) is not { } names)
        {
            // An inline table is a document of its own, so it can still be checked.
            ValidateValue(value, valueOffset, _diagnostics);
            return;
        }

        var keyOffset = offset + keyNode.GetLeadingTriviaWidth();
        void Report(DiagnosticDescriptor descriptor, int nameCount)
            => Add(_diagnostics, keyOffset, keyNode.Width, descriptor, [.. _header, .. names.AsSpan(0, nameCount)]);

        // The tables a dotted key goes through must not have been defined by a header, and become explicit at the
        // next one. The frozen check comes after that one, as in tomllib, so it is only remembered on the way.
        var flags = _headerFlags;
        var frozenLength = 0;
        for (var i = 0; i < names.Length - 1; i++)
        {
            flags = flags.GetOrAdd(names[i]);
            if (flags.Explicit)
            {
                Report(TomlDiagnosticDescriptors.TableDefinedByHeader, i + 1);
                ValidateValue(value, valueOffset, _diagnostics);
                return;
            }

            _pendingExplicit.Add(flags);
            if (frozenLength == 0 && flags.Frozen)
            {
                frozenLength = i + 1;
            }
        }

        if (frozenLength > 0)
        {
            Report(TomlDiagnosticDescriptors.ImmutableValue, frozenLength);
            ValidateValue(value, valueOffset, _diagnostics);
            return;
        }

        for (var i = 0; i < names.Length - 1; i++)
        {
            if (GetOrCreateChild(table, names[i], accessArrays: true) is not { } child)
            {
                Report(TomlDiagnosticDescriptors.NotATable, i + 1);
                ValidateValue(value, valueOffset, _diagnostics);
                return;
            }

            table = child;
        }

        if (!table.Children.TryAdd(names[^1], Value))
        {
            Report(TomlDiagnosticDescriptors.DuplicateKey, names.Length);
            ValidateValue(value, valueOffset, _diagnostics);
            return;
        }

        if (value is TomlInlineTableSyntax or TomlArraySyntax)
        {
            flags.GetOrAdd(names[^1]).Frozen = true;
        }

        ValidateValue(value, valueOffset, _diagnostics);
    }

    /// <summary>Checks the inline tables in <paramref name="value"/>, each of which is a document of its own.</summary>
    /// <remarks>
    /// Values are walked with a stack rather than by recursion, so a tree built by hand as deep as memory allows is
    /// checked rather than overflowing the stack.
    /// </remarks>
    private static void ValidateValue(GreenNode value, int offset, List<SyntaxDiagnosticInfo> diagnostics)
    {
        if (value is not (TomlArraySyntax or TomlInlineTableSyntax))
            return;

        var stack = new Stack<(GreenNode Value, int Offset)>();
        stack.Push((value, offset));
        while (stack.TryPop(out var current))
        {
            switch (current.Value)
            {
                case TomlArraySyntax array:
                    foreach (var (element, elementOffset) in GetListItems(array, current.Offset))
                    {
                        if (element is TomlArraySyntax or TomlInlineTableSyntax)
                        {
                            stack.Push((element, elementOffset));
                        }
                    }

                    break;

                case TomlInlineTableSyntax inlineTable:
                    ValidateInlineTable(inlineTable, current.Offset, diagnostics, stack);
                    break;
            }
        }
    }

    private static void ValidateInlineTable(TomlInlineTableSyntax inlineTable, int offset, List<SyntaxDiagnosticInfo> diagnostics, Stack<(GreenNode Value, int Offset)> values)
    {
        var root = new Table();
        var rootFlags = new FlagNode();
        foreach (var (item, itemOffset) in GetListItems(inlineTable, offset))
        {
            if (item is not TomlPropertySyntax property)
                continue;

            var memberValue = property.GetRequiredSlot(2);
            if (memberValue is TomlArraySyntax or TomlInlineTableSyntax)
            {
                values.Push((memberValue, itemOffset + property.GetSlotOffset(2)));
            }

            var keyNode = property.GetRequiredSlot(0);
            if (GetNames(keyNode) is not { } names)
                continue;

            var keyOffset = itemOffset + keyNode.GetLeadingTriviaWidth();
            if (rootFlags.GetFrozenPrefixLength(names, includeLast: true) is var frozenLength and > 0)
            {
                Add(diagnostics, keyOffset, keyNode.Width, TomlDiagnosticDescriptors.ImmutableValue, names[..frozenLength]);
                continue;
            }

            var parent = GetOrCreateTable(root, names, names.Length - 1, accessArrays: false, out var failedAt);
            if (parent is null)
            {
                Add(diagnostics, keyOffset, keyNode.Width, TomlDiagnosticDescriptors.NotATable, names[..failedAt]);
                continue;
            }

            if (!parent.Children.TryAdd(names[^1], Value))
            {
                Add(diagnostics, keyOffset, keyNode.Width, TomlDiagnosticDescriptors.DuplicateKey, names);
                continue;
            }

            if (memberValue is TomlInlineTableSyntax or TomlArraySyntax)
            {
                rootFlags.GetOrAdd(names).Frozen = true;
            }
        }
    }

    /// <summary>Gets the nodes of the list in slot 1 of an array or an inline table, with their offsets, skipping the commas.</summary>
    private static IEnumerable<(GreenNode Item, int Offset)> GetListItems(GreenNode container, int containerOffset)
    {
        var list = container.GetSlot(1);
        if (list is null)
            yield break;

        var offset = containerOffset + container.GetSlotOffset(1);
        var count = GreenNodeList.Count(list);
        for (var i = 0; i < count; i++)
        {
            var item = GreenNodeList.ElementAt(list, i);
            if (item is null)
                continue;

            if (!item.IsToken)
                yield return (item, offset);

            offset += item.FullWidth;
        }
    }

    /// <summary>Gets the names a key is made of, or <see langword="null"/> when a part of it could not be read.</summary>
    /// <remarks>
    /// Only a mistake in the name itself stops the key from being checked. One in the trivia around it, such as a bad
    /// character in the comment on the line above, or a feature of a newer TOML version, leaves the name readable.
    /// </remarks>
    private static string[]? GetNames(GreenNode key)
    {
        var tokens = key.GetSlot(0);
        if (tokens is null)
            return null;

        var names = new List<string>();
        var count = GreenNodeList.Count(tokens);
        for (var i = 0; i < count; i++)
        {
            if (GreenNodeList.ElementAt(tokens, i) is not GreenToken token || token.RawKind == (int)SyntaxKind.DotToken)
                continue;

            if (token.IsMissing || !SyntaxFacts.IsKeyToken((SyntaxKind)token.RawKind) || HasUnreadableName(token))
                return null;

            names.Add(token.ValueText);
        }

        return names.Count == 0 ? null : [.. names];

        static bool HasUnreadableName(GreenToken token)
        {
            foreach (var diagnostic in token.GetDiagnostics())
            {
                if (diagnostic.Descriptor == TomlDiagnosticDescriptors.UnterminatedString
                    || diagnostic.Descriptor == TomlDiagnosticDescriptors.InvalidEscapeSequence
                    || diagnostic.Descriptor == TomlDiagnosticDescriptors.InvalidUnicodeEscapeSequence)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Walks down to the table <paramref name="key"/> names, creating the ones that do not exist yet.</summary>
    /// <param name="root">The table to start from.</param>
    /// <param name="key">The names to follow.</param>
    /// <param name="count">How many of the names to follow.</param>
    /// <param name="accessArrays">Whether an array of tables is gone through by its last table, as a header does.</param>
    /// <param name="failedAt">How many names lead to the value that is not a table, when there is one.</param>
    /// <returns>The table, or <see langword="null"/> when a name on the way is not a table.</returns>
    private static Table? GetOrCreateTable(Table root, string[] key, int count, bool accessArrays, out int failedAt)
    {
        var current = root;
        for (var i = 0; i < count; i++)
        {
            if (GetOrCreateChild(current, key[i], accessArrays) is not { } child)
            {
                failedAt = i + 1;
                return null;
            }

            current = child;
        }

        failedAt = 0;
        return current;
    }

    /// <summary>Gets the table <paramref name="name"/> names in <paramref name="parent"/>, creating it when it does not exist yet.</summary>
    /// <returns>The table, or <see langword="null"/> when the name is something else.</returns>
    private static Table? GetOrCreateChild(Table parent, string name, bool accessArrays)
    {
        if (!parent.Children.TryGetValue(name, out var child))
        {
            child = new Table();
            parent.Children.Add(name, child);
        }

        if (accessArrays && child is ArrayOfTables array)
        {
            child = array.Last;
        }

        return child as Table;
    }

    private static void Add(List<SyntaxDiagnosticInfo> diagnostics, int offset, int width, DiagnosticDescriptor descriptor, IEnumerable<string> key)
        => diagnostics.Add(new SyntaxDiagnosticInfo(offset, width, descriptor, [TomlFormatting.FormatKey(key)]));

    private sealed class Table
    {
        public Dictionary<string, object> Children { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>An array of tables. Only its last table can still be added to.</summary>
    private sealed class ArrayOfTables
    {
        public Table Last { get; set; } = new();
    }

    /// <summary>The flags set on a name, and the names under it.</summary>
    /// <remarks>Frozen applies to everything under the name as well: nothing can be added inside an inline table or an array.</remarks>
    private sealed class FlagNode
    {
        private Dictionary<string, FlagNode>? _children;

        public bool Explicit { get; set; }

        public bool Frozen { get; set; }

        public FlagNode GetOrAdd(string name)
        {
            _children ??= new(StringComparer.Ordinal);
            if (!_children.TryGetValue(name, out var child))
            {
                child = new FlagNode();
                _children.Add(name, child);
            }

            return child;
        }

        public FlagNode GetOrAdd(string[] key)
        {
            var node = this;
            foreach (var name in key)
            {
                node = node.GetOrAdd(name);
            }

            return node;
        }

        /// <summary>Gets how many names of <paramref name="key"/> lead to the first frozen one, or 0 when none is.</summary>
        /// <param name="key">The names to follow.</param>
        /// <param name="includeLast">Whether the last name counts, or only the ones it is under.</param>
        public int GetFrozenPrefixLength(string[] key, bool includeLast)
        {
            var node = this;
            var count = includeLast ? key.Length : key.Length - 1;
            for (var i = 0; i < count; i++)
            {
                if (node._children is null || !node._children.TryGetValue(key[i], out node))
                    return 0;

                if (node.Frozen)
                    return i + 1;
            }

            return 0;
        }

        /// <summary>Removes the flags of <paramref name="key"/> and of everything under it.</summary>
        public void Remove(string[] key)
        {
            var node = this;
            for (var i = 0; i < key.Length - 1; i++)
            {
                if (node._children is null || !node._children.TryGetValue(key[i], out node))
                    return;
            }

            node._children?.Remove(key[^1]);
        }
    }
}
