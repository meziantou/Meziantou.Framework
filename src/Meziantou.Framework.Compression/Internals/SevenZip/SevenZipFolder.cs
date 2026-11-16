namespace Meziantou.Framework.Compression.Internals.SevenZip;

/// <summary>A folder: coders bound together, that decode packed streams to one stream, the concatenated content of files.</summary>
internal sealed class SevenZipFolder
{
    public required SevenZipCoder[] Coders { get; init; }

    public required SevenZipBindPair[] BindPairs { get; init; }

    /// <summary>Gets the in-stream index of each packed stream of the folder, in the order of the packed streams.</summary>
    public required int[] PackedStreams { get; init; }

    /// <summary>Gets the size of each out stream (one per coder, as every coder has one out stream).</summary>
    public long[] UnpackSizes { get; set; } = [];

    public uint? UnpackCrc { get; set; }

    /// <summary>Gets or sets the index of the first packed stream of the folder in the archive.</summary>
    public int FirstPackStreamIndex { get; set; }

    /// <summary>Gets or sets the absolute offsets of the packed streams of the folder in the archive stream.</summary>
    public long[] PackOffsets { get; set; } = [];

    /// <summary>Gets or sets the sizes of the packed streams of the folder.</summary>
    public long[] PackSizes { get; set; } = [];

    /// <summary>Gets or sets the number of files of the folder (the substreams).</summary>
    public int UnpackStreamCount { get; set; } = 1;

    /// <summary>Gets the index of the out stream that is not bound: the output of the folder.</summary>
    public int MainOutStream
    {
        get
        {
            for (var i = 0; i < Coders.Length; i++)
            {
                if (!Array.Exists(BindPairs, pair => pair.OutIndex == i))
                    return i;
            }

            return -1;
        }
    }

    public long UnpackSize => UnpackSizes[MainOutStream];

    public long TotalPackSize
    {
        get
        {
            long total = 0;
            foreach (var size in PackSizes)
            {
                total += size;
            }

            return total;
        }
    }

    public bool IsEncrypted => Array.Exists(Coders, coder => coder.MethodId == SevenZipMethodId.Aes);

    /// <summary>Gets the index of the first in stream of a coder.</summary>
    public int GetFirstInStream(int coderIndex)
    {
        var index = 0;
        for (var i = 0; i < coderIndex; i++)
        {
            index += Coders[i].InStreamCount;
        }

        return index;
    }
}
