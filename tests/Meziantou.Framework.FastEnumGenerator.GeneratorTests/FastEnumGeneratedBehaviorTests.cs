using Meziantou.Framework.FastEnumGenerator.GeneratorTests.Generated;
using Xunit;

[assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Meziantou.Framework.FastEnumGenerator.GeneratorTests.Color), ExtensionMethodNamespace = "Meziantou.Framework.FastEnumGenerator.GeneratorTests.Generated")]
[assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Meziantou.Framework.FastEnumGenerator.GeneratorTests.ColorWithAliases), ExtensionMethodNamespace = "Meziantou.Framework.FastEnumGenerator.GeneratorTests.Generated")]
[assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Meziantou.Framework.FastEnumGenerator.GeneratorTests.Permission), ExtensionMethodNamespace = "Meziantou.Framework.FastEnumGenerator.GeneratorTests.Generated")]
[assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Meziantou.Framework.FastEnumGenerator.GeneratorTests.PermissionWithCombination), ExtensionMethodNamespace = "Meziantou.Framework.FastEnumGenerator.GeneratorTests.Generated")]
[assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Meziantou.Framework.FastEnumGenerator.GeneratorTests.NonConsecutiveEnum), ExtensionMethodNamespace = "Meziantou.Framework.FastEnumGenerator.GeneratorTests.Generated")]
[assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Meziantou.Framework.FastEnumGenerator.GeneratorTests.NonZeroEnum), ExtensionMethodNamespace = "Meziantou.Framework.FastEnumGenerator.GeneratorTests.Generated")]
[assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Meziantou.Framework.FastEnumGenerator.GeneratorTests.ByteBasedEnum), ExtensionMethodNamespace = "Meziantou.Framework.FastEnumGenerator.GeneratorTests.Generated")]
[assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Meziantou.Framework.FastEnumGenerator.GeneratorTests.SByteBasedEnum), ExtensionMethodNamespace = "Meziantou.Framework.FastEnumGenerator.GeneratorTests.Generated")]
[assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Meziantou.Framework.FastEnumGenerator.GeneratorTests.Int16BasedEnum), ExtensionMethodNamespace = "Meziantou.Framework.FastEnumGenerator.GeneratorTests.Generated")]
[assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Meziantou.Framework.FastEnumGenerator.GeneratorTests.UInt16BasedEnum), ExtensionMethodNamespace = "Meziantou.Framework.FastEnumGenerator.GeneratorTests.Generated")]
[assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Meziantou.Framework.FastEnumGenerator.GeneratorTests.Int32BasedEnum), ExtensionMethodNamespace = "Meziantou.Framework.FastEnumGenerator.GeneratorTests.Generated")]
[assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Meziantou.Framework.FastEnumGenerator.GeneratorTests.UInt32BasedEnum), ExtensionMethodNamespace = "Meziantou.Framework.FastEnumGenerator.GeneratorTests.Generated")]
[assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Meziantou.Framework.FastEnumGenerator.GeneratorTests.Int64BasedEnum), ExtensionMethodNamespace = "Meziantou.Framework.FastEnumGenerator.GeneratorTests.Generated")]
[assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Meziantou.Framework.FastEnumGenerator.GeneratorTests.UInt64BasedEnum), ExtensionMethodNamespace = "Meziantou.Framework.FastEnumGenerator.GeneratorTests.Generated")]

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
        Assert.Equal(["Blue", "Red", "Green"], Color.GetNames(useMetadata: false).ToArray());
        Assert.Equal(["Blue metadata", "Red metadata", "Green"], Color.GetNames(useMetadata: true).ToArray());
        Assert.Equal([Color.Blue, Color.Red, Color.Green], Color.GetValues().ToArray());
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
        Assert.Equal(["Blue", "Azure", "Red"], ColorWithAliases.GetNames(useMetadata: false).ToArray());
        Assert.Equal(["Blue", "Azure", "Red"], ColorWithAliases.GetNames(useMetadata: true).ToArray());
        Assert.Equal([ColorWithAliases.Blue, ColorWithAliases.Azure, ColorWithAliases.Red], ColorWithAliases.GetValues().ToArray());
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
        Assert.Equal(["None", "A", "B", "AandB", "C"], PermissionWithCombination.GetNames(useMetadata: false).ToArray());
        Assert.Equal(["None", "A", "B", "AandB", "C"], PermissionWithCombination.GetNames(useMetadata: true).ToArray());
        Assert.Equal([PermissionWithCombination.None, PermissionWithCombination.A, PermissionWithCombination.B, PermissionWithCombination.AandB, PermissionWithCombination.C], PermissionWithCombination.GetValues().ToArray());
    }

    [Fact]
    public void ReadOnlySpanApis_ReturnStableData()
    {
        ReadOnlySpan<string> names = Color.GetNames(useMetadata: false);
        ReadOnlySpan<Color> values = Color.GetValues();
        Assert.Equal("Blue", names[0]);
        Assert.Equal(Color.Blue, values[0]);
        Assert.Equal(["Blue", "Red", "Green"], names.ToArray());
    }

    [Fact]
    public void NonConsecutiveAndNonZeroEnums_Work()
    {
        Assert.Equal("Two", NonConsecutiveEnum.Two.GetName());
        Assert.Equal("5", ((NonConsecutiveEnum)5).ToStringFast());
        Assert.True(NonConsecutiveEnum.IsDefined(NonConsecutiveEnum.Zero));
        Assert.False(NonConsecutiveEnum.IsDefined((NonConsecutiveEnum)1));
        Assert.Equal(["Zero", "Two", "Nine"], NonConsecutiveEnum.GetNames(useMetadata: false).ToArray());
        Assert.Equal([NonConsecutiveEnum.Zero, NonConsecutiveEnum.Two, NonConsecutiveEnum.Nine], NonConsecutiveEnum.GetValues().ToArray());

        Assert.Equal("Ten", NonZeroEnum.Ten.GetName());
        Assert.Equal("42", ((NonZeroEnum)42).ToStringFast());
        Assert.True(NonZeroEnum.IsDefined(NonZeroEnum.Ten));
        Assert.False(NonZeroEnum.IsDefined((NonZeroEnum)0));
        Assert.True(NonZeroEnum.TryParse("Ten", ignoreCase: false, out var nonZeroParsed));
        Assert.Equal(NonZeroEnum.Ten, nonZeroParsed);
    }

    [Fact]
    public void UInt64DenseEnum_DoesNotOverflowDenseIndexLookup()
    {
        Assert.Equal("One", UInt64BasedEnum.One.GetName());
        Assert.Equal(UInt64BasedEnum.One, UInt64BasedEnum.Parse("One", ignoreCase: false));
        Assert.Equal("4294967297", ((UInt64BasedEnum)4_294_967_297UL).GetName());
        Assert.False(UInt64BasedEnum.IsDefined((UInt64BasedEnum)4_294_967_297UL));
    }

    [Fact]
    public void AllUnderlyingTypes_AreSupported()
    {
        Assert.True(ByteBasedEnum.Two.HasFlag(ByteBasedEnum.Two));
        Assert.Equal(ByteBasedEnum.One, ByteBasedEnum.Parse("One", ignoreCase: false));

        Assert.True(SByteBasedEnum.Two.HasFlag(SByteBasedEnum.Two));
        Assert.Equal(SByteBasedEnum.One, SByteBasedEnum.Parse("One", ignoreCase: false));

        Assert.True(Int16BasedEnum.Two.HasFlag(Int16BasedEnum.Two));
        Assert.Equal(Int16BasedEnum.One, Int16BasedEnum.Parse("One", ignoreCase: false));

        Assert.True(UInt16BasedEnum.Two.HasFlag(UInt16BasedEnum.Two));
        Assert.Equal(UInt16BasedEnum.One, UInt16BasedEnum.Parse("One", ignoreCase: false));

        Assert.True(Int32BasedEnum.Two.HasFlag(Int32BasedEnum.Two));
        Assert.Equal(Int32BasedEnum.One, Int32BasedEnum.Parse("One", ignoreCase: false));

        Assert.True(UInt32BasedEnum.Two.HasFlag(UInt32BasedEnum.Two));
        Assert.Equal(UInt32BasedEnum.One, UInt32BasedEnum.Parse("One", ignoreCase: false));

        Assert.True(Int64BasedEnum.Two.HasFlag(Int64BasedEnum.Two));
        Assert.Equal(Int64BasedEnum.One, Int64BasedEnum.Parse("One", ignoreCase: false));

        Assert.True(UInt64BasedEnum.Two.HasFlag(UInt64BasedEnum.Two));
        Assert.Equal(UInt64BasedEnum.One, UInt64BasedEnum.Parse("One", ignoreCase: false));
    }
}
