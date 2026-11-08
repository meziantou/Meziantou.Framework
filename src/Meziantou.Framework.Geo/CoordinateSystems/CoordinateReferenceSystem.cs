using System.Globalization;
using Meziantou.Framework.Geo.Internals;

namespace Meziantou.Framework.Geo;

/// <summary>
/// A coordinate reference system (CRS): what the two numbers of a position mean. The library works with longitudes and
/// latitudes on WGS 84 (EPSG:4326); a CRS converts its coordinates to and from them, so a position can go from any CRS
/// to any other one through WGS 84.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="GeoPosition"/> holds the coordinates of any CRS in the GIS order: <see cref="GeoPosition.Longitude"/> is
/// the longitude or the easting (x), and <see cref="GeoPosition.Latitude"/> is the latitude or the northing (y), whatever
/// the order of the axes in the definition of the CRS. The altitude is kept by the projections, and changed by the datum
/// shifts, as an ellipsoidal height.
/// </para>
/// <para>
/// Derive from this class to plug a coordinate system that the library does not support: implement
/// <see cref="ToWgs84"/> and <see cref="FromWgs84"/>, for instance with a call to another library.
/// </para>
/// </remarks>
public abstract class CoordinateReferenceSystem
{
    /// <summary>Initializes a new instance of the <see cref="CoordinateReferenceSystem"/> class.</summary>
    /// <param name="name">The name of the coordinate reference system.</param>
    protected CoordinateReferenceSystem(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }

    /// <summary>Gets the longitudes and latitudes of WGS 84, in degrees (EPSG:4326): the coordinates of GPS devices, of GeoJSON and of the library.</summary>
    public static GeographicCoordinateSystem Wgs84 => EpsgCoordinateSystems.Wgs84;

    /// <summary>Gets the Web Mercator projection of web maps, in meters (WGS 84 / Pseudo-Mercator, EPSG:3857).</summary>
    public static ProjectedCoordinateSystem WebMercator => EpsgCoordinateSystems.WebMercator;

    /// <summary>Gets the Lambert 93 projection of metropolitan France, in meters (RGF93 v1 / Lambert-93, EPSG:2154).</summary>
    /// <remarks>RGF93 is taken as equal to WGS 84, as PROJ does without a grid: the difference is below a meter.</remarks>
    public static ProjectedCoordinateSystem Lambert93 => EpsgCoordinateSystems.Lambert93;

    /// <summary>Gets the name of the coordinate reference system.</summary>
    public string Name { get; }

    /// <summary>Gets the code of the coordinate reference system in the EPSG dataset, such as 4326 or 2154, or <see langword="null"/> when it is not known.</summary>
    public int? EpsgCode { get; init; }

    /// <summary>Gets a value that indicates whether the coordinates are a longitude and a latitude, rather than an easting and a northing.</summary>
    public abstract bool IsGeographic { get; }

    /// <summary>Converts a position of this coordinate reference system to a longitude and a latitude of WGS 84.</summary>
    /// <param name="position">The position, in this coordinate reference system.</param>
    /// <returns>The longitude and the latitude on WGS 84, in degrees, the longitude from -180 to 180.</returns>
    public abstract GeoPosition ToWgs84(GeoPosition position);

    /// <summary>Converts a longitude and a latitude of WGS 84 to a position of this coordinate reference system.</summary>
    /// <param name="position">The longitude and the latitude on WGS 84, in degrees.</param>
    /// <returns>The position, in this coordinate reference system.</returns>
    public abstract GeoPosition FromWgs84(GeoPosition position);

    /// <summary>Converts a position of this coordinate reference system to another one, through WGS 84.</summary>
    /// <param name="position">The position, in this coordinate reference system.</param>
    /// <param name="target">The coordinate reference system to convert the position to.</param>
    /// <returns>The position, in <paramref name="target"/>. The position itself when <paramref name="target"/> is this instance.</returns>
    public GeoPosition Transform(GeoPosition position, CoordinateReferenceSystem target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (ReferenceEquals(this, target))
            return position;

        return target.FromWgs84(ToWgs84(position));
    }

