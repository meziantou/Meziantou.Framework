namespace Meziantou.Framework.Imaging;

/// <summary>
/// Defines a pixel type of the application that the library reads and writes through one of its built-in pixel formats,
/// its storage format.
/// </summary>
/// <typeparam name="TSelf">The pixel type itself.</typeparam>
/// <remarks>
/// <para>
/// An image always stores a built-in format (<see cref="StorageFormat"/>): every codec, kernel and conversion works on
/// it. A custom pixel type gives the conversions of whole rows between its pixels and the bytes of that format, and the
/// library calls them to import pixels (<see cref="Image.ImportCustomPixelData{TPixel}(ReadOnlySpan{TPixel}, int, int, int, ImageConfiguration?)"/>),
/// to copy the pixels of a frame of any format (<see cref="ImageFrame.CopyCustomPixelDataTo{TPixel}(Span{TPixel}, int, PixelConversionOptions?)"/>,
/// which converts them to the storage format first) and to edit the pixels of a frame of the storage format in place
/// (<see cref="ImageFrame.ProcessCustomPixelRows{TPixel}(CustomPixelRowAction{TPixel})"/>). <see cref="Image{TPixel}"/>,
/// the decoders and the sequential readers and writers only take the built-in pixel types.
/// </para>
/// <para>
/// The members are static, so the calls are resolved when the generic code is compiled: no reflection is involved, and the
/// type works with trimming and native AOT. The conversions must not keep the spans, and they are called with rows of
/// the same number of pixels: <c>width</c> pixels of the storage format, <c>width * PixelFormats.GetBytesPerPixel(StorageFormat)</c>
/// bytes in native endianness, laid out like the struct of the format (for example <see cref="Rgba64"/>).
/// </para>
/// </remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Imaging.Tests/PixelAccessTests.cs" ref:region="ICustomPixel_Bgr555" ref:dedent="true">
/// /// &lt;summary&gt;A pixel of 5-bit blue, green and red components packed in 16 bits (blue in the low bits), stored as &lt;see cref="Rgb24"/&gt;.&lt;/summary&gt;
/// private readonly record struct Bgr555(ushort Packed) : ICustomPixel&lt;Bgr555&gt;
/// {
///     public static PixelFormat StorageFormat =&gt; PixelFormat.Rgb24;
///
///     public int Blue =&gt; Packed &amp; 31;
///
///     public int Green =&gt; (Packed &gt;&gt; 5) &amp; 31;
///
///     public int Red =&gt; (Packed &gt;&gt; 10) &amp; 31;
///
///     public static Bgr555 FromColor(int red, int green, int blue) =&gt; new((ushort)((red &lt;&lt; 10) | (green &lt;&lt; 5) | blue));
///
///     public static void FromStorage(ReadOnlySpan&lt;byte&gt; source, Span&lt;Bgr555&gt; destination)
///     {
///         for (var i = 0; i &lt; destination.Length; i++)
///         {
///             // The nearest 5-bit component of each 8-bit sample
///             destination[i] = FromColor(((source[3 * i] * 31) + 127) / 255, ((source[(3 * i) + 1] * 31) + 127) / 255, ((source[(3 * i) + 2] * 31) + 127) / 255);
///         }
///     }
///
///     public static void ToStorage(ReadOnlySpan&lt;Bgr555&gt; source, Span&lt;byte&gt; destination)
///     {
///         for (var i = 0; i &lt; source.Length; i++)
///         {
///             destination[3 * i] = (byte)(((source[i].Red * 255) + 15) / 31);
///             destination[(3 * i) + 1] = (byte)(((source[i].Green * 255) + 15) / 31);
///             destination[(3 * i) + 2] = (byte)(((source[i].Blue * 255) + 15) / 31);
///         }
///     }
/// }
/// </code>
/// </example>
public interface ICustomPixel<TSelf>
    where TSelf : unmanaged, ICustomPixel<TSelf>
{
    /// <summary>Gets the built-in pixel format the pixels convert from and to.</summary>
    static abstract PixelFormat StorageFormat { get; }

    /// <summary>Converts a row of pixels of the storage format to pixels of this type.</summary>
    /// <param name="source">The bytes of the pixels of <see cref="StorageFormat"/>.</param>
    /// <param name="destination">The pixels, as many as <paramref name="source"/> holds.</param>
    static abstract void FromStorage(ReadOnlySpan<byte> source, Span<TSelf> destination);

    /// <summary>Converts a row of pixels of this type to pixels of the storage format.</summary>
    /// <param name="source">The pixels.</param>
    /// <param name="destination">The bytes of the pixels of <see cref="StorageFormat"/>, as many as <paramref name="source"/> holds.</param>
    static abstract void ToStorage(ReadOnlySpan<TSelf> source, Span<byte> destination);
}
