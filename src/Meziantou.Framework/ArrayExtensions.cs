using System.ComponentModel;

namespace Meziantou.Framework;

public static class ArrayExtensions
{
    extension<T>(T[] array)
    {
        [EditorBrowsable(EditorBrowsableState.Never)]
        public int Count => array.Length;

        [EditorBrowsable(EditorBrowsableState.Never)]
        public long LongCount => array.LongLength;
    }
}
