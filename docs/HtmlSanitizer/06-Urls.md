---
title: URLs
description: Which URLs are kept in links and images, how srcset is checked, how to validate your own URL attributes, and how to check a URL yourself.
---

The values of the attributes of `UriAttributes` and `SrcsetAttributes` are validated. An unsafe value is replaced with
an empty value; the attribute and its element are kept.

| Set | Default | The value is |
| --- | --- | --- |
| `UriAttributes` | `background`, `cite`, `href`, `longdesc`, `src`, `xlink:href` | One URL |
| `SrcsetAttributes` | `srcset` | A list of image candidates, each one a URL with optional descriptors |

These sets only decide how a value is checked: an attribute is kept only when it is also allowed by `ValidAttributes`
or `ValidAttributePrefixes`.

## Allowed URLs

A URL is kept when it is:

- a relative URL, such as `/about`, `page.html`, `#top` or `//example.com/a.png`;
- an `http:`, `https:`, `mailto:`, `ftp:`, `tel:` or `file:` URL;
- a base64 `data:` URL of an image (`bmp`, `gif`, `jpeg`, `jpg`, `png`, `tiff`, `webp`), a video (`mpeg`, `mp4`,
  `ogg`, `webm`) or an audio file (`mp3`, `oga`, `ogg`, `opus`).

Any other URL is replaced with an empty value: `javascript:` and `vbscript:` URLs, `data:` URLs of HTML or SVG, and
any scheme that is not in the list.

<!-- ref:../../tests/Meziantou.Framework.HtmlSanitizer.Tests/HtmlSanitizerTests.cs;region=HtmlSanitizer_UrlKinds;format=md-fenced-code-block;dedent=true -->
```csharp
var sanitizer = new HtmlSanitizer();

// Relative URLs and the allowed schemes are kept
Assert.Equal("""<a href="/account/settings">x</a>""", sanitizer.SanitizeHtmlFragment("<a href='/account/settings'>x</a>"));
Assert.Equal("""<a href="mailto:user@example.com">x</a>""", sanitizer.SanitizeHtmlFragment("<a href='mailto:user@example.com'>x</a>"));

// data: URLs are kept only for base64-encoded images, videos and audio
Assert.Equal("""<img src="data:image/png;base64,iVBORw0KGgo=">""", sanitizer.SanitizeHtmlFragment("<img src='data:image/png;base64,iVBORw0KGgo='>"));
Assert.Equal("""<img src="">""", sanitizer.SanitizeHtmlFragment("<img src='data:image/svg+xml;base64,PHN2Zz4='>"));

// Any other scheme empties the attribute, and the element is kept
Assert.Equal("""<a href="">x</a>""", sanitizer.SanitizeHtmlFragment("<a href='vbscript:msgbox(1)'>x</a>"));
```
<!-- endref -->

The value is validated once the parser has decoded it, as the browser reads it, so an entity cannot hide the scheme of
a URL:

<!-- ref:../../tests/Meziantou.Framework.HtmlSanitizer.Tests/HtmlSanitizerTests.cs;region=HtmlSanitizer_EncodedUrls;format=md-fenced-code-block;dedent=true -->
```csharp
var sanitizer = new HtmlSanitizer();

// The value is validated once decoded, as the browser reads it
Assert.Equal("""<a href="">x</a>""", sanitizer.SanitizeHtmlFragment("<a href='java&#115;cript:alert(1)'>x</a>"));
Assert.Equal("""<a href="https://example.com/?a=1&amp;b=2">x</a>""", sanitizer.SanitizeHtmlFragment("<a href='https://example.com/?a=1&amp;b=2'>x</a>"));
```
<!-- endref -->

An empty `href` links to the current page. The list of schemes cannot be changed; to restrict it, remove the attribute,
or check the URLs of the output yourself.

## Responsive images

A `srcset` value is kept only when every one of its URLs is allowed:

