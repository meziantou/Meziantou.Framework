namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The constants of the Netpbm portable anymap family (PBM, PGM, PPM and PAM) shared by the decoder and the encoder.
/// </summary>
internal static class PnmFormat
{
    /// <summary>The largest <c>MAXVAL</c> the formats allow.</summary>
    public const int MaxSampleValue = 65535;

    /// <summary>
    /// The largest number of header bytes (magic, tokens, comments and, for PAM, lines up to <c>ENDHDR</c>) read before the
    /// raster. A longer header is rejected, so adversarial whitespace and comments cannot make the header walk unbounded.
    /// </summary>
    public const int MaxHeaderLength = 64 * 1024;

    /// <summary>The largest number of bytes of one PAM header line.</summary>
    public const int MaxHeaderLineLength = 1024;

    /// <summary>The number of header tokens parsed between two cancellation checks.</summary>
    public const int PlainCancellationInterval = 64 * 1024;

    /// <summary>Determines whether a byte is Netpbm white space (space, tab, line feed, carriage return, vertical tab or form feed).</summary>
    public static bool IsWhiteSpace(byte value) => value is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or 0x0B or 0x0C;

    /// <summary>Creates the exception reported for malformed PNM data.</summary>
    public static InvalidImageContentException Invalid(string message) => new(message, ImageFormat.Pnm);

    /// <summary>Creates the exception reported for a recognized but unsupported PNM variant.</summary>
    public static UnsupportedImageFeatureException Unsupported(string message, string feature) => new(message, ImageFormat.Pnm, feature);
}
