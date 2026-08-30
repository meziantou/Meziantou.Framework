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
