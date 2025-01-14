using System.Numerics;
using FluentAssertions;
using Meziantou.Framework.SimpleQueryLanguage.Ranges;
using Xunit;

namespace Meziantou.Framework.SimpleQueryLanguage.Tests;

// Prevent parallelization because of RangeSyntax.UtcNow
[Collection("QueryBuilderTests")]
public sealed class QueryBuilderTests
{
    [Fact]
    public void FieldEquals_Byte()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<byte>("id", (obj, value) => obj.ByteValue == value);
        var query = queryBuilder.Build("id:10");
        Assert.True(query.Evaluate(new() { ByteValue = 10 }));
        Assert.False(query.Evaluate(new()));
    }

    [Fact]
    public void FieldEquals_SByte()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<sbyte>("field", (obj, value) => obj.SByteValue == value);
        queryBuilder.SetTextFilterHandler((obj, value) => throw new Exception($"Unexpected text query '{value}'"));
        var query = queryBuilder.Build("field:\"-10\"");
        Assert.True(query.Evaluate(new() { SByteValue = -10 }));
        Assert.False(query.Evaluate(new()));
    }

    [Fact]
    public void FieldEquals_Int16()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<short>("id", (obj, value) => obj.Int16Value == value);
        var query = queryBuilder.Build("id:10");
        Assert.True(query.Evaluate(new() { Int16Value = 10 }));
        Assert.False(query.Evaluate(new()));
    }

    [Fact]
    public void FieldEquals_UInt16()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<ushort>("id", (obj, value) => obj.UInt16Value == value);
        var query = queryBuilder.Build("id:10");
        Assert.True(query.Evaluate(new() { UInt16Value = 10 }));
        Assert.False(query.Evaluate(new()));
    }

    [Fact]
    public void FieldEquals_Int32()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<int>("id", (obj, value) => obj.Int32Value == value);
        var query = queryBuilder.Build("id:10");
        Assert.True(query.Evaluate(new() { Int32Value = 10 }));
        Assert.False(query.Evaluate(new()));
    }

    [Fact]
    public void FieldEquals_UInt32()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<uint>("id", (obj, value) => obj.UInt32Value == value);
        var query = queryBuilder.Build("id:10");
        Assert.True(query.Evaluate(new() { UInt32Value = 10 }));
        Assert.False(query.Evaluate(new()));
    }

    [Fact]
    public void FieldEquals_Int64()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<long>("id", (obj, value) => obj.Int64Value == value);
        var query = queryBuilder.Build("id:10");
        Assert.True(query.Evaluate(new() { Int64Value = 10 }));
        Assert.False(query.Evaluate(new()));
    }

    [Fact]
    public void FieldEquals_UInt64()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<ulong>("id", (obj, value) => obj.UInt64Value == value);
        var query = queryBuilder.Build("id:10");
        Assert.True(query.Evaluate(new() { UInt64Value = 10 }));
        Assert.False(query.Evaluate(new()));
    }

#if NET7_0_OR_GREATER
    [Fact]
    public void FieldEquals_Int128()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<Int128>("id", (obj, value) => obj.Int128Value == value);
        var query = queryBuilder.Build("id:10");
        Assert.True(query.Evaluate(new() { Int128Value = 10 }));
        Assert.False(query.Evaluate(new()));
    }

    [Fact]
    public void FieldEquals_UInt128()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<UInt128>("id", (obj, value) => obj.UInt128Value == value);
        var query = queryBuilder.Build("id:10");
        Assert.True(query.Evaluate(new() { UInt128Value = 10 }));
        Assert.False(query.Evaluate(new()));
    }
