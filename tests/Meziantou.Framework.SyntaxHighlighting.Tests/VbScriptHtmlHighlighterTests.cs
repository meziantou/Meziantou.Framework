namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class VbScriptHtmlHighlighterTests
{
    [Fact]
    public void ResponseWrite()
    {
        AssertHighlighter("vbscript-html",
"""
<html>
<body>
<% Response.Write "Hello, World!" %>
</body>
</html>
""",
"""
<span class="language-html"><span class="hljs-tag">&lt;<span class="hljs-name">html</span>&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">body</span>&gt;</span>
</span><span class="language-vbscript">&lt;% <span class="hljs-built_in">Response</span>.Write <span class="hljs-string">&quot;Hello, World!&quot;</span> %&gt;</span><span class="language-html">
<span class="hljs-tag">&lt;/<span class="hljs-name">body</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">html</span>&gt;</span></span>
""");
    }

    [Fact]
    public void BlockInAttributeValue()
    {
        AssertHighlighter("vbscript-html",
"""
<a href="<%= url %>">link</a>
""",
"""
<span class="language-html"><span class="hljs-tag">&lt;<span class="hljs-name">a</span> <span class="hljs-attr">href</span>=<span class="hljs-string">&quot;</span></span></span><span class="language-vbscript">&lt;%= url %&gt;</span><span class="language-html"><span class="hljs-tag"><span class="hljs-string">&quot;</span>&gt;</span>link<span class="hljs-tag">&lt;/<span class="hljs-name">a</span>&gt;</span></span>
""");
    }

    [Fact]
    public void IfAroundHtml()
    {
        AssertHighlighter("vbscript-html",
"""
<% If loggedIn Then %>
  <p>Welcome, <%= Server.HTMLEncode(userName) %>!</p>
<% Else %>
  <a href="login.asp">Log in</a>
<% End If %>
""",
"""
<span class="language-vbscript">&lt;% <span class="hljs-keyword">If</span> loggedIn <span class="hljs-keyword">Then</span> %&gt;</span><span class="language-html">
  <span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>Welcome, </span><span class="language-vbscript">&lt;%= <span class="hljs-built_in">Server</span>.HTMLEncode(userName) %&gt;</span><span class="language-html">!<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
</span><span class="language-vbscript">&lt;% <span class="hljs-keyword">Else</span> %&gt;</span><span class="language-html">
  <span class="hljs-tag">&lt;<span class="hljs-name">a</span> <span class="hljs-attr">href</span>=<span class="hljs-string">&quot;login.asp&quot;</span>&gt;</span>Log in<span class="hljs-tag">&lt;/<span class="hljs-name">a</span>&gt;</span>
</span><span class="language-vbscript">&lt;% <span class="hljs-keyword">End</span> <span class="hljs-keyword">If</span> %&gt;</span>
""");
    }

    [Fact]
    public void LoopAroundHtml()
    {
        AssertHighlighter("vbscript-html",
"""
<ul>
<% For i = 1 To 3 %>
  <li>Item <%= i %></li>
<% Next %>
</ul>
""",
"""
<span class="language-html"><span class="hljs-tag">&lt;<span class="hljs-name">ul</span>&gt;</span>
</span><span class="language-vbscript">&lt;% <span class="hljs-keyword">For</span> i = <span class="hljs-number">1</span> <span class="hljs-keyword">To</span> <span class="hljs-number">3</span> %&gt;</span><span class="language-html">
  <span class="hljs-tag">&lt;<span class="hljs-name">li</span>&gt;</span>Item </span><span class="language-vbscript">&lt;%= i %&gt;</span><span class="language-html"><span class="hljs-tag">&lt;/<span class="hljs-name">li</span>&gt;</span>
</span><span class="language-vbscript">&lt;% <span class="hljs-keyword">Next</span> %&gt;</span><span class="language-html">
<span class="hljs-tag">&lt;/<span class="hljs-name">ul</span>&gt;</span></span>
""");
    }

    [Fact]
    public void MultilineBlock()
    {
        AssertHighlighter("vbscript-html",
"""
<%
Option Explicit
Dim conn
Set conn = Server.CreateObject("ADODB.Connection")
conn.Open "DSN=mydb"
%>
<!DOCTYPE html>
<title>Page</title>
""",
"""
<span class="language-vbscript">&lt;%
<span class="hljs-keyword">Option</span> <span class="hljs-keyword">Explicit</span>
<span class="hljs-keyword">Dim</span> conn
<span class="hljs-keyword">Set</span> conn = <span class="hljs-built_in">Server</span>.<span class="hljs-built_in">CreateObject</span>(<span class="hljs-string">&quot;ADODB.Connection&quot;</span>)
conn.Open <span class="hljs-string">&quot;DSN=mydb&quot;</span>
%&gt;</span><span class="language-html">
<span class="hljs-meta">&lt;!DOCTYPE <span class="hljs-keyword">html</span>&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">title</span>&gt;</span>Page<span class="hljs-tag">&lt;/<span class="hljs-name">title</span>&gt;</span></span>
""");
    }

    [Fact]
    public void Directive()
    {
        AssertHighlighter("vbscript-html",
"""
<%@ Language="VBScript" %>
<% Response.Buffer = True %>
""",
"""
<span class="language-vbscript">&lt;%@ Language=<span class="hljs-string">&quot;VBScript&quot;</span> %&gt;</span><span class="language-html">
</span><span class="language-vbscript">&lt;% <span class="hljs-built_in">Response</span>.Buffer = <span class="hljs-literal">True</span> %&gt;</span>
""");
    }

    [Fact]
    public void ScriptElement()
    {
        AssertHighlighter("vbscript-html",
"""
<script>
var x = 1;
</script>
<% y = 2 %>
""",
"""
<span class="language-html"><span class="hljs-tag">&lt;<span class="hljs-name">script</span>&gt;</span><span class="language-javascript">
<span class="hljs-keyword">var</span> x = <span class="hljs-number">1</span>;
</span><span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span>
</span><span class="language-vbscript">&lt;% y = <span class="hljs-number">2</span> %&gt;</span>
""");
    }

    [Fact]
    public void CommentInBlock()
    {
        AssertHighlighter("vbscript-html",
"""
<p><% ' comment %></p>
<% x = 1 %>
""",
"""
<span class="language-html"><span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span></span><span class="language-vbscript">&lt;% <span class="hljs-comment">&#x27; comment %&gt;</span></span><span class="language-html"><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
</span><span class="language-vbscript">&lt;% x = <span class="hljs-number">1</span> %&gt;</span>
""");
    }

    [Fact]
    public void UnterminatedString_DoesNotLeakIntoNextBlock()
    {
        AssertHighlighter("vbscript-html",
"""
<% s = "never closed %>
<p>text</p>
<% x = 1 %>
""",
"""
<span class="language-vbscript">&lt;% s = <span class="hljs-string">&quot;never closed %&gt;</span></span><span class="language-html">
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>text<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
</span><span class="language-vbscript">&lt;% x = <span class="hljs-number">1</span> %&gt;</span>
""");
    }

    [Fact]
    public void UnterminatedBlock()
    {
        AssertHighlighter("vbscript-html",
"""
<p>a</p>
<% x = 1
<p>b</p>
""",
"""
<span class="language-html"><span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>a<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
</span><span class="language-vbscript">&lt;% x = <span class="hljs-number">1</span>
&lt;p&gt;b&lt;/p&gt;</span>
""");
    }

    [Fact]
    public void PercentInString()
    {
        AssertHighlighter("vbscript-html",
"""
<% s = "100%" %><p>x</p>
""",
"""
<span class="language-vbscript">&lt;% s = <span class="hljs-string">&quot;100%&quot;</span> %&gt;</span><span class="language-html"><span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>x<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span></span>
""");
    }

    [Fact]
    public void BlockInScriptString_ScriptResumesInTheString()
    {
        AssertHighlighter("vbscript-html",
"""
<script>
  var url = "<%= url %>";
  var count = 3;
</script>
""",
"""
<span class="language-html"><span class="hljs-tag">&lt;<span class="hljs-name">script</span>&gt;</span><span class="language-javascript">
  <span class="hljs-keyword">var</span> url = <span class="hljs-string">&quot;</span></span></span><span class="language-vbscript">&lt;%= url %&gt;</span><span class="language-html"><span class="language-javascript"><span class="hljs-string">&quot;</span>;
  <span class="hljs-keyword">var</span> count = <span class="hljs-number">3</span>;
</span><span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span></span>
""");
    }

    [Fact]
    public void EmptyInput()
    {
        AssertHighlighter("vbscript-html",
"",
"");
    }
}
