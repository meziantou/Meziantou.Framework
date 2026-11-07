namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Receives the samples of a JPEG 2000 codestream that is the compressed data of another format (a strip or a tile of a
/// TIFF page): the samples of the components as the codestream holds them, interleaved, without any interpretation of
/// what the components are.
/// </summary>
internal abstract class Jpeg2000SampleReceiver
{
    /// <summary>Called once the main header is read, before any sample is decoded: the receiver checks that the codestream is the one it expects.</summary>
    /// <param name="width">The width of the image area.</param>
    /// <param name="height">The height of the image area.</param>
    /// <param name="components">The components: unsigned samples of at most 16 bits.</param>
    public abstract void OnImage(int width, int height, ReadOnlySpan<Jpeg2000ComponentSize> components);

    /// <summary>Called for each row of each tile, in the order the tiles are completed.</summary>
    /// <param name="x">The column of the first pixel.</param>
    /// <param name="y">The row.</param>
    /// <param name="samples">
    /// The samples of consecutive pixels, one per component: bytes when every component has at most 8 bits, 16-bit values
    /// in the byte order of the machine otherwise.
    /// </param>
    public abstract void OnRow(int x, int y, ReadOnlySpan<byte> samples);
}
