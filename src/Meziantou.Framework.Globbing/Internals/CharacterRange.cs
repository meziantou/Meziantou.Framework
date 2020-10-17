using System.Runtime.InteropServices;

namespace Meziantou.Framework.Globbing.Internals
{
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

        public bool IsSingleCharacterRange => Min == Max;

        public override string ToString()
        {
            return $"[{Min}-{Max}]";
        }
    }
}
