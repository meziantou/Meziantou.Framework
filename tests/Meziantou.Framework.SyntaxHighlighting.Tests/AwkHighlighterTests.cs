namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class AwkHighlighterTests
{
    [Fact]
    public void SumColumn()
    {
        AssertHighlighter("awk",
"""
#!/usr/bin/awk -f
# Sum the second column
BEGIN { FS = ","; total = 0 }
NR > 1 { total += $2 }
END { printf "Total: %d\n", total }
""",
"""
<span class="hljs-comment">#!/usr/bin/awk -f</span>
<span class="hljs-comment"># Sum the second column</span>
<span class="hljs-keyword">BEGIN</span> { FS = <span class="hljs-string">&quot;,&quot;</span>; total = <span class="hljs-number">0</span> }
NR &gt; <span class="hljs-number">1</span> { total += <span class="hljs-variable">$2</span> }
<span class="hljs-keyword">END</span> { printf <span class="hljs-string">&quot;Total: %d\n&quot;</span>, total }
""");
    }

    [Fact]
    public void PatternsAndRegularExpressions()
    {
        AssertHighlighter("awk",
"""
/^#/ { next }
$3 ~ /error|fail/ { count[$1]++ }
length($0) > 72 { print NR": "$0 }
END {
  for (host in count)
    print host, count[host]
}
""",
"""
<span class="hljs-regexp">/^#/</span> { <span class="hljs-keyword">next</span> }
<span class="hljs-variable">$3</span> ~ <span class="hljs-regexp">/error|fail/</span> { count[<span class="hljs-variable">$1</span>]++ }
length(<span class="hljs-variable">$0</span>) &gt; <span class="hljs-number">72</span> { print NR<span class="hljs-string">&quot;: &quot;</span><span class="hljs-variable">$0</span> }
<span class="hljs-keyword">END</span> {
  <span class="hljs-keyword">for</span> (host <span class="hljs-keyword">in</span> count)
    print host, count[host]
}
""");
    }

    [Fact]
    public void Functions()
    {
        AssertHighlighter("awk",
"""
function max(a, b) {
  return a > b ? a : b
}
func trim(s) { gsub(/^[ \t]+|[ \t]+$/, "", s); return s }
{ print max($1, $2), trim($3) }
""",
"""
<span class="hljs-keyword">function</span> max(a, b) {
  return a &gt; b ? a : b
}
<span class="hljs-keyword">func</span> trim(s) { gsub(<span class="hljs-regexp">/^[ \t]+|[ \t]+$/</span>, <span class="hljs-string">&quot;&quot;</span>, s); return s }
{ print max(<span class="hljs-variable">$1</span>, <span class="hljs-variable">$2</span>), trim(<span class="hljs-variable">$3</span>) }
""");
    }

    [Fact]
    public void ControlFlow()
    {
        AssertHighlighter("awk",
"""
{
  i = 1
  while (i <= NF) {
    if ($i == "") { delete arr[i]; break }
    else if ($i ~ /[0-9]+/) continue
    do { i++ } while (i < 3)
  }
  nextfile
  exit 1
}
""",
"""
{
  i = <span class="hljs-number">1</span>
  <span class="hljs-keyword">while</span> (i &lt;= NF) {
    <span class="hljs-keyword">if</span> (<span class="hljs-variable">$i</span> == <span class="hljs-string">&quot;&quot;</span>) { <span class="hljs-keyword">delete</span> arr[i]; <span class="hljs-keyword">break</span> }
    <span class="hljs-keyword">else</span> <span class="hljs-keyword">if</span> (<span class="hljs-variable">$i</span> ~ <span class="hljs-regexp">/[0-9]+/</span>) <span class="hljs-keyword">continue</span>
    <span class="hljs-keyword">do</span> { i++ } <span class="hljs-keyword">while</span> (i &lt; <span class="hljs-number">3</span>)
  }
  <span class="hljs-keyword">nextfile</span>
  <span class="hljs-keyword">exit</span> <span class="hljs-number">1</span>
}
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("awk",
"""
BEGIN {
  s = "tab\there \"quoted\""
  t = 'single'
  u = "unterminated
  print "next"
}
""",
"""
<span class="hljs-keyword">BEGIN</span> {
  s = <span class="hljs-string">&quot;tab\there \&quot;quoted\&quot;&quot;</span>
  t = <span class="hljs-string">&#x27;single&#x27;</span>
  u = <span class="hljs-string">&quot;unterminated
  print &quot;</span><span class="hljs-keyword">next</span><span class="hljs-string">&quot;
}</span>
""");
    }

    [Fact]
    public void FieldVariables()
    {
        AssertHighlighter("awk",
"""
{ print $1, $NF, $0, ${x} }
""",
"""
{ print <span class="hljs-variable">$1</span>, <span class="hljs-variable">$NF</span>, <span class="hljs-variable">$0</span>, <span class="hljs-variable">${x}</span> }
""");
    }

    [Fact]
    public void RegularExpressionCharacterClass()
    {
        AssertHighlighter("awk",
"""
$0 ~ /[/]x/ { print }
/a\/b/ { print "slash" }
""",
"""
<span class="hljs-variable">$0</span> ~ <span class="hljs-regexp">/[/]x/</span> { print }
<span class="hljs-regexp">/a\/b/</span> { print <span class="hljs-string">&quot;slash&quot;</span> }
""");
    }

    [Fact]
    public void DivisionLooksLikeRegularExpression()
    {
        AssertHighlighter("awk",
"""
{ avg = total / count; ratio = a / b / c }
""",
"""
{ avg = total <span class="hljs-regexp">/ count; ratio = a /</span> b / c }
""");
    }

    [Fact]
    public void PrefixedStrings()
    {
        AssertHighlighter("awk",
"""
{ print var"x" u"y" }
""",
"""
{ print va<span class="hljs-string">r&quot;x&quot;</span> <span class="hljs-string">u&quot;y&quot;</span> }
""");
    }

    [Fact]
    public void BracedVariables()
    {
        AssertHighlighter("awk",
"""
${a ${b} c} ${d
${e} ${f ${g
x
""",
"""
<span class="hljs-variable">${a ${b}</span> c} ${d
<span class="hljs-variable">${e}</span> ${f ${g
x
""");
    }

    [Fact]
    public void GawkAlias()
    {
        AssertHighlighter("gawk",
"""
BEGIN { print 1 }
""",
"""
<span class="hljs-keyword">BEGIN</span> { print <span class="hljs-number">1</span> }
""");
    }

    [Fact]
    public void Empty()
    {
        AssertHighlighter("awk", "", "");
    }
}
