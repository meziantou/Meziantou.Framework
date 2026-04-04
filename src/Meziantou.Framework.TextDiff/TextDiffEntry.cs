using System.Runtime.InteropServices;

namespace Meziantou.Framework;

[StructLayout(LayoutKind.Auto)]
public readonly struct TextDiffEntry : IEquatable<TextDiffEntry>
{
    public TextDiffEntry(TextDiffOperation operation, string text)
    {
        Operation = operation;
        Text = text;
    }

    public TextDiffOperation Operation { get; }

    public string Text { get; }

    public bool Equals(TextDiffEntry other)
        => Operation == other.Operation && Text == other.Text;

    public override bool Equals(object? obj)
        => obj is TextDiffEntry other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Operation);
        hash.Add(Text);
        return hash.ToHashCode();
    }

    public static bool operator ==(TextDiffEntry left, TextDiffEntry right) => left.Equals(right);

    public static bool operator !=(TextDiffEntry left, TextDiffEntry right) => !left.Equals(right);

    public override string ToString() => $"{Operation}: {Text}";
}
