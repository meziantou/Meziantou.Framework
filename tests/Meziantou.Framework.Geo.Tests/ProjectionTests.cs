namespace Meziantou.Framework.Geo.Tests;

public sealed class ProjectionTests
{
    [Fact]
    public void Projection_Equirectangular()
    {
        var projection = new EquirectangularProjection();
        Assert.Equal((Math.PI, Math.PI / 2), projection.Project(180, 90));
        Assert.Equal((-Math.PI / 4, -Math.PI / 6), projection.Project(-45, -30));
    }

    [Fact]
    public void Projection_Mercator()
    {
        // y = asinh(tan(latitude)); the latitudes are limited to atan(sinh(π)), where y is π, as x is at 180°
        var projection = new MercatorProjection();
        Assert.Equal(0, projection.Project(0, 0).Y, 1e-15);
        var (x, y) = projection.Project(90, 45);
        Assert.Equal(Math.PI / 2, x, 1e-12);
        Assert.Equal(0.881373587019543, y, 1e-12);
        Assert.Equal(Math.PI, projection.Project(0, 89).Y, 1e-9);
        Assert.Equal(-Math.PI, projection.Project(0, -90).Y, 1e-9);

        // Conformal: the scale along the meridians is the scale along the parallels, 1 / cos(latitude)
        Assert.Equal(1 / Math.Cos(Math.PI / 3), Derivative(projection, 10, 60, alongLongitude: false).Length, 1e-6);
        Assert.Equal(1 / Math.Cos(Math.PI / 3), Derivative(projection, 10, 60, alongLongitude: true).Length / Math.Cos(Math.PI / 3), 1e-6);
    }

    [Fact]
    public void Projection_EqualEarth()
    {
        // At the equator, θ = 0 and x = λ / (M A1); at the poles, sin θ = M = √3/2, so θ = π/3 and y = θ (A1 + A2 θ² + θ⁶ (A3 + A4 θ²))
        var projection = new EqualEarthProjection();
        const double M = 0.8660254037844386;
        Assert.Equal(Math.PI / (M * 1.340264), projection.Project(180, 0).X, 1e-12);
        var theta = Math.PI / 3;
        var pole = theta * (1.340264 - (0.081106 * theta * theta) + (Math.Pow(theta, 6) * (0.000893 + (0.003796 * theta * theta))));
        Assert.Equal(1.3173627, pole, 1e-7);
        Assert.Equal(pole, projection.Project(0, 90).Y, 1e-12);
        Assert.Equal(-pole, projection.Project(0, -90).Y, 1e-12);
        AssertEqualArea(projection);
    }

    [Fact]
    public void Projection_Albers()
    {
        var projection = new AlbersProjection { Parallel1 = 0, Parallel2 = 60, CentralMeridian = 0 };
        Assert.Equal((0.0, 0.0), projection.Project(0, 0));

        // n = sin(60°) / 2, C = 1, ρ0 = 1 / n; at 30°: ρ = √(1 - 2n sin(30°)) / n
        var n = Math.Sqrt(3) / 4;
        Assert.Equal((1 / n) - (Math.Sqrt(1 - n) / n), projection.Project(0, 30).Y, 1e-12);
        AssertEqualArea(projection);

        // No distortion along the standard parallels: the length of a degree of longitude is cos(latitude)
        Assert.Equal(1, Derivative(projection, 20, 0, alongLongitude: true).Length, 1e-6);
        Assert.Equal(0.5, Derivative(projection, 20, 60, alongLongitude: true).Length, 1e-6);

        // The longitudes are measured from the central meridian
        var centered = new AlbersProjection { Parallel1 = 0, Parallel2 = 60, CentralMeridian = 100 };
        Assert.Equal(projection.Project(-170, 20).X, centered.Project(-70, 20).X, 1e-12);
        Assert.Equal(projection.Project(170, 20).X, centered.Project(-90, 20).X, 1e-12);
        Assert.Equal(projection.Project(10, 20).X, centered.Project(110, 20).X, 1e-12);

        // Parallels symmetric around the equator: the cylindrical equal-area projection
        var cylindrical = new AlbersProjection { Parallel1 = -30, Parallel2 = 30, CentralMeridian = 0 };
        var (x, y) = cylindrical.Project(90, 30);
        Assert.Equal(Math.PI / 2 * Math.Cos(Math.PI / 6), x, 1e-12);
        Assert.Equal(0.5 / Math.Cos(Math.PI / 6), y, 1e-12);
    }

