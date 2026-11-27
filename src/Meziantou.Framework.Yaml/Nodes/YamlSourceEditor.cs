using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Meziantou.Framework.Yaml.Events;

namespace Meziantou.Framework.Yaml.Nodes;

/// <summary>
/// Writes a stream as the text it was loaded from, with only the nodes that changed rewritten. The changes are found by
/// comparing the nodes with the state they recorded before their first change, and applied as replacements of ranges of
/// the text.
/// </summary>
internal static class YamlSourceEditor
{
    /// <summary>The state of a node, recorded before its first change: its event, its location and its children.</summary>
    public static YamlNodeSnapshot CaptureCurrent(IYamlModelNode node)
    {
        switch (node)
        {
            case YamlStream stream:
                return new YamlNodeSnapshot { Documents = [.. stream] };

            case YamlStreamDocument document:
                return new YamlNodeSnapshot { Header = document.DocumentStart, Contents = document.Contents };

            case YamlValue value:
                return new YamlNodeSnapshot { Header = value.Scalar, Start = value.Scalar.Start, End = value.Scalar.End };

            case YamlMapping mapping:
                var keys = new YamlNode[mapping.Count];
                var values = new YamlNode?[mapping.Count];
                var index = 0;
                foreach (var entry in mapping)
                {
                    keys[index] = entry.Key;
                    values[index] = entry.Value;
                    index++;
                }

                return new YamlNodeSnapshot { Header = mapping.MappingStart, Start = mapping.MappingStart.Start, End = mapping.MappingEnd.End, Keys = keys, Values = values };

            case YamlSequence sequence:
                return new YamlNodeSnapshot { Header = sequence.SequenceStart, Start = sequence.SequenceStart.Start, End = sequence.SequenceEnd.End, Items = [.. sequence] };

            default:
                throw new InvalidOperationException("Unexpected node type: " + node.GetType());
        }
    }

