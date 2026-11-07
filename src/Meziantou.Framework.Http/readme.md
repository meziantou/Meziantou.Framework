# Meziantou.Framework.Http

Parse [RFC 8288](https://datatracker.ietf.org/doc/html/rfc8288) `Link` header values.

<!-- ref:../../tests/Meziantou.Framework.Http.Tests/LinkHeaderValueTests.cs;region=LinkHeaderValue_Parse;format=md-fenced-code-block;dedent=true -->
```csharp
var links = LinkHeaderValue.Parse("</style.css>; rel=preload; as=style; fetchpriority=\"high\"");
var link = links.GetLink("preload");
Assert.Equal("style", link?.GetParameterValue("as"));
```
<!-- endref -->

Enumerate the `Link` headers of a response:

<!-- ref:../../tests/Meziantou.Framework.Http.Tests/LinkHeaderValueTests.cs;region=LinkHeaderValue_EnumerateLinkHeaders;format=md-fenced-code-block;dedent=true -->
```csharp
using var response = new HttpResponseMessage();
response.Headers.Add("Link", "</items?page=2>; rel=next, </items?page=5>; rel=last");

var links = new List<string>();
foreach (var item in response.Headers.EnumerateLinkHeaders())
{
    links.Add($"{item.Rel}: {item.Url}");
}

Assert.Equal(["next: /items?page=2", "last: /items?page=5"], links);
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.Http.Tests/LinkHeaderValueTests.cs;region=LinkHeaderValue_GetLinkUrl;format=md-fenced-code-block;dedent=true -->
```csharp
// Follow pagination links
var nextUrl = response.Headers.EnumerateLinkHeaders().GetLinkUrl("next");
Assert.Equal("/items?page=2", nextUrl);
```
<!-- endref -->
