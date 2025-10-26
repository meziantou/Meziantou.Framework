# Meziantou.Framework.HtmlSanitizer

A .NET library for sanitizing HTML fragments to prevent XSS attacks and other security vulnerabilities.

## Usage

This library provides functionality to sanitize HTML content by removing potentially dangerous elements, attributes, and URLs while preserving safe HTML structure.

### Basic Sanitization

```csharp
using Meziantou.Framework.Sanitizers;

var sanitizer = new HtmlSanitizer();
var safeHtml = sanitizer.SanitizeHtmlFragment("<p>Hello <script>alert('xss')</script>World</p>");
// Result: "<p>Hello World</p>"
```

### Remove Dangerous Attributes

The sanitizer removes potentially dangerous attributes like `style`, `id`, and event handlers:

```csharp
var sanitizer = new HtmlSanitizer();

// Removes style attributes
var result = sanitizer.SanitizeHtmlFragment("<p style='color:red'>test</p>");
// Result: "<p>test</p>"

// Removes id attributes (not in allowed list)
var result = sanitizer.SanitizeHtmlFragment("<p id='test'>test</p>");
// Result: "<p>test</p>"
```

### Sanitize URLs

The sanitizer validates URLs in attributes like `href` and `src` to prevent javascript: and other dangerous protocols:

```csharp
var sanitizer = new HtmlSanitizer();

// Blocks javascript: URLs
var result = sanitizer.SanitizeHtmlFragment("<a href='javascript:alert(\"xss\")'>click</a>");
// Result: "<a href=''>click</a>"

// Allows safe URLs
var result = sanitizer.SanitizeHtmlFragment("<a href='https://example.com'>click</a>");
// Result: "<a href='https://example.com'>click</a>"
```

### Sanitize Srcset Attributes

The sanitizer also validates `srcset` attributes used for responsive images:

```csharp
var sanitizer = new HtmlSanitizer();

// Blocks srcset with dangerous URLs
var result = sanitizer.SanitizeHtmlFragment("<img srcset='javascript:alert() 300w, https://example.com 600w'>");
// Result: "<img srcset=''>"

// Allows safe srcset
var result = sanitizer.SanitizeHtmlFragment("<img srcset='https://example.com/img1.jpg 300w, https://example.com/img2.jpg 600w'>");
// Result: "<img srcset='https://example.com/img1.jpg 300w, https://example.com/img2.jpg 600w'>"
```

### Customizing Allowed Elements

You can customize which elements are allowed:

```csharp
var sanitizer = new HtmlSanitizer();

// Add custom elements
sanitizer.ValidElements.Add("custom-element");

// Remove elements from allowed list
sanitizer.ValidElements.Remove("img");

// Add elements to blocked list (will be removed entirely)
sanitizer.BlockedElements.Add("iframe");
```

### Customizing Allowed Attributes

You can customize which attributes are allowed:

```csharp
var sanitizer = new HtmlSanitizer();

// Add custom attributes
sanitizer.ValidAttributes.Add("data-custom");

// Remove attributes from allowed list
sanitizer.ValidAttributes.Remove("target");

// Add attributes that should be URL-validated
sanitizer.UriAttributes.Add("data-url");
```

## Default Configuration

### Allowed Elements

By default, the sanitizer allows:
- **Block elements**: `address`, `article`, `aside`, `blockquote`, `caption`, `center`, `col`, `colgroup`, `dd`, `div`, `dl`, `dt`, `figure`, `figcaption`, `footer`, `h1`-`h6`, `header`, `hgroup`, `hr`, `li`, `nav`, `ol`, `p`, `pre`, `section`, `table`, `tbody`, `td`, `tfoot`, `th`, `thead`, `tr`, `ul`, and more
- **Inline elements**: `a`, `abbr`, `acronym`, `b`, `bdi`, `bdo`, `big`, `br`, `cite`, `code`, `del`, `dfn`, `em`, `font`, `i`, `img`, `ins`, `kbd`, `label`, `map`, `mark`, `q`, `ruby`, `rp`, `rt`, `s`, `samp`, `small`, `span`, `strike`, `strong`, `sub`, `sup`, `time`, `tt`, `u`, `var`
- **Void elements**: `area`, `br`, `col`, `hr`, `img`, `wbr`

### Blocked Elements

By default, the sanitizer blocks (removes entirely):
- `script`
- `style`

### Allowed Attributes

By default, the sanitizer allows common HTML attributes like:
- **URI attributes** (with URL validation): `background`, `cite`, `href`, `longdesc`, `src`, `xlink:href`
- **Srcset attributes** (with URL validation): `srcset`
- **General attributes**: `abbr`, `align`, `alt`, `axis`, `bgcolor`, `border`, `cellpadding`, `cellspacing`, `class`, `clear`, `color`, `cols`, `colspan`, `compact`, `coords`, `dir`, `face`, `headers`, `height`, `hreflang`, `hspace`, `ismap`, `lang`, `language`, `nohref`, `nowrap`, `rel`, `rev`, `rows`, `rowspan`, `rules`, `scope`, `scrolling`, `shape`, `size`, `span`, `start`, `summary`, `tabindex`, `target`, `title`, `type`, `valign`, `value`, `vspace`, `width`

## URL Sanitizer

The library includes a `UrlSanitizer` class that validates URLs:

```csharp
using Meziantou.Framework.Sanitizers;

// Check if a URL is safe
bool isSafe = UrlSanitizer.IsSafeUrl("https://example.com"); // true
bool isUnsafe = UrlSanitizer.IsSafeUrl("javascript:alert('xss')"); // false

// Check if a srcset value is safe
bool isSafeSrcset = UrlSanitizer.IsSafeSrcset("https://example.com/img1.jpg 300w, https://example.com/img2.jpg 600w"); // true
```

### Allowed URL Protocols

The URL sanitizer allows:
- `http:` and `https:`
- `mailto:`
- `ftp:`
- `tel:`
- `file:`
- Relative URLs (no protocol)
- Safe data URLs (base64-encoded images, videos, and audio)

## Implementation Details

This library is inspired by:
- [Angular's HTML sanitizer](https://github.com/angular/angular/blob/main/packages/core/src/sanitization/html_sanitizer.ts)
- [Sanitizer API specification](https://wicg.github.io/sanitizer-api/#default-configuration-dictionary)

The library uses [AngleSharp](https://anglesharp.github.io/) for HTML parsing.

## Additional Resources

- [Angular HTML Sanitizer](https://github.com/angular/angular/blob/main/packages/core/src/sanitization/html_sanitizer.ts)
- [W3C Sanitizer API](https://wicg.github.io/sanitizer-api/)
- [AngleSharp](https://anglesharp.github.io/)
