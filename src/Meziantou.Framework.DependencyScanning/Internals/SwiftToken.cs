using System.Runtime.InteropServices;

namespace Meziantou.Framework.DependencyScanning.Internals;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct SwiftToken(SwiftTokenKind Kind, int Start, int End, int ContentStart, int ContentEnd, bool IsMultiline, bool IsTerminated)
{
    public int Length => End - Start;

    public bool Is(string text, SwiftTokenKind kind, string value)
    {
        return Kind == kind && text.AsSpan(Start, End - Start).SequenceEqual(value);
    }
}
