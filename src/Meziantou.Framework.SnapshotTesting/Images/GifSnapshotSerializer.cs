namespace Meziantou.Framework.SnapshotTesting;

internal sealed class GifSnapshotSerializer : ISnapshotSerializer
{
    public static ISnapshotSerializer Instance { get; } = new GifSnapshotSerializer();

    public bool TrySerialize(SnapshotType type, object? value, [NotNullWhen(true)] out SerializedSnapshot? result)
    {
        if (type != SnapshotType.Gif || value is not byte[] gifData || !TryExtractFrames(gifData, out var frames))
        {
            result = null;
            return false;
        }

        var snapshotData = new SnapshotData[frames.Count];
        for (var i = 0; i < frames.Count; i++)
        {
            if (!GifImageLoader.TryLoadSingleFrame(frames[i], out var image))
            {
                result = null;
                return false;
            }

            snapshotData[i] = new SnapshotData(".png", PngImageEncoder.Encode(image));
        }

        result = new SerializedSnapshot(snapshotData);
        return true;
    }

    private static bool TryExtractFrames(byte[] source, [NotNullWhen(true)] out List<byte[]>? frames)
    {
        frames = null;
        if (!IsGifHeader(source))
            return false;

        var offset = 13; // Header + logical screen descriptor
        var packedFields = source[10];
        if (HasGlobalColorTable(packedFields))
        {
            if (!TrySkipColorTable(source, packedFields, ref offset))
                return false;
        }

        var sharedPrefixLength = offset;
        var pendingExtensions = new List<(int Start, int Length)>();
        var extractedFrames = new List<byte[]>();

        while (offset < source.Length)
        {
            var blockType = source[offset];
            if (blockType == 0x3B) // Trailer
            {
                if (offset != source.Length - 1 || extractedFrames.Count == 0)
                    return false;

                frames = extractedFrames;
                return true;
            }

            if (blockType == 0x21) // Extension block
            {
                if (!TryReadExtensionBlock(source, ref offset, out var extensionStart, out var extensionLength))
                    return false;

                pendingExtensions.Add((extensionStart, extensionLength));
                continue;
            }

            if (blockType != 0x2C) // Image descriptor
                return false;

            if (!TryReadImageBlock(source, ref offset, out var imageStart, out var imageLength))
                return false;

            var outputLength = sharedPrefixLength + imageLength + 1; // + trailer
            foreach (var extension in pendingExtensions)
            {
                outputLength += extension.Length;
            }

            var frameData = new byte[outputLength];
            var destinationOffset = 0;
            Buffer.BlockCopy(source, 0, frameData, destinationOffset, sharedPrefixLength);
            destinationOffset += sharedPrefixLength;

            foreach (var extension in pendingExtensions)
            {
                Buffer.BlockCopy(source, extension.Start, frameData, destinationOffset, extension.Length);
                destinationOffset += extension.Length;
            }

            Buffer.BlockCopy(source, imageStart, frameData, destinationOffset, imageLength);
            destinationOffset += imageLength;
            frameData[destinationOffset] = 0x3B;

            extractedFrames.Add(frameData);
            pendingExtensions.Clear();
        }

        return false;
    }

    private static bool TryReadExtensionBlock(byte[] source, ref int offset, out int start, out int length)
    {
        start = offset;
        length = 0;

        if (offset >= source.Length || source[offset] != 0x21)
            return false;

        offset++; // Extension introducer
        if (offset >= source.Length)
            return false;

        offset++; // Extension label
        if (!TrySkipSubBlocks(source, ref offset))
            return false;

        length = offset - start;
        return true;
    }

    private static bool TryReadImageBlock(byte[] source, ref int offset, out int start, out int length)
    {
        start = offset;
        length = 0;

        if (offset + 10 > source.Length || source[offset] != 0x2C)
            return false;

        var packedFields = source[offset + 9];
        offset += 10; // Image separator + image descriptor

        if (HasLocalColorTable(packedFields) && !TrySkipColorTable(source, packedFields, ref offset))
            return false;

        if (offset >= source.Length)
            return false;

        offset++; // LZW minimum code size
        if (!TrySkipSubBlocks(source, ref offset))
            return false;

        length = offset - start;
        return true;
    }

    private static bool TrySkipSubBlocks(byte[] source, ref int offset)
    {
        while (offset < source.Length)
        {
            var blockSize = source[offset];
            offset++;
            if (blockSize == 0)
                return true;

            if (offset + blockSize > source.Length)
                return false;

            offset += blockSize;
        }

        return false;
    }

    private static bool TrySkipColorTable(byte[] source, byte packedFields, ref int offset)
    {
        var colorTableSize = GetColorTableSize(packedFields);
        if (offset + colorTableSize > source.Length)
            return false;

        offset += colorTableSize;
        return true;
    }

    private static int GetColorTableSize(byte packedFields)
    {
        var colorCountPower = (packedFields & 0b0000_0111) + 1;
        var colorCount = 1 << colorCountPower;
        return colorCount * 3;
    }

    private static bool IsGifHeader(byte[] source)
    {
        if (source.Length < 14)
            return false;

        if (source[0] != (byte)'G' || source[1] != (byte)'I' || source[2] != (byte)'F')
            return false;

        if (source[3] != (byte)'8')
            return false;

        return (source[4], source[5]) is ((byte)'7', (byte)'a') or ((byte)'9', (byte)'a');
    }

    private static bool HasGlobalColorTable(byte packedFields) => (packedFields & 0b1000_0000) != 0;
    private static bool HasLocalColorTable(byte packedFields) => (packedFields & 0b1000_0000) != 0;
}
