namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The table of contents of a frame (ISO/IEC 18181-1, section C.3): the size in bytes of each section, in the order of the
/// codestream, and the permutation that gives the section each of them holds.
/// </summary>
internal sealed class JpegXlToc
{
    /// <summary>The largest number of sections the decoder accepts, which the reference decoder also enforces.</summary>
    public const int MaxSectionCount = 65536;

    private JpegXlToc(uint[] sizes, long[] offsets, long totalLength)
    {
        Sizes = sizes;
        Offsets = offsets;
        TotalLength = totalLength;
    }

    /// <summary>Gets the size in bytes of each section, indexed by section (the permutation is applied).</summary>
    public uint[] Sizes { get; }

    /// <summary>Gets the offset of each section from the end of the table of contents, indexed by section.</summary>
    public long[] Offsets { get; }

    /// <summary>Gets the number of bytes of all the sections: the length of the frame after its table of contents.</summary>
    public long TotalLength { get; }

    /// <summary>
    /// Reads a table of contents. The reader may overrun (<see cref="JpegXlBitReader.IsOverrun"/>): the result is then
    /// meaningless.
    /// </summary>
    /// <param name="reader">The reader, after the frame header.</param>
    /// <param name="sectionCount">The number of sections of the frame.</param>
    /// <param name="scope">The scope of the tables of the stream that codes a permutation.</param>
    /// <returns>The table; the reader is at the first byte of the first section.</returns>
    public static JpegXlToc Read(JpegXlBitReader reader, ulong sectionCount, AllocationScope scope)
    {
        if (sectionCount > MaxSectionCount)
            throw JpegXlFormat.Unsupported("The JPEG XL frame has more than 65,536 sections.", "frame with more than 65,536 sections");

        var count = (int)sectionCount;
        int[]? permutation = null;
        if (reader.ReadBool())
        {
            permutation = JpegXlPermutation.Read(reader, count, skip: 0, scope);
        }

        reader.ZeroPadToByte();
        var codedSizes = new uint[count];
        long total = 0;
        for (var i = 0; i < codedSizes.Length; i++)
        {
            codedSizes[i] = reader.ReadU32(0, 10, 1024, 14, 17408, 22, 4211712, 30);
            total += codedSizes[i];
        }

        reader.ZeroPadToByte();

        // Sizes are stored in the order of the codestream; the section i is the permutation[i]-th of them
        var codedOffsets = new long[count];
        long offset = 0;
        for (var i = 0; i < count; i++)
        {
            codedOffsets[i] = offset;
            offset += codedSizes[i];
        }

        if (permutation is null)
            return new JpegXlToc(codedSizes, codedOffsets, total);

        var sizes = new uint[count];
        var offsets = new long[count];
        for (var i = 0; i < count; i++)
        {
            sizes[i] = codedSizes[permutation[i]];
            offsets[i] = codedOffsets[permutation[i]];
        }

        return new JpegXlToc(sizes, offsets, total);
    }
}