    [Fact]
    public void Projection_AzimuthalEquidistant()
    {
        // The North Pole is the center; the distance from it is true, and the central meridian points down
        var north = new AzimuthalEquidistantProjection();
        Assert.Equal((0.0, 0.0), north.Project(0, 90));
        AssertPoint((0, -Math.PI / 2), north.Project(0, 0));
        AssertPoint((Math.PI / 2, 0), north.Project(90, 0));
        AssertPoint((0, Math.PI / 3), north.Project(180, 30));
        foreach (var longitude in new[] { -170.0, -45, 0, 60, 135 })
        {
            var (x, y) = north.Project(longitude, 20);
            Assert.Equal(70 * Math.PI / 180, Math.Sqrt((x * x) + (y * y)), 1e-12);
        }

        // The opposite pole is the edge of the map, a circle of radius π
        var (edgeX, edgeY) = north.Project(45, -90);
        Assert.Equal(Math.PI, Math.Sqrt((edgeX * edgeX) + (edgeY * edgeY)), 1e-12);

        // The South Pole is the center, and the central meridian points up
        var south = new AzimuthalEquidistantProjection { SouthPole = true };
        Assert.Equal((0.0, 0.0), south.Project(0, -90));
        AssertPoint((0, Math.PI / 2), south.Project(0, 0));
        AssertPoint((Math.PI / 6, 0), south.Project(90, -60));

        // The angles are measured from the central meridian
        var centered = new AzimuthalEquidistantProjection { SouthPole = true, CentralMeridian = 100 };
        AssertPoint((0, Math.PI / 2), centered.Project(100, 0));
        AssertPoint(south.Project(-60, -70), centered.Project(40, -70));
        Assert.Equal(100, centered.CenterLongitude);
        Assert.True(centered.HasStraightMeridians);
        Assert.False(centered.HasStraightParallels);
        Assert.Equal(90, centered.MaxLatitude);
    }

    [Theory]
    [InlineData("Equirectangular")]
    [InlineData("Mercator")]
    [InlineData("EqualEarth")]
    [InlineData("Albers")]
    [InlineData("AlbersCylindrical")]
    [InlineData("AzimuthalNorth")]
    [InlineData("AzimuthalSouth")]
    public void Invert_RoundTrips(string name)
    {
        GeoProjection projection = name switch
        {
            "Equirectangular" => new EquirectangularProjection(),
            "Mercator" => new MercatorProjection(),
            "EqualEarth" => new EqualEarthProjection(),
            "Albers" => new AlbersProjection(),
            "AzimuthalNorth" => new AzimuthalEquidistantProjection(),
            "AzimuthalSouth" => new AzimuthalEquidistantProjection { SouthPole = true, CentralMeridian = 30 },
            _ => new AlbersProjection { Parallel1 = -30, Parallel2 = 30, CentralMeridian = 20 },
        };
        var longitudes = projection is AlbersProjection albers ? new[] { albers.CentralMeridian - 60, albers.CentralMeridian, albers.CentralMeridian + 45 } : [-179.0, -100, 0, 45.5, 179];
        foreach (var longitude in longitudes)
        {
            foreach (var latitude in new[] { -80.0, -33.3, 0, 12, 60, 85 })
            {
                var (x, y) = projection.Project(longitude, latitude);
                var position = projection.Invert(x, y);
                Assert.NotNull(position);
                Assert.Equal(Math.IEEERemainder(longitude, 360), position.Value.Longitude, 1e-9);
                Assert.Equal(Math.Clamp(latitude, -projection.MaxLatitude, projection.MaxLatitude), position.Value.Latitude, 1e-9);
            }
        }
    }

    [Fact]
    public void Invert_OutsideOfTheMap()
    {
        Assert.Null(new EqualEarthProjection().Invert(10, 0));
        Assert.Null(new EqualEarthProjection().Invert(0, 2));
        Assert.Null(new MercatorProjection().Invert(double.NaN, 0));
        Assert.Null(new AlbersProjection().Invert(0, 100));
        Assert.Null(new AzimuthalEquidistantProjection().Invert(3, 2));
        Assert.Equal(new GeoPosition(0, 90), new AzimuthalEquidistantProjection().Invert(0, 0));
        Assert.Null(new CustomProjection().Invert(0, 0));
    }

    [Fact]
    public void Project_Position()
    {
        #region Geo_Project
        var projection = new MercatorProjection();

        // The point is in radii of the Earth, with y increasing to the north
        var (x, y) = projection.Project(new GeoPosition(Longitude: 2.35, Latitude: 48.86));

        // Scale and translate it to draw a map of 512 by 512 pixels, as the tiles of web maps
        var pixelX = 256 + (x * 256 / Math.PI);
        var pixelY = 256 - (y * 256 / Math.PI);
        Assert.Equal(259.34, pixelX, 0.01);
        Assert.Equal(176.14, pixelY, 0.01);

        // Invert converts a point back to a position
        var position = projection.Invert(x, y);
        Assert.Equal(48.86, position!.Value.Latitude, 1e-9);
        #endregion
    }

    [Fact]
    public void CustomProjection_UsesTheDefaults()
    {
        var projection = new CustomProjection();
        Assert.Equal(90, projection.MaxLatitude);
        Assert.Equal(0, projection.CenterLongitude);
        Assert.True(projection.HasStraightMeridians);
        Assert.True(projection.HasStraightParallels);
        Assert.Equal((2.0, 1.0), projection.Project(new GeoPosition(180 / Math.PI, 180 / Math.PI, Altitude: 100)));
    }

