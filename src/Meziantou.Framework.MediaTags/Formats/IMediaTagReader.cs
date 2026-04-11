namespace Meziantou.Framework.MediaTags.Formats;

internal interface IMediaTagReader
{
    MediaTagResult<MediaTagInfo> ReadTags(Stream stream);
}
