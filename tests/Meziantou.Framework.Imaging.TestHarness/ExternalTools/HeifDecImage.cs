using System.Buffers.Binary;
using System.IO.Compression;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>
/// An image decoded by <c>heif-dec</c> to a PNG file, read back without the library under test.
/// </summary>
/// <remarks>
/// heif-dec writes an RGB file, with an alpha channel when the image has an alpha plane; a monochrome image is written as
/// RGB too. The samples of an 8-bit image are written as they are; the samples of a deeper image are written in 16 bits,
/// shifted to the most significant bits (a 10-bit sample <c>v</c> is written as <c>v &lt;&lt; 6</c>), which is not the scale of
/// the 16-bit samples of the library: <see cref="GetPixels"/> scales them. The images of a sequence (<c>heif-dec -S</c>)
/// are always written in 8 bits: the 8 most significant bits of the samples of a deeper image. The reader only accepts
/// what heif-dec writes: non-interlaced, 8 or 16 bits per sample, no palette.
/// </remarks>
public sealed class HeifDecImage
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    // Four samples per pixel (R, G, B, A), as written in the file
    private readonly ushort[] _samples;

    private HeifDecImage(int width, int height, int fileBitDepth, bool hasAlpha, ushort[] samples)
    {
        Width = width;
        Height = height;
        FileBitDepth = fileBitDepth;
        HasAlpha = hasAlpha;
        _samples = samples;
    }

    /// <summary>Gets the width heif-dec wrote, after it applied the transformations of the file (<c>clap</c>, <c>irot</c>, <c>imir</c>).</summary>
    public int Width { get; }

    /// <summary>Gets the height heif-dec wrote, after it applied the transformations of the file (<c>clap</c>, <c>irot</c>, <c>imir</c>).</summary>
    public int Height { get; }

    /// <summary>Gets the bit depth of the samples of the PNG file: 8 for an 8-bit image, 16 otherwise.</summary>
    public int FileBitDepth { get; }

    /// <summary>Gets a value indicating whether heif-dec wrote an alpha channel.</summary>
    public bool HasAlpha { get; }

    /// <summary>Gets a sample as it is in the file: 0 to 255 or 0 to 65535 (<see cref="FileBitDepth"/>).</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <param name="channel">0 (red), 1 (green), 2 (blue) or 3 (alpha; the maximum when the file has no alpha channel).</param>
    /// <returns>The sample.</returns>
    public int GetFileSample(int x, int y, int channel) => _samples[(((y * Width) + x) * 4) + channel];

    /// <summary>Gets a sample at the bit depth of the image.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <param name="channel">0 (red), 1 (green), 2 (blue) or 3 (alpha; the maximum when the file has no alpha channel).</param>
    /// <param name="bitDepth">The bit depth of the image: 8 to 16.</param>
    /// <returns>The sample: 0 to <c>2^bitDepth - 1</c>.</returns>
    /// <exception cref="InvalidDataException">The file does not hold samples of that bit depth.</exception>
    public int GetSample(int x, int y, int channel, int bitDepth)
    {
        var sample = GetFileSample(x, y, channel);
        if (bitDepth == 8 && FileBitDepth == 8)
            return sample;

        if (bitDepth < 8 || bitDepth > 16 || FileBitDepth != (bitDepth == 8 ? 8 : 16))
            throw new InvalidDataException($"heif-dec wrote a {FileBitDepth.ToString(CultureInfo.InvariantCulture)}-bit PNG file, which does not hold the samples of a {bitDepth.ToString(CultureInfo.InvariantCulture)}-bit image.");

        // A missing alpha channel is opaque
        if (channel == 3 && !HasAlpha)
            return (1 << bitDepth) - 1;

        var shift = 16 - bitDepth;
        if ((sample & ((1 << shift) - 1)) != 0)
            throw new InvalidDataException($"The sample {sample.ToString(CultureInfo.InvariantCulture)} of the PNG file written by heif-dec is not a {bitDepth.ToString(CultureInfo.InvariantCulture)}-bit sample shifted to 16 bits.");

        return sample >> shift;
    }

    /// <summary>
    /// Gets the pixels as straight RGBA, 16-bit little-endian samples (<see cref="RawPixelLayout.Rgba16Le"/>), on the scale
    /// of the library: a sample <c>v</c> of an image of <paramref name="bitDepth"/> bits is <c>round(v * 65535 / max)</c>,
    /// which is <c>v * 257</c> at 8 bits. A missing alpha channel is opaque.
    /// </summary>
    /// <param name="bitDepth">The bit depth of the image: 8 to 16.</param>
    /// <returns>The pixels.</returns>
    /// <exception cref="InvalidDataException">The file does not hold samples of that bit depth.</exception>
    public RawPixelBuffer GetPixels(int bitDepth)
    {
        var maximum = (1 << bitDepth) - 1;
        var result = new byte[RawPixelLayout.Rgba16Le.GetByteLength(Width, Height)];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                for (var channel = 0; channel < 4; channel++)
                {
                    var sample = GetSample(x, y, channel, bitDepth);
                    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(((((y * Width) + x) * 4) + channel) * 2), (ushort)(((sample * 65535L) + (maximum / 2)) / maximum));
                }
            }
        }

        return RawPixelBuffer.Create(Width, Height, RawPixelLayout.Rgba16Le, result);
    }

    /// <summary>Reads a PNG file written by heif-dec.</summary>
    /// <param name="data">The file.</param>
    /// <returns>The image.</returns>
    /// <exception cref="InvalidDataException">The file is not a non-interlaced 8 or 16-bit gray, gray+alpha, RGB or RGBA PNG, or its data is invalid.</exception>
    public static HeifDecImage ReadPng(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8 || !data[..8].SequenceEqual(Signature))
            throw new InvalidDataException("heif-dec did not write a PNG file.");

        var header = default(ReadOnlySpan<byte>);
        using var compressed = new MemoryStream();
        var offset = 8;
        var ended = false;
        while (!ended)
        {
            if (data.Length - offset < 12)
                throw new InvalidDataException("The PNG file written by heif-dec is truncated.");

            var length = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            if (length > data.Length - offset - 12)
                throw new InvalidDataException("The PNG file written by heif-dec has an invalid chunk length.");

            var type = data.Slice(offset + 4, 4);
            var body = data.Slice(offset + 8, (int)length);
            if (type.SequenceEqual("IHDR"u8))
            {
                header = body;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                compressed.Write(body);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                ended = true;
            }

            offset += 12 + (int)length;
        }

        if (header.Length != 13)
            throw new InvalidDataException("The PNG file written by heif-dec has no valid IHDR chunk.");

        var width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header));
        var height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header[4..]));
        var (bitDepth, colorType, interlace) = (header[8], header[9], header[12]);
        var channels = colorType switch
        {
            0 => 1,
            2 => 3,
            4 => 2,
            6 => 4,
            _ => throw new InvalidDataException($"heif-dec wrote a PNG file with the unexpected color type {colorType}."),
        };

        if (bitDepth is not (8 or 16) || interlace != 0 || width <= 0 || height <= 0)
            throw new InvalidDataException($"heif-dec wrote a PNG file that is not a non-interlaced 8 or 16-bit image (bit depth {bitDepth}, interlace method {interlace}).");

        var bytesPerSample = bitDepth / 8;
        var bytesPerPixel = channels * bytesPerSample;
        var rowLength = width * bytesPerPixel;
        var inflated = new byte[checked(height * (rowLength + 1))];
        compressed.Position = 0;
        using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress))
        {
            // ZLibStream reports a truncated stream as a clean end: the expected number of bytes must be produced, and no more
            if (zlib.ReadAtLeast(inflated, inflated.Length, throwOnEndOfStream: false) != inflated.Length || zlib.ReadByte() >= 0)
                throw new InvalidDataException("The image data of the PNG file written by heif-dec does not have the expected length.");
        }

        var isGray = channels <= 2;
        var hasAlpha = channels is 2 or 4;
        var maximum = (ushort)((1 << bitDepth) - 1);
        var samples = new ushort[checked(width * height * 4)];
        var previous = new byte[rowLength];
        var current = new byte[rowLength];
        for (var y = 0; y < height; y++)
        {
            var filter = inflated[y * (rowLength + 1)];
            inflated.AsSpan((y * (rowLength + 1)) + 1, rowLength).CopyTo(current);
            Unfilter(filter, current, previous, bytesPerPixel);
            for (var x = 0; x < width; x++)
            {
                var source = current.AsSpan(x * bytesPerPixel, bytesPerPixel);
                var target = samples.AsSpan(((y * width) + x) * 4, 4);
                for (var channel = 0; channel < 3; channel++)
                {
                    target[channel] = ReadSample(source[((isGray ? 0 : channel) * bytesPerSample)..], bytesPerSample);
                }

                target[3] = hasAlpha ? ReadSample(source[((channels - 1) * bytesPerSample)..], bytesPerSample) : maximum;
            }

            (previous, current) = (current, previous);
        }

        return new HeifDecImage(width, height, bitDepth, hasAlpha, samples);
    }

    // PNG samples of 16 bits are big-endian
    private static ushort ReadSample(ReadOnlySpan<byte> source, int bytesPerSample) => bytesPerSample == 1 ? source[0] : BinaryPrimitives.ReadUInt16BigEndian(source);

    /// <summary>Reverses a PNG scanline filter in place (PNG specification, section 9.2).</summary>
    private static void Unfilter(byte filter, Span<byte> row, ReadOnlySpan<byte> previous, int bytesPerPixel)
    {
        for (var i = 0; i < row.Length; i++)
        {
            int left = i >= bytesPerPixel ? row[i - bytesPerPixel] : 0;
            int up = previous[i];
            int upLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;
            var predictor = filter switch
            {
                0 => 0,
                1 => left,
                2 => up,
                3 => (left + up) / 2,
                4 => Paeth(left, up, upLeft),
                _ => throw new InvalidDataException($"The PNG file written by heif-dec has the invalid filter type {filter}."),
            };

            row[i] = (byte)(row[i] + predictor);
        }
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        var estimate = left + up - upLeft;
        var distanceLeft = Math.Abs(estimate - left);
        var distanceUp = Math.Abs(estimate - up);
        var distanceUpLeft = Math.Abs(estimate - upLeft);
        if (distanceLeft <= distanceUp && distanceLeft <= distanceUpLeft)
            return left;

        return distanceUp <= distanceUpLeft ? up : upLeft;
    }
}
