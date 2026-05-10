using Meziantou.Framework.FastEnumGenerator.GeneratorTests.Generated;
using Xunit;

[assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Meziantou.Framework.FastEnumGenerator.GeneratorTests.Color), ExtensionMethodNamespace = "Meziantou.Framework.FastEnumGenerator.GeneratorTests.Generated")]
[assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Meziantou.Framework.FastEnumGenerator.GeneratorTests.ColorWithAliases), ExtensionMethodNamespace = "Meziantou.Framework.FastEnumGenerator.GeneratorTests.Generated")]
[assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Meziantou.Framework.FastEnumGenerator.GeneratorTests.Permission), ExtensionMethodNamespace = "Meziantou.Framework.FastEnumGenerator.GeneratorTests.Generated")]
[assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Meziantou.Framework.FastEnumGenerator.GeneratorTests.PermissionWithCombination), ExtensionMethodNamespace = "Meziantou.Framework.FastEnumGenerator.GeneratorTests.Generated")]

namespace Meziantou.Framework.FastEnumGenerator.GeneratorTests;

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
        Assert.Equal(Color.Red, Color.Parse("Red".AsSpan(), ignoreCase: false));
        Assert.Equal(Color.Red, Color.Parse("red metadata", ignoreCase: true, useMetadata: true));
        Assert.Equal(Color.Red, Color.Parse("red metadata".AsSpan(), ignoreCase: true, useMetadata: true));
        Assert.True(Color.TryParse("Blue", ignoreCase: false, out var colorFromString));
        Assert.Equal(Color.Blue, colorFromString);
        Assert.True(Color.TryParse("Blue".AsSpan(), ignoreCase: false, out var colorFromSpan));
        Assert.Equal(Color.Blue, colorFromSpan);
        Assert.True(Color.TryParse("blue metadata", ignoreCase: true, useMetadata: true, out var color));
        Assert.Equal(Color.Blue, color);
        Assert.True(Color.TryParse("blue metadata".AsSpan(), ignoreCase: true, useMetadata: true, out var colorFromMetadataSpan));
        Assert.Equal(Color.Blue, colorFromMetadataSpan);
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

    [Fact]
    public void AllMethods_WorkWithMultipleNamesForSameValue()
    {
        Assert.Equal("Blue", ColorWithAliases.Blue.ToStringFast());
        Assert.Equal("Blue", ColorWithAliases.Azure.ToStringFast());
        Assert.Equal("Blue", ColorWithAliases.Azure.ToStringFast(useMetadata: true));
        Assert.Equal("Blue", ColorWithAliases.Azure.GetName());
        Assert.False(ColorWithAliases.Azure.HasFlag(ColorWithAliases.Red));
        Assert.Equal(ColorWithAliases.Blue, ColorWithAliases.Parse("Azure", ignoreCase: false));
        Assert.Equal(ColorWithAliases.Blue, ColorWithAliases.Parse("azure".AsSpan(), ignoreCase: true));
        Assert.Equal(ColorWithAliases.Blue, ColorWithAliases.Parse("azure", ignoreCase: true, useMetadata: true));
        Assert.Equal(ColorWithAliases.Blue, ColorWithAliases.Parse("Azure".AsSpan(), ignoreCase: false, useMetadata: true));
        Assert.True(ColorWithAliases.TryParse("Azure", ignoreCase: false, out var parsedFromString));
        Assert.Equal(ColorWithAliases.Blue, parsedFromString);
        Assert.True(ColorWithAliases.TryParse("Azure".AsSpan(), ignoreCase: false, out var parsedFromSpan));
        Assert.Equal(ColorWithAliases.Blue, parsedFromSpan);
        Assert.True(ColorWithAliases.TryParse("azure", ignoreCase: true, useMetadata: true, out var parsedFromStringWithMetadata));
        Assert.Equal(ColorWithAliases.Blue, parsedFromStringWithMetadata);
        Assert.True(ColorWithAliases.TryParse("Azure".AsSpan(), ignoreCase: false, useMetadata: true, out var parsedFromSpanWithMetadata));
        Assert.Equal(ColorWithAliases.Blue, parsedFromSpanWithMetadata);
        Assert.True(ColorWithAliases.IsDefined(ColorWithAliases.Azure));
        Assert.False(ColorWithAliases.IsDefined((ColorWithAliases)42));
        Assert.Equal(["Blue", "Azure", "Red"], ColorWithAliases.GetNames(useMetadata: false));
        Assert.Equal(["Blue", "Azure", "Red"], ColorWithAliases.GetNames(useMetadata: true));
        Assert.Equal([ColorWithAliases.Blue, ColorWithAliases.Azure, ColorWithAliases.Red], ColorWithAliases.GetValues());
    }

    [Fact]
    public void AllMethods_WorkWithFlagsAndDefinedCombination()
    {
        Assert.Equal("AandB", PermissionWithCombination.AandB.ToStringFast());
        Assert.Equal("AandB", (PermissionWithCombination.A | PermissionWithCombination.B).ToStringFast());
        Assert.Equal("AandB", PermissionWithCombination.AandB.ToStringFast(useMetadata: true));
        Assert.Equal("AandB", PermissionWithCombination.AandB.GetName());
        Assert.True(PermissionWithCombination.AandB.HasFlag(PermissionWithCombination.A));
        Assert.True(PermissionWithCombination.AandB.HasFlag(PermissionWithCombination.B));
        Assert.False(PermissionWithCombination.AandB.HasFlag(PermissionWithCombination.C));
        Assert.Equal(PermissionWithCombination.AandB, PermissionWithCombination.Parse("AandB", ignoreCase: false));
        Assert.Equal(PermissionWithCombination.AandB, PermissionWithCombination.Parse("A, B".AsSpan(), ignoreCase: false));
        Assert.Equal(PermissionWithCombination.AandB, PermissionWithCombination.Parse("aandb", ignoreCase: true, useMetadata: true));
        Assert.Equal(PermissionWithCombination.A | PermissionWithCombination.C, PermissionWithCombination.Parse("A, C".AsSpan(), ignoreCase: false, useMetadata: true));
        Assert.True(PermissionWithCombination.TryParse("AandB", ignoreCase: false, out var parsedFromString));
        Assert.Equal(PermissionWithCombination.AandB, parsedFromString);
        Assert.True(PermissionWithCombination.TryParse("A, B".AsSpan(), ignoreCase: false, out var parsedFromSpan));
        Assert.Equal(PermissionWithCombination.AandB, parsedFromSpan);
        Assert.True(PermissionWithCombination.TryParse("aandb", ignoreCase: true, useMetadata: true, out var parsedFromStringWithMetadata));
        Assert.Equal(PermissionWithCombination.AandB, parsedFromStringWithMetadata);
        Assert.True(PermissionWithCombination.TryParse("A, C".AsSpan(), ignoreCase: false, useMetadata: true, out var parsedFromSpanWithMetadata));
        Assert.Equal(PermissionWithCombination.A | PermissionWithCombination.C, parsedFromSpanWithMetadata);
        Assert.True(PermissionWithCombination.IsDefined(PermissionWithCombination.AandB));
        Assert.False(PermissionWithCombination.IsDefined(PermissionWithCombination.A | PermissionWithCombination.C));
        Assert.Equal(["None", "A", "B", "AandB", "C"], PermissionWithCombination.GetNames(useMetadata: false));
        Assert.Equal(["None", "A", "B", "AandB", "C"], PermissionWithCombination.GetNames(useMetadata: true));
        Assert.Equal([PermissionWithCombination.None, PermissionWithCombination.A, PermissionWithCombination.B, PermissionWithCombination.AandB, PermissionWithCombination.C], PermissionWithCombination.GetValues());
    }
}
