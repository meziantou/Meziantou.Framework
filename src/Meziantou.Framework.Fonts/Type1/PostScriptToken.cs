using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>A token: its kind, where its text is, and its value when it is a number.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct PostScriptToken(PostScriptTokenKind Kind, int Start, int Length, double Number = 0)
{
    public bool IsExecutable(ReadOnlySpan<byte> data, ReadOnlySpan<byte> name) => Kind == PostScriptTokenKind.ExecutableName && data.Slice(Start, Length).SequenceEqual(name);

    public bool IsInteger => Kind == PostScriptTokenKind.Number && Number == Math.Floor(Number) && Math.Abs(Number) <= int.MaxValue;
}
