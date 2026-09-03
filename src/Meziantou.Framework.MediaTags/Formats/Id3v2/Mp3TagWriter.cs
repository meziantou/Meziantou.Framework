using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Id3v2;

internal sealed class Mp3TagWriter : IMediaTagWriter
{
    public MediaTagResult WriteTags(Stream inputStream, Stream outputStream, MediaTagInfo tags, MediaTagWriteOptions options)
    {
        try
        {
            inputStream.Position = 0;

            // Determine where the audio data starts (skip existing ID3v2 tag).
            if (!Id3v2Reader.TryGetTagSize(inputStream, out var existingTagSize))
                return MediaTagResult.Failure(MediaTagError.CorruptFile, "The ID3v2 tag declares a size that runs past the end of the file.");

            // Determine where the audio data ends (check for ID3v1 tag)
            var audioEnd = inputStream.Length;
            if (Id3v1.Id3v1Writer.HasId3v1Tag(inputStream))
            {
                audioEnd -= 128;
            }

            var audioLength = audioEnd - existingTagSize;
            if (audioLength < 0)
                return MediaTagResult.Failure(MediaTagError.CorruptFile, "The ID3v2 and ID3v1 tags overlap; the file contains no audio data.");

            // Build new ID3v2 tag
            if (!Id3v2Writer.TryBuildTag(tags, options.Id3v2PaddingSize, out var id3v2Tag, out var buildError))
                return MediaTagResult.Failure(MediaTagError.InvalidTagData, buildError);

            if (id3v2Tag.Length > 0)
                outputStream.Write(id3v2Tag);

            // Copy audio data. A short read here means the input ended early: the output would be a truncated
            // file, and it is about to replace the caller's, so fail instead of reporting success.
            inputStream.Position = existingTagSize;
            if (!StreamHelpers.CopyExactly(inputStream, outputStream, audioLength))
                return MediaTagResult.Failure(MediaTagError.UnexpectedEndOfStream, "The file ended before all audio data could be read.");

            // Write ID3v1 tag at end
            if (options.WriteId3v1Tag)
                outputStream.Write(Id3v1.Id3v1Writer.BuildTag(tags));

            return MediaTagResult.Success();
        }
        catch (Exception ex) when (MediaTagErrors.TryMap(ex, out var error))
        {
            return MediaTagResult.Failure(error, ex.Message);
        }
    }
}
