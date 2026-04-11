namespace Meziantou.Framework.MediaTags.Formats;

internal interface IMediaTagWriter
{
    MediaTagResult WriteTags(Stream inputStream, Stream outputStream, MediaTagInfo tags);
}
