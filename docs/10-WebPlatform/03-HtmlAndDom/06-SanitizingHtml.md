---
title: Sanitizing HTML
description: Insert markup that comes from users without scripts, with the HTML Sanitizer API, and restrict the allowed elements and attributes.
---

Markup written by users (comments, messages, rich text) must not run scripts in your pages. `SetHtml` parses the markup
and removes everything that can run a script, with the [HTML Sanitizer API](https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#html-sanitization)
of the browsers:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Html/SanitizerTests.cs;region=WebPlatform_SanitizeHtml;format=md-fenced-code-block;dedent=true -->
```csharp
var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><div id=comment></div>");
var comment = document.GetElementById("comment")!;

// SetHtml removes what can run scripts: script elements, event handlers, javascript: URLs...
comment.SetHtml("""<p onclick="steal()">Nice post! <a href="javascript:steal()">Link</a><script>steal()</script></p>""");
Assert.Equal("<p>Nice post! <a>Link</a></p>", comment.InnerHtml);

// A configuration restricts the elements and the attributes further
comment.SetHtml("<h1>Title</h1><p title=t>Some <b>bold</b> and <i>italic</i> text</p>", new SetHtmlOptions
{
    Sanitizer = new SanitizerConfig { Elements = ["p", "b"], Attributes = [] },
});
Assert.Equal("<p>Some <b>bold</b> and  text</p>", comment.InnerHtml);

// Parse a whole document with the same rules
var page = Document.ParseHtml("<title>Post</title><p onclick=steal()>Hello</p>");
Assert.Equal("<p>Hello</p>", page.Body!.InnerHtml);
```
<!-- endref -->

## Safe and unsafe methods

| Methods | Sanitized |
| --- | --- |
| `Element.SetHtml`, `Document.ParseHtml` | Always: the elements and attributes that can run scripts are removed, whatever the configuration |
| `BeforeHtml`, `AfterHtml`, `ReplaceWithHtml`, `AppendHtml`... | The same, for the positional insertions |
| `Element.SetHtmlUnsafe`, `Document.ParseHtmlUnsafe` and the `...HtmlUnsafe` methods | Only by the configuration they are given |
| `InnerHtml`, `OuterHtml`, `InsertAdjacentHtml` | Never: for trusted markup only |

The default configuration is a list of allowed elements and attributes. It removes the `script` elements, the event
handler attributes (`onclick`...), the `javascript:` URLs, the `iframe` elements and the comments. `class`, `id` and the
`data-*` attributes are not in the list either: give a configuration to keep them.

## Configure the sanitizer

A `SanitizerConfig` lists what is allowed or removed. Give it in `SetHtmlOptions.Sanitizer`, or create a `Sanitizer`
from it to reuse it:

| Option | Effect |
| --- | --- |
| `Elements`, `Attributes` | The elements and attributes that are kept: the others are removed |
| `RemoveElements`, `RemoveAttributes` | The elements and attributes that are removed: the others are kept |
| `ReplaceWithChildrenElements` | The elements that are replaced by their content (keep the text of a `b`, without the `b`) |
| `Comments`, `DataAttributes` | Whether the comments and the `data-*` attributes are kept |

A `Sanitizer` can also be changed with its methods (`AllowElement`, `RemoveElement`, `AllowAttribute`, `SetComments`...),
and `Get()` returns its configuration. A configuration that has both `Elements` and `RemoveElements`, or that lists an
element twice, is invalid: the `Sanitizer` constructor throws a `TypeErrorException`.
