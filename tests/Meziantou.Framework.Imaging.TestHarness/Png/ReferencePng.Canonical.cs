using System.Buffers.Binary;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.TestHarness.Png;

/// <summary>
/// The canonical decoding of the reference reader: every color type at every bit depth, with the <c>tRNS</c> color key of
/// grayscale and RGB images, to 16-bit RGBA (<see cref="RawPixelLayout.Rgba16Le"/>), and an APNG compositor of the frames
/// that an exact encoder can write.
/// </summary>
/// <remarks>
/// <para>
/// A sample of <c>d</c> bits is scaled to 16 bits as <c>s * 65535 / (2^d - 1)</c>, which is exact for every PNG bit depth.
/// A grayscale or RGB pixel whose samples equal the <c>tRNS</c> key is fully transparent (its color is kept), every other one
/// is opaque; a palette entry takes the alpha value of <c>tRNS</c> at its index, or is opaque.
/// </para>
/// <para>
/// The compositor follows the APNG rules: the canvas starts transparent black; <c>APNG_BLEND_OP_SOURCE</c> replaces the
/// region, <c>APNG_BLEND_OP_OVER</c> composites it; after a frame is displayed, <c>APNG_DISPOSE_OP_BACKGROUND</c> clears its
/// region to transparent black and <c>APNG_DISPOSE_OP_PREVIOUS</c> restores the canvas it was drawn on (BACKGROUND for the
/// first frame). OVER is only computed where its result does not depend on the rounding of a decoder: a fully transparent
/// source pixel leaves the canvas pixel, an opaque one replaces it, and so does any visible source pixel on a fully
/// transparent canvas pixel. Any other OVER pixel is reported as an error, since no exact encoder writes it.
/// </para>
/// </remarks>
public sealed partial class ReferencePng
{
    /// <summary>Decodes the <c>IDAT</c> image (the static image, the poster, or frame zero) to 16-bit RGBA.</summary>
    /// <returns>The pixels, <see cref="RawPixelLayout.Rgba16Le"/>.</returns>
    public RawPixelBuffer DecodeImageRgba16() => DecodeCanonical(GetImageData(), Width, Height);

    /// <summary>Decodes the datastream of an animation frame, at its region size, to 16-bit RGBA.</summary>
    /// <param name="index">The frame index in playback order.</param>
    /// <returns>The region pixels, <see cref="RawPixelLayout.Rgba16Le"/>.</returns>
    public RawPixelBuffer DecodeFrameRgba16(int index)
    {
        var animation = Animation ?? throw new InvalidOperationException("The file is not an APNG.");
        var frame = animation.Frames[index];
        return DecodeCanonical(frame.Datastream.ToArray(), frame.Width, frame.Height);
    }

    /// <summary>Composites the frames of the animation and returns the displayed canvas of each, in 16-bit RGBA.</summary>
    /// <returns>One canvas per displayed frame, <see cref="RawPixelLayout.Rgba16Le"/>.</returns>
    /// <exception cref="InvalidDataException">An OVER pixel would depend on the rounding of the decoder.</exception>
    public IReadOnlyList<RawPixelBuffer> ComposeDisplayedFramesRgba16()
    {
        var animation = Animation ?? throw new InvalidOperationException("The file is not an APNG.");
        var canvas = new ushort[Width * Height * 4];
        var result = new List<RawPixelBuffer>();
        for (var i = 0; i < animation.Frames.Count; i++)
        {
            var frame = animation.Frames[i];
            var region = DecodeFrameRgba16(i).Span;
            var before = frame.DisposeOp == 2 && i > 0 ? (ushort[])canvas.Clone() : null;
            for (var y = 0; y < frame.Height; y++)
            {
                for (var x = 0; x < frame.Width; x++)
                {
                    var source = region.Slice(((y * frame.Width) + x) * 8, 8);
                    var target = (((frame.YOffset + y) * Width) + frame.XOffset + x) * 4;
                    var sourceAlpha = BinaryPrimitives.ReadUInt16LittleEndian(source[6..]);
                    if (frame.BlendOp == 1)
                    {
                        if (sourceAlpha == 0)
                            continue;

                        var canvasAlpha = canvas[target + 3];
                        if (sourceAlpha != ushort.MaxValue && canvasAlpha != 0)
                            throw new InvalidDataException($"Frame {i} blends the pixel ({frame.XOffset + x}, {frame.YOffset + y}) with OVER, alpha {sourceAlpha} over a canvas of alpha {canvasAlpha}: the result depends on the rounding of the decoder.");
                    }

                    for (var c = 0; c < 4; c++)
                    {
                        canvas[target + c] = BinaryPrimitives.ReadUInt16LittleEndian(source[(c * 2)..]);
                    }
                }
            }

            result.Add(ToBuffer(canvas, Width, Height));
            if (frame.DisposeOp == 1 || (frame.DisposeOp == 2 && i == 0))
            {
                for (var y = frame.YOffset; y < frame.YOffset + frame.Height; y++)
                {
                    canvas.AsSpan(((y * Width) + frame.XOffset) * 4, frame.Width * 4).Clear();
                }
            }
            else if (before is not null)
            {
                canvas = before;
            }
        }

        return result;
    }

