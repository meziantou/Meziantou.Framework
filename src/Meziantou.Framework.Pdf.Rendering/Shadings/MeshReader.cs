using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering.Functions;

namespace Meziantou.Framework.Pdf.Rendering.Shadings;

/// <summary>
/// Reads the data of a mesh shading (ISO 32000-2, 8.7.4.5.5 to 8.7.4.5.8) into triangles: the free-form and lattice
/// triangle meshes as they are, the Coons and tensor-product patches divided into a grid of triangles.
/// </summary>
internal static class MeshReader
{
    // The triangles a patch is divided into, per side
    private const int PatchDivisions = 16;

    public static Mesh Read(PdfStream stream, int componentCount, bool hasFunction, int maxItems)
    {
        var dictionary = stream.Dictionary;
        var type = (int)dictionary.GetInteger(PdfName.Create("ShadingType"), 0);
        var bitsPerCoordinate = (int)dictionary.GetInteger(PdfName.Create("BitsPerCoordinate"), 0);
        var bitsPerComponent = (int)dictionary.GetInteger(PdfName.Create("BitsPerComponent"), 0);
        var bitsPerFlag = (int)dictionary.GetInteger(PdfName.Create("BitsPerFlag"), 8);
        if (bitsPerCoordinate is not (1 or 2 or 4 or 8 or 12 or 16 or 24 or 32) || bitsPerComponent is not (1 or 2 or 4 or 8 or 12 or 16))
            throw new InvalidPdfContentException("A mesh shading has invalid bit depths");

        var colorCount = hasFunction ? 1 : componentCount;
        var decode = RenderFunction.ReadNumbers(dictionary.Get<PdfArray>(PdfNames.Decode));
        if (decode is null || decode.Length < 4 + (2 * colorCount))
            throw new InvalidPdfContentException("A mesh shading has an invalid /Decode array");

        var domain = hasFunction ? RenderFunction.ReadNumbers(dictionary.Get<PdfArray>(PdfName.Create("Domain"))) is { Length: 2 } d ? d : [decode[4], decode[5]] : [0, 1];
        var reader = new BitReader(stream.GetDecodedData());
        var mesh = new Mesh(domain);

        MeshVertex ReadVertex()
        {
            var x = Decode(reader.Read(bitsPerCoordinate), bitsPerCoordinate, decode[0], decode[1]);
            var y = Decode(reader.Read(bitsPerCoordinate), bitsPerCoordinate, decode[2], decode[3]);
            return new MeshVertex(x, y, ReadColor());
        }

        double[] ReadColor()
        {
            var color = new double[colorCount];
            for (var i = 0; i < colorCount; i++)
            {
                color[i] = Decode(reader.Read(bitsPerComponent), bitsPerComponent, decode[4 + (2 * i)], decode[5 + (2 * i)]);
            }

            return color;
        }

        (double X, double Y) ReadPoint()
        {
            var x = Decode(reader.Read(bitsPerCoordinate), bitsPerCoordinate, decode[0], decode[1]);
            var y = Decode(reader.Read(bitsPerCoordinate), bitsPerCoordinate, decode[2], decode[3]);
            return (x, y);
        }

        var items = 0;
        switch (type)
        {
            case 4:
                {
                    // A vertex of flag 0 starts a triangle of three new vertices (the flags of the two others are not
                    // used); flags 1 and 2 make a triangle of the vertex and two vertices of the previous triangle
                    var pending = new List<MeshVertex>(3);
                    MeshTriangle? last = null;
                    while (reader.HasData && items++ < maxItems && mesh.Triangles.Count < maxItems)
                    {
                        var flag = reader.Read(bitsPerFlag);
                        var vertex = ReadVertex();
                        reader.AlignToByte();
                        if (reader.IsPastEnd)
                            break;

                        if (pending.Count == 0 && last is not null && flag is 1 or 2)
                        {
                            last = flag == 1 ? new MeshTriangle(last.B, last.C, vertex) : new MeshTriangle(last.A, last.C, vertex);
                            mesh.Triangles.Add(last);
                            continue;
                        }

                        pending.Add(vertex);
                        if (pending.Count == 3)
                        {
                            last = new MeshTriangle(pending[0], pending[1], pending[2]);
                            mesh.Triangles.Add(last);
                            pending.Clear();
                        }
                    }

                    break;
                }

            case 5:
                {
                    var perRow = (int)dictionary.GetInteger(PdfName.Create("VerticesPerRow"), 0);
                    if (perRow < 2)
                        throw new InvalidPdfContentException("A lattice-form mesh has fewer than two vertices per row");

                    if (perRow > maxItems)
                        throw new PdfResourceLimitException("A lattice-form mesh has too many vertices per row");

                    MeshVertex[]? previous = null;
                    while (reader.HasData && items < maxItems && mesh.Triangles.Count < maxItems)
                    {
                        var row = new MeshVertex[perRow];
                        for (var i = 0; i < perRow; i++)
                        {
                            row[i] = ReadVertex();
                            items++;
                        }

                        if (reader.IsPastEnd)
                            break;

                        if (previous is not null)
                        {
                            for (var i = 0; i + 1 < perRow; i++)
                            {
                                mesh.Triangles.Add(new MeshTriangle(previous[i], previous[i + 1], row[i]));
                                mesh.Triangles.Add(new MeshTriangle(previous[i + 1], row[i + 1], row[i]));
                            }
                        }

                        previous = row;
                    }

                    break;
                }

            case 6 or 7:
                {
                    var tensor = type == 7;
                    (double X, double Y)[]? previousPoints = null;
                    double[][]? previousColors = null;
                    while (reader.HasData && items++ < maxItems && mesh.Triangles.Count < maxItems)
                    {
                        var flag = reader.Read(bitsPerFlag);
                        var points = new (double X, double Y)[16];
                        var colors = new double[4][];
                        var start = 0;
                        if (flag != 0 && previousPoints is not null && previousColors is not null)
                        {
                            // The first edge is an edge of the previous patch (8.7.4.5.7, table 85)
                            var (edge, color0, color1) = flag switch
                            {
                                1 => (new[] { 3, 4, 5, 6 }, 1, 2),
                                2 => (new[] { 6, 7, 8, 9 }, 2, 3),
                                _ => (new[] { 9, 10, 11, 0 }, 3, 0),
                            };
                            for (var i = 0; i < 4; i++)
                            {
                                points[i] = previousPoints[edge[i]];
                            }

                            colors[0] = previousColors[color0];
                            colors[1] = previousColors[color1];
                            start = 4;
                        }

                        for (var i = start; i < 12; i++)
                        {
                            points[i] = ReadPoint();
                        }

                        if (tensor)
                        {
                            for (var i = 12; i < 16; i++)
                            {
                                points[i] = ReadPoint();
                            }
                        }

                        for (var i = start == 0 ? 0 : 2; i < 4; i++)
                        {
                            colors[i] = ReadColor();
                        }

                        reader.AlignToByte();
                        if (reader.IsPastEnd)
                            break;

                        if (!tensor)
                        {
                            ComputeCoonsInnerPoints(points);
                        }

                        AddPatch(mesh, points, colors);
                        previousPoints = points;
                        previousColors = colors;
                    }

                    break;
                }
        }

        return mesh;
    }

