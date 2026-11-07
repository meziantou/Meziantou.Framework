#if NET11_0_OR_GREATER
using System.Numerics;
#endif
using Meziantou.Framework.Yaml.Serialization;
using Meziantou.Xunit;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlWellKnownScalarConverterTests
{
    private sealed class Payload
    {
        public DateTime WhenUtc { get; set; }
        public DateTimeOffset WhenOffset { get; set; }
        public Guid Id { get; set; }
        public TimeSpan Duration { get; set; }
    }

    private sealed class ModernPayload
    {
        public DateOnly Date { get; set; }
        public TimeOnly Time { get; set; }
        public Half Ratio { get; set; }
        public Int128 Big { get; set; }
        public UInt128 UBig { get; set; }
    }

#if NET11_0_OR_GREATER
    private sealed class Ieee754Payload
    {
        public BFloat16 Brain { get; set; }
        public Decimal32 Small { get; set; }
        public Decimal64 Medium { get; set; }
        public Decimal128 Large { get; set; }
        public Decimal64? OptionalMedium { get; set; }
    }
#endif

    private sealed class UriAndCulturePayload
    {
        public Uri? Absolute { get; set; }
        public Uri? Relative { get; set; }
        public CultureInfo? Culture { get; set; }
    }

    private sealed class NullablePayload
    {
        public DateTimeOffset? PublishDate { get; set; }
        public bool? AllowPostingOnSocialMedia { get; set; }
    }

    [Fact]
    public void RoundTrip_WellKnownScalarTypes_ShouldSucceed()
    {
        var payload = new Payload
        {
            WhenUtc = new DateTime(2026, 03, 01, 12, 34, 56, DateTimeKind.Utc),
            WhenOffset = new DateTimeOffset(2026, 03, 01, 12, 34, 56, TimeSpan.FromHours(2)),
            Id = new Guid(0x6d0c86e2, 0x1e37, 0x4c33, 0x9c, 0x2f, 0x53, 0x4, 0xa3, 0x3f, 0x2c, 0x5e) /* 6d0c86e2-1e37-4c33-9c2f-5304a33f2c5e */,
            Duration = TimeSpan.FromMilliseconds(1234),
        };

        var yaml = YamlSerializer.Serialize(payload);
        var roundTrip = YamlSerializer.Deserialize<Payload>(yaml);

        Assert.NotNull(roundTrip);
        Assert.Equal(payload.WhenUtc, roundTrip.WhenUtc);
        Assert.Equal(payload.WhenOffset, roundTrip.WhenOffset);
        Assert.Equal(payload.Id, roundTrip.Id);
        Assert.Equal(payload.Duration, roundTrip.Duration);
    }

    [Fact]
    public void Deserialize_InvalidGuid_ShouldThrowYamlExceptionWithContext()
    {
        var ex = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Guid>("not-a-guid"));
        Assert.Contains("Guid", ex.Message);
        // Marks are zero-based, so line/column can be 0 for a scalar at the start of the document.
        Assert.Contains("Lin:", ex.Message);
        Assert.Contains("Col:", ex.Message);
    }

    [Fact]
    public void RoundTrip_ModernScalarTypes_ShouldSucceed()
    {
        var payload = new ModernPayload
        {
            Date = new DateOnly(2026, 03, 01),
            Time = new TimeOnly(12, 34, 56),
            Ratio = (Half)1.5f,
            Big = Int128.Parse("123456789012345678901234567890", CultureInfo.InvariantCulture),
            UBig = UInt128.Parse("123456789012345678901234567891", CultureInfo.InvariantCulture),
        };

        var yaml = YamlSerializer.Serialize(payload);
        var roundTrip = YamlSerializer.Deserialize<ModernPayload>(yaml);

        Assert.NotNull(roundTrip);
        Assert.Equal(payload.Date, roundTrip.Date);
        Assert.Equal(payload.Time, roundTrip.Time);
        Assert.Equal(payload.Ratio, roundTrip.Ratio);
        Assert.Equal(payload.Big, roundTrip.Big);
        Assert.Equal(payload.UBig, roundTrip.UBig);
    }

    [Fact]
    public void Deserialize_InvalidInt128_ShouldThrowYamlExceptionWithContext()
    {
        var ex = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Int128>("not-an-int128"));
        Assert.Contains("Int128", ex.Message);
        Assert.Contains("Lin:", ex.Message);
        Assert.Contains("Col:", ex.Message);
    }

