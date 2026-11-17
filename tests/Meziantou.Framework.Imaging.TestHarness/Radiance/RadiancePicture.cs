using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.TestHarness.Radiance;

/// <summary>A Radiance HDR picture read by <see cref="ReferenceRadiance"/>.</summary>
/// <param name="Width">The size of the X axis.</param>
/// <param name="Height">The size of the Y axis.</param>
/// <param name="Rgbe">The RGBE pixels in the standard orientation: the rows from the top, left to right.</param>
/// <param name="HeaderLines">The lines of the information header after the first one.</param>
/// <param name="ScanOrder">The orientation of the resolution string, such as <c>-Y +X</c>.</param>
/// <param name="Encodings">The encoding of each scanline, in file order.</param>
public sealed record RadiancePicture(int Width, int Height, ReadOnlyMemory<byte> Rgbe, IReadOnlyList<string> HeaderLines, string ScanOrder, IReadOnlyList<RadianceScanlineEncoding> Encodings)
{
    /// <summary>
    /// Gets the colors of the pixels as the reference routines of Radiance read them, <c>(m + 0.5) × 2^(e - 136)</c> (0 for
    /// the exponent 0), as little-endian <c>rgbaf32le</c> samples with an alpha of 1.
    /// </summary>
    public byte[] GetFloatPixels(bool middle = true)
    {
        var count = Width * Height;
        var result = new byte[count * 16];
        for (var i = 0; i < count; i++)
        {
            for (var c = 0; c < 3; c++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan((i * 16) + (c * 4)), ReferenceRadiance.ToFloat(Rgbe.Span[(i * 4) + c], Rgbe.Span[(i * 4) + 3], middle));
            }

            BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan((i * 16) + 12), 1f);
        }

        return result;
    }

    /// <summary>Gets the value of a header variable (the last one), or <see langword="null"/>.</summary>
    public string? GetVariable(string name)
        => HeaderLines.LastOrDefault(line => line.StartsWith(name + "=", StringComparison.Ordinal)) is { } line ? line[(name.Length + 1)..] : null;
}
