using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// Converts between JPEG files and JPEG XL files without decoding pixels: a JPEG XL file can hold the quantized
/// coefficients of a JPEG file, about a fifth smaller, with the data that rebuild the JPEG file byte for byte.
/// </summary>
/// <remarks>
/// <para>
/// These operations work on files, not on images: an <see cref="Image"/> holds pixels, and a JPEG file cannot be rebuilt
/// from pixels. <see cref="Image.Load(string, ImageDecodeOptions?)"/> reads the pixels of such a JPEG XL file like the ones
/// of any other.
/// </para>
/// <para>
/// Nothing is approximated: an input that cannot be converted exactly throws, and the destination is only written once the
/// whole output is known. A path destination is replaced once it is written.
/// </para>
/// </remarks>
public static class JpegXlTranscoder
{
    /// <summary>Tells whether a JPEG XL file holds the data that rebuild the JPEG file it was made from.</summary>
    /// <param name="jpegXl">The JPEG XL file.</param>
    /// <param name="options">The options, or <see langword="null"/> for the default ones.</param>
    /// <returns><see langword="true"/> when the data are present; they are not validated.</returns>
    /// <remarks>
    /// Encoders write the data before the image: the answer is then known from the start of the file, and nothing else
    /// is read. When no data precede an image that could be the one of a JPEG file, the rest of the file is read, without
    /// decoding it, since the data may follow the image.
    /// </remarks>
    /// <exception cref="UnknownImageFormatException">The format of the input is not recognized.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The input is not a JPEG XL file.</exception>
    /// <exception cref="InvalidImageContentException">The part of the file that is read is malformed or truncated.</exception>
    public static bool HasJpegReconstructionData(ReadOnlySpan<byte> jpegXl, JpegXlTranscodeOptions? options = null)
        => JpegXlTranscodeIO.HasJpegReconstructionData(jpegXl, options);

    /// <inheritdoc cref="HasJpegReconstructionData(ReadOnlySpan{byte}, JpegXlTranscodeOptions?)"/>
    /// <exception cref="ArgumentNullException"><paramref name="jpegXl"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="jpegXl"/> is not readable.</exception>
    public static bool HasJpegReconstructionData(Stream jpegXl, JpegXlTranscodeOptions? options = null)
    {
        Image.ValidateReadableStream(jpegXl);
        return JpegXlTranscodeIO.HasJpegReconstructionData(jpegXl, ownsStream: false, options);
    }

    /// <summary>Tells whether a JPEG XL file holds the data that rebuild the JPEG file it was made from.</summary>
    /// <param name="jpegXlPath">The path of the JPEG XL file.</param>
    /// <param name="options">The options, or <see langword="null"/> for the default ones.</param>
    /// <returns><see langword="true"/> when the data are present; they are not validated.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="jpegXlPath"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="jpegXlPath"/> is empty.</exception>
    /// <exception cref="UnknownImageFormatException">The format of the input is not recognized.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The input is not a JPEG XL file.</exception>
    /// <exception cref="InvalidImageContentException">The part of the file that is read is malformed or truncated.</exception>
    public static bool HasJpegReconstructionData(string jpegXlPath, JpegXlTranscodeOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(jpegXlPath);
        return JpegXlTranscodeIO.HasJpegReconstructionData(ImageIO.OpenFile(jpegXlPath, asynchronous: false), ownsStream: true, options);
    }

    /// <summary>Asynchronously tells whether a JPEG XL file holds the data that rebuild the JPEG file it was made from.</summary>
    /// <param name="jpegXl">The JPEG XL file.</param>
    /// <param name="options">The options, or <see langword="null"/> for the default ones.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns><see langword="true"/> when the data are present; they are not validated.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="jpegXl"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="jpegXl"/> is not readable.</exception>
    public static Task<bool> HasJpegReconstructionDataAsync(Stream jpegXl, JpegXlTranscodeOptions? options = null, CancellationToken cancellationToken = default)
    {
        Image.ValidateReadableStream(jpegXl);
        return JpegXlTranscodeIO.HasJpegReconstructionDataAsync(jpegXl, ownsStream: false, options, cancellationToken);
    }

