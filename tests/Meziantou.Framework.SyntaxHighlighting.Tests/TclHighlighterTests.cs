namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class TclHighlighterTests
{
    [Fact]
    public void Alias()
    {
        AssertHighlighter("tk",
"""
label .l -text "tk alias"
""",
"""
label .l -text <span class="hljs-string">&quot;tk alias&quot;</span>
""");
    }

    [Fact]
    public void Control()
    {
        AssertHighlighter("tcl",
"""
while {$i < 10} {
    incr i
    if {$i == 5} continue
}
switch -exact -- $cmd {
    start { puts "starting" }
    stop { puts "stopping" }
    default { error "unknown: $cmd" }
}
catch {open missing.txt} err
  # indented comment
""",
"""
<span class="hljs-keyword">while</span> {<span class="hljs-variable">$i</span> &lt; <span class="hljs-number">10</span>} {
    <span class="hljs-keyword">incr</span> i
    <span class="hljs-keyword">if</span> {<span class="hljs-variable">$i</span> == <span class="hljs-number">5</span>} <span class="hljs-keyword">continue</span>
}
<span class="hljs-keyword">switch</span> -exact -- <span class="hljs-variable">$cmd</span> {
    start { <span class="hljs-keyword">puts</span> <span class="hljs-string">&quot;starting&quot;</span> }
    stop { <span class="hljs-keyword">puts</span> <span class="hljs-string">&quot;stopping&quot;</span> }
    default { <span class="hljs-keyword">error</span> <span class="hljs-string">&quot;unknown: $cmd&quot;</span> }
}
<span class="hljs-keyword">catch</span> {<span class="hljs-keyword">open</span> missing.txt} err
<span class="hljs-comment">  # indented comment</span>
""");
    }

    [Fact]
    public void Edge()
    {
        AssertHighlighter("tcl",
"""
set s "unterminated
set t 1
proc noBrace
""",
"""
<span class="hljs-keyword">set</span> s <span class="hljs-string">&quot;unterminated
set t 1
proc noBrace</span>
""");
    }

    [Fact]
    public void Empty()
    {
        AssertHighlighter("tcl", "", "");
    }

    [Fact]
    public void Hello()
    {
        AssertHighlighter("tcl",
"""
puts "Hello, World!"
set name [gets stdin]
puts "Hi, $name!"
""",
"""
<span class="hljs-keyword">puts</span> <span class="hljs-string">&quot;Hello, World!&quot;</span>
<span class="hljs-keyword">set</span> name [<span class="hljs-keyword">gets</span> stdin]
<span class="hljs-keyword">puts</span> <span class="hljs-string">&quot;Hi, $name!&quot;</span>
""");
    }

    [Fact]
    public void Lists()
    {
        AssertHighlighter("tcl",
"""
set fruits [list apple banana cherry]
lappend fruits date
puts [llength $fruits]
puts [lindex $fruits 0]
set sorted [lsort -decreasing $fruits]
foreach {key value} [array get colors] {
    puts "$key => $value"
}
dict set config port 8080
""",
"""
<span class="hljs-keyword">set</span> fruits [<span class="hljs-keyword">list</span> apple banana cherry]
<span class="hljs-keyword">lappend</span> fruits date
<span class="hljs-keyword">puts</span> [<span class="hljs-keyword">llength</span> <span class="hljs-variable">$fruits</span>]
<span class="hljs-keyword">puts</span> [<span class="hljs-keyword">lindex</span> <span class="hljs-variable">$fruits</span> <span class="hljs-number">0</span>]
<span class="hljs-keyword">set</span> sorted [<span class="hljs-keyword">lsort</span> -decreasing <span class="hljs-variable">$fruits</span>]
<span class="hljs-keyword">foreach</span> {key value} [<span class="hljs-keyword">array</span> get colors] {
    <span class="hljs-keyword">puts</span> <span class="hljs-string">&quot;$key =&gt; $value&quot;</span>
}
<span class="hljs-keyword">dict</span> <span class="hljs-keyword">set</span> config port <span class="hljs-number">8080</span>
""");
    }

    [Fact]
    public void Namespace()
    {
        AssertHighlighter("tcl",
"""
namespace eval ::app {
    variable counter 0
    proc incr_counter {} {
        variable counter
        incr counter
    }
}
set ::app::counter 5
puts $::app::counter
puts ${app::counter}
set x ${my var}
""",
"""
<span class="hljs-keyword">namespace</span> <span class="hljs-keyword">eval</span> ::app {
    <span class="hljs-keyword">variable</span> counter <span class="hljs-number">0</span>
    <span class="hljs-keyword">proc</span><span class="hljs-title"> incr_counter</span> {} {
        <span class="hljs-keyword">variable</span> counter
        <span class="hljs-keyword">incr</span> counter
    }
}
<span class="hljs-keyword">set</span> ::app::counter <span class="hljs-number">5</span>
<span class="hljs-keyword">puts</span> <span class="hljs-variable">$::app::counter</span>
<span class="hljs-keyword">puts</span> <span class="hljs-variable">${app::counter}</span>
<span class="hljs-keyword">set</span> x <span class="hljs-variable">${my var}</span>
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("tcl",
"""
set a 42
set b 3.14
set c 0x1F
set d 0b1010
set e 1e10
set f -7
""",
"""
<span class="hljs-keyword">set</span> a <span class="hljs-number">42</span>
<span class="hljs-keyword">set</span> b <span class="hljs-number">3.14</span>
<span class="hljs-keyword">set</span> c <span class="hljs-number">0x1F</span>
<span class="hljs-keyword">set</span> d <span class="hljs-number">0b1010</span>
<span class="hljs-keyword">set</span> e <span class="hljs-number">1e10</span>
<span class="hljs-keyword">set</span> f <span class="hljs-number">-7</span>
""");
    }

    [Fact]
    public void Proc()
    {
        AssertHighlighter("tcl",
"""
# Compute a factorial
proc factorial {n} {
    if {$n <= 1} {
        return 1
    }
    return [expr {$n * [factorial [expr {$n - 1}]]}]
}

proc ::myns::helper {args} {
    foreach arg $args {
        puts $arg
    }
}

puts [factorial 5] ;# inline comment
""",
"""
<span class="hljs-comment"># Compute a factorial</span>
<span class="hljs-keyword">proc</span><span class="hljs-title"> factorial</span> {n} {
    <span class="hljs-keyword">if</span> {<span class="hljs-variable">$n</span> &lt;= <span class="hljs-number">1</span>} {
        <span class="hljs-keyword">return</span> <span class="hljs-number">1</span>
    }
    <span class="hljs-keyword">return</span> [<span class="hljs-keyword">expr</span> {<span class="hljs-variable">$n</span> * [factorial [<span class="hljs-keyword">expr</span> {<span class="hljs-variable">$n</span> - <span class="hljs-number">1</span>}]]}]
}

<span class="hljs-keyword">proc</span><span class="hljs-title"> ::myns::helper</span> {args} {
    <span class="hljs-keyword">foreach</span> arg <span class="hljs-variable">$args</span> {
        <span class="hljs-keyword">puts</span> <span class="hljs-variable">$arg</span>
    }
}

<span class="hljs-keyword">puts</span> [factorial <span class="hljs-number">5</span>] <span class="hljs-comment">;# inline comment</span>
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("tcl",
"""
set s "tab\t newline\n quote \" dollar \$"
set b {braced $not substituted}
set m "multi
line"
regsub -all {\s+} $s " " result
string length $s
""",
"""
<span class="hljs-keyword">set</span> s <span class="hljs-string">&quot;tab\t newline\n quote \&quot; dollar \$&quot;</span>
<span class="hljs-keyword">set</span> b {braced <span class="hljs-variable">$not</span> substituted}
<span class="hljs-keyword">set</span> m <span class="hljs-string">&quot;multi
line&quot;</span>
<span class="hljs-keyword">regsub</span> -all {\s+} <span class="hljs-variable">$s</span> <span class="hljs-string">&quot; &quot;</span> result
<span class="hljs-keyword">string</span> length <span class="hljs-variable">$s</span>
""");
    }

    [Fact]
    public void Tk()
    {
        AssertHighlighter("tcl",
"""
package require Tk
button .b -text "Click me" -command {puts clicked}
pack .b -side left
wm title . "My App"
bind . <Key-q> {exit}
""",
"""
<span class="hljs-keyword">package</span> require Tk
button .b -text <span class="hljs-string">&quot;Click me&quot;</span> -command {<span class="hljs-keyword">puts</span> clicked}
pack .b -side left
wm title . <span class="hljs-string">&quot;My App&quot;</span>
bind . &lt;Key-q&gt; {<span class="hljs-keyword">exit</span>}
""");
    }
}
