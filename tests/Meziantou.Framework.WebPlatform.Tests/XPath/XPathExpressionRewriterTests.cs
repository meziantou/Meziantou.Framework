using Meziantou.Framework.WebPlatform.XPath;

namespace Meziantou.Framework.WebPlatform.Tests.XPath;

public sealed class XPathExpressionRewriterTests
{
    private const string H = XPathExpressionRewriter.HtmlPrefix + ":";
    private const string AttributeMatch = XPathExpressionRewriter.AttributeFunctionPrefix + ":" + XPathExpressionRewriter.AttributeFunctionName;

    [Theory]
    // Element name tests use the HTML namespace and are lowercased
    [InlineData("div", H + "div")]
    [InlineData("DIV", H + "div")]
    [InlineData("//Div/Span", "//" + H + "div/" + H + "span")]
    [InlineData("/html/body", "/" + H + "html/" + H + "body")]
    [InlineData("child::p", "child::" + H + "p")]
    [InlineData("descendant-or-self::P", "descendant-or-self::" + H + "p")]
    [InlineData("following-sibling :: p", "following-sibling :: " + H + "p")]
    [InlineData("ancestor::div[1]", "ancestor::" + H + "div[1]")]
    [InlineData("self::div", "self::" + H + "div")]
    [InlineData("a|b", H + "a|" + H + "b")]
    [InlineData("count(//li)", "count(//" + H + "li)")]
    [InlineData("concat(a, b)", "concat(" + H + "a, " + H + "b)")]
    [InlineData("//ul[li]", "//" + H + "ul[" + H + "li]")]
    [InlineData("//x-foo", "//" + H + "x-foo")]
    [InlineData("//a.b", "//" + H + "a.b")]
    [InlineData("//caf\U000000E9", "//" + H + "caf\U000000E9")]
    public void ElementNameTests(string expression, string expected) => Assert.Equal(expected, XPathExpressionRewriter.Rewrite(expression));

    [Theory]
    // Lowercase attribute names keep their meaning; others use the case-insensitive function
    [InlineData("@id", "@id")]
    [InlineData("attribute::id", "attribute::id")]
    [InlineData("@ID", "@*[" + AttributeMatch + "('ID')]")]
    [InlineData("attribute::viewBox", "attribute::*[" + AttributeMatch + "('viewBox')]")]
    [InlineData("attribute :: Title", "attribute :: *[" + AttributeMatch + "('Title')]")]
    [InlineData("//div[@Title='x']", "//" + H + "div[@*[" + AttributeMatch + "('Title')]='x']")]
    [InlineData("@*", "@*")]
    public void AttributeNameTests(string expression, string expected) => Assert.Equal(expected, XPathExpressionRewriter.Rewrite(expression));

    [Theory]
    // Prefixed name tests, node type tests, function names and namespace axis are not rewritten
    [InlineData("//svg:path")]
    [InlineData("//svg:*")]
    [InlineData("@xlink:href")]
    [InlineData("//*")]
    [InlineData("text()")]
    [InlineData("node()")]
    [InlineData("comment()")]
    [InlineData("processing-instruction('x')")]
    [InlineData("//text() | //comment()")]
    [InlineData("namespace::svg")]
    [InlineData("string-length('abc')")]
    [InlineData("normalize-space( 'abc' )")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../*")]
    [InlineData("1 + 2")]
    [InlineData("1.5 * .5")]
    [InlineData("$var")]
    [InlineData("$p:var")]
    public void UnchangedExpressions(string expression) => Assert.Equal(expression, XPathExpressionRewriter.Rewrite(expression));

    [Theory]
    // Literals are not rewritten
    [InlineData("'div'")]
    [InlineData("\"DIV\"")]
    [InlineData("concat('a b', \"c'd\")")]
    [InlineData("'unterminated")]
    public void Literals(string expression) => Assert.Equal(expression, XPathExpressionRewriter.Rewrite(expression));

    [Theory]
    // An NCName after an operand is an operator name; after an operator it is a name test
    [InlineData("div div div", H + "div div " + H + "div")]
    [InlineData("mod mod mod", H + "mod mod " + H + "mod")]
    [InlineData("and and or", H + "and and " + H + "or")]
    [InlineData("1 div 2", "1 div 2")]
    [InlineData("5 mod 2", "5 mod 2")]
    [InlineData("//a[@b and @c or @d]", "//" + H + "a[@b and @c or @d]")]
    [InlineData("//a[1] | //b", "//" + H + "a[1] | //" + H + "b")]
    [InlineData("count(a) div count(b)", "count(" + H + "a) div count(" + H + "b)")]
    [InlineData("(a)[1]", "(" + H + "a)[1]")]
    [InlineData("a/../b", H + "a/../" + H + "b")]
    [InlineData("a = b", H + "a = " + H + "b")]
    [InlineData("a != b", H + "a != " + H + "b")]
    [InlineData("a<=b", H + "a<=" + H + "b")]
    [InlineData("-a", "-" + H + "a")]
    public void OperatorNames(string expression, string expected) => Assert.Equal(expected, XPathExpressionRewriter.Rewrite(expression));

    [Theory]
    // '*' is a name test after an operator, and the multiply operator after an operand
    [InlineData("* * *")]
    [InlineData("2 * 3")]
    [InlineData("count(*) * 2")]
    [InlineData("@* | *")]
    public void Star(string expression) => Assert.Equal(expression, XPathExpressionRewriter.Rewrite(expression));

    [Fact]
    public void MultiplyFollowedByNameTest() => Assert.Equal("2 * " + H + "a", XPathExpressionRewriter.Rewrite("2 * a"));

    [Fact]
    public void WhitespaceIsPreserved() => Assert.Equal("\t//" + H + "a\r\n[ @b ]", XPathExpressionRewriter.Rewrite("\t//a\r\n[ @b ]"));

    [Fact]
    public void FunctionNameFollowedByWhitespace() => Assert.Equal("count (//" + H + "a)", XPathExpressionRewriter.Rewrite("count (//a)"));
}
