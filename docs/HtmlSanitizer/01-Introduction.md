---
title: Introduction
description: A .NET library that removes the dangerous parts of untrusted HTML fragments to prevent cross-site scripting.
---

`Meziantou.Framework.HtmlSanitizer` cleans HTML that you did not write, such as a comment, a rich-text field or a
product description, so you can insert it in your pages without letting it run script (cross-site scripting, or XSS).
It keeps the safe structure of the fragment (paragraphs, formatting, lists, tables, links and images) and removes the
rest.

## What you can do with it

- **Sanitize an HTML fragment** with the default policy, built for user content. See
  [Getting started](02-GettingStarted.md) and [The default policy](03-DefaultPolicy.md).
- **Choose the elements** that are kept, unwrapped or removed with their content. See
  [Allowed elements](04-AllowedElements.md).
- **Choose the attributes** that are kept, by name or by prefix, and decide what to do with styles. See
  [Allowed attributes and styles](05-AllowedAttributesAndStyles.md).
- **Validate URLs** in links, images and responsive images, and in the URLs that you write in your own markup. See
  [URLs](06-Urls.md).

## Principles

- **Allow list, not block list.** An element or an attribute is kept only when it is in the allowed sets. Anything the
  sanitizer does not know about, such as a new event handler attribute, is removed.
- **The fragment is read as a browser reads it.** It is parsed with the HTML parser of
  `Meziantou.Framework.WebPlatform`, which follows the HTML standard, so malformed markup, entities and comments are
  understood the same way as in the browser that shows the result.
- **Content is kept when it can be.** An element that is not allowed is unwrapped: its tag is dropped and its content
  is kept and sanitized. Only the elements whose content is unsafe, such as `script` and `style`, are removed with their
  content.
- **An unsafe URL empties its attribute.** The element is kept, with an empty value.
- **The output is stable.** It is written with the HTML serialization algorithm, and a browser reads it back as the same
  elements: sanitizing it again changes nothing.

The guarantees, and what the sanitizer cannot protect you from, are listed in
[Security guarantees and limits](07-SecurityGuaranteesAndLimits.md).

## Namespace

[HtmlSanitizer](cref:Meziantou.Framework.Sanitizers.HtmlSanitizer) and [UrlSanitizer](cref:Meziantou.Framework.Sanitizers.UrlSanitizer) are in the [Meziantou.Framework.Sanitizers](cref:Meziantou.Framework.Sanitizers) namespace.

| Type | Use it to |
| --- | --- |
| [HtmlSanitizer](cref:Meziantou.Framework.Sanitizers.HtmlSanitizer) | Sanitize an HTML fragment, and configure its allowed elements, attributes and URL attributes |
| [UrlSanitizer](cref:Meziantou.Framework.Sanitizers.UrlSanitizer) | Check a single URL or a `srcset` value |

## Next steps

- [Getting started](02-GettingStarted.md): install the package and sanitize your first fragment.
- [The default policy](03-DefaultPolicy.md): what is kept, unwrapped and removed.
- [Security guarantees and limits](07-SecurityGuaranteesAndLimits.md): what to check before you go to production.
