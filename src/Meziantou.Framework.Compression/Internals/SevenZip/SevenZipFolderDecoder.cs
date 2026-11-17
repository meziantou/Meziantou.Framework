using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.SevenZip;

/// <summary>Builds the decoders of a folder: each coder reads the packed streams or the outputs of the coders bound to it.</summary>
internal static class SevenZipFolderDecoder
{
    private const int PackBufferSize = 64 * 1024;

    /// <summary>Opens the output of a folder, limited to its unpack size.</summary>
    public static Stream Open(Stream archive, SevenZipFolder folder, SevenZipDecoderContext context)
    {
        return OpenCoder(archive, folder, folder.MainOutStream, context);
    }

    private static SevenZipLimitedStream OpenCoder(Stream archive, SevenZipFolder folder, int coderIndex, SevenZipDecoderContext context)
    {
        var coder = folder.Coders[coderIndex];
        var inputs = new Stream[coder.InStreamCount];
        try
        {
            var first = folder.GetFirstInStream(coderIndex);
            for (var i = 0; i < inputs.Length; i++)
            {
                var inIndex = first + i;
                var pairIndex = Array.FindIndex(folder.BindPairs, pair => pair.InIndex == inIndex);
                if (pairIndex >= 0)
                {
                    inputs[i] = OpenCoder(archive, folder, folder.BindPairs[pairIndex].OutIndex, context);
                }
                else
                {
                    var packIndex = Array.IndexOf(folder.PackedStreams, inIndex);
                    inputs[i] = new BufferedStream(new BoundedReadStream(archive, folder.PackOffsets[packIndex], folder.PackSizes[packIndex]), PackBufferSize);
                }
            }

            var unpackSize = folder.UnpackSizes[coderIndex];
            var decoder = SevenZipCoders.CreateDecoder(coder, inputs, unpackSize, context);
            return new SevenZipLimitedStream(decoder, unpackSize);
        }
        catch
        {
            foreach (var input in inputs)
            {
                input?.Dispose();
            }

            throw;
        }
    }
}
