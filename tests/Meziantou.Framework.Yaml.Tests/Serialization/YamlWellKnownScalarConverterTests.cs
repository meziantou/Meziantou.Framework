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
}
