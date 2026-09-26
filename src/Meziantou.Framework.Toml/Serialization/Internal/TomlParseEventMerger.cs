using System;
using System.Collections.Generic;
using Meziantou.Framework.Toml.Parsing;

namespace Meziantou.Framework.Toml.Serialization.Internal;

/// <summary>
/// Reorders parse events so that every table and array of tables is contiguous.
/// </summary>
/// <remarks>
/// TOML allows a table to be defined in several fragments: <c>[[a]]</c> can be reopened after another header, <c>[a.b]</c>
/// extends the last element of the array of tables <c>a</c>, and dotted keys can extend a table after another key.
/// The parser emits each fragment where it appears, so consumers would see the same key several times. The merged order
/// emits the first <c>PropertyName</c> and <c>StartTable</c>/<c>StartArray</c> events of a container, the members of all
/// its fragments, then the last <c>EndTable</c>/<c>EndArray</c> event.
/// </remarks>
internal static class TomlParseEventMerger
{
    /// <summary>Gets the merged event order, or <see langword="null"/> when the events are already contiguous.</summary>
    public static int[]? GetMergedOrder(TomlBufferedParseEvent[] events, int count, TomlParser parser, string sourceName)
    {
        if (!HasSplitContainers(events, count))
        {
            return null;
        }

        var nodes = new List<Node>();
        var stack = new List<Node>();
        var pendingName = -1;
        var merged = false;
        var prefix = new List<int>();
        var suffix = new List<int>();
        Node? root = null;

        for (var i = 0; i < count; i++)
        {
            ref readonly var parseEvent = ref events[i];
            switch (parseEvent.Kind)
            {
                case TomlParseEventKind.PropertyName:
                    pendingName = i;
                    break;

                case TomlParseEventKind.StartTable:
                case TomlParseEventKind.StartArray:
                    {
                        var isArray = parseEvent.Kind == TomlParseEventKind.StartArray;
                        if (stack.Count == 0)
                        {
                            root = AddNode(nodes, i, isArray);
                            stack.Add(root);
                            break;
                        }

                        var parent = stack[^1];

                        // Only table headers and dotted keys, which have no span, can extend an existing container
                        string? name = null;
                        if (!parseEvent.HasSpan && pendingName >= 0 && !parent.IsArray)
                        {
                            name = parser.DecodePropertyName(events[pendingName].ToParseEvent(sourceName));
                            if (parent.Children is not null && parent.Children.TryGetValue(name, out var existing))
                            {
                                merged = true;
                                pendingName = -1;

                                // [a.b] after [[a]] extends the last element of the array of tables
                                stack.Add(existing.IsArray && !isArray ? nodes[-existing.Entries[^1] - 1] : existing);
                                break;
                            }
                        }

                        var node = AddNode(nodes, i, isArray);
                        if (!parent.IsArray)
                        {
                            parent.Entries.Add(pendingName);
                        }

                        parent.Entries.Add(-node.Id - 1);
                        if (name is not null)
                        {
                            (parent.Children ??= new Dictionary<string, Node>(StringComparer.Ordinal))[name] = node;
                        }

                        pendingName = -1;
                        stack.Add(node);
                        break;
                    }

                case TomlParseEventKind.EndTable:
                case TomlParseEventKind.EndArray:
                    if (stack.Count > 0)
                    {
                        stack[^1].EndEvent = i;
                        stack.RemoveAt(stack.Count - 1);
                    }

                    break;

                case TomlParseEventKind.StartDocument:
                case TomlParseEventKind.EndDocument:
                    (root is null ? prefix : suffix).Add(i);
                    break;

                default:
                    {
                        var parent = stack[^1];
                        if (!parent.IsArray)
                        {
                            parent.Entries.Add(pendingName);
                        }

                        parent.Entries.Add(i);
                        pendingName = -1;
                        break;
                    }
            }
        }

        if (!merged || root is null)
        {
            return null;
        }

        var order = new List<int>(count);
        order.AddRange(prefix);
        var frames = new Stack<(Node Node, int EntryIndex)>();
        order.Add(root.StartEvent);
        frames.Push((root, 0));
        while (frames.Count > 0)
        {
            var (node, entryIndex) = frames.Pop();
            if (entryIndex == node.Entries.Count)
            {
                order.Add(node.EndEvent);
                continue;
            }

            frames.Push((node, entryIndex + 1));
            var entry = node.Entries[entryIndex];
            if (entry >= 0)
            {
                order.Add(entry);
            }
            else
            {
                var child = nodes[-entry - 1];
                order.Add(child.StartEvent);
                frames.Push((child, 0));
            }
        }

        order.AddRange(suffix);
        return order.ToArray();
    }

    // A cheap check that most documents pass: no table header or dotted key reopens a container of the same parent.
    // It compares the name hashes, so a collision only costs an unnecessary merge.
    private static bool HasSplitContainers(TomlBufferedParseEvent[] events, int count)
    {
        HashSet<(int Parent, ulong NameHash)>? names = null;
        var containers = new Stack<(int Id, bool IsArray)>();
        var nextContainerId = 0;
        var hasPendingName = false;
        ulong pendingNameHash = 0;
        for (var i = 0; i < count; i++)
        {
            ref readonly var parseEvent = ref events[i];
            switch (parseEvent.Kind)
            {
                case TomlParseEventKind.PropertyName:
                    hasPendingName = true;
                    pendingNameHash = TomlParseEventData.UnpackPropertyNameHash(parseEvent.Data);
                    break;

                case TomlParseEventKind.StartTable:
                case TomlParseEventKind.StartArray:
                    if (!parseEvent.HasSpan && hasPendingName && containers.TryPeek(out var parent) && !parent.IsArray)
                    {
                        names ??= [];
                        if (!names.Add((parent.Id, pendingNameHash)))
                        {
                            return true;
                        }
                    }

                    hasPendingName = false;
                    containers.Push((nextContainerId++, parseEvent.Kind == TomlParseEventKind.StartArray));
                    break;

                case TomlParseEventKind.EndTable:
                case TomlParseEventKind.EndArray:
                    containers.TryPop(out _);
                    break;

                default:
                    hasPendingName = false;
                    break;
            }
        }

        return false;
    }

    private static Node AddNode(List<Node> nodes, int startEvent, bool isArray)
    {
        var node = new Node(nodes.Count, startEvent, isArray);
        nodes.Add(node);
        return node;
    }

    private sealed class Node(int id, int startEvent, bool isArray)
    {
        public int Id { get; } = id;

        public int StartEvent { get; } = startEvent;

        public int EndEvent { get; set; } = -1;

        public bool IsArray { get; } = isArray;

        // An event index, or -(node id + 1) for a child container. Tables alternate property names and values.
        public List<int> Entries { get; } = [];

        // The containers created by a table header or dotted keys, by name
        public Dictionary<string, Node>? Children { get; set; }
    }
}
