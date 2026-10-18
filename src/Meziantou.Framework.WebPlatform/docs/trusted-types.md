# Trusted Types

The library implements [Trusted Types](https://w3c.github.io/trusted-types/dist/spec/): `TrustedHTML`, `TrustedScript`,
`TrustedScriptURL`, `TrustedHTMLParserOptions`, `TrustedTypePolicyFactory` and `TrustedTypePolicy`, and the "get trusted
type compliant string", "get trusted type compliant input" and "get trusted type compliant parser options" algorithms at
the injection sinks of the DOM and HTML specifications.

## Enforcement

Trusted Types are enforced for a document when its
`TrustedTypesDirectives` contain the `require-trusted-types-for 'script'` directive. A document loaded in a
[browsing context](browsing-context.md) gets the Trusted Types directives of the `Content-Security-Policy` and
`Content-Security-Policy-Report-Only` headers of its response (`DocumentResource.Headers`), and of the
`<meta http-equiv="Content-Security-Policy">` elements inserted in its `head`; the `about:blank`, `about:srcdoc`, `data:`
and `blob:` documents inherit the directives of their creator, parent or initiator with their policy container (see
[Policy inheritance](browsing-context.md#policy-inheritance)). Otherwise, the list is empty: the sinks
accept strings. The fetch directives of the same policies block the requests of the subresources, and their script and
style directives block the inline scripts and styles (see [Content security policy](browsing-context.md#content-security-policy));
the other directives are not implemented.

```csharp
var document = HtmlDocumentParser.ParseDocument(html);

// The directives of a Content-Security-Policy header (the other directives are ignored)
document.TrustedTypesDirectives.Add(TrustedTypesDirectives.Parse("require-trusted-types-for 'script'; trusted-types app"));

var policy = document.TrustedTypes.CreatePolicy("app", new TrustedTypePolicyOptions
{
    CreateHtml = (input, arguments) => Sanitize(input),
});

element.InnerHtml = "<b>text</b>";                   // throws TypeErrorException
element.SetInnerHtml(policy.CreateHtml("<b>text</b>")); // ok
```

- `TrustedTypesDirectives.TrustedTypes` is the `trusted-types` directive, which restricts the names of the policies
  (`*`, `'allow-duplicates'` and `'none'` are supported).
- `TrustedTypesDirectives.ReportOnly` is the disposition of a `Content-Security-Policy-Report-Only` policy: strings are
  still given to the default policy, but nothing is blocked.
- The violations are reported as violations of the content security policy that contains the directives (see
  [Violation reports](browsing-context.md#violation-reports)): a `securitypolicyviolation` event fired at the document
  (and the `report-uri` reports of the policy), whose `effectiveDirective` is `trusted-types` and `blockedURI`
  `trusted-types-policy` for a policy creation (the `sample` is the policy name), or `require-trusted-types-for` and
  `trusted-types-sink` for a string given to a sink (the `sample` is the sink name, `|` and the first 40 characters of the
  value), including the `javascript:` URL navigations (`Location href`). Each policy reports its violation, the
  report-only ones included. The directives added to `TrustedTypesDirectives` by the application report the policy made
  of these directives.

The directives of a document apply to the objects whose relevant global object is its window: the nodes of the documents
it creates (`DOMParser`, `createHTMLDocument()`, template contents...) use the directives and the policies of the window of
the creator document. A document without a window (parsed without a [browsing context](browsing-context.md)) is its own
global object.

### Workers

Each dedicated, shared or service worker has its own trusted type policy factory (`self.trustedTypes`,
`WorkerGlobalScope.TrustedTypes`), with its own policies and its own default policy. Trusted Types are enforced in a
worker by the directives of the `Content-Security-Policy` and `Content-Security-Policy-Report-Only` headers of the
response of its script; a worker whose URL is a `data:` or `blob:` URL inherits the directives of its creator (or of the
creator of the blob URL) with its CSP list. The policies of a window do not apply to the workers it creates from an
`http(s):` URL. The violations are reported to the worker: a `securitypolicyviolation` event fired at its global object,
and the reports of the policy.

## Policies

`Window.TrustedTypes` and `WorkerGlobalScope.TrustedTypes` are the `trustedTypes` attribute of the global objects.
`Document.TrustedTypes` returns the factory of the relevant global object of a document, including the documents that
have no window.

A policy named `default` is used when a string is given to a sink while Trusted Types are enforced: its callback receives
the string, the name of the expected type and the name of the sink (e.g. `"TrustedHTML"`, `"Element innerHTML"`). The
string is rejected with a `TypeErrorException` when there is no default policy, or when the callback returns null.

## Sinks

The IDL attributes accepting a trusted type are .NET properties of type `string` (the string is checked) and a method
taking the trusted type:

| IDL | String | Trusted type |
|---|---|---|
| `Element.innerHTML`, `ShadowRoot.innerHTML` | `InnerHtml` | `SetInnerHtml(TrustedHtml)` |
| `Element.outerHTML` | `OuterHtml` | `SetOuterHtml(TrustedHtml)` |
| `HTMLIFrameElement.srcdoc` | `Srcdoc` | `SetSrcdoc(TrustedHtml)` |
| `HTMLScriptElement.text`, `textContent`, `innerText` | `Text`, `TextContent`, `InnerText` | `SetText(TrustedScript)`, `SetTextContent(TrustedScript)`, `SetInnerText(TrustedScript)` |
| `HTMLScriptElement.src` | `Src` | `SetSrc(TrustedScriptUrl)` |
| `SVGAnimatedString.baseVal` (`href` of an SVG `script`) | `BaseVal` | `SetBaseVal(TrustedScriptUrl)` |

The methods have an overload taking a trusted type: `SetAttribute`, `SetAttributeNS` (event handler content attributes,
`script` `src`, `iframe` `srcdoc`, SVG `script` `href`), `InsertAdjacentHtml`, `SetHtmlUnsafe`, the positional HTML methods
(`AppendHtmlUnsafe`, `PrependHtmlUnsafe`, `BeforeHtmlUnsafe`, `AfterHtmlUnsafe`, `ReplaceWithHtmlUnsafe`), `Document.ParseHtmlUnsafe`,
`DomParser.ParseFromString` and `Range.CreateContextualFragment`. `SetAttributeNode`, `NamedNodeMap.SetNamedItem` and the
`Attr` value setters check the value of the attribute.
The nodes of the template contents use the policies and the directives of the window of the document of the template.

### Parser options

The HTML parsing sinks also accept parser options vetted by a policy (`TrustedHTMLParserOptions`, from
https://github.com/w3c/trusted-types/pull/606 and https://github.com/whatwg/html/pull/12583):
`TrustedTypePolicy.CreateParserOptions()` calls the `createParserOptions` callback of the policy with the options (their
`runScripts`, and their `sanitizer` as a new `Sanitizer` when there is one), and returns an opaque
`TrustedHtmlParserOptions` holding the `runScripts` and a copy of the sanitizer configuration of the options returned by
the callback (a `TypeErrorException` when the policy has no `createParserOptions` callback, or when it returns null).

```csharp
var policy = document.TrustedTypes.CreatePolicy("app", new TrustedTypePolicyOptions
{
    CreateParserOptions = (options, arguments) => new SetHtmlUnsafeOptions { Sanitizer = new SanitizerConfig { RemoveElements = ["script"] } },
});

element.SetHtmlUnsafe("<b>text</b><script>...</script>", policy.CreateParserOptions()); // ok, the script is removed
```

- `SetHtmlUnsafe()`, `Document.ParseHtmlUnsafe()` and the unsafe positional methods take
  `SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions` (or `ParseHtmlUnsafeOptionsOrTrustedHtmlParserOptions`): a
  `TrustedHtmlParserOptions` makes a string compliant, and its sanitizer and `runScripts` replace the ones of the caller.
- When Trusted Types are enforced and the options are a dictionary, the `createParserOptions` callback of the default
  policy is called with the options, `"TrustedHTMLParserOptions"` and the name of the sink (for `innerHTML`, `outerHTML`,
  `insertAdjacentHTML()` and `createContextualFragment()` too, with the options of the sink: `createContextualFragment()`
  runs the scripts unless the policy disables them). The options it returns are used, whatever the options of the
  caller (the policy may enable the scripts, or remove the sanitizer), and the string is compliant without
  `createHTML`. When it returns null, the string is rejected (the violation is reported).
- Without a `createParserOptions` callback in the default policy, the string must be a `TrustedHtml` or be converted by
  `createHTML`, and the options of the caller that need vetting (`RunScripts`, or a sanitizer that is not an empty
  `SanitizerConfig`) are rejected even with a `TrustedHtml`.
- The unsafe streaming methods (`StreamAppendHtmlUnsafe`...), whose HTML is not known when they are called, require a
  `TrustedHtmlParserOptions` or a default policy with a `createParserOptions` callback.
- The parser options are not used in XML documents (`innerHTML`, `outerHTML`, `insertAdjacentHTML()` and
  `createContextualFragment()` use the XML parser, which does not sanitize), nor by `srcdoc`, `document.write()`,
  `DOMParser` and `execCommand("insertHTML")`.
- `TrustedHTMLParserOptions` is only exposed in windows (`createParserOptions()` is not defined in the workers).

As the `innerText` and `textContent` attributes of `HTMLScriptElement` are distinct from the ones of `HTMLElement` and
`Node`, `((Node)script).TextContent = text` does not check the value: the default policy is applied to the text of the
script when the script is prepared (the script does not run when the text is rejected).

`document.write()` and `document.writeln()` check their strings as `TrustedHTML` (the `Document write` and
`Document writeln` sinks), and `document.execCommand()` the value of the `insertHTML` command (the `Document execCommand`
sink, `ExecCommand(string, bool, TrustedHtml)`): the value is checked before the command is tested, so a string that is
not trusted throws even when the command is not enabled.

The sinks of the global objects, in the windows and in the workers:

| Sink | Type | Name of the sink |
|---|---|---|
| The string handlers of `setTimeout()` and `setInterval()` | `TrustedScript` | `Window setTimeout`, `Window setInterval`, `WorkerGlobalScope setTimeout`, `WorkerGlobalScope setInterval` |
| `eval()` and the `Function` constructors (Jint only) | `TrustedScript` | `eval`, `Function` |
| `importScripts()` (each URL, before the first script is fetched) | `TrustedScriptURL` | `WorkerGlobalScope importScripts` |
| `new Worker()`, `new SharedWorker()` | `TrustedScriptURL` | `Worker constructor`, `SharedWorker constructor` |
| `navigator.serviceWorker.register()` (the promise is rejected) | `TrustedScriptURL` | `ServiceWorkerContainer register` |

The string handlers of the timers are checked when the timer is created, as in the browsers and as the tests expect:
`setTimeout("code")` throws a `TypeError` when the string is rejected. The HTML Standard runs the check in the task of the
timer (https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#timer-initialisation-steps), where the
exception would only be reported.

With the Jint engine, `eval()` and the `Function` constructors check their code
(https://w3c.github.io/webappsec-csp/#can-compile-strings): the code is trusted when it is a `TrustedScript` (all the
arguments of a `Function` constructor), else the default policy is called with the `eval` or `Function` sink, and the
compilation throws an `EvalError` when it is rejected or changed. `eval()` evaluates the data of a `TrustedScript`
(HostGetCodeForEval), also without policy.
`TrustedHTML.fromLiteral()` is not implemented (it is not part of the IDL of the specification).
