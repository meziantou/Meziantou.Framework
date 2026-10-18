using System.Buffers.Binary;
using System.Numerics;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Dom.FileApi;

namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// The binary form of a value serialized for storage (https://html.spec.whatwg.org/multipage/structured-data.html#structuredserializeforstorage),
/// e.g. the value of an IndexedDB record: the engine-independent records of <see cref="AgentSerializedValue"/> (the
/// serialized platform objects included, e.g. the bytes of a <see cref="Blob"/>), so that the value can be stored by a
/// backend and deserialized by any realm later.
/// </summary>
/// <remarks>
/// The format starts with a version byte. A value is a tag followed by its data; the objects (records, arrays, byte arrays
/// and platform objects) are numbered in the order of their first occurrence, and the next occurrences are references to
/// their number, so that the shared and the cyclic references are kept.
/// </remarks>
internal static class StructuredValueEncoding
{
    private const byte Version = 1;

    private enum Tag : byte
    {
        Undefined,
        Null,
        False,
        True,
        Number,
        String,
        BigInt,
        Bytes,
        Array,
        Record,
        Platform,
        Reference,
    }

    private enum PlatformTag : byte
    {
        Blob,
        File,
        FileList,
        DomException,
        QuotaExceededError,
        DomPointReadOnly,
        DomPoint,
        DomRectReadOnly,
        DomRect,
        DomQuad,
        DomMatrixReadOnly,
        DomMatrix,
        ImageData,
        FileSystemHandle,
    }

    /// <summary>Encodes a value serialized for storage by a script engine (an <see cref="AgentSerializedValue"/>).</summary>
    /// <exception cref="DomException">A <c>DataCloneError</c> when the value has a platform object that cannot be stored.</exception>
    public static byte[] Encode(AgentSerializedValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var writer = new Writer();
        writer.Buffer.WriteByte(Version);
        writer.Write(value.Root);
        return writer.Buffer.ToArray();
    }

