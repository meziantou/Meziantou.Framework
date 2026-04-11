using Meziantou.Framework.MediaTags.Formats;
using Meziantou.Framework.MediaTags.Formats.Aiff;
using Meziantou.Framework.MediaTags.Formats.Flac;
using Meziantou.Framework.MediaTags.Formats.Id3v1;
using Meziantou.Framework.MediaTags.Formats.Id3v2;
using Meziantou.Framework.MediaTags.Formats.Mp4;
using Meziantou.Framework.MediaTags.Formats.Ogg;
using Meziantou.Framework.MediaTags.Formats.Wav;
using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags;

/// <summary>
/// Provides static methods for reading and writing media file tags.
/// </summary>
public static class MediaFile
{
    /// <summary>
    /// Reads tags from the specified file.
    /// </summary>
    /// <param name="filePath">The path to the media file.</param>
    /// <returns>A result containing the parsed tags, or an error.</returns>
    public static MediaTagResult<MediaTagInfo> ReadTags(string filePath)
    {
        var format = DetectFormat(filePath);
        if (format is null)
            return MediaTagResult<MediaTagInfo>.Failure(MediaTagError.UnsupportedFormat, "Could not detect file format.");

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return ReadTagsCore(stream, format.Value);
        }
        catch (IOException ex)
        {
            return MediaTagResult<MediaTagInfo>.Failure(MediaTagError.IoError, ex.Message);
        }
    }

    /// <summary>
    /// Reads tags from the specified stream.
    /// </summary>
    /// <param name="stream">The stream containing the media file.</param>
    /// <param name="format">The format of the media file. If <see langword="null"/>, the format is auto-detected.</param>
    /// <returns>A result containing the parsed tags, or an error.</returns>
    public static MediaTagResult<MediaTagInfo> ReadTags(Stream stream, MediaFormat? format = null)
    {
        if (format is null)
        {
            format = DetectFormat(stream);
            if (format is null)
                return MediaTagResult<MediaTagInfo>.Failure(MediaTagError.UnsupportedFormat, "Could not detect file format from stream.");
        }

        return ReadTagsCore(stream, format.Value);
    }

    /// <summary>
    /// Writes tags to the specified file.
    /// </summary>
    /// <param name="filePath">The path to the media file.</param>
    /// <param name="tags">The tags to write.</param>
    /// <returns>A result indicating success or failure.</returns>
    public static MediaTagResult WriteTags(string filePath, MediaTagInfo tags)
    {
        var format = DetectFormat(filePath);
        if (format is null)
            return MediaTagResult.Failure(MediaTagError.UnsupportedFormat, "Could not detect file format.");

        try
        {
            var tempPath = filePath + ".tmp";
            try
            {
                using (var inputStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var outputStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                {
                    var result = WriteTagsCore(inputStream, outputStream, tags, format.Value);
                    if (!result.IsSuccess)
                    {
                        return result;
                    }
                }

                File.Delete(filePath);
                File.Move(tempPath, filePath);
                return MediaTagResult.Success();
            }
            catch
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { /* best effort cleanup */ }
                }
                throw;
            }
        }
        catch (IOException ex)
        {
            return MediaTagResult.Failure(MediaTagError.IoError, ex.Message);
        }
    }

    /// <summary>
    /// Writes tags from the input stream to the output stream.
    /// </summary>
    /// <param name="inputStream">The stream containing the original media file.</param>
    /// <param name="outputStream">The stream to write the modified media file to.</param>
    /// <param name="tags">The tags to write.</param>
    /// <param name="format">The format of the media file.</param>
    /// <returns>A result indicating success or failure.</returns>
    public static MediaTagResult WriteTags(Stream inputStream, Stream outputStream, MediaTagInfo tags, MediaFormat format)
    {
        return WriteTagsCore(inputStream, outputStream, tags, format);
    }

    /// <summary>
    /// Detects the media format from a file path using both magic bytes and file extension.
    /// </summary>
    /// <param name="filePath">The path to the media file.</param>
    /// <returns>The detected format, or <see langword="null"/> if not recognized.</returns>
    public static MediaFormat? DetectFormat(string filePath)
    {
        // Try magic bytes first
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var format = DetectFormat(stream);
            if (format is not null)
                return format;
        }
        catch (IOException)
        {
            // Fall through to extension-based detection
        }

        return FormatDetector.DetectFromExtension(filePath);
    }

    /// <summary>
    /// Detects the media format from stream content (magic bytes).
    /// The stream position is restored after detection.
    /// </summary>
    /// <param name="stream">The stream to detect the format from.</param>
    /// <returns>The detected format, or <see langword="null"/> if not recognized.</returns>
    public static MediaFormat? DetectFormat(Stream stream)
    {
        if (!stream.CanSeek)
            return null;

        var originalPosition = stream.Position;
        try
        {
            Span<byte> header = stackalloc byte[FormatDetector.MinHeaderSize];
            var bytesRead = stream.ReadAtLeast(header, FormatDetector.MinHeaderSize, throwOnEndOfStream: false);
            return FormatDetector.DetectFromHeader(header[..bytesRead]);
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    private static MediaTagResult<MediaTagInfo> ReadTagsCore(Stream stream, MediaFormat format)
    {
        var reader = GetReader(format);
        var result = reader.ReadTags(stream);
        if (result.IsSuccess)
        {
            result.Value.Format = format;
        }
        return result;
    }

    private static MediaTagResult WriteTagsCore(Stream inputStream, Stream outputStream, MediaTagInfo tags, MediaFormat format)
    {
        var writer = GetWriter(format);
        return writer.WriteTags(inputStream, outputStream, tags);
    }

    private static IMediaTagReader GetReader(MediaFormat format) => format switch
    {
        MediaFormat.Mp3 => new Mp3TagReader(),
        MediaFormat.OggVorbis => new OggVorbisReader(),
        MediaFormat.OggOpus => new OggOpusReader(),
        MediaFormat.Flac => new FlacReader(),
        MediaFormat.Mp4 => new Mp4Reader(),
        MediaFormat.Wav => new WavReader(),
        MediaFormat.Aiff => new AiffReader(),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported media format."),
    };

    private static IMediaTagWriter GetWriter(MediaFormat format) => format switch
    {
        MediaFormat.Mp3 => new Mp3TagWriter(),
        MediaFormat.OggVorbis => new OggVorbisWriter(),
        MediaFormat.OggOpus => new OggOpusWriter(),
        MediaFormat.Flac => new FlacWriter(),
        MediaFormat.Mp4 => new Mp4Writer(),
        MediaFormat.Wav => new WavWriter(),
        MediaFormat.Aiff => new AiffWriter(),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported media format."),
    };
}
