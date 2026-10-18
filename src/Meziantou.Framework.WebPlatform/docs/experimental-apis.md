# Experimental APIs

Some public APIs implement tentative features of the web platform: proposals that no specification defines yet (a pull
request of a specification, an explainer, an issue), which the browsers implement behind flags or ship before the
standardization, and whose web-platform-tests are tentative (`*.tentative.*`). They may change or be removed when the
proposal changes, is merged into a specification, or is abandoned.

These APIs have the [`[Experimental]`](https://learn.microsoft.com/dotnet/api/system.diagnostics.codeanalysis.experimentalattribute)
attribute: using them from your code reports an error, with one diagnostic identifier per feature. Suppress the
identifier to use the feature:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);WEBPLATEXP0001</NoWarn>
</PropertyGroup>
```

or locally:

```csharp
#pragma warning disable WEBPLATEXP0001
element.AppendHtml("<p>Hello</p>");
#pragma warning restore WEBPLATEXP0001
```

The attribute only concerns the .NET API: the scripts use these features as any other one (some of them can be hidden from
the scripts with [`BrowsingContextOptions.Features`](browsing-context.md#disabling-features)).

The identifiers are defined in `src/Meziantou.Framework.WebPlatform/Specification/ExperimentalApis.cs`. The proposals
that add IDL to the IDL of webref (`tools/Meziantou.Framework.WebPlatform.SpecGen/IdlModel.cs`) set `ExperimentalApi` on the IDL they add, and
`SpecGen --check` fails when a .NET type or member that maps this IDL does not have the matching attribute, or when a
type or member still has the attribute after webref defines its IDL (the feature is then standard: remove the attribute and
its section below).

<a id="WEBPLATEXP0001"></a>

## WEBPLATEXP0001: HTML insertion methods

The HTML insertion methods proposed by [whatwg/html#12753](https://github.com/whatwg/html/pull/12753): `Element.AppendHtml()`,
`PrependHtml()`, `BeforeHtml()`, `AfterHtml()` and `ReplaceWithHtml()`, their `*Unsafe` variants, and the same methods of
`ShadowRoot` (`AppendHtml`, `PrependHtml`) and `CharacterData` (`BeforeHtml`, `AfterHtml`, `ReplaceWithHtml`).

<a id="WEBPLATEXP0002"></a>

## WEBPLATEXP0002: Streaming HTML insertion

The streaming HTML insertion methods proposed by [whatwg/html#12758](https://github.com/whatwg/html/pull/12758):
`HtmlStreamWriter`, and `StreamHtml()`, `StreamAppendHtml()`, `StreamPrependHtml()`, `StreamBeforeHtml()`,
`StreamAfterHtml()`, `StreamReplaceWithHtml()` and their `*Unsafe` variants on `Element`, `ShadowRoot` and `CharacterData`.

<a id="WEBPLATEXP0003"></a>

## WEBPLATEXP0003: Declarative partial updates

`WebPlatformFeatures.DeclarativePartialUpdatesEnabled`: the `buffer`, `sanitize` and `src` attributes of the template
elements ([explainer](https://github.com/WICG/declarative-partial-updates/blob/main/fragment-include-explainer.md)). The
patching of the content (`HtmlTemplateElement.HtmlFor`) is standard.

<a id="WEBPLATEXP0004"></a>

## WEBPLATEXP0004: CSS Mixins @result and @private rules

`CssMixinResultRule`, the `@result` rule that the browsers and the web-platform-tests use in the body of the mixins (the
specification no longer defines it), and `CssPrivateRule`, the `@private` rule
([w3c/csswg-drafts#14243](https://github.com/w3c/csswg-drafts/issues/14243#issuecomment-5180782117)). The other rules of
[CSS Mixins](https://drafts.csswg.org/css-mixins-1/) (`@mixin`, `@apply`, `@contents`, `@function`) are not experimental.

<a id="WEBPLATEXP0005"></a>

## WEBPLATEXP0005: Element.matchContainer()

`Element.MatchContainer()` and `ContainerQueryList`, proposed by
[w3c/csswg-drafts#13551](https://github.com/w3c/csswg-drafts/pull/13551) as an extension of CSS Conditional 5.

<a id="WEBPLATEXP0006"></a>

## WEBPLATEXP0006: Class prefix selector

`ClassPrefixSelector`: the tentative class prefix selectors of [Selectors 5](https://drafts.csswg.org/selectors-5/#class-prefix)
(`.foo-*`).

<a id="WEBPLATEXP0007"></a>

## WEBPLATEXP0007: File System proposals

The members of the File System Standard that the browsers implement before the specification:
`FileSystemHandle.Remove()` ([whatwg/fs#9](https://github.com/whatwg/fs/pull/9)), `FileSystemFileHandle.Move()`
([whatwg/fs#10](https://github.com/whatwg/fs/pull/10)), and the locking modes
([proposal](https://github.com/whatwg/fs/blob/main/proposals/MultipleReadersWriters.md)):
`FileSystemCreateWritableOptions.Mode`, `FileSystemCreateSyncAccessHandleOptions`, the
`FileSystemFileHandle.CreateSyncAccessHandle(FileSystemCreateSyncAccessHandleOptions?)` overload,
`FileSystemWritableFileStream.Mode` and `FileSystemSyncAccessHandle.Mode`.

<a id="WEBPLATEXP0008"></a>

## WEBPLATEXP0008: UI event pseudoTarget

`UIEvent.PseudoTarget` and `UIEventInit.PseudoTarget`, proposed by [w3c/uievents#413](https://github.com/w3c/uievents/pull/413).

<a id="WEBPLATEXP0009"></a>

## WEBPLATEXP0009: ShadowRoot selection and hit testing

`ShadowRoot.GetSelection()`, `ShadowRoot.ElementFromPoint()` and `ShadowRoot.ElementsFromPoint()`: defined by the obsolete
Shadow DOM specification and implemented by the browsers, but CSSOM View and the Selection API only define them for the
documents ([w3c/csswg-drafts#556](https://github.com/w3c/csswg-drafts/issues/556)).

<a id="WEBPLATEXP0010"></a>

## WEBPLATEXP0010: oncontentvisibilityautostatechange

The `OnContentVisibilityAutoStateChange` event handler of `Document`, `Window`, `HtmlElement`, `SvgElement` and
`MathMLElement`: CSS Containment defines the [`contentvisibilityautostatechange` event](https://drafts.csswg.org/css-contain-2/#content-visibility-auto-state-change),
but no specification defines its event handler, which the browsers implement.

<a id="WEBPLATEXP0011"></a>

## WEBPLATEXP0011: Error stack accessor

`IJsAdapter<TValue>.TrySetErrorStack()`, the [[Stack]] internal slot of the errors of the
[error stack accessor proposal](https://github.com/tc39/proposal-error-stack-accessor) (TC39): only the script engine
adapters implement or call it.
