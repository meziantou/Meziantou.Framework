using System.Buffers.Binary;
using System.Text;
using Meziantou.Framework.Email.Internals;

namespace Meziantou.Framework.Email.Storage;

/// <summary>An offline address book of Exchange and Outlook in the version 4 Full Details format ([MS-OXOAB] 2.9): the file <c>udetails.oab</c> Outlook keeps in its <c>Offline Address Books</c> folder. It has the schemas of its records, the header record that describes the address list, and a record per Address Book object.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../../tests/Meziantou.Framework.Email.Tests/OabFileTests.cs" ref:region="OabFile_Usage" ref:dedent="true">
/// var book = OabFile.Create();
/// book.Header.Values[MapiPropertyTag.FromValue(0x6800001F)] = "\\Global Address List";
/// book.Header.Values[MapiPropertyTag.FromValue(0x68010003)] = 1;
/// var record = new OabRecord();
/// record.Values[MapiPropertyTag.FromValue(0x3003001E)] = "/o=Example/ou=Exchange Administrative Group/cn=Recipients/cn=alice";
/// record.Values[MapiPropertyTag.FromValue(0x39FE001F)] = "alice@example.com";
/// record.Values[MapiPropertyTag.FromValue(0x3001001F)] = "Alice Martin";
/// record.Values[MapiPropertyTag.FromValue(0x800F101F)] = new[] { "SMTP:alice@example.com", "smtp:a.martin@example.com" };
/// record.Values[MapiPropertyTag.FromValue(0x0FFE0003)] = 6;
/// book.Records.Add(record);
/// book.Save(path);
///
/// var file = OabFile.Load(path);
/// Assert.Equal("\\Global Address List", file.Name);
/// var alice = file.Records.Single();
/// Assert.Equal("alice@example.com", alice.SmtpAddress);
/// Assert.Equal(["SMTP:alice@example.com", "smtp:a.martin@example.com"], alice.ProxyAddresses);
///
/// // A record as a vCard, or as a contact of Outlook
/// var card = VCard.FromOab(alice);
/// Assert.Equal("Alice Martin", card.FormattedName);
/// </code>
/// </example>
public sealed class OabFile
{
    private const uint FullDetailsVersion = 0x20;
    private const long MaxSize = 2L * 1024 * 1024 * 1024 - 1;

    /// <summary>Gets the properties the header record can have (<c>rgHdrAtts</c>).</summary>
    public IList<OabPropertyDefinition> HeaderProperties { get; } = [];

    /// <summary>Gets the properties the records can have (<c>rgOabAtts</c>), in their order in the records.</summary>
    public IList<OabPropertyDefinition> RecordProperties { get; } = [];

    /// <summary>Gets the header record: the name of the address list (<c>0x6800001F</c>), its distinguished name (<c>0x6804001E</c>), its sequence number (<c>0x68010003</c>) and its GUID (<c>0x6802001E</c>).</summary>
    public OabRecord Header { get; } = new();

    /// <summary>Gets the records of the Address Book objects.</summary>
    public IList<OabRecord> Records { get; } = [];

    /// <summary>Gets the name of the address list (<c>PidTagOfflineAddressBookName</c>).</summary>
    public string? Name => Header.GetString(0x6800);

    /// <summary>Gets the sequence number of the address book (<c>PidTagOfflineAddressBookSequence</c>), which increases with each generation.</summary>
    public int? Sequence => Header.Find(0x6801) as int?;

    /// <summary>Gets the checksum of the file, as read (<c>ulSerial</c>): the CRC of the file after its header.</summary>
    public uint Checksum { get; private set; }