    /// <summary>Converts raw pixels of an 8- or 16-bit layout (gray, gray and alpha, RGB, RGBA) to 16-bit RGBA.</summary>
    /// <param name="pixels">The pixels.</param>
    /// <returns>The pixels, <see cref="RawPixelLayout.Rgba16Le"/>.</returns>
    public static RawPixelBuffer ToRgba16(RawPixelBuffer pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        var layout = pixels.Layout;
        var samples = new ushort[pixels.Width * pixels.Height * 4];
        for (var y = 0; y < pixels.Height; y++)
        {
            for (var x = 0; x < pixels.Width; x++)
            {
                var pixel = ((y * pixels.Width) + x) * 4;
                var scale = layout.BytesPerSample == 1 ? 257 : 1;
                int Sample(int channel) => pixels.GetSample(x, y, channel) * scale;
                var (r, g, b, a) = layout.ChannelCount switch
                {
                    1 => (Sample(0), Sample(0), Sample(0), ushort.MaxValue),
                    2 => (Sample(0), Sample(0), Sample(0), Sample(1)),
                    3 => (Sample(0), Sample(1), Sample(2), ushort.MaxValue),
                    _ => (Sample(0), Sample(1), Sample(2), Sample(3)),
                };

                (samples[pixel], samples[pixel + 1], samples[pixel + 2], samples[pixel + 3]) = ((ushort)r, (ushort)g, (ushort)b, (ushort)a);
            }
        }

        return ToBuffer(samples, pixels.Width, pixels.Height);
    }

