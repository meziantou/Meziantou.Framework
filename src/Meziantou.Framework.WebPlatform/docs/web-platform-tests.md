# web-platform-tests

`tests/Meziantou.Framework.WebPlatform.Wpt` runs the [web-platform-tests](https://github.com/web-platform-tests/wpt)
(testharness.js tests and [reftests](#reftests)) against the library:

- The test page is loaded by a navigation, with the headers of its `.headers` files (an XML test page is parsed from its
  file: only the `Content-Language` of its `.headers` file is applied, as the default language of its elements), in a top-level
  [browsing context](browsing-context.md) whose script engine is implemented with [Jint](https://github.com/sebastienros/jint) (see [scripting.md](scripting.md)). The library
  runs the scripts as in a browser: the script elements, the event handler content attributes, the `javascript:` URLs.
  The documents of the iframes and the scripts are loaded from the files of the test suite by the `DocumentLoader` of the
  browsing context. The headers of a response are the ones of the `.headers` files (the `__dir__.headers` file of its
  directory, then its own) and of the `pipe=header()` query parameter (the `Content-Type` comes from the extension when
  there is none), and its status is set by `pipe=status()`
  (`Runner/WptPipes.cs` parses the pipes as wptserve does: escapes, `header()` replaces the headers with the same name
  unless its third argument is `True`; `gzip` sets the `Content-Encoding` and the `Content-Length` of the compressed body). The test page and the documents
  are loaded as bytes, so the encoding is determined as in a browser: the BOM, the `charset` of the `.headers` file, the
  `<meta charset>` or the XML declaration, the encoding of the parent document, the encoding detected from the content
  (the legacy encodings only: the tests are loaded from `http:` and `https:` URLs, for which UTF-8 is not detected, as in
  the browsers), then windows-1252. The style sheets are
  loaded as bytes too (the CSS rules determine their encoding), as are the JSON modules, the text modules and the scripts that are not ASCII
  (the BOM, the `charset` of the `Content-Type`, the `charset` attribute, then the encoding of the document). The response
  of an external script, a style sheet, an image or a font is delayed by the `pipe=trickle(d<seconds>)` query parameter
  (using the virtual clock): a parser-blocking script pauses the parser. The document of a navigation (a file or the
  response of a handler) whose URL has a `trickle()` pipe is sent as wptserve sends it: the headers at once, then the
  parts of the body after their delays (`<bytes>`, `d<seconds>`, a final `r<n>` that repeats the parts), as chunks
  that the parser parses as they arrive. The browsing context enables the speculative
  parsing of the library, as in a browser.
- The requests of `fetch()` and `XMLHttpRequest` (`RequestDestination.Empty`) are answered as by wptserve: the files of
  the test suite with their status, status message and headers (plus `Content-Length`; a missing file is a `404`
  response, and a directory gets the listing of wptserve, after a `301` redirect when its path has no trailing slash), the raw HTTP responses of the `.asis` files, and the Python handlers. Their delays use the virtual clock,
  except for the synchronous requests of the windows (a synchronous request of a worker only blocks the thread of the
  worker). The redirects are not followed by the loader: the library follows them.
- The Python handlers of the WPT server (`.py` files) cannot run: the ones the tests use are ported to .NET, the generic
  handlers of `common` in `Runner/WptPythonHandlers.cs`, the ones of the `xhr` and `fetch/api` tests in
  `WptPythonHandlers.Fetch.cs`, the ones of the `cookies` and `cookiestore` tests in `WptPythonHandlers.Cookies.cs`, the ones of the
  `content-security-policy` and `x-frame-options` tests and the report collector (`reporting/resources/report.py`) in
  `WptPythonHandlers.ContentSecurityPolicy.cs`, the
  ones of the `css` tests in `WptPythonHandlers.Css.cs`, the ones of the `resource-timing` and `navigation-timing` tests in
  `WptPythonHandlers.Timing.cs`, the ones of the `worklets` tests (the credentials and referrer checkers) in
  `WptPythonHandlers.Worklets.cs`, the ones of the `client-hints` tests in `WptPythonHandlers.ClientHints.cs` (they echo
  the `Sec-CH-*` headers and the legacy hints of the requests in response headers or in a document, opt into hints with
  the `Accept-CH`, `Critical-CH` and `Clear-Site-Data` headers, and count the requests of a token) and the other ones in
  `WptPythonHandlers.Html.cs`. Each port is a function of the request,
  the response and the stash that mirrors the `main(request, response)` function of its file:
  - The request (`WptHandlerRequest`) has the method, the query parameters and the body as byte strings, and the headers
    that wptserve receives: the headers of the fetch algorithm for `fetch()` and `XMLHttpRequest`, and the ones a browser
    sends for the other requests (the `Accept` header of the destination, `Origin` for a cross-origin CORS request or a
    request that is not a GET request, `Referer`, the `Content-Type` of a submitted form), plus the headers of the network
    layer (`Host`, `User-Agent`, `Accept-Encoding`, `Accept-Language`, `Cookie`).
  - The response (`WptHandlerResponse`) has a status, headers, a body and a delay (`time.sleep()`, measured by the virtual
    clock). The pipes of the URL apply to it, as for the files. The streamed responses (chunks, `trickle.py`) are sent at
    once after their delays, except the documents of the navigations and the content of the templates with a `src`
    attribute, which are parsed as their chunks arrive (`WptHandlerResponse.Chunks`, `DocumentResource.RemainingContentBytes`),
    the media resources, and the event streams (`text/event-stream`),
    whose events are dispatched as their chunks arrive (a port of a handler that writes a part of the body, sleeps, then
    writes the rest, sends the parts as chunks); an infinite response never completes
    (the test aborts it), and a handler that writes in a loop repeats its chunks (`WptHandlerResponse.ChunksRepeatAfter`,
    e.g. `eventsource/resources/message2.py`). The loader follows the
    redirects of the handlers, except for `fetch()` and `XMLHttpRequest`: the response has the URL of the location and the
    URLs of the redirects.
  - The headers are written as they are: a line break in a value ends the header (the browsers ignore the next line).
    `request.cookies` is the `Cookie` header parsed as Python's `http.cookies` does (e.g. a cookie named `path` is an
    attribute of the previous cookie, and a name with an illegal character makes the handler fail).
  - A handler that throws returns a `500` response, as wptserve does. A handler that is not ported is a network error (a
    `404` response for `fetch()` and `XMLHttpRequest`, so that the tests that only check the response of another request
    continue), and the log of the test lists it (`Handler not emulated`). A handler whose file does not exist is a `404`
    response, as a missing file (`Handler not found`).
  - The body of a response with a content coding is the decoded body (the network layer of a browser decodes it), and
    its `Content-Length` is the length of the encoded body, which the library reports as the `encodedBodySize` of the
    resource timing: the handlers that compress their body (e.g. `resource-timing/resources/gzip_xml.py`), the `gzip`
    pipe, and the files served with a `Content-Encoding` header (`gzip`, `deflate` and `br` are decoded).
  - The stash of the server (`Runner/WptStash.cs`) is shared by the documents of a test: a value put by a request is
    taken (and removed) by another one. `stash.put(key, value, path)` and `stash.take(key, path)` scope the key to a path.
  - The browsing context of a test has a cookie jar (`BrowsingContextOptions.CookieJar`, measured by the virtual clock):
    the `Set-Cookie` headers of the responses (handlers, files and the test page itself) are stored, and sent back in the
    `Cookie` header of the next requests when their credentials are included, with the SameSite rules of the library
    (see [Cookies](browsing-context.md#cookies)). `document.cookie` and `cookieStore` use the same jar. The local storage
    of a test is the default storage shed of its browsing context (see [Web Storage](browsing-context.md#web-storage)),
    used by the test helpers that report the results of other windows (`PrefixedLocalStorage.js`).
  - The browsing context of each page (a test, a reference) has its own history of visited links, empty at first
    (`BrowsingContextOptions.VisitedLinks`, see [Visited links](computed-style.md#visited-links)): as in the browsers, the
    tests of `:visited` link to their own URL (`href=""`), which the navigation to the page records, and the other links
    are unvisited.
  - `common/dispatcher/dispatcher.py`, the message queues used by `RemoteContext` (`common/dispatcher/dispatcher.js`), is
    ported, so the tests can run scripts in other windows and frames, cross-origin ones included: the executors of
    `common/dispatcher` (`executor.html`, `remote-executor.html`) and the `executor-window.py` of `RemoteContextHelper`
    (`html/browsers/browsing-the-web/remote-context-helper`). The other ported handlers of `common` are `redirect.py`,
    `redirect-opt-in.py`, `slow.py`, `slow-redirect.py`, `echo.py`, `refresh.py` and `CustomCorsResponse.py`. The path of
    a handler is the one of its file: the empty segments are ignored (`/cors//resources/status.py`).
  - The other ports: the handlers of `xhr` and `fetch/api` (e.g. `content.py`, `redirect.py`, `inspect-headers.py`,
    `status.py`, `preflight.py`, the `access-control-*.py` and `auth*.py` handlers, `stash-put.py`/`stash-take.py`), the
    ones that echo the body of the form submissions, the `encoding` and `url` documents in a given encoding, the
    `send-oac-header.py` handler of `html/browsers/origin`, the `stash.py` handlers of `html/syntax/speculative-parsing`
    (which record the `Accept`, `Origin` and `Referer` headers of the speculative fetches), the handlers of the script
    element tests of `html/semantics/scripting-1` (e.g. `log.py`, the `referrer-checker.py` modules, `serve-custom-response.py`
    and `module-fetch-tester.py`, whose responses depend on the previous requests of a key), the image handlers of
    `html/semantics/embedded-content/the-img-element` (`image-and-stash.py` counts the requests), the `stash.py` and
    `support/download_stash.py` handlers of `the-iframe-element` (the token of a download is put when the request is
    received), the handlers of
    `html/infrastructure/urls` (`resource.py` returns a resource of each type containing the `q` parameter as it appears
    in the URL; the duration of the videos tells the query that was received), the referrer checkers of
    `css/css-values/urls/support` (`image-`, `font-`, `stylesheet-` and `svg-filter-referrer-policy.py`, whose response depends
    on the `Referer` header of the request), the handlers of `resource-timing` (e.g.
    `TAOResponse.py`, `multi_redirect.py`, `status-code.py`, `SyntheticResponse.py`, whose delays and chunks use the
    virtual clock; the interim responses of
    `header-delay.py` are not sent, as the loader does not expose them) and of `navigation-timing` (`respond-slowly.py`
    streams its document over one second), `import-maps/static-import.py`, the handlers of `navigation-api` whose
    response depends on the previous requests (`cross-origin-redirect-on-second-visit.py`,
    `204-205-download-on-second-visit.py`) and `slow-no-store.py`, and the event streams of
    `eventsource/resources` (`WptPythonHandlers.EventSource.cs`: `message.py`, `message2.py`, the `last-event-id*.py`,
    `status-*.py` and `reconnect-fail.py` handlers, whose state is a cookie, and the `cors*.py` handlers). The
    `.event_stream` files are served as `text/event-stream`, as wptserve does. The handlers of `preload/resources` are
    in `WptPythonHandlers.Preload.cs` (the stash handlers, `echo-with-cors.py`, `echo-preload-header.py`,
    `link-header-referrer-policy.py`, `prefetch-info.py`, which records the headers of the requests, and
    `preload-module.py`, which blocks a module until another request releases it). The handlers of the `speculation-rules`
    tests are in `WptPythonHandlers.SpeculationRules.cs`: the executor documents of `prefetch/resources` (`executor.sub.html`
    with the headers of the request: `cookies.py`, `authenticate.py`, `counting-executor.py`, `prefetch_nvs_hint.py`, which
    holds the response of a prefetch until another request releases it...), the counters of the prefetch requests, the rule
    sets of `ruleset.py`, and the handlers of `prerender/resources` (`exec.py`, the key-value store and the message queues of
    `PrerenderChannel`, `deprecated-broadcast-channel.py`), and the documents of `clear-site-data/support` that send the
    `Clear-Site-Data` header (the random images of `clear-site-data-cache.py` are not emulated). The runner declares that
    its loader hides the IP address of the client (`SpeculativeLoadingOptions.AnonymousClientIpSupported`), as the tests
    only check the `Sec-Purpose` header. The `.sub.headers` files get the
    headers of the request (e.g. `{{header_or_default(Origin, *)}}`) for the images, the fonts and the requests of
    `fetch()` and `XMLHttpRequest`.
  - The handlers of the media tests (`WptPythonHandlers.Media.cs`): `delayed-broken-video.py`, `fetch/range/resources/video-with-range.py`
    (the ranges of the `Range` header and their rewrites) and `css/selectors/media/support/stall-resume.py`, which sends the
    first bytes of a video and the rest when another request resumes it (`WptHandlerResponse.ChunksCondition`: the chunks
    wait for a value of the stash, checked every 100 ms of the virtual clock). The chunks of the responses of the media
    resources are given to the library as they arrive, as for the documents. The handlers of the `cors` tests are in `WptPythonHandlers.Cors.cs`
    (`cors-makeheader.py`, which returns the headers of the request and records the preflights in the stash,
    `cors-cookie.py`, `preflight.py`, `preflight-partitioning.py`, `status.py`, `expose-headers.py`, whose raw response
    has the header lines of the query, and the `304.py` and `cache-304.py` handlers of the revalidation tests, which need
    an HTTP cache); `checkandremove.py`, which no test uses, is not ported. `track-element/cors/support/cors-tester.py`
    serves a WebVTT file (or a redirect) and records the `Origin` header and the cookie of each request of a `track`
    element. The resources of the `track` elements are served as bytes with the status of `pipe=status()`.
- The client hints of the runner (`BrowsingContextOptions.ClientHints`) are the ones of a desktop browser on Linux
  (`x86`, 64 bits) with 8 GiB of memory and a 4g connection (a round-trip time of 50 ms, 10 Mbit/s): the library sends
  no device memory nor network hint by default, and the `client-hints` tests check their presence and their range. The
  test page is given to the library as the response of a navigation (no request is sent): its `Accept-CH` header opts
  its origin in, but its `Critical-CH` header cannot restart the navigation.
- The media elements use the media backend of the runner (`Runner/WptMediaBackend.cs`, `BrowsingContextOptions.MediaBackend`,
  see [Media elements](browsing-context.md#media-elements)), which decodes nothing: it recognizes the media files of the
  test suite by the signature of their container (`Runner/WptMediaContainer.cs`: WebM/Matroska, MP4, Ogg, WAV, MP3, FLAC,
  AAC), reads their duration, their tracks and the size of their video (including the changes of the size of the VP8, VP9
  and H.264 key frames), answers `canPlayType()` as a browser that plays these containers and their usual codecs, plays the
  resources with the virtual clock (the default playback of `MediaPlayer`) and gives opaque black frames of the size of the
  video: the tests that compare the pixels of the decoded frames fail. The frames of the two videos of the test suite
  whose frames are all white (`/media/white.webm` and `/media/white.mp4`, recognized by their path) are opaque white: they
  are the videos of the reftests of the rendering of the WebVTT cues (`webvtt/rendering`), whose references are pages
  without video. The text tracks of the media resources are not read. A resource that is not complete (a response that
  arrives over time) only has its metadata (`HAVE_METADATA`). The media files are served with the MIME type of their
  extension, and the status of `pipe=status()` applies to the media resources.
- The backend also parses the byte streams of the [Media Source Extensions](browsing-context.md#media-source-extensions)
  (`Runner/WptMediaContainer.Segments.cs`, `MediaBackend.CreateSegmentParser()`): the ISO BMFF streams (an initialization
  segment is a `moov` box; a media segment is a `moof` box and its `mdat` box, whose samples give the decode timestamps,
  the composition offsets, the durations and the sync sample flags; the empty edits and the media time of the edit list
  shift the timestamps; an unknown top-level box is an error), the WebM streams (the EBML header up to the `Tracks`
  element, then the clusters: the duration of a block without `BlockDuration` is the time to the next block of its
  track; a block of an unknown track is an error) and the MPEG audio and ADTS streams (each frame is a media segment, and
  the timestamps are generated). The frames of the tracks of an MP4 fragment are interleaved by presentation time.
  `MediaSource.isTypeSupported()` requires the codecs of the MP4 and WebM types, as Chromium does. The tests that read
  the pixels of the frames (`mediasource-correct-frames*.html`) fail: nothing is decoded.
- The WebSocket server of wptserve (pywebsocket3) is emulated by `Runner/WptWebSocketServer.cs`: the connections of
  `DocumentLoader.ConnectWebSocketAsync` are in-memory pipes, on the `ws` port (`8888`) for the `http:` URLs and on the
  `wss` and `h2` ports (`8889`, `9000`) for the `https:` URLs. The path of the URL selects the handler of
  `websockets/handlers` (`/echo` is `echo_wsh.py`), whose functions (`web_socket_do_extra_handshake`,
  `web_socket_transfer_data`, `web_socket_passive_closing_handshake`) are ported to .NET in
  `Runner/WptWebSocketHandlers.cs` with the API of pywebsocket (`receive_message()`, `send_message()`,
  `close_connection()`, `request.connection.write()`...). The server validates the handshake and selects the subprotocol as
  pywebsocket does (a request that does not match is a `400` response), declines the extensions, and reads and writes the
  frames itself, so a handler can write raw bytes (e.g. an invalid handshake response). The browser reads the response
  from the connection (an invalid HTTP/1.1 response is a network error), and its cookies are stored by a task of the event
  loop. The handlers run on the thread pool: the runner does not advance the virtual clock while a connection is busy
  (bytes are waiting to be read, a side has read bytes and has not asked for more, or the server sent a Close frame and
  the connection is not closed yet); `time.sleep()` uses the virtual clock. The handlers of `msg_channel_wsh.py` and
  `stash_responder_blocking_wsh.py` are not ported.
- The hosts of the configuration of wptserve (`web-platform.test`, its subdomains `www`, `www1`... and their products of
  two labels such as `www2.www`, and `not-web-platform.test` and its subdomains, on any port) serve the files of the test suite, so that the tests can create cross-origin
  iframes. The tests whose file name or variant (`?wpt_flags=https`) has the `https` flag are loaded from
  `https://web-platform.test:8443/`, the ones whose variant has the `h2` flag (`?wpt_flags=h2`) from
  `https://web-platform.test:9000/` (the HTTP/2 server, which serves the same responses), the other ones from
  `http://web-platform.test:8000/`. The `.mjs` files are JavaScript, and a navigation to a missing file gets
  the 404 response of wptserve (a document with the origin of its URL), not a network error. The `nonexistent` subdomain (`{{domains[nonexistent]}}`) does not resolve: its requests are network errors. The responses have the headers of the `.headers` files (with the substitutions for the `.sub.headers` files) and of `pipe=header()`: the library checks the
  `Access-Control-Allow-Origin` header of the cross-origin requests in the CORS mode (a script with a `crossorigin`
  attribute, a module script, `fetch()`, `XMLHttpRequest`), and performs the CORS-preflight requests of `fetch()` and
  `XMLHttpRequest` (an image with a `crossorigin` attribute from another origin is a network error). As the other
  fetches, the requests of `fetch()` and `XMLHttpRequest` block the mixed content (an `http:` URL from an `https:` test page
  is a network error). The
  `.sub.` files and the `pipe=sub` responses get the substitutions of wptserve (`Runner/WptServer.cs`:
  `{{host}}`, `{{domains[...]}}`, `{{hosts[...][...]}}`, `{{ports[...][...]}}`, `{{GET[...]}}`, `{{location[...]}}`,
  `{{uuid()}}`, and the variables: `{{$id:uuid()}}` sets the variable `id`, `{{$id}}` is its value, in the same file). As in wptserve, the values are HTML-escaped by `pipe=sub` and in the `.sub.` markup files (`.html`,
  `.svg`...), not by `pipe=sub(none)`, in the other `.sub.` files (e.g. scripts) and in the `.sub.headers` files.
- The global object of each realm is the library's `Window`. Each window (the test page and its iframes) has its own
  Jint realm (global object, intrinsics, interface objects), so that cross-realm tests behave as in a browser. The
  interface objects of a realm are created on first use.
- The JavaScript bindings are the shared WebIDL bindings of the library (`src/Meziantou.Framework.WebPlatform/Scripting/WebIdl`,
  generated from the webref IDL and the .NET members annotated with `[IdlMember]` by `tools/Meziantou.Framework.WebPlatform.SpecGen`), which follow the WebIDL
  conversion rules (overloads, dictionaries, unions, callbacks, legacy platform objects...). The runner only adds the harness: the results of testharness.js, the loader of
  the files of the test suite and of the Python handlers, the pipes, the Content-Security-Policy headers, the WebDriver BiDi `log.entryAdded` events of the console messages (`test_driver.bidi.log.entry_added`),
  and a `WebAssembly.Memory` that only creates the `SharedArrayBuffer` of `common/sab.js` (with the intrinsic constructor,
  as the global object has no `SharedArrayBuffer` property when the test is not cross-origin isolated). `WebAssembly.compile()` returns
  a `WebAssembly.Module` without code, which the runner's wrapper of `postMessage()` delivers to the windows of the same agent cluster (a
  `messageerror` event is fired in the windows of another agent cluster, as for a real module); the other messages are
  posted by the library (see [Cross-document messaging](browsing-context.md#cross-document-messaging)). The `idlharness.js` tests
  read the IDL of the pinned `interfaces` directory, and `/resources/WebIDLParser.js` (a symbolic link in the WPT
  repository, which is not materialized) is served from `resources/webidl2/lib/webidl2.js`. A platform object assigned to an IDL attribute
  whose .NET property cannot represent it (e.g. a `TrustedHTML` assigned to `innerHTML`) is given to the .NET method
  annotated with the name of the attribute (`SetInnerHtml`). The `innerText` and `textContent` attributes that the Trusted
  Types specification adds to `HTMLScriptElement` are added to the IDL, as webref does not include them. `ShadowRoot.getSelection()`
  (implemented by Blink and WebKit, and used by the `shadow-dom` tests) is added to the IDL as well, and so are
  `Element.matchContainer()` and `ContainerQueryList` (proposed in a CSS Conditional 5 pull request, and used by the
  tentative `css-conditional` tests). `SVGPathSegment` is a
  dictionary (`type`, `sequence<unrestricted float> values`), as in the browsers, instead of the interface of the SVG Paths
  draft, so that `setPathData()` accepts plain objects.
- The custom elements defined by the tests (`customElements.define(name, class)`) are .NET custom element constructors
  that construct the JavaScript class: see [custom-elements.md](custom-elements.md#javascript-custom-elements). The
  bindings implement the HTML element constructors (`super()` in a custom element class) and push an element queue for the
  members decorated with `[CEReactions]`.
- The timers of the library (`setTimeout()`, `requestAnimationFrame()`, `performance.now()`...) use a virtual clock
  (`FakeTimeProvider`), which the runner advances to the next timer when the event loop is idle, so the tests run as fast
  as possible. Each read of the clock advances it by 100 µs (the resolution of `performance.now()`), so time passes while a
  script runs, and the times of the performance timeline (the steps of a navigation or of a fetch, see
  [Performance timeline](browsing-context.md#performance-timeline)) differ. `Date` uses the same clock (Jint's time system), in UTC. Microtasks of the DOM and promise jobs share Jint's job queue.
- Content Security Policy is partially implemented: the library applies the fetch, navigation and document directives (see
  [Content security policy](browsing-context.md#content-security-policy)), the `upgrade-insecure-requests` directive and
  the Trusted Types directives (see
  [trusted-types.md](trusted-types.md)) of the `Content-Security-Policy` and `Content-Security-Policy-Report-Only`
  headers (the `.headers` files and the `pipe=header()` query parameter) and of the
  `<meta http-equiv="Content-Security-Policy">` elements, and reports their violations (`securitypolicyviolation` events
  and `report-uri` reports, see [Violation reports](browsing-context.md#violation-reports), and the `report-to` reports
  of the [Reporting API](browsing-context.md#reporting-api)).
  The `content-security-policy` directory is run by one group per subdirectory (`ContentSecurityPolicyImgSrc`...).
  `reporting/resources/report.py`, the collector of the reports, is ported (`WptPythonHandlers.ContentSecurityPolicy.cs`):
  a POST request records the report (with the `Content-Type` and the cookies of the request), and a GET request returns
  the recorded reports, waiting for them as the Python handler does (it polls the stash every 0.5 second of the virtual
  clock until its `timeout` parameter elapses, see `WptHandlerResponse.RetryAfter`): the cookies are recorded as wptserve
  formats them (`[name=value]`). The collector of the COEP tests (`html/cross-origin-embedder-policy/resources/report.py`)
  is ported too (`WptPythonHandlers.CrossOriginIsolation.cs`). The `reporting`, `deprecation-reporting` and
  `intervention-reporting` directories are run. `http://127.0.0.1:8000/`, the loopback address the servers listen on, is
  served as the other hosts (e.g. the loopback reporting endpoints).
- The [test driver](https://web-platform-tests.org/writing-tests/testdriver.html) is implemented by the runner
  (`Runner/testdriver-vendor.js` and `Runner/WptTestDriver.cs`, served as `/resources/testdriver-vendor.js` and, as
  wptrunner does with its own implementation, appended to `/resources/testdriver.js`: the frames of a test that only load
  `testdriver.js` and call `test_driver.set_test_context()` use the test driver too). The pointer
  and wheel input is injected in the input devices of the top-level browsing context (`BrowsingContext.Input`, see
  [User input](browsing-context.md#user-input)), which dispatch the trusted events and run the default actions as for the
  input of a user; the runner only parses the WebDriver actions:
  - `click()` moves the mouse to the in-view center point of the element, then presses and releases its primary button
    (when the hit testing finds an element outside of the element at that point, the events of the click are dispatched to
    the element instead, as the interactability checks of WebDriver are not implemented; the click of a navigable
    container clicks in its document). Its promise resolves after the next rendering update (e.g. once the focus fixup rule
    ran), as the click of WebDriver.
  - `action_sequence()` performs the [actions](https://w3c.github.io/webdriver/#actions) tick by tick, in the order of the
    input sources, from tasks of the event loop (no script is running, so the microtasks run after each event listener, as
    for the input of a user). A tick lasts the longest duration of its actions on the clock of the runner (the `pause`
    actions, the `duration` of the moves and of the scrolls), and a `pointerMove` that has a duration moves the pointer
    along the line to its target, a step per frame (16 ms). The sequence starts 1 ms after the command, once the tasks
    that are already queued ran (e.g. the load of an image), as a command sent by a WebDriver client.
  - The pointer sources: the `mouse` sources are the mouse of the user agent (they share its position, its buttons and its
    pointer id, as in Chrome), and each `pen` and `touch` source is a pointer of its own (several touch sources are a
    multi-touch gesture). `pointerMove`, `pointerDown`, `pointerUp` and `pointerCancel` are supported with the pointer
    parameters (`width`, `height`, `pressure`, `tangentialPressure`, `tiltX`, `tiltY`, `twist`, `altitudeAngle`,
    `azimuthAngle`). The origin of a move is the viewport of the browsing context of the actions (the `context` argument,
    e.g. the window of an iframe: the positions are mapped to the top-level browsing context), the position of the
    pointer, or an element: its [in-view center point](https://w3c.github.io/webdriver/#dfn-in-view-center-point), the
    center of the part of its first box (`getClientRects()`) that is in the viewport of its document. An element that is
    entirely out of the viewport is scrolled into view first (WebDriver fails with "move target out of bounds"; the tests
    that pass in the browsers rely on the size of their window).
  - The wheel sources: a `scroll` action rotates the wheel at its position (`UserInput.Wheel()`). As in the browsers, the
    scrolls of the user are animated (the runner sets `ViewportOptions.SmoothUserScrolling`): the scrolls of the keys are
    smooth scrolls advanced by the rendering updates on the clock of the runner, and a wheel gesture ends (it snaps and
    fires `scrollend`) 100 ms after its last wheel event, so the `scroll` actions of a sequence separated by short pauses
    are one gesture and one wheel event transaction (their positions are the same), and the actions of two sequences at
    different positions are two transactions.
  - The key sources: `keyDown` and `keyUp` dispatch the keyboard events to the focused element; the pressed modifier keys
    are the modifiers of the pointer and wheel events (`UserInput.Modifiers`). The `keyUp` of a key that is not pressed
    does nothing.
  - As the runner of WPT, each action sequence of a test but the first one starts with the
    [release actions](https://w3c.github.io/webdriver/#release-actions) of WebDriver: the keys and the buttons of the
    pointers that the previous action sequences left pressed are released (e.g. a sequence that ends with a `pointerDown`
    is followed by a `pointerup` event when the next sequence starts).
  - The click count is counted within an action sequence: the presses of the same button at the same position and on the
    same element are a multiple click (the `detail` of `mousedown`, `mouseup`, `click` and `auxclick`), unless more than
    500 ms separate them.

  `send_keys()` dispatches the keyboard events of the keys (and inserts the characters in a text control or in editable
  content). The keyboard events are trusted, so the library runs the default actions of the user agent (see
  [Focus](browsing-context.md#focus)): `send_keys()` focuses the element (unless it is focused), the keyboard events are
  dispatched to the focused element, the characters are inserted at the caret of a text control by
  `Editing.UserEditTextControl()` (`send_keys()` first moves the caret to the end of the value of a control that was not
  focused, or after the content of an editing host that was not focused and did not contain the selection, as WebDriver
  specifies; the edits are committed when the control loses the focus, for `:user-valid` and
  `:user-invalid`), `Backspace` and `Delete` delete in it, the keys pressed in an editable
  element run the editing commands (the characters `insertText`, `Enter` `insertParagraph`, or `insertLineBreak` with
  `Shift`, `Backspace` `delete` and `Delete` `forwardDelete`, see [Editing](browsing-context.md#editing)), `Tab` moves the
  focus and `Escape` is a close request (see [Close watchers](browsing-context.md#close-watchers)), the arrow keys, `PageUp`, `PageDown`, `Home`, `End` and the space
  key scroll, and Control or Meta with `C`, `X` and `V` run the copy, cut and paste actions (see
  [Clipboard](browsing-context.md#clipboard)). Control or Alt with Backspace or Delete deletes a word (`deleteWordBackward`, `deleteWordForward`), and the up and down
  arrow keys step a number control. A key pressed with Control or Meta types no character (no `keypress`): the library
  runs the editing shortcuts (Select All, Undo, Redo, bold, italic, underline) as default actions of `keydown`. The space
  key types no character in a focused button or `summary` element, which it activates, even when the element is editable. A letter
  key pressed with Shift has the uppercase letter as its key value. `test_driver.bless()` clicks a button, which activates the window.
  `minimize_window()` hides the top-level browsing context (`BrowsingContext.IsVisible`: the runner has no window) and
  returns the rect of the window, `set_window_rect()` shows it again (the position and the size of the viewport do not
  change), and `get_window_rect()` returns `screenX`, `screenY`, `outerWidth` and `outerHeight`; the promises resolve
  once the visibility state of the documents changed. The popups that the tests open take the system focus of their
  opener (`BrowsingContext.HasSystemFocus`), as the windows of a browser.
  The runner has no window to make fullscreen: while an element is displayed fullscreen (the `fullscreen` tests, which
  activate the window with `test_driver.click()` or `bless()`), the viewport keeps its size, which is also the size of
  the screen, and holding the `Escape` key (repeated `keyDown` actions over 1.5 seconds of the clock of the runner) exits
  a fullscreen element that locks the keyboard.
  The pointer lock is always granted (the runner has no cursor), also with `unadjustedMovement`; while the mouse is
  locked, the `pointerMove` actions move the position of the input source, and the events get the movements between
  these positions.
  `get_computed_role()` and `get_computed_label()` return `Element.ComputedRole` and `Element.ComputedName` (see
  [accessibility.md](accessibility.md)). `set_permission()` sets the state of the `clipboard-read` and `clipboard-write`
  permissions (`BrowsingContextOptions.ClipboardPermission` denies the access when the state is `denied`); the other
  permissions are rejected. `generate_test_report()` generates a `test` report for the `default` endpoint in the window
  (the Generate Test Report WebDriver command, https://w3c.github.io/reporting/#generate-test-report-command).
  `get_all_cookies()` and `get_named_cookie()` return the cookies of the jar of the runner that
  the URL of the document would receive in a same-site request (the HttpOnly cookies included, serialized as WebDriver
  does), and `delete_all_cookies()` empties the jar. `bidi.log.entry_added.subscribe()` and `on()` emit the WebDriver BiDi `log.entryAdded` events
  of the messages of the `console` namespace of the test page (`BrowsingContextOptions.ConsoleMessage`), after the call
  that printed them; the console messages are also added to the log of the result.
- The test page is the active document of the browsing context of the test: when it navigates, the new document
  reports the results. The windows opened by the test (`window.open()`) share its event loop.
- The `.any.js` tests run in each of their globals (https://web-platform-tests.org/writing-tests/testharness.html#specifying-a-test-scope):
  the window (`.any.html`), the dedicated worker (`.any.worker.html`, by default with the window), the shared worker
  (`.any.sharedworker.html`) and their module variants; the `.worker.js` tests run in a dedicated worker. The page of a
  worker variant runs the tests with `fetch_tests_from_worker()`, and the runner serves the wrapper of the worker
  (`.any.worker.js`, `.any.worker-module.js`) as wptserve does (the generated pages of the `.any.js` tests have the
  `<meta name=timeout content=long>` of `// META: timeout=long`, and the generated pages and wrappers have the headers of the `.headers` files of the test file, e.g. the `Cross-Origin-Opener-Policy` of a cross-origin isolated test). The id of a worker variant in the expectations is the path
  of the test followed by `#` and the name of the global (e.g. `url/url-origin.any.js#dedicatedworker`). The service
  worker variants (`.any.serviceworker.html`, over HTTPS) are only run for the tests of `service-workers`: the page
  registers the wrapper for a scope without clients and runs the tests of `registration.installing`, as wptserve does.
  The ShadowRealms are not run. The workers run on their own threads: the runner does not advance the
  virtual clock while a worker is running a task (`DomEventLoop.HasRunningWorkers`), except when the task waits for a
  synchronous operation (e.g. a synchronous `XMLHttpRequest` or `importScripts()`), nor when a worker ran since the next
  timer was computed (`DomEventLoop.WorkerActivity`).
- Tests that need what the library does not provide (e.g. the pinch zoom, the other commands
  of the test driver such as `set_window_rect()` or `minimize_window()`) fail or time out, and are recorded as such in the
  expectations.

## Test groups

See [wpt-coverage.md](wpt-coverage.md) for the coverage of the test suite per section, and the page of each section
(`docs/wpt-coverage/`) for the result of each test and the reason why it does not pass. The tentative tests (`.tentative.`
in the file name or a `tentative` directory: experimental features that no specification requires yet) are reported
separately from the stable tests. The pages are generated from the
[WPT manifest](https://github.com/web-platform-tests/wpt/releases) of the pinned revision, the expectations and the
reasons (see [Reasons](#reasons)): regenerate them after updating the expectations (CI fails when they are out of date or
when a test that does not pass has no reason).

```bash
dotnet run --project tools/Meziantou.Framework.WebPlatform.SpecUpdate -- wpt-coverage
```


The pinned WPT paths are listed in `tests/Meziantou.Framework.WebPlatform.SpecData/sources.json`. Each group of `WptTests` runs the tests of a directory, e.g.
`dom`, `css/selectors`, `html/semantics/forms`. The `CssParsing` group runs the `css/*/parsing` directories (except
`css/selectors/parsing`, which is part of `CssSelectors`): the parsing and serialization of the values of the CSS
properties (`*-valid.html`, `*-invalid.html`), their computed values (`*-computed.html`) and the shorthands. The
`HtmlSemantics` group only runs the files at the root of `html/semantics` (`interfaces.html`,
`rellist-feature-detection.html`): its subdirectories are run by their own groups. The tests run
in parallel with the server garbage collector (the workstation GC pauses all the tests when one of them collects).

## CSS directories

The `css/*/parsing` directories are fetched for every CSS specification (the `CssParsing` group). The whole directory of
most specifications is fetched too (`tests/Meziantou.Framework.WebPlatform.SpecData/sources.json`): its testharness.js tests are run by its own group (e.g.
`CssImages` runs `css/css-images` without its `parsing` directory) and its reftests by `WptReftests`. The tests that
need what the library does not have stay in the expectations as failures, with their reasons. The following tests are not
run (see the disabled tests of [wpt-coverage.md](wpt-coverage.md) and their reasons):

- The specifications that are not implemented: `css-layout-api`, `css-paint-api`,
  `css-navigation`, `css-image-animation`, `css-exclusions`, `css-device-adapt`.

## Forced colors mode

The `forced-colors-mode` tests need a browser in forced colors mode: the WPT infrastructure has no way to enable it (the
tests fail in every browser on wpt.fyi), and Chromium runs them in a virtual test suite with the `--force-high-contrast`
flag, where the system colors keep their default values. The runner does the same: the pages of the `forced-colors-mode`
directory (the tests and their references, `WptTestRunner.GetViewport`) are loaded with `ViewportOptions.ForcedColors`
and the default palette (`ForcedColorsTheme.Light`: the system colors of the light color scheme, which the tests expect,
e.g. a yellow `Mark` and a `rgb(0, 0, 238)` `LinkText`). The other pages are not in forced colors mode, so a reference of
these tests must be in the directory (they all are).

## Reftests

The [reftests](https://web-platform-tests.org/writing-tests/reftests.html) compare the rendering of a page with the
rendering of its references. `WptReftests` runs the reftests of the fetched directories (`Runner/WptReftest.cs`,
`Runner/WptReftestRunner.cs`, `Runner/WptTestRunner.Reftest.cs`):

- **Discovery**: the `reftest` and `print-reftest` items of the manifest of the pinned revision (`tests/Meziantou.Framework.WebPlatform.External/wpt/MANIFEST.json.gz`) whose
  file is fetched, except the ones of `wpt-skip.json`: each variant (`?query`) is a test. The visual
  tests are another type of the manifest: they are not run. The directories that only contain print reftests are fetched
  too (`css/printing`, `svg/print`). The groups split the reftests by area (`Css2`, `CssFlexbox`,
  `CssGrid`, `Css`, `Html`, `Svg`, `MathML`, `Other`), so a filtered run can select one; fetching a directory
  (`tests/Meziantou.Framework.WebPlatform.SpecData/sources.json`) is enough to run its reftests. `fetch-tests` also fetches the references of the reftests of the
  fetched directories that are outside of them (e.g. the green squares of `css/reference`, about 25 files).
- **Rendering**: the test and each reference are loaded as a test page (same server emulation, scripts, test driver and
  virtual clock) in a top-level browsing context of 800x600 CSS px (the default viewport of the reftests) whose
  `RenderingBackend` is an `ImagingRenderingBackend` shared by all the reftests (the WPT project references the Imaging
  project; the core library does not). The screenshot is taken as wptrunner does (`test-wait.js`): after the `load` event and
  the web fonts (`document.fonts.ready`), and two animation frames; when the root element has the `reftest-wait` (or
  `test-wait`) class, `TestRendered` is fired at it, the runner waits until the class is removed, then for two animation
  frames again (no frame is awaited for a document whose rendering is suppressed by a [view transition](view-transitions.md),
  i.e. while its update callback runs: it runs no animation frame callback, and the screenshot shows the frame of the old
  state). The tasks that are queued when the page is ready run before the screenshot, without advancing the clock
  (e.g. the response of an image that the last update of the rendering fetched). A page that is not ready after 10 s of the virtual clock (60 s with `<meta name=timeout content=long>`) is a
  `TIMEOUT`; a page that cannot be loaded or rendered is an `ERROR`. The screenshot is `BrowsingContext.TakeScreenshot()` at
  a device pixel ratio of 1: the whole viewport (800x600), with its classic [scrollbars](rendering.md#scrollbars), which
  are painted and interactive as the tests expect (the defaults of `ViewportOptions`). The
  [caret](rendering.md#caret) is painted and does not blink (the default of `ViewportOptions.CaretBlinkInterval`), as in the
  test configurations of the browsers. The
  scripts of the pages have a recursion limit. A failed `Debug.Assert` of the library fails the page in a Debug build (as in
  the testharness.js tests): the results are the same in Debug and in Release when no assertion fails.
- **Comparison**: the RGB values of the pixels (wptrunner's `get_differences`) with the fuzziness of the manifest (the
  `<meta name=fuzzy>` elements: `maxDifference` and `totalPixels` ranges, optionally per reference). A test passes when it
  matches one of its match references (`==`) and differs from all its mismatch references (`!=`); a reference that has its
  own references passes only when its references pass too (a comparison already made in the tree is skipped, as wptrunner
  does for the cycles of references). `about:blank` references are a white page. The renderings of the references used by
  16 reftests or more (e.g. `css/reference/ref-filled-green-100px-square.xht`) are computed once and shared.
- **Print reftests** ([print reftests](https://web-platform-tests.org/writing-tests/print-reftests.html): `-print` before
  the extension of the file name, or a `print` directory): the test and its references are loaded and made ready for the
  screenshot as the other reftests (in a viewport of 800x600), then printed as wptrunner prints them to PDF (WebDriver
  `print` with pages of 5in x 3in and margins of 0.5in, the PDF pages rendered by pdf.js at 96 dpi): a `PrintSession`
  (see [Paged media](layout.md#paged-media-csslayoutpaged-layoutenginepagedcs)) with pages of 480x288 CSS px and margins
  of 48px, whose pages are rendered by `ImagingPrintExtensions` at a device pixel ratio of 1 (the `@page` rules may change
  the size of a page: its image has the size of its page box). The resources that only the print styles use are fetched,
  and the queued tasks run without advancing the clock, before the pages are rendered. The pages are compared one by one
  (as `check_pass` of wptrunner): a different number of pages is a difference, and the fuzziness applies to each page; the
  pages of `<meta name=reftest-pages>` (the `page_ranges` of the manifest, which apply to the test and its references) are
  the only ones compared. `about:blank` is a blank page.
- **Results**: the expectations of a reftest are its status (`{ "reftest": "FAIL" }`: `PASS`, `FAIL`, `ERROR`, `TIMEOUT`), in
  the same files as the testharness.js tests. The message of a failing comparison gives the number of different pixels and
  the maximum difference, and says when the differences are only on the edges of what is painted (subpixel offsets: the
  antialiased edges of the boxes and glyphs painted at fractional positions, or offsets of 1px: this is a description of the
  differences, not a diagnosis; see [Pixel snapping](rendering.md#pixel-snapping) for the rules that make a test and its
  reference paint the same pixels, e.g. the glyphs of Ahem are not antialiased). The reasons of the reftests
  that do not pass are in the `failingReftests` section of `wpt-reasons.json` (see [Reasons](#reasons)), and the coverage
  pages list the reftests in their own table.
- **Fonts**: see [Fonts](#fonts) for the fonts that the runner installs. A character without glyph in these fonts is
  painted as nothing and advances by a fixed width, so a test and its reference that generate different characters of
  a script without font still match when they have the same number of characters (e.g. the Ethiopic or the Indic digits
  of the predefined counter styles of `css/css-counter-styles`, whose representations are checked by the unit tests,
  `CounterStyleTests`).
- **Images**: `WPT_REFTEST_IMAGES=<directory>` saves the screenshots of the first failing comparison of each failing reftest
  (the test, the reference and a `diff.png` where the different pixels are red). Without it, the images of the reftests
  that are expected to pass and fail (the regressions) are saved in `TestResults/reftests`.
- **Cost**: the ~28,200 reftests of the fetched directories take about 7 minutes of CPU time (Release, measured on a busy
  machine; about 1.5 minutes on 16 cores, ~5 GB peak working set). In a full run they overlap with the testharness.js
  tests. `WPT_REFTESTS=0` skips them.

## Fonts

The library only uses its bundled fonts (see [Bundled fonts](layout.md#bundled-fonts): Ahem, the fallbacks of the generic
families for Latin, Greek and Cyrillic, and a math font). The runner installs more fonts for the pages of the tests
(`Runner/WptFonts.cs`, `BrowsingContextOptions.InstalledFonts`, an internal option): they are files of the test suites
fetched by `eng/webplatform/fetch-tests.sh`, not part of the library. A missing font file fails the tests with a message that says to
fetch them.

- **The fonts of the CSS test suite** (`fonts/CSSTest` and the kerned versions of `css/css-fonts/variations/resources`:
  `CSSTest Verify`, `CSSTest Fallback`, `CSSTest FamilyName`, `CSSTest Weights Full`...), which the tests of the font
  matching expect to be installed on the system: `font-family` and `local()` can name them (by all their localized family
  names). The first face installed with a style is the one of its family (the kerned versions are installed first, and
  `csstest-familyname.ttf` before the other faces that have the same localized family name).
- **Fallback fonts** for the scripts that the bundled fonts do not cover. Each one is only used for the characters of the
  blocks of its script that no other font supports (the last fonts of the installed font fallback, and the fonts of these
  scripts in the generic families): the default font and the text of the scripts of the bundled fonts do not change (with
  `line-height: normal`, the lines of the text of these scripts include the layout bounds of their fallback fonts).

  | Script | Font | Source | License |
  |---|---|---|---|
  | Han, kana, CJK punctuation, fullwidth forms | M+ 1p (803 KB) | WPT `fonts/mplus-1p-regular.woff` | M+ FONTS license (free) |
  | Arabic | Scheherazade (181 KB) | WPT `fonts/Scheherazade-Regular.woff` | SIL OFL 1.1 |
  | Devanagari | Noto Sans Devanagari (213 KB) | WPT `fonts/noto/NotoSansDevanagari-Regular.ttf` | SIL OFL 1.1 |
  | Thai | Noto Sans Thai (219 KB) | google/fonts `ofl/notosansthai` | SIL OFL 1.1 |
  | Lao | Noto Sans Lao (177 KB) | google/fonts `ofl/notosanslao` | SIL OFL 1.1 |
  | Tibetan | Noto Serif Tibetan (2.1 MB) | google/fonts `ofl/notoseriftibetan` | SIL OFL 1.1 |
  | Myanmar | Noto Sans Myanmar (785 KB) | google/fonts `ofl/notosansmyanmar` | SIL OFL 1.1 |
  | Hangul | Noto Sans KR (10.4 MB) | google/fonts `ofl/notosanskr` | SIL OFL 1.1 |

  The fonts of the web-platform-tests are preferred; the other ones are the `fonts` source of `tests/Meziantou.Framework.WebPlatform.SpecData/sources.json`
  (a sparse checkout of [google/fonts](https://github.com/google/fonts) pinned to a commit in `tests/Meziantou.Framework.WebPlatform.SpecData/versions.json`,
  in `tests/Meziantou.Framework.WebPlatform.External/fonts`). Their limits show in some tests: M+ 1p has no vertical metrics (its upright glyphs advance by
  the ascent and the descent in vertical text), no `halt`/`vhal` feature (its fullwidth punctuation is not trimmed by
  `text-spacing-trim`) and no language-specific glyphs; Scheherazade has no Arabic presentation forms (it is used rather
  than the Noto Naskh Arabic of WPT because it has no tabular digits: the markers of the list items use
  `font-variant-numeric: tabular-nums`, and the tests of the counter styles compare them with a text); there is one font
  per script, whatever the content language. The other scripts (Hebrew, the other Indic scripts, Ethiopic, emoji...) have no
  font.

## Expectations

`expectations/<directory>.json` lists, for each test, the harness status, the number of subtests and the subtests that
do not pass (and the status of a reftest). A test fails when a result differs from its expectations, including unexpected passes, so the files always
reflect the current state.

| Environment variable | Effect |
|---|---|
| `WPT_UPDATE_EXPECTATIONS=1` | Rewrite the expectations from the current results |
| `WPT_FILTER=dom/nodes/Node-append` | Only run the tests whose id contains the value (or one of the values separated by `\|`, e.g. `dom/nodes\|html/canvas`) |
| `WPT_REPORT=/path/report.jsonl` | Write the detailed results (including the failure messages) as JSON lines |
| `WPT_REFTESTS=0` | Do not run the reftests |
| `WPT_REFTEST_IMAGES=/path` | Save the images of the failing reftests (test, reference, differences) |

```bash
WPT_UPDATE_EXPECTATIONS=1 dotnet test --project tests/Meziantou.Framework.WebPlatform.Wpt
dotnet run --project tools/Meziantou.Framework.WebPlatform.SpecUpdate -- wpt-coverage
```

While they are updated, the files are written every second and when the process exits. The update writes a marker next
to them (`expectations/.incomplete-update-<process id>`, ignored by git), which it deletes when the process exits
normally. A marker whose process is gone is the trace of an update that was killed or canceled before the end (Ctrl+C, a
crash, the hang dump of the test platform): the expectations of the tests that did not run are not updated, so the
`WptExpectationsTests.UpdatesAreComplete` test fails, in every run, until the same update is run again (the same
`WPT_FILTER`, or no filter) or the marker is deleted.

The test platform is started with `--hangdump --hangdump-timeout 10m` (`Meziantou.NET.Sdk.Test`): when no test starts
nor completes for 10 minutes, it writes `TestResults/Meziantou.Framework.WebPlatform.Wpt_<process id>_hang.dmp` and kills
the test host (exit code 7; the tests that did not run are reported as not run). The duration of a test is bounded
(`MaxDuration`, checked by its event loop), so a hang dump means that the process itself is blocked: the stacks of the
dump tell what it waits for (`dotnet-dump analyze <dump> -c "clrstack -all"` for the managed frames,
`lldb -c <dump> -b -o "bt all"` for the native ones).

## Reasons

Every test that does not pass has a reason in the [coverage pages](wpt-coverage.md):

- The tests that are not run: the reason of `wpt-skip.json`, or else the `notRun` reason of `wpt-reasons.json` (e.g. a
  directory that is not fetched).
- The tests that are run and do not pass (a failing subtest, or a harness status other than `OK`): the `failing`
  reason of `wpt-reasons.json`, or the `failingReftests` reason for a reftest (separate from the testharness.js tests of the
  same directories, so a directory can share a reason for its reftests, e.g. a painting feature of a later issue).

A key of `wpt-reasons.json` is a test path, or a prefix followed by `*` (the longest matching key wins), so a directory can
share a reason and a test can override it. The value is an array of causes, so a test can have several:

```json
"css/css-foo/bar.html": [
  { "kind": "NOT IMPLEMENTED", "description": "the `foo` property" },
  { "kind": "REQUIRES", "features": ["layout", "user-input"], "description": "scrolling with wheel actions" }
]
```

The kinds are `BUG`, `TEST BUG` (the test is wrong and fails in every browser), `JINT` (a limitation of the JavaScript
engine), `NOT IMPLEMENTED`, `REQUIRES` (the feature or the test needs other features that are not implemented: `features`
lists them, e.g. `layout`, `rendering`, `workers`, `user-input`, `hardware`, `media`), `TEST RUNNER` (a limitation of the
runner or of the emulated WPT server), `OUT OF SCOPE` (e.g. the tests of the JavaScript engine), `FLAKY` and `PERFORMANCE`
(too slow to be run by every build). The coverage pages show them in the **Kind** and **Description** columns, and
[wpt-coverage.md](wpt-coverage.md) defines every kind and feature (`Kinds` and `Features` in `tools/Meziantou.Framework.WebPlatform.SpecUpdate/WptCoverage.cs`).
`wpt-coverage` lists the tests without reason and the invalid reasons (unknown kind or feature, missing description), so
`--check` fails, and warns about the keys that match no test that does not pass.
`WPT_REPORT` gives the failure messages needed to write a reason.

## JavaScript engine

The expectations are the results of Jint, with the WebAssembly runtime of `Meziantou.Framework.WebPlatform.Wasmtime`
(`BrowsingContextOptions.WebAssemblyRuntime`) and Jint's stack overflow guard (an unbounded recursion throws a
`RangeError`, as the WebAssembly core tests expect, instead of ending the process). `WptEngine` abstracts the engine, so
that the adapter of another engine can be validated by the same tests (use `WPT_REPORT` to compare its results).

## Skipped tests

`wpt-skip.json` lists the tests that are not run, with their reasons in the format of `wpt-reasons.json` (e.g. JavaScript
engine stress tests that are too slow in Jint and do not test the DOM: `PERFORMANCE`). Prefer expectations: a skipped test cannot detect regressions.

## IDL coverage

`IdlCoverageTests` checks that every IDL member of the in-scope specifications has a .NET member annotated with
`[IdlMember]`, or is listed in `idl-exclusions.json` with a reason. Stale exclusions fail the test.