    /// <summary>Creates an address book with the properties the specification requires in the header record and in the records.</summary>
    /// <returns>The address book.</returns>
    public static OabFile Create()
    {
        var file = new OabFile();
        file.HeaderProperties.Add(new OabPropertyDefinition(MapiPropertyTag.FromValue(0x6800001F)));
        file.HeaderProperties.Add(new OabPropertyDefinition(MapiPropertyTag.FromValue(0x6804001E)));
        file.HeaderProperties.Add(new OabPropertyDefinition(MapiPropertyTag.FromValue(0x68010003)));
        file.HeaderProperties.Add(new OabPropertyDefinition(MapiPropertyTag.FromValue(0x6802001E)));
        foreach (var (tag, flags) in new[]
        {
            (0x3003001Eu, OabPropertyFlags.PrimaryKey), (0x39FE001Fu, OabPropertyFlags.PrimaryKey), (0x3001001Fu, OabPropertyFlags.AnrIndex), (0x3A00001Fu, OabPropertyFlags.AnrIndex),
            (0x3A11001Fu, OabPropertyFlags.AnrIndex), (0x3A06001Fu, OabPropertyFlags.AnrIndex), (0x800F101Fu, OabPropertyFlags.AnrIndex), (0x3A19001Fu, OabPropertyFlags.AnrIndex),
            (0x39000003u, OabPropertyFlags.None), (0x0FFE0003u, OabPropertyFlags.None), (0x3A40000Bu, OabPropertyFlags.None), (0x3A08001Fu, OabPropertyFlags.None),
            (0x3A0A001Fu, OabPropertyFlags.None), (0x3A29001Fu, OabPropertyFlags.None), (0x3A27001Fu, OabPropertyFlags.None), (0x3A28001Fu, OabPropertyFlags.None),
            (0x3A2A001Fu, OabPropertyFlags.None), (0x3A26001Fu, OabPropertyFlags.None), (0x3A17001Fu, OabPropertyFlags.None), (0x3A16001Fu, OabPropertyFlags.None),
            (0x3A30001Fu, OabPropertyFlags.None), (0x3A18001Fu, OabPropertyFlags.None), (0x8011001Fu, OabPropertyFlags.None), (0x3A09001Fu, OabPropertyFlags.None),
            (0x3A1B101Fu, OabPropertyFlags.None), (0x3A2F101Fu, OabPropertyFlags.None), (0x3A23001Fu, OabPropertyFlags.None), (0x3A1C001Fu, OabPropertyFlags.None),
            (0x3A2E001Fu, OabPropertyFlags.None), (0x3A21001Fu, OabPropertyFlags.None), (0x3004001Fu, OabPropertyFlags.None), (0x3A220102u, OabPropertyFlags.None),
            (0x3A701102u, OabPropertyFlags.None), (0x8C6A1102u, OabPropertyFlags.None), (0x8006001Eu, OabPropertyFlags.None), (0x39FF001Eu, OabPropertyFlags.None),
        })
        {
            file.RecordProperties.Add(new OabPropertyDefinition(MapiPropertyTag.FromValue(tag), flags));
        }

        return file;
    }

    /// <summary>Reads an address book.</summary>
    /// <param name="data">The content of the file.</param>
    /// <param name="verifyChecksum"><see langword="true"/> to check the checksum of the file.</param>
    /// <returns>The address book.</returns>
    /// <exception cref="InvalidEmailDataException">The file is not an uncompressed version 4 Full Details file, its structures are malformed, or its checksum is invalid.</exception>
    public static OabFile Load(ReadOnlySpan<byte> data, bool verifyChecksum = true)
    {
        if (data.Length < 12)
        {
            throw new InvalidEmailDataException("The file is too short to be an offline address book.");
        }

        var version = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (version != FullDetailsVersion)
        {
            throw new InvalidEmailDataException(version == 3 && BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) is 1 or 2
                ? "The file is a compressed offline address book (LZX) or a differential patch, which are not supported: read the uncompressed file Outlook keeps (udetails.oab)."
                : $"The file is not a version 4 Full Details file of an offline address book (version 0x{version:X}).");
        }

