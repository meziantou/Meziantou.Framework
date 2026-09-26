using System;
using System.Collections.Generic;
using System.Text;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Parsing;

public sealed partial class TomlParser
{
    // Tracks the tables and keys defined by the document to reject what TOML forbids: duplicate keys, table redefinitions,
    // extended inline tables, and static arrays reopened as arrays of tables. The rules mirror SyntaxValidator.
    // Nodes and entries are stored in arrays, so the parser does not allocate once they are large enough.
    private sealed partial class ParserCore
    {
        private const int RootStructureNode = 0;
        private const int ArrayStructureScope = -1;

        private readonly bool _allowDuplicateKeys;
        private StructureNode[] _structureNodes = new StructureNode[16];
        private int _structureNodeCount = 1;
        private StructureEntry[] _structureEntries = new StructureEntry[16];
        private int _structureEntryCount;
        private int[] _structureBuckets = new int[16];
        private int[] _structureScopes = new int[8];
        private int _structureScopeCount;
        private int _currentStructureTable = RootStructureNode;
        private int _pendingStructureValue = -1;

        private void DefineStructureTableHeader(List<KeySegment> path, bool isTableArray)
        {
            var node = RootStructureNode;
            for (var i = 0; i < path.Count - 1; i++)
            {
                node = DefineStructureHeaderPrefix(node, path, i);
            }

            _currentStructureTable = isTableArray
                ? DefineStructureTableArrayHeader(node, path, path.Count - 1)
                : DefineStructureTableHeaderLeaf(node, path, path.Count - 1);
        }

        private void DefineStructureKey(List<KeySegment>? path, in KeySegment singleSegment)
        {
            var node = _structureScopeCount > 0 ? _structureScopes[_structureScopeCount - 1] : _currentStructureTable;
            if (path is null)
            {
                _pendingStructureValue = DefineStructureKeyLeaf(node, singleSegment, path: null, index: 0);
                return;
            }

            for (var i = 0; i < path.Count - 1; i++)
            {
                node = DefineStructureKeyPrefix(node, path, i);
            }

            _pendingStructureValue = DefineStructureKeyLeaf(node, path[path.Count - 1], path, path.Count - 1);
        }

        private void PushStructureValueScope(bool isInlineTable)
        {
            var node = _pendingStructureValue;
            _pendingStructureValue = -1;
            if (!isInlineTable)
            {
                node = ArrayStructureScope;
            }
            else if (node < 0)
            {
                // An inline table in an array cannot be referenced by any other key
                node = AddStructureNode(StructureNodeKind.Value, isImplicit: false, fromDottedKeys: false, span: null);
            }

            if (_structureScopeCount == _structureScopes.Length)
            {
                Array.Resize(ref _structureScopes, _structureScopes.Length * 2);
            }

            _structureScopes[_structureScopeCount++] = node;
        }

        private void PopStructureValueScope()
        {
            if (_structureScopeCount > 0)
            {
                _structureScopeCount--;
            }
        }

        private int DefineStructureHeaderPrefix(int parent, List<KeySegment> path, int index)
        {
            var key = path[index];
            var entryIndex = FindStructureEntry(parent, key);
            if (entryIndex < 0)
            {
                return AddStructureEntry(parent, key, AddStructureNode(StructureNodeKind.Table, isImplicit: true, fromDottedKeys: false, key.Span));
            }

            var nodeIndex = _structureEntries[entryIndex].Node;
            ref var node = ref _structureNodes[nodeIndex];
            return node.Kind switch
            {
                StructureNodeKind.Table => nodeIndex,
                StructureNodeKind.TableArray => node.CurrentElement,
                _ => throw CreateKeyAlreadyDefinedException(key, path, index, nodeIndex),
            };
        }

        private int DefineStructureTableHeaderLeaf(int parent, List<KeySegment> path, int index)
        {
            var key = path[index];
            var entryIndex = FindStructureEntry(parent, key);
            if (entryIndex < 0)
            {
                return AddStructureEntry(parent, key, AddStructureNode(StructureNodeKind.Table, isImplicit: false, fromDottedKeys: false, key.Span));
            }

            // A header can define a table that a previous header created implicitly, but not one created by dotted keys
            var nodeIndex = _structureEntries[entryIndex].Node;
            ref var node = ref _structureNodes[nodeIndex];
            if (node is { Kind: StructureNodeKind.Table, IsImplicit: true, FromDottedKeys: false })
            {
                node.IsImplicit = false;
                node.Span = key.Span;
                return nodeIndex;
            }

            throw CreateKeyAlreadyDefinedException(key, path, index, nodeIndex);
        }

        private int DefineStructureTableArrayHeader(int parent, List<KeySegment> path, int index)
        {
            var key = path[index];
            var entryIndex = FindStructureEntry(parent, key);
            int arrayIndex;
            if (entryIndex < 0)
            {
                arrayIndex = AddStructureEntry(parent, key, AddStructureNode(StructureNodeKind.TableArray, isImplicit: false, fromDottedKeys: false, key.Span));
            }
            else
            {
                arrayIndex = _structureEntries[entryIndex].Node;
                if (_structureNodes[arrayIndex].Kind != StructureNodeKind.TableArray)
                {
                    throw CreateKeyAlreadyDefinedException(key, path, index, arrayIndex);
                }
            }

            var element = AddStructureNode(StructureNodeKind.Table, isImplicit: false, fromDottedKeys: false, key.Span);
            _structureNodes[arrayIndex].CurrentElement = element;
            return element;
        }

