---
title: Getting started
description: Install the package, sanitize a fragment of user HTML, and insert the result in a page.
---

## Install the package

Add the `Meziantou.Framework.HtmlSanitizer` package to a project that targets .NET 10 or later:

```bash
dotnet add package Meziantou.Framework.HtmlSanitizer
```

The examples of this documentation omit `using Meziantou.Framework.Sanitizers;`.

## Sanitize a fragment

Create an `HtmlSanitizer` and pass the untrusted HTML to `SanitizeHtmlFragment`:

<!-- ref:../../tests/Meziantou.Framework.HtmlSanitizer.Tests/HtmlSanitizerTests.cs;region=HtmlSanitizer_SanitizeComment;format=md-fenced-code-block;dedent=true -->
```csharp
var sanitizer = new HtmlSanitizer();

var comment = """<p onclick="steal()">Nice <b>post</b>! <a href="javascript:alert(1)">Click</a> <img src="cat.png" onerror="alert(1)"><script>alert(2)</script></p>""";
var safeHtml = sanitizer.SanitizeHtmlFragment(comment);

Assert.Equal("""<p>Nice <b>post</b>! <a href="">Click</a> <img src="cat.png"></p>""", safeHtml);
```
<!-- endref -->

- The `script` element is removed with its content.
- The `onclick` and `onerror` event handlers are removed: they are not in the allowed attributes.
- The `javascript:` URL is replaced with an empty value, and the link is kept.
- The paragraph, the bold text, the link and the image are kept.
- The result is written in a normalized form: attribute values are between double quotes, and void elements such as
  `img` have no end tag.

`SanitizeHtmlFragment` returns `null` when the input is `null`.

## Insert the result in a page

The output is HTML for the body of a page. Insert it as the content of an element, such as a `div`, in a page that
starts with `<!DOCTYPE html>`: the fragment is parsed as it would be in such a page.

The result is already HTML, so write it without encoding it again (`@Html.Raw(...)` in Razor, a `MarkupString` in
Blazor). Do not insert it in an attribute value, in a `script` or `style` element, or in an `svg` or `math` element:
the browser reads these places with other rules. Do not change the output afterward, for instance by decoding its
entities or concatenating it with other untrusted text: the result would no longer be sanitized.

## Reuse the sanitizer

`SanitizeHtmlFragment` reads the configuration of the sanitizer and does not change it. Configure a sanitizer once, for
instance at startup, and reuse it for every fragment. Change its sets only while no other thread is sanitizing.

## Next steps

- [The default policy](03-DefaultPolicy.md): what the sanitizer keeps, unwraps and removes.
- [Allowed elements](04-AllowedElements.md) and [Allowed attributes and styles](05-AllowedAttributesAndStyles.md):
  adapt the policy to your content.
- [Security guarantees and limits](07-SecurityGuaranteesAndLimits.md).
