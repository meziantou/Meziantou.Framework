using System.Buffers.Binary;
using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Asf;

/// <summary>
/// Rewrites the header object of an ASF file with new attributes, and copies the data and index objects that follow it.
/// </summary>
/// <remarks>
/// The data packets and the indexes do not store file offsets, so a header of another size leaves them valid. The file
/// properties object stores the size of the file, which is updated. Attributes that describe one stream or one language are
/// copied unchanged.
/// </remarks>
internal sealed class AsfWriter : IMediaTagWriter
{
    private const int FileSizeOffset = AsfObject.HeaderSize + 16;
    private const int FilePropertiesFlagsOffset = AsfObject.HeaderSize + 64;

    public MediaTagResult WriteTags(Stream inputStream, Stream outputStream, MediaTagInfo tags, MediaTagWriteOptions options)
    {
        try
        {
            if (!AsfHeader.TryRead(inputStream, out var header, out var complete, out var readError))
                return MediaTagResult.Failure(MediaTagError.CorruptFile, readError);

            // Rebuilding the header from a partial parse would drop the objects after the malformed one.
            if (!complete)
                return MediaTagResult.Failure(MediaTagError.CorruptFile, "The ASF header objects do not cover the whole header.");

            var nativeFields = GetNativeFields(header, tags, options);
            if (!AsfFields.TryBuildAttributes(tags, nativeFields, out var contentDescription, out var attributes, out var nativeAttributes, out var buildError))
                return MediaTagResult.Failure(MediaTagError.InvalidTagData, buildError);

            // Short attributes go in the extended content description object, which every reader knows. The others go in the
            // metadata library object, whose values have a 32-bit length and which can hold a name several times. Native fields
            // all go there, so they read back in the order they were written.
            var shortAttributes = new List<AsfAttribute>();
            var libraryAttributes = new List<AsfAttribute>();
            foreach (var attribute in attributes)
            {
                var isShort = attribute.Value.Length <= AsfAttributes.MaxShortValueLength && attribute.Name != AsfFields.Picture;
                (isShort ? shortAttributes : libraryAttributes).Add(attribute);
            }

            libraryAttributes.AddRange(nativeAttributes);
            if (shortAttributes.Count > ushort.MaxValue || libraryAttributes.Count > ushort.MaxValue)
                return MediaTagResult.Failure(MediaTagError.InvalidTagData, "An ASF file cannot store more than 65535 attributes in one object.");

            if (!TryBuildChildren(header, contentDescription, shortAttributes, libraryAttributes, out var children, out var childrenError))
                return MediaTagResult.Failure(MediaTagError.CorruptFile, childrenError);

            var headerSize = (long)AsfHeader.PrefixSize;
            foreach (var child in children)
            {
                headerSize += child.Length;
            }

            if (headerSize > AsfHeader.MaxSize)
                return MediaTagResult.Failure(MediaTagError.InvalidTagData, "The tags are too large for an ASF header.");

            var restLength = inputStream.Length - header.Bytes.Length;
            var prefix = header.Bytes.AsSpan(0, AsfHeader.PrefixSize).ToArray();
            BinaryPrimitives.WriteUInt64LittleEndian(prefix.AsSpan(16), (ulong)headerSize);
            BinaryPrimitives.WriteUInt32LittleEndian(prefix.AsSpan(24), (uint)children.Count);
            outputStream.Write(prefix);
            foreach (var child in children)
            {
                if (new Guid(child.AsSpan(0, 16)) == AsfGuids.FileProperties)
                    UpdateFileSize(child, headerSize + restLength);

                outputStream.Write(child);
            }

            if (!StreamHelpers.CopyExactlyFrom(inputStream, outputStream, header.Bytes.Length, restLength))
                return MediaTagResult.Failure(MediaTagError.UnexpectedEndOfStream, "The file ended before all data objects could be read.");

            return MediaTagResult.Success();
        }
        catch (Exception ex) when (MediaTagErrors.TryMap(ex, out var error))
        {
            return MediaTagResult.Failure(error, ex.Message);
        }
    }

    private static List<MediaNativeField> GetNativeFields(AsfHeader header, MediaTagInfo tags, MediaTagWriteOptions options)
    {
        if (!options.PreserveUnrecognizedFrames)
            return [];

        if (NativeFieldSelector.GetFieldsToWrite(tags, MediaTagType.Asf) is { } fields)
            return fields;

        var existing = new AsfReader().ReadTags(new MemoryStream(header.Bytes, writable: false));
        return existing.IsSuccess ? [.. existing.Value.NativeFields ?? []] : [];
    }