    // The expected values were computed by PROJ 9.3.0 (pyproj 3.6.1) with Transformer.from_crs("EPSG:4326", "EPSG:<code>", always_xy=True).
    // For 27572 and 27700, PROJ prefers grids that it does not ship: the target was the PROJ string of the definition with
    // the +towgs84 of the EPSG dataset, the transformation the library uses.
    [Theory]
    [InlineData(3857, 2.35, 48.86, 261600.803364, 6251139.623506)]
    [InlineData(3857, -74.006, 40.7128, -8238310.235647, 4970071.579142)]
    [InlineData(3857, 179.9, -80, 20026376.393710, -15538711.096309)]
    [InlineData(3395, 2.35, 48.86, 261600.803364, 6218943.034080)]
    [InlineData(3395, -74.006, 40.7128, -8238310.235647, 4942194.778103)]
    [InlineData(32631, 2.35, 48.86, 452324.388220, 5412096.502545)]
    [InlineData(32631, 8.0, 60.0, 778711.229752, 6661953.040545)]
    [InlineData(32631, 3.0, 0.0, 500000, 0)]
    [InlineData(32733, 15.5, -23.4, 551088.981819, 7412110.696330)]
    [InlineData(32733, 12.1, -0.5, 177176.037447, 9944663.622434)]
    [InlineData(32618, -72.0, 45.0, 736446.026101, 4987329.504699)]
    [InlineData(2154, 2.35, 48.86, 652310.718019, 6862414.636762)]
    [InlineData(2154, -4.48, 48.39, 147078.330538, 6836175.333849)]
    [InlineData(2154, 7.75, 48.58, 1050163.943557, 6841622.715488)]
    [InlineData(2154, 9.45, 42.7, 1228546.045316, 6199420.643975)]
    [InlineData(2154, 3, 46.5, 700000, 6600000)]
    [InlineData(3946, 4.84, 45.76, 1843123.768886, 5174979.711648)]
    [InlineData(27572, 2.35, 48.86, 600990.711973, 2429074.175049)]
    [InlineData(27572, -1.55, 47.22, 305767.871939, 2253971.253335)]
    [InlineData(27700, -0.1276, 51.5072, 530043.194981, 180358.208620, 2e-8)]
    [InlineData(27700, -3.19, 55.95, 325784.748008, 673635.768108, 2e-8)]
    public void CoordinateReferenceSystem_FromEpsg_MatchesProj(int code, double longitude, double latitude, double easting, double northing, double inverseTolerance = 1e-8)
    {
        // The inverse of a Helmert transformation with rotations uses the transposed rotation matrix, as PROJ does: the
        // round trip is off by about a millimeter, and PROJ inverts (530043.194981, 180358.208620) to (-0.12759998750, 51.50719999540) too
        var crs = CoordinateReferenceSystem.FromEpsg(code)!;
        Assert.Equal(code, crs.EpsgCode);
        Assert.False(crs.IsGeographic);
        AssertProjected(crs, longitude, latitude, easting, northing, inverseTolerance: inverseTolerance);
    }

    [Fact]
    public void CoordinateReferenceSystem_EpsgGuidanceExamples()
    {
        // The examples of the EPSG Guidance Note 7-2, without datum shift; the expected values are those of PROJ, which round to the ones of the note
        var nad27 = new GeographicCoordinateSystem("NAD27", Ellipsoid.Clarke1866);
        var jamaica = new ProjectedCoordinateSystem("JAD69 / Jamaica National Grid", nad27, new LambertConformalConicConversion { LatitudeOfOrigin = 18, CentralMeridian = -77, FalseEasting = 250_000, FalseNorthing = 150_000 });
        AssertProjected(jamaica, -(76 + (56 / 60.0) + (37.26 / 3600)), 17 + (55 / 60.0) + (55.80 / 3600), 255966.581850, 142493.511021);

        const double UsSurveyFoot = 1200.0 / 3937;
        var texas = new ProjectedCoordinateSystem(
            "NAD27 / Texas South Central",
            nad27,
            new LambertConformalConicConversion { LatitudeOfOrigin = 27 + (50 / 60.0), CentralMeridian = -99, StandardParallel1 = 28 + (23 / 60.0), StandardParallel2 = 30 + (17 / 60.0), FalseEasting = 2_000_000 * UsSurveyFoot })
        {
            LinearUnit = UsSurveyFoot,
        };
        AssertProjected(texas, -96, 28.5, 2963503.912811, 254759.800646);

        var airy = new GeographicCoordinateSystem("OSGB36", Ellipsoid.Airy1830);
        var nationalGrid = new ProjectedCoordinateSystem("OSGB36 / British National Grid", airy, new TransverseMercatorConversion { LatitudeOfOrigin = 49, CentralMeridian = -2, ScaleFactor = 0.9996012717, FalseEasting = 400_000, FalseNorthing = -100_000 });
        AssertProjected(nationalGrid, 0.5, 50.5, 577274.983813, 69740.492267);

        var pulkovo = new GeographicCoordinateSystem("Pulkovo 1942", new Ellipsoid("Krassowsky 1940", 6_378_245, 298.3));
        var mercator2Sp = new ProjectedCoordinateSystem("Pulkovo 1942 / Mercator 2SP", pulkovo, new MercatorConversion { CentralMeridian = 51, StandardParallel = 42 });
        AssertProjected(mercator2Sp, 53, 53, 165704.293311, 5171848.072896);
    }

