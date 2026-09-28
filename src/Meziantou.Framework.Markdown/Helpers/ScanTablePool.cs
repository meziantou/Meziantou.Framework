using System.Buffers;

namespace Meziantou.Framework.Markdown.Helpers;

/// <summary>
/// Provides the per-position tables of the scan caches. A large table is allocated instead of rented: the shared pool
/// keeps the arrays returned to it, so a few large documents would keep hundreds of megabytes alive after parsing.
/// </summary>
internal static class ScanTablePool<T>
{
    // The shared pool rounds the length up to a power of two, so the arrays it provides are never longer than this
    private const int MaximumPooledLength = 16 * 1024;

    public static T[] Rent(int length)
    {
        return length <= MaximumPooledLength ? ArrayPool<T>.Shared.Rent(length) : new T[length];
    }

    public static void Return(T[] array)
    {
        if (array.Length <= MaximumPooledLength)
        {
            ArrayPool<T>.Shared.Return(array);
        }
    }
}
