# Browsing contexts

By default, a parsed document has no browsing context: `Document.DefaultView` and `Document.Location` are null, and
`iframe` elements have no content document. A minimal browsing context can be created to get a `Window`, nested
documents for the `iframe` and `frame` elements, `Location`, `History` and same-document navigations. It never
fetches anything by itself, and executes scripts only when a script engine is configured (see [scripting.md](scripting.md)).

## Opt-in

Create a `BrowsingContext` (a top-level browsing context) and navigate it to load a document. `HtmlDocumentParser` and
`XmlDocumentParser` always create documents without a browsing context.

```csharp
using var context = new BrowsingContext(new BrowsingContextOptions
{
    // Optional: provides the documents of the navigations (iframe src, location.href...) with HTTP requests
    DocumentLoader = DocumentLoader.Create(),

    // Optional: the cookies of the documents, sent and stored by the loader
    CookieJar = new CookieJar(),

    // Optional: the local storage areas (window.localStorage); by default, each browsing context created by the application has its own
    LocalStorage = new StorageShed(),
});

// The callback returns the content of the document, given the URL resolved against the URL of the active document
var document = context.Navigate("https://example.com/", url => html);

// The load events and the nested documents are created by tasks, and the documents may be loaded asynchronously
await context.EventLoop.RunUntilIdleAsync();

var window = document.DefaultView!;                 // context.Window
var iframe = (HtmlIFrameElement)document.QuerySelector("iframe")!;
var frameDocument = iframe.ContentDocument;         // a nested browsing context
```

The `BrowsingContext.Navigate` overloads load the document synchronously, and return it:

