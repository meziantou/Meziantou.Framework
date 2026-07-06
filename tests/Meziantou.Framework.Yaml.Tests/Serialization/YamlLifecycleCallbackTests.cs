using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlLifecycleCallbackTests
{
    [Fact]
    public void Serialize_CallsSerializingAndSerialized()
    {
        var value = new LifecycleModel { Value = 123 };

        _ = YamlSerializer.Serialize(value);

        Assert.HasCount(2, value.Events);
        Assert.Equal(nameof(IYamlOnSerializing.OnSerializing), value.Events[0]);
        Assert.Equal(nameof(IYamlOnSerialized.OnSerialized), value.Events[1]);
    }

    [Fact]
    public void Deserialize_CallsDeserializingAndDeserialized()
    {
        var value = YamlSerializer.Deserialize<LifecycleModel>("value: 42\n")!;

        Assert.HasCount(2, value.Events);
        Assert.Equal(nameof(IYamlOnDeserializing.OnDeserializing), value.Events[0]);
        Assert.Equal(nameof(IYamlOnDeserialized.OnDeserialized), value.Events[1]);
    }

    [Fact]
    public void Serialize_WhenCallbackThrows_WrapsInYamlException()
    {
        var value = new ThrowingLifecycleModel();

        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Serialize(value));
        Assert.NotNull(exception.InnerException);
        Assert.Equal("boom", exception.InnerException!.Message);
    }

    private sealed class LifecycleModel : IYamlOnDeserializing, IYamlOnDeserialized, IYamlOnSerializing, IYamlOnSerialized
    {
        public List<string> Events { get; } = new();

        public int Value { get; set; }

        public void OnDeserialized() => Events.Add(nameof(IYamlOnDeserialized.OnDeserialized));

        public void OnDeserializing() => Events.Add(nameof(IYamlOnDeserializing.OnDeserializing));

        public void OnSerialized() => Events.Add(nameof(IYamlOnSerialized.OnSerialized));

        public void OnSerializing() => Events.Add(nameof(IYamlOnSerializing.OnSerializing));
    }

    private sealed class ThrowingLifecycleModel : IYamlOnSerializing
    {
        public void OnSerializing() => throw new InvalidOperationException("boom");
    }
}
