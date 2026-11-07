using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Id3v2;

internal sealed class Mp3TagWriter : IMediaTagWriter
{
    public MediaTagResult WriteTags(Stream inputStream, Stream outputStream, MediaTagInfo tags, MediaTagWriteOptions options)
    {
        try
        {
            if (!Mp3AudioLocator.TryLocate(inputStream, out var audioStart, out var audioEnd, out var locateError))
                return MediaTagResult.Failure(MediaTagError.CorruptFile, locateError);

            // Build new ID3v2 tag
            inputStream.Position = 0;
            var preservedFrames = Id3v2Writer.ReadFramesToPreserve(inputStream, options);
            if (!Id3v2Writer.TryBuildTag(tags, options.Id3v2PaddingSize, preservedFrames, out var id3v2Tag, out var buildError))
                return MediaTagResult.Failure(MediaTagError.InvalidTagData, buildError);

            if (id3v2Tag.Length > 0)
                outputStream.Write(id3v2Tag);

            // Copy audio data. A short read here means the input ended early: the output would be a truncated
            // file, and it is about to replace the caller's, so fail instead of reporting success.
            inputStream.Position = audioStart;
            if (!StreamHelpers.CopyExactly(inputStream, outputStream, audioEnd - audioStart))
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
