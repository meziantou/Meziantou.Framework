using System.Runtime.InteropServices;

namespace Meziantou.Framework;

[StructLayout(LayoutKind.Auto)]
public readonly struct TextDiffEntry : IEquatable<TextDiffEntry>
{
    public TextDiffEntry(TextDiffOperation operation, ReadOnlyMemory<char> text)
    {
        Operation = operation;
        Text = text;
    }

    public TextDiffOperation Operation { get; }

    public ReadOnlyMemory<char> Text { get; }

    public bool Equals(TextDiffEntry other)
        => Operation == other.Operation && Text.Span.SequenceEqual(other.Text.Span);

    public override bool Equals(object? obj)
        => obj is TextDiffEntry other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Operation);
        hash.AddBytes(MemoryMarshal.AsBytes(Text.Span));
        return hash.ToHashCode();
    }

    public static bool operator ==(TextDiffEntry left, TextDiffEntry right) => left.Equals(right);

    public static bool operator !=(TextDiffEntry left, TextDiffEntry right) => !left.Equals(right);

    public override string ToString() => $"{Operation}: {Text}";
}
