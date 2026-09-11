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
    [InlineData("$ .a [0]")]
    [InlineData("$[?@.true == @.null]")]
    [InlineData("$[?@.a == -0]")]
    [InlineData("$[?@.a == -0.0e-0]")]
    [InlineData("$[?@.a == 1E+2]")]
    [InlineData("$[9007199254740991]")]
    [InlineData("$[-9007199254740991:9007199254740991:-9007199254740991]")]
    [InlineData("$[?@.a == 9007199254740992]")] // The I-JSON range binds indexes and slices, not number literals
    [InlineData("$[?length(length(@)) == 1]")]
    [InlineData("$[?length(value(@..a)) == 1]")]
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
    [InlineData("$[-0:]")] // int leaves out -0, in a slice as in an index
    [InlineData("$[::-0]")]
    [InlineData("$[?@[-0] == 1]")]
    [InlineData("$[1:9007199254740992]")] // Outside the I-JSON range
    [InlineData("$[?@[9007199254740992] == 1]")]
    [InlineData("$[?foo(@)]")] // Unknown function
    [InlineData("$[?Length(@) == 1]")] // Function names are lower case
    [InlineData("$[?length(match(@.a, 'x')) == 1]")] // A LogicalType result is no ValueType argument
    [InlineData("$[?count(value(@.a)) == 1]")] // A ValueType result is no NodesType argument
    [InlineData("$[?match(@.*, 'a')]")] // A ValueType argument is a literal, a singular query or a function
    [InlineData("$[?length(@.a == 1) == 1]")]
    [InlineData("$[?(@.a) == 1]")] // A parenthesized expression is not a comparable
    [InlineData("$[?@.a == 1 == 2]")]
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
    public void TryParse_LoneSurrogateInStringLiteral_ReturnsFalse()
    {
        // 'unescaped' skips the surrogate code points just as name-first does. Built from char values for the
        // same reason as the member-name shorthand test above.
        const char High = '\uD834';
        const char Low = '\uDD1E';

        Assert.False(JsonPath.TryParse("$['" + High + "']", out _));           // lone high surrogate
        Assert.False(JsonPath.TryParse("$[\"" + Low + "\"]", out _));         // lone low surrogate
        Assert.False(JsonPath.TryParse("$['" + Low + High + "']", out _));     // reversed pair
        Assert.False(JsonPath.TryParse("$['" + High + "a']", out _));          // high surrogate followed by a non-surrogate
        Assert.False(JsonPath.TryParse("$['" + High, out _));                  // high surrogate at the end of the input
        Assert.False(JsonPath.TryParse("$[?@ == 'a" + Low + "']", out _));     // in a comparison literal
        Assert.False(JsonPath.TryParse("$[?match(@, '" + High + "')]", out _)); // in a function argument
        Assert.True(JsonPath.TryParse("$['" + High + Low + "']", out _));
    }

    [Theory]
    // RFC 9535 §2.3.5.1: singular-query-segments = *(S (name-segment / index-segment)), with
    // name-segment = ("[" name-selector "]") / ("." member-name-shorthand) and index-segment = "[" index-selector "]".
    // Blank space may separate the segments, but not appear inside one: with it the query is still well-formed,
    // just no longer singular, and only a singular query can be compared or passed as a ValueType argument.
    [InlineData("$[?@[ 'a' ] == 1]")]
    [InlineData("$[?@['a' ] == 1]")]
    [InlineData("$[?@[ 'a'] == 1]")]
    [InlineData("$[?@[ 0 ] == 1]")]
    [InlineData("$[?@[0\n] == 1]")]
    [InlineData("$[?@.a[ 0 ] == 1]")]
    [InlineData("$[?$[ 'a' ] == 1]")]
    [InlineData("$[?1 == @[ 0 ]]")]
    [InlineData("$[?@.b == @['a' ]]")]
    [InlineData("$[?length(@[ 'a' ]) == 1]")]
    [InlineData("$[?match(@[ 0 ], 'a')]")]
    public void Parse_BlankSpaceInsideTheBracketsOfASingularQuery_IsRejected(string expression)
    {
        var exception = Assert.Throws<FormatException>(() => JsonPath.Parse(expression));
        Assert.Contains("no blank space inside their brackets", exception.Message);
        Assert.False(JsonPath.TryParse(expression, out _));
    }

    [Theory]
    // A member-name shorthand has to follow "." directly, in a singular query as in any other segment.
    [InlineData("$[?@. a == 1]")]
    [InlineData("$[?@.\ta == 1]")]
    [InlineData("$[?@.a. b == 1]")]
    [InlineData("$[?1 == @. a]")]
    [InlineData("$[?1 == $. a]")]
    [InlineData("$[?length(@. a) == 1]")]
    public void Parse_BlankSpaceAfterTheDotOfASingularQuery_IsRejected(string expression)
    {
        Assert.Throws<FormatException>(() => JsonPath.Parse(expression));
        Assert.False(JsonPath.TryParse(expression, out _));
    }

    [Theory]
    // The places the grammar does leave room for blank space.
    [InlineData("$[?@ ['a'] == 1]")]
    [InlineData("$[?@\n[0] == 1]")]
    [InlineData("$[?@ .a == 1]")]
    [InlineData("$[?@.a [0] == 1]")]
    [InlineData("$[?1 == @ .a]")]
    [InlineData("$[?length( @['a'] ) == 1]")]
    [InlineData("$[?@[ 'a' ]]")] // An existence test takes any query
    [InlineData("$[?count(@[ 'a' ]) == 1]")] // So does a NodesType argument
    [InlineData("$[?@[ 'a' ][ 0 ]]")]
    public void Parse_BlankSpaceAroundTheSegmentsOfASingularQuery_IsAccepted(string expression)
    {
        Assert.True(JsonPath.TryParse(expression, out _));
    }

    [Fact]
    public void Evaluate_SingularQueryWithBlankSpaceBetweenSegments_ComparesItsValue()
    {
        var doc = System.Text.Json.Nodes.JsonNode.Parse("""[{"a": [1]}, {"a": [2]}]""");

        var result = JsonPath.Parse("$[?@ .a\n[0] == 2]").Evaluate(doc);

        Assert.Single(result);
        Assert.Equal("$[1]", result[0].Path);
    }

    [Theory]
    // logical-not-op only prefixes a paren-expr or a test-expr (RFC 9535 §2.3.5.1). "!@.a == 1" does not mean
    // "!(@.a == 1)" - no reading of it is well-formed.
    [InlineData("$[?!@.a == 1]")]
    [InlineData("$[?! @.a == 1]")]
    [InlineData("$[?!@.a==1 && @.b]")]
    [InlineData("$[?@.b || !@.a < 1]")]
    [InlineData("$[?!$.a != 1]")]
    [InlineData("$[?!1 == 1]")]
    [InlineData("$[?!'a' == @.a]")]
    [InlineData("$[?!null == @.a]")]
    [InlineData("$[?!length(@.a) == 1]")]
    [InlineData("$[?!value(@.a) >= 1]")]
    [InlineData("$[?(!@.a == 1)]")]
    public void Parse_NegatedComparison_IsRejected(string expression)
    {
        var exception = Assert.Throws<FormatException>(() => JsonPath.Parse(expression));
        Assert.Contains("parentheses", exception.Message);
        Assert.False(JsonPath.TryParse(expression, out _));
    }

    [Theory]
    [InlineData("$[?!(@.a == 1)]")]
    [InlineData("$[?! (@.a == 1)]")]
    [InlineData("$[?!(!(@.a == 1))]")]
    [InlineData("$[?!@.a]")]
    [InlineData("$[?! @.a]")]
    [InlineData("$[?!\n@.a]")]
    [InlineData("$[?!$.a]")]
    [InlineData("$[?!@.a && @.b == 1]")]
    [InlineData("$[?!match(@.a, 'a')]")]
    [InlineData("$[?!search(@.a, 'a') || @.b]")]
    public void Parse_NegatedTestOrParenthesizedExpression_IsAccepted(string expression)
    {
        Assert.True(JsonPath.TryParse(expression, out _));
    }

    [Fact]
    public void Evaluate_NegatedParenthesizedComparison_SelectsTheOtherNodes()
    {
        var doc = System.Text.Json.Nodes.JsonNode.Parse("""[{"a": 1}, {"a": 2}, {}]""");

        var result = JsonPath.Parse("$[?!(@.a == 1)]").Evaluate(doc);

        Assert.Equal(["$[1]", "$[2]"], result.Select(match => match.Path));
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
