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
    public void InsertGrowsTheBufferAndKeepsTheExistingContent()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 4);
        sb.Append("end");

        sb.Insert(0, new string('a', 100));
        sb.Insert(50, '-', 10);

        var expected = new string('a', 50) + new string('-', 10) + new string('a', 50) + "end";
        Assert.Equal(expected, sb.AsSpan().ToString());
    }

    [Fact]
    public void InsertAtTheEndAppends()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 8);
        sb.Append("ab");

        sb.Insert(2, "cd");
        sb.Insert(4, '!', 2);

        Assert.Equal("abcd!!", sb.AsSpan().ToString());
    }

    [Fact]
    public void InsertNullStringIsANoOp()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 8);
        sb.Append("ab");

        sb.Insert(1, null);

        Assert.Equal("ab", sb.AsSpan().ToString());
    }

    [Fact]
    public void AppendRepeatedCharGrowsTheBuffer()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 2);
        sb.Append('a', 0);
        sb.Append('b', 100);

        Assert.Equal(new string('b', 100), sb.AsSpan().ToString());
    }

    [Fact]
    public void AppendNullStringIsANoOp()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 8);
        sb.Append("ab");
        sb.Append((string?)null);

        Assert.Equal("ab", sb.AsSpan().ToString());
    }

    [Fact]
    public void EnsureCapacityGrowsWithoutLosingContent()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 4);
        sb.Append("abc");

        sb.EnsureCapacity(500);

        Assert.True(sb.Capacity >= 500);
        Assert.Equal(3, sb.Length);
        Assert.Equal("abc", sb.AsSpan().ToString());
    }

    [Fact]
    public void EnsureCapacityBelowTheCurrentCapacityIsANoOp()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 64);
        sb.Append("abc");
        var capacity = sb.Capacity;

        sb.EnsureCapacity(1);

        Assert.Equal(capacity, sb.Capacity);
        Assert.Equal("abc", sb.AsSpan().ToString());
    }

    [Fact]
    public void AppendSpanGrowsTheBufferAndKeepsTheExistingContent()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 4);
        sb.Append("ab");

        var span = sb.AppendSpan(100);
        span.Fill('c');

        Assert.Equal("ab" + new string('c', 100), sb.AsSpan().ToString());
    }

    [Fact]
    public void ClearResetsTheLengthButKeepsTheBuffer()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 64);
        sb.Append("hello");
        var capacity = sb.Capacity;

        sb.Clear();

        Assert.Equal(0, sb.Length);
        Assert.Equal(capacity, sb.Capacity);

        sb.Append("world");
        Assert.Equal("world", sb.AsSpan().ToString());
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        var sb = new ValueStringBuilder(initialCapacity: 64);
        sb.Append("hello");

        sb.Dispose();
        sb.Dispose();

        Assert.Equal(0, sb.Length);
        Assert.Equal(0, sb.Capacity);
    }

    [Fact]
    public void NullTerminateGrowsWhenTheBufferIsFull()
    {
        Span<char> initialBuffer = stackalloc char[2];
        using var sb = new ValueStringBuilder(initialBuffer);
        sb.Append("ab");

        sb.NullTerminate();

        Assert.Equal(2, sb.Length);
        Assert.Equal('\0', sb.RawChars[sb.Length]);
        Assert.Equal("ab", sb.AsSpan().ToString());
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Generating test inputs, and the fixed seed keeps the cases reproducible.")]
    public void BuilderMatchesStringBuilderForRandomOperationSequences()
    {
        var random = new Random(20260829);
        Span<char> runeChars = stackalloc char[2];

        for (var iteration = 0; iteration < 2000; iteration++)
        {
            var expected = new StringBuilder();
            var sb = iteration % 2 == 0
                ? new ValueStringBuilder(initialCapacity: random.Next(0, 8))
                : new ValueStringBuilder(new char[random.Next(0, 8)]);

            try
            {
                var operations = random.Next(1, 12);
                for (var i = 0; i < operations; i++)
                {
                    switch (random.Next(8))
                    {
                        case 0:
                            var c = (char)('a' + random.Next(26));
                            sb.Append(c);
                            expected.Append(c);
                            break;
                        case 1:
                            var s = new string((char)('a' + random.Next(26)), random.Next(0, 30));
                            sb.Append(s);
                            expected.Append(s);
                            break;
                        case 2:
                            var repeated = (char)('a' + random.Next(26));
                            var count = random.Next(0, 30);
                            sb.Append(repeated, count);
                            expected.Append(repeated, count);
                            break;
                        case 3:
                            var span = new string((char)('a' + random.Next(26)), random.Next(0, 30));
                            sb.Append(span.AsSpan());
                            expected.Append(span);
                            break;
                        case 4:
                            var charIndex = random.Next(0, expected.Length + 1);
                            var inserted = (char)('a' + random.Next(26));
                            var insertedCount = random.Next(0, 10);
                            sb.Insert(charIndex, inserted, insertedCount);
                            expected.Insert(charIndex, new string(inserted, insertedCount));
                            break;
                        case 5:
                            var stringIndex = random.Next(0, expected.Length + 1);
                            var insertedString = new string((char)('a' + random.Next(26)), random.Next(0, 20));
                            sb.Insert(stringIndex, insertedString);
                            expected.Insert(stringIndex, insertedString);
                            break;
                        case 6:
                            var length = random.Next(0, 20);
                            sb.AppendSpan(length).Fill('#');
                            expected.Append('#', length);
                            break;
                        default:
                            var rune = new Rune(random.Next(2) == 0 ? random.Next(0x20, 0xD7FF) : random.Next(0x10000, 0x10FFFF));
                            sb.Append(rune);
                            expected.Append(runeChars[..rune.EncodeToUtf16(runeChars)]);
                            break;
                    }
                }

                Assert.Equal(expected.ToString(), sb.AsSpan().ToString());
            }
            finally
            {
                sb.Dispose();
            }
        }
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
    public void ToStringDisposesTheBuilder()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 16);
        sb.Append("hello");

        Assert.Equal("hello", sb.ToString());
        Assert.Equal("", sb.ToString());
        Assert.Equal(0, sb.Length);
        Assert.Equal(0, sb.Capacity);
    }

    [Fact]
    public void AsSpanDoesNotDisposeTheBuilder()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 16);
        sb.Append("hello");

        Assert.Equal("hello", sb.AsSpan().ToString());

        sb.Append(" world");
        Assert.Equal("hello world", sb.AsSpan().ToString());
    }

    [Fact]
    public void DisposeIsIdempotentAfterToString()
    {
        var sb = new ValueStringBuilder(initialCapacity: 16);
        sb.Append("hello");

        Assert.Equal("hello", sb.ToString());
        sb.Dispose();
        sb.Dispose();
    }

    [Fact]
    public void AsSpanWithLengthReturnsASliceOfTheContent()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 16);
        sb.Append("hello");

        Assert.Equal("ell", sb.AsSpan(1, 3).ToString());
        Assert.Equal("hello", sb.AsSpan(0, 5).ToString());
        Assert.Equal("", sb.AsSpan(5, 0).ToString());
    }

    [Fact]
    public void AsSpanWithLengthCannotReadPastTheContent()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 64);
        sb.Append("hi");

        // Reading 10 characters would reach into the rented buffer, past the two that were written.
        Assert.True(sb.Capacity > 10);

        var threw = false;
        try
        {
            _ = sb.AsSpan(0, 10);
        }
        catch (ArgumentOutOfRangeException)
        {
            threw = true;
        }

        Assert.True(threw);
    }

    [Fact]
    public void AsSpanWithStartCannotReadPastTheContent()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 64);
        sb.Append("hi");

        var threw = false;
        try
        {
            _ = sb.AsSpan(10);
        }
        catch (ArgumentOutOfRangeException)
        {
            threw = true;
        }

        Assert.True(threw);
    }

    [Fact]
    public void LengthRejectsValuesOutsideTheBuffer()
    {
        Assert.True(ThrowsArgumentOutOfRange(() =>
        {
            var b = new ValueStringBuilder(initialCapacity: 16);
            try
            {
                b.Length = -1;
            }
            finally
            {
                b.Dispose();
            }
        }));

        Assert.True(ThrowsArgumentOutOfRange(() =>
        {
            var b = new ValueStringBuilder(initialCapacity: 16);
            try
            {
                b.Length = b.Capacity + 1;
            }
            finally
            {
                b.Dispose();
            }
        }));

        var sb = new ValueStringBuilder(initialCapacity: 16);
        try
        {
            sb.Append("abc");
            sb.Length = sb.Capacity;
            sb.Length = 0;
            Assert.Equal(0, sb.Length);
        }
        finally
        {
            sb.Dispose();
        }
    }

    [Fact]
    public void EnsureCapacityRejectsNegativeValues()
    {
        Assert.True(ThrowsArgumentOutOfRange(() => { using var b = new ValueStringBuilder(initialCapacity: 16); b.EnsureCapacity(-1); }));
    }

    [Fact]
    public void IndexerRejectsPositionsOutsideTheContent()
    {
        Assert.True(ThrowsArgumentOutOfRange(() => { using var b = new ValueStringBuilder(initialCapacity: 16); b.Append("abc"); _ = b[-1]; }));
        Assert.True(ThrowsArgumentOutOfRange(() => { using var b = new ValueStringBuilder(initialCapacity: 16); b.Append("abc"); _ = b[3]; }));

        using var sb = new ValueStringBuilder(initialCapacity: 16);
        sb.Append("abc");
        Assert.Equal('c', sb[2]);
    }

    [Fact]
    public void InsertRejectsPositionsOutsideTheContent()
    {
        Assert.True(ThrowsArgumentOutOfRange(() => { using var b = new ValueStringBuilder(initialCapacity: 16); b.Append("ab"); b.Insert(3, "x"); }));
        Assert.True(ThrowsArgumentOutOfRange(() => { using var b = new ValueStringBuilder(initialCapacity: 16); b.Append("ab"); b.Insert(-1, "x"); }));
        Assert.True(ThrowsArgumentOutOfRange(() => { using var b = new ValueStringBuilder(initialCapacity: 16); b.Append("ab"); b.Insert(3, 'x', 1); }));
        Assert.True(ThrowsArgumentOutOfRange(() => { using var b = new ValueStringBuilder(initialCapacity: 16); b.Append("ab"); b.Insert(0, 'x', -1); }));

        using var sb = new ValueStringBuilder(initialCapacity: 16);
        sb.Append("ab");
        sb.Insert(2, "c");
        Assert.Equal("abc", sb.AsSpan().ToString());
    }

    [Fact]
    public void AppendAndAppendSpanRejectNegativeCounts()
    {
        Assert.True(ThrowsArgumentOutOfRange(() => { using var b = new ValueStringBuilder(initialCapacity: 16); b.Append('x', -1); }));
        Assert.True(ThrowsArgumentOutOfRange(() => { using var b = new ValueStringBuilder(initialCapacity: 16); _ = b.AppendSpan(-1); }));
    }

    private static bool ThrowsArgumentOutOfRange(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return true;
        }
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
    public void AppendRuneSupportsBasicMultilingualPlaneRunes()
    {
        using var sb = new ValueStringBuilder(initialCapacity: 4);
        sb.Append(new Rune('a'));
        sb.Append(new Rune(0x00E9));
        sb.Append(new Rune(0xFFFD));

        Assert.Equal("a\u00E9\uFFFD", sb.AsSpan().ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AppendRuneGrowsAtBufferBoundaries(int fillCount)
    {
        Span<char> initialBuffer = stackalloc char[4];
        using var sb = new ValueStringBuilder(initialBuffer);
        sb.Append('x', fillCount);

        sb.Append(new Rune('a'));
        sb.Append(new Rune(0x1F600));

        Assert.Equal(new string('x', fillCount) + "a\U0001F600", sb.AsSpan().ToString());
    }

    [Fact]
    public void AppendRuneFillsTheBufferExactly()
    {
        Span<char> initialBuffer = stackalloc char[2];
        using var sb = new ValueStringBuilder(initialBuffer);

        sb.Append(new Rune(0x1F600));

        Assert.Equal("\U0001F600", sb.AsSpan().ToString());
        Assert.Equal(2, sb.Length);
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