    /// <summary>Decodes a value encoded by <see cref="Encode"/>.</summary>
    /// <exception cref="FormatException">The bytes are not an encoded value.</exception>
    public static AgentSerializedValue Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty || bytes[0] != Version)
            throw new FormatException("The value has an unsupported format");

        var reader = new Reader(bytes[1..].ToArray());
        var root = reader.Read();
        if (reader.Position != reader.Length)
            throw new FormatException("The value has trailing bytes");

        return new AgentSerializedValue(root);
    }

    private sealed class Writer
    {
        private readonly Dictionary<object, int> _objects = new(ReferenceEqualityComparer.Instance);

        public MemoryStream Buffer { get; } = new();

        public void Write(object? value)
        {
            switch (value)
            {
                case null:
                    WriteTag(Tag.Null);
                    return;
                case UndefinedValue:
                    WriteTag(Tag.Undefined);
                    return;
                case bool boolean:
                    WriteTag(boolean ? Tag.True : Tag.False);
                    return;
                case double number:
                    WriteTag(Tag.Number);
                    WriteDouble(number);
                    return;
                case string text:
                    WriteTag(Tag.String);
                    WriteString(text);
                    return;
                case BigInteger bigInt:
                    WriteTag(Tag.BigInt);
                    WriteBytes(bigInt.ToByteArray());
                    return;
            }

            if (_objects.TryGetValue(value, out var index))
            {
                WriteTag(Tag.Reference);
                WriteLength(index);
                return;
            }

            _objects.Add(value, _objects.Count);
            switch (value)
            {
                case byte[] bytes:
                    WriteTag(Tag.Bytes);
                    WriteBytes(bytes);
                    break;
                case object?[] items:
                    WriteTag(Tag.Array);
                    WriteLength(items.Length);
                    foreach (var item in items)
                    {
                        Write(item);
                    }

                    break;
                case ExportedRecord record:
                    WriteTag(Tag.Record);
                    WriteLength(record.Fields.Count);
                    foreach (var (key, field) in record.Fields)
                    {
                        WriteString(key);
                        Write(field);
                    }

                    break;
                case ExportedPlatformObject platformObject:
                    WriteTag(Tag.Platform);
                    WritePlatformObject(platformObject.Value);
                    break;
                default:
                    throw new DomException($"An object of type '{value.GetType().Name}' could not be stored", "DataCloneError");
            }
        }

        private void WritePlatformObject(object? value)
        {
            switch (value)
            {
                case SerializedFileSystemHandle fileSystemHandle:
                    // https://fs.spec.whatwg.org/#filesystemhandle-serialization-steps: the origin and the locator
                    WritePlatformTag(PlatformTag.FileSystemHandle);
                    WriteString(fileSystemHandle.SettingsOrigin);
                    WriteString(fileSystemHandle.Origin);
                    Buffer.WriteByte((byte)fileSystemHandle.Kind);
                    WriteDouble(fileSystemHandle.Path.Count);
                    foreach (var name in fileSystemHandle.Path)
                    {
                        WriteString(name);
                    }

                    break;
                case Dom.FileApi.File file:
                    WritePlatformTag(PlatformTag.File);
                    WriteBytes(file.Data.Span);
                    WriteString(file.Type);
                    WriteString(file.Name);
                    WriteDouble(file.LastModified);
                    break;
                case Blob blob:
                    WritePlatformTag(PlatformTag.Blob);
                    WriteBytes(blob.Data.Span);
                    WriteString(blob.Type);
                    break;
                case FileList files:
                    WritePlatformTag(PlatformTag.FileList);
                    WriteLength((int)files.Length);
                    for (var i = 0u; i < files.Length; i++)
                    {
                        WritePlatformObject(files.Item(i));
                    }

                    break;
                case QuotaExceededError quotaExceededError:
                    WritePlatformTag(PlatformTag.QuotaExceededError);
                    WriteString(quotaExceededError.Message);
                    WriteNullableDouble(quotaExceededError.Quota);
                    WriteNullableDouble(quotaExceededError.Requested);
                    break;
                case DomException exception:
                    WritePlatformTag(PlatformTag.DomException);
                    WriteString(exception.Message);
                    WriteString(exception.Name);
                    break;
                case DomPointReadOnly point:
                    WritePlatformTag(point is DomPoint ? PlatformTag.DomPoint : PlatformTag.DomPointReadOnly);
                    WritePoint(point);
                    break;
                case DomRectReadOnly rect:
                    WritePlatformTag(rect is DomRect ? PlatformTag.DomRect : PlatformTag.DomRectReadOnly);
                    WriteDouble(rect.X);
                    WriteDouble(rect.Y);
                    WriteDouble(rect.Width);
                    WriteDouble(rect.Height);
                    break;
                case DomQuad quad:
                    WritePlatformTag(PlatformTag.DomQuad);
                    WritePoint(quad.P1);
                    WritePoint(quad.P2);
                    WritePoint(quad.P3);
                    WritePoint(quad.P4);
                    break;
                case DomMatrixReadOnly matrix:
                    WritePlatformTag(matrix is DomMatrix ? PlatformTag.DomMatrix : PlatformTag.DomMatrixReadOnly);
                    Buffer.WriteByte(matrix.Is2D ? (byte)1 : (byte)0);
                    foreach (var component in matrix.Is2D ? [matrix.A, matrix.B, matrix.C, matrix.D, matrix.E, matrix.F] : matrix.ToFloat64Array())
                    {
                        WriteDouble(component);
                    }

                    break;
                case ImageData imageData:
                    // https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#the-imagedata-interface: the serialization
                    // steps store the width, the height, the color space, the pixel format and the pixels
                    WritePlatformTag(PlatformTag.ImageData);
                    WriteDouble(imageData.Width);
                    WriteDouble(imageData.Height);
                    Buffer.WriteByte((byte)imageData.ColorSpace);
                    Buffer.WriteByte((byte)imageData.PixelFormat);
                    WriteBytes(imageData.Data.Span);
                    break;
                default:
                    throw new DomException($"An object of type '{value?.GetType().Name}' could not be stored", "DataCloneError");
            }
        }

        private void WritePoint(DomPointReadOnly point)
        {
            WriteDouble(point.X);
            WriteDouble(point.Y);
            WriteDouble(point.Z);
            WriteDouble(point.W);
        }

        private void WriteTag(Tag tag) => Buffer.WriteByte((byte)tag);

        private void WritePlatformTag(PlatformTag tag) => Buffer.WriteByte((byte)tag);

        private void WriteLength(int value)
        {
            var remaining = (uint)value;
            while (remaining >= 0x80)
            {
                Buffer.WriteByte((byte)(remaining | 0x80));
                remaining >>= 7;
            }

            Buffer.WriteByte((byte)remaining);
        }

        private void WriteDouble(double value)
        {
            Span<byte> bytes = stackalloc byte[8];
            BinaryPrimitives.WriteDoubleLittleEndian(bytes, value);
            Buffer.Write(bytes);
        }

        private void WriteNullableDouble(double? value)
        {
            Buffer.WriteByte(value.HasValue ? (byte)1 : (byte)0);
            if (value is { } number)
            {
                WriteDouble(number);
            }
        }

        private void WriteBytes(ReadOnlySpan<byte> bytes)
        {
            WriteLength(bytes.Length);
            Buffer.Write(bytes);
        }

        // The strings are sequences of UTF-16 code units, possibly with lone surrogates
        private void WriteString(string value) => WriteBytes(ToUtf16Bytes(value));

        private static ReadOnlySpan<byte> ToUtf16Bytes(string value)
        {
            var bytes = new byte[value.Length * 2];
            for (var i = 0; i < value.Length; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), value[i]);
            }

            return bytes;
        }
    }

    private sealed class Reader(byte[] bytes)
    {
        private readonly List<object> _objects = [];

        public int Position { get; private set; }

        public int Length => bytes.Length;

        public object? Read()
        {
            switch ((Tag)ReadByte())
            {
                case Tag.Undefined:
                    return UndefinedValue.Instance;
                case Tag.Null:
                    return null;
                case Tag.False:
                    return false;
                case Tag.True:
                    return true;
                case Tag.Number:
                    return ReadDouble();
                case Tag.String:
                    return ReadString();
                case Tag.BigInt:
                    return new BigInteger(ReadBytes());
                case Tag.Reference:
                {
                    var index = ReadLength();
                    return index < _objects.Count ? _objects[index] : throw new FormatException("The value has an invalid reference");
                }

                case Tag.Bytes:
                {
                    var data = ReadBytes();
                    _objects.Add(data);
                    return data;
                }

                case Tag.Array:
                {
                    var items = new object?[ReadLength()];
                    _objects.Add(items);
                    for (var i = 0; i < items.Length; i++)
                    {
                        items[i] = Read();
                    }

                    return items;
                }

                case Tag.Record:
                {
                    var record = new ExportedRecord();
                    _objects.Add(record);
                    var count = ReadLength();
                    for (var i = 0; i < count; i++)
                    {
                        var key = ReadString();
                        record.Fields.Add(new(key, Read()));
                    }

                    return record;
                }

                case Tag.Platform:
                {
                    // The platform object has no reference to the other objects: it is numbered before it is read
                    var index = _objects.Count;
                    _objects.Add(null!);
                    var platformObject = new ExportedPlatformObject(ReadPlatformObject());
                    _objects[index] = platformObject;
                    return platformObject;
                }

                default:
                    throw new FormatException("The value has an invalid tag");
            }
        }

        private object ReadPlatformObject()
        {
            switch ((PlatformTag)ReadByte())
            {
                case PlatformTag.FileSystemHandle:
                {
                    var settingsOrigin = ReadString();
                    var origin = ReadString();
                    var kind = (FileSystemHandleKind)ReadByte();
                    var path = new string[(int)ReadDouble()];
                    for (var i = 0; i < path.Length; i++)
                    {
                        path[i] = ReadString();
                    }

                    return new SerializedFileSystemHandle(settingsOrigin, backend: null, origin, path, kind);
                }

                case PlatformTag.Blob:
                {
                    var data = ReadBytes();
                    return Blob.Create(data, ReadString());
                }

                case PlatformTag.File:
                {
                    var data = ReadBytes();
                    var type = ReadString();
                    var name = ReadString();
                    return Dom.FileApi.File.Create(data, type, name, (long)ReadDouble());
                }

                case PlatformTag.FileList:
                {
                    var files = new List<Dom.FileApi.File>();
                    var count = ReadLength();
                    for (var i = 0; i < count; i++)
                    {
                        files.Add(ReadPlatformObject() as Dom.FileApi.File ?? throw new FormatException("The file list has an invalid file"));
                    }

                    return new FileList(files);
                }

                case PlatformTag.QuotaExceededError:
                {
                    var message = ReadString();
                    var quota = ReadNullableDouble();
                    return new QuotaExceededError(message, quota, ReadNullableDouble());
                }

                case PlatformTag.DomException:
                {
                    var message = ReadString();
                    return new DomException(message, ReadString());
                }

                case PlatformTag.DomPointReadOnly:
                    return new DomPointReadOnly(ReadDouble(), ReadDouble(), ReadDouble(), ReadDouble());
                case PlatformTag.DomPoint:
                    return new DomPoint(ReadDouble(), ReadDouble(), ReadDouble(), ReadDouble());
                case PlatformTag.DomRectReadOnly:
                    return new DomRectReadOnly(ReadDouble(), ReadDouble(), ReadDouble(), ReadDouble());
                case PlatformTag.DomRect:
                    return new DomRect(ReadDouble(), ReadDouble(), ReadDouble(), ReadDouble());
                case PlatformTag.DomQuad:
                    return new DomQuad(ReadPoint(), ReadPoint(), ReadPoint(), ReadPoint());
                case PlatformTag.DomMatrixReadOnly:
                    return new DomMatrixReadOnly(ReadMatrixComponents());
                case PlatformTag.DomMatrix:
                    return new DomMatrix(ReadMatrixComponents());
                case PlatformTag.ImageData:
                {
                    var width = (uint)ReadDouble();
                    var height = (uint)ReadDouble();
                    var colorSpace = (PredefinedColorSpace)ReadByte();
                    var pixelFormat = (ImageDataPixelFormat)ReadByte();
                    return new ImageData(ReadBytes(), width, height, new ImageDataSettings { ColorSpace = colorSpace, PixelFormat = pixelFormat });
                }

                default:
                    throw new FormatException("The value has an invalid platform object");
            }
        }

        private DomPointInit ReadPoint() => new() { X = ReadDouble(), Y = ReadDouble(), Z = ReadDouble(), W = ReadDouble() };

        private double[] ReadMatrixComponents()
        {
            var components = new double[ReadByte() == 1 ? 6 : 16];
            for (var i = 0; i < components.Length; i++)
            {
                components[i] = ReadDouble();
            }

            return components;
        }

        private byte ReadByte() => Position < bytes.Length ? bytes[Position++] : throw new FormatException("The value is truncated");

        private int ReadLength()
        {
            var value = 0u;
            for (var shift = 0; shift < 35; shift += 7)
            {
                var b = ReadByte();
                value |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    return value <= int.MaxValue ? (int)value : throw new FormatException("The value has an invalid length");
            }

            throw new FormatException("The value has an invalid length");
        }

        private double ReadDouble()
        {
            if (Position + 8 > bytes.Length)
                throw new FormatException("The value is truncated");

            var value = BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(Position));
            Position += 8;
            return value;
        }

        private double? ReadNullableDouble() => ReadByte() == 1 ? ReadDouble() : null;

        private byte[] ReadBytes()
        {
            var length = ReadLength();
            if (length > bytes.Length - Position)
                throw new FormatException("The value is truncated");

            var result = bytes.AsSpan(Position, length).ToArray();
            Position += length;
            return result;
        }

        private string ReadString()
        {
            var data = ReadBytes();
            if (data.Length % 2 != 0)
                throw new FormatException("The value has an invalid string");

            var chars = new char[data.Length / 2];
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i * 2));
            }

            return new string(chars);
        }
    }
}
