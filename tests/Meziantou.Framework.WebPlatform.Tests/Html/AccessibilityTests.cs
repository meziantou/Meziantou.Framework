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

    private HtmlElement GetBody(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions());
        return context.Navigate(_ => html).Body!;
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
    [InlineData("<div id=target popover style='display:block'>x</div>", "group")]
    [InlineData("<span id=target popover=manual style='display:block'>x</span>", "group")]
    [InlineData("<p id=target popover style='display:block'>x</p>", "paragraph")]
    [InlineData("<div id=target role=generic popover style='display:block'>x</div>", "generic")]
    [InlineData("<div id=target popover>x</div>", "none")]
    [InlineData("<button id=target hidden>x</button>", "none")]
    [InlineData("<div style='display:none'><button id=target>x</button></div>", "none")]
    [InlineData("<button id=target style='visibility:hidden'>x</button>", "button")]
    [InlineData("<button id=target aria-hidden=true>x</button>", "button")]
    [InlineData("<div style='display:contents'><button id=target>x</button></div>", "button")]
    public void ComputedRole(string html, string expected) => Assert.Equal(expected, Get(html).ComputedRole);

    [Fact]
    public void ComputedRole_ShowingPopoverIsGroup()
    {
        var popover = (HtmlElement)Get("<div id=target popover>x</div>");
        Assert.Equal("none", popover.ComputedRole);

        popover.ShowPopover();
        Assert.Equal("group", popover.ComputedRole);

        popover.HidePopover();
        Assert.Equal("none", popover.ComputedRole);
    }

    [Fact]
    public void ComputedRole_WithoutBrowsingContextIgnoresTheStyle()
    {
        var document = new DomParser().ParseFromString("<div id=target popover style='display:none'>x</div>", DomParserSupportedType.TextHtml);
        Assert.Equal("group", document.QuerySelector("#target")!.ComputedRole);
    }

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

    [Fact]
    public void Documentation_ComputedRoleAndName()
    {
        #region WebPlatform_Accessibility
        using var context = new BrowsingContext();
        var document = context.Navigate("https://example.com/", _ => """
            <label>Email <input type="email" id="email"></label>
            <button id="close" aria-label="Close"><svg></svg></button>
            <img id="logo" src="logo.png">
            """);

        // The role and the accessible name that an accessibility tree exposes, as WebDriver reports them
        var email = document.GetElementById("email")!;
        Assert.Equal(("textbox", "Email"), (email.ComputedRole, email.ComputedName));

        var close = document.GetElementById("close")!;
        Assert.Equal(("button", "Close"), (close.ComputedRole, close.ComputedName));

        // An image without alt attribute has no name: a check of generated HTML can report it
        var logo = document.GetElementById("logo")!;
        Assert.Equal(("image", ""), (logo.ComputedRole, logo.ComputedName));
        #endregion
    }

    [Theory]
    [InlineData("<input id=target type=checkbox checked>", AccessibilityCheckedState.True)]
    [InlineData("<input id=target type=checkbox>", AccessibilityCheckedState.False)]
    [InlineData("<input id=target type=radio checked>", AccessibilityCheckedState.True)]
    [InlineData("<input id=target type=checkbox role=switch>", AccessibilityCheckedState.False)]
    [InlineData("<div id=target role=checkbox>x</div>", AccessibilityCheckedState.False)]
    [InlineData("<div id=target role=checkbox aria-checked=true>x</div>", AccessibilityCheckedState.True)]
    [InlineData("<div id=target role=checkbox aria-checked=' MIXED '>x</div>", AccessibilityCheckedState.Mixed)]
    [InlineData("<div id=target role=radio aria-checked=mixed>x</div>", AccessibilityCheckedState.False)]
    [InlineData("<div id=target role=option>x</div>", null)]
    [InlineData("<div id=target role=option aria-checked=true>x</div>", AccessibilityCheckedState.True)]
    [InlineData("<button id=target aria-checked=true>x</button>", null)]
    public void GetAccessibilityProperties_Checked(string html, AccessibilityCheckedState? expected) => Assert.Equal(expected, Get(html).GetAccessibilityProperties().Checked);

    [Fact]
    public void GetAccessibilityProperties_IndeterminateCheckboxIsMixed()
    {
        var checkbox = (HtmlInputElement)Get("<input id=target type=checkbox checked>");
        checkbox.Indeterminate = true;
        Assert.Equal(AccessibilityCheckedState.Mixed, checkbox.GetAccessibilityProperties().Checked);
    }

    [Theory]
    [InlineData("<button id=target aria-pressed=true>x</button>", AccessibilityCheckedState.True)]
    [InlineData("<button id=target aria-pressed=false>x</button>", AccessibilityCheckedState.False)]
    [InlineData("<button id=target aria-pressed=mixed>x</button>", AccessibilityCheckedState.Mixed)]
    [InlineData("<button id=target>x</button>", null)]
    [InlineData("<button id=target aria-pressed=yes>x</button>", null)]
    [InlineData("<div id=target role=link aria-pressed=true>x</div>", null)]
    public void GetAccessibilityProperties_Pressed(string html, AccessibilityCheckedState? expected) => Assert.Equal(expected, Get(html).GetAccessibilityProperties().Pressed);

    [Theory]
    [InlineData("<button id=target aria-expanded=true>x</button>", true)]
    [InlineData("<button id=target aria-expanded=false>x</button>", false)]
    [InlineData("<button id=target>x</button>", null)]
    [InlineData("<div id=target role=region aria-label=r aria-expanded=true>x</div>", null)]
    public void GetAccessibilityProperties_Expanded(string html, bool? expected) => Assert.Equal(expected, Get(html).GetAccessibilityProperties().Expanded);

    [Theory]
    [InlineData("<select><option>a<option id=target selected>b</select>", true)]
    [InlineData("<select><option id=target>a<option selected>b</select>", false)]
    [InlineData("<div role=tablist><div id=target role=tab aria-selected=true>x</div></div>", true)]
    [InlineData("<div role=tablist><div id=target role=tab>x</div></div>", false)]
    [InlineData("<button id=target aria-selected=true>x</button>", null)]
    public void GetAccessibilityProperties_Selected(string html, bool? expected) => Assert.Equal(expected, Get(html).GetAccessibilityProperties().Selected);

    [Theory]
    [InlineData("<button id=target disabled>x</button>", true)]
    [InlineData("<button id=target>x</button>", false)]
    [InlineData("<fieldset disabled><input id=target></fieldset>", true)]
    [InlineData("<fieldset disabled><legend><input id=target></legend></fieldset>", false)]
    [InlineData("<div aria-disabled=true><button id=target>x</button></div>", true)]
    [InlineData("<div aria-disabled=true><div aria-disabled=false><button id=target>x</button></div></div>", false)]
    [InlineData("<div id=target role=button aria-disabled=TRUE>x</div>", true)]
    [InlineData("<div id=target aria-disabled=true>x</div>", false)]
    public void GetAccessibilityProperties_Disabled(string html, bool expected) => Assert.Equal(expected, Get(html).GetAccessibilityProperties().Disabled);

    [Theory]
    [InlineData("<h2 id=target>x</h2>", 2)]
    [InlineData("<h2 id=target aria-level=4>x</h2>", 4)]
    [InlineData("<h2 id=target aria-level=0>x</h2>", 2)]
    [InlineData("<div id=target role=heading>x</div>", 2)]
    [InlineData("<div id=target role=heading aria-level=3>x</div>", 3)]
    [InlineData("<section headingoffset=1><h1 id=target>x</h1></section>", 2)]
    [InlineData("<div headingoffset=2><h6 id=target>x</h6></div>", 8)]
    [InlineData("<div headingoffset=5><h6 id=target>x</h6></div>", 9)]
    [InlineData("<div headingoffset=2><div headingreset headingoffset=1><h1 id=target>x</h1></div></div>", 2)]
    [InlineData("<ul><li id=target aria-level=2>x</li></ul>", 2)]
    [InlineData("<ul><li id=target>x</li></ul>", null)]
    [InlineData("<button id=target aria-level=2>x</button>", null)]
    public void GetAccessibilityProperties_Level(string html, int? expected) => Assert.Equal(expected, Get(html).GetAccessibilityProperties().Level);

    [Theory]
    [InlineData("<button id=target aria-describedby='d1 d2'>x</button><p id=d1>First</p><p id=d2>second</p>", "First second")]
    [InlineData("<button id=target aria-describedby=d>x</button><p id=d hidden>Hidden help</p>", "Hidden help")]
    [InlineData("<button id=target aria-description='  Some   help '>x</button>", "Some help")]
    [InlineData("<button id=target aria-describedby=d aria-description=ignored>x</button><p id=d>Help</p>", "Help")]
    [InlineData("<button id=target title=Tip>Go</button>", "Tip")]
    [InlineData("<button id=target title=Tip></button>", "")]
    [InlineData("<button id=target>x</button>", "")]
    public void GetAccessibilityProperties_Description(string html, string expected) => Assert.Equal(expected, Get(html).GetAccessibilityProperties().Description);

    [Theory]
    [InlineData("<button id=target>x</button>", false, "button")]
    [InlineData("<button id=target style='display:none'>x</button>", true, "none")]
    [InlineData("<div aria-hidden=true><button id=target>x</button></div>", true, "button")]
    [InlineData("<button id=target style='visibility:hidden'>x</button>", true, "button")]
    public void GetAccessibilityProperties_IsHidden(string html, bool expected, string expectedRole)
    {
        var properties = Get(html).GetAccessibilityProperties();
        Assert.Equal((expected, expectedRole), (properties.IsHidden, properties.Role));
    }

    // The expected values are the aria snapshots of <body> written by Playwright 1.64.1 in Chromium
    [Theory]
    [InlineData("<h1>Title</h1><p>Some <b>bold</b> text</p>", "- heading \"Title\" [level=1]\n- paragraph: Some bold text")]
    [InlineData("<div>a</div><div>b</div>", "- text: a b")]
    [InlineData("<ul><li>One</li><li>Two</li></ul>", "- list:\n  - listitem: One\n  - listitem: Two")]
    [InlineData("<label>Email <input type=email value='a@b.c'></label>", "- text: Email\n- textbox \"Email\": a@b.c")]
    [InlineData("<input type=checkbox checked aria-label=Accept><input type=checkbox aria-label=Other>", "- checkbox \"Accept\" [checked]\n- checkbox \"Other\"")]
    [InlineData("<button aria-pressed=true>Bold</button><button aria-pressed=mixed>Mixed</button><button aria-pressed=false>Off</button>", "- button \"Bold\" [pressed]\n- button \"Mixed\" [pressed=mixed]\n- button \"Off\"")]
    [InlineData("<a href='/docs'>Docs</a>", "- link \"Docs\":\n  - /url: /docs")]
    [InlineData("<input placeholder='Search here'>", "- textbox \"Search here\"")]
    [InlineData("<div hidden>x</div><span aria-hidden=true>y</span><span style='display:none'>z</span><p style='visibility:hidden'>v<span style='visibility:visible'>w</span></p>", "")]
    [InlineData("<nav aria-label=Main><a href='#a'>A</a></nav>", "- navigation \"Main\":\n  - link \"A\":\n    - /url: \"#a\"")]
    [InlineData("<select aria-label=Pick><option>One<option selected>Two</select>", "- combobox \"Pick\":\n  - option \"One\"\n  - option \"Two\" [selected]")]
    [InlineData("<details open><summary>More</summary>Content</details>", "- group: More Content")]
    [InlineData("<div role=tablist><div role=tab aria-selected=true>A</div><div role=tab>B</div></div>", "- tablist:\n  - tab \"A\" [selected]\n  - tab \"B\"")]
    [InlineData("<img src='a.png' alt='Logo'>", "- img \"Logo\"")]
    [InlineData("<p>Line: value</p><p>123</p><p>- dash</p><p>yes</p>", "- paragraph: \"Line: value\"\n- paragraph: \"123\"\n- paragraph: \"- dash\"\n- paragraph: \"yes\"")]
    [InlineData("<button>\"quoted\"</button>", "- button \"\\\"quoted\\\"\"")]
    [InlineData("<div role=list aria-owns=i2><div role=listitem>1</div></div><div id=i2 role=listitem>2</div>", "- list:\n  - listitem: \"1\"\n  - listitem: \"2\"")]
    [InlineData("<div><template shadowrootmode=open><p>In shadow <slot></slot></p></template>slotted</div>", "- paragraph: In shadow slotted")]
    [InlineData("<style>.x::before{content:'Hi '}</style><p class=x>there</p>", "- paragraph: Hi there")]
    [InlineData("<textarea aria-label=Notes>hello</textarea>", "- textbox \"Notes\": hello")]
    [InlineData("<input type=checkbox id=c><label for=c>Remember me</label>", "- checkbox \"Remember me\"\n- text: Remember me")]
    [InlineData("<span>inline</span> and <em>emphasis</em>", "- text: inline and\n- emphasis: emphasis")]
    [InlineData("<div aria-label='Named div'>content</div>", "- text: content")]
    [InlineData("<input type=radio name=r checked aria-label=A><input type=radio name=r aria-label=B>", "- radio \"A\" [checked]\n- radio \"B\"")]
    [InlineData("<progress value=30 max=100 aria-label=Loading></progress>", "- progressbar \"Loading\"")]
    [InlineData("<a>no href</a><a href='data:text/plain,hello'>data</a>", "- text: no href\n- link \"data\":\n  - /url: data:text/plain,\u2026")]
    public void CaptureAccessibilityTree_MatchesPlaywright(string html, string expected) => Assert.Equal(expected, GetBody(html).CaptureAccessibilityTree()!.ToAriaSnapshot());

    // The names and the states are those of GetAccessibilityProperties, where Playwright computes its own
    [Theory]
    // HTML-AAM does not name the rows and the cells from their content: Playwright writes row "H", columnheader "H"...
    [InlineData("<table><tr><th>H</th></tr><tr><td>C</td></tr></table>", "- table:\n  - rowgroup:\n    - row:\n      - columnheader: H\n    - row:\n      - cell: C")]

    // An article is named by its first heading (tentative, https://github.com/w3c/aria/pull/1860): Playwright writes "- article:"
    [InlineData("<article><h2>Post</h2><p>Body</p></article>", "- article \"Post\":\n  - heading \"Post\" [level=2]\n  - paragraph: Body")]

    // A disabled fieldset is disabled (https://w3c.github.io/html-aam/#att-disabled): Playwright only checks the form controls
    [InlineData("<fieldset disabled><legend>Group</legend><button>Go</button></fieldset>", "- group \"Group\" [disabled]:\n  - text: Group\n  - button \"Go\" [disabled]")]

    // The level of a heading takes headingoffset into account: Playwright writes [level=1]
    [InlineData("<section headingoffset=1><h1>Title</h1></section>", "- heading \"Title\" [level=2]")]
    public void CaptureAccessibilityTree_DiffersFromPlaywright(string html, string expected) => Assert.Equal(expected, GetBody(html).CaptureAccessibilityTree()!.ToAriaSnapshot());

    [Fact]
    public void CaptureAccessibilityTree_LinksTheNodes()
    {
        var body = GetBody("<ul id=list><li>One <span>more</span></li></ul>");
        var root = body.CaptureAccessibilityTree()!;
        Assert.Same(body, root.Node);
        Assert.Null(root.Parent);

        var list = Assert.Single(root.Children);
        Assert.Same(body.QuerySelector("#list"), list.Node);
        Assert.Same(root, list.Parent);
        Assert.Equal("list", list.Properties.Role);

        var item = Assert.Single(list.Children);
        var text = Assert.Single(item.Children);
        Assert.Equal(("text", "One more"), (text.Properties.Role, text.Properties.Name));
        Assert.IsType<Text>(text.Node);
        Assert.Same(item, text.Parent);
        Assert.Equal("text: One more", text.ToString());
        Assert.Equal("listitem", item.ToString());
    }

    [Fact]
    public void CaptureAccessibilityTree_HiddenElementIsNull()
    {
        var body = GetBody("<div id=hidden hidden><button>x</button></div><div aria-hidden=true><button id=ariaHidden>x</button></div>");
        Assert.Null(body.QuerySelector("#hidden")!.CaptureAccessibilityTree());
        Assert.Null(body.QuerySelector("#ariaHidden")!.CaptureAccessibilityTree());
    }

    [Fact]
    public void CaptureAccessibilityTree_RootWithRoleIsWritten()
    {
        var body = GetBody("<nav id=target aria-label=Main><a href=/>Home</a></nav>");
        Assert.Equal("- navigation \"Main\":\n  - link \"Home\":\n    - /url: /", body.QuerySelector("#target")!.CaptureAccessibilityTree()!.ToAriaSnapshot());
    }

    [Fact]
    public void CaptureAccessibilityTree_IsASnapshot()
    {
        var body = GetBody("<button>Before</button>");
        var tree = body.CaptureAccessibilityTree()!;
        body.QuerySelector("button")!.TextContent = "After";
        Assert.Equal("- button \"Before\"", tree.ToAriaSnapshot());
        Assert.Equal("- button \"After\"", body.CaptureAccessibilityTree()!.ToAriaSnapshot());
    }

    [Fact]
    public void CaptureAccessibilityTree_DeepDocument()
    {
        var body = GetBody("");
        var parent = (Element)body;
        for (var i = 0; i < 5_000; i++)
        {
            var child = body.OwnerDocument!.CreateElement("span");
            parent.AppendChild(child);
            parent = child;
        }

        parent.TextContent = "deep";
        Assert.Equal("- text: deep", body.CaptureAccessibilityTree()!.ToAriaSnapshot());
    }

    [Theory]
    [InlineData("<p>a: b</p>", "- paragraph: \"a: b\"")]
    [InlineData("<p>a:b</p>", "- paragraph: a:b")]
    [InlineData("<p>a #b</p>", "- paragraph: \"a #b\"")]
    [InlineData("<p>a#b</p>", "- paragraph: a#b")]
    [InlineData("<p>0x1F</p>", "- paragraph: \"0x1F\"")]
    [InlineData("<p>1e5</p>", "- paragraph: \"1e5\"")]
    [InlineData("<p>.5</p>", "- paragraph: \".5\"")]
    [InlineData("<p>1.2.3</p>", "- paragraph: 1.2.3")]
    [InlineData("<p>Infinity</p>", "- paragraph: \"Infinity\"")]
    [InlineData("<p>NULL</p>", "- paragraph: \"NULL\"")]
    [InlineData("<p>{x}</p>", "- paragraph: \"{x}\"")]
    [InlineData("<p>[x]</p>", "- paragraph: \"[x]\"")]
    [InlineData("<p>it's</p>", "- paragraph: it's")]
    [InlineData("<button>it's: \"x\"</button>", "- 'button \"it''s: \\\"x\\\"\"'")]
    [InlineData("<button>a\\b</button>", "- button \"a\\\\b\"")]
    public void CaptureAccessibilityTree_QuotesAsPlaywright(string html, string expected) => Assert.Equal(expected, GetBody(html).CaptureAccessibilityTree()!.ToAriaSnapshot());

    [Fact]
    public void Documentation_AccessibilityTree()
    {
        #region WebPlatform_AccessibilityTree
        using var context = new BrowsingContext();
        var document = context.Navigate("https://example.com/", _ => """
            <main>
              <section headingoffset="1">
                <h1>Sign in</h1>
                <label>Email <input type="email" placeholder="you@example.com"></label>
                <label><input type="checkbox" checked> Remember me</label>
                <button disabled>Continue</button>
              </section>
            </main>
            """);

        // The properties of one element
        var button = document.QuerySelector("button")!.GetAccessibilityProperties();
        Assert.Equal(("button", "Continue", true), (button.Role, button.Name, button.Disabled));

        // The level of a heading takes headingoffset into account
        var heading = document.QuerySelector("h1")!.GetAccessibilityProperties();
        Assert.Equal(2, heading.Level);

        // A snapshot of the tree, in the format of the aria snapshots of Playwright
        var tree = document.Body!.CaptureAccessibilityTree()!;
        Assert.Equal("""
            - main:
              - heading "Sign in" [level=2]
              - text: Email
              - textbox "Email":
                - /placeholder: you@example.com
              - checkbox "Remember me" [checked]
              - text: Remember me
              - button "Continue" [disabled]
            """, tree.ToAriaSnapshot());
        #endregion
    }
}
