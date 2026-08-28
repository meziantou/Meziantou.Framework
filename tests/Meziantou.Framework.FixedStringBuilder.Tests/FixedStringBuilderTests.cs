using System.Reflection;
using Meziantou.Framework.FixedStringBuilder;

namespace Meziantou.Framework.Tests;

public sealed class FixedStringBuilderTests
{
    [Theory]
    [InlineData("get_Length")]
    [InlineData("Equals")]
    [InlineData("GetHashCode")]
    [InlineData("ToString")]
    [InlineData("TryFormat")]
    [InlineData("AsSpan")]
    public void NonMutatingMembersAreReadOnly(string methodName)
    {
        // A non-readonly member forces a defensive copy of the whole struct when it is called on a readonly
        // field or through an "in" parameter.
        var methods = typeof(FixedStringBuilder64)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == methodName)
            .ToArray();

        Assert.NotEmpty(methods);
        Assert.All(methods, static method => Assert.Contains(
            method.GetCustomAttributesData(),
            static attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute"));
    }

    [Fact]
    public void MutatingMembersAreNotReadOnly()
    {
        var methods = typeof(FixedStringBuilder64)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(static method => method.Name is "Clear" or "AppendLiteral")
            .ToArray();

        Assert.NotEmpty(methods);
        Assert.All(methods, static method => Assert.DoesNotContain(
            method.GetCustomAttributesData(),
            static attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute"));
    }

    [Fact]
    public void MaxLengthValues()
    {
        Assert.Equal(8, FixedStringBuilder8.MaxLength);
        Assert.Equal(16, FixedStringBuilder16.MaxLength);
        Assert.Equal(32, FixedStringBuilder32.MaxLength);
        Assert.Equal(64, FixedStringBuilder64.MaxLength);
    }

    [Fact]
    public void StringCtorThrowsWhenValueIsTooLong()
    {
        Assert.Throws<ArgumentException>(() => new FixedStringBuilder8("123456789"));
    }

    [Fact]
    public void InterpolatedStringBuildsExpectedText()
    {
        FixedStringBuilder16 value = $"Hello {"World"}";

        Assert.Equal("Hello World", value.ToString(null, null));
    }

    [Fact]
    public void InterpolatedAlignmentPadsLeft()
    {
        FixedStringBuilder8 value = $"{1,4}";

        Assert.Equal("   1", value.ToString(null, null));
    }

    [Fact]
    public void TryFormatWritesCharacters()
    {
        FixedStringBuilder16 value = "abc";
        Span<char> buffer = stackalloc char[16];

        Assert.True(value.TryFormat(buffer, out var charsWritten, default, null));
        Assert.Equal(3, charsWritten);
        Assert.Equal("abc", buffer[..charsWritten].ToString());
    }

    [Fact]
    public void GetUnsafeFullSpanReturnsFixedCapacity()
    {
        FixedStringBuilder8 value = "abc";
        var fixedString = (IFixedString)value;
        var span = fixedString.GetUnsafeFullSpan();

        Assert.HasCount(FixedStringBuilder8.MaxLength, span);
        Assert.Equal('a', span[0]);
        Assert.Equal('b', span[1]);
        Assert.Equal('c', span[2]);
    }

    [Fact]
    public void StringCtorStoresValueWhenLengthIsExact()
    {
        var value = new FixedStringBuilder8("12345678");

        Assert.Equal(FixedStringBuilder8.MaxLength, value.Length);
        Assert.Equal("12345678", value.ToString(null, null));
    }

    [Fact]
    public void AppendLiteralThrowsWhenValueIsTooLong()
    {
        var value = new FixedStringBuilder8("12345678");

        Assert.Throws<ArgumentException>(() => value.AppendLiteral("9"));
    }

    [Fact]
    public void AppendFormattedThrowsWhenValueIsTooLong()
    {
        var value = new FixedStringBuilder8(0, 1);

        Assert.Throws<ArgumentException>(() => value.AppendFormatted(123456789));
    }

    [Fact]
    public void InterpolatedAlignmentThrowsWhenValueIsTooLong()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            FixedStringBuilder8 _ = $"{1,9}";
        });
    }

    [Fact]
    public void InterpolatedStringThrowsWhenValueIsTooLong()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            FixedStringBuilder8 _ = $"123456789";
        });
    }

    [Fact]
    public void InterpolatedHoleSupportsASpan()
    {
        ReadOnlySpan<char> span = "World".AsSpan();
        FixedStringBuilder16 value = $"Hi {span}";

        Assert.Equal("Hi World", value.ToString(null, null));
    }

    [Fact]
    public void InterpolatedHoleSupportsASpanWithAlignment()
    {
        ReadOnlySpan<char> span = "ab".AsSpan();
        FixedStringBuilder16 value = $"{span,4}";

        Assert.Equal("  ab", value.ToString(null, null));
    }

    [Fact]
    public void InterpolatedHoleSupportsAValueThatIsNotSpanFormattable()
    {
        FixedStringBuilder16 value = $"{new NotSpanFormattable()}";

        Assert.Equal("custom", value.ToString(null, null));
    }

    [Fact]
    public void InterpolatedHoleSupportsAnEnum()
    {
        FixedStringBuilder16 value = $"{DayOfWeek.Monday}";

        Assert.Equal("Monday", value.ToString(null, null));
    }

    [Fact]
    public void InterpolatedHoleSupportsAFormattableThatIsNotSpanFormattable()
    {
        FixedStringBuilder16 value = $"{new OnlyFormattable():X}";

        Assert.Equal("fmt:X", value.ToString(null, null));
    }

    [Fact]
    public void InterpolatedHoleSupportsAStringWithAlignmentAndFormat()
    {
        FixedStringBuilder16 value = $"{"ab",4:X}";

        Assert.Equal("  ab", value.ToString(null, null));
    }

    [Fact]
    public void InterpolatedHoleSupportsANullReference()
    {
        object? nullValue = null;
        FixedStringBuilder16 value = $"[{nullValue}]";

        Assert.Equal("[]", value.ToString(null, null));
    }

    private sealed class NotSpanFormattable
    {
        public override string ToString() => "custom";
    }

    private sealed class OnlyFormattable : IFormattable
    {
        public string ToString(string? format, IFormatProvider? formatProvider) => "fmt:" + format;

        public override string ToString() => "fmt:";
    }

    [Fact]
    public void EqualsSupportsStringComparison()
    {
        FixedStringBuilder8 a = "AbC";
        FixedStringBuilder8 b = "aBc";

        Assert.False(a.Equals(b, StringComparison.Ordinal));
        Assert.True(a.Equals(b, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("AbC", "aBc", StringComparison.Ordinal)]
    [InlineData("AbC", "aBc", StringComparison.OrdinalIgnoreCase)]
    [InlineData("AbC", "aBc", StringComparison.CurrentCulture)]
    [InlineData("AbC", "aBc", StringComparison.CurrentCultureIgnoreCase)]
    [InlineData("AbC", "AbC", StringComparison.Ordinal)]
    [InlineData("", "", StringComparison.Ordinal)]
    [InlineData("a", "", StringComparison.Ordinal)]
    [InlineData("a", "", StringComparison.CurrentCulture)]
    public void EqualsWithComparisonMatchesString(string left, string right, StringComparison comparison)
    {
        FixedStringBuilder8 a = left;
        FixedStringBuilder8 b = right;

        Assert.Equal(left.Equals(right, comparison), a.Equals(b, comparison));
    }
}
