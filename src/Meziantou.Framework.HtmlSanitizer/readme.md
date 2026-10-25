# Meziantou.Framework.HtmlSanitizer

A .NET library for sanitizing HTML fragments to prevent XSS attacks and other security vulnerabilities.

## Usage

This library provides functionality to sanitize HTML content by removing potentially dangerous elements, attributes, and URLs while preserving safe HTML structure.

The fragment is parsed exactly as a browser parses it, with the HTML parser of
[Meziantou.Framework.WebPlatform](../Meziantou.Framework.WebPlatform), and the result is written with the HTML
serialization algorithm: attribute values are written between double quotes, and void elements have no end tag
(`<img src="x">`).

### Basic Sanitization

`HtmlSanitizer` and `UrlSanitizer` are in the `Meziantou.Framework.Sanitizers` namespace.

<!-- ref:../../tests/Meziantou.Framework.HtmlSanitizer.Tests/HtmlSanitizerTests.cs;region=HtmlSanitizer_Basic;format=md-fenced-code-block;dedent=true -->
```csharp
var sanitizer = new HtmlSanitizer();
var safeHtml = sanitizer.SanitizeHtmlFragment("<p>Hello <script>alert('xss')</script>World</p>");
Assert.Equal("<p>Hello World</p>", safeHtml);
```
<!-- endref -->

### Remove Dangerous Attributes

The sanitizer removes potentially dangerous attributes like `style`, `id`, and event handlers:

<!-- ref:../../tests/Meziantou.Framework.HtmlSanitizer.Tests/HtmlSanitizerTests.cs;region=HtmlSanitizer_RemoveAttributes;format=md-fenced-code-block;dedent=true -->
```csharp
var sanitizer = new HtmlSanitizer();

// Removes style attributes
var result1 = sanitizer.SanitizeHtmlFragment("<p style='color:red'>test</p>");
Assert.Equal("<p>test</p>", result1);

// Removes id attributes (not in allowed list)
var result2 = sanitizer.SanitizeHtmlFragment("<p id='test'>test</p>");
Assert.Equal("<p>test</p>", result2);
```
<!-- endref -->

### Sanitize URLs

The sanitizer validates URLs in attributes like `href` and `src` to prevent javascript: and other dangerous protocols:

<!-- ref:../../tests/Meziantou.Framework.HtmlSanitizer.Tests/HtmlSanitizerTests.cs;region=HtmlSanitizer_Urls;format=md-fenced-code-block;dedent=true -->
```csharp
var sanitizer = new HtmlSanitizer();

// Blocks javascript: URLs
var result1 = sanitizer.SanitizeHtmlFragment("<a href='javascript:alert(\"xss\")'>click</a>");
Assert.Equal("<a href=\"\">click</a>", result1);

// Allows safe URLs
var result2 = sanitizer.SanitizeHtmlFragment("<a href='https://example.com'>click</a>");
Assert.Equal("<a href=\"https://example.com\">click</a>", result2);
```
<!-- endref -->

### Sanitize Srcset Attributes

The sanitizer also validates `srcset` attributes used for responsive images:

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

### Text

Text and attribute values are decoded by the parser and encoded again when the result is written, so already
encoded markup stays encoded. The markup is read as a browser reads it: an unterminated `<!--` is a comment that
runs to the end of the fragment, and `<![CDATA[`, `<?` or `</0` start a bogus comment that ends at the first `>`.

<!-- ref:../../tests/Meziantou.Framework.HtmlSanitizer.Tests/HtmlSanitizerTests.cs;region=HtmlSanitizer_Text;format=md-fenced-code-block;dedent=true -->
```csharp
var sanitizer = new HtmlSanitizer();

Assert.Equal("<p>&lt;script&gt;</p>", sanitizer.SanitizeHtmlFragment("<p>&lt;script&gt;</p>"));
Assert.Equal("<p>a &lt; b &amp; c</p>", sanitizer.SanitizeHtmlFragment("<p>a < b & c</p>"));
Assert.Equal("<b></b>", sanitizer.SanitizeHtmlFragment("<b><!--"));
```
<!-- endref -->

### Keeping Comments

Comments are removed by default: downlevel-hidden conditional comments (`<!--[if IE]><script>...</script><![endif]-->`)
are executed by legacy browsers. Set `AllowComments` to keep them:

<!-- ref:../../tests/Meziantou.Framework.HtmlSanitizer.Tests/HtmlSanitizerTests.cs;region=HtmlSanitizer_AllowComments;format=md-fenced-code-block;dedent=true -->
```csharp
var sanitizer = new HtmlSanitizer { AllowComments = true };
var result = sanitizer.SanitizeHtmlFragment("<p>a<!-- comment -->b</p>");
Assert.Equal("<p>a<!-- comment -->b</p>", result);
```
<!-- endref -->

### Customizing Allowed Elements

You can customize which elements are allowed:

