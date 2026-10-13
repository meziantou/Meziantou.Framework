namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Creates the exceptions of the HEVC decoder: every bitstream error is reported for <see cref="ImageFormat.Heic"/>.</summary>
internal static class HevcErrors
{
    /// <summary>The bitstream violates Rec. ITU-T H.265.</summary>
    public static InvalidImageContentException Invalid(string message) => new("The HEVC bitstream is invalid: " + message, ImageFormat.Heic);

    /// <summary>The bitstream ends before the syntax element being read.</summary>
    public static InvalidImageContentException Truncated() => new("The HEVC bitstream is truncated.", ImageFormat.Heic);

    /// <summary>The bitstream is valid but uses a feature that the decoder does not implement.</summary>
    public static UnsupportedImageFeatureException Unsupported(string feature) => new($"The HEVC feature '{feature}' is not supported.", ImageFormat.Heic, feature);
}