        var file = new OabFile { Checksum = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) };
        if (verifyChecksum && OabCrc.Compute(data[12..]) != file.Checksum)
        {
            throw new InvalidEmailDataException("The checksum of the offline address book is invalid.");
        }

        var count = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
        var position = 12;
        var metadataSize = ReadUInt32(data, ref position);
        var metadataEnd = 12 + (long)metadataSize;
        if (metadataSize < 12 || metadataEnd > data.Length)
        {
            throw new InvalidEmailDataException("The metadata of the offline address book is larger than the file.");
        }

        var metadata = data[..(int)metadataEnd];
        ReadSchema(metadata, ref position, file.HeaderProperties);
        ReadSchema(metadata, ref position, file.RecordProperties);
        position = (int)metadataEnd;
        ReadRecord(data, ref position, file.HeaderProperties, file.Header);

        // Each record has at least its size: the count cannot be larger than the rest of the file divided by 4
        if (count > (uint)(data.Length - position) / 4)
        {
            throw new InvalidEmailDataException("The offline address book has more records than its size allows.");
        }

        for (var i = 0; i < count; i++)
        {
            var record = new OabRecord();
            ReadRecord(data, ref position, file.RecordProperties, record);
            file.Records.Add(record);
        }

        return file;
    }

    /// <summary>Reads an address book from a stream, until its end.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="verifyChecksum"><see langword="true"/> to check the checksum of the file.</param>
    /// <returns>The address book.</returns>
    /// <exception cref="InvalidEmailDataException">The file is not an uncompressed version 4 Full Details file, its structures are malformed, or its checksum is invalid.</exception>
    public static OabFile Load(Stream stream, bool verifyChecksum = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Load(StreamHelper.ReadAll(stream, MaxSize), verifyChecksum);
    }

    /// <summary>Reads an address book, such as <c>udetails.oab</c>.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="verifyChecksum"><see langword="true"/> to check the checksum of the file.</param>
    /// <returns>The address book.</returns>
    /// <exception cref="InvalidEmailDataException">The file is not an uncompressed version 4 Full Details file, its structures are malformed, or its checksum is invalid.</exception>
    public static OabFile Load(string path, bool verifyChecksum = true)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = File.OpenRead(path);
        return Load(stream, verifyChecksum);
    }

    /// <summary>Writes the address book, with its checksum. The values of the records must have the types of their properties; the empty strings and binaries are not written, as the specification requires.</summary>
    /// <param name="stream">The stream.</param>
    /// <exception cref="InvalidOperationException">A value does not have the type of its property.</exception>
    public void Save(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        stream.Write(ToByteArray());
    }

    /// <summary>Writes the address book in a file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <exception cref="InvalidOperationException">A value does not have the type of its property.</exception>
    public void Save(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        File.WriteAllBytes(path, ToByteArray());
    }

    /// <summary>Returns the content of the file.</summary>
    /// <returns>The content.</returns>
    /// <exception cref="InvalidOperationException">A value does not have the type of its property.</exception>
    public byte[] ToByteArray()
    {
        using var output = new MemoryStream();
        output.Write(new byte[12]);
        var metadataSize = 4 + 4 + (HeaderProperties.Count * 8) + 4 + (RecordProperties.Count * 8);
        WriteUInt32(output, (uint)metadataSize);
        WriteSchema(output, HeaderProperties);
        WriteSchema(output, RecordProperties);
        WriteRecord(output, HeaderProperties, Header);
        foreach (var record in Records)
        {
            WriteRecord(output, RecordProperties, record);
        }

        var bytes = output.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, FullDetailsVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), OabCrc.Compute(bytes.AsSpan(12)));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)Records.Count);
        return bytes;
    }

    private static void ReadSchema(ReadOnlySpan<byte> data, ref int position, IList<OabPropertyDefinition> properties)
    {
        var count = ReadUInt32(data, ref position);
        if (count > (uint)(data.Length - position) / 8)
        {
            throw new InvalidEmailDataException("A property table of the offline address book is larger than its metadata.");
        }

        for (var i = 0; i < count; i++)
        {
            var tag = MapiPropertyTag.FromValue(ReadUInt32(data, ref position));
            var flags = (OabPropertyFlags)(ReadUInt32(data, ref position) & 0x3);
            properties.Add(new OabPropertyDefinition(tag, flags));
        }
    }

    private static void ReadRecord(ReadOnlySpan<byte> data, ref int position, IList<OabPropertyDefinition> properties, OabRecord record)
    {
        var start = position;
        var size = ReadUInt32(data, ref position);
        if (size < 4 || size > (uint)(data.Length - start))
        {
            throw new InvalidEmailDataException("A record of the offline address book is larger than the file.");
        }

        var end = start + (int)size;
        var content = data[..end];
        var presenceSize = (properties.Count + 7) / 8;
        if (presenceSize > end - position)
        {
            throw new InvalidEmailDataException("The presence bits of a record of the offline address book are truncated.");
        }

        var presence = content.Slice(position, presenceSize);
        position += presenceSize;
        for (var i = 0; i < properties.Count; i++)
        {
            // The first property is the most significant bit of the first byte
            if ((presence[i / 8] & (0x80 >> (i % 8))) == 0)
            {
                continue;
            }

            var tag = properties[i].Tag;
            var value = ReadValue(content, ref position, tag.Type);
            if (value is not null)
            {
                record.Values[tag] = value;
            }
        }

        position = end;
    }

    private static object? ReadValue(ReadOnlySpan<byte> data, ref int position, MapiPropertyType type)
    {
        switch (type)
        {
            case MapiPropertyType.Integer32:
                return unchecked((int)ReadInteger(data, ref position));
            case MapiPropertyType.Boolean:
                return ReadByte(data, ref position) != 0;
            case MapiPropertyType.String8:
                return Encoding.Latin1.GetString(ReadString(data, ref position));
            case MapiPropertyType.Unicode:
                return Encoding.UTF8.GetString(ReadString(data, ref position));
            case MapiPropertyType.Binary:
                return ReadBinary(data, ref position);
            case MapiPropertyType.ObjectReference:
                // An object has no value in the file ([MS-OXOAB] 2.9.2.2.1)
                return null;
        }

        var count = ReadCount(data, ref position);
        return type switch
        {
            MapiPropertyType.MultipleInteger32 => Repeat(count, data, ref position, (ReadOnlySpan<byte> d, ref int p) => unchecked((int)ReadInteger(d, ref p))),
            MapiPropertyType.MultipleString8 => Repeat(count, data, ref position, (ReadOnlySpan<byte> d, ref int p) => Encoding.Latin1.GetString(ReadString(d, ref p))),
            MapiPropertyType.MultipleUnicode => Repeat(count, data, ref position, (ReadOnlySpan<byte> d, ref int p) => Encoding.UTF8.GetString(ReadString(d, ref p))),
            MapiPropertyType.MultipleBinary => Repeat(count, data, ref position, (ReadOnlySpan<byte> d, ref int p) => ReadBinary(d, ref p)),
            _ => throw new InvalidEmailDataException($"A property of the offline address book has the type 0x{(ushort)type:X4}, which the format does not allow."),
        };
    }

    private delegate T ValueReader<T>(ReadOnlySpan<byte> data, ref int position);

    private static T[] Repeat<T>(int count, ReadOnlySpan<byte> data, ref int position, ValueReader<T> read)
    {
        var result = new T[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = read(data, ref position);
        }

        return result;
    }

    /// <summary>Reads a count: each value has at least one byte, so the count cannot be larger than the rest of the record.</summary>
    private static int ReadCount(ReadOnlySpan<byte> data, ref int position)
    {
        var count = ReadInteger(data, ref position);
        if (count > (uint)(data.Length - position))
        {
            throw new InvalidEmailDataException("A multi-valued property of the offline address book has more values than its record has bytes.");
        }

        return (int)count;
    }

    /// <summary>Reads an integer: a byte for the values up to 127, else 0x81 to 0x84 then 1 to 4 bytes in little endian.</summary>
    private static uint ReadInteger(ReadOnlySpan<byte> data, ref int position)
    {
        var first = ReadByte(data, ref position);
        if (first < 0x80)
        {
            return first;
        }

        var length = first - 0x80;
        if (length is < 1 or > 4 || length > data.Length - position)
        {
            throw new InvalidEmailDataException("An integer of the offline address book is malformed.");
        }

        uint value = 0;
        for (var i = 0; i < length; i++)
        {
            value |= (uint)data[position + i] << (8 * i);
        }

        position += length;
        return value;
    }

    private static byte[] ReadBinary(ReadOnlySpan<byte> data, ref int position)
    {
        var length = ReadInteger(data, ref position);
        if (length > (uint)(data.Length - position))
        {
            throw new InvalidEmailDataException("A binary value of the offline address book is larger than its record.");
        }

        var result = data.Slice(position, (int)length).ToArray();
        position += (int)length;
        return result;
    }

    private static ReadOnlySpan<byte> ReadString(ReadOnlySpan<byte> data, ref int position)
    {
        var end = data[position..].IndexOf((byte)0);
        if (end < 0)
        {
            throw new InvalidEmailDataException("A string of the offline address book has no terminator.");
        }

        var result = data.Slice(position, end);
        position += end + 1;
        return result;
    }

    private static byte ReadByte(ReadOnlySpan<byte> data, ref int position)
    {
        if (position >= data.Length)
        {
            throw new InvalidEmailDataException("A record of the offline address book is truncated.");
        }

        return data[position++];
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, ref int position)
    {
        if (data.Length - position < 4)
        {
            throw new InvalidEmailDataException("The offline address book is truncated.");
        }

        var value = BinaryPrimitives.ReadUInt32LittleEndian(data[position..]);
        position += 4;
        return value;
    }

    private static void WriteSchema(Stream output, IList<OabPropertyDefinition> properties)
    {
        WriteUInt32(output, (uint)properties.Count);
        foreach (var property in properties)
        {
            WriteUInt32(output, property.Tag.Value);
            WriteUInt32(output, (uint)property.Flags);
        }
    }

    private static void WriteRecord(Stream output, IList<OabPropertyDefinition> properties, OabRecord record)
    {
        using var data = new MemoryStream();
        var presence = new byte[(properties.Count + 7) / 8];
        for (var i = 0; i < properties.Count; i++)
        {
            var tag = properties[i].Tag;
            if (record.Values.TryGetValue(tag, out var value) && WriteValue(data, tag, value))
            {
                presence[i / 8] |= (byte)(0x80 >> (i % 8));
            }
        }

        WriteUInt32(output, (uint)(4 + presence.Length + data.Length));
        output.Write(presence);
        data.Position = 0;
        data.CopyTo(output);
    }

    /// <summary>Writes a value. Returns <see langword="false"/> for a value the format does not write: an empty string or binary, an empty list, or an object.</summary>
    private static bool WriteValue(Stream output, MapiPropertyTag tag, object value)
    {
        switch (tag.Type, value)
        {
            case (MapiPropertyType.Integer32, int integer):
                WriteInteger(output, unchecked((uint)integer));
                return true;
            case (MapiPropertyType.Boolean, bool boolean):
                output.WriteByte(boolean ? (byte)1 : (byte)0);
                return true;
            case (MapiPropertyType.String8 or MapiPropertyType.Unicode, string text):
                return WriteString(output, text, tag.Type == MapiPropertyType.String8);
            case (MapiPropertyType.Binary, byte[] bytes):
                if (bytes.Length == 0)
                {
                    return false;
                }

                WriteInteger(output, (uint)bytes.Length);
                output.Write(bytes);
                return true;
            case (MapiPropertyType.MultipleInteger32, int[] integers) when integers.Length > 0:
                WriteInteger(output, (uint)integers.Length);
                foreach (var integer in integers)
                {
                    WriteInteger(output, unchecked((uint)integer));
                }

                return true;
            case (MapiPropertyType.MultipleString8 or MapiPropertyType.MultipleUnicode, string[] texts) when texts.Any(text => IsWritable(text, tag.Type == MapiPropertyType.MultipleString8)):
                var values = texts.Where(text => IsWritable(text, tag.Type == MapiPropertyType.MultipleString8)).ToList();
                WriteInteger(output, (uint)values.Count);
                foreach (var text in values)
                {
                    WriteString(output, text, tag.Type == MapiPropertyType.MultipleString8);
                }

                return true;
            case (MapiPropertyType.MultipleBinary, byte[][] binaries) when binaries.Any(binary => binary.Length > 0):
                var items = binaries.Where(binary => binary.Length > 0).ToList();
                WriteInteger(output, (uint)items.Count);
                foreach (var item in items)
                {
                    WriteInteger(output, (uint)item.Length);
                    output.Write(item);
                }

                return true;
            case (MapiPropertyType.MultipleInteger32 or MapiPropertyType.MultipleString8 or MapiPropertyType.MultipleUnicode or MapiPropertyType.MultipleBinary or MapiPropertyType.ObjectReference, _):
                return false;
            default:
                throw new InvalidOperationException($"The value of the property {tag} is a {value.GetType()}, which is not the type of the property.");
        }
    }

    /// <summary>Gets whether a string can be written: not empty, and without a null character, which ends the strings of the format.</summary>
    private static bool IsWritable(string text, bool string8) => text.Length > 0 && !text.Contains('\0', StringComparison.Ordinal) && (!string8 || text.All(c => c <= 0xFF));

    private static bool WriteString(Stream output, string text, bool string8)
    {
        if (!IsWritable(text, string8))
        {
            return false;
        }

        output.Write(string8 ? Encoding.Latin1.GetBytes(text) : Encoding.UTF8.GetBytes(text));
        output.WriteByte(0);
        return true;
    }

    private static void WriteInteger(Stream output, uint value)
    {
        if (value < 0x80)
        {
            output.WriteByte((byte)value);
            return;
        }

        var length = value <= 0xFF ? 1 : value <= 0xFFFF ? 2 : value <= 0xFFFFFF ? 3 : 4;
        output.WriteByte((byte)(0x80 + length));
        for (var i = 0; i < length; i++)
        {
            output.WriteByte((byte)(value >> (8 * i)));
        }
    }

    private static void WriteUInt32(Stream output, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        output.Write(buffer);
    }
}
