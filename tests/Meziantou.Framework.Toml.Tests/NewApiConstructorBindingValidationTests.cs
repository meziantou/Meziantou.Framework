
namespace Meziantou.Framework.Toml.Tests;

public sealed class NewApiConstructorBindingValidationTests
{
    private sealed class CollisionType
    {
#pragma warning disable IDE1006 // The parameter names only differ by case on purpose
        public CollisionType(int a, int A)
#pragma warning restore IDE1006
        {
            Lower = a;
            Upper = A;
        }

        public int Lower { get; }

        public int Upper { get; }
    }

    [Fact]
    public void ConstructorParameterNameCollision_ThrowsTomlException()
    {
        var options = TomlSerializerOptions.Default with
        {
            PropertyNameCaseInsensitive = true,
        };

        Assert.Throws<TomlException>(() => TomlSerializer.Serialize(new CollisionType(1, 2), options));
    }
}

