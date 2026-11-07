using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The geometry of the uncompressed data of one chunk (compression notes, section 1): for each line of the chunk from top to
/// bottom, for each channel in name order that has a sample line there, its samples from left to right.
/// </summary>
/// <param name="Channels">The channels, in name order.</param>
/// <param name="Width">The width of the chunk: the data window of a scan-line image, the width of a tile.</param>
/// <param name="Y0">The y coordinate of the first line (0 for a tile, whose channels are not sub-sampled).</param>
/// <param name="Lines">The number of lines.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ExrChunkLayout(IReadOnlyList<ExrChannel> Channels, int Width, int Y0, int Lines)
{
    /// <summary>Gets the number of samples of a line of a channel.</summary>
    public int GetSampleCount(int channel) => Width / Channels[channel].XSampling;

    /// <summary>Gets the number of sample lines of a channel in the chunk.</summary>
    public int GetLineCount(int channel) => ExrHeader.GetSampleLineCount(Channels[channel], Y0, Lines);

    /// <summary>Determines whether a channel has a sample line at the line <paramref name="line"/> of the chunk.</summary>
    public bool HasLine(int channel, int line)
    {
        var y = (long)Y0 + line;
        var sampling = Channels[channel].YSampling;
        return ((y % sampling) + sampling) % sampling == 0;
    }

    /// <summary>
    /// Interleaves planes (for each channel in name order, its sample lines from top to bottom) into the layout of the
    /// uncompressed data of the chunk.
    /// </summary>
    public void InterleavePlanes(ReadOnlySpan<byte> planes, Span<byte> data)
    {
        var channelCount = Channels.Count;
        Span<int> planeOffsets = channelCount <= 64 ? stackalloc int[channelCount] : new int[channelCount];
        var offset = 0;
        for (var c = 0; c < channelCount; c++)
        {
            planeOffsets[c] = offset;
            offset += Channels[c].BytesPerSample * GetSampleCount(c) * GetLineCount(c);
        }

        var position = 0;
        for (var line = 0; line < Lines; line++)
        {
            for (var c = 0; c < channelCount; c++)
            {
                if (!HasLine(c, line))
                    continue;

                var rowLength = Channels[c].BytesPerSample * GetSampleCount(c);
                planes.Slice(planeOffsets[c], rowLength).CopyTo(data[position..]);
                planeOffsets[c] += rowLength;
                position += rowLength;
            }
        }
    }

    /// <summary>Gets the size of the uncompressed data.</summary>
    public int GetDataLength()
    {
        long length = 0;
        for (var c = 0; c < Channels.Count; c++)
        {
            length += (long)Channels[c].BytesPerSample * GetSampleCount(c) * GetLineCount(c);
        }

        return checked((int)length);
    }
}
