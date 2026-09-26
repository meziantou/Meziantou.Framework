using System;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml.Tests;

#pragma warning disable MA0048 // File name must match type name

internal sealed class GeneratedBytePairModel
{
    public byte First { get; set; }

    public byte Second { get; set; }
}

[TomlSerializable(typeof(GeneratedBytePairModel))]
internal sealed partial class TestBuiltInConvertersContext : TomlSerializerContext
{
}

public sealed class NewApiBuiltInConvertersTests
{
    private sealed class GuidModel
    {
        public Guid Id { get; set; }
    }

    private enum SampleEnum
    {
        None = 0,
        First = 1,
        Second = 2,
    }

    private sealed class EnumModel
    {
        public SampleEnum Value { get; set; }
    }

    private sealed class ByteModel
    {
        public byte B { get; set; }
    }

    private sealed class BytePairModel
    {
        public byte First { get; set; }

        public byte Second { get; set; }
    }

    private sealed class DateTimeOffsetModel
    {
        public DateTimeOffset Value { get; set; }
    }

    [Fact]
    public void Guid_Roundtrip()
    {
        var model = new GuidModel { Id = Guid.NewGuid() };
        var toml = TomlSerializer.Serialize(model);
        var roundtrip = TomlSerializer.Deserialize<GuidModel>(toml);
        Assert.NotNull(roundtrip);
        Assert.Equal(model.Id, roundtrip!.Id);
    }

    [Fact]
    public void Enum_Roundtrip_Integer()
    {
        var model = new EnumModel { Value = SampleEnum.Second };
        var toml = TomlSerializer.Serialize(model);
        var roundtrip = TomlSerializer.Deserialize<EnumModel>(toml);
        Assert.NotNull(roundtrip);
        Assert.Equal(model.Value, roundtrip!.Value);
    }

    [Fact]
    public void Enum_ReadsStringName()
    {
        var model = TomlSerializer.Deserialize<EnumModel>("Value = \"Second\"");
        Assert.NotNull(model);
        Assert.Equal(SampleEnum.Second, model!.Value);
    }

