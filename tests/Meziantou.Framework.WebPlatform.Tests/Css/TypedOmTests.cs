using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.TypedOm;
using Meziantou.Framework.WebPlatform.Dom;
using CssFactory = Meziantou.Framework.WebPlatform.Css.Om.Css;

namespace Meziantou.Framework.WebPlatform.Tests.Css;

public sealed class TypedOmTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private static HtmlElement CreateElement(string style = "")
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><div style='" + style + "'></div>");
        return (HtmlElement)document.QuerySelector("div")!;
    }

    private Element CreateRenderedElement(string html)
    {
        var document = _contexts.Create().Navigate(_ => html);
        return document.QuerySelector("#target")!;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Numeric values
    // ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void UnitValue_FactoryFunctions()
    {
        Assert.Equal("10px", CssFactory.Px(10).ToString());
        Assert.Equal("1.5", CssFactory.Number(1.5).ToString());
        Assert.Equal("50%", CssFactory.Percent(50).ToString());
        Assert.Equal("hz", CssFactory.Hz(1).Unit);
        Assert.Equal("q", CssFactory.Q(1).Unit);
        Assert.Equal("10cqi", CssFactory.Cqi(10).ToString());
    }

    [Fact]
    public void UnitValue_InvalidUnit_ThrowsTypeError() => Assert.Throws<TypeErrorException>(() => new CssUnitValue(1, "lemon"));

    [Fact]
    public void Arithmetic_SameUnit_ReturnsUnitValue()
    {
        var result = Assert.IsType<CssUnitValue>(CssFactory.Px(1).Add(CssFactory.Px(2), CssFactory.Px(3)));
        Assert.Equal(6, result.Value);
        Assert.Equal("px", result.Unit);

        var product = Assert.IsType<CssUnitValue>(CssFactory.Px(2).Mul(3, 4));
        Assert.Equal("24px", product.ToString());
    }

    [Fact]
    public void Arithmetic_DifferentUnits_ReturnsMathValue()
    {
        var sum = Assert.IsType<CssMathSum>(CssFactory.Px(1).Add(CssFactory.Em(2)));
        Assert.Equal("calc(1px + 2em)", sum.ToString());
        Assert.Equal("calc(1px + -2em)", CssFactory.Px(1).Sub(CssFactory.Em(2)).ToString());
        Assert.Equal("calc(1px / 2em)", CssFactory.Px(1).Div(CssFactory.Em(2)).ToString());
        Assert.Equal("min(1px, 2em)", CssFactory.Px(1).Min(CssFactory.Em(2)).ToString());
        Assert.Equal("clamp(1px, 2em, 3px)", new CssMathClamp(CssFactory.Px(1), CssFactory.Em(2), CssFactory.Px(3)).ToString());
    }

    [Fact]
    public void Arithmetic_IncompatibleTypes_ThrowsTypeError()
    {
        Assert.Throws<TypeErrorException>(() => CssFactory.Px(1).Add(CssFactory.S(1)));
        Assert.Throws<TypeErrorException>(() => new CssMathSum(CssFactory.Number(1), CssFactory.Px(1)));
    }

    [Fact]
    public void Arithmetic_DivisionByZero_ThrowsRangeError() => Assert.Throws<RangeErrorException>(() => CssFactory.Px(1).Div(0));

    [Fact]
    public void Type_PercentHint()
    {
        var type = new CssMathSum(CssFactory.Px(1), CssFactory.Percent(2)).Type();
        Assert.Equal(1, type.Length);
        Assert.Null(type.Percent);
        Assert.Equal(CssNumericBaseType.Length, type.PercentHint);

        var product = new CssMathProduct(CssFactory.Px(1), CssFactory.Px(1)).Type();
        Assert.Equal(2, product.Length);
    }

    [Fact]
    public void To_ConvertsCompatibleUnits()
    {
        Assert.Equal(2.54, CssFactory.In(1).To("cm").Value, 6);
        Assert.Equal(3, new CssMathSum(CssFactory.Px(1), CssFactory.Px(2)).To("px").Value);
        Assert.Throws<TypeErrorException>(() => CssFactory.Px(1).To("deg"));
        Assert.Throws<DomException>(() => CssFactory.Px(1).To("lemon"));
    }

    [Fact]
    public void ToSum_ConvertsToUnits()
    {
        var sum = new CssMathSum(CssFactory.Cm(1), CssFactory.Mm(10), CssFactory.Em(1)).ToSum("mm", "em");
        Assert.Equal("calc(20mm + 1em)", sum.ToString());
        Assert.Throws<TypeErrorException>(() => new CssMathSum(CssFactory.Px(1), CssFactory.Em(1)).ToSum("px"));
    }

    [Fact]
    public void Equals_ComparesStructure()
    {
        Assert.True(CssFactory.Px(1).NumericEquals(CssFactory.Px(1)));
        Assert.False(CssFactory.Px(1).NumericEquals(CssFactory.In(1)));
        Assert.True(new CssMathSum(1, 2).NumericEquals(new CssMathSum(1, 2)));
        Assert.False(new CssMathSum(1, 2).NumericEquals(new CssMathSum(2, 1)));
    }

    [Theory]
    [InlineData("1px", "1px")]
    [InlineData("calc(1px + 1in)", "calc(97px)")]
    [InlineData("calc(1px + 2em)", "calc(2em + 1px)")]
    [InlineData("calc(9em - 8px + 1vh)", "calc(9em - 8px + 1vh)")]
    [InlineData("calc(2 * min(10px, 20%))", "calc(2 * min(10px, 20%))")]
    [InlineData("clamp(10px, 10%, 20px)", "clamp(10px, 10%, 20px)")]
    public void NumericValue_Parse(string text, string expected) => Assert.Equal(expected, CssNumericValue.Parse(text).ToString());

    [Theory]
    [InlineData("auto")]
    [InlineData("1 2")]
    [InlineData("1xyz")]
    [InlineData("calc(sign(10em - 10rem))")]
    public void NumericValue_Parse_Invalid_ThrowsSyntaxError(string text)
    {
        var exception = Assert.Throws<DomException>(() => CssNumericValue.Parse(text));
        Assert.Equal("SyntaxError", exception.Name);
    }

    [Fact]
    public void NumericValue_Parse_Subtraction()
    {
        var sum = Assert.IsType<CssMathSum>(CssNumericValue.Parse("calc(1em - 2px)"));
        Assert.Equal(2, sum.Values.Count);
        var negate = Assert.IsType<CssMathNegate>(sum.Values[1]);
        Assert.Equal("2px", negate.Value.ToString());
    }

    // ----------------------------------------------------------------------------------------------------------------
    // CSSStyleValue.parse()
    // ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Parse_Keyword() => Assert.Equal("auto", Assert.IsType<CssKeywordValue>(CssStyleValue.Parse("width", "auto")).Value);

    [Fact]
    public void Parse_UnitlessZeroLength_IsPx()
    {
        var value = Assert.IsType<CssUnitValue>(CssStyleValue.Parse("width", "0"));
        Assert.Equal("px", value.Unit);
        Assert.Equal("number", Assert.IsType<CssUnitValue>(CssStyleValue.Parse("opacity", "0")).Unit);
    }

    [Fact]
    public void Parse_Color_IsDirectStyleValue()
    {
        var value = CssStyleValue.Parse("color", "red");
        Assert.Equal(typeof(CssStyleValue), value.GetType());
        Assert.Equal("red", value.ToString());
        Assert.IsType<CssKeywordValue>(CssStyleValue.Parse("color", "currentcolor"));
    }

    [Fact]
    public void Parse_VariableReference()
    {
        var value = Assert.IsType<CssUnparsedValue>(CssStyleValue.Parse("width", "calc(42px + var(--foo, 15em))"));
        Assert.Equal(3, value.Count);
        Assert.Equal("calc(42px + ", value[0].StringValue);
        var reference = value[1].VariableReference!;
        Assert.Equal("--foo", reference.Variable);
        Assert.Equal(" 15em", reference.Fallback![0].StringValue);
        Assert.Equal(")", value[2].StringValue);
        Assert.Equal("calc(42px + var(--foo, 15em))", value.ToString());
    }

    [Fact]
    public void Parse_Image() => Assert.IsType<CssImageValue>(CssStyleValue.Parse("background-image", "linear-gradient(red, blue)"));

    [Fact]
    public void Parse_Transform()
    {
        var value = Assert.IsType<CssTransformValue>(CssStyleValue.Parse("transform", "translate(10px, 5%) rotate(45deg) scale(2)"));
        Assert.Equal(3, value.Count);
        var translate = Assert.IsType<CssTranslate>(value[0]);
        Assert.True(translate.Is2D);
        Assert.Equal("5%", translate.Y.ToString());
        Assert.IsType<CssRotate>(value[1]);
        Assert.Equal("scale(2, 2)", value[2].ToString());
        Assert.Equal("translate(10px, 5%) rotate(45deg) scale(2, 2)", value.ToString());
    }

    [Fact]
    public void Parse_ListValuedProperty()
    {
        var values = CssStyleValue.ParseAll("transition-duration", "1s, 200ms");
        Assert.Equal(new[] { "1s", "200ms" }, values.Select(v => v.ToString()));
    }

    [Theory]
    [InlineData("width", "10deg")]
    [InlineData("lemon", "auto")]
    [InlineData("--foo", "")]
    public void Parse_Invalid_ThrowsTypeError(string property, string value) => Assert.Throws<TypeErrorException>(() => CssStyleValue.Parse(property, value));

    // ----------------------------------------------------------------------------------------------------------------
    // Transforms
    // ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Transform_ToMatrix()
    {
        var value = new CssTransformValue([new CssTranslate(CssFactory.Px(10), CssFactory.Px(20)), new CssScale(2, 3)]);
        var matrix = value.ToMatrix();
        Assert.True(matrix.Is2D);
        Assert.Equal("matrix(2, 0, 0, 3, 10, 20)", matrix.ToString());
    }

    [Fact]
    public void Transform_ToMatrix_RelativeLength_ThrowsTypeError() => Assert.Throws<TypeErrorException>(() => new CssTranslate(CssFactory.Em(1), CssFactory.Px(0)).ToMatrix());

    [Fact]
    public void Transform_Perspective()
    {
        Assert.Equal("perspective(calc(-1px))", new CssPerspective(CssFactory.Px(-1)).ToString());
        Assert.Equal("perspective(none)", new CssPerspective("none").ToString());
        Assert.Throws<TypeErrorException>(() => new CssPerspective("auto"));
    }

    // ----------------------------------------------------------------------------------------------------------------
    // StylePropertyMap
    // ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void AttributeStyleMap_Get()
    {
        var element = CreateElement("width: 10px; transition-duration: 1s, 2s; --foo: bar");
        var map = element.AttributeStyleMap;
        Assert.Same(map, element.AttributeStyleMap);
        Assert.Equal("10px", map.Get("WIDTH")!.ToString());
        Assert.Equal(2, map.GetAll("transition-duration").Count);
        Assert.IsType<CssUnparsedValue>(map.Get("--foo"));
        Assert.Null(map.Get("height"));
        Assert.True(map.Has("width"));
        Assert.Equal(3u, map.Size);
        Assert.Equal(new[] { "width", "transition-duration", "--foo" }, map.Select(entry => entry.Key));
        Assert.Throws<TypeErrorException>(() => map.Get("lemon"));
    }

    [Theory]
    [InlineData("paint-order", "fill stroke")]
    [InlineData("scroll-snap-type", "inline proximity")]
    public void AttributeStyleMap_Get_ListOfKeywordsSerializedAsOneKeyword(string property, string value)
    {
        // The value is serialized in its shortest form (a single keyword), but it is not a single keyword
        var element = CreateElement(property + ": " + value);
        var reified = element.AttributeStyleMap.Get(property)!;
        Assert.Equal(typeof(CssStyleValue), reified.GetType());

        var other = CreateElement();
        other.AttributeStyleMap.Set(property, reified);
        Assert.Equal(element.Style.GetPropertyValue(property), other.Style.GetPropertyValue(property));
        Assert.Equal(typeof(CssStyleValue), other.AttributeStyleMap.Get(property)!.GetType());
    }

    [Fact]
    public void AttributeStyleMap_Set_UpdatesTheStyleAttribute()
    {
        var element = CreateElement();
        element.AttributeStyleMap.Set("width", CssFactory.Px(10));
        Assert.Equal("width: 10px;", element.GetAttribute("style"));

        element.AttributeStyleMap.Set("height", "auto");
        Assert.Equal("auto", element.Style.Height);

        Assert.Throws<TypeErrorException>(() => element.AttributeStyleMap.Set("width", CssFactory.S(1)));
        Assert.Throws<TypeErrorException>(() => element.AttributeStyleMap.Set("width", CssFactory.Number(0)));
        Assert.Throws<TypeErrorException>(() => element.AttributeStyleMap.Set("width", CssFactory.Px(1), CssFactory.Px(2)));
    }

    [Fact]
    public void AttributeStyleMap_Set_OutOfRangeValue_IsWrappedInCalc()
    {
        var element = CreateElement();
        element.AttributeStyleMap.Set("width", CssFactory.Px(-1));
        Assert.Equal("calc(-1px)", element.Style.Width);
        var value = Assert.IsType<CssMathSum>(element.AttributeStyleMap.Get("width"));
        Assert.Equal("-1px", value.Values[0].ToString());
    }

    [Fact]
    public void AttributeStyleMap_Set_DirectStyleValue_OnlyForItsProperty()
    {
        var element = CreateElement("color: red");
        var value = element.AttributeStyleMap.Get("color")!;
        element.AttributeStyleMap.Set("background-color", "blue");
        Assert.Throws<TypeErrorException>(() => element.AttributeStyleMap.Set("background-color", value));

        var other = CreateElement();
        other.AttributeStyleMap.Set("color", value);
        Assert.Equal("red", other.Style.Color);
    }

    [Fact]
    public void AttributeStyleMap_AppendDeleteClear()
    {
        var element = CreateElement("width: 10px");
        var map = element.AttributeStyleMap;
        map.Append("transition-duration", CssFactory.S(1), "2s");
        map.Append("transition-duration", CssFactory.Ms(500));
        Assert.Equal("1s, 2s, 500ms", element.Style.TransitionDuration);
        Assert.Throws<TypeErrorException>(() => map.Append("width", CssFactory.Px(1)));

        map.Delete("transition-duration");
        Assert.False(map.Has("transition-duration"));

        map.Clear();
        Assert.Equal(0u, map.Size);
    }

    [Fact]
    public void StyleRule_StyleMap()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><style>div { margin: 1px 2px; width: 50% }</style>");
        var rule = (CssStyleRule)document.StyleSheets[0].CssRules[0];
        var map = rule.StyleMap;
        Assert.Same(map, rule.StyleMap);
        Assert.Equal(typeof(CssStyleValue), map.Get("margin")!.GetType());
        Assert.Equal("50%", map.Get("width")!.ToString());

        map.Set("width", CssFactory.Px(30));
        Assert.Equal("div { margin: 1px 2px; width: 30px; }", rule.CssText);
    }

    [Fact]
    public void ComputedStyleMap_ComputedValues()
    {
        var element = CreateRenderedElement("<div style='font-size: 10px'><div id=target style='width: 50%; height: 2em; transform: rotate(1turn); --foo: bar'></div></div>");
        var map = element.ComputedStyleMap();
        Assert.Same(map, element.ComputedStyleMap());
        Assert.Equal("50%", map.Get("width")!.ToString());
        Assert.Equal("20px", map.Get("height")!.ToString());
        Assert.Equal("rotate(360deg)", Assert.IsType<CssTransformValue>(map.Get("transform")).ToString());
        Assert.Equal("bar", map.Get("--foo")!.ToString());
        Assert.Null(map.Get("--bar"));
        Assert.Equal(typeof(CssStyleValue), map.Get("margin")!.GetType());

        element.SetAttribute("style", "width: 10px");
        Assert.Equal("10px", map.Get("width")!.ToString());
    }

    [Fact]
    public void ComputedStyleMap_DisconnectedElement_IsEmpty()
    {
        var element = CreateRenderedElement("<div id=target></div>");
        element.Remove();
        Assert.Null(element.ComputedStyleMap().Get("width"));
        Assert.Equal(0u, element.ComputedStyleMap().Size);
    }

    [Fact]
    public void AttributeStyleMap_Get_ReifiesTheValueAsSpecified()
    {
        // The serialization does not keep the percentage of an <opacity-value> nor the step-start keyword
        var element = CreateElement("opacity: 50%; animation-timing-function: step-start, ease");
        Assert.Equal("0.5", element.Style.Opacity);
        var opacity = Assert.IsType<CssUnitValue>(element.AttributeStyleMap.Get("opacity"));
        Assert.Equal(50, opacity.Value);
        Assert.Equal("percent", opacity.Unit);

        Assert.Equal("steps(1, start), ease", element.Style.AnimationTimingFunction);
        var easings = element.AttributeStyleMap.GetAll("animation-timing-function");
        Assert.Equal("step-start", Assert.IsType<CssKeywordValue>(easings[0]).Value);
        Assert.Equal("ease", Assert.IsType<CssKeywordValue>(easings[1]).Value);

        element.AttributeStyleMap.Set("opacity", CssFactory.Percent(-3.14));
        Assert.Equal("-0.0314", element.Style.Opacity);
        Assert.Equal("-3.14%", element.AttributeStyleMap.Get("opacity")!.ToString());
    }

    [Fact]
    public void AttributeStyleMap_Set_MathValue_KeepsItsStructure()
    {
        var element = CreateElement();
        element.AttributeStyleMap.Set("column-count", new CssMathSum(CssFactory.Number(2), CssFactory.Number(3)));
        Assert.Equal("calc(5)", element.Style.GetPropertyValue("column-count"));
        var sum = Assert.IsType<CssMathSum>(element.AttributeStyleMap.Get("column-count"));
        Assert.Equal(["2", "3"], sum.Values.Select(value => value.ToString()));

        // The units that are not canonical are converted: the value is simplified like a specified value
        element.AttributeStyleMap.Set("transition-delay", new CssMathSum(CssFactory.S(1), CssFactory.Ms(500)));
        Assert.Equal("calc(1.5s)", element.AttributeStyleMap.Get("transition-delay")!.ToString());

        // The same math function set as a string is simplified
        element.Style.SetProperty("column-count", "calc(2 + 3)");
        Assert.Equal("calc(5)", element.AttributeStyleMap.Get("column-count")!.ToString());
    }

    [Fact]
    public void AttributeStyleMap_Set_TypedValue_IsReifiedAsItWasSet()
    {
        // The values of the rule properties of CSS Gaps are lists: they are not reified, unless they were set as typed values
        var element = CreateElement();
        element.AttributeStyleMap.Set("column-rule-style", new CssKeywordValue("dotted"));
        Assert.Equal("dotted", Assert.IsType<CssKeywordValue>(element.AttributeStyleMap.Get("column-rule-style")).Value);

        element.Style.SetProperty("column-rule-style", "dotted");
        Assert.Equal(typeof(CssStyleValue), element.AttributeStyleMap.Get("column-rule-style")!.GetType());

        // The calc() that the serialization of a CSSPerspective adds around a relative length is not part of the value
        element.AttributeStyleMap.Set("transform", new CssTransformValue([new CssPerspective(CssFactory.Em(10))]));
        Assert.Equal("perspective(calc(10em))", element.Style.Transform);
        var transform = Assert.IsType<CssTransformValue>(element.AttributeStyleMap.Get("transform"));
        Assert.Equal("10em", Assert.IsType<CssUnitValue>(Assert.IsType<CssPerspective>(transform[0]).Length.Value).ToString());
    }

    [Fact]
    public void GridLine_IsNotANumericValue()
    {
        var element = CreateElement("grid-row-start: 3; grid-row-end: auto");
        Assert.Equal(typeof(CssStyleValue), element.AttributeStyleMap.Get("grid-row-start")!.GetType());
        Assert.Equal("auto", Assert.IsType<CssKeywordValue>(element.AttributeStyleMap.Get("grid-row-end")).Value);
        Assert.Throws<TypeErrorException>(() => element.AttributeStyleMap.Set("grid-row-start", CssFactory.Number(3)));
        element.AttributeStyleMap.Set("grid-row-start", "span 2");
        Assert.Equal("span 2", element.Style.GetPropertyValue("grid-row-start"));
    }

    [Fact]
    public void AttributeStyleMap_Set_CustomProperty_OnlyAcceptsStringsAndUnparsedValues()
    {
        var element = CreateElement();
        element.AttributeStyleMap.Set("--foo", "10px");
        element.AttributeStyleMap.Set("--foo", new CssUnparsedValue(["20px"]));
        Assert.Equal("20px", element.Style.GetPropertyValue("--foo"));
        Assert.Throws<TypeErrorException>(() => element.AttributeStyleMap.Set("--foo", CssFactory.Px(10)));
        Assert.Throws<TypeErrorException>(() => element.AttributeStyleMap.Set("--foo", new CssKeywordValue("auto")));
    }

    [Fact]
    public void ComputedStyleMap_RegisteredCustomProperty_IsReifiedAccordingToItsSyntax()
    {
        var element = CreateRenderedElement("<div id=target style='font-size: 10px; --length: 2em; --list: 1px 2em; --ident: foo; --color: red; --any: 2em'></div>");
        var document = element.NodeDocument;
        Assert.IsType<CssUnparsedValue>(element.ComputedStyleMap().Get("--length"));

        CssFactory.RegisterProperty(document, new PropertyDefinition { Name = "--length", Syntax = "<length>", Inherits = false, InitialValue = "0px" });
        CssFactory.RegisterProperty(document, new PropertyDefinition { Name = "--list", Syntax = "<length>+", Inherits = false, InitialValue = "0px" });
        CssFactory.RegisterProperty(document, new PropertyDefinition { Name = "--ident", Syntax = "foo | <length>", Inherits = false, InitialValue = "0px" });
        CssFactory.RegisterProperty(document, new PropertyDefinition { Name = "--color", Syntax = "<color>", Inherits = false, InitialValue = "black" });
        CssFactory.RegisterProperty(document, new PropertyDefinition { Name = "--any", Syntax = "*", Inherits = false });

        var map = element.ComputedStyleMap();
        Assert.Equal("20px", Assert.IsType<CssUnitValue>(map.Get("--length")).ToString());
        Assert.Equal(["1px", "20px"], map.GetAll("--list").Select(value => Assert.IsType<CssUnitValue>(value).ToString()));
        Assert.Equal("foo", Assert.IsType<CssKeywordValue>(map.Get("--ident")).Value);
        Assert.Equal(typeof(CssStyleValue), map.Get("--color")!.GetType());
        Assert.IsType<CssUnparsedValue>(map.Get("--any"));
    }

    [Fact]
    public void ComputedStyleMap_ValuesWithSeveralComponents()
    {
        var element = CreateRenderedElement("<div id=target style='background: blue; border-top-left-radius: 2px; text-overflow: clip; grid-auto-columns: calc(-5%); stroke-width: calc(2 + 3)'></div>");
        var map = element.ComputedStyleMap();

        // Every longhand of the background shorthand is serialized, as in the browsers
        Assert.Equal("rgb(0, 0, 255) none repeat scroll 0% 0% / auto padding-box border-box", map.Get("background")!.ToString());

        // The computed value of a corner radius is a pair of radii, which has no representation
        Assert.Equal(typeof(CssStyleValue), map.Get("border-top-left-radius")!.GetType());
        Assert.Equal("clip", Assert.IsType<CssKeywordValue>(map.Get("text-overflow")).Value);

        // The math functions are clamped to the range of the type, and the numbers of the strokes are lengths
        Assert.Equal("0%", map.Get("grid-auto-columns")!.ToString());
        Assert.Equal("5px", map.Get("stroke-width")!.ToString());
    }

    [Fact]
    public void Transform_Scale_SerializesBothScales()
    {
        Assert.Equal("scale(1.5, 1.5)", new CssScale(1.5, 1.5).ToString());
        Assert.Equal("scale(1.5, 2)", new CssScale(1.5, 2).ToString());
        Assert.Equal("scale3d(1, 2, 3)", new CssScale(1, 2, 3).ToString());
    }

    [Theory]
    [InlineData("flex-shrink: calc(-1)", "flex-shrink", "0")]
    [InlineData("column-count: calc(0)", "column-count", "1")]
    [InlineData("transition-duration: calc(-1s)", "transition-duration", "0s")]
    [InlineData("width: calc(-10px)", "width", "0px")]
    public void ComputedValue_MathFunctionIsClampedToTheRange(string style, string property, string expected)
    {
        var element = CreateRenderedElement("<div id=target style='" + style + "'></div>");
        Assert.Equal(expected, element.NodeDocument.DefaultView!.GetComputedStyle(element).GetPropertyValue(property));
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Colors
    // ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ColorValue_Parse()
    {
        var rgb = Assert.IsType<CssRgb>(CssColorValue.Parse("#00bfff"));
        Assert.Equal("191", ((CssNumericValue)rgb.G.Value).ToString());
        Assert.Equal("100%", ((CssNumericValue)rgb.Alpha.Value).ToString());

        var hsl = Assert.IsType<CssHsl>(CssColorValue.Parse("hsl(195 100% 50% / 0.5)"));
        Assert.Equal("195deg", ((CssNumericValue)hsl.H.Value).ToString());
        Assert.Equal("50%", ((CssNumericValue)hsl.Alpha.Value).ToString());

        Assert.Equal("canvas", Assert.IsType<CssKeywordValue>(CssColorValue.Parse("Canvas")).Value);
        Assert.Throws<DomException>(() => CssColorValue.Parse("initial"));
    }

    [Fact]
    public void ColorValue_Rectification()
    {
        var rgb = new CssRgb(0.5, CssFactory.Number(10), "none");
        Assert.Equal("50%", ((CssNumericValue)rgb.R.Value).ToString());
        Assert.Equal("none", ((CssKeywordValue)rgb.B.Value).Value);
        Assert.Throws<DomException>(() => new CssRgb(CssFactory.Deg(1), 0, 0));
        Assert.Throws<DomException>(() => rgb.Alpha = CssFactory.Number(1));
    }
}
