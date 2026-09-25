using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Language.Toml.Internals;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

/// <summary>Reports what the grammar cannot see: keys defined twice, tables defined twice, and values extended after the fact.</summary>
/// <remarks>
/// <para>
/// The entries are checked in document order, each as soon as it is parsed, against a model of the tables the
/// document has built so far. Only the shape is modelled -- which names are tables, arrays of tables, or values --
/// never the values themselves.
/// </para>
/// <para>
/// The rules are those of Python's <c>tomllib</c>, which passes the whole <c>toml-test</c> suite. Two flags do most of
/// the work: a table is <em>explicit</em> once a header or a dotted key has defined it, after which no header can
/// define it again; and an inline table or an array is <em>frozen</em>, so nothing can be added to it, nor to anything
/// inside it. A dotted key marks the tables it goes through as explicit only once the next header is reached, which
/// is what lets <c>a.b = 1</c> and <c>a.c = 2</c> share <c>a</c> while stopping <c>[a]</c> from reopening it.
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
    private readonly List<string[]> _pendingExplicit = [];
    private readonly List<SyntaxDiagnosticInfo> _diagnostics = [];

    /// <summary>The key of the table the key/value pairs being read belong to, or <see langword="null"/> when its header is in error.</summary>
    private string[]? _header = [];

    /// <summary>Checks <paramref name="entry"/>, and returns it with whatever is wrong with it attached.</summary>
    public GreenNode Validate(GreenNode entry)
    {
        var diagnostics = _diagnostics;
        diagnostics.Clear();
        switch (entry)
        {
            case TomlTableSyntax table:
                ValidateHeader(table, diagnostics);
                break;
            case TomlPropertySyntax property when _header is not null:
                ValidateProperty(property, diagnostics);
                break;
        }

        return diagnostics.Count == 0 ? entry : entry.WithAdditionalDiagnostics([.. diagnostics]);
    }

    /// <summary>Checks the inline tables of a value parsed on its own, and returns it with whatever is wrong with them attached.</summary>
    public static GreenNode ValidateStandaloneValue(GreenNode value)
    {
        var diagnostics = new List<SyntaxDiagnosticInfo>();
        ValidateValue(value, offset: 0, diagnostics);

        return diagnostics.Count == 0 ? value : value.WithAdditionalDiagnostics([.. diagnostics]);
    }

    private void ValidateHeader(TomlTableSyntax table, List<SyntaxDiagnosticInfo> diagnostics)
    {
        foreach (var key in _pendingExplicit)
        {
            _flags.Set(key, FlagKind.Explicit, recursive: false);
        }

        _pendingExplicit.Clear();
        _header = null;

        var keyNode = table.GetRequiredSlot(1);
        if (GetNames(keyNode) is not { } names)
            return;

        var keyOffset = table.GetSlotOffset(1) + keyNode.GetLeadingTriviaWidth();
        void Report(DiagnosticDescriptor descriptor, IEnumerable<string> key)
            => Add(diagnostics, keyOffset, keyNode.Width, descriptor, key);

        if (_flags.Is(names, FlagKind.Frozen))
        {
            Report(TomlDiagnosticDescriptors.ImmutableValue, FrozenPrefix(_flags, names));
            return;
        }

        if (table.Kind == SyntaxKind.TomlArrayOfTables)
        {
            // Each [[a]] starts a new table, so what the previous one defined is free to be defined again.
            _flags.Remove(names);
            _flags.Set(names, FlagKind.Explicit, recursive: false);

            var parent = GetOrCreateTable(_root, names, names.Length - 1, accessArrays: true, out var failedAt);
            if (parent is null)
            {
                Report(TomlDiagnosticDescriptors.NotATable, names[..failedAt]);
                return;
            }

            if (!parent.Children.TryGetValue(names[^1], out var existing))
            {
                parent.Children.Add(names[^1], new ArrayOfTables());
            }
            else if (existing is ArrayOfTables array)
            {
                array.Last = new Table();
            }
            else
            {
                Report(existing is Table ? TomlDiagnosticDescriptors.DuplicateTable : TomlDiagnosticDescriptors.NotATable, names);
                return;
            }
        }
        else
        {
            if (_flags.Is(names, FlagKind.Explicit))
            {
                Report(TomlDiagnosticDescriptors.DuplicateTable, names);
                return;
            }

            _flags.Set(names, FlagKind.Explicit, recursive: false);
            if (GetOrCreateTable(_root, names, names.Length, accessArrays: true, out var failedAt) is null)
            {
                Report(TomlDiagnosticDescriptors.NotATable, names[..failedAt]);
                return;
            }
        }

        _header = names;
    }

    private void ValidateProperty(TomlPropertySyntax property, List<SyntaxDiagnosticInfo> diagnostics)
    {
        var keyNode = property.GetRequiredSlot(0);
        if (GetNames(keyNode) is not { } names)
            return;

        var keyOffset = keyNode.GetLeadingTriviaWidth();
        void Report(DiagnosticDescriptor descriptor, IEnumerable<string> key)
            => Add(diagnostics, keyOffset, keyNode.Width, descriptor, key);

        var header = _header!;
        for (var i = 1; i < names.Length; i++)
        {
            string[] table = [.. header, .. names[..i]];
            if (_flags.Is(table, FlagKind.Explicit))
            {
                Report(TomlDiagnosticDescriptors.TableDefinedByHeader, table);
                return;
            }

            _pendingExplicit.Add(table);
        }

        string[] fullKey = [.. header, .. names];
        if (_flags.Is(fullKey[..^1], FlagKind.Frozen))
        {
            Report(TomlDiagnosticDescriptors.ImmutableValue, FrozenPrefix(_flags, fullKey[..^1]));
            return;
        }

        var parent = GetOrCreateTable(_root, fullKey, fullKey.Length - 1, accessArrays: true, out var failedAt);
        if (parent is null)
        {
            Report(TomlDiagnosticDescriptors.NotATable, fullKey[..failedAt]);
            return;
        }

        if (!parent.Children.TryAdd(fullKey[^1], Value))
        {
            Report(TomlDiagnosticDescriptors.DuplicateKey, fullKey);
            return;
        }

        var value = property.GetRequiredSlot(2);
        if (value is TomlInlineTableSyntax or TomlArraySyntax)
        {
            _flags.Set(fullKey, FlagKind.Frozen, recursive: true);
        }

        ValidateValue(value, property.GetSlotOffset(2), diagnostics);
    }

    /// <summary>Checks the inline tables in <paramref name="value"/>, each of which is a document of its own.</summary>
    private static void ValidateValue(GreenNode value, int offset, List<SyntaxDiagnosticInfo> diagnostics)
    {
        switch (value)
        {
            case TomlArraySyntax array:
                foreach (var (element, elementOffset) in GetListItems(array, offset))
                {
                    ValidateValue(element, elementOffset, diagnostics);
                }

                break;

            case TomlInlineTableSyntax inlineTable:
                var root = new Table();
                var flags = new FlagNode();
                foreach (var (item, itemOffset) in GetListItems(inlineTable, offset))
                {
                    if (item is not TomlPropertySyntax property)
                        continue;

                    var memberValue = property.GetRequiredSlot(2);
                    ValidateValue(memberValue, itemOffset + property.GetSlotOffset(2), diagnostics);

                    var keyNode = property.GetRequiredSlot(0);
                    if (GetNames(keyNode) is not { } names)
                        continue;

                    var keyOffset = itemOffset + keyNode.GetLeadingTriviaWidth();
                    if (flags.Is(names, FlagKind.Frozen))
                    {
                        Add(diagnostics, keyOffset, keyNode.Width, TomlDiagnosticDescriptors.ImmutableValue, FrozenPrefix(flags, names));
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
                        flags.Set(names, FlagKind.Frozen, recursive: true);
                    }
                }

                break;
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

            if (token.IsMissing || !SyntaxFacts.IsKeyToken((SyntaxKind)token.RawKind) || token.ContainsDiagnostics)
                return null;

            names.Add(token.ValueText);
        }

        return names.Count == 0 ? null : [.. names];
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
            if (!current.Children.TryGetValue(key[i], out var child))
            {
                child = new Table();
                current.Children.Add(key[i], child);
            }

            if (accessArrays && child is ArrayOfTables array)
            {
                child = array.Last;
            }

            if (child is not Table table)
            {
                failedAt = i + 1;
                return null;
            }

            current = table;
        }

        failedAt = 0;
        return current;
    }

    /// <summary>Gets the shortest part of <paramref name="key"/> that is frozen, which is the inline table or array to name.</summary>
    private static string[] FrozenPrefix(FlagNode flags, string[] key)
    {
        for (var i = 1; i < key.Length; i++)
        {
            if (flags.Is(key[..i], FlagKind.Frozen))
                return key[..i];
        }

        return key;
    }

    private static void Add(List<SyntaxDiagnosticInfo> diagnostics, int offset, int width, DiagnosticDescriptor descriptor, IEnumerable<string> key)
        => diagnostics.Add(new SyntaxDiagnosticInfo(offset, width, descriptor, [TomlFormatting.FormatKey(key)]));

    private enum FlagKind
    {
        Explicit,
        Frozen,
    }

    private sealed class Table
    {
        public Dictionary<string, object> Children { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>An array of tables. Only its last table can still be added to.</summary>
    private sealed class ArrayOfTables
    {
        public Table Last { get; set; } = new();
    }

    /// <summary>The flags set on a name, and on the names under it.</summary>
    private sealed class FlagNode
    {
        private Dictionary<string, FlagNode>? _children;
        private bool _explicit;
        private bool _frozen;
        private bool _frozenRecursive;

        public void Set(string[] key, FlagKind flag, bool recursive)
        {
            var node = this;
            foreach (var name in key)
            {
                node._children ??= new(StringComparer.Ordinal);
                if (!node._children.TryGetValue(name, out var child))
                {
                    child = new FlagNode();
                    node._children.Add(name, child);
                }

                node = child;
            }

            switch (flag)
            {
                case FlagKind.Explicit:
                    node._explicit = true;
                    break;
                case FlagKind.Frozen when recursive:
                    node._frozenRecursive = true;
                    break;
                case FlagKind.Frozen:
                    node._frozen = true;
                    break;
            }
        }

        /// <summary>Determines whether <paramref name="key"/> has <paramref name="flag"/>, directly or through a name above it.</summary>
        public bool Is(string[] key, FlagKind flag)
        {
            if (key.Length == 0)
                return false;

            var node = this;
            for (var i = 0; i < key.Length; i++)
            {
                if (node._children is null || !node._children.TryGetValue(key[i], out var child))
                    return false;

                if (i < key.Length - 1)
                {
                    if (flag == FlagKind.Frozen && child._frozenRecursive)
                        return true;
                }
                else
                {
                    return flag == FlagKind.Explicit ? child._explicit : child._frozen || child._frozenRecursive;
                }

                node = child;
            }

            return false;
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
