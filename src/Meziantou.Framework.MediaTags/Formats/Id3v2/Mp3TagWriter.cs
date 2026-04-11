namespace Meziantou.Framework.MediaTags.Formats.Id3v2;

internal sealed class Mp3TagWriter : IMediaTagWriter
{
    public MediaTagResult WriteTags(Stream inputStream, Stream outputStream, MediaTagInfo tags)
    {
        try
        {
            inputStream.Position = 0;

            // Determine where the audio data starts (skip existing ID3v2 tag)
            var existingTagSize = Id3v2Reader.GetTagSize(inputStream);
            inputStream.Position = existingTagSize;

            // Determine where the audio data ends (check for ID3v1 tag)
            var audioEnd = inputStream.Length;
            if (Id3v1.Id3v1Writer.HasId3v1Tag(inputStream))
            {
                audioEnd -= 128;
            }
            inputStream.Position = existingTagSize;

            // Build new ID3v2 tag
            var id3v2Tag = Id3v2Writer.BuildTag(tags);
            outputStream.Write(id3v2Tag);

            // Copy audio data
            var audioLength = audioEnd - existingTagSize;
            if (audioLength > 0)
            {
                inputStream.Position = existingTagSize;
                CopyBytes(inputStream, outputStream, audioLength);
            }

            // Write ID3v1 tag at end
            var id3v1Tag = Id3v1.Id3v1Writer.BuildTag(tags);
            outputStream.Write(id3v1Tag);

            return MediaTagResult.Success();
        }
        catch (Exception ex)
        {
            return MediaTagResult.Failure(MediaTagError.IoError, ex.Message);
        }
    }

    private static void CopyBytes(Stream source, Stream destination, long count)
    {
        var buffer = new byte[8192];
        while (count > 0)
        {
            var toRead = (int)Math.Min(count, buffer.Length);
            var read = source.Read(buffer, 0, toRead);
            if (read == 0)
                break;
            destination.Write(buffer, 0, read);
            count -= read;
        }
    }
}
