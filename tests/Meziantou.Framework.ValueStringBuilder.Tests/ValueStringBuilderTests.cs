#nullable enable

using System.Text;
using Xunit;

namespace Meziantou.Framework.Tests;

public class ValueStringBuilderTests
{
    [Fact]
    public void AppendWithStackBuffer()
    {
        Span<char> initialBuffer = stackalloc char[8];
        using var sb = new ValueStringBuilder(initialBuffer);

        sb.Append("hello");
        sb.Append(' ');
        sb.Append("world");

        Assert.Equal("hello world", sb.ToString());
    }

    [Fact]
    public void AppendGrowsBuffer()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 2);
        sb.Append("0123456789");

        Assert.Equal("0123456789", sb.ToString());
    }

    [Fact]
    public void InsertSupportsCharAndString()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 8);
        sb.Append("hello");
        sb.Insert(5, '!', 1);
        sb.Insert(0, "say ");

        Assert.Equal("say hello!", sb.ToString());
    }

    [Fact]
    public void AppendSpanReturnsWritableSlice()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 8);
        var span = sb.AppendSpan(5);
        "hello".AsSpan().CopyTo(span);

        Assert.Equal("hello", sb.ToString());
    }

    [Fact]
    public void NullTerminateWritesTerminator()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 2);
        sb.Append("ab");
        sb.NullTerminate();

        Assert.Equal('\0', sb.RawChars[sb.Length]);
        Assert.Equal("ab", sb.ToString());
    }

#if NET6_0_OR_GREATER
    [Fact]
    public void AppendRuneSupportsSurrogatePairs()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 4);
        sb.Append(new Rune(0x1F600));

        Assert.Equal("\U0001F600", sb.ToString());
    }

    [Fact]
    public void AppendSpanFormattableUsesTryFormat()
    {
        Span<char> initialBuffer = stackalloc char[16];
        using var sb = new ValueStringBuilder(initialBuffer);
        var value = new TestSpanFormattable("abc", returnFromTryFormat: true);

        sb.AppendSpanFormattable(value);

        Assert.Equal(1, value.TryFormatCount);
        Assert.Equal(0, value.ToStringCount);
        Assert.Equal("abc", sb.ToString());
    }

    [Fact]
    public void AppendSpanFormattableFallsBackToToString()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 1);
        var value = new TestSpanFormattable("fallback", returnFromTryFormat: false);

        sb.AppendSpanFormattable(value);

        Assert.Equal(1, value.TryFormatCount);
        Assert.Equal(1, value.ToStringCount);
        Assert.Equal("fallback", sb.ToString());
    }

    private sealed class TestSpanFormattable : ISpanFormattable
    {
        private readonly bool _returnFromTryFormat;
        private readonly string _value;

        public TestSpanFormattable(string value, bool returnFromTryFormat)
        {
            _value = value;
            _returnFromTryFormat = returnFromTryFormat;
        }

        public int TryFormatCount { get; private set; }
        public int ToStringCount { get; private set; }

        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            ToStringCount++;
            return _value;
        }

        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        {
            TryFormatCount++;
            if (!_returnFromTryFormat || destination.Length < _value.Length)
            {
                charsWritten = 0;
                return false;
            }

            _value.AsSpan().CopyTo(destination);
            charsWritten = _value.Length;
            return true;
        }
    }
#endif
}
