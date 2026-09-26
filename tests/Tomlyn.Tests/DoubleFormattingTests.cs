
namespace Tomlyn.Tests;

public sealed class DoubleFormattingTests
{
    private sealed class Model
    {
        public double Value { get; set; }
    }

    [Fact]
    public void SerializeDeserialize_Double_Roundtrips()
    {
        const double ExpectedValue = 126.92842769438576;

        var toml = TomlSerializer.Serialize(new Model { Value = ExpectedValue });
        var roundtrip = TomlSerializer.Deserialize<Model>(toml);

        Assert.NotNull(roundtrip);
        Assert.Equal(ExpectedValue, roundtrip!.Value);
    }
}
