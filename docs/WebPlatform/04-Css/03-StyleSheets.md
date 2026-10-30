---
title: Style sheets
description: Read, edit and create style sheets and rules with the CSSOM, and inspect their parsed selectors.
---

`Document.StyleSheets` lists the style sheets of a document: the `style` elements and the `link` elements whose style
sheet is loaded. Each `CssStyleSheet` has its rules (`CssRules`), as in a browser:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Css/CssomTests.cs;region=WebPlatform_StyleSheets;format=md-fenced-code-block;dedent=true -->
```csharp
var document = HtmlDocumentParser.ParseDocument("""
    <style>
      .button { color: white; background-color: #2563eb }
      @media (min-width: 800px) { .button { padding: 8px 16px } }
    </style>
    """);

var sheet = document.StyleSheets[0];
var button = sheet.CssRules.OfType<CssStyleRule>().Single(rule => rule.SelectorText == ".button");
Assert.Equal("rgb(37, 99, 235)", button.Style.GetPropertyValue("background-color"));

var media = sheet.CssRules.OfType<CssMediaRule>().Single();
Assert.Equal("(min-width: 800px)", media.Media.MediaText);

// Edit the rules: the documents that use the style sheet see the changes
button.Style.SetProperty("border-radius", "6px");
sheet.InsertRule(".button:hover { opacity: 0.8 }", index: 2); // at the end
Assert.Equal(".button { color: white; background-color: rgb(37, 99, 235); border-radius: 6px; }", button.CssText);

// A style sheet without document (a constructable style sheet)
var standalone = new CssStyleSheet();
standalone.ReplaceSync("p { margin: 0 } h1 { font-size: 2em }");
Assert.Equal(2, standalone.CssRules.Count);
```
<!-- endref -->

- The rules are typed: `CssStyleRule` (a selector and declarations), `CssMediaRule`, `CssImportRule`, `CssFontFaceRule`,
  `CssKeyframesRule`, `CssLayerBlockRule`, `CssContainerRule`, `CssSupportsRule`... in the [Meziantou.Framework.WebPlatform.Css.Om](cref:Meziantou.Framework.WebPlatform.Css.Om)
  namespace.
- The object model is what a browser keeps: the comments, the invalid rules and the declarations of unsupported
  properties are dropped, and the values are serialized (`#2563eb` reads as `rgb(37, 99, 235)`).
- `InsertRule` and `DeleteRule` change the rules; `Style.SetProperty` and `Style.RemoveProperty` change the declarations.
  In a browsing context, the computed styles follow.
- `new CssStyleSheet()` with `ReplaceSync` parses CSS without a document. Assign it to `Document.AdoptedStyleSheets` to
  apply it to a document.

## Inspect the selectors

The selectors of the rules are parsed: `CssStyleRule.Selectors` is a `SelectorList` of the
[Meziantou.Framework.WebPlatform.Css.Selectors](cref:Meziantou.Framework.WebPlatform.Css.Selectors) namespace. Walk it to list the classes, the IDs or the elements a style
sheet uses, or parse a selector once with `SelectorList.Parse` to match it many times:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Css/SelectorObjectModelTests.cs;region=WebPlatform_SelectorObjectModel;format=md-fenced-code-block;dedent=true -->
```csharp
var document = HtmlDocumentParser.ParseDocument("""
    <style>
      nav > a.active { color: red }
      .menu .item:hover, #main .title { font-weight: bold }
    </style>
    """);

// The class names used by the selectors of a style sheet
var classNames = new SortedSet<string>(StringComparer.Ordinal);
foreach (var rule in document.StyleSheets[0].CssRules.OfType<CssStyleRule>())
{
    foreach (var complex in rule.Selectors.Selectors)
    {
        foreach (var compound in complex.Compounds)
        {
            classNames.UnionWith(compound.Selectors.OfType<ClassSelector>().Select(selector => selector.ClassName));
        }
    }
}

Assert.Equal(["active", "item", "menu", "title"], classNames);

// Parse a selector once, then match it or compare its specificity
var selector = SelectorList.Parse("#main .title");
Assert.Equal(new Specificity(1, 1, 0), selector.Selectors[0].Specificity);
Assert.False(selector.Matches(document.DocumentElement!));
```
<!-- endref -->

| Type | Is |
| --- | --- |
| `SelectorList` | A list of `ComplexSelector` separated by commas |
| `ComplexSelector` | The `CompoundSelector` items (`Compounds`) and the `Combinators` between them |
| `CompoundSelector` | The `SimpleSelector` items: `TypeSelector`, `IdSelector`, `ClassSelector`, `AttributeSelector`, `PseudoClassSelector`, `PseudoElementSelector`... |

- Each level has its `Specificity` (`A`, `B`, `C`) and serializes with `ToString()`, as `SelectorText` does.
- The arguments of `:is()`, `:where()`, `:not()` and `:has()` are selector lists too (`PseudoClassSelector.Argument`):
  recurse into them to find every class.
- `SelectorList.Parse` throws a `DomException` (`SyntaxError`) for an invalid selector, and `TryParse` returns false. The
  parsed selectors are immutable: share them between threads.
