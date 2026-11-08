using System.Globalization;

namespace Meziantou.Framework.Geo.Internals;

/// <summary>The coordinate reference systems that <see cref="CoordinateReferenceSystem.FromEpsg"/> knows, with the parameters of the EPSG dataset.</summary>
internal static class EpsgCoordinateSystems
{
    // NTF to WGS 84 (EPSG:1193) and OSGB36 to WGS 84 (EPSG:1314)
    public static readonly HelmertTransformation NtfToWgs84 = new(-168, -60, 320);
    public static readonly HelmertTransformation Osgb36ToWgs84 = new(446.448, -125.157, 542.06, 0.15, 0.247, 0.842, -20.489);

    public static readonly GeographicCoordinateSystem Wgs84 = new("WGS 84", Ellipsoid.Wgs84) { EpsgCode = 4326 };

    public static readonly GeographicCoordinateSystem Rgf93 = new("RGF93 v1", Ellipsoid.Grs80) { EpsgCode = 4171 };

    public static readonly ProjectedCoordinateSystem WebMercator = new("WGS 84 / Pseudo-Mercator", Wgs84, new MercatorConversion { IsSpherical = true }) { EpsgCode = 3857 };

    public static readonly ProjectedCoordinateSystem Lambert93 = ConicConformalFrance("RGF93 v1 / Lambert-93", 46.5, 49, 44, 6_600_000, 2154);

    public static GeographicCoordinateSystem Ntf => new("NTF", Ellipsoid.Clarke1880Ign) { EpsgCode = 4275, ToWgs84Transformation = NtfToWgs84 };

    public static GeographicCoordinateSystem Osgb36 => new("OSGB36", Ellipsoid.Airy1830) { EpsgCode = 4277, ToWgs84Transformation = Osgb36ToWgs84 };

    public static CoordinateReferenceSystem? Get(int code)
    {
        return code switch
        {
            4326 => Wgs84,
            4171 => Rgf93,
            4258 => Etrs89(),
            4269 => Nad83(),
            4275 => Ntf,
            4277 => Osgb36,
            3857 or 3785 or 900913 or 102100 or 102113 => WebMercator,
            3395 => new ProjectedCoordinateSystem("WGS 84 / World Mercator", Wgs84, new MercatorConversion()) { EpsgCode = 3395 },
            >= 32601 and <= 32660 => CoordinateReferenceSystem.Utm(code - 32600, isNorthernHemisphere: true),
            >= 32701 and <= 32760 => CoordinateReferenceSystem.Utm(code - 32700, isNorthernHemisphere: false),
            >= 25828 and <= 25838 => Utm(Etrs89(), "ETRS89", code - 25800, isNorthernHemisphere: true, code),
            >= 26901 and <= 26923 => Utm(Nad83(), "NAD83", code - 26900, isNorthernHemisphere: true, code),
            2154 => Lambert93,
            >= 3942 and <= 3950 => ConicConformalFrance(
                "RGF93 v1 / CC" + (code - 3900).ToString(CultureInfo.InvariantCulture),
                latitudeOfOrigin: code - 3900,
                standardParallel1: code - 3900 - 0.75,
                standardParallel2: code - 3900 + 0.75,
                falseNorthing: ((code - 3941) * 1_000_000) + 200_000,
                code),
            27572 => new ProjectedCoordinateSystem(
                "NTF (Paris) / Lambert zone II",
                new GeographicCoordinateSystem("NTF (Paris)", Ellipsoid.Clarke1880Ign) { EpsgCode = 4807, PrimeMeridian = 2.33722917, ToWgs84Transformation = NtfToWgs84 },
                new LambertConformalConicConversion { LatitudeOfOrigin = 46.8, ScaleFactor = 0.99987742, FalseEasting = 600_000, FalseNorthing = 2_200_000 })
            {
                EpsgCode = 27572,
            },
            27700 => new ProjectedCoordinateSystem(
                "OSGB36 / British National Grid",
                Osgb36,
                new TransverseMercatorConversion { LatitudeOfOrigin = 49, CentralMeridian = -2, ScaleFactor = 0.9996012717, FalseEasting = 400_000, FalseNorthing = -100_000 })
            {
                EpsgCode = 27700,
            },
            _ => null,
        };
    }

    public static ProjectedCoordinateSystem Utm(GeographicCoordinateSystem geographic, string datumName, int zone, bool isNorthernHemisphere, int code)
    {
        var name = datumName + " / UTM zone " + zone.ToString(CultureInfo.InvariantCulture) + (isNorthernHemisphere ? "N" : "S");
        var conversion = new TransverseMercatorConversion
        {
            CentralMeridian = (zone * 6) - 183,
            ScaleFactor = 0.9996,
            FalseEasting = 500_000,
            FalseNorthing = isNorthernHemisphere ? 0 : 10_000_000,
        };

        return new ProjectedCoordinateSystem(name, geographic, conversion) { EpsgCode = code };
    }

    private static GeographicCoordinateSystem Etrs89() => new("ETRS89", Ellipsoid.Grs80) { EpsgCode = 4258 };

    private static GeographicCoordinateSystem Nad83() => new("NAD83", Ellipsoid.Grs80) { EpsgCode = 4269 };

    private static ProjectedCoordinateSystem ConicConformalFrance(string name, double latitudeOfOrigin, double standardParallel1, double standardParallel2, double falseNorthing, int code)
    {
        var conversion = new LambertConformalConicConversion
        {
            LatitudeOfOrigin = latitudeOfOrigin,
            CentralMeridian = 3,
            StandardParallel1 = standardParallel1,
            StandardParallel2 = standardParallel2,
            FalseEasting = code is 2154 ? 700_000 : 1_700_000,
            FalseNorthing = falseNorthing,
        };

        return new ProjectedCoordinateSystem(name, Rgf93, conversion) { EpsgCode = code };
    }
}
