namespace Meziantou.Framework;

public static class ArrayExtensions
{
    extension<T>(T[] array)
    {
        public int Count => array.Length;
        public long LongCount => array.LongLength;
    }
}
