namespace Meziantou.Framework.Geo.Tests;

public sealed class EncodedPolylineTests
{
    private static readonly GeoPosition[] GooglePositions = [new(-120.2, 38.5), new(-120.95, 40.7), new(-126.453, 43.252)];

    [Fact]
    public void Google()
    {
        // The example of the documentation of the Google Maps Platform
        Assert.Equal("_p~iF~ps|U_ulLnnqC_mqNvxq`@", EncodedPolyline.Encode(GooglePositions));
        Assert.Equal(GooglePositions, EncodedPolyline.Decode("_p~iF~ps|U_ulLnnqC_mqNvxq`@"));
    }

    [Fact]
    public void Precision6()
    {
        // The example of the documentation of the polyline package for Python
        Assert.Equal("_izlhA~rlgdF_{geC~ywl@_kwzCn`{nI", EncodedPolyline.Encode(GooglePositions, precision: 6));
        Assert.Equal(GooglePositions, EncodedPolyline.Decode("_izlhA~rlgdF_{geC~ywl@_kwzCn`{nI", precision: 6));
    }

    [Fact]
    public void Encode_Rounding()
    {
        // Half away from zero, on the scaled coordinates
        Assert.Equal([new GeoPosition(1, 1), new(-1, -1), new(2, 2), new(3, 3)], EncodedPolyline.Decode(EncodedPolyline.Encode([new(0.5, 0.5), new(-0.5, -0.5), new(1.5, 1.5), new(2.5, 2.5)], precision: 0), precision: 0));

        // The differences are between rounded positions, so the errors do not add up
        var positions = Enumerable.Range(0, 1000).Select(i => new GeoPosition(i * 0.000004, 0)).ToList();
        var decoded = EncodedPolyline.Decode(EncodedPolyline.Encode(positions));
        Assert.Equal(0.004, decoded[^1].Longitude);
    }

    [Fact]
    public void Encode_IgnoresAltitudes()
    {
        Assert.Equal("??", EncodedPolyline.Encode([new GeoPosition(0, 0, 100)]));
        Assert.Equal("", EncodedPolyline.Encode([]));
        Assert.Empty(EncodedPolyline.Decode(""));
    }

    [Fact]
    public void Encode_LargeValues()
    {
        var positions = new GeoPosition[] { new(180, -90), new(-180, 90), new(179.9999999999, -89.9999999999) };
        Assert.Equal(positions, EncodedPolyline.Decode(EncodedPolyline.Encode(positions, precision: 10), precision: 10));
    }

    [Fact]
    public void Encode_Invalid()
    {
        Assert.Throws<ArgumentException>(() => EncodedPolyline.Encode([new GeoPosition(double.NaN, 0)]));
        Assert.Throws<ArgumentException>(() => EncodedPolyline.Encode([new GeoPosition(0, double.PositiveInfinity)]));
        Assert.Throws<ArgumentException>(() => EncodedPolyline.Encode([new GeoPosition(1e300, 0)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => EncodedPolyline.Encode(GooglePositions, precision: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => EncodedPolyline.Encode(GooglePositions, precision: 11));
        Assert.Throws<ArgumentOutOfRangeException>(() => EncodedPolyline.Decode("??", precision: 11));
        Assert.Throws<ArgumentNullException>(() => EncodedPolyline.Encode(null!));
        Assert.Throws<ArgumentNullException>(() => EncodedPolyline.Decode(null!));
    }

    [Theory]
    [InlineData("?", "The encoded polyline ends after the latitude at position 0, without its longitude.")]
    [InlineData("_p~iF", "The encoded polyline ends after the latitude at position 0, without its longitude.")]
    [InlineData("_p~iF~ps|", "The encoded polyline ends in the middle of the value at position 5.")]
    [InlineData("??_", "The encoded polyline ends in the middle of the value at position 2.")]
    [InlineData("?? ?", "The character ' ' at position 2 is not valid in an encoded polyline.")]
    [InlineData("??\u007F?", "The character '\u007F' at position 2 is not valid in an encoded polyline.")]
    [InlineData("~~~~~~~~~~~~~~?", "The value at position 0 of the encoded polyline is too large.")]
    public void Decode_Invalid(string encoded, string message)
    {
        var exception = Assert.Throws<FormatException>(() => EncodedPolyline.Decode(encoded));
        Assert.Equal(message, exception.Message);
    }

    [Fact]
    public void Decode_Overflow()
    {
        // Each value is close to the largest long: their sum overflows
        var encoded = string.Concat(Enumerable.Repeat("}}}}}}}}}}}}]?", 4));
        Assert.Throws<FormatException>(() => EncodedPolyline.Decode(encoded));
    }

    [Fact]
    public void Decode_Example()
    {
        #region Geo_EncodedPolylineDecode
        // The geometry of a route returned by OSRM or by the Google Maps APIs
        var positions = EncodedPolyline.Decode("_p~iF~ps|U_ulLnnqC_mqNvxq`@");

        Assert.Equal(new GeoPosition(-120.2, 38.5), positions[0]);
        var route = new GeoLineString(positions);
        Assert.Equal(3, route.Positions.Count);

        // Valhalla, and OSRM with geometries=polyline6, use 6 decimal digits
        var encoded = EncodedPolyline.Encode(route.Positions, precision: 6);
        Assert.Equal("_izlhA~rlgdF_{geC~ywl@_kwzCn`{nI", encoded);
        #endregion
    }

    [Fact]
    public void Decode_CoordinateTooLargeToEncode()
    {
        // Each value is valid, but their sum is a coordinate that Encode cannot write back
        var part = EncodedPolyline.Encode([new GeoPosition(0, 6e9)]);
        Assert.Equal(6e9, Assert.Single(EncodedPolyline.Decode(part)).Latitude);
        Assert.Throws<FormatException>(() => EncodedPolyline.Decode(part + part));
    }
}
