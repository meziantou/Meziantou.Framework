using System.Buffers.Binary;

namespace Meziantou.Framework.MediaTags.Formats.Asf;

/// <summary>Reads and writes the objects of an ASF header that store attributes.</summary>
internal static class AsfAttributes
{
    /// <summary>The fields of the content description object, in the order they are stored.</summary>
    public const int ContentDescriptionFieldCount = 5;
    public const int TitleIndex = 0;
    public const int AuthorIndex = 1;
    public const int CopyrightIndex = 2;
    public const int DescriptionIndex = 3;
    public const int RatingIndex = 4;

    /// <summary>The largest value of an extended content description attribute, whose length is a 16-bit value.</summary>
    public const int MaxShortValueLength = ushort.MaxValue;

    /// <summary>The longest name, whose length in bytes, terminator included, is a 16-bit value.</summary>
    public const int MaxNameLength = (ushort.MaxValue / 2) - 1;

    /// <summary>Reads the title, author, copyright, description and rating of a content description object.</summary>
    public static string[] ReadContentDescription(ReadOnlySpan<byte> data)
    {
        var result = new string[ContentDescriptionFieldCount];
        if (data.Length < ContentDescriptionFieldCount * 2)
            return result;

        var offset = ContentDescriptionFieldCount * 2;
        for (var i = 0; i < ContentDescriptionFieldCount; i++)
        {
            var length = BinaryPrimitives.ReadUInt16LittleEndian(data[(i * 2)..]);
            if (length > data.Length - offset)
                break;

            result[i] = ReadString(data.Slice(offset, length));
            offset += length;
        }

        return result;
    }

    public static byte[] BuildContentDescription(string?[] values)
    {
        var encoded = new byte[ContentDescriptionFieldCount][];
        var length = ContentDescriptionFieldCount * 2;
        for (var i = 0; i < ContentDescriptionFieldCount; i++)
        {
            encoded[i] = EncodeString(values[i] ?? "");
            length += encoded[i].Length;
        }

        var data = new byte[length];
        var offset = ContentDescriptionFieldCount * 2;
        for (var i = 0; i < ContentDescriptionFieldCount; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(i * 2), (ushort)encoded[i].Length);
            encoded[i].CopyTo(data, offset);
            offset += encoded[i].Length;
        }

