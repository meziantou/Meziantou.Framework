using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Syntax;

internal class SyntaxValidator : SyntaxVisitor
{
    private readonly DiagnosticsBag _diagnostics;
    private ObjectPath _currentPath;
    private readonly Dictionary<ObjectPath, ObjectPathValue> _maps;
    private int _currentArrayIndex;

    public SyntaxValidator(DiagnosticsBag diagnostics)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _currentPath = new ObjectPath();
        _maps = new Dictionary<ObjectPath, ObjectPathValue>();
    }

    public override void Visit(KeyValueSyntax keyValue)
    {
        var savedPath = _currentPath.Clone();
        if (keyValue.Key == null)
        {
            _diagnostics.Error(keyValue.Span, "A KeyValueSyntax must have a non null Key");
            // returns immediately
            return;
        }

        if (!KeyNameToObjectPath(keyValue.Key, ObjectKind.Table, true))
        {
            return;
        }

        ObjectKind kind;
        switch (keyValue.Value)
        {
            case ArraySyntax _:
                kind = ObjectKind.Array;
                break;
            case BooleanValueSyntax _:
                kind = ObjectKind.Boolean;
                break;
            case DateTimeValueSyntax time:
                switch (time.Kind)
                {
                    case SyntaxKind.OffsetDateTimeByZ:
                        kind = ObjectKind.OffsetDateTimeByZ;
                        break;
                    case SyntaxKind.OffsetDateTimeByNumber:
                        kind = ObjectKind.OffsetDateTimeByNumber;
                        break;
                    case SyntaxKind.LocalDateTime:
                        kind = ObjectKind.LocalDateTime;
                        break;
                    case SyntaxKind.LocalDate:
                        kind = ObjectKind.LocalDate;
                        break;
                    case SyntaxKind.LocalTime:
                        kind = ObjectKind.LocalTime;
                        break;
                    default:
                        _diagnostics.Error(keyValue.Span, $"Unsupported datetime kind `{time.Kind}` for the key-value `{keyValue}`");
                        return;
                }
                break;
            case FloatValueSyntax _:
                kind = ObjectKind.Float;
                break;
            case InlineTableSyntax _:
                kind = ObjectKind.InlineTable;
                break;
            case IntegerValueSyntax _:
                kind = ObjectKind.Integer;
                break;
            case StringValueSyntax _:
                kind = ObjectKind.String;
                break;
            default:
                _diagnostics.Error(keyValue.Span, keyValue.Value == null ? $"A KeyValueSyntax must have a non null Value" : $"Not supported type `{keyValue.Value.Kind}` for the value of a KeyValueSyntax");
                return;
        }
        AddObjectPath(keyValue, kind, false, true);

        base.Visit(keyValue);
        _currentPath = savedPath;
    }

    public override void Visit(StringValueSyntax stringValue)
    {
        if (stringValue.Token == null)
        {
            _diagnostics.Error(stringValue.Span, $"A StringValueSyntax must have a non null Token");
        }
        base.Visit(stringValue);
    }

    public override void Visit(IntegerValueSyntax integerValue)
    {
        if (integerValue.Token == null)
        {
            _diagnostics.Error(integerValue.Span, $"A IntegerValueSyntax must have a non null Token");
        }
        base.Visit(integerValue);
    }

    public override void Visit(BooleanValueSyntax boolValue)
    {
        if (boolValue.Token == null)
        {
            _diagnostics.Error(boolValue.Span, $"A BooleanValueSyntax must have a non null Token");
        }
        base.Visit(boolValue);
    }

    public override void Visit(FloatValueSyntax floatValue)
    {
        if (floatValue.Token == null)
        {
            _diagnostics.Error(floatValue.Span, $"A FloatValueSyntax must have a non null Token");
        }
        base.Visit(floatValue);
    }

    public override void Visit(TableSyntax table)
    {
        VerifyTable(table);
        var savedPath = _currentPath.Clone();
        if (table.Name == null || !KeyNameToObjectPath(table.Name, ObjectKind.Table))
        {
            return;
        }

        AddObjectPath(table, ObjectKind.Table, false, false);

        base.Visit(table);

        _currentPath = savedPath;
    }

    public override void Visit(TableArraySyntax table)
    {
        VerifyTable(table);
        var savedPath = _currentPath.Clone();
        if (table.Name == null || !KeyNameToObjectPath(table.Name, ObjectKind.TableArray))
        {
            return;
        }
        var currentArrayTable = AddObjectPath(table, ObjectKind.TableArray, false, false);

        var savedIndex = _currentArrayIndex;
        _currentArrayIndex = currentArrayTable.ArrayIndex;

        base.Visit(table);

        _currentArrayIndex = savedIndex;
        _currentPath = savedPath;
    }

    public override void Visit(BareKeySyntax bareKey)
    {
        if (bareKey.Key == null)
        {
            _diagnostics.Error(bareKey.Span, $"A BareKeySyntax must have a non null property Key");
        }
        base.Visit(bareKey);
    }

    public override void Visit(KeySyntax key)
    {
        if (key.Key == null)
        {
            _diagnostics.Error(key.Span, $"A KeySyntax must have a non null property Key");
        }
        base.Visit(key);
    }

    public override void Visit(DateTimeValueSyntax dateTime)
    {
        if (dateTime.Token == null)
        {
            _diagnostics.Error(dateTime.Span, $"A DateTimeValueSyntax must have a non null Token");
        }
        base.Visit(dateTime);
    }

    private void VerifyTable(TableSyntaxBase table)
    {
        var isTableArray = table is TableArraySyntax;
        if (table.OpenBracket == null)
        {
            _diagnostics.Error(table.Span, $"The table{(isTableArray? " array" : string.Empty)} must have an {table.OpenTokenKind} `{table.OpenTokenKind.ToText()}`");
        }
        if (table.CloseBracket == null)
        {
            _diagnostics.Error(table.Span, $"The table{(isTableArray ? " array" : string.Empty)} must have an {table.CloseTokenKind} `{table.CloseTokenKind.ToText()}`");
        }
        if (table.EndOfLineToken == null && table.Items.ChildrenCount > 0)
        {
            _diagnostics.Error(table.Span, $"The table{(isTableArray ? " array" : string.Empty)} must have a EndOfLine set after the open/closing brackets and before any elements");
        }
        if (table.Name == null)
        {
            _diagnostics.Error(table.Span, $"The table{(isTableArray ? " array" : string.Empty)} must have a name");
        }
    }

    private bool KeyNameToObjectPath(KeySyntax key, ObjectKind kind, bool fromDottedKeys = false)
    {
        if (key.Key == null)
        {
            _diagnostics.Error(key.Span, $"The property KeySyntax.Key cannot be null");
        }

        var name = SyntaxValidator.GetStringFromBasic(key.Key!);
        if (name is null) return false;

        _currentPath.Add(name!);

        var items = key.DotKeysIfCreated;
        for (int i = 0; i < (items?.ChildrenCount ?? 0); i++)
        {
            AddObjectPath(key, kind, true, fromDottedKeys);
            var dotItem = SyntaxValidator.GetStringFromBasic(items!.GetChild(i)!.Key!)!;
            if (dotItem is null) return false;
            _currentPath.Add(dotItem);
        }

        return true;
    }

    private ObjectPathValue AddObjectPath(SyntaxNode node, ObjectKind kind, bool isImplicit, bool fromDottedKeys)
    {
        var currentPath = _currentPath.Clone();

        // array-implicit.toml
        if (kind == ObjectKind.TableArray && isImplicit)
        {
            kind = ObjectKind.Table;
        }

        if (_maps.TryGetValue(currentPath, out var existingValue))
        {
            // The following tests are the trickiest to get right with the spec, as the behavior
            // of TOML Table/TableArray with implicit/non implicit and dotted keys is quite complicated.

            // Cover the various valid cases for TOML Table:
            //   Previous.Implicit | Previous.FromDottedKeys | New.Implicit | New.FromDottedKeys
            //           true                 false
            //           true                 true              (true|false)         true
            //                                                      true             false
            bool ifNotTableOrExplicitImplicitMatches = kind != ObjectKind.Table || existingValue.IsImplicit && (!existingValue.FromDottedKeys || fromDottedKeys && existingValue.FromDottedKeys) || (isImplicit && !fromDottedKeys);

            // TableArray + TableArray => valid
            // TableArray + (Table && Implicit) => valid
            // Table + Table ok => valid
            bool areTypeMatching = existingValue.Kind == ObjectKind.TableArray && (kind == ObjectKind.TableArray || (kind == ObjectKind.Table && isImplicit)) ||
                                   (existingValue.Kind == ObjectKind.Table && kind == ObjectKind.Table);

            if (!(ifNotTableOrExplicitImplicitMatches && areTypeMatching))
            {
                _diagnostics.Error(node.Span, $"The key `{currentPath}` is already defined at {existingValue.Node.Span.Start} with `{existingValue.Node.ToString().TrimEnd('\r', '\n').ToPrintableString()}` and cannot be redefined");
            }
            else if (existingValue.Kind == ObjectKind.TableArray)
            {
                // [[a]] starts a new element; [a.b] and [[a.b]] extend the last one
                if (kind == ObjectKind.TableArray)
                {
                    existingValue.ArrayIndex++;
                }

                _currentPath.Add(existingValue.ArrayIndex);
            }
            else if (existingValue.IsImplicit && !isImplicit)
            {
                // Upgrade an implicit table created by dotted keys to an explicit table so further redefinitions are rejected.
                var upgraded = new ObjectPathValue(node, existingValue.Kind, isImplicit: false, existingValue.FromDottedKeys)
                {
                    ArrayIndex = existingValue.ArrayIndex,
                };
                _maps[currentPath] = upgraded;
                existingValue = upgraded;
            }
            else if (existingValue.IsImplicit && fromDottedKeys && !existingValue.FromDottedKeys)
            {
                // Dotted keys define the table created by a previous header, so a table header can no longer define it
                var upgraded = new ObjectPathValue(existingValue.Node, existingValue.Kind, isImplicit: true, fromDottedKeys: true)
                {
                    ArrayIndex = existingValue.ArrayIndex,
                };
                _maps[currentPath] = upgraded;
                existingValue = upgraded;
            }
        }
        else
        {
            existingValue = new ObjectPathValue(node, kind, isImplicit, fromDottedKeys);
            _maps.Add(currentPath, existingValue);
            if (kind == ObjectKind.TableArray)
            {
                _currentPath.Add(existingValue.ArrayIndex);
            }
        }
        return existingValue;
    }

    private static string? GetStringFromBasic(BareKeyOrStringValueSyntax value)
    {
        string? result;
        if (value is BareKeySyntax basicKey)
        {
            result = basicKey.Key?.Text;
        }
        else
        {
            result = ((StringValueSyntax) value).Value;
        }
        return result;
    }

    public override void Visit(ArraySyntax array)
    {
        var savedIndex = _currentArrayIndex;

        if (array.OpenBracket == null)
        {
            _diagnostics.Error(array.Span, $"The array must have an OpenBracket `[`");
        }
        else if (array.CloseBracket == null)
        {
            _diagnostics.Error(array.Span, $"The array must have an CloseBracket `[`");
        }

        var items = array.Items;
        for(int i = 0; i < items.ChildrenCount; i++)
        {
            var item = items.GetChild(i)!;
            if (i + 1 < items.ChildrenCount && item.Comma == null)
            {
                _diagnostics.Error(item.Span, $"The array item [{i}] must have a comma `,`");
            }
        }
        base.Visit(array);

        _currentArrayIndex = savedIndex;
    }

    public override void Visit(InlineTableItemSyntax inlineTableItem)
    {
        base.Visit(inlineTableItem);
    }

    public override void Visit(ArrayItemSyntax arrayItem)
    {
        // The index is part of the path of this item only, not of the next ones
        _currentPath.Add(_currentArrayIndex);

        if (arrayItem.Value == null)
        {
            _diagnostics.Error(arrayItem.Span, $"The array item [{_currentArrayIndex}] must have a non null value");
        }
        base.Visit(arrayItem);
        _currentPath.RemoveAt(_currentPath.Count - 1);
        _currentArrayIndex++;
    }

    public override void Visit(DottedKeyItemSyntax dottedKeyItem)
    {
        base.Visit(dottedKeyItem);
    }

    public override void Visit(InlineTableSyntax inlineTable)
    {
        base.Visit(inlineTable);
    }

    private class ObjectPath : List<ObjectPathItem>
    {
        private int _hashCode;

        public void Add(string key)
        {
            _hashCode = (_hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(key);
            base.Add(new ObjectPathItem(key));
        }

        public void Add(int index)
        {
            _hashCode = (_hashCode * 397) ^ index;
            base.Add(new ObjectPathItem(index));
        }

        // A deep copy: the paths stored in the map must not share the items of the current path
        public ObjectPath Clone()
        {
            var clone = new ObjectPath();
            foreach (var item in this)
            {
                ((List<ObjectPathItem>)clone).Add(item);
            }

            clone._hashCode = _hashCode;
            return clone;
        }

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            var other = obj as ObjectPath;
            if (other?.Count != Count) return false;
            if (other._hashCode != _hashCode) return false;
            for (int i = 0; i < Count; i++)
            {
                if (this[i] != other[i]) return false;
            }
            return true;
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }

        public override string ToString()
        {
            var buffer = new StringBuilder();
            for (int i = 0; i < Count; i++)
            {
                if (i > 0) buffer.Append('.');
                buffer.Append(this[i]);
            }
            return buffer.ToString();
        }
    }

    [DebuggerDisplay("{Node} - {Kind}")]
    private class ObjectPathValue
    {

        public ObjectPathValue(SyntaxNode node, ObjectKind kind, bool isImplicit, bool fromDottedKeys)
        {
            FromDottedKeys = fromDottedKeys;
            Node = node;
            Kind = kind;
            IsImplicit = isImplicit;
        }


        public readonly SyntaxNode Node;

        public readonly ObjectKind Kind;

        public readonly bool IsImplicit;

        public readonly bool FromDottedKeys;

        public int ArrayIndex;
    }

    private readonly struct ObjectPathItem : IEquatable<ObjectPathItem>
    {
        public ObjectPathItem(string key) : this()
        {
            Key = key;
        }

        public ObjectPathItem(int index) : this()
        {
            Index = index;
        }


        public readonly string? Key;

        public readonly int Index;

        public bool Equals(ObjectPathItem other)
        {
            return string.Equals(Key, other.Key, StringComparison.Ordinal) && Index == other.Index;
        }

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is ObjectPathItem other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Key != null ? StringComparer.Ordinal.GetHashCode(Key) : 0) * 397) ^ Index;
            }
        }

        public static bool operator ==(ObjectPathItem left, ObjectPathItem right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ObjectPathItem left, ObjectPathItem right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return Key ?? $"[{Index}]";
        }
    }
}
