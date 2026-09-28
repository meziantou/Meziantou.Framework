namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class HamlHighlighterTests
{
    [Fact]
    public void Doctype()
    {
        AssertHighlighter("haml",
"""
!!! 5
!!! XML
!!!
!!! Strict
""",
"""
<span class="hljs-meta">!!! 5</span>
<span class="hljs-meta">!!! XML</span>
<span class="hljs-meta">!!!</span>
<span class="hljs-meta">!!! Strict</span>
""");
    }

    [Fact]
    public void Page()
    {
        AssertHighlighter("haml",
"""
!!! 5
%html
  %head
    %title= @title
    %meta{:charset => "utf-8"}
    %link{:rel => 'stylesheet', :href => '/css/app.css'}
  %body
    #content.container
      %h1.title Hello, #{@user.name}!
      - if @items.any?
        %ul
          - @items.each do |item|
            %li= item.name
      - else
        %p No items
      = render 'footer'
      != raw_html
      ~ preserve_me
""",
"""
<span class="hljs-meta">!!! 5</span>
<span class="hljs-tag">%<span class="hljs-selector-tag">html</span></span>
<span class="hljs-tag">  %<span class="hljs-selector-tag">head</span></span>
<span class="hljs-tag">    %<span class="hljs-selector-tag">title</span>=<span class="language-ruby"> @title</span></span>
<span class="hljs-tag">    %<span class="hljs-selector-tag">meta</span>{<span class="hljs-attr">:charset</span> =&gt; <span class="hljs-string">&quot;utf-8&quot;</span>}</span>
<span class="hljs-tag">    %<span class="hljs-selector-tag">link</span>{<span class="hljs-attr">:rel</span> =&gt; <span class="hljs-string">&#x27;stylesheet&#x27;</span>, <span class="hljs-attr">:href</span> =&gt; <span class="hljs-string">&#x27;/css/app.css&#x27;</span>}</span>
<span class="hljs-tag">  %<span class="hljs-selector-tag">body</span></span>
<span class="hljs-tag">    <span class="hljs-selector-id">#content</span><span class="hljs-selector-class">.container</span></span>
<span class="hljs-tag">      %<span class="hljs-selector-tag">h1</span><span class="hljs-selector-class">.title</span></span> Hello, #{<span class="language-ruby"><span class="hljs-variable">@user</span>.name</span>}!
      -<span class="language-ruby"> <span class="hljs-keyword">if</span> <span class="hljs-variable">@items</span>.any?</span>
<span class="hljs-tag">        %<span class="hljs-selector-tag">ul</span></span>
          -<span class="language-ruby"> <span class="hljs-variable">@items</span>.each <span class="hljs-keyword">do</span> |<span class="hljs-params">item</span>|</span>
<span class="hljs-tag">            %<span class="hljs-selector-tag">li</span>=<span class="language-ruby"> item.name</span></span>
      -<span class="language-ruby"> <span class="hljs-keyword">else</span></span>
<span class="hljs-tag">        %<span class="hljs-selector-tag">p</span></span> No items
      =<span class="language-ruby"> render <span class="hljs-string">&#x27;footer&#x27;</span></span>
      !=<span class="language-ruby"> raw_html</span>
      ~ preserve_me
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("haml",
"""
-# a silent comment
/ an html comment
  %p nested
=# ruby comment
!=# other comment
%p after
""",
"""
<span class="hljs-comment">-# a silent comment</span>
<span class="hljs-comment">/ an html comment
  %p nested</span>
<span class="hljs-comment">=# ruby comment</span>
<span class="hljs-comment">!=# other comment</span>
<span class="hljs-tag">%<span class="hljs-selector-tag">p</span></span> after
""");
    }

    [Fact]
    public void Attributes()
    {
        AssertHighlighter("haml",
"""
%a{:href => "/", :title => 'Home', :class => active}
%a(href="/" title='Home' data=value)
%input(type="checkbox" checked)
%div{:id => [@item.type, @item.number], :class => [@item.type, @item.urgency]}
%a{href: "/", title: "Home"}
""",
"""
<span class="hljs-tag">%<span class="hljs-selector-tag">a</span>{<span class="hljs-attr">:href</span> =&gt; <span class="hljs-string">&quot;/&quot;</span>, <span class="hljs-attr">:title</span> =&gt; <span class="hljs-string">&#x27;Home&#x27;</span>, <span class="hljs-attr">:class</span> =&gt; active}</span>
<span class="hljs-tag">%<span class="hljs-selector-tag">a</span>(<span class="hljs-attr">href</span>=<span class="hljs-string">&quot;/&quot;</span> <span class="hljs-attr">title</span>=<span class="hljs-string">&#x27;Home&#x27;</span> <span class="hljs-attr">data</span>=value)</span>
<span class="hljs-tag">%<span class="hljs-selector-tag">input</span>(<span class="hljs-attr">type</span>=<span class="hljs-string">&quot;checkbox&quot;</span> checked)</span>
<span class="hljs-tag">%<span class="hljs-selector-tag">div</span>{<span class="hljs-attr">:id</span> =&gt; [@item.type, @item.number], <span class="hljs-attr">:class</span> =&gt; [@item.type, @item.urgency]}</span>
<span class="hljs-tag">%<span class="hljs-selector-tag">a</span>{<span class="hljs-attr">href</span>: <span class="hljs-string">&quot;/&quot;</span>, <span class="hljs-attr">title</span>: <span class="hljs-string">&quot;Home&quot;</span>}</span>
""");
    }

    [Fact]
    public void AttributeHashSpanningLines()
    {
        AssertHighlighter("haml",
"""
%a{:href => "/",
   :title => "Home"} Link
%p next
""",
"""
<span class="hljs-tag">%<span class="hljs-selector-tag">a</span>{<span class="hljs-attr">:href</span> =&gt; <span class="hljs-string">&quot;/&quot;</span>,
   <span class="hljs-attr">:title</span> =&gt; <span class="hljs-string">&quot;Home&quot;</span>}</span> Link
<span class="hljs-tag">%<span class="hljs-selector-tag">p</span></span> next
""");
    }

    [Fact]
    public void UnterminatedAttributeHash()
    {
        AssertHighlighter("haml",
"""
%a{:href => x
%p hi
%p there
""",
"""
<span class="hljs-tag">%<span class="hljs-selector-tag">a</span>{<span class="hljs-attr">:href</span> =&gt; x</span>
<span class="hljs-tag">%<span class="hljs-selector-tag">p</span></span> hi
<span class="hljs-tag">%<span class="hljs-selector-tag">p</span></span> there
""");
    }

    [Fact]
    public void Selectors()
    {
        AssertHighlighter("haml",
"""
%div#main.wrapper.clear-fix
.item
#only-id
%span.a.b#c text
%br/
%p<= "no whitespace"
""",
"""
<span class="hljs-tag">%<span class="hljs-selector-tag">div</span><span class="hljs-selector-id">#main</span><span class="hljs-selector-class">.wrapper</span><span class="hljs-selector-class">.clear-fix</span></span>
<span class="hljs-tag"><span class="hljs-selector-class">.item</span></span>
<span class="hljs-tag"><span class="hljs-selector-id">#only-id</span></span>
<span class="hljs-tag">%<span class="hljs-selector-tag">span</span><span class="hljs-selector-class">.a</span><span class="hljs-selector-class">.b</span><span class="hljs-selector-id">#c</span></span> text
<span class="hljs-tag">%<span class="hljs-selector-tag">br</span></span>/
<span class="hljs-tag">%<span class="hljs-selector-tag">p</span>&lt;=<span class="language-ruby"> <span class="hljs-string">&quot;no whitespace&quot;</span></span></span>
""");
    }

    [Fact]
    public void Interpolation()
    {
        AssertHighlighter("haml",
"""
%p This is #{h @user} and #{1 + 2}
Plain text #{ "nested #{x}" } end
""",
"""
<span class="hljs-tag">%<span class="hljs-selector-tag">p</span></span> This is #{<span class="language-ruby">h @user</span>} and #{<span class="language-ruby"><span class="hljs-number">1</span> + <span class="hljs-number">2</span></span>}
Plain text #{<span class="language-ruby"> <span class="hljs-string">&quot;nested <span class="hljs-subst">#{x}</span>&quot;</span> </span>} end
""");
    }

    [Fact]
    public void UnterminatedInterpolation()
    {
        AssertHighlighter("haml",
"""
%p a #{oops
%p b #{ {a: 1}[:a] } c
""",
"""
<span class="hljs-tag">%<span class="hljs-selector-tag">p</span></span> a #{<span class="language-ruby">oops</span>
<span class="hljs-tag">%<span class="hljs-selector-tag">p</span></span> b #{<span class="language-ruby"> {<span class="hljs-symbol">a:</span> <span class="hljs-number">1</span>}[<span class="hljs-symbol">:a</span>] </span>} c
""");
    }

    [Fact]
    public void RubyLines()
    {
        AssertHighlighter("haml",
"""
- foo = bar(1, "two")
= link_to "Home", root_path
-if x
  = x
- # not a comment
""",
"""
-<span class="language-ruby"> foo = bar(<span class="hljs-number">1</span>, <span class="hljs-string">&quot;two&quot;</span>)</span>
=<span class="language-ruby"> link_to <span class="hljs-string">&quot;Home&quot;</span>, root_path</span>
-<span class="language-ruby"><span class="hljs-keyword">if</span> x</span>
  =<span class="language-ruby"> x</span>
-<span class="language-ruby"> <span class="hljs-comment"># not a comment</span></span>
""");
    }

    [Fact]
    public void Filters()
    {
        AssertHighlighter("haml",
"""
%head
  :javascript
    var x = 1;
    alert("hi");
  :css
    body { color: red; }
  %title Hi
:markdown
  # Title
  - list
%p after
""",
"""
<span class="hljs-tag">%<span class="hljs-selector-tag">head</span></span>
  <span class="hljs-meta">:javascript</span><span class="language-javascript">
    <span class="hljs-keyword">var</span> x = <span class="hljs-number">1</span>;
    <span class="hljs-title function_">alert</span>(<span class="hljs-string">&quot;hi&quot;</span>);</span>
  <span class="hljs-meta">:css</span><span class="language-css">
    <span class="hljs-selector-tag">body</span> { <span class="hljs-attribute">color</span>: red; }</span>
<span class="hljs-tag">  %<span class="hljs-selector-tag">title</span></span> Hi
<span class="hljs-meta">:markdown</span>
  # Title
  - list
<span class="hljs-tag">%<span class="hljs-selector-tag">p</span></span> after
""");
    }

    [Fact]
    public void FilterEndsAtDedent()
    {
        AssertHighlighter("haml",
"""
%div
  %div
    :javascript
      console.log(1)

      var y = 2
%p done
""",
"""
<span class="hljs-tag">%<span class="hljs-selector-tag">div</span></span>
<span class="hljs-tag">  %<span class="hljs-selector-tag">div</span></span>
    <span class="hljs-meta">:javascript</span><span class="language-javascript">
      <span class="hljs-variable language_">console</span>.<span class="hljs-title function_">log</span>(<span class="hljs-number">1</span>)

      <span class="hljs-keyword">var</span> y = <span class="hljs-number">2</span></span>
<span class="hljs-tag">%<span class="hljs-selector-tag">p</span></span> done
""");
    }

    [Fact]
    public void EmptyFilter()
    {
        AssertHighlighter("haml",
"""
:javascript
%p next
""",
"""
<span class="hljs-meta">:javascript</span>
<span class="hljs-tag">%<span class="hljs-selector-tag">p</span></span> next
""");
    }

    [Fact]
    public void FilterContentStartingWithColon()
    {
        AssertHighlighter("haml",
"""
:css
  :root {
    --x: 1;
  }
  a { color: red }
%p after
""",
"""
<span class="hljs-meta">:css</span><span class="language-css">
  <span class="hljs-selector-pseudo">:root</span> {
    <span class="hljs-attr">--x</span>: <span class="hljs-number">1</span>;
  }
  <span class="hljs-selector-tag">a</span> { <span class="hljs-attribute">color</span>: red }</span>
<span class="hljs-tag">%<span class="hljs-selector-tag">p</span></span> after
""");
    }

    [Fact]
    public void FilterIndentedWithTabs()
    {
        AssertHighlighter("haml",
"""
%div
	:javascript
		var a = 1
		if (a) {
			b()
		}
	%p x
""",
"""
<span class="hljs-tag">%<span class="hljs-selector-tag">div</span></span>
	<span class="hljs-meta">:javascript</span><span class="language-javascript">
		<span class="hljs-keyword">var</span> a = <span class="hljs-number">1</span>
		<span class="hljs-keyword">if</span> (a) {
			<span class="hljs-title function_">b</span>()
		}</span>
<span class="hljs-tag">	%<span class="hljs-selector-tag">p</span></span> x
""");
    }

    [Fact]
    public void FilterNameNotAloneOnItsLine()
    {
        AssertHighlighter("haml",
"""
%p :javascript is a filter
:javascript foo
  bar
""",
"""
<span class="hljs-tag">%<span class="hljs-selector-tag">p</span></span> :javascript is a filter
:javascript foo
  bar
""");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("haml",
"""
%p Héllo wörld — ünïcode #{名前}
.日本 text
""",
"""
<span class="hljs-tag">%<span class="hljs-selector-tag">p</span></span> Héllo wörld — ünïcode #{<span class="language-ruby">名前</span>}
<span class="hljs-tag"><span class="hljs-selector-class">.日本</span></span> text
""");
    }

    [Fact]
    public void CaseInsensitive()
    {
        AssertHighlighter("haml",
"""
%DIV.Foo
%P Text
""",
"""
<span class="hljs-tag">%<span class="hljs-selector-tag">DIV</span><span class="hljs-selector-class">.Foo</span></span>
<span class="hljs-tag">%<span class="hljs-selector-tag">P</span></span> Text
""");
    }
}