#if NET11_0_OR_GREATER
    [Fact]
    public void RoundTrip_Ieee754ScalarTypes_ShouldSucceed()
    {
        var payload = new Ieee754Payload
        {
            Brain = (BFloat16)1.5f,
            Small = Decimal32.Parse("-5.30", CultureInfo.InvariantCulture),
            Medium = Decimal64.Parse("123.456", CultureInfo.InvariantCulture),
            Large = Decimal128.Pi,
            OptionalMedium = null,
        };

        var yaml = YamlSerializer.Serialize(payload);
        var roundTrip = YamlSerializer.Deserialize<Ieee754Payload>(yaml);

        Assert.Equal("""
            Brain: 1.5
            Small: -5.30
            Medium: 123.456
            Large: 3.141592653589793238462643383279503
            OptionalMedium: null

            """, yaml, ignoreLineEndingDifferences: true);

        Assert.NotNull(roundTrip);
        Assert.Equal(payload.Brain, roundTrip.Brain);
        Assert.Equal(payload.Small, roundTrip.Small);
        Assert.Equal(payload.Medium, roundTrip.Medium);
        Assert.Equal(payload.Large, roundTrip.Large);
        Assert.Null(roundTrip.OptionalMedium);
    }

    [Theory]
    [InlineData(".inf")]
    [InlineData("+.inf")]
    [InlineData("-.inf")]
    [InlineData(".nan")]
    public void Deserialize_Ieee754NamedLiterals_ShouldSucceed(string scalar)
    {
        var expected = scalar switch
        {
            ".nan" => Decimal64.NaN,
            "-.inf" => Decimal64.NegativeInfinity,
            _ => Decimal64.PositiveInfinity,
        };

        var value = YamlSerializer.Deserialize<Decimal64>(scalar);

        Assert.Equal(expected.ToString(null, CultureInfo.InvariantCulture), value.ToString(null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Serialize_Ieee754NonFiniteValues_ShouldUseYamlLiterals()
    {
        Assert.Equal(".inf\n", YamlSerializer.Serialize(BFloat16.PositiveInfinity), ignoreLineEndingDifferences: true);
        Assert.Equal("-.inf\n", YamlSerializer.Serialize(Decimal32.NegativeInfinity), ignoreLineEndingDifferences: true);
        Assert.Equal(".nan\n", YamlSerializer.Serialize(Decimal128.NaN), ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Deserialize_Ieee754UnderscoreSeparators_ShouldSucceed()
    {
        var value = YamlSerializer.Deserialize<Decimal128>("1_000.5");

        Assert.Equal(Decimal128.Parse("1000.5", CultureInfo.InvariantCulture), value);
    }

    [Fact]
    public void Deserialize_InvalidDecimal64_ShouldThrowYamlExceptionWithContext()
    {
        var ex = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Decimal64>("not-a-decimal64"));
        Assert.Contains("Decimal64", ex.Message);
        Assert.Contains("Lin:", ex.Message);
        Assert.Contains("Col:", ex.Message);
    }

    [Fact]
    public void Deserialize_InvalidBFloat16_ShouldThrowYamlExceptionWithContext()
    {
        var ex = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<BFloat16>("not-a-bfloat16"));
        Assert.Contains("BFloat16", ex.Message);
    }
#endif

    [Fact]
    public void RoundTrip_UriAndCultureInfo_ShouldSucceed()
    {
        var payload = new UriAndCulturePayload
        {
            Absolute = new Uri("https://example.com/path?query=1#fragment", UriKind.Absolute),
            Relative = new Uri("path/to/resource", UriKind.Relative),
            Culture = CultureInfo.InvariantCulture,
        };

        var yaml = YamlSerializer.Serialize(payload);
        var roundTrip = YamlSerializer.Deserialize<UriAndCulturePayload>(yaml);

        Assert.NotNull(roundTrip);
        Assert.Equal(payload.Absolute, roundTrip.Absolute);
        Assert.Equal(payload.Relative, roundTrip.Relative);
        Assert.Equal(CultureInfo.InvariantCulture, roundTrip.Culture);
    }

    [Fact]
    public void Serialize_Uri_ShouldUseOriginalString()
    {
        var yaml = YamlSerializer.Serialize(new Uri("https://example.com/a%20b", UriKind.Absolute));

        Assert.Equal("https://example.com/a%20b\n", yaml, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void RoundTrip_NullUriAndCultureInfo_ShouldSucceed()
    {
        var yaml = YamlSerializer.Serialize(new UriAndCulturePayload());
        var roundTrip = YamlSerializer.Deserialize<UriAndCulturePayload>(yaml);

        Assert.NotNull(roundTrip);
        Assert.Null(roundTrip.Absolute);
        Assert.Null(roundTrip.Relative);
        Assert.Null(roundTrip.Culture);
    }

    [Fact]
    public void RoundTrip_UriDictionaryKey_ShouldSucceed()
    {
        var payload = new Dictionary<Uri, string>
        {
            [new Uri("https://example.com/", UriKind.Absolute)] = "root",
            [new Uri("relative", UriKind.Relative)] = "other",
        };

        var yaml = YamlSerializer.Serialize(payload);
        var roundTrip = YamlSerializer.Deserialize<Dictionary<Uri, string>>(yaml);

        Assert.NotNull(roundTrip);
        Assert.Equal(payload, roundTrip);
    }

    [Fact, RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void RoundTrip_SpecificCulture_ShouldSucceed()
    {
        var yaml = YamlSerializer.Serialize(CultureInfo.GetCultureInfo("fr-FR"));

        Assert.Equal("fr-FR\n", yaml, ignoreLineEndingDifferences: true);
        Assert.Equal(CultureInfo.GetCultureInfo("fr-FR"), YamlSerializer.Deserialize<CultureInfo>(yaml));
    }

    [Fact]
    public void Deserialize_InvalidUri_ShouldThrowYamlExceptionWithContext()
    {
        var ex = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Uri>("\"http://\""));
        Assert.Contains("Uri", ex.Message);
        Assert.Contains("Lin:", ex.Message);
        Assert.Contains("Col:", ex.Message);
    }

    [Fact]
    public void Deserialize_InvalidCultureInfo_ShouldThrowYamlExceptionWithContext()
    {
        var ex = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<CultureInfo>("not a culture!"));
        Assert.Contains("CultureInfo", ex.Message);
        Assert.Contains("Lin:", ex.Message);
        Assert.Contains("Col:", ex.Message);
    }

    [Fact]
    public void Serialize_NullableDateTimeOffsetAndBoolean_ShouldRemainPlain()
    {
        var payload = new NullablePayload
        {
            PublishDate = new DateTimeOffset(2019, 06, 17, 0, 0, 0, TimeSpan.Zero),
            AllowPostingOnSocialMedia = false,
        };

        var yaml = YamlSerializer.Serialize(payload);

        Assert.Equal("""
            PublishDate: 2019-06-17T00:00:00.0000000Z
            AllowPostingOnSocialMedia: false

            """, yaml, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Serialize_DateTimeAndDateTimeOffset_UseZSuffixForUtc()
    {
        var payload = new Payload
        {
            WhenUtc = new DateTime(2027, 04, 19, 12, 00, 00, DateTimeKind.Utc),
            WhenOffset = new DateTimeOffset(2027, 04, 19, 12, 00, 00, TimeSpan.Zero),
            Id = Guid.Empty,
            Duration = TimeSpan.Zero,
        };

        var yaml = YamlSerializer.Serialize(payload);

        Assert.Contains("WhenUtc: 2027-04-19T12:00:00.0000000Z", yaml);
        Assert.Contains("WhenOffset: 2027-04-19T12:00:00.0000000Z", yaml);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuiltInScalarRoots_RoundTrip(bool useSourceGeneration)
    {
        AssertRoundTrip(new Guid("a4b7b0e6-8d2e-4d33-9d8e-6a8e0e4a1d2c"), "a4b7b0e6-8d2e-4d33-9d8e-6a8e0e4a1d2c\n", useSourceGeneration);
        AssertRoundTrip(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc), "2024-01-02T03:04:05.0000000Z\n", useSourceGeneration);
        AssertRoundTrip(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)), "2024-01-02T03:04:05.0000000+02:00\n", useSourceGeneration);
        AssertRoundTrip(TimeSpan.FromMinutes(90), "01:30:00\n", useSourceGeneration);
        AssertRoundTrip(new DateOnly(2024, 1, 2), "2024-01-02\n", useSourceGeneration);
        AssertRoundTrip(new TimeOnly(3, 4, 5), "03:04:05.0000000\n", useSourceGeneration);
        AssertRoundTrip((Half)1.5f, "1.5\n", useSourceGeneration);
        AssertRoundTrip(Int128.MaxValue, "170141183460469231731687303715884105727\n", useSourceGeneration);
        AssertRoundTrip(UInt128.MaxValue, "340282366920938463463374607431768211455\n", useSourceGeneration);
        AssertRoundTrip(new Uri("relative/path", UriKind.Relative), "relative/path\n", useSourceGeneration);
        AssertRoundTrip(CultureInfo.InvariantCulture, "''\n", useSourceGeneration);
        Assert.Null(Deserialize<Uri>("~\n", useSourceGeneration));
        Assert.Equal(Half.PositiveInfinity, Deserialize<Half>(".inf\n", useSourceGeneration));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Char_TildeIsNotReadAsNull(bool useSourceGeneration)
    {
        var yaml = Serialize(new List<char?> { '~', null }, useSourceGeneration);

        Assert.Equal("- \"~\"\n- null\n", yaml, ignoreLineEndingDifferences: true);
        Assert.Equal(new char?[] { '~', null }, Deserialize<List<char?>>(yaml, useSourceGeneration)!);
        Assert.Equal('~', Deserialize<char?>(Serialize<char?>('~', useSourceGeneration), useSourceGeneration));
        Assert.Equal("\"~\"\n", YamlSerializer.Serialize<object>('~'), ignoreLineEndingDifferences: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BigIntegerVersionAndRune_RoundTripAsScalars(bool useSourceGeneration)
    {
        var big = System.Numerics.BigInteger.Parse("-123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890", CultureInfo.InvariantCulture);
        AssertRoundTrip(big, "-123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890\n", useSourceGeneration);
        AssertRoundTrip(new System.Version(1, 2, 3), "1.2.3\n", useSourceGeneration);
        AssertRoundTrip(new System.Text.Rune(0x1F600), "\"\U0001F600\"\n", useSourceGeneration);
        AssertRoundTrip(new System.Text.Rune('~'), "\"~\"\n", useSourceGeneration);
        Assert.Null(Deserialize<System.Version>("~\n", useSourceGeneration));

        var model = new ExtendedScalarModel
        {
            Big = System.Numerics.BigInteger.MinusOne,
            OptionalBig = null,
            Version = new System.Version(2, 0),
            Rune = new System.Text.Rune('a'),
            OptionalRune = new System.Text.Rune('b'),
            VersionKeys = new Dictionary<System.Version, System.Numerics.BigInteger> { [new System.Version(3, 1)] = System.Numerics.BigInteger.One },
            RuneKeys = new Dictionary<System.Text.Rune, int> { [new System.Text.Rune('~')] = 1 },
        };

        var yaml = Serialize(model, useSourceGeneration);
        var roundTrip = Deserialize<ExtendedScalarModel>(yaml, useSourceGeneration)!;

        Assert.Equal("Big: -1\nOptionalBig: null\nVersion: 2.0\nRune: a\nOptionalRune: b\nVersionKeys:\n  3.1: 1\nRuneKeys:\n  \"~\": 1\n", yaml, ignoreLineEndingDifferences: true);
        Assert.Equal(model.Big, roundTrip.Big);
        Assert.Null(roundTrip.OptionalBig);
        Assert.Equal(model.Version, roundTrip.Version);
        Assert.Equal(model.Rune, roundTrip.Rune);
        Assert.Equal(model.OptionalRune, roundTrip.OptionalRune);
        Assert.Equal(model.VersionKeys, roundTrip.VersionKeys!);
        Assert.Equal(model.RuneKeys, roundTrip.RuneKeys!);

        Assert.Throws<YamlException>(() => Deserialize<System.Numerics.BigInteger>("1.5\n", useSourceGeneration));
        Assert.Throws<YamlException>(() => Deserialize<System.Version>("one\n", useSourceGeneration));
        Assert.Throws<YamlException>(() => Deserialize<System.Text.Rune>("ab\n", useSourceGeneration));
        Assert.Throws<YamlException>(() => Deserialize<System.Text.Rune>("''\n", useSourceGeneration));
    }

    [Theory, RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    [InlineData(false)]
    [InlineData(true)]
    public void NegativeBigInteger_IsWrittenWithInvariantCulture(bool useSourceGeneration)
    {
        var currentCulture = CultureInfo.CurrentCulture;
        try
        {
            // The negative sign of ar-SA starts with a directional mark.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");

            var yaml = Serialize(new ExtendedScalarModel { Big = -12, OptionalBig = -3 }, useSourceGeneration);

            Assert.Contains("Big: -12\nOptionalBig: -3\n", yaml);
            Assert.Equal(-12, Deserialize<ExtendedScalarModel>(yaml, useSourceGeneration)!.Big);
        }
        finally
        {
            CultureInfo.CurrentCulture = currentCulture;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UriWhoseTextIsNull_IsNotReadAsNull(bool useSourceGeneration)
    {
        foreach (var text in new[] { "null", "Null", "NULL", "~" })
        {
            var uri = new Uri(text, UriKind.Relative);
            var yaml = Serialize(uri, useSourceGeneration);

            Assert.Equal("\"" + text + "\"\n", yaml, ignoreLineEndingDifferences: true);
            Assert.Equal(uri, Deserialize<Uri>(yaml, useSourceGeneration));
            Assert.Equal([uri], Deserialize<List<Uri>>(Serialize(new List<Uri> { uri }, useSourceGeneration), useSourceGeneration)!);
        }
    }

    [Fact]
    public void CustomConverterWritingNullText_IsNotReadAsNull()
    {
        var options = new YamlSerializerOptions { Converters = [new NullTextConverter()] };

        var yaml = YamlSerializer.Serialize(new NullTextHolder { Value = new NullText() }, options);

        Assert.Equal("Value: \"null\"\n", yaml, ignoreLineEndingDifferences: true);
        Assert.NotNull(YamlSerializer.Deserialize<NullTextHolder>(yaml, options)!.Value);
    }

    private sealed class NullText
    {
    }

    private sealed class NullTextHolder
    {
        public NullText? Value { get; set; }
    }

    private sealed class NullTextConverter : YamlConverter<NullText>
    {
        public override NullText? Read(YamlReader reader)
        {
            reader.Read();
            return new NullText();
        }

        public override void Write(YamlWriter writer, NullText value) => writer.WriteScalar("null");
    }

    private static void AssertRoundTrip<T>(T value, string expectedYaml, bool useSourceGeneration)
    {
        var yaml = Serialize(value, useSourceGeneration);

        Assert.Equal(expectedYaml, yaml, ignoreLineEndingDifferences: true);
        Assert.Equal(value, Deserialize<T>(yaml, useSourceGeneration));
    }

    private static string Serialize<T>(T value, bool useSourceGeneration)
        => useSourceGeneration
            ? YamlSerializer.Serialize(value, WellKnownScalarRootYamlContext.Default)
            : YamlSerializer.Serialize(value);

    private static T? Deserialize<T>(string yaml, bool useSourceGeneration)
        => useSourceGeneration
            ? YamlSerializer.Deserialize<T>(yaml, WellKnownScalarRootYamlContext.Default)
            : YamlSerializer.Deserialize<T>(yaml);
}

#pragma warning disable MA0048 // File name must match type name
internal sealed class ExtendedScalarModel
{
    public System.Numerics.BigInteger Big { get; set; }

    public System.Numerics.BigInteger? OptionalBig { get; set; }

    public System.Version? Version { get; set; }

    public System.Text.Rune Rune { get; set; }

    public System.Text.Rune? OptionalRune { get; set; }

    public Dictionary<System.Version, System.Numerics.BigInteger>? VersionKeys { get; set; }

    public Dictionary<System.Text.Rune, int>? RuneKeys { get; set; }
}

[YamlSerializable(typeof(ExtendedScalarModel))]
[YamlSerializable(typeof(System.Numerics.BigInteger))]
[YamlSerializable(typeof(System.Version))]
[YamlSerializable(typeof(System.Text.Rune))]
[YamlSerializable(typeof(List<Uri>))]
[YamlSerializable(typeof(Guid))]
[YamlSerializable(typeof(DateTime))]
[YamlSerializable(typeof(DateTimeOffset))]
[YamlSerializable(typeof(TimeSpan))]
[YamlSerializable(typeof(DateOnly))]
[YamlSerializable(typeof(TimeOnly))]
[YamlSerializable(typeof(Half))]
[YamlSerializable(typeof(Int128))]
[YamlSerializable(typeof(UInt128))]
[YamlSerializable(typeof(Uri))]
[YamlSerializable(typeof(CultureInfo))]
[YamlSerializable(typeof(char?))]
[YamlSerializable(typeof(List<char?>))]
internal sealed partial class WellKnownScalarRootYamlContext : YamlSerializerContext
{
}
