namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class RazorHighlighterTests
{

    [Fact]
    public void Directive_Page()
    {
        AssertHighlighter("razor",
"""
@page "/home"
""",
"""
<span class="hljs-built_in">@page</span><span class="hljs-type"> &quot;/home&quot;</span>
""");
    }

    [Fact]
    public void Directive_PageWithParam()
    {
        AssertHighlighter("razor",
"""
@page "/users/{id:int}"
""",
"""
<span class="hljs-built_in">@</span><span class="language-csharp">page</span> &quot;/users/{id:int}&quot;
""");
    }

    [Fact]
    public void Directive_Model()
    {
        AssertHighlighter("razor",
"""
@model UserViewModel
""",
"""
<span class="hljs-built_in">@model</span><span class="hljs-type"> UserViewModel</span>
""");
    }

    [Fact]
    public void Directive_ModelGeneric()
    {
        AssertHighlighter("razor",
"""
@model IEnumerable<MyApp.Models.Product>
""",
"""
<span class="hljs-built_in">@model</span><span class="hljs-type"> IEnumerable&lt;MyApp.Models.Product&gt;</span>
""");
    }

    [Fact]
    public void Directive_UsingDirective()
    {
        AssertHighlighter("razor",
"""
@using System.Linq
""",
"""
<span class="hljs-built_in">@using</span><span class="hljs-type"> System.Linq</span>
""");
    }

    [Fact]
    public void Directive_UsingStaticDirective()
    {
        AssertHighlighter("razor",
"""
@using static System.Math
""",
"""
<span class="hljs-built_in">@using</span><span class="hljs-type"> static System.Math</span>
""");
    }

    [Fact]
    public void Directive_Inject()
    {
        AssertHighlighter("razor",
"""
@inject ILogger<Page> Logger
""",
"""
<span class="hljs-built_in">@inject</span><span class="hljs-type"> ILogger&lt;Page&gt; Logger</span>
""");
    }

    [Fact]
    public void Directive_InjectMultiple()
    {
        AssertHighlighter("razor",
"""
@inject ILogger<Page> Logger
@inject IUserService Users
""",
"""
<span class="hljs-built_in">@inject</span><span class="hljs-type"> ILogger&lt;Page&gt; Logger</span>
<span class="hljs-built_in">@inject</span><span class="hljs-type"> IUserService Users</span>
""");
    }

    [Fact]
    public void Directive_Inherits()
    {
        AssertHighlighter("razor",
"""
@inherits MyAppPageBase
""",
"""
<span class="hljs-built_in">@inherits</span><span class="hljs-type"> MyAppPageBase</span>
""");
    }

    [Fact]
    public void Directive_NamespaceDir()
    {
        AssertHighlighter("razor",
"""
@namespace MyApp.Pages
""",
"""
<span class="hljs-built_in">@</span><span class="language-csharp"><span class="hljs-keyword">namespace</span></span> MyApp.Pages
""");
    }

    [Fact]
    public void Directive_Attribute()
    {
        AssertHighlighter("razor",
"""
@attribute [Authorize]
""",
"""
<span class="hljs-built_in">@</span><span class="language-csharp">attribute</span> [Authorize]
""");
    }

    [Fact]
    public void Directive_AttributeWithArgs()
    {
        AssertHighlighter("razor",
"""
@attribute [Authorize(Roles = "Admin")]
""",
"""
<span class="hljs-built_in">@</span><span class="language-csharp">attribute</span> [Authorize(Roles = &quot;Admin&quot;)]
""");
    }

    [Fact]
    public void Directive_AddTagHelper()
    {
        AssertHighlighter("razor",
"""
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
""",
"""
<span class="hljs-built_in">@</span><span class="language-csharp">addTagHelper</span> *, Microsoft.AspNetCore.Mvc.TagHelpers
""");
    }

    [Fact]
    public void Directive_RemoveTagHelper()
    {
        AssertHighlighter("razor",
"""
@removeTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
""",
"""
<span class="hljs-built_in">@</span><span class="language-csharp">removeTagHelper</span> *, Microsoft.AspNetCore.Mvc.TagHelpers
""");
    }

    [Fact]
    public void Directive_TagHelperPrefix()
    {
        AssertHighlighter("razor",
"""
@tagHelperPrefix "th:"
""",
"""
<span class="hljs-built_in">@</span><span class="language-csharp">tagHelperPrefix</span> &quot;th:&quot;
""");
    }

    [Fact]
    public void Directive_ViewDataDirective()
    {
        AssertHighlighter("razor",
"""
@{ ViewData["Title"] = "Home"; }
""",
"""
<span class="hljs-built_in">@{</span><span class="language-csharp"> ViewData[<span class="hljs-string">&quot;Title&quot;</span>] = <span class="hljs-string">&quot;Home&quot;</span>; </span><span class="hljs-built_in">}</span>
""");
    }

    [Fact]
    public void Directive_Layout()
    {
        AssertHighlighter("razor",
"""
@{ Layout = "_Layout"; }
""",
"""
<span class="hljs-built_in">@{</span><span class="language-csharp"> Layout = <span class="hljs-string">&quot;_Layout&quot;</span>; </span><span class="hljs-built_in">}</span>
""");
    }

    [Fact]
    public void Directive_Section()
    {
        AssertHighlighter("razor",
"""
@section Scripts {
    <script src="~/js/site.js"></script>
}
""",
"""
<span class="hljs-built_in">@section Scripts {</span><span class="language-cshtml-razor">
    <span class="hljs-tag">&lt;<span class="hljs-name">script</span> <span class="hljs-attr">src</span>=<span class="hljs-string">&quot;~/js/site.js&quot;</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span>
</span><span class="hljs-built_in">}</span>
""");
    }

    [Fact]
    public void Directive_SectionRender()
    {
        AssertHighlighter("razor",
"""
@RenderSection("Scripts", required: false)
""",
"""
<span class="hljs-built_in">@</span><span class="language-csharp">RenderSection(<span class="hljs-string">&quot;Scripts&quot;</span>, <span class="hljs-keyword">required</span>: <span class="hljs-literal">false</span>)</span>
""");
    }

    [Fact]
    public void Directive_Implements()
    {
        AssertHighlighter("razor",
"""
@implements IDisposable
""",
"""
<span class="hljs-built_in">@</span><span class="language-csharp">implements</span> IDisposable
""");
    }

    [Fact]
    public void Directive_TypeParam()
    {
        AssertHighlighter("razor",
"""
@typeparam TItem
""",
"""
<span class="hljs-built_in">@</span><span class="language-csharp">typeparam</span> TItem
""");
    }

    [Fact]
    public void Directive_TypeParamConstraint()
    {
        AssertHighlighter("razor",
"""
@typeparam TItem where TItem : class, new()
""",
"""
<span class="hljs-built_in">@</span><span class="language-csharp">typeparam</span> TItem where TItem : class, new()
""");
    }

    [Fact]
    public void Directive_Preserves()
    {
        AssertHighlighter("razor",
"""
@preservewhitespace true
""",
"""
<span class="hljs-built_in">@</span><span class="language-csharp">preservewhitespace</span> true
""");
    }

    [Fact]
    public void Directive_Functions()
    {
        AssertHighlighter("razor",
"""
@functions {
    public string Greet(string name) => $"Hello {name}";
}
""",
"""
<span class="hljs-built_in">@</span><span class="language-csharp">functions</span> {
    public string Greet(string name) =&gt; $&quot;Hello {name}&quot;;
}
""");
    }

    [Fact]
    public void Directive_CodeBlock()
    {
        AssertHighlighter("razor",
"""
@code {
    private int _count = 0;

    private void Increment() => _count++;
}
""",
"""
<span class="hljs-built_in">@code {</span><span class="language-csharp">
    <span class="hljs-keyword">private</span> <span class="hljs-built_in">int</span> _count = <span class="hljs-number">0</span>;

    <span class="hljs-function"><span class="hljs-keyword">private</span> <span class="hljs-keyword">void</span> <span class="hljs-title">Increment</span>()</span> =&gt; _count++;
</span><span class="hljs-built_in">}</span>
""");
    }

    [Fact]
    public void Expression_ImplicitProperty()
    {
        AssertHighlighter("razor",
"""
<p>@Model.Name</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="hljs-built_in">@</span><span class="language-csharp">Model.Name</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void Expression_ImplicitMethod()
    {
        AssertHighlighter("razor",
"""
<span>@DateTime.Now.ToShortDateString()</span>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">span</span>&gt;</span><span class="hljs-built_in">@</span><span class="language-csharp">DateTime.Now.ToShortDateString()</span><span class="hljs-tag">&lt;/<span class="hljs-name">span</span>&gt;</span>
""");
    }

    [Fact]
    public void Expression_ImplicitChain()
    {
        AssertHighlighter("razor",
"""
<p>@user.Profile.Address.City</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="hljs-built_in">@</span><span class="language-csharp">user.Profile.Address.City</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void Expression_ExplicitParens()
    {
        AssertHighlighter("razor",
"""
<p>@(user.FirstName + " " + user.LastName)</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="hljs-built_in">@(</span><span class="language-csharp">user.FirstName + <span class="hljs-string">&quot; &quot;</span> + user.LastName</span><span class="hljs-built_in">)</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void Expression_ExplicitArith()
    {
        AssertHighlighter("razor",
"""
<p>Total: @(price * quantity)</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>Total: <span class="hljs-built_in">@(</span><span class="language-csharp">price * quantity</span><span class="hljs-built_in">)</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void Expression_NullConditional()
    {
        AssertHighlighter("razor",
"""
<p>@(user?.Name)</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="hljs-built_in">@(</span><span class="language-csharp">user?.Name</span><span class="hljs-built_in">)</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void Expression_StringInterp()
    {
        AssertHighlighter("razor",
"""
<p>@($"Hello {user.Name}!")</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="hljs-built_in">@(</span><span class="language-csharp"><span class="hljs-string">$&quot;Hello <span class="hljs-subst">{user.Name}</span>!&quot;</span></span><span class="hljs-built_in">)</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void Expression_AwaitExpr()
    {
        AssertHighlighter("razor",
"""
<p>@await Html.PartialAsync("_Sidebar")</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="hljs-built_in">@await </span><span class="language-csharp">Html.PartialAsync(<span class="hljs-string">&quot;_Sidebar&quot;</span>)</span>&lt;/p&gt;
""");
    }

    [Fact]
    public void Expression_HtmlRaw()
    {
        AssertHighlighter("razor",
"""
<p>@Html.Raw(rawHtml)</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="hljs-built_in">@</span><span class="language-csharp">Html.Raw(rawHtml)</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void Expression_HtmlActionLink()
    {
        AssertHighlighter("razor",
"""
<p>@Html.ActionLink("Edit", "Edit", new { id = user.Id })</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="hljs-built_in">@</span><span class="language-csharp">Html.ActionLink(<span class="hljs-string">&quot;Edit&quot;</span>, <span class="hljs-string">&quot;Edit&quot;</span>, <span class="hljs-keyword">new</span> { id = user.Id })</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void Expression_HtmlPartial()
    {
        AssertHighlighter("razor",
"""
<div>@await Html.PartialAsync("_UserCard", user)</div>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">div</span>&gt;</span><span class="hljs-built_in">@await </span><span class="language-csharp">Html.PartialAsync(<span class="hljs-string">&quot;_UserCard&quot;</span>, </span>user)<span class="hljs-tag">&lt;/<span class="hljs-name">div</span>&gt;</span>
""");
    }

    [Fact]
    public void Expression_UrlAction()
    {
        AssertHighlighter("razor",
"""
<a href="@Url.Action("Index", "Home")">Home</a>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">a</span> <span class="hljs-attr">href</span>=<span class="hljs-string">&quot;<span class="hljs-built_in">@</span><span class="language-csharp">Url.Action(<span class="hljs-string">&quot;Index&quot;</span>, <span class="hljs-string">&quot;Home&quot;</span>)</span>&quot;</span>&gt;</span>Home<span class="hljs-tag">&lt;/<span class="hljs-name">a</span>&gt;</span>
""");
    }

    [Fact]
    public void Expression_EscapedAt()
    {
        AssertHighlighter("razor",
"""
<p>Email me @@example.com</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>Email me @@example.com<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void CodeBlock_Simple()
    {
        AssertHighlighter("razor",
"""
@{
    var greeting = "hello";
    ViewData["Title"] = "Home";
}
""",
"""
<span class="hljs-built_in">@{</span><span class="language-csharp">
    <span class="hljs-keyword">var</span> greeting = <span class="hljs-string">&quot;hello&quot;</span>;
    ViewData[<span class="hljs-string">&quot;Title&quot;</span>] = <span class="hljs-string">&quot;Home&quot;</span>;
</span><span class="hljs-built_in">}</span>
""");
    }

    [Fact]
    public void CodeBlock_Multiple()
    {
        AssertHighlighter("razor",
"""
@{
    var users = await dbContext.Users.ToListAsync();
    var count = users.Count;
    Layout = "_Layout";
}
""",
"""
<span class="hljs-built_in">@{</span><span class="language-csharp">
    <span class="hljs-keyword">var</span> users = <span class="hljs-keyword">await</span> dbContext.Users.ToListAsync();
    <span class="hljs-keyword">var</span> count = users.Count;
    Layout = <span class="hljs-string">&quot;_Layout&quot;</span>;
</span><span class="hljs-built_in">}</span>
""");
    }

    [Fact]
    public void CodeBlock_WithFunction()
    {
        AssertHighlighter("razor",
"""
@{
    string Format(decimal price) => price.ToString("C2");
    var label = Format(9.99m);
}
""",
"""
<span class="hljs-built_in">@{</span><span class="language-csharp">
    <span class="hljs-function"><span class="hljs-built_in">string</span> <span class="hljs-title">Format</span>(<span class="hljs-params"><span class="hljs-built_in">decimal</span> price</span>)</span> =&gt; price.ToString(<span class="hljs-string">&quot;C2&quot;</span>);
    <span class="hljs-keyword">var</span> label = Format(<span class="hljs-number">9.99</span>m);
</span><span class="hljs-built_in">}</span>
""");
    }

    [Fact]
    public void CodeBlock_Inline()
    {
        AssertHighlighter("razor",
"""
<p>@{ var n = 1 + 2; } The answer is @n.</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="hljs-built_in">@{</span><span class="language-csharp"> <span class="hljs-keyword">var</span> n = <span class="hljs-number">1</span> + <span class="hljs-number">2</span>; </span><span class="hljs-built_in">}</span> The answer is <span class="hljs-built_in">@</span><span class="language-csharp">n.</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void ControlFlow_If()
    {
        AssertHighlighter("razor",
"""
@if (user.IsActive)
{
    <p>Welcome back, @user.Name!</p>
}
""",
"""
<span class="hljs-built_in">@</span><span class="language-csharp"><span class="hljs-keyword">if</span> (user.IsActive)
</span><span class="hljs-built_in">{</span><span class="language-csharp">
    </span><span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="language-csharp">Welcome back, </span><span class="hljs-built_in">@</span><span class="language-csharp">user.Name!</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span><span class="language-csharp">
</span><span class="hljs-built_in">}</span>
""");
    }

    [Fact]
    public void ControlFlow_IfElse()
    {
        AssertHighlighter("razor",
"""
@if (user.IsActive)
{
    <p>Welcome back, @user.Name!</p>
}
else
{
    <p>Please <a href="/login">log in</a>.</p>
}
""",
"""
<span class="hljs-built_in">@</span><span class="language-csharp"><span class="hljs-keyword">if</span> (user.IsActive)
</span><span class="hljs-built_in">{</span><span class="language-csharp">
    </span><span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="language-csharp">Welcome back, </span><span class="hljs-built_in">@</span><span class="language-csharp">user.Name!</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span><span class="language-csharp">
</span><span class="hljs-built_in">}</span><span class="language-csharp">
<span class="hljs-keyword">else</span>
</span><span class="hljs-built_in">{</span><span class="language-csharp">
    </span><span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="language-csharp">Please </span><span class="hljs-tag">&lt;<span class="hljs-name">a</span> <span class="hljs-attr">href</span>=<span class="hljs-string">&quot;/login&quot;</span>&gt;</span><span class="language-csharp">log <span class="hljs-keyword">in</span></span><span class="hljs-tag">&lt;/<span class="hljs-name">a</span>&gt;</span><span class="language-csharp">.</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span><span class="language-csharp">
</span><span class="hljs-built_in">}</span>
""");
    }

    [Fact]
    public void ControlFlow_IfElseIf()
    {
        AssertHighlighter("razor",
"""
@if (status == "ok")
{
    <span class="ok">OK</span>
}
else if (status == "warn")
{
    <span class="warn">Warning</span>
}
else
{
    <span class="error">Error</span>
}
""",
"""
<span class="hljs-built_in">@</span><span class="language-csharp"><span class="hljs-keyword">if</span> (status == <span class="hljs-string">&quot;ok&quot;</span>)
</span><span class="hljs-built_in">{</span><span class="language-csharp">
    </span><span class="hljs-tag">&lt;<span class="hljs-name">span</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;ok&quot;</span>&gt;</span><span class="language-csharp">OK</span><span class="hljs-tag">&lt;/<span class="hljs-name">span</span>&gt;</span><span class="language-csharp">
</span><span class="hljs-built_in">}</span><span class="language-csharp">
<span class="hljs-keyword">else</span> <span class="hljs-keyword">if</span> (status == <span class="hljs-string">&quot;warn&quot;</span>)
</span><span class="hljs-built_in">{</span><span class="language-csharp">
    </span><span class="hljs-tag">&lt;<span class="hljs-name">span</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;warn&quot;</span>&gt;</span><span class="language-csharp">Warning</span><span class="hljs-tag">&lt;/<span class="hljs-name">span</span>&gt;</span><span class="language-csharp">
</span><span class="hljs-built_in">}</span><span class="language-csharp">
<span class="hljs-keyword">else</span>
</span><span class="hljs-built_in">{</span><span class="language-csharp">
    </span><span class="hljs-tag">&lt;<span class="hljs-name">span</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;error&quot;</span>&gt;</span><span class="language-csharp">Error</span><span class="hljs-tag">&lt;/<span class="hljs-name">span</span>&gt;</span><span class="language-csharp">
</span><span class="hljs-built_in">}</span>
""");
    }

    [Fact]
    public void ControlFlow_ForLoop()
    {
        AssertHighlighter("razor",
"""
@for (int i = 0; i < 10; i++)
{
    <li>Item @i</li>
}
""",
"""
<span class="hljs-built_in">@</span><span class="language-csharp"><span class="hljs-keyword">for</span> (<span class="hljs-built_in">int</span> i = <span class="hljs-number">0</span>; i &lt; <span class="hljs-number">10</span>; i++)
</span><span class="hljs-built_in">{</span><span class="language-csharp">
    </span><span class="hljs-tag">&lt;<span class="hljs-name">li</span>&gt;</span><span class="language-csharp">Item </span><span class="hljs-built_in">@</span><span class="language-csharp">i</span><span class="hljs-tag">&lt;/<span class="hljs-name">li</span>&gt;</span><span class="language-csharp">
</span><span class="hljs-built_in">}</span>
""");
    }

    [Fact]
    public void ControlFlow_ForEach()
    {
        AssertHighlighter("razor",
"""
@foreach (var user in Model.Users)
{
    <li>@user.Name</li>
}
""",
"""
<span class="hljs-built_in">@</span><span class="language-csharp"><span class="hljs-keyword">foreach</span> (<span class="hljs-keyword">var</span> user <span class="hljs-keyword">in</span> Model.Users)
</span><span class="hljs-built_in">{</span><span class="language-csharp">
    </span><span class="hljs-tag">&lt;<span class="hljs-name">li</span>&gt;</span><span class="hljs-built_in">@</span><span class="language-csharp">user.Name</span><span class="hljs-tag">&lt;/<span class="hljs-name">li</span>&gt;</span><span class="language-csharp">
</span><span class="hljs-built_in">}</span>
""");
    }

    [Fact]
    public void ControlFlow_ForEachAwait()
    {
        AssertHighlighter("razor",
"""
@await foreach (var item in source)
{
    <li>@item.Name</li>
}
""",
"""
<span class="hljs-built_in">@await </span><span class="language-csharp"><span class="hljs-keyword">foreach</span> </span>(var item in source)
{
    <span class="hljs-tag">&lt;<span class="hljs-name">li</span>&gt;</span><span class="hljs-built_in">@</span><span class="language-csharp">item.Name</span><span class="hljs-tag">&lt;/<span class="hljs-name">li</span>&gt;</span>
}
""");
    }

    [Fact]
    public void ControlFlow_WhileLoop()
    {
        AssertHighlighter("razor",
"""
@while (queue.Any())
{
    var item = queue.Dequeue();
    <p>@item</p>
}
""",
"""
<span class="hljs-built_in">@</span><span class="language-csharp"><span class="hljs-keyword">while</span> (queue.Any())
</span><span class="hljs-built_in">{</span><span class="language-csharp">
    <span class="hljs-keyword">var</span> item = queue.Dequeue();
    </span><span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="hljs-built_in">@</span><span class="language-csharp">item</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span><span class="language-csharp">
</span><span class="hljs-built_in">}</span>
""");
    }

    [Fact]
    public void ControlFlow_SwitchExpr()
    {
        AssertHighlighter("razor",
"""
@switch (user.Role)
{
    case Role.Admin:
        <span class="admin">Admin</span>
        break;
    case Role.User:
        <span class="user">User</span>
        break;
    default:
        <span>Unknown</span>
        break;
}
""",
"""
<span class="hljs-built_in">@</span><span class="language-csharp"><span class="hljs-keyword">switch</span> (user.Role)
</span><span class="hljs-built_in">{</span><span class="language-csharp">
    <span class="hljs-keyword">case</span> Role.Admin:
        </span><span class="hljs-tag">&lt;<span class="hljs-name">span</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;admin&quot;</span>&gt;</span><span class="language-csharp">Admin</span><span class="hljs-tag">&lt;/<span class="hljs-name">span</span>&gt;</span><span class="language-csharp">
        <span class="hljs-keyword">break</span>;
    <span class="hljs-keyword">case</span> Role.User:
        </span><span class="hljs-tag">&lt;<span class="hljs-name">span</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;user&quot;</span>&gt;</span><span class="language-csharp">User</span><span class="hljs-tag">&lt;/<span class="hljs-name">span</span>&gt;</span><span class="language-csharp">
        <span class="hljs-keyword">break</span>;
    <span class="hljs-literal">default</span>:
        </span><span class="hljs-tag">&lt;<span class="hljs-name">span</span>&gt;</span><span class="language-csharp">Unknown</span><span class="hljs-tag">&lt;/<span class="hljs-name">span</span>&gt;</span><span class="language-csharp">
        <span class="hljs-keyword">break</span>;
</span><span class="hljs-built_in">}</span>
""");
    }

    [Fact]
    public void ControlFlow_TryCatch()
    {
        AssertHighlighter("razor",
"""
@try
{
    await DoWorkAsync();
}
catch (Exception ex)
{
    <p class="error">@ex.Message</p>
}
""",
"""
<span class="hljs-built_in">@</span><span class="language-csharp"><span class="hljs-keyword">try</span>
{</span><span class="language-csharp">
    <span class="hljs-keyword">await</span> DoWorkAsync();
</span><span class="hljs-built_in">}</span><span class="language-csharp">
<span class="hljs-keyword">catch</span> (Exception ex)
</span><span class="hljs-built_in">{</span><span class="language-csharp">
    </span><span class="hljs-tag">&lt;<span class="hljs-name">p</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;error&quot;</span>&gt;</span><span class="hljs-built_in">@</span><span class="language-csharp">ex.Message</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span><span class="language-csharp">
</span><span class="hljs-built_in">}</span>
""");
    }

    [Fact]
    public void ControlFlow_Using()
    {
        AssertHighlighter("razor",
"""
@using (Html.BeginForm("Save", "Users"))
{
    <input asp-for="Name" />
    <button type="submit">Save</button>
}
""",
"""
<span class="hljs-built_in">@</span><span class="language-csharp"><span class="hljs-keyword">using</span> (Html.BeginForm(<span class="hljs-string">&quot;Save&quot;</span>, <span class="hljs-string">&quot;Users&quot;</span>))
</span><span class="hljs-built_in">{</span><span class="language-csharp">
    </span><span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">asp-for</span>=<span class="hljs-string">&quot;Name&quot;</span> /&gt;</span><span class="language-csharp">
    </span><span class="hljs-tag">&lt;<span class="hljs-name">button</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;submit&quot;</span>&gt;</span><span class="language-csharp">Save</span><span class="hljs-tag">&lt;/<span class="hljs-name">button</span>&gt;</span><span class="language-csharp">
</span><span class="hljs-built_in">}</span>
""");
    }

    [Fact]
    public void ControlFlow_Lock()
    {
        AssertHighlighter("razor",
"""
@lock (_sync)
{
    <p>Count: @_counter</p>
}
""",
"""
<span class="hljs-built_in">@</span><span class="language-csharp"><span class="hljs-keyword">lock</span> (_sync)
</span><span class="hljs-built_in">{</span><span class="language-csharp">
    </span><span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="language-csharp">Count: </span><span class="hljs-built_in">@</span><span class="language-csharp">_counter</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span><span class="language-csharp">
</span><span class="hljs-built_in">}</span>
""");
    }

    [Fact]
    public void TagHelper_AspFor()
    {
        AssertHighlighter("razor",
"""
<input asp-for="UserName" class="form-control" />
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">asp-for</span>=<span class="hljs-string">&quot;UserName&quot;</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;form-control&quot;</span> /&gt;</span>
""");
    }

    [Fact]
    public void TagHelper_AspValidation()
    {
        AssertHighlighter("razor",
"""
<span asp-validation-for="UserName" class="text-danger"></span>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">span</span> <span class="hljs-attr">asp-validation-for</span>=<span class="hljs-string">&quot;UserName&quot;</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;text-danger&quot;</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">span</span>&gt;</span>
""");
    }

    [Fact]
    public void TagHelper_AspController()
    {
        AssertHighlighter("razor",
"""
<a asp-controller="Users" asp-action="Edit" asp-route-id="@user.Id">Edit</a>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">a</span> <span class="hljs-attr">asp-controller</span>=<span class="hljs-string">&quot;Users&quot;</span> <span class="hljs-attr">asp-action</span>=<span class="hljs-string">&quot;Edit&quot;</span> <span class="hljs-attr">asp-route-id</span>=<span class="hljs-string">&quot;<span class="hljs-built_in">@</span><span class="language-csharp">user.Id</span>&quot;</span>&gt;</span>Edit<span class="hljs-tag">&lt;/<span class="hljs-name">a</span>&gt;</span>
""");
    }

    [Fact]
    public void TagHelper_AspPage()
    {
        AssertHighlighter("razor",
"""
<a asp-page="/Users/Edit" asp-route-id="@user.Id">Edit</a>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">a</span> <span class="hljs-attr">asp-page</span>=<span class="hljs-string">&quot;/Users/Edit&quot;</span> <span class="hljs-attr">asp-route-id</span>=<span class="hljs-string">&quot;<span class="hljs-built_in">@</span><span class="language-csharp">user.Id</span>&quot;</span>&gt;</span>Edit<span class="hljs-tag">&lt;/<span class="hljs-name">a</span>&gt;</span>
""");
    }

    [Fact]
    public void TagHelper_EnvironmentDev()
    {
        AssertHighlighter("razor",
"""
<environment include="Development">
    <script src="~/js/site.js"></script>
</environment>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">environment</span> <span class="hljs-attr">include</span>=<span class="hljs-string">&quot;Development&quot;</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">script</span> <span class="hljs-attr">src</span>=<span class="hljs-string">&quot;~/js/site.js&quot;</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">environment</span>&gt;</span>
""");
    }

    [Fact]
    public void TagHelper_EnvironmentProd()
    {
        AssertHighlighter("razor",
"""
<environment include="Staging,Production">
    <script src="~/js/site.min.js" asp-append-version="true"></script>
</environment>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">environment</span> <span class="hljs-attr">include</span>=<span class="hljs-string">&quot;Staging,Production&quot;</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">script</span> <span class="hljs-attr">src</span>=<span class="hljs-string">&quot;~/js/site.min.js&quot;</span> <span class="hljs-attr">asp-append-version</span>=<span class="hljs-string">&quot;true&quot;</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">environment</span>&gt;</span>
""");
    }

    [Fact]
    public void TagHelper_CacheBust()
    {
        AssertHighlighter("razor",
"""
<link rel="stylesheet" href="~/css/site.css" asp-append-version="true" />
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">link</span> <span class="hljs-attr">rel</span>=<span class="hljs-string">&quot;stylesheet&quot;</span> <span class="hljs-attr">href</span>=<span class="hljs-string">&quot;~/css/site.css&quot;</span> <span class="hljs-attr">asp-append-version</span>=<span class="hljs-string">&quot;true&quot;</span> /&gt;</span>
""");
    }

    [Fact]
    public void TagHelper_AnchorRoute()
    {
        AssertHighlighter("razor",
"""
<a asp-route="user-edit" asp-route-id="@user.Id">Edit</a>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">a</span> <span class="hljs-attr">asp-route</span>=<span class="hljs-string">&quot;user-edit&quot;</span> <span class="hljs-attr">asp-route-id</span>=<span class="hljs-string">&quot;<span class="hljs-built_in">@</span><span class="language-csharp">user.Id</span>&quot;</span>&gt;</span>Edit<span class="hljs-tag">&lt;/<span class="hljs-name">a</span>&gt;</span>
""");
    }

    [Fact]
    public void TagHelper_FormAttrs()
    {
        AssertHighlighter("razor",
"""
<form asp-action="Save" asp-controller="Users" method="post">
    <input asp-for="Name" />
    <button type="submit">Save</button>
</form>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">form</span> <span class="hljs-attr">asp-action</span>=<span class="hljs-string">&quot;Save&quot;</span> <span class="hljs-attr">asp-controller</span>=<span class="hljs-string">&quot;Users&quot;</span> <span class="hljs-attr">method</span>=<span class="hljs-string">&quot;post&quot;</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">asp-for</span>=<span class="hljs-string">&quot;Name&quot;</span> /&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">button</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;submit&quot;</span>&gt;</span>Save<span class="hljs-tag">&lt;/<span class="hljs-name">button</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">form</span>&gt;</span>
""");
    }

    [Fact]
    public void Blazor_PageDirective()
    {
        AssertHighlighter("razor",
"""
@page "/counter"

<h1>Counter</h1>

<p>Current count: @currentCount</p>

<button class="btn btn-primary" @onclick="IncrementCount">Click me</button>

@code {
    private int currentCount = 0;

    private void IncrementCount()
    {
        currentCount++;
    }
}
""",
"""
<span class="hljs-built_in">@page</span><span class="hljs-type"> &quot;/counter&quot;</span>

<span class="hljs-tag">&lt;<span class="hljs-name">h1</span>&gt;</span>Counter<span class="hljs-tag">&lt;/<span class="hljs-name">h1</span>&gt;</span>

<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>Current count: <span class="hljs-built_in">@</span><span class="language-csharp">currentCount</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>

<span class="hljs-tag">&lt;<span class="hljs-name">button</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;btn btn-primary&quot;</span> @<span class="hljs-attr">onclick</span>=<span class="hljs-string">&quot;IncrementCount&quot;</span>&gt;</span>Click me<span class="hljs-tag">&lt;/<span class="hljs-name">button</span>&gt;</span>

<span class="hljs-built_in">@code {</span><span class="language-csharp">
    <span class="hljs-keyword">private</span> <span class="hljs-built_in">int</span> currentCount = <span class="hljs-number">0</span>;

    <span class="hljs-function"><span class="hljs-keyword">private</span> <span class="hljs-keyword">void</span> <span class="hljs-title">IncrementCount</span>()</span>
    {
        currentCount++;
    }
</span><span class="hljs-built_in">}</span>
""");
    }

    [Fact]
    public void Blazor_OnClickEvent()
    {
        AssertHighlighter("razor",
"""
<button @onclick="HandleClick">Click</button>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">button</span> @<span class="hljs-attr">onclick</span>=<span class="hljs-string">&quot;HandleClick&quot;</span>&gt;</span>Click<span class="hljs-tag">&lt;/<span class="hljs-name">button</span>&gt;</span>
""");
    }

    [Fact]
    public void Blazor_OnClickLambda()
    {
        AssertHighlighter("razor",
"""
<button @onclick="@(e => Counter++)">Increment</button>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">button</span> @<span class="hljs-attr">onclick</span>=<span class="hljs-string">&quot;<span class="hljs-built_in">@(</span><span class="language-csharp">e =&gt; Counter++</span><span class="hljs-built_in">)</span>&quot;</span>&gt;</span>Increment<span class="hljs-tag">&lt;/<span class="hljs-name">button</span>&gt;</span>
""");
    }

    [Fact]
    public void Blazor_BindValue()
    {
        AssertHighlighter("razor",
"""
<input @bind="userName" />
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> @<span class="hljs-attr">bind</span>=<span class="hljs-string">&quot;userName&quot;</span> /&gt;</span>
""");
    }

    [Fact]
    public void Blazor_BindEvent()
    {
        AssertHighlighter("razor",
"""
<input @bind="userName" @bind:event="oninput" />
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> @<span class="hljs-attr">bind</span>=<span class="hljs-string">&quot;userName&quot;</span> @<span class="hljs-attr">bind:event</span>=<span class="hljs-string">&quot;oninput&quot;</span> /&gt;</span>
""");
    }

    [Fact]
    public void Blazor_BindWithFormat()
    {
        AssertHighlighter("razor",
"""
<input @bind="birthday" @bind:format="yyyy-MM-dd" />
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> @<span class="hljs-attr">bind</span>=<span class="hljs-string">&quot;birthday&quot;</span> @<span class="hljs-attr">bind:format</span>=<span class="hljs-string">&quot;yyyy-MM-dd&quot;</span> /&gt;</span>
""");
    }

    [Fact]
    public void Blazor_BindAfter()
    {
        AssertHighlighter("razor",
"""
<input @bind="userName" @bind:after="OnNameChanged" />
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> @<span class="hljs-attr">bind</span>=<span class="hljs-string">&quot;userName&quot;</span> @<span class="hljs-attr">bind:after</span>=<span class="hljs-string">&quot;OnNameChanged&quot;</span> /&gt;</span>
""");
    }

    [Fact]
    public void Blazor_Ref()
    {
        AssertHighlighter("razor",
"""
<input @ref="nameInput" />
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> @<span class="hljs-attr">ref</span>=<span class="hljs-string">&quot;nameInput&quot;</span> /&gt;</span>
""");
    }

    [Fact]
    public void Blazor_Key()
    {
        AssertHighlighter("razor",
"""
@foreach (var user in users)
{
    <UserCard @key="user.Id" User="@user" />
}
""",
"""
<span class="hljs-built_in">@</span><span class="language-csharp"><span class="hljs-keyword">foreach</span> (<span class="hljs-keyword">var</span> user <span class="hljs-keyword">in</span> users)
</span><span class="hljs-built_in">{</span><span class="language-csharp">
    </span><span class="hljs-tag">&lt;<span class="hljs-name">UserCard</span> @<span class="hljs-attr">key</span>=<span class="hljs-string">&quot;user.Id&quot;</span> <span class="hljs-attr">User</span>=<span class="hljs-string">&quot;<span class="hljs-built_in">@</span><span class="language-csharp">user</span>&quot;</span> /&gt;</span><span class="language-csharp">
</span><span class="hljs-built_in">}</span>
""");
    }

    [Fact]
    public void Blazor_OnInitializedAsync()
    {
        AssertHighlighter("razor",
"""
@code {
    protected override async Task OnInitializedAsync()
    {
        users = await UserService.GetAllAsync();
    }
}
""",
"""
<span class="hljs-built_in">@code {</span><span class="language-csharp">
    <span class="hljs-function"><span class="hljs-keyword">protected</span> <span class="hljs-keyword">override</span> <span class="hljs-keyword">async</span> Task <span class="hljs-title">OnInitializedAsync</span>()</span>
    {
        users = <span class="hljs-keyword">await</span> UserService.GetAllAsync();
    }
</span><span class="hljs-built_in">}</span>
""");
    }

    [Fact]
    public void Blazor_ParameterCapture()
    {
        AssertHighlighter("razor",
"""
@code {
    [Parameter] public string Title { get; set; } = string.Empty;
    [Parameter] public EventCallback<int> OnSelected { get; set; }
    [CascadingParameter] public Theme CurrentTheme { get; set; } = default!;
}
""",
"""
<span class="hljs-built_in">@code {</span><span class="language-csharp">
    [<span class="hljs-meta">Parameter</span>] <span class="hljs-keyword">public</span> <span class="hljs-built_in">string</span> Title { <span class="hljs-keyword">get</span>; <span class="hljs-keyword">set</span>; } = <span class="hljs-built_in">string</span>.Empty;
    [<span class="hljs-meta">Parameter</span>] <span class="hljs-keyword">public</span> EventCallback</span><span class="hljs-tag">&lt;<span class="hljs-name">int</span>&gt;</span><span class="language-csharp"> OnSelected { <span class="hljs-keyword">get</span>; <span class="hljs-keyword">set</span>; }
    [<span class="hljs-meta">CascadingParameter</span>] <span class="hljs-keyword">public</span> Theme CurrentTheme { <span class="hljs-keyword">get</span>; <span class="hljs-keyword">set</span>; } = <span class="hljs-literal">default</span>!;
</span><span class="hljs-built_in">}</span>
""");
    }

    [Fact]
    public void Blazor_ComponentUsage()
    {
        AssertHighlighter("razor",
"""
<UserCard User="@user" OnEdit="@HandleEdit" />
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">UserCard</span> <span class="hljs-attr">User</span>=<span class="hljs-string">&quot;<span class="hljs-built_in">@</span><span class="language-csharp">user</span>&quot;</span> <span class="hljs-attr">OnEdit</span>=<span class="hljs-string">&quot;<span class="hljs-built_in">@</span><span class="language-csharp">HandleEdit</span>&quot;</span> /&gt;</span>
""");
    }

    [Fact]
    public void Blazor_RenderFragment()
    {
        AssertHighlighter("razor",
"""
<Card>
    <Header>
        <h2>@title</h2>
    </Header>
    <Body>
        <p>@body</p>
    </Body>
</Card>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">Card</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">Header</span>&gt;</span>
        <span class="hljs-tag">&lt;<span class="hljs-name">h2</span>&gt;</span><span class="hljs-built_in">@</span><span class="language-csharp">title</span><span class="hljs-tag">&lt;/<span class="hljs-name">h2</span>&gt;</span>
    <span class="hljs-tag">&lt;/<span class="hljs-name">Header</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">Body</span>&gt;</span>
        <span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="hljs-built_in">@</span><span class="language-csharp">body</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
    <span class="hljs-tag">&lt;/<span class="hljs-name">Body</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">Card</span>&gt;</span>
""");
    }

    [Fact]
    public void Blazor_GenericComponent()
    {
        AssertHighlighter("razor",
"""
<TypedList TItem="User" Items="@users">
    <ItemTemplate Context="user">
        <li>@user.Name</li>
    </ItemTemplate>
</TypedList>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">TypedList</span> <span class="hljs-attr">TItem</span>=<span class="hljs-string">&quot;User&quot;</span> <span class="hljs-attr">Items</span>=<span class="hljs-string">&quot;<span class="hljs-built_in">@</span><span class="language-csharp">users</span>&quot;</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">ItemTemplate</span> <span class="hljs-attr">Context</span>=<span class="hljs-string">&quot;user&quot;</span>&gt;</span>
        <span class="hljs-tag">&lt;<span class="hljs-name">li</span>&gt;</span><span class="hljs-built_in">@</span><span class="language-csharp">user.Name</span><span class="hljs-tag">&lt;/<span class="hljs-name">li</span>&gt;</span>
    <span class="hljs-tag">&lt;/<span class="hljs-name">ItemTemplate</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">TypedList</span>&gt;</span>
""");
    }

    [Fact]
    public void Blazor_CapturingArgs()
    {
        AssertHighlighter("razor",
"""
<input type="text" @attributes="extraAttributes" />
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;text&quot;</span> @<span class="hljs-attr">attributes</span>=<span class="hljs-string">&quot;extraAttributes&quot;</span> /&gt;</span>
""");
    }

    [Fact]
    public void Blazor_PreventDefault()
    {
        AssertHighlighter("razor",
"""
<a href="/" @onclick="HandleClick" @onclick:preventDefault>Click</a>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">a</span> <span class="hljs-attr">href</span>=<span class="hljs-string">&quot;/&quot;</span> @<span class="hljs-attr">onclick</span>=<span class="hljs-string">&quot;HandleClick&quot;</span> @<span class="hljs-attr">onclick:preventDefault</span>&gt;</span>Click<span class="hljs-tag">&lt;/<span class="hljs-name">a</span>&gt;</span>
""");
    }

    [Fact]
    public void Blazor_StopPropagation()
    {
        AssertHighlighter("razor",
"""
<button @onclick="HandleClick" @onclick:stopPropagation>Click</button>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">button</span> @<span class="hljs-attr">onclick</span>=<span class="hljs-string">&quot;HandleClick&quot;</span> @<span class="hljs-attr">onclick:stopPropagation</span>&gt;</span>Click<span class="hljs-tag">&lt;/<span class="hljs-name">button</span>&gt;</span>
""");
    }

    [Fact]
    public void Comment_Razor()
    {
        AssertHighlighter("razor",
"""
@* this is a Razor comment *@
""",
"""
<span class="hljs-comment">@* this is a Razor comment *@</span>
""");
    }

    [Fact]
    public void Comment_RazorMultiLine()
    {
        AssertHighlighter("razor",
"""
@*
   This comment
   spans multiple lines
*@
""",
"""
<span class="hljs-comment">@*
   This comment
   spans multiple lines
*@</span>
""");
    }

    [Fact]
    public void Comment_Html()
    {
        AssertHighlighter("razor",
"""
<!-- this is an HTML comment -->
""",
"""
<span class="hljs-comment">&lt;!-- this is an HTML comment --&gt;</span>
""");
    }

    [Fact]
    public void Comment_CSharpInBlock()
    {
        AssertHighlighter("razor",
"""
@{
    // this is a C# line comment
    /* and a block one */
    var x = 1;
}
""",
"""
<span class="hljs-built_in">@{</span><span class="language-csharp">
    <span class="hljs-comment">// this is a C# line comment</span>
    <span class="hljs-comment">/* and a block one */</span>
    <span class="hljs-keyword">var</span> x = <span class="hljs-number">1</span>;
</span><span class="hljs-built_in">}</span>
""");
    }

    [Fact]
    public void Markup_TextLine()
    {
        AssertHighlighter("razor",
"""
@if (true)
{
    @:literal text on a single line
}
""",
"""
<span class="hljs-built_in">@</span><span class="language-csharp"><span class="hljs-keyword">if</span> (<span class="hljs-literal">true</span>)
</span><span class="hljs-built_in">{</span><span class="language-csharp">
    </span><span class="hljs-built_in">@</span><span class="language-csharp">:literal</span><span class="language-csharp"> text on a single line
</span><span class="hljs-built_in">}</span>
""");
    }

    [Fact]
    public void Markup_TextBlock()
    {
        AssertHighlighter("razor",
"""
@if (true)
{
    <text>multi-line
    literal text block</text>
}
""",
"""
<span class="hljs-built_in">@</span><span class="language-csharp"><span class="hljs-keyword">if</span> (<span class="hljs-literal">true</span>)
</span><span class="hljs-built_in">{</span><span class="language-csharp">
    </span><span class="hljs-built_in">&lt;text&gt;</span><span class="language-cshtml-razor">multi-line
    literal text block</span><span class="hljs-built_in">&lt;/text&gt;</span><span class="language-csharp">
</span><span class="hljs-built_in">}</span>
""");
    }

    [Fact]
    public void Markup_RawHtml()
    {
        AssertHighlighter("razor",
"""
@Html.Raw("<b>bold</b>")
""",
"""
<span class="hljs-built_in">@</span><span class="language-csharp">Html.Raw(<span class="hljs-string">&quot;&lt;b&gt;bold&lt;/b&gt;&quot;</span>)</span>
""");
    }

    [Fact]
    public void Embedded_Style()
    {
        AssertHighlighter("razor",
"""
<style>p { color: red; }</style>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">style</span>&gt;</span><span class="language-css"><span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: red; }</span><span class="hljs-tag">&lt;/<span class="hljs-name">style</span>&gt;</span>
""");
    }

    [Fact]
    public void Embedded_StyleMultiline()
    {
        AssertHighlighter("razor",
"""
<style>
  .card { padding: 1rem; border: 1px solid #ccc; }
  .card-title { font-weight: bold; }
</style>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">style</span>&gt;</span><span class="language-css">
  <span class="hljs-selector-class">.card</span> { <span class="hljs-attribute">padding</span>: <span class="hljs-number">1rem</span>; <span class="hljs-attribute">border</span>: <span class="hljs-number">1px</span> solid <span class="hljs-number">#ccc</span>; }
  <span class="hljs-selector-class">.card-title</span> { <span class="hljs-attribute">font-weight</span>: bold; }
</span><span class="hljs-tag">&lt;/<span class="hljs-name">style</span>&gt;</span>
""");
    }

    [Fact]
    public void Embedded_StyleWithRazor()
    {
        AssertHighlighter("razor",
"""
<style>
  body { background: @backgroundColor; }
</style>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">style</span>&gt;</span><span class="language-css">
  <span class="hljs-selector-tag">body</span> { <span class="hljs-attribute">background</span>: @backgroundColor; }
</span><span class="hljs-tag">&lt;/<span class="hljs-name">style</span>&gt;</span>
""");
    }

    [Fact]
    public void Embedded_Script()
    {
        AssertHighlighter("razor",
"""
<script>console.log("hi");</script>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">script</span>&gt;</span><span class="language-javascript"><span class="hljs-variable language_">console</span>.<span class="hljs-title function_">log</span>(<span class="hljs-string">&quot;hi&quot;</span>);</span><span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span>
""");
    }

    [Fact]
    public void Embedded_ScriptMultiline()
    {
        AssertHighlighter("razor",
"""
<script>
  function greet(name) {
    return "Hello " + name;
  }
</script>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">script</span>&gt;</span><span class="language-javascript">
  <span class="hljs-keyword">function</span> <span class="hljs-title function_">greet</span>(<span class="hljs-params">name</span>) {
    <span class="hljs-keyword">return</span> <span class="hljs-string">&quot;Hello &quot;</span> + name;
  }
</span><span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span>
""");
    }

    [Fact]
    public void Embedded_ScriptWithRazor()
    {
        AssertHighlighter("razor",
"""
<script>
  var user = "@Model.Name";
  console.log(user);
</script>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">script</span>&gt;</span><span class="language-javascript">
  <span class="hljs-keyword">var</span> user = <span class="hljs-string">&quot;@Model.Name&quot;</span>;
  <span class="hljs-variable language_">console</span>.<span class="hljs-title function_">log</span>(user);
</span><span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span>
""");
    }

    [Fact]
    public void Embedded_StyleAndScript()
    {
        AssertHighlighter("razor",
"""
<style>p { color: blue; }</style>
<script>alert(1);</script>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">style</span>&gt;</span><span class="language-css"><span class="hljs-selector-tag">p</span> { <span class="hljs-attribute">color</span>: blue; }</span><span class="hljs-tag">&lt;/<span class="hljs-name">style</span>&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">script</span>&gt;</span><span class="language-javascript"><span class="hljs-title function_">alert</span>(<span class="hljs-number">1</span>);</span><span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span>
""");
    }

    [Fact]
    public void Composite_IndexCshtml()
    {
        AssertHighlighter("razor",
"""
@page
@model IndexModel
@{
    ViewData["Title"] = "Home";
}

<h1>@ViewData["Title"]</h1>

<p>Hello, @User.Identity?.Name!</p>

@if (Model.Items.Any())
{
    <ul>
        @foreach (var item in Model.Items)
        {
            <li>
                <a asp-page="/Items/Details" asp-route-id="@item.Id">@item.Name</a>
                <small>@item.CreatedAt.ToShortDateString()</small>
            </li>
        }
    </ul>
}
else
{
    <p>No items yet.</p>
}
""",
"""
<span class="hljs-built_in">@page</span><span class="hljs-type">
</span><span class="hljs-built_in">@model</span><span class="hljs-type"> IndexModel</span>
<span class="hljs-built_in">@{</span><span class="language-csharp">
    ViewData[<span class="hljs-string">&quot;Title&quot;</span>] = <span class="hljs-string">&quot;Home&quot;</span>;
</span><span class="hljs-built_in">}</span>

<span class="hljs-tag">&lt;<span class="hljs-name">h1</span>&gt;</span><span class="hljs-built_in">@</span><span class="language-csharp">ViewData[<span class="hljs-string">&quot;Title&quot;</span>]</span><span class="hljs-tag">&lt;/<span class="hljs-name">h1</span>&gt;</span>

<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>Hello, <span class="hljs-built_in">@</span><span class="language-csharp">User.Identity?.Name!</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>

<span class="hljs-built_in">@</span><span class="language-csharp"><span class="hljs-keyword">if</span> (Model.Items.Any())
</span><span class="hljs-built_in">{</span><span class="language-csharp">
    </span><span class="hljs-tag">&lt;<span class="hljs-name">ul</span>&gt;</span><span class="language-csharp">
        </span><span class="hljs-built_in">@</span><span class="language-csharp"><span class="hljs-keyword">foreach</span> (<span class="hljs-keyword">var</span> item <span class="hljs-keyword">in</span> Model.Items)
        </span><span class="hljs-built_in">{</span><span class="language-csharp">
            </span><span class="hljs-tag">&lt;<span class="hljs-name">li</span>&gt;</span><span class="language-csharp">
                </span><span class="hljs-tag">&lt;<span class="hljs-name">a</span> <span class="hljs-attr">asp-page</span>=<span class="hljs-string">&quot;/Items/Details&quot;</span> <span class="hljs-attr">asp-route-id</span>=<span class="hljs-string">&quot;<span class="hljs-built_in">@</span><span class="language-csharp">item.Id</span>&quot;</span>&gt;</span><span class="hljs-built_in">@</span><span class="language-csharp">item.Name</span><span class="hljs-tag">&lt;/<span class="hljs-name">a</span>&gt;</span><span class="language-csharp">
                </span><span class="hljs-tag">&lt;<span class="hljs-name">small</span>&gt;</span><span class="hljs-built_in">@</span><span class="language-csharp">item.CreatedAt.ToShortDateString()</span><span class="hljs-tag">&lt;/<span class="hljs-name">small</span>&gt;</span><span class="language-csharp">
            </span><span class="hljs-tag">&lt;/<span class="hljs-name">li</span>&gt;</span><span class="language-csharp">
        </span><span class="hljs-built_in">}</span>
    <span class="hljs-tag">&lt;/<span class="hljs-name">ul</span>&gt;</span>
}
else
{
    <span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>No items yet.<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
}
""");
    }

    [Fact]
    public void Composite_LayoutCshtml()
    {
        AssertHighlighter("razor",
"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <title>@ViewData["Title"] - MyApp</title>
    <link rel="stylesheet" href="~/css/site.css" asp-append-version="true" />
</head>
<body>
    <header>
        <nav>
            <a asp-area="" asp-page="/Index">Home</a>
            <a asp-area="" asp-page="/Privacy">Privacy</a>
        </nav>
    </header>

    <main>
        @RenderBody()
    </main>

    <footer>
        &copy; @DateTime.Now.Year - MyApp
    </footer>

    <script src="~/js/site.js" asp-append-version="true"></script>
    @await RenderSectionAsync("Scripts", required: false)
</body>
</html>
""",
"""
<span class="hljs-meta">&lt;!DOCTYPE html&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">html</span> <span class="hljs-attr">lang</span>=<span class="hljs-string">&quot;en&quot;</span>&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">head</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">meta</span> <span class="hljs-attr">charset</span>=<span class="hljs-string">&quot;utf-8&quot;</span> /&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">title</span>&gt;</span><span class="hljs-built_in">@</span><span class="language-csharp">ViewData[<span class="hljs-string">&quot;Title&quot;</span>]</span> - MyApp<span class="hljs-tag">&lt;/<span class="hljs-name">title</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">link</span> <span class="hljs-attr">rel</span>=<span class="hljs-string">&quot;stylesheet&quot;</span> <span class="hljs-attr">href</span>=<span class="hljs-string">&quot;~/css/site.css&quot;</span> <span class="hljs-attr">asp-append-version</span>=<span class="hljs-string">&quot;true&quot;</span> /&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">head</span>&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">body</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">header</span>&gt;</span>
        <span class="hljs-tag">&lt;<span class="hljs-name">nav</span>&gt;</span>
            <span class="hljs-tag">&lt;<span class="hljs-name">a</span> <span class="hljs-attr">asp-area</span>=<span class="hljs-string">&quot;&quot;</span> <span class="hljs-attr">asp-page</span>=<span class="hljs-string">&quot;/Index&quot;</span>&gt;</span>Home<span class="hljs-tag">&lt;/<span class="hljs-name">a</span>&gt;</span>
            <span class="hljs-tag">&lt;<span class="hljs-name">a</span> <span class="hljs-attr">asp-area</span>=<span class="hljs-string">&quot;&quot;</span> <span class="hljs-attr">asp-page</span>=<span class="hljs-string">&quot;/Privacy&quot;</span>&gt;</span>Privacy<span class="hljs-tag">&lt;/<span class="hljs-name">a</span>&gt;</span>
        <span class="hljs-tag">&lt;/<span class="hljs-name">nav</span>&gt;</span>
    <span class="hljs-tag">&lt;/<span class="hljs-name">header</span>&gt;</span>

    <span class="hljs-tag">&lt;<span class="hljs-name">main</span>&gt;</span>
        <span class="hljs-built_in">@</span><span class="language-csharp">RenderBody()</span>
    <span class="hljs-tag">&lt;/<span class="hljs-name">main</span>&gt;</span>

    <span class="hljs-tag">&lt;<span class="hljs-name">footer</span>&gt;</span>
        &amp;copy; <span class="hljs-built_in">@</span><span class="language-csharp">DateTime.Now.Year</span> - MyApp
    <span class="hljs-tag">&lt;/<span class="hljs-name">footer</span>&gt;</span>

    <span class="hljs-tag">&lt;<span class="hljs-name">script</span> <span class="hljs-attr">src</span>=<span class="hljs-string">&quot;~/js/site.js&quot;</span> <span class="hljs-attr">asp-append-version</span>=<span class="hljs-string">&quot;true&quot;</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span>
    <span class="hljs-built_in">@await </span><span class="language-csharp">RenderSectionAsync(<span class="hljs-string">&quot;Scripts&quot;</span>, </span>required: false)
<span class="hljs-tag">&lt;/<span class="hljs-name">body</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">html</span>&gt;</span>
""");
    }

    [Fact]
    public void Composite_BlazorComponent()
    {
        AssertHighlighter("razor",
"""
@page "/users"
@inject IUserService Users
@implements IDisposable

<h1>Users</h1>

@if (users is null)
{
    <p><em>Loading...</em></p>
}
else if (users.Count == 0)
{
    <p>No users found.</p>
}
else
{
    <ul>
        @foreach (var user in users)
        {
            <li @key="user.Id">
                <UserCard User="@user" OnEdit="@(() => Edit(user.Id))" />
            </li>
        }
    </ul>
}

@code {
    private List<User>? users;
    private CancellationTokenSource _cts = new();

    protected override async Task OnInitializedAsync()
    {
        users = await Users.GetAllAsync(_cts.Token);
    }

    private void Edit(int id)
    {
        // navigate
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
""",
"""
<span class="hljs-built_in">@page</span><span class="hljs-type"> &quot;/users&quot;</span>
<span class="hljs-built_in">@inject</span><span class="hljs-type"> IUserService Users</span>
<span class="hljs-built_in">@</span><span class="language-csharp">implements</span> IDisposable

<span class="hljs-tag">&lt;<span class="hljs-name">h1</span>&gt;</span>Users<span class="hljs-tag">&lt;/<span class="hljs-name">h1</span>&gt;</span>

<span class="hljs-built_in">@</span><span class="language-csharp"><span class="hljs-keyword">if</span> (users <span class="hljs-keyword">is</span> <span class="hljs-literal">null</span>)
</span><span class="hljs-built_in">{</span><span class="language-csharp">
    </span><span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">em</span>&gt;</span><span class="language-csharp">Loading...</span><span class="hljs-tag">&lt;/<span class="hljs-name">em</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span><span class="language-csharp">
</span><span class="hljs-built_in">}</span><span class="language-csharp">
<span class="hljs-keyword">else</span> <span class="hljs-keyword">if</span> (users.Count == <span class="hljs-number">0</span>)
</span><span class="hljs-built_in">{</span><span class="language-csharp">
    </span><span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="language-csharp">No users found.</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span><span class="language-csharp">
</span><span class="hljs-built_in">}</span><span class="language-csharp">
<span class="hljs-keyword">else</span>
</span><span class="hljs-built_in">{</span><span class="language-csharp">
    </span><span class="hljs-tag">&lt;<span class="hljs-name">ul</span>&gt;</span><span class="language-csharp">
        </span><span class="hljs-built_in">@</span><span class="language-csharp"><span class="hljs-keyword">foreach</span> (<span class="hljs-keyword">var</span> user <span class="hljs-keyword">in</span> users)
        </span><span class="hljs-built_in">{</span><span class="language-csharp">
            </span><span class="hljs-tag">&lt;<span class="hljs-name">li</span> @<span class="hljs-attr">key</span>=<span class="hljs-string">&quot;user.Id&quot;</span>&gt;</span><span class="language-csharp">
                </span><span class="hljs-tag">&lt;<span class="hljs-name">UserCard</span> <span class="hljs-attr">User</span>=<span class="hljs-string">&quot;<span class="hljs-built_in">@</span><span class="language-csharp">user</span>&quot;</span> <span class="hljs-attr">OnEdit</span>=<span class="hljs-string">&quot;<span class="hljs-built_in">@(</span><span class="language-csharp">()</span><span class="language-csharp"> =&gt; Edit</span><span class="language-csharp">(user.Id)</span><span class="hljs-built_in">)</span>&quot;</span> /&gt;</span><span class="language-csharp">
            </span><span class="hljs-tag">&lt;/<span class="hljs-name">li</span>&gt;</span><span class="language-csharp">
        </span><span class="hljs-built_in">}</span>
    <span class="hljs-tag">&lt;/<span class="hljs-name">ul</span>&gt;</span>
}

<span class="hljs-built_in">@code {</span><span class="language-csharp">
    <span class="hljs-keyword">private</span> List</span><span class="hljs-tag">&lt;<span class="hljs-name">User</span>&gt;</span><span class="language-csharp">? users;
    <span class="hljs-keyword">private</span> CancellationTokenSource _cts = <span class="hljs-keyword">new</span>();

    <span class="hljs-function"><span class="hljs-keyword">protected</span> <span class="hljs-keyword">override</span> <span class="hljs-keyword">async</span> Task <span class="hljs-title">OnInitializedAsync</span>()</span>
    {
        users = <span class="hljs-keyword">await</span> Users.GetAllAsync(_cts.Token);
    }

    <span class="hljs-function"><span class="hljs-keyword">private</span> <span class="hljs-keyword">void</span> <span class="hljs-title">Edit</span>(<span class="hljs-params"><span class="hljs-built_in">int</span> id</span>)</span>
    {
        <span class="hljs-comment">// navigate</span>
    }

    <span class="hljs-function"><span class="hljs-keyword">public</span> <span class="hljs-keyword">void</span> <span class="hljs-title">Dispose</span>()</span>
    {
        _cts.Cancel();
        _cts.Dispose();
    }
</span><span class="hljs-built_in">}</span>
""");
    }

    [Fact]
    public void Composite_EditFormCshtml()
    {
        AssertHighlighter("razor",
"""
@model UserEditViewModel
@{
    ViewData["Title"] = "Edit user";
}

<form asp-action="Save" asp-controller="Users" method="post">
    <div asp-validation-summary="ModelOnly" class="text-danger"></div>

    <div class="form-group">
        <label asp-for="UserName"></label>
        <input asp-for="UserName" class="form-control" />
        <span asp-validation-for="UserName" class="text-danger"></span>
    </div>

    <div class="form-group">
        <label asp-for="Email"></label>
        <input asp-for="Email" class="form-control" />
        <span asp-validation-for="Email" class="text-danger"></span>
    </div>

    <button type="submit" class="btn btn-primary">Save</button>
    <a asp-action="Index" class="btn btn-link">Cancel</a>
</form>
""",
"""
<span class="hljs-built_in">@model</span><span class="hljs-type"> UserEditViewModel</span>
<span class="hljs-built_in">@{</span><span class="language-csharp">
    ViewData[<span class="hljs-string">&quot;Title&quot;</span>] = <span class="hljs-string">&quot;Edit user&quot;</span>;
</span><span class="hljs-built_in">}</span>

<span class="hljs-tag">&lt;<span class="hljs-name">form</span> <span class="hljs-attr">asp-action</span>=<span class="hljs-string">&quot;Save&quot;</span> <span class="hljs-attr">asp-controller</span>=<span class="hljs-string">&quot;Users&quot;</span> <span class="hljs-attr">method</span>=<span class="hljs-string">&quot;post&quot;</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">div</span> <span class="hljs-attr">asp-validation-summary</span>=<span class="hljs-string">&quot;ModelOnly&quot;</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;text-danger&quot;</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">div</span>&gt;</span>

    <span class="hljs-tag">&lt;<span class="hljs-name">div</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;form-group&quot;</span>&gt;</span>
        <span class="hljs-tag">&lt;<span class="hljs-name">label</span> <span class="hljs-attr">asp-for</span>=<span class="hljs-string">&quot;UserName&quot;</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">label</span>&gt;</span>
        <span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">asp-for</span>=<span class="hljs-string">&quot;UserName&quot;</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;form-control&quot;</span> /&gt;</span>
        <span class="hljs-tag">&lt;<span class="hljs-name">span</span> <span class="hljs-attr">asp-validation-for</span>=<span class="hljs-string">&quot;UserName&quot;</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;text-danger&quot;</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">span</span>&gt;</span>
    <span class="hljs-tag">&lt;/<span class="hljs-name">div</span>&gt;</span>

    <span class="hljs-tag">&lt;<span class="hljs-name">div</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;form-group&quot;</span>&gt;</span>
        <span class="hljs-tag">&lt;<span class="hljs-name">label</span> <span class="hljs-attr">asp-for</span>=<span class="hljs-string">&quot;Email&quot;</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">label</span>&gt;</span>
        <span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">asp-for</span>=<span class="hljs-string">&quot;Email&quot;</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;form-control&quot;</span> /&gt;</span>
        <span class="hljs-tag">&lt;<span class="hljs-name">span</span> <span class="hljs-attr">asp-validation-for</span>=<span class="hljs-string">&quot;Email&quot;</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;text-danger&quot;</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">span</span>&gt;</span>
    <span class="hljs-tag">&lt;/<span class="hljs-name">div</span>&gt;</span>

    <span class="hljs-tag">&lt;<span class="hljs-name">button</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;submit&quot;</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;btn btn-primary&quot;</span>&gt;</span>Save<span class="hljs-tag">&lt;/<span class="hljs-name">button</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">a</span> <span class="hljs-attr">asp-action</span>=<span class="hljs-string">&quot;Index&quot;</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;btn btn-link&quot;</span>&gt;</span>Cancel<span class="hljs-tag">&lt;/<span class="hljs-name">a</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">form</span>&gt;</span>
""");
    }

    [Fact]
    public void SpecialEdge_Empty()
    {
        AssertHighlighter("razor",
"""

""",
"""

""");
    }

    [Fact]
    public void SpecialEdge_OnlyDirective()
    {
        AssertHighlighter("razor",
"""
@page "/"
""",
"""
<span class="hljs-built_in">@page</span><span class="hljs-type"> &quot;/&quot;</span>
""");
    }

    [Fact]
    public void SpecialEdge_OnlyExpression()
    {
        AssertHighlighter("razor",
"""
@DateTime.Now
""",
"""
<span class="hljs-built_in">@</span><span class="language-csharp">DateTime.Now</span>
""");
    }

    [Fact]
    public void SpecialEdge_OnlyHtml()
    {
        AssertHighlighter("razor",
"""
<h1>Hello</h1>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">h1</span>&gt;</span>Hello<span class="hljs-tag">&lt;/<span class="hljs-name">h1</span>&gt;</span>
""");
    }

    [Fact]
    public void SpecialEdge_OnlyCodeBlock()
    {
        AssertHighlighter("razor",
"""
@{ var x = 1; }
""",
"""
<span class="hljs-built_in">@{</span><span class="language-csharp"> <span class="hljs-keyword">var</span> x = <span class="hljs-number">1</span>; </span><span class="hljs-built_in">}</span>
""");
    }

    [Fact]
    public void SpecialEdge_OnlyComment()
    {
        AssertHighlighter("razor",
"""
@* nothing here *@
""",
"""
<span class="hljs-comment">@* nothing here *@</span>
""");
    }
}
