using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The decoder of the PXR24 compression (compression notes, section 6): for each line and channel, the differences of
/// consecutive samples split into byte planes (4 for UINT, 2 for HALF, 3 for FLOAT samples rounded to 24 bits), the whole
/// chunk compressed as one zlib datastream. HALF and UINT samples are exact; FLOAT samples lose their 8 low bits.
/// </summary>
internal static class ExrPxr24
{
    /// <summary>Gets the size of the inflated data of a chunk.</summary>
    public static int GetInflatedLength(in ExrChunkLayout layout)
    {
        long length = 0;
        for (var c = 0; c < layout.Channels.Count; c++)
        {
            var bytes = layout.Channels[c].PixelType switch
            {
                ExrPixelType.UInt => 4,
                ExrPixelType.Half => 2,
                _ => 3,
            };

            length += (long)bytes * layout.GetSampleCount(c) * layout.GetLineCount(c);
        }

        return checked((int)length);
    }

    /// <summary>Rebuilds the samples of a chunk from its inflated byte planes.</summary>
    /// <param name="planes">The inflated data (<see cref="GetInflatedLength"/> bytes).</param>
    /// <param name="destination">Receives the uncompressed data of the chunk.</param>
    /// <param name="layout">The geometry of the chunk.</param>
    public static void Decode(ReadOnlySpan<byte> planes, Span<byte> destination, in ExrChunkLayout layout)
    {
        var input = 0;
        var output = 0;
        for (var line = 0; line < layout.Lines; line++)
        {
            for (var c = 0; c < layout.Channels.Count; c++)
            {
                if (!layout.HasLine(c, line))
                    continue;

                var n = layout.GetSampleCount(c);
                switch (layout.Channels[c].PixelType)
                {
                    case ExrPixelType.UInt:
                    {
                        uint value = 0;
                        for (var i = 0; i < n; i++)
                        {
                            var difference = ((uint)planes[input + i] << 24) | ((uint)planes[input + n + i] << 16) | ((uint)planes[input + (2 * n) + i] << 8) | planes[input + (3 * n) + i];
                            value += difference;
                            BinaryPrimitives.WriteUInt32LittleEndian(destination[(output + (4 * i))..], value);
                        }

                        input += 4 * n;
                        output += 4 * n;
                        break;
                    }

                    case ExrPixelType.Half:
                    {
                        ushort value = 0;
                        for (var i = 0; i < n; i++)
                        {
                            var difference = (planes[input + i] << 8) | planes[input + n + i];
                            value = (ushort)(value + difference);
                            BinaryPrimitives.WriteUInt16LittleEndian(destination[(output + (2 * i))..], value);
                        }

                        input += 2 * n;
                        output += 2 * n;
                        break;
                    }

                    default:
                    {
                        uint value = 0;
                        for (var i = 0; i < n; i++)
                        {
                            var difference = ((uint)planes[input + i] << 16) | ((uint)planes[input + n + i] << 8) | planes[input + (2 * n) + i];
                            value = (value + difference) & 0xFFFFFF;
                            BinaryPrimitives.WriteUInt32LittleEndian(destination[(output + (4 * i))..], value << 8);
                        }

                        input += 3 * n;
                        output += 4 * n;
                        break;
                    }
                }
            }
        }
    }
}
