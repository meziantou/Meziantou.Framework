namespace Meziantou.Framework.Imaging;

/// <summary>Immutable options for <see cref="JpegXlTranscoder"/>.</summary>
public sealed class JpegXlTranscodeOptions
{
    /// <summary>Gets the default options.</summary>
    public static JpegXlTranscodeOptions Default { get; } = new();

    /// <summary>Gets the configuration (resource limits). Defaults to <see cref="ImageConfiguration.Default"/>.</summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public ImageConfiguration Configuration
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = ImageConfiguration.Default;

    /// <summary>
    /// Gets the compression effort of <see cref="JpegXlTranscoder.RecompressJpeg(Stream, Stream, JpegXlTranscodeOptions?)"/>,
    /// from 0 (fastest, largest output) to 9 (slowest, smallest output). Defaults to 5. The effort never changes the JPEG
    /// file that the output rebuilds.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 9.</exception>
    public int Effort
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 0);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 9);
            field = value;
        }
    } = 5;
}
