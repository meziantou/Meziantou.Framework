using System.Runtime.InteropServices;

namespace Meziantou.Framework.Geo;

/// <summary>
/// A seven-parameter Helmert transformation from the geocentric coordinates of a datum to those of WGS 84, in the
/// position vector convention (EPSG method 9606), as the <c>TOWGS84</c> element of WKT and the <c>+towgs84</c> parameter
/// of PROJ give it.
/// </summary>
/// <param name="TranslationX">The translation along the X axis, in meters.</param>
/// <param name="TranslationY">The translation along the Y axis, in meters.</param>
/// <param name="TranslationZ">The translation along the Z axis, in meters.</param>
/// <param name="RotationX">The rotation around the X axis, in arc-seconds.</param>
/// <param name="RotationY">The rotation around the Y axis, in arc-seconds.</param>
/// <param name="RotationZ">The rotation around the Z axis, in arc-seconds.</param>
/// <param name="ScaleDifference">The scale difference, in parts per million.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct HelmertTransformation(double TranslationX, double TranslationY, double TranslationZ, double RotationX = 0, double RotationY = 0, double RotationZ = 0, double ScaleDifference = 0)
{
    private const double ArcSecondsToRadians = Math.PI / (180 * 3600);

    internal (double X, double Y, double Z) Apply(double x, double y, double z)
    {
        var rx = RotationX * ArcSecondsToRadians;
        var ry = RotationY * ArcSecondsToRadians;
        var rz = RotationZ * ArcSecondsToRadians;
        var scale = 1 + (ScaleDifference * 1e-6);
        return (
            TranslationX + (scale * (x - (rz * y) + (ry * z))),
            TranslationY + (scale * ((rz * x) + y - (rx * z))),
            TranslationZ + (scale * ((-ry * x) + (rx * y) + z)));
    }

    // The inverse uses the transposed rotation matrix, as PROJ does
    internal (double X, double Y, double Z) Invert(double x, double y, double z)
    {
        var rx = RotationX * ArcSecondsToRadians;
        var ry = RotationY * ArcSecondsToRadians;
        var rz = RotationZ * ArcSecondsToRadians;
        var scale = 1 + (ScaleDifference * 1e-6);
        x = (x - TranslationX) / scale;
        y = (y - TranslationY) / scale;
        z = (z - TranslationZ) / scale;
        return (
            x + (rz * y) - (ry * z),
            (-rz * x) + y + (rx * z),
            (ry * x) - (rx * y) + z);
    }
}
