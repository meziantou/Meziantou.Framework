using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A precinct of a tile-component at a resolution level, with the point of the reference grid at which the progressions
/// that follow the position visit it (T.800, B.12.1.3 to B.12.1.5): its top-left corner, or the corner of the tile for the
/// precincts the tile cuts. The decoder reads the packets and the encoder writes them in the order of these points.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct Jpeg2000PacketPosition
{
    public long Y;
    public long X;
    public int Component;
    public int Resolution;
    public int Precinct;

    /// <summary>Computes the position of every precinct of a tile, component by component and level by level.</summary>
    /// <param name="siz">The image and tile size.</param>
    /// <param name="geometry">The partition of the tile.</param>
    /// <param name="positions">Receives <see cref="Jpeg2000TileGeometry.PrecinctCount"/> positions.</param>
    public static void Fill(Jpeg2000Siz siz, Jpeg2000TileGeometry geometry, Span<Jpeg2000PacketPosition> positions)
    {
        var index = 0;
        foreach (var component in geometry.Components)
        {
            var size = siz.Components[component.Index];
            var levels = component.Style.DecompositionLevels;
            for (var r = 0; r <= levels; r++)
            {
                ref readonly var resolution = ref geometry.Resolutions[component.FirstResolution + r];
                var firstColumn = resolution.X0 >> resolution.PrecinctWidthExponent;
                var firstRow = resolution.Y0 >> resolution.PrecinctHeightExponent;

                // The size of a sample of the resolution level on the reference grid
                var scaleX = (long)size.XSubsampling << (levels - r);
                var scaleY = (long)size.YSubsampling << (levels - r);
                for (var py = 0; py < resolution.PrecinctsHigh; py++)
                {
                    // A precinct that starts before the tile is visited at the edge of the tile
                    var top = (firstRow + py) << resolution.PrecinctHeightExponent;
                    var y = top < resolution.Y0 ? geometry.Y0 : top * scaleY;
                    for (var px = 0; px < resolution.PrecinctsWide; px++)
                    {
                        var left = (firstColumn + px) << resolution.PrecinctWidthExponent;
                        var x = left < resolution.X0 ? geometry.X0 : left * scaleX;
                        positions[index++] = new Jpeg2000PacketPosition { Y = y, X = x, Component = component.Index, Resolution = r, Precinct = (py * resolution.PrecinctsWide) + px };
                    }
                }
            }
        }
    }

    /// <summary>Sorts the positions in the order of a progression that follows the position.</summary>
    /// <param name="positions">The positions.</param>
    /// <param name="progression">One of <see cref="Jpeg2000CodingDefaults.ProgressionRpcl"/>, <see cref="Jpeg2000CodingDefaults.ProgressionPcrl"/> and <see cref="Jpeg2000CodingDefaults.ProgressionCprl"/>.</param>
    public static void Sort(Span<Jpeg2000PacketPosition> positions, int progression)
    {
        switch (progression)
        {
            case Jpeg2000CodingDefaults.ProgressionRpcl:
                positions.Sort((a, b) => Compare(a.Resolution, b.Resolution, a.Y, b.Y, a.X, b.X, a.Component, b.Component));
                break;

            case Jpeg2000CodingDefaults.ProgressionPcrl:
                positions.Sort((a, b) => Compare(a.Y, b.Y, a.X, b.X, a.Component, b.Component, a.Resolution, b.Resolution));
                break;

            default:
                positions.Sort((a, b) => Compare(a.Component, b.Component, a.Y, b.Y, a.X, b.X, a.Resolution, b.Resolution));
                break;
        }

        static int Compare(long a1, long b1, long a2, long b2, long a3, long b3, long a4, long b4)
        {
            var result = a1.CompareTo(b1);
            if (result == 0)
            {
                result = a2.CompareTo(b2);
            }

            if (result == 0)
            {
                result = a3.CompareTo(b3);
            }

            return result == 0 ? a4.CompareTo(b4) : result;
        }
    }
}
