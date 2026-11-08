using System.Buffers;
using System.Buffers.Binary;

namespace Meziantou.Framework.Geo.Maps.GeoPackage.Internals;

/// <summary>
/// Writes the geometries of GeoPackage (standard GeoPackageBinary): <c>GP</c>, a version, flags, the SRS id, an optional
/// envelope, then the WKB of the geometry. Meziantou.Framework.Geo.GeoPackage reads them.
/// </summary>
internal static class GeoPackageGeometry
{
    private const byte LittleEndianFlag = 0b0000_0001;

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
}
