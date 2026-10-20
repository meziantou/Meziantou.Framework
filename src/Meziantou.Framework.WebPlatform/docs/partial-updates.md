# HTML insertion methods and partial updates

The library implements the HTML insertion methods of the HTML Standard, the streaming methods proposed for the HTML
Standard and the `<template for>` patching of the HTML Standard. They are available from .NET and from the scripts. The
streaming methods are [experimental .NET APIs](experimental-apis.md) (`WEBPLATEXP0002`).

## Positional methods

The positional methods parse HTML as `setHTML()` and `setHTMLUnsafe()` do, and insert the result at a position instead
of replacing the children (https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#html-insertion-methods):

| Method | Inserts the result |
|---|---|
| `Element.AppendHtml()`, `ShadowRoot.AppendHtml()` | after the last child |
| `Element.PrependHtml()`, `ShadowRoot.PrependHtml()` | before the first child |
| `BeforeHtml()` (`Element`, `CharacterData`) | before the node |
| `AfterHtml()` (`Element`, `CharacterData`) | after the node |
| `ReplaceWithHtml()` (`Element`, `CharacterData`) | in place of the node |

Each method has an `Unsafe` variant (`AppendHtmlUnsafe()`...) that takes `SetHtmlUnsafeOptions` (no default sanitizer,
`RunScripts`) and a `TrustedHtml` overload, as `SetHtmlUnsafe()` (see [trusted-types.md](trusted-types.md)). The
methods of a `template` element insert into its template contents. The context element of the parsing is the element
(the host of a shadow root), or the parent of the node for `BeforeHtml()`, `AfterHtml()` and `ReplaceWithHtml()`: they do
nothing when the node has no parent, and throw a `HierarchyRequestError` when the parent is a document, a `template`
element or a document fragment without a host. The safe methods do nothing in a `script` element.

```csharp
list.AppendHtml("<li>New item</li>");
heading.AfterHtml("<p>Inserted after the heading</p>");
```

## Streaming methods

The streaming methods return an `HtmlStreamWriter` whose chunks are parsed as they are written
(https://github.com/whatwg/html/pull/12758): `StreamHtml()` (it removes the children first), `StreamAppendHtml()` and
`StreamPrependHtml()` on `Element` and `ShadowRoot`, `StreamBeforeHtml()`, `StreamAfterHtml()` and
`StreamReplaceWithHtml()` (it removes the node first) on `Element` and `CharacterData`, and their `Unsafe` variants. In
JavaScript, they return a `WritableStream` (`element.streamAppendHTMLUnsafe().getWriter()`), whose chunks are converted
to strings.

```csharp
var writer = element.StreamAppendHtmlUnsafe();
writer.Write("<ul><li>First");
writer.Write("</li><li>Second</li></ul>");
writer.Close();
```

- The chunks are parsed by a fragment parser whose root is the target: the nodes are inserted directly into the target,
  before the reference child (the first child, the node, or its next sibling at the time of the call), as they are
  parsed. An element that the parser has not closed yet is already in the tree, and the text of a chunk is inserted when
  the chunk is written.
- `Write()` throws a `HierarchyRequestError` (and the stream rejects) when the reference child is no longer a child of
  the target; the parser is aborted. `Abort()` stops the parser: the rest of the input is not parsed, and the `script`
  elements that are still open never run.
- The elements are created in the node document of the target, and the custom element constructors run synchronously,
  as for the document parser. The sanitizer is applied while parsing, with a copy of the
  configuration taken when the method is called; the elements that it removes are created in an inert document, so
  neither they nor their descendants have side effects (e.g. an image fetch).
- The scripts run only with `RunScripts` in a connected target: a streamed script runs when its end tag is parsed, as a
  script inserted by a script. The parser does not pause for an external script, and the `defer` attribute is ignored.
- Streaming into a `script` element throws a `NotSupportedError`.
- When Trusted Types are enforced, the unsafe streaming methods require a `TrustedHtmlParserOptions` or a default policy
  with a `createParserOptions` callback, which is called before the target is checked (see
  [trusted-types.md](trusted-types.md#parser-options)).

## `<template for>`

A `template` element with a `for` attribute patches the content marked by processing instructions
(https://html.spec.whatwg.org/multipage/scripting.html#the-template-element): `<?marker name="x">` marks a location, and
`<?start name="x">`...`<?end>` a range (until the end of the parent without `<?end>`). When the parser inserts the
template, the range is emptied, and the content of the template is inserted at the marker (or in the range) instead of
the template contents; the markers are removed when the template is closed. The markers are found in the tree into
which the parser inserts the template: the document for the document parser and `document.write()`, the fragment for
`innerHTML`, `setHTMLUnsafe()`, the positional methods and `createContextualFragment()` (the markers of the document
are not patched), and the target for the streaming methods, so a document can be updated out of order:

```csharp
var writer = document.Body!.StreamAppendHtmlUnsafe();
writer.Write("<template for=\"results\"><li>Result 1</li><li>Result 2</li></template>");
writer.Close();
```

### Tentative extensions

The tentative attributes of the declarative partial updates
(https://github.com/WICG/declarative-partial-updates/blob/main/fragment-include-explainer.md) are supported by the parser
for a template that patches content (a `for` attribute whose markers are found; the other templates ignore them). They are
read from the start tag (a later change of the attributes has no effect), and they can be disabled with
`WebPlatformFeatures.DeclarativePartialUpdatesEnabled` (`BrowsingContextOptions.Features`), which also removes
`HTMLTemplateElement.buffer` from the scripts.

- `buffer` (`HTMLTemplateElement.buffer` reflects it): the template is inserted where it is, and its content is parsed into
  its template contents (`template.content` shows it as it is parsed). When the template is closed, the content between the
  markers is replaced by the template contents at once (one mutation), the markers and the template are removed, and the
  scripts of the content run when it is inserted. Removing the template before it is closed cancels the patch.
- `sanitize` (a boolean attribute: absent, or the empty string or `sanitize` in any case; another value such as `unsafe`
  does not sanitize): the content is sanitized while it is parsed, as by `setHTML()`, with the built-in safe default
  configuration (the unsafe elements and attributes are removed, so the scripts do not run and the removed custom elements
  are not constructed). Inside the content of another sanitizer (`setHTML()`, a sanitized template), the configuration of
  that sanitizer without its unsafe elements and attributes is used instead: an inner template cannot escape the
  sanitization of the outer one (the default configuration removes the `template` elements themselves).
- `src`: the content is fetched instead of the children of the template (they are ignored), asynchronously (the parser is
  not blocked), with a CORS request checked by the content security policies as a script (`script-src`, with the `nonce`
  of the template) and the `integrity` and `referrerpolicy` attributes of the template. The template stays in the document
  while the content is fetched; the content between the markers is replaced when the first bytes arrive, and the content is
  parsed as it arrives (all at once with `buffer`, or with `integrity`, whose check needs the whole body), as by
  `streamAppendHTMLUnsafe()` with `runScripts` (or `streamAppendHTML()` with `sanitize`). The markers and the template are
  removed at the end of the content; a failed fetch changes nothing; removing the template cancels the fetch (the content
  already inserted stays). Unlike the explainer, the fetched content is not sanitized by default (as the tests expect).
- The in-place templates of the proposal (an empty `for` attribute: the content is inserted before the template) are not
  supported: they contradict the HTML Standard, where an empty marker name patches nothing (the template is an ordinary
  template, as in the browsers).
