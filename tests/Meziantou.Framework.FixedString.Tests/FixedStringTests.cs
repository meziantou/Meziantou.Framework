using System;
using Meziantou.Framework.FixedString;
using Xunit;

namespace Meziantou.Framework.Tests;

public sealed class FixedStringTests
{
    [Fact]
    public void MaxLengthValues()
    {
        Assert.Equal(8, FixedString8.MaxLength);
        Assert.Equal(16, FixedString16.MaxLength);
        Assert.Equal(32, FixedString32.MaxLength);
        Assert.Equal(64, FixedString64.MaxLength);
    }

    [Fact]
    public void StringCtorThrowsWhenValueIsTooLong()
    {
        Assert.Throws<ArgumentException>(() => new FixedString8("123456789"));
    }

    [Fact]
    public void InterpolatedStringBuildsExpectedText()
    {
        FixedString16 value = $"Hello {"World"}";

        Assert.Equal("Hello World", value.ToString());
    }

    [Fact]
    public void InterpolatedAlignmentPadsLeft()
    {
        FixedString8 value = $"{1,4}";

        Assert.Equal("   1", value.ToString());
    }

    [Fact]
    public void TryFormatWritesCharacters()
    {
        FixedString16 value = "abc";
        Span<char> buffer = stackalloc char[16];

        Assert.True(value.TryFormat(buffer, out var charsWritten, default, null));
        Assert.Equal(3, charsWritten);
        Assert.Equal("abc", buffer[..charsWritten].ToString());
    }

    [Fact]
    public void GetUnsafeFullSpanReturnsFixedCapacity()
    {
        FixedString8 value = "abc";
        var fixedString = (IFixedString)value;
        var span = fixedString.GetUnsafeFullSpan();

        Assert.Equal(FixedString8.MaxLength, span.Length);
        Assert.Equal('a', span[0]);
        Assert.Equal('b', span[1]);
        Assert.Equal('c', span[2]);
    }

    [Fact]
    public void StringCtorStoresValueWhenLengthIsExact()
    {
        var value = new FixedString8("12345678");

        Assert.Equal(FixedString8.MaxLength, value.Length);
        Assert.Equal("12345678", value.ToString());
    }

    [Fact]
    public void AppendLiteralThrowsWhenValueIsTooLong()
    {
        var value = new FixedString8("12345678");

        Assert.Throws<ArgumentException>(() => value.AppendLiteral("9"));
    }

    [Fact]
    public void AppendFormattedThrowsWhenValueIsTooLong()
    {
        var value = new FixedString8(0, 1);

        Assert.Throws<ArgumentException>(() => value.AppendFormatted(123456789));
    }

    [Fact]
    public void InterpolatedAlignmentThrowsWhenValueIsTooLong()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            FixedString8 _ = $"{1,9}";
        });
    }

    [Fact]
    public void InterpolatedStringThrowsWhenValueIsTooLong()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            FixedString8 _ = $"123456789";
        });
    }
}