        return AsfObject.Build(AsfGuids.ContentDescription, data);
    }

    /// <summary>Reads the attributes of an extended content description object. Reading stops at the first malformed one.</summary>
    public static List<AsfAttribute> ReadExtendedContentDescription(ReadOnlyMemory<byte> data)
    {
        var result = new List<AsfAttribute>();
        var span = data.Span;
        if (span.Length < 2)
            return result;

        var count = BinaryPrimitives.ReadUInt16LittleEndian(span);
        var offset = 2;
        for (var i = 0; i < count; i++)
        {
            if (span.Length - offset < 2)
                break;

            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]);
            if (span.Length - offset - 2 < nameLength + 4)
                break;

            var name = ReadString(span.Slice(offset + 2, nameLength));
            var type = (AsfDataType)BinaryPrimitives.ReadUInt16LittleEndian(span[(offset + 2 + nameLength)..]);
            var valueLength = BinaryPrimitives.ReadUInt16LittleEndian(span[(offset + 4 + nameLength)..]);
            var valueStart = offset + 6 + nameLength;
            if (valueLength > span.Length - valueStart)
                break;

            result.Add(new AsfAttribute(name, type, NormalizeBool(type, data.Slice(valueStart, valueLength)), Stream: 0, Language: 0, data[offset..(valueStart + valueLength)]));
            offset = valueStart + valueLength;
        }

        return result;
    }

    public static byte[] BuildExtendedContentDescription(IReadOnlyList<AsfAttribute> attributes)
    {
        using var stream = new MemoryStream();
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, (ushort)attributes.Count);
        stream.Write(buffer);
        foreach (var attribute in attributes)
        {
            var name = EncodeString(attribute.Name);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer, (ushort)name.Length);
            stream.Write(buffer);
            stream.Write(name);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer, (ushort)attribute.Type);
            stream.Write(buffer);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer, (ushort)attribute.Value.Length);
            stream.Write(buffer);
            stream.Write(attribute.Value.Span);
        }

        return AsfObject.Build(AsfGuids.ExtendedContentDescription, stream.ToArray());
    }

    /// <summary>Reads the records of a metadata or metadata library object.</summary>
    /// <returns><see langword="false"/> when the records do not cover the whole object; the records before the malformed one are returned.</returns>
    public static bool TryReadMetadata(ReadOnlyMemory<byte> data, out List<AsfAttribute> attributes)
    {
        attributes = [];
        var span = data.Span;
        if (span.Length < 2)
            return false;

        var count = BinaryPrimitives.ReadUInt16LittleEndian(span);
        var offset = 2;
        for (var i = 0; i < count; i++)
        {
            if (span.Length - offset < 12)
                return false;

            var language = BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]);
            var streamNumber = BinaryPrimitives.ReadUInt16LittleEndian(span[(offset + 2)..]);
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(span[(offset + 4)..]);
            var type = (AsfDataType)BinaryPrimitives.ReadUInt16LittleEndian(span[(offset + 6)..]);
            var valueLength = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 8)..]);
            var nameStart = offset + 12;
            if (nameLength > span.Length - nameStart || valueLength > (uint)(span.Length - nameStart - nameLength))
                return false;

            var valueStart = nameStart + nameLength;
            var end = valueStart + (int)valueLength;
            var name = ReadString(span.Slice(nameStart, nameLength));
            attributes.Add(new AsfAttribute(name, type, NormalizeBool(type, data[valueStart..end]), streamNumber, language, data[offset..end]));
            offset = end;
        }

        return offset == span.Length;
    }

    /// <summary>Builds a metadata library object from records copied as stored and from new file-level attributes.</summary>
    public static byte[] BuildMetadataLibrary(IReadOnlyList<ReadOnlyMemory<byte>> keptRecords, IReadOnlyList<AsfAttribute> attributes)
        => BuildMetadata(AsfGuids.MetadataLibrary, keptRecords, attributes);

    public static byte[] BuildMetadata(Guid objectId, IReadOnlyList<ReadOnlyMemory<byte>> keptRecords, IReadOnlyList<AsfAttribute> attributes)
    {
        using var stream = new MemoryStream();
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)(keptRecords.Count + attributes.Count));
        stream.Write(header[..2]);
        foreach (var record in keptRecords)
        {
            stream.Write(record.Span);
        }

        foreach (var attribute in attributes)
        {
            var name = EncodeString(attribute.Name);

            // A boolean is a 16-bit value in these objects, and a 32-bit value in the extended content description.
            var value = attribute.Type == AsfDataType.Bool ? (ReadOnlySpan<byte>)[attribute.Value.Span[0], 0] : attribute.Value.Span;
            header.Clear();
            BinaryPrimitives.WriteUInt16LittleEndian(header[4..], (ushort)name.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(header[6..], (ushort)attribute.Type);
            BinaryPrimitives.WriteUInt32LittleEndian(header[8..], (uint)value.Length);
            stream.Write(header);
            stream.Write(name);
            stream.Write(value);
        }

        return AsfObject.Build(objectId, stream.ToArray());
    }

    /// <summary>Gets the length a value of the data type must have, or -1 when any length is valid.</summary>
    public static int GetValueLength(AsfDataType type) => type switch
    {
        AsfDataType.Bool or AsfDataType.DWord => 4,
        AsfDataType.QWord => 8,
        AsfDataType.Word => 2,
        AsfDataType.Guid => 16,
        _ => -1,
    };

    /// <summary>Reads a number stored as an integer of any size, or as text.</summary>
    public static bool TryReadNumber(in AsfAttribute attribute, out ulong value)
    {
        var data = attribute.Value.Span;
        switch (attribute.Type)
        {
            case AsfDataType.DWord when data.Length >= 4:
                value = BinaryPrimitives.ReadUInt32LittleEndian(data);
                return true;

            case AsfDataType.QWord when data.Length >= 8:
                value = BinaryPrimitives.ReadUInt64LittleEndian(data);
                return true;

            case AsfDataType.Word when data.Length >= 2:
                value = BinaryPrimitives.ReadUInt16LittleEndian(data);
                return true;

            case AsfDataType.Bool when data.Length >= 4:
                value = BinaryPrimitives.ReadUInt32LittleEndian(data);
                return true;

            case AsfDataType.String:
                return ulong.TryParse(ReadString(data), NumberStyles.None, CultureInfo.InvariantCulture, out value);

            default:
                value = 0;
                return false;
        }
    }

    public static string ReadString(ReadOnlySpan<byte> data) => Encoding.Unicode.GetString(data).TrimEnd('\0');

    /// <summary>Encodes a string as UTF-16 with a null terminator, as every string of the format is stored.</summary>
    public static byte[] EncodeString(string value)
    {
        var result = new byte[Encoding.Unicode.GetByteCount(value) + 2];
        Encoding.Unicode.GetBytes(value, result);
        return result;
    }

    public static AsfAttribute CreateString(string name, string value) => new(name, AsfDataType.String, EncodeString(value), 0, 0, default);

    public static AsfAttribute CreateDWord(string name, uint value)
    {
        var data = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(data, value);
        return new AsfAttribute(name, AsfDataType.DWord, data, 0, 0, default);
    }

    public static AsfAttribute CreateBool(string name, bool value) => new(name, AsfDataType.Bool, new byte[] { value ? (byte)1 : (byte)0, 0, 0, 0 }, 0, 0, default);

    /// <summary>Converts a boolean to a 32-bit 0 or 1, from the 16-bit or 32-bit value the objects store.</summary>
    private static ReadOnlyMemory<byte> NormalizeBool(AsfDataType type, ReadOnlyMemory<byte> value)
    {
        if (type != AsfDataType.Bool)
            return value;

        return value.Span.ContainsAnyExcept((byte)0) ? new byte[] { 1, 0, 0, 0 } : new byte[4];
    }
}
