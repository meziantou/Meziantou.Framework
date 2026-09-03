using Meziantou.Framework.MediaTags.Formats;
using Meziantou.Framework.MediaTags.Formats.Aiff;
using Meziantou.Framework.MediaTags.Formats.Flac;
using Meziantou.Framework.MediaTags.Formats.Id3v2;
using Meziantou.Framework.MediaTags.Formats.Mp4;
using Meziantou.Framework.MediaTags.Formats.Ogg;
using Meziantou.Framework.MediaTags.Formats.Wav;
using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags;

/// <summary>
/// Provides static methods for reading and writing media file tags.
/// </summary>
/// <remarks>
/// <para>
/// Writing <b>replaces</b> the tags of a file: the written <see cref="MediaTagInfo"/> becomes the whole tag,
/// and any field left <see langword="null"/> is removed from the file rather than left at its current value.
/// To change one field, read the tags first, modify the returned object and write it back.
/// </para>
/// <para>
/// These methods report problems with the file through the returned <see cref="MediaTagResult"/> instead of
/// throwing. They still throw for invalid arguments.
/// </para>
/// </remarks>
public static class MediaFile
{
    /// <summary>The number of random names tried before giving up on creating the temporary file.</summary>
    private const int MaxTemporaryFileAttempts = 8;

    /// <summary>
    /// Reads tags from the specified file.
    /// </summary>
    /// <param name="filePath">The path to the media file.</param>
    /// <returns>A result containing the parsed tags, or an error.</returns>
    /// <exception cref="ArgumentException"><paramref name="filePath"/> is <see langword="null"/> or empty.</exception>
    public static MediaTagResult<MediaTagInfo> ReadTags(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        try
        {
            // Detecting from the same handle avoids opening the file a second time, which matters when a
            // caller walks a whole library.
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var format = DetectFormat(stream) ?? FormatDetector.DetectFromExtension(filePath);
            if (format is null)
                return MediaTagResult<MediaTagInfo>.Failure(MediaTagError.UnsupportedFormat, "Could not detect file format.");

            return ReadTagsCore(stream, format.Value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return MediaTagResult<MediaTagInfo>.Failure(MediaTagError.IoError, ex.Message);
        }
    }

    /// <summary>
    /// Reads tags from the specified stream, starting at its current position.
    /// </summary>
    /// <param name="stream">The seekable stream containing the media file.</param>
    /// <param name="format">The format of the media file. If <see langword="null"/>, the format is auto-detected.</param>
    /// <returns>A result containing the parsed tags, or an error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    public static MediaTagResult<MediaTagInfo> ReadTags(Stream stream, MediaFormat? format = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
            return MediaTagResult<MediaTagInfo>.Failure(MediaTagError.IoError, "The stream must support seeking.");

        if (format is null)
        {
            format = DetectFormat(stream);
            if (format is null)
                return MediaTagResult<MediaTagInfo>.Failure(MediaTagError.UnsupportedFormat, "Could not detect file format from stream.");
        }

        return ReadTagsCore(stream, format.Value);
    }

    /// <summary>
    /// Replaces the tags of the specified file.
    /// </summary>
    /// <param name="filePath">The path to the media file.</param>
    /// <param name="tags">The tags to write. They replace the existing tags: fields left <see langword="null"/> are removed from the file.</param>
    /// <returns>A result indicating success or failure. The file is left untouched when the operation fails.</returns>
    /// <exception cref="ArgumentException"><paramref name="filePath"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="tags"/> is <see langword="null"/>.</exception>
    public static MediaTagResult WriteTags(string filePath, MediaTagInfo tags) => WriteTags(filePath, tags, MediaTagWriteOptions.Default);

    /// <summary>
    /// Replaces the tags of the specified file, using the specified options.
    /// </summary>
    /// <param name="filePath">The path to the media file.</param>
    /// <param name="tags">The tags to write. They replace the existing tags: fields left <see langword="null"/> are removed from the file.</param>
    /// <param name="options">Controls how the tags are written.</param>
    /// <returns>A result indicating success or failure. The file is left untouched when the operation fails.</returns>
    /// <exception cref="ArgumentException"><paramref name="filePath"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="tags"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    public static MediaTagResult WriteTags(string filePath, MediaTagInfo tags, MediaTagWriteOptions options)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(options);

