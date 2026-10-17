# Custom elements

The library implements [custom elements](https://html.spec.whatwg.org/multipage/custom-elements.html) with .NET
definitions: `CustomElementRegistry` (global and scoped registries), upgrades, custom element reactions, `ElementInternals`,
form-associated custom elements and custom states (`:state()`). The web-platform-tests define custom elements with
JavaScript classes, through the same API (see [JavaScript custom elements](#javascript-custom-elements)).

## Defining a custom element

A custom element is defined with a `CustomElementConstructor`, which creates the `CustomElement` of each element. The
virtual methods of `CustomElement` are the lifecycle callbacks:

```csharp
sealed class Counter : CustomElement
{
    private readonly ElementInternals _internals;

    public Counter(HtmlElement element)
        : base(element)
    {
        // As in a JavaScript constructor, the element must not get attributes or children here
        _internals = element.AttachInternals();
    }

    protected override void ConnectedCallback() => Update();

    protected override void AttributeChangedCallback(string name, string? oldValue, string? newValue, string? @namespace) => Update();

    private void Update()
    {
        var count = int.TryParse(Element.GetAttribute("count"), out var value) ? value : 0;
        _internals.SetFormValue(count.ToString(CultureInfo.InvariantCulture));
        if (count == 0)
        {
            _internals.States.Add("empty");
        }
        else
        {
            _internals.States.Delete("empty");
        }
    }
}

using var context = new BrowsingContext();
var document = context.Navigate("https://example.com/", _ => html);

context.Window.CustomElements.Define("x-counter", new CustomElementConstructor(element => new Counter(element))
{
    ObservedAttributes = ["count"],
    FormAssociated = true,
});

var counter = (Counter)((HtmlElement)document.QuerySelector("x-counter")!).CustomElement!;
```

| JavaScript | .NET |
|---|---|
| `class X extends HTMLElement { constructor() { super(); ... } }` | a `CustomElement` created by the factory of `new CustomElementConstructor(element => ...)` |
| `this` | `CustomElement.Element` (`HtmlElement.CustomElement` returns the `CustomElement` of an element) |
| `connectedCallback()`, `disconnectedCallback()`, `connectedMoveCallback()`, `adoptedCallback()`, `attributeChangedCallback()` | the virtual methods of `CustomElement` (the default `ConnectedMoveCallback` calls `DisconnectedCallback` then `ConnectedCallback`, as a definition without `connectedMoveCallback`) |
| `formAssociatedCallback()`, `formResetCallback()`, `formDisabledCallback()`, `formStateRestoreCallback()` | the virtual methods of `CustomElement`, invoked when `FormAssociated` is true |
| `static observedAttributes`, `static disabledFeatures`, `static formAssociated` | `CustomElementConstructor.ObservedAttributes`, `DisabledFeatures`, `FormAssociated` (read once by `Define`) |
| `customElements.define(name, C, { extends: "button" })` | `registry.Define(name, constructor, new ElementDefinitionOptions { Extends = "button" })` |
| `customElements.whenDefined(name)` | `registry.WhenDefined(name)`: a `Task` that completes synchronously when the element is defined, like the promise is resolved by `define()` (its continuations may run during `Define`) |

### Why the element is not a subclass of HtmlElement

In a browser, a custom element is an instance of the author's class, and an upgrade changes the prototype of an element
that already exists (e.g. created by the parser before the definition). A .NET object cannot change its type, and the
identity of the element must be kept by an upgrade, so:

- The element is always created by the library: an `HtmlElement` for an autonomous custom element, or the element
  interface of the extended element for a customized built-in element (e.g. `HtmlButtonElement`).
- The constructor attaches a `CustomElement` to the element, when the element is created with the definition (e.g.
  `createElement()`, or the parser once the element is defined) or when it is upgraded. The factory runs as the body of a
  JavaScript constructor after `super()`: `CustomElement.Element` is the element being created or upgraded.

The steps of the [HTML element constructors](https://html.spec.whatwg.org/multipage/dom.html#html-element-constructors)
(the construction stack, the "already constructed" marker) are run by the library before the factory, so the rules are
the ones of JavaScript: constructing the same element again during its upgrade throws a `TypeError`, and a constructor that
adds attributes or children to an element created by `createElement()` makes it a failed `HtmlUnknownElement`. The
exceptions thrown by constructors and callbacks are reported with `ExceptionReporter.ExceptionReported`.

## Registries

A registry is either global or scoped:

- Each document of a [browsing context](browsing-context.md) is created with a global registry: `Window.CustomElements`
  and `Document.CustomElementRegistry`. It is the only kind of registry that can define customized built-in elements.
- `new CustomElementRegistry()` creates a scoped registry. `ShadowRootInit.CustomElementRegistry`,
  `ElementCreationOptions.CustomElementRegistry` and `ImportNodeOptions.CustomElementRegistry` use it for a shadow tree
  or an element, and `Initialize(root)` sets it as the registry of the nodes that have none.

The other documents (parsed by `HtmlDocumentParser` or `XmlDocumentParser`, `DomParser`, `CreateHtmlDocument()`...) have no registry, so
their custom elements stay undefined. Use `HtmlParserOptions.CustomElementRegistry` (or
`XmlParserOptions.CustomElementRegistry`) to parse a document with a registry, so the parser runs the constructors of the
defined elements, or `registry.Initialize(document)` to set the registry of an existing document and upgrade its elements:

```csharp
var registry = new CustomElementRegistry();
registry.Define("x-counter", new CustomElementConstructor(element => new Counter(element)));
var document = HtmlDocumentParser.ParseDocument(html, new HtmlParserOptions { CustomElementRegistry = registry });
```

The registries follow the [DOM integration of scoped registries](https://dom.spec.whatwg.org/#concept-create-element):
the registry of an element is set when it is created (the registry of its document, of its parent for the parser, or the
given one), cloning and adoption use the "effective global" registry of the document, and
`<template shadowrootcustomelementregistry>` creates a declarative shadow root without registry.

## Custom element reactions

The callbacks and the upgrades are [custom element reactions](https://html.spec.whatwg.org/multipage/custom-elements.html#custom-element-reactions):
they are invoked after the operation that queued them, rather than in the middle of a DOM algorithm. The members decorated
with `[CEReactions]` in the IDL push an element queue and invoke its reactions before they return.

The library does not decorate the 568 `[CEReactions]` members one by one: an operation of the library pushes an element
queue when it is not nested in another operation, and the calls to author code (the constructors and callbacks, the
event listeners, the script runner) start a new nesting level. The following operations invoke their reactions before
they return: the mutations of the tree (`AppendChild`, `InsertBefore`, `Remove`, `ReplaceChildren`, `MoveBefore`,
`TextContent`...), the changes of attributes (including the reflected IDL attributes, `ClassList`, `Dataset`...),
`CreateElement`, `CloneNode`, `ImportNode`, `AdoptNode`, `InnerHtml`, `OuterHtml`, `InsertAdjacentHtml`, `SetHtml`,
the `Range` operations, `Normalize`, `HtmlFormElement.Reset`, and the methods of `CustomElementRegistry`. A reaction
queued outside of such an operation is added to the backup element queue, and invoked by a microtask of the event loop
of the browsing context (`DomEventLoop.PerformMicrotaskCheckpoint`). The documents without a browsing context have no
event loop: such a reaction stays in the reaction queue of the element until the next reactions of the element are invoked.

The HTML and XML parsers run the constructors of the defined elements synchronously (except for fragments, e.g.
`innerHTML`, whose elements are upgraded when the operation completes), with a microtask checkpoint before each
constructor, and invoke the `connectedCallback` of each element when it is inserted.

## Element internals and form-associated custom elements

`HtmlElement.AttachInternals()` returns the `ElementInternals` of a custom element (it throws for other elements, when
the definition disables `"internals"`, or when it was already called):

- `ShadowRoot`: the shadow root of the element, including a closed one.
- `States`: the `CustomStateSet` matched by the `:state()` pseudo-class.
- The `ARIAMixin` members (`Role`, `AriaLabel`, `AriaLabelledByElements`...) are the default accessibility semantics:
  they are stored in the internal content attribute map, but nothing uses them (there is no accessibility tree).

A definition whose `FormAssociated` is true defines form-associated custom elements: the elements are listed, labelable,
submittable and resettable (`form.elements`, `fieldset.elements`, `labels`, `:disabled`, `:invalid`...), and:

- `SetFormValue(value, state)` sets the entries of the element in the form data set (a string, a `File`, or the entries
  of a `FormData`), submitted with the `name` attribute of the element.
- `SetValidity(flags, message, anchor)`, `Validity`, `ValidationMessage`, `WillValidate`, `CheckValidity()` and
  `ReportValidity()` take part in constraint validation (`ReportValidity` does not show anything).
- `Form` and `Labels` return the form owner and the labels.
- `FormAssociatedCallback` is invoked when the form owner changes, `FormDisabledCallback` when the element becomes
  disabled or enabled (its `disabled` attribute, or a disabled `fieldset` ancestor), and `FormResetCallback` when its form
  is reset. `FormStateRestoreCallback` is never invoked: there is no session history restoration or autofill.

## JavaScript custom elements

The [web-platform-tests runner](web-platform-tests.md) maps `customElements.define(name, class)` to the .NET registry: the
class is a `CustomElementConstructor` whose definition data is read from the class (the `prototype`, the lifecycle
callbacks, `observedAttributes`, `disabledFeatures`, `formAssociated`, in the order of the specification), and constructing
it calls the JavaScript constructor. The interface objects of the HTML elements implement the
[HTML element constructors](https://html.spec.whatwg.org/multipage/dom.html#html-element-constructors) (`super()` creates
the element, or returns the element being upgraded and sets its prototype). The bindings push an element queue for the
members decorated with `[CEReactions]`, following the IDL exactly.

## Not implemented

- Focus: form-associated custom elements are not focusable, as no element is.
- `formStateRestoreCallback` is never invoked (see above).
- The documents of the `iframe` `srcdoc` attribute are parsed synchronously, so their parser does not perform a microtask
  checkpoint before running a constructor when the `iframe` is inserted by a script.
- The `customelementregistry` content attribute on elements, which is not in the HTML standard yet.
- A custom element inserted in a document that has no registry (e.g. adopted by a document created by
  `createHTMLDocument()`) gets its `connectedCallback`, as in browsers and the web-platform-tests, although the insert
  algorithm of the DOM standard only enqueues it for elements that have a registry.