    private static bool TryBuildChildren(AsfHeader header, string?[]? contentDescription, List<AsfAttribute> shortAttributes, List<AsfAttribute> libraryAttributes, out List<byte[]> children, out string? error)
    {
        children = [];
        error = null;
        var contentDescriptionWritten = false;
        var extendedContentDescriptionWritten = false;
        var headerExtensionWritten = false;
        foreach (var child in header.Children)
        {
            // The first object of each kind is replaced in place. Later ones are stale: their values were read into the tags.
            if (child.Id == AsfGuids.ContentDescription)
            {
                if (!contentDescriptionWritten && contentDescription is not null)
                    children.Add(AsfAttributes.BuildContentDescription(contentDescription));

                contentDescriptionWritten = true;
            }
            else if (child.Id == AsfGuids.ExtendedContentDescription)
            {
                if (!extendedContentDescriptionWritten && shortAttributes.Count > 0)
                    children.Add(AsfAttributes.BuildExtendedContentDescription(shortAttributes));

                extendedContentDescriptionWritten = true;
            }
            else if (child.Id == AsfGuids.HeaderExtension)
            {
                // The attributes go in the first one. The file-level attributes of the others are removed too: the reader
                // reads them, so they would bring back the values the tags no longer have.
                if (!TryBuildHeaderExtension(child, headerExtensionWritten ? [] : libraryAttributes, out var headerExtension, out error))
                    return false;

                children.Add(headerExtension);
                headerExtensionWritten = true;
            }
            else
            {
                children.Add(child.Bytes.ToArray());
            }
        }

        // The specification requires a header extension object. One is added when the file has none, whether or not it holds
        // attributes, so that removing the tags of a written file gives the same file as removing the tags of the original.
        if (!headerExtensionWritten)
        {
            var empty = new byte[22];
            AsfGuids.HeaderExtensionReserved.TryWriteBytes(empty);
            BinaryPrimitives.WriteUInt16LittleEndian(empty.AsSpan(16), 6);
            if (!TryBuildHeaderExtension(new AsfObject(AsfGuids.HeaderExtension, AsfObject.Build(AsfGuids.HeaderExtension, empty)), libraryAttributes, out var headerExtension, out error))
                return false;

            children.Add(headerExtension);
        }

        if (!contentDescriptionWritten && contentDescription is not null)
            children.Add(AsfAttributes.BuildContentDescription(contentDescription));

        if (!extendedContentDescriptionWritten && shortAttributes.Count > 0)
            children.Add(AsfAttributes.BuildExtendedContentDescription(shortAttributes));

        return true;
    }

    /// <summary>
    /// Rebuilds a header extension object: the file-level attributes of its metadata and metadata library objects are replaced,
    /// and the attributes of a stream or a language are kept.
    /// </summary>
    private static bool TryBuildHeaderExtension(AsfObject headerExtension, List<AsfAttribute> libraryAttributes, [NotNullWhen(true)] out byte[]? result, out string? error)
    {
        result = null;
        error = null;
        if (!AsfReader.TryGetHeaderExtensionData(headerExtension, out var data) || !AsfObject.TryParseList(data, out var objects))
        {
            error = "The ASF header extension object is malformed.";
            return false;
        }

        using var content = new MemoryStream();
        var libraryWritten = false;
        foreach (var extension in objects)
        {
            if (extension.Id != AsfGuids.Metadata && extension.Id != AsfGuids.MetadataLibrary)
            {
                content.Write(extension.Bytes.Span);
                continue;
            }

            if (!AsfAttributes.TryReadMetadata(extension.Data, out var records))
            {
                error = "An ASF metadata object is malformed.";
                return false;
            }

            var kept = records.Where(record => !record.IsFileLevel).Select(record => record.Record).ToList();
            var added = extension.Id == AsfGuids.MetadataLibrary && !libraryWritten ? libraryAttributes : [];
            libraryWritten |= extension.Id == AsfGuids.MetadataLibrary;
            if (kept.Count + added.Count > ushort.MaxValue)
            {
                error = "An ASF metadata object cannot store more than 65535 attributes.";
                return false;
            }

            // An object left without attributes is removed, so writing does not leave empty objects behind.
            if (kept.Count + added.Count > 0)
                content.Write(AsfAttributes.BuildMetadata(extension.Id, kept, added));
        }

        if (!libraryWritten && libraryAttributes.Count > 0)
            content.Write(AsfAttributes.BuildMetadataLibrary([], libraryAttributes));

        // The reserved GUID and 16-bit value, then the size of the objects
        var prefix = headerExtension.Data.Span[..18];
        var body = new byte[22 + content.Length];
        prefix.CopyTo(body);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(18), (uint)content.Length);
        content.ToArray().CopyTo(body, 22);
        result = AsfObject.Build(AsfGuids.HeaderExtension, body);
        return true;
    }

    private static void UpdateFileSize(byte[] fileProperties, long fileSize)
    {
        // The size is not known while a file is being broadcast, and is then left as is.
        if (fileProperties.Length < FilePropertiesFlagsOffset + 4 || (BinaryPrimitives.ReadUInt32LittleEndian(fileProperties.AsSpan(FilePropertiesFlagsOffset)) & 0x01) != 0)
            return;

        BinaryPrimitives.WriteUInt64LittleEndian(fileProperties.AsSpan(FileSizeOffset), (ulong)fileSize);
    }
}
