using System.Runtime.InteropServices;

namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>
/// The location of a node, for an error that may never be reported: when the reader keeps the whole input, the line
/// and column are computed only when they are asked for, from the offsets of the node.
/// </summary>
/// <remarks>
/// Read <see cref="Start"/> and <see cref="End"/> while the input is read, for instance in the converter that got the
/// location: once <see cref="YamlSerializer"/> returns, the bytes that a deferred location needs may be gone.
/// </remarks>
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
    /// <exception cref="InvalidOperationException">The location is deferred, and the deserialization that read the input has returned.</exception>
    public Mark Start => _source is null ? _start : _source.GetMarkAt(_startOffset);

    /// <summary>Gets the end of the node.</summary>
    /// <exception cref="InvalidOperationException">The location is deferred, and the deserialization that read the input has returned.</exception>
    public Mark End => _source is null ? _end : _source.GetMarkAt(_endOffset);
}
