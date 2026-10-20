namespace Meziantou.Framework.WebPlatform.Dom;

public partial class Element
{
    // Shared by the elements without part attribute: callers must not modify the list
    private static readonly List<string> NoPartNames = [];

    /// <summary>
    /// The part names of the element (the part attribute, https://drafts.csswg.org/css-shadow-1/#part-attr): an element of a
    /// shadow tree with a part name can be styled from outside the tree with ::part().
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/css-shadow-1/#dom-element-part"/>
    [IdlMember("part", "https://drafts.csswg.org/css-shadow-1/#dom-element-part")]
    public DomTokenList Part => Html.Reflection.GetTokenList(this, "part");

    /// <summary>Returns the part name list of the element (https://drafts.csswg.org/css-shadow-1/#part-name-list).</summary>
    internal List<string> GetPartNames()
    {
        var value = GetAttributeValue("part");
        return value is null ? NoPartNames : OrderedSets.Parse(value);
    }
}