    private static RawPixelBuffer ToBuffer(ushort[] samples, int width, int height)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), samples[i]);
        }

        return RawPixelBuffer.Create(width, height, RawPixelLayout.Rgba16Le, bytes);
    }

    private RawPixelBuffer DecodeCanonical(byte[] zlib, int width, int height)
    {
        var channels = ColorType switch
        {
            0 or 3 => 1,
            2 => 3,
            4 => 2,
            _ => 4,
        };

        var bitsPerPixel = channels * BitDepth;
        var filterDistance = Math.Max(1, bitsPerPixel / 8);
        var maximum = (1 << BitDepth) - 1;
        var key = GetColorKey();
        var inflated = Inflate(zlib);
        var samples = new ushort[width * height * 4];
        var offset = 0;
        var passes = InterlaceMethod == 0 ? [(0, 0, 1, 1)] : Adam7;
        var raw = new int[channels];
        foreach (var (x0, y0, dx, dy) in passes)
        {
            var passWidth = width > x0 ? ((width - x0 - 1) / dx) + 1 : 0;
            var passHeight = height > y0 ? ((height - y0 - 1) / dy) + 1 : 0;
            if (passWidth == 0 || passHeight == 0)
                continue;

            var rowLength = ((passWidth * bitsPerPixel) + 7) / 8;
            var previous = new byte[rowLength];
            for (var row = 0; row < passHeight; row++)
            {
                if (offset + 1 + rowLength > inflated.Length)
                    throw new InvalidDataException("The image data is too short.");

                var filter = inflated[offset];
                var current = inflated.AsSpan(offset + 1, rowLength).ToArray();
                offset += 1 + rowLength;
                Unfilter(filter, current, previous, filterDistance);
                for (var i = 0; i < passWidth; i++)
                {
                    for (var c = 0; c < channels; c++)
                    {
                        var index = (i * channels) + c;
                        raw[c] = BitDepth switch
                        {
                            16 => BinaryPrimitives.ReadUInt16BigEndian(current.AsSpan(index * 2)),
                            8 => current[index],
                            _ => (current[index * BitDepth / 8] >> (8 - BitDepth - (index * BitDepth % 8))) & maximum,
                        };
                    }

                    var target = ((((y0 + (row * dy)) * width) + x0 + (i * dx)) * 4);
                    WriteCanonical(raw, key, maximum, samples.AsSpan(target, 4));
                }

                previous = current;
            }
        }

        if (offset != inflated.Length)
            throw new InvalidDataException($"The image data has {inflated.Length - offset} extra bytes.");

        return ToBuffer(samples, width, height);
    }

    private void WriteCanonical(int[] raw, int[]? key, int maximum, Span<ushort> pixel)
    {
        int Scale(int sample) => (int)((long)sample * 65535 / maximum);
        switch (ColorType)
        {
            case 0:
                pixel[0] = pixel[1] = pixel[2] = (ushort)Scale(raw[0]);
                pixel[3] = key is not null && raw[0] == key[0] ? (ushort)0 : ushort.MaxValue;
                break;
            case 2:
                (pixel[0], pixel[1], pixel[2]) = ((ushort)Scale(raw[0]), (ushort)Scale(raw[1]), (ushort)Scale(raw[2]));
                pixel[3] = key is not null && raw[0] == key[0] && raw[1] == key[1] && raw[2] == key[2] ? (ushort)0 : ushort.MaxValue;
                break;
            case 3:
                var palette = Palette.Span;
                if (raw[0] >= palette.Length / 3)
                    throw new InvalidDataException($"The palette index {raw[0]} is outside the {palette.Length / 3} PLTE entries.");

                (pixel[0], pixel[1], pixel[2]) = ((ushort)(palette[raw[0] * 3] * 257), (ushort)(palette[(raw[0] * 3) + 1] * 257), (ushort)(palette[(raw[0] * 3) + 2] * 257));
                var transparency = Transparency.Span;
                pixel[3] = raw[0] < transparency.Length ? (ushort)(transparency[raw[0]] * 257) : ushort.MaxValue;
                break;
            case 4:
                pixel[0] = pixel[1] = pixel[2] = (ushort)Scale(raw[0]);
                pixel[3] = (ushort)Scale(raw[1]);
                break;
            default:
                (pixel[0], pixel[1], pixel[2], pixel[3]) = ((ushort)Scale(raw[0]), (ushort)Scale(raw[1]), (ushort)Scale(raw[2]), (ushort)Scale(raw[3]));
                break;
        }
    }

    /// <summary>Gets the <c>tRNS</c> color key of a grayscale or RGB image, validated, or <see langword="null"/>.</summary>
    private int[]? GetColorKey()
    {
        if (ColorType is not (0 or 2))
            return null;

        var chunks = Chunks.ToList();
        var transparencies = chunks.Where(chunk => chunk.Type == "tRNS").ToList();
        if (transparencies.Count == 0)
            return null;

        if (transparencies.Count > 1)
            throw new InvalidDataException("Duplicate tRNS.");

        if (chunks.IndexOf(transparencies[0]) > chunks.FindIndex(chunk => chunk.Type == "IDAT"))
            throw new InvalidDataException("tRNS must precede the first IDAT.");

        var data = transparencies[0].Data.Span;
        var count = ColorType == 0 ? 1 : 3;
        if (data.Length != 2 * count)
            throw new InvalidDataException($"A tRNS chunk of {data.Length} bytes is invalid for color type {ColorType}.");

        var key = new int[count];
        for (var i = 0; i < count; i++)
        {
            key[i] = BinaryPrimitives.ReadUInt16BigEndian(data[(i * 2)..]);
            if (key[i] > (1 << BitDepth) - 1)
                throw new InvalidDataException($"The tRNS sample {key[i]} does not fit {BitDepth} bits.");
        }

        return key;
    }
}