    /// <summary>Gets a built-in coordinate reference system by its EPSG code, such as 4326, 3857, 2154 or 32631, as the SRID of EWKT and EWKB gives it.</summary>
    /// <param name="code">The EPSG code. The codes 900913, 3785 and 102100 also give Web Mercator.</param>
    /// <returns>The coordinate reference system, or <see langword="null"/> when the library does not know the code.</returns>
    /// <remarks>
    /// The built-in codes are: 4326 (WGS 84), 4258 (ETRS89), 4171 (RGF93 v1), 4269 (NAD83), 4275 (NTF), 4277 (OSGB36),
    /// 3857 (Web Mercator), 3395 (World Mercator), 32601 to 32660 and 32701 to 32760 (UTM on WGS 84), 25828 to 25838
    /// (UTM on ETRS89), 26901 to 26923 (UTM on NAD83), 2154 (Lambert 93), 3942 to 3950 (the conic conformal zones CC42 to
    /// CC50 of France), 27572 (NTF (Paris) / Lambert zone II, the "Lambert II étendu") and 27700 (British National Grid).
    /// ETRS89, RGF93 and NAD83 are taken as equal to WGS 84 (the difference is below a meter or two); NTF and OSGB36 are
    /// shifted with the Helmert transformation of the EPSG dataset, accurate to a few meters.
    /// </remarks>
    public static CoordinateReferenceSystem? FromEpsg(int code) => EpsgCoordinateSystems.Get(code);

    /// <summary>
    /// Reads the definition of a coordinate reference system in the Well-Known Text of the OGC (WKT 1) or of ESRI, as in
    /// the <c>.prj</c> file of a shapefile, such as <c>PROJCS["RGF_1993_Lambert_93",GEOGCS[...],PROJECTION["Lambert_Conformal_Conic"],...]</c>.
    /// </summary>
    /// <param name="wkt">The definition.</param>
    /// <returns>The coordinate reference system.</returns>
    /// <exception cref="FormatException">The text is not valid WKT, or it is not the definition of a coordinate reference system.</exception>
    /// <exception cref="NotSupportedException">The projection of the definition is not supported.</exception>
    /// <remarks>
    /// <para>
    /// A definition with an EPSG code (<c>AUTHORITY["EPSG","2154"]</c>, or <c>ID["EPSG",2154]</c> in WKT 2) that
    /// <see cref="FromEpsg"/> knows gives that built-in system. Otherwise the system is built from the definition: a
    /// <c>GEOGCS</c> or a <c>PROJCS</c> whose projection is a transverse Mercator, a Lambert conformal conic (1SP or 2SP)
    /// or a Mercator (1SP, 2SP, or the pseudo-Mercator of web maps), with its ellipsoid, its <c>TOWGS84</c> datum shift,
    /// its prime meridian and its units. As GDAL and ESRI write them, the prime meridian is read in degrees, and the
    /// angles of the parameters in the angular unit of the <c>GEOGCS</c>. A <c>COMPD_CS</c> gives its horizontal system.
    /// </para>
    /// <para>
    /// A datum without <c>TOWGS84</c> is taken as equal to WGS 84, except the NTF and OSGB 1936 datums of ESRI
    /// definitions, which get the shifts of <see cref="FromEpsg"/>.
    /// </para>
    /// </remarks>
    public static CoordinateReferenceSystem ParseWkt(string wkt)
    {
        ArgumentNullException.ThrowIfNull(wkt);
        return CoordinateSystemWktReader.Read(wkt);
    }

    /// <summary>Gets the UTM zone of WGS 84 (EPSG:32601 to 32660 in the northern hemisphere, 32701 to 32760 in the southern one).</summary>
    /// <param name="zone">The number of the zone, from 1 (180°W to 174°W) to 60.</param>
    /// <param name="isNorthernHemisphere"><see langword="true"/> for the northern hemisphere, whose northings start at 0 at the equator; <see langword="false"/> for the southern one, whose northings start at 10,000,000 meters.</param>
    /// <returns>The coordinate reference system, in meters.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="zone"/> is not between 1 and 60.</exception>
    public static ProjectedCoordinateSystem Utm(int zone, bool isNorthernHemisphere)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(zone, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(zone, 60);
        return EpsgCoordinateSystems.Utm(Wgs84, "WGS 84", zone, isNorthernHemisphere, (isNorthernHemisphere ? 32600 : 32700) + zone);
    }

    /// <inheritdoc />
    public override string ToString() => EpsgCode is { } code ? Name + " (EPSG:" + code.ToString(CultureInfo.InvariantCulture) + ")" : Name;
}
