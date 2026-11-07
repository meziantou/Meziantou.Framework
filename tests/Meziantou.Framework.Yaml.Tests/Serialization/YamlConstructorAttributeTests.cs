using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlConstructorAttributeTests
{
    [Fact]
    public void Deserialize_UsesYamlConstructor()
    {
        var value = YamlSerializer.Deserialize<YamlCtorModel>("Name: Bob\nAge: 42\n")!;

        Assert.Equal("Bob", value.Name);
        Assert.Equal(42, value.Age);
    }

    [Fact]
    public void Deserialize_WhenConstructorParameterMissing_ThrowsYamlException()
    {
        var ex = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<YamlCtorModel>("Name: Bob\n"));
        Assert.Contains("age", ex.Message);
    }

    [Fact]
    public void Deserialize_UsesJsonConstructor()
    {
        var value = YamlSerializer.Deserialize<JsonCtorModel>("Name: Bob\nAge: 42\n")!;

        Assert.Equal("Bob", value.Name);
        Assert.Equal(42, value.Age);
    }

    [Fact]
    public void Deserialize_UsesPrivateYamlConstructor()
    {
        var value = YamlSerializer.Deserialize<PrivateYamlCtorModel>("Name: Bob\nAge: 42\n")!;

        Assert.Equal("Bob", value.Name);
        Assert.Equal(42, value.Age);
    }

    [Fact]
    public void Deserialize_UsesPrivateJsonConstructor()
    {
        var value = YamlSerializer.Deserialize<PrivateJsonCtorModel>("Name: Bob\nAge: 42\n")!;

        Assert.Equal("Bob", value.Name);
        Assert.Equal(42, value.Age);
    }

    [Fact]
    public void Serialize_SerializesGetOnlyProperties()
    {
        var yaml = YamlSerializer.Serialize(new YamlCtorModel("Bob", 42));

        Assert.Contains("Name: Bob", yaml);
        Assert.Contains("Age: 42", yaml);
    }

    private sealed class YamlCtorModel
    {
        public YamlCtorModel(string name, int age)
        {
            Name = name;
            Age = age;
        }

#pragma warning disable IDE0060 // Remove unused parameter
        [YamlConstructor]
        public YamlCtorModel(string name, int age, bool ignored = false)
        {
            Name = name;
            Age = age;
        }
#pragma warning restore IDE0060 // Remove unused parameter

        public string Name { get; }

        public int Age { get; }
    }

    private sealed class JsonCtorModel
    {
        [YamlConstructor]
        public JsonCtorModel(string name, int age)
        {
            Name = name;
            Age = age;
        }

        public string Name { get; }

        public int Age { get; }
    }

    private sealed class PrivateYamlCtorModel
    {
#pragma warning disable IDE0051 // Remove unused private members
        [YamlConstructor]
        private PrivateYamlCtorModel(string name, int age)
#pragma warning restore IDE0051 // Remove unused private members
        {
            Name = name;
            Age = age;
        }

        public string Name { get; }

        public int Age { get; }
    }

    private sealed class PrivateJsonCtorModel
    {
#pragma warning disable IDE0051 // Remove unused private members
        [YamlConstructor]
        private PrivateJsonCtorModel(string name, int age)
#pragma warning restore IDE0051 // Remove unused private members
        {
            Name = name;
            Age = age;
        }

        public string Name { get; }

        public int Age { get; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Serialize_DoesNotRequireADeserializationConstructor(bool useSourceGeneration)
    {
        var privateConstructorYaml = useSourceGeneration
            ? YamlSerializer.Serialize(PrivateConstructorOnlyModel.Create("Bob"), ConstructorYamlContext.Default)
            : YamlSerializer.Serialize(PrivateConstructorOnlyModel.Create("Bob"));
        var multipleConstructorsYaml = useSourceGeneration
            ? YamlSerializer.Serialize(new MultiplePublicConstructorsModel("Bob", 42), ConstructorYamlContext.Default)
            : YamlSerializer.Serialize(new MultiplePublicConstructorsModel("Bob", 42));

        Assert.Equal("Name: Bob\n", privateConstructorYaml);
        Assert.Equal("Name: Bob\nAge: 42\n", multipleConstructorsYaml);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deserialize_WithoutADeserializationConstructor_Throws(bool useSourceGeneration)
    {
        var privateConstructorException = Assert.Throws<YamlException>(() => useSourceGeneration
            ? YamlSerializer.Deserialize<PrivateConstructorOnlyModel>("Name: Bob\n", ConstructorYamlContext.Default)
            : YamlSerializer.Deserialize<PrivateConstructorOnlyModel>("Name: Bob\n"));
        var multipleConstructorsException = Assert.Throws<YamlException>(() => useSourceGeneration
            ? YamlSerializer.Deserialize<MultiplePublicConstructorsModel>("Name: Bob\n", ConstructorYamlContext.Default)
            : YamlSerializer.Deserialize<MultiplePublicConstructorsModel>("Name: Bob\n"));

        Assert.Contains("does not have a public constructor", privateConstructorException.Message);
        Assert.Contains("multiple public constructors", multipleConstructorsException.Message);
    }

    [Theory]
    [InlineData(typeof(PrivateConstructorOnlyModel), "Type 'Meziantou.Framework.Yaml.Tests.Serialization.YamlConstructorAttributeTests+PrivateConstructorOnlyModel' does not have a public constructor. Use 'Meziantou.Framework.Yaml.Serialization.YamlConstructorAttribute' to opt into a non-public constructor.")]
    [InlineData(typeof(MultiplePublicConstructorsModel), "Type 'Meziantou.Framework.Yaml.Tests.Serialization.YamlConstructorAttributeTests+MultiplePublicConstructorsModel' defines multiple public constructors. Use 'Meziantou.Framework.Yaml.Serialization.YamlConstructorAttribute' to select the constructor to use for deserialization.")]
    [InlineData(typeof(MultipleYamlConstructorsModel), "Type 'Meziantou.Framework.Yaml.Tests.Serialization.YamlConstructorAttributeTests+MultipleYamlConstructorsModel' defines multiple constructors annotated with 'Meziantou.Framework.Yaml.Serialization.YamlConstructorAttribute'.")]
    public void Deserialize_WithoutADeserializationConstructor_ReportsTheSameMessageInBothModes(Type type, string expectedMessage)
    {
        var reflectionException = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize("Name: Bob\n", type));
        var generatedException = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize("Name: Bob\n", type, ConstructorYamlContext.Default));

        Assert.EndsWith("): " + expectedMessage, reflectionException.Message);
        Assert.Equal(reflectionException.Message, generatedException.Message);
    }

    internal sealed class PrivateConstructorOnlyModel
    {
        private PrivateConstructorOnlyModel(string name) => Name = name;

        public string Name { get; }

        public static PrivateConstructorOnlyModel Create(string name) => new(name);
    }

    internal sealed class MultiplePublicConstructorsModel
    {
        public MultiplePublicConstructorsModel(string name) => Name = name;

        public MultiplePublicConstructorsModel(string name, int age)
        {
            Name = name;
            Age = age;
        }

        public string Name { get; }

        public int Age { get; }
    }

    internal sealed class MultipleYamlConstructorsModel
    {
        [YamlConstructor]
        public MultipleYamlConstructorsModel(string name) => Name = name;

        [YamlConstructor]
        public MultipleYamlConstructorsModel(string name, int age)
        {
            Name = name;
            Age = age;
        }

        public string Name { get; }

        public int Age { get; }
    }
}

#pragma warning disable MA0048 // File name must match type name
[YamlSerializable(typeof(YamlConstructorAttributeTests.PrivateConstructorOnlyModel))]
[YamlSerializable(typeof(YamlConstructorAttributeTests.MultiplePublicConstructorsModel))]
[YamlSerializable(typeof(YamlConstructorAttributeTests.MultipleYamlConstructorsModel))]
internal sealed partial class ConstructorYamlContext : YamlSerializerContext
{
}
