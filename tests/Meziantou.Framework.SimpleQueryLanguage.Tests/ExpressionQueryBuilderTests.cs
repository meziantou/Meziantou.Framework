using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.SimpleQueryLanguage.Tests;

public sealed class ExpressionQueryBuilderTests
{
    [Fact]
    public void FieldEquals_Int32()
    {
        var queryBuilder = new ExpressionQueryBuilder<Sample>();
        queryBuilder.AddHandler("id", item => item.Int32Value);
        var query = queryBuilder.Build("id:10");

        var items = new[] { new Sample { Int32Value = 10 }, new Sample { Int32Value = 5 } }.AsQueryable();
        var result = query.Apply(items).ToList();

        Assert.Single(result);
        Assert.Equal(10, result[0].Int32Value);
    }

    [Fact]
    public void FieldEquals_GreaterThan()
    {
        var queryBuilder = new ExpressionQueryBuilder<Sample>();
        queryBuilder.AddHandler("id", item => item.Int32Value);
        var query = queryBuilder.Build("id>5");

        var items = new[] { new Sample { Int32Value = 10 }, new Sample { Int32Value = 5 }, new Sample { Int32Value = 3 } }.AsQueryable();
        var result = query.Apply(items).ToList();

        Assert.Single(result);
        Assert.Equal(10, result[0].Int32Value);
    }

    [Theory]
    [InlineData(-11, true)]
    [InlineData(-10, false)]
    [InlineData(-9, false)]
    public void FieldEquals_LessThan_NegativeValue(int value, bool expectedResult)
    {
        var queryBuilder = new ExpressionQueryBuilder<Sample>();
        queryBuilder.AddHandler("amount", item => item.Int32Value);
        var query = queryBuilder.Build("amount<-10");

        var items = new[] { new Sample { Int32Value = value } }.AsQueryable();
        var result = query.Apply(items).ToList();

        Assert.Equal(expectedResult, result.Count == 1);
    }

    [Fact]
    public void FieldEquals_Range()
    {
        var queryBuilder = new ExpressionQueryBuilder<Sample>();
        queryBuilder.AddHandler("id", item => item.Int32Value);
        var query = queryBuilder.Build("id:5..10");

        var items = new[] { new Sample { Int32Value = 3 }, new Sample { Int32Value = 5 }, new Sample { Int32Value = 7 }, new Sample { Int32Value = 10 }, new Sample { Int32Value = 12 } }.AsQueryable();
        var result = query.Apply(items).ToList();

        Assert.HasCount(3, result);
    }

    [Fact]
    public void OrQuery()
    {
        var queryBuilder = new ExpressionQueryBuilder<Sample>();
        queryBuilder.AddHandler("int32", item => item.Int32Value);
        queryBuilder.AddHandler("int64", item => item.Int64Value);
        var query = queryBuilder.Build("int32:1 OR int64:2");

        var items = new[]
        {
            new Sample { Int32Value = 1, Int64Value = 99 },
            new Sample { Int32Value = 99, Int64Value = 2 },
            new Sample { Int32Value = 99, Int64Value = 99 },
        }.AsQueryable();
        var result = query.Apply(items).ToList();

        Assert.HasCount(2, result);
    }

    [Fact]
    public void AndQuery()
    {
        var queryBuilder = new ExpressionQueryBuilder<Sample>();
        queryBuilder.AddHandler("int32", item => item.Int32Value);
        queryBuilder.AddHandler("int64", item => item.Int64Value);
        var query = queryBuilder.Build("int32:1 AND int64:2");

        var items = new[]
        {
            new Sample { Int32Value = 1, Int64Value = 2 },
            new Sample { Int32Value = 1, Int64Value = 99 },
            new Sample { Int32Value = 99, Int64Value = 2 },
            new Sample { Int32Value = 99, Int64Value = 99 },
        }.AsQueryable();
        var result = query.Apply(items).ToList();

        Assert.Single(result);
    }

    [Fact]
    public void Not()
    {
        var queryBuilder = new ExpressionQueryBuilder<Sample>();
        queryBuilder.AddHandler("int32", item => item.Int32Value);
        var query = queryBuilder.Build("-int32:1");

        var items = new[]
        {
            new Sample { Int32Value = 1 },
            new Sample { Int32Value = 2 },
        }.AsQueryable();
        var result = query.Apply(items).ToList();

        Assert.Single(result);
        Assert.Equal(2, result[0].Int32Value);
    }