    private static double Decode(uint value, int bits, double low, double high)
    {
        var max = bits == 32 ? uint.MaxValue : (1u << bits) - 1;
        return low + (value * (high - low) / max);
    }

    /// <summary>The grid p[i][j] of a patch from the twelve boundary points and the four inner points (p11, p12, p22, p21).</summary>
    private static (double X, double Y)[][] Grid((double X, double Y)[] points)
    {
        var p = new (double X, double Y)[4][];
        for (var i = 0; i < 4; i++)
        {
            p[i] = new (double X, double Y)[4];
        }

        p[0][0] = points[0];
        p[0][1] = points[1];
        p[0][2] = points[2];
        p[0][3] = points[3];
        p[1][3] = points[4];
        p[2][3] = points[5];
        p[3][3] = points[6];
        p[3][2] = points[7];
        p[3][1] = points[8];
        p[3][0] = points[9];
        p[2][0] = points[10];
        p[1][0] = points[11];
        p[1][1] = points[12];
        p[1][2] = points[13];
        p[2][2] = points[14];
        p[2][1] = points[15];
        return p;
    }

    /// <summary>Computes the inner points of a Coons patch: the tensor-product patch that has the same surface (8.7.4.5.8).</summary>
    private static void ComputeCoonsInnerPoints((double X, double Y)[] points)
    {
        var p = Grid(points);
        (double X, double Y) Inner(int i, int j)
        {
            var i0 = i == 1 ? 0 : 3;
            var i3 = 3 - i0;
            var j0 = j == 1 ? 0 : 3;
            var j3 = 3 - j0;
            double Combine(Func<(double X, double Y), double> axis) =>
                ((-4 * axis(p[i0][j0])) + (6 * (axis(p[i0][j]) + axis(p[i][j0]))) - (2 * (axis(p[i0][j3]) + axis(p[i3][j0]))) + (3 * (axis(p[i3][j]) + axis(p[i][j3]))) - axis(p[i3][j3])) / 9;
            return (Combine(point => point.X), Combine(point => point.Y));
        }

        points[12] = Inner(1, 1);
        points[13] = Inner(1, 2);
        points[14] = Inner(2, 2);
        points[15] = Inner(2, 1);
    }

