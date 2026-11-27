using System.Runtime.InteropServices;

namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>
/// The location of a node, for an error that may never be reported: when the reader keeps the whole input, the line
/// and column are computed only when they are asked for, from the offsets of the node.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct YamlLocation
{
    private readonly Utf8YamlTokenSource? _source;
    private readonly long _startOffset;
    private readonly long _endOffset;
    private readonly Mark _start;
    private readonly Mark _end;

    /// <summary>Initializes a location from its start and end.</summary>
    /// <param name="start">The start of the node.</param>
    /// <param name="end">The end of the node.</param>
    public YamlLocation(Mark start, Mark end)
    {
        _start = start;
        _end = end;
    }

    internal YamlLocation(Utf8YamlTokenSource source, long startOffset, long endOffset)
    {
        _source = source;
        _startOffset = startOffset;
        _endOffset = endOffset;
    }

    /// <summary>Gets the start of the node.</summary>
    public Mark Start => _source is null ? _start : _source.GetMarkAt(_startOffset);

    /// <summary>Gets the end of the node.</summary>
    public Mark End => _source is null ? _end : _source.GetMarkAt(_endOffset);
}
