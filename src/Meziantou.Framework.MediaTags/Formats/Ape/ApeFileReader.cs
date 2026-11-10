using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Ape;

/// <summary>
/// Reads the tags of a Monkey's Audio, WavPack or Musepack file: an APE tag at the end of the file, and an ID3v1 tag that
/// older taggers wrote, whose values are only used for the fields the APE tag does not set.
/// </summary>
internal sealed class ApeFileReader(MediaFormat format) : IMediaTagReader
{
    public MediaTagResult<MediaTagInfo> ReadTags(Stream stream)
    {
        try
        {
            var header = ApeAudioHeaders.ReadHeader(stream);
            if (!ApeAudioHeaders.HasSignature(header, format))
                return MediaTagResult<MediaTagInfo>.Failure(MediaTagError.CorruptFile, $"The file is not a {format} file.");

            // The duration only comes from the audio, which the writer copies unchanged: bytes of a tag must not change it.
            var audioEnd = ApeTag.FindAudioEnd(stream, ApeAudioHeaders.SignatureLength, stream.Length, out _, out _);
            var tags = new MediaTagInfo
            {
                Duration = ApeAudioHeaders.GetDuration(header.AsSpan(0, (int)Math.Min(header.Length, audioEnd)), format),
                NativeFields = [],
            };

            if (!TryReadEndTags(stream, ApeAudioHeaders.SignatureLength, tags, readNativeFields: true, out var error))
                return MediaTagResult<MediaTagInfo>.Failure(MediaTagError.CorruptFile, error);

            return MediaTagResult<MediaTagInfo>.Success(tags);
        }
        catch (Exception ex) when (MediaTagErrors.TryMap(ex, out var error))
        {
            return MediaTagResult<MediaTagInfo>.Failure(error, ex.Message);
        }
    }

    /// <summary>
    /// Reads the last APE tag and the last ID3v1 tag at the end of a file into <paramref name="tags"/>, filling the fields that
    /// are not set yet. The APE tag takes precedence over the ID3v1 tag, which stores less.
    /// </summary>
    /// <param name="stream">The stream.</param>
    /// <param name="audioStart">The first byte the tags cannot cover.</param>
    /// <param name="tags">The tags to fill.</param>
    /// <param name="readNativeFields">Whether the items that no property represents are added to the native fields of <paramref name="tags"/>.</param>
    /// <param name="error">Why the tag cannot be read.</param>
    /// <returns><see langword="false"/> when the APE tag is too large to read. The ID3v1 tag is read anyway.</returns>
    public static bool TryReadEndTags(Stream stream, long audioStart, MediaTagInfo tags, bool readNativeFields, out string? error)
    {
        error = null;
        ApeTag.FindAudioEnd(stream, audioStart, stream.Length, out var apeTag, out var id3v1Tag);
        if (apeTag is not null)
        {
            if (!ApeTag.TryReadItems(stream, apeTag.Value, out var items))
            {
                error = "The APE tag is too large to read.";
                items = [];
            }

            var nativeFields = readNativeFields ? new List<MediaNativeField>() : null;
            ApeTagFields.Apply(items, tags, nativeFields);
            if (nativeFields is not null)
                tags.NativeFields = [.. tags.NativeFields ?? [], .. nativeFields];
        }

        if (id3v1Tag is { } id3v1Offset)
        {
            var buffer = new byte[ApeTag.Id3v1TagSize];
            stream.Position = id3v1Offset;
            if (stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) == buffer.Length)
                Id3v1.Id3v1Reader.TryParseTag(buffer, tags);
        }

        // A custom or native item named like a field that is set, such as an unparsable "Track" when the ID3v1 tag has a
        // track number, is not written: the field wins. It is not read either, so the tags read are the ones a write keeps.
        if (readNativeFields && ApeTagFields.TryBuildItems(tags, [], out var propertyItems, out _, propertiesOnly: true))
        {
            var propertyKeys = new HashSet<string>(propertyItems.Select(item => item.Key), StringComparer.OrdinalIgnoreCase);
            if (tags.NativeFields is { Count: > 0 } fields)
                tags.NativeFields = [.. fields.Where(field => field.TagType != MediaTagType.Ape || !propertyKeys.Contains(field.Id))];

            foreach (var key in tags.CustomFields.Keys.Where(propertyKeys.Contains).ToList())
            {
                tags.CustomFields.Remove(key);
            }
        }

        return error is null;
    }
}
