namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The clean aperture of ISO/IEC 14496-12, section 12.1.4 (the <c>clap</c> box): the part of an image that is shown,
/// given by its size and by the offset of its center from the center of the image, as fractions. It is an item property
/// of a still image and a box of the visual sample entry of an image sequence.
/// </summary>
/// <remarks>
/// Only the apertures that select whole samples are supported, which is what MIAF requires of a file (ISO/IEC 23000-22,
/// section 7.3.6.7): the width, the height and the position of the top-left sample are integers.
/// </remarks>
internal static class HeifCleanAperture
{
    /// <summary>Reads a <c>clap</c> box.</summary>
    /// <param name="reader">The payload of the box.</param>
    /// <param name="image">The size of the image that the aperture is a part of.</param>
    /// <returns>The aperture, relative to the top-left corner of the image.</returns>
    /// <exception cref="InvalidImageContentException">A denominator or a size is zero, or the aperture is not inside the image.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The aperture does not select whole samples.</exception>
    public static Rectangle Read(ref HeifBoxReader reader, Size image)
    {
        var binding = reader.Binding;
        long widthN = reader.ReadUInt32();
        long widthD = reader.ReadUInt32();
        long heightN = reader.ReadUInt32();
        long heightD = reader.ReadUInt32();
        long horizontalOffsetN = reader.ReadInt32();
        long horizontalOffsetD = reader.ReadUInt32();
        long verticalOffsetN = reader.ReadInt32();
        long verticalOffsetD = reader.ReadUInt32();
        if (widthD is 0 || heightD is 0 || horizontalOffsetD is 0 || verticalOffsetD is 0 || widthN is 0 || heightN is 0)
            throw binding.Invalid("the clean aperture is not valid.");

        if (widthN % widthD is not 0 || heightN % heightD is not 0)
            throw binding.Unsupported($"The clean aperture of the {binding.Name} image is not a whole number of samples.", "fractional clean aperture");

        var width = widthN / widthD;
        var height = heightN / heightD;

        // The center of the aperture is at (size - 1) / 2 + offset: left = (size - width) / 2 + offset
        var left2N = ((image.Width - width) * horizontalOffsetD) + (2 * horizontalOffsetN);
        var top2N = ((image.Height - height) * verticalOffsetD) + (2 * verticalOffsetN);
        if (left2N % (2 * horizontalOffsetD) is not 0 || top2N % (2 * verticalOffsetD) is not 0)
            throw binding.Unsupported($"The clean aperture of the {binding.Name} image is not a whole number of samples.", "fractional clean aperture");

        var left = left2N / (2 * horizontalOffsetD);
        var top = top2N / (2 * verticalOffsetD);
        if (left < 0 || top < 0 || width > image.Width - left || height > image.Height - top)
            throw binding.Invalid("the clean aperture is outside the image.");

        return new Rectangle((int)left, (int)top, (int)width, (int)height);
    }

    /// <summary>Writes a <c>clap</c> box that selects a rectangle of whole samples.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="image">The size of the image.</param>
    /// <param name="aperture">The aperture, inside the image.</param>
    public static void Write(HeifBoxWriter writer, Size image, Rectangle aperture) => Write(writer, image, aperture, reduce: true);

    /// <summary>
    /// Writes the <c>clap</c> box of an image that is the top-left part of coded pictures that are larger than it, which
    /// a codec that pads its pictures needs: the offsets are the halves of the difference of the sizes, as they are.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="codedSize">The size of the coded pictures.</param>
    /// <param name="width">The width of the image.</param>
    /// <param name="height">The height of the image.</param>
    public static void WritePadding(HeifBoxWriter writer, Size codedSize, int width, int height) => Write(writer, codedSize, new Rectangle(0, 0, width, height), reduce: false);

    private static void Write(HeifBoxWriter writer, Size image, Rectangle aperture, bool reduce)
    {
        // The offset of the center of the aperture from the center of the image: left + (width - size) / 2, which is a
        // whole number or a number of halves
        var horizontalOffset2 = (2L * aperture.X) + aperture.Width - image.Width;
        var verticalOffset2 = (2L * aperture.Y) + aperture.Height - image.Height;
        var box = writer.BeginBox(HeifFormat.Clap);
        writer.WriteUInt32((uint)aperture.Width);
        writer.WriteUInt32(1);
        writer.WriteUInt32((uint)aperture.Height);
        writer.WriteUInt32(1);
        WriteHalves(writer, horizontalOffset2, reduce);
        WriteHalves(writer, verticalOffset2, reduce);
        writer.EndBox(box);

        static void WriteHalves(HeifBoxWriter writer, long halves, bool reduce)
        {
            var isWhole = reduce && halves % 2 is 0;
            writer.WriteUInt32(unchecked((uint)(int)(isWhole ? halves / 2 : halves)));
            writer.WriteUInt32(isWhole ? 1u : 2u);
        }
    }
}
