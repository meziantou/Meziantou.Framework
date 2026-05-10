using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using Meziantou.Framework.FastEnumGenerator.GeneratorTests.Generated;
using Xunit;

[assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Meziantou.Framework.FastEnumGenerator.GeneratorTests.Color), ExtensionMethodNamespace = "Meziantou.Framework.FastEnumGenerator.GeneratorTests.Generated")]
[assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Meziantou.Framework.FastEnumGenerator.GeneratorTests.Permission), ExtensionMethodNamespace = "Meziantou.Framework.FastEnumGenerator.GeneratorTests.Generated")]

namespace Meziantou.Framework.FastEnumGenerator.GeneratorTests;

public enum Color
{
    [Display(Name = "Blue metadata")]
    Blue,

    [EnumMember(Value = "Red metadata")]
    Red,

    Green,
}

[Flags]
public enum Permission
{
    None = 0,
    Read = 1,
    Write = 2,
    Execute = 4,
}

public sealed class FastEnumGeneratedBehaviorTests
{
    [Fact]
    public void ToStringFast_UsesMetadataWhenRequested()
    {
        Assert.Equal("Blue", Color.Blue.ToStringFast());
        Assert.Equal("Blue metadata", Color.Blue.ToStringFast(useMetadata: true));
        Assert.Equal("Red metadata", Color.Red.ToStringFast(useMetadata: true));
    }

    [Fact]
    public void GetNameAndHasFlag_Work()
    {
        Assert.Equal("Green", Color.Green.GetName());
        Assert.True((Permission.Read | Permission.Write).HasFlag(Permission.Write));
    }

    [Fact]
    public void ParseAndTryParse_SupportMetadataAndFlags()
    {
        Assert.Equal(Color.Red, Color.Parse("Red", ignoreCase: false));
        Assert.Equal(Color.Red, Color.Parse("red metadata", ignoreCase: true, useMetadata: true));
        Assert.True(Color.TryParse("blue metadata", ignoreCase: true, useMetadata: true, out var color));
        Assert.Equal(Color.Blue, color);
        Assert.Equal(Permission.Read | Permission.Write, Permission.Parse("Read, Write", ignoreCase: false));
    }

    [Fact]
    public void IsDefinedGetNamesGetValues_Work()
    {
        Assert.True(Color.IsDefined(Color.Blue));
        Assert.False(Color.IsDefined((Color)42));
        Assert.Equal(["Blue", "Red", "Green"], Color.GetNames(useMetadata: false));
        Assert.Equal(["Blue metadata", "Red metadata", "Green"], Color.GetNames(useMetadata: true));
        Assert.Equal([Color.Blue, Color.Red, Color.Green], Color.GetValues());
    }
}
