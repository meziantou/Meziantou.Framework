using System.IO.Compression;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Fuzz;

/// <summary>
/// The zlib datastreams of the PNG codec: the encoder writes a raw deflate datastream between a zlib header and an Adler-32
/// trailer computed by the library, and the bytes must be those of a <see cref="ZLibStream"/> of the same level; the
/// inflater of the decoder, pushed pieces of any size, must give the data back as soon as the bytes before the trailer
/// were pushed and see the end of the datastream.
/// </summary>
/// <remarks>
/// The input is a byte of zlib level (0 to 9), a byte of piece size (the size of the writes and of the pushes, 1 to 64, so
/// that the bytes held back by the inflater span several pushes) and the data.
/// </remarks>
public static partial class ImagingFuzzTargets
{
    private static void PngZlibTarget(ReadOnlySpan<byte> data)
    {
        var level = FuzzInput.ReadByte(ref data) % 10;
        var pieceSize = 1 + (FuzzInput.ReadByte(ref data) % 64);

        using var expected = new MemoryStream();
        using (var stream = new ZLibStream(expected, new ZLibCompressionOptions { CompressionLevel = level }, leaveOpen: true))
        {
            // zlib-ng's output depends on how the data is split into writes: both streams get the same writes
            for (var offset = 0; offset < data.Length; offset += pieceSize)
            {
                stream.Write(data.Slice(offset, Math.Min(pieceSize, data.Length - offset)));
            }
        }

        using var actual = new MemoryStream();
        using (var stream = new PngZlibStream(actual, level, leaveOpen: true))
        {
            for (var offset = 0; offset < data.Length; offset += pieceSize)
            {
                stream.Write(data.Slice(offset, Math.Min(pieceSize, data.Length - offset)));
            }
        }

        var zlib = actual.ToArray();
        FuzzAssert.True(zlib.AsSpan().SequenceEqual(expected.ToArray()), "The zlib datastream differs from the one of ZLibStream");

        // A stream without data writes nothing on .NET 10, which is not a zlib datastream
        if (zlib.Length == 0)
            return;

        using var inflater = new PngZlibInflater(new byte[pieceSize], rawDeflate: false);
        var output = new byte[data.Length + 1];
        var written = 0;
        var pushed = 0;
        while (true)
        {
            int read;
            while ((read = inflater.Inflate(output.AsSpan(written))) > 0)
            {
                written += read;
            }

            if (pushed == zlib.Length)
                break;

            // The rows of an image that is still loading are all decoded: the bytes held back are not data once the
            // inflater needs them
            FuzzAssert.True(pushed < zlib.Length - 4 || written == data.Length, "The inflater did not give the data of every byte pushed before the trailer");

            FuzzAssert.True(inflater.Available == 0, "The inflater stopped reading before the end of the zlib datastream");
            pushed += inflater.Push(zlib.AsSpan(pushed, Math.Min(pieceSize, zlib.Length - pushed)));
        }

        FuzzAssert.True(inflater.Available == 0, "The inflater did not read the whole zlib datastream");
        inflater.Complete();
        FuzzAssert.True(output.AsSpan(0, written).SequenceEqual(data), "The inflated data differs from the compressed data");
    }

    private static IEnumerable<FuzzSeed> GetPngZlibSeeds()
    {
        // Every level, pieces of 1 byte to whole writes, empty, short, repetitive and less compressible data
        byte[] text = [.. "Meziantou.Framework.Imaging PNG zlib datastream, Meziantou.Framework.Imaging PNG zlib datastream"u8];
        var noise = new byte[3000];
        for (var i = 0; i < noise.Length; i++)
        {
            noise[i] = (byte)((i * 2654435761u) >> 24);
        }

        for (var level = 0; level < 10; level++)
        {
            yield return new FuzzSeed(FormattableString.Invariant($"empty-{level}"), (byte[])[(byte)level, 63]);
            yield return new FuzzSeed(FormattableString.Invariant($"text-{level}"), (byte[])[(byte)level, 0, .. text]);
            yield return new FuzzSeed(FormattableString.Invariant($"noise-{level}"), (byte[])[(byte)level, (byte)(level * 7), .. noise]);
        }

        yield return new FuzzSeed("one-byte", (byte[])[6, 4, 42]);
    }
}