    [Fact]
    public void FreeTextHandler()
    {
        var queryBuilder = new ExpressionQueryBuilder<Sample>();
        queryBuilder.SetFreeTextHandler(value => item => item.StringValue != null && item.StringValue.Contains(value, StringComparison.OrdinalIgnoreCase));
        var query = queryBuilder.Build("hello");

        var items = new[]
        {
            new Sample { StringValue = "Hello World" },
            new Sample { StringValue = "Goodbye World" },
        }.AsQueryable();
        var result = query.Apply(items).ToList();

        Assert.Single(result);
        Assert.Equal("Hello World", result[0].StringValue);
    }

    [Fact]
    public void EmptyQuery_ReturnsAll()
    {
        var queryBuilder = new ExpressionQueryBuilder<Sample>();
        queryBuilder.AddHandler("id", item => item.Int32Value);
        var query = queryBuilder.Build("");

        var items = new[] { new Sample { Int32Value = 1 }, new Sample { Int32Value = 2 } }.AsQueryable();
        var result = query.Apply(items).ToList();

        Assert.HasCount(2, result);
    }

    [Fact]
    public void ExpressionQuery_Predicate_IsAccessible()
    {
        var queryBuilder = new ExpressionQueryBuilder<Sample>();
        queryBuilder.AddHandler("id", item => item.Int32Value);
        var query = queryBuilder.Build("id:10");

        Assert.NotNull(query.Predicate);
        Assert.Equal("id:10", query.Text);
    }

    [Fact]
    public void EmptyQuery_Predicate_IsNull()
    {
        var queryBuilder = new ExpressionQueryBuilder<Sample>();
        queryBuilder.AddHandler("id", item => item.Int32Value);
        var query = queryBuilder.Build("");

        Assert.Null(query.Predicate);
    }

    [Fact]
    public void Build_NullQuery_ThrowsArgumentNullException()
    {
        var queryBuilder = new ExpressionQueryBuilder<Sample>();

        var exception = Assert.Throws<ArgumentNullException>(() => queryBuilder.Build(query: null!));
        Assert.Equal("query", exception.ParamName);
    }

    [Fact]
    public void StringHandler_MatchesSubstring()
    {
        var queryBuilder = new ExpressionQueryBuilder<Sample>();
        queryBuilder.AddHandler("name", item => item.StringValue);
        var query = queryBuilder.Build("name:John");

        var items = new[]
        {
            new Sample { StringValue = "John Doe" },
            new Sample { StringValue = "Jane Doe" },
        }.AsQueryable();
        var result = query.Apply(items).ToList();

        Assert.Single(result);
        Assert.Equal("John Doe", result[0].StringValue);
    }

    [Fact]
    public void StringHandler_NullProperty_DoesNotThrow()
    {
        var queryBuilder = new ExpressionQueryBuilder<Sample>();
        queryBuilder.AddHandler("name", item => item.StringValue);
        var query = queryBuilder.Build("name:John");

        var items = new[]
        {
            new Sample { StringValue = null },
            new Sample { StringValue = "John Doe" },
        }.AsQueryable();
        var result = query.Apply(items).ToList();

        Assert.Single(result);
        Assert.Equal("John Doe", result[0].StringValue);
    }

    [Fact]
    public void StringHandler_DoesNotEmitStringComparison()
    {
        var queryBuilder = new ExpressionQueryBuilder<Sample>();
        queryBuilder.AddHandler("name", item => item.StringValue);
        var query = queryBuilder.Build("name:John");

        // string.Contains(string, StringComparison) is not translatable by Entity Framework Core
        Assert.DoesNotContain(nameof(StringComparison.OrdinalIgnoreCase), query.Predicate!.ToString());
    }

    [Fact]
    public void StringHandler_ExplicitComparisonType_IgnoresCase()
    {
        var queryBuilder = new ExpressionQueryBuilder<Sample>();
        queryBuilder.AddHandler("name", item => item.StringValue, StringComparison.OrdinalIgnoreCase);
        var query = queryBuilder.Build("name:john");

        var items = new[]
        {
            new Sample { StringValue = "John Doe" },
            new Sample { StringValue = null },
        }.AsQueryable();
        var result = query.Apply(items).ToList();

        Assert.Single(result);
        Assert.Equal("John Doe", result[0].StringValue);
    }

