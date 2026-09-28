namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class ErbHighlighterTests
{
    [Fact]
    public void Loop()
    {
        AssertHighlighter("erb",
"""
<h1>Listing Books</h1>
<table>
  <% @books.each do |book| %>
    <tr>
      <td><%= book.title %></td>
      <td><%= link_to "Show", book %></td>
    </tr>
  <% end %>
</table>
""",
"""
<span class="language-html"><span class="hljs-tag">&lt;<span class="hljs-name">h1</span>&gt;</span>Listing Books<span class="hljs-tag">&lt;/<span class="hljs-name">h1</span>&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">table</span>&gt;</span>
  &lt;%</span><span class="language-ruby"> <span class="hljs-variable">@books</span>.each <span class="hljs-keyword">do</span> |<span class="hljs-params">book</span>| </span><span class="language-html">%&gt;
    <span class="hljs-tag">&lt;<span class="hljs-name">tr</span>&gt;</span>
      <span class="hljs-tag">&lt;<span class="hljs-name">td</span>&gt;</span>&lt;%=</span><span class="language-ruby"> book.title </span><span class="language-html">%&gt;<span class="hljs-tag">&lt;/<span class="hljs-name">td</span>&gt;</span>
      <span class="hljs-tag">&lt;<span class="hljs-name">td</span>&gt;</span>&lt;%=</span><span class="language-ruby"> link_to <span class="hljs-string">&quot;Show&quot;</span>, book </span><span class="language-html">%&gt;<span class="hljs-tag">&lt;/<span class="hljs-name">td</span>&gt;</span>
    <span class="hljs-tag">&lt;/<span class="hljs-name">tr</span>&gt;</span>
  &lt;%</span><span class="language-ruby"> <span class="hljs-keyword">end</span> </span><span class="language-html">%&gt;
<span class="hljs-tag">&lt;/<span class="hljs-name">table</span>&gt;</span></span>
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("erb",
"""
<%# This is a comment %>
<%# TODO: remove this
    multiline %>
<p>after</p>
""",
"""
<span class="hljs-comment">&lt;%# This is a comment %&gt;</span><span class="language-html">
</span><span class="hljs-comment">&lt;%# <span class="hljs-doctag">TODO:</span> remove this
    multiline %&gt;</span><span class="language-html">
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>after<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span></span>
""");
    }

    [Fact]
    public void TrimModifiers()
    {
        AssertHighlighter("erb",
"""
<%- if admin? -%>
  <p>Admin</p>
<%- end -%>
<%= raw @html -%>
""",
"""
<span class="language-html">&lt;%-</span><span class="language-ruby"> <span class="hljs-keyword">if</span> admin? </span><span class="language-html">-%&gt;
  <span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>Admin<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
&lt;%-</span><span class="language-ruby"> <span class="hljs-keyword">end</span> </span><span class="language-html">-%&gt;
&lt;%=</span><span class="language-ruby"> raw <span class="hljs-variable">@html</span> </span><span class="language-html">-%&gt;</span>
""");
    }

    [Fact]
    public void RubyInAttributeValue()
    {
        AssertHighlighter("erb",
"""
<a href="<%= url_for(@post) %>" class="<%= 'active' if current %>">Link</a>
<input type="text" value='<%= @value %>' <%= 'disabled' if locked %>>
""",
"""
<span class="language-html"><span class="hljs-tag">&lt;<span class="hljs-name">a</span> <span class="hljs-attr">href</span>=<span class="hljs-string">&quot;&lt;%=</span></span></span><span class="language-ruby"> url_for(<span class="hljs-variable">@post</span>) </span><span class="language-html"><span class="hljs-tag"><span class="hljs-string">%&gt;&quot;</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;&lt;%=</span></span></span><span class="language-ruby"> <span class="hljs-string">&#x27;active&#x27;</span> <span class="hljs-keyword">if</span> current </span><span class="language-html"><span class="hljs-tag"><span class="hljs-string">%&gt;&quot;</span>&gt;</span>Link<span class="hljs-tag">&lt;/<span class="hljs-name">a</span>&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;text&quot;</span> <span class="hljs-attr">value</span>=<span class="hljs-string">&#x27;&lt;%=</span></span></span><span class="language-ruby"> <span class="hljs-variable">@value</span> </span><span class="language-html"><span class="hljs-tag"><span class="hljs-string">%&gt;&#x27;</span> &lt;%=</span></span><span class="language-ruby"> <span class="hljs-string">&#x27;disabled&#x27;</span> <span class="hljs-keyword">if</span> locked </span><span class="language-html"><span class="hljs-tag">%&gt;</span>&gt;</span>
""");
    }

    [Fact]
    public void RubyInScript()
    {
        AssertHighlighter("erb",
"""
<script>
  var user = "<%= j @user.name %>";
  var count = <%= @count %>;
  <% if debug %>console.log(user);<% end %>
</script>
""",
"""
<span class="language-html"><span class="hljs-tag">&lt;<span class="hljs-name">script</span>&gt;</span><span class="language-javascript">
  <span class="hljs-keyword">var</span> user = <span class="hljs-string">&quot;&lt;%=</span></span></span><span class="language-ruby"> j <span class="hljs-variable">@user</span>.name </span><span class="language-html"><span class="language-javascript"><span class="hljs-string">%&gt;&quot;</span>;
  <span class="hljs-keyword">var</span> count = &lt;%=</span></span><span class="language-ruby"> <span class="hljs-variable">@count</span> </span><span class="language-html"><span class="language-javascript">%&gt;;
  &lt;%</span></span><span class="language-ruby"> <span class="hljs-keyword">if</span> debug </span><span class="language-html"><span class="language-javascript">%&gt;<span class="hljs-variable language_">console</span>.<span class="hljs-title function_">log</span>(user);&lt;%</span></span><span class="language-ruby"> <span class="hljs-keyword">end</span> </span><span class="language-html"><span class="language-javascript">%&gt;
</span><span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span></span>
""");
    }

    [Fact]
    public void Form()
    {
        AssertHighlighter("erb",
"""
<%= form_with(model: @article) do |form| %>
  <div>
    <%= form.label :title %><br>
    <%= form.text_field :title, class: "input", placeholder: 'Title' %>
    <% @article.errors.full_messages_for(:title).each do |message| %>
      <div class="error"><%= message %></div>
    <% end %>
  </div>
  <%= form.submit %>
<% end %>
""",
"""
<span class="language-html">&lt;%=</span><span class="language-ruby"> form_with(<span class="hljs-symbol">model:</span> <span class="hljs-variable">@article</span>) <span class="hljs-keyword">do</span> |<span class="hljs-params">form</span>| </span><span class="language-html">%&gt;
  <span class="hljs-tag">&lt;<span class="hljs-name">div</span>&gt;</span>
    &lt;%=</span><span class="language-ruby"> form.label <span class="hljs-symbol">:title</span> </span><span class="language-html">%&gt;<span class="hljs-tag">&lt;<span class="hljs-name">br</span>&gt;</span>
    &lt;%=</span><span class="language-ruby"> form.text_field <span class="hljs-symbol">:title</span>, <span class="hljs-symbol">class:</span> <span class="hljs-string">&quot;input&quot;</span>, <span class="hljs-symbol">placeholder:</span> <span class="hljs-string">&#x27;Title&#x27;</span> </span><span class="language-html">%&gt;
    &lt;%</span><span class="language-ruby"> <span class="hljs-variable">@article</span>.errors.full_messages_for(<span class="hljs-symbol">:title</span>).each <span class="hljs-keyword">do</span> |<span class="hljs-params">message</span>| </span><span class="language-html">%&gt;
      <span class="hljs-tag">&lt;<span class="hljs-name">div</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;error&quot;</span>&gt;</span>&lt;%=</span><span class="language-ruby"> message </span><span class="language-html">%&gt;<span class="hljs-tag">&lt;/<span class="hljs-name">div</span>&gt;</span>
    &lt;%</span><span class="language-ruby"> <span class="hljs-keyword">end</span> </span><span class="language-html">%&gt;
  <span class="hljs-tag">&lt;/<span class="hljs-name">div</span>&gt;</span>
  &lt;%=</span><span class="language-ruby"> form.submit </span><span class="language-html">%&gt;
&lt;%</span><span class="language-ruby"> <span class="hljs-keyword">end</span> </span><span class="language-html">%&gt;</span>
""");
    }

    [Fact]
    public void RubyLiterals()
    {
        AssertHighlighter("erb",
"""
<%= "Hello #{name}!" %> <%= 'single' %> <%= %w[a b c].join(", ") %> <%= :symbol %> <%= 42 + 3.14 %>
""",
"""
<span class="language-html">&lt;%=</span><span class="language-ruby"> <span class="hljs-string">&quot;Hello <span class="hljs-subst">#{name}</span>!&quot;</span> </span><span class="language-html">%&gt; &lt;%=</span><span class="language-ruby"> <span class="hljs-string">&#x27;single&#x27;</span> </span><span class="language-html">%&gt; &lt;%=</span><span class="language-ruby"> <span class="hljs-string">%w[a b c]</span>.join(<span class="hljs-string">&quot;, &quot;</span>) </span><span class="language-html">%&gt; &lt;%=</span><span class="language-ruby"> <span class="hljs-symbol">:symbol</span> </span><span class="language-html">%&gt; &lt;%=</span><span class="language-ruby"> <span class="hljs-number">42</span> + <span class="hljs-number">3.14</span> </span><span class="language-html">%&gt;</span>
""");
    }

    [Fact]
    public void MultilineRuby()
    {
        AssertHighlighter("erb",
"""
<%
  total = items.sum(&:price)
  # a ruby comment
  tax = total * 0.2
%>
<p><%= number_to_currency(total + tax) %></p>
""",
"""
<span class="language-html">&lt;%</span><span class="language-ruby">
  total = items.sum(&amp;<span class="hljs-symbol">:price</span>)
  <span class="hljs-comment"># a ruby comment</span>
  tax = total * <span class="hljs-number">0.2</span>
</span><span class="language-html">%&gt;
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>&lt;%=</span><span class="language-ruby"> number_to_currency(total + tax) </span><span class="language-html">%&gt;<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span></span>
""");
    }

    [Fact]
    public void Heredoc()
    {
        AssertHighlighter("erb",
"""
<% text = <<~EOS
  hello
EOS
%><p><%= text %></p>
""",
"""
<span class="language-html">&lt;%</span><span class="language-ruby"> text = <span class="hljs-string">&lt;&lt;~EOS
  hello
EOS</span>
</span><span class="language-html">%&gt;<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>&lt;%=</span><span class="language-ruby"> text </span><span class="language-html">%&gt;<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span></span>
""");
    }

    [Fact]
    public void UnterminatedTag()
    {
        AssertHighlighter("erb",
"""
<p><%= name</p>
<b>bold</b>
""",
"""
<span class="language-html"><span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>&lt;%=</span><span class="language-ruby"> name&lt;<span class="hljs-regexp">/p&gt;
&lt;b&gt;bold&lt;/b</span>&gt;</span>
""");
    }

    [Fact]
    public void UnterminatedComment()
    {
        AssertHighlighter("erb",
"""
<%# never closed
<p>x</p>
""",
"""
<span class="hljs-comment">&lt;%# never closed
&lt;p&gt;x&lt;/p&gt;</span>
""");
    }

    [Fact]
    public void StringContinuesInNextTag()
    {
        AssertHighlighter("erb",
"""
<%= "abc %>
<p>x</p>
<%= 1 %>
""",
"""
<span class="language-html">&lt;%=</span><span class="language-ruby"> <span class="hljs-string">&quot;abc </span></span><span class="language-html">%&gt;
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>x<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
&lt;%=</span><span class="language-ruby"><span class="hljs-string"> 1 </span></span><span class="language-html">%&gt;</span>
""");
    }

    [Fact]
    public void TagInTagName()
    {
        AssertHighlighter("erb",
"""
<<%= tag %> class="x">content</<%= tag %>>
""",
"""
<span class="language-html">&lt;&lt;%=</span><span class="language-ruby"> tag </span><span class="language-html">%&gt; class=&quot;x&quot;&gt;content&lt;/&lt;%=</span><span class="language-ruby"> tag </span><span class="language-html">%&gt;&gt;</span>
""");
    }

    [Fact]
    public void CrLf()
    {
        AssertHighlighter("erb",
"<% if x %>\r\n<p><%= y %></p>\r\n<% end %>",
"<span class=\"language-html\">&lt;%</span><span class=\"language-ruby\"> <span class=\"hljs-keyword\">if</span> x </span><span class=\"language-html\">%&gt;\r\n<span class=\"hljs-tag\">&lt;<span class=\"hljs-name\">p</span>&gt;</span>&lt;%=</span><span class=\"language-ruby\"> y </span><span class=\"language-html\">%&gt;<span class=\"hljs-tag\">&lt;/<span class=\"hljs-name\">p</span>&gt;</span>\r\n&lt;%</span><span class=\"language-ruby\"> <span class=\"hljs-keyword\">end</span> </span><span class=\"language-html\">%&gt;</span>");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("erb",
"""
<p class="é"><%= prénom %> — <%= "日本" %></p>
""",
"""
<span class="language-html"><span class="hljs-tag">&lt;<span class="hljs-name">p</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;é&quot;</span>&gt;</span>&lt;%=</span><span class="language-ruby"> prénom </span><span class="language-html">%&gt; — &lt;%=</span><span class="language-ruby"> <span class="hljs-string">&quot;日本&quot;</span> </span><span class="language-html">%&gt;<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span></span>
""");
    }
}
