using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Converts 16-bit samples between their encoded byte order and the native-endian <see cref="ushort"/> storage of
/// <see cref="Rgba64"/> and <see cref="Gray16"/>. Raw pixel byte access always exposes native
/// endianness; codecs use these helpers for the encoded order (PNG is big-endian, raw corpus references little-endian).
/// </summary>
internal static class SampleEndianness
{
    /// <summary>Reads big-endian 16-bit samples into native-endian values.</summary>
    /// <param name="source">The encoded bytes (two per sample).</param>
    /// <param name="destination">The samples; must hold <c>source.Length / 2</c> values.</param>
    public static void ReadBigEndian(ReadOnlySpan<byte> source, Span<ushort> destination) => Read(source, destination, bigEndian: true);

    /// <summary>Reads little-endian 16-bit samples into native-endian values.</summary>
    /// <param name="source">The encoded bytes (two per sample).</param>
    /// <param name="destination">The samples; must hold <c>source.Length / 2</c> values.</param>
    public static void ReadLittleEndian(ReadOnlySpan<byte> source, Span<ushort> destination) => Read(source, destination, bigEndian: false);

    /// <summary>Writes native-endian 16-bit samples as big-endian bytes.</summary>
    /// <param name="source">The samples.</param>
    /// <param name="destination">The encoded bytes; must hold <c>source.Length * 2</c> bytes.</param>
    public static void WriteBigEndian(ReadOnlySpan<ushort> source, Span<byte> destination) => Write(source, destination, bigEndian: true);

    /// <summary>Writes native-endian 16-bit samples as little-endian bytes.</summary>
    /// <param name="source">The samples.</param>
    /// <param name="destination">The encoded bytes; must hold <c>source.Length * 2</c> bytes.</param>
    public static void WriteLittleEndian(ReadOnlySpan<ushort> source, Span<byte> destination) => Write(source, destination, bigEndian: false);

    /// <summary>Reads 16-bit pixels (<see cref="Rgba64"/> or <see cref="Gray16"/>) from encoded bytes in the specified byte order.</summary>
    /// <typeparam name="TPixel">The 16-bit pixel type.</typeparam>
    /// <param name="source">The encoded bytes.</param>
    /// <param name="destination">The pixels.</param>
    /// <param name="bigEndian"><see langword="true"/> for big-endian bytes, <see langword="false"/> for little-endian bytes.</param>
    /// <exception cref="NotSupportedException"><typeparamref name="TPixel"/> is not a 16-bit pixel struct.</exception>
    public static void ReadPixels<TPixel>(ReadOnlySpan<byte> source, Span<TPixel> destination, bool bigEndian)
        where TPixel : unmanaged
    {
        EnsureSixteenBit<TPixel>();
        Read(source, unsafe(MemoryMarshal.Cast<TPixel, ushort>(destination)), bigEndian);
    }

    /// <summary>Writes 16-bit pixels (<see cref="Rgba64"/> or <see cref="Gray16"/>) as encoded bytes in the specified byte order.</summary>
    /// <typeparam name="TPixel">The 16-bit pixel type.</typeparam>
    /// <param name="source">The pixels.</param>
    /// <param name="destination">The encoded bytes.</param>
    /// <param name="bigEndian"><see langword="true"/> for big-endian bytes, <see langword="false"/> for little-endian bytes.</param>
    /// <exception cref="NotSupportedException"><typeparamref name="TPixel"/> is not a 16-bit pixel struct.</exception>
    public static void WritePixels<TPixel>(ReadOnlySpan<TPixel> source, Span<byte> destination, bool bigEndian)
        where TPixel : unmanaged
    {
        EnsureSixteenBit<TPixel>();
        Write(unsafe(MemoryMarshal.Cast<TPixel, ushort>(source)), destination, bigEndian);
    }

    private static void EnsureSixteenBit<TPixel>()
        where TPixel : unmanaged
    {
        if (PixelFormats.GetBitsPerComponent(PixelFormats.GetPixelFormat<TPixel>()) != 16)
            throw new NotSupportedException($"Pixel type '{typeof(TPixel).Name}' does not store 16-bit samples.");
    }

    private static void Read(ReadOnlySpan<byte> source, Span<ushort> destination, bool bigEndian)
    {
        if (source.Length % 2 != 0)
            throw new ArgumentException("The source must contain a whole number of 16-bit samples.", nameof(source));

        var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(source));
        if (destination.Length < samples.Length)
            throw new ArgumentException("The destination is too short.", nameof(destination));

        destination = destination[..samples.Length];
        if (bigEndian == BitConverter.IsLittleEndian)
        {
            BinaryPrimitives.ReverseEndianness(samples, destination);
        }
        else
        {
            samples.CopyTo(destination);
        }
    }

    private static void Write(ReadOnlySpan<ushort> source, Span<byte> destination, bool bigEndian)
    {
        if (destination.Length / 2 < source.Length)
            throw new ArgumentException("The destination is too short.", nameof(destination));

        var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(destination))[..source.Length];
        if (bigEndian == BitConverter.IsLittleEndian)
        {
            BinaryPrimitives.ReverseEndianness(source, samples);
        }
        else
        {
            source.CopyTo(samples);
        }
    }
}