    [Fact]
    public void CoordinateReferenceSystem_Utm()
    {
        var utm = CoordinateReferenceSystem.Utm(31, isNorthernHemisphere: true);
        Assert.Equal(32631, utm.EpsgCode);
        Assert.Equal("WGS 84 / UTM zone 31N (EPSG:32631)", utm.ToString());
        Assert.Equal(3, Assert.IsType<TransverseMercatorConversion>(utm.Conversion).CentralMeridian);
        Assert.Equal(32760, CoordinateReferenceSystem.Utm(60, isNorthernHemisphere: false).EpsgCode);
        Assert.Throws<ArgumentOutOfRangeException>("zone", () => CoordinateReferenceSystem.Utm(0, isNorthernHemisphere: true));
        Assert.Throws<ArgumentOutOfRangeException>("zone", () => CoordinateReferenceSystem.Utm(61, isNorthernHemisphere: true));

        // The same projection on ETRS89 and NAD83
        Assert.Equal("ETRS89 / UTM zone 31N", CoordinateReferenceSystem.FromEpsg(25831)!.Name);
        Assert.Equal("NAD83 / UTM zone 18N", CoordinateReferenceSystem.FromEpsg(26918)!.Name);
        AssertProjected(CoordinateReferenceSystem.FromEpsg(25831)!, 2.35, 48.86, 452324.388220, 5412096.502545);
    }

