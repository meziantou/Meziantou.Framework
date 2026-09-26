using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml.Tests;

#pragma warning disable MA0048 // File name must match type name

public sealed class BomInputTests
{
    [Fact]
    public void TomlReader_StringWithBom_IsAccepted()
    {
        var reader = TomlReader.Create("\uFEFFa = 1\n");
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.Equal("a", reader.PropertyName);
        Assert.True(reader.Read());
        Assert.Equal(1L, reader.GetInt64());
    }
}