    [Fact]
    public void DateKeyword_Today_UsesTimeProvider()
    {
        var query = CreateDateQueryBuilder(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero)).Build("date:today");

        var items = new[]
        {
            new Sample { DateTimeValue = new DateTime(2026, 3, 15, 8, 0, 0, DateTimeKind.Utc) },
            new Sample { DateTimeValue = new DateTime(2026, 3, 14, 8, 0, 0, DateTimeKind.Utc) },
            new Sample { DateTimeValue = new DateTime(2026, 3, 16, 8, 0, 0, DateTimeKind.Utc) },
        }.AsQueryable();
        var result = query.Apply(items).ToList();

        Assert.Single(result);
        Assert.Equal(new DateTime(2026, 3, 15, 8, 0, 0, DateTimeKind.Utc), result[0].DateTimeValue);
    }

    [Fact]
    public void DateKeyword_ThisMonth_UsesTimeProvider()
    {
        var query = CreateDateQueryBuilder(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero)).Build("date:\"this month\"");

        var items = new[]
        {
            new Sample { DateTimeValue = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Sample { DateTimeValue = new DateTime(2026, 3, 31, 23, 0, 0, DateTimeKind.Utc) },
            new Sample { DateTimeValue = new DateTime(2026, 2, 28, 8, 0, 0, DateTimeKind.Utc) },
            new Sample { DateTimeValue = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc) },
        }.AsQueryable();
        var result = query.Apply(items).ToList();

        Assert.HasCount(2, result);
    }

    [Theory]
    [InlineData("id:5..*", 5, 7, 10, 12)]
    [InlineData("id:*..5", 3, 5)]
    [InlineData("id<>5..*", 3)]
    public void FieldEquals_UnboundedRange(string query, params int[] expectedValues)
    {
        var queryBuilder = new ExpressionQueryBuilder<Sample>();
        queryBuilder.AddHandler("id", item => item.Int32Value);

        var items = new[] { 3, 5, 7, 10, 12 }.Select(value => new Sample { Int32Value = value }).AsQueryable();

        Assert.Equal(expectedValues, queryBuilder.Build(query).Apply(items).Select(item => item.Int32Value));
    }

    [Theory]
    [InlineData("date>@today-1w", "2026-03-14")]
    [InlineData("date:@today-1w..*", "2026-03-14")]
    [InlineData("date<@today-1w", "2026-03-01")]
    [InlineData("date:@today-2w..@today", "2026-03-01", "2026-03-14")]
    [InlineData("date:*..@today-1d", "2026-03-01", "2026-03-14")]
    public void RelativeDate_IsSupported(string query, params string[] expectedDates)
    {
        var items = new[]
        {
            new Sample { DateTimeValue = new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc) },
            new Sample { DateTimeValue = new DateTime(2026, 3, 14, 0, 0, 0, DateTimeKind.Utc) },
        }.AsQueryable();

        var result = CreateDateQueryBuilder(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero)).Build(query).Apply(items);

        Assert.Equal(expectedDates, result.Select(item => item.DateTimeValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData("date:last_month", "2026-02-20")]
    [InlineData("date:\"last month\"", "2026-02-20")]
    [InlineData("date:this_year", "2026-02-20", "2026-03-15")]
    [InlineData("date:last_year", "2025-12-31")]
    public void DateKeyword_UnderscoreForm_IsSupported(string query, params string[] expectedDates)
    {
        var items = new[]
        {
            new Sample { DateTimeValue = new DateTime(2025, 12, 31, 8, 0, 0, DateTimeKind.Utc) },
            new Sample { DateTimeValue = new DateTime(2026, 2, 20, 8, 0, 0, DateTimeKind.Utc) },
            new Sample { DateTimeValue = new DateTime(2026, 3, 15, 8, 0, 0, DateTimeKind.Utc) },
        }.AsQueryable();

        var result = CreateDateQueryBuilder(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero)).Build(query).Apply(items);

        Assert.Equal(expectedDates, result.Select(item => item.DateTimeValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData("id:10", 1)]
    [InlineData("id>5", 1)]
    [InlineData("id<5", 1)]
    [InlineData("id>=10", 1)]
    [InlineData("id:5..15", 1)]
    [InlineData("id:5..*", 1)]
    [InlineData("id:*..5", 1)]
    [InlineData("id:*..*", 0)]
    public void NullableInt32(string query, int expectedCount)
    {
        var queryBuilder = new ExpressionQueryBuilder<Sample>();
        queryBuilder.AddHandler<int?>("id", item => item.NullableInt32Value);

        var items = new[]
        {
            new Sample { NullableInt32Value = 10 },
            new Sample { NullableInt32Value = 3 },
            new Sample { NullableInt32Value = null },
        }.AsQueryable();

        Assert.HasCount(expectedCount, queryBuilder.Build(query).Apply(items).ToList());
    }

    [Fact]
    public void TimeSpan_SupportsComparisonOperators()
    {
        var queryBuilder = new ExpressionQueryBuilder<Sample>();
        queryBuilder.AddHandler<TimeSpan>("duration", item => item.TimeSpanValue);
        var query = queryBuilder.Build("duration>00:05:00");

        var items = new[]
        {
            new Sample { TimeSpanValue = TimeSpan.FromMinutes(10) },
            new Sample { TimeSpanValue = TimeSpan.FromMinutes(1) },
        }.AsQueryable();
        var result = query.Apply(items).ToList();

        Assert.Single(result);
        Assert.Equal(TimeSpan.FromMinutes(10), result[0].TimeSpanValue);
    }

    [Fact]
    public void DateTime_SupportsComparisonOperators()
    {
        var queryBuilder = new ExpressionQueryBuilder<Sample>();
        queryBuilder.AddHandler<DateTime>("date", item => item.DateTimeValue);
        var query = queryBuilder.Build("date>2026-03-01");

        var items = new[]
        {
            new Sample { DateTimeValue = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc) },
            new Sample { DateTimeValue = new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc) },
        }.AsQueryable();
        var result = query.Apply(items).ToList();

        Assert.Single(result);
        Assert.Equal(new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc), result[0].DateTimeValue);
    }

    [Fact]
    public void Enum_RegistersEqualityOnly()
    {
        // Expression.LessThan is not defined for enum types, so registering the handler must not throw
        var queryBuilder = new ExpressionQueryBuilder<Sample>();
        queryBuilder.AddHandler<DayOfWeek>("day", item => item.DayOfWeekValue);

        var items = new[]
        {
            new Sample { DayOfWeekValue = DayOfWeek.Friday },
            new Sample { DayOfWeekValue = DayOfWeek.Monday },
        }.AsQueryable();
        var result = queryBuilder.Build("day:friday").Apply(items).ToList();

        Assert.Single(result);
        Assert.Equal(DayOfWeek.Friday, result[0].DayOfWeekValue);
    }

    [Fact]
    public void UnorderableType_RegistersEqualityOnly()
    {
        var id = Guid.NewGuid();

        // Guid cannot be ordered with <, so registering the handler must not throw
        var queryBuilder = new ExpressionQueryBuilder<Sample>();
        queryBuilder.AddHandler<Guid>("guid", item => item.GuidValue);

        var items = new[]
        {
            new Sample { GuidValue = id },
            new Sample { GuidValue = Guid.NewGuid() },
        }.AsQueryable();

        Assert.Single(queryBuilder.Build($"guid:{id}").Apply(items).ToList());
    }

    [Fact]
    public void ManyDisjunctions_BuildsAndApplies()
    {
        // 2^12 disjunctions, only the last of which matches. Combining them pairwise took seconds.
        var query = string.Join(" AND ", Enumerable.Range(0, 12).Select(i => $"(a{i.ToString(CultureInfo.InvariantCulture)} OR sample)"));

        var queryBuilder = new ExpressionQueryBuilder<Sample>();
        queryBuilder.SetFreeTextHandler(value => item => item.StringValue == value);

        var items = new[]
        {
            new Sample { StringValue = "sample" },
            new Sample { StringValue = "a0" },
        }.AsQueryable();
        var result = queryBuilder.Build(query).Apply(items).ToList();

        Assert.Single(result);
        Assert.Equal("sample", result[0].StringValue);
    }

    [Fact]
    public void CombinedTerms_ShareTheLambdaParameter()
    {
        var queryBuilder = new ExpressionQueryBuilder<Sample>();
        queryBuilder.AddHandler("int32", item => item.Int32Value);
        queryBuilder.AddHandler("int64", other => other.Int64Value);
        queryBuilder.SetFreeTextHandler(value => text => text.StringValue == value);
        var query = queryBuilder.Build("(int32:1 OR sample) AND NOT int64:2");

        var items = new[]
        {
            new Sample { Int32Value = 1, Int64Value = 1 },
            new Sample { Int32Value = 2, Int64Value = 1, StringValue = "sample" },
            new Sample { Int32Value = 1, Int64Value = 2 },
            new Sample { Int32Value = 2, Int64Value = 1 },
        }.AsQueryable();
        var result = query.Apply(items).ToList();

        Assert.HasCount(2, result);
        Assert.Single(query.Predicate!.Parameters);
    }

    [Theory]
    [InlineData("today", 1)]
    [InlineData("yesterday", 0)]
    public void DateKeyword_OnNullableDateTime_IsSupported(string keyword, int expectedCount)
    {
        var timeProvider = new FakeTimeProvider();
        timeProvider.SetUtcNow(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero));

        var queryBuilder = new ExpressionQueryBuilder<Sample>(timeProvider);
        queryBuilder.AddHandler<DateTime?>("date", item => item.NullableDateTimeValue);

        var items = new[]
        {
            new Sample { NullableDateTimeValue = new DateTime(2026, 3, 15, 8, 0, 0, DateTimeKind.Utc) },
            new Sample { NullableDateTimeValue = null },
        }.AsQueryable();

        Assert.HasCount(expectedCount, queryBuilder.Build($"date:{keyword}").Apply(items).ToList());
    }

    [Theory]
    [InlineData("state:or", 1)]
    [InlineData("state:and", 0)]
    public void KeywordDirectlyAfterOperator_IsTheValue(string query, int expectedCount)
    {
        var queryBuilder = new ExpressionQueryBuilder<Sample>();
        queryBuilder.AddHandler("state", item => item.StringValue);
        queryBuilder.SetFreeTextHandler(value => item => true);

        var items = new[]
        {
            new Sample { StringValue = "oregon" },
            new Sample { StringValue = "idaho" },
        }.AsQueryable();

        Assert.HasCount(expectedCount, queryBuilder.Build(query).Apply(items).ToList());
    }

    [Fact]
    public void UnhandledField_FallbackToTextSearch_KeepsOperator()
    {
        var queryBuilder = new ExpressionQueryBuilder<Sample>();
        queryBuilder.SetFreeTextHandler(value => item => item.StringValue == value);

        var items = new[]
        {
            new Sample { StringValue = "dummy>10" },
            new Sample { StringValue = "dummy:10" },
        }.AsQueryable();
        var result = queryBuilder.Build("dummy>10").Apply(items).ToList();

        Assert.Single(result);
        Assert.Equal("dummy>10", result[0].StringValue);
    }

    [Theory]
    [InlineData("sample", 0)]
    [InlineData("NOT sample", 2)]
    public void TextWithoutTextHandler_OnlyItsNegationMatches(string query, int expectedCount)
    {
        var queryBuilder = new ExpressionQueryBuilder<Sample>();
        queryBuilder.AddHandler("int32", item => item.Int32Value);

        var items = new[] { new Sample { Int32Value = 1 }, new Sample { Int32Value = 2 } }.AsQueryable();

        Assert.HasCount(expectedCount, queryBuilder.Build(query).Apply(items).ToList());
    }

    private static ExpressionQueryBuilder<Sample> CreateDateQueryBuilder(DateTimeOffset utcNow)
    {
        var timeProvider = new FakeTimeProvider();
        timeProvider.SetUtcNow(utcNow);

        var queryBuilder = new ExpressionQueryBuilder<Sample>(timeProvider);
        queryBuilder.AddHandler<DateTime>("date", item => item.DateTimeValue);
        return queryBuilder;
    }

    private sealed class Sample
    {
        public int Int32Value { get; set; }
        public long Int64Value { get; set; }
        public string? StringValue { get; set; }
        public int? NullableInt32Value { get; set; }
        public TimeSpan TimeSpanValue { get; set; }
        public DateTime DateTimeValue { get; set; }
        public DateTime? NullableDateTimeValue { get; set; }
        public DayOfWeek DayOfWeekValue { get; set; }
        public Guid GuidValue { get; set; }
    }
}
