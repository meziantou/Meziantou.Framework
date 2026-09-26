using Meziantou.Framework.Toml.Model;

namespace Meziantou.Framework.Toml.Tests;

public class FloatingRoundtripTests
{

    // xUnit considers -0.0 a duplicate of 0.0 in InlineData, so negative zero has its own test
    [Fact]
    public void TestNegativeZeroDoubleRoundtrip() => TestDoublesRoundtrip(double.NegativeZero);

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-1.0)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.NaN)]
    [InlineData(double.Epsilon)]
    [InlineData(-double.Epsilon)]
    // [TestCase(0.1)] - These fail due to float-as-double roundtrip behavior in TomlTable.
    // [TestCase(0.99)]
    // [TestCase(0.3)]
    // [TestCase(double.MinValue)]
    // [TestCase(double.MaxValue)]
    public void TestDoublesRoundtrip(double number)
    {
        // we want to increment f64 by the smallest possible value
        // and verify it changes the serialized value
        // IEEE 754 compliance means -0.0 and +0.0 are different
        // special values +inf=inf, -inf, nan=+nan, -nan
        var model = new TomlTable
        {
            ["float"] = (float)number,
            ["double"] = number
        };
        var toml = TomlSerializer.Serialize(model);
        var parsed = TomlSerializer.Deserialize<TomlTable>(toml);
        Assert.NotNull(parsed);
        var parsedTable = parsed!;
        var parsedDouble = (double)parsedTable["double"];
        var parsedFloat = (double)parsedTable["float"];
        Assert.True(number == parsedDouble || double.IsNaN(number), message: $"(f64->str->f64) expected {number:g30} but got {parsedDouble:g30}. \nString form: \n{toml}");
        Assert.Equal(number, parsedDouble);
        Assert.True((float)number == parsedFloat || double.IsNaN(number), message: $"(f64->f32->str->f64->f32) expected {(float)number:g30} but got {parsedFloat:g30}. \nString form: \n{toml}");
        Assert.Equal((float)number, parsedFloat);
    }

    [Fact]
    public void TestNegativeZeroFloatRoundtrip() => TestFloatsRoundtrip(float.NegativeZero);

    [Theory]
    [InlineData(0.0f)]
    [InlineData(1.0f)]
    [InlineData(-1.0f)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(float.NaN)]
    [InlineData(float.Epsilon)]
    [InlineData(-float.Epsilon)]
    // [TestCase(0.1f)] - These fail due to float-as-double roundtrip behavior in TomlTable.
    // [TestCase(0.99f)]
    // [TestCase(0.3f)]
    // [TestCase(float.MinValue)]
    // [TestCase(float.MaxValue)]
    public void TestFloatsRoundtrip(float number)
    {
        var model = new TomlTable
        {
            ["float"] = number,
            ["double"] = (double)number
        };
        var toml = TomlSerializer.Serialize(model);

        var parsed = TomlSerializer.Deserialize<TomlTable>(toml);
        Assert.NotNull(parsed);
        var parsedTable = parsed!;
        var parsedDouble = (double)parsedTable["double"];
        var parsedFloatAsDouble = (double)parsedTable["float"];
        Assert.True((double)number == parsedDouble || double.IsNaN(number), message: $"(f32->f64->str->f64) expected double {(double)number:g64} but got double {parsedDouble:g64}. \nString form: \n{toml}");
        Assert.True(number == (float)parsedFloatAsDouble || double.IsNaN(number), message: $"(f32->str->f64->f32) expected float {number:g64} but got float {(float)parsedFloatAsDouble:g64}. \nString form: \n{toml}");

    }
}
