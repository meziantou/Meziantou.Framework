namespace Meziantou.Framework.MediaTags.Internals;

internal static class NativeFieldSelector
{
    /// <summary>
    /// Gets the native fields of <paramref name="tagType"/> to write, or <see langword="null"/> when the caller did not set
    /// <see cref="MediaTagInfo.NativeFields"/>, and the fields of the existing file must be kept.
    /// </summary>
    public static List<MediaNativeField>? GetFieldsToWrite(MediaTagInfo tags, MediaTagType tagType)
    {
        if (tags.NativeFields is not { } fields)
            return null;

        var result = new List<MediaNativeField>();
        foreach (var field in fields)
        {
            if (field.TagType == tagType)
                result.Add(field);
        }

        return result;
    }
}
