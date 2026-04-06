#nullable enable

using System.Globalization;
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

    [Fact]
    public void AppendInterpolatedStringAppendsContent()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 2);
        var count = 42;
        string? text = null;

        sb.Append(CultureInfo.InvariantCulture, $"count={count},text={text}");

        Assert.Equal("count=42,text=", sb.ToString());
    }

    [Fact]
    public void AppendInterpolatedStringSupportsAlignment()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 2);

        sb.Append(CultureInfo.InvariantCulture, $"|{12,4}|{34,-4}|");

        Assert.Equal("|  12|34  |", sb.ToString());
    }

    [Fact]
    public void AppendInterpolatedStringSupportsProvider()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 2);

        sb.Append(CultureInfo.GetCultureInfo("fr-FR"), $"{12.5m:0.0}");

        Assert.Equal("12,5", sb.ToString());
    }

    [Fact]
    public void AppendInterpolatedStringSupportsCustomFormatter()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 2);
        var provider = new TestCustomFormatterProvider();

        sb.Append(provider, $"A={12}, B={34,4:000}");

        Assert.Equal("A=<12>, B=<34>", sb.ToString());
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

    private sealed class TestCustomFormatterProvider : IFormatProvider, ICustomFormatter
    {
        public object? GetFormat(Type? formatType)
        {
            return formatType == typeof(ICustomFormatter) ? this : null;
        }

        public string Format(string? format, object? arg, IFormatProvider? formatProvider)
        {
            return arg is null ? "<null>" : $"<{arg}>";
        }
    }
}
