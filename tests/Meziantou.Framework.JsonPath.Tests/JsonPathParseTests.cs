using Meziantou.Framework.Json;

namespace Meziantou.Framework.JsonPathTests;

public sealed class JsonPathParseTests
{
    [Theory]
    [InlineData("$")]
    [InlineData("$.a")]
    [InlineData("$.a.b.c")]
    [InlineData("$['a']")]
    [InlineData("$[\"a\"]")]
    [InlineData("$[0]")]
    [InlineData("$[-1]")]
    [InlineData("$[*]")]
    [InlineData("$.*")]
    [InlineData("$[0:3]")]
    [InlineData("$[0:3:2]")]
    [InlineData("$[::-1]")]
    [InlineData("$[?@.a]")]
    [InlineData("$[?@.a == 'b']")]
    [InlineData("$[?@.a > 1 && @.b < 10]")]
    [InlineData("$[?@.a > 1 || @.b < 10]")]
    [InlineData("$[?!@.a]")]
    [InlineData("$..a")]
    [InlineData("$..[*]")]
    [InlineData("$..*")]
    [InlineData("$[?length(@) > 2]")]
    [InlineData("$[?count(@.*) == 1]")]
    [InlineData("$[?match(@.a, 'foo')]")]
    [InlineData("$[?search(@.a, 'foo')]")]
    [InlineData("$[?value(@.a) == 1]")]
    public void Parse_ValidExpression_Succeeds(string expression)
    {
        var path = JsonPath.Parse(expression);
        Assert.NotNull(path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("$.")]
    [InlineData("$..")]
    [InlineData("$[")]
    [InlineData("$[]")]
    [InlineData(" $")]
    [InlineData("$ ")]
    [InlineData("$[?@.a = 1]")]
    [InlineData("$[0 2]")]
    [InlineData("$[,0]")]
    [InlineData("$[0,]")]
    public void Parse_InvalidExpression(string expression)
    {
        Assert.ThrowsAny<FormatException>(() => JsonPath.Parse(expression));
        Assert.False(JsonPath.TryParse(expression, out var result));
        Assert.Null(result);
    }

    [Fact]
    public void TryParse_NullExpression_ReturnsFalse()
    {
        Assert.False(JsonPath.TryParse((string?)null, out var result));
        Assert.Null(result);
    }

    [Fact]
    public void Parse_NullExpression_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => JsonPath.Parse((string)null!));
    }

    [Fact]
    public void ToString_ReturnsOriginalExpression()
    {
        var expression = "$.store.book[*].author";
        var path = JsonPath.Parse(expression);
        Assert.Equal(expression, path.ToString());
    }

    [Theory]
    [MemberData(nameof(DeeplyNestedExpressions), 5_000)]
    public void TryParse_DeeplyNestedExpression_ReturnsFalseInsteadOfOverflowingTheStack(string expression)
    {
        Assert.False(JsonPath.TryParse(expression, out var result));
        Assert.Null(result);
    }

    [Theory]
    [MemberData(nameof(DeeplyNestedExpressions), 8)]
    public void TryParse_ModeratelyNestedExpression_StaysWithinTheDepthLimit(string expression)
    {
        Assert.True(JsonPath.TryParse(expression, out var result));
        Assert.NotNull(result);
    }

    [Fact]
    public void Parse_ManyConjuncts_IsNotTreatedAsNesting()
    {
        // '&&' chains are parsed iteratively, so a flat chain must not consume nesting budget.
        var expression = "$[?" + string.Join(" && ", Enumerable.Range(0, 500).Select(i => $"@.a{i}")) + "]";
        Assert.True(JsonPath.TryParse(expression, out _));
    }

    public static TheoryData<string> DeeplyNestedExpressions(int depth)
    {
        return
        [
            // paren-expr recursion
            "$[?" + new string('(', depth) + "@.a" + new string(')', depth) + "]",
            // nested filter-selector recursion
            "$[?" + string.Concat(Enumerable.Repeat("@.a[?", depth)) + "@.b" + new string(']', depth) + "]",
            // nested function-argument recursion
            "$[?length(" + string.Concat(Enumerable.Repeat("length(", depth)) + "@" + new string(')', depth) + ")==1]",
        ];
    }
}