- `Navigate(url, Func<URL, string>)` loads an HTML document from its text.
- `Navigate(url, Func<URL, DocumentResource?>)` loads a document as if it was returned by the document loader: HTML, XML
  or text, from its text or its bytes (see [the document loader](#the-document-loader)). A null resource is a network
  error.
- `Navigate(Func<URL, string>)` and `Navigate(Func<URL, DocumentResource?>)` do the same with the `about:blank` URL.
- `NavigateAsync(url, ...)` also waits until the parser has finished, when a script engine waits for a parser-blocking
  script (see [scripting.md](scripting.md#the-parser)).
- `Navigate(url)` loads the document provided by `BrowsingContextOptions.DocumentLoader`, from a task of the event loop.

As in a browser, the navigation aborts the ongoing navigation of the browsing context, and it adds an entry to the session
history (or replaces the current entry, for the initial `about:blank` document or the same URL). The navigations of a
nested browsing context (`BrowsingContext.Children`) work the same way.

The loaded document becomes the active document of the browsing context. When the parser ends, its `readyState` is
`interactive`; the `DOMContentLoaded` event, the `load` events (of the `iframe` elements, then of the window) and the
navigations of the nested browsing contexts are run by the tasks of the event loop of the browsing context
(`BrowsingContext.EventLoop`): call `RunUntilIdleAsync()` to run them, including the tasks queued when an asynchronous
document load completes. `RunUntilIdle()` (or `RunTask()`) runs the tasks that are already queued, and
`DomEventLoop.HasPendingOperations` tells whether document loads are in progress.

A `BrowsingContext` that has not navigated yet has an initial `about:blank` document.

## Event loops

Each top-level `BrowsingContext` is the root of a browsing context group and creates its own `DomEventLoop`
(`BrowsingContext.EventLoop`), shared by its nested browsing contexts. The tasks, the microtasks and the timers of the
documents of a page run on its event loop only: two pages do not run each other's tasks, and the
`BrowsingContextOptions.TimeProvider` option sets the clock of its own event loop. A document created by a script of
the page (e.g. `DOMParser`, `document.implementation.createHTMLDocument()`) uses the event loop of the window that
created it.

The specification keys the agents of a browsing context group by site, each with its own event loop. The library runs
all the documents of a group on a single event loop, which is one of the orders the specification allows, as the
documents of a page are used from a single thread.

Each worker (see [Workers](#workers)) runs its own event loop on its own thread. The event loop of its owner (the event
loop of the page, or of the worker that created it) considers that an asynchronous operation is in progress while the
worker has a task to run or waits for an operation, so `RunUntilIdleAsync()` does not return while a worker may still
post a message. `PromoteDueTimers()` and `WaitForNextTimerAsync()` include the timers of the workers, so that a host
that advances a fake clock to the next timer also runs the timers of the workers; `DomEventLoop.HasRunningWorkers` tells
whether a worker is running a task (a host should not advance a fake clock meanwhile). A worker may run a task between
two reads (e.g. its timer becomes due when another thread reads the clock) and add a timer meanwhile:
`DomEventLoop.WorkerActivity` changes each time a worker stops running, so a host reads it before computing the next
timer and does not advance the clock if it changed.

The rendering opportunities (the animation frames) are timers of the event loop, aligned on a fixed rate from the first
frame. A frame captures one timestamp, shared by the timelines of the animations and the `requestAnimationFrame()`
callbacks (`document.timeline.currentTime` is the time given to the callbacks): it updates the animations and sends
their events ([update animations and send events](https://drafts.csswg.org/web-animations-1/#update-animations-and-send-events)),
runs the animation frame callbacks, then updates the styles to create the CSS animations and start the CSS transitions
(see [Animations and transitions](computed-style.md#animations-and-transitions)). A frame is requested while an
animation is pending or running, or when events are queued (the paused, finished and canceled animations do not need
frames): an infinite running animation keeps requesting frames, so `WaitForNextTimerAsync()` keeps returning true while
it runs. A trusted event of the user input also requests a frame, which sets the duration of its
[Event Timing](#performance-timeline) entry. With a fake `TimeProvider`, advancing the clock runs the frames that are due.

A host that runs an event loop on its own thread (instead of calling `RunUntilIdleAsync()`) sets `DomEventLoop.WorkSignal`:
the callback is invoked, from any thread, when a task or a timer is queued (e.g. by a document loaded by the
`DocumentLoader` on another thread) or when an asynchronous operation ends, so that the thread wakes up and calls
`RunTask()` and `PromoteDueTimers()`. The callback must not run the event loop itself. The Playwright server
(Meziantou.Framework.WebPlatform.Playwright) runs the pages of each browser context this way.

### Render blocking

The [render-blocking mechanism](https://html.spec.whatwg.org/multipage/dom.html#render-blocking-mechanism) is
implemented: a document that is loaded by a navigation is render-blocked while its render-blocking element set is not
empty, or while it has no body element. The rendering updates skip a render-blocked document: its animation frame
callbacks do not run, its animations and transitions do not start, the resize, scroll, fullscreen and media query steps,
the resize and intersection observers, the paint timing (`first-paint`, `first-contentful-paint`...), the `pagereveal`
event and the pending operations of its view transitions wait. A rendering update is requested when the document is
no longer render-blocked, and the steps that waited run.

- The elements that block the rendering, when they are fetched or prepared while the document has no body element yet:
  the `link` elements of style sheets and the `style` elements (until their imported style sheets are fetched) that
  were created by the parser or have `blocking=render`, when their `media` matches; the scripts that have
  `blocking=render` and the parser-inserted classic scripts without `async` or `defer` attribute, until they run. An
  element no longer blocks when it is removed from the document or when its `blocking` attribute is changed and it is not
  implicitly render-blocking. As in the browsers, an alternative style sheet that is not enabled does not block.
- `<link rel=expect href="#id" blocking=render>` blocks the rendering while the document is loading and the element with
  the ID (or the `a` element with the name) is not in the document, or is still on the stack of open elements of the
  parser: the rendering starts when the end tag of the expected element is parsed, or when the document is parsed
  (https://html.spec.whatwg.org/multipage/links.html#link-type-expect). The link is processed again when its `href`,
  `media`, `rel` or `blocking` attribute changes (the specification only lists `href` and `media`), and a URL without
  fragment does not block, as in the browsers (in the specification, its indicated part is not an element, so it would
  block until the document is parsed).
- The timeout of the specification, which is implementation-defined, is not a duration: a document is never
  render-blocked again once a rendering update found it not render-blocked (it was rendered), or once its readiness is
  `complete`, whichever comes first. A document whose render-blocking resource never loads is never rendered by the
  event loop (its load event is delayed too).
- As in the browsers, which start to render a document when its parser inserts the body element, a document that is
  being parsed is also render-blocked until its parser inserts its body element, although a script inserted one
  before (the specification only considers whether the document has a body element): a parser-blocking script of the
  head blocks the rendering even when a script inserts a body element while it is loading.
- The documents that are not loaded by a navigation are never render-blocked: the documents without browsing context,
  the initial `about:blank` documents and the documents of `document.open()`. Render blocking only applies to the
  rendering updates of the event loop: the layout, the computed styles, the geometry and the display list of a
  render-blocked document are still computed when a script or the host asks for them (e.g. `GetBoundingClientRect()`,
  a screenshot), so a host that never runs the event loop, or that renders a document right after `Navigate()`, is not
  affected.
- `PerformanceResourceTiming.renderBlockingStatus` is `blocking` for the scripts and the style sheet links that were
  render-blocking when their fetch started.

A document that is not associated with a browsing context (e.g. parsed by `HtmlDocumentParser` or `XmlDocumentParser`) has no
event loop: it is only a DOM tree. Nothing is queued for its nodes: the callbacks of the mutation observers are not
invoked (`MutationObserver.TakeRecords()` still returns the records), and the `slotchange`, `load` (style sheets),
`select` and `error` events are not fired. The custom element reactions of the operations still run synchronously. Its
`document.timeline` is inactive: the animations of its elements do not progress.

## Disposal

`BrowsingContext.Dispose()` discards a page: it destroys the documents of the top-level browsing context and of its
nested browsing contexts, cancels the token given to the document loader, and disposes the event loop. The pending
tasks, microtasks, timers and animation frames are discarded, the ones queued afterwards are ignored
(`DomEventLoop.IsDisposed`), and a pending `RunUntilIdleAsync()` returns. No event is fired: to run the `pagehide` and
`unload` event handlers first, call `Window.Close()` and run the event loop before disposing. The nested browsing
contexts are destroyed with their `iframe` element: `Dispose()` throws for them.

`BrowsingContext.TryClose(runBeforeUnload)` closes a top-level browsing context as the user closes a tab: with
`runBeforeUnload`, the `beforeunload` event is fired first, and the user can cancel the closing (see
[Dialogs](#dialogs)); then the documents are unloaded (`pagehide`, `unload`) and destroyed. It does not dispose the event loop:
the other top-level browsing contexts of the group (its popups, or the page that opened it) keep running.

## Nested browsing contexts

When an `iframe` or a `frame` element is connected to a document of a browsing context, a nested browsing context is
created with an initial `about:blank` document, and it is destroyed when the element is removed:

- Without `src` (or with `about:blank`), the `load` event of the `iframe` is fired synchronously.
- With `srcdoc`, an `about:srcdoc` document is parsed from the attribute. Its base URL and its origin are the ones of
  the document of the `iframe`.
- With `src`, the document is provided by `BrowsingContextOptions.DocumentLoader`.
- With `loading="lazy"` (when scripting is enabled), the navigation to the `srcdoc` or the `src` is deferred until the
  `iframe` is about to intersect the viewport (see [Lazy loading](#lazy-loading)); such an `iframe` does not delay the load
  event of its document, and another navigation of its content navigable (not a reload) cancels the deferred one.

`HtmlIFrameElement.ContentWindow`, `ContentDocument` (null when the origin differs) and `GetSvgDocument()`,
`Window.Length`, the indexer of `Window` (`window[0]`), `Window.Parent`, `Window.Top`, `Window.FrameElement` and
`Window.GetNamedItem()` (`window.name`) expose the tree of browsing contexts (`BrowsingContext.Children` and
`BrowsingContext.Parent` from .NET). The `object` and `embed` elements never have a nested browsing context.

- The indexed windows (`window[0]`, `frames[1]`) are in the order in which their containers were inserted into the
  document (https://html.spec.whatwg.org/multipage/nav-history-apis.html#windowproxy-getownproperty), which is not the
  tree order of `BrowsingContext.Children` when an `iframe` is inserted before another one.
- The named properties of the window (https://html.spec.whatwg.org/multipage/nav-history-apis.html#named-access-on-the-window-object)
  are the child navigables with a target name, the `embed`, `form`, `img` and `object` elements with a `name`, and the
  elements with an `id`: a single element, or a live `HTMLCollection` when several elements have the name. Only the first
  child navigable of a name counts, and its name is exposed when its document is same origin with the document of the
  window (a cross-origin child hides the next children that have its name). A cross-origin caller gets any named child
  from the `WindowProxy` (`parent.name`), as in the browsers: the specification restricts it to the names exposed to the
  window itself.
- The named properties are own properties of the named properties object (`WindowProperties`, the prototype of
  `Window.prototype`) unless a property of its prototype chain (`EventTarget.prototype`, `Object.prototype`) has the
  name. Web IDL also hides the ones that the window or `Window.prototype` shadow, but the browsers do not (and the
  lookups from the window find the shadowing property first anyway).
- The named properties of the document (https://html.spec.whatwg.org/multipage/dom.html#dom-document-nameditem,
  `document.name`, the indexer of `Document` from .NET) are the `embed`, `form`, `iframe`, `img` and `object` elements of
  the document tree with a `name`, the `object` elements with an `id`, and the `img` elements with an `id` that also have
  a non-empty `name`: a single element (the window of its content navigable for an `iframe`), or a live
  `HTMLCollection` when several elements have the name (the same object for a name). `Document` is
  `[LegacyOverrideBuiltIns]`: a named property hides the properties of the prototype chain (`<img name=body>` hides
  `document.body`), but not the own properties of the document (`document.location`, which is `[LegacyUnforgeable]`,
  and the properties created by the scripts). The current specification has no "exposed" condition for the `embed`
  and `object` elements any more.
- The bindings ask the document whether a name is a named property on each property access (`document.body`,
  `document.createElement`...). A document that never had a candidate element (a `name` on one of the five elements,
  an `id` on an `object`) answers from a flag. The others answer from a map of the names to their elements
  (`DocumentNamedItemMap`), created on the first access and updated by the insertions, the removals and the changes of
  the `name` and `id` attributes of these elements: no access walks the tree, except the enumeration of the names
  (`Object.getOwnPropertyNames(document)`) and the collection of several elements, which is in tree order.

## Auxiliary browsing contexts

`Window.Open(url, target, features)` (`window.open()`) navigates the browsing context chosen by the target, or creates
an auxiliary browsing context (https://html.spec.whatwg.org/multipage/document-sequences.html#auxiliary-browsing-context)
for `_blank` and the unknown names: a new top-level browsing context of the browsing context group of the opener, with
an initial `about:blank` document that has the origin of the opener (an `about:blank` URL with a query or a fragment
only changes the URL of that document). It shares the options and the `DomEventLoop` of the
browsing context created by the application that owns the group, and it is discarded when that browsing context is
disposed. `BrowsingContext.IsAuxiliary` and `BrowsingContext.Opener` expose it from .NET, and
`BrowsingContext.CreatorBrowsingContext` is the browsing context that opened it, even without opener (`noopener`, the default of
the `_blank` links). `BrowsingContextOptions.BrowsingContextCreated` is invoked when it is created (see
[Host callbacks](#host-callbacks)).

- The `noopener` and `noreferrer` features create a browsing context without opener, and `window.open()` returns null.
- The other features of a new browsing context tell whether a popup is requested
  (https://html.spec.whatwg.org/multipage/nav-history-apis.html#popup-window-is-requested: `popup`, or the absence of
  `location`/`toolbar`, `menubar`, `scrollbars`, `status`, or `resizable=no`): `BrowsingContext.IsPopup`. Nothing is
  displayed, so it only changes the `BarProp` objects of the windows (`window.locationbar`, `menubar`, `personalbar`,
  `scrollbars`, `statusbar`, `toolbar`): `visible` is false in a popup. It is also false for a window without browsing
  context (a closed popup, a removed iframe), as in the browsers; the specification says true.
- The `width`, `height`, `left` and `top` features (and `innerWidth`, `innerHeight`, `screenX`, `screenY`) size the
  viewport of the new browsing context and position its client window
  (https://drafts.csswg.org/cssom-view/#set-up-browsing-context-features): `innerWidth`, `innerHeight` (also the
  viewport of the layout and of the media queries), `outerWidth`, `outerHeight` (the client window has no decoration),
  `screenX`, `screenY`. The values are integers (`width=405.5` is 405, an invalid value is 0, which keeps the default);
  a size is at least 100 CSS pixels and a position is not negative. The screen stays the viewport of the options, and
  the window is not kept inside it. The other top-level browsing contexts have the viewport of the options, at the
  origin of the screen. As in the browsers, a `resize` event is fired at the listeners that the opener adds to the new
  window right after `window.open()` (the client window is created, then sized).
- `window.open()` returns null while a document of the event loop is being unloaded (in the `beforeunload`, `pagehide`,
  `visibilitychange` and `unload` event listeners: the termination nesting level), and for a document sandboxed without
  `allow-popups` when the target needs a new browsing context. A navigation that the caller is not allowed to perform
  (see the sandboxing flags) throws a `SecurityError`.
- `window.opener` returns the window of the opener browsing context, or null once the opener is discarded (e.g. closed);
  setting it to null disowns the opener, another value defines an own property. `window.open()` targeting an existing
  navigable makes the calling navigable its opener, except when the target is an inclusive ancestor of the caller
  (`_self`, `_parent`, `_top`...), as in the browsers.
- `window.close()` closes an auxiliary browsing context (they are script-closable): `window.closed` is true at once,
  and a task fires `beforeunload` (the user can cancel the closing with the unload prompt, see [Dialogs](#dialogs)), then unloads the documents
  (`pagehide`, `visibilitychange`, `unload`) and destroys the browsing context. As in the browsers, `pagehide` and `unload`
  are also fired at a popup that still has its initial `about:blank` document (the specification fires no `pagehide` at
  a document that was never shown).
- The names of the browsing contexts are looked up as specified by "find a navigable by target name"
  (https://html.spec.whatwg.org/multipage/document-sequences.html#find-a-navigable-by-target-name): the descendants of the
  current navigable first, then the other navigables of its top-level traversable, then the navigables of the other
  top-level browsing contexts of the browsing context group that the current browsing context is familiar with (a
  navigable of a cross-origin popup is not found: a new popup is created).
- The target name (`window.name`) of a top-level traversable is cleared when a navigation changes the origin of its
  document, unless it is an auxiliary browsing context with an opener. As in the browsers, the navigation away from the
  initial `about:blank` document keeps it. The target name of a nested navigable is the `name` attribute of its
  container.
- A document sandboxed with `allow-popups` can navigate the popups it opens, and its sandboxing flags apply to them
  unless it uses `allow-popups-to-escape-sandbox`. As in the browsers, a document sandboxed without `allow-scripts`
  cannot run a `javascript:` URL in another navigable (e.g. in a popup that escapes its sandbox). Opening a popup
  consumes the user activation of the window.
- The sandboxing flags of a new document are the ones of its navigable when the navigation starts: changing the
  `sandbox` attribute of an `iframe` applies to its next navigation.
- A navigation to `about:srcdoc` that does not come from a `srcdoc` attribute (`location = 'about:srcdoc'`,
  `window.open('about:srcdoc')`) loads an error document, whose origin is opaque.
- The `Cross-Origin-Opener-Policy` header of a top-level document (in a secure context) is enforced
  (https://html.spec.whatwg.org/multipage/browsers.html#cross-origin-opener-policies): a navigation whose policy does not
  match the one of the current document switches browsing context group. The navigation continues in a new browsing
  context, without opener nor name, that the windows of the previous group cannot reach: the `WindowProxy` they hold keeps
  the previous window (`closed` is true), and the popups it opened lose their opener. A nested document that is
  cross-origin with its top-level document opens its popups without opener when the top-level document has the
  `same-origin` policy. The `Cross-Origin-Opener-Policy-Report-Only` header and the COOP reports (`report-to`) are not
  sent: HTML queues them for the endpoint of the policy "with" the URL of the response, which the Reporting API does not
  define.
- The `Cross-Origin-Embedder-Policy` header (`require-corp`, `credentialless`) is enforced for the cross-origin no-cors
  subresources (`fetch()`, images, scripts, style sheets...) with the `Cross-Origin-Resource-Policy` check, and for the
  documents of the iframes, which must have an embedder policy themselves (except in a `credentialless` iframe). A
  `credentialless` policy omits the cookies of the cross-origin no-cors requests. The documents of the local schemes
  (`about:blank`, `about:srcdoc`, `blob:`, `data:`) inherit the policy of their creator. A top-level document whose opener
  policy is `same-origin` with such an embedder policy is in a cross-origin isolated browsing context group:
  `window.crossOriginIsolated` is true for it and for the nested documents that are allowed to use the
  `cross-origin-isolated` feature (default `'self'`, see [Permissions Policy](#permissions-policy)). The violations of
  the policy and of the `Cross-Origin-Embedder-Policy-Report-Only` header are reported as `coep` reports (see
  [Reporting API](#reporting-api)): a response blocked by the `Cross-Origin-Resource-Policy` check (type `corp`, also for
  the opaque responses of `Cache.match()`), and a document or a dedicated worker without a compatible embedder policy
  (type `navigation` or `worker initialization`, reported to the container document or to the owner).
- The window of a browsing context is its `WindowProxy` (https://html.spec.whatwg.org/multipage/nav-history-apis.html#the-windowproxy-exotic-object),
  for every script, including the scripts of the window itself (`window`, `self`, `globalThis` and the `this` of the
  global code are the `WindowProxy`, the global object of the realm is the `Window`): one object per browsing context,
  which stays the same when the browsing context navigates (e.g. the value returned by `window.open()`,
  `iframe.contentWindow`, `event.source`). Its indexed properties are the windows of the child navigables (read-only,
  they cannot be defined or deleted), it cannot be made non-extensible, and its prototype cannot be changed. For a
  cross-origin caller, only the cross-origin properties (`location`, `postMessage`, `closed`, `close()`, `frames`,
  `parent`, `top`, `opener`, the child windows...) are accessible, the others throw a `SecurityError`, and its prototype is
  null. The functions and the accessors of the cross-origin properties are created for each realm that reads them
  (https://html.spec.whatwg.org/multipage/nav-history-apis.html#crossoriginpropertydescriptormap): they have the
  `Function.prototype` of the caller, and they run the steps of the IDL member even when the window replaced it. The
  `Location` objects are cross-origin objects too (only `href` can be set, and `replace()` called, cross-origin); as in
  the browsers, their `[LegacyUnforgeable]` members stay non-configurable for the same-origin callers (the
  specification reports the members of [[DefaultProperties]] as configurable).
  The `WindowProxy` of a discarded browsing context (a removed iframe, a closed popup) keeps its last window
  (`contentWindow.self === contentWindow` still holds, `closed` is true).

The history traversal (`history.back()`, `history.go()`) loads the URL of an entry of another document again (there is
no back/forward cache), and the new document replaces the document of the entry.

## Blob URLs

`URL.createObjectURL(blob)` (`Url.CreateObjectUrl` in .NET, which uses the document of the current global object) adds
the blob to the blob URL store of the browsing context group (the top-level browsing context and its nested browsing
contexts), until `URL.revokeObjectURL(url)` or the destruction of the document that created it. The navigations to a
`blob:` URL and the external scripts whose `src` is a `blob:` URL load the content of the blob, whose `type` is the
Content-Type (`text/plain` when it is empty); a URL that is not in the store is a network error. The loader is not
called. The blob of a navigation is resolved when the navigation starts: revoking the URL afterwards does not prevent the
document from loading. `URL.revokeObjectURL(url)` only revokes the URLs without fragment (as in the browsers, revoking
`blob:...#fragment` does nothing).

`FileReader` reads a blob as text (decoded with the BOM, the `encoding` argument, the `charset` of the type of the blob,
else UTF-8), a `data:` URL, an `ArrayBuffer` or a binary string. The events (`loadstart`, `progress`, `load`, `loadend`,
`abort`) are fired by the tasks of the event loop of the window that created the reader; a reader created without
current global object (e.g. from .NET without script engine) fires them synchronously.

## Data URLs

The navigations to a `data:` URL (e.g. `<iframe src="data:text/html,...">`) load the body of the URL, whose MIME type is
the Content-Type, without calling the loader. The document has an opaque origin.

## Hyperlinks

Clicking an `a` or `area` element that has an `href` attribute (a click by the user, `click()`, or a dispatched `click`
event) follows the hyperlink ([follow the hyperlink](https://html.spec.whatwg.org/multipage/links.html#following-hyperlinks-2)):
the browsing context chosen by the `target` attribute (or the `target` of the first `base` element) navigates to the URL
of the `href` attribute, with the referrer policy of the `referrerpolicy` attribute (`no-referrer` for `rel=noreferrer`).
The targets are `_self` (the default), `_parent`, `_top` and the names of the browsing contexts (`iframe` `name`
attribute, `window.name`) of the browsing context group; `_blank` and the unknown names open a new auxiliary browsing
context (see [Auxiliary browsing contexts](#auxiliary-browsing-contexts)), without opener for `_blank` unless
`rel=opener` (and never with `rel=noopener` or `rel=noreferrer`). The hyperlinks with a `download` attribute are not
followed (downloads are not supported).

The navigations are recorded in the history of the visited links when the browsing context has one
(`BrowsingContextOptions.VisitedLinks`; none by default): see [Visited links](computed-style.md#visited-links).

## Form submission

The submission of a form (`submit()`, `requestSubmit()`, the activation of a submit button, or the implicit submission
of a text field with the Enter key) runs the form submission algorithm
(https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#form-submission-algorithm): the `submit` event,
the entry list and the `formdata` event, then the navigation of the target browsing context (`target`, `formtarget`, the
`target` of the first `base` element) is planned (a later submission replaces it, and a navigation of the target that
starts in the meantime wins). The method, the action and the enctype are the ones of the submit button (`formmethod`,
`formaction`, `formenctype`) or of the form:

- `GET`: the entry list is serialized as `application/x-www-form-urlencoded`, in the encoding of the form
  (`accept-charset`, else the encoding of the document), as the query of the action URL;
- `POST`: the body is `application/x-www-form-urlencoded`, `multipart/form-data` (the files are included) or
  `text/plain`; the `DocumentLoader` receives the method, the body and its `Content-Type` (`DocumentRequest.Method`,
  `Body` and `BodyContentType`), and the HTTP loader sends them;
- `dialog`: the ancestor `dialog` is closed with the value of the submit button;
- `mailto:` and the other schemes as specified.

As for the hyperlinks, `_blank` and the unknown names open a new auxiliary browsing context. The forms of a sandboxed
iframe without `allow-forms` are not submitted.

## Images

The `img` elements of the documents of a browsing context load their image
([update the image data](https://html.spec.whatwg.org/multipage/images.html#update-the-image-data)): the source is
selected from the `src`, `srcset` and `sizes` attributes and the `source` elements of a parent `picture` element (with
the viewport and the device pixel ratio of `ViewportOptions`), and fetched by the loader (`RequestDestination.Image`,
the resource should be returned as bytes), or from a `data:` or `blob:` URL. The image is available when the response is
an image (a known signature: PNG, GIF, JPEG, BMP, WebP, ICO, AVIF, or an SVG document) and, with a rendering backend
(`BrowsingContextOptions.RenderingBackend`), when the decoder of the backend can read a raster image (see
[rendering.md](rendering.md#images): the pixels are decoded by the backend). Its natural dimensions are read from its
header: the size of the raster images (the EXIF orientation of a JPEG image, or of the `eXIf` chunk of a PNG image, can
swap them), the absolute `width` and `height` of the root of an SVG image and the aspect ratio of its `viewBox`.
The EXIF resolution of a JPEG image (or of the `eXIf` chunk of a PNG image) corrects its density
([prepare an image for presentation](https://html.spec.whatwg.org/multipage/images.html#prepare-an-image-for-presentation)):
when the `PixelXDimension` and `PixelYDimension` tags are the size of the image at 72 pixels per inch (`XResolution`,
`YResolution`, `ResolutionUnit` in inches), they are its natural dimensions, wherever the image is used (`img`, the CSS
images, the SVG `image` elements, the posters, the source rectangle of `drawImage()` and the size of an `ImageBitmap`): a
100x50 image at 144 pixels per inch is a 50x25 image, whose pixels are scaled when it is painted.
`naturalWidth`/`naturalHeight` are the density-corrected natural dimensions (divided by the pixel density of the selected
source, completed by the default sizing algorithm with a 300x150 default object size). `width`/`height` are the rendered
size of the image (from the layout), else the natural dimensions replaced by the `width` and `height` attributes. The
`load` and `error` events are fired, `currentSrc`, `complete` and `decode()` reflect the state of the request (`decode()`
also rejects when the backend cannot decode the pixels), and the images delay the load event of the document. The images of a document without browsing context are not loaded. The request is a CORS request when the
element has a `crossorigin` attribute, and its referrer policy is the one of the `referrerpolicy` attribute. The images
with `loading="lazy"` are fetched when they are about to intersect the viewport and do not delay the load event (see
[Lazy loading](#lazy-loading)).

### Lazy loading

The `img` and `iframe` elements whose `loading` attribute is `lazy` are [loaded lazily](https://html.spec.whatwg.org/multipage/urls-and-fetching.html#lazy-loading-attributes)
when scripting is enabled (`BrowsingContextOptions.ScriptingEnabled` and the document is not sandboxed without
`allow-scripts`): the fetch of the image (after the source selection) or the navigation of the `iframe` is stored as the
lazy load resumption steps of the element, and the element is observed by the lazy load intersection observer of its
document, an `IntersectionObserver` of the user agent whose scroll margin is 1250px (`LazyLoading.LazyLoadScrollMargin`):
the steps run when the element intersects the viewport of the top-level browsing context (or a scrollport) expanded by
this margin, as computed by the layout at a rendering update, or when the attribute changes to `eager`. A document with
lazy elements is therefore laid out by the rendering updates. With `WebPlatformFeatures.IntersectionObserverEnabled`
disabled, the `lazy` elements are loaded at once, as `eager` ones. The `audio` and `video` elements are loaded lazily
too (see [Media elements](#media-elements)).

The SVG `image` elements fetch the image of their `href` (or `xlink:href`) attribute the same way: the `load` event is
fired when the response is an image, the `error` event otherwise, and they delay the load event of the document. The
`poster` of a `video` element is fetched the same way (`RequestDestination.Image`), and painted while the element
represents its poster frame (see [Media elements](#media-elements)).

The images referenced by the computed styles of the rendered elements (and of their `::before` and `::after`
pseudo-elements) are fetched at the next rendering opportunity after the tree, the state of an element or a style sheet
changes, as when the user agent renders them ([fetch an external resource](https://drafts.csswg.org/css-values-4/#fetch-an-external-resource)):
`background-image` (including the `background` attribute), `border-image-source`, `list-style-image`, `content` and
`cursor` with the `no-cors` mode, `mask-image`, `mask-border-source` and `shape-outside` with the `cors` mode
(`RequestDestination.Image`, `DocumentRequest.Initiator` is the element). An `image-set()` fetches its first image. The
SVG documents of the elements that `fill`, `stroke`, the markers, `filter`, `backdrop-filter`, `clip-path`, `mask-image`
and `offset-path` reference in other documents (`fill: url(other.svg#gradient)`) are fetched the same way, with the `cors`
mode (see [layout.md](layout.md#svg-painting)). Each
The request URL modifiers of a `url()` (`cross-origin()`, `integrity()`, `referrer-policy()`) set the mode, the
credentials, the integrity metadata and the referrer policy of its request (see
[Request URL modifiers](computed-style.md#request-url-modifiers)). Each URL is fetched once per document, mode and
modifiers, and the images are not decoded. The elements of a `display: none` subtree
are not rendered, so their images are not fetched. As in the browsers, the fetches started before the load event delay it
(`Document.CssImagesDelayingTheLoadEvent`), and the images of the styles that changed since the last rendering opportunity
are fetched before the load event is queued: the layout at the load event uses them (the natural size of the image of a
generated content, the float area of a `shape-outside` image).

## Media elements

The library implements the processing model of the `audio` and `video` elements
([media elements](https://html.spec.whatwg.org/multipage/media.html#media-elements)), but it never decodes audio or video
by itself: the media resources are read and played by a media backend, `BrowsingContextOptions.MediaBackend`. Without a
backend (the default), the media elements have no media resource: nothing is fetched, no media event is fired,
`networkState` stays `NETWORK_EMPTY`, `canPlayType()` returns the empty string and `play()` is rejected with a
`NotSupportedError`.

```csharp
using var context = new BrowsingContext(new BrowsingContextOptions
{
    DocumentLoader = DocumentLoader.Create(),
    MediaBackend = new MyMediaBackend(),

    // Optional: play() and autoplay need a user gesture unless the element is muted (the default is Allowed)
    MediaAutoplayPolicy = MediaAutoplayPolicy.UserActivationRequired,
});

sealed class MyMediaBackend : MediaBackend
{
    // canPlayType(), and the type attribute of the source elements
    public override MediaTypeSupport CanPlayType(string mimeType, IReadOnlyList<string> codecs)
        => mimeType == "video/webm" ? (codecs.Count == 0 ? MediaTypeSupport.Maybe : MediaTypeSupport.Probably) : MediaTypeSupport.NotSupported;

    // The resource fetched by a media element: null when its format is not supported
    public override MediaPlayer? CreatePlayer(MediaResource resource) => MyPlayer.TryOpen(resource);
}

sealed class MyPlayer(MediaResource resource) : MediaPlayer(resource.TimeProvider)
{
    public override double Duration => ...;                     // seconds
    public override int VideoWidth => ...;                      // the natural size of the video
    public override int VideoHeight => ...;
    public override IReadOnlyList<MediaTrackInfo> AudioTracks => ...;
    public override MediaVideoFrame? GetVideoFrame() => ...;    // the frame painted in the box of the video element
}
```

- **The backend** (`Meziantou.Framework.WebPlatform.Media`): `MediaBackend.CanPlayType()` answers `canPlayType()` and
  filters the `source` elements; `MediaBackend.CreatePlayer()` opens a fetched resource (`MediaResource`: the bytes, the
  `Content-Type`, the URL, the clock of the event loop) and returns its `MediaPlayer`, or null when the format is not
  supported. The player gives the metadata (the duration, the natural dimensions of the video, the audio and video
  tracks, the buffered and seekable ranges, the ready state, the start date), holds the playback position (`CurrentTime`,
  `Play()`, `Pause()`, `Seek()`, `PlaybackRate`, `SetVolume()`...) and gives the frame of the video to paint
  (`MediaVideoFrame`: RGBA pixels). The playback members have a default implementation that decodes nothing: the position
  advances with the clock of the event loop (`BrowsingContextOptions.TimeProvider`) at the playback rate, up to the
  duration, so a backend that only reads the metadata of the resources is enough to run the scripts of a page. A player
  whose state changes by itself (a streamed resource, an error) calls `NotifyStateChanged()`, from any thread.
- **The resource selection** ([algorithm](https://html.spec.whatwg.org/multipage/media.html#concept-media-load-algorithm)):
  `load()`, the `src` attribute, `srcObject` (a `Blob`, a `MediaSource` or a `MediaSourceHandle`, see
  [Media Source Extensions](#media-source-extensions); `MediaStream` is not implemented) and the
  `source` children (their `src`, `type` and `media` attributes, the `error` events of the candidates, the pointer that
  follows the insertions and the removals, the wait for a new `source` element), with `networkState`, `currentSrc`,
  `error` (`MediaError`) and the events `loadstart`, `progress`, `suspend`, `abort`, `emptied`, `stalled` and `error`. The
  synchronous sections of the algorithm run in microtasks, as specified (the browsers run them later, each in its own
  way). The element delays the load event of its document until `loadeddata`. An element created in a document without
  media backend (e.g. the contents of a `template`) selects its resource when it is adopted by a document that has one.
- **The fetch** of the resource uses [the fetch algorithm](#the-fetch-algorithm) (`RequestDestination.Audio` or
  `RequestDestination.Video`, the CORS mode of the `crossorigin` attribute, the content security policy `media-src`, the
  mixed content upgrade). As in the browsers, the request has a `Range: bytes=0-` header: a `206` response that contains
  a part of the resource is completed with other range requests
  ([verify a media response](https://html.spec.whatwg.org/multipage/media.html#verify-a-media-response)), and an HTTP
  error fails the load. A response that arrives over time (`DocumentResource.RemainingContentBytes`) is given to the
  backend as its chunks arrive: `CreatePlayer()` is called until the metadata can be read, then the player receives the
  rest (`MediaPlayer.OnDataReceived()`); `progress` is fired at most every 350ms, `stalled` after 3 seconds without
  data, and the load fails with `MEDIA_ERR_NETWORK` when the connection is interrupted after the metadata.
  `preload="none"` defers the fetch until `play()` (or until the attribute changes, or `autoplay` is set), and
  `loading="lazy"` until the element is about to intersect the viewport (see [Lazy loading](#lazy-loading); as in the
  browsers, `loadstart` is then fired when the fetch starts, where the specification fires it at once; the poster of a
  lazy `video` is deferred too).
- **The ready states and the playback**: `readyState` follows the player; `durationchange`, `resize`, `loadedmetadata`,
  `loadeddata`, `canplay`, `canplaythrough`, `waiting` and `playing` are fired in the specified order; `play()` and
  `pause()` with their pending play promises (`AbortError`, `NotSupportedError`, `NotAllowedError`), `autoplay` (unless
  the document is sandboxed without `allow-scripts` or is not allowed to use the `autoplay` feature, see
  [Permissions Policy](#permissions-policy)), `paused`, `ended`, `loop`, `currentTime` and `fastSeek()` (`seeking`,
  `seeked`; the position is limited to the seekable ranges at once, as in the browsers), the media fragment `#t=` of the
  URL, `duration`, `played`, `buffered`, `seekable` (`TimeRanges`), `playbackRate`, `defaultPlaybackRate` (`ratechange`),
  `preservesPitch`, `volume` and `muted` (`volumechange`), `videoWidth` and `videoHeight`, `audioTracks` and `videoTracks`
  (`addtrack`, `change`), `getStartDate()`. While the element is playing, a timer of the event loop reads the position of
  the player every 250ms (`timeupdate`) and at the time the resource ends (`ended`, or the seek of `loop`): an element that
  plays keeps the event loop busy, as an infinite animation does. The element is paused when it is removed from its
  document, and its position stops advancing while its document is not fully active.
- **The pseudo-classes** `:playing`, `:paused`, `:seeking`, `:buffering`, `:stalled` and `:muted` follow the state of the
  element (`:volume-locked` never matches).
- **Rendering**: a `video` element has the natural size of its poster frame while it represents it, else the one of the
  video, and paints the poster or the frame of the player (see [rendering.md](rendering.md#painting)); `drawImage()` and
  `createImageBitmap()` use the frame of the player (a cross-origin resource without CORS taints the canvas). An `audio`
  element without `controls` is not rendered. The controls of the media elements are not rendered.
- **Media documents**: a navigation to a resource whose type is an image type, or a type the backend can play, creates a
  [media document](https://html.spec.whatwg.org/multipage/document-lifecycle.html#read-media) whose body contains an
  `img`, a `video` or an `audio` element (with `controls` and `autoplay`) that uses the response of the navigation.
- **Text tracks**: see [Text tracks and WebVTT](#text-tracks-and-webvtt).
- **Playback quality** ([Media Playback Quality](https://w3c.github.io/media-playback-quality/)):
  `HTMLVideoElement.getVideoPlaybackQuality()` returns the numbers of frames the player reports
  (`MediaPlayer.TotalVideoFrames`, `DroppedVideoFrames`: 0 by default, as nothing is decoded) at the current time.
- **Not implemented**: `MediaStream`, Encrypted Media, the controls, `controlsList`, the pause
  of the videos that are not visible, Media Session and Picture-in-Picture.

### Media Source Extensions

The [Media Source Extensions](https://w3c.github.io/media-source/) (`Dom/MediaSources`) let the scripts give the media data
to a media element: `MediaSource`, `SourceBuffer`, `SourceBufferList`, `MediaSourceHandle`, and `ManagedMediaSource`,
`ManagedSourceBuffer` and `BufferedChangeEvent`. The media backend parses the byte streams, and the library implements the
algorithms of the specification on the coded frames it reports (their timestamps, not their data):

```csharp
sealed class MyMediaBackend : MediaBackend
{
    // MediaSource.isTypeSupported(), addSourceBuffer() and changeType(): null when the type is not supported
    public override MediaSegmentParser? CreateSegmentParser(string mimeType, IReadOnlyList<string> codecs)
        => mimeType == "video/mp4" ? new MyMp4SegmentParser() : null;
}

sealed class MyMp4SegmentParser : MediaSegmentParser
{
    // Called with the bytes that were appended and not consumed yet, until it needs more data
    public override MediaSegmentParseResult Parse(ReadOnlyMemory<byte> input)
    {
        // An initialization segment: the tracks (and the duration, if any)
        //   return MediaSegmentParseResult.Initialization(byteCount, new MediaInitializationSegment(tracks, duration));
        // A media segment: the coded frames (track, presentation and decode timestamps, duration, key frame flag)
        //   return MediaSegmentParseResult.Media(byteCount, frames);
        // Bytes to skip, an incomplete segment, or an error (the append error algorithm)
        //   return MediaSegmentParseResult.Ignore(byteCount) / NeedMoreData() / Error("...");
    }
}
```

- **The backend** (`MediaBackend.CreateSegmentParser()`, `MediaSegmentParser`): a parser is created for each source buffer
  (and by `changeType()`); `Parse()` implements the format-specific part of the segment parser loop, `Reset()` is the
  reset of the parser state (`abort()`), `GeneratesTimestamps` is the "generate timestamps flag" of the byte stream format
  (MPEG audio: the mode is then `sequence`), and `BufferCapacity` the size of the buffer. Without parser, no type is
  supported (`addSourceBuffer()` throws a `NotSupportedError`). `MediaSource.isTypeSupported()` also requires
  `CanPlayType()` to answer `Probably`: as in Chromium (and the tests), a type that allows the `codecs` parameter is only
  supported with its codecs, though `addSourceBuffer()` and `changeType()` accept it without them.
- **Attaching**: a `MediaSource` constructed in a window is attached by a `blob:` URL (`URL.createObjectURL(mediaSource)`,
  in the `src` attribute of the element or of a `source` child; the URL is resolved when the `src` attribute is set, so it
  can be revoked at once, as in Firefox) or by `srcObject`; a source constructed in a dedicated worker is attached by its
  `handle` (`MediaSourceHandle`, transferred to the window and assigned to `srcObject`; a handle is used once). The source
  opens in a task (`sourceopen`), the element does not delay the load event of its document and stays in `NETWORK_LOADING`
  without `progress`, `suspend` nor `stalled` event. The source is detached (`sourceclose`, its source buffers are removed)
  when the element loads another resource (`load()`, the `src` attribute), when its resource selection fails, and when its
  document is destroyed.
- **The source**: `readyState`, `duration` (the duration change algorithm; the element has the duration of the source
  even before it has its metadata), `addSourceBuffer()`, `removeSourceBuffer()`, `endOfStream()` (with a `network` or a
  `decode` error: the element fails with `MEDIA_ERR_SRC_NOT_SUPPORTED` and detaches the source before it has its metadata,
  with `MEDIA_ERR_NETWORK` or `MEDIA_ERR_DECODE` afterwards), `setLiveSeekableRange()`, `clearLiveSeekableRange()`,
  `sourceBuffers` and `activeSourceBuffers` (the source buffers of the enabled audio tracks and of the selected video
  track, with their events).
- **The source buffers**: `appendBuffer()` runs the buffer append algorithm in a task (`updatestart`, then `update` or
  `error`, then `updateend`), `remove()` the range removal the same way, `abort()`, `changeType()`, `mode`,
  `timestampOffset`, `appendWindowStart`, `appendWindowEnd`, `updating` and `buffered`. The initialization segment
  received algorithm creates the audio and video tracks on the element (`audioTracks`, `videoTracks` and their
  `sourceBuffer` attribute; for a source constructed in a window, also on the source buffer); the coded frame processing
  algorithm applies the timestamp offset, the sequence mode, the coded frame groups (the discontinuities), the append
  window, the random access points and the removal of the frames that a new frame overlaps (with their decoding
  dependencies, up to the next key frame); the coded frame removal algorithm removes the frames of a range. The track
  buffers keep the frames in decode order. Their ranges coalesce the gaps shorter than the longest frame of the track
  (the specification allows up to twice this duration), and `buffered` is the intersection of the ranges of the tracks.
  A source buffer is full after 150 MB of media segments with a video track, or 12 MB for audio (as in Chromium, unless
  the parser gives its `BufferCapacity`): the frames before the key frame of the current playback position are then
  evicted, and `appendBuffer()` throws a `QuotaExceededError` when the buffer is still full.
- **The element**: `buffered` is the intersection of the ranges of the active source buffers, `seekable` follows the
  duration and the live seekable range, and the ready state follows the data buffered around the current playback
  position (the source buffer monitoring): `HAVE_METADATA` once every source buffer received an initialization segment,
  or when the position is not buffered (a seek waits there until the data is appended); `HAVE_CURRENT_DATA` at the end of
  a buffered range (the playback is suspended: `waiting`); `HAVE_FUTURE_DATA` when the range goes beyond the position;
  `HAVE_ENOUGH_DATA` when 500 ms are buffered after the position, or when the range reaches the end of the stream. The
  position advances with the clock of the event loop (nothing is decoded) within the buffered ranges.
- **Choices where the specification leaves room, as in the browsers**: the ready state only rises asynchronously (from a
  timer queued after the `updateend` event, as the browsers decode the frames after the append), and it is
  `HAVE_METADATA` while the element is seeking (the `seeked` event is fired before `canplay`); the playback skips the
  gaps of the buffered ranges that are shorter than 125 ms (appending over a group of pictures leaves such gaps; Firefox
  skips them too, and Chromium coalesces larger gaps), and a position before the first buffered range is played when
  the range starts within the first second (the tracks rarely start at 0).
- **Managed media source**: `streaming` is true (`startstreaming`) while the source is open and the ready state of the
  element is lower than `HAVE_ENOUGH_DATA`, and a `ManagedSourceBuffer` fires `bufferedchange` with the ranges that
  were added and removed by an append, a removal or `endOfStream()`. The library has no memory pressure: the memory
  cleanup algorithm never runs.
- **Workers**: `MediaSource`, `SourceBuffer` and the other interfaces are exposed in the dedicated workers. The element
  and the source then run on two event loops: the element reads a copy of the state of the source (its duration, its
  buffered ranges), and the steps that the specification mirrors (the tracks, the ready state, the errors, the detach)
  are tasks of the other event loop. `buffered` and `seekable` are empty once the worker is closing.
- **Not implemented**: the text tracks of the byte streams (`SourceBuffer.textTracks` is empty), the audio splices with a
  crossfade (the overlapped audio frame is truncated), and the playback of the frames: a `video` element attached to a
  media source paints no frame, and `MediaPlayer` is not involved (the backend only parses the segments).

### Text tracks and WebVTT

The media elements have the [text tracks](https://html.spec.whatwg.org/multipage/media.html#timed-text-tracks) of the HTML
Standard, and their cues are [WebVTT](https://w3c.github.io/webvtt/) cues. The text tracks do not need a media backend
(the cues are parsed and exposed without one), but the cues only become active when a media resource is played.

```csharp
var video = (HtmlVideoElement)document.QuerySelector("video")!;

// The tracks of the track element children, of addTextTrack() and of the media resource
foreach (var track in video.TextTracks)
{
    Console.WriteLine($"{track.Kind} {track.Language} {track.Label}: {track.Mode}");
}

// A track that is not disabled loads the WebVTT file of its track element
var captions = video.TextTracks[0];
captions.Mode = TextTrackMode.Showing;
captions.AddEventListener("cuechange", _ =>
{
    foreach (var cue in captions.ActiveCues!)
    {
        Console.WriteLine(((VttCue)cue).Text);
    }
});

// Cues can be added by the application
var chapters = video.AddTextTrack(TextTrackKind.Chapters, "Chapters", "en");
chapters.AddCue(new VttCue(startTime: 0, endTime: 30, "Introduction") { Id = "intro", PauseOnExit = true });
```

- **The model**: `HtmlMediaElement.TextTracks` (`TextTrackList`, with `GetTrackById()` and the `addtrack`, `removetrack`
  and `change` events), `TextTrack` (`Kind`, `Label`, `Language`, `Id`, `Mode`, `Cues`, `ActiveCues`, `AddCue()`,
  `RemoveCue()`, `cuechange`), `TextTrackCueList`, `TextTrackCue` (`Id`, `StartTime`, `EndTime`, `PauseOnExit`, `enter`,
  `exit`), `HtmlMediaElement.AddTextTrack()` and `HtmlTrackElement` (`Track`, `ReadyState`, the reflected attributes). The
  cues of a track are in the [text track cue order](https://html.spec.whatwg.org/multipage/media.html#text-track-cue-order).
- **The track elements**: the text track of a `track` element is in the list of its parent media element. When its mode
  is not `disabled`, the [track processing model](https://html.spec.whatwg.org/multipage/media.html#start-the-track-processing-model)
  fetches the track URL with the fetch algorithm (`RequestDestination.Track`, the CORS mode of the `crossorigin`
  attribute of the media element and a same-origin request without it, the content security policy `media-src`) and
  parses the response as WebVTT, as its bytes arrive (`DocumentResource.RemainingContentBytes`); `readyState` follows the
  load, and a `load` or an `error` event is fired at the element. A change of the `src` attribute empties the list of cues
  and loads the new URL (the load of the previous URL is aborted, with an `error` event). The MIME type of the response is
  not checked, as in the browsers.
- **The automatic track selection**: the first `track` element with a `default` attribute whose kind is subtitles or
  captions is `showing` (unless a track of these kinds is showing), likewise for the descriptions, and the default
  chapters and metadata tracks are `hidden`. There is no user preference. The selection runs when the media element is
  popped off the stack of open elements of the parser, or in a task after a `track` element is inserted. The ready state
  of the element does not reach `HAVE_FUTURE_DATA` while a track that was enabled when the resource was selected is
  loading ([the text tracks are ready](https://html.spec.whatwg.org/multipage/media.html#the-text-tracks-are-ready)).
- **The cues of the playback** ([time marches on](https://html.spec.whatwg.org/multipage/media.html#time-marches-on)):
  the cues of the tracks that are not disabled whose range contains the current playback position are active
  (`ActiveCues`); the `enter` and `exit` events are fired at the cues, in the order of their times, then `cuechange` at
  their tracks and at their `track` elements; the cues that the playback skipped (shorter than the time between two
  runs) get both events; the playback pauses when it goes past the end of a cue whose `PauseOnExit` is set. While the
  element plays, the timer of the playback is due at the next time a cue starts or ends, so the events are fired at the
  positions of the cues.
- **The WebVTT parser** (`VttCue`, `VttRegion`): the [file parser](https://w3c.github.io/webvtt/#file-parsing) (the
  signature, the header, the `REGION` and `STYLE` blocks, the identifiers, the timings and the settings of the cues, the
  `NOTE` blocks, with the error recovery of the specification) is incremental; the
  [cue text parser](https://w3c.github.io/webvtt/#cue-text-parsing-rules) builds the WebVTT node objects (`c`, `i`, `b`,
  `u`, `ruby`, `rt`, `v`, `lang`, the timestamps, the HTML character references) for `VttCue.GetCueAsHtml()` and for the
  rendering. `VttCue` has the settings of the cues (`Region`, `Vertical`, `SnapToLines`, `Line`, `LineAlign`, `Position`,
  `PositionAlign`, `Size`, `Align`, `Text`) with their validation; `Line` and `Position` are a number or `auto`
  (`LineAndPositionSetting`).
- **The media-resource-specific text tracks**: the tracks that the player of the media backend reports
  (`MediaPlayer.TextTracks`: `MediaTextTrackInfo` with its `MediaTextTrackCue` items, whose text and settings are WebVTT)
  are added to the list when the metadata of the resource is known (disabled, with an `addtrack` event), and removed when
  another resource is loaded.
- **Rendering**: the cues of the showing subtitles and captions tracks are rendered over the video (see
  [rendering.md](rendering.md#webvtt-cues)), and styled with the `::cue`, `::cue()` and `::cue-region` pseudo-elements.
- **Where the library follows the browsers rather than the specifications** (the tests of the Web Platform Tests expect
  it): setting the `src` attribute of a `track` element to its current value does nothing (the specification empties
  the list of cues without loading the URL again), and removing it resets the track URL; an element created by the
  parser without `track` child still selects a default track that a script adds later; a default track enabled while the
  media resource is not ready to play delays the ready state as a pending text track; a cue whose end time is less than
  its start time is missed once, at its start time; `GetCueAsHtml()` of an empty cue text returns an empty text node.
- **Not implemented**: the user preferences for the selection of the tracks, the chapter navigation and the exposure of
  the descriptions, the `cues` filter of the earliest possible position, and the tracks of the WebM and MP4 resources in
  the test backend (a media backend reports them).

## The document loader

Nothing is fetched unless a loader is configured. The navigations to URLs other than `about:blank`, `about:srcdoc`,
`blob:` and `data:` (`iframe` `src`, `location.href = url`, `location.assign(url)`, `BrowsingContext.Navigate(url)`...) are
fetched with the fetch algorithm of the library (see [The fetch algorithm](#the-fetch-algorithm)), which calls
`DocumentLoader.LoadAsync` (the `BrowsingContextOptions.DocumentLoader` option) with a `DocumentRequest` and a
cancellation token for each HTTP request, from a task of the event loop. It returns a `ValueTask`: a document loaded synchronously is created
in the same task; otherwise it is created by a task queued when the `ValueTask` completes (from any thread). The token is
canceled when the navigation is aborted (another navigation of the same browsing context, `window.stop()`, or the removal
of the `iframe`); the result of an aborted navigation is ignored. A navigation of a child navigable delays the `load` event
of its parent document until the new document is loaded; a reload or a traversal of the session history does not.

- `DocumentRequest.Url` is the absolute URL of the document. `DocumentRequest.CookieJar` is the
  `BrowsingContextOptions.CookieJar` option (see [Cookies](#cookies)), or an ephemeral jar shared by the documents of the
  credentialless iframes of a top-level document. A loader sends the `Cookie` header returned by
  `DocumentRequest.GetCookieHeader()` and stores the `Set-Cookie` headers of the response with
  `DocumentRequest.StoreCookies()`: they apply the SameSite rules with `DocumentRequest.SiteForCookies`,
  `DocumentRequest.TopLevelOrigin`, `DocumentRequest.Origin` and `DocumentRequest.RedirectUrls` (the URLs of the redirects
  that led to the request).
- `DocumentRequest.Origin` is the serialized origin of the document that initiated the request (e.g.
  `https://example.com`, or `"null"` for an opaque origin): the document containing the `iframe`, the document whose
  `location` is set, or the document of the `script` element. It is null when the application starts the navigation
  (`BrowsingContext.Navigate(url)`). A loader can use it to decide whether the document should be downloaded (e.g. to
  reject the cross-origin requests).
- `DocumentRequest.Referrer` is the referrer of the request (null for no referrer), as determined by
  `DocumentRequest.ReferrerPolicy`: the `referrerpolicy` attribute of the `iframe` or `script` element, else the
  `Referrer-Policy` header or `<meta name="referrer">` of the document, else `strict-origin-when-cross-origin` (the full
  URL for a same-origin request, the origin for a cross-origin request, nothing from `https:` to `http:`). It is also the
  `document.referrer` of the new document.
- The method returns a `DocumentResource`: the response, with the decoded text or the bytes of the document, its
  `StatusCode` (200 by default) and its `Headers`. The MIME type is the `Content-Type` header (`text/html` when there is
  none). The bytes are decoded as specified: byte order mark, `charset` parameter, then, for HTML, `<meta charset>`, the
  encoding of the same-origin container document, the encoding detected from the content (see
  [Encoding detection](#encoding-detection)) and windows-1252 (UTF-8 for the other types). `text/html` is parsed with the HTML parser, the XML MIME types (`application/xhtml+xml`,
  `image/svg+xml`, `application/xml`...) with the XML parser (a document that is not well-formed contains a
  `parsererror` element), and `text/plain`, `text/css`, `text/vtt`, the JavaScript MIME types and the JSON MIME types as
  a text document (the text is in a `pre` element whose lines wrap, as in the browsers; `document.contentType` is the type
  of the response). The image types and the types the media backend can play
  create a media document (see [Media elements](#media-elements)). The other types create an empty document.
  A redirect response (e.g. a `302` status with a `Location` header) is followed by the library. A loader that follows
  the redirects anyway sets `DocumentResource.Url` (the URL of the response) and `DocumentResource.RedirectUrls` (the URLs
  of the redirects between the requested URL and the response URL): the library checks them as the redirects it follows
  (e.g. a redirect to another origin taints the response of a no-CORS request, so the rules of a style sheet that went
  through another origin cannot be read).
  `DocumentResource.RemainingContentBytes` is the rest of a body that arrives over time (e.g. a chunked response): the
  HTML document of a navigation is parsed as its chunks arrive (the parser waits for the next chunk from a task of the
  event loop, and the end of the sequence is the end of the file), and the events of an `EventSource` are dispatched as
  the chunks of its event stream arrive (see [Server-sent events](#server-sent-events)). The other requests, and the
  navigations to a document that is not an HTML document, wait for the end of the sequence and get the complete body;
  a navigation that does not create a document (a `204` response, a download) disposes the enumerator of the sequence
  without reading it. A loader that returns the whole body (no sequence) works as before: the document is parsed in one
  go.
  The [encoding sniffing algorithm](https://html.spec.whatwg.org/multipage/parsing.html#encoding-sniffing-algorithm) of
  a document parsed as its chunks arrive waits for the bytes that determine the encoding, as the specification allows
  ("the user agent may wait for more bytes"): when the response has no byte order mark nor `charset` parameter, the
  parser starts once the prescan is over (it found a `<meta charset>`, or it read the first 1024 bytes and the end of
  the head), at the end of the body, or 500 ms (measured by the clock of the event loop) after the first bytes, whichever
  comes first; an XML declaration that has not ended yet is waited for, however long it takes, as in the browsers.
  Meanwhile, as in Gecko, the bytes received are parsed speculatively (when `BrowsingContextOptions.SpeculativeParsing`
  is true, see [Speculative parsing](#speculative-parsing)) with the encoding the document gets when it does not declare
  one (the encoding of the same-origin container document, else the default encoding), so the resources they reference
  are requested while the parser waits; the parser requests them again with the determined encoding (e.g. a URL whose
  query has a character that the two encodings encode differently). When the bytes do not declare an encoding, it is
  detected from the bytes received when the first chunk that has a non-ASCII byte arrives (the ASCII bytes before it are
  parsed as they arrive: they are the same in all the encodings), and the first `<meta>` element of the head that the
  parser inserts (e.g. after the timeout) changes the encoding on the fly when the bytes consumed before it are ASCII
  bytes ([change the encoding](https://html.spec.whatwg.org/multipage/parsing.html#change-the-encoding), step 5); after
  another byte, the specification navigates to the document again with the new encoding, which is not implemented (the
  encoding does not change, as for a document whose `<meta>` element is found after the first 1024 bytes and a non-ASCII
  byte). As in Gecko, the `<meta>` elements written by `document.write()` do not change the encoding.
- The body of a document is displayed whatever its status (e.g. a 404 page). A script or a style sheet whose
  status is not in the 200-299 range is a network error, as is a script whose MIME type is an audio, image or video type,
  or `text/csv`. A response whose status is 204 or 205, or whose
  `Content-Disposition` is `attachment` (a download, which is not saved), does not create a document: the navigation
  ends and the active document stays.
- The headers used by the library are:

  | Header | Effect |
  | --- | --- |
  | `Accept-CH`, `Critical-CH` | The client hints that the origin of a top-level document opts into, and the ones that restart the navigation (see [Client hints](#client-hints)) |
  | `Clear-Site-Data` | `"clientHints"`, `"cookies"`, `"cache"` and `"*"` clear the client hints of the origin (see [Client hints](#client-hints)); `"prefetchCache"`, `"prerenderCache"`, `"cache"` and `"*"` discard the prefetches and the prerenders of the documents of the origin (see [Speculative loading](#speculative-loading)); nothing else is cleared |
  | `Content-Type` | The MIME type and the encoding of the resource |
  | `Content-Disposition` | `attachment`: the navigation does not create a document |
  | `Last-Modified` | `document.lastModified` (the current date and time when there is none) |
  | `Link` | The `preload` and `modulepreload` links are fetched (see [Preloads](#preloads)) |
  | `Content-Language` | The default language of the elements (`:lang()`) when there is no `<meta http-equiv="content-language">`: its first language (the spec makes the language unknown when there are several, but the browsers use the first one) |
  | `Content-Security-Policy`, `Content-Security-Policy-Report-Only` | The fetch directives, which block the requests of the subresources and the inline scripts and styles, the navigation and document directives (see [Content security policy](#content-security-policy)), the `upgrade-insecure-requests` directive (see [Upgrade Insecure Requests](#upgrade-insecure-requests)), the `sandbox` directive, and the Trusted Types directives (see [trusted-types.md](trusted-types.md)) |
  | `Document-Policy` | `expect-no-linked-resources`: the document is not parsed speculatively (see [Speculative parsing](#speculative-parsing)); `force-load-at-top`: the document does not scroll to its fragment when it is loaded (see [Text directives](#text-directives)) |
  | `Referrer-Policy` | The referrer policy of the document (`DocumentRequest.Referrer` of its requests) |
  | `Refresh` | The declarative refresh of the document (see [Declarative refresh](#declarative-refresh)) |
  | `Speculation-Rules` | The speculation rule sets of a top-level document are fetched (see [Speculative loading](#speculative-loading)) |
  | `No-Vary-Search` | The query parameters that a prefetched or prerendered response does not depend on (see [Speculative loading](#speculative-loading)) |
  | `Supports-Loading-Mode` | `credentialed-prerender`: a cross-origin same-site document can be prerendered; `prerender-cross-origin-frames`: the cross-origin frames of the document are loaded while it is prerendered |
  | `X-Content-Type-Options` | `nosniff`: a script whose MIME type is not a JavaScript MIME type, or a style sheet whose MIME type is not `text/css`, is a network error |
  | `X-Frame-Options` | `DENY` and `SAMEORIGIN`: the document of a nested navigable is replaced by an error document (see [Navigations and document directives](#navigations-and-document-directives)) |

  The other headers are ignored.

```csharp
var resource = new DocumentResource(bytes)
{
    StatusCode = 200,
    Headers = [new("Content-Type", "text/html; charset=utf-8")],
};
```

- The method returns null when the resource is not available. The navigation then results in an error document whose
  origin is opaque, as for a network error. An exception thrown by the method is also a network error.

### Encoding detection

An HTML document that does not declare its encoding (no byte order mark, no `charset` in the `Content-Type` header, no
`<meta>` element found in the head, and no same-origin container document to inherit the encoding from) gets the
encoding detected from its content by `BrowsingContextOptions.EncodingDetector`, the step of the
[encoding sniffing algorithm](https://html.spec.whatwg.org/multipage/parsing.html#determining-the-character-encoding)
that "may attempt to autodetect the character encoding from applying frequency analysis or other algorithms to the data
stream". The confidence of the detected encoding is tentative: a `<meta>` element of the head after the first 1024 bytes
still changes it. When nothing is detected, the document is decoded as windows-1252.

- The default detector, `EncodingDetector.Default`, is a port of [chardetng](https://github.com/hsivonen/chardetng), the
  detector of Firefox (Apache-2.0 or MIT license; its notice is distributed with the package, and its tables are
  generated from its data by `tools/Meziantou.Framework.WebPlatform.SpecGen`, see [updating-specs.md](updating-specs.md)). Each candidate encoding scores
  the bytes and is disqualified by the byte sequences that are not valid for it: the single-byte encodings
  (windows-1250 to windows-1258, windows-874, ISO-8859-2, ISO-8859-4 to ISO-8859-8, ISO-8859-13, KOI8-U and IBM866) score
  the pairs of adjacent characters by their frequency in the languages that use the encoding, and the multi-byte
  encodings (Shift_JIS, EUC-JP, GBK, Big5 and EUC-KR) score the kinds of the decoded characters (kana, common ideographs,
  hangul syllables, private use characters...). The differences with chardetng: the top-level domain of the URL is not
  used, nothing is detected (the document is decoded as windows-1252) where chardetng falls back to an encoding, and the
  legacy encodings are guessed from the 256 KiB that follow the first non-ASCII byte, to bound the work for the large
  documents (the validity of UTF-8 is checked for all the bytes).
- As in the browsers, UTF-8 is only detected for the documents that do not come from the network (the `file:` URLs): the
  other documents whose bytes are valid UTF-8 are decoded as windows-1252, so that the content of the Web does not start
  to rely on the detection instead of declaring its encoding (WPT `encoding-detection/utf-8.html`). ISO-2022-JP and
  UTF-16 are never detected.
- A host replaces the detector with a class that derives from `EncodingDetector`: `Detect(bytes, context)` returns a
  [label](https://encoding.spec.whatwg.org/#names-and-labels) of the encoding, or null when it is not detected.
  `context.Url` is the URL of the document, and `context.IsComplete` is false for a document parsed as its chunks arrive
  (the bytes may end in the middle of a character). The detector is not called for the documents that only have plain
  ASCII bytes (the ASCII bytes other than the escape, shift out and shift in characters). The labels that are not
  supported and the ones of UTF-16BE/LE and of the replacement encoding are ignored.
  `BrowsingContextOptions.EncodingDetector = null` disables the detection.
- Only the HTML documents of the navigations are detected. The XML documents (the XML declaration, else UTF-8), the text
  documents (UTF-8), the style sheets, the scripts, the responses of `XMLHttpRequest` (including the document responses)
  and of `fetch()`, and `TextDecoder` have the fallback encoding of their specification.
- `HtmlDocumentParser.Parse(bytes)` detects the encoding in the same way, with `HtmlParserOptions.EncodingDetector`: the
  default detector also detects UTF-8 when `HtmlParserOptions.Url` is not set (the bytes do not come from the network).
  The documents whose encoding is not detected get `HtmlParserOptions.DefaultEncoding`; the detector is not used when the
  default encoding is UTF-16BE/LE (the detected encodings are ASCII-compatible).

```csharp
// Decode the documents that do not declare their encoding as windows-1252, as before the detection
var options = new BrowsingContextOptions { EncodingDetector = null };

// Or detect the encoding with another library
sealed class MyDetector : EncodingDetector
{
    public override string? Detect(ReadOnlySpan<byte> bytes, EncodingDetectionContext context) => LooksLikeShiftJis(bytes) ? "shift_jis" : null;
}
```

`javascript:` URLs are ignored, unless a script engine is configured (see [scripting.md](scripting.md#javascript-urls)).
When a script engine is configured, the external scripts are also fetched with the loader (`DocumentRequest.Destination` is
`RequestDestination.Script`). The style sheets of the `<link rel=stylesheet>` elements, of the `@import` rules and of
the `xml-stylesheet` processing instructions are loaded by the loader as well (`RequestDestination.Style`, see
[computed-style.md](computed-style.md#linked-style-sheets)), as are the resources of `<link rel=preload>`,
`<link rel=modulepreload>` and `<link rel=prefetch>` (see [Preloads](#preloads)), and the documents and the rule sets of the
speculation rules (see [Speculative loading](#speculative-loading)). The images of
the `img` and SVG `image` elements and the posters of the videos are loaded by the loader too (`RequestDestination.Image`,
see [Images](#images)), the font faces (`RequestDestination.Font`, see [computed-style.md](computed-style.md#fonts)) and,
with a media backend, the media resources of the `audio` and `video` elements (`RequestDestination.Audio`,
`RequestDestination.Video`, see [Media elements](#media-elements)).
The requests of `fetch()`, `XMLHttpRequest`, `EventSource` and `navigator.sendBeacon()` are sent to the loader too
(`RequestDestination.Empty`, see [The fetch algorithm](#the-fetch-algorithm)), and the opening handshakes of
the WebSockets are sent to `DocumentLoader.ConnectWebSocketAsync` (see [WebSockets](#websockets)).
The `http:` subresources of an `https:` document are upgraded or blocked before the loader is called (see
[Mixed content](#mixed-content)).

### Performance timeline

Each window and each worker has a performance timeline ([Performance Timeline](https://w3c.github.io/performance-timeline/),
`Dom/Performance`): `performance.getEntries()`, `getEntriesByType()`, `getEntriesByName()` and `PerformanceObserver`
(`observe()` with `entryTypes` or with `type`, `buffered` and `durationThreshold`, `takeRecords()`, `supportedEntryTypes`, the
`droppedEntriesCount` of the first callback; the callback this value is the observer). The entry types are `navigation`,
`resource`, `mark` and `measure`, described below, `visibility-state` (see [Page visibility](#page-visibility)), and the entry types of the rendering: `layout-shift`, observed only by
the performance observers of the windows (see [Layout shifts](layout.md#layout-shifts-csslayoutlayoutshifttrackercs)), and
`paint`, `largest-contentful-paint` and `element` (see [Paint timing](rendering.md#paint-timing)), and the entry types of
the user input of the windows, `event` and `first-input`, described below. The times are measured
with the `TimeProvider` of the event loop, relative to the time origin of the global object, and coarsened to 100 µs.

**[Resource Timing](https://w3c.github.io/resource-timing/)**: the requests sent to the loader for the subresources (the
style sheets, the fonts, the images, the scripts, the module scripts and their imports, the resources of the `link`,
`embed` and `object` elements, the scripts of the dedicated workers and of `importScripts()`, the pings of the hyperlinks,
`fetch()`, `XMLHttpRequest`, `EventSource` and `navigator.sendBeacon()`) add a `PerformanceResourceTiming` entry to the timeline of the
window (or of the worker) that fetches them, whose name is the requested URL. Its `initiatorType` is the local name of
the element (`img`, `script`, `link`, `iframe`...), `css` for the resources of the style sheets (the imports and the fonts of
a style sheet that is not origin-clean are not reported; its images are), `script` for the module scripts, `fetch`,
`xmlhttprequest`, `beacon`, `ping`, or `other` (the classic worker scripts, `importScripts()`, the module preloads, and
each connection of an `EventSource`, reported when its response ends). A fetch that consumes a preloaded resource adds no
entry (see [Preloads](#preloads)).

- The fetch algorithm records the fetch timing info (`FetchRequest.Timing`): the start, the redirects (`redirectStart`,
  `redirectEnd`, `fetchStart`), the time the request is sent to the loader (`requestStart`) and the time the loader completes
  (`responseStart`); `responseEnd` is the time the response is processed. The loader does not report the timing of the
  connection, of the first byte nor of the interim responses: the connection times are `fetchStart` (a reused connection),
  `nextHopProtocol` is `http/1.1` (the empty string for a response given by a service worker) and
  `firstInterimResponseStart` is 0.
- The timing allow check (https://fetch.spec.whatwg.org/#concept-tao-check) runs for every response of the fetch, the
  redirects included: when one of them is cross-origin (or the request was redirected through another origin) without a
  `Timing-Allow-Origin` header that allows the origin of the request, or for a network error, the entry only has
  `startTime`, `fetchStart`, `responseEnd` and `duration`, and its `transferSize` is 0. As in the browsers, a navigation
  redirected through another origin also needs the header for the next responses (the specification only checks the
  responses of the other origins, as the response tainting of a navigation is always `basic`).
- The sizes, the status, `contentType` (the minimized MIME type) and `contentEncoding` are 0 or empty for an opaque
  response (a cross-origin `no-cors` response). The loader returns the decoded body: `encodedBodySize` is the
  `Content-Length` of a response that has a `Content-Encoding`, else the size of the body. `contentEncoding` is the
  content coding when the browsers support it (`gzip`, `deflate`, `br`, `zstd`, `dcb`, `dcz`), `multiple` or `@unknown`.
  There is no HTTP cache: `deliveryType` is empty and `transferSize` is `encodedBodySize + 300`.
- `serverTiming` is the list of the metrics of the `Server-Timing` headers ([Server Timing](https://w3c.github.io/server-timing/),
  `PerformanceServerTiming`: `name`, `duration`, `description`), in a secure context, when the timing allow check passes.
- The navigation started by an `iframe` or `frame` element (its `src` attribute) is reported to the document of the
  element when the timing allow check passes (the entry is added when the response is received); otherwise the element
  reports a fallback entry (`startTime` and `responseEnd` only) when its `load` event is fired, or when the response has no
  content (`204`, `205`). The next navigations of the frame are not reported. As in the browsers, the status and the
  content type of a navigation (an `iframe`, an `object`...) are only exposed when no URL of the redirect chain is
  cross-origin with its container.
- The resource timing buffer holds 250 entries by default (`setResourceTimingBufferSize()`, `clearResourceTimings()`, the
  `resourcetimingbufferfull` event); an entry queued while the buffer is full is counted as dropped.
- `renderBlockingStatus` is `blocking` for the resource of a script or of a style sheet link that was
  [render-blocking](#render-blocking) when its fetch started, as the specifications say (the element is in the
  render-blocking element set, so the document had no body element yet). Chrome also reports `blocking` for a
  `blocking=render` element inserted when the body element exists, for the style sheets imported by a render-blocking
  style sheet and for a preload consumed by a render-blocking element.
- `workerStart` is the time the request was given to a service worker (0 when no fetch event is dispatched for it), also
  when the timing allow check fails; `fetchStart` is then the time the fetch event was dispatched, once the worker is
  running and activated. The requests answered by a service worker are reported as the requests answered by the network.
  The times of the static routing API (`workerRouterEvaluationStart`...) are 0.

**[Navigation Timing](https://w3c.github.io/navigation-timing/)**: each document of a browsing context has a
`PerformanceNavigationTiming` entry (`performance.getEntriesByType("navigation")`), created with the document and queued to
the observers once the `load` event completed. The time origin of the window is the start of the navigation.

- `type` is `navigate`, `reload` or `back_forward`; `name` is the URL of the document when it is created.
- The times of the fetch of the navigation (`redirectStart`, `redirectEnd`, `fetchStart`, `requestStart`, `responseStart`,
  `responseEnd`, the sizes, the status, `serverTiming`) are the ones described above; `responseEnd` and the sizes are 0
  until the body is received when the document is parsed as its bytes arrive. A document that is not fetched (the initial
  `about:blank` document, `Navigate(url, html)`) starts, is requested and is received when it is navigated to.
- `domInteractive`, `domContentLoadedEventStart`, `domContentLoadedEventEnd`, `domComplete`, `loadEventStart` and
  `loadEventEnd` are 0 until they happen (they are set once: `document.open()` does not reset them); `duration` is
  `loadEventEnd`.
- `unloadEventStart` and `unloadEventEnd` are the times of the `unload` event of the previous document when it has the
  origin of the new document and the redirect taint of the response is `same-origin`.
- `redirectCount`, `redirectStart` and `redirectEnd` are exposed when every redirect comes from the origin of the
  document, or when the request has a referrer and the response of every redirect has a `Timing-Allow-Origin` header that
  allows the origin of the document (https://fetch.spec.whatwg.org/#navigation-tao-check). The specification also exposes
  the redirects of the origin of the initiator to a document of another origin (the redirect taint is `same-origin`); as the
  browsers, the library does not.
- The legacy `performance.timing` (`PerformanceTiming`, the same times in milliseconds since the Unix epoch, plus
  `domLoading`) and `performance.navigation` (`PerformanceNavigation`: `type`, `redirectCount`) are implemented.
- `deliveryType` is `navigational-prefetch` when the navigation used a navigational prefetch (see
  [Speculative loading](#speculative-loading)): the request and the response of the prefetch that happened before the
  navigation are reported at its fetch start (`connectEnd`, `requestStart` and `responseStart` are equal for a completed
  prefetch). `activationStart` is the time a prerendered document was activated (0 for the other documents).
- `notRestoredReasons` (there is no back/forward cache) and `confidence` are not implemented; `criticalCHRestart` is 0.

**[Event Timing](https://w3c.github.io/event-timing/)**: the dispatch of a trusted event to a target of a window creates a
`PerformanceEventTiming` entry (`Dom/Performance/EventTiming.cs`) when its type is one of the types of the specification:
the events of the [user input](#user-input) (`pointerdown`, `pointerup`, `pointercancel`, the pointer and mouse boundary
events, `gotpointercapture`, `lostpointercapture`, `mousedown`, `mouseup`, `click`, `auxclick`, `dblclick`, `contextmenu`,
`touchstart`, `touchend`, `touchcancel`, the drag-and-drop events but `drag`), the keyboard events (`keydown`, `keypress`,
`keyup`), `beforeinput` and `input`, and the composition events. The continuous events (`pointermove`, `mousemove`,
`touchmove`, `wheel`...) and the events dispatched by the scripts (`dispatchEvent()`, `click()`) are not timed. The events
are timed whether or not they have listeners.

- `name` is the type of the event and `startTime` its `timeStamp` (the events of `BrowsingContext.Input` are created when
  they are dispatched, with the clock of the window of their target); `processingStart` and `processingEnd` are the times
  the dispatch starts and ends (the listeners, the activation behavior and the default actions of the user agent, e.g. the
  click of a key press, ran); `cancelable` is the one of the event.
- `duration` is the time between `startTime` and the next rendering update of the event loop, rounded to the nearest
  8 ms. An event requests a rendering opportunity (a frame of the event loop, see [Event loops](#event-loops)), which sets
  the duration of the pending entries right after the paint timing ("dispatch pending Event Timing entries"), counts
  their events and queues the entries that last at least 16 ms; with a fake `TimeProvider`, the durations are the time the
  host advances the clock by until it runs the frame. Nothing is rasterized: the duration does not include a presentation
  time. The entries wait while their document is not fully active.
- The observers of the `event` type receive the entries whose duration is at least their `durationThreshold`
  (`PerformanceObserverInit.DurationThreshold`: 104 ms by default, at least 16 ms; ignored with `entryTypes`, as in the
  browsers). The performance entry buffer of the `event` type (the `buffered` observers) holds the first 150 entries of at
  least 104 ms; the `event` entries are not available from `getEntries()`.
- `interactionId` groups the events of an interaction of the user; it is 0 for the other events. The identifiers start at
  a random value and increase by 7; `performance.interactionCount` is the number of interactions. A pointer interaction is
  a `pointerdown` followed by the `pointerup` of the pointer (or by `contextmenu`), with its `click`: the entry of a
  `pointerdown` waits for the release (its duration is still set by the first rendering update, so it does not include
  the time the button stays pressed), and a pointer that is canceled (e.g. a touch that pans) is not an interaction.
  `mousedown`, `mouseup`, `auxclick`, `dblclick` and the click forwarded by a `label` to its control have no interaction.
  The keyboard interactions follow the browsers (and the tests) instead of the "pending key downs" of the specification,
  which only gives an interaction to a `keydown` once its `keyup` is dispatched: `keydown` starts the interaction (each
  repetition of a held key is one), `keypress` and the `keyup` of the same `keyCode` belong to it, and so does the click
  that the key simulates (`Enter` or the space key on a button: a trusted `click` whose `pointerId` is -1). An `input`
  event of a composition (`isComposing`) is an interaction, and `compositionstart` ends the interactions of the pressed keys.
- The `first-input` entry is a copy of the first entry that has an interaction, queued once, whatever its duration; it is
  available from `getEntriesByType("first-input")`. As in the browsers, a `pointerdown` whose duration was set before its
  release is still the first input (the specification only considers the entries whose duration is being set).
- `target` is the last target of the event (the host of the shadow tree when the event propagated out of it), or null
  when it is no longer connected, in a shadow tree, or in a document that is not fully active; `targetSelector` is the
  selector of the specification, computed when the event is dispatched (the name of the node, with its id, `DIV#id`, or
  else its `src` attribute, `IMG[src=…]`, which is not quoted nor escaped).
- `performance.eventCounts` (`EventCounts`, a read-only map) counts the events of each of the 36 types, whatever their
  duration; as in the specification, an event is counted when its duration is set, by the rendering update that follows it.
- Not done: the duration is not ended by a modal dialog nor when the document becomes hidden, and the events dispatched
  while a touch flings or a selection autoscrolls are not distinguished (there is no fling nor autoscroll).
  `WebPlatformFeatures.EventTimingEnabled` disables the feature: no event is timed.

**[User Timing](https://w3c.github.io/user-timing/)**: `performance.mark()`, `measure()` (with a start mark and an end
mark, or with `PerformanceMeasureOptions`: `start`, `end`, `duration`, `detail`), `clearMarks()`, `clearMeasures()` and the
`PerformanceMark` constructor, in the windows and in the workers. The `detail` is a structured clone made when the entry is
created (the entry returns the same object each time). In a window, a mark cannot have the name of an attribute of
`PerformanceTiming` (a `SyntaxError`), and `measure()` accepts these names as marks (an `InvalidAccessError` while the
attribute is 0).

### Speculative parsing

When `BrowsingContextOptions.SpeculativeParsing` is true, the HTML parser fetches the resources of the rest of the
document while it waits for a parsing-blocking script, as a browser does
([speculative HTML parsing](https://html.spec.whatwg.org/multipage/parsing.html#speculative-html-parsing)): the rest of
the input is parsed into a copy of the document, starting from the state of the parser (the open elements, the
insertion mode...), without running its scripts, and the resources that its elements would fetch are requested from
the loader with `DocumentRequest.IsSpeculative` set to true (`DocumentRequest.Initiator` is null): the external scripts,
the images (`img`, `picture`, SVG `image`, `poster`), the style sheets and the preloads, but not the resources that the
parser would not fetch (the contents of a `template` that is not a declarative shadow root, the lazy images, the scripts
whose type is not supported...). The `base` elements, the `<meta name="referrer">` elements and the
`<meta http-equiv="Content-Security-Policy">` elements found speculatively apply to the subsequent speculative requests
(a request blocked by a content security policy is not sent); a `<meta charset>` does not change the encoding, and a
`<meta name="viewport">` is ignored, as in the desktop browsers. Each URL is requested speculatively once per document.
When the document is parsed as its bytes arrive (see [the document loader](#the-document-loader)), the speculative parser
also parses the chunks that arrive while the parser waits for the script, and the bytes received while the encoding
sniffing algorithm waits for the bytes that determine the encoding (the parser has not started yet).

The responses of the speculative requests are ignored: the parser requests the resources again when it reaches the
elements, so the loader should cache the responses to benefit from them (a `<link rel=preload>` found speculatively is
requested speculatively, then by its element; the element that uses the resource consumes the preload). A service worker
that controls the document gets a single `fetch` event for a speculative request and the request of the parser that
follows it while the event is still handled (the browsers send one request): the parser gets the response of that event. The module
preloads and the prefetches are not requested speculatively. The documents whose response has the
`Document-Policy: expect-no-linked-resources` header are not parsed speculatively. The option is false by default.

### Preloads

The `link` elements and the `Link` headers of the documents fetch resources in advance
(https://html.spec.whatwg.org/multipage/links.html#link-type-preload). None of them delays the `load` event of the
document. The element fires a `load` event when its resource is fetched, or an `error` event for a network error (a
response blocked by a check included; a `404` response is not a network error, as specified, where the browsers fire
`error`).

- **`<link rel=preload>`** fetches the resource when its `as` attribute is a preload destination: `fetch` (the empty
  destination), `font`, `image`, `script`, `style` or `track` (`RequestDestination.Track`); the other values (`video`,
  `audio`, `json`...) and a missing attribute fetch nothing and fire no event. The request is a potential-CORS request:
  its mode is `cors` when the element has a `crossorigin` attribute (also for a font: a font preload without the
  attribute is not used by the font face, whose request is always a CORS request), with the `integrity` and
  `referrerpolicy` attributes and the nonce of the element. The `type` attribute must be a MIME type that the library
  supports for the destination, and the `media` attribute must match. The `imagesrcset` and `imagesizes` attributes of
  an image preload select the URL from their source set (the `href` attribute is its default source). The resource is
  fetched again when `href` or `as` changes, or when `type` or `media` start to match.
- **The map of preloaded resources** of the document holds the response of each preload (a network error included) by
  preload key: the URL, the destination, the mode and the credentials mode of the request. The next fetch of a window
  with the same key **consumes** the entry (https://html.spec.whatwg.org/multipage/links.html#consume-a-preloaded-resource)
  instead of calling the loader: a script, a style sheet, an image, a font, a CSS image, `fetch()`, `XMLHttpRequest`...
  When the preload is still being fetched, the consumer waits for its response (a synchronous `XMLHttpRequest` does
  not: it sends its own request). The integrity metadata of the consumer must be empty or equal to the ones of the
  preload. The checks of the consumer still apply (content security policy, MIME type, integrity). A resource has a
  single resource timing entry, the one of its preload (`initiatorType` is `link`): the consumer adds none. A request
  with another mode or destination (e.g. an `img` without `crossorigin` for a preload with it) fetches the resource again.
- **`Link` headers**: the `preload` links of the response of a document are preloaded when the document is created
  (`as`, `crossorigin`, `integrity`, `referrerpolicy`, `nonce`, `type`, `media`, `imagesrcset`, `imagesizes`; the
  target is resolved against the URL of the document). As in the browsers, the `modulepreload` links are fetched too
  (the specification makes them do nothing). The links that have a `media` attribute are evaluated at once (the
  specification waits for the viewport `meta` element), the `Link` headers of the responses of the subresources are not
  processed (Chromium and WebKit do it; it is not specified), and there are no early hints (`103` responses).
- **`<link rel=modulepreload>`** fetches a module script into the module map of the window
  (`ScriptEngine.PreloadModuleScript`, see [scripting.md](scripting.md#module-scripts)), without fetching its
  dependencies (the specification makes it optional) nor running it: the module scripts and the imports of the URL do
  not fetch it again. The `as` attribute gives the destination and the type of the module: a script-like destination
  (`script` by default), `json`, `style` (a CSS module) or `text`; another destination fires an `error` event. Only a
  change of `href` fetches again. Its resource timing entry has the `other` initiator type, as in the browsers.
- **`<link rel=prefetch>`** sends a request whose destination is empty, with the `Sec-Purpose: prefetch` header and
  the `Accept` header of a navigation. There is no HTTP cache, so the response is not used by the next request of the
  URL. A content security policy that has a `default-src` directive blocks the prefetch unless one of its fetch
  directives allows the URL (https://w3c.github.io/webappsec-csp/#does-resource-hint-violate-policy).
- **`<link rel=prerender>`** (removed from the HTML Standard) is processed as a prefetch, as in the browsers (Chromium's
  NoState Prefetch).
- **`<link rel=preconnect>`** and **`<link rel=dns-prefetch>`** fetch nothing and fire no event: the loader has
  neither a connection pool nor a DNS cache to fill in advance, so the hints are satisfied by doing nothing (the
  specification makes them recommendations). `<link rel=next>` is processed as a preconnect (the specification lets
  the user agent choose between a DNS prefetch, a preconnect and a prefetch). The `preconnect` links of the `Link`
  headers are ignored for the same reason.
- `apple-touch-icon` and `apple-touch-icon-precomposed` are icons, and `canonical` has no processing model:
  `relList.supports()` is true for them, as in the browsers.
- As in the browsers, the request of a preload whose element was created by the parser is "parser-inserted" for the
  content security policies: a script preload without nonce is blocked by `'strict-dynamic'`.

### Speculative loading

The speculation rules of the documents ([speculative loading](https://html.spec.whatwg.org/multipage/speculative-loading.html),
`Html/SpeculativeLoading/`) fetch or prerender in advance the documents that the user is likely to navigate to. They need a
`DocumentLoader`, and scripting must be enabled (a script engine is not required).

- **Rule sets**: the inline `<script type="speculationrules">` elements (registered when the element is prepared,
  unregistered when it is removed, and parsed again when the document base URL changes; changing the text of a registered
  element does nothing, as specified), and the rule sets of the `Speculation-Rules` header of a top-level document, fetched
  with CORS (`RequestDestination.SpeculationRules`, MIME type `application/speculationrules+json`). The `prefetch` and
  `prerender` rules are parsed as specified: list rules (`urls`, `relative_to`) and document rules (`where`: `and`, `or`,
  `not`, `href_matches` with URL patterns, `selector_matches`), `eagerness`, `referrer_policy`, `requires`, `tag`,
  `expects_no_vary_search` and `target_hint`. A rule with an unknown key or an invalid value is ignored; JSON that is not
  an object reports an exception to the window. The `'inline-speculation-rules'` source of a `script-src` directive
  allows the inline rules.
- **Candidates**: the documents of the top-level navigables consider their speculative loads in a microtask when their
  rule sets change, and at each rendering update (the links that the document rules select are the `a` and `area`
  elements with an HTTP(S) URL that are being rendered). The specification lets the user agent decide when a candidate is
  enacted: an `immediate` candidate at once, an `eager` candidate when the pointer of the user
  ([`BrowsingContext.Input`](#user-input)) moves over a link to its URL, a `moderate` candidate when the pointer stays
  200 ms over the link, and a `conservative` candidate when a button is pressed on the link. A document has at most 50
  speculative loads. `BrowsingContextOptions.SpeculativeLoading` holds the preferences of the user: `PrefetchEnabled`
  (false: nothing is prefetched nor prerendered), `PrerenderEnabled` (false: the prerender candidates are prefetched) and
  `AnonymousClientIpSupported` (see below).
- **Navigational prefetch** ([Prefetch](https://wicg.github.io/nav-speculation/prefetch.html)): a prefetch record of the
  document fetches a potentially trustworthy URL as a navigation would (mode `navigate`, `Sec-Purpose: prefetch`, the
  `Sec-Speculation-Tags` header for the same-site requests, the referrer policy of the rule, of the link or of the
  document, the redirects followed one by one). A same-site request has the cookies of the navigable. A cross-site
  request is isolated: it has no credentials, its cookies are stored apart and copied to the cookie jar when the prefetch
  is used, its referrer policy must be at least as strict as `strict-origin-when-cross-origin`, and, as in Chromium, it is
  not sent when the cookie jar has cookies for the URL or when a service worker controls it. A rule that requires
  `anonymous-client-ip-when-cross-origin` is only prefetched across origins when the loader hides the IP address of the
  client (`AnonymousClientIpSupported`; the request has `Sec-Purpose: prefetch;anonymous-client-ip`). Only the responses
  with an ok status are kept, for 5 minutes. The requests go through the service workers (as in Chromium, which the
  tentative tests describe: a prefetch redirected from or to a URL that a service worker controls fails, a prefetch is
  not used when a service worker started or stopped controlling its URL, and the fetch event has the `Sec-Purpose`
  header and no resulting client id).
- **Use by a navigation**: a GET navigation of a top-level navigable (also a reload or a traversal, as in the browsers)
  whose source document has a completed prefetch for its URL processes the response of the prefetch instead of fetching
  the URL, and waits for an ongoing one. The URLs match when they are equal without their fragments, or equivalent for
  the `No-Vary-Search` header of the response (the `expects_no_vary_search` hint, while the response is not received).
  A record is used once. The document has the URL of the navigation; `document.referrer` is the referrer of the prefetch.
  A fragment link to the document itself is not prefetched. There is no HTTP cache: the other documents fetch the URL again.
- **Prerendering** ([Prerendering](https://wicg.github.io/nav-speculation/prerendering.html)): a prerender candidate
  loads a same-site URL in a prerendering traversable, a top-level browsing context in a new browsing context group that
  the application does not see (`Document.Prerendering`, `document.prerendering`). Its requests have
  `Sec-Purpose: prefetch;prerender`, its documents are hidden (`visibilityState`), never have the focus
  (`document.hasFocus()`), are not revealed and run no animation frame callback, its navigations replace its single
  session history entry, and `window.open()` returns null. `BroadcastChannel.postMessage()`,
  `navigator.serviceWorker.register()`, `ServiceWorkerRegistration.update()` and `unregister()`,
  `ServiceWorker.postMessage()`, `navigator.locks.request()` and `query()` and `navigator.storage.persist()` are delayed
  until the activation, and the documents of the cross-origin and sandboxed
  iframes are created at the activation (their requests are sent), unless the response of their container document has
  `Supports-Loading-Mode: prerender-cross-origin-frames` (as in Chromium). The prerendering is canceled when a response
  of its top-level navigation is not an ok response that creates a document, or is cross-origin without
  `Supports-Loading-Mode: credentialed-prerender`, and when its rule is removed. A document in a prerendering traversable
  does not prerender (its prerender rules are prefetched).
- **Activation**: a GET navigation (not a reload nor a traversal) of a top-level navigable started by its active
  document to the URL of one of its prerender records (matched as the prefetches) activates the prerendered document: the
  previous document is unloaded as by any navigation, the prerendered document and its window become the active document
  of the `BrowsingContext` (the specification replaces the navigable; the object of the application stays), with a new
  session history entry, and the navigation switches browsing context group (no opener, no name). Then the documents of
  the page get the visibility state of the browsing context (`visibilitychange` when `BrowsingContext.IsVisible` is true;
  they share its system focus), run the delayed operations and fire `prerenderingchange`, and
  `PerformanceNavigationTiming.activationStart` is the time of the activation. A prerender with
  `"target_hint": "_blank"` is activated by `window.open(url, "_blank", "noopener")` instead. As in Chromium, the
  prerender is discarded and the URL is fetched when the prerendered document is not created yet, or when the browsing
  context group of the navigable has other top-level browsing contexts (an opener, or the windows it opened).
- Not implemented: the suspension of the workers and of the media elements of a prerendering document, the other delayed
  or restricted APIs of the specification (the library does not implement them), the entries of the navigation API of
  the initiator in the prerendered document, the `form_submission`, `prerender_until_script` and activation beacon
  extensions of Chromium, and the `Cookie-Indices` header.

### HTTP loader

`DocumentLoader.Create(HttpClient? httpClient = null)` returns a loader that gets the `http:` and `https:` resources with
HTTP requests (the other URLs are network errors): the method, the body and the headers of `DocumentRequest` (`GET` for
the navigations, any method for `fetch()` and `XMLHttpRequest`). It sends the cookies of `DocumentRequest.CookieJar`
and stores the cookies of the responses when `DocumentRequest.IncludeCredentials` is true, sends the `Referer` header
(determined again for each redirect, with the `Referrer-Policy` of the redirect), follows the redirects (up to 20, reported in `DocumentResource.RedirectUrls`) when
the client does not and `DocumentRequest.FollowRedirects` is true, and returns the status, the status message, the
headers and the body of any response (e.g. a 404 page). The body of a `text/event-stream` response that is not the response
of a navigation, and the body of the HTML response of a navigation (`text/html`, or no `Content-Type`; not a redirect nor a
prefetch, whose request has a `Sec-Purpose` header), are read as they arrive (`DocumentResource.RemainingContentBytes`), so
the events of an `EventSource` are dispatched when the server sends them and the document is parsed as its bytes arrive;
the other bodies are read before the response is returned. Without a client, it returns a shared loader whose client does not follow redirects, does not
use its own cookies and decompresses the responses. A client given to `Create` should not use its own cookies
(`UseCookies = false`) when `BrowsingContextOptions.CookieJar` is set. The opening handshake of a WebSocket is an HTTP/1.1
upgrade request sent with the client (with the cookies): the connection of a `101` response is the stream of its content.

The loader requests any URL the documents navigate to: do not use it with untrusted documents when the requests can
reach private resources (server-side request forgery). Use a client whose handler filters the destinations, or a custom
`DocumentLoader`:

```csharp
sealed class FileDocumentLoader(string root) : DocumentLoader
{
    public override async ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
    {
        const string Prefix = "https://example.com/";
        if (!request.Url.StartsWith(Prefix, StringComparison.Ordinal))
            return null;

        var path = Path.GetFullPath(Path.Combine(root, request.Url[Prefix.Length..]));
        if (!path.StartsWith(root, StringComparison.Ordinal) || !File.Exists(path))
            return null;

        return new DocumentResource(await File.ReadAllBytesAsync(path, cancellationToken))
        {
            Headers = [new("Content-Type", "text/html")],
        };
    }
}
```

## The fetch algorithm

The navigations, the subresources of the documents (scripts, style sheets, images, fonts, the resources of the `embed`,
`object` and `<link rel=preload>` elements, the hyperlink auditing pings), the scripts of the workers, and the requests
of `fetch()`, `XMLHttpRequest` (https://xhr.spec.whatwg.org/), `EventSource`, `navigator.sendBeacon()` and the WebSockets are fetched
with the fetch algorithm of the library (https://fetch.spec.whatwg.org/#fetching), as a browser does. Only the HTTP
requests are sent to the `DocumentLoader`, and the library follows their redirects: the redirect mode of a subresource is
`follow`, and a navigation follows its redirects itself (`manual`) to check each response with the opener policy (of a
top-level navigable) and the cross-origin resource policy (of a child navigable). A redirect of a navigation to a scheme
that is not a fetch scheme (e.g. `mailto:`) ends the navigation, and a redirect to another fetch scheme (e.g. `data:`)
is a network error. When a script engine is configured, the scripts can send requests with `fetch()` and `XMLHttpRequest`
(`DocumentRequest.Destination` is `RequestDestination.Empty`):

- The `about:blank`, `blob:` (including the range requests) and `data:` URLs are fetched by the library; the other
  schemes and the bad ports (https://fetch.spec.whatwg.org/#bad-port) are network errors.
- The CORS protocol (https://fetch.spec.whatwg.org/#http-cors-protocol): the mode of the request (`cors`, `no-cors`,
  `same-origin`), the CORS check of the responses from another origin (`Access-Control-Allow-Origin`,
  `Access-Control-Allow-Credentials`), the CORS-preflight requests (`OPTIONS` with `Access-Control-Request-Method` and
  `Access-Control-Request-Headers`) and their cache (`Access-Control-Max-Age`, measured with the clock of the event loop:
  5 seconds when the header is missing or is not a number, and no caching for a negative number, as in the browsers),
  the exposed headers (`Access-Control-Expose-Headers`), and the filtered responses (`basic`, `cors`, `opaque`,
  `opaqueredirect`).
- The redirects: the loader does not follow them (`DocumentRequest.FollowRedirects` is false) and returns the redirect
  responses, which the library follows with the redirect mode of the request (`follow`, `error`, `manual`), up to 20 times,
  changing the method and removing the body as specified. The checks of the request (mixed content, content security
  policies, bad ports) and the CORS protocol apply to each URL of the redirect chain. A loader that follows the redirects
  anyway should set `DocumentResource.Url` and `DocumentResource.RedirectUrls`.
- `DocumentRequest.Headers` contains the headers of the request: the ones set by the script (except the forbidden
  request headers, e.g. `Cookie` or `Host`), `Accept` (determined by the destination), `Content-Type`, `Content-Length`,
  `Origin`, `Referer`, the Fetch Metadata headers (`Sec-Fetch-Dest`, `Sec-Fetch-Mode`, `Sec-Fetch-Site`, `Sec-Fetch-User`),
  the client hints (`Sec-CH-UA`, `Sec-CH-UA-Mobile`, `Sec-CH-UA-Platform` and the ones the origin opted into, see
  [Client hints](#client-hints)), the cache headers and `Authorization` (the credentials of the URL, tried once after a `401` response with a `WWW-Authenticate`
  header, then remembered for the origin). `DocumentRequest.IncludeCredentials` tells whether the cookies should be sent
  (the credentials mode of the request and the `withCredentials` attribute).
- `DocumentResource.StatusText` is the status message (`statusText`); it is the reason phrase of the status by default.
  The values of the response headers are normalized, and a response whose headers are invalid is a network error.
- The `integrity` of a request (https://w3c.github.io/webappsec-subresource-integrity/) is checked (SHA-256, SHA-384 and
  SHA-512): the `integrity` option of `fetch()`, and the `integrity` attribute of the `script` and `link` elements (classic
  and module scripts, style sheets, preloads). As in the browsers, a hash expression whose value has a character that
  is not a base64 character is ignored.

`Headers` and `XMLHttpRequest` (with `XMLHttpRequestUpload`) are .NET classes (`Headers`, `XmlHttpRequest`), whose
events are fired by the tasks of the event loop. `Request`, `Response` and `fetch()` are implemented in JavaScript by the
WebIDL bindings, because their bodies are streams (see [scripting.md](scripting.md#fetch)). `fetch()` resolves once
the headers are received, and its body is a `ReadableStream` that pulls the chunks of the response as it is read
(`DocumentRequest.StreamsResponseBody` asks the loader for `DocumentResource.RemainingContentBytes`; one chunk is read
ahead, so the end or the failure of the body errors or closes the stream without a pending read): a body that fails
after the response errors the stream, and aborting the fetch or canceling the stream stops reading the body. The other
responses are processed once their body is fully received (so an `XMLHttpRequest` fires a single `progress` event; the
fetch algorithm waits for the end of `DocumentResource.RemainingContentBytes`), except the document of a navigation,
the event stream of an `EventSource` and the media resources, and the body of a request created from a `ReadableStream`
is read before the request is sent. A synchronous
`XMLHttpRequest` blocks the event loop until the loader completes (`DocumentRequest.IsSynchronous` is true): the loader
must not wait for a task of the event loop.

There is no HTTP cache (`cache: "only-if-cached"` is a network error). The requests are handled by the service workers
(see [Service workers](#service-workers)), and checked by the `connect-src` directive of the content security policies (see [Content security policy](#content-security-policy)).

The fetches of a document are aborted when it is aborted (`window.stop()`) or destroyed, except the `keepalive` requests
(https://fetch.spec.whatwg.org/#request-keepalive-flag), which are still sent by the loader of the browsing context after
the document is unloaded or its iframe is removed. The bodies of the inflight `keepalive` requests of a document are
limited to 64 KiB: a `fetch()` beyond the quota is a network error.

`navigator.sendBeacon()` (https://w3c.github.io/beacon/, `Navigator.SendBeacon`) sends a `keepalive` POST request
whose response is ignored, with the credentials. Its mode is `no-cors`, or `cors` (with a CORS-preflight request) when the
`Content-Type` of the data is not CORS-safelisted (e.g. a `Blob` of type `application/octet-stream`). It returns false
when the data exceeds the `keepalive` quota, and, as in the browsers, when the request is blocked before it is sent (mixed
content, `connect-src`). A request is inflight until its response is received, in a later task.

`fetchLater(input, { activateAfter, ...init })` ([deferred fetching](https://fetch.spec.whatwg.org/#deferred-fetch), in
the windows of the secure contexts) queues a `keepalive` request that is sent later, and returns a `FetchLaterResult`
whose `activated` attribute tells whether it was sent:

- The request is validated when it is queued: a `TypeError` for a document that is not fully active, for a URL that is
  not an HTTP(S) URL or that is not potentially trustworthy, and for a body that is a stream (its length is not known);
  a `RangeError` for a negative `activateAfter`; the abort reason of a signal that is already aborted.
- The deferred fetch record is sent when the fetch group of its document is terminated (the document is destroyed: a
  navigation, the removal of its `iframe`, the disposal of its browsing context), or `activateAfter` milliseconds after
  the call, on the clock of the event loop (`BrowsingContextOptions.TimeProvider`), whichever comes first. Aborting the
  signal of the request before it is sent discards it. The request is sent as the other `keepalive` requests (the
  `connect-src` directive, the referrer policy, the credentials, the CORS protocol; the service workers do not handle
  it), and its response is ignored. `activated` becomes true in a task once the request is sent.
- **Quota.** The total length of the pending requests (the URL, the referrer, the headers and the body) is limited: a
  top-level document and the documents of its origin that it embeds share 640 kibibytes (512 while the
  `deferred-fetch-minimal` feature is enabled, as 128 are reserved for the cross-origin frames), of which at most 64 per
  origin of the requests. A cross-origin `iframe` gets 64 kibibytes when its container delegates the `deferred-fetch`
  policy-controlled feature to its origin (`allow="deferred-fetch"`; its default allowlist is `'self'`), else 8 kibibytes
  with `deferred-fetch-minimal` (default allowlist `*`; at most 16 frames of a page): the quota is reserved when the
  document of the container navigates the frame, and freed when the frame ends up same-origin. A request beyond the
  quota throws a `QuotaExceededError` (with its `quota` and `requested` members, as specified; Chromium leaves them
  null). As in Chromium, a sandboxed `iframe` without `allow-same-origin` is cross-origin: it gets the minimal quota (the
  specification reserves the quota for the origin of the URL).
- Not implemented: the back/forward cache (a document that is navigated away is destroyed, so its requests are sent),
  the sending when the page is hidden for a long time or the browser is moved to the background (a host can dispose the
  browsing context), and the priority of the deferred fetch task source over the other task sources.

`WebPlatformFeatures.FetchLaterEnabled` hides `fetchLater()` and `FetchLaterResult`.

## WebSockets

When a script engine is configured, the scripts can open WebSockets (https://websockets.spec.whatwg.org/, the .NET
classes `WebSocket` and `CloseEvent`), in the windows and in the workers. The opening handshake is fetched by the fetch
algorithm of the library with the `websocket` mode (`RequestMode.WebSocket`): the bad ports, the mixed content (a `ws:`
URL from an `https:` document is blocked), the `connect-src` directive, the `Origin` header, the cookies (the credentials
are included), the `Authorization` header of the credentials of the URL (after a `401` response), no referrer, no cache
and no redirect (a redirect is a network error). The HTTP request is sent by `DocumentLoader.ConnectWebSocketAsync`, with
the URL of the WebSocket in the `http:` or `https:` scheme and the headers of `DocumentRequest.Headers` (`Upgrade`,
`Connection`, `Sec-WebSocket-Key`, `Sec-WebSocket-Version`, `Sec-WebSocket-Protocol`, `Sec-WebSocket-Extensions`, `Origin`...):
the loader returns the response, whose `DocumentResource.Connection` is the stream on which the frames are read and
written when the status is `101`. The default implementation returns null (a network error): the WebSockets cannot connect
unless the loader implements it, as the [HTTP loader](#http-loader) does.

The library validates the response as specified (the status, `Upgrade`, `Connection`, `Sec-WebSocket-Accept`, the
selected subprotocol and extensions): an invalid response fails the connection (`error` and `close` events, with the
`1006` code). The frames of the protocol (https://www.rfc-editor.org/rfc/rfc6455) are read and written by
`System.Net.WebSockets.WebSocket`, with the permessage-deflate extension (https://www.rfc-editor.org/rfc/rfc7692) when the
server accepts it (the request offers `permessage-deflate; client_max_window_bits`). The library does not send pings, and
answers the pings of the server. There is at most one connection being established to a host and a port in a browsing
context group: the other ones wait until it is established or has failed (https://www.rfc-editor.org/rfc/rfc6455#section-4.1).

The events are fired by the tasks of the event loop of the global object of the WebSocket (`open`, `message`, `error`
and `close`). `bufferedAmount` decreases once the messages are transmitted, in a later task. The closing handshake echoes
the Close frame of the server (with its status code and its reason), and `close()` sends a Close frame without status
code when it has no argument (the close code is then `1005`). A connection closed without closing handshake is closed
abnormally (code `1006`, `wasClean` is false). A WebSocket that is connecting or closing, or that is transmitting a
message, is an asynchronous operation of its event loop (`DomEventLoop.HasPendingOperations`, so
`DomEventLoop.RunUntilIdleAsync` waits for it); an open WebSocket that waits for messages is not. The WebSockets of a
document are closed with the `1001` code (going away) when the document is unloaded, and the WebSockets of a worker when
the worker ends. The `WebSocketStream` proposal is not implemented, and neither are the WebSockets over HTTP/2.

## Server-sent events

When a script engine is configured, the scripts can receive the events of a server with `EventSource`
(https://html.spec.whatwg.org/multipage/server-sent-events.html, the .NET classes `EventSource` and `EventSourceInit`),
in the windows and in the workers. The request is fetched by the fetch algorithm of the library: a `GET` request in the
`cors` mode (the credentials mode is `include` with `withCredentials`, else `same-origin`), with the `Accept:
text/event-stream` header, the `no-store` cache mode (`Cache-Control: no-cache`) and, after a reconnection, the
`Last-Event-ID` header (the last event ID string, encoded as UTF-8). It follows the redirects, and it is handled by the
service workers and checked by the `connect-src` directive as the requests of `fetch()` are.

- The connection is announced (`open`) when the status of the response is `200` and its MIME type is
  `text/event-stream` (whatever its parameters: the stream is always decoded as UTF-8). Any other response fails the
  connection: an `error` event is fired and `readyState` is `CLOSED`.
- The event stream is interpreted as its bytes arrive (`DocumentResource.RemainingContentBytes`, see
  [The document loader](#the-document-loader); the [HTTP loader](#http-loader) reads the body of a `text/event-stream`
  response as it arrives): one leading byte order mark is removed, the lines end with CRLF, LF or CR, and the `data`,
  `event`, `id` (ignored when it contains U+0000) and `retry` fields are processed. A blank line dispatches a
  `MessageEvent` (`data`, `lastEventId`, and `origin`, the origin of the URL of the response) whose type is the `event`
  field, or `message`. A line that ends with a CR is processed at once (a LF that follows in the next chunk is ignored).
  As in the browsers, the last event ID buffer of a new stream is the last event ID string of the event source (the
  specification initializes it to the empty string, so that the first event of a reconnection that has no `id` field
  would reset the string).
- When the response ends, the connection is reestablished: an `error` event is fired (`readyState` is `CONNECTING`), and
  the request is fetched again after the reconnection time: 3 seconds, or the value of the last `retry` field, measured
  by the timers of the event loop (`DomEventLoop.QueueTimer`, so a fake `TimeProvider` controls it). There is no
  additional backoff.
- A network error fails the connection, except when the loader returns no response (the server cannot be reached): the
  connection is then reestablished as when a response ends. The other network errors are the ones for which a new
  attempt is known to be futile, as in the browsers: a URL that cannot be fetched, a response that fails the CORS
  check, a request blocked by a content security policy or as mixed content, an invalid redirect.
- `close()` aborts the request (the cancellation token of `DocumentLoader.LoadAsync` is canceled) and drops the events
  that were received and not dispatched yet. The event sources of a document are closed when the document is unloaded
  or destroyed, and the ones of a worker when the worker ends; `window.stop()` aborts the request and fails the
  connection.

The events are fired by tasks of the event loop of the global object of the event source. An open connection that
waits for events is not an asynchronous operation of its event loop (`DomEventLoop.RunUntilIdleAsync` does not wait for
the next event), and neither is the wait of the reconnection time (a timer).

## Origins

The origin of a document is the one of its URL. The initial `about:blank` documents, the `about:blank` and
`about:srcdoc` documents get the origin of the document that created them (the top-level initial `about:blank` document
has an opaque origin). The `data:` documents, the network errors and the sandboxed documents (an `iframe` whose `sandbox`
attribute does not have `allow-same-origin`, or a `sandbox` directive in the `Content-Security-Policy` header of the
response) have a new opaque origin. Scripting is disabled in the sandboxed documents without `allow-scripts`: their
scripts do not run, and their `noscript` elements are parsed as markup.

`document.domain` can be set to a registrable domain suffix of the current domain (the public suffixes are the ones of the
[Public Suffix List](https://publicsuffix.org/list/), `tests/Meziantou.Framework.WebPlatform.SpecData/publicsuffix`): the documents that set it to the same
value are same origin-domain, so they can access each other (`contentDocument`, `frameElement` and the members of the
`Window` and `Location` objects). It throws a `SecurityError` in a document without a browsing context, with an opaque
origin, or in a sandboxed document.

The windows of a browsing context group are in agent clusters, keyed by site (scheme and registrable domain). A secure
context whose response has the `Origin-Agent-Cluster: ?1` header is put in an origin-keyed agent cluster
(`window.originAgentCluster`), unless the origin was already used by the group without it: in an origin-keyed agent
cluster, setting `document.domain` does nothing. The `Origin` interface (`Origin.from()`, `isSameOrigin()`,
`isSameSite()`) compares origins.

With a script engine, the members of the `Window` and `Location` objects of another window throw a `SecurityError` when
the windows are not same origin-domain, except the cross-origin properties (`window`, `self`, `location`, `close()`,
`closed`, `focus()`, `blur()`, `frames`, `length`, `top`, `opener`, `parent`, `postMessage()`, the `href` setter and
`replace()` of `Location`). The objects are not cross-origin exotic objects: their own properties that are not IDL
members (e.g. the global variables of another window) are not protected.

## Mixed content

The subresources of a document that prohibits mixed security contexts are checked as specified by
[Mixed Content](https://w3c.github.io/webappsec-mixed-content/): a document prohibits mixed security contexts when its
origin, or the origin of the document of an ancestor browsing context (e.g. the `https:` page of an `http:` `iframe`),
is potentially trustworthy (`https:`, `wss:`, `file:`, `localhost` and the loopback addresses, as for
`window.isSecureContext`). A URL is potentially trustworthy when its origin is, or when it is a `data:`, `about:blank` or
`about:srcdoc` URL. `window.isSecureContext` is true when the document and the documents of its ancestors are
potentially trustworthy; a document whose origin is opaque (e.g. a sandboxed `srcdoc` iframe) is checked with its URL,
as the browsers check the origin it would have had.

- The images whose URL is an `http:` URL (the `img` elements, the SVG `image` elements and the posters of the videos),
  or which are redirected to an `http:` URL, are upgraded: `DocumentRequest.Url` is the `https:` URL (with the same port, unless it is 443), and the referrer is
  determined for the upgraded URL. `currentSrc` is still the `http:` URL. The images whose host is an IP address, and
  the `img` elements that use `srcset` or a `picture` element (the `imageset` initiator), are not upgraded: they are
  blocked.
- The other requests whose URL is not potentially trustworthy are blocked: the style sheets (`<link rel=stylesheet>`,
  `@import`, `xml-stylesheet`), the classic and module scripts, the font faces, the `<link rel=preload>` resources, the
  speculative requests, the hyperlink auditing pings, and the requests of `fetch()` and `XMLHttpRequest` (including each
  URL of a redirect chain). A blocked request is a network error: the loader is not called,
  and the element fires its `error` event (or the next source of a font face is tried).
- Each URL of a redirect chain is checked: a redirect to an URL that is not potentially trustworthy is a network error too.
- The navigations of the child navigables (`iframe` and `frame`, whatever initiates them: `src`, `location`, hyperlinks,
  form submissions...) and their redirects are checked as well: the navigation of an `iframe` of an `https:` page to an
  `http:` URL is a network error (the `iframe` has an error document), and so are the resources of the `embed` and
  `object` elements. The navigations of the top-level navigables are not mixed content (an `https:` page can navigate
  to an `http:` page), and neither are the navigations that no document initiates (e.g. `BrowsingContext.Navigate(url)`
  of the navigable of an `iframe`): their `http:` document is not secure, and its subresources are checked because its
  parent prohibits mixed security contexts.

Nothing is checked when no loader is configured, as nothing is fetched. The `data:` and `blob:` URLs are never blocked.
The `http:` requests of a document whose content security policy has the `upgrade-insecure-requests` directive are
upgraded before these checks (see [Upgrade Insecure Requests](#upgrade-insecure-requests)).

## Content security policy

The [fetch directives](https://w3c.github.io/webappsec-csp/#directives-fetch) of the content security policies of a
document block the requests of its subresources. The policies are the ones of the `Content-Security-Policy` headers of
the response (a list of policies separated by commas) and of the `<meta http-equiv="Content-Security-Policy">` elements
inserted in its `head` (they apply to the subsequent requests); the `Content-Security-Policy-Report-Only` policies do not
block anything. A request is blocked when an enforced policy does not allow it:

- The directive that applies is determined by the destination of the request, with the fallbacks of the specification:
  `img-src` for the images (`img`, SVG `image`, `poster`), `script-src-elem` then `script-src` for the classic and
  module scripts, `style-src-elem` then `style-src` for the style sheets (`<link rel=stylesheet>`, `@import`,
  `xml-stylesheet`, CSS modules), `font-src` for the font faces, and `connect-src` for `fetch()`, `XMLHttpRequest`, the
  JSON modules, the text modules and the hyperlink auditing pings, each falling back to `default-src`.
- The source lists are matched as specified: `'none'`, `*`, `'self'`, the scheme sources (`https:`, `data:`), the host
  sources (with the wildcards, the ports and the paths, which are ignored after a redirect), and the secure upgrades
  (`http:` matches `https:`). The scripts and the style sheets whose element has a nonce matching a `'nonce-…'` source
  are allowed, so are the scripts whose `integrity` attribute matches the hash sources, and `'strict-dynamic'` allows the
  scripts that are not inserted by the parser (and the modules they import).
- A blocked request is a network error: the loader is not called, and the element fires its `error` event. The `data:`
  and `blob:` URLs are checked too, and so is each URL of a redirect chain. The speculative requests are checked with the policies of the document and of the `meta` elements found
  speculatively (without reporting their violations: the request is checked again when it is fetched).
- `'self'` matches the origin of the URL of the response of the document (the self-origin of its CSP list), also when
  the document is sandboxed (its origin is opaque).
- `worker-src` (and `script-src` or `default-src` through the fallback list) applies the checks of the script
  directives to the workers: the nonce and `'strict-dynamic'` (a worker is not parser-inserted).
- The policies of the `Content-Security-Policy` headers come before the ones of the `Content-Security-Policy-Report-Only`
  headers (https://w3c.github.io/webappsec-csp/#parse-response-csp), then the ones of the `meta` elements.
- A response provided by a service worker whose URL is not the URL of the request is checked again (the post-request
  check).
- The violations are also reported for a request that is blocked as mixed content (e.g. a `ws:` WebSocket of an
  `https:` document whose policy has `connect-src 'none'`), as in the browsers.
- As in the browsers, a directive that contains a character that the grammar does not allow (a control character such as
  U+0001, U+000B VT or U+007F, which is not ASCII whitespace) is ignored, rather than parsed as a source list that
  matches nothing.

The host is notified of each violation, whatever the disposition of the policy, by `BrowsingContextOptions.ContentSecurityPolicyViolation`
(a `ContentSecurityPolicyViolationReport`: the stripped document and blocked URLs, the effective directive, the policy,
its disposition, the source location and the sample). It is invoked in the task that fires the `securitypolicyviolation`
event, on the thread of the event loop of the global object, and plays the role of the developer console of a browser.
The violations of a navigation response check (`frame-ancestors`, `sandbox`) are given too, with the global scope of the
document that reports them.

The Trusted Types directives are described in [trusted-types.md](trusted-types.md).

### Navigations and document directives

The navigations are checked with the policies of their source (the document that started them,
https://w3c.github.io/webappsec-csp/#should-block-navigation-request), and with the policies of their response:

- `frame-src` (then `child-src` and `default-src`) applies to the navigations of the `iframe` and `frame` elements (the
  request of the navigation of a child navigable, and each URL of its redirect chain, `data:` and `blob:` URLs included;
  as in the browsers, the `about:` URLs are not blocked), and `object-src` (then `default-src`) to the resources of the
  `object` and `embed` elements. A blocked navigation is a network error: the child navigable gets an error document
  (whose origin is opaque) and its container fires its `load` event. As in the browsers, the blocked URI of the
  violation of a navigation to another origin is the origin of its URL. The plugins are not supported, so an `object`
  or `embed` element without URL does not check `object-src`.
- `form-action` applies to the form submissions (GET and POST, and each URL of the redirect chain; the blocked URI is the
  URL of the request), including the submissions to a `javascript:` URL. It does not fall back to `default-src`.
- `javascript:` URL navigations: the pre-navigation checks (the `require-trusted-types-for` directives, whose default
  policy may convert the URL, and `form-action`), then the inline checks of the script directives (see
  [Inline checks](#inline-checks)), all with the policies of the source of the navigation, as the specification says (the
  browsers also check the policies of the target document).
- `frame-ancestors` (in a `Content-Security-Policy` or `Content-Security-Policy-Report-Only` header; it is ignored in a
  `meta` element) applies to the response of the navigation of a child navigable (`iframe`, `frame`, `object`, `embed`)
  whose URL is not local: the origin of the container document of each ancestor must match its source list (`'self'`
  matches the origin of the response). A blocked response is replaced by an error document. The violation has no global
  object: no event is fired, the `report-uri` reports are sent from the origin of the response, to the endpoints
  resolved against its URL, and the `report-to` report is sent to the endpoint of the response (its
  `Reporting-Endpoints` or `Report-To` header), as in Chromium. The specification generates this report with the
  settings object of the global object of the violation, which does not exist, so it is not visible to the
  `ReportingObserver`s.
- `X-Frame-Options` (https://html.spec.whatwg.org/multipage/document-lifecycle.html#the-x-frame-options-header): `DENY`
  blocks the response of a child navigable, and `SAMEORIGIN` blocks it unless every ancestor is same origin with it;
  several different values that include a valid one block it, and invalid values are ignored. An enforced
  `frame-ancestors` directive overrides it. As specified, the response is checked by the `frame-ancestors` directives,
  then by the embedder policy of the container document (whose `coep` report is queued), then by `X-Frame-Options`.
- `base-uri` applies to the URL of the first `base` element with an `href` attribute when its frozen base URL is set
  (when it is inserted or its `href` changes; the URL is checked when the document has policies): a blocked URL is
  ignored (the base URL of the document is its fallback base URL). As in the browsers and the tests, the blocked URI of
  the violation is the URL rather than `inline`.
- `worker-src` (then `child-src`, `script-src` and `default-src`) applies to the scripts of the dedicated, shared and
  service workers, and to the static imports of the module workers, which are checked with the policies of the
  environment that created the worker (the client of the job that installs a service worker), and whose violations are
  reported to it (https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-module-worker-script-tree; the requests
  keep the worker as their client for the loader and the service workers). The dynamic imports are checked with the
  policies of the worker.
- `<meta http-equiv="Content-Security-Policy">` only applies when the element is inserted in the `head` of a document
  that has a browsing context; `report-uri`, `frame-ancestors` and `sandbox` are ignored in it, and there is no
  report-only `meta` element.

### CSP Embedded Enforcement

An `iframe` element can require the documents loaded into it to enforce a policy, with its `csp` attribute
([CSP Embedded Enforcement](https://w3c.github.io/webappsec-cspee/), an editors' draft that only Chromium implements):

- The required CSP of a navigation of a child navigable is snapshotted when the navigation starts: the value of the `csp`
  attribute of its `iframe` container when it is valid, else the required CSP of the policy container of the container
  document (so a nested frame without `csp` attribute gets the requirement of its ancestor). A valid value is not empty,
  has at most 4096 characters, matches the serialized-policy grammar, has no `report-uri` nor `report-to` directive, and
  is subsumed by the required CSP of the container document. As in Chromium, a value that contains a line break is
  invalid (it would inject a header).
- The request of the navigation (and of its redirects) has the `Sec-Required-CSP` header, whose value is the required
  CSP.
- The response is blocked (an error document, whose origin is opaque, after the `frame-ancestors`, embedder policy and
  `X-Frame-Options` checks) unless it accepts the required policy: its URL has a local scheme, or it has an
  `Allow-CSP-From` header whose value is `*` or the serialized origin of the request (the origin of the initiator of the
  navigation), or its enforced `Content-Security-Policy` policies subsume the required policy.
- The document enforces the required policy (it is added to its CSP list, so `'self'` is the origin of the response),
  unless the policies of its response subsume it, and the required policy becomes the required CSP of its policy
  container. A document of a local scheme (`about:blank`, `about:srcdoc`, `data:`, `blob:`) has no such policy: it
  enforces the required policy in addition to the policies it inherits. A document of a local scheme loaded again from
  the session history keeps the required policy it enforced, as in Chromium (the one of its history policy container, or
  of the document state of a `blob:` document); a document that is fetched again gets the current one.
- The subsumption follows Chromium, as the web-platform-tests do: the source lists are compared for each fetch directive
  (and `base-uri`, `form-action` and `frame-ancestors`), with their fallback lists (a required `default-src` directive
  constrains every directive of the response), against the intersection of the enforced policies of the response. The
  effective source lists expand `'self'` (the origin of the response) and `*`, ignore `'none'` among other expressions,
  the nonces, the hashes and the keywords outside the script and style directives, `'unsafe-inline'` with a nonce or a
  hash, and the URL expressions of a script directive with `'strict-dynamic'`. A nonce is subsumed by any nonce (the
  value is not compared, so that an embedder cannot guess it), a hash by the same hash, a URL expression by an expression
  whose scheme, host, port and path allow it (with the secure upgrades of the scheme and of the default port: `http://a`
  allows `https://a`). The `sandbox` flags and `upgrade-insecure-requests` of the required policy must be in the policies
  of the response.
- The `object` and `embed` elements have no `csp` attribute, and their requests have no `Sec-Required-CSP` header (their
  resource is fetched by the element).
- The initial `about:blank` document of an `iframe` is not created by a navigation: it does not enforce the required
  policy of the `csp` attribute (Chromium applies its `sandbox` directive to it).

### Policy inheritance

A document gets the policies (the CSP list, its self-origin and its Trusted Types directives) of its policy container
(https://html.spec.whatwg.org/multipage/browsers.html#determining-navigation-params-policy-container):

- The initial `about:blank` document of an `iframe` or of a popup gets a clone of the policies of its creator (the
  container document, or the opener).
- An `about:srcdoc` document gets the policies of its parent (the container document).
- An `about:blank`, `data:` or `blob:` document gets the policies of the initiator of the navigation (the document that
  started it, e.g. the document that set `location`, the owner of the hyperlink or the caller of `window.open()`); a
  `blob:` document without initiator gets the ones of the environment that created the blob URL.
- The document created by the result of a `javascript:` URL keeps the policies of the document it replaces.
- The other documents get the policies of the headers of their response.
- The session history entry of an `about:` or `data:` document keeps its policies, which a traversal or a reload gives to
  the document it loads again. A `blob:` document loaded again by a traversal gets the policies of the environment that
  created the blob URL (as in the browsers).
- A worker whose URL is a `data:` or `blob:` URL inherits the policies of its creator (or of the creator of the blob
  URL), with their self-origin; the other workers get the policies of the response of their script.

The inherited policies apply to the subresources and the inline content of the document, and the `meta` elements of the
document add their policies to them. `'self'` matches the self-origin of the inherited policies (e.g. the origin of the
parent of a sandboxed `srcdoc` document, whose own origin is opaque). The `sandbox` directives of the inherited enforced
policies are applied to the document.

### Inline checks

The script and style directives also apply to the inline content
([Should element's inline type behavior be blocked by Content Security Policy?](https://w3c.github.io/webappsec-csp/#should-block-inline)),
checked with the policies of the window of the document of the element (the documents created by a window, e.g. by
`DOMParser`, the fragment parsing of `innerHTML` and the template contents, use the policies of the window):

| Content | Directives (fallback list) | When it is checked | When it is blocked |
|---|---|---|---|
| Inline `script` (HTML and SVG) | `script-src-elem`, `script-src`, `default-src` | When the script is prepared | It does not run |
| `javascript:` URL navigation (`href`, `location`, `window.open()`, `iframe` `src`) | `script-src-elem`, `script-src`, `default-src` (policies of the source document) | When the navigation runs, after the Trusted Types check | The URL is not evaluated |
| Event handler content attribute (`onclick`...) | `script-src-attr`, `script-src`, `default-src` | When the attribute is set (`setAttribute()`, the parser) | The event handler is not changed |
| Inline `style` (HTML and SVG) | `style-src-elem`, `style-src`, `default-src` | When the style block is updated (inserted, its text changed, popped off the stack of open elements of the parser) | No style sheet (`sheet` is null), and an `error` event is fired, as in the browsers |
| `style` attribute | `style-src-attr`, `style-src`, `default-src` | When the attribute is set by the parser or `setAttribute()` | Its declarations are not applied (`element.style` is empty) |

- A source list allows all the inline content with `'unsafe-inline'`, unless it contains a nonce or a hash source (or
  `'strict-dynamic'` for the scripts, the event handlers and the `javascript:` URLs).
- A nonce source matches the inline scripts and styles whose cryptographic nonce (`element.nonce`) is the nonce, unless
  the element is not nonceable (https://w3c.github.io/webappsec-csp/#is-element-nonceable): a `script` element one of
  whose attribute names or values contains `<script`, `<style` or `<link` (dangling markup), or an element that had a
  duplicate attribute when it was parsed. The nonce of an element that is not nonceable is not sent with its requests
  either (as in the browsers).
- A hash source (`'sha256-…'`, `'sha384-…'`, `'sha512-…'`, in base64 or base64url) matches the inline scripts and styles
  whose UTF-8 source text has this hash, and the event handlers, the `style` attributes and the `javascript:` URLs (the
  whole URL) when the source list contains `'unsafe-hashes'`.
- `'strict-dynamic'` allows the inline scripts that are not inserted by the parser (e.g. created by an allowed script).
- The CSSOM is not checked: `element.style` and `cssText` update the `style` attribute without the check (the
  declarations are applied), as do the copies of an element (`cloneNode()`), whose blocked `style` attribute stays
  blocked. The style sheets created by `insertRule()` and the constructed style sheets are not checked either.
- The violations are fired at the element (the `script`, `style` or element of the attribute, the hyperlink or the
  navigable container of a `javascript:` URL) with the `inline` blocked URI, and the source text (at most 40 characters)
  as `sample` when the directive contains `'report-sample'`. Their location is the one of the running script (the call
  of `setAttribute()`, the assignment of `location.href`...), else the position of the source text of an inline script
  or style created by the parser in the document (the line, and the column after the start tag).
- Nonce hiding (https://html.spec.whatwg.org/multipage/urls-and-fetching.html#nonce-attributes): when the CSP list of
  the document contains a policy delivered by a header, the `nonce` content attribute of an element connected to the
  document (in its post-connection steps, after its `connectedCallback`) is set to the empty string, so that the nonce is
  not exposed to the CSS attribute selectors; `element.nonce` keeps it. As in the browsers, the parser also hides the
  nonce that a dangling `<html` or `<body` start tag adds to the `html` or `body` element.
- When the document has a content security policy, the style sheet of a `style` element is created when its style block
  changes (when it is popped off the stack of open elements for an element of the parser), rather than lazily, so that
  the violations are reported when the style block changes.

### Violation reports

The violations of the policies are reported (https://w3c.github.io/webappsec-csp/#report-violation), whatever their
disposition: a policy of a `Content-Security-Policy-Report-Only` header reports its violations and blocks nothing, and
when several policies are violated, each one reports its violation (in the order of the CSP list).

- A `securitypolicyviolation` event (`SecurityPolicyViolationEvent`, which bubbles and is composed) is fired by a task of
  the event loop, after the script that caused the violation: at the element that caused it when it is connected to the
  document, else at the document, or at the global object of a worker. The task is not associated with the document, so
  the event is fired even when the document was replaced in the meantime (e.g. by a navigation started by a
  `javascript:` URL).
- Its `documentURI`, `blockedURI`, `referrer` and `sourceFile` are stripped for the reports: no fragment and no
  credentials, and only the scheme for the URLs whose scheme is not HTTP(S) (e.g. `data`). The `blockedURI` of a request
  is the URL of the request, not the URL of a redirect (which is not exposed to the page); it is a keyword for the other
  violations (`trusted-types-policy`, `trusted-types-sink`). `effectiveDirective` and `violatedDirective` are the
  directive of the request (e.g. `img-src` when `default-src` applies), `originalPolicy` is the policy as it was
  delivered, `statusCode` is the status of the response of the document (`200` for a worker), and `sample` is the sample
  of the violation (the first 40 UTF-16 code units, or 41 when the 40th one starts a surrogate pair, as in Firefox).
- `sourceFile`, `lineNumber` and `columnNumber` are the location of the script that is running when the violation is
  found, given by the script engine (`ScriptEngine.GetCurrentScriptLocation()`; `JavaScriptScriptEngine` gives the
  position of the running instruction, as the top frame of its stack traces, e.g. the call of
  `trustedTypes.createPolicy()`). A request that is fetched after the
  script that started it (e.g. the image of an `img` element, fetched in a microtask) has the location of the code
  running at that time.
- The `report-uri` directive sends the deprecated JSON report (`{"csp-report": {...}}`, `application/csp-report`) to each
  of its URLs, resolved against the URL of the document (or against the origin of a worker whose URL cannot be a base
  URL, e.g. a `blob:` worker inheriting the policy of its creator): a POST request of the fetch algorithm whose
  destination is `report` (`RequestDestination.Report`, given to the `DocumentLoader`), with the credentials of a
  same-origin request. As in the browsers, the reports are sent to the other origins (the specification makes a
  cross-origin "no-cors" request whose redirect mode is "error" a network error): the redirects are followed. The
  `report-uri` directive is ignored in a `meta` element, and when the policy has a `report-to` directive.
- The `report-to` directive (allowed in a `meta` element) queues a `csp-violation` report (`CSPViolationReportBody`) for
  the endpoint it names (its first token, the next ones are ignored as in the browsers), observed by the
  `ReportingObserver`s and delivered by the [Reporting API](#reporting-api). As specified, a policy without `report-to`
  generates no report (Chromium gives its violations to the observers too).
- A script whose policy has a `'report-sha256'`, `'report-sha384'` or `'report-sha512'` source expression in the
  directive that applies to it (`script-src-elem`, `script-src`, `default-src`, or `worker-src` for a worker) queues a
  `csp-hash` report with the hash of its body (the empty string for a response that is not CORS-same-origin) when its
  policy has a `report-to` directive (https://w3c.github.io/webappsec-csp/#potentially-report-hash). The strongest
  algorithm of the keywords is used, and nothing is reported for a script that fails its integrity metadata (as in the
  browsers). These reports are not visible to the observers.
- Nothing is reported for a document that has no browsing context (no event loop).

### Compilation of strings and of WebAssembly

The `script-src` directive (or `default-src`) of the policies of a window or of a worker also blocks the code compiled
from strings (https://w3c.github.io/webappsec-csp/#can-compile-strings, the HostEnsureCanCompileStrings hook) unless it
allows `'unsafe-eval'`, or `'trusted-types-eval'` when Trusted Types are enforced (`require-trusted-types-for 'script'`):

- `eval()` (direct and indirect) and the `Function`, `AsyncFunction`, `GeneratorFunction` and `AsyncGeneratorFunction`
  constructors throw an `EvalError`. The violation's `blockedURI` is `eval`, its directive is `script-src`, and its
  sample (with `'report-sample'`) is the first 40 characters of the code (the source text of the function, e.g.
  `function anonymous(a\n) {\nreturn a\n}`, for the constructors). The Trusted Types checks of the code run first (see
  [trusted-types.md](trusted-types.md)).
- The tentative `'eval-sha256-…'`, `'eval-sha384-…'` and `'eval-sha512-…'` source expressions (not in CSP3) allow the
  code whose UTF-8 hash matches; a source list that has some ignores `'unsafe-eval'`. The deprecated `report-uri` report
  of a blocked compilation has an `eval-hash` field (the SHA-256 hash of the code). The `evalHash` and `urlHash`
  attributes of the violation events and the `'url-sha256-…'` sources of `script-src` are not implemented.
- The string handlers of `setTimeout()` and `setInterval()` are checked when the timer is created, as in the browsers: a
  blocked string creates no timer (the method returns `0`), the violation is reported and no exception is thrown nor
  reported. The HTML Standard checks the string when the timer runs (on each run of an interval) and reports the
  `EvalError` (an `error` event at the global object), which no browser does and the tests do not expect.
- `new WebAssembly.Module()`, `WebAssembly.compile()`, `WebAssembly.instantiate()` (with bytes) and their streaming
  forms throw (or reject with) a `WebAssembly.CompileError` unless `script-src` allows `'wasm-unsafe-eval'` or
  `'unsafe-eval'` (https://w3c.github.io/webappsec-csp/#can-compile-wasm-bytes): the violation's `blockedURI` is
  `wasm-eval`. `WebAssembly.validate()` is not blocked, nor is a module received by `postMessage()`.
- A worker applies the policies of its own global object (the ones of its script response, or the ones of its creator
  for a `data:` or `blob:` worker).

The engine of `Meziantou.Framework.WebPlatform.JavaScript` calls the HostEnsureCanCompileStrings hook of the HTML
Standard before it parses the code, with the realm of `eval` or of the constructor, the parameter strings, the body, the
code (the source text of the function for the constructors) and the original arguments (the `TrustedScript` objects,
whose data HostGetCodeForEval gives to `eval`). The intrinsics are not replaced: the direct and the indirect calls of
`eval` are the ones of the engine. A realm whose global object has no CSP list pays a null check.

### Upgrade Insecure Requests

The `upgrade-insecure-requests` directive of an enforced policy (a `Content-Security-Policy` header, or a
`<meta http-equiv="Content-Security-Policy">` element) sets the insecure requests policy of the document to "Upgrade",
as specified by [Upgrade Insecure Requests](https://w3c.github.io/webappsec-upgrade-insecure-requests/). The fetch
algorithm then upgrades the `http:` URLs of the requests of the document to `https:` before the mixed content and the
content security policy checks, for each URL of a redirect chain. The port is kept, unless it is 80 (`http://a:8080/`
becomes `https://a:8080/`):

- All the subresources are upgraded (images, scripts, style sheets, fonts, `fetch()`, `XMLHttpRequest`, the scripts of
  the workers, `sendBeacon()`, the pings...), and so are the WebSockets (the `http:` URL of a `ws:` URL is fetched as
  `https:`, i.e. `wss:`).
- The navigations of the child navigables (`iframe`, `frame`, `embed`, `object`) and the form submissions are upgraded.
  The other navigations of a top-level navigable are upgraded when the host and port of their URL are the ones of a
  document whose policy has the directive (its upgrade insecure navigations set), e.g. the same-origin links.
- Every navigation request has the `Upgrade-Insecure-Requests: 1` header.
- The documents of the child navigables inherit the policy and the upgrade insecure navigations set of their parent
  document, and the workers the ones of their creator. As in the browsers, a document inherits the policy of its parent
  document when it is created (the specification copies it when the child navigable is created): a `meta` element added
  after an `iframe` applies to its next documents. A document that inherits a policy container (e.g. the `about:blank`
  document of a popup, see [Policy inheritance](#policy-inheritance)) also runs the initialization of the directive of
  its inherited policies.

A `Content-Security-Policy-Report-Only` policy does not upgrade anything (the directive has no violation report). The
fetch directives of the report-only policies check the URL of a request before it is upgraded (by this directive or by
Mixed Content, https://fetch.spec.whatwg.org/#main-fetch, step 4), and the ones of the enforced policies the upgraded URL:
an `http:` image upgraded to `https:` is reported by a report-only `img-src https:` directive.

## Reporting API

The [Reporting API](https://w3c.github.io/reporting/) collects the reports generated by a window or a worker (its global
object): the `csp-violation` and `csp-hash` reports of [Content Security Policy](#violation-reports), the `coep` reports
of the embedder policy, the `permissions-policy-violation` reports of [Permissions Policy](#permissions-policy), and the
`test` reports of the Generate Test Report WebDriver command.

- The endpoints of a window or a worker are the members of the `Reporting-Endpoints` header (a structured field
  dictionary) of the response of its document or of its script: the string members whose URL, resolved against the URL of
  the response, is potentially trustworthy. Without `Reporting-Endpoints`, the groups of the legacy `Report-To` header
  (JSON) are used, as in Chromium: the first potentially trustworthy URL of each group (`max_age`,
  `include_subdomains`, the priorities and the weights are ignored, and the endpoints belong to the document, not to the
  origin). A response that is not potentially trustworthy has no endpoint.
- `ReportingObserver` (`observe()`, `disconnect()`, `takeRecords()`, the `types` and `buffered` options) receives the
  reports of its global object whose type is visible to the observers (`csp-violation`, `coep`,
  `permissions-policy-violation`, `potential-permissions-policy-violation`, `test`), from a task.
  The report buffer of the global object keeps the last 100 reports of each type for the `buffered` observers. As
  specified (w3c/reporting#284), `Report` and the report bodies are dictionaries: the observers get plain objects
  (`JSON.stringify()` serializes them), without the `toJSON()` method and the `Report` interface of the browsers. The
  `coep` bodies, which have no IDL, are the objects of their JSON serialization.
- The reports are delivered by a task queued on the event loop of the global object (it runs even if the document is
  unloaded in the meantime): the reports of an endpoint are sent together, one POST request (`application/reports+json`,
  a JSON array of `{age, type, url, user_agent, body}`) per endpoint and origin of the reports, whose destination is
  `report` and mode `cors`, with the credentials of a same-origin endpoint only. The request has the environment of the
  global object as client (its cookies, as in the browsers; the specification has no client) and the keepalive flag, so
  that the reports of a document that is unloaded are sent. A `410` response removes the endpoint, the failures are
  counted, and nothing is retried. The reports whose destination names no endpoint are only observed.
- No `deprecation` or `intervention` report is generated (no feature of the library is deprecated), nor are the reports
  of Document Policy, COOP and the Crash Reporting API (`crashReport`). The `permissions-policy-violation` reports are
  described in [Permissions Policy](#permissions-policy).

## Permissions Policy

[Permissions Policy](https://w3c.github.io/webappsec-permissions-policy/) lets a page enable or disable features in its
own document and in the documents of its iframes. Each document of a browsing context has a permissions policy, created
with the document:

- The inherited policy: a feature is enabled in the document of a top-level browsing context. In a nested document, it
  is enabled when the policy of the document of the container enables it for the origin of the container document and
  for the origin of the new document, and then when the `allow` attribute of the `iframe` allows the origin of the new
  document (`allow="fullscreen"` is `'src'`: the origin of the `src` attribute; `*`, `'self'`, `'none'` and origins are
  the other values; the `allowfullscreen` attribute is `fullscreen *` unless `allow` names the feature) or, without
  `allow` declaration, according to the default allowlist of the feature: `*` (enabled) or `'self'` (enabled when the
  document is same origin with the container document). The attribute is read when the document is created: changing it
  affects the next document of the iframe. The documents of `about:blank`, `about:srcdoc`, `javascript:`, `blob:` and
  `data:` URLs have no header: their policy is their inherited policy (a `data:` document and a sandboxed document have
  an opaque origin, which only the `*` and `'src'` allowlists match).
- The declared policy: the `Permissions-Policy` header of the response, a structured field dictionary
  (`fullscreen=(self "https://a.example"), geolocation=(), camera=*`): the allowlist of a feature is `*`, `self`, and
  source expressions as strings (an origin, a scheme such as `"https:"`, or a host with wildcards such as
  `"https://*.example.com"`). The unknown features and the invalid members are ignored; a document cannot declare a
  feature that its inherited policy disables. The former `Feature-Policy` header is ignored.
- `document.permissionsPolicy` (`Document.PermissionsPolicy`) and `iframe.permissionsPolicy`
  (`HtmlIFrameElement.PermissionsPolicy`) are `PermissionsPolicy` objects: `allowsFeature(feature, origin)`, `features()`,
  `allowedFeatures()` and `getAllowlistForFeature(feature)`. The object of an `iframe` gives what a document of its
  declared origin (its `src`, `srcdoc` and `sandbox` attributes) would inherit, before the document is loaded.
  `document.featurePolicy` is not defined.

The features that the library enforces are `autoplay` (`'self'`: `play()` rejects with a `NotAllowedError` in a document
that is not allowed to use it, unless the element is muted or its window has transient activation, and the `autoplay`
attribute does not start the playback), `clipboard-read` and `clipboard-write`
(`'self'`: the asynchronous clipboard API), `cross-origin-isolated` (`'self'`: `window.crossOriginIsolated`),
`focus-without-user-activation` (`'self'`: the allow focus steps), `fullscreen` (`'self'`: `requestFullscreen()` fails and
`document.fullscreenEnabled` is false, see [Fullscreen](#fullscreen)), `sync-xhr` (`*`: `send()` of a synchronous
`XMLHttpRequest` throws a `NetworkError`, as in Chromium), `unload` (`*`: the `unload` event is not fired, as in
Chromium), `ch-ua-high-entropy-values` (`*`: `navigator.userAgentData.getHighEntropyValues()` only returns the
low-entropy values) and the `ch-*` features of the client hints (`ch-ua`, `ch-ua-mobile`, `ch-ua-platform` and
`ch-save-data` are `*`, the others `'self'`: they delegate the hints to the requests to other origins, see
[Client hints](#client-hints)), and `deferred-fetch` (`'self'`) and `deferred-fetch-minimal` (`*`), which delegate the
quota of `fetchLater()` to the cross-origin frames (see [The fetch algorithm](#the-fetch-algorithm)). The supported features (`features()`) also include features whose API is not implemented (`accelerometer`,
`ambient-light-sensor`, `bluetooth`, `camera`, `encrypted-media`,
`geolocation`, `gyroscope`, `local-fonts`, `magnetometer`, `media-playback-while-not-visible`,
`microphone`, `midi`, `payment`, `picture-in-picture`, `private-state-token-issuance`,
`private-state-token-redemption`, `screen-wake-lock`, `serial`, `speaker-selection`, `usb`, `xr-spatial-tracking`):
their policy is computed and can be observed, but there is nothing to enforce. The .NET code checks a feature with
`PermissionsPolicyAlgorithms.IsAllowedToUse` (internal).

A document that uses a feature that its policy disables queues a `permissions-policy-violation` report (see
[Reporting API](#reporting-api)) whose body has the `featureId`, the `disposition` (`enforce`) and the location of the
running script (`sourceFile`, `lineNumber`, `columnNumber`). The `Permissions-Policy-Report-Only` header declares a
policy that is not enforced: its violations are reported with the `report` disposition. When an `iframe` is loaded, the
features that its `allow` attribute delegates although the policy of the document disables them for the declared origin
of the iframe are reported as `potential-permissions-policy-violation` reports, with the `allowAttribute` and the
`srcAttribute` of the iframe. The reports are given to the `ReportingObserver`s, and sent to the endpoint named by the
`report-to` parameter of the feature in the header (`camera=();report-to=endpoint`, an endpoint of the
`Reporting-Endpoints` header).

Differences from the specification and from the browsers:

- The declared origin of a sandboxed `iframe` (or of a `data:` URL) is a new opaque origin, which is same origin with no
  document: as specified, the `'src'` allowlist of its `allow` attribute (`<iframe sandbox allow="autoplay">`) would
  delegate nothing. As in Chromium, and as the web-platform-tests expect, `'src'` then matches the opaque origin of the
  documents of the iframe (the policy of the container document must also allow an opaque origin: no declaration, or
  `*`).
- The origins of an `allow` attribute are compared exactly: the specification stores their serialization as source
  expressions, so an origin such as `https://*.example.com` (which the URL parser accepts) would be a wildcard; as in
  Chromium, the wildcards are only supported in the header.
- The specification reports a potential violation for every feature that is disabled for the declared origin of an
  iframe, including the features that a cross-origin iframe does not get by default; as in Chromium, only the features
  that the `allow` attribute names are reported.
- `getAllowlistForFeature()` of a feature that is not declared returns its default allowlist (`*`, or the origin of the
  document), which the specification does not define.
- The permissions policy does not apply to the workers (the specification does not define it yet): a worker can send
  synchronous requests whatever the policy of its owner.

## Client hints

[Client Hints](https://wicg.github.io/client-hints-infrastructure/) let a server ask for information about the user
agent, the device and the preferences of the user in request headers, instead of reading the `User-Agent` header. The
library implements the infrastructure and the [User-Agent Client Hints](https://wicg.github.io/ua-client-hints/):

```csharp
var hints = new AcceptCHCache();
using var context = new BrowsingContext(new BrowsingContextOptions
{
    DocumentLoader = loader,
    ClientHints = new ClientHintsOptions
    {
        Brands = [new() { Brand = "MyBrowser", Version = "12" }],
        FullVersionList = [new() { Brand = "MyBrowser", Version = "12.0.1" }],
        Platform = "Linux",
        Architecture = "x86",
        Bitness = "64",
        DeviceMemory = 8,
    },
    AcceptCHCache = hints, // optional: the hints the origins opted into outlive the browsing context
});
```

- The low-entropy hints are sent with every request to a potentially trustworthy URL (`https:`, or `http://localhost`):
  `Sec-CH-UA` (the brands and their significant versions: `"MyBrowser";v="12"`), `Sec-CH-UA-Mobile` (`?0`),
  `Sec-CH-UA-Platform` (`"Linux"`), and `Save-Data: on` when `ClientHintsOptions.SaveData` is set. The `User-Agent` header
  is not sent by the library (the loader sends it) and is not reduced.
- The other hints are sent to the origins that opted into them. The `Accept-CH` header of the response of a top-level
  navigation whose URL is potentially trustworthy (including its redirects) creates or overrides the entry of the origin
  in the Accept-CH cache: a structured field list of client hints tokens (`Accept-CH: Sec-CH-UA-Arch, Sec-CH-DPR`). The
  next requests to the origin have the hints. An empty header, or a header that is not a list, opts out; a response
  without header changes nothing. The header is ignored in the responses of the iframes and of the subresources.
- `BrowsingContextOptions.AcceptCHCache` is the cache (an `AcceptCHCache`, which several browsing contexts can share):
  by default, each browsing context created by the application has its own cache, shared by its auxiliary browsing
  contexts. It is kept in memory: `GetOrigins()`, `GetClientHints(origin)` and `SetClientHints(origin, hints)` let the
  application save and restore it, `Remove(origin)` and `Clear()` clear it (it can identify the user as the cookies do:
  clear it with them). The `Clear-Site-Data` header of a response clears the entry of the origin of its URL with the
  `"clientHints"`, `"cookies"`, `"cache"` and `"*"` types (also the entry that the same response creates); as the cache
  is only written by the top-level documents, a response in a document whose top-level document has another origin
  clears nothing. The header clears nothing else of the user data: the cookies and the storage are kept (it also
  discards the speculative loads, see [Speculative loading](#speculative-loading)).
- The speculative loads: a cross-site navigational prefetch is isolated, so its request only has the hints that are sent
  by default. As in Chromium, a prerendering traversable has its own cache over the one of the group: the hints that its
  documents opt into are sent with the requests of the prerendered page (with the ones of the group), and they are stored
  in the cache of the group when the page is activated. The response of a prefetch is processed (`Accept-CH`,
  `Critical-CH`) by the navigation that uses it.
- The hints and their values (`BrowsingContextOptions.ClientHints`, a `ClientHintsOptions`, and the
  [viewport](computed-style.md#usage) of `BrowsingContextOptions.Viewport`):

  | Hints | Value |
  | --- | --- |
  | `Sec-CH-UA`, `Sec-CH-UA-Full-Version-List` | `Brands`, `FullVersionList` (the brand of the library and its version by default; no arbitrary "GREASE" brand is added) |
  | `Sec-CH-UA-Mobile`, `Sec-CH-UA-Platform`, `Sec-CH-UA-Platform-Version`, `Sec-CH-UA-Arch`, `Sec-CH-UA-Bitness`, `Sec-CH-UA-Model`, `Sec-CH-UA-WoW64`, `Sec-CH-UA-Form-Factors`, `Sec-CH-UA-Full-Version` | `Mobile`, `Platform` (`Unknown` by default), `PlatformVersion`, `Architecture`, `Bitness`, `Model` (empty strings by default), `Wow64`, `FormFactors` (`Desktop`), `FullVersion` |
  | `Sec-CH-Device-Memory` | `DeviceMemory`, rounded down to a power of two between 0.25 and 8 GiB |
  | `Sec-CH-RTT`, `Sec-CH-Downlink`, `Sec-CH-ECT` | `RoundTripTime` (a multiple of 50 ms, 3 s at most), `Downlink` (Mbit/s, a multiple of 0.05, 10 at most) and `EffectiveConnectionType`, as Chromium rounds them |
  | `Sec-CH-DPR`, `Sec-CH-Viewport-Width`, `Sec-CH-Viewport-Height` | The device pixel ratio and the size of the viewport of the document (of the iframe for a nested document); the navigations have the viewport of their top-level browsing context |
  | `Sec-CH-Width` | The source size of an `img` element that has a `sizes` attribute (on the element or on its selected `source` element; the layout width for `sizes="auto"`), in device pixels. The other requests do not have it |
  | `Sec-CH-Prefers-Color-Scheme`, `Sec-CH-Prefers-Reduced-Motion`, `Sec-CH-Prefers-Reduced-Transparency` | The preferences of the viewport options (`"dark"`, `"reduce"`...) |
  | `Save-Data` | `on` when `SaveData` is set |

  The hints whose value is null (`DeviceMemory` and the network hints by default) are not sent: the default values
  describe the library, not the machine it runs on. The legacy hints that Chromium still accepts, and that the
  web-platform-tests use, are tokens too: `DPR`, `Width`, `Viewport-Width`, `Device-Memory`, `RTT`, `Downlink` and `ECT`
  send the header of that name, with the value and the policy-controlled feature of the prefixed hint.
- The delegation: the hints are sent to the origin of the document, and to the origins its
  [permissions policy](#permissions-policy) delegates them to (the `ch-*` features: `Permissions-Policy:
  ch-ua-arch=(self "https://cdn.example")`), for its subresources and for the navigations of its iframes
  (https://w3c.github.io/webappsec-permissions-policy/#algo-should-request-be-allowed-to-use-feature). They are computed
  again for each URL of a redirect chain. The requests of the workers and of the service workers have no client hint (the
  permissions policy does not apply to them yet), and a service worker does not see the hints of the requests it
  handles: they are appended when a request is sent to the network.
- `<meta http-equiv="delegate-ch" content="sec-ch-ua-arch https://cdn.example; sec-ch-dpr">` in the top-level document of a
  secure context adds hints for the requests of the document, without changing the Accept-CH cache, and delegates them
  to the listed origins (https://wicg.github.io/client-hints-infrastructure/#delegate-ch-algo). The element is ignored
  once a `script` or `style` element, or a `link` element whose resource is fetched (`stylesheet`, `preload`,
  `modulepreload`, `prefetch`), is in the document: so the elements inserted by the scripts are ignored.
- The `Critical-CH` header (`Critical-CH: Sec-CH-UA-Model`) restarts a top-level navigation, before any redirect, when
  its response opts into a hint of the header that the request did not have: the new request has it. The time of the
  restart is `PerformanceNavigationTiming.criticalCHRestart`. The navigation started with a response
  (`BrowsingContext.Navigate(url, response)`) sends no request, so it is not restarted.
- `navigator.userAgentData` (`Navigator.UserAgentData` and `WorkerNavigator.UserAgentData`, a `NavigatorUAData`) exposes
  the same values to the scripts of the windows and of the workers, in the secure contexts: `brands`, `mobile`,
  `platform`, `toJSON()` and `getHighEntropyValues(hints)` (`architecture`, `bitness`, `formFactors`, `fullVersionList`,
  `model`, `platformVersion`, `uaFullVersion`, `wow64`), which only returns the low-entropy values in a document that is
  not allowed to use the `ch-ua-high-entropy-values` feature.
- `WebPlatformFeatures.ClientHintsEnabled = false` hides the API and sends no hint (see
  [Disabling features](#disabling-features)).

Differences from the specifications, where they are silent or where the web-platform-tests expect the behavior of
Chromium (the only implementation):

- The hints of a top-level navigation are the ones of the origin of its URL (the document that is requested), for each
  URL of the redirect chain; the specification uses the hints of the document that starts the navigation, which would
  send the hints of an origin to another one. A sandboxed navigable (e.g. the popup of a sandboxed iframe) only sends
  the low-entropy hints.
- The hints of a nested document are the ones of its top-level document, filtered by its own permissions policy. The
  specification uses the entry of the Accept-CH cache of the origin of the nested document, which only a top-level
  document can create: a cross-origin iframe could not use the hints that its embedder delegates to it.
- The `allow` attribute of an iframe overrides the allowlist of the hints it names for the navigation requests of the
  iframe (`allow="ch-ua-model"`), and the hints are delegated to the opaque origin of the documents of a sandboxed iframe
  only when their allowlist is `*`.
- The requests of the scripts of the service workers have no hint, as the ones of the scripts of the dedicated and
  shared workers (the specification only excludes the latter).
- A navigation is restarted for the critical hints once per origin of its redirect chain (the specification restarts a
  navigable once), and only when the method of its request is `GET`.
- `<meta http-equiv="accept-ch" content="sec-ch-dpr, sec-ch-width">` (a list of hints, as the header) adds hints as
  `delegate-ch` does, without delegation: it was removed from the specification, but Chromium still supports it.
- The hints added by a `delegate-ch` element whose feature is already allowed for every origin (`*`) are added too; the
  specification skips them.

## Same-document navigations

- `location.hash` (and the other `Location` members when only the fragment changes) navigates to a fragment
  synchronously: the URL of the document changes (and `:target` with it), `popstate` is fired, `hashchange` is
  fired by a task, and the ancestors of the target element are revealed (the `beforematch` event is fired at the
  `hidden=until-found` ancestors before their `hidden` attribute is removed, and the closed `details` ancestors are
  opened). There is no scrolling and the target element is not focused.
- `history.pushState()` and `history.replaceState()` change the URL and the state of the session history entry. They do
  not scroll to the fragment: the target element (`:target`) stays the one of the fragment the document last scrolled
  to, until the next fragment navigation or traversal.
- `history.back()`, `history.forward()` and `history.go()` traverse the session history in a task and fire `popstate`
  and `hashchange`. As in Chromium and Firefox, a traversal is ignored while a previous traversal is loading the
  document of the top-level browsing context (e.g. the second of two `history.go()` calls of the same task); the
  traversals of the nested browsing contexts are applied one after the other, as specified. A traversal waits for the
  previous one to update its documents: the `popstate` and `hashchange` events of two same-document traversals are not
  interleaved, and a traversal started by the `navigate` event of another traversal is applied after it.
- Setting `location.hash` to the fragment of the URL (the empty string for a URL without fragment) does nothing.

The state given to `pushState()` and `replaceState()` is structured-serialized (StructuredSerializeForStorage) by the
script engine of the window, which throws a `DataCloneError` for a function or a platform object that is not
serializable; without a script engine, only the primitive values and the serializable platform objects can be
serialized. `history.state` (and the `state` of `popstate`) is a deserialization of the state of the session history
entry, also restored when a traversal loads the document of the entry again. Each document has its own `History` object
(a navigation that reuses the window of an initial `about:blank` document gives `window.history` a new object). As in
the browsers, a browsing context is rate limited: after 200 `Location` navigations, `pushState()`/`replaceState()`
calls and `history.go()`/`back()`/`forward()` traversals in 10 seconds (measured by the clock of the event loop), the
next ones are ignored (https://html.spec.whatwg.org/multipage/document-sequences.html#allowed-to-perform-a-navigation-or-history-update).

`location.ancestorOrigins` lists the origins of the container documents, computed when the document is created
(https://html.spec.whatwg.org/multipage/dom.html#internal-ancestor-origin-objects-list-creation-steps): the origin of the
parent is masked (`"null"`) when the `referrerpolicy` attribute of the `iframe` was `no-referrer` (or `same-origin` and
the documents are cross-origin) when the navigation started, and so are the ancestors that follow it and are same origin
with it. The `Referrer-Policy` of the documents does not mask them.

The session history is
the joint session history of the top-level browsing context: each entry has a step, the entries of the nested browsing
contexts are held by the document states of their parent's entries, `history.length` counts the steps of all of them
(the navigations of an `iframe` add entries to the history of the page), and a traversal changes the browsing contexts
whose entry differs at the target step. The documents of the previous entries are not kept alive (there is no
back/forward cache): a traversal to an entry of another document loads it again. A traversal cancels the ongoing
navigations of the browsing contexts it changes, and the navigations started during a cross-document traversal are
ignored. A document that is sandboxed cannot navigate (or traverse) the browsing contexts it is not allowed to navigate
(https://html.spec.whatwg.org/multipage/browsing-the-web.html#allowed-to-navigate): a `Location` navigation throws a
`SecurityError`, the other navigations are ignored.

A navigation fires `beforeunload` (synchronously, as the browsers do) at the active document of the browsing context and
of its descendants; there is no user to confirm an unload prompt, so the navigation continues. A traversal fires it at
the documents it unloads before the `navigate` events of their windows. As in the browsers, a POST request to the URL
of the document adds an entry to the session history (the specification replaces the current entry for every
navigation to the URL of the document), and a document of the web (`http:`, `https:`) cannot navigate to a `file:` URL:
the navigation is ignored. The `pageswap` event
(`PageSwapEvent`, with the `NavigationActivation` of a same-origin navigation) is fired at the window of the previous
document, then the previous document and
its descendants are unloaded (`pagehide`, `visibilitychange` and `unload`, the children first) before the new
document becomes active. The active document of a browsing context is `visible` (the system visibility state is visible).
The `pagereveal` event (`PageRevealEvent`) is fired at the window of the new document by its first rendering update,
once the document is no longer [render-blocked](#render-blocking) (a
rendering opportunity is scheduled for it when a `pagereveal` listener is added); the two events carry the
[cross-document view transition](view-transitions.md#cross-document-transitions) of the navigation, if any.

As in all the browsers, the `load` event of the container (`iframe`, `frame`, `object`, `embed`) is fired right after
the `load` event of the window of its document, before its `pageshow` event; the specification fires `pageshow` first
and fires the `load` event of the container from another task (which is still what happens while the browsing context
navigates away from the document: the container gets the `load` event of the new document only). A `load` event
listener that removes the container unloads the document (`pagehide`, `unload`); its `pageshow` event is then not fired.

When an `iframe` (or a `frame`) is removed, the documents of its browsing context and of the descendants are unloaded
before the element is removed, as the browsers do (the specification destroys them without unloading them): the event
handlers may change the tree, and the element is not removed when they removed or moved it. The initial `about:blank`
document of an `iframe` is not unloaded.

## Text directives

The [URL fragment text directives](https://wicg.github.io/scroll-to-text-fragment/) (`#:~:text=[prefix-,]start[,end][,-suffix]`,
several separated by `&`, after an optional element fragment: `#id:~:text=...`) scroll to a text of the document and
highlight it (`::target-text`, see [computed-style.md](computed-style.md)):

- **Fragment directive**: the part of the fragment after `:~:` is removed from the URL when the browsing context is
  navigated (a navigation, a fragment navigation, the `Location` setters, `window.open()`, the `src` of an `iframe`, the
  `Location` header of a redirect): `location.href`, `document.URL` and the performance entries do not expose it. It is
  the directive state of the session history entry; the entries of the fragment navigations that specify no directive
  share the one of the previous entry (the highlight stays), and `hashchange` is not fired when only the directive changes.
  A redirect whose `Location` has no fragment keeps the directive of the request, as in the browsers.
  `document.fragmentDirective` is an object without members (`FragmentDirective`, for the feature detection).
- **Search**: the text directives of the entry are searched when the parsing of a new document ends, at once for a
  fragment navigation of a loaded document (the ones that are not found while the document is still being parsed are
  searched again when the parsing ends, as in Firefox), and when a traversal changes the directive state of a document
  (the text is highlighted without scrolling). `TextDirectives` implements "find a range from a text directive" on the
  rendered text: the text nodes in shadow-including tree order (the shadow trees are searched), except the ones of the
  search-invisible subtrees (`display: none`, the form controls, `select` without `multiple`, the embedded content,
  `content-visibility: hidden` unless the element is `hidden=until-found`) and the ones that are not visible
  (`visibility`); a text matches in the text of a block (the start and the end of every element that is not an inline box
  are block boundaries, even when the element has no visible text), with the white space collapsed, case-insensitively
  (the invariant culture: the collation of the locale is not used, so the accents are not ignored), at the word boundaries
  of UAX #29 (no dictionary: each ideograph is a word). As in the browsers, a term can contain a `-` that is not
  percent-encoded.
- **Indicated part**: the first range found is the indicated part of the document when the document is allowed to scroll
  to it. Its target element (`:target`) is the first common ancestor of its start and its end that is an element, also
  in a shadow tree (as specified and as in Chrome; Firefox uses the element of its start), whose `hidden=until-found` and
  `details` ancestors are revealed (`beforematch`); the range is scrolled into view, centered in the block axis (the start of a range
  larger than the viewport), and its start is the sequential focus navigation starting point. As in the browsers, the
  target element is not focused. When the text is not found, or when the scroll is not allowed, the indicated part is the
  one of the element fragment (`#id:~:text=...` scrolls to `id`).
- **Restrictions** ([check if a text directive can be scrolled](https://wicg.github.io/scroll-to-text-fragment/#check-if-a-text-directive-can-be-scrolled)):
  a new document scrolls to a text directive only when its content type is `text/html` or `text/plain`, the user is
  involved in the navigation, its navigable is a top-level one, and the initiator of the navigation is same-origin or the
  browsing context is alone in its browsing context group (e.g. a popup opened with `noopener`). The user is involved when
  the navigation is the one of the application (`BrowsingContext.Navigate()`, the "browser UI" involvement), the
  activation of a hyperlink or the submission of a form by the user, a script navigation started while its window has
  transient activation (as in the browsers), or a navigation of a document that has not used the involvement of the
  navigation that created it (the text directive user activation: a client-side redirect can scroll once). As in the
  browsers (the specification has an [open issue](https://github.com/WICG/scroll-to-text-fragment/issues/240)), a fragment
  navigation scrolls when its initiator is same-origin with the document, without user involvement, also in an `iframe`.
  A traversal or a reload does not scroll to the text.
- **`force-load-at-top`**: a document whose `Document-Policy` header enables `force-load-at-top` does not scroll to its
  fragment (text directive or element) when it is loaded; its fragment navigations still scroll.
- **Prefetch and prerendering** (see [Speculative loading](#speculative-loading)): the fragment directive is removed from
  the URL before it is matched with the prefetched and prerendered URLs. The specifications do not integrate the text
  directives with the activation of a prerendered document: the navigation that activates it is the one the user is
  involved in, so its fragment directive, when it has one, is the directive state of the new entry (else the entry keeps
  the one of the prerendered entry), and its text directives are searched in the activated document with the
  restrictions above (at once when the document is already parsed).

`WebPlatformFeatures.TextDirectivesEnabled` disables the feature: the directive stays in the fragment.

## Navigation API

`Window.Navigation` (`window.navigation`, https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigation-api)
exposes the session history entries of the browsing context that are same origin with the current one (`entries()`,
`currentEntry`, `canGoBack`, `canGoForward`), and navigates with `navigate()`, `reload()`, `traverseTo()`, `back()` and
`forward()`, whose result has a `committed` and a `finished` promise.

- The `navigate` event is fired at the navigation API object of the document for the navigations initiated by a same
  origin-domain document (hyperlinks, forms, `location`, `window.open()` targets, `navigation.navigate()`), the fragment
  navigations, `history.pushState()`/`replaceState()`, the reloads, the traversals and the hyperlinks with a `download`
  attribute (nothing is downloaded). Its listeners can cancel the navigation, or intercept it
  (`event.intercept({ precommitHandler, handler })`) to convert it into a same-document navigation; the handlers' promises
  settle the `finished` promise and fire `navigatesuccess` or `navigateerror`, and `navigation.transition` describes the
  ongoing intercepted navigation. A new navigation, `window.stop()` or the removal of the iframe aborts the ongoing one
  (its signal is aborted and `navigateerror` is fired). The `AbortError` is created in the realm of the window of the
  navigation API object (e.g. the `DOMException` of the `iframe` that its parent removes).
- The `navigateerror` event is an `ErrorEvent` whose `message` is the error string (`TypeError: a message`) and whose
  location is the one of the error: where the exception was thrown, where the error object was created (the first frame
  of its stack) for the reason of a rejected promise, the script that aborts the navigation (e.g. the call of
  `window.stop()`) for the `AbortError` of an aborted navigation, else, as in Chromium, the URL of the document (the
  location is implementation-defined).
- `currententrychange` is fired at the navigation API object and `dispose` at the entries removed by a same-document
  navigation; `navigation.activation` describes the navigation that created the document. As in the browsers (the
  specification only updates their `History` object), the forward entries of the other browsing contexts that a push
  removes from the joint session history (e.g. the entries of the parent when its `iframe` navigates) are removed from
  their `entries()` by a task, which then fires their `dispose` events.
- The states (`navigate({ state })`, `updateCurrentEntry()`, `entry.getState()`) are serialized by the script engine of the
  window. The URL of an entry of another document whose referrer policy is `no-referrer` or `origin` is null.
- As in the browsers, a traversal by key goes to the nearest step of the joint session history where the browsing context
  shows the entry, and a navigation API tracker of a cross-document navigation can still be aborted until the document
  changes. The nested `navigate` events are limited (a listener that updates the history again throws a `SecurityError`),
  and the browsing contexts nested more than 20 levels deep do not navigate.
- The other deviations from the specification, as in the browsers: the entry of an intercepted navigation that was not
  started by `history.pushState()` or `replaceState()` has a null classic history state (`history.state`; the
  specification keeps the state of the previous entry); `NavigateEvent.sourceElement` is null when the element belongs
  to a cross-origin document (a hyperlink that navigates a cross-origin `iframe` to a fragment); the promises of a
  traversal of a nested browsing context that the `navigate` event of the top-level one cancels are rejected with an
  `AbortError` (as in Chromium and WebKit; the specification leaves them pending); a hyperlink with a `download`
  attribute aborts the ongoing intercepted navigation before its `navigate` event is fired (as in Chromium and
  Firefox). A `forward()` whose entry is removed by a navigation started before the traversal is applied rejects with
  an `InvalidStateError`, as specified (Chromium and WebKit reject with an `AbortError`).
- An intercepted navigation changes the URL without scrolling to the fragment: the target element (`:target`) is
  updated when the scroll behavior is processed (`event.scroll()`, or when the handlers are fulfilled unless
  `scroll: "manual"`). The scroll position of an intercepted reload is not changed (there is no scroll position data
  to restore for the active entry; Chromium scrolls to the fragment), and the restoration of a traversal is not skipped
  when the document was scrolled during the navigation.
- There is no back/forward cache: a traversal to an entry of another document loads it again.

## Declarative refresh

The `Refresh` header of a response and the `<meta http-equiv="refresh">` elements (e.g. `5; url=next.html`) navigate the
browsing context once the refresh comes due: the given number of seconds after the document is completely loaded (or
after the insertion of the `meta` element), measured by the clock of the event loop. Only the first refresh of a
document is processed, and the navigation replaces its session history entry. As in all the browsers, a refresh to the
URL of the document is a reload (the `navigationType` of its `navigate` event is `reload`; the specification navigates
with a history handling of "replace").

The navigation happens when `BrowsingContextOptions.DeclarativeRefreshEnabled` is true, which is the default when a
`DocumentLoader` is set: without a loader, the navigation would replace the document by an error document. A refresh to
the URL of the document reloads it after each load, as in a browser.

## Computed styles

`Window.GetComputedStyle()` computes the styles of the elements of the documents of a browsing context. The
`BrowsingContextOptions.Viewport` option sets the virtual viewport and the preferences of the user that the media
queries and the viewport-relative lengths are evaluated against: see [computed-style.md](computed-style.md). The
preferences (`PreferredColorScheme`, `PreferredContrast`, `PrefersReducedMotion`, `PrefersReducedTransparency`) and the
[forced colors mode](computed-style.md#forced-colors-mode) (`ForcedColors` and its palette `ForcedColorsTheme`: the high
contrast themes of the operating systems) apply to the documents of the browsing context and of its nested browsing
contexts. `CaretVisible` and `CaretBlinkInterval` control the painting of the [caret](rendering.md#caret) (painted, without
blinking, by default); `ScrollbarThickness` and `ScrollbarsVisible` the classic [scrollbars](rendering.md#scrollbars)
(15px, painted and interactive by default); `SmoothUserScrolling` animates the scrolls of the [user input](#user-input)
(instant by default).

## Timers and animation frames

`Window.SetTimeout()`, `SetInterval()`, `ClearTimeout()`, `ClearInterval()` (with a callback, or with the source text of a
script, which only runs when a [script engine](scripting.md) is configured), `RequestAnimationFrame()`,
`CancelAnimationFrame()`, `QueueMicrotask()` and `Performance` (`performance.now()`, `performance.timeOrigin`, and the
[performance timeline](#performance-timeline)) are
implemented as specified: the map of active timers, the nesting level clamping (4 ms after 5 nested timers), the
ordering of the timers that have the same timeout. The callbacks run from the tasks of the `DomEventLoop`, and their
exceptions are reported to the window.

There is no rendering: the animation frame callbacks run at a fixed rate (60 frames per second), once the document is
not [render-blocked](#render-blocking). The clock is the
`TimeProvider` of the event loop (`DomEventLoop.TimeProvider`, set by the `BrowsingContextOptions.TimeProvider` option;
`TimeProvider.System` by default). `DomEventLoop.RunUntilIdleAsync()` does not wait for the timers:
`DomEventLoop.WaitForNextTimerAsync()` waits until the next timer is due.

```csharp
var clock = new FakeTimeProvider(); // Microsoft.Extensions.TimeProvider.Testing
using var context = new BrowsingContext(new BrowsingContextOptions { TimeProvider = clock });
var document = context.Navigate("https://example.com/", _ => html);

context.Window.SetTimeout(() => Console.WriteLine("timeout"), 1000);
clock.Advance(TimeSpan.FromSeconds(1));  // the virtual clock: no wait
context.EventLoop.RunUntilIdle();         // prints "timeout"

// With the system clock: run the tasks and the timers until there is nothing left
do { await context.EventLoop.RunUntilIdleAsync(); } while (await context.EventLoop.WaitForNextTimerAsync());
```

## Prioritized task scheduling

[Prioritized Task Scheduling](https://wicg.github.io/scheduling-apis/) is implemented in the windows and in the
workers: `scheduler.postTask()` and `scheduler.yield()` (`GlobalScope.Scheduler`), `TaskController`, `TaskSignal`
(`priority`, `prioritychange`, `TaskSignal.any()`) and `TaskPriorityChangeEvent`.

```csharp
var scheduler = context.Window.Scheduler;
var controller = new TaskController(new TaskControllerInit { Priority = TaskPriority.Background });
Task<object?> task = scheduler.PostTask(() => Compute(), new SchedulerPostTaskOptions { Signal = controller.Signal });
controller.SetPriority(TaskPriority.UserBlocking);   // the task now runs before the other tasks
context.EventLoop.RunUntilIdle();
```

- **Tasks.** `postTask(callback, { priority, signal, delay })` returns a promise (a `Task<object?>`) resolved with the
  result of the callback, rejected with the exception it throws, or with the abort reason of the signal when it is
  aborted before the callback runs. The priority is the `priority` option (fixed), else the priority of the signal when
  it is a `TaskSignal` (it follows `TaskController.setPriority()`), else `user-visible`. A delayed task is queued when its
  delay elapsed, on the clock of the event loop.
- **Order.** Each scheduler has a task queue per priority and kind (task or continuation), and one per `TaskSignal`
  whose priority can change. The event loop runs the oldest task of the queue of the highest effective priority of all
  its schedulers (`user-blocking` > `user-visible` > `background`; the continuations of a priority precede its tasks).
  The specification leaves the order between these tasks and the tasks of the other task sources to the user agent;
  the library follows its suggestion, as Chromium: the `user-blocking` tasks and continuations and the `user-visible`
  continuations run before the other tasks (a loop of `await scheduler.yield()` delays the timers), the `user-visible`
  tasks are ordered with the other tasks (e.g. the timers) in the order they were queued, and the `background` tasks
  and continuations run when no other task is queued.
- **Continuations.** `yield()` returns a promise resolved by a continuation. In the callback of a posted task, it
  inherits the scheduling state of the task: its priority source (the continuations follow the `TaskSignal`) and its
  abort source (the promise is rejected when the signal is aborted); elsewhere it is a `user-visible` continuation.
  The state is the one of the scheduler that posted the task: a task of another window that calls `yield()` of this
  window inherits nothing.
- **Propagation of the scheduling state.** The state of a task is set while its callback and the microtasks of its
  microtask checkpoint run. The promise reactions capture the state of the event loop when they are created
  (HostMakeJobCallback: `then()`, and the reactions that the engine creates for `await`, the async generators...) and
  restore it when they run (HostCallJobCallback), so an `await` of a promise that another task settles (a timer, a
  fetch) continues with the state of the code that awaited it. The microtasks of `queueMicrotask()` restore the state
  they were created with, and a continuation of `yield()` restores the state that `yield()` inherited. The reactions
  capture nothing until a scheduler is used by a global object of the script engine.
- **Documents that are not fully active.** The tasks of the scheduler of a document that is no longer fully active
  (e.g. its `iframe` was removed) never run, and `postTask()` and `yield()` reject with a `NotSupportedError`, as in the
  browsers.
- **Not done.** `requestIdleCallback()` (whose callbacks would have the `background` scheduling state), and
  `scheduler.isInputPending()` (not in the specification).

`WebPlatformFeatures.PrioritizedTaskSchedulingEnabled` hides the API from the scripts.

## Page visibility

`BrowsingContext.IsVisible` is the [system visibility state](https://html.spec.whatwg.org/multipage/interaction.html#system-visibility-state)
of a top-level browsing context, true by default. The host sets it to false when its window is minimized, when its tab
is in the background or when another window covers it:

```csharp
browsingContext.IsVisible = false; // tasks of the event loop: document.visibilityState is "hidden", visibilitychange is fired
browsingContext.IsVisible = true;
```

The visibility state of the document of the top-level browsing context, then of the documents of its nested browsing
contexts (which share its state), is updated by tasks of the event loop: `document.visibilityState` and
`document.hidden` change, `visibilitychange` is fired at each document (it bubbles to the window), and a
`VisibilityStateEntry` (`visibility-state`) is added to the performance timeline of each window (the first entry of a
document is its initial state, at 0). A document that is created while the browsing context is hidden starts hidden. A
document is also hidden when it is unloaded (the documents of the nested browsing contexts first, their initial
`about:blank` documents included). The view transitions are skipped when their document becomes hidden; the rendering
updates, the timers and the animation frames are not throttled while a document is hidden. The documents of a
prerendering traversable (see [Speculative loading](#speculative-loading)) are hidden and never have the system focus,
whatever the state of the browsing context that prerenders them; they get its state when they are activated.

## Focus

The focus is implemented as specified (https://html.spec.whatwg.org/multipage/interaction.html#focus): the focused area
of each document, the focusing and unfocusing steps (`focus()`, `blur()`, `window.focus()`), the `focus`, `blur`,
`focusin` and `focusout` events, `document.activeElement` and `ShadowRoot.activeElement` (retargeted), `hasFocus()`, the
`:focus`, `:focus-within` and `:focus-visible` pseudo-classes, the focus delegates (`delegatesFocus`, dialogs), the
autofocus candidates, the focusing steps of the dialogs and of the popovers (and the restoration of the previously
focused element), the focus fixup rule and the sequential focus navigation order (tabindex, shadow trees, slots,
popovers, reading flows). Only the documents that have a browsing context have focusable areas. As in the browsers,
`blur()` also unfocuses the focused element when it is not focusable anymore (e.g. its `tabindex` attribute was removed
and the focus fixup rule has not run yet; the unfocusing steps of the specification do nothing then).

`BrowsingContext.HasSystemFocus` is the system focus of a top-level browsing context
(https://html.spec.whatwg.org/multipage/interaction.html#tlbc-system-focus), true by default: the host sets it when its
window is deactivated or activated. When it is lost, the focused element then the window of its document get `blur`
(`focus` in the reverse order when it comes back), `document.hasFocus()` is false, and the scripts still move the focus,
without focus events (as in the browsers). One top-level browsing context of a group has the system focus at a time: a
new auxiliary browsing context (`window.open()`) takes it from a task, and gives it back to its opener when it is
closed.

Where the browsers all differ from the specification, the browsers are followed (the `focus` directory of WPT tests
their behavior):

- the `focus` and `blur` events of the windows follow the focused document (the document of the focused area), not the
  focus chain: the window of a document gets `blur` when the focus moves to another document, even to the document of
  one of its nested browsing contexts, and `focus` when the focus moves to it from another document; the windows of
  the other documents of the chain get no event;
- a document that loses the focus to an element of another document no longer has a focused element (its
  `activeElement` is its body), and a navigable container is the active element of its document only while the focus is
  in its content navigable;
- `window.focus()` makes the document the focused document and keeps its focused element, unless the element is a
  navigable container (the viewport of the document is then focused);
- `focus()` on a `label` that is not focusable focuses its labeled control;
- the nodes of the document of an inert navigable container are not inert: the container cannot be focused, clicked or
  entered by the sequential focus navigation, but the scripts of its document focus its elements, and `Tab` moves within
  the document and leaves it.

The sequential focus navigation order is the flattened tabindex-ordered
[focus navigation scope](https://html.spec.whatwg.org/multipage/interaction.html#focus-navigation-scope) of the document
(`Html/FocusNavigationScopes.cs`). The scope owners are the document, the shadow hosts, the slots and the triggers of the
showing popovers: the areas of a scope are visited together, in the order of their tabindex values, at the position of
the owner in its own scope (after the owner when it is focusable; a scope whose owner has a negative tabindex value is
skipped). As in the browsers (the specification differs): a slot that generates no box (`display: contents`, its default
style) is focusable by script but is not a tab stop, the fallback content of a slot is in the scope of the slot (not in
the one of the shadow host), and a showing popover that contains its trigger is not in the scope of the trigger. The
navigation is searched scope by scope, so it also starts from an element that is not in the order (an element with a
negative tabindex value or in the shadow tree of a host with a negative tabindex value, a node that is not focusable):
the search continues in tree order from the position of the element in its scope and, when the scope is exhausted, from
the owner of the scope in the enclosing scope (backward, a focusable owner is the area before its scope).

As in the browsers (unspecified), the radio buttons of a
[radio button group](https://html.spec.whatwg.org/multipage/input.html#radio-button-group) are a single stop of the
sequential focus navigation (`FocusModel.IsRadioTabStop`): the checked radio button of the group when it can be focused,
else the first one that can be focused in tree order (in both directions); a radio button without group is a stop. The
arrow keys move the focus within the group (`HtmlInputElement.RunRadioKeyDefaultAction`): the down and right arrow keys
focus the next radio button that can be focused in tree order, the up and left arrow keys the previous one (left and
right are reversed when its direction is `rtl`), wrapping around; the radio button is scrolled into view (`nearest`) and
checked by a synthetic click (`click`, `input` and `change`).

The [`reading-flow` and `reading-order`](https://drafts.csswg.org/css-display-4/#reading-flow) properties reorder the
sequential focus navigation (not the layout). Their focus navigation is not specified yet: the scopes are the ones of
Chromium. A reading flow container (a block container with `reading-flow: source-order`, a flex container with
`source-order`, `flex-visual` or `flex-flow`, a grid container with `source-order`, `grid-rows`, `grid-columns` or
`grid-order`) owns a scope whose items are its children in the flat tree, visited in the reading flow order whatever
their positive tabindex values: the items are sorted by `reading-order`, then by the reading flow of the container, and
the children that the container does not lay out (the absolutely positioned boxes) come last, in tree order.
`source-order` follows the tree; `flex-flow` and `grid-order` follow the order-modified document order (`order`);
`flex-visual` follows the flex lines and the items of each line in the writing mode of the container (the items of a line
are reversed by `row-reverse` and `column-reverse`, the lines by `wrap-reverse`; the lines come from the layout);
`grid-rows` and `grid-columns` follow the first row then the first column of the grid areas of the items, or the first
column then the first row (from the layout of the grid). Each item owns the scope of its descendants (ordered by their
tabindex values). A child with `display: contents` owns a scope with the same reading flow for its own children, at the
position of its first item; a slot without box in such a scope is transparent (its assigned nodes are items of the scope
of its parent).

As in browsers, the sequential focus navigation starting point is the clicked node, the blurred element, the target of
a fragment navigation (scrolling to the fragment also focuses the target, or the viewport), or the position in the flat
tree of a removed node that contained the focused element: the navigation continues from there. When the starting point
is inside the focused element, the nearest focusable area inside the element is selected before the next one in the
tabindex order. Focusing a text control collapses the selection before it, and focusing an editing host that does not
contain the selection puts the caret at the start of its content (as in the browsers, at the start of the parent of the
first element that is not editable, or of the first `hr` element, when only empty inline elements precede it).

Nothing is rendered, so an element is considered rendered when its document has a browsing context and the computed
styles of the element and of its ancestors in the flat tree generate a box (the [box generation query](layout.md#box-generation-query)):
neither has `display: none`, the element is not in the contents of a replaced element (e.g. the fallback content of a
`video`, the options of a `select` whose contents are not rendered with base appearance, or whose picker is closed) except the
[relevant canvas fallback content](https://html.spec.whatwg.org/multipage/canvas.html#being-used-as-relevant-canvas-fallback-content),
it is not in a never-rendered SVG element, no ancestor skips its contents (`content-visibility: hidden`, the contents of a
closed `details` element other than its summary), and (as in browsers) the element is not `visibility: hidden`. An
element with `display: contents` delegates its rendering to its children. An element is focusable when it is rendered, it is not
actually disabled, it is not inert (the `inert` attribute, or a document blocked by a modal dialog), it does not delegate
its focus, and it has a tabindex value or is focusable by default (`a` and SVG `a` with `href`, `button`, `input` that is
not hidden, `select`, `textarea`, the `summary` of a `details`, the editing hosts, the navigable containers, the media
elements with `controls`, the options of a `select` whose contents are rendered with base appearance (a list box with
base appearance is not focusable itself), and, as in browsers, the `area` elements with `href` of an image map used by a
rendered `img`). The button of a drop-down `select` (its first child element when it is a `button`) is inert. The allow focus steps check that the document is allowed to use the "focus-without-user-activation" feature
(default `'self'`, see [Permissions Policy](#permissions-policy)) or that its window has transient activation: a
cross-origin iframe cannot take the focus by script, unless the `allow` attribute of the iframe delegates the feature.

The focus fixup rule and the flush of the autofocus candidates run at the rendering opportunities of the event loop,
which are scheduled when an element is focused and the document changes, or when an autofocus element is inserted.

The user agent default actions run for the trusted `MouseEvent` and `KeyboardEvent` objects of the user input (the
pointer events fired by [`BrowsingContext.Input`](#user-input), and the keyboard events dispatched by the host, e.g. the
WPT test driver): `mousedown` (primary button; the middle and the secondary buttons too, as in the browsers) puts the
caret at the position of the pointer, or starts a selection (see [Selection with the pointer](#selection-with-the-pointer)),
and focuses the clicked focusable area (or the viewport), `Control` or `Meta` with `A`, `Z`, `Shift`+`Z`, `Y`, `B`, `I`
or `U` selects all, undoes, redoes or formats (see [Editing](#editing)), `Tab` (with `Shift`) moves the focus sequentially, `Escape` is a close request for the
[close watchers](#close-watchers) of the window (the open dialogs, the auto and hint popovers, the open picker of a
`select` and the `CloseWatcher` objects), `Enter` and the space key activate the buttons and the links, the
[select elements](#select-elements) open their picker and pick their options, the up and down arrow keys (and a press on
the spin buttons, the 15px at the inline-end edge of the padding box) step the value of an `<input type=number>`
(`beforeinput` with the `insertReplacementText` input type, which can cancel the change, then `input` and `change`),
and the arrow keys,
`PageUp`, `PageDown`, `Home`, `End` and the space key scroll the nearest scrolling box of the target of the key that can
scroll (of the node the user last clicked when the viewport is focused), unless the key edits a text control, a `select`
element or editable content: 40px for an arrow key, 87.5% of the scrollport for a page. `Alt`+`Shift`
with the assigned access key of an element (the first token of its `accesskey` attribute that is a single code point,
also returned by `accessKeyLabel`, e.g. `Alt+Shift+B`) triggers the action of its command
(https://html.spec.whatwg.org/multipage/interactive-elements.html#the-accesskey-attribute): the element is focused and
clicked (a link is only clicked), an option is picked, and a legend triggers its accesskey delegatee. The trusted
`pointerdown` and `pointerup` events light dismiss the auto and hint popovers and the dialogs whose `closedby` attribute
is `any` (https://html.spec.whatwg.org/multipage/popover.html#popover-light-dismiss), before they are dispatched (an
event targeted at a modal dialog at a point outside of its border box, from `getBoundingClientRect()`, is a click on its
`::backdrop`, outside of the dialog). The trusted pointer and mouse events also update the state of the pointing device before they are dispatched (`:hover` and
`:active`, see [User action pseudo-classes](selectors.md#user-action-pseudo-classes)). As in the browsers
(https://github.com/whatwg/html/issues/5886), the path of the trusted `mousedown`, `mouseup`, `click` and `dblclick` events
stops at a disabled form control (not a `fieldset`): they are not dispatched to a disabled form control, and do not
propagate from its descendants to it and its ancestors.

The scroll navigation controls of CSS Overflow 5 (https://drafts.csswg.org/css-overflow-5/#scroll-focus, see
[Scroll markers and scroll buttons](layout.md#scroll-markers-and-scroll-buttons-csslayoutlayoutenginescrollmarkerscs)) are
focusable and activatable pseudo-elements. They are not nodes: the trusted pointer and mouse events designate the control
whose box is at their point (the target of the events is the originating element, and the pseudo-elements are not in the
event path), and a focused control is recorded next to the focused area of its document, which is then its scroll
container (`document.activeElement`; the scroll container receives the focus events and the keyboard events).
A press of the primary button on a `::scroll-marker` or on an enabled `::scroll-button()` focuses it and empties the
selection; a click, or Enter or the space key when it is focused, activates it: a button scrolls its originating element
by one page in its direction, with its `scroll-behavior`; a marker scrolls its originating element into view (aligned by
its `scroll-snap-align`, `start` by default) in the scroll containers up to the one of its group, then, in the `links` mode
of `scroll-marker-group` (the default), the focus is lost and the sequential focus navigation continues at the originating
element (which is the next area when it is focusable), and in the `tabs` mode the marker keeps the focus. The arrow keys
activate the next or the previous marker of the group of the focused marker, in the writing mode of the scroll container,
cycling. In the sequential focus navigation, the controls of a scroll container are placed around it: the markers of its
group when it is before, its enabled buttons, the scroll container and its contents, the markers of its group when it is
after; every marker is a tab stop in the `links` mode, only the active one in the `tabs` mode, where `Tab` moves from the
focused marker to its originating element or the next area after it (for the marker of a column, the first element that
starts in the column or after it). Not done: the focus events are fired at the scroll
container (not retargeted from a pseudo-element), and the pointer actions are not routed into nested browsing contexts.

`:focus-visible` follows the heuristics of the browsers (https://drafts.csswg.org/selectors/#the-focus-visible-pseudo),
decided when the focus moves (before the focus events are fired): the focus is indicated after a keyboard navigation
(`Tab`), for the elements that support the keyboard input (text controls, `select`, editing hosts), and for the elements
focused by script, unless the last focus change made by the user was a click that focused an element, with no keyboard
interaction since. A click never indicates the focus of the clicked element. A trusted `keydown` event (other than a
modifier key, or a key pressed with `Control`, `Alt` or `Meta`) indicates the focus of the focused element.
`:focus-visible` matches the focused element only (not the shadow hosts of its trees). When the focused element is
removed, the next `Tab` continues from its position.

When the focus leaves a text control (`input`, `textarea`) whose value the user edited (`Editing.UserEditTextControl()`,
used by the WPT test driver), the change is committed: the user validity of the control becomes true (`:user-valid` and
`:user-invalid`) and `change` is fired when the value changed.

```csharp
var input = (HtmlInputElement)document.QuerySelector("input")!;
input.Focus();
Console.WriteLine(document.ActiveElement == input);  // True
Console.WriteLine(input.Matches(":focus-visible"));  // True
```

## Close watchers

The close requests of the user and the close watchers are implemented as specified
(https://html.spec.whatwg.org/multipage/interaction.html#close-requests-and-close-watchers). The close request is the
`Escape` key: when the trusted `keydown` event of the key is not canceled (the event is not an activation triggering
input event; `keypress` and `keyup` cannot prevent the request), the close watchers of the window of the document of the
target of the event are processed. Each window has a close watcher manager, whose close watchers are the ones of the
open `dialog` elements (disabled while the computed `closedby` state is `none`), of the showing `popover=auto` and
`popover=hint` elements, of the open pickers of the [select elements](#select-elements), and of the `CloseWatcher`
objects:

```csharp
var watcher = new CloseWatcher(window);
watcher.AddEventListener("cancel", e => { if (hasUnsavedData) e.PreventDefault(); });
watcher.AddEventListener("close", _ => sidebar.Hide());
watcher.RequestClose(); // cancel (cancelable), then close; Close() only fires close, Destroy() fires nothing
```

- **Groups.** A close request closes the close watchers of the most recent group, from the most recent one. A close
  watcher starts a new group only when the number of groups is lower than the allowed number of groups (1, plus 1 for
  each user activation of the window that follows the creation of a close watcher); otherwise it joins the last group,
  so that the scripts cannot stack close watchers to trap the close requests.
- **`cancel`.** A close request of the user fires `cancel` before `close`; the event is cancelable only when the window
  has history-action activation and a group is still allowed, and canceling it consumes the history-action activation:
  the second close request in a row closes the watcher. `requestClose()` (of `CloseWatcher` and of `dialog`) always
  fires a cancelable `cancel`. The popovers have no `cancel` event.
- The close watchers of a document that is not fully active do nothing, and `new CloseWatcher()` throws an
  `InvalidStateError` there. The `signal` option of the constructor destroys the watcher when it is aborted.
- As in the browsers (the spec keeps them), the return value and the source element of a `dialog.requestClose()` that
  `cancel` prevented are not used by the next close request.
- A close request first [fully exits fullscreen](#fullscreen) when the top-level document has a fullscreen element (the
  close watchers are then not processed). There is no fallback behavior when nothing watches the close request. A host
  whose close request is not a key (e.g. a back button) dispatches the trusted `keydown` event of `Escape`.

## Fullscreen

The [Fullscreen API](https://fullscreen.spec.whatwg.org/) is implemented: `Element.requestFullscreen(options)`
(`Element.RequestFullscreen()`), `document.exitFullscreen()`, `document.fullscreenElement` and
`shadowRoot.fullscreenElement` (retargeted out of the shadow trees), `document.fullscreenEnabled`, the historical
`document.fullscreen`, and the `fullscreenchange` and `fullscreenerror` events (`onfullscreenchange`,
`onfullscreenerror`). Nothing is displayed: an element is "displayed fullscreen" when its fullscreen flag is set and it
is in the top layer of its document.

```csharp
var context = new BrowsingContext(new BrowsingContextOptions
{
    // The host makes its window fullscreen and gives the size of the screen (null keeps the viewport)
    EnterFullscreen = (context, options) => new FullscreenViewportSize(1920, 1080),
    ExitFullscreen = context => { /* restore the window */ },
});
```

- **Request.** `requestFullscreen()` returns a promise (a `Task`). The element must be an HTML element, an `svg` or a
  `math` element, not a `dialog`, connected, not a showing popover, in a document that is allowed to use the `fullscreen`
  [policy-controlled feature](#permissions-policy) (the `allowfullscreen` and `allow` attributes of the iframes), and its
  window must have transient activation, which the request consumes. The steps that the specification runs in parallel
  are a task: the element is not fullscreen when the method returns. The task asks the host for the size of the viewport
  (`BrowsingContextOptions.EnterFullscreen`, with the `navigationUI` option), sets the fullscreen flag of the element and
  of the containers of the ancestor navigables (the `iframe` elements up to the top-level document), which are added to
  the top layer (the auto and hint popovers that do not contain them are hidden), and resolves the promise. A request
  that fails rejects with a `TypeError` and fires `fullscreenerror`.
- **Events.** `fullscreenchange` and `fullscreenerror` are fired by the fullscreen steps of the rendering update that
  follows, in the order of the documents of the tree (the document of a container first), at the element, or at its
  document when it is no longer connected, before the animation frame callbacks. The rendering opportunity is a task
  queued with the request, not the next frame of the frame rate.
- **Exit.** `exitFullscreen()` unsets the flag of the fullscreen element (the topmost element of the top layer whose
  flag is set): the previous element of a nested request is the fullscreen element again. The containers of the ancestor
  navigables exit with the last fullscreen element of their navigable, and the documents of the descendant navigables
  exit too. The document fully exits fullscreen when it is unloaded (a navigation, the removal of its `iframe`), and
  when the user makes a close request (the `Escape` key), before the [close watchers](#close-watchers). Removing the
  fullscreen element from its document exits fullscreen; removing another element of the stack only unsets its flag.
  Moving an element with `moveBefore()` keeps it fullscreen.
- **Keyboard lock.** With `keyboardLock: "browser"`, the `Escape` key is given to the page (it is not a close request)
  and holding it for 1.5 seconds on the clock of the event loop (from its first `keydown` event to a repeated one) exits
  fullscreen, whether the page cancels the events or not, as in Chromium. The other keys are not locked: the library
  handles no other key for the user agent.
- **Rendering.** `:fullscreen` matches the elements whose flag is set and the shadow hosts of the fullscreen element;
  `:modal` matches the elements whose flag is set. The rules of the user agent style sheet of the specification make
  the fullscreen elements fill the viewport (`position: fixed`, `inset: 0`, `width` and `height` of `100%`,
  `transform: none`, without border and padding for an `iframe`), over a black `::backdrop`. The root element is left
  alone: it already fills the viewport. The viewport keeps its size unless `EnterFullscreen` returns another one (the
  dimensions of the screen): `resize` is then fired by the resize steps of the next rendering update, and
  `BrowsingContextOptions.ExitFullscreen` is called when the top-level document exits fullscreen (the viewport gets the
  size of the options back).
- **Not done.** The `allowWithoutGesture` permission, the screen orientation lock that fullscreen releases, the
  `toggle-fullscreen`, `request-fullscreen` and `exit-fullscreen` commands of the buttons (an Open UI proposal), and the
  inertness of the rest of the document while an element is fullscreen (as in the browsers; HTML only makes the documents
  that a modal dialog blocks inert).

`WebPlatformFeatures.FullscreenEnabled` hides the API from the scripts.

## Pointer lock

The [Pointer Lock API](https://w3c.github.io/pointerlock/) is implemented: `Element.requestPointerLock(options)`
(`Element.RequestPointerLock()`), `document.exitPointerLock()`, `document.pointerLockElement` and
`shadowRoot.pointerLockElement` (retargeted out of the shadow trees), the `pointerlockchange` and `pointerlockerror`
events (`onpointerlockchange`, `onpointerlockerror`), and `MouseEvent.movementX` and `movementY`. The pointer-lock state
belongs to the top-level browsing context: it locks the mouse of [`BrowsingContext.Input`](#user-input) to an element of
one of its documents. A host without cursor has nothing to do; a host with a window hides its cursor and reads the
movements of its mouse:

```csharp
var context = new BrowsingContext(new BrowsingContextOptions
{
    // Return false to refuse the lock (e.g. when options.UnadjustedMovement cannot be honored)
    RequestPointerLock = (context, options) => { /* hide the cursor, read the raw input */ return true; },
    ExitPointerLock = context => { /* show the cursor at context.Input.Mouse.X, Y */ },
});

context.Input.Mouse.MoveBy(3, -2);   // pointermove and mousemove at the target, with movementX: 3 and movementY: -2
context.ExitPointerLock();           // the unlock gesture of the host, or the system took the pointer back
```

- **Request.** `requestPointerLock()` returns a promise (a `Task`). The element must be connected, in a fully active
  document whose top-level browsing context has the system focus (`WrongDocumentError`), whose window has transient
  activation unless the document released a pointer lock with `exitPointerLock()` (`NotAllowedError`; the request does
  not consume the activation), and that is not sandboxed without `allow-pointer-lock` (`SecurityError`). The steps of the
  lock requests queue, which the specification runs in parallel, are a task: the request fails with an
  `InvalidStateError` while the pointer is locked to an element of another document; the target changes without asking
  the host when the pointer is already locked in the document with the same options; otherwise the host is asked
  (`BrowsingContextOptions.RequestPointerLock`; the default accepts, also with `unadjustedMovement`: the movements that a
  host injects are not adjusted by the library) and its refusal is a `NotSupportedError`. The task fires
  `pointerlockchange` at the document (not at the element) and resolves the promise; a failure fires `pointerlockerror`
  from a task, then rejects the promise. `pointerLockElement` is null while the request is pending.
- **Events of the locked mouse.** The pointer keeps its position (`clientX`, `clientY`, `screenX`, `screenY`, and
  `PointerInput.X` and `Y`): `PointerInput.Move()` and `MoveBy()` fire `pointermove` (after `pointerrawupdate`) and
  `mousemove` at the pointer-lock target with the movement from the previous move, without boundary event. The presses,
  the releases, the clicks and the wheel events are fired at the target too, and the wheel scrolls from the target.
  Locking the pointer releases the pointer captures (`lostpointercapture` precedes the next event), and
  `setPointerCapture()` throws an `InvalidStateError` in the document of the target. The pens and the touches are not
  locked. When the pointer is not locked, `movementX` and `movementY` of the move events are the difference with the
  position of the previous move (not adjusted either).
- **Exit.** `exitPointerLock()` exits the lock of an element of the document, at once: `pointerLockElement` is null when
  it returns, `BrowsingContextOptions.ExitPointerLock` is called, and `pointerlockchange` is fired by a task. The lock
  is also exited when the target is removed from its document (or moved to another one), when its document is unloaded
  (a navigation, the removal of its `iframe`), and by the user agent: the `Escape` key (a trusted `keydown` event, whether
  the page cancels it or not: the default unlock gesture; it is also a [close request](#close-watchers), which exits
  fullscreen), `BrowsingContext.ExitPointerLock()`, and the loss of the system focus (`HasSystemFocus`) or of the
  visibility (`IsVisible`) of the top-level browsing context. After an exit by the user agent, the document needs a user
  activation to lock the pointer again. Entering and exiting fullscreen keeps the lock; with the
  [keyboard lock](#fullscreen) of a fullscreen element, holding `Escape` exits both.
- **Not done.** The `pointer-lock` policy-controlled feature (not in the specification), and a delay before a new lock
  after an exit by the user.

`WebPlatformFeatures.PointerLockEnabled` hides the API from the scripts (`movementX` and `movementY` stay).

## Editing

The editing hosts (`contenteditable` in the True or Plaintext-Only state, the document element in design mode) and the
editable nodes follow https://w3c.github.io/editing/docs/execCommand/#editable: they define `isContentEditable` and
`:read-write`. Turning `designMode` on puts the caret at the start of the document element when the selection is empty
and focuses the document element.

`document.execCommand()`, `queryCommandEnabled()`, `queryCommandIndeterm()`, `queryCommandState()`,
`queryCommandSupported()` and `queryCommandValue()` implement the editing specification
(https://w3c.github.io/editing/docs/execCommand/): the commands edit the DOM at the active range (the range of the
selection) when its boundary points are in an editing host, and fire `input` at the editing host with the `inputType` of
the command (e.g. `formatBold`, `insertParagraph`, `deleteContentBackward`; no `beforeinput`, as in Chromium, also for
the `cut` and `paste` commands). The methods throw an `InvalidStateError` in a document that is not an HTML document.
While a text control is focused, the commands apply to its value (see [Text controls](#text-controls)). As in Chromium
and Gecko, a command that a script runs while another command of the document runs (e.g. from a listener of its `input`
event) is not run and returns false (https://github.com/w3c/editing/issues/200).

- **Inline formatting**: `bold`, `italic`, `underline`, `strikethrough`, `subscript`, `superscript`, `fontName`,
  `fontSize`, `foreColor`, `backColor`, `hiliteColor`, `createLink`, `unlink` and `removeFormat`, with their state,
  value and indeterminate flag, the state and value overrides of a collapsed selection (e.g. `bold` then `insertText`),
  and `styleWithCSS` / `useCSS` (a `span` with a `style` attribute instead of `b`, `i`, `font`...).
- **Block formatting**: `formatBlock`, `indent`, `outdent`, `insertOrderedList`, `insertUnorderedList`, `justifyCenter`,
  `justifyFull`, `justifyLeft` and `justifyRight`, and `defaultParagraphSeparator` (`div` by default, or `p`).
- **Insertion and deletion**: `insertText`, `insertParagraph`,
  `insertLineBreak`, `insertHTML`, `insertImage`, `insertHorizontalRule`, `delete` and `forwardDelete`: the selection is
  deleted first, the blocks of its boundary points are merged, the white space around the edit is canonicalized
  (collapsible spaces and no-break spaces), and a `br` keeps the blocks that become empty visible.
- `selectAll` selects the contents of the editing host of the selection, or of the body element; `copy`, `cut` and
  `paste` are the clipboard commands (see [Clipboard](#clipboard)).

The algorithms are the ones of the specification (the `EditingAlgorithms` class is a port of its reference
implementation, which the `editing/run` tests of WPT were generated with). The notions that the specification bases on
the rendering are computed from the cascade, without layout: a block node is a node whose computed `display` is not
inline-level, a node is visible when it is not in a `display: none` subtree and is not collapsed white space, and the
`br` elements that are collapsed or extraneous are found from the contents of their line (a `br` followed by nothing on
its line, and preceded by a content of the line when it is extraneous). Where the browsers all differ from the
specification, the browsers are followed, as the tests expect:

- the formattable block names of `formatBlock` also include `article`, `aside`, `blockquote`, `footer`, `header`,
  `hgroup`, `nav` and `section`, the `listing` and `xmp` elements are wrapped instead of replaced, and the value of the
  command is `dl` for the `dt` and `dd` elements of one list;
- `strikethrough` creates a `strike` element (the specification: `s`), and `fontName`, `fontSize` and `foreColor` split
  the inline elements that have another value (e.g. `<font size=1>`) instead of nesting the new element in them;
- the `br` of a block that a deletion empties is a child of the block (the inline elements that the deletion empties are
  removed first), an inline editing host that a deletion empties gets no `br`, and the elements that are not editable, the
  replaced elements and the form controls are deleted as a whole, as an `img`;
- `insertImage` splits the inline elements of the insertion point (the image is inserted before or after a Text node
  when the caret is at its start or at its end, so that no empty element is left);
- the scripts that `insertHTML` inserts never run: the specification creates the fragment with
  `createContextualFragment()`, whose scripts run when they are inserted; the `script` elements of the fragment are
  "already started" instead, as the ones of `innerHTML` (no browser runs them);
- when a deletion merges two blocks whose `white-space` differ, only the first line of the second block is moved, in a
  `span` (or on its elements) with the `white-space` of its block (the white-space that the user agent style sheet gives to a `pre` element is not kept; the one that an author style declares
  for it is: the origin of the winning declaration is read from the cascade); the first line of the second block is also the only
  one that is moved when its `br` is in an inline element that contains other lines (`<b>def<br>ghi</b>`: the element is split); `insertLineBreak` inserts a line feed where `white-space` preserves it (except in the `pre`, `listing` and
  `xmp` elements), followed by a `br` when the line feed ends its block (a line feed at the end of a block is not
  rendered; the same `br` is inserted when `delete` or `forwardDelete` removes the content of the last line of a
  block, after a line feed), and `insertParagraph` inserts a line break when the editing host is inline or cannot
  contain the paragraph separator (e.g. a `span` or a `p` element);
- `insertText` removes the line break that only rendered the empty last line of a block when text is inserted on this
  line: the `br` that follows the caret and ends the block, when the caret follows a `br` or a line feed that
  `white-space` preserves (`AB<br>[]<br>` gives `AB<br>a`), and the preserved line feed that is the only content of
  the block (the specification only removes the `br` that is the sole child of its parent);
- `delete` and `forwardDelete` do not canonicalize the white space around the deletion as the specification does (the whole
  sequence of spaces and no-break spaces, through the inline elements). The spaces that collapse around the selection are
  removed first, with the spaces that are not rendered around a line break that is deleted (after a `br`, around an `hr`, a
  block boundary or a preserved line feed). After the deletion, the white space around the caret is rebalanced in its Text node
  only (the Text node that follows the caret when the caret ends its node, else the one that precedes it): a single space is a
  space, the spaces of a longer sequence alternate with no-break spaces from a no-break space, and a space that starts or ends
  the Text node is a no-break space unless a Text node that starts with another character follows it; the Text nodes of two
  lines that the deletion joins are rebalanced as one. A space before the deleted contents that now ends its line becomes a
  no-break space. The caret is then in the Text node that precedes it, else in the one that follows it (the empty Text nodes
  between them are removed). The other commands (`insertText`, `insertParagraph`...) canonicalize the white space as the
  specification;
- a `br` keeps a line that a deletion empties when other lines or blocks of its block remain (` a[]<p>bc</p>` gives
  `<br><p>bc</p>`); the specification only inserts it in a block that becomes empty;
- Backspace at the start of a `blockquote` without attributes that is not nested in another indentation element and that
  follows other contents joins its first line to the previous line (`foo<blockquote>[]bar</blockquote>` gives `foobar`); the
  specification outdents it, which is still done in the other cases;
- a deletion whose range starts at the start of a line and ends in a child block deletes the lines and keeps the first line
  of the block in it (`<br>[abc<br><div>]def</div>` gives `<br><div>def</div>`; the specification moves `def` out of the block);
  it applies to the commands that delete the selection first (e.g. `insertText`). Backspace at the start of a block removes the
  empty line that precedes it when its `br` is in inline elements, or is a line feed that `white-space` preserves, and
  `forwardDelete` on an empty line that precedes a block removes the line and puts the caret at the start of the block;
- two `li` whose contents are inline are joined as the other blocks (`<ol><li>foo<li>[]bar</ol>` gives `<ol><li>foobar</li></ol>`;
  the specification keeps a `br` between their lines); a block that the deletion empties is removed instead of receiving the
  contents of the end block when the end block is more indented (the item of a nested list, or of a list that follows a
  paragraph), and the nodes of an end block without rendered content are removed with it; a range that starts before a list or
  a list item with contents and ends after the last contents of the editing host deletes the contents of the items and keeps
  the first item, empty; the `li` that only contained the nested list of an item that Backspace outdents is removed; an empty
  block that follows a block that is not editable is removed by Backspace;
- `insertParagraph` only splits the single-line containers that are block-level: one that is inline-level (e.g. a `div` whose `display` is
  `inline` or `inline-block`) is a content of its line (the line is wrapped in a new `div`, or a line break is inserted when the editing
  host cannot contain it). A caret that is in a part of a table that is not a cell or a caption, or in a void element, is handled before the
  table or the element. The inline elements of the caret are cloned in the line that becomes empty (`<p><b>foo[]</b></p>` gives
  `<p><b>foo</b></p><p><b><br></b></p>`; the specification creates `<p><br></p>`), except when the caret is in a link
  (as in Chromium and WebKit; the elements that contain the link are still cloned in the line that the caret began:
  `<p><b><a href=foo>[]foo</a></b></p>` gives `<p><b><br></b></p><p><b><a href="foo">foo</a></b></p>`), the caret is in
  the inline elements that start the new container (the specification: at its start), the text typed in the new
  paragraph keeps the formatting of the caret (or of the text that began the selection that was deleted) as state and value overrides, the
  `br` that ends the previous line is kept before the container that wraps the line, and at a caret the white space is not canonicalized:
  the collapsed spaces around the caret are removed, and the space that ends the first container or begins the new one becomes a
  no-break space;
- the line break that `insertLineBreak` (or `insertParagraph`) inserts at the end of an inline editing host is followed by a second one,
  whatever follows the host on the line (a preserved line feed that also ends its block is followed by a `br`, as above);
- `insertText` never creates links (the autolinking of the specification is not implemented by any browser), does nothing for an empty
  string at a caret, inserts before a void element that contains the caret, and replaces the `br` (or the preserved line feed) that only
  keeps the line of the caret rendered. The text typed at the start or at the end of a link (`a[href]`) is inserted before or after the
  link, with the inline formatting of the caret that does not come from the link; a caret whose container is the link element itself
  stays in the link, unless a deletion has just left it there (Gecko). The text that replaces a selection that is in one link stays in the
  link, as the text typed after it; a link that a selection going out of it empties is removed;
- `insertHTML` splits the inline elements of the caret, except the links, when the fragment has elements and only inline contents
  (`<u>b</u>` in `<b>a[]c</b>` gives `<b>a</b><u>b</u><b>c</b>`), and `insertHTML` and `insertImage` at a caret leave the white space
  around it as it is;
- `delete`, `forwardDelete` and `insertText` do nothing (they return false) when a boundary point of the selection is in a `select` element;
- when the range of an inline formatting command is in one inline element without `style` attribute and contains all its contents,
  the new element is created in this element (`bold` on `<i>[foo]</i>` gives `<i><b>foo</b></i>`, `createLink` on `<b>[abc]</b>` gives `<b><a>abc</a></b>`; the
  specification wraps the element), except the `font` elements of `fontName`, `fontSize` and `foreColor`, which stay around the other
  inline elements (`<font size="5"><b>`).
- the states and the values of a collapsed selection that is after an inline element are the ones of the end of this
  element (the caret of the browsers);
- a color that is not valid is black, as in the reference implementation.

An inert node behaves as a node that is not editable (https://html.spec.whatwg.org/multipage/interaction.html#inert): the
subtree of an element with the `inert` attribute is not editable, the commands are not enabled in an inert editing host,
and the text of the inert nodes is not in the text of a selection (`Selection.toString()`, which also returns the
selected text of the focused text control while the selection of the document is a caret).

In an editing host whose `contenteditable` attribute is in the Plaintext-Only state, the content is edited as plain
text. The state is the one of the outermost editing host of the editable content: an editing host that is nested in
editable content behaves as the host that contains it (a `plaintext-only` host in a `contenteditable=true` host is
rich text, a `true` host in a `plaintext-only` host is plain text), an element that is not editable
(`contenteditable=false`) starts new editable content, and the document element in design mode is never plain text.
The specifications do not define the nesting: this is the behavior of Firefox, which the tests expect (Chrome and
Safari use the innermost host of the start of the selection for the formatting commands). In plain text content:

- the commands that format the content or insert elements are not enabled (`queryCommandEnabled()` is false,
  `execCommand()` returns false, the content is not changed and no event is fired): all the commands except `delete`,
  `forwardDelete`, `insertHTML`, `insertLineBreak`, `insertParagraph`, `insertText`, the clipboard commands and the
  miscellaneous commands (`insertImage` and `insertHorizontalRule` are disabled too, as in Chromium);
- `insertHTML` inserts the rendered text of the HTML (its `innerText`: the blocks and the `br` elements are line
  breaks, the content of `script`, `style`, `title`... is dropped, the white space collapses unless the HTML preserves
  it, e.g. a `pre` element, whatever the `white-space` of the host). As in Blink and WebKit, the fragment is inserted in
  the editing host for the time of the computation, in a `div` whose style is `all: initial` (the mutation observers
  see it). The lines are inserted as `insertText` inserts them, and the line breaks as `insertLineBreak` (a line feed
  where `white-space` preserves it, else a `br`);
- the Enter key inserts a line break (`beforeinput` and `input` with the `insertLineBreak` input type);
  `execCommand("insertParagraph")` still splits the block;
- a paste inserts the plain text of the clipboard (the rendered text of its HTML when it has no plain text) in the
  same way.

The user edits (`Editing.UserEdit()`, e.g. the keys pressed by the WPT test driver in an editable element: characters,
Enter, Backspace, Delete, the paste and cut actions) run the same algorithms, with the events of
[Input Events](https://w3c.github.io/input-events/): `beforeinput` (cancelable) then `input`, with their `inputType`
and `data`. `getTargetRanges()` of `beforeinput` returns the range of the selection, or, for a deletion at a caret, the caret extended by
a character (`deleteContentBackward`, `deleteContentForward`) or by a word (`deleteWordBackward`, `deleteWordForward`: Control
or Alt with Backspace or Delete) as `Selection.modify()` extends it, adjusted as the browsers do: Backspace in a Text node
targets the previous code point (not the grapheme cluster), the range of a line break or of an atomic inline (e.g. an `img`)
that is deleted is the range of the node in its parent, a boundary point of a selection that is next to a Text node is in the
Text node, and the range of a selection of list items is the one that is deleted (the contents of the items); as in Chromium,
the ranges are empty once the event is dispatched. The deletion of a selection that is not collapsed is always
`deleteContentBackward` or `deleteContentForward`. The data of a paste in editable content is in `dataTransfer` (`text/plain` and `text/html`, read-only; `data` is null),
the one of a paste in a text control in `data`.

A paste in editable content inserts the HTML of the clipboard when there is one: the selection is deleted, the HTML is
parsed as a fragment in the context of the insertion point and sanitized (the "remove unsafe" step of the sanitizer of
`setHTML()`: the scripts, the event handler attributes, the `javascript:` URLs..., and also the comments and the
`link`, `meta`, `noscript`, `style` and `title` elements), the fragment is inserted as `insertHTML` inserts it, the
Text nodes at its boundaries are merged with the adjacent Text nodes (text pasted in the middle of a Text node leaves
one Text node), and the white space at the boundaries is canonicalized (a space at the end of a line becomes a
no-break space). Without HTML, in plain text content, or with the "paste as plain text" action
(`ClipboardActions.Paste(asPlainText: true)`, Control/Meta+Shift+V in the WPT test driver: only `text/plain` is
available to the `paste` and `beforeinput` events), the plain text is inserted: its lines as `insertText` inserts them, its line breaks as `insertLineBreak` inserts them (one Text node with line feeds where
`white-space` preserves them, `br` elements elsewhere; no paragraph is created). There is no specification for the
content of a paste: this is what the browsers share.

### Text controls

While a text control is the focused area of the document (a `textarea`, or an `input` in the Text, Search, Telephone,
URL, Email, Password or Number state), the commands apply to its value, as in the browsers (the editing specification
only defines the commands for the editable content): `insertText`, `delete`, `forwardDelete`, `selectAll`, `cut`, `copy`,
`paste`, `undo` and `redo`, `insertLineBreak` and `insertParagraph` in a `textarea` (a line feed), and `insertHTML`,
which inserts the text of the fragment (as Chromium and WebKit; the command is not enabled in Gecko). The value is edited
at the selection of the control, the caret is collapsed after the inserted text, and `input` is fired at the control with
the `inputType` of the command (`insertText`, `deleteContentBackward`, `deleteByCut`, `historyUndo`...), without
`beforeinput`. The other commands (the formatting and block commands) are not enabled and return false; `styleWithCSS`,
`useCSS` and `defaultParagraphSeparator` set the state of the document whatever is focused (only Gecko disables them).
`cut` and `copy` need a selection in the control and are not enabled in a password field, whose text no browser copies;
the commands that edit are not enabled when the control is read-only or not rendered (no editor).

The edits of the user (`Editing.UserEditTextControl()`, e.g. the keys of the WPT test driver, the paste and cut
actions) edit the value at the selection of the control too — a character or a pasted text replaces the selection,
Backspace and Delete remove the selection or the character before or after the caret (the word with Control or Alt),
Enter inserts a line feed in a `textarea` — and fire `beforeinput` (cancelable) then `input` with their `inputType` and
`data`. The inserted text is truncated to the `maxlength` of the control and loses its line breaks in an `input`. A
control that a listener of `keydown`, `keypress` or `beforeinput` hides or makes read-only is not edited. The caret
moves without `select` event (`selectionchange` is fired). The selection is also kept for the types whose selection is
not exposed to the scripts (e.g. `email`): setting `value` moves their caret to the end.

Each text control has its own undo history (the values and the selections before and after each edit, up to 1000):
`undo` restores the value and the selection before the last edit, `redo` applies it again, and a new edit clears the
edits that can be redone. As in the browsers, a change of the value by a script (`value`, `setRangeText()`, a form
reset, a change of `type`) clears the history.

### Undo and redo

The editing specification leaves `undo` and `redo` to the user agent. Each document has an undo history of its editable
content (up to 1000 transactions): a command, or an edit of the user, is a transaction that records the mutations it
makes to the DOM (the child lists, the character data and the attributes, as mutation records) and the selection before
and after it. `undo` reverts the mutations of the last transaction in the reverse order and restores the selection,
`redo` applies them again, and a new transaction clears the ones that can be redone. Both fire `input` at the editing
host of the transaction with the `historyUndo` or `historyRedo` input type; the shortcuts of the user (Control or Meta
with Z, and with Shift+Z or Y) fire `beforeinput` (cancelable) first, when there is a transaction to undo or redo. As
the other commands, they are enabled when the active range is in an editing host (and the history is not empty).

The mutations made by the scripts are not recorded and do not clear the history, as the tests of the browsers expect: a
transaction is reverted or applied again on the nodes it recorded, wherever they are now. A node that the transaction
inserted is removed from its current parent; a node that it removed is inserted before its recorded next sibling when
that sibling is still a child of the recorded parent, else appended to the parent (a node that cannot be inserted
anymore is skipped). The consecutive characters typed by the user are separate transactions (the browsers merge them).

### Selection with the pointer

A press of a mouse button puts the caret at the position of the pointer (the hit testing of
`document.caretPositionFromPoint()`; a position between the children of an element is moved to the start or the end of
the adjacent text), or extends the selection to it with the Shift key (not on a link); a double click selects the word
at the position (the segment between the word boundaries of UAX #29) and a triple click the block, both without
direction; moving the pointer while the primary button is pressed extends the selection from the position of the
press. In a text control, the selection of its value is set the same way. `selectstart` (cancelable) is fired before a
press changes the selection of the document, and before a key (Shift with an arrow key, Select All) extends a caret.
The selection is set before the focus moves, so that a `focus` listener can change it (e.g. select the value), and the
caret of a focused editing host is where it was clicked instead of at its start.

- A press in the selection keeps it: a move then drags it (see [Drag and drop](#drag-and-drop)), and a release without
  a move collapses it at the position.
- Nothing is selected from an element whose used `user-select` is `none`, from a draggable element (a link, an image,
  `draggable="true"`) outside of the editable content, nor from a button or another form control that is not a text
  control, and the selection does not extend into such content or into another tree. A press in an area without content
  (the position is the start of the root element) removes the selection.
- Not done: the extension by words or blocks while dragging after a double or triple click, the auto-scroll while
  dragging, and `user-select: all` and `contain`.

### Not implemented

`EditContext`, composition (IME), the white space normalization of the browsers for the insertion commands (the one of `delete` and `forwardDelete` is implemented), the drop of dragged content in editable content (`insertFromDrop`, `deleteByDrag`), `insertFromPasteAsQuotation`,
and the formatting context of the copied HTML (the browsers add the inline ancestors of the selection, or their
computed styles, to the `text/html` data; the library copies the serialization of the selected range).

## Select elements

The user interactions with the `select` elements (https://html.spec.whatwg.org/multipage/form-elements.html#the-select-element)
are default actions of the trusted mouse and keyboard events (`SelectActions`). The specification lets the user agent
choose them ("the user agent should allow the user to..."): they are the ones of Chrome on Windows and Linux.

- **Picker of a drop-down box.** A press of the primary button on the select element opens its picker, a second one
  closes it; the space key, Enter and (with base appearance) the arrow keys open it, and Enter never submits the form.
  `showPicker()` opens it too (it requires a transient activation, which it consumes, and a rendered element). The select
  element matches `:open` while its picker is open. The picker is the *select popover* when the select element and its
  `::picker(select)` have base appearance (see [layout.md](layout.md#select-elements-with-base-appearance)): there is no
  shadow tree of the user agent, so the select element stands for its popover in the top layer, in the showing auto
  popover list and in the close watchers of its window (`HtmlSelectElement.OpenPicker`/`ClosePicker`): opening it hides
  the auto popovers that are not ancestors of the select element, a popover shown from it is nested in it, `Escape` and a
  press outside of it (light dismiss) close it. The toggle events of the select popover, which no script can observe, are
  not fired. Otherwise the picker is the native one: it is not rendered, but it has the same state. A picker whose
  appearance changes (the select element or `::picker(select)` gets or loses base appearance) is closed, when it opens or
  at the next rendering update.
- **Options of the select popover.** Opening the picker focuses the selected option (the focus is indicated when the
  keyboard opened it) and centers it in the popover when it is not in view. The arrow keys of the block axis of the
  writing mode, Home and End focus another option that is not disabled; PageDown (PageUp) focuses the last (first) option
  that is entirely in the scrollport, or the option one page further when it is the focused one; a typed character
  focuses the next option whose label starts with the characters typed in the last second. The focused option is
  scrolled into view in the popover. The release of the primary button on
  an option, and Enter or the space key on the focused option, [pick the
  option](https://html.spec.whatwg.org/multipage/form-elements.html#concept-select-pick) (unless the `mouseup` or `keydown`
  event was canceled, or the option is disabled) and close the picker, which focuses the select element again; the release
  of the press that opened the picker does not pick the option under the pointer unless the pointer moved. `Tab` closes
  the picker. As in Chrome, the select element and its ancestors do not match `:hover` and `:active` while the pointer is
  over the contents of the select popover.
- **Update notifications.** `input` and `change` are fired by a task of the user interaction task source, as specified
  (Chrome fires them before the picker closes), and only when the selected option changed (as in the browsers).
- **Native picker and closed drop-down box with native appearance**: the arrow keys, Home and End pick the previous, the
  next, the first or the last option that is not disabled.
- **List box with base appearance**: its options are boxes. A press on an option, and Enter or the space key on the
  focused option, toggle the option of a multiple selection, and select the option of a single selection or unselect it
  when it is selected. The arrow keys, Home, End and the type-ahead focus the other options without selecting them; the
  options are a single stop of the sequential focus navigation (the first selected option that can be focused, else the
  first one).
- **List box with native appearance**: its rows are the boxes of its options (see
  [layout.md](layout.md#replaced-elements-csslayoutreplaced)), which are the targets of the pointer events. A press on a
  row picks its option (the other options of a multiple selection are unselected, unless the Control or Meta key, or a
  tap on a touch screen, toggles the option); the arrow keys of the block axis, Home and End select the previous, the
  next, the first or the last option, and scroll the list box to reveal its row.
- As in the browsers, a click of the user on a `label` focuses its labeled control (a select element is not opened).
- Not done: the selection of
  a range of options (Shift, a drag); the multi-select drop-down box; the restoration of the state on history traversal.

## Input pickers

Whether an `input` element supports a picker is implementation-defined, except for the File Upload state
(https://html.spec.whatwg.org/multipage/input.html#input-support-picker). As in the browsers, the Color, Date, Month,
Week, Time and Local Date and Time states support one, and so does an input with a suggestions source (a `datalist`).
`showPicker()` and the activation of the color, file, date and time inputs (a click) *show the picker, if applicable*:
it consumes the transient activation of the window, then opens the picker of a mutable element (the HTML Standard gives
the date and time states no activation behavior, but all the browsers show their picker when they are clicked). There is
no picker user interface: the user does not choose a value, and the file chooser closes at once, unless
`BrowsingContextOptions.ShowFilePicker` is set: the host shows its own file picker, sets the chosen files with
`HtmlInputElement.Files` and fires the `input` and `change` events. The other pickers are
open until a press of a pointing device outside of the element, `Escape` (it is not a close request then), or until the
element is removed, changes its type or is no longer mutable; opening a picker closes the other ones of the document.
The element matches `:open` while its picker is open (https://html.spec.whatwg.org/multipage/semantics-other.html#selector-open).
`showPicker()` does not focus the element, and clicking a text input does not show the suggestions of its `datalist`.

## User input

`BrowsingContext.Input` (`UserInput`) is the entry point of a host application that injects the input of the user: the
pointers (the mouse, the pens and the fingers on a touch screen) and the wheel. The positions are in CSS pixels, in the
viewport of the top-level browsing context (the nested browsing contexts share the input devices of their top-level
browsing context). The library fires the trusted events of [Pointer Events](https://w3c.github.io/pointerevents/),
[UI Events](https://w3c.github.io/uievents/) and [Touch Events](https://w3c.github.io/touch-events/), and runs the default
actions of the user agent (the focus, the activation, the light dismiss, the drag and drop, the sliders, the scrolling).

```csharp
var input = browsingContext.Input;
input.Mouse.Move(120, 80);              // pointerover, pointerenter, mouseover, mouseenter, pointermove, mousemove
input.Mouse.Down();                     // pointerdown, mousedown (the clicked element is focused)
input.Mouse.Up();                       // pointerup, mouseup, click

var finger = input.CreatePointer("touch");
finger.Move(120, 300);
finger.Down();                          // pointerover, pointerenter, pointerdown, touchstart
finger.Move(120, 200);                  // pans: pointercancel, pointerout, pointerleave, then the scroll
finger.Up();                            // touchend

input.Modifiers = InputModifiers.Shift; // the shiftKey of the next events
input.Wheel(120, 80, deltaX: 0, deltaY: 100);
```

- **Targets.** The target of an event is the element at the position of the pointer, found by the hit testing of the
  layout (see [Geometry](computed-style.md#geometry)): not retargeted out of the shadow trees, skipping the inert elements
  (a point of an element blocked by a modal dialog hits its `::backdrop`), and in the document of the nested browsing
  context when the point is in the content box of an `iframe` (the coordinates of the events are the ones of the viewport
  of the document of their target; `screenX` and `screenY` are the position in the top-level viewport plus the position of
  the window). The coordinates of the pointer events are fractional; the ones of the mouse events, `click`, `auxclick` and
  `contextmenu` are integers (`MouseEvent.ClientX`, `ClientY`, `ScreenX` and `ScreenY` are doubles, as in CSSOM View).
- **Pointers.** The mouse is the pointer 1 and is always active. A pen gets its pointer id when it is first used, and a
  touch pointer gets a new one for each contact (the first touch in contact is the primary one); a touch pointer that is
  not in contact fires no event. `PointerInputProperties` gives the geometry, the pressure, the tilt and the twist of the
  contact (the pressure is 0.5 by default while a button is pressed).
- **Events.** The pending pointer capture is processed, then the boundary events (`pointerout`, `pointerleave`,
  `pointerover`, `pointerenter`, in the flat tree; in the documents of the ancestor browsing contexts the pointer is over
  the `iframe`) are fired before each event. `pointerrawupdate` precedes `pointermove` in the secure contexts when a
  listener can observe it; its coalesced events list (and the one of `pointermove`) has one event (the moves are not
  coalesced), and the predicted events list is empty. The press of another button of a pressed mouse is a `pointermove`
  (chorded buttons). When a listener removes the target of a pointer, its next events (and the click) are fired at the
  nearest ancestor that stays connected, and a `pointerover` is fired at the next target even if it is the same. When
  the layout or the scroll positions change under a mouse or a pen that does not move, its boundary events are fired
  by the next rendering update of the event loop.
- **Compatibility mouse events.** The mouse events of the mouse and of the pens follow each pointer event
  (`mousemove`, `mousedown`, `mouseup`, and the mouse boundary events after the pointer ones); canceling `pointerdown`
  suppresses them until the buttons are released. A tap of the primary touch fires them after `pointerup` and
  `touchend` (`mousemove`, `mousedown`, `mouseup`), unless `pointerdown`, `touchstart` or `touchend` was canceled or the
  touch moved more than 15px. `click` (`auxclick` for the other buttons) is a `PointerEvent` of the pointer, fired at the
  nearest common ancestor of the targets of the press and of the release (the capture target when the pointer was
  captured); the second click at the same position within 500 ms is followed by `dblclick`, and the release of the
  secondary button by `contextmenu` (after the release, as on Windows).
- **Pointer capture.** `Element.SetPointerCapture()`, `ReleasePointerCapture()` and `HasPointerCapture()`;
  `gotpointercapture` and `lostpointercapture` are fired before the next event of the pointer (at the document when the
  capture target was removed), and the capture is released after `pointerup` and `pointercancel`. A touch implicitly
  captures its `pointerdown` target. As in the browsers, an element of another document than the one the pointer is
  active in cannot capture it, and the events of a mouse pressed in a nested browsing context are dispatched to its
  document until the buttons are released.
- **Touch.** `touch-action` is determined when the contact starts: the intersection of the values of the touched element
  and of its ancestors up to the nearest scroll container that the user can scroll along the axis (to the root for the
  zoom). The first move of more than 15px pans when `touch-action` allows its direction, unless `touchstart` or that
  `touchmove` was canceled: `pointercancel`, `pointerout` and `pointerleave` are fired, the capture is released, and the
  next moves scroll (the touch events continue, not cancelable). The move of two touches is a pinch zoom, which cancels
  the pointers when it is allowed but does not zoom. A touch that starts on a scrollbar does not pan. `touchstart`,
  `touchmove`, `touchend` and `touchcancel` are fired at the element the touch started on, after the pointer event (as in
  Chromium and Gecko, the targets of the `Touch` objects of `touches`, `targetTouches` and `changedTouches` are retargeted
  against the current target of the event while it is dispatched, as the target of the event: a listener outside of a
  shadow tree gets copies of the touch points whose target is the shadow host); as in
  the desktop browsers, the `ontouch*` event handlers are not exposed. `wheel`, `touchstart` and `touchmove` are not
  cancelable when all their listeners are passive (nor when the listeners of the legacy `mousewheel` event are).
- **Scrolling.** The wheel fires `wheel` at the element at its position and, unless it is canceled, scrolls the nearest
  scrolling box (in the flat tree, then the viewport, then the ones of the ancestor browsing contexts) that the user can
  scroll in the direction of the deltas (`overflow: auto` or `scroll`); a box at its limit chains the scroll to its
  ancestors unless its `overscroll-behavior` is `contain` or `none` in a scrollable axis. A panning touch scrolls the box
  it latched when the pan started. The wheel, a panning touch and a drag of the thumb of a scrollbar are gestures: they are
  not snapped while they last, and when they end (the release of the pointer; for the wheel, see below) the box snaps to
  the snap position closest to their end among their start position and the ones beyond it in their direction (a short
  gesture snaps back), then fires a single `scrollend`. The keys and the presses on the track of a scrollbar are relative
  scrolls that snap to the next snap position in their direction. With `ViewportOptions.SmoothUserScrolling`, the scrolls of
  the keys, of the track presses and the snapping at the end of a gesture are smooth scrolls animated by the rendering
  updates of the event loop (on its `TimeProvider`: deterministic with a fake clock), which a later key retargets from their
  destination, and a gesture of the wheel ends when no wheel event follows for 100 ms (a timer of the event loop); by
  default they are instant and each wheel event is a complete gesture, so that a screenshot taken after an input does not
  depend on the time (the WPT runner animates them, as the browsers). The scrolls of the wheel are instant in both cases
  (the precise deltas of a touchpad, or of the wheel actions of WebDriver).
- **Wheel event transactions** (https://w3c.github.io/uievents/#wheel-event-transactions). The `wheel` events at the
  position of the first one are fired at its target, even when the scroll moves other contents under the pointer, and the
  scroll is chained from it. The transaction ends when a wheel event is at another position or the mouse moves, 500 ms
  after its last wheel event, or when its target is removed from its document (even if it is inserted again, as in Gecko)
  or stops generating a box.
- **Legacy `mousewheel` event.** As in Blink and WebKit (not in a specification; Gecko does not fire it), the `wheel` event
  is dispatched with the type `mousewheel` to the listeners of the targets of its path that have no `wheel` listener (the
  mechanism of the legacy-prefixed animation and transition events of the DOM Standard). The `WheelEvent` has no legacy
  `wheelDelta` attributes, and there is no `onmousewheel` event handler.
- **Scrollbars.** The press of the primary button of the mouse or of a pen on a classic scrollbar (see
  [the scrollbars](rendering.md#scrollbars); `ViewportOptions.ScrollbarsVisible`) is handled by the scrollbar, whether
  the page cancels its events or not: a press on the track scrolls by a page (87.5% of the scrollport) towards the
  pointer, once (a held press does not repeat); a drag of the thumb scrolls its box so that the thumb follows the pointer
  (a gesture: instant scrolls that snap when the button is released; the scroll position is clamped while the pointer is
  beyond the ends of the track). As in the browsers, `pointerdown`, `mousedown`, `pointerup` and `mouseup` are fired at the scroll
  container (the document element for the scrollbars of the viewport, also the ones of the nested browsing contexts),
  but the press starts no selection and no drag-and-drop, has no `click`, and the moves of the pointer fire no event until
  the release (the scrollbar captures the pointer); the press focuses as a press on the scroll container does, except on
  a scrollbar of the viewport, which keeps the focus where it is. The scrollbars of an inert element do not react. A touch
  that starts on a scrollbar neither pans nor scrolls.
- **Resizer.** The press of the primary button of the mouse or of a pen on the resizer of a scroll container whose `resize`
  is not `none` (`UserInput.Resizer.cs`: a square as large as its classic scrollbars, 15px without them, at its bottom
  corner on the side of its vertical scrollbar, inside its borders) is handled as a press on a scrollbar (no selection, no
  drag-and-drop, no `click`, no move event until the release): a move of the pointer, mapped to the coordinates of the box
  (its transforms included), changes its size by as much ([css-ui-4](https://drafts.csswg.org/css-ui-4/#resize)): the
  `width` and/or `height` (the axes of `resize`) that change are set in px in its `style` attribute, rounded to 1/64px, and
  never smaller than the resizer. The resizer is not painted.
- **Default actions.** The trusted events run the default actions of [Focus](#focus) and of the light dismiss; a move of
  the mouse with the primary button pressed starts a [drag-and-drop operation](#drag-and-drop) (the pointer events are
  then suppressed until the drop); a press of the primary button on an `<input type=range>` and the moves that follow set
  its value from the position of the pointer along its track (`input` events, then `change` at the release), and a
  touch does not pan along the track of a slider.
- The [pointer lock](#pointer-lock) locks the mouse to an element: its events then carry movements instead of positions.
- Not done: the pinch zoom, the resize handles, the repetition of a held press on a scrollbar, `contextmenu` for a long
  press, the coalescing of the moves, the fling of the gestures (their inertia).

`UserInput.Keyboard` (`KeyboardInput`) is the keyboard: `Down(key)`, `Up(key)`, `Press(key)` and `Type(text)` take the key
values of [UI Events](https://w3c.github.io/uievents-key/) (a character, or the name of a key such as `Enter`, `Shift`,
`ArrowLeft`, `F1`), whose code, legacy key code, location and text are the ones of a US keyboard; a `KeyboardKey` gives
them explicitly. The keys are dispatched as trusted `keydown`, `keypress` and `keyup` events to the focused element of the
top-level browsing context (the body of the focused document when the viewport is focused), and the library runs their
default actions: the insertion of the text of the key in a text control or in editable content (`beforeinput` and `input`),
Backspace and Delete, the sequential focus navigation of Tab, the activation of the focused element by Enter and Space
(and the implicit submission of the forms), the caret movements of the arrow keys, Home and End, the close requests of
Escape, and the clipboard shortcuts (Control or Meta with C, X and V; the other keys pressed with Control or Meta type no
text). A key that is already pressed repeats (`repeat`), a letter typed while Shift is pressed is an uppercase letter, and
the pressed modifier keys are the modifiers of the keyboard and pointer events (`UserInput.Modifiers`). `InsertText(text)`
inserts a text as an input method commits it: an `insertText` edit without keyboard events.

```csharp
var keyboard = browsingContext.Input.Keyboard;
keyboard.Type("Hello");                 // keydown, keypress, beforeinput, input, keyup for each character
keyboard.Down("Shift");
keyboard.Press("a");                    // types "A"
keyboard.Up("Shift");
keyboard.Press("Enter");                // submits the form of the focused input
```

`UserInput.HitTest(x, y)` returns the element at a point of the top-level viewport (in the document of an `iframe` when the
point is in its content box), and `UserInput.MapToTopLevel(document, x, y)` and `MapToDocument(document, x, y)` convert
the coordinates between the viewport of a document and the top-level viewport (e.g. to click the center of the
`getBoundingClientRect()` of an element of an `iframe`).

## Host callbacks

The callbacks of `BrowsingContextOptions` let a host follow the browsing contexts that use the options (the nested browsing
contexts and the popups share the options of their top-level browsing context). They are invoked on the thread of the event
loop:

- `BrowsingContextCreated`: the library created a nested browsing context (an `iframe`, `frame`, `object` or `embed`
  element is inserted) or an auxiliary browsing context (a popup, see [Auxiliary browsing contexts](#auxiliary-browsing-contexts)),
  with its initial `about:blank` document. It is not invoked for the browsing contexts created by the application.
- `BrowsingContextDestroyed`: a browsing context was destroyed (its container was removed, `window.close()`,
  `BrowsingContext.TryClose()` or `Dispose()`).
- `DocumentActivated`: a navigation made a document the active document of a browsing context (also the error document of
  a navigation whose response is a network error, see `Document.IsNetworkErrorDocument`), before it is parsed. It is not
  invoked for the initial `about:blank` documents.
- `SameDocumentNavigated`: the URL of the active document changed without a new document (a fragment navigation,
  `history.pushState()`, `history.replaceState()`, a traversal within the document).

`DocumentRequest.BrowsingContext` attributes the requests of the `DocumentLoader` to their browsing context (the browsing
context that is navigated for a navigation request, else the one of the document that makes the request), and
`BrowsingContext.Reload()` reloads the active document, as the reload button of a browser. `BrowsingContext.IsNavigating` is
true from the start of a navigation until its document is active, or until it ends without a document (e.g. a 204 response):
`window.open(url)` creates the popup (`BrowsingContextCreated`), then navigates it.

## Dialogs

`BrowsingContextOptions.UserPromptHandler` shows the simple dialogs of the windows (`UserPrompt`): `alert()`, `confirm()`,
`prompt()`, and the confirmation of the unloading of a document that cancels its `beforeunload` event while it has sticky
activation (as in the browsers, a document that the user never interacted with cannot prevent its unloading). The handler
returns `UserPromptResult.Accept(text)` or `UserPromptResult.Dismiss`; the script waits for it (it blocks the event loop).
A dismissed unload confirmation cancels the navigation, the traversal or the closing. Without handler, no dialog is shown:
`confirm()` returns false, `prompt()` returns null and the documents are always unloaded.

```csharp
using var context = new BrowsingContext(new BrowsingContextOptions
{
    ScriptEngine = engine,
    UserPromptHandler = (window, prompt) => prompt.Type switch
    {
        UserPromptType.Confirm => UserPromptResult.Accept(),
        UserPromptType.Prompt => UserPromptResult.Accept("answer"),
        _ => UserPromptResult.Dismiss,
    },
});
```

## Emulation

The options of a browsing context are immutable, but a host can change the emulated environment of a top-level browsing
context while its documents are loaded (as `page.setViewportSize()`, `page.emulateMedia()` and `context.setOffline()` of
Playwright):

- `BrowsingContext.Viewport` (initially `BrowsingContextOptions.Viewport`): the size of the viewport, the device pixel ratio,
  the media type and the user preference media features. A new value re-evaluates the media queries, lays the documents out
  again and fires `resize` at the next rendering opportunity.
- `BrowsingContext.IsOffline`: `navigator.onLine`, and the `online` and `offline` events of the windows. The requests are not
  blocked: the `DocumentLoader` fails them.
- `BrowsingContextOptions.Languages`: `navigator.language` and `navigator.languages`, and the `Accept-Language` header of the
  requests (`fr-FR,fr;q=0.9`...). It is set when the browsing context is created.

## Drag and drop

`DragAndDropOperation` implements the drag-and-drop processing model
(https://html.spec.whatwg.org/multipage/dnd.html#drag-and-drop-processing-model). `BrowsingContext.Input` drives it for the
mouse; a host can also drive it directly: `TryStart()` when the pointer moves with the primary button pressed on a node,
`Update()` each time the pointer moves over an element, and `End()` when the button is released. The drag events are
trusted `DragEvent` objects whose `DataTransfer` exposes the drag data store in the mode of the event (read/write in
`dragstart`, read-only in `drop`, protected otherwise).

```csharp
var source = document.GetElementById("source")!;   // <div draggable="true">
var target = document.GetElementById("target")!;   // a drop zone (dragover is canceled)
var operation = DragAndDropOperation.TryStart(source, new DragPointerState(0, 0, Buttons: 1));
operation?.Update(target, new DragPointerState(10, 10, Buttons: 1));  // drag, dragenter, dragover
operation?.End(new DragPointerState(10, 10, Buttons: 0));           // drag, drop, dragend
```

- What is dragged: the selection when the pressed node is selected, else the first draggable inclusive ancestor
  (`draggable="true"`, the images and the links). As in the browsers, a press in a text control, a `select` element or
  editable content does not drag. The links and the images add their URL as `text/uri-list` data.
- `dragstart` can cancel the operation; `pointercancel`, `pointerout` and `pointerleave` are then fired at the source
  node, and the host suppresses the pointer events until the drop.
- As in the browsers, the element under the pointer is the current target element whether or not `dragenter` is
  canceled (`dragenter` and `dragleave` are fired when the pointer enters and leaves elements, with the other element as
  `relatedTarget`), the drop is accepted when `dragover` is canceled (or when text is dragged over a text control or
  editable content), and a `drop` event that is not canceled keeps the drag operation. The text dropped in a text control
  or in editable content is inserted at its end.
- There is no drag feedback (`setDragImage()` only records the image), no drop of files from outside the user agent, and
  no drag between documents.

## Clipboard

The [Clipboard API and events](https://w3c.github.io/clipboard-apis/) use the `SystemClipboard` of
`BrowsingContextOptions.Clipboard`: an in-memory clipboard shared by the browsing contexts of the options when it is
null. The application can read and replace its items (each item has representations: a format name and bytes, e.g.
`text/plain` encoded as UTF-8) to observe what a page copies or to simulate what the user copied, or override it to use
the clipboard of the operating system.

```csharp
var clipboard = new SystemClipboard();
var context = new BrowsingContext(new BrowsingContextOptions
{
    ScriptEngine = new JavaScriptScriptEngine(),
    Clipboard = clipboard,
    ClipboardPermission = (document, access) => access == ClipboardAccess.Write, // the pages cannot read the clipboard
});
clipboard.WriteText("pasted text");
// ... navigator.clipboard.writeText('copied'), or document.execCommand('copy')
var text = clipboard.ReadText();
```

- `navigator.clipboard` (`read()`, `readText()`, `write()`, `writeText()`) and `ClipboardItem` are only exposed in
  secure contexts, and the promises are rejected with a `NotAllowedError` when the document is not fully active, does not
  have the focus, or `ClipboardPermission` denies the access (it plays the role of the permission prompt; all the
  documents are allowed by default). There is no user activation: the access does not require a user gesture.
- The data of the items returned by `read()` is read from the clipboard by `getType()`, which fails with an
  `InvalidStateError` when the clipboard changed since. The HTML is sanitized by the HTML Sanitizer, unless
  `read({ unsanitized: ['text/html'] })` is used. The supported types are `text/plain`, `text/html`, `image/png`,
  `text/uri-list`, `image/svg+xml` and the web custom formats (`web ` followed by a MIME type), stored with the
  formats of the clipboard of Linux (`application/web;type="custom/formatmap"`...).
- `document.execCommand('copy' | 'cut' | 'paste')` runs the clipboard actions, which fire trusted `copy`, `cut` and
  `paste` events at the focused text control, the editing host of the selection, or the body element. A canceled `copy`
  or `cut` event writes the data set on `clipboardData`; otherwise the selection is copied (the selected text of the
  focused text control, or the rendered text and the HTML of the selection; in the rendered text of a selection (also `Selection.toString()`), a collapsible space that ends the selection is
kept when content follows it on its line, and, as in the browsers, the block-level boxes at the start and at the end
of an atomic inline (e.g. the items of an inline flex container) do not break the line (`innerText` keeps their line
breaks, as specified)), and `cut` removes it from the editable
  context. As in the browsers, the `copy` command returns false and copies nothing when nothing is selected or when the
  document is not the focused document (the event is still fired). `paste` inserts the plain text of the clipboard in a text control, and its HTML (sanitized), or else its plain text,
in editable content (see [Editing](#editing)). The `clipboardData` of the events is only usable while they
  are dispatched. The WPT test driver runs the same actions for the Control/Meta + C, X and V keys (with Shift, V pastes as plain
text).
- `clipboardchange` is fired at `navigator.clipboard` (as in the browsers) when the clipboard changes, including when
  the application calls `SystemClipboard.Write()`, for the documents allowed to read the clipboard: immediately when the
  document has the focus, else when it gains the focus.
- `navigator` only implements `clipboard` and `platform` (the empty string): the user agent information is not exposed.

## Cross-document messaging

`window.postMessage(message, targetOrigin, transfer)` and `window.postMessage(message, { targetOrigin, transfer })`
(`Window.PostMessage` in .NET) run the window post message steps
(https://html.spec.whatwg.org/multipage/web-messaging.html#window-post-message-steps):

- The message is serialized when the method is called (a `DataCloneError` is thrown synchronously), with the objects of
  the transfer list (`ArrayBuffer` objects are detached, `MessagePort` objects are transferred and given to the
  `ports` of the event). The windows that can reach each other share the script engine, so the message is serialized for
  the realms of the engine, as `structuredClone()` does.
- A task of the target window checks `targetOrigin` (`*`, `/` for the origin of the caller, or the origin of a URL; an
  invalid URL throws a `SyntaxError`) against the origin of its document, deserializes the message in its realm and fires
  a `message` event (`origin` and `source` are the origin and the window of the caller), or a `messageerror` event when
  the message cannot be deserialized (e.g. a `SharedArrayBuffer` sent to another agent cluster).
- The caller (the incumbent settings object) is the realm of the script that calls the method (`ScriptEngine.GetCurrentGlobal()`),
  or the target window itself for a call from .NET.

The agent clusters of a cross-origin isolated browsing context group (`Cross-Origin-Opener-Policy: same-origin` with a
compatible `Cross-Origin-Embedder-Policy`) are origin-keyed. A `SharedArrayBuffer` can be posted (or sent to a
`MessagePort`, a `BroadcastChannel` or a dedicated worker) by a cross-origin isolated global object to the windows and
the dedicated workers of its agent cluster: the receiver gets a new `SharedArrayBuffer` that shares the memory. The
global objects of the agent clusters that are not cross-origin isolated have no `SharedArrayBuffer` property.

## Workers

The dedicated workers (`Worker`) and the shared workers (`SharedWorker`) are implemented
(https://html.spec.whatwg.org/multipage/workers.html), with classic and module scripts:

- Each worker is a new agent: its `DedicatedWorkerGlobalScope` or `SharedWorkerGlobalScope` runs its own event loop on
  its own thread, and its scripts run in the realm of its own script engine, created by
  `ScriptEngine.CreateWorkerScriptEngine()` (`JavaScriptScriptEngine` creates a new engine with the same options). The
  members of a worker global scope must be used on the thread of the worker. The callbacks of the options
  (`UnhandledScriptError`, `ConsoleMessage`) receive the `GlobalScope` and are invoked on the thread of the worker.
- The script of a worker is fetched with the environment of its creator (classic scripts with the `same-origin` mode,
  module scripts with the `cors` mode and the `credentials` option); an HTTP(S) response must have a JavaScript MIME type,
  and the script is decoded as UTF-8. A worker created from a `data:` URL has an opaque origin, from a `blob:` URL the
  origin of its creator. A script that cannot be fetched or parsed fires an `error` event (an `Event`) at the worker
  object.
- A worker gets the referrer policy, the CSP list (`Content-Security-Policy` headers) and the embedder policy
  (`Cross-Origin-Embedder-Policy`) of the response of its script, and a shared worker its document isolation policy; a
  worker created from a `data:` URL inherits them from its creator, from a `blob:` URL from the creator of the blob URL.
  A dedicated worker whose owner requires CORP (or is credentialless) fails to load when its own embedder policy does not.
  A worker is a secure context when its creator is one, and the mixed content checks of its requests also consider its
  creator (e.g. a `data:` worker created by an `https:` page). A dedicated worker is cross-origin isolated when its owner
  is (except a `data:` worker), a shared worker when its embedder policy is compatible with cross-origin isolation. A
  shared worker cannot be shared by creators that differ in their secure context.
- `importScripts()` fetches and runs the scripts synchronously; the errors of a cross-origin script fetched without CORS
  are muted (a `NetworkError`). `fetch()`, `XMLHttpRequest` (including the synchronous requests and their timeout), `MessageChannel`,
  `BroadcastChannel`, `FileReaderSync`, the blob URLs (shared with the windows of the group), the timers,
  `performance.now()` and `requestAnimationFrame()` (in the dedicated workers of a window) are available in the workers.
  Only the interfaces exposed in the workers (`[Exposed]`) are defined in their realms.
- The messages are serialized for the other agent (`ScriptEngine.StructuredSerializeForAgent`) and the `MessagePort`
  objects can be transferred (with their port message queue). A `SharedArrayBuffer` cannot be sent to a worker: the
  engines of the agents do not share their memory.
- The errors that a dedicated worker does not handle are fired at its `Worker` object (an `ErrorEvent` whose `error` is
  null), then reported to its owner when the event is not canceled. `terminate()` aborts the running script (the
  cancellation token of the engine), discards the tasks and the messages that are not delivered, and ends the event loop.
  `close()` ends it once the running task completes.
- The dedicated workers of a window are terminated when its document is discarded; the workers are terminated when the
  browsing context group is disposed (`BrowsingContext.Dispose()`). The shared workers are shared by the objects of the
  browsing context group with the same constructor origin, URL, name, type, credentials and extended lifetime, and live
  until they call `close()` or the group is disposed.

## Worklets

The worklets (`Worklet`, `WorkletGlobalScope`) are not workers: their global scopes are realms of the script engine of
their document, and their scripts run on the thread of its event loop. See [Worklets](scripting.md#worklets),
[Paint worklet images](rendering.md#paint-worklet-images), [Layout API containers](layout.md#layout-api-containers-csslayoutlayoutapi-csslayoutapi)
and [Animation worklet](animations.md#animation-worklet). `DocumentRequest.Destination` is
`RequestDestination.PaintWorklet` for the modules of a paint worklet, and `RequestDestination.Script` for the modules of
the layout and animation worklets.

## Service workers

The service workers (https://w3c.github.io/ServiceWorker/) are implemented when the scripts run
(`BrowsingContextOptions.ScriptEngine`), in the secure contexts: `navigator.serviceWorker` registers them, and a service
worker handles the requests of the documents and the workers it controls.

```csharp
using var context = new BrowsingContext(new BrowsingContextOptions
{
    ScriptEngine = engine,
    DocumentLoader = loader,
    ServiceWorkers = new SqliteServiceWorkerBackend(path), // optional: the registrations outlive the browsing context
});

var document = context.Navigate("https://example.com/");
// <script>navigator.serviceWorker.register('/sw.js')</script>
await context.WaitUntilIdleAsync();
```

- The registrations are stored by a `ServiceWorkerBackend`: `BrowsingContextOptions.ServiceWorkers`, which several browsing
  contexts can share, or by default an `InMemoryServiceWorkerBackend` per browsing context created by the application,
  shared by its auxiliary browsing contexts. The backend stores the registrations of each origin (`GetRegistrations`,
  `SaveRegistration`, `DeleteRegistration`) with their installed and activated service workers and the responses of
  their scripts (strings and byte arrays): a browsing context created later with the same backend runs the stored
  scripts, without fetching them again. The methods are called from several threads: an implementation must be
  thread-safe. The test project has a SQLite backend (`tests/Meziantou.Framework.WebPlatform.Tests/Storage/SqliteServiceWorkerBackend.cs`).
- The browsing contexts that share a backend share the registrations, but each browsing context group runs its own
  instances of the service workers, as several browser sessions of the same profile.
- A service worker runs on its own thread, with its own event loop and its own script engine, as the dedicated workers
  (see [Workers](#workers)). It is started when an event is dispatched to it, and terminated when it becomes redundant or
  when the browsing context group is disposed (it is not terminated when it is idle). Its event loop is a child of the
  event loop of the group: `WaitUntilIdleAsync` waits for its tasks and for the jobs of the registrations (register,
  update, unregister, install, activate).
- The update checks the script of the service worker and the scripts it imported byte for byte (the `Service-Worker`
  header, the scope restricted by the path of the script or by the `Service-Worker-Allowed` header). A soft update runs
  1 second after a navigation handled by a service worker (a timer of the event loop of the group): the specification
  runs it in parallel with the load of the document, but the browsers defer it (they all wait 1 second), so the
  `updatefound` event of that update is fired after the `load` event of the document. The requests handled while a soft
  update is scheduled share it. The messages posted to a service worker are dispatched in the order they were posted. `importScripts()` stores the scripts while the service worker is
  installed; afterwards they are read from its script resource map. The module graph of a module service worker is
  fetched with the credentials mode "omit", and its static imports are stored in the script resource map too: as in the
  browsers (the specification only checks the scripts of `importScripts()`), the update also fetches them again and
  compares them byte for byte. A script that the update check fails to fetch (e.g. a 404) is not a change, but the new
  worker installed because another script changed fails to import it. A module service worker whose evaluation throws,
  or that is an asynchronous module (a top-level `await`), cannot be registered, and `import()` is rejected with a
  `TypeError` in a service worker. The registration of a script that is redirected is rejected with a `SecurityError`
  (as in the browsers; the specification makes it a network error, so a `TypeError`, which an update still gets).
- The algorithms of a registration run in parallel with its clients and its workers: the steps that clear an
  unregistered registration or activate its waiting worker run again each time an event of a worker ends (an extended
  event, or an event that is not dispatched yet), as one atomic step. The objects of the clients (`ServiceWorker`,
  `ServiceWorkerRegistration`) are updated by tasks, in the order of the changes: an object created by a script reflects
  the state of the tasks that already ran in its event loop (e.g. the controller is still `activating` in the
  `controllerchange` event of a worker that is being activated, and `self.registration.active.state` is `activating`
  when the promise of `skipWaiting()` is fulfilled, after the `activate` event). When a registration is cleared (or an
  installation fails), the attributes of the registration objects are updated before the `statechange` events, as in
  the browsers.
- The requests whose service-workers mode is "all" are handled by the `fetch` event: the navigations and the worker
  scripts by the service worker whose scope matches their URL (the new document or worker is then controlled by it), the
  subresource requests (`fetch()`, `XMLHttpRequest`, scripts, style sheets, images...) by the service worker that
  controls their client. The `Request` and `Response` objects are the ones of the Fetch API (Fetch.js): the body of the
  response given to `respondWith()` is read as it is given to a `fetch()` of the client (the client that cancels the body,
  or aborts the fetch, cancels the stream in the service worker), and is read before it is given to the other requests. The requests
  of a service worker are not handled by the service workers, nor the redirects of the subresource requests answered by
  the network; each redirect of a navigation is (its reserved client changes when the origin changes).
- The request of a `fetch` event has a single URL, the current URL of the request of the client (`event.request.url` is
  the URL after the redirects of a navigation, as in the browsers; the specification gives it the whole URL list). When
  the service worker fetches it again (`fetch(event.request)`), the request keeps the mode ("navigate"), the origin, the
  redirect chain and the cookies of the navigation: its `Origin` header is `null` after a cross-origin redirect, its
  `Sec-Fetch-Site` header tells the least related URL of the chain (`Sec-Fetch-Dest` is `empty`), a top-level
  navigation initiated by another site sends the `SameSite=Lax` cookies, and a redirect is an opaque-redirect filtered
  response, which the navigation follows when the service worker gives it back. A request created with other members
  (`new Request(event.request, { ... })`) is a request of the service worker. A response of the network that the service worker gives back keeps the headers of its internal response
  (e.g. the `Content-Range` of a CORS response, which a media element needs).
- The navigation preload (`registration.navigationPreload`) sends the preload request in parallel with the `fetch`
  event, with the `Service-Worker-Navigation-Preload` header.
- The clients (`clients.get()`, `matchAll()`, `claim()`, `openWindow()`, `WindowClient.focus()` and `navigate()`) are the
  windows and the dedicated and shared workers of the browsing context group. `openWindow()` and `focus()` require the
  transient activation of a window of the origin. `matchAll()` lists the windows that were focused first, the most
  recently focused one first, then the other windows and the workers in creation order. `WindowClient.navigate()` is
  rejected with a `TypeError` when the navigation creates no document (e.g. a URL handed off to external software) or the
  error document of a network error. The initial `about:blank` document of an iframe or a popup is controlled by the
  service worker of its creator. The messages of a service worker are dispatched at the
  `ServiceWorkerContainer` of the client once its client message queue is enabled (`startMessages()`, `onmessage`, or the
  `DOMContentLoaded` event of the document). `ServiceWorker.postMessage()` throws an `InvalidStateError` for the object
  of a window whose document is not fully active (e.g. a removed iframe), and `register()` is rejected with an
  `InvalidStateError` in a window that is closing.
- Not implemented: the static routing API (`InstallEvent.addRoutes()`), the idle termination of the service workers and
  the timeouts of their events, `Clear-Site-Data` (it only clears the client hints and the speculative loads), the storage partitioning by top-level site, the `cookieStore` of the
  service workers, and the reserved clients (`clients.get(event.resultingClientId)` resolves with `undefined` until the
  document or the worker of the navigation exists, and the document that replaces the initial `about:blank` document of
  an iframe is another client).

## Cookies

`BrowsingContextOptions.CookieJar` is the cookie store of the browsing contexts (a `CookieJar`, which several browsing
contexts can share): without it, the documents have no cookies. The jar implements the user agent requirements of
[RFC 6265bis](https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html): the parsing of the `Set-Cookie`
headers (the `Expires`, `Max-Age`, `Domain`, `Path`, `Secure`, `HttpOnly`, `SameSite` and `Partitioned` attributes, the
size limits), the public suffixes, the `__Secure-`, `__Host-`, `__Http-` and `__Host-Http-` prefixes, the lifetime limit
of 400 days, the order of the `Cookie` header (the longest paths first) and the eviction of the expired and excess
cookies. Create it with the `TimeProvider` of the browsing contexts so that the cookies expire with their clock.

```csharp
var cookies = new CookieJar(timeProvider);
cookies.SetCookie("https://example.com/", "session=abc; Path=/; Secure; HttpOnly");
using var context = new BrowsingContext(new BrowsingContextOptions { DocumentLoader = loader, CookieJar = cookies });
// ...
foreach (var cookie in cookies.GetCookies("https://example.com/")) { /* name, value, domain, path, expiry, flags */ }
```

- The SameSite rules are schemeful: the "site for cookies" of a document is the origin of its top-level document when the
  document and its ancestors are same-site with it, else it is opaque (the requests and the scripts of a cross-site
  iframe, or of a sandboxed iframe, are cross-site). A cross-site request only sends and stores the `SameSite=None`
  cookies (which must be `Secure`), except a top-level navigation, which sends the `Lax` cookies (the cookies without
  `SameSite` attribute are `Lax` by default) when its method is safe, and stores any cookie. A request is cross-site when
  a URL of its redirect chain is cross-site, and the navigation of an iframe is cross-site when it is initiated by a
  cross-site document.
- The `Partitioned` cookies (CHIPS) are keyed by the site of the top-level document and whether the context has a
  cross-site ancestor. The third-party cookies are not blocked.
- `document.cookie` reads and writes the cookies of the URL of the document, except the `HttpOnly` cookies (the cookies
  of a "non-HTTP" API). An `about:blank` or `about:srcdoc` document uses the URL of the document whose origin it inherits,
  as the browsers do. A document whose origin is opaque throws a `SecurityError`.
- `window.cookieStore` (the [Cookie Store API](https://cookiestore.spec.whatwg.org/), in secure contexts) reads and writes
  the same cookies for the creation URL of the document (`history.pushState()` does not change it). The cookies it sets
  are `Secure` (`SameSite=Strict` by default), and its `change` events report the changes of the jar visible to the
  document (the cookies set by the responses, other documents, `document.cookie`...). The `CookieStoreManager` of the
  service workers is not implemented.
- The names and the values of the cookies are byte sequences, represented by strings of bytes (as the HTTP headers):
  the scripts store the UTF-8 bytes of their strings, and read the cookies decoded as UTF-8.
- A `CookiePersistence` given to the jar (`new CookieJar(persistence, timeProvider)`) persists the persistent cookies
  (the cookies with an expiry time), e.g. in a file or a database. The jar keeps the cookies in memory: it loads them with
  `LoadCookies()` when it is created (the expired cookies are removed, and the cookies without domain or whose path does
  not start with `/` are ignored), and reports each change in its lock, in order: `SetCookie` for a created or changed
  persistent cookie, `RemoveCookie` for a deleted, expired or evicted cookie (and for a persisted cookie replaced by a
  session cookie), and `ClearAll` for `CookieJar.Clear()`. A cookie is identified by its name, domain, host-only flag,
  path and `PartitionKey` (a `CookiePartitionKey`: the top-level site and the cross-site ancestor bit). The last access
  time (`StoredCookie.LastAccessTime`, which orders the eviction of the excess cookies) is persisted at most once a
  minute. An implementation must be thread-safe and should buffer the writes; its exceptions propagate to the caller of
  the jar. The ephemeral jar of the credentialless iframes is not persisted.

  ```csharp
  sealed class FileCookiePersistence : CookiePersistence { /* LoadCookies, SetCookie, RemoveCookie, ClearAll */ }

  var cookies = new CookieJar(new FileCookiePersistence(path), timeProvider);
  ```

## Web Storage

`window.localStorage` and `window.sessionStorage` (https://html.spec.whatwg.org/multipage/webstorage.html) are
implemented, with the `storage` events:

- The storage key is the origin of the document (the Storage Standard does not partition the storage by top-level site
  yet), with the page credentialless nonce of the top-level window for the documents of the credentialless iframes
  (https://wicg.github.io/anonymous-iframe/#spec-storage-key). A document whose origin is opaque (e.g. a sandboxed iframe
  without `allow-same-origin`, a `data:` URL) throws a `SecurityError`.
- The local storage areas are kept in a `StorageShed` (in memory): `BrowsingContextOptions.LocalStorage`, which several
  browsing contexts can share (as `CookieJar`), or by default a shed per browsing context created by the application,
  shared by its auxiliary browsing contexts. `StorageShed.GetItems(origin)` returns the items of an origin and `Clear()`
  removes all of them.

  ```csharp
  var storage = new StorageShed();
  using var context = new BrowsingContext(new BrowsingContextOptions { DocumentLoader = loader, LocalStorage = storage });
  // ...
  foreach (var (key, value) in storage.GetItems("https://example.com")) { }
  ```

- A `WebStoragePersistence` given to the shed (`new StorageShed(persistence)`) persists the local storage areas, e.g. in
  a file or a database. The shed keeps the items in memory: it loads the items of an origin with `LoadItems(origin)` the
  first time the origin is used, and reports each change (`SetItem`, `RemoveItem`, `ClearItems`, and `ClearAll` for
  `StorageShed.Clear()`). The methods are called synchronously by the scripts, from the threads of the browsing contexts
  that share the shed: an implementation must be thread-safe, and should buffer the writes to a slow storage. A failure
  to load makes `localStorage` throw a `SecurityError` (the area is loaded again the next time), and a failure of
  `SetItem` makes `setItem()` throw a `QuotaExceededError`; the item is then not changed. The areas of the credentialless iframes are not
  persisted, and the session storage is always kept in memory.

  ```csharp
  sealed class FileWebStoragePersistence : WebStoragePersistence { /* LoadItems, SetItem, RemoveItem, ClearItems, ClearAll */ }

  var storage = new StorageShed(new FileWebStoragePersistence(path));
  ```

- The session storage areas belong to the top-level browsing context: its documents of the same origin share them. A
  popup opened with an opener gets a copy of the session storage of the top-level browsing context of its opener
  (https://storage.spec.whatwg.org/#legacy-clone-a-traversable-storage-shed); a `noopener` popup starts empty.
- The quota of a storage area is 5 Mi UTF-16 code units (the lengths of the keys and the values; `new StorageShed(quota)`
  sets another one): `setItem()` throws a `QuotaExceededError` (the `QuotaExceededError` interface of WebIDL) when it is
  exceeded.
- `Storage` is a legacy platform object: the items are named properties (`localStorage.key = 'value'`, `delete
  localStorage.key`, `Object.keys(localStorage)`).
- `setItem()`, `removeItem()` and `clear()` fire a `storage` event (`StorageEvent`, with `storageArea`) at the other
  windows whose document has the same storage key, from a task: for the local storage, the windows of all the browsing
  contexts that use the shed (the tasks of another event loop are queued on it); for the session storage, the windows of
  the same top-level browsing context. A change that does not modify the storage area (the same value, a missing key, an
  empty area) fires no event.

## IndexedDB

`indexedDB` (https://w3c.github.io/IndexedDB/, the third edition with `getAllRecords()` and the `IDBGetAllOptions`
dictionaries) is implemented in the windows and the workers:

- The storage key is the origin of the global object (with the page credentialless nonce of the top-level window for the
  credentialless iframes, whose databases are kept in memory), as for Web Storage. An opaque origin throws a `SecurityError`.
- The databases are stored by an `IndexedDBBackend`: `BrowsingContextOptions.IndexedDB`, which several browsing contexts
  can share, or by default an `InMemoryIndexedDBBackend` per browsing context created by the application, shared by its
  auxiliary browsing contexts and their workers. The connections of a window and of its workers (on other threads) to the
  same database see each other (`versionchange` and `blocked` events, transaction scheduling).
- The library implements the API: the requests and their events, the transaction lifecycle (auto-commit, `commit()`,
  `abort()`, the cleanup of the transactions at the end of the task), the scheduling of the transactions, the upgrade
  transactions, the key generators, the key paths, the indexes (`unique`, `multiEntry`) and the cursors. The backend is
  the database engine: it stores the schemas, the records and the index records, and answers the range queries. A
  connection or a transaction of a terminated worker or of an unloaded document is closed (its transactions are aborted).
- The keys and the values given to the backend are opaque byte arrays. The keys are encoded so that their byte order
  (`memcmp`) is the order of the IndexedDB keys: a backend sorts and compares them as byte strings. The values are the
  values serialized for storage (StructuredSerializeForStorage), including the bytes of their `Blob` and `File` objects,
  in a versioned binary format.
- The library guarantees to a backend that the calls for a database are never concurrent, that at most one `readwrite` or
  `versionchange` transaction of a database is open at a time (the read/write transactions of a database are run one after
  the other, which the specification allows), and that the `readonly` transactions open at the same time do not read its
  object stores. So a SQLite backend can map a read/write transaction to `BEGIN`/`COMMIT`/`ROLLBACK`, and run the read-only
  ones without transaction. `Abort()` reverts all the changes, including the schema, the version and the key generators,
  and deletes a database created by the transaction.

  ```csharp
  sealed class SqliteIndexedDBBackend : IndexedDBBackend
  {
      // GetDatabases, GetDatabase, DeleteDatabase, BeginTransaction (returns an IndexedDBBackendTransaction:
      // the schema changes, GetRecords/CountRecords/PutRecord/DeleteRecords, GetIndexRecords/CountIndexRecords, Commit/Abort)
  }

  // A file per database:
  //   CREATE TABLE records (store_id INTEGER, key BLOB, value BLOB, PRIMARY KEY (store_id, key)) WITHOUT ROWID;
  //   CREATE TABLE index_records (index_id INTEGER, key BLOB, primary_key BLOB, PRIMARY KEY (index_id, key, primary_key)) WITHOUT ROWID;
  //   SELECT key, value FROM records WHERE store_id = @id AND key >= @lower AND key < @upper ORDER BY key LIMIT @limit;
  using var context = new BrowsingContext(new BrowsingContextOptions { ScriptEngine = engine, IndexedDB = new SqliteIndexedDBBackend(directory) });
  ```

  The test project has such a backend (`tests/Meziantou.Framework.WebPlatform.Tests/Storage/SqliteIndexedDBBackend.cs`),
  checked by the same tests as `InMemoryIndexedDBBackend`.
- The exceptions of a backend make the request fail with an `UnknownError` (or abort the transaction for the schema
  changes and the commit). The values that cannot be serialized for storage (e.g. a `SharedArrayBuffer`, a
  `WebAssembly.Module`) throw a `DataCloneError`.
- Without script engine, the .NET values can be used as keys (the numbers, the strings, the dates, the byte arrays and the
  lists of keys) and values (the primitive values and the serializable platform objects such as `Blob`).
- The storage is not partitioned by top-level site, and the storage buckets (`navigator.storageBuckets`) are not
  implemented.

## Cache API

`caches` (https://w3c.github.io/ServiceWorker/#cache-objects: `CacheStorage` and `Cache`) is implemented in the windows
and the workers (including the service workers) of the secure contexts:

- The storage key is the origin of the global object, as for IndexedDB; an opaque origin rejects the promises with a
  `SecurityError`.
- The caches are stored by a `CacheStorageBackend`: `BrowsingContextOptions.CacheStorage`, which several browsing contexts
  can share, or by default an `InMemoryCacheStorageBackend` per browsing context created by the application, shared by its
  auxiliary browsing contexts and their workers.
- The library implements the API: the matching of the requests (`ignoreSearch`, `ignoreMethod`, `ignoreVary` and the
  `Vary` header), the batch operations (`put()`, `add()`, `addAll()` and `delete()` are atomic), the fetches of `add()`
  and `addAll()`, and the reading of the bodies. A `Cache` object keeps working (in memory) when its cache is deleted.
- The backend stores the named caches of each origin (in creation order) and their entries (a `CachedRequest` and a
  `CachedResponse` with its body, in insertion order): `GetCacheNames`, `CreateCache`, `DeleteCache`, `GetEntries`
  (optionally filtered by the URL of the request without query, an indexable column) and `ApplyBatch` (removes entries by
  id and appends new entries, atomically). The status, the headers and the body of an opaque response are the ones of its
  internal response (the scripts only see the filtered response), so that a service worker can give it to a page. The methods are called from the threads of the windows and the workers: an
  implementation must be thread-safe.

  ```csharp
  sealed class SqliteCacheStorageBackend : CacheStorageBackend { /* GetCacheNames, CreateCache, DeleteCache, GetEntries, ApplyBatch */ }

  using var context = new BrowsingContext(new BrowsingContextOptions { ScriptEngine = engine, CacheStorage = new SqliteCacheStorageBackend(path) });
  ```

  The test project has such a backend (`tests/Meziantou.Framework.WebPlatform.Tests/Storage/SqliteCacheStorageBackend.cs`).
- The responses are not checked against the Cross-Origin-Resource-Policy of the document (`matchAll()` returns the opaque
  responses), and the storage is not partitioned by top-level site.

## Storage manager

`navigator.storage` (`Navigator.Storage`, `WorkerNavigator.Storage`: the `StorageManager` of the
[Storage Standard](https://storage.spec.whatwg.org/)) is implemented in the windows and the workers of the secure
contexts:

```csharp
using var context = new BrowsingContext(new BrowsingContextOptions
{
    ScriptEngine = engine,
    Storage = new StorageOptions
    {
        Quota = 512 * 1024 * 1024,                                            // what estimate() reports; enforced by the file system
        RequestPersistentStorage = origin => origin == "https://example.com", // the answer to persist()
    },
});

StorageEstimate estimate = await context.Window.Navigator.Storage.Estimate();
```

- **`estimate()`** resolves with the usage and the quota of the storage of the origin. The usage is the sum of what the
  storages of the origin report: the local storage area (the UTF-16 code units of its items), and the backends of
  IndexedDB (`IndexedDBBackend.GetUsage`), of the Cache API (`CacheStorageBackend.GetUsage`) and of the origin private
  file system (`FileSystemBackend.GetUsage`). The in-memory backends report the size of their records, entries and
  files; `GetUsage` is virtual and returns 0 by default, so a backend that cannot measure its storage is not counted.
  The service worker registrations and the cookies are not counted. The quota is `StorageOptions.Quota` (1 GiB by
  default): the file system enforces it, the other storages have their own limits (e.g. `StorageShed.Quota`).
- **`persisted()` and `persist()`**: the local storage bucket of a storage key is "best-effort" until `persist()` is
  granted, then "persistent" for the browsing context group. The library never evicts a storage: the mode only is what
  the scripts observe. `persist()` (in the windows only) asks the host with `StorageOptions.RequestPersistentStorage`,
  which plays the role of the "persistent-storage" permission; without it, the permission is denied. In a prerendering
  document, `persist()` waits for the activation (`[DelayWhilePrerendering]`).
- The promises are settled by tasks. A document whose origin is opaque (e.g. a sandboxed iframe) has no storage: the
  three methods reject with a `TypeError`, and `getDirectory()` with a `SecurityError`.
- Not implemented: the Storage Buckets API (`navigator.storageBuckets`: the storage of an origin is its default bucket),
  the `usageDetails` member that Chromium adds to the estimation, the Permissions API
  (`navigator.permissions.query({ name: "persistent-storage" })`), and the partitioning of the storage by top-level site.

`WebPlatformFeatures.StorageManagerEnabled` hides `navigator.storage` (and the file system below) from the scripts.

## Origin private file system

`navigator.storage.getDirectory()` (`StorageManager.GetDirectory()`) returns the root directory of the origin private
file system of the [File System Standard](https://fs.spec.whatwg.org/), the bucket file system of the storage key of the
window or of the worker:

```csharp
var root = await context.Window.Navigator.Storage.GetDirectory();
var directory = await root.GetDirectoryHandle("data", new FileSystemGetDirectoryOptions { Create = true });
var file = await directory.GetFileHandle("notes.txt", new FileSystemGetFileOptions { Create = true });

FileSystemWritableFileStream stream = await file.CreateWritable();
stream.Write("Hello"u8);
stream.Close();                                   // the buffer replaces the content of the file
File content = await file.GetFile();              // a snapshot
await foreach (var (name, handle) in root.GetEntriesAsync()) { /* name, handle.Kind */ }
```

- **Backend.** The files are stored by a `FileSystemBackend`: `BrowsingContextOptions.FileSystem`, which several
  browsing contexts can share, or by default an `InMemoryFileSystemBackend` per browsing context created by the
  application, shared by its auxiliary browsing contexts and their workers (the credentialless iframes have their own
  in-memory file system). The backend stores a tree of directories and files per origin, addressed by path:
  `GetEntryKind`, `GetEntries`, `CreateEntry`, `RemoveEntry`, `MoveFile`, `GetFileInfo` (size and modification time),
  `ReadFile` / `WriteFile` / `SetFileSize` at an offset (the access handles write in place), `ReplaceFileContent` (a
  writable stream is closed: the replacement must be atomic), `FlushFile` and `GetUsage`. The library implements the
  API: the handles, the validation of the names, the locks, the buffers of the writable streams and the quota. The
  methods are called from the threads of the windows and of the workers: an implementation must be thread-safe; its
  `IOException`s reject the promises with an `InvalidStateError`.

  ```csharp
  sealed class DirectoryFileSystemBackend : FileSystemBackend { /* a directory per origin: Directory.*, File.*, FileStream */ }

  using var context = new BrowsingContext(new BrowsingContextOptions { ScriptEngine = engine, FileSystem = new DirectoryFileSystemBackend(path) });
  ```

- **Handles.** A `FileSystemHandle` is a locator: the backend, the origin and the path of an entry, which may not exist
  (the operations then reject with a `NotFoundError`). `kind`, `name` (the empty string for the root), `isSameEntry()`,
  `FileSystemDirectoryHandle.getFileHandle()` and `getDirectoryHandle()` (with `create`; a `TypeMismatchError` when the
  entry has the other kind), `removeEntry()` (with `recursive`; an `InvalidModificationError` for a directory that is not
  empty), `resolve()`, and the async iteration (`entries()`, `keys()`, `values()`, `for await`; in .NET,
  `GetEntriesAsync()`). The names are valid file names: not empty, not `.` nor `..`, without `/` nor `\` (a
  `TypeError` otherwise). The steps that the specification runs on its file system queue run when the method is called
  (under the mutex of the backend), and the promises are settled by tasks of the event loop of the window or the worker.
- **Files.** `getFile()` resolves with a `File`, a copy of the content (its type is empty). As in the browsers, a
  writable stream refuses the snapshot of a file that was removed since (a `NotFoundError`).
- **Writable streams.** `createWritable({ keepExistingData })` resolves with a `FileSystemWritableFileStream`: in the
  scripts, a `WritableStream` (Streams.js) with `write()`, `seek()` and `truncate()`, whose chunks are a `BufferSource`,
  a `Blob`, a string or a `WriteParams` command (`write` with `position`, `seek`, `truncate`); in .NET, the sink object
  (`Write`, `Seek`, `Truncate`, `Close`, `Abort`). The writes go to a buffer in memory (a copy of the file with
  `keepExistingData`), which replaces the content of the file when the stream is closed: the file never has a partial
  write, and an aborted or errored stream changes nothing. The specification rejects a command without its `data`,
  `position` or `size` member with a `TypeError`; the browsers all reject with a `SyntaxError` `DOMException` (and with
  a `TypeError` for a null `data`), which the library follows.
- **Access handles.** `createSyncAccessHandle()`, in the dedicated workers, resolves with a
  `FileSystemSyncAccessHandle`: `read()` and `write()` (into and from a buffer source, at the cursor or `at` an offset),
  `truncate()`, `getSize()`, `flush()` and `close()` are synchronous, and change the file in place.
- **Locks.** A writable stream takes the shared lock of the file, and an access handle its exclusive lock, until they
  are closed (or aborted, or errored): the conflicting primitive is rejected with a `NoModificationAllowedError`. The
  locks are the ones of the backend: they are seen by all the browsing contexts and the workers that share it. They are
  released when the document is unloaded or the worker ends. As in the browsers, a locked file, or a directory that
  contains one, is not removed nor replaced (`NoModificationAllowedError`).
- **Quota.** A write that makes the usage of the file system of the origin (`FileSystemBackend.GetUsage`, plus the
  buffer of the stream) exceed `StorageOptions.Quota` is rejected with a `QuotaExceededError`.
- **Serialization.** The handles are serializable: `postMessage()` to the windows and the workers of the origin,
  `structuredClone()`, and IndexedDB (the stored locator is resolved in the file system of the global object that reads
  it). A handle received by another origin fires `messageerror` (a `DataCloneError`).
- **Beyond the standard**, as the web-platform-tests of the directory exercise them: `FileSystemFileHandle.move(name)`,
  `move(directory)` and `move(directory, name)` (https://github.com/whatwg/fs/pull/10, implemented by the three
  browsers), `FileSystemHandle.remove({ recursive })` (https://github.com/whatwg/fs/pull/9, Chromium), and the locking
  modes of Chromium (`createWritable({ mode: "siloed" | "exclusive" })`,
  `createSyncAccessHandle({ mode: "readwrite" | "read-only" | "readwrite-unsafe" })` and the `mode` attributes): the
  primitives of the same shareable mode share the lock.
- Not implemented: `FileSystemObserver`, `getUniqueId()`, the file systems of the storage buckets, the File System
  Access API (the pickers and the handles of the files of the user), and the locks that Chromium takes during a
  `move()` or a `remove()` (the operations of the library are atomic).

`WebPlatformFeatures.FileSystemEnabled` hides `getDirectory()` and the `FileSystem*` interfaces from the scripts.

## Web Locks

The [Web Locks API](https://w3c.github.io/web-locks/) is implemented in the secure contexts: `navigator.locks`
(`Navigator.Locks`, `WorkerNavigator.Locks`: a `LockManager`) in the windows and in the dedicated, shared and service
workers, `request()`, `query()` and the `Lock` interface (`WebLock` in .NET, as `System.Threading.Lock` has its name).

```csharp
var locks = context.Window.Navigator.Locks;
Task<object?> released = locks.Request("resource", new LockOptions { Mode = LockMode.Shared }, async @lock =>
{
    await DoWorkAsync();   // the lock is held until the task completes
    return "done";
});
LockManagerSnapshot state = await locks.Query();   // the held locks and the pending requests, with their client ids
```

- **Lock managers.** The windows and the workers of a browsing context group share a lock manager per storage key (the
  origin, and the nonce of the credentialless iframes); the popups of a page are in its group. The storage of the
  third-party contexts is partitioned: the key also has the site of the top-level document and whether the document or
  one of its ancestors is cross-site with it, so an iframe embedded by another site does not share the locks of the
  first-party pages of its origin. A document whose origin is opaque (e.g. a sandboxed iframe) has no lock manager:
  `request()` and `query()` reject with a `SecurityError`. Two browsing context groups (two `BrowsingContext` created
  by the host) do not share their locks.
- **Requests.** `request(name, options, callback)` rejects with an `InvalidStateError` when the document is not fully
  active, and with a `NotSupportedError` for a name that starts with `-`, for `steal` with `ifAvailable`, with the
  `shared` mode or with a signal, and for `ifAvailable` with a signal; a signal that is already aborted rejects with its
  reason. The requests of a resource are granted in the order they were made: a request is grantable when it is the
  first of the queue of its resource and no held lock conflicts (`exclusive` conflicts with every lock of the resource,
  `shared` with the exclusive ones). `ifAvailable` calls the callback with null instead of waiting. `steal` releases
  the held locks of the resource (their promises reject with an `AbortError`) and is granted before the pending
  requests. Aborting the signal removes a pending request and rejects its promise; the signal is ignored once the
  callback is called.
- **Callbacks.** The callback is called by a task of the event loop of the window or of the worker that made the
  request, with a `Lock`. The lock is held until the promise the callback returns settles (a callback that throws, or
  returns a value that is not a promise, releases it at once); the promise of `request()` then settles as the promise
  of the callback. `query()` resolves from a task with the held locks and the pending requests of the lock manager;
  `clientId` is the id of the environment, the one of its [service worker client](#service-workers).
- **Prerendering.** `request()` and `query()` of a prerendering document (see [Speculative loading](#speculative-loading))
  return a pending promise: the request is made, and the snapshot is taken, when the document is activated
  (`[DelayWhilePrerendering]`), so a page that is never shown holds no lock.
- **Threads.** The workers run on their own threads: the lock manager is thread-safe (the steps that the specification
  runs on its lock task queue run under a mutex, on the thread of the caller), and a grant queues a task on the event
  loop of the environment of the request, whatever the thread that released the lock.
- **Termination.** The locks of a window are released and its requests are removed when its document is unloaded or
  destroyed (a navigation, the removal of its `iframe`, the disposal of its browsing context), and the ones of a worker
  when it ends (`terminate()`, `close()`, the discard of its owner). A shared worker lives until it calls `close()` or
  its group is disposed (see [Workers](#workers)): it keeps its locks meanwhile.
- **Not done.** The back/forward cache (a page that holds a lock is unloaded by a navigation), and the lock managers
  of the storage buckets (`navigator.storageBuckets`).

`WebPlatformFeatures.WebLocksEnabled` hides the API from the scripts.

## Disabling features

`BrowsingContextOptions.Features` hides optional features of the web platform from the scripts of the windows and of
the workers of the browsing contexts, as the runtime flags of a browser: the interface objects and the members of a
disabled feature are not defined, so the scripts see an unsupported feature (`typeof WebSocket === "undefined"`,
`'sendBeacon' in navigator === false`) and take their fallback paths. Use it to reduce what the scripts of untrusted
documents can do, or to test the fallbacks of a page.

```csharp
using var context = new BrowsingContext(new BrowsingContextOptions
{
    ScriptEngine = engine,
    Features = new WebPlatformFeatures
    {
        WebSocketEnabled = false,
        BeaconEnabled = false,
        WebCryptoEnabled = false,
    },
});
```

| Property | What is hidden when false |
| --- | --- |
| `WebSocketEnabled` | `WebSocket` |
| `EventSourceEnabled` | `EventSource` |
| `BeaconEnabled` | `navigator.sendBeacon()` |
| `FetchLaterEnabled` | `fetchLater()`, `FetchLaterResult` (also hidden when `FetchEnabled` is false) |
| `SpeculationRulesEnabled` | The [speculation rules](#speculative-loading): `HTMLScriptElement.supports("speculationrules")` is false, `document.prerendering`, `document.onprerenderingchange` and `PerformanceNavigationTiming.activationStart` are hidden, and the rules of the `script` elements and of the `Speculation-Rules` header are ignored |
| `ClientHintsEnabled` | `navigator.userAgentData`, `NavigatorUAData`; no [client hint](#client-hints) is sent (the `Sec-CH-*` headers), and the `Accept-CH` and `Critical-CH` headers and the `<meta http-equiv="delegate-ch">` elements are ignored |
| `WebCryptoEnabled` | `crypto`, `Crypto`, `SubtleCrypto`, `CryptoKey` |
| `FetchEnabled` | `fetch()`, `Request`, `Response`, `Headers` |
| `XmlHttpRequestEnabled` | `XMLHttpRequest`, `XMLHttpRequestEventTarget`, `XMLHttpRequestUpload` |
| `DedicatedWorkersEnabled` | `Worker` |
| `SharedWorkersEnabled` | `SharedWorker` |
| `ServiceWorkersEnabled` | `navigator.serviceWorker`, `ServiceWorker`, `ServiceWorkerRegistration`, `ServiceWorkerContainer`, `NavigationPreloadManager`; no service worker runs or handles the requests |
| `AsyncClipboardEnabled` | `navigator.clipboard`, `Clipboard`, `ClipboardItem`, `ClipboardChangeEvent` (the copy, cut and paste actions still work) |
| `CookieStoreEnabled` | `cookieStore`, `CookieStore`, `CookieChangeEvent` (`document.cookie` still works) |
| `WebStorageEnabled` | `localStorage`, `sessionStorage`, `Storage`, `StorageEvent` |
| `IndexedDBEnabled` | `indexedDB` and the `IDB*` interfaces |
| `CacheStorageEnabled` | `caches`, `Cache`, `CacheStorage` (also hidden when `FetchEnabled` is false) |
| `BroadcastChannelEnabled` | `BroadcastChannel` |
| `WebLocksEnabled` | [Web Locks](#web-locks): `navigator.locks` (in the windows and the workers), `LockManager`, `Lock` |
| `StorageManagerEnabled` | [Storage manager](#storage-manager): `navigator.storage` (in the windows and the workers), `StorageManager`; the origin private file system is hidden too |
| `FileSystemEnabled` | [Origin private file system](#origin-private-file-system): `navigator.storage.getDirectory()`, `FileSystemHandle`, `FileSystemFileHandle`, `FileSystemDirectoryHandle`, `FileSystemWritableFileStream`, `FileSystemSyncAccessHandle` |
| `CompressionStreamsEnabled` | `CompressionStream`, `DecompressionStream` |
| `NavigationApiEnabled` | `navigation`, `Navigation`, `NavigateEvent` and the other `Navigation*` interfaces |
| `UrlPatternEnabled` | `URLPattern` |
| `WebAssemblyEnabled` | `WebAssembly` (defined only when `WebAssemblyRuntime` is set) |
| `ResizeObserverEnabled` | `ResizeObserver`, `ResizeObserverEntry`, `ResizeObserverSize` |
| `IntersectionObserverEnabled` | `IntersectionObserver`, `IntersectionObserverEntry`; when disabled, the `loading="lazy"` images and iframes are loaded at once |
| `LayoutInstabilityEnabled` | `LayoutShift`, `LayoutShiftAttribution` and the `layout-shift` entry type of `PerformanceObserver.supportedEntryTypes` |
| `CanvasEnabled` | `CanvasRenderingContext2D`, `CanvasGradient`, `CanvasPattern`, `TextMetrics`, `Path2D`, `OffscreenCanvas`, `OffscreenCanvasRenderingContext2D` and `HTMLCanvasElement.transferControlToOffscreen()`; when disabled, `getContext("2d")` returns null |
| `PaintWorkletEnabled` | `CSS.paintWorklet` (and `Worklet`, when the three kinds of worklets are disabled); when disabled, the `paint()` images are invalid images |
| `LayoutWorkletEnabled` | `CSS.layoutWorklet`; when disabled, the `display: layout()` boxes are flow-root block containers |
| `AnimationWorkletEnabled` | `CSS.animationWorklet` and `WorkletAnimation` |
| `PaintTimingEnabled` | `PerformancePaintTiming` and the `paint` entry type |
| `LargestContentfulPaintEnabled` | `LargestContentfulPaint` and the `largest-contentful-paint` entry type |
| `ElementTimingEnabled` | `PerformanceElementTiming`, the `element` entry type and `Element.elementTiming` |
| `EventTimingEnabled` | `PerformanceEventTiming`, `EventCounts`, `performance.eventCounts`, `performance.interactionCount` and the `event` and `first-input` entry types; the events of the user input are not timed (they request no rendering opportunity) |
| `UserTimingEnabled` | `PerformanceMark`, `PerformanceMeasure`, `performance.mark()`, `measure()`, `clearMarks()`, `clearMeasures()` and the `mark` and `measure` entry types |
| `NavigationTimingEnabled` | `PerformanceNavigationTiming`, `PerformanceTiming`, `PerformanceNavigation`, `performance.timing`, `performance.navigation` and the `navigation` entry type |
| `ScrollDrivenAnimationsEnabled` | `ScrollTimeline`, `ViewTimeline`, `Animation.rangeStart` and `Animation.rangeEnd`; the scroll and view timelines of `animation-timeline` are then no timeline |
| `SvgAnimationsEnabled` | The SVG animations (SMIL): the `animate`, `set`, `animateMotion` and `animateTransform` elements have no effect and fire no event, and `TimeEvent` is hidden |
| `MediaSourceEnabled` | The [Media Source Extensions](#media-source-extensions): `MediaSource`, `MediaSourceHandle`, `SourceBuffer`, `SourceBufferList`, `ManagedMediaSource`, `ManagedSourceBuffer`, `BufferedChangeEvent` and the `sourceBuffer` attribute of the tracks |
| `MediaPlaybackQualityEnabled` | `HTMLVideoElement.getVideoPlaybackQuality()` and `VideoPlaybackQuality` |
| `TextTracksEnabled` | The text tracks of the media elements: `TextTrack`, `TextTrackList`, `TextTrackCue`, `TextTrackCueList`, `VTTCue`, `VTTRegion`, `HTMLMediaElement.textTracks`, `HTMLMediaElement.addTextTrack()` and `HTMLTrackElement.track`; the resources of the `track` elements are not fetched and no cue is rendered |
| `ViewTransitionsEnabled` | The [view transitions](view-transitions.md): `document.startViewTransition()`, `document.activeViewTransition`, `element.startViewTransition()`, `element.activeViewTransition`, `ViewTransition`, `ViewTransitionTypeSet` and `CSSViewTransitionRule`; the cross-document transitions are then disabled (`pageswap` and `pagereveal` are still fired, with a null `viewTransition`) |
| `ReportingObserverEnabled` | `ReportingObserver` (the reports are still delivered to the endpoints) |
| `DeclarativePartialUpdatesEnabled` | The tentative `buffer`, `sanitize` and `src` attributes of the templates that patch content and `HTMLTemplateElement.buffer` (see [partial-updates.md](partial-updates.md#tentative-extensions)); the `<template for>` patching of the HTML Standard still works |
| `PermissionsPolicyEnabled` | `PermissionsPolicy`, `document.permissionsPolicy` and `iframe.permissionsPolicy` (the policies are still enforced) |
| `TouchEventsEnabled` | `Touch`, `TouchList`, `TouchEvent`, and the `touchstart`, `touchmove`, `touchend` and `touchcancel` events of the touch pointers of [`BrowsingContext.Input`](#user-input) (the pointer events are still fired) |
| `CloseWatcherEnabled` | `CloseWatcher` (the close requests still close the dialogs and the popovers) |
| `FullscreenEnabled` | The [Fullscreen API](#fullscreen): `Element.requestFullscreen()`, `document.exitFullscreen()`, `fullscreenElement` (of `Document` and `ShadowRoot`), `fullscreenEnabled`, `fullscreen`, `onfullscreenchange` and `onfullscreenerror` (the `:fullscreen` pseudo-class is still parsed) |
| `PrioritizedTaskSchedulingEnabled` | [Prioritized Task Scheduling](#prioritized-task-scheduling): `scheduler`, `Scheduler`, `TaskController`, `TaskSignal`, `TaskPriorityChangeEvent` (`Promise.prototype.then` is then the function of the engine) |
| `PointerLockEnabled` | The [Pointer Lock API](#pointer-lock): `Element.requestPointerLock()`, `document.exitPointerLock()`, `pointerLockElement` (of `Document` and `ShadowRoot`), `onpointerlockchange` and `onpointerlockerror` |
| `TextDirectivesEnabled` | The [text directives](#text-directives): `FragmentDirective`, `document.fragmentDirective`; the fragment directive is then a part of the fragment of the URLs (it is neither removed nor searched) |
| `VisibilityStateEntryEnabled` | `VisibilityStateEntry` and the `visibility-state` entry type (`document.visibilityState` and `visibilitychange` still work) |
| `CssParserApiEnabled` | The [CSS Parser API](css-parser-api.md), a draft that no browser implements: `CSS.parseStylesheet()`, `CSS.parseRuleList()`, `CSS.parseRule()`, `CSS.parseDeclarationList()`, `CSS.parseDeclaration()`, `CSS.parseValue()`, `CSS.parseValueList()`, `CSS.parseCommaValueList()` and the `CSSParser*` interfaces |

All the properties are true by default (`WebPlatformFeatures.Default`, used when `Features` is null). `WebPlatformFeatures`
is a record: derive a set of features from another one with `with` (e.g. `WebPlatformFeatures.Default with { FetchEnabled = false }`). The .NET API of the
features is not affected (e.g. `Navigator.SendBeacon()` still sends the beacon when the application calls it).
`DeclarativePartialUpdatesEnabled` is an [experimental API](experimental-apis.md#WEBPLATEXP0003) (`WEBPLATEXP0003`): the
declarative partial updates are a tentative feature.

## Out of scope

The back/forward cache is not implemented (`window.external` is the obsolete `External`
object, whose methods do nothing). Of the sandboxing flags, the origin, scripts, forms, `document.domain`, navigation
(including the top-level navigation flags), popups (auxiliary navigation, and its propagation to the popups), pointer
lock and downloads flags have an effect; the others (modals, orientation lock, presentation, automatic features,
storage access) restrict features that are not implemented.
