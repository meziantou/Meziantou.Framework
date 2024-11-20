# Meziantou.Framework.Http

```c#
var links = LinkHeaderValue.Parse("</style.css>; rel=preload; as=style; fetchpriority="high"");
var link = links.GetLink("preload");
_ = link.GetParameter("as");
```

```c#
HttpResponseMessage response = ...;
foreach(var item in response.Headers.EnumerateLinkHeaders())
{
    Console.WriteLine($"{item.Rel}: {item.Url}");
}
```