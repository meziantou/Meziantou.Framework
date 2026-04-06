using System;
using Meziantou.Framework.FixedStringBuilder;
using Xunit;

namespace Meziantou.Framework.Tests;

public sealed class FixedStringBuilderTests
{
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

        Assert.Equal(FixedStringBuilder8.MaxLength, span.Length);
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
    public void EqualsSupportsStringComparison()
    {
        FixedStringBuilder8 a = "AbC";
        FixedStringBuilder8 b = "aBc";

        Assert.False(a.Equals(b, StringComparison.Ordinal));
        Assert.True(a.Equals(b, StringComparison.OrdinalIgnoreCase));
    }
}
