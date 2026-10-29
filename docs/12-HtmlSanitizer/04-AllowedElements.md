---
title: Allowed elements
description: Allow more elements, restrict the policy to a few, remove elements with their content, and know which elements can never be kept.
---

The elements are configured with two sets of names, compared without regard to case:

- `ValidElements`: the elements that are kept. An element that is not in it is unwrapped: its tag is dropped and its
  content is kept.
- `BlockedElements`: the elements that are removed with their content. It wins over `ValidElements`.

Both sets are `ISet<string>`: use `Add`, `Remove`, `Clear` or `UnionWith` to change them.

## Allow more elements

Add the names of the elements to `ValidElements`:

<!-- ref:../../tests/Meziantou.Framework.HtmlSanitizer.Tests/HtmlSanitizerTests.cs;region=HtmlSanitizer_AllowElements;format=md-fenced-code-block;dedent=true -->
```csharp
var sanitizer = new HtmlSanitizer();
var html = "<details open><summary>More</summary><p>Text</p></details>";

// details and summary are not allowed by default, so they are unwrapped
Assert.Equal("More<p>Text</p>", sanitizer.SanitizeHtmlFragment(html));

sanitizer.ValidElements.Add("details");
sanitizer.ValidElements.Add("summary");
Assert.Equal("""<details open=""><summary>More</summary><p>Text</p></details>""", sanitizer.SanitizeHtmlFragment(html));
```
<!-- endref -->

The attributes of an element you allow go through the same attribute rules as the others: `open` is already allowed,
but an attribute that is not, such as `poster` on `video`, is removed until you allow it. When the new element has an
attribute that holds a URL, such as `poster`, `action`, `formaction` or `data`, allow it in `UriAttributes` too, so its
value is validated. See [URLs](06-Urls.md#validate-your-own-url-attributes).

Think about what the element lets the author of the content do in your page. A `form`, for instance, lets them show a
fake sign-in form that sends what your readers type to another site.

## Restrict the policy

For short content such as chat messages, clear `ValidElements` and add only the elements you want:

<!-- ref:../../tests/Meziantou.Framework.HtmlSanitizer.Tests/HtmlSanitizerTests.cs;region=HtmlSanitizer_RestrictElements;format=md-fenced-code-block;dedent=true -->
```csharp
// Only keep inline formatting and links
var sanitizer = new HtmlSanitizer();
sanitizer.ValidElements.Clear();
sanitizer.ValidElements.UnionWith(["a", "b", "br", "code", "em", "i", "strong"]);

var result = sanitizer.SanitizeHtmlFragment("<h1>Title</h1><p>Some <b>bold</b> text<img src='https://example.com/a.png'></p>");
Assert.Equal("TitleSome <b>bold</b> text", result);
```
<!-- endref -->

The heading and the paragraph are unwrapped, so their texts are joined (`TitleSome`). The image has no content, so
nothing is left of it. Keep `p` and `br` in the set when line breaks matter.

## Remove elements with their content

Unwrapping keeps the text of an element. To drop an element and everything in it, add it to `BlockedElements`:

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

## Elements that cannot be allowed

Some elements are never kept, whatever the configuration:

- **Elements whose content is text rather than markup** are removed with their content: `script`, `style`, `iframe`,
  `noembed`, `noframes`, `noscript`, `plaintext`, `textarea`, `title` and `xmp`. The content of most of them is written
  back exactly as it was read, so it can never be sanitized. Adding them to `ValidElements`, or removing `script` and
  `style` from `BlockedElements`, has no effect.
- **SVG and MathML elements** (`svg`, `math` and everything in them) are unwrapped, even when their name is in
  `ValidElements`: a browser parses them with other rules.
- **Elements that a browser would move** are unwrapped: a link in another link, a heading directly in a heading, a
  `form` in a `form`, a `button` in a `button`, a `div` in a `p`, an `li` in an `li`, a `tr` outside of a table
  section, a table that would contain text, and so on. The parser only builds them when an element separates them, such
  as `object` or `template`, and unwrapping that element puts them together. See
  [Security guarantees and limits](07-SecurityGuaranteesAndLimits.md#what-the-sanitizer-guarantees).

For instance, an `svg` element is unwrapped even when it is allowed:

<!-- ref:../../tests/Meziantou.Framework.HtmlSanitizer.Tests/HtmlSanitizerTests.cs;region=HtmlSanitizer_ForeignContent;format=md-fenced-code-block;dedent=true -->
```csharp
var sanitizer = new HtmlSanitizer();
sanitizer.ValidElements.Add("svg");

// SVG and MathML elements are always unwrapped: only their text is kept
var result = sanitizer.SanitizeHtmlFragment("<p><svg onload='alert(1)'><text>Hello</text></svg></p>");
Assert.Equal("<p>Hello</p>", result);
```
<!-- endref -->

The content of a `template` element you allow is sanitized like the rest of the fragment.