<!-- ref:../../tests/Meziantou.Framework.HtmlSanitizer.Tests/HtmlSanitizerTests.cs;region=HtmlSanitizer_Srcset;format=md-fenced-code-block;dedent=true -->
```csharp
var sanitizer = new HtmlSanitizer();

// Blocks srcset with dangerous URLs
var result1 = sanitizer.SanitizeHtmlFragment("<img srcset='javascript:alert() 300w, https://example.com 600w'>");
Assert.Equal("<img srcset=\"\">", result1);

// Allows safe srcset
var result2 = sanitizer.SanitizeHtmlFragment("<img srcset='https://example.com/img1.jpg 300w, https://example.com/img2.jpg 600w'>");
Assert.Equal("<img srcset=\"https://example.com/img1.jpg 300w, https://example.com/img2.jpg 600w\">", result2);
```
<!-- endref -->

An attribute can be in both sets. Its value must then pass both validations.

## Validate your own URL attributes

`ValidAttributes` and `UriAttributes` are separate sets, and only `UriAttributes` triggers URL validation. Adding an
attribute to `ValidAttributes` alone lets any value through unchecked:

<!-- ref:../../tests/Meziantou.Framework.HtmlSanitizer.Tests/HtmlSanitizerTests.cs;region=HtmlSanitizer_UriAttributes;format=md-fenced-code-block;dedent=true -->
```csharp
var sanitizer = new HtmlSanitizer();
sanitizer.ValidAttributes.Add("data-url");

// The value is not validated
Assert.Equal("<p data-url=\"javascript:alert(1)\">test</p>", sanitizer.SanitizeHtmlFragment("<p data-url='javascript:alert(1)'>test</p>"));

sanitizer.UriAttributes.Add("data-url");
Assert.Equal("<p data-url=\"\">test</p>", sanitizer.SanitizeHtmlFragment("<p data-url='javascript:alert(1)'>test</p>"));
```
<!-- endref -->

Add any attribute that a browser resolves as a URL to both sets, such as `action`, `formaction`, `poster`, `data`,
`dynsrc` and `lowsrc`. The same applies in reverse: removing an attribute from `UriAttributes` while leaving it in
`ValidAttributes` stops its value from being checked. Removing it from `ValidAttributes` alone is safe: the attribute
is then dropped.

## Check a URL yourself

`UrlSanitizer` applies the same rules to a single value. Use it for the URLs that you write in your own markup, such
as the website of a user profile:

<!-- ref:../../tests/Meziantou.Framework.HtmlSanitizer.Tests/UrlSanitizerTests.cs;region=UrlSanitizer_IsSafeUrlAndSrcset;format=md-fenced-code-block;dedent=true -->
```csharp
// Check if a URL is safe
Assert.True(UrlSanitizer.IsSafeUrl("https://example.com"));
Assert.False(UrlSanitizer.IsSafeUrl("javascript:alert('xss')"));

// Check if a srcset value is safe
Assert.True(UrlSanitizer.IsSafeSrcset("https://example.com/img1.jpg 300w, https://example.com/img2.jpg 600w"));
```
<!-- endref -->

`UrlSanitizer` does not decode its input, so it rejects a URL that contains a `&` before the first `/`, `?` or `#`.
This stops an entity from hiding the scheme when the URL is read from markup, as in `javascript&#58;alert(1)`. It also
rejects the rare relative URL whose first segment contains a `&`, such as `q&a.html`: encode it as `q%26a.html`.

<!-- ref:../../tests/Meziantou.Framework.HtmlSanitizer.Tests/UrlSanitizerTests.cs;region=UrlSanitizer_ValidateUrl;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.True(UrlSanitizer.IsSafeUrl("https://example.com/about"));
Assert.True(UrlSanitizer.IsSafeUrl("/about"));
Assert.False(UrlSanitizer.IsSafeUrl("javascript:alert(1)"));

// The value is not decoded, so a '&' before the first '/', '?' or '#' is rejected
Assert.False(UrlSanitizer.IsSafeUrl("javascript&#58;alert(1)"));
Assert.False(UrlSanitizer.IsSafeUrl("q&a.html"));
Assert.True(UrlSanitizer.IsSafeUrl("q%26a.html"));

// null is safe: there is no URL
Assert.True(UrlSanitizer.IsSafeUrl(url: null));
```
<!-- endref -->

`IsSafeUrl` and `IsSafeSrcset` only check the value. Encode it when you write it in an attribute, as Razor does.
