---
title: Security guarantees and limits
description: What the sanitizer guarantees about its output, what it depends on, and what it does not protect you from.
---

The sanitizer removes what lets an HTML fragment run script in your page. It does not decide whether the content is
appropriate, and its guarantees only hold for the default policy, or for a policy you changed with care, and for an
output inserted where it is meant to be. Read this page before you show user content.

## What the sanitizer guarantees

With the default policy, the output:

- **contains no element that runs script or loads a page**: `script`, `iframe`, `object`, `embed`, `base`, `meta`,
  `link`, forms and SVG are removed or unwrapped;
- **contains no event handler** or other attribute that is not in the allowed sets;
- **contains no unsafe URL** in the attributes of `UriAttributes` and `SrcsetAttributes`: `javascript:`, `vbscript:`,
  `data:text/html` and the other schemes that are not allowed are emptied, even when an entity masks them;
- **is read back by a browser as the same elements.** The fragment is parsed with a parser that follows the HTML
  standard, and an element is unwrapped when keeping it would put it where a browser never puts it. Markup that a
  browser would read with another structure is how mutation XSS starts, so sanitizing the output again changes nothing:

<!-- ref:../../tests/Meziantou.Framework.HtmlSanitizer.Tests/HtmlSanitizerTests.cs;region=HtmlSanitizer_Idempotent;format=md-fenced-code-block;dedent=true -->
```csharp
var sanitizer = new HtmlSanitizer();

// object is not allowed: unwrapping it would put a link in a link, so the inner link is unwrapped too
var once = sanitizer.SanitizeHtmlFragment("<a href='https://a.example'>x<object><a href='https://b.example'>y</a></object></a>");
Assert.Equal("""<a href="https://a.example">xy</a>""", once);

// The output is read back as the same elements, so sanitizing it again changes nothing
Assert.Equal(once, sanitizer.SanitizeHtmlFragment(once));
```
<!-- endref -->

Text and attribute values are encoded when the output is written, so text can never become markup and a value can
never close its quotes. Deeply nested fragments, such as thousands of nested elements, do not overflow the stack.

## What the guarantees depend on

- **Your configuration.** Each element or attribute that you allow is kept as it is. An attribute in `ValidAttributes`
  but not in `UriAttributes` is not validated, `style` keeps any CSS, and `data-` lets through the attributes that some
  frameworks run. See [Allowed elements](04-AllowedElements.md), [Allowed attributes and styles](05-AllowedAttributesAndStyles.md)
  and [URLs](06-Urls.md#validate-your-own-url-attributes).
- **Where you insert the output.** It is HTML for the content of an element of a page that starts with
  `<!DOCTYPE html>`. In a page in quirks mode, in an attribute value, in a `script` or `style` element, or in an `svg`
  or `math` element, the browser reads it with other rules. See
  [Getting started](02-GettingStarted.md#insert-the-result-in-a-page).
- **What you do with it afterward.** Decoding the output, or concatenating it with other untrusted text, produces HTML
  that was never sanitized. Sanitize the final fragment.

## What the sanitizer does not protect you from

- **Client-side templates.** Text is kept as text. A framework that compiles the markup of the page as a template, such
  as AngularJS or Vue with in-DOM templates, evaluates the expressions that the text contains. Do not insert user
  content in a part of the page that such a framework compiles (see the example below).
- **Requests made by images.** An allowed `img` loads its URL as soon as the page is shown: a remote image tells its
  site who reads the page and when, and a relative URL such as `/account/logout` sends a request to your own site with
  the cookies of the reader. Do not change state on a `GET` request, and remove `img` from `ValidElements` when the
  content must not load anything.
- **Misleading links.** Links are kept as written, to any site, with the text the author chose. The sanitizer never
  adds attributes, such as `rel="nofollow ugc"` on the links of user content. `file:`, `ftp:` and `tel:` URLs are
  allowed.
- **Classes of your stylesheets.** The value of `class` is not checked. See
  [Styles](05-AllowedAttributesAndStyles.md#styles).
- **Large inputs.** There is no limit on the size of the fragment. Reject inputs that are larger than you expect before
  you sanitize them.

For instance, a template expression in the text is kept as it is:

<!-- ref:../../tests/Meziantou.Framework.HtmlSanitizer.Tests/HtmlSanitizerTests.cs;region=HtmlSanitizer_TemplateExpressions;format=md-fenced-code-block;dedent=true -->
```csharp
var sanitizer = new HtmlSanitizer();

// Text is kept as text: a client-side framework that compiles this markup as a template would evaluate it
var result = sanitizer.SanitizeHtmlFragment("<p>{{constructor.constructor('alert(1)')()}}</p>");
Assert.Equal("<p>{{constructor.constructor('alert(1)')()}}</p>", result);
```
<!-- endref -->

## Defense in depth

Use a Content Security Policy that does not allow inline scripts in the pages that show user content. If a mistake in
the configuration or in the way the output is used lets a script through, the browser still refuses to run it.
