using System.Buffers;
using System.Runtime.CompilerServices;

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
        var provider = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        provider.NumberDecimalSeparator = ",";

        sb.Append(provider, $"{12.5m:0.0}");

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

    [Fact]
    public void AppendInterpolatedStringDoesNotShareItsBufferWithTheBuilderWhenAHoleThrows()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 32);
        try
        {
            // The literal forces the handler to grow before the throwing hole is evaluated.
            sb.Append($"{new string('x', 200)}{ThrowingHole()}");
        }
        catch (InvalidOperationException)
        {
        }

        var rented = ArrayPool<char>.Shared.Rent(32);
        try
        {
            Assert.False(Unsafe.AreSame(ref sb.GetPinnableReference(), ref rented[0]));
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }

        sb.Append("still usable");
        Assert.Equal("still usable", sb.ToString());
    }

    [Fact]
    public void AppendInterpolatedStringReturnsTheBufferOnlyOnceWhenAHoleThrows()
    {
        var sb = new ValueStringBuilder(initialCapacity: 32);
        try
        {
            sb.Append($"{new string('x', 200)}{ThrowingHole()}");
        }
        catch (InvalidOperationException)
        {
        }

        sb.Dispose();

        var first = ArrayPool<char>.Shared.Rent(32);
        var second = ArrayPool<char>.Shared.Rent(32);
        try
        {
            Assert.NotSame(first, second);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(first);
            ArrayPool<char>.Shared.Return(second);
        }
    }

    [Fact]
    public void AppendInterpolatedStringLeavesTheBuilderUntouchedWhenAHoleThrows()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 32);
        sb.Append("before");

        try
        {
            sb.Append($"{new string('x', 200)}{ThrowingHole()}");
        }
        catch (InvalidOperationException)
        {
        }

        Assert.Equal("before", sb.AsSpan().ToString());
    }

    [Fact]
    public void AppendInterpolatedStringGrowsTheBuilderWhenTheResultIsLarger()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 2);
        var value = new string('a', 500);

        sb.Append(CultureInfo.InvariantCulture, $"{value}|{value}");

        Assert.Equal(value + "|" + value, sb.AsSpan().ToString());
    }

    [Fact]
    public void AppendInterpolatedStringWorksWithAStackAllocatedBuffer()
    {
        Span<char> initialBuffer = stackalloc char[4];
        using var sb = new ValueStringBuilder(initialBuffer);

        sb.Append(CultureInfo.InvariantCulture, $"{new string('z', 50)}");

        Assert.Equal(new string('z', 50), sb.AsSpan().ToString());
    }

    private static string ThrowingHole() => throw new InvalidOperationException("boom");

    [Fact]
    public void AppendInterpolatedStringUsesTryFormatInsteadOfToString()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 64);
        var value = new TestSpanFormattable("abc", returnFromTryFormat: true);

        sb.Append(CultureInfo.InvariantCulture, $"{value}");

        Assert.Equal(1, value.TryFormatCount);
        Assert.Equal(0, value.ToStringCount);
        Assert.Equal("abc", sb.AsSpan().ToString());
    }

    [Fact]
    public void AppendInterpolatedStringGrowsAndRetriesWhenTryFormatDoesNotFit()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 1);
        var value = new TestSpanFormattable(new string('a', 400), returnFromTryFormat: true);

        sb.Append(CultureInfo.InvariantCulture, $"{value}");

        Assert.True(value.TryFormatCount > 1);
        Assert.Equal(0, value.ToStringCount);
        Assert.Equal(new string('a', 400), sb.AsSpan().ToString());
    }

    [Fact]
    public void AppendInterpolatedStringFallsBackToToStringForNonSpanFormattableValues()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 64);
        var value = new FormattableOnly("xyz");

        sb.Append(CultureInfo.InvariantCulture, $"{value}");

        Assert.Equal(1, value.ToStringCount);
        Assert.Equal("xyz", sb.AsSpan().ToString());
    }

    [Fact]
    public void AppendInterpolatedStringFormatsSpanFormattableValuesInPlace()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 2);

        sb.Append(CultureInfo.InvariantCulture, $"{42}|{2.5}|{DateTime.UnixEpoch:yyyy-MM-dd}|{Guid.Empty}");

        Assert.Equal("42|2.5|1970-01-01|00000000-0000-0000-0000-000000000000", sb.AsSpan().ToString());
    }

    [Fact]
    public void AppendInterpolatedStringPadsSpanFormattableValuesWithoutAllocating()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 64);

        sb.Append(CultureInfo.InvariantCulture, $"|{12,6}|{34,-6}|{5,2}|{123456,3}|");

        Assert.Equal("|    12|34    | 5|123456|", sb.AsSpan().ToString());
    }

    [Fact]
    public void AppendInterpolatedStringSupportsNonSpanFormattableValues()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 8);

        sb.Append(CultureInfo.InvariantCulture, $"{new NotSpanFormattable(),8}|{new NotSpanFormattable(),-8}|");

        Assert.Equal("    text|text    |", sb.AsSpan().ToString());
    }

    private sealed class NotSpanFormattable
    {
        public override string ToString() => "text";
    }

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

    private sealed class FormattableOnly : IFormattable
    {
        private readonly string _value;

        public FormattableOnly(string value) => _value = value;

        public int ToStringCount { get; private set; }

        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            ToStringCount++;
            return _value;
        }
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
