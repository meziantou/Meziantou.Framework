namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class HandlebarsHighlighterTests
{
    [Fact]
    public void Expressions()
    {
        AssertHighlighter("handlebars",
"""
<div class="entry">
  <h1>{{title}}</h1>
  <div class="body">
    {{{body}}}
  </div>
</div>
""",
"""
<span class="language-html"><span class="hljs-tag">&lt;<span class="hljs-name">div</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;entry&quot;</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">h1</span>&gt;</span></span><span class="hljs-template-variable">{{<span class="hljs-name">title</span>}}</span><span class="language-html"><span class="hljs-tag">&lt;/<span class="hljs-name">h1</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">div</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;body&quot;</span>&gt;</span>
    </span><span class="hljs-template-variable">{{{<span class="hljs-name">body</span>}}}</span><span class="language-html">
  <span class="hljs-tag">&lt;/<span class="hljs-name">div</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">div</span>&gt;</span></span>
""");
    }

    [Fact]
    public void EachWithElse()
    {
        AssertHighlighter("handlebars",
"""
<ul class="people_list">
  {{#each people}}
    <li>{{this}}</li>
  {{else}}
    <li>No people</li>
  {{/each}}
</ul>
""",
"""
<span class="language-html"><span class="hljs-tag">&lt;<span class="hljs-name">ul</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;people_list&quot;</span>&gt;</span>
  </span><span class="hljs-template-tag">{{#<span class="hljs-name"><span class="hljs-built_in">each</span></span> people}}</span><span class="language-html">
    <span class="hljs-tag">&lt;<span class="hljs-name">li</span>&gt;</span></span><span class="hljs-template-variable">{{<span class="hljs-name">this</span>}}</span><span class="language-html"><span class="hljs-tag">&lt;/<span class="hljs-name">li</span>&gt;</span>
  </span><span class="hljs-template-tag">{{<span class="hljs-keyword">else</span>}}</span><span class="language-html">
    <span class="hljs-tag">&lt;<span class="hljs-name">li</span>&gt;</span>No people<span class="hljs-tag">&lt;/<span class="hljs-name">li</span>&gt;</span>
  </span><span class="hljs-template-tag">{{/<span class="hljs-name"><span class="hljs-built_in">each</span></span>}}</span><span class="language-html">
<span class="hljs-tag">&lt;/<span class="hljs-name">ul</span>&gt;</span></span>
""");
    }

    [Fact]
    public void IfElseIf()
    {
        AssertHighlighter("handlebars",
"""
{{#if isActive}}
  <img src="star.gif" alt="Active">
{{else if isInactive}}
  <img src="cry.gif" alt="Inactive">
{{else}}
  <p>Unknown</p>
{{/if}}
""",
"""
<span class="hljs-template-tag">{{#<span class="hljs-name"><span class="hljs-built_in">if</span></span> isActive}}</span><span class="language-html">
  <span class="hljs-tag">&lt;<span class="hljs-name">img</span> <span class="hljs-attr">src</span>=<span class="hljs-string">&quot;star.gif&quot;</span> <span class="hljs-attr">alt</span>=<span class="hljs-string">&quot;Active&quot;</span>&gt;</span>
</span><span class="hljs-template-tag">{{<span class="hljs-keyword">else</span> <span class="hljs-keyword">if</span> isInactive}}</span><span class="language-html">
  <span class="hljs-tag">&lt;<span class="hljs-name">img</span> <span class="hljs-attr">src</span>=<span class="hljs-string">&quot;cry.gif&quot;</span> <span class="hljs-attr">alt</span>=<span class="hljs-string">&quot;Inactive&quot;</span>&gt;</span>
</span><span class="hljs-template-tag">{{<span class="hljs-keyword">else</span>}}</span><span class="language-html">
  <span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>Unknown<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
</span><span class="hljs-template-tag">{{/<span class="hljs-name"><span class="hljs-built_in">if</span></span>}}</span>
""");
    }

    [Fact]
    public void HelpersAndBlockParameters()
    {
        AssertHighlighter("handlebars",
"""
{{link "See Website" url}}
{{loud lastname}}
{{format-date date format="YYYY-MM-DD" locale='fr' count=3}}
{{outer-helper (inner-helper 'abc') 'def'}}
{{#with city as | city |}}{{city.name}} ({{city.country}}){{/with}}
{{> userMessage tagName="h2" }}
{{#each items as |item index|}}{{index}}: {{item}}{{/each}}
""",
"""
<span class="hljs-template-variable">{{<span class="hljs-name">link</span> <span class="hljs-string">&quot;See Website&quot;</span> url}}</span><span class="language-html">
</span><span class="hljs-template-variable">{{<span class="hljs-name">loud</span> lastname}}</span><span class="language-html">
</span><span class="hljs-template-variable">{{<span class="hljs-name">format-date</span> date <span class="hljs-attr">format</span>=<span class="hljs-string">&quot;YYYY-MM-DD&quot;</span> <span class="hljs-attr">locale</span>=<span class="hljs-string">&#x27;fr&#x27;</span> <span class="hljs-attr">count</span>=<span class="hljs-number">3</span>}}</span><span class="language-html">
</span><span class="hljs-template-variable">{{<span class="hljs-name">outer-helper</span> (<span class="hljs-name">inner-helper</span> <span class="hljs-string">&#x27;abc&#x27;</span>) <span class="hljs-string">&#x27;def&#x27;</span>}}</span><span class="language-html">
</span><span class="hljs-template-tag">{{#<span class="hljs-name"><span class="hljs-built_in">with</span></span> city <span class="hljs-keyword">as</span> | city |}}</span><span class="hljs-template-variable">{{<span class="hljs-name">city.name</span>}}</span><span class="language-html"> (</span><span class="hljs-template-variable">{{<span class="hljs-name">city.country</span>}}</span><span class="language-html">)</span><span class="hljs-template-tag">{{/<span class="hljs-name"><span class="hljs-built_in">with</span></span>}}</span><span class="language-html">
</span><span class="hljs-template-variable">{{&gt; <span class="hljs-name">userMessage</span> <span class="hljs-attr">tagName</span>=<span class="hljs-string">&quot;h2&quot;</span> }}</span><span class="language-html">
</span><span class="hljs-template-tag">{{#<span class="hljs-name"><span class="hljs-built_in">each</span></span> items <span class="hljs-keyword">as</span> |item index|}}</span><span class="hljs-template-variable">{{<span class="hljs-name">index</span>}}</span><span class="language-html">: </span><span class="hljs-template-variable">{{<span class="hljs-name">item</span>}}</span><span class="hljs-template-tag">{{/<span class="hljs-name"><span class="hljs-built_in">each</span></span>}}</span>
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("handlebars",
"""
{{! This comment will not show up in the output}}
<!-- This comment will show up as HTML-comment -->
{{!-- This comment may contain mustaches like }} --}}
{{!-- TODO: fix this --}}
""",
"""
<span class="hljs-comment">{{! This comment will not show up in the output}}</span><span class="language-html">
<span class="hljs-comment">&lt;!-- This comment will show up as HTML-comment --&gt;</span>
</span><span class="hljs-comment">{{!-- This comment may contain mustaches like }} --}}</span><span class="language-html">
</span><span class="hljs-comment">{{!-- <span class="hljs-doctag">TODO:</span> fix this --}}</span>
""");
    }

    [Fact]
    public void Paths()
    {
        AssertHighlighter("handlebars",
"""
{{person.firstname}} {{./name}} {{../permalink}} {{this/name}} {{[foo bar]}} {{"quoted id"}} {{@index}} {{@root.title}}
{{article.[title with space]}} {{array.[0].item}}
""",
"""
<span class="hljs-template-variable">{{<span class="hljs-name">person.firstname</span>}}</span><span class="language-html"> </span><span class="hljs-template-variable">{{<span class="hljs-name">./name</span>}}</span><span class="language-html"> </span><span class="hljs-template-variable">{{.<span class="hljs-name">./permalink</span>}}</span><span class="language-html"> </span><span class="hljs-template-variable">{{<span class="hljs-name">this/name</span>}}</span><span class="language-html"> </span><span class="hljs-template-variable">{{<span class="hljs-name">[foo bar]</span>}}</span><span class="language-html"> </span><span class="hljs-template-variable">{{<span class="hljs-name">&quot;quoted id&quot;</span>}}</span><span class="language-html"> </span><span class="hljs-template-variable">{{@<span class="hljs-name">index</span>}}</span><span class="language-html"> </span><span class="hljs-template-variable">{{@<span class="hljs-name">root.title</span>}}</span><span class="language-html">
</span><span class="hljs-template-variable">{{<span class="hljs-name">article.[title <span class="hljs-built_in">with</span> space]</span>}}</span><span class="language-html"> </span><span class="hljs-template-variable">{{<span class="hljs-name">array.[0].item</span>}}</span>
""");
    }

    [Fact]
    public void RawBlock()
    {
        AssertHighlighter("handlebars",
"""
{{{{raw-helper}}}}
  {{bar}} <b>bold</b>
{{{{/raw-helper}}}}
<p>after</p>
""",
"""
<span class="hljs-template-tag">{{{{<span class="hljs-name">raw-helper</span>}}}}</span><span class="language-html">
  {{bar}} <span class="hljs-tag">&lt;<span class="hljs-name">b</span>&gt;</span>bold<span class="hljs-tag">&lt;/<span class="hljs-name">b</span>&gt;</span>
