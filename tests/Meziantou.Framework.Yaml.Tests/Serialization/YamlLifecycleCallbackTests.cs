#pragma warning disable MA0048 // File name must match type name
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deserialize_Struct_KeepsMemberValues(bool useSourceGeneration)
    {
        var value = Deserialize<LifecyclePlainStruct>("X: 1\nName: text\n", useSourceGeneration);

        Assert.Equal(1, value.X);
        Assert.Equal("text", value.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deserialize_Struct_KeepsMutationsOfCallbacks(bool useSourceGeneration)
    {
        var value = Deserialize<LifecycleStruct>("X: 1\n", useSourceGeneration);

        Assert.Equal(1, value.X);
        Assert.Equal("ing(0)ed(1)", value.Log);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Serialize_Struct_WritesMutationsOfSerializingCallback(bool useSourceGeneration)
    {
        var yaml = Serialize(new LifecycleStruct { X = 1 }, useSourceGeneration);

        Assert.Equal("X: 1\nLog: serializing\n", yaml);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deserialize_SealedTypeImplementingSomeCallbacks_InvokesThem(bool useSourceGeneration)
    {
        var value = Deserialize<LifecycleSealedPartialModel>("Value: 3\n", useSourceGeneration);

        Assert.Equal(3, value.Value);
        Assert.Equal(1, value.DeserializedCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Callback_ThrowingYamlException_IsNotWrapped(bool useSourceGeneration)
    {
        var writeException = Assert.Throws<YamlException>(() => Serialize(new LifecycleYamlExceptionModel(), useSourceGeneration));
        Assert.Equal("write", writeException.Message);
        Assert.Null(writeException.InnerException);

        var readException = Assert.Throws<YamlException>(() => Deserialize<LifecycleYamlExceptionModel>("Value: 1\n", useSourceGeneration));
        Assert.Equal("read", readException.Message);
        Assert.Null(readException.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deserialize_WhenDeserializedCallbackThrows_ReportsTheEndOfTheMapping(bool useSourceGeneration)
    {
        var exception = Assert.Throws<YamlException>(() => Deserialize<LifecycleThrowingDeserializedModel>("Value: 1\n", useSourceGeneration));

        Assert.Equal("(Lin: 1, Col: 0, Chr: 9) - (Lin: 1, Col: 0, Chr: 9): An error occurred while invoking 'IYamlOnDeserialized.OnDeserialized' on 'Meziantou.Framework.Yaml.Tests.Serialization.LifecycleThrowingDeserializedModel'.", exception.Message);
        Assert.Equal("boom", exception.InnerException!.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deserialize_InitOnlyAndRequiredMembers_DeserializingCallbackRunsBeforeMembersAreRead(bool useSourceGeneration)
    {
        var value = Deserialize<LifecycleInitOnlyModel>("Name: a\nValue: 1\n", useSourceGeneration);

        Assert.Equal("a", value.Name);
        Assert.Equal(1, value.Value);
        Assert.Equal("ing(,0)ed(a,1)", value.Log);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deserialize_StructWithYamlConstructor_KeepsMutationsOfCallbacks(bool useSourceGeneration)
    {
        var value = Deserialize<LifecycleConstructorStruct>("X: 1\nExtra: 2\n", useSourceGeneration);

        Assert.Equal(1, value.X);
        Assert.Equal(2, value.Extra);
        Assert.Equal("ing(1,0)ed(1,2)", value.Log);
    }

    private static string Serialize<T>(T value, bool useSourceGeneration)
        => useSourceGeneration
            ? YamlSerializer.Serialize(value, LifecycleCallbackContext.Default)
            : YamlSerializer.Serialize(value);

    private static T Deserialize<T>(string yaml, bool useSourceGeneration)
        => useSourceGeneration
            ? YamlSerializer.Deserialize<T>(yaml, LifecycleCallbackContext.Default)!
            : YamlSerializer.Deserialize<T>(yaml)!;

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

internal struct LifecyclePlainStruct
{
    public int X { get; set; }

    public string? Name { get; set; }
}

internal struct LifecycleStruct : IYamlOnDeserializing, IYamlOnDeserialized, IYamlOnSerializing
{
    public int X { get; set; }

    public string? Log { get; set; }

    public void OnDeserializing() => Log += $"ing({X})";

    public void OnDeserialized() => Log += $"ed({X})";

    public void OnSerializing() => Log = "serializing";
}

internal sealed class LifecycleSealedPartialModel : IYamlOnDeserialized
{
    public int Value { get; set; }

    [YamlIgnore]
    public int DeserializedCount { get; private set; }

    public void OnDeserialized() => DeserializedCount++;
}

internal sealed class LifecycleYamlExceptionModel : IYamlOnSerializing, IYamlOnDeserialized
{
    public int Value { get; set; }

    public void OnSerializing() => throw new YamlException("write");

    public void OnDeserialized() => throw new YamlException("read");
}

internal sealed class LifecycleThrowingDeserializedModel : IYamlOnDeserialized
{
    public int Value { get; set; }

    public void OnDeserialized() => throw new InvalidOperationException("boom");
}

internal sealed class LifecycleInitOnlyModel : IYamlOnDeserializing, IYamlOnDeserialized
{
    public required string Name { get; init; }

    public int Value { get; init; }

    [YamlIgnore]
    public string Log { get; private set; } = "";

    public void OnDeserializing() => Log += $"ing({Name},{Value})";

    public void OnDeserialized() => Log += $"ed({Name},{Value})";
}

internal struct LifecycleConstructorStruct : IYamlOnDeserializing, IYamlOnDeserialized
{
    [YamlConstructor]
    public LifecycleConstructorStruct(int x) => X = x;

    public int X { get; }

    public int Extra { get; set; }

    [YamlIgnore]
    public string? Log { get; private set; }

    public void OnDeserializing() => Log += $"ing({X},{Extra})";

    public void OnDeserialized() => Log += $"ed({X},{Extra})";
}

[YamlSerializable(typeof(LifecycleInitOnlyModel))]
[YamlSerializable(typeof(LifecycleConstructorStruct))]
[YamlSerializable(typeof(LifecyclePlainStruct))]
[YamlSerializable(typeof(LifecycleStruct))]
[YamlSerializable(typeof(LifecycleSealedPartialModel))]
[YamlSerializable(typeof(LifecycleYamlExceptionModel))]
[YamlSerializable(typeof(LifecycleThrowingDeserializedModel))]
internal sealed partial class LifecycleCallbackContext : YamlSerializerContext
{
}
