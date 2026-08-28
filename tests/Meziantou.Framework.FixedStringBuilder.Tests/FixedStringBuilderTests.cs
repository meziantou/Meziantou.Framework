using Meziantou.Framework.FixedStringBuilder;

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
    public void EqualsSupportsStringComparison()
    {
        FixedStringBuilder8 a = "AbC";
        FixedStringBuilder8 b = "aBc";

        Assert.False(a.Equals(b, StringComparison.Ordinal));
        Assert.True(a.Equals(b, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ToStringReturnsTheContent()
    {
        FixedStringBuilder16 value = "abc";

        Assert.Equal("abc", value.ToString(null, null));
        Assert.Equal("", default(FixedStringBuilder16).ToString(null, null));
    }

    [Fact]
    public void AsSpanReturnsTheWrittenCharacters()
    {
        FixedStringBuilder16 value = "abc";

        Assert.HasCount(3, value.AsSpan());
        Assert.Equal("abc", value.AsSpan().ToString());
        Assert.Empty(default(FixedStringBuilder16).AsSpan());
    }

    [Fact]
    public void EqualityOperatorsCompareTheContent()
    {
        FixedStringBuilder8 a = "abc";
        FixedStringBuilder8 b = "abc";
        FixedStringBuilder8 c = "abd";

        Assert.True(a == b);
        Assert.False(a != b);
        Assert.False(a == c);
        Assert.True(a != c);
    }

    [Fact]
    public void EqualsObjectComparesOnlyTheSameType()
    {
        FixedStringBuilder8 a = "abc";
        FixedStringBuilder8 b = "abc";

        Assert.True(a.Equals((object)b));
        Assert.False(a.Equals((object)"abc"));
        Assert.False(a.Equals((object?)null));
    }

    [Fact]
    public void GetHashCodeIsEqualForEqualValues()
    {
        FixedStringBuilder8 a = "abc";
        FixedStringBuilder8 b = "abc";

        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Equal(default(FixedStringBuilder8).GetHashCode(), default(FixedStringBuilder8).GetHashCode());
    }

    [Fact]
    public void InterpolatedAlignmentPadsRightWhenNegative()
    {
        FixedStringBuilder8 value = $"{1,-4}";

        Assert.Equal("1   ", value.ToString(null, null));
    }

    [Fact]
    public void InterpolatedAlignmentIsIgnoredWhenTheValueIsWider()
    {
        FixedStringBuilder8 value = $"{1234,2}";

        Assert.Equal("1234", value.ToString(null, null));
    }

    [Fact]
    public void InterpolatedHoleSupportsAFormatString()
    {
        FixedStringBuilder8 value = $"{255:X2}";

        Assert.Equal("FF", value.ToString(null, null));
    }

    [Fact]
    public void InterpolatedHoleSupportsAFormatStringAndAlignment()
    {
        FixedStringBuilder8 value = $"{255,4:X2}";

        Assert.Equal("  FF", value.ToString(null, null));
    }
}
