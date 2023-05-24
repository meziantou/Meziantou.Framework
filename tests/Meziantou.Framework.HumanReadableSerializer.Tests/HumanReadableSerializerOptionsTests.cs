using Meziantou.Framework.HumanReadable;
using Xunit;

namespace Meziantou.Framework.HumanReadableSerializer.Tests;
public sealed class HumanReadableSerializerOptionsTests
{
    [Fact]
    public void CloneShouldCreateNewConvertersInstance()
    {
        var options = new HumanReadableSerializerOptions();
        options.Converters.Add(new DummyConverter());
        HumanReadable.HumanReadableSerializer.Serialize(12, options);

        var clone = options with { };

        Assert.Equal(1, clone.Converters.Count);
        clone.Converters.Clear();
        HumanReadable.HumanReadableSerializer.Serialize(12, clone);

        Assert.Equal(0, clone.Converters.Count);
        Assert.Equal(1, options.Converters.Count);
    }

    private sealed class DummyConverter : HumanReadableConverter
    {
        public override bool CanConvert(Type type) => false;
        public override void WriteValue(HumanReadableTextWriter writer, object value, HumanReadableSerializerOptions options) { }
    }
}
