using System.Security.Cryptography;

namespace Meziantou.Framework.Imaging.TestHarness.Hevc;

/// <summary>
/// The hashes of the sample array of a colour component of a decoded picture, as an HEVC bitstream carries them (Rec. ITU-T
/// H.265, clause D.3.20 "Decoded picture hash SEI message semantics"): MD5, CRC and checksum of the samples of the picture
/// before its cropping to the conformance window.
/// </summary>
public static class ReferenceHevcPictureHash
{
    /// <summary>Computes <c>picture_md5[ cIdx ]</c> (Equation D-23).</summary>
    /// <param name="samples">The rows of the component: <paramref name="stride"/> samples per row.</param>
    /// <param name="width">The number of samples of a row that are part of the component.</param>
    /// <param name="height">The number of rows.</param>
    /// <param name="stride">The number of samples of a row of <paramref name="samples"/>.</param>
    /// <param name="bitDepth">The bit depth of the component.</param>
    /// <returns>The 16 bytes of the digest.</returns>
    [SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms", Justification = "MD5 is the hash that the HEVC specification defines for the decoded picture hash SEI message; it is not used for security.")]
    public static byte[] ComputeMd5(ReadOnlySpan<ushort> samples, int width, int height, int stride, int bitDepth)
    {
        return MD5.HashData(GetPictureData(samples, width, height, stride, bitDepth));
    }

    /// <summary>Computes <c>picture_crc[ cIdx ]</c> (Equation D-24).</summary>
    /// <inheritdoc cref="ComputeMd5" path="/param"/>
    /// <returns>The CRC.</returns>
    public static ushort ComputeCrc(ReadOnlySpan<ushort> samples, int width, int height, int stride, int bitDepth)
    {
        // pictureData is followed by two zero bytes
        var pictureData = GetPictureData(samples, width, height, stride, bitDepth);
        var crc = 0xFFFF;
        for (var bitIdx = 0; bitIdx < ((long)pictureData.Length + 2) * 8; bitIdx++)
        {
            var byteIndex = bitIdx >> 3;
            var dataByte = byteIndex < pictureData.Length ? pictureData[byteIndex] : 0;
            var crcMsb = (crc >> 15) & 1;
            var bitVal = (dataByte >> (7 - (bitIdx & 7))) & 1;
            crc = (((crc << 1) + bitVal) & 0xFFFF) ^ (crcMsb * 0x1021);
        }

        return (ushort)crc;
    }

    /// <summary>Computes <c>picture_checksum[ cIdx ]</c> (Equation D-25).</summary>
    /// <inheritdoc cref="ComputeMd5" path="/param"/>
    /// <returns>The checksum.</returns>
    public static uint ComputeChecksum(ReadOnlySpan<ushort> samples, int width, int height, int stride, int bitDepth)
    {
        uint sum = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var xorMask = (uint)((x & 0xFF) ^ (y & 0xFF) ^ (x >> 8) ^ (y >> 8));
                uint sample = samples[(y * stride) + x];
                sum += (sample & 0xFF) ^ xorMask;
                if (bitDepth > 8)
                {
                    sum += (sample >> 8) ^ xorMask;
                }
            }
        }

        return sum;
    }

    // Equation D-22: one byte per sample of at most 8 bits, otherwise two bytes, the least significant one first
    private static byte[] GetPictureData(ReadOnlySpan<ushort> samples, int width, int height, int stride, int bitDepth)
    {
        var bytesPerSample = bitDepth > 8 ? 2 : 1;
        var pictureData = new byte[checked(width * height * bytesPerSample)];
        var iLen = 0;
        for (var y = 0; y < height; y++)
        {
            var row = samples.Slice(y * stride, width);
            foreach (var sample in row)
            {
                pictureData[iLen++] = (byte)(sample & 0xFF);
                if (bytesPerSample is 2)
                {
                    pictureData[iLen++] = (byte)(sample >> 8);
                }
            }
        }

        return pictureData;
    }
}