        private int DefineStructureKeyPrefix(int parent, List<KeySegment> path, int index)
        {
            var key = path[index];
            var entryIndex = FindStructureEntry(parent, key);
            if (entryIndex < 0)
            {
                return AddStructureEntry(parent, key, AddStructureNode(StructureNodeKind.Table, isImplicit: true, fromDottedKeys: true, key.Span));
            }

            // Dotted keys can only extend tables that no header defined. Once extended, a header cannot define the table.
            var nodeIndex = _structureEntries[entryIndex].Node;
            ref var node = ref _structureNodes[nodeIndex];
            if (node is { Kind: StructureNodeKind.Table, IsImplicit: true })
            {
                node.FromDottedKeys = true;
                return nodeIndex;
            }

            throw CreateKeyAlreadyDefinedException(key, path, index, nodeIndex);
        }

        private int DefineStructureKeyLeaf(int parent, in KeySegment key, List<KeySegment>? path, int index)
        {
            var entryIndex = FindStructureEntry(parent, key);
            if (entryIndex < 0)
            {
                return AddStructureEntry(parent, key, AddStructureNode(StructureNodeKind.Value, isImplicit: false, fromDottedKeys: true, key.Span));
            }

            ref var entry = ref _structureEntries[entryIndex];
            // DuplicateKeyHandling only applies to key/value pairs: table headers always follow the TOML rules
            if (_allowDuplicateKeys && _structureNodes[entry.Node].Kind == StructureNodeKind.Value)
            {
                // The new value replaces the previous one, including the keys of a previous inline table
                var node = AddStructureNode(StructureNodeKind.Value, isImplicit: false, fromDottedKeys: true, key.Span);
                _structureEntries[entryIndex].Node = node;
                return node;
            }

            throw CreateKeyAlreadyDefinedException(key, path, index, entry.Node);
        }

        private int FindStructureEntry(int parent, in KeySegment key)
        {
            var entryIndex = _structureBuckets[GetStructureBucket(parent, key.Hash, _structureBuckets.Length)] - 1;
            while (entryIndex >= 0)
            {
                ref var entry = ref _structureEntries[entryIndex];
                if (entry.Parent == parent && KeySegmentsEqual(entry.Key, key))
                {
                    return entryIndex;
                }

                entryIndex = entry.Next - 1;
            }

            return -1;
        }

        // Returns the node, so callers can chain it
        private int AddStructureEntry(int parent, in KeySegment key, int node)
        {
            if (_structureEntryCount == _structureEntries.Length)
            {
                Array.Resize(ref _structureEntries, _structureEntries.Length * 2);
            }

            if (_structureEntryCount == _structureBuckets.Length)
            {
                var buckets = new int[_structureBuckets.Length * 2];
                for (var i = 0; i < _structureEntryCount; i++)
                {
                    ref var existing = ref _structureEntries[i];
                    var existingBucket = GetStructureBucket(existing.Parent, existing.Key.Hash, buckets.Length);
                    existing.Next = buckets[existingBucket];
                    buckets[existingBucket] = i + 1;
                }

                _structureBuckets = buckets;
            }

            var bucket = GetStructureBucket(parent, key.Hash, _structureBuckets.Length);
            _structureEntries[_structureEntryCount] = new StructureEntry(parent, node, _structureBuckets[bucket], key);
            _structureBuckets[bucket] = ++_structureEntryCount;
            return node;
        }

        private int AddStructureNode(StructureNodeKind kind, bool isImplicit, bool fromDottedKeys, TomlSourceSpan? span)
        {
            if (_structureNodeCount == _structureNodes.Length)
            {
                Array.Resize(ref _structureNodes, _structureNodes.Length * 2);
            }

            _structureNodes[_structureNodeCount] = new StructureNode
            {
                Kind = kind,
                IsImplicit = isImplicit,
                FromDottedKeys = fromDottedKeys,
                CurrentElement = -1,
                Span = span,
            };

            return _structureNodeCount++;
        }

        private static int GetStructureBucket(int parent, ulong hash, int bucketCount)
        {
            var value = hash ^ ((ulong)(uint)parent * 0x9E3779B97F4A7C15UL);
            value ^= value >> 32;
            return (int)value & (bucketCount - 1);
        }

        private TomlException CreateKeyAlreadyDefinedException(in KeySegment key, List<KeySegment>? path, int index, int existingNode)
        {
            var name = new StringBuilder();
            if (path is null)
            {
                name.Append(GetSpan(key.Span));
            }
            else
            {
                for (var i = 0; i <= index; i++)
                {
                    if (i > 0)
                    {
                        name.Append('.');
                    }

                    name.Append(GetSpan(path[i].Span));
                }
            }

            var message = _structureNodes[existingNode].Span is { } existingSpan
                ? $"The key `{name}` is already defined at {existingSpan.Start} and cannot be redefined."
                : $"The key `{name}` is already defined and cannot be redefined.";
            return CreateException(key.Span, message);
        }

        private enum StructureNodeKind : byte
        {
            Table,
            TableArray,

            // A scalar, a static array, or an inline table: none of them can be extended
            Value,
        }

        private struct StructureNode
        {
            public StructureNodeKind Kind;
            public bool IsImplicit;
            public bool FromDottedKeys;
            public int CurrentElement;
            public TomlSourceSpan? Span;
        }

        private struct StructureEntry
        {
            public StructureEntry(int parent, int node, int next, KeySegment key)
            {
                Parent = parent;
                Node = node;
                Next = next;
                Key = key;
            }

            public int Parent;
            public int Node;
            public int Next;
            public KeySegment Key;
        }
    }
}
