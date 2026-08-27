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

    [Theory]
    [InlineData("id:10", 1)]
    [InlineData("id>5", 1)]
    [InlineData("id<5", 1)]
    [InlineData("id>=10", 1)]
    [InlineData("id:5..15", 1)]
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

    private sealed class Sample
    {
        public int Int32Value { get; set; }
        public long Int64Value { get; set; }
        public string? StringValue { get; set; }
        public int? NullableInt32Value { get; set; }
        public TimeSpan TimeSpanValue { get; set; }
        public DateTime DateTimeValue { get; set; }
        public DayOfWeek DayOfWeekValue { get; set; }
        public Guid GuidValue { get; set; }
    }
}
