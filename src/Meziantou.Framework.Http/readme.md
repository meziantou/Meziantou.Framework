# Meziantou.Framework.Http

Parse [RFC 8288](https://datatracker.ietf.org/doc/html/rfc8288) `Link` header values.

```c#
var links = LinkHeaderValue.Parse(@"</style.css>; rel=preload; as=style; fetchpriority=""high""");
var link = links.GetLink("preload");
_ = link?.GetParameterValue("as"); // style
```

```c#
HttpResponseMessage response = ...;
foreach (var item in response.Headers.EnumerateLinkHeaders())
{
    Console.WriteLine($"{item.Rel}: {item.Url}");
}
```

```c#
// Follow pagination links
var nextUrl = response.Headers.EnumerateLinkHeaders().GetLinkUrl("next");
```
