namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlPrimitiveRoundTripTests
{
    [Fact]
    public void Primitives_RoundTrip()
    {
        Assert.Equal(true, RoundTrip(true));
        Assert.Equal((byte)255, RoundTrip((byte)255));
        Assert.Equal((sbyte)-12, RoundTrip((sbyte)-12));
        Assert.Equal((short)-32000, RoundTrip((short)-32000));
        Assert.Equal((ushort)65000, RoundTrip((ushort)65000));
        Assert.Equal(-123456789, RoundTrip(-123456789));
        Assert.Equal(4000000000u, RoundTrip(4000000000u));
        Assert.Equal(-1234567890123L, RoundTrip(-1234567890123L));
        Assert.Equal(1234567890123UL, RoundTrip(1234567890123UL));
        Assert.Equal('x', RoundTrip('x'));
        Assert.Equal(123.125m, RoundTrip(123.125m));

        Assert.Equal(3.5f, RoundTrip(3.5f));
        Assert.True(float.IsPositiveInfinity(RoundTrip(float.PositiveInfinity)));
        Assert.True(float.IsNaN(RoundTrip(float.NaN)));

        Assert.Equal(3.5d, RoundTrip(3.5d));
        Assert.True(double.IsPositiveInfinity(RoundTrip(double.PositiveInfinity)));
        Assert.True(double.IsNaN(RoundTrip(double.NaN)));

        Assert.Equal((nint)123, RoundTrip((nint)123));
        Assert.Equal((nuint)123, RoundTrip((nuint)123));
    }

    [Fact]
    public void Primitives_ParseUnderscoresAndBases()
    {
        Assert.Equal(1000, YamlSerializer.Deserialize<int>("1_000"));
        Assert.Equal(16, YamlSerializer.Deserialize<int>("0x10"));
        Assert.Equal(8, YamlSerializer.Deserialize<int>("0o10"));
        Assert.Equal(10u, YamlSerializer.Deserialize<uint>("0b1010"));
        Assert.Equal(255ul, YamlSerializer.Deserialize<ulong>("0xFF"));
        Assert.Equal(12.5, YamlSerializer.Deserialize<double>("1_2.5"));
        Assert.Equal(12.5m, YamlSerializer.Deserialize<decimal>("1_2.5"));
    }

    private static T RoundTrip<T>(T value)
    {
        var yaml = YamlSerializer.Serialize(value);
        return YamlSerializer.Deserialize<T>(yaml)!;
    }
}

