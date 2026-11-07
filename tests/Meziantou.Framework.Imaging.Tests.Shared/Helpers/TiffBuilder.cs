using System.Buffers.Binary;
using System.Text;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>Builds TIFF-structured EXIF test data: IFD0, optional Exif IFD, optional IFD1 thumbnail, trailing bytes.</summary>
internal sealed class TiffBuilder(bool bigEndian)
{
    public static TiffEntry Short(ushort tag, ushort value) => new(tag, ExifTiff.TypeShort, 1, be => be ? [(byte)(value >> 8), (byte)value] : [(byte)value, (byte)(value >> 8)]);

    public static TiffEntry Long(ushort tag, uint value) => new(tag, ExifTiff.TypeLong, 1, be =>
    {
        var bytes = new byte[4];
        if (be)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        }

        return bytes;
    });

    public static TiffEntry Ascii(ushort tag, string value) => new(tag, 2, (uint)value.Length + 1, _ => [.. Encoding.ASCII.GetBytes(value), 0]);

    private TiffEntry[] _ifd0 = [];
    private TiffEntry[]? _exif;
    private byte[]? _jpegThumbnail;
    private byte[][]? _strips;
    private byte[] _trailer = [];

    public TiffBuilder Ifd0(params TiffEntry[] entries)
    {
        _ifd0 = entries;
        return this;
    }

    public TiffBuilder ExifIfd(params TiffEntry[] entries)
    {
        _exif = entries;
        return this;
    }

    public TiffBuilder Thumbnail(byte[] jpeg)
    {
        _jpegThumbnail = jpeg;
        return this;
    }

    public TiffBuilder StripThumbnail(params byte[][] strips)
    {
        _strips = strips;
        return this;
    }

    public TiffBuilder Trailer(byte[] trailer)
    {
        _trailer = trailer;
        return this;
    }

    public byte[] Build()
    {
        var buffer = new List<byte>(bigEndian ? "MM\0*"u8.ToArray() : "II*\0"u8.ToArray());
        AddUInt32(buffer, 8);

        var ifd0 = _ifd0.ToList();
        var exifPointerIndex = -1;
        if (_exif is not null)
        {
            ifd0.Add(new TiffEntry(ExifTiff.ExifIfdPointerTag, ExifTiff.TypeLong, 1, _ => new byte[4]));
        }

        ifd0 = [.. ifd0.OrderBy(entry => entry.Tag)];
        exifPointerIndex = ifd0.FindIndex(entry => entry.Tag == ExifTiff.ExifIfdPointerTag);
        var ifd0Position = WriteIfd(buffer, ifd0);

        if (_exif is not null)
        {
            Align(buffer);
            var exifPosition = WriteIfd(buffer, [.. _exif.OrderBy(entry => entry.Tag)]);
            PatchUInt32(buffer, ifd0Position + 2 + (exifPointerIndex * 12) + 8, (uint)exifPosition);
        }

        if (_jpegThumbnail is not null || _strips is not null)
        {
            Align(buffer);
            List<TiffEntry> ifd1;
            if (_jpegThumbnail is not null)
            {
                ifd1 = [Long(ExifTiff.JpegInterchangeFormatTag, 0), Long(ExifTiff.JpegInterchangeFormatLengthTag, (uint)_jpegThumbnail.Length)];
            }
            else
            {
                ifd1 = [Short(ExifTiff.StripOffsetsTag, 0), Short(ExifTiff.StripByteCountsTag, 0)];
            }

            var ifd1Position = WriteIfd(buffer, ifd1);
            PatchUInt32(buffer, ifd0Position + 2 + (ifd0.Count * 12), (uint)ifd1Position);
            if (_jpegThumbnail is not null)
            {
                PatchUInt32(buffer, ifd1Position + 2 + 8, (uint)buffer.Count);
                buffer.AddRange(_jpegThumbnail);
            }
            else
            {
                // Two strips: offsets/counts arrays stored out of line (2 SHORT values fit inline: 4 bytes)
                var offsets = new List<ushort>();
                foreach (var strip in _strips!)
                {
                    offsets.Add((ushort)buffer.Count);
                    buffer.AddRange(strip);
                }

                PatchEntry(buffer, ifd1Position, 0, (uint)offsets.Count, offsets);
                PatchEntry(buffer, ifd1Position, 1, (uint)offsets.Count, [.. _strips.Select(strip => (ushort)strip.Length)]);
            }
        }

        buffer.AddRange(_trailer);
        return [.. buffer];
    }

    private int WriteIfd(List<byte> buffer, List<TiffEntry> entries)
    {
        var position = buffer.Count;
        AddUInt16(buffer, (ushort)entries.Count);
        var valuesPosition = position + 2 + (entries.Count * 12) + 4;
        var values = new List<byte>();
        foreach (var entry in entries)
        {
            AddUInt16(buffer, entry.Tag);
            AddUInt16(buffer, entry.Type);
            AddUInt32(buffer, entry.Count);
            var payload = entry.Payload(bigEndian);
            if (payload.Length <= 4)
            {
                buffer.AddRange(payload);
                buffer.AddRange(new byte[4 - payload.Length]);
            }
            else
            {
                AddUInt32(buffer, (uint)(valuesPosition + values.Count));
                values.AddRange(payload);
                if (values.Count % 2 != 0)
                {
                    values.Add(0);
                }
            }
        }

        AddUInt32(buffer, 0);
        buffer.AddRange(values);
        return position;
    }

    private void PatchEntry(List<byte> buffer, int ifdPosition, int index, uint count, List<ushort> values)
    {
        var entry = ifdPosition + 2 + (index * 12);
        PatchUInt32(buffer, entry + 4, count);
        for (var i = 0; i < values.Count; i++)
        {
            var bytes = bigEndian ? [(byte)(values[i] >> 8), (byte)values[i]] : new[] { (byte)values[i], (byte)(values[i] >> 8) };
            buffer[entry + 8 + (2 * i)] = bytes[0];
            buffer[entry + 9 + (2 * i)] = bytes[1];
        }
    }

    private static void Align(List<byte> buffer)
    {
        if (buffer.Count % 2 != 0)
        {
            buffer.Add(0);
        }
    }

    private void AddUInt16(List<byte> buffer, ushort value)
    {
        var bytes = new byte[2];
        if (bigEndian)
        {
            BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        }

        buffer.AddRange(bytes);
    }

    private void AddUInt32(List<byte> buffer, uint value)
    {
        var bytes = new byte[4];
        if (bigEndian)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        }

        buffer.AddRange(bytes);
    }

    private void PatchUInt32(List<byte> buffer, int position, uint value)
    {
        var bytes = new byte[4];
        if (bigEndian)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        }

        for (var i = 0; i < 4; i++)
        {
            buffer[position + i] = bytes[i];
        }
    }
}