    /// <summary>Marks the nodes of a copy of an anchored node as loaded from the text, like the nodes of the text.</summary>
    // Iterative: the depth of a document is only bounded by MaxDepth, which can be larger than what the stack supports
    public static void Track(YamlNode root)
    {
        var pending = new Stack<YamlNode>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            node.IsTracked = true;
            switch (node)
            {
                case YamlMapping mapping:
                    foreach (var entry in mapping)
                    {
                        pending.Push(entry.Key);
                        if (entry.Value is not null)
                        {
                            pending.Push(entry.Value);
                        }
                    }

                    break;

                case YamlSequence sequence:
                    foreach (var item in sequence)
                    {
                        pending.Push(item);
                    }

                    break;
            }
        }
    }

    // The state of a node loaded from the text: recorded before its first change, or its current state while it is
    // unchanged. Null for a node that was not loaded from the text.
    private static YamlNodeSnapshot? SnapshotOf(IYamlModelNode? node)
    {
        if (node is null || !node.IsTracked)
            return null;

        return node.Snapshot ??= CaptureCurrent(node);
    }

    // The nodes of the stream being written, to know whether the node of an anchor is still there
    [ThreadStatic]
    private static HashSet<YamlNode>? s_liveNodes;

    private static HashSet<YamlNode> CollectNodes(YamlStream stream)
    {
        var nodes = new HashSet<YamlNode>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<YamlNode>();
        foreach (var document in stream)
        {
            if (document.Contents is not null)
            {
                pending.Push(document.Contents);
            }
        }

        while (pending.TryPop(out var node))
        {
            if (!nodes.Add(node))
                continue;

            switch (node)
            {
                case YamlMapping mapping:
                    foreach (var entry in mapping)
                    {
                        pending.Push(entry.Key);
                        if (entry.Value is not null)
                        {
                            pending.Push(entry.Value);
                        }
                    }

                    break;

                case YamlSequence sequence:
                    foreach (var item in sequence)
                    {
                        pending.Push(item);
                    }

                    break;
            }
        }

        return nodes;
    }

    public static string Render(YamlStream stream)
    {
        var source = stream.SourceText;
        if (source is null || SnapshotOf(stream)?.Documents is not { } documents || !documents.SequenceEqual(stream))
            return EmitStream(stream);

        var edits = new List<Edit>();
        s_liveNodes = CollectNodes(stream);
        try
        {
            foreach (var document in stream)
            {
                if (!AddDocumentEdits(document, source, edits))
                    return EmitStream(stream);
            }
        }
        finally
        {
            s_liveNodes = null;
        }

        if (edits.Count == 0)
            return source;

        edits.Sort((a, b) => a.Start.CompareTo(b.Start));
        var crlf = source.Contains("\r\n", StringComparison.Ordinal);
        var builder = new StringBuilder(source.Length);
        var position = 0;
        foreach (var edit in edits)
        {
            builder.Append(source, position, edit.Start - position);
            var text = edit.Text;

            // A replaced text that ends with a line break, such as a kept block scalar, is replaced by a text that ends with one
            if (text.Length > 0 && edit.End > edit.Start && source[edit.End - 1] is '\n' or '\r')
            {
                text += "\n";
            }

            builder.Append(crlf ? text.Replace("\n", "\r\n", StringComparison.Ordinal) : text);
            position = edit.End;
        }

        builder.Append(source, position, source.Length - position);
        return builder.ToString();
    }

    private static bool AddDocumentEdits(YamlStreamDocument document, string source, List<Edit> edits)
    {
        var snapshot = SnapshotOf(document);
        if (snapshot is null || !ReferenceEquals(snapshot.Header, document.DocumentStart))
            return false;

        if (!ReferenceEquals(snapshot.Contents, document.Contents))
        {
            // The root node was replaced: rewrite it where the previous one was
            if (SnapshotOf(snapshot.Contents) is not { } previous || document.Contents is null || previous.End.Index <= previous.Start.Index)
                return false;

            edits.Add(new Edit(previous.Start.Index, previous.End.Index, Emit(document.Contents, previous.Start.Column, inFlow: false)));
            return true;
        }

        if (document.Contents is not null)
        {
            AddNodeEdits(document.Contents, source, inFlow: false, edits);
        }

        return true;
    }

    private static void AddNodeEdits(YamlNode node, string source, bool inFlow, List<Edit> edits)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();
        var snapshot = SnapshotOf(node)!;
        if (node.AliasStart is { } aliasStart)
        {
            // A copy of an anchored node: its text is the alias, which stays as long as neither the copy nor the anchored
            // node changed. Otherwise the copy is written in the flow style, which is valid where the alias was.
            if (IsChanged(node) || node.AliasTarget is not YamlNode target || IsChanged(target) || target.Anchor is null || s_liveNodes?.Contains(target) is not true)
            {
                edits.Add(new Edit(aliasStart.Index, node.AliasEnd!.Value.Index, Emit(node, aliasStart.Column, inFlow: true)));
            }

            return;
        }

        switch (node)
        {
            case YamlValue value:
                if (!ReferenceEquals(value.Scalar, snapshot.Header))
                {
                    edits.Add(new Edit(snapshot.Start.Index, snapshot.End.Index, Emit(value, snapshot.Start.Column, inFlow)));
                }

                return;

            case YamlMapping mapping:
                if (!ReferenceEquals(mapping.MappingStart, snapshot.Header))
                {
                    ReplaceNode(node, inFlow, edits);
                    return;
                }

                var flow = inFlow || mapping.Style is YamlStyle.Flow;
                if (SameEntries(mapping, snapshot))
                {
                    foreach (var entry in mapping)
                    {
                        AddNodeEdits(entry.Key, source, flow, edits);
                        if (entry.Value is not null)
                        {
                            AddNodeEdits(entry.Value, source, flow, edits);
                        }
                    }

                    return;
                }

                var mappingEditCount = edits.Count;
                if (flow || !TryAddMappingEntryEdits(mapping, snapshot, source, edits))
                {
                    edits.RemoveRange(mappingEditCount, edits.Count - mappingEditCount);
                    ReplaceNode(node, inFlow, edits);
                }

                return;

            case YamlSequence sequence:
                if (!ReferenceEquals(sequence.SequenceStart, snapshot.Header))
                {
                    ReplaceNode(node, inFlow, edits);
                    return;
                }

                var flowSequence = inFlow || sequence.Style is YamlStyle.Flow;
                if (sequence.Count == snapshot.Items!.Length && sequence.Select((item, i) => ReferenceEquals(item, snapshot.Items[i])).All(same => same))
                {
                    foreach (var item in sequence)
                    {
                        AddNodeEdits(item, source, flowSequence, edits);
                    }

                    return;
                }

                var sequenceEditCount = edits.Count;
                if (flowSequence || !TryAddSequenceItemEdits(sequence, snapshot, source, edits))
                {
                    edits.RemoveRange(sequenceEditCount, edits.Count - sequenceEditCount);
                    ReplaceNode(node, inFlow, edits);
                }

                return;
        }
    }

    private static bool SameEntries(YamlMapping mapping, YamlNodeSnapshot snapshot)
    {
        if (mapping.Count != snapshot.Keys!.Length)
            return false;

        var index = 0;
        foreach (var entry in mapping)
        {
            if (!ReferenceEquals(entry.Key, snapshot.Keys[index]) || !ReferenceEquals(entry.Value, snapshot.Values![index]))
                return false;

            index++;
        }

        return true;
    }

    // Entries were added, removed or replaced: each original entry that is still there keeps its lines, in the same order
    private static bool TryAddMappingEntryEdits(YamlMapping mapping, YamlNodeSnapshot snapshot, string source, List<Edit> edits)
    {
        var keys = snapshot.Keys!;
        var values = snapshot.Values!;
        if (keys.Length == 0 || snapshot.End.Index <= snapshot.Start.Index)
            return false;

        // Every original entry starts its own line, at the indentation of the mapping
        var column = StartOf(keys[0]).Column;
        foreach (var key in keys)
        {
            if (key.AliasStart is not null || !StartsLine(source, StartOf(key).Index) || StartOf(key).Column != column)
                return false;
        }

        var current = mapping.ToList();
        var originalIndexes = new int[current.Count];
        var lastOriginal = -1;
        for (var i = 0; i < current.Count; i++)
        {
            originalIndexes[i] = Array.FindIndex(keys, key => ReferenceEquals(key, current[i].Key));
            if (originalIndexes[i] >= 0)
            {
                if (originalIndexes[i] <= lastOriginal)
                    return false;

                lastOriginal = originalIndexes[i];
            }
        }

        // Removed entries
        for (var k = 0; k < keys.Length; k++)
        {
            if (Array.IndexOf(originalIndexes, k) < 0)
            {
                edits.Add(new Edit(LineStart(source, StartOf(keys[k]).Index), EntryEnd(source, keys, values, k), ""));
            }
        }

        // Kept and added entries
        var pending = new StringBuilder();
        for (var i = 0; i < current.Count; i++)
        {
            var (key, value) = current[i];
            var k = originalIndexes[i];
            if (k < 0)
            {
                pending.Append(' ', column).Append(EmitEntry(key, value, column)).Append('\n');
                continue;
            }

            if (pending.Length > 0)
            {
                var position = LineStart(source, StartOf(keys[k]).Index);
                edits.Add(new Edit(position, position, pending.ToString()));
                pending.Clear();
            }

            AddNodeEdits(key, source, inFlow: false, edits);
            if (ReferenceEquals(value, values[k]))
            {
                if (value is not null)
                {
                    AddNodeEdits(value, source, inFlow: false, edits);
                }
            }
            else
            {
                if (!TryReplaceValue(values[k], value, column, EndOf(keys[k]).Index, source, edits))
                    return false;
            }
        }

        if (pending.Length > 0)
        {
            var position = NextLineStart(source, snapshot.End.Index);
            var text = pending.ToString();
            if (position == source.Length && (source.Length == 0 || source[^1] is not '\n'))
            {
                text = "\n" + text.TrimEnd('\n');
            }

            edits.Add(new Edit(position, position, text));
        }

        return true;
    }

    private static bool TryAddSequenceItemEdits(YamlSequence sequence, YamlNodeSnapshot snapshot, string source, List<Edit> edits)
    {
        var items = snapshot.Items!;
        if (items.Length == 0 || snapshot.End.Index <= snapshot.Start.Index)
            return false;

        // Every original item is on its own line after a '-' at the indentation of the sequence
        var column = -1;
        foreach (var item in items)
        {
            var start = StartOf(item).Index;
            var lineStart = LineStart(source, start);
            var prefix = source.AsSpan(lineStart, start - lineStart);
            var dash = prefix.IndexOf('-');
            if (item.AliasStart is not null || dash < 0 || !prefix[..dash].IsWhiteSpace() || !prefix[(dash + 1)..].IsWhiteSpace() || (column >= 0 && dash != column))
                return false;

            column = dash;
        }

        var current = sequence.ToList();
        if (current.Count == items.Length)
        {
            for (var i = 0; i < items.Length; i++)
            {
                if (ReferenceEquals(current[i], items[i]))
                {
                    AddNodeEdits(current[i], source, inFlow: false, edits);
                }
                else if (!TryReplaceValue(items[i], current[i], column, keyEnd: -1, source, edits))
                {
                    return false;
                }
            }

            return true;
        }

        var originalIndexes = new int[current.Count];
        var lastOriginal = -1;
        for (var i = 0; i < current.Count; i++)
        {
            originalIndexes[i] = Array.FindIndex(items, item => ReferenceEquals(item, current[i]));
            if (originalIndexes[i] >= 0)
            {
                if (originalIndexes[i] <= lastOriginal)
                    return false;

                lastOriginal = originalIndexes[i];
            }
        }

        for (var k = 0; k < items.Length; k++)
        {
            if (Array.IndexOf(originalIndexes, k) < 0)
            {
                edits.Add(new Edit(LineStart(source, StartOf(items[k]).Index), NextLineStart(source, EndOf(items[k]).Index), ""));
            }
        }

        var pending = new StringBuilder();
        for (var i = 0; i < current.Count; i++)
        {
            var k = originalIndexes[i];
            if (k < 0)
            {
                pending.Append(' ', column).Append("- ").Append(Emit(current[i], column + 2, inFlow: false)).Append('\n');
                continue;
            }

            if (pending.Length > 0)
            {
                var position = LineStart(source, StartOf(items[k]).Index);
                edits.Add(new Edit(position, position, pending.ToString()));
                pending.Clear();
            }

            AddNodeEdits(current[i], source, inFlow: false, edits);
        }

        if (pending.Length > 0)
        {
            var position = NextLineStart(source, snapshot.End.Index);
            var text = pending.ToString();
            if (position == source.Length && (source.Length == 0 || source[^1] is not '\n'))
            {
                text = "\n" + text.TrimEnd('\n');
            }

            edits.Add(new Edit(position, position, text));
        }

        return true;
    }

    // Replaces the value of a block mapping entry (keyEnd is the end of its key), or of a block sequence item (keyEnd is
    // -1), at the indentation of the collection
    private static bool TryReplaceValue(YamlNode? previous, YamlNode? value, int column, int keyEnd, string source, List<Edit> edits)
    {
        if (SnapshotOf(previous) is not { } snapshot || previous!.AliasStart is not null)
            return false;

        var isSequenceItem = keyEnd < 0;
        var start = snapshot.Start.Index;
        var end = snapshot.End.Index;

        // An empty value has no text: it can be replaced only right after its indicator, the ':' that follows the key or
        // the '-' of the item. An empty value elsewhere, such as the missing value of an explicit key, is not replaced in place.
        if (start == end)
        {
            var before = isSequenceItem ? source.AsSpan(0, start).TrimEnd(" \t") : source.AsSpan(keyEnd, Math.Max(0, start - keyEnd)).Trim(" \t\r\n");
            if (isSequenceItem ? before.IsEmpty || before[^1] != '-' : !before.SequenceEqual(":"))
                return false;
        }

        var lineStart = LineStart(source, start);
        var indentation = source.AsSpan(lineStart, start - lineStart);
        var onNewLine = indentation.IsWhiteSpace();

        // A value on its own line indented with a tab is kept as it is only when unchanged
        if (onNewLine && indentation.Contains('\t'))
            return false;

        if (value is null)
        {
            edits.Add(new Edit(start, end, ""));
            return true;
        }

        var isBlockCollection = value is YamlMapping { Style: not YamlStyle.Flow, Count: > 0 } or YamlSequence { Style: not YamlStyle.Flow, Count: > 0 };
        string text;
        if (isBlockCollection && !onNewLine && !isSequenceItem)
        {
            text = "\n" + new string(' ', column + 2) + Emit(value, column + 2, inFlow: false);
        }
        else
        {
            text = Emit(value, snapshot.Start.Column, inFlow: false);
            if (end == start && (start == 0 || source[start - 1] is not (' ' or '\t' or '\n' or '\r')))
            {
                text = " " + text;
            }
        }

        edits.Add(new Edit(start, end, text));
        return true;
    }

    private static void ReplaceNode(YamlNode node, bool inFlow, List<Edit> edits)
    {
        var snapshot = SnapshotOf(node)!;
        edits.Add(new Edit(snapshot.Start.Index, snapshot.End.Index, Emit(node, snapshot.Start.Column, inFlow)));
    }

    // Whether the node or one of its descendants changed since it was loaded
    private static bool IsChanged(YamlNode? node)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();
        if (node is null)
            return false;

        if (!node.IsTracked)
            return true;

        var snapshot = SnapshotOf(node)!;

        switch (node)
        {
            case YamlValue value:
                return !ReferenceEquals(value.Scalar, snapshot.Header);

            case YamlMapping mapping:
                if (!ReferenceEquals(mapping.MappingStart, snapshot.Header) || !SameEntries(mapping, snapshot))
                    return true;

                foreach (var entry in mapping)
                {
                    if (IsChanged(entry.Key) || IsChanged(entry.Value))
                        return true;
                }

                return false;

            case YamlSequence sequence:
                if (!ReferenceEquals(sequence.SequenceStart, snapshot.Header) || sequence.Count != snapshot.Items!.Length)
                    return true;

                for (var i = 0; i < sequence.Count; i++)
                {
                    if (!ReferenceEquals(sequence[i], snapshot.Items[i]) || IsChanged(sequence[i]))
                        return true;
                }

                return false;

            default:
                return true;
        }
    }

    private static Mark StartOf(YamlNode node) => SnapshotOf(node)!.Start;

    private static Mark EndOf(YamlNode node) => SnapshotOf(node)!.End;

    private static int EntryEnd(string source, YamlNode[] keys, YamlNode?[] values, int index)
    {
        var end = SnapshotOf(values[index]) is { } value ? Math.Max(value.End.Index, EndOf(keys[index]).Index) : EndOf(keys[index]).Index;
        return NextLineStart(source, end);
    }

    private static bool StartsLine(string source, int index) => source.AsSpan(LineStart(source, index), index - LineStart(source, index)).IsWhiteSpace();

    private static int LineStart(string source, int index)
    {
        var lineBreak = source.AsSpan(0, index).LastIndexOfAny('\n', '\r');
        return lineBreak + 1;
    }

    // The start of the line after the one that contains index, or the end of the text
    private static int NextLineStart(string source, int index)
    {
        var lineBreak = source.AsSpan(index).IndexOfAny('\n', '\r');
        if (lineBreak < 0)
            return source.Length;

        var position = index + lineBreak;
        return source[position] == '\r' && position + 1 < source.Length && source[position + 1] == '\n' ? position + 2 : position + 1;
    }

    private static string EmitEntry(YamlNode key, YamlNode? value, int column)
    {
        var mapping = new YamlMapping { { key.DeepCloneElement(), value?.DeepCloneElement() } };
        return Emit(mapping, column, inFlow: false);
    }

    // Writes a node with the emitter, with the lines after the first one indented to the column of the node
    private static string Emit(YamlNode node, int column, bool inFlow)
    {
        var element = node.DeepCloneElement();
        element.AliasStart = null;
        if (inFlow)
        {
            SetFlowStyle(element);
        }

        WriteEmptyScalarsAsNull(element);

        var writer = new StringWriter(CultureInfo.InvariantCulture);
        element.WriteTo(writer, suppressDocumentTags: true);
        var text = writer.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);

        // Remove the document markers that the emitter writes for a single node
        if (text.StartsWith("--- ", StringComparison.Ordinal))
        {
            text = text[4..];
        }
        else if (text.StartsWith("---\n", StringComparison.Ordinal))
        {
            text = text[4..];
        }

        if (text.EndsWith("\n...\n", StringComparison.Ordinal))
        {
            text = text[..^4];
        }

        // The line break that ends the node is the one of the source; the empty lines of a kept block scalar stay
        if (text is [.., '\n'])
        {
            text = text[..^1];
        }

        if (column > 0 && text.Contains('\n', StringComparison.Ordinal))
        {
            // Indent the lines after the first one, except the empty lines
            var indent = new string(' ', column);
            var lines = text.Split('\n');
            for (var i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length > 0)
                {
                    lines[i] = indent + lines[i];
                }
            }

            text = string.Join('\n', lines);
        }

        return text;
    }

    // The emitter writes an empty plain scalar as '' where it cannot leave it empty, which is a string instead of a null
    private static void WriteEmptyScalarsAsNull(YamlNode element)
    {
        switch (element)
        {
            case YamlMapping mapping:
                foreach (var entry in mapping)
                {
                    WriteEmptyScalarsAsNull(entry.Key);
                    if (entry.Value is not null)
                    {
                        WriteEmptyScalarsAsNull(entry.Value);
                    }
                }

                break;

            case YamlSequence sequence:
                foreach (var item in sequence)
                {
                    WriteEmptyScalarsAsNull(item);
                }

                break;

            case YamlValue { Scalar: { Value.Length: 0, Style: ScalarStyle.Plain or ScalarStyle.Any, Tag: null } scalar } value:
                value.Scalar = new Scalar(scalar.Anchor, scalar.Tag, "null", ScalarStyle.Plain, scalar.IsPlainImplicit, scalar.IsQuotedImplicit, scalar.Start, scalar.End);
                break;
        }
    }

    private static void SetFlowStyle(YamlNode element)
    {
        switch (element)
        {
            case YamlMapping mapping:
                mapping.Style = YamlStyle.Flow;
                foreach (var entry in mapping)
                {
                    SetFlowStyle(entry.Key);
                    if (entry.Value is not null)
                    {
                        SetFlowStyle(entry.Value);
                    }
                }

                break;

            case YamlSequence sequence:
                sequence.Style = YamlStyle.Flow;
                foreach (var item in sequence)
                {
                    SetFlowStyle(item);
                }

                break;

            case YamlValue value when value.Scalar.Style is ScalarStyle.Literal or ScalarStyle.Folded:
                var scalar = value.Scalar;
                value.Scalar = new Scalar(scalar.Anchor, scalar.Tag, scalar.Value, ScalarStyle.DoubleQuoted, scalar.IsPlainImplicit, scalar.IsQuotedImplicit, scalar.Start, scalar.End);
                break;
        }
    }

    private static string EmitStream(YamlStream stream)
    {
        var writer = new StringWriter(CultureInfo.InvariantCulture);
        stream.WriteTo(writer);
        return writer.ToString();
    }

    private static YamlNode DeepCloneElement(this YamlNode element) => (YamlNode)element.DeepClone();

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Edit(int Start, int End, string Text);
}
