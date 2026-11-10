using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Ape;

/// <summary>
/// Writes the tags of a Monkey's Audio, WavPack or Musepack file: the audio, then an APEv2 tag.
/// </summary>
/// <remarks>
/// The existing APE and ID3v1 tags at the end of the file are all removed. The values of an ID3v1 tag are read into
/// <see cref="MediaTagInfo"/>, so a read-modify-write moves them to the APE tag instead of losing them.
/// </remarks>
internal sealed class ApeFileWriter(MediaFormat format) : IMediaTagWriter
{
    public MediaTagResult WriteTags(Stream inputStream, Stream outputStream, MediaTagInfo tags, MediaTagWriteOptions options)
    {
        try
        {
            var header = ApeAudioHeaders.ReadHeader(inputStream);
            if (!ApeAudioHeaders.HasSignature(header, format))
                return MediaTagResult.Failure(MediaTagError.CorruptFile, $"The file is not a {format} file.");

            var audioEnd = ApeTag.FindAudioEnd(inputStream, ApeAudioHeaders.SignatureLength, inputStream.Length, out var existingTag, out _);
            if (!TryGetNativeFields(inputStream, tags, options, existingTag, out var nativeFields, out var readError))
                return MediaTagResult.Failure(MediaTagError.CorruptFile, readError);

            if (!TryBuildTag(tags, nativeFields, out var tag, out var buildError))
                return MediaTagResult.Failure(MediaTagError.InvalidTagData, buildError);

            if (!StreamHelpers.CopyExactlyFrom(inputStream, outputStream, 0, audioEnd))
                return MediaTagResult.Failure(MediaTagError.UnexpectedEndOfStream, "The file ended before all audio data could be read.");

            outputStream.Write(tag);
            return MediaTagResult.Success();
        }
        catch (Exception ex) when (MediaTagErrors.TryMap(ex, out var error))
        {
            return MediaTagResult.Failure(error, ex.Message);
        }
    }

    /// <summary>Builds an APEv2 tag, or an empty array when there is nothing to write.</summary>
    public static bool TryBuildTag(MediaTagInfo tags, IReadOnlyList<MediaNativeField> nativeFields, [NotNullWhen(true)] out byte[]? tag, out string? error)
    {
        tag = null;
        if (!ApeTagFields.TryBuildItems(tags, nativeFields, out var items, out error))
            return false;

        if (items.Count == 0)
        {
            tag = [];
            return true;
        }

        var size = 0L;
        foreach (var item in items)
        {
            size += 8 + item.Key.Length + 1 + item.Value.Length;
        }

        if (size + (2 * ApeTag.HeaderSize) > ApeTag.MaxTagSize)
        {
            error = $"The tags are too large for an APE tag ({size} bytes, the maximum is {ApeTag.MaxTagSize}).";
            return false;
        }

        tag = ApeTag.Build(items);
        return true;
    }

    private static bool TryGetNativeFields(Stream stream, MediaTagInfo tags, MediaTagWriteOptions options, ApeTagLocation? existingTag, out IReadOnlyList<MediaNativeField> nativeFields, out string? error)
    {
        error = null;
        nativeFields = [];
        if (!options.PreserveUnrecognizedFrames)
            return true;

        if (NativeFieldSelector.GetFieldsToWrite(tags, MediaTagType.Ape) is { } fields)
        {
            nativeFields = fields;
            return true;
        }

        if (existingTag is null)
            return true;

        if (!ApeTag.TryReadItems(stream, existingTag.Value, out var items))
        {
            error = "The existing APE tag is too large to read.";
            return false;
        }

        var existing = new List<MediaNativeField>();
        ApeTagFields.Apply(items, new MediaTagInfo(), existing);
        nativeFields = existing;
        return true;
    }
}
