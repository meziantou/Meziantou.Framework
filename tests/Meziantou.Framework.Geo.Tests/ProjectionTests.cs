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

    [Theory]
    [InlineData("Equirectangular")]
    [InlineData("Mercator")]
    [InlineData("EqualEarth")]
    [InlineData("Albers")]
    [InlineData("AlbersCylindrical")]
    public void Invert_RoundTrips(string name)
    {
        GeoProjection projection = name switch
        {
            "Equirectangular" => new EquirectangularProjection(),
            "Mercator" => new MercatorProjection(),
            "EqualEarth" => new EqualEarthProjection(),
            "Albers" => new AlbersProjection(),
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
}
