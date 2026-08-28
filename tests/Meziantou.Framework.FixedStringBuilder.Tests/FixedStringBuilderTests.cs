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
    public void ClearResetsTheLengthWithoutErasingTheBuffer()
    {
        FixedStringBuilder8 value = "abcdefgh";
        value.Clear();

        Assert.Equal(0, value.Length);
        Assert.Equal("", value.ToString(null, null));

        // Documented behavior: Clear only resets the length, the characters stay in the underlying buffer.
        var fullSpan = ((IFixedString)value).GetUnsafeFullSpan();
        Assert.Equal("abcdefgh", fullSpan.ToString());
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
