namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Brings the colors of the paints of a drawing to the color space of the image that is drawn on.</summary>
/// <remarks>
/// A color is the 16-bit red, green and blue values of the profile of the paints, as fractions of the largest one.
/// The conversion gives the device values of the image, stored as 16-bit samples with the rounding of every ICC
/// conversion (<see cref="IccUInt16Sample.Store"/>); a grayscale image has one, which is the red, the green and the
/// blue of the result, so that the gray level any conversion of the pixel formats makes of it is that value. The
/// alpha is not a color: it is kept.
/// </remarks>
internal static class PaintColorConverter
{
    /// <summary>Converts a color.</summary>
    /// <param name="pipeline">The conversion from the profile of the paints to the color space of the image, or <see langword="null"/> when the paints are colors of the image.</param>
    /// <param name="color">The color, with straight alpha.</param>
    public static Rgba64 Convert(IccPipeline? pipeline, Rgba64 color)
    {
        if (pipeline is null)
            return color;

        Span<double> values = stackalloc double[IccPipeline.MaxChannels];
        values[0] = IccUInt16Sample.Load(color.R);
        values[1] = IccUInt16Sample.Load(color.G);
        values[2] = IccUInt16Sample.Load(color.B);
        values[3] = 0;
        pipeline.Apply(values);
        if (pipeline.DestinationChannelCount == 1)
        {
            var gray = IccUInt16Sample.Store(values[0]);
            return new Rgba64(gray, gray, gray, color.A);
        }

        return new Rgba64(IccUInt16Sample.Store(values[0]), IccUInt16Sample.Store(values[1]), IccUInt16Sample.Store(values[2]), color.A);
    }
}
