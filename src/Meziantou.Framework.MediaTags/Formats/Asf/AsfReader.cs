using System.Buffers.Binary;
using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Asf;

internal sealed class AsfReader : IMediaTagReader
{
    public MediaTagResult<MediaTagInfo> ReadTags(Stream stream)
    {
        try
        {
            if (!AsfHeader.TryRead(stream, out var header, out _, out var error))
                return MediaTagResult<MediaTagInfo>.Failure(MediaTagError.CorruptFile, error);

            // Reading is best effort: the objects parsed before a malformed one still carry usable tags.
            var tags = new MediaTagInfo { NativeFields = [] };
            var attributes = new List<AsfAttribute>();
            string? rating = null;
            foreach (var child in header.Children)
            {
                if (child.Id == AsfGuids.ContentDescription)
                {
                    var values = AsfAttributes.ReadContentDescription(child.Data.Span);
                    AddContentDescription(attributes, AsfFields.Title, values[AsfAttributes.TitleIndex]);
                    AddContentDescription(attributes, AsfFields.Author, values[AsfAttributes.AuthorIndex]);
                    AddContentDescription(attributes, AsfFields.Copyright, values[AsfAttributes.CopyrightIndex]);
                    AddContentDescription(attributes, AsfFields.Description, values[AsfAttributes.DescriptionIndex]);
                    rating ??= values[AsfAttributes.RatingIndex] is { Length: > 0 } value ? value : null;
                }
                else if (child.Id == AsfGuids.ExtendedContentDescription)
                {
                    attributes.AddRange(AsfAttributes.ReadExtendedContentDescription(child.Data));
                }
                else if (child.Id == AsfGuids.HeaderExtension)
                {
                    foreach (var extension in GetHeaderExtensionObjects(child))
                    {
                        if (extension.Id == AsfGuids.Metadata || extension.Id == AsfGuids.MetadataLibrary)
                        {
                            _ = AsfAttributes.TryReadMetadata(extension.Data, out var records);
                            attributes.AddRange(records);
                        }
                    }
                }
                else if (child.Id == AsfGuids.FileProperties)
                {
                    tags.Duration ??= ReadDuration(child.Data.Span);
                }
                else if (child.Id == AsfGuids.StreamProperties && tags.VideoWidth is null)
                {
                    ReadVideoSize(child.Data.Span, tags);
                }
            }

            var nativeFields = new List<MediaNativeField>();
            AsfFields.Apply(attributes, tags, nativeFields);
            tags.NativeFields = nativeFields;

            // The rating of the content description has no property. It is a custom field, which the writer writes as an
            // attribute of the same name.
            if (rating is not null)
                tags.CustomFields.TryAdd(AsfFields.Rating, rating);

            // A custom field named like an attribute a property is written to, such as an unparsable WM/TrackNumber next to
            // a valid one, is not written: the property wins. It is not read either, so the tags read are the ones a write keeps.
            var propertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _ = AsfFields.TryBuildAttributes(tags, [], out _, out _, out _, out _, propertyNames);
            foreach (var key in tags.CustomFields.Keys.Where(propertyNames.Contains).ToList())
            {
                tags.CustomFields.Remove(key);
            }

            return MediaTagResult<MediaTagInfo>.Success(tags);
        }
        catch (Exception ex) when (MediaTagErrors.TryMap(ex, out var error))
        {
            return MediaTagResult<MediaTagInfo>.Failure(error, ex.Message);
        }
    }

    /// <summary>Gets the objects of a header extension object, up to the first malformed one.</summary>
    internal static List<AsfObject> GetHeaderExtensionObjects(AsfObject headerExtension)
    {
        if (!TryGetHeaderExtensionData(headerExtension, out var data))
            return [];

        _ = AsfObject.TryParseList(data, out var objects);
        return objects;
    }

    /// <summary>Gets the objects of a header extension object: what follows a reserved GUID, a reserved 16-bit value, and the 32-bit size of the objects.</summary>
    internal static bool TryGetHeaderExtensionData(AsfObject headerExtension, out ReadOnlyMemory<byte> data)
    {
        data = default;
        var span = headerExtension.Data.Span;
        if (span.Length < 22)
            return false;

        var size = BinaryPrimitives.ReadUInt32LittleEndian(span[18..]);
        if (size > (uint)(span.Length - 22))
            return false;

        data = headerExtension.Data.Slice(22, (int)size);
        return true;
    }

    private static void AddContentDescription(List<AsfAttribute> attributes, string name, string? value)
    {
        if (value is { Length: > 0 })
            attributes.Add(AsfAttributes.CreateString(name, value));
    }

    private static TimeSpan? ReadDuration(ReadOnlySpan<byte> data)
    {
        // File ID, file size, creation date, data packet count, play duration (100 ns), send duration, preroll (ms), flags
        if (data.Length < 68)
            return null;

        var playDuration = BinaryPrimitives.ReadUInt64LittleEndian(data[40..]);
        var preroll = BinaryPrimitives.ReadUInt64LittleEndian(data[56..]);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(data[64..]);

        // A file being broadcast does not know its duration yet.
        const uint BroadcastFlag = 0x01;
        if ((flags & BroadcastFlag) != 0 || preroll > ulong.MaxValue / 10_000)
            return null;

        var preroll100ns = preroll * 10_000;
        if (playDuration <= preroll100ns || playDuration - preroll100ns > long.MaxValue)
            return null;

        return TimeSpan.FromTicks((long)(playDuration - preroll100ns));
    }

    private static void ReadVideoSize(ReadOnlySpan<byte> data, MediaTagInfo tags)
    {
        // Stream type, error correction type, time offset, type-specific data length, error correction data length, flags,
        // reserved, then the type-specific data, which starts with the encoded width and height of a video stream.
        if (data.Length < 62 || new Guid(data[..16]) != AsfGuids.VideoMedia)
            return;

        var width = BinaryPrimitives.ReadUInt32LittleEndian(data[54..]);
        var height = BinaryPrimitives.ReadUInt32LittleEndian(data[58..]);
        if (width is > 0 and <= int.MaxValue && height is > 0 and <= int.MaxValue)
        {
            tags.VideoWidth = (int)width;
            tags.VideoHeight = (int)height;
        }
    }
}