    /// <summary>
    /// Recompresses a JPEG file as a JPEG XL file that holds its quantized coefficients and the data that rebuild it:
    /// <see cref="ReconstructJpeg(ReadOnlySpan{byte}, Stream, JpegXlTranscodeOptions?)"/> gives the JPEG file back, byte for
    /// byte. No pixel is decoded, and nothing is lost.
    /// </summary>
    /// <param name="jpeg">The JPEG file.</param>
    /// <param name="destination">Receives the JPEG XL file, once it is whole.</param>
    /// <param name="options">The options, or <see langword="null"/> for the default ones.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is not writable.</exception>
    /// <exception cref="UnknownImageFormatException">The format of the input is not recognized.</exception>
    /// <exception cref="UnsupportedImageFeatureException">
    /// The input is not a JPEG file, or it is a JPEG file that a JPEG XL frame cannot hold: four components, sampling factors
    /// other than 1 or 2, arithmetic coding, 12-bit samples, or entropy-coded data that could not be rebuilt byte for byte.
    /// </exception>
    /// <exception cref="InvalidImageContentException">The file is malformed or truncated.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    public static void RecompressJpeg(ReadOnlySpan<byte> jpeg, Stream destination, JpegXlTranscodeOptions? options = null)
    {
        Image.ValidateWritableStream(destination);
        JpegXlTranscodeIO.RecompressJpeg(jpeg, destination, destinationPath: null, options);
    }

    /// <inheritdoc cref="RecompressJpeg(ReadOnlySpan{byte}, Stream, JpegXlTranscodeOptions?)"/>
    /// <exception cref="ArgumentNullException"><paramref name="jpeg"/> or <paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="jpeg"/> is not readable, or <paramref name="destination"/> is not writable.</exception>
    public static void RecompressJpeg(Stream jpeg, Stream destination, JpegXlTranscodeOptions? options = null)
    {
        Image.ValidateReadableStream(jpeg);
        Image.ValidateWritableStream(destination);
        JpegXlTranscodeIO.RecompressJpeg(jpeg, ownsStream: false, destination, destinationPath: null, options);
    }

