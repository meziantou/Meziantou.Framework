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
    // RFC 9535 requires exactly 4 HEXDIG in a \u escape; whitespace is not a hex digit.
    [InlineData("$[\"\\u 041\"]")]
    [InlineData("$[\"\\u041 \"]")]
    [InlineData("$['\\u 041']")]
    [InlineData("$[\"\\u\t041\"]")]
    [InlineData("$[\"\\u04 1\"]")]
    public void TryParse_UnicodeEscapeWithWhitespaceInHexDigits_ReturnsFalse(string expression)
    {
        Assert.False(JsonPath.TryParse(expression, out var result));
        Assert.Null(result);
    }

    [Theory]
    [InlineData("$[\"\\u0041\"]")]
    [InlineData("$[\"\\u00e9\"]")]
    [InlineData("$[\"\\u00E9\"]")]
    [InlineData("$[\"\\uD834\\uDD1E\"]")]
    public void TryParse_WellFormedUnicodeEscape_ReturnsTrue(string expression)
    {
        Assert.True(JsonPath.TryParse(expression, out _));
    }

    [Fact]
    public void Parse_UnicodeEscape_IsCaseInsensitiveAndDecodesToTheSameName()
    {
        var doc = System.Text.Json.Nodes.JsonNode.Parse("""{"é":1}""");
        Assert.Equal(1, JsonPath.Parse("$[\"\\u00e9\"]").EvaluateValue(doc)!.GetValue<int>());
        Assert.Equal(1, JsonPath.Parse("$[\"\\u00E9\"]").EvaluateValue(doc)!.GetValue<int>());
    }
}
