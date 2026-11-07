using System.Runtime.InteropServices;
using Meziantou.Framework.Toml.Parsing;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Serialization.Internal;

/// <summary>
/// A parse event without references, so a large buffer of events costs nothing to the garbage collector.
/// </summary>
/// <remarks>
/// Property names and strings are decoded from the source text on demand. The value of a date-time event is stored
/// separately, and <see cref="Data"/> is its index.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal readonly struct TomlBufferedParseEvent
{
    public TomlBufferedParseEvent(TomlParseEventKind kind, TomlSourceSpan? span, ulong data)
    {
        Kind = kind;
        if (span is { } value)
        {
            HasSpan = true;
            Start = value.Start;
            End = value.End;
        }

        Data = data;
    }

    public TomlParseEventKind Kind { get; }

    public bool HasSpan { get; }

    public TomlTextPosition Start { get; }

    public TomlTextPosition End { get; }

    public ulong Data { get; }

    public TomlSourceSpan? GetSpan(string sourceName) => HasSpan ? new TomlSourceSpan(sourceName, Start, End) : null;

    public TomlParseEvent ToParseEvent(string sourceName) => new(Kind, GetSpan(sourceName), propertyName: null, stringValue: null, Data);
}
