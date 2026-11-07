namespace Meziantou.Framework.MediaTags.Internals;

internal static class MediaTagErrors
{
    /// <summary>
    /// Maps an exception to the <see cref="MediaTagError"/> that describes it.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> for exception types that indicate a defect in this library rather than a problem
    /// with the file. Those must propagate: reporting them as <see cref="MediaTagError.CorruptFile"/> tells the
    /// caller their file is at fault and makes library bugs impossible to distinguish from bad input.
    /// </returns>
    public static bool TryMap(Exception exception, out MediaTagError error)
    {
        switch (exception)
        {
            // EndOfStreamException derives from IOException, so it must be matched first.
            case EndOfStreamException:
                error = MediaTagError.UnexpectedEndOfStream;
                return true;

            case DecoderFallbackException or EncoderFallbackException:
                error = MediaTagError.EncodingError;
                return true;

            case InvalidDataException:
                error = MediaTagError.CorruptFile;
                return true;

            case IOException or UnauthorizedAccessException or NotSupportedException or ObjectDisposedException:
                error = MediaTagError.IoError;
                return true;

            // Thrown when a value read from the file reaches a bounds or conversion check.
            case ArgumentOutOfRangeException or IndexOutOfRangeException or OverflowException or FormatException:
                error = MediaTagError.CorruptFile;
                return true;

            default:
                error = default;
                return false;
        }
    }
}