        return WriteFile(filePath, tags, options);
    }

    /// <summary>
    /// Removes every tag this library can write from the specified file.
    /// </summary>
    /// <param name="filePath">The path to the media file.</param>
    /// <returns>A result indicating success or failure. The file is left untouched when the operation fails.</returns>
    /// <exception cref="ArgumentException"><paramref name="filePath"/> is <see langword="null"/> or empty.</exception>
    public static MediaTagResult RemoveTags(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        return WriteFile(filePath, new MediaTagInfo(), MediaTagWriteOptions.Remove);
    }

    /// <summary>
    /// Writes the input stream to the output stream, replacing its tags.
    /// </summary>
    /// <param name="inputStream">The seekable stream containing the original media file, read from its current position.</param>
    /// <param name="outputStream">The stream to write the modified media file to.</param>
    /// <param name="tags">The tags to write. They replace the existing tags: fields left <see langword="null"/> are removed.</param>
    /// <param name="format">The format of the media file.</param>
    /// <returns>
    /// A result indicating success or failure. On failure the content already written to
    /// <paramref name="outputStream"/> must be discarded: it is not a valid media file.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputStream"/>, <paramref name="outputStream"/> or <paramref name="tags"/> is <see langword="null"/>.</exception>
    public static MediaTagResult WriteTags(Stream inputStream, Stream outputStream, MediaTagInfo tags, MediaFormat format)
    {
        return WriteTags(inputStream, outputStream, tags, format, MediaTagWriteOptions.Default);
    }

    /// <summary>
    /// Writes the input stream to the output stream, replacing its tags, using the specified options.
    /// </summary>
    /// <param name="inputStream">The seekable stream containing the original media file, read from its current position.</param>
    /// <param name="outputStream">The stream to write the modified media file to.</param>
    /// <param name="tags">The tags to write. They replace the existing tags: fields left <see langword="null"/> are removed.</param>
    /// <param name="format">The format of the media file.</param>
    /// <param name="options">Controls how the tags are written.</param>
    /// <returns>
    /// A result indicating success or failure. On failure the content already written to
    /// <paramref name="outputStream"/> must be discarded: it is not a valid media file.
    /// </returns>
    /// <exception cref="ArgumentNullException">One of the arguments is <see langword="null"/>.</exception>
    public static MediaTagResult WriteTags(Stream inputStream, Stream outputStream, MediaTagInfo tags, MediaFormat format, MediaTagWriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(inputStream);
        ArgumentNullException.ThrowIfNull(outputStream);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(options);

        if (!inputStream.CanSeek)
            return MediaTagResult.Failure(MediaTagError.IoError, "The input stream must support seeking.");

        return WriteTagsCore(inputStream, outputStream, tags, format, options);
    }

    /// <summary>
    /// Removes every tag this library can write, writing the result to the output stream.
    /// </summary>
    /// <param name="inputStream">The seekable stream containing the original media file, read from its current position.</param>
    /// <param name="outputStream">The stream to write the modified media file to.</param>
    /// <param name="format">The format of the media file.</param>
    /// <returns>A result indicating success or failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputStream"/> or <paramref name="outputStream"/> is <see langword="null"/>.</exception>
    public static MediaTagResult RemoveTags(Stream inputStream, Stream outputStream, MediaFormat format)
    {
        return WriteTags(inputStream, outputStream, new MediaTagInfo(), format, MediaTagWriteOptions.Remove);
    }

    /// <summary>
    /// Detects the media format from a file path using both magic bytes and file extension.
    /// </summary>
    /// <param name="filePath">The path to the media file.</param>
    /// <returns>The detected format, or <see langword="null"/> if not recognized.</returns>
    /// <exception cref="ArgumentException"><paramref name="filePath"/> is <see langword="null"/> or empty.</exception>
    public static MediaFormat? DetectFormat(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        // Try magic bytes first
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var format = DetectFormat(stream);
            if (format is not null)
                return format;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    public static MediaFormat? DetectFormat(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
            return null;

        var originalPosition = stream.Position;
        try
        {
            var format = DetectFormatFromCurrentPosition(stream);
            if (format == MediaFormat.Mp3 && stream.Length >= originalPosition + 10)
            {
                if (Id3v2TagLocator.TryGetAudioDataOffsets(stream, originalPosition, out var primaryOffset, out var secondaryOffset)
                    && (TryDetectNestedFormatAtOffset(stream, primaryOffset, out var nestedFormat)
                        || (secondaryOffset >= 0 && TryDetectNestedFormatAtOffset(stream, secondaryOffset, out nestedFormat))))
                {
                    return nestedFormat;
                }
            }

            return format;
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    private static MediaTagResult WriteFile(string filePath, MediaTagInfo tags, MediaTagWriteOptions options)
    {
        try
        {
            // Write through a symbolic link instead of replacing the link with a regular file
            var targetPath = ResolveLinkTarget(filePath);
            string? tempPath = null;
            try
            {
                using (var inputStream = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var format = DetectFormat(inputStream) ?? FormatDetector.DetectFromExtension(filePath);
                    if (format is null)
                        return MediaTagResult.Failure(MediaTagError.UnsupportedFormat, "Could not detect file format.");

                    using var outputStream = CreateTemporaryFile(targetPath, out tempPath);
                    inputStream.Position = 0;
                    var result = WriteTagsCore(inputStream, outputStream, tags, format.Value, options);
                    if (!result.IsSuccess)
                        return result;

                    // Closing the stream only flushes to the operating system. Without this a machine-level
                    // crash can make the rename durable while the new content is not, and the original file
                    // the rename replaced is already gone.
                    outputStream.Flush(flushToDisk: true);
                }

                CopyFilePermissions(targetPath, tempPath);

                // Moving with overwrite replaces the file in one step. Deleting the original first
                // leaves no file at all if the process stops between the two operations.
                File.Move(tempPath, targetPath, overwrite: true);

                // The temporary file is now the user's file: it must not be deleted below.
                tempPath = null;
                return MediaTagResult.Success();
            }
            finally
            {
                // Runs for a failed result as well as for an exception.
                if (tempPath is not null)
                    DeleteIfExists(tempPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return MediaTagResult.Failure(MediaTagError.IoError, ex.Message);
        }
    }

    /// <summary>
    /// Creates the file the new content is written to, next to the file being replaced.
    /// </summary>
    /// <remarks>
    /// The name is random and the file is created with <see cref="FileMode.CreateNew"/>: a predictable name can
    /// be pre-created by anyone who can write to the directory, as a symbolic link that the write then follows,
    /// or as an unrelated file that the write then destroys. The Unix mode is applied at creation so the
    /// content is never briefly visible to other users.
    /// </remarks>
    private static FileStream CreateTemporaryFile(string targetPath, out string tempPath)
    {
        var directory = Path.GetDirectoryName(targetPath);
        var prefix = Path.GetFileName(targetPath);

        for (var attempt = 0; attempt < MaxTemporaryFileAttempts; attempt++)
        {
            var candidate = prefix + "." + Path.GetRandomFileName() + ".tmp";
            var candidatePath = directory is { Length: > 0 } ? Path.Combine(directory, candidate) : candidate;

            try
            {
                var stream = OperatingSystem.IsWindows()
                    ? new FileStream(candidatePath, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                    : new FileStream(candidatePath, new FileStreamOptions
                    {
                        Mode = FileMode.CreateNew,
                        Access = FileAccess.Write,
                        Share = FileShare.None,
                        UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
                    });

                tempPath = candidatePath;
                return stream;
            }
            catch (IOException) when (File.Exists(candidatePath))
            {
                // The name is already taken; try another one.
            }
        }

        throw new IOException($"Could not create a temporary file next to '{targetPath}'.");
    }

    private static string ResolveLinkTarget(string filePath)
    {
        try
        {
            return File.ResolveLinkTarget(filePath, returnFinalTarget: true)?.FullName ?? filePath;
        }
        catch (IOException)
        {
            return filePath;
        }
    }

    private static void CopyFilePermissions(string sourcePath, string destinationPath)
    {
        if (OperatingSystem.IsWindows())
            return;

        // The temporary file was created with the most restrictive mode; give it the original's mode now.
        File.SetUnixFileMode(destinationPath, File.GetUnixFileMode(sourcePath));
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort cleanup
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup
        }
    }

    private static MediaFormat? DetectFormatFromCurrentPosition(Stream stream)
    {
        Span<byte> header = stackalloc byte[FormatDetector.MinHeaderSize];
        var bytesRead = stream.ReadAtLeast(header, FormatDetector.MinHeaderSize, throwOnEndOfStream: false);
        return FormatDetector.DetectFromHeader(header[..bytesRead]);
    }

    private static bool TryDetectNestedFormatAtOffset(Stream stream, long offset, out MediaFormat nestedFormat)
    {
        nestedFormat = default;

        if (offset < 0 || stream.Length < offset + 4)
            return false;

        stream.Position = offset;
        var detectedFormat = DetectFormatFromCurrentPosition(stream);
        if (detectedFormat is not null and not MediaFormat.Mp3)
        {
            nestedFormat = detectedFormat.Value;
            return true;
        }

        return false;
    }

    private static MediaTagResult<MediaTagInfo> ReadTagsCore(Stream stream, MediaFormat format)
    {
        if (!TryGetReader(format, out var reader))
            return MediaTagResult<MediaTagInfo>.Failure(MediaTagError.UnsupportedFormat, FormatOutOfRangeMessage(format));

        MediaTagResult<MediaTagInfo> result;
        if (stream.Position == 0)
        {
            result = reader.ReadTags(stream);
        }
        else
        {
            // The parsers address the file from offset 0, so the stream is shifted to show them the same bytes
            // format detection saw.
            using var offsetStream = new OffsetStream(stream, stream.Position);
            result = reader.ReadTags(offsetStream);
        }

        if (result.IsSuccess)
        {
            result.Value.Format = format;
        }

        return result;
    }

    private static MediaTagResult WriteTagsCore(Stream inputStream, Stream outputStream, MediaTagInfo tags, MediaFormat format, MediaTagWriteOptions options)
    {
        if (!TryGetWriter(format, out var writer))
            return MediaTagResult.Failure(MediaTagError.UnsupportedFormat, FormatOutOfRangeMessage(format));

        if (inputStream.Position == 0)
            return writer.WriteTags(inputStream, outputStream, tags, options);

        using var offsetStream = new OffsetStream(inputStream, inputStream.Position);
        return writer.WriteTags(offsetStream, outputStream, tags, options);
    }

    private static string FormatOutOfRangeMessage(MediaFormat format)
        => $"Unsupported media format: {(int)format}.";

    private static bool TryGetReader(MediaFormat format, [NotNullWhen(true)] out IMediaTagReader? reader)
    {
        reader = format switch
        {
            MediaFormat.Mp3 => new Mp3TagReader(),
            MediaFormat.OggVorbis => new OggVorbisReader(),
            MediaFormat.OggOpus => new OggOpusReader(),
            MediaFormat.Flac => new FlacReader(),
            MediaFormat.Mp4 => new Mp4Reader(),
            MediaFormat.Wav => new WavReader(),
            MediaFormat.Aiff => new AiffReader(),
            _ => null,
        };

        return reader is not null;
    }

    private static bool TryGetWriter(MediaFormat format, [NotNullWhen(true)] out IMediaTagWriter? writer)
    {
        writer = format switch
        {
            MediaFormat.Mp3 => new Mp3TagWriter(),
            MediaFormat.OggVorbis => new OggVorbisWriter(),
            MediaFormat.OggOpus => new OggOpusWriter(),
            MediaFormat.Flac => new FlacWriter(),
            MediaFormat.Mp4 => new Mp4Writer(),
            MediaFormat.Wav => new WavWriter(),
            MediaFormat.Aiff => new AiffWriter(),
            _ => null,
        };

        return writer is not null;
    }
}
