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
}
