---
title: Custom elements
description: Define custom elements with .NET classes, react to their lifecycle and attributes, and make them form controls.
---

A custom element is an element whose behavior you define, as `customElements.define()` does in a browser. In .NET, a
[CustomElement](cref:Meziantou.Framework.WebPlatform.Dom.CustomElement) subclass is attached to each element, and its virtual methods are the lifecycle callbacks:

<!-- ref:../../../samples/Meziantou.Framework.WebPlatform.Samples/ReadmeExamples.cs;region=WebPlatform_CustomElement;format=md-fenced-code-block;dedent=true -->
```csharp
/// <summary>The <c>x-greeting</c> element: its text greets the person named by its <c>name</c> attribute.</summary>
internal sealed class GreetingElement(HtmlElement element) : CustomElement(element)
{
    // As in a JavaScript constructor, the element must not get attributes or children in the constructor
    protected override void ConnectedCallback() => Update();

    protected override void AttributeChangedCallback(string name, string? oldValue, string? newValue, string? @namespace) => Update();

    private void Update() => Element.TextContent = "Hello " + Element.GetAttribute("name");
}
```
<!-- endref -->

Define the element in the registry of the window. The elements already in the document are upgraded, and the new ones
are created with the definition:

<!-- ref:../../../samples/Meziantou.Framework.WebPlatform.Samples/ReadmeExamples.cs;region=WebPlatform_DefineCustomElement;format=md-fenced-code-block;dedent=true -->
```csharp
using var context = new BrowsingContext();
var document = context.Navigate("https://example.com/", _ => "<x-greeting name=Ada></x-greeting>");

// The elements already in the document are upgraded when the element is defined
context.Window.CustomElements.Define("x-greeting", new CustomElementConstructor(element => new GreetingElement(element))
{
    ObservedAttributes = ["name"],
});

var greeting = (HtmlElement)document.QuerySelector("x-greeting")!;
Console.WriteLine(greeting.TextContent); // Hello Ada

greeting.SetAttribute("name", "Grace");
Console.WriteLine(greeting.TextContent); // Hello Grace
```
<!-- endref -->

## From JavaScript to .NET

| JavaScript | .NET |
| --- | --- |
| `class X extends HTMLElement` | A [CustomElement](cref:Meziantou.Framework.WebPlatform.Dom.CustomElement) created by the factory of `new CustomElementConstructor(element => ...)` |
| `this` | [CustomElement.Element](cref:Meziantou.Framework.WebPlatform.Dom.CustomElement.Element); [HtmlElement.CustomElement](cref:Meziantou.Framework.WebPlatform.Dom.HtmlElement.CustomElement) returns the [CustomElement](cref:Meziantou.Framework.WebPlatform.Dom.CustomElement) of an element |
| `connectedCallback()`, `disconnectedCallback()`, `adoptedCallback()`, `attributeChangedCallback()`, `connectedMoveCallback()` | The virtual methods of [CustomElement](cref:Meziantou.Framework.WebPlatform.Dom.CustomElement) |
| `formAssociatedCallback()`, `formResetCallback()`, `formDisabledCallback()`, `formStateRestoreCallback()` | The virtual methods of [CustomElement](cref:Meziantou.Framework.WebPlatform.Dom.CustomElement), called when [FormAssociated](cref:Meziantou.Framework.WebPlatform.Dom.CustomElementConstructor.FormAssociated) is true |
| `static observedAttributes`, `static formAssociated`, `static disabledFeatures` | [CustomElementConstructor.ObservedAttributes](cref:Meziantou.Framework.WebPlatform.Dom.CustomElementConstructor.ObservedAttributes), [FormAssociated](cref:Meziantou.Framework.WebPlatform.Dom.CustomElementConstructor.FormAssociated), [DisabledFeatures](cref:Meziantou.Framework.WebPlatform.Dom.CustomElementConstructor.DisabledFeatures) |
| `customElements.define(name, C, { extends: "button" })` | `registry.Define(name, constructor, new ElementDefinitionOptions { Extends = "button" })` |
| `customElements.whenDefined(name)` | `registry.WhenDefined(name)`, a `Task` |

The element is not a subclass of [HtmlElement](cref:Meziantou.Framework.WebPlatform.Dom.HtmlElement): it is created by the library (an [HtmlElement](cref:Meziantou.Framework.WebPlatform.Dom.HtmlElement), or the interface of the
extended element for a customized built-in element such as [HtmlButtonElement](cref:Meziantou.Framework.WebPlatform.Dom.HtmlButtonElement)), and the [CustomElement](cref:Meziantou.Framework.WebPlatform.Dom.CustomElement) is attached to
it. This keeps the identity of the elements that are upgraded after they were parsed.

## Rules of the constructor

The factory runs as the body of a JavaScript constructor after `super()`. As in JavaScript, the element must not get
attributes or children there: do this work in [ConnectedCallback](cref:Meziantou.Framework.WebPlatform.Dom.CustomElement.ConnectedCallback). `Element.AttachInternals()` returns the
[ElementInternals](cref:Meziantou.Framework.WebPlatform.Dom.ElementInternals) of the element: its form value ([SetFormValue](cref:Meziantou.Framework.WebPlatform.Dom.ElementInternals.SetFormValue)), its validity, its default accessibility semantics, and
its custom states ([States](cref:Meziantou.Framework.WebPlatform.Dom.ElementInternals.States), matched by the `:state()` pseudo-class).

## Registries

[Window.CustomElements](cref:Meziantou.Framework.WebPlatform.Dom.Window.CustomElements) is the global registry of a window: custom elements need a browsing context, or a registry given
to the parser with [HtmlParserOptions.CustomElementRegistry](cref:Meziantou.Framework.WebPlatform.HtmlParserOptions.CustomElementRegistry). Scoped registries (`new CustomElementRegistry()`) define
elements for a part of the document only. The scripts of a page can define custom elements too, with
`customElements.define()`, when the browsing context has a [script engine](../06-Scripting/01-Overview.md).
