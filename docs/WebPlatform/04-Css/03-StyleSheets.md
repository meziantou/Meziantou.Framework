---
title: Style sheets
description: Read, edit and create style sheets and rules with the CSSOM, and inspect their parsed selectors.
---

[Document.StyleSheets](cref:Meziantou.Framework.WebPlatform.Dom.Document.StyleSheets) lists the style sheets of a document: the `style` elements and the `link` elements whose style
sheet is loaded. Each [CssStyleSheet](cref:Meziantou.Framework.WebPlatform.Css.Om.CssStyleSheet) has its rules ([CssRules](cref:Meziantou.Framework.WebPlatform.Css.Om.CssStyleSheet.CssRules)), as in a browser:

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

- The rules are typed: [CssStyleRule](cref:Meziantou.Framework.WebPlatform.Css.Om.CssStyleRule) (a selector and declarations), [CssMediaRule](cref:Meziantou.Framework.WebPlatform.Css.Om.CssMediaRule), [CssImportRule](cref:Meziantou.Framework.WebPlatform.Css.Om.CssImportRule), [CssFontFaceRule](cref:Meziantou.Framework.WebPlatform.Css.Om.CssFontFaceRule),
  [CssKeyframesRule](cref:Meziantou.Framework.WebPlatform.Css.Om.CssKeyframesRule), [CssLayerBlockRule](cref:Meziantou.Framework.WebPlatform.Css.Om.CssLayerBlockRule), [CssContainerRule](cref:Meziantou.Framework.WebPlatform.Css.Om.CssContainerRule), [CssSupportsRule](cref:Meziantou.Framework.WebPlatform.Css.Om.CssSupportsRule)... in the [Meziantou.Framework.WebPlatform.Css.Om](cref:Meziantou.Framework.WebPlatform.Css.Om)
  namespace.
- The object model is what a browser keeps: the comments, the invalid rules and the declarations of unsupported
  properties are dropped, and the values are serialized (`#2563eb` reads as `rgb(37, 99, 235)`).
- [InsertRule](cref:Meziantou.Framework.WebPlatform.Css.Om.CssStyleSheet.InsertRule) and [DeleteRule](cref:Meziantou.Framework.WebPlatform.Css.Om.CssStyleSheet.DeleteRule) change the rules; `Style.SetProperty` and `Style.RemoveProperty` change the declarations.
  In a browsing context, the computed styles follow.
- `new CssStyleSheet()` with [ReplaceSync](cref:Meziantou.Framework.WebPlatform.Css.Om.CssStyleSheet.ReplaceSync) parses CSS without a document. Assign it to [Document.AdoptedStyleSheets](cref:Meziantou.Framework.WebPlatform.Dom.Document.AdoptedStyleSheets) to
  apply it to a document.

## Inspect the selectors

The selectors of the rules are parsed: [CssStyleRule.Selectors](cref:Meziantou.Framework.WebPlatform.Css.Om.CssStyleRule.Selectors) is a [SelectorList](cref:Meziantou.Framework.WebPlatform.Css.Selectors.SelectorList) of the
[Meziantou.Framework.WebPlatform.Css.Selectors](cref:Meziantou.Framework.WebPlatform.Css.Selectors) namespace. Walk it to list the classes, the IDs or the elements a style
sheet uses, or parse a selector once with [SelectorList.Parse](cref:Meziantou.Framework.WebPlatform.Css.Selectors.SelectorList.Parse) to match it many times:

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
| [SelectorList](cref:Meziantou.Framework.WebPlatform.Css.Selectors.SelectorList) | A list of [ComplexSelector](cref:Meziantou.Framework.WebPlatform.Css.Selectors.ComplexSelector) separated by commas |
| [ComplexSelector](cref:Meziantou.Framework.WebPlatform.Css.Selectors.ComplexSelector) | The [CompoundSelector](cref:Meziantou.Framework.WebPlatform.Css.Selectors.CompoundSelector) items ([Compounds](cref:Meziantou.Framework.WebPlatform.Css.Selectors.ComplexSelector.Compounds)) and the [Combinators](cref:Meziantou.Framework.WebPlatform.Css.Selectors.ComplexSelector.Combinators) between them |
| [CompoundSelector](cref:Meziantou.Framework.WebPlatform.Css.Selectors.CompoundSelector) | The [SimpleSelector](cref:Meziantou.Framework.WebPlatform.Css.Selectors.SimpleSelector) items: [TypeSelector](cref:Meziantou.Framework.WebPlatform.Css.Selectors.TypeSelector), [IdSelector](cref:Meziantou.Framework.WebPlatform.Css.Selectors.IdSelector), [ClassSelector](cref:Meziantou.Framework.WebPlatform.Css.Selectors.ClassSelector), [AttributeSelector](cref:Meziantou.Framework.WebPlatform.Css.Selectors.AttributeSelector), [PseudoClassSelector](cref:Meziantou.Framework.WebPlatform.Css.Selectors.PseudoClassSelector), [PseudoElementSelector](cref:Meziantou.Framework.WebPlatform.Css.Selectors.PseudoElementSelector)... |

- Each level has its [Specificity](cref:Meziantou.Framework.WebPlatform.Css.Selectors.Specificity) ([A](cref:Meziantou.Framework.WebPlatform.Css.Selectors.Specificity.A), [B](cref:Meziantou.Framework.WebPlatform.Css.Selectors.Specificity.B), [C](cref:Meziantou.Framework.WebPlatform.Css.Selectors.Specificity.C)) and serializes with `ToString()`, as [SelectorText](cref:Meziantou.Framework.WebPlatform.Css.Om.CssStyleRule.SelectorText) does.
- The arguments of `:is()`, `:where()`, `:not()` and `:has()` are selector lists too ([PseudoClassSelector.Argument](cref:Meziantou.Framework.WebPlatform.Css.Selectors.PseudoClassSelector.Argument)):
  recurse into them to find every class.
- [SelectorList.Parse](cref:Meziantou.Framework.WebPlatform.Css.Selectors.SelectorList.Parse) throws a [DomException](cref:Meziantou.Framework.WebPlatform.Dom.DomException) (`SyntaxError`) for an invalid selector, and [TryParse](cref:Meziantou.Framework.WebPlatform.Css.Selectors.SelectorList.TryParse) returns false. The
  parsed selectors are immutable: share them between threads.
