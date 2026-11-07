namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Creates the exceptions of the AV1 decoder: every bitstream error is reported for <see cref="ImageFormat.Avif"/>.</summary>
internal static class Av1Errors
{
    /// <summary>The bitstream violates the AV1 specification.</summary>
    public static InvalidImageContentException Invalid(string message) => new("The AV1 bitstream is invalid: " + message, ImageFormat.Avif);

    /// <summary>The bitstream ends before the syntax element being read.</summary>
    public static InvalidImageContentException Truncated() => new("The AV1 bitstream is truncated.", ImageFormat.Avif);

    /// <summary>The bitstream is valid but uses a feature that the decoder does not implement.</summary>
    public static UnsupportedImageFeatureException Unsupported(string feature) => new($"The AV1 feature '{feature}' is not supported.", ImageFormat.Avif, feature);
}