</span><span class="hljs-template-tag">{{{{/<span class="hljs-name">raw-helper</span>}}}}</span><span class="language-html">
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>after<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span></span>
""");
    }

    [Fact]
    public void EscapedMustache()
    {
        AssertHighlighter("handlebars",
"""
\{{escaped}} \\{{notEscaped}} {{normal}}
""",
"""
<span class="language-html">\{{escaped}} \\</span><span class="hljs-template-variable">{{<span class="hljs-name">notEscaped</span>}}</span><span class="language-html"> </span><span class="hljs-template-variable">{{<span class="hljs-name">normal</span>}}</span>
""");
    }

    [Fact]
    public void MustacheInAttributeValue()
    {
        AssertHighlighter("handlebars",
"""
<a href="{{url}}" class="btn {{#if active}}active{{/if}}">{{text}}</a>
<input type="checkbox" {{#if checked}}checked{{/if}} value='{{value}}'>
""",
"""
<span class="language-html"><span class="hljs-tag">&lt;<span class="hljs-name">a</span> <span class="hljs-attr">href</span>=<span class="hljs-string">&quot;</span></span></span><span class="hljs-template-variable">{{<span class="hljs-name">url</span>}}</span><span class="language-html"><span class="hljs-tag"><span class="hljs-string">&quot;</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;btn </span></span></span><span class="hljs-template-tag">{{#<span class="hljs-name"><span class="hljs-built_in">if</span></span> active}}</span><span class="language-html"><span class="hljs-tag"><span class="hljs-string">active</span></span></span><span class="hljs-template-tag">{{/<span class="hljs-name"><span class="hljs-built_in">if</span></span>}}</span><span class="language-html"><span class="hljs-tag"><span class="hljs-string">&quot;</span>&gt;</span></span><span class="hljs-template-variable">{{<span class="hljs-name">text</span>}}</span><span class="language-html"><span class="hljs-tag">&lt;/<span class="hljs-name">a</span>&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;checkbox&quot;</span> </span></span><span class="hljs-template-tag">{{#<span class="hljs-name"><span class="hljs-built_in">if</span></span> checked}}</span><span class="language-html"><span class="hljs-tag"><span class="hljs-attr">checked</span></span></span><span class="hljs-template-tag">{{/<span class="hljs-name"><span class="hljs-built_in">if</span></span>}}</span><span class="language-html"><span class="hljs-tag"> <span class="hljs-attr">value</span>=<span class="hljs-string">&#x27;</span></span></span><span class="hljs-template-variable">{{<span class="hljs-name">value</span>}}</span><span class="language-html"><span class="hljs-tag"><span class="hljs-string">&#x27;</span>&gt;</span></span>
""");
    }

    [Fact]
    public void MustacheInScriptAndStyle()
    {
        AssertHighlighter("handlebars",
"""
<script type="text/javascript">
  var name = "{{name}}";
  var count = {{count}};
  if (count > 1) { console.log(name); }
</script>
<style>body { color: {{color}}; }</style>
""",
"""
<span class="language-html"><span class="hljs-tag">&lt;<span class="hljs-name">script</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;text/javascript&quot;</span>&gt;</span><span class="language-javascript">
  <span class="hljs-keyword">var</span> name = <span class="hljs-string">&quot;</span></span></span><span class="hljs-template-variable">{{<span class="hljs-name">name</span>}}</span><span class="language-html"><span class="language-javascript"><span class="hljs-string">&quot;</span>;
  <span class="hljs-keyword">var</span> count = </span></span><span class="hljs-template-variable">{{<span class="hljs-name">count</span>}}</span><span class="language-html"><span class="language-javascript">;
  <span class="hljs-keyword">if</span> (count &gt; <span class="hljs-number">1</span>) { <span class="hljs-variable language_">console</span>.<span class="hljs-title function_">log</span>(name); }
</span><span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">style</span>&gt;</span><span class="language-css"><span class="hljs-selector-tag">body</span> { <span class="hljs-attribute">color</span>: </span></span><span class="hljs-template-variable">{{<span class="hljs-name">color</span>}}</span><span class="language-html"><span class="language-css">; }</span><span class="hljs-tag">&lt;/<span class="hljs-name">style</span>&gt;</span></span>
""");
    }

    [Fact]
    public void Literals()
    {
        AssertHighlighter("handlebars",
"""
{{helper true false null undefined 42 -1.5 "str" 'apos'}}
{{#if (eq value true)}}yes{{/if}}
""",
"""
<span class="hljs-template-variable">{{<span class="hljs-name">helper</span> <span class="hljs-literal">true</span> <span class="hljs-literal">false</span> <span class="hljs-literal">null</span> <span class="hljs-literal">undefined</span> <span class="hljs-number">42</span> -1.5 <span class="hljs-string">&quot;str&quot;</span> <span class="hljs-string">&#x27;apos&#x27;</span>}}</span><span class="language-html">
</span><span class="hljs-template-tag">{{#<span class="hljs-name"><span class="hljs-built_in">if</span></span> (<span class="hljs-name">eq</span> value <span class="hljs-literal">true</span>)}}</span><span class="language-html">yes</span><span class="hljs-template-tag">{{/<span class="hljs-name"><span class="hljs-built_in">if</span></span>}}</span>
""");
    }

    [Fact]
    public void HyphenatedBuiltIns()
    {
        AssertHighlighter("handlebars",
"""
{{#link-to "posts" class="nav"}}Posts{{/link-to}}
{{input type="text" value=name placeholder="Name"}}
{{yield}} {{outlet}} {{action "save" model}} {{component "my-comp" title=(concat "a" b)}}
{{#each-in categories as |category products|}}{{category}}{{/each-in}}
""",
"""
<span class="hljs-template-tag">{{#<span class="hljs-name"><span class="hljs-built_in">link-to</span></span> <span class="hljs-string">&quot;posts&quot;</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;nav&quot;</span>}}</span><span class="language-html">Posts</span><span class="hljs-template-tag">{{/<span class="hljs-name"><span class="hljs-built_in">link-to</span></span>}}</span><span class="language-html">
</span><span class="hljs-template-variable">{{<span class="hljs-name"><span class="hljs-built_in">input</span></span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;text&quot;</span> <span class="hljs-attr">value</span>=name <span class="hljs-attr">placeholder</span>=<span class="hljs-string">&quot;Name&quot;</span>}}</span><span class="language-html">
</span><span class="hljs-template-variable">{{<span class="hljs-name"><span class="hljs-built_in">yield</span></span>}}</span><span class="language-html"> </span><span class="hljs-template-variable">{{<span class="hljs-name"><span class="hljs-built_in">outlet</span></span>}}</span><span class="language-html"> </span><span class="hljs-template-variable">{{<span class="hljs-name"><span class="hljs-built_in">action</span></span> <span class="hljs-string">&quot;save&quot;</span> model}}</span><span class="language-html"> </span><span class="hljs-template-variable">{{<span class="hljs-name"><span class="hljs-built_in">component</span></span> <span class="hljs-string">&quot;my-comp&quot;</span> <span class="hljs-attr">title</span>=(<span class="hljs-name"><span class="hljs-built_in">concat</span></span> <span class="hljs-string">&quot;a&quot;</span> b)}}</span><span class="language-html">
</span><span class="hljs-template-tag">{{#<span class="hljs-name"><span class="hljs-built_in">each-in</span></span> categories <span class="hljs-keyword">as</span> |category products|}}</span><span class="hljs-template-variable">{{<span class="hljs-name">category</span>}}</span><span class="hljs-template-tag">{{/<span class="hljs-name"><span class="hljs-built_in">each-in</span></span>}}</span>
""");
    }

    [Fact]
    public void CaseInsensitive()
    {
        AssertHighlighter("handlebars",
"""
{{#EACH items}}{{IF x}}{{/EACH}} {{TRUE}} {{helper TRUE}}
""",
"""
<span class="hljs-template-tag">{{#<span class="hljs-name"><span class="hljs-built_in">EACH</span></span> items}}</span><span class="hljs-template-variable">{{<span class="hljs-name"><span class="hljs-built_in">IF</span></span> x}}</span><span class="hljs-template-tag">{{/<span class="hljs-name"><span class="hljs-built_in">EACH</span></span>}}</span><span class="language-html"> </span><span class="hljs-template-variable">{{<span class="hljs-name">TRUE</span>}}</span><span class="language-html"> </span><span class="hljs-template-variable">{{<span class="hljs-name">helper</span> <span class="hljs-literal">TRUE</span>}}</span>
""");
    }

    [Fact]
    public void UnterminatedMustache()
    {
        AssertHighlighter("handlebars",
"""
<p>{{#if foo</p>
<div>{{name</div>
""",
"""
<span class="language-html"><span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span></span><span class="hljs-template-tag">{{#<span class="hljs-name"><span class="hljs-built_in">if</span></span> foo&lt;/p&gt;
&lt;div&gt;{{name&lt;/div&gt;</span>
""");
    }

    [Fact]
    public void NestedSubExpressions()
    {
        AssertHighlighter("handlebars",
"""
{{helper (outer (inner a "b" key=c) d) e=(f g)}}
""",
"""
<span class="hljs-template-variable">{{<span class="hljs-name">helper</span> (<span class="hljs-name">outer</span> (<span class="hljs-name">inner</span> a <span class="hljs-string">&quot;b&quot;</span> <span class="hljs-attr">key</span>=c) d) <span class="hljs-attr">e</span>=(<span class="hljs-name">f</span> g)}}</span>
""");
    }

    [Fact]
    public void HashValues()
    {
        AssertHighlighter("handlebars",
"""
{{my-helper key1=1 key2="two" key3='three' key4=value key5=(sub x) key6=true}}
""",
"""
<span class="hljs-template-variable">{{<span class="hljs-name">my-helper</span> <span class="hljs-attr">key1</span>=<span class="hljs-number">1</span> <span class="hljs-attr">key2</span>=<span class="hljs-string">&quot;two&quot;</span> <span class="hljs-attr">key3</span>=<span class="hljs-string">&#x27;three&#x27;</span> <span class="hljs-attr">key4</span>=value <span class="hljs-attr">key5</span>=(<span class="hljs-name">sub</span> x) <span class="hljs-attr">key6</span>=<span class="hljs-literal">true</span>}}</span>
""");
    }

    [Fact]
    public void PartialBlock()
    {
        AssertHighlighter("handlebars",
"""
{{#> layout title="Home"}}
  <p>content</p>
{{/layout}}
{{#*inline "myPartial"}}My Content{{/inline}}
""",
"""
<span class="hljs-template-tag">{{#&gt; <span class="hljs-name">layout</span> <span class="hljs-attr">title</span>=<span class="hljs-string">&quot;Home&quot;</span>}}</span><span class="language-html">
  <span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>content<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
</span><span class="hljs-template-tag">{{/<span class="hljs-name">layout</span>}}</span><span class="language-html">
</span><span class="hljs-template-tag">{{#*<span class="hljs-name">inline</span> <span class="hljs-string">&quot;myPartial&quot;</span>}}</span><span class="language-html">My Content</span><span class="hljs-template-tag">{{/<span class="hljs-name">inline</span>}}</span>
""");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("handlebars",
"""
<p class="é">{{prénom}} — {{名前}}</p>
""",
"""
<span class="language-html"><span class="hljs-tag">&lt;<span class="hljs-name">p</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;é&quot;</span>&gt;</span></span><span class="hljs-template-variable">{{<span class="hljs-name">prénom</span>}}</span><span class="language-html"> — </span><span class="hljs-template-variable">{{<span class="hljs-name">名前</span>}}</span><span class="language-html"><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span></span>
""");
    }

    [Fact]
    public void CrLf()
    {
        AssertHighlighter("handlebars",
"<p>\r\n{{name}}\r\n</p>",
"<span class=\"language-html\"><span class=\"hljs-tag\">&lt;<span class=\"hljs-name\">p</span>&gt;</span>\r\n</span><span class=\"hljs-template-variable\">{{<span class=\"hljs-name\">name</span>}}</span><span class=\"language-html\">\r\n<span class=\"hljs-tag\">&lt;/<span class=\"hljs-name\">p</span>&gt;</span></span>");
    }

    [Fact]
    public void TagInTagName()
    {
        AssertHighlighter("handlebars",
"""
<{{tag}} class="x">content</{{tag}}>
""",
"""
<span class="language-html">&lt;</span><span class="hljs-template-variable">{{<span class="hljs-name">tag</span>}}</span><span class="language-html"> class=&quot;x&quot;&gt;content&lt;/</span><span class="hljs-template-variable">{{<span class="hljs-name">tag</span>}}</span><span class="language-html">&gt;</span>
""");
    }

    [Fact]
    public void HbsAlias()
    {
        AssertHighlighter("hbs",
"""
{{#unless x}}a{{else}}b{{/unless}} {{else}} {{else if y}}
""",
"""
<span class="hljs-template-tag">{{#<span class="hljs-name"><span class="hljs-built_in">unless</span></span> x}}</span><span class="language-html">a</span><span class="hljs-template-tag">{{<span class="hljs-keyword">else</span>}}</span><span class="language-html">b</span><span class="hljs-template-tag">{{/<span class="hljs-name"><span class="hljs-built_in">unless</span></span>}}</span><span class="language-html"> </span><span class="hljs-template-tag">{{<span class="hljs-keyword">else</span>}}</span><span class="language-html"> </span><span class="hljs-template-tag">{{<span class="hljs-keyword">else</span> <span class="hljs-keyword">if</span> y}}</span>
""");
    }

    [Fact]
    public void MustacheAlias()
    {
        AssertHighlighter("mustache",
"""
<div class="entry">
  <h1>{{title}}</h1>
  <div class="body">
    {{{body}}}
  </div>
</div>
""",
"""
<span class="language-html"><span class="hljs-tag">&lt;<span class="hljs-name">div</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;entry&quot;</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">h1</span>&gt;</span></span><span class="hljs-template-variable">{{<span class="hljs-name">title</span>}}</span><span class="language-html"><span class="hljs-tag">&lt;/<span class="hljs-name">h1</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">div</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;body&quot;</span>&gt;</span>
    </span><span class="hljs-template-variable">{{{<span class="hljs-name">body</span>}}}</span><span class="language-html">
  <span class="hljs-tag">&lt;/<span class="hljs-name">div</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">div</span>&gt;</span></span>
""");
    }
}
