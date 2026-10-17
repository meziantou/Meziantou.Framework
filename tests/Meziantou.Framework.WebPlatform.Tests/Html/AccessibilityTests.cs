using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

public sealed class AccessibilityTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private Element Get(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions());
        var document = context.Navigate(_ => html);
        return document.QuerySelector("#target")!;
    }

    [Theory]
    [InlineData("<div id=target role='foo button'>x</div>", "button")]
    [InlineData("<div id=target role=img>x</div>", "image")]
    [InlineData("<div id=target role=region>x</div>", "generic")]
    [InlineData("<div id=target role='region group'>x</div>", "group")]
    [InlineData("<h2 id=target>x</h2>", "heading")]
    [InlineData("<a id=target href=#>x</a>", "link")]
    [InlineData("<a id=target>x</a>", "generic")]
    [InlineData("<header id=target>x</header>", "banner")]
    [InlineData("<article><header id=target>x</header></article>", "sectionheader")]
    [InlineData("<main><footer id=target>x</footer></main>", "sectionfooter")]
    [InlineData("<ul><li id=target>x</li></ul>", "listitem")]
    [InlineData("<ul role=none><li id=target>x</li></ul>", "none")]
    [InlineData("<select id=target><option>x</select>", "combobox")]
    [InlineData("<select id=target multiple><option>x</select>", "listbox")]
    [InlineData("<input id=target type=search>", "searchbox")]
    [InlineData("<button id=target role=none>x</button>", "button")]
    [InlineData("<img id=target alt='' src='data:,'>", "none")]
    public void ComputedRole(string html, string expected) => Assert.Equal(expected, Get(html).ComputedRole);

    [Theory]
    [InlineData("<button id=target>  Save   changes </button>", "Save changes")]
    [InlineData("<button id=target aria-label=Close>x</button>", "Close")]
    [InlineData("<span id=a>Hello</span><span id=b>World</span><div id=target role=group aria-labelledby='a b'></div>", "Hello World")]
    [InlineData("<label>Email <input id=target></label>", "Email")]
    [InlineData("<label for=target>Name</label><input id=target>", "Name")]
    [InlineData("<input id=target placeholder=Search>", "Search")]
    [InlineData("<img id=target alt=Logo src='data:,'>", "Logo")]
    [InlineData("<button id=target>a<span hidden>b</span>c</button>", "ac")]
    [InlineData("<fieldset id=target><legend>Options</legend></fieldset>", "Options")]
    [InlineData("<table id=target><caption>Prices</caption></table>", "Prices")]
    [InlineData("<div id=target title=Tip>x</div>", "Tip")]
    [InlineData("<label for=target>Count <select><option selected>3</option></select></label><input id=target type=checkbox>", "Count 3")]
    [InlineData("<label for=target>Count <span role=combobox tabindex=0>3</span></label><input id=target type=checkbox>", "Count 3")]
    [InlineData("<button id=target aria-owns=extra>Save</button><div aria-hidden=true><span id=extra> now</span></div>", "Save now")]
    [InlineData("<h2 id=target>Save <span id=moved>now</span></h2><div aria-owns=moved></div>", "Save")]
    [InlineData("<button id=target aria-owns=extra>Save</button><span id=extra hidden>now</span>", "Save")]
    [InlineData("<button id=target><span role=link aria-labelledby=img>c</span> a <span id=img>b</span></button>", "b a")]
    [InlineData("<ol><li id=item>One</li></ol><button id=target aria-labelledby=item>x</button>", "1. One")]
    [InlineData("<ul><li id=item>One</li></ul><button id=target aria-labelledby=item>x</button>", "\u2022 One")]
    [InlineData("<ol start=5><li id=item>Five</li></ol><button id=target aria-labelledby=item>x</button>", "5. Five")]
    [InlineData("<ol><li><li value=7><li id=item>Eight</li></ol><button id=target aria-labelledby=item>x</button>", "8. Eight")]
    [InlineData("<ol reversed><li id=item>Three<li>Two<li>One</li></ol><button id=target aria-labelledby=item>x</button>", "3. Three")]
    [InlineData("<ol reversed><li>Two<ol><li><li></ol><li id=item>One</li></ol><button id=target aria-labelledby=item>x</button>", "1. One")]
    [InlineData("<ol reversed start=10><li>Ten<li id=item>Nine</li></ol><button id=target aria-labelledby=item>x</button>", "9. Nine")]
    [InlineData("<ol reversed><li>Eleven<li id=item value=10>Ten<li>Nine</li></ol><button id=target aria-labelledby=item>x</button>", "10. Ten")]
    [InlineData("<ol reversed><li id=item>Eleven<li value=10>Ten<li>Nine</li></ol><button id=target aria-labelledby=item>x</button>", "11. Eleven")]
    [InlineData("<style>li::marker { content: '* ' / 'Star' }</style><ul><li id=item>One</li></ul><button id=target aria-labelledby=item>x</button>", "Star One")]
    [InlineData("<details open><summary id=target>More</summary></details>", "More")]
    [InlineData("<style>div { counter-reset: n 3 } button::before { counter-increment: n; content: '' / counter(n, upper-roman) }</style><div><button>a</button><button id=target>b</button></div>", "V b")]
    [InlineData("<div id=target role=dialog><div><h2>Title</h2></div><h3>Other</h3></div>", "Title")]
    [InlineData("<div id=target role=group><h2>Title</h2></div>", "")]
    public void ComputedName(string html, string expected) => Assert.Equal(expected, Get(html).ComputedName);
}
