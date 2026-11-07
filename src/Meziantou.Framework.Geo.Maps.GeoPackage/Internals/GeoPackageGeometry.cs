using System.Buffers;
using System.Buffers.Binary;

namespace Meziantou.Framework.Geo.Maps.GeoPackage.Internals;

/// <summary>
/// The geometries of GeoPackage (standard GeoPackageBinary): <c>GP</c>, a version, flags, the SRS id, an optional
/// envelope, then the WKB of the geometry.
/// </summary>
internal static class GeoPackageGeometry
{
    private const byte LittleEndianFlag = 0b0000_0001;
    private const byte EmptyFlag = 0b0001_0000;
    private const byte ExtendedFlag = 0b0010_0000;

    public static byte[] Write(GeoGeometry geometry, GeoBounds bounds, int srsId)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var hasEnvelope = geometry is not GeoPoint;
        var header = buffer.GetSpan(8 + (hasEnvelope ? 32 : 0));
        header[0] = (byte)'G';
        header[1] = (byte)'P';
        header[2] = 0;

        // Envelope contents indicator 1: [minx, maxx, miny, maxy]; a point has none, its envelope is the point
        header[3] = (byte)(LittleEndianFlag | (hasEnvelope ? 1 << 1 : 0));
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], srsId);
        if (hasEnvelope)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(header[8..], bounds.West);
            BinaryPrimitives.WriteDoubleLittleEndian(header[16..], bounds.East);
            BinaryPrimitives.WriteDoubleLittleEndian(header[24..], bounds.South);
            BinaryPrimitives.WriteDoubleLittleEndian(header[32..], bounds.North);
        }

        buffer.Advance(8 + (hasEnvelope ? 32 : 0));
        WellKnownBinary.Write(geometry, buffer);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Reads a geometry; an empty geometry is <see langword="null"/>.</summary>
    public static GeoGeometry? Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8 || data[0] != 'G' || data[1] != 'P')
            throw new FormatException("The geometry is not a GeoPackage geometry: it does not start with 'GP'.");

        if (data[2] != 0)
            throw new FormatException($"The version {data[2]} of the GeoPackage geometry is not supported.");

        var flags = data[3];
        if ((flags & ExtendedFlag) != 0)
            throw new FormatException("The extended GeoPackage geometries are not supported.");

        if ((flags & EmptyFlag) != 0)
            return null;

        var envelopeSize = ((flags >> 1) & 0b111) switch
        {
            0 => 0,
            1 => 32,
            2 or 3 => 48,
            4 => 64,
            var indicator => throw new FormatException($"The envelope contents indicator {indicator} of the GeoPackage geometry is not valid."),
        };

        if (data.Length < 8 + envelopeSize)
            throw new FormatException("The GeoPackage geometry is truncated.");

        return WellKnownBinary.Read(data[(8 + envelopeSize)..]);
    }
}
