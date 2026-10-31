---
title: Link headers
description: Parse RFC 8288 Link header values, find a link by relation type, read its parameters and follow pagination links.
---

A `Link` header (RFC 8288) lists links related to a response: the next page of a collection, a resource to preload,
the canonical URL of a document. [LinkHeaderValue](cref:Meziantou.Framework.Http.LinkHeaderValue), in the `Meziantou.Framework.Http` package, parses it.

<InstallNuGetPackage Id="Meziantou.Framework.Http" />

## Read the links of a response

[EnumerateLinkHeaders](cref:Meziantou.Framework.Http.LinkHeaderValueExtensions.EnumerateLinkHeaders) parses every `Link` header of a response, or of any `HttpHeaders`, and returns one
[LinkHeaderValue](cref:Meziantou.Framework.Http.LinkHeaderValue) per link:

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

[LinkHeaderValue.Parse](cref:Meziantou.Framework.Http.LinkHeaderValue.Parse) does the same from an `HttpResponseMessage`, an `HttpHeaders`, a `string` or a
`ReadOnlySpan<char>`. A header can hold several links separated by commas, and a response can have several `Link`
headers: the links of all of them are returned, in order.

## Find a link by relation type

[GetLink](cref:Meziantou.Framework.Http.LinkHeaderValueExtensions.GetLink) returns the first link of a relation type, and [GetLinkUrl](cref:Meziantou.Framework.Http.LinkHeaderValueExtensions.GetLinkUrl) its URL, or `null` when there is none:

<!-- ref:../../tests/Meziantou.Framework.Http.Tests/LinkHeaderValueTests.cs;region=LinkHeaderValue_GetLinkUrl;format=md-fenced-code-block;dedent=true -->
```csharp
// Follow pagination links
var nextUrl = response.Headers.EnumerateLinkHeaders().GetLinkUrl("next");
Assert.Equal("/items?page=2", nextUrl);
```
<!-- endref -->

The relation type is compared case-insensitively. A link can declare several relation types separated by spaces
(`rel="next last"`): it matches each of them, but not a part of one.

## Resolve the URL of a link

[Url](cref:Meziantou.Framework.Http.LinkHeaderValue.Url) is the target as written between `<` and `>`. It is often relative: resolve it against the URI of the request
before following it:

<!-- ref:../../tests/Meziantou.Framework.Http.Tests/LinkHeaderValueTests.cs;region=LinkHeaderValue_ResolveRelativeUrl;format=md-fenced-code-block;dedent=true -->
```csharp
using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/items?page=1");
using var response = new HttpResponseMessage { RequestMessage = request };
response.Headers.Add("Link", "</items?page=2>; rel=next");

// Url is the target as written in the header. Resolve a relative target against the request URI.
var next = response.Headers.EnumerateLinkHeaders().GetLinkUrl("next");
Assert.NotNull(next);
var nextUri = new Uri(response.RequestMessage.RequestUri!, next);
Assert.Equal(new Uri("https://api.example.com/items?page=2"), nextUri);
```
<!-- endref -->

## Read the parameters of a link

[Parameters](cref:Meziantou.Framework.Http.LinkHeaderValue.Parameters) lists the parameters of a link in order, and [GetParameterValue](cref:Meziantou.Framework.Http.LinkHeaderValue.GetParameterValue) returns the value of one, or `null` when
the link does not have it:

<!-- ref:../../tests/Meziantou.Framework.Http.Tests/LinkHeaderValueTests.cs;region=LinkHeaderValue_Parse;format=md-fenced-code-block;dedent=true -->
```csharp
var links = LinkHeaderValue.Parse("</style.css>; rel=preload; as=style; fetchpriority=\"high\"");
var link = links.GetLink("preload");
Assert.Equal("style", link?.GetParameterValue("as"));
```
<!-- endref -->

- Parameter names are lowercased, and [GetParameterValue](cref:Meziantou.Framework.Http.LinkHeaderValue.GetParameterValue) compares them case-insensitively.
- A quoted value is unquoted and unescaped (`\"` becomes `"`). An unquoted value is trimmed.
- A parameter without a value (`; crossorigin`) has an empty value.
- [Rel](cref:Meziantou.Framework.Http.LinkHeaderValue.Rel) is the value of the `rel` parameter, or an empty string when there is none.

<!-- ref:../../tests/Meziantou.Framework.Http.Tests/LinkHeaderValueTests.cs;region=LinkHeaderValue_Parameters;format=md-fenced-code-block;dedent=true -->
```csharp
var links = LinkHeaderValue.Parse("<https://example.com/chapter2>; REL=\"next last\"; title=\"Chapter \\\"2\\\"\"; hreflang=en");
var link = Assert.Single(links);

// The names of the parameters are lowercased, and quoted values are unescaped
Assert.Equal(["rel", "title", "hreflang"], link.Parameters.Select(parameter => parameter.Key));
Assert.Equal("Chapter \"2\"", link.GetParameterValue("Title"));

// Rel holds the whole value; GetLink matches each of its space-separated relation types
Assert.Equal("next last", link.Rel);
Assert.Same(link, links.GetLink("last"));
```
<!-- endref -->

## Malformed headers

Parsing never throws on a malformed value. An element that does not start with a `<target>` is skipped, and the links
that follow it are still returned:

<!-- ref:../../tests/Meziantou.Framework.Http.Tests/LinkHeaderValueTests.cs;region=LinkHeaderValue_LenientParsing;format=md-fenced-code-block;dedent=true -->
```csharp
// An element that is not a link-value is skipped, and the following ones are still read
var links = LinkHeaderValue.Parse("not-a-link, </a>; rel=first, , </b>; rel=second");
Assert.Equal(["/a", "/b"], links.Select(link => link.Url));

// A header without a link-value yields no link
Assert.Empty(LinkHeaderValue.Parse("rel=next"));
```
<!-- endref -->

A comma inside the target (`<https://example.com/a,b>`) or inside a quoted value does not split the link.
