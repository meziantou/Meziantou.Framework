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

    public char[] EnumerateCharacters()
    {
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
