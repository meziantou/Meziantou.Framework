namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Walks the chain of image file directories of a TIFF document, lazily, with the validation that keeps a malformed file
/// from turning into unbounded work:
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>a directory offset that was already visited is a cycle and is rejected, so <c>IFD0 → IFD1 → IFD0</c> cannot loop forever;</description></item>
/// <item><description>a directory whose bytes overlap a directory already read is rejected: a self-overlapping chain is malformed and is a cheap way to describe an enormous number of pages;</description></item>
/// <item><description>the number of pages is bounded by <see cref="ImageResourceLimits.MaxFrames"/> (pages are not animation frames, but the same safety bound applies to "structures produced from one input");</description></item>
/// <item><description>every offset and length is checked against the length of the input before anything is read.</description></item>
/// </list>
/// <para>
/// Walking the chain reads only the directories, never pixel data: describing a 400-page document costs a few kilobytes of
/// reads, whatever the size of the file.
/// </para>
/// </remarks>
internal static class TiffDirectoryChain
{
    /// <summary>Enumerates the directories of a document, from the first one.</summary>
    /// <param name="source">The input.</param>
    /// <param name="header">The file header.</param>
    /// <param name="limits">The limits bounding the number of directories.</param>
    /// <returns>The directories, in file order.</returns>
    /// <exception cref="InvalidImageContentException">The chain has a cycle, overlaps itself, or references bytes outside the input.</exception>
    /// <exception cref="ImageResourceLimitException">The document has more directories than <see cref="ImageResourceLimits.MaxFrames"/>.</exception>
    public static IEnumerable<TiffDirectory> Enumerate(RandomAccessSource source, TiffHeader header, ImageResourceLimits limits)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(limits);
        var visited = new HashSet<ulong>();
        var ranges = new List<(long Start, long End)>();
        var offset = header.FirstDirectoryOffset;
        var count = 0;
        while (offset != 0)
        {
            if (!visited.Add(offset))
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF directory chain is cyclic: the directory at offset {offset} is referenced twice."));

            if (count >= limits.MaxFrames)
                throw new ImageResourceLimitException(ImageResourceLimitKind.Frames, limits.MaxFrames, (long)limits.MaxFrames + 1);

            var directory = TiffDirectory.Read(source, header, offset);
            var start = (long)offset;
            var end = start + directory.Length;
            foreach (var (otherStart, otherEnd) in ranges)
            {
                if (start < otherEnd && otherStart < end)
                    throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF directory at offset {offset} overlaps the directory at offset {otherStart}."));
            }

            ranges.Add((start, end));
            count++;
            yield return directory;
            offset = directory.NextDirectoryOffset;
        }
    }
}
