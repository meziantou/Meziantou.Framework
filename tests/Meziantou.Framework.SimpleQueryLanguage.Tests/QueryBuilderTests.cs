using System.Numerics;
using Meziantou.Framework.SimpleQueryLanguage.Ranges;
using Meziantou.Framework.SimpleQueryLanguage.Syntax;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.SimpleQueryLanguage.Tests;

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
        var queryBuilder = CreateDateTimeOffsetRangeQueryBuilder(new DateTimeOffset(2022, 1, 1, 10, 0, 0, TimeSpan.Zero));
        var query = queryBuilder.Build("date:today");

        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero) }));
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 1, 1, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2021, 12, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 2, 1, 0, 0, 0, TimeSpan.Zero) }));
    }

    [Fact]
    public void FieldEquals_DateTime_Yesterday()
    {
        var queryBuilder = CreateDateTimeOffsetRangeQueryBuilder(new DateTimeOffset(2022, 1, 2, 10, 0, 0, TimeSpan.Zero));
        var query = queryBuilder.Build("date:Yesterday");

        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero) }));
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 1, 1, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2021, 12, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 2, 1, 0, 0, 0, TimeSpan.Zero) }));
    }

    [Fact]
    public void FieldEquals_DateTime_ThisWeek()
    {
        var queryBuilder = CreateDateTimeOffsetRangeQueryBuilder(new DateTimeOffset(2022, 1, 2, 10, 0, 0, TimeSpan.Zero));
        var query = queryBuilder.Build("date:\"this week\"");

        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2021, 12, 27, 0, 0, 0, TimeSpan.Zero) }));
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 1, 2, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2021, 12, 26, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 2, 3, 0, 0, 0, TimeSpan.Zero) }));
    }

    [Fact]
    public void FieldEquals_DateTime_ThisMonth()
    {
        var queryBuilder = CreateDateTimeOffsetRangeQueryBuilder(new DateTimeOffset(2022, 1, 2, 10, 0, 0, TimeSpan.Zero));
        var query = queryBuilder.Build("date:\"this month\"");

        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero) }));
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 1, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2021, 12, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 2, 1, 0, 0, 0, TimeSpan.Zero) }));
    }

    [Fact]
    public void FieldEquals_DateTime_ThisMonth_LeapYear()
    {
        var queryBuilder = CreateDateTimeOffsetRangeQueryBuilder(new DateTimeOffset(2020, 2, 2, 10, 0, 0, TimeSpan.Zero));
        var query = queryBuilder.Build("date:\"this month\"");

        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2020, 2, 1, 0, 0, 0, TimeSpan.Zero) }));
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2020, 2, 29, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2020, 1, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2020, 3, 1, 0, 0, 0, TimeSpan.Zero) }));
    }

    [Fact]
    public void FieldEquals_DateTime_ThisMonth_NonLeapYear()
    {
        var queryBuilder = CreateDateTimeOffsetRangeQueryBuilder(new DateTimeOffset(2022, 2, 2, 10, 0, 0, TimeSpan.Zero));
        var query = queryBuilder.Build("date:\"this month\"");

        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 2, 1, 0, 0, 0, TimeSpan.Zero) }));
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 2, 28, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 1, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 3, 1, 0, 0, 0, TimeSpan.Zero) }));
    }

    [Fact]
    public void FieldEquals_DateTime_LastMonth()
    {
        var queryBuilder = CreateDateTimeOffsetRangeQueryBuilder(new DateTimeOffset(2022, 2, 2, 10, 0, 0, TimeSpan.Zero));
        var query = queryBuilder.Build("date:\"last month\"");

        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero) }));
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 1, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2021, 12, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 2, 1, 0, 0, 0, TimeSpan.Zero) }));
    }

    [Fact]
    public void FieldEquals_DateTime_LastMonth_LeapYear()
    {
        var queryBuilder = CreateDateTimeOffsetRangeQueryBuilder(new DateTimeOffset(2020, 3, 2, 10, 0, 0, TimeSpan.Zero));
        var query = queryBuilder.Build("date:\"last month\"");

        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2020, 2, 1, 0, 0, 0, TimeSpan.Zero) }));
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2020, 2, 29, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2020, 1, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2020, 3, 1, 0, 0, 0, TimeSpan.Zero) }));
    }

    [Fact]
    public void FieldEquals_DateTime_LastMonth_NonLeapYear()
    {
        var queryBuilder = CreateDateTimeOffsetRangeQueryBuilder(new DateTimeOffset(2022, 3, 2, 10, 0, 0, TimeSpan.Zero));
        var query = queryBuilder.Build("date:\"last month\"");

        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 2, 1, 0, 0, 0, TimeSpan.Zero) }));
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 2, 28, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 1, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 3, 1, 0, 0, 0, TimeSpan.Zero) }));
    }

    [Fact]
    public void FieldEquals_DateTime_ThisYear()
    {
        var queryBuilder = CreateDateTimeOffsetRangeQueryBuilder(new DateTimeOffset(2022, 2, 2, 10, 0, 0, TimeSpan.Zero));
        var query = queryBuilder.Build("date:\"This Year\"");

        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero) }));
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 12, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2021, 12, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero) }));
    }

    [Fact]
    public void FieldEquals_DateTime_LastYear()
    {
        var queryBuilder = CreateDateTimeOffsetRangeQueryBuilder(new DateTimeOffset(2023, 2, 2, 10, 0, 0, TimeSpan.Zero));
        var query = queryBuilder.Build("date:\"last Year\"");

        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero) }));
        Assert.True(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2022, 12, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2021, 12, 31, 23, 59, 59, TimeSpan.Zero) }));
        Assert.False(query.Evaluate(new() { DateTimeOffsetValue = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero) }));
    }

    [Fact]
    public void FieldEquals_DateOnly_LastYear()
    {
        var queryBuilder = CreateDateOnlyRangeQueryBuilder(new DateTimeOffset(2023, 2, 2, 10, 0, 0, TimeSpan.Zero));
        var query = queryBuilder.Build("date:\"last Year\"");

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

        Assert.Throws<NotSupportedException>(() => query.Evaluate(new() { StringValue = "sample" }));
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
    [InlineData(-11, true)]
    [InlineData(-10, false)]
    [InlineData(-9, false)]
    public void FieldRange_Int32_LessThan_NegativeValue(int value, bool expectedResult)
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<int>("amount", (obj, range) => range.IsInRange(obj.Int32Value));

        var query = queryBuilder.Build("amount<-10");

        Assert.Equal(expectedResult, query.Evaluate(new() { Int32Value = value }));
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

        Assert.Throws<NotSupportedException>(() => queryBuilder.Build("a:1").Evaluate(new() { Int32Value = 1 }));
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

        Assert.Throws<NotSupportedException>(() => query.Evaluate(new() { StringValue = "dummy:10" }));
    }

    [Theory]
    [InlineData("dummy:10", true)]
    [InlineData("-dummy:10", false)]
    [InlineData("NOT dummy:10", false)]
    public void UnhandledField_HandlerRespectsNegation(string query, bool expectedResult)
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.SetUnhandledPropertyHandler((obj, key, op, value) => true);

        Assert.Equal(expectedResult, queryBuilder.Build(query).Evaluate(new Sample()));
    }

    [Theory]
    [InlineData("size:medium", true)]
    [InlineData("size>small", true)]
    [InlineData("size>large", false)]
    [InlineData("size>=medium", true)]
    [InlineData("size<large", true)]
    [InlineData("size<=small", false)]
    public void RangeHandler_UsesCustomParserForComparisonOperators(string query, bool expectedResult)
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<int>("size", (obj, range) => range.IsInRange(obj.Int32Value), TryParseSize);

        Assert.Equal(expectedResult, queryBuilder.Build(query).Evaluate(new Sample { Int32Value = 2 }));

        static bool TryParseSize(string value, out int result)
        {
            result = value switch
            {
                "small" => 1,
                "medium" => 2,
                "large" => 3,
                _ => 0,
            };

            return result is not 0;
        }
    }

    [Fact]
    public void Build_NullQuery_ThrowsArgumentNullException()
    {
        var queryBuilder = new QueryBuilder<Sample>();

        var exception = Assert.Throws<ArgumentNullException>(() => queryBuilder.Build(query: null!));
        Assert.Equal("query", exception.ParamName);
    }

    [Theory]
    [InlineData("today")]
    [InlineData("yesterday")]
    [InlineData("this week")]
    [InlineData("this month")]
    [InlineData("last month")]
    [InlineData("this year")]
    [InlineData("last year")]
    public void DateKeyword_OnNonDateHandler_DoesNotMatch(string keyword)
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddRangeHandler<int>("age", (obj, range) => range.IsInRange(obj.Int32Value));
        var query = queryBuilder.Build($"age:\"{keyword}\"");

        Assert.False(query.Evaluate(new Sample { Int32Value = 42 }));
    }

    // 2026-03-15 is a Sunday, so "this week" is 2026-03-09..2026-03-16
    [Theory]
    [InlineData("today", true)]
    [InlineData("yesterday", false)]
    [InlineData("this week", true)]
    [InlineData("this month", true)]
    [InlineData("last month", false)]
    [InlineData("this year", true)]
    [InlineData("last year", false)]
    public void DateKeyword_OnDateTimeHandler_IsSupported(string keyword, bool expectedResult)
    {
        var timeProvider = new FakeTimeProvider();
        timeProvider.SetUtcNow(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero));

        var queryBuilder = new QueryBuilder<Sample>(timeProvider);
        queryBuilder.AddRangeHandler<DateTime>("date", (obj, range) => range.IsInRange(obj.DateTimeValue));
        var query = queryBuilder.Build($"date:\"{keyword}\"");

        Assert.Equal(expectedResult, query.Evaluate(new Sample { DateTimeValue = new DateTime(2026, 3, 15, 8, 0, 0, DateTimeKind.Utc) }));
    }

    [Fact]
    public void DateKeyword_OnDateTimeHandler_ProducesUtcBounds()
    {
        var timeProvider = new FakeTimeProvider();
        timeProvider.SetUtcNow(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero));

        BinaryRangeSyntax<DateTime>? capturedRange = null;
        var queryBuilder = new QueryBuilder<Sample>(timeProvider);
        queryBuilder.AddRangeHandler<DateTime>("date", (obj, range) =>
        {
            capturedRange = Assert.IsType<BinaryRangeSyntax<DateTime>>(range);
            return true;
        });

        queryBuilder.Build("date:today").Evaluate(new Sample());

        Assert.NotNull(capturedRange);
        Assert.Equal(new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc), capturedRange.LowerBound);
        Assert.Equal(DateTimeKind.Utc, capturedRange.LowerBound.Kind);
        Assert.Equal(new DateTime(2026, 3, 16, 0, 0, 0, DateTimeKind.Utc), capturedRange.UpperBound);
        Assert.Equal(DateTimeKind.Utc, capturedRange.UpperBound.Kind);
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

    [Fact]
    public void DeeplyNestedParentheses_ThrowsQueryTooComplex()
    {
        var query = new string('(', 100_000) + "a" + new string(')', 100_000);

        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.SetTextFilterHandler((obj, value) => true);

        Assert.Throws<QueryTooComplexException>(() => queryBuilder.Build(query));
    }

    [Fact]
    public void VeryLongConjunction_ThrowsQueryTooComplex()
    {
        var query = string.Join(' ', Enumerable.Range(0, 100_000).Select(i => "term" + i.ToString(CultureInfo.InvariantCulture)));

        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.SetTextFilterHandler((obj, value) => true);

        Assert.Throws<QueryTooComplexException>(() => queryBuilder.Build(query));
    }

    [Fact]
    public void ManyOrGroupsCombinedWithAnd_ThrowsQueryTooComplex()
    {
        // Converting to disjunctive normal form would produce 2^30 terms
        var query = string.Join(" AND ", Enumerable.Range(0, 30).Select(i => $"(a{i}:1 OR b{i}:2)"));

        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.SetTextFilterHandler((obj, value) => true);

        Assert.Throws<QueryTooComplexException>(() => queryBuilder.Build(query));
    }

    [Fact]
    public void ModeratelyComplexQuery_IsStillSupported()
    {
        // 2^10 disjunctions is well within the limit and must keep working
        var query = string.Join(" AND ", Enumerable.Range(0, 10).Select(i => $"(int32:{i} OR int32:{i + 100})"));

        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<int>("int32", (obj, value) => obj.Int32Value == value);

        Assert.False(queryBuilder.Build(query).Evaluate(new Sample { Int32Value = 0 }));
    }

    [Fact]
    public void LongFreeTextQuery_IsStillSupported()
    {
        // A user pasting a long sentence into a search box must not be rejected
        var query = string.Join(' ', Enumerable.Repeat("word", 500));

        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.SetTextFilterHandler((obj, value) => obj.StringValue == value);

        Assert.True(queryBuilder.Build(query).Evaluate(new Sample { StringValue = "word" }));
    }

    [Theory]
    [InlineData("name:john", true)]
    [InlineData("name=john", true)]
    [InlineData("name<>john", false)]
    [InlineData("name<>jane", true)]
    [InlineData("-name<>john", true)]
    [InlineData("name>john", false)]
    [InlineData("name<=john", false)]
    public void GenericHandler_String_SupportsEqualityOperatorsOnly(string query, bool expectedResult)
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<string>("name", (obj, value) => obj.StringValue == value);

        Assert.Equal(expectedResult, queryBuilder.Build(query).Evaluate(new Sample { StringValue = "john" }));
    }

    [Theory]
    [InlineData("id:100", true)]
    [InlineData("id<>100", false)]
    [InlineData("id<>1", true)]
    [InlineData("id>100", false)]
    [InlineData("id>=100", false)]
    [InlineData("id<>invalid", false)]
    public void GenericHandler_Int32_SupportsEqualityOperatorsOnly(string query, bool expectedResult)
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<int>("id", (obj, value) => obj.Int32Value == value);

        Assert.Equal(expectedResult, queryBuilder.Build(query).Evaluate(new Sample { Int32Value = 100 }));
    }

    [Theory]
    [InlineData("state:or", "or")]
    [InlineData("state:OR", "OR")]
    [InlineData("state=or", "or")]
    [InlineData("state:and", "and")]
    [InlineData("state:not", "not")]
    public void KeywordDirectlyAfterOperator_IsTheValue(string query, string expectedValue)
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler("state", (obj, value) => obj.StringValue == value);
        queryBuilder.SetTextFilterHandler((obj, value) => throw new InvalidOperationException($"Unexpected text query '{value}'"));

        var compiledQuery = queryBuilder.Build(query);

        Assert.True(compiledQuery.Evaluate(new Sample { StringValue = expectedValue }));
        Assert.False(compiledQuery.Evaluate(new Sample { StringValue = "ca" }));
    }

    [Theory]
    [InlineData("int32:1 OR")]
    [InlineData("(int32:1 OR)")]
    public void OrKeywordAsText(string query)
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<int>("int32", (obj, value) => obj.Int32Value == value);
        queryBuilder.SetTextFilterHandler((obj, value) => obj.StringValue == value);

        var compiledQuery = queryBuilder.Build(query);

        Assert.True(compiledQuery.Evaluate(new Sample { Int32Value = 1, StringValue = "OR" }));
        Assert.False(compiledQuery.Evaluate(new Sample { Int32Value = 1, StringValue = "dummy" }));
        Assert.False(compiledQuery.Evaluate(new Sample { Int32Value = 99, StringValue = "OR" }));
    }

    [Fact]
    public void OpenParenthesisAtEndAsText()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<int>("int32", (obj, value) => obj.Int32Value == value);
        queryBuilder.SetTextFilterHandler((obj, value) => obj.StringValue == value);

        var query = queryBuilder.Build("int32:1 (");

        Assert.True(query.Evaluate(new Sample { Int32Value = 1, StringValue = "(" }));
        Assert.False(query.Evaluate(new Sample { Int32Value = 1, StringValue = "dummy" }));
    }

    [Fact]
    public void UnmatchedClosingParenthesisAsText()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.SetTextFilterHandler((obj, value) => obj.StringValue?.Contains(value, StringComparison.Ordinal) == true);

        var query = queryBuilder.Build("a ) b");

        Assert.True(query.Evaluate(new Sample { StringValue = "a ) b" }));
        Assert.False(query.Evaluate(new Sample { StringValue = "a b" }));
    }

    [Fact]
    public void Parse_ManyUnmatchedClosingParentheses()
    {
        // Each unmatched ')' used to restart parsing from the first token, which took seconds for this query
        var query = "sample" + string.Concat(Enumerable.Repeat(" )", 50_000));

        var syntax = QuerySyntax.Parse(query);

        Assert.Equal(query.Length, syntax.Span.End);
    }

    [Fact]
    public void SyntaxTree_LongConjunction_DoesNotOverflowTheStack()
    {
        var query = string.Join(' ', Enumerable.Range(0, 5000).Select(i => "term" + i.ToString(CultureInfo.InvariantCulture)));
        var syntax = QuerySyntax.Parse(query);

        TextSpan? span = null;
        string? text = null;
        var thread = new Thread(() =>
        {
            span = syntax.Span;
            text = syntax.ToString();
        }, maxStackSize: 256 * 1024);
        thread.Start();
        thread.Join();

        Assert.Equal(query.Length, span?.End);
        Assert.StartsWith("AND", text);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(" \t\r\n")]
    public void WhitespaceQuery_MatchesEverything(string query)
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.SetUnhandledPropertyHandler((obj, key, op, value) => false);
        queryBuilder.SetTextFilterHandler((obj, value) => false);

        Assert.True(queryBuilder.Build(query).Evaluate(new Sample()));
    }

    [Fact]
    public void NonBreakingSpace_SeparatesTerms()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<int>("int32", (obj, value) => obj.Int32Value == value);
        queryBuilder.SetTextFilterHandler((obj, value) => obj.StringValue == value);

        var query = queryBuilder.Build("int32:1 sample");

        Assert.True(query.Evaluate(new Sample { Int32Value = 1, StringValue = "sample" }));
        Assert.False(query.Evaluate(new Sample { Int32Value = 1, StringValue = "no" }));
    }

    // 2026-03-15 is a Sunday, so "this week" is 2026-03-09..2026-03-16
    [Theory]
    [InlineData("today", true)]
    [InlineData("yesterday", false)]
    [InlineData("this week", true)]
    [InlineData("last year", false)]
    public void DateKeyword_OnNullableDateTimeHandler_IsSupported(string keyword, bool expectedResult)
    {
        var timeProvider = new FakeTimeProvider();
        timeProvider.SetUtcNow(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero));

        var queryBuilder = new QueryBuilder<Sample>(timeProvider);
        queryBuilder.AddRangeHandler<DateTime?>("date", (obj, range) => range.IsInRange(obj.NullableDateTimeValue));
        var query = queryBuilder.Build($"date:\"{keyword}\"");

        Assert.Equal(expectedResult, query.Evaluate(new Sample { NullableDateTimeValue = new DateTime(2026, 3, 15, 8, 0, 0, DateTimeKind.Utc) }));
    }

    [Fact]
    public void DateKeyword_IsResolvedWhenTheQueryIsBuilt()
    {
        var timeProvider = new FakeTimeProvider();
        timeProvider.SetUtcNow(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero));

        var queryBuilder = new QueryBuilder<Sample>(timeProvider);
        queryBuilder.AddRangeHandler<DateTime>("date", (obj, range) => range.IsInRange(obj.DateTimeValue));
        var query = queryBuilder.Build("date:today");

        timeProvider.Advance(TimeSpan.FromDays(1));

        Assert.True(query.Evaluate(new Sample { DateTimeValue = new DateTime(2026, 3, 15, 8, 0, 0, DateTimeKind.Utc) }));
    }

    [Fact]
    public void Build_ParsesValuesOnce()
    {
        var parseCount = 0;
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<int>("int32", (obj, value) => obj.Int32Value == value, CountingParser);
        queryBuilder.AddRangeHandler<int>("int64", (obj, range) => range.IsInRange((int)obj.Int64Value), CountingParser);

        var query = queryBuilder.Build("int32:1 AND int64:1..10");
        var parseCountAfterBuild = parseCount;
        for (var i = 0; i < 10; i++)
        {
            Assert.True(query.Evaluate(new Sample { Int32Value = 1, Int64Value = 5 }));
        }

        Assert.NotEqual(0, parseCountAfterBuild);
        Assert.Equal(parseCountAfterBuild, parseCount);

        bool CountingParser(string value, out int result)
        {
            parseCount++;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }
    }

    [Fact]
    public void Build_IsNotAffectedByLaterHandlerChanges()
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.SetUnhandledPropertyHandler((obj, key, op, value) => true);
        queryBuilder.SetTextFilterHandler((obj, value) => true);
        var query = queryBuilder.Build("dummy:10 sample");

        queryBuilder.SetUnhandledPropertyHandler(predicate: null);
        queryBuilder.SetTextFilterHandler((obj, value) => false);

        Assert.True(query.Evaluate(new Sample()));
    }

    [Theory]
    [InlineData("dummy:10", "dummy:10")]
    [InlineData("dummy=10", "dummy:10")]
    [InlineData("dummy<>10", "dummy<>10")]
    [InlineData("dummy<10", "dummy<10")]
    [InlineData("dummy<=10", "dummy<=10")]
    [InlineData("dummy>10", "dummy>10")]
    [InlineData("dummy>=10", "dummy>=10")]
    public void UnhandledField_FallbackToTextSearch_KeepsOperator(string query, string expectedText)
    {
        string? text = null;
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.SetTextFilterHandler((obj, value) =>
        {
            text = value;
            return true;
        });

        queryBuilder.Build(query).Evaluate(new Sample());

        Assert.Equal(expectedText, text);
    }

    [Theory]
    [InlineData("sample", false)]
    [InlineData("NOT sample", true)]
    [InlineData("-sample", true)]
    [InlineData("NOT (int32:1 AND sample)", true)]
    public void TextWithoutTextHandler_OnlyItsNegationMatches(string query, bool expectedResult)
    {
        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.AddHandler<int>("int32", (obj, value) => obj.Int32Value == value);

        Assert.Equal(expectedResult, queryBuilder.Build(query).Evaluate(new Sample { Int32Value = 1 }));
    }

    [Fact]
    public void ManyDisjunctions_EvaluateOnSmallStack()
    {
        // 2^13 disjunctions, only the last of which matches. Chaining them as nested delegates overflowed a small stack.
        var query = string.Join(" AND ", Enumerable.Range(0, 13).Select(i => $"(a{i.ToString(CultureInfo.InvariantCulture)} OR sample)"));

        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.SetTextFilterHandler((obj, value) => obj.StringValue == value);
        var compiledQuery = queryBuilder.Build(query);

        var result = false;
        var thread = new Thread(() => result = compiledQuery.Evaluate(new Sample { StringValue = "sample" }), maxStackSize: 256 * 1024);
        thread.Start();
        thread.Join();

        Assert.True(result);
    }

    [Fact]
    public void ManyOrGroupsCombinedWithManyTerms_ThrowsQueryTooComplex()
    {
        // 2^13 disjunctions is within the limit, but each one repeats the 500 other terms
        var query = string.Join(" ", Enumerable.Range(0, 13).Select(i => $"(a{i.ToString(CultureInfo.InvariantCulture)} OR b{i.ToString(CultureInfo.InvariantCulture)})"))
            + " " + string.Join(' ', Enumerable.Range(0, 500).Select(i => "term" + i.ToString(CultureInfo.InvariantCulture)));

        var queryBuilder = new QueryBuilder<Sample>();
        queryBuilder.SetTextFilterHandler((obj, value) => true);

        Assert.Throws<QueryTooComplexException>(() => queryBuilder.Build(query));
    }

    private static QueryBuilder<Sample> CreateDateTimeOffsetRangeQueryBuilder(DateTimeOffset utcNow)
    {
        var timeProvider = new FakeTimeProvider();
        timeProvider.SetUtcNow(utcNow);

        var queryBuilder = new QueryBuilder<Sample>(timeProvider);
        queryBuilder.AddRangeHandler<DateTimeOffset>("date", (obj, range) => range.IsInRange(obj.DateTimeOffsetValue));
        return queryBuilder;
    }

    private static QueryBuilder<Sample> CreateDateOnlyRangeQueryBuilder(DateTimeOffset utcNow)
    {
        var timeProvider = new FakeTimeProvider();
        timeProvider.SetUtcNow(utcNow);

        var queryBuilder = new QueryBuilder<Sample>(timeProvider);
        queryBuilder.AddRangeHandler<DateOnly>("date", (obj, range) => range.IsInRange(obj.DateOnlyValue));
        return queryBuilder;
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
        public Int128 Int128Value { get; set; }
        public UInt128 UInt128Value { get; set; }
        public BigInteger BigIntegerValue { get; set; }
        public Half HalfValue { get; set; }
        public float SingleValue { get; set; }
        public double DoubleValue { get; set; }
        public decimal DecimalValue { get; set; }
        public DateOnly DateOnlyValue { get; set; }
        public DateTime DateTimeValue { get; set; }
        public DateTime? NullableDateTimeValue { get; set; }
        public DateTimeOffset DateTimeOffsetValue { get; set; }
        public DayOfWeek DayOfWeekValue { get; set; }
        public string? StringValue { get; set; }
    }
}
