using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml.Tests;

public sealed class NewApiPolymorphismValidationTests
{
    [TomlPolymorphic]
    [TomlDerivedType(typeof(DerivedEmptyDiscriminator), "")]
    private abstract class BaseEmptyDiscriminator
    {
    }

    private sealed class DerivedEmptyDiscriminator : BaseEmptyDiscriminator
    {
        public int Value { get; set; } = 1;
    }

    [TomlPolymorphic]
    [TomlDerivedType(typeof(string), "x")]
    private abstract class BaseNotAssignable
    {
    }

    private sealed class DerivedNotAssignable : BaseNotAssignable
    {
        public int Value { get; set; } = 1;
    }

    [Fact]
    public void TomlDerivedType_EmptyDiscriminator_ThrowsTomlException()
    {
        Assert.Throws<TomlException>(() => TomlSerializer.Serialize<BaseEmptyDiscriminator>(new DerivedEmptyDiscriminator()));
    }

    [Fact]
    public void TomlDerivedType_NotAssignable_ThrowsTomlException()
    {
        Assert.Throws<TomlException>(() => TomlSerializer.Serialize<BaseNotAssignable>(new DerivedNotAssignable()));
    }

    [Fact]
    public void PolymorphismOptions_EmptyDiscriminatorPropertyName_ThrowsTomlException()
    {
        var options = TomlSerializerOptions.Default with
        {
            PolymorphismOptions = new TomlPolymorphismOptions { TypeDiscriminatorPropertyName = "" },
        };

        Assert.Throws<TomlException>(() => TomlSerializer.Serialize<BaseNotAssignable>(new DerivedNotAssignable(), options));
    }
}