    /// <summary>Recompresses a JPEG file as a JPEG XL file that holds its quantized coefficients and the data that rebuild it.</summary>
    /// <param name="jpegPath">The path of the JPEG file.</param>
    /// <param name="destinationPath">The path of the JPEG XL file, which is created or replaced once it is whole.</param>
    /// <param name="options">The options, or <see langword="null"/> for the default ones.</param>
    /// <exception cref="ArgumentNullException"><paramref name="jpegPath"/> or <paramref name="destinationPath"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="jpegPath"/> or <paramref name="destinationPath"/> is empty.</exception>
    /// <exception cref="UnknownImageFormatException">The format of the input is not recognized.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The input is not a JPEG file, or it is a JPEG file that a JPEG XL frame cannot hold.</exception>
    /// <exception cref="InvalidImageContentException">The file is malformed or truncated.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    public static void RecompressJpeg(string jpegPath, string destinationPath, JpegXlTranscodeOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(jpegPath);
        ArgumentException.ThrowIfNullOrEmpty(destinationPath);
        JpegXlTranscodeIO.RecompressJpeg(ImageIO.OpenFile(jpegPath, asynchronous: false), ownsStream: true, destination: null, destinationPath, options);
    }

    /// <summary>Asynchronously recompresses a JPEG file as a JPEG XL file that holds its quantized coefficients and the data that rebuild it.</summary>
    /// <param name="jpeg">The JPEG file.</param>
    /// <param name="destination">Receives the JPEG XL file, once it is whole.</param>
    /// <param name="options">The options, or <see langword="null"/> for the default ones.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the file is written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="jpeg"/> or <paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="jpeg"/> is not readable, or <paramref name="destination"/> is not writable.</exception>
    public static Task RecompressJpegAsync(Stream jpeg, Stream destination, JpegXlTranscodeOptions? options = null, CancellationToken cancellationToken = default)
    {
        Image.ValidateReadableStream(jpeg);
        Image.ValidateWritableStream(destination);
        return JpegXlTranscodeIO.RecompressJpegAsync(jpeg, ownsStream: false, destination, destinationPath: null, options, cancellationToken);
    }

    /// <summary>Asynchronously recompresses a JPEG file as a JPEG XL file that holds its quantized coefficients and the data that rebuild it.</summary>
    /// <param name="jpegPath">The path of the JPEG file.</param>
    /// <param name="destinationPath">The path of the JPEG XL file, which is created or replaced once it is whole.</param>
    /// <param name="options">The options, or <see langword="null"/> for the default ones.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the file is written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="jpegPath"/> or <paramref name="destinationPath"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="jpegPath"/> or <paramref name="destinationPath"/> is empty.</exception>
    public static Task RecompressJpegAsync(string jpegPath, string destinationPath, JpegXlTranscodeOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(jpegPath);
        ArgumentException.ThrowIfNullOrEmpty(destinationPath);
        cancellationToken.ThrowIfCancellationRequested();
        return JpegXlTranscodeIO.RecompressJpegAsync(ImageIO.OpenFile(jpegPath, asynchronous: true), ownsStream: true, destination: null, destinationPath, options, cancellationToken);
    }

    /// <summary>Rebuilds the JPEG file a JPEG XL file was made from, byte for byte.</summary>
    /// <param name="jpegXl">The JPEG XL file.</param>
    /// <param name="destination">Receives the JPEG file, once it is whole.</param>
    /// <param name="options">The options, or <see langword="null"/> for the default ones.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is not writable.</exception>
    /// <exception cref="UnknownImageFormatException">The format of the input is not recognized.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The input is not a JPEG XL file, or it does not hold the data that rebuild a JPEG file (<see cref="TryReconstructJpeg(ReadOnlySpan{byte}, Stream, JpegXlTranscodeOptions?)"/> returns <see langword="false"/> instead).</exception>
    /// <exception cref="InvalidImageContentException">The file is malformed or truncated, or its reconstruction data do not match its image.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    public static void ReconstructJpeg(ReadOnlySpan<byte> jpegXl, Stream destination, JpegXlTranscodeOptions? options = null)
    {
        Image.ValidateWritableStream(destination);
        JpegXlTranscodeIO.ReconstructJpeg(jpegXl, destination, options, throwIfMissing: true);
    }

    /// <inheritdoc cref="ReconstructJpeg(ReadOnlySpan{byte}, Stream, JpegXlTranscodeOptions?)"/>
    /// <exception cref="ArgumentNullException"><paramref name="jpegXl"/> or <paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="jpegXl"/> is not readable, or <paramref name="destination"/> is not writable.</exception>
    public static void ReconstructJpeg(Stream jpegXl, Stream destination, JpegXlTranscodeOptions? options = null)
    {
        Image.ValidateReadableStream(jpegXl);
        Image.ValidateWritableStream(destination);
        JpegXlTranscodeIO.ReconstructJpeg(jpegXl, ownsStream: false, destination, destinationPath: null, options, throwIfMissing: true);
    }

    /// <summary>Rebuilds the JPEG file a JPEG XL file was made from, byte for byte.</summary>
    /// <param name="jpegXlPath">The path of the JPEG XL file.</param>
    /// <param name="destinationPath">The path of the JPEG file, which is created or replaced once it is whole.</param>
    /// <param name="options">The options, or <see langword="null"/> for the default ones.</param>
    /// <exception cref="ArgumentNullException"><paramref name="jpegXlPath"/> or <paramref name="destinationPath"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="jpegXlPath"/> or <paramref name="destinationPath"/> is empty.</exception>
    /// <exception cref="UnknownImageFormatException">The format of the input is not recognized.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The input is not a JPEG XL file, or it does not hold the data that rebuild a JPEG file.</exception>
    /// <exception cref="InvalidImageContentException">The file is malformed or truncated, or its reconstruction data do not match its image.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    public static void ReconstructJpeg(string jpegXlPath, string destinationPath, JpegXlTranscodeOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(jpegXlPath);
        ArgumentException.ThrowIfNullOrEmpty(destinationPath);
        JpegXlTranscodeIO.ReconstructJpeg(ImageIO.OpenFile(jpegXlPath, asynchronous: false), ownsStream: true, destination: null, destinationPath, options, throwIfMissing: true);
    }

    /// <summary>Asynchronously rebuilds the JPEG file a JPEG XL file was made from, byte for byte.</summary>
    /// <param name="jpegXl">The JPEG XL file.</param>
    /// <param name="destination">Receives the JPEG file, once it is whole.</param>
    /// <param name="options">The options, or <see langword="null"/> for the default ones.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the file is written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="jpegXl"/> or <paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="jpegXl"/> is not readable, or <paramref name="destination"/> is not writable.</exception>
    public static Task ReconstructJpegAsync(Stream jpegXl, Stream destination, JpegXlTranscodeOptions? options = null, CancellationToken cancellationToken = default)
    {
        Image.ValidateReadableStream(jpegXl);
        Image.ValidateWritableStream(destination);
        return JpegXlTranscodeIO.ReconstructJpegAsync(jpegXl, ownsStream: false, destination, destinationPath: null, options, throwIfMissing: true, cancellationToken);
    }

    /// <summary>Asynchronously rebuilds the JPEG file a JPEG XL file was made from, byte for byte.</summary>
    /// <param name="jpegXlPath">The path of the JPEG XL file.</param>
    /// <param name="destinationPath">The path of the JPEG file, which is created or replaced once it is whole.</param>
    /// <param name="options">The options, or <see langword="null"/> for the default ones.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the file is written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="jpegXlPath"/> or <paramref name="destinationPath"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="jpegXlPath"/> or <paramref name="destinationPath"/> is empty.</exception>
    public static Task ReconstructJpegAsync(string jpegXlPath, string destinationPath, JpegXlTranscodeOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(jpegXlPath);
        ArgumentException.ThrowIfNullOrEmpty(destinationPath);
        cancellationToken.ThrowIfCancellationRequested();
        return JpegXlTranscodeIO.ReconstructJpegAsync(ImageIO.OpenFile(jpegXlPath, asynchronous: true), ownsStream: true, destination: null, destinationPath, options, throwIfMissing: true, cancellationToken);
    }

    /// <summary>Rebuilds the JPEG file a JPEG XL file was made from, byte for byte, when the file holds the data that rebuild it.</summary>
    /// <param name="jpegXl">The JPEG XL file.</param>
    /// <param name="destination">Receives the JPEG file, once it is whole.</param>
    /// <param name="options">The options, or <see langword="null"/> for the default ones.</param>
    /// <returns><see langword="false"/> when the file does not hold the data that rebuild a JPEG file: nothing is written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is not writable.</exception>
    /// <exception cref="UnknownImageFormatException">The format of the input is not recognized.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The input is not a JPEG XL file.</exception>
    /// <exception cref="InvalidImageContentException">The file is malformed or truncated, or its reconstruction data do not match its image.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    public static bool TryReconstructJpeg(ReadOnlySpan<byte> jpegXl, Stream destination, JpegXlTranscodeOptions? options = null)
    {
        Image.ValidateWritableStream(destination);
        return JpegXlTranscodeIO.ReconstructJpeg(jpegXl, destination, options, throwIfMissing: false);
    }

    /// <inheritdoc cref="TryReconstructJpeg(ReadOnlySpan{byte}, Stream, JpegXlTranscodeOptions?)"/>
    /// <exception cref="ArgumentNullException"><paramref name="jpegXl"/> or <paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="jpegXl"/> is not readable, or <paramref name="destination"/> is not writable.</exception>
    public static bool TryReconstructJpeg(Stream jpegXl, Stream destination, JpegXlTranscodeOptions? options = null)
    {
        Image.ValidateReadableStream(jpegXl);
        Image.ValidateWritableStream(destination);
        return JpegXlTranscodeIO.ReconstructJpeg(jpegXl, ownsStream: false, destination, destinationPath: null, options, throwIfMissing: false);
    }

    /// <summary>Rebuilds the JPEG file a JPEG XL file was made from, byte for byte, when the file holds the data that rebuild it.</summary>
    /// <param name="jpegXlPath">The path of the JPEG XL file.</param>
    /// <param name="destinationPath">The path of the JPEG file, which is created or replaced once it is whole.</param>
    /// <param name="options">The options, or <see langword="null"/> for the default ones.</param>
    /// <returns><see langword="false"/> when the file does not hold the data that rebuild a JPEG file: nothing is written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="jpegXlPath"/> or <paramref name="destinationPath"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="jpegXlPath"/> or <paramref name="destinationPath"/> is empty.</exception>
    /// <exception cref="UnknownImageFormatException">The format of the input is not recognized.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The input is not a JPEG XL file.</exception>
    /// <exception cref="InvalidImageContentException">The file is malformed or truncated, or its reconstruction data do not match its image.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    public static bool TryReconstructJpeg(string jpegXlPath, string destinationPath, JpegXlTranscodeOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(jpegXlPath);
        ArgumentException.ThrowIfNullOrEmpty(destinationPath);
        return JpegXlTranscodeIO.ReconstructJpeg(ImageIO.OpenFile(jpegXlPath, asynchronous: false), ownsStream: true, destination: null, destinationPath, options, throwIfMissing: false);
    }

    /// <summary>Asynchronously rebuilds the JPEG file a JPEG XL file was made from, byte for byte, when the file holds the data that rebuild it.</summary>
    /// <param name="jpegXl">The JPEG XL file.</param>
    /// <param name="destination">Receives the JPEG file, once it is whole.</param>
    /// <param name="options">The options, or <see langword="null"/> for the default ones.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns><see langword="false"/> when the file does not hold the data that rebuild a JPEG file: nothing is written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="jpegXl"/> or <paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="jpegXl"/> is not readable, or <paramref name="destination"/> is not writable.</exception>
    public static Task<bool> TryReconstructJpegAsync(Stream jpegXl, Stream destination, JpegXlTranscodeOptions? options = null, CancellationToken cancellationToken = default)
    {
        Image.ValidateReadableStream(jpegXl);
        Image.ValidateWritableStream(destination);
        return JpegXlTranscodeIO.ReconstructJpegAsync(jpegXl, ownsStream: false, destination, destinationPath: null, options, throwIfMissing: false, cancellationToken);
    }
}
