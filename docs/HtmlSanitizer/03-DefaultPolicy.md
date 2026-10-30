---
title: The default policy
description: The elements, attributes, URLs, comments and text that a new HtmlSanitizer keeps, unwraps or removes.
---

A new [HtmlSanitizer](cref:Meziantou.Framework.Sanitizers.HtmlSanitizer) is configured for user content: formatted text, lists, tables, links and images. This page lists
what it keeps. The next pages show how to change it.

## Elements

Each element of the fragment is handled in one of three ways:

| The element is | What happens | Elements |
| --- | --- | --- |
| In [BlockedElements](cref:Meziantou.Framework.Sanitizers.HtmlSanitizer.BlockedElements), or an element whose content is text rather than markup | Removed with its content | `script` and `style`; always `iframe`, `noembed`, `noframes`, `noscript`, `plaintext`, `textarea`, `title` and `xmp` |
| An HTML element in [ValidElements](cref:Meziantou.Framework.Sanitizers.HtmlSanitizer.ValidElements) | Kept, with its attributes and its content sanitized | See the list below |
| Anything else | Unwrapped: the tag is dropped, the content is kept and sanitized | `form`, `input`, `object`, `svg`, `math`, custom elements, … |

<!-- ref:../../tests/Meziantou.Framework.HtmlSanitizer.Tests/HtmlSanitizerTests.cs;region=HtmlSanitizer_ElementHandling;format=md-fenced-code-block;dedent=true -->
```csharp
var sanitizer = new HtmlSanitizer();

// script is blocked, so its content is removed too
Assert.Equal("<p>Hello World</p>", sanitizer.SanitizeHtmlFragment("<p>Hello <script>alert('xss')</script>World</p>"));

// form is not allowed, but its content is kept
Assert.Equal("<p>Hello</p>", sanitizer.SanitizeHtmlFragment("<form><p>Hello</p></form>"));
```
<!-- endref -->

The allowed elements are:

- **Text**: `a`, `abbr`, `acronym`, `b`, `bdi`, `bdo`, `big`, `br`, `cite`, `code`, `del`, `dfn`, `em`, `font`, `i`,
  `ins`, `kbd`, `label`, `mark`, `q`, `s`, `samp`, `small`, `span`, `strike`, `strong`, `sub`, `sup`, `time`, `tt`,
  `u`, `var`, `wbr`, and `ruby`, `rp`, `rt`
- **Blocks**: `address`, `article`, `aside`, `blockquote`, `center`, `div`, `figure`, `figcaption`, `footer`,
  `h1` to `h6`, `header`, `hgroup`, `hr`, `nav`, `p`, `pre`, `section`
- **Lists**: `dir`, `dl`, `dd`, `dt`, `li`, `menu`, `ol`, `ul`
- **Tables**: `caption`, `col`, `colgroup`, `table`, `tbody`, `td`, `tfoot`, `th`, `thead`, `tr`
- **Images**: `img`, `map`, `area`

SVG and MathML elements are always unwrapped, and an element is also unwrapped when keeping it would put it where a
browser never puts it, such as a link in a link. See [Allowed elements](04-AllowedElements.md#elements-that-cannot-be-allowed).

## Attributes

An attribute is kept when its name is in [ValidAttributes](cref:Meziantou.Framework.Sanitizers.HtmlSanitizer.ValidAttributes) or starts with a prefix of [ValidAttributePrefixes](cref:Meziantou.Framework.Sanitizers.HtmlSanitizer.ValidAttributePrefixes). Every
other attribute is removed, including `id`, `name`, `style` and every event handler (`onclick`, `onerror`, …):

<!-- ref:../../tests/Meziantou.Framework.HtmlSanitizer.Tests/HtmlSanitizerTests.cs;region=HtmlSanitizer_DefaultAttributes;format=md-fenced-code-block;dedent=true -->
```csharp
var sanitizer = new HtmlSanitizer();

var result = sanitizer.SanitizeHtmlFragment("<p id='intro' class='note' title='Tip' aria-label='Tip' onclick='alert(1)' style='color:red'>x</p>");
Assert.Equal("""<p class="note" title="Tip" aria-label="Tip">x</p>""", result);
```
<!-- endref -->

The allowed attributes are the same for every element:

- **URL attributes**, whose value is validated: `background`, `cite`, `href`, `longdesc`, `src`, `xlink:href`, and
  `srcset`
- **Descriptive attributes**: `abbr`, `align`, `alt`, `axis`, `bgcolor`, `border`, `cellpadding`, `cellspacing`,
  `class`, `clear`, `color`, `cols`, `colspan`, `compact`, `coords`, `datetime`, `decoding`, `dir`, `face`, `headers`,
  `height`, `hidden`, `hreflang`, `hspace`, `ismap`, `lang`, `language`, `loading`, `nohref`, `nowrap`, `open`, `rel`,
  `rev`, `reversed`, `role`, `rows`, `rowspan`, `rules`, `scope`, `scrolling`, `shape`, `size`, `span`, `start`,
  `summary`, `tabindex`, `target`, `title`, `translate`, `type`, `valign`, `value`, `vspace`, `width`
- **Prefixes**: `aria-`

## URLs

The values of the URL attributes are validated. Relative URLs, `http:`, `https:`, `mailto:`, `ftp:`, `tel:` and `file:`
URLs, and base64 `data:` URLs of images, videos and audio are kept. Any other value, such as a `javascript:` URL, is
replaced with an empty value:

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

See [URLs](06-Urls.md) for the details and for `srcset`.

## Comments

Comments are removed: downlevel-hidden conditional comments (`<!--[if IE]><script>...</script><![endif]-->`) are
executed by legacy browsers. Set [AllowComments](cref:Meziantou.Framework.Sanitizers.HtmlSanitizer.AllowComments) to keep them:

<!-- ref:../../tests/Meziantou.Framework.HtmlSanitizer.Tests/HtmlSanitizerTests.cs;region=HtmlSanitizer_AllowComments;format=md-fenced-code-block;dedent=true -->
```csharp
var sanitizer = new HtmlSanitizer { AllowComments = true };
var result = sanitizer.SanitizeHtmlFragment("<p>a<!-- comment -->b</p>");
Assert.Equal("<p>a<!-- comment -->b</p>", result);
```
<!-- endref -->

## Text

Text is kept. It is decoded by the parser and encoded again when the result is written, so a `<` in the text can
never start an element, and markup that is already encoded stays encoded. Incomplete markup is read as a browser
reads it: an unterminated `<!--` is a comment that runs to the end of the fragment, and `<![CDATA[`, `<?` or `</0`
start a bogus comment that ends at the first `>`.

<!-- ref:../../tests/Meziantou.Framework.HtmlSanitizer.Tests/HtmlSanitizerTests.cs;region=HtmlSanitizer_Text;format=md-fenced-code-block;dedent=true -->
```csharp
var sanitizer = new HtmlSanitizer();

Assert.Equal("<p>&lt;script&gt;</p>", sanitizer.SanitizeHtmlFragment("<p>&lt;script&gt;</p>"));
Assert.Equal("<p>a &lt; b &amp; c</p>", sanitizer.SanitizeHtmlFragment("<p>a < b & c</p>"));
Assert.Equal("<b></b>", sanitizer.SanitizeHtmlFragment("<b><!--"));
```
<!-- endref -->
