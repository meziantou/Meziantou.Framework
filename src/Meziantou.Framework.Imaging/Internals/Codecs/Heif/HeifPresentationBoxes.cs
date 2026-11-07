using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The boxes of ISO/IEC 14496-12 that describe how the samples of an image are presented without changing how they are
/// decoded: the pixel aspect ratio (<c>pasp</c>, section 12.1.4), the content light level (<c>clli</c>, section 12.1.6)
/// and the mastering display color volume (<c>mdcv</c>, section 12.1.7). They are item properties of a still image and
/// boxes of the visual sample entry of an image sequence, with the same content.
/// </summary>
internal static class HeifPresentationBoxes
{
    /// <summary>Reads a <c>pasp</c> box.</summary>
    /// <exception cref="InvalidImageContentException">A term of the ratio is zero, or the box is too short.</exception>
    public static PixelAspectRatio ReadPixelAspectRatio(ref HeifBoxReader reader)
    {
        var binding = reader.Binding;
        var horizontalSpacing = reader.ReadUInt32();
        var verticalSpacing = reader.ReadUInt32();
        if (horizontalSpacing is 0 || verticalSpacing is 0)
            throw binding.Invalid("the pixel aspect ratio is not valid.");

        return new PixelAspectRatio(horizontalSpacing, verticalSpacing);
    }

    /// <summary>Reads a <c>clli</c> box.</summary>
    /// <exception cref="InvalidImageContentException">The box is too short.</exception>
    public static ContentLightLevel ReadContentLightLevel(ref HeifBoxReader reader)
    {
        var maxContentLightLevel = reader.ReadUInt16();
        var maxPictureAverageLightLevel = reader.ReadUInt16();
        return new ContentLightLevel(maxContentLightLevel, maxPictureAverageLightLevel);
    }

    /// <summary>Reads a <c>mdcv</c> box.</summary>
    /// <remarks>
    /// The fields have the semantics of the mastering display colour volume SEI message of Rec. ITU-T H.265 (section
    /// D.3.28), which stores the primaries of a display with red, green and blue primaries in the order green, blue, red.
    /// </remarks>
    /// <exception cref="InvalidImageContentException">The box is too short.</exception>
    public static MasteringDisplayColorVolume ReadMasteringDisplay(ref HeifBoxReader reader)
    {
        var greenX = reader.ReadUInt16();
        var greenY = reader.ReadUInt16();
        var blueX = reader.ReadUInt16();
        var blueY = reader.ReadUInt16();
        var redX = reader.ReadUInt16();
        var redY = reader.ReadUInt16();
        var whitePointX = reader.ReadUInt16();
        var whitePointY = reader.ReadUInt16();
        var maxLuminance = reader.ReadUInt32();
        var minLuminance = reader.ReadUInt32();
        return new MasteringDisplayColorVolume(redX, redY, greenX, greenY, blueX, blueY, whitePointX, whitePointY, maxLuminance, minLuminance);
    }

    /// <summary>Writes a <c>pasp</c> box.</summary>
    public static void Write(HeifBoxWriter writer, PixelAspectRatio ratio)
    {
        var box = writer.BeginBox(HeifFormat.Pasp);
        writer.WriteUInt32(ratio.HorizontalSpacing);
        writer.WriteUInt32(ratio.VerticalSpacing);
        writer.EndBox(box);
    }

    /// <summary>Writes a <c>clli</c> box.</summary>
    public static void Write(HeifBoxWriter writer, ContentLightLevel level)
    {
        var box = writer.BeginBox(HeifFormat.Clli);
        writer.WriteUInt16(level.MaxContentLightLevel);
        writer.WriteUInt16(level.MaxPictureAverageLightLevel);
        writer.EndBox(box);
    }

    /// <summary>Writes a <c>mdcv</c> box: the primaries in the order green, blue, red (see <see cref="ReadMasteringDisplay"/>).</summary>
    public static void Write(HeifBoxWriter writer, MasteringDisplayColorVolume display)
    {
        var box = writer.BeginBox(HeifFormat.Mdcv);
        writer.WriteUInt16(display.GreenX);
        writer.WriteUInt16(display.GreenY);
        writer.WriteUInt16(display.BlueX);
        writer.WriteUInt16(display.BlueY);
        writer.WriteUInt16(display.RedX);
        writer.WriteUInt16(display.RedY);
        writer.WriteUInt16(display.WhitePointX);
        writer.WriteUInt16(display.WhitePointY);
        writer.WriteUInt32(display.MaxLuminance);
        writer.WriteUInt32(display.MinLuminance);
        writer.EndBox(box);
    }
}
