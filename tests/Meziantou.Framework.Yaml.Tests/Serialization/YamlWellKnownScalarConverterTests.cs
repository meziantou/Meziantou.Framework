#if NET11_0_OR_GREATER
using System.Numerics;
#endif
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

        Assert.Equal("\"https://example.com/a%20b\"\n", yaml, ignoreLineEndingDifferences: true);
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
}
