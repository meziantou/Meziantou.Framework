---
title: Sanitizing HTML
description: Insert markup that comes from users without scripts, with the HTML Sanitizer API, and restrict the allowed elements and attributes.
---

Markup written by users (comments, messages, rich text) must not run scripts in your pages. [SetHtml](cref:Meziantou.Framework.WebPlatform.Dom.Element.SetHtml) parses the markup
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
| [Element.SetHtml](cref:Meziantou.Framework.WebPlatform.Dom.Element.SetHtml), [Document.ParseHtml](cref:Meziantou.Framework.WebPlatform.Dom.Document.ParseHtml) | Always: the elements and attributes that can run scripts are removed, whatever the configuration |
| [BeforeHtml](cref:Meziantou.Framework.WebPlatform.Dom.Element.BeforeHtml), [AfterHtml](cref:Meziantou.Framework.WebPlatform.Dom.Element.AfterHtml), [ReplaceWithHtml](cref:Meziantou.Framework.WebPlatform.Dom.Element.ReplaceWithHtml), [AppendHtml](cref:Meziantou.Framework.WebPlatform.Dom.Element.AppendHtml)... | The same, for the positional insertions |
| [Element.SetHtmlUnsafe](cref:Meziantou.Framework.WebPlatform.Dom.Element.SetHtmlUnsafe), [Document.ParseHtmlUnsafe](cref:Meziantou.Framework.WebPlatform.Dom.Document.ParseHtmlUnsafe) and the `...HtmlUnsafe` methods | Only by the configuration they are given |
| [InnerHtml](cref:Meziantou.Framework.WebPlatform.Dom.Element.InnerHtml), [OuterHtml](cref:Meziantou.Framework.WebPlatform.Dom.Element.OuterHtml), [InsertAdjacentHtml](cref:Meziantou.Framework.WebPlatform.Dom.Element.InsertAdjacentHtml) | Never: for trusted markup only |

The default configuration is a list of allowed elements and attributes. It removes the `script` elements, the event
handler attributes (`onclick`...), the `javascript:` URLs, the `iframe` elements and the comments. `class`, `id` and the
`data-*` attributes are not in the list either: give a configuration to keep them.

## Configure the sanitizer

A [SanitizerConfig](cref:Meziantou.Framework.WebPlatform.Dom.SanitizerConfig) lists what is allowed or removed. Give it in [SetHtmlOptions.Sanitizer](cref:Meziantou.Framework.WebPlatform.Dom.SetHtmlOptions.Sanitizer), or create a [Sanitizer](cref:Meziantou.Framework.WebPlatform.Dom.Sanitizer)
from it to reuse it:

| Option | Effect |
| --- | --- |
| [Elements](cref:Meziantou.Framework.WebPlatform.Dom.SanitizerConfig.Elements), [Attributes](cref:Meziantou.Framework.WebPlatform.Dom.SanitizerConfig.Attributes) | The elements and attributes that are kept: the others are removed |
| [RemoveElements](cref:Meziantou.Framework.WebPlatform.Dom.SanitizerConfig.RemoveElements), [RemoveAttributes](cref:Meziantou.Framework.WebPlatform.Dom.SanitizerConfig.RemoveAttributes) | The elements and attributes that are removed: the others are kept |
| [ReplaceWithChildrenElements](cref:Meziantou.Framework.WebPlatform.Dom.SanitizerConfig.ReplaceWithChildrenElements) | The elements that are replaced by their content (keep the text of a `b`, without the `b`) |
| [Comments](cref:Meziantou.Framework.WebPlatform.Dom.SanitizerConfig.Comments), [DataAttributes](cref:Meziantou.Framework.WebPlatform.Dom.SanitizerConfig.DataAttributes) | Whether the comments and the `data-*` attributes are kept |

A [Sanitizer](cref:Meziantou.Framework.WebPlatform.Dom.Sanitizer) can also be changed with its methods ([AllowElement](cref:Meziantou.Framework.WebPlatform.Dom.Sanitizer.AllowElement), [RemoveElement](cref:Meziantou.Framework.WebPlatform.Dom.Sanitizer.RemoveElement), [AllowAttribute](cref:Meziantou.Framework.WebPlatform.Dom.Sanitizer.AllowAttribute), [SetComments](cref:Meziantou.Framework.WebPlatform.Dom.Sanitizer.SetComments)...),
and [Get()](cref:Meziantou.Framework.WebPlatform.Dom.Sanitizer.Get) returns its configuration. A configuration that has both [Elements](cref:Meziantou.Framework.WebPlatform.Dom.SanitizerConfig.Elements) and [RemoveElements](cref:Meziantou.Framework.WebPlatform.Dom.SanitizerConfig.RemoveElements), or that lists an
element twice, is invalid: the [Sanitizer](cref:Meziantou.Framework.WebPlatform.Dom.Sanitizer) constructor throws a [TypeErrorException](cref:Meziantou.Framework.WebPlatform.Dom.TypeErrorException).
