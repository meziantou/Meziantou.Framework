using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>The select element APIs (https://html.spec.whatwg.org/multipage/form-elements.html#the-select-element).</summary>
public sealed class SelectTests
{
    private static HtmlSelectElement Parse(string html) => (HtmlSelectElement)new HtmlDocumentParser().Parse(html).QuerySelector("select")!;

    [Fact]
    public void OptionsLength_GreaterThanTheLimit_DoesNothing()
    {
        var select = Parse("<select><option>a<option>b<option>c</select>");
        select.Options.Length = 100_001;
        Assert.Equal(3u, select.Options.Length);
    }

    [Fact]
    public void OptionsLength_AppendsAndRemovesOptions()
    {
        var select = Parse("<select><option disabled>a</select>");
        select.Options.Length = 100_000;
        Assert.Equal(100_000u, select.Options.Length);
        Assert.Equal(1, select.SelectedIndex);

        select.Options.Length = 1;
        Assert.Equal(1u, select.Options.Length);
        Assert.Equal(-1, select.SelectedIndex);
    }

    [Fact]
    public void IndexedSetter_AppendsNewOptionsBeforeTheValue()
    {
        var select = Parse("<select><option>a</select>");
        var option = HtmlOptionElement.Create(select.OwnerDocument!, "d");
        select[3] = option;
        Assert.Equal(4u, select.Options.Length);
        Assert.Same(option, select.Options[3]);
        Assert.Equal(0, select.SelectedIndex);
    }

    [Fact]
    public void InnerHtml_OptionInOption_IsNotAnOptionOfTheSelect()
    {
        // Same as Chromium: the nested option is not in the list of options, so it is not selected
        var select = Parse("<select></select>");
        select.InnerHtml = "<option>X<span><option selected>Y</option></span></option>";
        Assert.Equal(1u, select.Options.Length);
        Assert.Equal(0, select.SelectedIndex);
        Assert.Equal("XY", select.Value);
    }
}