    [Fact]
    public void CoordinateReferenceSystem_FromEpsg_Geographic()
    {
        Assert.Same(CoordinateReferenceSystem.Wgs84, CoordinateReferenceSystem.FromEpsg(4326));
        Assert.Same(CoordinateReferenceSystem.WebMercator, CoordinateReferenceSystem.FromEpsg(900913));
        Assert.Same(CoordinateReferenceSystem.Lambert93, CoordinateReferenceSystem.FromEpsg(2154));
        Assert.Null(CoordinateReferenceSystem.FromEpsg(1234));
        foreach (var code in new[] { 4258, 4171, 4269 })
        {
            var crs = CoordinateReferenceSystem.FromEpsg(code)!;
            Assert.True(crs.IsGeographic);
            Assert.Equal(new GeoPosition(2.35, 48.86, 10), crs.ToWgs84(new GeoPosition(2.35, 48.86, 10)));
        }

        // The Helmert transformation of OSGB36 changes the ellipsoidal height; computed by a PROJ pipeline (cart, inverse helmert, inverse cart)
        var osgb36 = CoordinateReferenceSystem.FromEpsg(4277)!;
        var position = osgb36.FromWgs84(new GeoPosition(-0.1276, 51.5072, 100));
        Assert.Equal(-0.12599353753741305, position.Longitude, 1e-9);
        Assert.Equal(51.50668957323988, position.Latitude, 1e-9);
        Assert.Equal(53.878907597623765, position.Altitude!.Value, 1e-3);
        var back = osgb36.ToWgs84(position);
        Assert.Equal(-0.1276, back.Longitude, 1e-9);
        Assert.Equal(51.5072, back.Latitude, 1e-9);
        Assert.Equal(100, back.Altitude!.Value, 1e-3);

        // Without an altitude, the height is 0 and the result has no altitude either
        Assert.Null(osgb36.FromWgs84(new GeoPosition(-0.1276, 51.5072)).Altitude);

        // The longitudes and the latitudes of a projected system keep their altitude
        Assert.Equal(25, CoordinateReferenceSystem.Lambert93.FromWgs84(new GeoPosition(2.35, 48.86, 25)).Altitude);

        // The measures and the times are kept, through projections and datum shifts
        var time = new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.FromHours(1));
        var measured = new GeoPosition(2.35, 48.86, 25) { Measure = 42, Time = time };
        foreach (var crs in new[] { CoordinateReferenceSystem.Lambert93, osgb36, CoordinateReferenceSystem.FromEpsg(27700)! })
        {
            var converted = crs.FromWgs84(measured);
            Assert.Equal(42, converted.Measure);
            Assert.Equal(time, converted.Time);
            Assert.Equal(42, crs.ToWgs84(converted).Measure);
            Assert.Equal(time, crs.ToWgs84(converted).Time);
        }
    }

    [Fact]
    public void CoordinateReferenceSystem_GeographicUnitsAndPrimeMeridian()
    {
        // NTF (Paris): grads from the meridian of Paris
        var ntfParis = new GeographicCoordinateSystem("NTF (Paris)", Ellipsoid.Clarke1880Ign) { PrimeMeridian = 2.33722917, AngularUnit = Math.PI / 200 };
        var paris = ntfParis.ToWgs84(new GeoPosition(0, 54.2733));
        Assert.Equal(2.33722917, paris.Longitude, 1e-9);
        Assert.Equal(48.84597, paris.Latitude, 1e-9);

        // 178°W is 180.33722917° west of Paris, so 179.66277083° east of it
        var position = ntfParis.FromWgs84(new GeoPosition(-178, 0));
        Assert.Equal((180 - 0.33722917) / 0.9, position.Longitude, 1e-9);
        Assert.Equal(-178, ntfParis.ToWgs84(position).Longitude, 1e-9);
    }

    [Fact]
    public void CoordinateReferenceSystem_Transform()
    {
        #region Geo_CoordinateReferenceSystemTransform
        // A point of a dataset in Lambert 93 (EPSG:2154), in meters
        var lambert93 = CoordinateReferenceSystem.Lambert93;
        var eiffelTower = new GeoPoint(new GeoPosition(648237.30, 6862271.68));

        // Transform converts every position of a geometry
        var wgs84 = (GeoPoint)eiffelTower.Transform(lambert93, CoordinateReferenceSystem.Wgs84);
        Assert.Equal(2.2945, wgs84.Position.Longitude, 1e-7);
        Assert.Equal(48.8584, wgs84.Position.Latitude, 1e-7);

        // Or convert a position to any other coordinate reference system, such as a UTM zone
        var utm = CoordinateReferenceSystem.Utm(31, isNorthernHemisphere: true);
        var position = lambert93.Transform(new GeoPosition(648237.30, 6862271.68), utm);
        Assert.Equal(448252.0, position.Longitude, 0.1);
        Assert.Equal(5411954.9, position.Latitude, 0.1);
        #endregion
    }

    [Fact]
    public void CoordinateReferenceSystem_TransformSameInstanceIsIdentity()
    {
        var position = new GeoPosition(1.23456789, 2.3456789);
        Assert.Equal(position, CoordinateReferenceSystem.Lambert93.Transform(position, CoordinateReferenceSystem.Lambert93));
        Assert.Throws<ArgumentNullException>("target", () => CoordinateReferenceSystem.Lambert93.Transform(position, null!));
        var polygon = new GeoPolygon([[new(648237.30, 6862271.68), new(648273.59, 6862226.88), new(648237.30, 6862226.88), new(648237.30, 6862271.68)]]);
        var transformed = Assert.IsType<GeoPolygon>(polygon.Transform(CoordinateReferenceSystem.Lambert93, CoordinateReferenceSystem.Wgs84));
        Assert.Equal(2.2950, transformed.Rings[0][1].Longitude, 1e-6);
        Assert.Equal(48.8580, transformed.Rings[0][1].Latitude, 1e-6);
    }

    [Fact]
    public void CoordinateReferenceSystem_ParseWkt_Esri()
    {
        // The .prj file that ArcGIS and QGIS write for Lambert 93
        var crs = Assert.IsType<ProjectedCoordinateSystem>(CoordinateReferenceSystem.ParseWkt("""
            PROJCS["RGF_1993_Lambert_93",GEOGCS["GCS_RGF_1993",DATUM["D_RGF_1993",SPHEROID["GRS_1980",6378137.0,298.257222101]],PRIMEM["Greenwich",0.0],UNIT["Degree",0.0174532925199433]],PROJECTION["Lambert_Conformal_Conic"],PARAMETER["False_Easting",700000.0],PARAMETER["False_Northing",6600000.0],PARAMETER["Central_Meridian",3.0],PARAMETER["Standard_Parallel_1",49.0],PARAMETER["Standard_Parallel_2",44.0],PARAMETER["Latitude_Of_Origin",46.5],UNIT["Meter",1.0]]
            """));
        Assert.Equal("RGF_1993_Lambert_93", crs.Name);
        Assert.Null(crs.EpsgCode);
        Assert.Equal("GRS_1980", crs.GeographicCoordinateSystem.Ellipsoid.Name);
        AssertProjected(crs, 2.35, 48.86, 652310.718019, 6862414.636762);

        // NTF (Paris) / Lambert zone II: grads, the meridian of Paris (in degrees), and the shift of NTF from its datum name
        var ntf = CoordinateReferenceSystem.ParseWkt("""
            PROJCS["NTF_Paris_Lambert_Zone_II",GEOGCS["GCS_NTF_Paris",DATUM["D_NTF",SPHEROID["Clarke_1880_IGN",6378249.2,293.466021293627]],PRIMEM["Paris",2.33722917],UNIT["Grad",0.0157079632679489]],PROJECTION["Lambert_Conformal_Conic"],PARAMETER["False_Easting",600000.0],PARAMETER["False_Northing",2200000.0],PARAMETER["Central_Meridian",0.0],PARAMETER["Standard_Parallel_1",52.0],PARAMETER["Scale_Factor",0.99987742],PARAMETER["Latitude_Of_Origin",52.0],UNIT["Meter",1.0]]
            """);
        AssertProjected(ntf, 2.35, 48.86, 600990.711973, 2429074.175049);

        // Web Mercator
        var webMercator = CoordinateReferenceSystem.ParseWkt("""
            PROJCS["WGS_1984_Web_Mercator_Auxiliary_Sphere",GEOGCS["GCS_WGS_1984",DATUM["D_WGS_1984",SPHEROID["WGS_1984",6378137.0,298.257223563]],PRIMEM["Greenwich",0.0],UNIT["Degree",0.0174532925199433]],PROJECTION["Mercator_Auxiliary_Sphere"],PARAMETER["False_Easting",0.0],PARAMETER["False_Northing",0.0],PARAMETER["Central_Meridian",0.0],PARAMETER["Standard_Parallel_1",0.0],PARAMETER["Auxiliary_Sphere_Type",0.0],UNIT["Meter",1.0]]
            """);
        AssertProjected(webMercator, 2.35, 48.86, 261600.803364, 6251139.623506);

        var geographic = Assert.IsType<GeographicCoordinateSystem>(CoordinateReferenceSystem.ParseWkt("""GEOGCS["GCS_WGS_1984",DATUM["D_WGS_1984",SPHEROID["WGS_1984",6378137.0,298.257223563]],PRIMEM["Greenwich",0.0],UNIT["Degree",0.0174532925199433]]"""));
        Assert.True(geographic.IsGeographic);
        Assert.Null(geographic.ToWgs84Transformation);
        Assert.Equal(new GeoPosition(2.35, 48.86), geographic.ToWgs84(new GeoPosition(2.35, 48.86)));
    }

    [Fact]
    public void CoordinateReferenceSystem_ParseWkt_Ogc()
    {
        // A code that FromEpsg knows gives the built-in system
        var utm = CoordinateReferenceSystem.ParseWkt("""
            PROJCS["WGS 84 / UTM zone 31N",GEOGCS["WGS 84",DATUM["WGS_1984",SPHEROID["WGS 84",6378137,298.257223563,AUTHORITY["EPSG","7030"]],AUTHORITY["EPSG","6326"]],PRIMEM["Greenwich",0,AUTHORITY["EPSG","8901"]],UNIT["degree",0.0174532925199433,AUTHORITY["EPSG","9122"]],AUTHORITY["EPSG","4326"]],PROJECTION["Transverse_Mercator"],PARAMETER["latitude_of_origin",0],PARAMETER["central_meridian",3],PARAMETER["scale_factor",0.9996],PARAMETER["false_easting",500000],PARAMETER["false_northing",0],UNIT["metre",1,AUTHORITY["EPSG","9001"]],AXIS["Easting",EAST],AXIS["Northing",NORTH],AUTHORITY["EPSG","32631"]]
            """);
        Assert.Equal("WGS 84 / UTM zone 31N", utm.Name);
        Assert.Equal(32631, utm.EpsgCode);

        // Otherwise the definition gives the parameters: Belgian Lambert 72 (EPSG:31370), with a TOWGS84 of 7 parameters
        var belgium = Assert.IsType<ProjectedCoordinateSystem>(CoordinateReferenceSystem.ParseWkt("""
            PROJCS["BD72 / Belgian Lambert 72",GEOGCS["BD72",DATUM["Reseau_National_Belge_1972",SPHEROID["International 1924",6378388,297,AUTHORITY["EPSG","7022"]],TOWGS84[-106.8686,52.2978,-103.7239,0.3366,-0.457,1.8422,-1.2747],AUTHORITY["EPSG","6313"]],PRIMEM["Greenwich",0,AUTHORITY["EPSG","8901"]],UNIT["degree",0.0174532925199433,AUTHORITY["EPSG","9122"]],AUTHORITY["EPSG","4313"]],PROJECTION["Lambert_Conformal_Conic_2SP"],PARAMETER["latitude_of_origin",90],PARAMETER["central_meridian",4.36748666666667],PARAMETER["standard_parallel_1",51.1666672333333],PARAMETER["standard_parallel_2",49.8333339],PARAMETER["false_easting",150000.013],PARAMETER["false_northing",5400088.438],UNIT["metre",1,AUTHORITY["EPSG","9001"]],AXIS["Easting",EAST],AXIS["Northing",NORTH],AUTHORITY["EPSG","31370"]]
            """));
        Assert.Equal(31370, belgium.EpsgCode);
        Assert.Equal(4313, belgium.GeographicCoordinateSystem.EpsgCode);
        Assert.Equal(new HelmertTransformation(-106.8686, 52.2978, -103.7239, 0.3366, -0.457, 1.8422, -1.2747), belgium.GeographicCoordinateSystem.ToWgs84Transformation);
        AssertProjected(belgium, 4.35, 50.85, 148679.447378, 171066.810330);

        // GDAL writes Web Mercator as a Mercator_1SP on a sphere, given by its PROJ4 extension
        var webMercator = Assert.IsType<ProjectedCoordinateSystem>(CoordinateReferenceSystem.ParseWkt("""
            PROJCS["WGS 84 / Pseudo-Mercator",GEOGCS["WGS 84",DATUM["WGS_1984",SPHEROID["WGS 84",6378137,298.257223563]],PRIMEM["Greenwich",0],UNIT["degree",0.0174532925199433]],PROJECTION["Mercator_1SP"],PARAMETER["central_meridian",0],PARAMETER["scale_factor",1],PARAMETER["false_easting",0],PARAMETER["false_northing",0],UNIT["metre",1],EXTENSION["PROJ4","+proj=merc +a=6378137 +b=6378137 +lat_ts=0 +lon_0=0 +x_0=0 +y_0=0 +k=1 +units=m +nadgrids=@null +wktext +no_defs"]]
            """));
        Assert.True(Assert.IsType<MercatorConversion>(webMercator.Conversion).IsSpherical);
        AssertProjected(webMercator, 2.35, 48.86, 261600.803364, 6251139.623506);

        // The units of the false easting and northing are those of the PROJCS: NAD83 / Texas South Central (ftUS), EPSG:2278
        var texas = CoordinateReferenceSystem.ParseWkt("""
            PROJCS["NAD83 / Texas South Central (ftUS)",GEOGCS["NAD83",DATUM["North_American_Datum_1983",SPHEROID["GRS 1980",6378137,298.257222101]],PRIMEM["Greenwich",0],UNIT["degree",0.0174532925199433]],PROJECTION["Lambert_Conformal_Conic_2SP"],PARAMETER["latitude_of_origin",27.8333333333333],PARAMETER["central_meridian",-99],PARAMETER["standard_parallel_1",30.2833333333333],PARAMETER["standard_parallel_2",28.3833333333333],PARAMETER["false_easting",1968500],PARAMETER["false_northing",13123333.333],UNIT["US survey foot",0.304800609601219]]
            """);
        AssertProjected(texas, -96.0, 28.5, 2931985.267783, 13378102.358581, tolerance: 1e-2);

        // A compound system gives its horizontal system; a WKT 2 definition is read by its EPSG code
        Assert.Same(CoordinateReferenceSystem.Lambert93, CoordinateReferenceSystem.ParseWkt("""COMPD_CS["RGF93 + NGF-IGN69",PROJCS["RGF93 / Lambert-93",GEOGCS["RGF93",DATUM["RGF93",SPHEROID["GRS 1980",6378137,298.257222101]],PRIMEM["Greenwich",0],UNIT["degree",0.0174532925199433]],PROJECTION["Lambert_Conformal_Conic_2SP"],UNIT["metre",1],AUTHORITY["EPSG","2154"]],VERT_CS["NGF-IGN69 height",VERT_DATUM["NGF-IGN69",2005],UNIT["metre",1]]]"""));
        Assert.Same(CoordinateReferenceSystem.Lambert93, CoordinateReferenceSystem.ParseWkt("""PROJCRS["RGF93 v1 / Lambert-93",BASEGEOGCRS["RGF93 v1",DATUM["Reseau Geodesique Francais 1993 v1",ELLIPSOID["GRS 1980",6378137,298.257222101]]],CONVERSION["Lambert-93",METHOD["Lambert Conic Conformal (2SP)"]],CS[Cartesian,2],ID["EPSG",2154]]"""));
    }

    [Theory]
    [InlineData("", typeof(FormatException))]
    [InlineData("PROJCS[\"x\"", typeof(FormatException))]
    [InlineData("PROJCS[\"x\",GEOGCS[\"y\"]]", typeof(FormatException))]
    [InlineData("GEOGCS[\"y\",DATUM[\"d\",SPHEROID[\"s\",-1,298]]]", typeof(FormatException))]
    [InlineData("GEOGCS[\"y\",DATUM[\"d\",SPHEROID[\"s\",6378137,298]]] extra", typeof(FormatException))]
    [InlineData("POINT(1 2)", typeof(FormatException))]
    [InlineData("PROJCS[\"Albers\",GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]],PROJECTION[\"Albers_Conic_Equal_Area\"],UNIT[\"metre\",1]]", typeof(NotSupportedException))]
    [InlineData("PROJCRS[\"x\",BASEGEOGCRS[\"y\"]]", typeof(NotSupportedException))]
    public void CoordinateReferenceSystem_ParseWkt_Invalid(string wkt, Type exceptionType)
    {
        Assert.Throws(exceptionType, () => CoordinateReferenceSystem.ParseWkt(wkt));
    }

    [Fact]
    public void CoordinateReferenceSystem_Custom()
    {
        var swiss = new SwissLv95CoordinateSystem();
        var bern = swiss.FromWgs84(new GeoPosition(7.43863, 46.95108));

        // The approximate formulas of swisstopo are accurate to a meter; PROJ gives (2599999.82, 1199999.69)
        Assert.Equal(2599999.82, bern.Longitude, 1);
        Assert.Equal(1199999.69, bern.Latitude, 1);
        var zurich = swiss.Transform(new GeoPosition(2683303.89, 1247925.63), CoordinateReferenceSystem.Wgs84);
        Assert.Equal(8.5417, zurich.Longitude, 1e-4);
        Assert.Equal(47.3769, zurich.Latitude, 1e-4);
    }

    #region Geo_CustomCoordinateReferenceSystem
    // The Swiss coordinates LV95 (EPSG:2056), with the approximate formulas of swisstopo (about a meter)
    private sealed class SwissLv95CoordinateSystem() : CoordinateReferenceSystem("CH1903+ / LV95")
    {
        public override bool IsGeographic => false;

        public override GeoPosition ToWgs84(GeoPosition position)
        {
            var y = (position.Longitude - 2_600_000) / 1_000_000;
            var x = (position.Latitude - 1_200_000) / 1_000_000;
            var lambda = 2.6779094 + (4.728982 * y) + (0.791484 * y * x) + (0.1306 * y * x * x) - (0.0436 * y * y * y);
            var phi = 16.9023892 + (3.238272 * x) - (0.270978 * y * y) - (0.002528 * x * x) - (0.0447 * y * y * x) - (0.0140 * x * x * x);
            return new GeoPosition(lambda * 100 / 36, phi * 100 / 36, position.Altitude);
        }

        public override GeoPosition FromWgs84(GeoPosition position)
        {
            var phi = ((position.Latitude * 3600) - 169_028.66) / 10_000;
            var lambda = ((position.Longitude * 3600) - 26_782.5) / 10_000;
            var easting = 2_600_072.37 + (211_455.93 * lambda) - (10_938.51 * lambda * phi) - (0.36 * lambda * phi * phi) - (44.54 * lambda * lambda * lambda);
            var northing = 1_200_147.07 + (308_807.95 * phi) + (3_745.25 * lambda * lambda) + (76.63 * phi * phi) - (194.56 * lambda * lambda * phi) + (119.79 * phi * phi * phi);
            return new GeoPosition(easting, northing, position.Altitude);
        }
    }
    #endregion

    private static void AssertProjected(CoordinateReferenceSystem crs, double longitude, double latitude, double easting, double northing, double tolerance = 1e-3, double inverseTolerance = 1e-8)
    {
        // A millimeter, and a hundred-millionth of a degree (about a millimeter) back
        var projected = crs.FromWgs84(new GeoPosition(longitude, latitude));
        Assert.Equal(easting, projected.Longitude, tolerance);
        Assert.Equal(northing, projected.Latitude, tolerance);
        var back = crs.ToWgs84(new GeoPosition(easting, northing));
        Assert.Equal(longitude, back.Longitude, inverseTolerance);
        Assert.Equal(latitude, back.Latitude, inverseTolerance);
    }

    // An equal-area projection of the unit sphere: the area of a small cell is cos(latitude) dλ dφ
    private static void AssertEqualArea(GeoProjection projection)
    {
        foreach (var (longitude, latitude) in new[] { (0.0, 0.0), (40, 20), (-100, 45), (150, -70) })
        {
            var a = Derivative(projection, longitude, latitude, alongLongitude: true);
            var b = Derivative(projection, longitude, latitude, alongLongitude: false);
            Assert.Equal(Math.Cos(latitude * Math.PI / 180), Math.Abs((a.X * b.Y) - (a.Y * b.X)), 1e-6);
        }
    }

    // The derivative of the projection, per radian of longitude or latitude, by central differences
    private static (double X, double Y, double Length) Derivative(GeoProjection projection, double longitude, double latitude, bool alongLongitude)
    {
        const double H = 1e-4;
        var (x1, y1) = alongLongitude ? projection.Project(longitude - H, latitude) : projection.Project(longitude, latitude - H);
        var (x2, y2) = alongLongitude ? projection.Project(longitude + H, latitude) : projection.Project(longitude, latitude + H);
        var scale = 180 / Math.PI / (2 * H);
        var (dx, dy) = ((x2 - x1) * scale, (y2 - y1) * scale);
        return (dx, dy, Math.Sqrt((dx * dx) + (dy * dy)));
    }

    private sealed class CustomProjection : GeoProjection
    {
        protected override (double X, double Y) ProjectRadians(double lambda, double phi) => (2 * lambda, phi);
    }

    private static void AssertPoint((double X, double Y) expected, (double X, double Y) actual)
    {
        Assert.Equal(expected.X, actual.X, 1e-12);
        Assert.Equal(expected.Y, actual.Y, 1e-12);
    }
}