    [Fact]
    public void Byte_Overflow_ReportsLocation()
    {
        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<ByteModel>("B = 256\n"));
        Assert.NotNull(ex);
        Assert.Equal(1, ex!.Line);
        Assert.Equal(5, ex.Column);
    }

    [Fact]
    public void Byte_Overflow_AggregatesMultipleLocations()
    {
        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<BytePairModel>("First = 256\nSecond = 300\n"));

        Assert.NotNull(ex);
        Assert.Equal(2, ex!.Diagnostics.Count);
        Assert.Equal(0, ex.Diagnostics[0].Span.Start.Line);
        Assert.Equal(8, ex.Diagnostics[0].Span.Start.Column);
        Assert.Contains("TOML integer value 256 is out of range.", ex.Diagnostics[0].Message);
        Assert.Equal(1, ex.Diagnostics[1].Span.Start.Line);
        Assert.Equal(9, ex.Diagnostics[1].Span.Start.Column);
        Assert.Contains("TOML integer value 300 is out of range.", ex.Diagnostics[1].Message);
    }

    [Fact]
    public void Byte_Overflow_GeneratedContextAggregatesMultipleLocations()
    {
        var context = TestBuiltInConvertersContext.Default;

        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize("First = 256\nSecond = 300\n", context.GeneratedBytePairModel));

        Assert.NotNull(ex);
        Assert.Equal(2, ex!.Diagnostics.Count);
        Assert.Equal(0, ex.Diagnostics[0].Span.Start.Line);
        Assert.Equal(8, ex.Diagnostics[0].Span.Start.Column);
        Assert.Contains("TOML integer value 256 is out of range.", ex.Diagnostics[0].Message);
        Assert.Equal(1, ex.Diagnostics[1].Span.Start.Line);
        Assert.Equal(9, ex.Diagnostics[1].Span.Start.Column);
        Assert.Contains("TOML integer value 300 is out of range.", ex.Diagnostics[1].Message);
    }

    [Fact]
    public void DateTimeOffset_RejectsLocalDateTime_ByDefault()
    {
        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<DateTimeOffsetModel>("Value = 1979-05-27T07:32:00\n"));
        Assert.NotNull(ex);
        Assert.Equal(1, ex!.Line);
        Assert.True(ex.Column > 0);
    }

    private sealed class DateOnlyModel
    {
        public DateOnly Date { get; set; }
    }

    private sealed class TimeOnlyModel
    {
        public TimeOnly Time { get; set; }
    }

    [Fact]
    public void DateOnly_Roundtrip()
    {
        var model = new DateOnlyModel { Date = new DateOnly(2024, 10, 31) };
        var toml = TomlSerializer.Serialize(model);
        var roundtrip = TomlSerializer.Deserialize<DateOnlyModel>(toml);
        Assert.NotNull(roundtrip);
        Assert.Equal(model.Date, roundtrip!.Date);
    }

    [Fact]
    public void TimeOnly_Roundtrip_MinuteOnly()
    {
        var model = TomlSerializer.Deserialize<TimeOnlyModel>("Time = 07:32\n");
        Assert.NotNull(model);
        Assert.Equal(new TimeOnly(7, 32, 0), model!.Time);
    }

    [Theory]
    [InlineData("0.1234567890123456789")]
    [InlineData("79228162514264337593543950335")]
    [InlineData("-79228162514264337593543950335")]
    [InlineData("12345678.901234567")]
    [InlineData("1.0")]
    public void Decimal_RoundtripsWithoutLosingDigits(string text)
    {
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        var toml = TomlSerializer.Serialize(new DecimalModel { D = value });

        Assert.Equal(value, TomlSerializer.Deserialize<DecimalModel>(toml)!.D);
        var literal = text.Contains('.', StringComparison.Ordinal) ? text : text + ".0";
        Assert.Equal(value, TomlSerializer.Deserialize<DecimalModel>("D = " + literal + "\n")!.D);
    }

    [Fact]
    public void Decimal_ReadsLiteralWithUnderscoresAndExponent()
    {
        Assert.Equal(1000.0001m, TomlSerializer.Deserialize<DecimalModel>("D = 1_000.000_1\n")!.D);
        Assert.Equal(12500m, TomlSerializer.Deserialize<DecimalModel>("D = 1.25e4\n")!.D);
    }

    [Theory]
    [InlineData("1e300")]
    [InlineData("nan")]
    [InlineData("-inf")]
    public void Decimal_OutOfRange_ThrowsTomlException(string literal)
    {
        var toml = "D = " + literal + "\n";

        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<DecimalModel>(toml));
        Assert.False(TomlSerializer.TryDeserialize<DecimalModel>(toml, out _));
    }

    private sealed class DecimalModel
    {
        public decimal D { get; set; }
    }

    [Theory]
    [InlineData("V = 300\n")]
    [InlineData("V = -1\n")]
    [InlineData("V = \"300\"\n")]
    [InlineData("V = \"Unknown\"\n")]
    public void Enum_OutOfRange_ThrowsTomlException(string toml)
    {
        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<ByteEnumModel>(toml));
        Assert.False(TomlSerializer.TryDeserialize<ByteEnumModel>(toml, out _));
    }

    [Theory]
    [InlineData("V = 255\n", ByteEnum.Max)]
    [InlineData("V = \"max\"\n", ByteEnum.Max)]
    [InlineData("V = 0\n", ByteEnum.Zero)]
    public void Enum_InRange_IsRead(string toml, ByteEnum expected)
    {
        Assert.Equal(expected, TomlSerializer.Deserialize<ByteEnumModel>(toml)!.V);
    }

    public enum ByteEnum : byte
    {
        Zero = 0,
        Max = 255,
    }

    private sealed class ByteEnumModel
    {
        public ByteEnum V { get; set; }
    }
}
