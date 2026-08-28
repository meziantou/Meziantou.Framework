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
    // RFC 9535: name-first = ALPHA / "_" / %x80-D7FF / %xE000-10FFFF, so supplementary-plane
    // code points are valid in a member-name shorthand.
    [InlineData("$.\U0001D11E")]        // U+1D11E MUSICAL SYMBOL G CLEF
    [InlineData("$.\U0001F600")]        // U+1F600 GRINNING FACE
    [InlineData("$.a\U0001D11E")]       // supplementary code point as a name-char
    [InlineData("$.\U0001D11Eb")]
    [InlineData("$.\U0010FFFF")]        // highest valid code point
    [InlineData("$.☺")]            // BMP, already worked
    [InlineData("$..\U0001D11E")]       // descendant segment
    public void TryParse_SupplementaryPlaneMemberNameShorthand_ReturnsTrue(string expression)
    {
        Assert.True(JsonPath.TryParse(expression, out _));
    }

    [Fact]
    public void TryParse_LoneSurrogateInMemberNameShorthand_ReturnsFalse()
    {
        // Built from char values rather than InlineData: xUnit serializes theory arguments, which
        // replaces a lone surrogate with U+FFFD — itself a valid name character, so the test would pass
        // for the wrong reason.
        const char High = '\uD834';
        const char Low = '\uDD1E';

        Assert.False(JsonPath.TryParse("$." + High, out _));            // lone high surrogate
        Assert.False(JsonPath.TryParse("$." + Low, out _));             // lone low surrogate
        Assert.False(JsonPath.TryParse("$." + Low + High, out _));      // reversed pair
        Assert.False(JsonPath.TryParse("$.a" + High, out _));           // lone high surrogate as a name-char
        Assert.False(JsonPath.TryParse("$." + High + "a", out _));      // high surrogate followed by a non-surrogate
    }

    [Fact]
    public void Evaluate_SupplementaryPlaneShorthand_MatchesTheBracketForm()
    {
        var doc = System.Text.Json.Nodes.JsonNode.Parse("""{"𝄞":1,"a𝄞":2}""");

        Assert.Equal(1, JsonPath.Parse("$.\U0001D11E").EvaluateValue(doc)!.GetValue<int>());
        Assert.Equal(1, JsonPath.Parse("$[\"\U0001D11E\"]").EvaluateValue(doc)!.GetValue<int>());
        Assert.Equal(2, JsonPath.Parse("$.a\U0001D11E").EvaluateValue(doc)!.GetValue<int>());
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