<!-- ref:../../tests/Meziantou.Framework.HtmlSanitizer.Tests/HtmlSanitizerTests.cs;region=HtmlSanitizer_CustomElements;format=md-fenced-code-block;dedent=true -->
```csharp
var sanitizer = new HtmlSanitizer();

// Add custom elements
sanitizer.ValidElements.Add("custom-element");

// Has no effect: the content of a raw text element is not markup, so the element is
// removed with its content whether or not it is in ValidElements
sanitizer.ValidElements.Add("iframe");

// Remove elements from allowed list
sanitizer.ValidElements.Remove("img");

// Add elements to blocked list (will be removed entirely)
sanitizer.BlockedElements.Add("iframe");

var result = sanitizer.SanitizeHtmlFragment("<custom-element>a</custom-element><iframe>b</iframe><img src='c.png'>");
Assert.Equal("<custom-element>a</custom-element>", result);
```
<!-- endref -->

### Customizing Allowed Attributes

You can customize which attributes are allowed:

<!-- ref:../../tests/Meziantou.Framework.HtmlSanitizer.Tests/HtmlSanitizerTests.cs;region=HtmlSanitizer_CustomAttributes;format=md-fenced-code-block;dedent=true -->
```csharp
var sanitizer = new HtmlSanitizer();

// Add custom attributes
sanitizer.ValidAttributes.Add("data-custom");

// Remove attributes from allowed list
sanitizer.ValidAttributes.Remove("target");

// Add attributes that should be URL-validated
sanitizer.UriAttributes.Add("data-url");

var result = sanitizer.SanitizeHtmlFragment("<a data-custom='value' target='_blank' href='https://example.com'>link</a>");
Assert.Equal("<a data-custom=\"value\" href=\"https://example.com\">link</a>", result);
```
<!-- endref -->

Attributes can also be allowed by prefix with `ValidAttributePrefixes`. It contains `aria-` by default:

<!-- ref:../../tests/Meziantou.Framework.HtmlSanitizer.Tests/HtmlSanitizerTests.cs;region=HtmlSanitizer_AttributePrefixes;format=md-fenced-code-block;dedent=true -->
```csharp
var sanitizer = new HtmlSanitizer();

// Allow every data-* attribute
sanitizer.ValidAttributePrefixes.Add("data-");

// Remove the ARIA attributes
sanitizer.ValidAttributePrefixes.Remove("aria-");

var result = sanitizer.SanitizeHtmlFragment("<p data-id='1' aria-label='text'>test</p>");
Assert.Equal("<p data-id=\"1\">test</p>", result);
```
<!-- endref -->

An attribute allowed by a prefix is URL-validated only when it is in `UriAttributes`. Frameworks such as htmx or
Knockout run the value of some `data-*` attributes (`data-hx-on:click`, `data-bind`), so only allow `data-` when
the page does not use them.

The `is` attribute is removed like any other attribute that is not allowed, and the element is recreated without it:
otherwise it would still be a customized built-in element, which a browser upgrades with the custom element of the
page that has this name.

`ValidAttributes` and `UriAttributes` are separate sets, and only `UriAttributes` triggers URL validation.
Adding an attribute to `ValidAttributes` alone lets any value through unchecked:

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

Add any attribute that a browser resolves as a URL to both sets — `action`, `formaction`, `poster`, `data`,
`srcdoc`, `dynsrc` and `lowsrc` among them. The same applies in reverse: removing an attribute from
`UriAttributes` while leaving it in `ValidAttributes` stops its value from being checked. Removing it from
`ValidAttributes` alone is safe, since the attribute is then dropped.

## Default Configuration

### Allowed Elements

By default, the sanitizer allows:
- **Block elements**: `address`, `article`, `aside`, `blockquote`, `caption`, `center`, `col`, `colgroup`, `dd`, `div`, `dl`, `dt`, `figure`, `figcaption`, `footer`, `h1`-`h6`, `header`, `hgroup`, `hr`, `li`, `nav`, `ol`, `p`, `pre`, `section`, `table`, `tbody`, `td`, `tfoot`, `th`, `thead`, `tr`, `ul`, and more
- **Inline elements**: `a`, `abbr`, `acronym`, `b`, `bdi`, `bdo`, `big`, `br`, `cite`, `code`, `del`, `dfn`, `em`, `font`, `i`, `img`, `ins`, `kbd`, `label`, `map`, `mark`, `q`, `ruby`, `rp`, `rt`, `s`, `samp`, `small`, `span`, `strike`, `strong`, `sub`, `sup`, `time`, `tt`, `u`, `var`
- **Void elements**: `area`, `br`, `col`, `hr`, `img`, `wbr`

### Element Handling

An element is handled in one of three ways:

