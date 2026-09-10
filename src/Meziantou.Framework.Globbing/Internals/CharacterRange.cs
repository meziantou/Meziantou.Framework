using System.Runtime.InteropServices;

namespace Meziantou.Framework.Globbing.Internals;

[StructLayout(LayoutKind.Auto)]
internal readonly struct CharacterRange
{
    public CharacterRange(char min)
        : this(min, min)
    {
    }

    public CharacterRange(char min, char max)
    {
        Min = min;
        Max = max;
    }

    public char Min { get; }
    public char Max { get; }

    public int Length => Max - Min + 1;

    public bool IsSingleCharacterRange => Min == Max;

    public bool IsInRange(char c) => c >= Min && c <= Max;

    /// <summary>
    ///     Tests the character and both of its case variants against the range instead of normalizing the range,
    ///     which would change the characters it contains: '[@-B]' contains 'A', and lowering its bounds would drop
    ///     both '@' and 'A'.
    /// </summary>
    public bool IsInRangeIgnoreCase(char c) => IsInRange(c) || IsInRange(char.ToLowerInvariant(c)) || IsInRange(char.ToUpperInvariant(c));

    public char[] EnumerateCharacters()
    {
        // The index is an int on purpose: a 'char' counter wraps around when the range ends at char.MaxValue.
        var array = new char[Length];
        for (var i = 0; i < array.Length; i++)
        {
            array[i] = (char)(Min + i);
        }

        return array;
    }

    public override string ToString()
    {
        return $"[{Min}-{Max}]";
    }
}
