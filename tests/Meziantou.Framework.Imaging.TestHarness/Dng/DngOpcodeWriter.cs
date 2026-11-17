using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.TestHarness.Dng;

/// <summary>Writes the opcode lists of DNG (DNG specification 1.7.1, chapter 7): big-endian, a count then each opcode.</summary>
public sealed class DngOpcodeWriter
{
    private readonly List<byte[]> _opcodes = [];

    /// <summary>Adds an opcode with its raw parameters.</summary>
    public DngOpcodeWriter Add(uint id, byte[] parameters, bool optional = false, byte[]? version = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var opcode = new byte[16 + parameters.Length];
        BinaryPrimitives.WriteUInt32BigEndian(opcode, id);
        (version ?? [1, 3, 0, 0]).CopyTo(opcode, 4);
        BinaryPrimitives.WriteUInt32BigEndian(opcode.AsSpan(8), optional ? 1u : 0u);
        BinaryPrimitives.WriteUInt32BigEndian(opcode.AsSpan(12), (uint)parameters.Length);
        parameters.CopyTo(opcode, 16);
        _opcodes.Add(opcode);
        return this;
    }

    public DngOpcodeWriter WarpRectilinear(double[][] coefficients, double centerX, double centerY)
    {
        var p = new Parameters().WriteInt32(coefficients.Length);
        foreach (var set in coefficients)
        {
            foreach (var value in set)
            {
                p.WriteDouble(value);
            }
        }

        return Add(1, p.WriteDouble(centerX).WriteDouble(centerY).ToArray());
    }

    public DngOpcodeWriter FixVignetteRadial(double[] k, double centerX, double centerY)
    {
        var p = new Parameters();
        foreach (var value in k)
        {
            p.WriteDouble(value);
        }

        return Add(3, p.WriteDouble(centerX).WriteDouble(centerY).ToArray());
    }

    public DngOpcodeWriter FixBadPixelsConstant(uint constant, int phase) => Add(4, new Parameters().WriteInt32((int)constant).WriteInt32(phase).ToArray());

    public DngOpcodeWriter FixBadPixelsList(int phase, (int Row, int Column)[] points, (int Top, int Left, int Bottom, int Right)[] rectangles)
    {
        var p = new Parameters().WriteInt32(phase).WriteInt32(points.Length).WriteInt32(rectangles.Length);
        foreach (var (row, column) in points)
        {
            p.WriteInt32(row).WriteInt32(column);
        }

        foreach (var (top, left, bottom, right) in rectangles)
        {
            p.WriteInt32(top).WriteInt32(left).WriteInt32(bottom).WriteInt32(right);
        }

        return Add(5, p.ToArray());
    }

    public DngOpcodeWriter MapTable(DngOpcodeArea area, ushort[] table)
    {
        var p = area.Write(new Parameters()).WriteInt32(table.Length);
        foreach (var value in table)
        {
            p.WriteUInt16(value);
        }

        return Add(7, p.ToArray());
    }

    public DngOpcodeWriter MapPolynomial(DngOpcodeArea area, double[] coefficients)
    {
        var p = area.Write(new Parameters()).WriteInt32(coefficients.Length - 1);
        foreach (var value in coefficients)
        {
            p.WriteDouble(value);
        }

        return Add(8, p.ToArray());
    }

    public DngOpcodeWriter GainMap(DngOpcodeArea area, int pointsV, int pointsH, double spacingV, double spacingH, double originV, double originH, int mapPlanes, float[] gains)
    {
        var p = area.Write(new Parameters()).WriteInt32(pointsV).WriteInt32(pointsH).WriteDouble(spacingV).WriteDouble(spacingH).WriteDouble(originV).WriteDouble(originH).WriteInt32(mapPlanes);
        foreach (var value in gains)
        {
            p.WriteSingle(value);
        }

        return Add(9, p.ToArray());
    }

    /// <summary>Adds DeltaPerRow (10), DeltaPerColumn (11), ScalePerRow (12) or ScalePerColumn (13).</summary>
    public DngOpcodeWriter PerLine(uint id, DngOpcodeArea area, float[] values)
    {
        var p = area.Write(new Parameters()).WriteInt32(values.Length);
        foreach (var value in values)
        {
            p.WriteSingle(value);
        }

        return Add(id, p.ToArray());
    }

    public byte[] ToArray()
    {
        var result = new List<byte>(new byte[4]);
        BinaryPrimitives.WriteUInt32BigEndian(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(result), (uint)_opcodes.Count);
        foreach (var opcode in _opcodes)
        {
            result.AddRange(opcode);
        }

        return [.. result];
    }

    /// <summary>The big-endian parameters of an opcode.</summary>
    public sealed class Parameters
    {
        private readonly List<byte> _bytes = [];

        public Parameters WriteInt32(int value) => Bytes(BinaryPrimitives.WriteInt32BigEndian, value, 4);

        public Parameters WriteUInt16(ushort value) => Bytes(BinaryPrimitives.WriteUInt16BigEndian, value, 2);

        public Parameters WriteDouble(double value) => Bytes(BinaryPrimitives.WriteDoubleBigEndian, value, 8);

        public Parameters WriteSingle(float value) => Bytes(BinaryPrimitives.WriteSingleBigEndian, value, 4);

        public byte[] ToArray() => [.. _bytes];

        private Parameters Bytes<T>(SpanAction<T> write, T value, int length)
        {
            var bytes = new byte[length];
            write(bytes, value);
            _bytes.AddRange(bytes);
            return this;
        }

        private delegate void SpanAction<in T>(Span<byte> destination, T value);
    }
}