- It is in `BlockedElements`, or its content is text rather than markup (`script`, `style`, `title`, `textarea`, `iframe`, `noscript`, `noembed`, `noframes`, `plaintext`, `xmp`): the element is removed **with its content**. This takes precedence over `ValidElements` — the content of most of these elements is written back exactly as it was read, so it can never be sanitized and adding such an element to `ValidElements` has no effect.
- Otherwise, it is an HTML element in `ValidElements`: the element and its content are kept, and its attributes are sanitized. The content of a `template` element is sanitized too.
- Otherwise the element is unwrapped: the tag is dropped but its content is kept and sanitized. SVG and MathML elements (`svg`, `math` and their descendants) are always unwrapped, even when their name is in `ValidElements`, as a browser parses them with different rules.
- A link (`a`) inside another link with no table cell between them, or a heading (`h1`-`h6`) whose parent is a heading,
  is unwrapped as well. The parser only builds them when an element separates them, such as `object` or `template`;
  once that element is unwrapped (`<a><object><a>`), a browser would not read the markup back as the same elements.
  For the same reason, an allowed `form` inside another `form` is unwrapped, unless a `template` that is kept
  separates them.

<!-- ref:../../tests/Meziantou.Framework.HtmlSanitizer.Tests/HtmlSanitizerTests.cs;region=HtmlSanitizer_ElementHandling;format=md-fenced-code-block;dedent=true -->
```csharp
var sanitizer = new HtmlSanitizer();

// script is blocked, so its content is removed too
Assert.Equal("<p>Hello World</p>", sanitizer.SanitizeHtmlFragment("<p>Hello <script>alert('xss')</script>World</p>"));

// form is not allowed, but its content is kept
Assert.Equal("<p>Hello</p>", sanitizer.SanitizeHtmlFragment("<form><p>Hello</p></form>"));
```
<!-- endref -->

### Blocked Elements

By default, the sanitizer blocks (removes entirely):
- `script`
- `style`

### Allowed Attributes

By default, the sanitizer allows common HTML attributes like:
- **URI attributes** (with URL validation): `background`, `cite`, `href`, `longdesc`, `src`, `xlink:href`
- **Srcset attributes** (with URL validation): `srcset`
- **General attributes**: `abbr`, `align`, `alt`, `axis`, `bgcolor`, `border`, `cellpadding`, `cellspacing`, `class`, `clear`, `color`, `cols`, `colspan`, `compact`, `coords`, `datetime`, `decoding`, `dir`, `face`, `headers`, `height`, `hidden`, `hreflang`, `hspace`, `ismap`, `lang`, `language`, `loading`, `nohref`, `nowrap`, `open`, `rel`, `rev`, `reversed`, `role`, `rows`, `rowspan`, `rules`, `scope`, `scrolling`, `shape`, `size`, `span`, `start`, `summary`, `tabindex`, `target`, `title`, `translate`, `type`, `valign`, `value`, `vspace`, `width`
- **Attribute prefixes**: `aria-*`

The general attributes other than `class` are shared with the generic attributes of
[Meziantou.Framework.Markdown](../Meziantou.Framework.Markdown), which allows them by default.

## URL Sanitizer

The library includes a `UrlSanitizer` class that validates URLs:

<!-- ref:../../tests/Meziantou.Framework.HtmlSanitizer.Tests/UrlSanitizerTests.cs;region=UrlSanitizer_IsSafeUrlAndSrcset;format=md-fenced-code-block;dedent=true -->
```csharp
// Check if a URL is safe
Assert.True(UrlSanitizer.IsSafeUrl("https://example.com"));
Assert.False(UrlSanitizer.IsSafeUrl("javascript:alert('xss')"));

// Check if a srcset value is safe
Assert.True(UrlSanitizer.IsSafeSrcset("https://example.com/img1.jpg 300w, https://example.com/img2.jpg 600w"));
```
<!-- endref -->

### Allowed URL Protocols

The URL sanitizer allows:
- `http:` and `https:`
- `mailto:`
- `ftp:`
- `tel:`
- `file:`
- Relative URLs (no protocol)
- Safe data URLs (base64-encoded images, videos, and audio)

A URL is rejected when it contains a `&` before the first `/`, `?` or `#`. This is what stops an entity from
masking the scheme, as in `java&#115;cript:alert(1)` or `javascript&#58;alert(1)`, when the URL is read from
markup without decoding it. It also rejects the rare relative URL whose first segment contains a `&`, such as
`q&a.html` — encode it as `q%26a.html`. `HtmlSanitizer` validates the attribute values once decoded, as the
browser reads them.

## Implementation Details

This library is inspired by:
- [Angular's HTML sanitizer](https://github.com/angular/angular/blob/main/packages/core/src/sanitization/html_sanitizer.ts)
- [Sanitizer API specification](https://wicg.github.io/sanitizer-api/#default-configuration-dictionary)

The library uses [Meziantou.Framework.WebPlatform](../Meziantou.Framework.WebPlatform) for HTML parsing and serialization.

## Additional Resources

- [Angular HTML Sanitizer](https://github.com/angular/angular/blob/main/packages/core/src/sanitization/html_sanitizer.ts)
- [W3C Sanitizer API](https://wicg.github.io/sanitizer-api/)
