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

    private sealed class Sample
    {
        public int Int32Value { get; set; }
        public long Int64Value { get; set; }
        public string? StringValue { get; set; }
    }
}
