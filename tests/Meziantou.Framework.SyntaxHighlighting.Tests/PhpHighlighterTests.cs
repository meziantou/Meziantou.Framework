namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class PhpHighlighterTests
{

    [Fact]
    public void Tag_OpenClose()
    {
        AssertHighlighter("php",
"""
<?php
echo "hi";
?>
""",
"""
<span class="hljs-meta">&lt;?php</span>
<span class="hljs-keyword">echo</span> <span class="hljs-string">&quot;hi&quot;</span>;
<span class="hljs-meta">?&gt;</span>
""");
    }

    [Fact]
    public void Tag_OpenNoClose()
    {
        AssertHighlighter("php",
"""
<?php
echo "hi";
""",
"""
<span class="hljs-meta">&lt;?php</span>
<span class="hljs-keyword">echo</span> <span class="hljs-string">&quot;hi&quot;</span>;
""");
    }

    [Fact]
    public void Tag_OpenEcho()
    {
        AssertHighlighter("php",
"""
<?= $name ?>
""",
"""
<span class="hljs-meta">&lt;?=</span> <span class="hljs-variable">$name</span> <span class="hljs-meta">?&gt;</span>
""");
    }

    [Fact]
    public void Tag_MixedHtml()
    {
        AssertHighlighter("php",
"""
<p>Hello, <?= htmlspecialchars($name) ?>!</p>
""",
"""
&lt;p&gt;Hello, <span class="hljs-meta">&lt;?=</span> <span class="hljs-title function_ invoke__">htmlspecialchars</span>(<span class="hljs-variable">$name</span>) <span class="hljs-meta">?&gt;</span>!&lt;/p&gt;
""");
    }

    [Fact]
    public void Variable_Simple()
    {
        AssertHighlighter("php",
"""
$name = "alice";
""",
"""
<span class="hljs-variable">$name</span> = <span class="hljs-string">&quot;alice&quot;</span>;
""");
    }

    [Fact]
    public void Variable_TypedAssign()
    {
        AssertHighlighter("php",
"""
$count = 42;
""",
"""
<span class="hljs-variable">$count</span> = <span class="hljs-number">42</span>;
""");
    }

    [Fact]
    public void Variable_Array()
    {
        AssertHighlighter("php",
"""
$names = ["alice", "bob"];
""",
"""
<span class="hljs-variable">$names</span> = [<span class="hljs-string">&quot;alice&quot;</span>, <span class="hljs-string">&quot;bob&quot;</span>];
""");
    }

    [Fact]
    public void Variable_Reference()
    {
        AssertHighlighter("php",
"""
$ref = &$original;
""",
"""
<span class="hljs-variable">$ref</span> = &amp;<span class="hljs-variable">$original</span>;
""");
    }

    [Fact]
    public void Variable_Global()
    {
        AssertHighlighter("php",
"""
function f() {
    global $counter;
    $counter++;
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">f</span>(<span class="hljs-params"></span>) </span>{
    <span class="hljs-keyword">global</span> <span class="hljs-variable">$counter</span>;
    <span class="hljs-variable">$counter</span>++;
}
""");
    }

    [Fact]
    public void Variable_Static()
    {
        AssertHighlighter("php",
"""
function counter() {
    static $count = 0;
    return ++$count;
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">counter</span>(<span class="hljs-params"></span>) </span>{
    <span class="hljs-built_in">static</span> <span class="hljs-variable">$count</span> = <span class="hljs-number">0</span>;
    <span class="hljs-keyword">return</span> ++<span class="hljs-variable">$count</span>;
}
""");
    }

    [Fact]
    public void Variable_Variable()
    {
        AssertHighlighter("php",
"""
$name = "x";
$$name = 42;
""",
"""
<span class="hljs-variable">$name</span> = <span class="hljs-string">&quot;x&quot;</span>;
<span class="hljs-variable">$$name</span> = <span class="hljs-number">42</span>;
""");
    }

    [Fact]
    public void Variable_NullCoalesce()
    {
        AssertHighlighter("php",
"""
$value = $maybe ?? "default";
""",
"""
<span class="hljs-variable">$value</span> = <span class="hljs-variable">$maybe</span> ?? <span class="hljs-string">&quot;default&quot;</span>;
""");
    }

    [Fact]
    public void Variable_NullCoalesceAssign()
    {
        AssertHighlighter("php",
"""
$cache ??= compute();
""",
"""
<span class="hljs-variable">$cache</span> ??= <span class="hljs-title function_ invoke__">compute</span>();
""");
    }

    [Fact]
    public void Variable_NullSafe()
    {
        AssertHighlighter("php",
"""
$name = $user?->profile?->name;
""",
"""
<span class="hljs-variable">$name</span> = <span class="hljs-variable">$user</span>?-&gt;profile?-&gt;name;
""");
    }

    [Fact]
    public void String_SingleQuote()
    {
        AssertHighlighter("php",
"""
$s = 'hello';
""",
"""
<span class="hljs-variable">$s</span> = <span class="hljs-string">&#x27;hello&#x27;</span>;
""");
    }

    [Fact]
    public void String_DoubleQuote()
    {
        AssertHighlighter("php",
"""
$s = "hello";
""",
"""
<span class="hljs-variable">$s</span> = <span class="hljs-string">&quot;hello&quot;</span>;
""");
    }

    [Fact]
    public void String_EscapeNewline()
    {
        AssertHighlighter("php",
"""
$s = "line1\nline2";
""",
"""
<span class="hljs-variable">$s</span> = <span class="hljs-string">&quot;line1\nline2&quot;</span>;
""");
    }

    [Fact]
    public void String_EscapeTab()
    {
        AssertHighlighter("php",
"""
$s = "a\tb";
""",
"""
<span class="hljs-variable">$s</span> = <span class="hljs-string">&quot;a\tb&quot;</span>;
""");
    }

    [Fact]
    public void String_EscapeDollar()
    {
        AssertHighlighter("php",
"""
$s = "\$literal";
""",
"""
<span class="hljs-variable">$s</span> = <span class="hljs-string">&quot;\$literal&quot;</span>;
""");
    }

    [Fact]
    public void String_EscapeUnicode()
    {
        AssertHighlighter("php",
"""
$s = "\u{1F600}";
""",
"""
<span class="hljs-variable">$s</span> = <span class="hljs-string">&quot;\u{1F600}&quot;</span>;
""");
    }

    [Fact]
    public void String_EscapeHex()
    {
        AssertHighlighter("php",
"""
$s = "\x41";
""",
"""
<span class="hljs-variable">$s</span> = <span class="hljs-string">&quot;\x41&quot;</span>;
""");
    }

    [Fact]
    public void String_EscapeOctal()
    {
        AssertHighlighter("php",
"""
$s = "\101";
""",
"""
<span class="hljs-variable">$s</span> = <span class="hljs-string">&quot;\101&quot;</span>;
""");
    }

    [Fact]
    public void String_InterpolationSimple()
    {
        AssertHighlighter("php",
"""
$msg = "Hello $name";
""",
"""
<span class="hljs-variable">$msg</span> = <span class="hljs-string">&quot;Hello <span class="hljs-subst">$name</span>&quot;</span>;
""");
    }

    [Fact]
    public void String_InterpolationBraces()
    {
        AssertHighlighter("php",
"""
$msg = "Hello {$user->name}";
""",
"""
<span class="hljs-variable">$msg</span> = <span class="hljs-string">&quot;Hello <span class="hljs-subst">{$user-&gt;name}</span>&quot;</span>;
""");
    }

    [Fact]
    public void String_InterpolationIndex()
    {
        AssertHighlighter("php",
"""
$msg = "Got {$items[0]}";
""",
"""
<span class="hljs-variable">$msg</span> = <span class="hljs-string">&quot;Got <span class="hljs-subst">{$items[0]}</span>&quot;</span>;
""");
    }

    [Fact]
    public void String_Heredoc()
    {
        AssertHighlighter("php",
"""
$msg = <<<EOT
Hello, $name!
Welcome aboard.
EOT;
""",
"""
<span class="hljs-variable">$msg</span> = <span class="hljs-string">&lt;&lt;&lt;EOT
Hello, <span class="hljs-subst">$name</span>!
Welcome aboard.
EOT</span>;
""");
    }

    [Fact]
    public void String_HeredocIndented()
    {
        AssertHighlighter("php",
"""
    $msg = <<<EOT
        Hello, $name!
        Welcome.
        EOT;
""",
"""
    <span class="hljs-variable">$msg</span> = <span class="hljs-string">&lt;&lt;&lt;EOT
        Hello, <span class="hljs-subst">$name</span>!
        Welcome.
        EOT</span>;
""");
    }

    [Fact]
    public void String_Nowdoc()
    {
        AssertHighlighter("php",
"""
$msg = <<<'EOT'
No $interpolation here.
EOT;
""",
"""
<span class="hljs-variable">$msg</span> = <span class="hljs-string">&lt;&lt;&lt;&#x27;EOT&#x27;
No $interpolation here.
EOT</span>;
""");
    }

    [Fact]
    public void String_NowdocIndented()
    {
        AssertHighlighter("php",
"""
    $msg = <<<'EOT'
        Verbatim text.
        EOT;
""",
"""
    <span class="hljs-variable">$msg</span> = <span class="hljs-string">&lt;&lt;&lt;&#x27;EOT&#x27;
        Verbatim text.
        EOT</span>;
""");
    }

    [Fact]
    public void String_Concat()
    {
        AssertHighlighter("php",
"""
$msg = "hello " . $name . "!";
""",
"""
<span class="hljs-variable">$msg</span> = <span class="hljs-string">&quot;hello &quot;</span> . <span class="hljs-variable">$name</span> . <span class="hljs-string">&quot;!&quot;</span>;
""");
    }

    [Fact]
    public void Number_Integer()
    {
        AssertHighlighter("php",
"""
$n = 42;
""",
"""
<span class="hljs-variable">$n</span> = <span class="hljs-number">42</span>;
""");
    }

    [Fact]
    public void Number_Negative()
    {
        AssertHighlighter("php",
"""
$n = -42;
""",
"""
<span class="hljs-variable">$n</span> = -<span class="hljs-number">42</span>;
""");
    }

    [Fact]
    public void Number_Float()
    {
        AssertHighlighter("php",
"""
$n = 3.14;
""",
"""
<span class="hljs-variable">$n</span> = <span class="hljs-number">3.14</span>;
""");
    }

    [Fact]
    public void Number_Exponent()
    {
        AssertHighlighter("php",
"""
$n = 1.5e10;
""",
"""
<span class="hljs-variable">$n</span> = <span class="hljs-number">1.5e10</span>;
""");
    }

    [Fact]
    public void Number_Hex()
    {
        AssertHighlighter("php",
"""
$n = 0xDEADBEEF;
""",
"""
<span class="hljs-variable">$n</span> = <span class="hljs-number">0xDEADBEEF</span>;
""");
    }

    [Fact]
    public void Number_Octal()
    {
        AssertHighlighter("php",
"""
$n = 0o755;
""",
"""
<span class="hljs-variable">$n</span> = <span class="hljs-number">0o755</span>;
""");
    }

    [Fact]
    public void Number_OctalLegacy()
    {
        AssertHighlighter("php",
"""
$n = 0755;
""",
"""
<span class="hljs-variable">$n</span> = <span class="hljs-number">0755</span>;
""");
    }

    [Fact]
    public void Number_Binary()
    {
        AssertHighlighter("php",
"""
$n = 0b1010_1100;
""",
"""
<span class="hljs-variable">$n</span> = <span class="hljs-number">0b1010_1100</span>;
""");
    }

    [Fact]
    public void Number_DigitSeparator()
    {
        AssertHighlighter("php",
"""
$n = 1_000_000;
""",
"""
<span class="hljs-variable">$n</span> = <span class="hljs-number">1_000_000</span>;
""");
    }

    [Fact]
    public void Operator_Arithmetic()
    {
        AssertHighlighter("php",
"""
$r = ($a + $b) * $c;
""",
"""
<span class="hljs-variable">$r</span> = (<span class="hljs-variable">$a</span> + <span class="hljs-variable">$b</span>) * <span class="hljs-variable">$c</span>;
""");
    }

    [Fact]
    public void Operator_PowerOp()
    {
        AssertHighlighter("php",
"""
$r = 2 ** 10;
""",
"""
<span class="hljs-variable">$r</span> = <span class="hljs-number">2</span> ** <span class="hljs-number">10</span>;
""");
    }

    [Fact]
    public void Operator_IntegerDiv()
    {
        AssertHighlighter("php",
"""
$r = intdiv(10, 3);
""",
"""
<span class="hljs-variable">$r</span> = <span class="hljs-title function_ invoke__">intdiv</span>(<span class="hljs-number">10</span>, <span class="hljs-number">3</span>);
""");
    }

    [Fact]
    public void Operator_Modulo()
    {
        AssertHighlighter("php",
"""
$r = $a % $b;
""",
"""
<span class="hljs-variable">$r</span> = <span class="hljs-variable">$a</span> % <span class="hljs-variable">$b</span>;
""");
    }

    [Fact]
    public void Operator_Concat()
    {
        AssertHighlighter("php",
"""
$s = "Hello, " . $name;
""",
"""
<span class="hljs-variable">$s</span> = <span class="hljs-string">&quot;Hello, &quot;</span> . <span class="hljs-variable">$name</span>;
""");
    }

    [Fact]
    public void Operator_ConcatAssign()
    {
        AssertHighlighter("php",
"""
$s .= "!";
""",
"""
<span class="hljs-variable">$s</span> .= <span class="hljs-string">&quot;!&quot;</span>;
""");
    }

    [Fact]
    public void Operator_Comparison()
    {
        AssertHighlighter("php",
"""
if ($a == $b || $c <> $d) run();
""",
"""
<span class="hljs-keyword">if</span> (<span class="hljs-variable">$a</span> == <span class="hljs-variable">$b</span> || <span class="hljs-variable">$c</span> &lt;&gt; <span class="hljs-variable">$d</span>) <span class="hljs-title function_ invoke__">run</span>();
""");
    }

    [Fact]
    public void Operator_StrictEqual()
    {
        AssertHighlighter("php",
"""
if ($a === $b && $c !== $d) run();
""",
"""
<span class="hljs-keyword">if</span> (<span class="hljs-variable">$a</span> === <span class="hljs-variable">$b</span> &amp;&amp; <span class="hljs-variable">$c</span> !== <span class="hljs-variable">$d</span>) <span class="hljs-title function_ invoke__">run</span>();
""");
    }

    [Fact]
    public void Operator_Spaceship()
    {
        AssertHighlighter("php",
"""
$cmp = $a <=> $b;
""",
"""
<span class="hljs-variable">$cmp</span> = <span class="hljs-variable">$a</span> &lt;=&gt; <span class="hljs-variable">$b</span>;
""");
    }

    [Fact]
    public void Operator_NullCoalesce()
    {
        AssertHighlighter("php",
"""
$value = $maybe ?? "default";
""",
"""
<span class="hljs-variable">$value</span> = <span class="hljs-variable">$maybe</span> ?? <span class="hljs-string">&quot;default&quot;</span>;
""");
    }

    [Fact]
    public void Operator_Ternary()
    {
        AssertHighlighter("php",
"""
$s = ($x > 0) ? "pos" : "non-pos";
""",
"""
<span class="hljs-variable">$s</span> = (<span class="hljs-variable">$x</span> &gt; <span class="hljs-number">0</span>) ? <span class="hljs-string">&quot;pos&quot;</span> : <span class="hljs-string">&quot;non-pos&quot;</span>;
""");
    }

    [Fact]
    public void Operator_ShortTernary()
    {
        AssertHighlighter("php",
"""
$s = $name ?: "anonymous";
""",
"""
<span class="hljs-variable">$s</span> = <span class="hljs-variable">$name</span> ?: <span class="hljs-string">&quot;anonymous&quot;</span>;
""");
    }

    [Fact]
    public void Operator_TypeCheck()
    {
        AssertHighlighter("php",
"""
if ($x instanceof User) handleUser($x);
""",
"""
<span class="hljs-keyword">if</span> (<span class="hljs-variable">$x</span> <span class="hljs-keyword">instanceof</span> User) <span class="hljs-title function_ invoke__">handleUser</span>(<span class="hljs-variable">$x</span>);
""");
    }

    [Fact]
    public void Operator_Bitwise()
    {
        AssertHighlighter("php",
"""
$r = $a & $b | $c ^ $d;
""",
"""
<span class="hljs-variable">$r</span> = <span class="hljs-variable">$a</span> &amp; <span class="hljs-variable">$b</span> | <span class="hljs-variable">$c</span> ^ <span class="hljs-variable">$d</span>;
""");
    }

    [Fact]
    public void Operator_Shift()
    {
        AssertHighlighter("php",
"""
$r = $a << 2 | $b >> 1;
""",
"""
<span class="hljs-variable">$r</span> = <span class="hljs-variable">$a</span> &lt;&lt; <span class="hljs-number">2</span> | <span class="hljs-variable">$b</span> &gt;&gt; <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void ControlFlow_IfElse()
    {
        AssertHighlighter("php",
"""
if ($x > 0) {
    positive();
} elseif ($x < 0) {
    negative();
} else {
    zero();
}
""",
"""
<span class="hljs-keyword">if</span> (<span class="hljs-variable">$x</span> &gt; <span class="hljs-number">0</span>) {
    <span class="hljs-title function_ invoke__">positive</span>();
} <span class="hljs-keyword">elseif</span> (<span class="hljs-variable">$x</span> &lt; <span class="hljs-number">0</span>) {
    <span class="hljs-title function_ invoke__">negative</span>();
} <span class="hljs-keyword">else</span> {
    <span class="hljs-title function_ invoke__">zero</span>();
}
""");
    }

    [Fact]
    public void ControlFlow_IfColon()
    {
        AssertHighlighter("php",
"""
if ($x > 0):
    positive();
elseif ($x < 0):
    negative();
else:
    zero();
endif;
""",
"""
<span class="hljs-keyword">if</span> (<span class="hljs-variable">$x</span> &gt; <span class="hljs-number">0</span>):
    <span class="hljs-title function_ invoke__">positive</span>();
<span class="hljs-keyword">elseif</span> (<span class="hljs-variable">$x</span> &lt; <span class="hljs-number">0</span>):
    <span class="hljs-title function_ invoke__">negative</span>();
<span class="hljs-keyword">else</span>:
    <span class="hljs-title function_ invoke__">zero</span>();
<span class="hljs-keyword">endif</span>;
""");
    }

    [Fact]
    public void ControlFlow_Switch()
    {
        AssertHighlighter("php",
"""
switch ($status) {
    case "open":
    case "pending":
        active();
        break;
    case "closed":
        inactive();
        break;
    default:
        unknown();
}
""",
"""
<span class="hljs-keyword">switch</span> (<span class="hljs-variable">$status</span>) {
    <span class="hljs-keyword">case</span> <span class="hljs-string">&quot;open&quot;</span>:
    <span class="hljs-keyword">case</span> <span class="hljs-string">&quot;pending&quot;</span>:
        <span class="hljs-title function_ invoke__">active</span>();
        <span class="hljs-keyword">break</span>;
    <span class="hljs-keyword">case</span> <span class="hljs-string">&quot;closed&quot;</span>:
        <span class="hljs-title function_ invoke__">inactive</span>();
        <span class="hljs-keyword">break</span>;
    <span class="hljs-keyword">default</span>:
        <span class="hljs-title function_ invoke__">unknown</span>();
}
""");
    }

    [Fact]
    public void ControlFlow_Match()
    {
        AssertHighlighter("php",
"""
$label = match ($status) {
    "open", "pending" => "active",
    "closed"          => "inactive",
    default           => "unknown",
};
""",
"""
<span class="hljs-variable">$label</span> = <span class="hljs-keyword">match</span> (<span class="hljs-variable">$status</span>) {
    <span class="hljs-string">&quot;open&quot;</span>, <span class="hljs-string">&quot;pending&quot;</span> =&gt; <span class="hljs-string">&quot;active&quot;</span>,
    <span class="hljs-string">&quot;closed&quot;</span>          =&gt; <span class="hljs-string">&quot;inactive&quot;</span>,
    <span class="hljs-keyword">default</span>           =&gt; <span class="hljs-string">&quot;unknown&quot;</span>,
};
""");
    }

    [Fact]
    public void ControlFlow_MatchNoDefault()
    {
        AssertHighlighter("php",
"""
$label = match (true) {
    $x > 0 => "positive",
    $x < 0 => "negative",
    default => "zero",
};
""",
"""
<span class="hljs-variable">$label</span> = <span class="hljs-keyword">match</span> (<span class="hljs-literal">true</span>) {
    <span class="hljs-variable">$x</span> &gt; <span class="hljs-number">0</span> =&gt; <span class="hljs-string">&quot;positive&quot;</span>,
    <span class="hljs-variable">$x</span> &lt; <span class="hljs-number">0</span> =&gt; <span class="hljs-string">&quot;negative&quot;</span>,
    <span class="hljs-keyword">default</span> =&gt; <span class="hljs-string">&quot;zero&quot;</span>,
};
""");
    }

    [Fact]
    public void ControlFlow_ForLoop()
    {
        AssertHighlighter("php",
"""
for ($i = 0; $i < 10; $i++) {
    echo $i;
}
""",
"""
<span class="hljs-keyword">for</span> (<span class="hljs-variable">$i</span> = <span class="hljs-number">0</span>; <span class="hljs-variable">$i</span> &lt; <span class="hljs-number">10</span>; <span class="hljs-variable">$i</span>++) {
    <span class="hljs-keyword">echo</span> <span class="hljs-variable">$i</span>;
}
""");
    }

    [Fact]
    public void ControlFlow_ForEach()
    {
        AssertHighlighter("php",
"""
foreach ($items as $item) {
    process($item);
}
""",
"""
<span class="hljs-keyword">foreach</span> (<span class="hljs-variable">$items</span> <span class="hljs-keyword">as</span> <span class="hljs-variable">$item</span>) {
    <span class="hljs-title function_ invoke__">process</span>(<span class="hljs-variable">$item</span>);
}
""");
    }

    [Fact]
    public void ControlFlow_ForEachKey()
    {
        AssertHighlighter("php",
"""
foreach ($ages as $name => $age) {
    echo "$name is $age";
}
""",
"""
<span class="hljs-keyword">foreach</span> (<span class="hljs-variable">$ages</span> <span class="hljs-keyword">as</span> <span class="hljs-variable">$name</span> =&gt; <span class="hljs-variable">$age</span>) {
    <span class="hljs-keyword">echo</span> <span class="hljs-string">&quot;<span class="hljs-subst">$name</span> is <span class="hljs-subst">$age</span>&quot;</span>;
}
""");
    }

    [Fact]
    public void ControlFlow_WhileLoop()
    {
        AssertHighlighter("php",
"""
while ($queue->count() > 0) {
    $queue->pop();
}
""",
"""
<span class="hljs-keyword">while</span> (<span class="hljs-variable">$queue</span>-&gt;<span class="hljs-title function_ invoke__">count</span>() &gt; <span class="hljs-number">0</span>) {
    <span class="hljs-variable">$queue</span>-&gt;<span class="hljs-title function_ invoke__">pop</span>();
}
""");
    }

    [Fact]
    public void ControlFlow_DoWhile()
    {
        AssertHighlighter("php",
"""
do {
    $line = readline();
} while ($line !== null);
""",
"""
<span class="hljs-keyword">do</span> {
    <span class="hljs-variable">$line</span> = <span class="hljs-title function_ invoke__">readline</span>();
} <span class="hljs-keyword">while</span> (<span class="hljs-variable">$line</span> !== <span class="hljs-literal">null</span>);
""");
    }

    [Fact]
    public void ControlFlow_Break()
    {
        AssertHighlighter("php",
"""
foreach ($items as $item) {
    if ($item->isBad()) break;
}
""",
"""
<span class="hljs-keyword">foreach</span> (<span class="hljs-variable">$items</span> <span class="hljs-keyword">as</span> <span class="hljs-variable">$item</span>) {
    <span class="hljs-keyword">if</span> (<span class="hljs-variable">$item</span>-&gt;<span class="hljs-title function_ invoke__">isBad</span>()) <span class="hljs-keyword">break</span>;
}
""");
    }

    [Fact]
    public void ControlFlow_BreakN()
    {
        AssertHighlighter("php",
"""
foreach ($items as $item) {
    foreach ($item->children as $c) {
        if ($c->isBad()) break 2;
    }
}
""",
"""
<span class="hljs-keyword">foreach</span> (<span class="hljs-variable">$items</span> <span class="hljs-keyword">as</span> <span class="hljs-variable">$item</span>) {
    <span class="hljs-keyword">foreach</span> (<span class="hljs-variable">$item</span>-&gt;children <span class="hljs-keyword">as</span> <span class="hljs-variable">$c</span>) {
        <span class="hljs-keyword">if</span> (<span class="hljs-variable">$c</span>-&gt;<span class="hljs-title function_ invoke__">isBad</span>()) <span class="hljs-keyword">break</span> <span class="hljs-number">2</span>;
    }
}
""");
    }

    [Fact]
    public void ControlFlow_Continue()
    {
        AssertHighlighter("php",
"""
foreach ($items as $item) {
    if (!$item->isValid()) continue;
    process($item);
}
""",
"""
<span class="hljs-keyword">foreach</span> (<span class="hljs-variable">$items</span> <span class="hljs-keyword">as</span> <span class="hljs-variable">$item</span>) {
    <span class="hljs-keyword">if</span> (!<span class="hljs-variable">$item</span>-&gt;<span class="hljs-title function_ invoke__">isValid</span>()) <span class="hljs-keyword">continue</span>;
    <span class="hljs-title function_ invoke__">process</span>(<span class="hljs-variable">$item</span>);
}
""");
    }

    [Fact]
    public void ControlFlow_Goto()
    {
        AssertHighlighter("php",
"""
start:
if ($count++ < 10) goto start;
""",
"""
start:
<span class="hljs-keyword">if</span> (<span class="hljs-variable">$count</span>++ &lt; <span class="hljs-number">10</span>) <span class="hljs-keyword">goto</span> start;
""");
    }

    [Fact]
    public void ControlFlow_TryCatch()
    {
        AssertHighlighter("php",
"""
try {
    risky();
} catch (Exception $ex) {
    error($ex);
}
""",
"""
<span class="hljs-keyword">try</span> {
    <span class="hljs-title function_ invoke__">risky</span>();
} <span class="hljs-keyword">catch</span> (<span class="hljs-built_in">Exception</span> <span class="hljs-variable">$ex</span>) {
    <span class="hljs-title function_ invoke__">error</span>(<span class="hljs-variable">$ex</span>);
}
""");
    }

    [Fact]
    public void ControlFlow_TryCatchMulti()
    {
        AssertHighlighter("php",
"""
try {
    risky();
} catch (InvalidArgumentException | TypeError $ex) {
    badArg($ex);
} catch (Exception $ex) {
    other($ex);
} finally {
    cleanup();
}
""",
"""
<span class="hljs-keyword">try</span> {
    <span class="hljs-title function_ invoke__">risky</span>();
} <span class="hljs-keyword">catch</span> (<span class="hljs-built_in">InvalidArgumentException</span> | <span class="hljs-built_in">TypeError</span> <span class="hljs-variable">$ex</span>) {
    <span class="hljs-title function_ invoke__">badArg</span>(<span class="hljs-variable">$ex</span>);
} <span class="hljs-keyword">catch</span> (<span class="hljs-built_in">Exception</span> <span class="hljs-variable">$ex</span>) {
    <span class="hljs-title function_ invoke__">other</span>(<span class="hljs-variable">$ex</span>);
} <span class="hljs-keyword">finally</span> {
    <span class="hljs-title function_ invoke__">cleanup</span>();
}
""");
    }

    [Fact]
    public void ControlFlow_CatchNoVar()
    {
        AssertHighlighter("php",
"""
try {
    risky();
} catch (Exception) {
    /* swallow */
}
""",
"""
<span class="hljs-keyword">try</span> {
    <span class="hljs-title function_ invoke__">risky</span>();
} <span class="hljs-keyword">catch</span> (<span class="hljs-built_in">Exception</span>) {
    <span class="hljs-comment">/* swallow */</span>
}
""");
    }

    [Fact]
    public void ControlFlow_Throw()
    {
        AssertHighlighter("php",
"""
throw new InvalidArgumentException("bad");
""",
"""
<span class="hljs-keyword">throw</span> <span class="hljs-keyword">new</span> <span class="hljs-built_in">InvalidArgumentException</span>(<span class="hljs-string">&quot;bad&quot;</span>);
""");
    }

    [Fact]
    public void ControlFlow_ThrowExpression()
    {
        AssertHighlighter("php",
"""
$name = $input ?? throw new InvalidArgumentException("missing");
""",
"""
<span class="hljs-variable">$name</span> = <span class="hljs-variable">$input</span> ?? <span class="hljs-keyword">throw</span> <span class="hljs-keyword">new</span> <span class="hljs-built_in">InvalidArgumentException</span>(<span class="hljs-string">&quot;missing&quot;</span>);
""");
    }

    [Fact]
    public void Function_Simple()
    {
        AssertHighlighter("php",
"""
function add($a, $b) {
    return $a + $b;
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">add</span>(<span class="hljs-params"><span class="hljs-variable">$a</span>, <span class="hljs-variable">$b</span></span>) </span>{
    <span class="hljs-keyword">return</span> <span class="hljs-variable">$a</span> + <span class="hljs-variable">$b</span>;
}
""");
    }

    [Fact]
    public void Function_Typed()
    {
        AssertHighlighter("php",
"""
function add(int $a, int $b): int {
    return $a + $b;
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">add</span>(<span class="hljs-params"><span class="hljs-keyword">int</span> <span class="hljs-variable">$a</span>, <span class="hljs-keyword">int</span> <span class="hljs-variable">$b</span></span>): <span class="hljs-title">int</span> </span>{
    <span class="hljs-keyword">return</span> <span class="hljs-variable">$a</span> + <span class="hljs-variable">$b</span>;
}
""");
    }

    [Fact]
    public void Function_NullableType()
    {
        AssertHighlighter("php",
"""
function find(?int $id): ?User {
    return $id ? lookup($id) : null;
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">find</span>(<span class="hljs-params">?<span class="hljs-keyword">int</span> <span class="hljs-variable">$id</span></span>): ?<span class="hljs-title">User</span> </span>{
    <span class="hljs-keyword">return</span> <span class="hljs-variable">$id</span> ? <span class="hljs-title function_ invoke__">lookup</span>(<span class="hljs-variable">$id</span>) : <span class="hljs-literal">null</span>;
}
""");
    }

    [Fact]
    public void Function_UnionTypes()
    {
        AssertHighlighter("php",
"""
function parseLen(string|int $value): int {
    return is_string($value) ? strlen($value) : $value;
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">parseLen</span>(<span class="hljs-params"><span class="hljs-keyword">string</span>|<span class="hljs-keyword">int</span> <span class="hljs-variable">$value</span></span>): <span class="hljs-title">int</span> </span>{
    <span class="hljs-keyword">return</span> <span class="hljs-title function_ invoke__">is_string</span>(<span class="hljs-variable">$value</span>) ? <span class="hljs-title function_ invoke__">strlen</span>(<span class="hljs-variable">$value</span>) : <span class="hljs-variable">$value</span>;
}
""");
    }

    [Fact]
    public void Function_IntersectionTypes()
    {
        AssertHighlighter("php",
"""
function process(Countable&Traversable $items): void { /* ... */ }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">process</span>(<span class="hljs-params"><span class="hljs-built_in">Countable</span>&amp;<span class="hljs-built_in">Traversable</span> <span class="hljs-variable">$items</span></span>): <span class="hljs-title">void</span> </span>{ <span class="hljs-comment">/* ... */</span> }
""");
    }

    [Fact]
    public void Function_NeverType()
    {
        AssertHighlighter("php",
"""
function fail(string $message): never {
    throw new RuntimeException($message);
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">fail</span>(<span class="hljs-params"><span class="hljs-keyword">string</span> <span class="hljs-variable">$message</span></span>): <span class="hljs-title">never</span> </span>{
    <span class="hljs-keyword">throw</span> <span class="hljs-keyword">new</span> <span class="hljs-built_in">RuntimeException</span>(<span class="hljs-variable">$message</span>);
}
""");
    }

    [Fact]
    public void Function_TrueFalseType()
    {
        AssertHighlighter("php",
"""
function isReady(): true { return true; }
function failed(): false { return false; }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">isReady</span>(<span class="hljs-params"></span>): <span class="hljs-title">true</span> </span>{ <span class="hljs-keyword">return</span> <span class="hljs-literal">true</span>; }
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">failed</span>(<span class="hljs-params"></span>): <span class="hljs-title">false</span> </span>{ <span class="hljs-keyword">return</span> <span class="hljs-literal">false</span>; }
""");
    }

    [Fact]
    public void Function_Variadic()
    {
        AssertHighlighter("php",
"""
function sum(int ...$values): int {
    return array_sum($values);
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">sum</span>(<span class="hljs-params"><span class="hljs-keyword">int</span> ...<span class="hljs-variable">$values</span></span>): <span class="hljs-title">int</span> </span>{
    <span class="hljs-keyword">return</span> <span class="hljs-title function_ invoke__">array_sum</span>(<span class="hljs-variable">$values</span>);
}
""");
    }

    [Fact]
    public void Function_SpreadCall()
    {
        AssertHighlighter("php",
"""
sum(...$numbers);
""",
"""
<span class="hljs-title function_ invoke__">sum</span>(...<span class="hljs-variable">$numbers</span>);
""");
    }

    [Fact]
    public void Function_DefaultArgs()
    {
        AssertHighlighter("php",
"""
function greet(string $name, string $greeting = "Hello"): string {
    return "$greeting, $name";
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">greet</span>(<span class="hljs-params"><span class="hljs-keyword">string</span> <span class="hljs-variable">$name</span>, <span class="hljs-keyword">string</span> <span class="hljs-variable">$greeting</span> = <span class="hljs-string">&quot;Hello&quot;</span></span>): <span class="hljs-title">string</span> </span>{
    <span class="hljs-keyword">return</span> <span class="hljs-string">&quot;<span class="hljs-subst">$greeting</span>, <span class="hljs-subst">$name</span>&quot;</span>;
}
""");
    }

    [Fact]
    public void Function_NamedArgs()
    {
        AssertHighlighter("php",
"""
createUser(name: "alice", email: "alice@example.com", isActive: true);
""",
"""
<span class="hljs-title function_ invoke__">createUser</span>(<span class="hljs-attr">name</span>: <span class="hljs-string">&quot;alice&quot;</span>, <span class="hljs-attr">email</span>: <span class="hljs-string">&quot;alice@example.com&quot;</span>, <span class="hljs-attr">isActive</span>: <span class="hljs-literal">true</span>);
""");
    }

    [Fact]
    public void Function_ByReferenceParam()
    {
        AssertHighlighter("php",
"""
function swap(int &$a, int &$b): void {
    [$a, $b] = [$b, $a];
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">swap</span>(<span class="hljs-params"><span class="hljs-keyword">int</span> &amp;<span class="hljs-variable">$a</span>, <span class="hljs-keyword">int</span> &amp;<span class="hljs-variable">$b</span></span>): <span class="hljs-title">void</span> </span>{
    [<span class="hljs-variable">$a</span>, <span class="hljs-variable">$b</span>] = [<span class="hljs-variable">$b</span>, <span class="hljs-variable">$a</span>];
}
""");
    }

    [Fact]
    public void Function_FirstClassCallable()
    {
        AssertHighlighter("php",
"""
$len = strlen(...);
""",
"""
<span class="hljs-variable">$len</span> = <span class="hljs-title function_ invoke__">strlen</span>(...);
""");
    }

    [Fact]
    public void Function_FirstClassClosureMethod()
    {
        AssertHighlighter("php",
"""
$callback = $service->process(...);
""",
"""
<span class="hljs-variable">$callback</span> = <span class="hljs-variable">$service</span>-&gt;<span class="hljs-title function_ invoke__">process</span>(...);
""");
    }

    [Fact]
    public void Function_ReturnByRef()
    {
        AssertHighlighter("php",
"""
function &counter(): int {
    static $count = 0;
    return $count;
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> &amp;<span class="hljs-title">counter</span>(<span class="hljs-params"></span>): <span class="hljs-title">int</span> </span>{
    <span class="hljs-built_in">static</span> <span class="hljs-variable">$count</span> = <span class="hljs-number">0</span>;
    <span class="hljs-keyword">return</span> <span class="hljs-variable">$count</span>;
}
""");
    }

    [Fact]
    public void Closure_Anonymous()
    {
        AssertHighlighter("php",
"""
$square = function ($x) { return $x * $x; };
""",
"""
<span class="hljs-variable">$square</span> = <span class="hljs-function"><span class="hljs-keyword">function</span> (<span class="hljs-params"><span class="hljs-variable">$x</span></span>) </span>{ <span class="hljs-keyword">return</span> <span class="hljs-variable">$x</span> * <span class="hljs-variable">$x</span>; };
""");
    }

    [Fact]
    public void Closure_UseClause()
    {
        AssertHighlighter("php",
"""
$multiplier = 3;
$mul = function ($x) use ($multiplier) {
    return $x * $multiplier;
};
""",
"""
<span class="hljs-variable">$multiplier</span> = <span class="hljs-number">3</span>;
<span class="hljs-variable">$mul</span> = <span class="hljs-function"><span class="hljs-keyword">function</span> (<span class="hljs-params"><span class="hljs-variable">$x</span></span>) <span class="hljs-keyword">use</span> (<span class="hljs-params"><span class="hljs-variable">$multiplier</span></span>) </span>{
    <span class="hljs-keyword">return</span> <span class="hljs-variable">$x</span> * <span class="hljs-variable">$multiplier</span>;
};
""");
    }

    [Fact]
    public void Closure_UseByReference()
    {
        AssertHighlighter("php",
"""
$count = 0;
$inc = function () use (&$count) {
    $count++;
};
""",
"""
<span class="hljs-variable">$count</span> = <span class="hljs-number">0</span>;
<span class="hljs-variable">$inc</span> = <span class="hljs-function"><span class="hljs-keyword">function</span> (<span class="hljs-params"></span>) <span class="hljs-keyword">use</span> (<span class="hljs-params">&amp;<span class="hljs-variable">$count</span></span>) </span>{
    <span class="hljs-variable">$count</span>++;
};
""");
    }

    [Fact]
    public void Closure_Arrow()
    {
        AssertHighlighter("php",
"""
$square = fn ($x) => $x * $x;
""",
"""
<span class="hljs-variable">$square</span> = <span class="hljs-function"><span class="hljs-keyword">fn</span> (<span class="hljs-params"><span class="hljs-variable">$x</span></span>) =&gt;</span> <span class="hljs-variable">$x</span> * <span class="hljs-variable">$x</span>;
""");
    }

    [Fact]
    public void Closure_ArrowMulti()
    {
        AssertHighlighter("php",
"""
$add = fn ($a, $b) => $a + $b;
""",
"""
<span class="hljs-variable">$add</span> = <span class="hljs-function"><span class="hljs-keyword">fn</span> (<span class="hljs-params"><span class="hljs-variable">$a</span>, <span class="hljs-variable">$b</span></span>) =&gt;</span> <span class="hljs-variable">$a</span> + <span class="hljs-variable">$b</span>;
""");
    }

    [Fact]
    public void Closure_ArrowTyped()
    {
        AssertHighlighter("php",
"""
$add = fn (int $a, int $b): int => $a + $b;
""",
"""
<span class="hljs-variable">$add</span> = <span class="hljs-function"><span class="hljs-keyword">fn</span> (<span class="hljs-params"><span class="hljs-keyword">int</span> <span class="hljs-variable">$a</span>, <span class="hljs-keyword">int</span> <span class="hljs-variable">$b</span></span>): <span class="hljs-title">int</span> =&gt;</span> <span class="hljs-variable">$a</span> + <span class="hljs-variable">$b</span>;
""");
    }

    [Fact]
    public void Closure_StaticClosure()
    {
        AssertHighlighter("php",
"""
$run = static function () { /* no $this */ };
""",
"""
<span class="hljs-variable">$run</span> = <span class="hljs-built_in">static</span> <span class="hljs-function"><span class="hljs-keyword">function</span> (<span class="hljs-params"></span>) </span>{ <span class="hljs-comment">/* no $this */</span> };
""");
    }

    [Fact]
    public void Class_Simple()
    {
        AssertHighlighter("php",
"""
class User {
    public $name;
    public $age;
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">User</span> </span>{
    <span class="hljs-keyword">public</span> <span class="hljs-variable">$name</span>;
    <span class="hljs-keyword">public</span> <span class="hljs-variable">$age</span>;
}
""");
    }

    [Fact]
    public void Class_Typed()
    {
        AssertHighlighter("php",
"""
class User {
    public string $name;
    public int $age;
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">User</span> </span>{
    <span class="hljs-keyword">public</span> <span class="hljs-keyword">string</span> <span class="hljs-variable">$name</span>;
    <span class="hljs-keyword">public</span> <span class="hljs-keyword">int</span> <span class="hljs-variable">$age</span>;
}
""");
    }

    [Fact]
    public void Class_Constructor()
    {
        AssertHighlighter("php",
"""
class User {
    public string $name;

    public function __construct(string $name) {
        $this->name = $name;
    }
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">User</span> </span>{
    <span class="hljs-keyword">public</span> <span class="hljs-keyword">string</span> <span class="hljs-variable">$name</span>;

    <span class="hljs-keyword">public</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">__construct</span>(<span class="hljs-params"><span class="hljs-keyword">string</span> <span class="hljs-variable">$name</span></span>) </span>{
        <span class="hljs-variable language_">$this</span>-&gt;name = <span class="hljs-variable">$name</span>;
    }
}
""");
    }

    [Fact]
    public void Class_PromotedCtor()
    {
        AssertHighlighter("php",
"""
class User {
    public function __construct(
        public readonly string $name,
        public readonly int $age,
        private string $secret = ""
    ) {}
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">User</span> </span>{
    <span class="hljs-keyword">public</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">__construct</span>(<span class="hljs-params">
        <span class="hljs-keyword">public</span> <span class="hljs-keyword">readonly</span> <span class="hljs-keyword">string</span> <span class="hljs-variable">$name</span>,
        <span class="hljs-keyword">public</span> <span class="hljs-keyword">readonly</span> <span class="hljs-keyword">int</span> <span class="hljs-variable">$age</span>,
        <span class="hljs-keyword">private</span> <span class="hljs-keyword">string</span> <span class="hljs-variable">$secret</span> = <span class="hljs-string">&quot;&quot;</span>
    </span>) </span>{}
}
""");
    }

    [Fact]
    public void Class_Inheritance()
    {
        AssertHighlighter("php",
"""
class Manager extends Employee {
    public function __construct(string $name, public int $level) {
        parent::__construct($name);
    }
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Manager</span> <span class="hljs-keyword">extends</span> <span class="hljs-title">Employee</span> </span>{
    <span class="hljs-keyword">public</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">__construct</span>(<span class="hljs-params"><span class="hljs-keyword">string</span> <span class="hljs-variable">$name</span>, <span class="hljs-keyword">public</span> <span class="hljs-keyword">int</span> <span class="hljs-variable">$level</span></span>) </span>{
        <span class="hljs-built_in">parent</span>::<span class="hljs-title function_ invoke__">__construct</span>(<span class="hljs-variable">$name</span>);
    }
}
""");
    }

    [Fact]
    public void Class_ImplementsOne()
    {
        AssertHighlighter("php",
"""
class Logger implements LoggerInterface {
    public function log(string $message): void { /* ... */ }
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Logger</span> <span class="hljs-keyword">implements</span> <span class="hljs-title">LoggerInterface</span> </span>{
    <span class="hljs-keyword">public</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">log</span>(<span class="hljs-params"><span class="hljs-keyword">string</span> <span class="hljs-variable">$message</span></span>): <span class="hljs-title">void</span> </span>{ <span class="hljs-comment">/* ... */</span> }
}
""");
    }

    [Fact]
    public void Class_ImplementsMany()
    {
        AssertHighlighter("php",
"""
class Resource implements Countable, IteratorAggregate, Stringable {
    /* ... */
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Resource</span> <span class="hljs-keyword">implements</span> <span class="hljs-title">Countable</span>, <span class="hljs-title">IteratorAggregate</span>, <span class="hljs-title">Stringable</span> </span>{
    <span class="hljs-comment">/* ... */</span>
}
""");
    }

    [Fact]
    public void Class_AbstractClass()
    {
        AssertHighlighter("php",
"""
abstract class Shape {
    abstract public function area(): float;
}
""",
"""
<span class="hljs-keyword">abstract</span> <span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Shape</span> </span>{
    <span class="hljs-keyword">abstract</span> <span class="hljs-keyword">public</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">area</span>(<span class="hljs-params"></span>): <span class="hljs-title">float</span></span>;
}
""");
    }

    [Fact]
    public void Class_FinalClass()
    {
        AssertHighlighter("php",
"""
final class Money {
    public function __construct(public readonly float $amount, public readonly string $currency) {}
}
""",
"""
<span class="hljs-keyword">final</span> <span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Money</span> </span>{
    <span class="hljs-keyword">public</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">__construct</span>(<span class="hljs-params"><span class="hljs-keyword">public</span> <span class="hljs-keyword">readonly</span> <span class="hljs-keyword">float</span> <span class="hljs-variable">$amount</span>, <span class="hljs-keyword">public</span> <span class="hljs-keyword">readonly</span> <span class="hljs-keyword">string</span> <span class="hljs-variable">$currency</span></span>) </span>{}
}
""");
    }

    [Fact]
    public void Class_ReadonlyClass()
    {
        AssertHighlighter("php",
"""
final readonly class Money {
    public function __construct(public float $amount, public string $currency) {}
}
""",
"""
<span class="hljs-keyword">final</span> <span class="hljs-keyword">readonly</span> <span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Money</span> </span>{
    <span class="hljs-keyword">public</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">__construct</span>(<span class="hljs-params"><span class="hljs-keyword">public</span> <span class="hljs-keyword">float</span> <span class="hljs-variable">$amount</span>, <span class="hljs-keyword">public</span> <span class="hljs-keyword">string</span> <span class="hljs-variable">$currency</span></span>) </span>{}
}
""");
    }

    [Fact]
    public void Class_StaticMembers()
    {
        AssertHighlighter("php",
"""
class Counter {
    private static int $count = 0;

    public static function increment(): int {
        return ++self::$count;
    }
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Counter</span> </span>{
    <span class="hljs-keyword">private</span> <span class="hljs-built_in">static</span> <span class="hljs-keyword">int</span> <span class="hljs-variable">$count</span> = <span class="hljs-number">0</span>;

    <span class="hljs-keyword">public</span> <span class="hljs-built_in">static</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">increment</span>(<span class="hljs-params"></span>): <span class="hljs-title">int</span> </span>{
        <span class="hljs-keyword">return</span> ++<span class="hljs-built_in">self</span>::<span class="hljs-variable">$count</span>;
    }
}
""");
    }

    [Fact]
    public void Class_ConstClass()
    {
        AssertHighlighter("php",
"""
class Config {
    public const VERSION = "1.0";
    public const int DEFAULT_PORT = 8080;
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Config</span> </span>{
    <span class="hljs-keyword">public</span> <span class="hljs-keyword">const</span> <span class="hljs-variable constant_">VERSION</span> = <span class="hljs-string">&quot;1.0&quot;</span>;
    <span class="hljs-keyword">public</span> <span class="hljs-keyword">const</span> <span class="hljs-variable constant_">int</span> DEFAULT_PORT = <span class="hljs-number">8080</span>;
}
""");
    }

    [Fact]
    public void Class_TypedConstants()
    {
        AssertHighlighter("php",
"""
class Limits {
    public const int MIN = 0;
    public const string LABEL = "max";
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Limits</span> </span>{
    <span class="hljs-keyword">public</span> <span class="hljs-keyword">const</span> <span class="hljs-variable constant_">int</span> MIN = <span class="hljs-number">0</span>;
    <span class="hljs-keyword">public</span> <span class="hljs-keyword">const</span> <span class="hljs-variable constant_">string</span> LABEL = <span class="hljs-string">&quot;max&quot;</span>;
}
""");
    }

    [Fact]
    public void Class_MagicMethods()
    {
        AssertHighlighter("php",
"""
class Vector {
    public function __construct(private array $data) {}
    public function __get(string $name) { return $this->data[$name] ?? null; }
    public function __set(string $name, $value): void { $this->data[$name] = $value; }
    public function __toString(): string { return "Vector"; }
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Vector</span> </span>{
    <span class="hljs-keyword">public</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">__construct</span>(<span class="hljs-params"><span class="hljs-keyword">private</span> <span class="hljs-keyword">array</span> <span class="hljs-variable">$data</span></span>) </span>{}
    <span class="hljs-keyword">public</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">__get</span>(<span class="hljs-params"><span class="hljs-keyword">string</span> <span class="hljs-variable">$name</span></span>) </span>{ <span class="hljs-keyword">return</span> <span class="hljs-variable language_">$this</span>-&gt;data[<span class="hljs-variable">$name</span>] ?? <span class="hljs-literal">null</span>; }
    <span class="hljs-keyword">public</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">__set</span>(<span class="hljs-params"><span class="hljs-keyword">string</span> <span class="hljs-variable">$name</span>, <span class="hljs-variable">$value</span></span>): <span class="hljs-title">void</span> </span>{ <span class="hljs-variable language_">$this</span>-&gt;data[<span class="hljs-variable">$name</span>] = <span class="hljs-variable">$value</span>; }
    <span class="hljs-keyword">public</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">__toString</span>(<span class="hljs-params"></span>): <span class="hljs-title">string</span> </span>{ <span class="hljs-keyword">return</span> <span class="hljs-string">&quot;Vector&quot;</span>; }
}
""");
    }

    [Fact]
    public void Class_StaticConstructor()
    {
        AssertHighlighter("php",
"""
class User {
    public static function fromArray(array $data): self {
        return new self($data["name"], $data["age"]);
    }
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">User</span> </span>{
    <span class="hljs-keyword">public</span> <span class="hljs-built_in">static</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">fromArray</span>(<span class="hljs-params"><span class="hljs-keyword">array</span> <span class="hljs-variable">$data</span></span>): <span class="hljs-title">self</span> </span>{
        <span class="hljs-keyword">return</span> <span class="hljs-keyword">new</span> <span class="hljs-built_in">self</span>(<span class="hljs-variable">$data</span>[<span class="hljs-string">&quot;name&quot;</span>], <span class="hljs-variable">$data</span>[<span class="hljs-string">&quot;age&quot;</span>]);
    }
}
""");
    }

    [Fact]
    public void Class_NewInInitializer()
    {
        AssertHighlighter("php",
"""
class Service {
    public function __construct(
        private Logger $logger = new NullLogger()
    ) {}
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Service</span> </span>{
    <span class="hljs-keyword">public</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">__construct</span>(<span class="hljs-params">
        <span class="hljs-keyword">private</span> Logger <span class="hljs-variable">$logger</span> = <span class="hljs-keyword">new</span> NullLogger(<span class="hljs-params"></span>)
    </span>) </span>{}
}
""");
    }

    [Fact]
    public void Class_AsymmetricVisibility()
    {
        AssertHighlighter("php",
"""
class User {
    public function __construct(
        public private(set) string $email
    ) {}
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">User</span> </span>{
    <span class="hljs-keyword">public</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">__construct</span>(<span class="hljs-params">
        <span class="hljs-keyword">public</span> <span class="hljs-keyword">private</span>(<span class="hljs-params">set</span>) <span class="hljs-keyword">string</span> <span class="hljs-variable">$email</span>
    </span>) </span>{}
}
""");
    }

    [Fact]
    public void Interface_Simple()
    {
        AssertHighlighter("php",
"""
interface LoggerInterface {
    public function log(string $message): void;
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">interface</span> <span class="hljs-title">LoggerInterface</span> </span>{
    <span class="hljs-keyword">public</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">log</span>(<span class="hljs-params"><span class="hljs-keyword">string</span> <span class="hljs-variable">$message</span></span>): <span class="hljs-title">void</span></span>;
}
""");
    }

    [Fact]
    public void Interface_Constants()
    {
        AssertHighlighter("php",
"""
interface Status {
    const ACTIVE = "active";
    const INACTIVE = "inactive";
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">interface</span> <span class="hljs-title">Status</span> </span>{
    <span class="hljs-keyword">const</span> <span class="hljs-variable constant_">ACTIVE</span> = <span class="hljs-string">&quot;active&quot;</span>;
    <span class="hljs-keyword">const</span> <span class="hljs-variable constant_">INACTIVE</span> = <span class="hljs-string">&quot;inactive&quot;</span>;
}
""");
    }

    [Fact]
    public void Interface_Inheritance()
    {
        AssertHighlighter("php",
"""
interface Resource extends Countable, IteratorAggregate { }
""",
"""
<span class="hljs-class"><span class="hljs-keyword">interface</span> <span class="hljs-title">Resource</span> <span class="hljs-keyword">extends</span> <span class="hljs-title">Countable</span>, <span class="hljs-title">IteratorAggregate</span> </span>{ }
""");
    }

    [Fact]
    public void Interface_WithDefaults()
    {
        AssertHighlighter("php",
"""
interface Greeter {
    public function greet(string $name = "world"): string;
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">interface</span> <span class="hljs-title">Greeter</span> </span>{
    <span class="hljs-keyword">public</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">greet</span>(<span class="hljs-params"><span class="hljs-keyword">string</span> <span class="hljs-variable">$name</span> = <span class="hljs-string">&quot;world&quot;</span></span>): <span class="hljs-title">string</span></span>;
}
""");
    }

    [Fact]
    public void Trait_Definition()
    {
        AssertHighlighter("php",
"""
trait Greet {
    public function hello(string $name): string {
        return "Hello, $name";
    }
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">trait</span> <span class="hljs-title">Greet</span> </span>{
    <span class="hljs-keyword">public</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">hello</span>(<span class="hljs-params"><span class="hljs-keyword">string</span> <span class="hljs-variable">$name</span></span>): <span class="hljs-title">string</span> </span>{
        <span class="hljs-keyword">return</span> <span class="hljs-string">&quot;Hello, <span class="hljs-subst">$name</span>&quot;</span>;
    }
}
""");
    }

    [Fact]
    public void Trait_Use()
    {
        AssertHighlighter("php",
"""
class Greeter {
    use Greet;
    use Logger, Counter;
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Greeter</span> </span>{
    <span class="hljs-keyword">use</span> <span class="hljs-title">Greet</span>;
    <span class="hljs-keyword">use</span> <span class="hljs-title">Logger</span>, <span class="hljs-title">Counter</span>;
}
""");
    }

    [Fact]
    public void Trait_WithConflict()
    {
        AssertHighlighter("php",
"""
class Mixed {
    use Greet, Welcomer {
        Greet::hello insteadof Welcomer;
        Welcomer::hello as welcomeHello;
    }
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Mixed</span> </span>{
    <span class="hljs-keyword">use</span> <span class="hljs-title">Greet</span>, <span class="hljs-title">Welcomer</span> {
        <span class="hljs-title">Greet</span>::<span class="hljs-title">hello</span> <span class="hljs-title">insteadof</span> <span class="hljs-title">Welcomer</span>;
        <span class="hljs-title class_">Welcomer</span>::<span class="hljs-variable constant_">hello</span> <span class="hljs-keyword">as</span> welcomeHello;
    }
}
""");
    }

    [Fact]
    public void Enum_PureEnum()
    {
        AssertHighlighter("php",
"""
enum Status {
    case Active;
    case Inactive;
    case Pending;
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">enum</span> <span class="hljs-title">Status</span> </span>{
    <span class="hljs-keyword">case</span> Active;
    <span class="hljs-keyword">case</span> Inactive;
    <span class="hljs-keyword">case</span> Pending;
}
""");
    }

    [Fact]
    public void Enum_BackedString()
    {
        AssertHighlighter("php",
"""
enum Status: string {
    case Active   = "active";
    case Inactive = "inactive";
    case Pending  = "pending";
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">enum</span> <span class="hljs-title">Status</span>: <span class="hljs-title">string</span> </span>{
    <span class="hljs-keyword">case</span> Active   = <span class="hljs-string">&quot;active&quot;</span>;
    <span class="hljs-keyword">case</span> Inactive = <span class="hljs-string">&quot;inactive&quot;</span>;
    <span class="hljs-keyword">case</span> Pending  = <span class="hljs-string">&quot;pending&quot;</span>;
}
""");
    }

    [Fact]
    public void Enum_BackedInt()
    {
        AssertHighlighter("php",
"""
enum HttpCode: int {
    case Ok       = 200;
    case NotFound = 404;
    case Error    = 500;
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">enum</span> <span class="hljs-title">HttpCode</span>: <span class="hljs-title">int</span> </span>{
    <span class="hljs-keyword">case</span> Ok       = <span class="hljs-number">200</span>;
    <span class="hljs-keyword">case</span> NotFound = <span class="hljs-number">404</span>;
    <span class="hljs-keyword">case</span> <span class="hljs-built_in">Error</span>    = <span class="hljs-number">500</span>;
}
""");
    }

    [Fact]
    public void Enum_WithMethods()
    {
        AssertHighlighter("php",
"""
enum Status: string {
    case Active   = "active";
    case Inactive = "inactive";

    public function label(): string {
        return match ($this) {
            self::Active   => "Active",
            self::Inactive => "Inactive",
        };
    }
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">enum</span> <span class="hljs-title">Status</span>: <span class="hljs-title">string</span> </span>{
    <span class="hljs-keyword">case</span> Active   = <span class="hljs-string">&quot;active&quot;</span>;
    <span class="hljs-keyword">case</span> Inactive = <span class="hljs-string">&quot;inactive&quot;</span>;

    <span class="hljs-keyword">public</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">label</span>(<span class="hljs-params"></span>): <span class="hljs-title">string</span> </span>{
        <span class="hljs-keyword">return</span> <span class="hljs-keyword">match</span> (<span class="hljs-variable language_">$this</span>) {
            <span class="hljs-built_in">self</span>::<span class="hljs-variable constant_">Active</span>   =&gt; <span class="hljs-string">&quot;Active&quot;</span>,
            <span class="hljs-built_in">self</span>::<span class="hljs-variable constant_">Inactive</span> =&gt; <span class="hljs-string">&quot;Inactive&quot;</span>,
        };
    }
}
""");
    }

    [Fact]
    public void Enum_ImplementsInterface()
    {
        AssertHighlighter("php",
"""
enum Status: string implements HasLabel {
    case Active   = "active";
    case Inactive = "inactive";

    public function getLabel(): string {
        return ucfirst($this->value);
    }
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">enum</span> <span class="hljs-title">Status</span>: <span class="hljs-title">string</span> <span class="hljs-keyword">implements</span> <span class="hljs-title">HasLabel</span> </span>{
    <span class="hljs-keyword">case</span> Active   = <span class="hljs-string">&quot;active&quot;</span>;
    <span class="hljs-keyword">case</span> Inactive = <span class="hljs-string">&quot;inactive&quot;</span>;

    <span class="hljs-keyword">public</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">getLabel</span>(<span class="hljs-params"></span>): <span class="hljs-title">string</span> </span>{
        <span class="hljs-keyword">return</span> <span class="hljs-title function_ invoke__">ucfirst</span>(<span class="hljs-variable">$this</span>-&gt;value);
    }
}
""");
    }

    [Fact]
    public void Attribute_Simple()
    {
        AssertHighlighter("php",
"""
#[Route("/users")]
class UserController { }
""",
"""
<span class="hljs-meta">#[Route</span>(<span class="hljs-string">&quot;/users&quot;</span>)<span class="hljs-meta">]</span>
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">UserController</span> </span>{ }
""");
    }

    [Fact]
    public void Attribute_Multiple()
    {
        AssertHighlighter("php",
"""
#[Route('/users/{id}')]
#[RequireRole('admin')]
function getUser(int $id) { }
""",
"""
<span class="hljs-meta">#[Route</span>(<span class="hljs-string">&#x27;/users/{id}&#x27;</span>)<span class="hljs-meta">]</span>
<span class="hljs-meta">#[RequireRole</span>(<span class="hljs-string">&#x27;admin&#x27;</span>)<span class="hljs-meta">]</span>
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">getUser</span>(<span class="hljs-params"><span class="hljs-keyword">int</span> <span class="hljs-variable">$id</span></span>) </span>{ }
""");
    }

    [Fact]
    public void Attribute_Stacked()
    {
        AssertHighlighter("php",
"""
#[Route("/api"), RequireRole("admin"), Cache(ttl: 60)]
class Api { }
""",
"""
<span class="hljs-meta">#[Route</span>(<span class="hljs-string">&quot;/api&quot;</span>), <span class="hljs-meta">RequireRole</span>(<span class="hljs-string">&quot;admin&quot;</span>), <span class="hljs-meta">Cache</span>(<span class="hljs-attr">ttl</span>: <span class="hljs-number">60</span>)<span class="hljs-meta">]</span>
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Api</span> </span>{ }
""");
    }

    [Fact]
    public void Attribute_Definition()
    {
        AssertHighlighter("php",
"""
#[Attribute(Attribute::TARGET_METHOD)]
class Route {
    public function __construct(public string $path, public string $method = "GET") {}
}
""",
"""
<span class="hljs-meta">#[Attribute</span>(<span class="hljs-title class_">Attribute</span>::<span class="hljs-variable constant_">TARGET_METHOD</span>)<span class="hljs-meta">]</span>
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Route</span> </span>{
    <span class="hljs-keyword">public</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">__construct</span>(<span class="hljs-params"><span class="hljs-keyword">public</span> <span class="hljs-keyword">string</span> <span class="hljs-variable">$path</span>, <span class="hljs-keyword">public</span> <span class="hljs-keyword">string</span> <span class="hljs-variable">$method</span> = <span class="hljs-string">&quot;GET&quot;</span></span>) </span>{}
}
""");
    }

    [Fact]
    public void Attribute_ParamAttribute()
    {
        AssertHighlighter("php",
"""
function process(#[Sensitive] string $token) { }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">process</span>(<span class="hljs-params"><span class="hljs-meta">#[Sensitive</span><span class="hljs-meta">]</span> <span class="hljs-keyword">string</span> <span class="hljs-variable">$token</span></span>) </span>{ }
""");
    }

    [Fact]
    public void Generator_YieldSimple()
    {
        AssertHighlighter("php",
"""
function range(int $start, int $end): Generator {
    for ($i = $start; $i <= $end; $i++) {
        yield $i;
    }
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">range</span>(<span class="hljs-params"><span class="hljs-keyword">int</span> <span class="hljs-variable">$start</span>, <span class="hljs-keyword">int</span> <span class="hljs-variable">$end</span></span>): <span class="hljs-title">Generator</span> </span>{
    <span class="hljs-keyword">for</span> (<span class="hljs-variable">$i</span> = <span class="hljs-variable">$start</span>; <span class="hljs-variable">$i</span> &lt;= <span class="hljs-variable">$end</span>; <span class="hljs-variable">$i</span>++) {
        <span class="hljs-keyword">yield</span> <span class="hljs-variable">$i</span>;
    }
}
""");
    }

    [Fact]
    public void Generator_YieldKeyValue()
    {
        AssertHighlighter("php",
"""
function attribs(array $data): Generator {
    foreach ($data as $key => $value) {
        yield $key => $value;
    }
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">attribs</span>(<span class="hljs-params"><span class="hljs-keyword">array</span> <span class="hljs-variable">$data</span></span>): <span class="hljs-title">Generator</span> </span>{
    <span class="hljs-keyword">foreach</span> (<span class="hljs-variable">$data</span> <span class="hljs-keyword">as</span> <span class="hljs-variable">$key</span> =&gt; <span class="hljs-variable">$value</span>) {
        <span class="hljs-keyword">yield</span> <span class="hljs-variable">$key</span> =&gt; <span class="hljs-variable">$value</span>;
    }
}
""");
    }

    [Fact]
    public void Generator_YieldFrom()
    {
        AssertHighlighter("php",
"""
function flatten(array $nested): Generator {
    foreach ($nested as $item) {
        yield from $item;
    }
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">flatten</span>(<span class="hljs-params"><span class="hljs-keyword">array</span> <span class="hljs-variable">$nested</span></span>): <span class="hljs-title">Generator</span> </span>{
    <span class="hljs-keyword">foreach</span> (<span class="hljs-variable">$nested</span> <span class="hljs-keyword">as</span> <span class="hljs-variable">$item</span>) {
        <span class="hljs-keyword">yield</span> <span class="hljs-keyword">from</span> <span class="hljs-variable">$item</span>;
    }
}
""");
    }

    [Fact]
    public void Array_Numeric()
    {
        AssertHighlighter("php",
"""
$arr = [1, 2, 3];
""",
"""
<span class="hljs-variable">$arr</span> = [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>];
""");
    }

    [Fact]
    public void Array_NumericLegacy()
    {
        AssertHighlighter("php",
"""
$arr = array(1, 2, 3);
""",
"""
<span class="hljs-variable">$arr</span> = <span class="hljs-keyword">array</span>(<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>);
""");
    }

    [Fact]
    public void Array_Associative()
    {
        AssertHighlighter("php",
"""
$user = ["name" => "alice", "age" => 30];
""",
"""
<span class="hljs-variable">$user</span> = [<span class="hljs-string">&quot;name&quot;</span> =&gt; <span class="hljs-string">&quot;alice&quot;</span>, <span class="hljs-string">&quot;age&quot;</span> =&gt; <span class="hljs-number">30</span>];
""");
    }

    [Fact]
    public void Array_Nested()
    {
        AssertHighlighter("php",
"""
$matrix = [[1, 2], [3, 4]];
""",
"""
<span class="hljs-variable">$matrix</span> = [[<span class="hljs-number">1</span>, <span class="hljs-number">2</span>], [<span class="hljs-number">3</span>, <span class="hljs-number">4</span>]];
""");
    }

    [Fact]
    public void Array_Spread()
    {
        AssertHighlighter("php",
"""
$all = [...$first, ...$second, "extra"];
""",
"""
<span class="hljs-variable">$all</span> = [...<span class="hljs-variable">$first</span>, ...<span class="hljs-variable">$second</span>, <span class="hljs-string">&quot;extra&quot;</span>];
""");
    }

    [Fact]
    public void Array_AssocSpread()
    {
        AssertHighlighter("php",
"""
$merged = [...$defaults, ...$overrides];
""",
"""
<span class="hljs-variable">$merged</span> = [...<span class="hljs-variable">$defaults</span>, ...<span class="hljs-variable">$overrides</span>];
""");
    }

    [Fact]
    public void Array_DestructureNumeric()
    {
        AssertHighlighter("php",
"""
[$a, $b, $c] = $arr;
""",
"""
[<span class="hljs-variable">$a</span>, <span class="hljs-variable">$b</span>, <span class="hljs-variable">$c</span>] = <span class="hljs-variable">$arr</span>;
""");
    }

    [Fact]
    public void Array_DestructureAssoc()
    {
        AssertHighlighter("php",
"""
["name" => $name, "age" => $age] = $user;
""",
"""
[<span class="hljs-string">&quot;name&quot;</span> =&gt; <span class="hljs-variable">$name</span>, <span class="hljs-string">&quot;age&quot;</span> =&gt; <span class="hljs-variable">$age</span>] = <span class="hljs-variable">$user</span>;
""");
    }

    [Fact]
    public void Array_ListSyntax()
    {
        AssertHighlighter("php",
"""
list($a, $b) = $pair;
""",
"""
<span class="hljs-keyword">list</span>(<span class="hljs-variable">$a</span>, <span class="hljs-variable">$b</span>) = <span class="hljs-variable">$pair</span>;
""");
    }

    [Fact]
    public void Namespace_Declaration()
    {
        AssertHighlighter("php",
"""
<?php
namespace MyApp\Domain;
""",
"""
<span class="hljs-meta">&lt;?php</span>
<span class="hljs-keyword">namespace</span> <span class="hljs-title class_">MyApp</span>\<span class="hljs-title class_">Domain</span>;
""");
    }

    [Fact]
    public void Namespace_UseStmt()
    {
        AssertHighlighter("php",
"""
use MyApp\Domain\User;
""",
"""
<span class="hljs-keyword">use</span> <span class="hljs-title">MyApp</span>\<span class="hljs-title">Domain</span>\<span class="hljs-title">User</span>;
""");
    }

    [Fact]
    public void Namespace_UseAlias()
    {
        AssertHighlighter("php",
"""
use MyApp\Domain\User as DomainUser;
""",
"""
<span class="hljs-keyword">use</span> <span class="hljs-title">MyApp</span>\<span class="hljs-title">Domain</span>\<span class="hljs-title">User</span> <span class="hljs-keyword">as</span> <span class="hljs-title">DomainUser</span>;
""");
    }

    [Fact]
    public void Namespace_UseGrouped()
    {
        AssertHighlighter("php",
"""
use MyApp\Domain\{User, Role, Permission};
""",
"""
<span class="hljs-keyword">use</span> <span class="hljs-title">MyApp</span>\<span class="hljs-title">Domain</span>\{<span class="hljs-title">User</span>, <span class="hljs-title">Role</span>, <span class="hljs-title">Permission</span>};
""");
    }

    [Fact]
    public void Namespace_UseFunction()
    {
        AssertHighlighter("php",
"""
use function MyApp\Helpers\format_name;
""",
"""
<span class="hljs-keyword">use</span> <span class="hljs-keyword">function</span> <span class="hljs-title">MyApp</span>\<span class="hljs-title">Helpers</span>\<span class="hljs-title">format_name</span>;
""");
    }

    [Fact]
    public void Namespace_UseConst()
    {
        AssertHighlighter("php",
"""
use const MyApp\Limits\MAX_AGE;
""",
"""
<span class="hljs-keyword">use</span> <span class="hljs-keyword">const</span> <span class="hljs-title">MyApp</span>\<span class="hljs-title">Limits</span>\<span class="hljs-title">MAX_AGE</span>;
""");
    }

    [Fact]
    public void Namespace_GlobalRoot()
    {
        AssertHighlighter("php",
"""
use \DateTime;
""",
"""
<span class="hljs-keyword">use</span> \<span class="hljs-title">DateTime</span>;
""");
    }

    [Fact]
    public void Namespace_BlockSyntax()
    {
        AssertHighlighter("php",
"""
namespace MyApp {
    class Foo { }
}

namespace OtherApp {
    class Bar { }
}
""",
"""
<span class="hljs-keyword">namespace</span> <span class="hljs-title class_">MyApp</span> {
    <span class="hljs-title class_">class</span> <span class="hljs-title class_">Foo</span> { }
}

<span class="hljs-title class_">namespace</span> <span class="hljs-title class_">OtherApp</span> {
    <span class="hljs-title class_">class</span> <span class="hljs-title class_">Bar</span> { }
}
""");
    }

    [Fact]
    public void Comment_Hash()
    {
        AssertHighlighter("php",
"""
# this is a hash comment
""",
"""
<span class="hljs-comment"># this is a hash comment</span>
""");
    }

    [Fact]
    public void Comment_Slash()
    {
        AssertHighlighter("php",
"""
// this is a line comment
""",
"""
<span class="hljs-comment">// this is a line comment</span>
""");
    }

    [Fact]
    public void Comment_Block()
    {
        AssertHighlighter("php",
"""
/* this is a block comment */
""",
"""
<span class="hljs-comment">/* this is a block comment */</span>
""");
    }

    [Fact]
    public void Comment_PhpDoc()
    {
        AssertHighlighter("php",
"""
/**
 * Adds two integers.
 *
 * @param int $a
 * @param int $b
 * @return int
 */
function add(int $a, int $b): int { return $a + $b; }
""",
"""
<span class="hljs-comment">/**
 * Adds two integers.
 *
 * <span class="hljs-doctag">@param</span> int $a
 * <span class="hljs-doctag">@param</span> int $b
 * <span class="hljs-doctag">@return</span> int
 */</span>
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">add</span>(<span class="hljs-params"><span class="hljs-keyword">int</span> <span class="hljs-variable">$a</span>, <span class="hljs-keyword">int</span> <span class="hljs-variable">$b</span></span>): <span class="hljs-title">int</span> </span>{ <span class="hljs-keyword">return</span> <span class="hljs-variable">$a</span> + <span class="hljs-variable">$b</span>; }
""");
    }

    [Fact]
    public void Composite_HelloWorld()
    {
        AssertHighlighter("php",
"""
<?php
echo "Hello, world!" . PHP_EOL;
""",
"""
<span class="hljs-meta">&lt;?php</span>
<span class="hljs-keyword">echo</span> <span class="hljs-string">&quot;Hello, world!&quot;</span> . PHP_EOL;
""");
    }

    [Fact]
    public void Composite_ApiHandler()
    {
        AssertHighlighter("php",
"""
<?php
declare(strict_types=1);

namespace MyApp\Api;

use MyApp\Domain\UserService;

final class UserController
{
    public function __construct(
        private readonly UserService $users,
    ) {}

    #[Route(path: "/users/{id}", method: "GET")]
    public function show(int $id): array
    {
        $user = $this->users->find($id)
            ?? throw new NotFoundException("user $id");

        return [
            "id"    => $user->id,
            "name"  => $user->name,
            "email" => $user->email,
        ];
    }
}
""",
"""
<span class="hljs-meta">&lt;?php</span>
<span class="hljs-keyword">declare</span>(strict_types=<span class="hljs-number">1</span>);

<span class="hljs-keyword">namespace</span> <span class="hljs-title class_">MyApp</span>\<span class="hljs-title class_">Api</span>;

<span class="hljs-keyword">use</span> <span class="hljs-title">MyApp</span>\<span class="hljs-title">Domain</span>\<span class="hljs-title">UserService</span>;

<span class="hljs-keyword">final</span> <span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">UserController</span>
</span>{
    <span class="hljs-keyword">public</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">__construct</span>(<span class="hljs-params">
        <span class="hljs-keyword">private</span> <span class="hljs-keyword">readonly</span> UserService <span class="hljs-variable">$users</span>,
    </span>) </span>{}

    <span class="hljs-meta">#[Route</span>(<span class="hljs-attr">path</span>: <span class="hljs-string">&quot;/users/{id}&quot;</span>, <span class="hljs-attr">method</span>: <span class="hljs-string">&quot;GET&quot;</span>)<span class="hljs-meta">]</span>
    <span class="hljs-keyword">public</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">show</span>(<span class="hljs-params"><span class="hljs-keyword">int</span> <span class="hljs-variable">$id</span></span>): <span class="hljs-title">array</span>
    </span>{
        <span class="hljs-variable">$user</span> = <span class="hljs-variable language_">$this</span>-&gt;users-&gt;<span class="hljs-title function_ invoke__">find</span>(<span class="hljs-variable">$id</span>)
            ?? <span class="hljs-keyword">throw</span> <span class="hljs-keyword">new</span> <span class="hljs-title class_">NotFoundException</span>(<span class="hljs-string">&quot;user <span class="hljs-subst">$id</span>&quot;</span>);

        <span class="hljs-keyword">return</span> [
            <span class="hljs-string">&quot;id&quot;</span>    =&gt; <span class="hljs-variable">$user</span>-&gt;id,
            <span class="hljs-string">&quot;name&quot;</span>  =&gt; <span class="hljs-variable">$user</span>-&gt;name,
            <span class="hljs-string">&quot;email&quot;</span> =&gt; <span class="hljs-variable">$user</span>-&gt;email,
        ];
    }
}
""");
    }

    [Fact]
    public void Composite_EnumWithBehavior()
    {
        AssertHighlighter("php",
"""
<?php
enum Priority: int
{
    case Low    = 1;
    case Normal = 2;
    case High   = 3;

    public function label(): string
    {
        return match ($this) {
            self::Low    => "low",
            self::Normal => "normal",
            self::High   => "high",
        };
    }

    public static function fromLabel(string $label): self
    {
        return match (strtolower($label)) {
            "low", "lo"      => self::Low,
            "normal", "med"  => self::Normal,
            "high", "hi"     => self::High,
        };
    }
}
""",
"""
<span class="hljs-meta">&lt;?php</span>
<span class="hljs-class"><span class="hljs-keyword">enum</span> <span class="hljs-title">Priority</span>: <span class="hljs-title">int</span>
</span>{
    <span class="hljs-keyword">case</span> Low    = <span class="hljs-number">1</span>;
    <span class="hljs-keyword">case</span> Normal = <span class="hljs-number">2</span>;
    <span class="hljs-keyword">case</span> High   = <span class="hljs-number">3</span>;

    <span class="hljs-keyword">public</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">label</span>(<span class="hljs-params"></span>): <span class="hljs-title">string</span>
    </span>{
        <span class="hljs-keyword">return</span> <span class="hljs-keyword">match</span> (<span class="hljs-variable language_">$this</span>) {
            <span class="hljs-built_in">self</span>::<span class="hljs-variable constant_">Low</span>    =&gt; <span class="hljs-string">&quot;low&quot;</span>,
            <span class="hljs-built_in">self</span>::<span class="hljs-variable constant_">Normal</span> =&gt; <span class="hljs-string">&quot;normal&quot;</span>,
            <span class="hljs-built_in">self</span>::<span class="hljs-variable constant_">High</span>   =&gt; <span class="hljs-string">&quot;high&quot;</span>,
        };
    }

    <span class="hljs-keyword">public</span> <span class="hljs-built_in">static</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">fromLabel</span>(<span class="hljs-params"><span class="hljs-keyword">string</span> <span class="hljs-variable">$label</span></span>): <span class="hljs-title">self</span>
    </span>{
        <span class="hljs-keyword">return</span> <span class="hljs-keyword">match</span> (<span class="hljs-title function_ invoke__">strtolower</span>(<span class="hljs-variable">$label</span>)) {
            <span class="hljs-string">&quot;low&quot;</span>, <span class="hljs-string">&quot;lo&quot;</span>      =&gt; <span class="hljs-built_in">self</span>::<span class="hljs-variable constant_">Low</span>,
            <span class="hljs-string">&quot;normal&quot;</span>, <span class="hljs-string">&quot;med&quot;</span>  =&gt; <span class="hljs-built_in">self</span>::<span class="hljs-variable constant_">Normal</span>,
            <span class="hljs-string">&quot;high&quot;</span>, <span class="hljs-string">&quot;hi&quot;</span>     =&gt; <span class="hljs-built_in">self</span>::<span class="hljs-variable constant_">High</span>,
        };
    }
}
""");
    }

    [Fact]
    public void Composite_TemplateMixed()
    {
        AssertHighlighter("php",
"""
<?php $title = "Users"; ?>
<!DOCTYPE html>
<html lang="en">
<head>
    <title><?= htmlspecialchars($title) ?></title>
</head>
<body>
    <h1><?= htmlspecialchars($title) ?></h1>
    <?php if (count($users) === 0): ?>
        <p>No users found.</p>
    <?php else: ?>
        <ul>
        <?php foreach ($users as $user): ?>
            <li><?= htmlspecialchars($user->name) ?> (<?= $user->age ?>)</li>
        <?php endforeach; ?>
        </ul>
    <?php endif; ?>
</body>
</html>
""",
"""
<span class="hljs-meta">&lt;?php</span> <span class="hljs-variable">$title</span> = <span class="hljs-string">&quot;Users&quot;</span>; <span class="hljs-meta">?&gt;</span>
&lt;!DOCTYPE html&gt;
&lt;html lang=<span class="hljs-string">&quot;en&quot;</span>&gt;
&lt;head&gt;
    &lt;title&gt;<span class="hljs-meta">&lt;?=</span> <span class="hljs-title function_ invoke__">htmlspecialchars</span>(<span class="hljs-variable">$title</span>) <span class="hljs-meta">?&gt;</span>&lt;/title&gt;
&lt;/head&gt;
&lt;body&gt;
    &lt;h1&gt;<span class="hljs-meta">&lt;?=</span> <span class="hljs-title function_ invoke__">htmlspecialchars</span>(<span class="hljs-variable">$title</span>) <span class="hljs-meta">?&gt;</span>&lt;/h1&gt;
    <span class="hljs-meta">&lt;?php</span> <span class="hljs-keyword">if</span> (<span class="hljs-title function_ invoke__">count</span>(<span class="hljs-variable">$users</span>) === <span class="hljs-number">0</span>): <span class="hljs-meta">?&gt;</span>
        &lt;p&gt;No users found.&lt;/p&gt;
    <span class="hljs-meta">&lt;?php</span> <span class="hljs-keyword">else</span>: <span class="hljs-meta">?&gt;</span>
        &lt;ul&gt;
        <span class="hljs-meta">&lt;?php</span> <span class="hljs-keyword">foreach</span> (<span class="hljs-variable">$users</span> <span class="hljs-keyword">as</span> <span class="hljs-variable">$user</span>): <span class="hljs-meta">?&gt;</span>
            &lt;li&gt;<span class="hljs-meta">&lt;?=</span> <span class="hljs-title function_ invoke__">htmlspecialchars</span>(<span class="hljs-variable">$user</span>-&gt;name) <span class="hljs-meta">?&gt;</span> (<span class="hljs-meta">&lt;?=</span> <span class="hljs-variable">$user</span>-&gt;age <span class="hljs-meta">?&gt;</span>)&lt;/li&gt;
        <span class="hljs-meta">&lt;?php</span> <span class="hljs-keyword">endforeach</span>; <span class="hljs-meta">?&gt;</span>
        &lt;/ul&gt;
    <span class="hljs-meta">&lt;?php</span> <span class="hljs-keyword">endif</span>; <span class="hljs-meta">?&gt;</span>
&lt;/body&gt;
&lt;/html&gt;
""");
    }

    [Fact]
    public void SpecialEdge_Empty()
    {
        AssertHighlighter("php",
"""

""",
"""

""");
    }

    [Fact]
    public void SpecialEdge_OnlyTag()
    {
        AssertHighlighter("php",
"""
<?php
""",
"""
<span class="hljs-meta">&lt;?php</span>
""");
    }

    [Fact]
    public void SpecialEdge_OnlyComment()
    {
        AssertHighlighter("php",
"""
<?php
// nothing here
""",
"""
<span class="hljs-meta">&lt;?php</span>
<span class="hljs-comment">// nothing here</span>
""");
    }

    [Fact]
    public void SpecialEdge_OnlyNamespace()
    {
        AssertHighlighter("php",
"""
<?php
namespace MyApp;
""",
"""
<span class="hljs-meta">&lt;?php</span>
<span class="hljs-keyword">namespace</span> <span class="hljs-title class_">MyApp</span>;
""");
    }

    [Fact]
    public void SpecialEdge_TrailingNewline()
    {
        AssertHighlighter("php",
"""
<?php
echo "hi";

""",
"""
<span class="hljs-meta">&lt;?php</span>
<span class="hljs-keyword">echo</span> <span class="hljs-string">&quot;hi&quot;</span>;

""");
    }
}
