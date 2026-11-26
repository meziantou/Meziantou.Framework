using System.Globalization;
using System.Text;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Fuzz;

/// <summary>
/// The target of the accessibility properties (role, accessible name and description, states) and of the accessibility tree
/// of a parsed document. The relocations of aria-owns are not computed for a document that never had an aria-owns
/// attribute: the properties are the same once a detached element of the document has one, which makes them computed from
/// a walk of the tree.
/// </summary>
public static partial class WebPlatformFuzzTargets
{
    // The relocations of aria-owns are computed for each element: the cost is quadratic in the number of elements
    private const int AccessibilityMaxLength = 2048;

    private static readonly string[] AccessibilityTokens =
    [
        " aria-owns=", " aria-labelledby=", " aria-describedby=", " aria-label=", " aria-hidden=true", " aria-checked=", " aria-level=",
        " role=", " id=", " hidden", " title=", " alt=", " for=", " style='display:none'", " style='visibility:hidden'",
        "<button>", "</button>", "<label>", "<input>", "<select><option>", "<ul><li>", "<ol>", "<table><tr><td>", "<h2>", "<img>",
        "<fieldset><legend>", "<details><summary>", "<slot>", "<template shadowrootmode=open>", "</template>",
        "list", "listitem", "button", "link", "heading", "none", "presentation", "group", "dialog", "img",
    ];

    /// <summary>Parses a document, and checks that its accessibility properties do not depend on the aria-owns fast path.</summary>
    public static void AccessibilityProperties(string input)
    {
        var document = HtmlDocumentParser.ParseDocument(input);
        var elements = document.QuerySelectorAll("*").Cast<Element>().ToList();
        var properties = elements.ConvertAll(FormatAccessibilityProperties);
        var tree = document.DocumentElement?.CaptureAccessibilityTree()?.ToAriaSnapshot();

        // A detached element does not change the tree: it only makes the relocations of aria-owns computed from the tree
        document.CreateElement("div").SetAttribute("aria-owns", "fuzz");

        for (var i = 0; i < elements.Count; i++)
        {
            FuzzAssert.Equal(properties[i], FormatAccessibilityProperties(elements[i]), "the accessibility properties computed from the tree walk of aria-owns");
        }

        FuzzAssert.Equal(tree, document.DocumentElement?.CaptureAccessibilityTree()?.ToAriaSnapshot(), "the accessibility tree computed from the tree walk of aria-owns");
    }

    private static string FormatAccessibilityProperties(Element element)
    {
        var properties = element.GetAccessibilityProperties();
        return string.Create(CultureInfo.InvariantCulture, $"{properties.Role}|{properties.Name}|{properties.Description}|{properties.IsHidden}|{properties.Checked}|{properties.Pressed}|{properties.Expanded}|{properties.Selected}|{properties.Disabled}|{properties.Level}");
    }

    /// <summary>The seeds: the documents of the accessibility unit tests, with and without aria-owns.</summary>
    private static IEnumerable<FuzzSeed> GetAccessibilitySeeds()
    {
        string[] documents =
        [
            "<button id=target aria-owns=extra>Save</button><div aria-hidden=true><span id=extra> now</span></div>",
            "<h2 id=target>Save <span id=moved>now</span></h2><div aria-owns=moved></div>",
            "<button id=target aria-owns=extra>Save</button><span id=extra hidden>now</span>",
            "<div role=list aria-owns=i2><div role=listitem>1</div></div><div id=i2 role=listitem>2</div>",
            "<div role=list aria-owns='a b'></div><div id=a>a</div><div id=b aria-owns=a>b</div>",
            "<div id=a aria-owns=b><div id=b aria-owns=a>x</div></div>",
            "<label for=target>Count <select><option selected>3</option></select></label><input id=target type=checkbox>",
            "<button id=target><span role=link aria-labelledby=img>c</span> a <span id=img>b</span></button>",
            "<ul><li>One<li>Two</ul><table><tr><th>Name<td><button aria-label='Delete'>X</button></table>",
            "<div><template shadowrootmode=open><button><slot></slot></button><span id=s aria-owns=t></span></template><span id=t>Slotted</span></div>",
            "<fieldset><legend>Options</legend><input type=radio aria-checked=mixed></fieldset><h2 aria-level=4>Title</h2>",
        ];

        for (var i = 0; i < documents.Length; i++)
        {
            yield return new FuzzSeed("accessibility-" + i.ToString(CultureInfo.InvariantCulture), Encoding.UTF8.GetBytes(documents[i]));
        }
    }
}
