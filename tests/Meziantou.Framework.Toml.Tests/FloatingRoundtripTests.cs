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
    [InlineData(0.1f)]
    [InlineData(0.99f)]
    [InlineData(0.3f)]
    [InlineData(float.MinValue)]
    [InlineData(float.MaxValue)]
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

    [Theory]
    [InlineData(0.1f, "0.1")]
    [InlineData(0.3f, "0.3")]
    [InlineData(1e30f, "1E+30")]
    [InlineData(16777216f, "16777216.0")]
    public void Float_IsWrittenWithItsOwnPrecisionByEveryPath(float number, string expected)
    {
        Assert.Equal($"value = {expected}\n", TomlSerializer.Serialize(new TomlTable { ["value"] = number }).ReplaceLineEndings("\n"));
        Assert.Equal($"value = {expected}\n", TomlSerializer.Serialize(new Dictionary<string, float> { ["value"] = number }).ReplaceLineEndings("\n"));
        Assert.Equal($"value = {expected}\n", TomlSerializer.Serialize(new Dictionary<string, object> { ["value"] = number }).ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(65504.0)]
    [InlineData(6e-8)]
    public void Half_Roundtrips(double number)
    {
        var value = (Half)number;

        var toml = TomlSerializer.Serialize(new Dictionary<string, Half> { ["value"] = value });

        Assert.Equal(value, TomlSerializer.Deserialize<Dictionary<string, Half>>(toml)!["value"]);
        Assert.DoesNotContain("00000", toml);
    }

    [Theory]
    [InlineData(float.MaxValue)]
    [InlineData(float.MinValue)]
    public void Float_Extremes_RoundtripThroughTypedModels(float number)
    {
        var toml = TomlSerializer.Serialize(new Dictionary<string, float> { ["value"] = number });

        Assert.Equal(number, TomlSerializer.Deserialize<Dictionary<string, float>>(toml)!["value"]);
    }

    [Theory]
    [InlineData("3.4028235E+38")]
    [InlineData("3.40282356E+38")]
    public void Float_ValuesThatRoundToMaxValue_AreAccepted(string text)
    {
        Assert.Equal(float.MaxValue, TomlSerializer.Deserialize<Dictionary<string, float>>($"value = {text}")!["value"]);
    }

    [Fact]
    public void Half_ValuesThatRoundToMaxValue_AreAccepted()
    {
        Assert.Equal(Half.MaxValue, TomlSerializer.Deserialize<Dictionary<string, Half>>("value = 65519.0")!["value"]);
    }

    [Fact]
    public void Float_ValuesThatOverflow_AreRejected()
    {
        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<Dictionary<string, float>>("value = 3.5E+38"));
        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<Dictionary<string, Half>>("value = 65520.0"));
        Assert.True(float.IsPositiveInfinity(TomlSerializer.Deserialize<Dictionary<string, float>>("value = inf")!["value"]));
    }
}