    /// <summary>Divides a patch into triangles: its surface (the tensor product of cubic Bézier curves) evaluated on a grid, the colors interpolated between the corners.</summary>
    private static void AddPatch(Mesh mesh, (double X, double Y)[] points, double[][] colors)
    {
        var p = Grid(points);
        static double Bernstein(int i, double t) => i switch
        {
            0 => (1 - t) * (1 - t) * (1 - t),
            1 => 3 * t * (1 - t) * (1 - t),
            2 => 3 * t * t * (1 - t),
            _ => t * t * t,
        };

        var grid = new MeshVertex[PatchDivisions + 1][];
        for (var a = 0; a <= PatchDivisions; a++)
        {
            grid[a] = new MeshVertex[PatchDivisions + 1];
            var u = (double)a / PatchDivisions;
            for (var b = 0; b <= PatchDivisions; b++)
            {
                var v = (double)b / PatchDivisions;
                double x = 0;
                double y = 0;
                for (var i = 0; i < 4; i++)
                {
                    for (var j = 0; j < 4; j++)
                    {
                        var weight = Bernstein(i, u) * Bernstein(j, v);
                        x += p[i][j].X * weight;
                        y += p[i][j].Y * weight;
                    }
                }

                // The colors of the corners: c00 at (0, 0), c03 at (0, 1), c33 at (1, 1), c30 at (1, 0)
                var color = new double[colors[0].Length];
                for (var k = 0; k < color.Length; k++)
                {
                    color[k] = ((1 - u) * (1 - v) * colors[0][k]) + ((1 - u) * v * colors[1][k]) + (u * v * colors[2][k]) + (u * (1 - v) * colors[3][k]);
                }

                grid[a][b] = new MeshVertex(x, y, color);
            }
        }

        for (var a = 0; a < PatchDivisions; a++)
        {
            for (var b = 0; b < PatchDivisions; b++)
            {
                mesh.Triangles.Add(new MeshTriangle(grid[a][b], grid[a + 1][b], grid[a][b + 1]));
                mesh.Triangles.Add(new MeshTriangle(grid[a + 1][b], grid[a + 1][b + 1], grid[a][b + 1]));
            }
        }
    }

    /// <summary>Reads values of 1 to 32 bits, most significant bit first.</summary>
    private sealed class BitReader(byte[] data)
    {
        private long _position;

        public bool HasData => _position < (long)data.Length * 8;

        public bool IsPastEnd => _position > (long)data.Length * 8;

        public uint Read(int bits)
        {
            uint value = 0;
            for (var i = 0; i < bits; i++, _position++)
            {
                var index = _position >> 3;
                var bit = index < data.Length ? (data[index] >> (7 - (int)(_position & 7))) & 1 : 0;
                value = (value << 1) | (uint)bit;
            }

            return value;
        }

        public void AlignToByte() => _position = (_position + 7) & ~7L;
    }
}
