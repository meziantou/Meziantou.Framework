using System.ComponentModel.DataAnnotations;

namespace Meziantou.Framework.WPF.Tests;

public sealed class EnumMarkupExtensionsTests
{
    private enum SampleEnum
    {
        [Display(Name = "First value")]
        First,

        Second,

        [Display(Description = "No name set")]
        Third,
    }

    [Fact]
    public void EnumValues_ReturnsEveryValue()
    {
        var extension = new EnumValuesExtension(typeof(SampleEnum));

        var values = Assert.IsType<SampleEnum[]>(extension.ProvideValue(serviceProvider: null!));

        Assert.Equal([SampleEnum.First, SampleEnum.Second, SampleEnum.Third], values);
    }

    [Fact]
    public void EnumValues_WithoutType_Throws()
    {
        var extension = new EnumValuesExtension();

        Assert.Throws<InvalidOperationException>(() => extension.ProvideValue(serviceProvider: null!));
    }

    [Fact]
    public void EnumValues_WithNonEnumType_Throws()
    {
        var extension = new EnumValuesExtension(typeof(string));

        var exception = Assert.Throws<InvalidOperationException>(() => extension.ProvideValue(serviceProvider: null!));
        Assert.Contains(nameof(String), exception.Message);
    }

    [Fact]
    public void LocalizedEnumValues_UsesDisplayNameThenMemberName()
    {
        var extension = new LocalizedEnumValuesExtension(typeof(SampleEnum));

        var values = Assert.IsAssignableTo<IReadOnlyList<LocalizedEnumValue>>(extension.ProvideValue(serviceProvider: null!));

        Assert.HasCount(3, values);
        Assert.Equal("First value", values[0].Name);
        Assert.Equal(SampleEnum.First, values[0].Value);

        Assert.Equal(nameof(SampleEnum.Second), values[1].Name);

        // The DisplayAttribute has no Name, so the member name is used
        Assert.Equal(nameof(SampleEnum.Third), values[2].Name);
    }

    [Fact]
    public void LocalizedEnumValues_IsCached()
    {
        var first = new LocalizedEnumValuesExtension(typeof(SampleEnum)).ProvideValue(serviceProvider: null!);
        var second = new LocalizedEnumValuesExtension(typeof(SampleEnum)).ProvideValue(serviceProvider: null!);

        Assert.Same(first, second);
    }

    [Fact]
    public void LocalizedEnumValues_WithoutType_Throws()
    {
        var extension = new LocalizedEnumValuesExtension();

        Assert.Throws<InvalidOperationException>(() => extension.ProvideValue(serviceProvider: null!));
    }

    [Fact]
    public void LocalizedEnumValues_WithNonEnumType_Throws()
    {
        var extension = new LocalizedEnumValuesExtension(typeof(string));

        Assert.Throws<InvalidOperationException>(() => extension.ProvideValue(serviceProvider: null!));
    }

    [Fact]
    public void LocalizedEnumValue_ToStringReturnsName()
    {
        var value = new LocalizedEnumValue(SampleEnum.Second);

        Assert.Equal(nameof(SampleEnum.Second), value.ToString());
        Assert.Equal(nameof(SampleEnum.Second), value.Name);
    }
}