#endif

    [Fact]
    public void FieldEquals_BigInteger()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<BigInteger>("id", (obj, value) => obj.BigIntegerValue == value);
        var query = queryBuilder.Build("id:10");
        Assert.True(query.Evaluate(new() { BigIntegerValue = 10 }));
        Assert.False(query.Evaluate(new()));
    }

    [Fact]
    public void FieldEquals_Half()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<Half>("id", (obj, value) => obj.HalfValue == value);
        var query = queryBuilder.Build("id:10");
        Assert.True(query.Evaluate(new() { HalfValue = (Half)10 }));
        Assert.False(query.Evaluate(new()));
    }

    [Fact]
    public void FieldEquals_Single()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<float>("id", (obj, value) => obj.SingleValue == value);
        var query = queryBuilder.Build("id:10");
        Assert.True(query.Evaluate(new() { SingleValue = 10f }));
        Assert.False(query.Evaluate(new()));
    }

    [Fact]
    public void FieldEquals_Double()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<double>("id", (obj, value) => obj.DoubleValue == value);
        var query = queryBuilder.Build("id:10");
        Assert.True(query.Evaluate(new() { DoubleValue = 10f }));
        Assert.False(query.Evaluate(new()));
    }

    [Fact]
    public void FieldEquals_Decimal()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<decimal>("id", (obj, value) => obj.DecimalValue == value);
        var query = queryBuilder.Build("id:10.2");
        Assert.True(query.Evaluate(new() { DecimalValue = 10.2m }));
        Assert.False(query.Evaluate(new()));
    }

    [Fact]
    public void FieldEquals_DateTimeOffset()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<DateTimeOffset>("id", (obj, value) => obj.DateTimeOffsetValue == value);
        var query = queryBuilder.Build("id:2022-01-01");
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new()));
    }

    [Fact]
    public void FieldEquals_DateTime()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<DateTime>("id", (obj, value) => obj.DateTimeValue == value);
        var query = queryBuilder.Build("id:2022-01-01");
        Assert.True(query.Evaluate(new() { DateTimeValue = new DateTime(2022, 1, 1, 0, 0, 0) }));
        Assert.False(query.Evaluate(new()));
    }

    [Fact]
    public void FieldEquals_DateOnly()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<DateOnly>("id", (obj, value) => obj.DateOnlyValue == value);
        var query = queryBuilder.Build("id:2022-01-01");
        Assert.True(query.Evaluate(new() { DateOnlyValue = DateOnly.FromDateTime(new DateTime(2022, 1, 1, 0, 0, 0)) }));
        Assert.False(query.Evaluate(new()));
    }

    [Fact]
    public void FieldEquals_QuotedString()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler("id", (obj, value) => obj.StringValue == value);
        var query = queryBuilder.Build("id:\"sample query\"");
        Assert.True(query.Evaluate(new() { StringValue = "sample query" }));
        Assert.False(query.Evaluate(new() { StringValue = "Another value" }));
    }

    [Fact]
    public void FieldEquals_DateTime_Today()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<DateTimeOffset>("date", (obj, range) => range.IsInRange(obj.DateTimeOffsetValue));
        var query = queryBuilder.Build("date:today");

        RangeSyntax.UtcNow = new DateTime(2022, 1, 1, 10, 0, 0);
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero) }));
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 1, 1, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2021, 12, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 2, 1, 0, 0, 0, TimeSpan.Zero) }));
    }

    [Fact]
    public void FieldEquals_DateTime_Yesterday()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<DateTimeOffset>("date", (obj, range) => range.IsInRange(obj.DateTimeOffsetValue));
        var query = queryBuilder.Build("date:Yesterday");

        RangeSyntax.UtcNow = new DateTime(2022, 1, 2, 10, 0, 0);
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero) }));
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 1, 1, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2021, 12, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 2, 1, 0, 0, 0, TimeSpan.Zero) }));
    }

    [Fact]
    public void FieldEquals_DateTime_ThisWeek()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<DateTimeOffset>("date", (obj, range) => range.IsInRange(obj.DateTimeOffsetValue));
        var query = queryBuilder.Build("date:\"this week\"");

        RangeSyntax.UtcNow = new DateTime(2022, 1, 2, 10, 0, 0);
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2021, 12, 27, 0, 0, 0, TimeSpan.Zero) }));
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 1, 2, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2021, 12, 26, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 2, 3, 0, 0, 0, TimeSpan.Zero) }));
    }

    [Fact]
    public void FieldEquals_DateTime_ThisMonth()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<DateTimeOffset>("date", (obj, range) => range.IsInRange(obj.DateTimeOffsetValue));
        var query = queryBuilder.Build("date:\"this month\"");

        RangeSyntax.UtcNow = new DateTime(2022, 1, 2, 10, 0, 0);
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero) }));
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 1, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2021, 12, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 2, 1, 0, 0, 0, TimeSpan.Zero) }));
    }

    [Fact]
    public void FieldEquals_DateTime_ThisMonth_LeapYear()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<DateTimeOffset>("date", (obj, range) => range.IsInRange(obj.DateTimeOffsetValue));
        var query = queryBuilder.Build("date:\"this month\"");

        RangeSyntax.UtcNow = new DateTime(2020, 2, 2, 10, 0, 0);
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2020, 2, 1, 0, 0, 0, TimeSpan.Zero) }));
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2020, 2, 29, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2020, 1, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2020, 3, 1, 0, 0, 0, TimeSpan.Zero) }));
    }

    [Fact]
    public void FieldEquals_DateTime_ThisMonth_NonLeapYear()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<DateTimeOffset>("date", (obj, range) => range.IsInRange(obj.DateTimeOffsetValue));
        var query = queryBuilder.Build("date:\"this month\"");

        RangeSyntax.UtcNow = new DateTime(2022, 2, 2, 10, 0, 0);
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 2, 1, 0, 0, 0, TimeSpan.Zero) }));
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 2, 28, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 1, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 3, 1, 0, 0, 0, TimeSpan.Zero) }));
    }

    [Fact]
    public void FieldEquals_DateTime_LastMonth()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<DateTimeOffset>("date", (obj, range) => range.IsInRange(obj.DateTimeOffsetValue));
        var query = queryBuilder.Build("date:\"last month\"");

        RangeSyntax.UtcNow = new DateTime(2022, 2, 2, 10, 0, 0);
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero) }));
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 1, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2021, 12, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 2, 1, 0, 0, 0, TimeSpan.Zero) }));
    }

    [Fact]
    public void FieldEquals_DateTime_LastMonth_LeapYear()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<DateTimeOffset>("date", (obj, range) => range.IsInRange(obj.DateTimeOffsetValue));
        var query = queryBuilder.Build("date:\"last month\"");

        RangeSyntax.UtcNow = new DateTime(2020, 3, 2, 10, 0, 0);
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2020, 2, 1, 0, 0, 0, TimeSpan.Zero) }));
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2020, 2, 29, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2020, 1, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2020, 3, 1, 0, 0, 0, TimeSpan.Zero) }));
    }

    [Fact]
    public void FieldEquals_DateTime_LastMonth_NonLeapYear()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<DateTimeOffset>("date", (obj, range) => range.IsInRange(obj.DateTimeOffsetValue));
        var query = queryBuilder.Build("date:\"last month\"");

        RangeSyntax.UtcNow = new DateTime(2022, 3, 2, 10, 0, 0);
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 2, 1, 0, 0, 0, TimeSpan.Zero) }));
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 2, 28, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 1, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 3, 1, 0, 0, 0, TimeSpan.Zero) }));
    }

    [Fact]
    public void FieldEquals_DateTime_ThisYear()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<DateTimeOffset>("date", (obj, range) => range.IsInRange(obj.DateTimeOffsetValue));
        var query = queryBuilder.Build("date:\"This Year\"");

        RangeSyntax.UtcNow = new DateTime(2022, 2, 2, 10, 0, 0);
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero) }));
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 12, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2021, 12, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero) }));
    }

    [Fact]
    public void FieldEquals_DateTime_LastYear()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<DateTimeOffset>("date", (obj, range) => range.IsInRange(obj.DateTimeOffsetValue));
        var query = queryBuilder.Build("date:\"last Year\"");

        RangeSyntax.UtcNow = new DateTime(2023, 2, 2, 10, 0, 0);
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero) }));
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 12, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2021, 12, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero) }));
    }

    [Fact]
    public void FieldEquals_DateOnly_LastYear()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<DateOnly>("date", (obj, range) => range.IsInRange(obj.DateOnlyValue));
        var query = queryBuilder.Build("date:\"last Year\"");

        RangeSyntax.UtcNow = new DateTime(2023, 2, 2, 10, 0, 0);
        Assert.True(query.Evaluate(new() { DateOnlyValue = DateOnly.FromDateTime(new DateTime(2022, 1, 1, 0, 0, 0)) }));
        Assert.True(query.Evaluate(new() { DateOnlyValue = DateOnly.FromDateTime(new DateTime(2022, 12, 31, 23, 59, 59)) }));
        Assert.False(query.Evaluate(new() { DateOnlyValue = DateOnly.FromDateTime(new DateTime(2021, 12, 31, 23, 59, 59)) }));
        Assert.False(query.Evaluate(new() { DateOnlyValue = DateOnly.FromDateTime(new DateTime(2023, 1, 1, 0, 0, 0)) }));
    }

    [Fact]
    public void FieldRange_SingleValue()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<int>("field", (obj, range) => range.IsInRange(obj.Int32Value));
        var query = queryBuilder.Build("field:10");
        Assert.True(query.Evaluate(new() { Int32Value = 10 }));
        Assert.False(query.Evaluate(new() { Int32Value = 9 }));
    }

    [Fact]
    public void FieldRange_InvalidRange()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<int>("field", (obj, range) => range.IsInRange(obj.Int32Value));
        var query = queryBuilder.Build("field:test");
        Assert.False(query.Evaluate(new() { Int32Value = 9 }));
    }

    [Fact]
    public void FieldEquals_UnquotedString()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler("id", (obj, value) => obj.StringValue == value);
        queryBuilder.SetUnhandledPropertyHandler((obj, key, op, value) => throw new NotSupportedException());
        queryBuilder.SetTextFilterHandler((obj, value) => throw new NotSupportedException());
        var query = queryBuilder.Build("id:sample query");

        new Func<object>(() => query.Evaluate(new() { StringValue = "sample" })).Should().ThrowExactly<NotSupportedException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public void FieldRange_Int32_Contains_True(int value)
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<int>("id", (obj, value) => value.IsInRange(obj.Int32Value));
        var query = queryBuilder.Build("id:1..10");
        Assert.True(query.Evaluate(new() { Int32Value = value }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void FieldRange_Int32_Contains_False(int value)
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<int>("id", (obj, value) => value.IsInRange(obj.Int32Value));
        var query = queryBuilder.Build("id:1..10");
        Assert.False(query.Evaluate(new() { Int32Value = value }));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(int.MaxValue)]
    public void FieldRange_Int32_GreaterThanOrEqual_True(int value)
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<int>("id", (obj, value) => value.IsInRange(obj.Int32Value));
        var query = queryBuilder.Build("id>=10");
        Assert.True(query.Evaluate(new() { Int32Value = value }));
    }

    [Theory]
    [InlineData(9)]
    [InlineData(int.MinValue)]
    public void FieldRange_Int32_GreaterThanOrEqual_False(int value)
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<int>("id", (obj, value) => value.IsInRange(obj.Int32Value));
        var query = queryBuilder.Build("id>=10");
        Assert.False(query.Evaluate(new() { Int32Value = value }));
    }

    [Theory]
    [InlineData(11)]
    [InlineData(int.MaxValue)]
    public void FieldRange_Int32_GreaterThan_True(int value)
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<int>("id", (obj, value) => value.IsInRange(obj.Int32Value));
        var query = queryBuilder.Build("id>10");
        Assert.True(query.Evaluate(new() { Int32Value = value }));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(int.MinValue)]
    public void FieldRange_Int32_GreaterThan_False(int value)
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<int>("id", (obj, value) => value.IsInRange(obj.Int32Value));
        var query = queryBuilder.Build("id>10");
        Assert.False(query.Evaluate(new() { Int32Value = value }));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(9)]
    [InlineData(int.MinValue)]
    public void FieldRange_Int32_LessThanOrEqual_True(int value)
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<int>("id", (obj, value) => value.IsInRange(obj.Int32Value));
        var query = queryBuilder.Build("id<=10");
        Assert.True(query.Evaluate(new() { Int32Value = value }));
    }

    [Theory]
    [InlineData(11)]
    [InlineData(int.MaxValue)]
    public void FieldRange_Int32_LessThanOrEqual_False(int value)
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<int>("id", (obj, value) => value.IsInRange(obj.Int32Value));
        var query = queryBuilder.Build("id<=10");
        Assert.False(query.Evaluate(new() { Int32Value = value }));
    }

    [Theory]
    [InlineData(9)]
    [InlineData(int.MinValue)]
    public void FieldRange_Int32_LessThan_True(int value)
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<int>("id", (obj, value) => value.IsInRange(obj.Int32Value));
        var query = queryBuilder.Build("id<10");
        Assert.True(query.Evaluate(new() { Int32Value = value }));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(int.MaxValue)]
    public void FieldRange_Int32_LessThan_False(int value)
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<int>("id", (obj, value) => value.IsInRange(obj.Int32Value));
        var query = queryBuilder.Build("id<10");
        Assert.False(query.Evaluate(new() { Int32Value = value }));
    }

    [Theory]
    [InlineData(11)]
    [InlineData(9)]
    [InlineData(int.MinValue)]
    public void FieldRange_Int32_NotEqual_True(int value)
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<int>("id", (obj, value) => value.IsInRange(obj.Int32Value));
        var query = queryBuilder.Build("id<>10");
        Assert.True(query.Evaluate(new() { Int32Value = value }));
    }

    [Theory]
    [InlineData(10)]
    public void FieldRange_Int32_NotEqual_False(int value)
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<int>("id", (obj, value) => value.IsInRange(obj.Int32Value));
        var query = queryBuilder.Build("id<>10");
        Assert.False(query.Evaluate(new() { Int32Value = value }));
    }

    [Fact]
    public void FieldRange_DateTimeOffset_Contains()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<DateTimeOffset>("date", (obj, value) => value.IsInRange(obj.DateTimeOffsetValue));
        Assert.True(queryBuilder.Build("date:2022-01-01..2022-01-31").Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 01, 01, 00, 00, 00, TimeSpan.Zero) }));
        Assert.True(queryBuilder.Build("date:2022-01-01..2022-01-31").Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 01, 30, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(queryBuilder.Build("date:2022-01-01..2022-01-31").Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2021, 12, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(queryBuilder.Build("date:2022-01-01..2022-01-31").Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 02, 01, 00, 00, 00, TimeSpan.Zero) }));
    }

    [Fact]
    public void FieldRange_DateTimeOffset_WithDateTime_Contains()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<DateTimeOffset>("date", (obj, value) => value.IsInRange(obj.DateTimeOffsetValue));
        Assert.True(queryBuilder.Build("date:2022-01-01T00:00:00..2022-01-31T23:59:59").Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 01, 01, 00, 00, 00, TimeSpan.Zero) }));
        Assert.True(queryBuilder.Build("date:2022-01-01T00:00:00..2022-01-31T23:59:59").Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 01, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(queryBuilder.Build("date:2022-01-01T00:00:00..2022-01-31T23:59:59").Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2021, 12, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(queryBuilder.Build("date:2022-01-01T00:00:00..2022-01-31T23:59:59").Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 02, 01, 00, 00, 00, TimeSpan.Zero) }));
    }

    [Fact]
    public void FieldRange_DateTimeOffset_WithDateTime2_Contains()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<DateTimeOffset>("date", (obj, value) => value.IsInRange(obj.DateTimeOffsetValue));
        Assert.True(queryBuilder.Build("date:2022-01-01T00:00:00Z..2022-01-31T23:59:59Z").Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 01, 01, 00, 00, 00, TimeSpan.Zero) }));
        Assert.True(queryBuilder.Build("date:2022-01-01T00:00:00Z..2022-01-31T23:59:59Z").Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 01, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(queryBuilder.Build("date:2022-01-01T00:00:00Z..2022-01-31T23:59:59Z").Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2021, 12, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(queryBuilder.Build("date:2022-01-01T00:00:00Z..2022-01-31T23:59:59Z").Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 02, 01, 00, 00, 00, TimeSpan.Zero) }));
    }

    [Fact]
    public void EnumFilter()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<DayOfWeek>("date", (obj, value) => obj.DayOfWeekValue == value);
        Assert.True(queryBuilder.Build("date:monday").Evaluate(new() { DayOfWeekValue = DayOfWeek.Monday }));
        Assert.False(queryBuilder.Build("date:monday").Evaluate(new() { DayOfWeekValue = DayOfWeek.Tuesday }));
    }

    [Fact]
    public void KeyAndValueMatch()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler("a", "value", (obj) => obj.Int32Value == 1);
        queryBuilder.SetUnhandledPropertyHandler((obj, key, op, value) => throw new NotSupportedException());
        Assert.True(queryBuilder.Build("a:value").Evaluate(new() { Int32Value = 1 }));
        Assert.False(queryBuilder.Build("a:value").Evaluate(new() { Int32Value = 2 }));

        new Func<object>(() => queryBuilder.Build("a:1").Evaluate(new() { Int32Value = 1 })).Should().ThrowExactly<NotSupportedException>();
    }

    [Fact]
    public void KeyAndValueMatch2()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler("a", "0", (obj) => true);
        queryBuilder.AddHandler<int>("a", (obj, value) => obj.Int32Value == value);
        queryBuilder.SetUnhandledPropertyHandler((obj, key, op, value) => throw new NotSupportedException());
        Assert.True(queryBuilder.Build("a:0").Evaluate(new() { Int32Value = 1 }));
        Assert.True(queryBuilder.Build("a:1").Evaluate(new() { Int32Value = 1 }));
        Assert.False(queryBuilder.Build("a:2").Evaluate(new() { Int32Value = 1 }));
    }

    [Fact]
    public void KeyAndValueMatch3()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler("a", "value", (obj, value) => obj.Int32Value == 1);
        Assert.True(queryBuilder.Build("a=value").Evaluate(new() { Int32Value = 1 }));
        Assert.True(queryBuilder.Build("a:value").Evaluate(new() { Int32Value = 1 }));
        Assert.False(queryBuilder.Build("a:1").Evaluate(new() { Int32Value = 1 }));
    }

    [Fact]
    public void KeyAndOperatorAndValueMatch()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<int>("a", (obj, op, value) => op == KeyValueOperator.EqualTo && obj.Int32Value == value);
        Assert.True(queryBuilder.Build("a=1").Evaluate(new() { Int32Value = 1 }));
        Assert.True(queryBuilder.Build("a:1").Evaluate(new() { Int32Value = 1 }));
        Assert.False(queryBuilder.Build("a<>2").Evaluate(new() { Int32Value = 1 }));
        Assert.False(queryBuilder.Build("a=invalid").Evaluate(new() { Int32Value = 1 }));
    }

    [Fact]
    public void KeyAndOperatorAndValueMatch2()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler("a", "1", (obj, op) => op == KeyValueOperator.EqualTo && obj.Int32Value == 1);
        Assert.True(queryBuilder.Build("a=1").Evaluate(new() { Int32Value = 1 }));
        Assert.True(queryBuilder.Build("a:1").Evaluate(new() { Int32Value = 1 }));
        Assert.False(queryBuilder.Build("a<>2").Evaluate(new() { Int32Value = 1 }));
    }

    [Fact]
    public void KeyAndOperatorAndValueMatch3()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler("a", "1", (obj, op) => op == KeyValueOperator.EqualTo && obj.Int32Value == 1);
        Assert.True(queryBuilder.Build("a=1").Evaluate(new() { Int32Value = 1 }));
        Assert.True(queryBuilder.Build("a:1").Evaluate(new() { Int32Value = 1 }));
        Assert.False(queryBuilder.Build("a<>2").Evaluate(new() { Int32Value = 1 }));
    }

    [Fact]
    public void KeyAndOperatorMatch()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler("a", (obj, op, value) => op == KeyValueOperator.EqualTo && obj.Int32Value == 1);
        Assert.True(queryBuilder.Build("a=1").Evaluate(new() { Int32Value = 1 }));
        Assert.True(queryBuilder.Build("a:1").Evaluate(new() { Int32Value = 1 }));
        Assert.False(queryBuilder.Build("a<>2").Evaluate(new() { Int32Value = 1 }));
    }

    [Fact]
    public void UnhandledField_FallbackToTextSearch()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.SetTextFilterHandler((obj, value) => obj.StringValue?.Contains(value, StringComparison.OrdinalIgnoreCase) == true);
        var query = queryBuilder.Build("dummy:10");
        Assert.True(query.Evaluate(new() { StringValue = "dummy:10" }));
        Assert.False(query.Evaluate(new() { StringValue = "Another value" }));
    }

    [Fact]
    public void UnhandledField_UseHandler()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.SetUnhandledPropertyHandler((obj, key, op, value) => throw new NotSupportedException());
        var query = queryBuilder.Build("dummy:10");

        new Func<object>(() => query.Evaluate(new() { StringValue = "dummy:10" })).Should().ThrowExactly<NotSupportedException>();
    }

    [Fact]
    public void EmptyQuery()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.SetUnhandledPropertyHandler((obj, key, op, value) => false);
        queryBuilder.SetTextFilterHandler((obj, value) => false);
        var query = queryBuilder.Build("");
        Assert.True(query.Evaluate(new Sample { }));
    }

    [Fact]
    public void OrQuery()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<int>("int32", (obj, value) => obj.Int32Value == value);
        queryBuilder.AddHandler<long>("int64", (obj, value) => obj.Int64Value == value);
        queryBuilder.SetTextFilterHandler((obj, value) => value == "test");
        var query = queryBuilder.Build("int32:1 OR int64:2");
        Assert.True(query.Evaluate(new Sample { Int32Value = 1, Int64Value = 2 }));
        Assert.True(query.Evaluate(new Sample { Int32Value = 1, Int64Value = 99 }));
        Assert.True(query.Evaluate(new Sample { Int32Value = 99, Int64Value = 2 }));
        Assert.False(query.Evaluate(new Sample { Int32Value = 99, Int64Value = 99 }));
    }

    [Fact]
    public void MultipleOrQuery()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<int>("int32", (obj, value) => obj.Int32Value == value);
        queryBuilder.AddHandler<long>("int64", (obj, value) => obj.Int64Value == value);
        queryBuilder.SetTextFilterHandler((obj, value) => value == "test");
        var query = queryBuilder.Build("int32:1 OR int64:2 OR int32:2");
        Assert.True(query.Evaluate(new Sample { Int32Value = 1, Int64Value = 2 }));
        Assert.True(query.Evaluate(new Sample { Int32Value = 1, Int64Value = 99 }));
        Assert.True(query.Evaluate(new Sample { Int32Value = 2, Int64Value = 99 }));
        Assert.True(query.Evaluate(new Sample { Int32Value = 99, Int64Value = 2 }));
        Assert.False(query.Evaluate(new Sample { Int32Value = 99, Int64Value = 99 }));
    }

    [Fact]
    public void OrTextQuery()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.SetTextFilterHandler((obj, value) => obj.StringValue == value);
        var query = queryBuilder.Build("abc OR def");
        Assert.True(query.Evaluate(new Sample { StringValue = "abc" }));
        Assert.True(query.Evaluate(new Sample { StringValue = "def" }));
        Assert.False(query.Evaluate(new Sample { StringValue = "dummy" }));
    }

    [Fact]
    public void AndQuery()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<int>("int32", (obj, value) => obj.Int32Value == value);
        queryBuilder.AddHandler<long>("int64", (obj, value) => obj.Int64Value == value);
        queryBuilder.SetTextFilterHandler((obj, value) => value == "test");
        var query = queryBuilder.Build("int32:1 AND int64:2");
        Assert.True(query.Evaluate(new Sample { Int32Value = 1, Int64Value = 2 }));
        Assert.False(query.Evaluate(new Sample { Int32Value = 1, Int64Value = 99 }));
        Assert.False(query.Evaluate(new Sample { Int32Value = 99, Int64Value = 2 }));
        Assert.False(query.Evaluate(new Sample { Int32Value = 99, Int64Value = 99 }));
    }

    [Fact]
    public void AndKeywordAsText()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<int>("int32", (obj, value) => obj.Int32Value == value);
        queryBuilder.SetTextFilterHandler((obj, value) => obj.StringValue == value);
        var query = queryBuilder.Build("int32:1 AND");
        Assert.True(query.Evaluate(new Sample { Int32Value = 1, StringValue = "AND" }));
        Assert.False(query.Evaluate(new Sample { Int32Value = 1, StringValue = "dummy" }));
        Assert.False(query.Evaluate(new Sample { Int32Value = 99, StringValue = "AND" }));
        Assert.False(query.Evaluate(new Sample { Int32Value = 99, StringValue = "dummy" }));
    }

    [Fact]
    public void Not1()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<int>("int32", (obj, value) => obj.Int32Value == value);
        queryBuilder.AddHandler<int>("int64", (obj, value) => obj.Int64Value == value);
        queryBuilder.SetTextFilterHandler((obj, value) => obj.StringValue == value);
        var query = queryBuilder.Build("-int32:1");
        Assert.False(query.Evaluate(new Sample { Int32Value = 1 }));
        Assert.True(query.Evaluate(new Sample { Int32Value = 99 }));
    }

    [Fact]
    public void Not2()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<int>("int32", (obj, value) => obj.Int32Value == value);
        queryBuilder.SetTextFilterHandler((obj, value) => obj.StringValue == value);
        var query = queryBuilder.Build("-\"int32:1\"");
        Assert.False(query.Evaluate(new Sample { StringValue = "int32:1" }));
        Assert.True(query.Evaluate(new Sample { StringValue = "dummy" }));
        Assert.True(query.Evaluate(new Sample { StringValue = "dummy" }));
        Assert.True(query.Evaluate(new Sample { StringValue = "dummy" }));
    }

    [Fact]
    public void Parentheses()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<int>("int32", (obj, value) => obj.Int32Value == value);
        queryBuilder.AddHandler<int>("int64", (obj, value) => obj.Int64Value == value);
        queryBuilder.SetTextFilterHandler((obj, value) => obj.StringValue == value);
        var query = queryBuilder.Build("(int32:1 AND int64:2) OR dummy");
        Assert.True(query.Evaluate(new Sample { Int32Value = 1, Int64Value = 2, StringValue = "dummy" }));
        Assert.True(query.Evaluate(new Sample { Int32Value = 99, Int64Value = 2, StringValue = "dummy" }));
        Assert.True(query.Evaluate(new Sample { Int32Value = 1, Int64Value = 2, StringValue = "AND" }));
        Assert.False(query.Evaluate(new Sample { Int32Value = 99, Int64Value = 2, StringValue = "AND" }));
    }

    [Fact]
    public void Complex1()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<int>("int32", (obj, value) => obj.Int32Value == value);
        queryBuilder.AddHandler<int>("int64", (obj, value) => obj.Int64Value == value);
        queryBuilder.SetTextFilterHandler((obj, value) => obj.StringValue == value);
        var query = queryBuilder.Build("(int32:1 AND int64:2) AND dummy");
        Assert.True(query.Evaluate(new Sample { Int32Value = 1, Int64Value = 2, StringValue = "dummy" }));
        Assert.False(query.Evaluate(new Sample { Int32Value = 99, Int64Value = 2, StringValue = "dummy" }));
        Assert.False(query.Evaluate(new Sample { Int32Value = 1, Int64Value = 2, StringValue = "AND" }));
        Assert.False(query.Evaluate(new Sample { Int32Value = 99, Int64Value = 2, StringValue = "AND" }));
    }

    [Fact]
    public void Complex2()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<int>("int32", (obj, value) => obj.Int32Value == value);
        queryBuilder.AddHandler<int>("int64", (obj, value) => obj.Int64Value == value);
        queryBuilder.SetTextFilterHandler((obj, value) => obj.StringValue == value);
        var query = queryBuilder.Build("(int32:1 AND int64:2) AND NOT dummy");
        Assert.True(query.Evaluate(new Sample { Int32Value = 1, Int64Value = 2, StringValue = "AND" }));
        Assert.False(query.Evaluate(new Sample { Int32Value = 1, Int64Value = 2, StringValue = "dummy" }));
        Assert.False(query.Evaluate(new Sample { Int32Value = 99, Int64Value = 2, StringValue = "dummy" }));
        Assert.False(query.Evaluate(new Sample { Int32Value = 99, Int64Value = 2, StringValue = "AND" }));
    }

    [Fact]
    public void FieldAndText()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<int>("int32", (obj, value) => obj.Int32Value == value);
        queryBuilder.SetTextFilterHandler((obj, value) => obj.StringValue == value);
        var query = queryBuilder.Build("int32:1 sample");
        Assert.True(query.Evaluate(new Sample { Int32Value = 1, StringValue = "sample" }));
        Assert.False(query.Evaluate(new Sample { Int32Value = 1, StringValue = "no" }));
        Assert.False(query.Evaluate(new Sample { Int32Value = 2, StringValue = "sample" }));
    }

    [Fact]
    public void FieldAndNotText()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<int>("int32", (obj, value) => obj.Int32Value == value);
        queryBuilder.SetTextFilterHandler((obj, value) => obj.StringValue == value);
        var query = queryBuilder.Build("int32:1 AND NOT sample");
        Assert.False(query.Evaluate(new Sample { Int32Value = 1, StringValue = "sample" }));
        Assert.True(query.Evaluate(new Sample { Int32Value = 1, StringValue = "no" }));
        Assert.False(query.Evaluate(new Sample { Int32Value = 2, StringValue = "sample" }));
    }

    private sealed class Sample
    {
        public byte ByteValue { get; set; }
        public sbyte SByteValue { get; set; }
        public short Int16Value { get; set; }
        public ushort UInt16Value { get; set; }
        public int Int32Value { get; set; }
        public uint UInt32Value { get; set; }
        public long Int64Value { get; set; }
        public ulong UInt64Value { get; set; }
#if NET7_0_OR_GREATER
        public Int128 Int128Value { get; set; }
        public UInt128 UInt128Value { get; set; }
#endif
        public BigInteger BigIntegerValue { get; set; }
        public Half HalfValue { get; set; }
        public float SingleValue { get; set; }
        public double DoubleValue { get; set; }
        public decimal DecimalValue { get; set; }
        public DateOnly DateOnlyValue { get; set; }
        public DateTime DateTimeValue { get; set; }
        public DateTimeOffset DateTimeOffsetValue { get; set; }
        public DayOfWeek DayOfWeekValue { get; set; }
        public string StringValue { get; set; }
    }
}