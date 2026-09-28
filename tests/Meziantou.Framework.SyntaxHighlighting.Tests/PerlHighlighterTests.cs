namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class PerlHighlighterTests
{
    [Fact]
    public void Hello()
    {
        AssertHighlighter("perl",
"""
#!/usr/bin/perl
use strict;
use warnings;

print "Hello, World!\n";
""",
"""
<span class="hljs-comment">#!/usr/bin/perl</span>
<span class="hljs-keyword">use</span> strict;
<span class="hljs-keyword">use</span> warnings;

<span class="hljs-keyword">print</span> <span class="hljs-string">&quot;Hello, World!\n&quot;</span>;
""");
    }

    [Fact]
    public void Variables()
    {
        AssertHighlighter("perl",
"""
my $name = "Alice";
my @list = (1, 2, 3);
my %hash = (a => 1, b => 2);
our $VERSION = '1.00';
local $_ = shift;
my ($x, $y) = @_;
print $list[0], $hash{a}, $#list;
""",
"""
<span class="hljs-keyword">my</span> <span class="hljs-variable">$name</span> = <span class="hljs-string">&quot;Alice&quot;</span>;
<span class="hljs-keyword">my</span> <span class="hljs-variable">@list</span> = (<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>);
<span class="hljs-keyword">my</span> <span class="hljs-variable">%hash</span> = (<span class="hljs-string">a =&gt;</span> <span class="hljs-number">1</span>, <span class="hljs-string">b =&gt;</span> <span class="hljs-number">2</span>);
<span class="hljs-keyword">our</span> <span class="hljs-variable">$VERSION</span> = <span class="hljs-string">&#x27;1.00&#x27;</span>;
<span class="hljs-keyword">local</span> <span class="hljs-variable">$_</span> = <span class="hljs-keyword">shift</span>;
<span class="hljs-keyword">my</span> (<span class="hljs-variable">$x</span>, <span class="hljs-variable">$y</span>) = <span class="hljs-variable">@_</span>;
<span class="hljs-keyword">print</span> <span class="hljs-variable">$list</span>[<span class="hljs-number">0</span>], <span class="hljs-variable">$hash</span><span class="hljs-string">{a}</span>, <span class="hljs-variable">$#list</span>;
""");
    }

    [Fact]
    public void SpecialVars()
    {
        AssertHighlighter("perl",
"""
print $0, $1, $@, $!, $/, $\, $;;
my $sep = $,;
$| = 1;
print "$ENV{HOME}\n";
@ARGV = ();
print STDERR $^O, $^W;
""",
"""
<span class="hljs-keyword">print</span> <span class="hljs-variable">$0</span>, <span class="hljs-variable">$1</span>, <span class="hljs-variable">$@</span>, <span class="hljs-variable">$!</span>, <span class="hljs-variable">$/</span>, <span class="hljs-variable">$\</span>, <span class="hljs-variable">$;</span>;
<span class="hljs-keyword">my</span> <span class="hljs-variable">$sep</span> = <span class="hljs-variable">$,</span>;
<span class="hljs-variable">$|</span> = <span class="hljs-number">1</span>;
<span class="hljs-keyword">print</span> <span class="hljs-string">&quot;<span class="hljs-variable">$ENV</span>{HOME}\n&quot;</span>;
<span class="hljs-variable">@ARGV</span> = ();
<span class="hljs-keyword">print</span> STDERR <span class="hljs-variable">$^O</span>, <span class="hljs-variable">$^W</span>;
""");
    }

    [Fact]
    public void PackageVars()
    {
        AssertHighlighter("perl",
"""
$Foo::Bar::baz = 1;
@My::Module::list = ();
%main::config = ();
my $ref = \&Some::Package::func;
""",
"""
<span class="hljs-variable">$Foo::Bar::baz</span> = <span class="hljs-number">1</span>;
<span class="hljs-variable">@My::Module::list</span> = ();
<span class="hljs-variable">%main::config</span> = ();
<span class="hljs-keyword">my</span> <span class="hljs-variable">$ref</span> = \&amp;Some::Package::func;
""");
    }

    [Fact]
    public void Subs()
    {
        AssertHighlighter("perl",
"""
sub greet {
    my ($name) = @_;
    return "Hello, $name";
}

sub add($$) { $_[0] + $_[1] }

sub with_sig ($x, $y = 2) {
    return $x * $y;
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">sub</span> <span class="hljs-title">greet</span> </span>{
    <span class="hljs-keyword">my</span> (<span class="hljs-variable">$name</span>) = <span class="hljs-variable">@_</span>;
    <span class="hljs-keyword">return</span> <span class="hljs-string">&quot;Hello, <span class="hljs-variable">$name</span>&quot;</span>;
}

<span class="hljs-function"><span class="hljs-keyword">sub</span> <span class="hljs-title">add</span>($$) </span>{ <span class="hljs-variable">$_</span>[<span class="hljs-number">0</span>] + <span class="hljs-variable">$_</span>[<span class="hljs-number">1</span>] }

<span class="hljs-function"><span class="hljs-keyword">sub</span> <span class="hljs-title">with_sig</span> (<span class="hljs-variable">$x</span>, <span class="hljs-variable">$y</span> = <span class="hljs-number">2</span>) </span>{
    <span class="hljs-keyword">return</span> <span class="hljs-variable">$x</span> * <span class="hljs-variable">$y</span>;
}
""");
    }

    [Fact]
    public void SubAttributes()
    {
        AssertHighlighter("perl",
"""
sub value :lvalue { $val }
my $x :shared = 1;
sub method_name : method { }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">sub</span> <span class="hljs-title">value</span><span class="hljs-attr"> :lvalue</span> </span>{ <span class="hljs-variable">$val</span> }
<span class="hljs-keyword">my</span> <span class="hljs-variable">$x<span class="hljs-attr"> :shared</span></span> = <span class="hljs-number">1</span>;
<span class="hljs-function"><span class="hljs-keyword">sub</span> <span class="hljs-title">method_name</span><span class="hljs-attr"> : method</span> </span>{ }
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("perl",
"""
my $a = 'single $not interpolated \' quote';
my $b = "double $interp @{[ 1 + 2 ]} \t\"";
my $c = `ls -la $dir`;
my $d = "hash $h{key} and array $a[0] and ${name}";
my $e = "method @{[ $obj->name ]}";
""",
"""
<span class="hljs-keyword">my</span> <span class="hljs-variable">$a</span> = <span class="hljs-string">&#x27;single $not interpolated \&#x27; quote&#x27;</span>;
<span class="hljs-keyword">my</span> <span class="hljs-variable">$b</span> = <span class="hljs-string">&quot;double <span class="hljs-variable">$interp</span> <span class="hljs-subst">@{[ <span class="hljs-number">1</span> + <span class="hljs-number">2</span> ]}</span> \t\&quot;&quot;</span>;
<span class="hljs-keyword">my</span> <span class="hljs-variable">$c</span> = <span class="hljs-string">`ls -la $dir`</span>;
<span class="hljs-keyword">my</span> <span class="hljs-variable">$d</span> = <span class="hljs-string">&quot;hash <span class="hljs-variable">$h</span>{key} and array <span class="hljs-variable">$a</span>[0] and <span class="hljs-subst">${name}</span>&quot;</span>;
<span class="hljs-keyword">my</span> <span class="hljs-variable">$e</span> = <span class="hljs-string">&quot;method <span class="hljs-subst">@{[ <span class="hljs-variable">$obj</span>-&gt;name ]}</span>&quot;</span>;
""");
    }

    [Fact]
    public void QuoteOps()
    {
        AssertHighlighter("perl",
"""
my @w = qw(foo bar baz);
my $q = q{single 'quoted'};
my $qq = qq[double "$quoted"];
my $cmd = qx|ls|;
my $re = qr<\d+>;
my @x = qw/a b c/;
my @y = qw
  (a b);
""",
"""
<span class="hljs-keyword">my</span> <span class="hljs-variable">@w</span> = <span class="hljs-string">qw(foo bar baz)</span>;
<span class="hljs-keyword">my</span> <span class="hljs-variable">$q</span> = <span class="hljs-string">q{single &#x27;quoted&#x27;}</span>;
<span class="hljs-keyword">my</span> <span class="hljs-variable">$qq</span> = <span class="hljs-string">qq[double &quot;<span class="hljs-variable">$quoted</span>&quot;]</span>;
<span class="hljs-keyword">my</span> <span class="hljs-variable">$cmd</span> = <span class="hljs-string">qx|ls|</span>;
<span class="hljs-keyword">my</span> <span class="hljs-variable">$re</span> = <span class="hljs-string">qr&lt;\d+&gt;</span>;
<span class="hljs-keyword">my</span> <span class="hljs-variable">@x</span> = <span class="hljs-string">qw/a b c/</span>;
<span class="hljs-keyword">my</span> <span class="hljs-variable">@y</span> = <span class="hljs-string">qw
  (a b)</span>;
""");
    }

    [Fact]
    public void RegexMatch()
    {
        AssertHighlighter("perl",
"""
if ($line =~ /^\d+$/) {
    print "number\n";
}
if ($str =~ m{^/path/(\w+)}x) { }
if ($s !~ m!foo!i) { }
my @parts = split /,\s*/, $csv;
my @found = grep { /^a/ } @words;
return /x/ ? 1 : 0;
""",
"""
<span class="hljs-keyword">if</span> (<span class="hljs-variable">$line</span> =~ <span class="hljs-regexp">/^\d+$/</span>) {
    <span class="hljs-keyword">print</span> <span class="hljs-string">&quot;number\n&quot;</span>;
}
<span class="hljs-keyword">if</span> (<span class="hljs-variable">$str</span> =~ <span class="hljs-regexp">m{^/path/(\w+)}x</span>) { }
<span class="hljs-keyword">if</span> (<span class="hljs-variable">$s</span> !~ <span class="hljs-regexp">m!foo!i</span>) { }
<span class="hljs-keyword">my</span> <span class="hljs-variable">@parts</span> = <span class="hljs-keyword">split</span> <span class="hljs-regexp">/,\s*/</span>, <span class="hljs-variable">$csv</span>;
<span class="hljs-keyword">my</span> <span class="hljs-variable">@found</span> = <span class="hljs-keyword">grep</span> { <span class="hljs-regexp">/^a/</span> } <span class="hljs-variable">@words</span>;
<span class="hljs-keyword">return</span> <span class="hljs-regexp">/x/</span> ? <span class="hljs-number">1</span> : <span class="hljs-number">0</span>;
""");
    }

    [Fact]
    public void RegexSubst()
    {
        AssertHighlighter("perl",
"""
$str =~ s/foo/bar/g;
$str =~ s{old}{new}gi;
$str =~ s(a)(b);
$str =~ s[x][y]e;
$path =~ tr/a-z/A-Z/;
$path =~ y/abc/xyz/;
$s =~ s!/!\\!g;
(my $t = $s) =~ s/^\s+|\s+$//g;
""",
"""
<span class="hljs-variable">$str</span> =~ <span class="hljs-regexp">s/foo/bar/g</span>;
<span class="hljs-variable">$str</span> =~ <span class="hljs-regexp">s{old}{new}gi</span>;
<span class="hljs-variable">$str</span> =~ <span class="hljs-regexp">s(a)(b)</span>;
<span class="hljs-variable">$str</span> =~ <span class="hljs-regexp">s[x][y]e</span>;
<span class="hljs-variable">$path</span> =~ <span class="hljs-regexp">tr/a-z/A-Z/</span>;
<span class="hljs-variable">$path</span> =~ <span class="hljs-regexp">y/abc/xyz/</span>;
<span class="hljs-variable">$s</span> =~ <span class="hljs-regexp">s!/!\\!g</span>;
(<span class="hljs-keyword">my</span> <span class="hljs-variable">$t</span> = <span class="hljs-variable">$s</span>) =~ <span class="hljs-regexp">s/^\s+|\s+$//g</span>;
""");
    }

    [Fact]
    public void RegexQr()
    {
        AssertHighlighter("perl",
"""
my $re = qr/(\d+)-(\d+)/;
my $re2 = qr{\bword\b}i;
if ($x =~ $re) { print "$1 $2\n" }
""",
"""
<span class="hljs-keyword">my</span> <span class="hljs-variable">$re</span> = <span class="hljs-regexp">qr/(\d+)-(\d+)/</span>;
<span class="hljs-keyword">my</span> <span class="hljs-variable">$re2</span> = <span class="hljs-regexp">qr{\bword\b}i</span>;
<span class="hljs-keyword">if</span> (<span class="hljs-variable">$x</span> =~ <span class="hljs-variable">$re</span>) { <span class="hljs-keyword">print</span> <span class="hljs-string">&quot;<span class="hljs-variable">$1</span> <span class="hljs-variable">$2</span>\n&quot;</span> }
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("perl",
"""
my @n = (42, 3.14, .5, 1_000_000, 0x1F, 0b1010, 0755, 1e10, 1.5e-3, v5.38.0);
""",
"""
<span class="hljs-keyword">my</span> <span class="hljs-variable">@n</span> = (<span class="hljs-number">42</span>, <span class="hljs-number">3.14</span>, <span class="hljs-number">.5</span>, <span class="hljs-number">1_000_000</span>, <span class="hljs-number">0x1F</span>, <span class="hljs-number">0b1010</span>, <span class="hljs-number">0755</span>, <span class="hljs-number">1e10</span>, <span class="hljs-number">1.5e-3</span>, <span class="hljs-number">v5.38</span>.<span class="hljs-number">0</span>);
""");
    }

    [Fact]
    public void ControlFlow()
    {
        AssertHighlighter("perl",
"""
if ($x > 0) {
    print "positive";
} elsif ($x == 0) {
    print "zero";
} else {
    print "negative";
}
unless ($ok) { die "failed" }
while (my $line = <STDIN>) {
    chomp $line;
    next if $line =~ /^#/;
    last if $line eq 'END';
}
for my $i (0 .. $#array) { }
foreach my $item (@list) { }
until ($done) { redo }
print "yes" if $cond;
""",
"""
<span class="hljs-keyword">if</span> (<span class="hljs-variable">$x</span> &gt; <span class="hljs-number">0</span>) {
    <span class="hljs-keyword">print</span> <span class="hljs-string">&quot;positive&quot;</span>;
} <span class="hljs-keyword">elsif</span> (<span class="hljs-variable">$x</span> == <span class="hljs-number">0</span>) {
    <span class="hljs-keyword">print</span> <span class="hljs-string">&quot;zero&quot;</span>;
} <span class="hljs-keyword">else</span> {
    <span class="hljs-keyword">print</span> <span class="hljs-string">&quot;negative&quot;</span>;
}
<span class="hljs-keyword">unless</span> (<span class="hljs-variable">$ok</span>) { <span class="hljs-keyword">die</span> <span class="hljs-string">&quot;failed&quot;</span> }
<span class="hljs-keyword">while</span> (<span class="hljs-keyword">my</span> <span class="hljs-variable">$line</span> = &lt;STDIN&gt;) {
    <span class="hljs-keyword">chomp</span> <span class="hljs-variable">$line</span>;
    <span class="hljs-keyword">next</span> <span class="hljs-keyword">if</span> <span class="hljs-variable">$line</span> =~ <span class="hljs-regexp">/^#/</span>;
    <span class="hljs-keyword">last</span> <span class="hljs-keyword">if</span> <span class="hljs-variable">$line</span> <span class="hljs-keyword">eq</span> <span class="hljs-string">&#x27;END&#x27;</span>;
}
<span class="hljs-keyword">for</span> <span class="hljs-keyword">my</span> <span class="hljs-variable">$i</span> (<span class="hljs-number">0</span> .. <span class="hljs-variable">$#array</span>) { }
<span class="hljs-keyword">foreach</span> <span class="hljs-keyword">my</span> <span class="hljs-variable">$item</span> (<span class="hljs-variable">@list</span>) { }
<span class="hljs-keyword">until</span> (<span class="hljs-variable">$done</span>) { <span class="hljs-keyword">redo</span> }
<span class="hljs-keyword">print</span> <span class="hljs-string">&quot;yes&quot;</span> <span class="hljs-keyword">if</span> <span class="hljs-variable">$cond</span>;
""");
    }

    [Fact]
    public void FileOps()
    {
        AssertHighlighter("perl",
"""
open(my $fh, '<', $file) or die "Cannot open $file: $!";
while (my $row = <$fh>) {
    print $row;
}
close($fh);
if (-e $file && -d $dir) { }
opendir(my $dh, $dir) || die;
""",
"""
<span class="hljs-keyword">open</span>(<span class="hljs-keyword">my</span> <span class="hljs-variable">$fh</span>, <span class="hljs-string">&#x27;&lt;&#x27;</span>, <span class="hljs-variable">$file</span>) <span class="hljs-keyword">or</span> <span class="hljs-keyword">die</span> <span class="hljs-string">&quot;Cannot open <span class="hljs-variable">$file</span>: <span class="hljs-variable">$!</span>&quot;</span>;
<span class="hljs-keyword">while</span> (<span class="hljs-keyword">my</span> <span class="hljs-variable">$row</span> = &lt;<span class="hljs-variable">$fh</span>&gt;) {
    <span class="hljs-keyword">print</span> <span class="hljs-variable">$row</span>;
}
<span class="hljs-keyword">close</span>(<span class="hljs-variable">$fh</span>);
<span class="hljs-keyword">if</span> (-e <span class="hljs-variable">$file</span> &amp;&amp; -d <span class="hljs-variable">$dir</span>) { }
<span class="hljs-keyword">opendir</span>(<span class="hljs-keyword">my</span> <span class="hljs-variable">$dh</span>, <span class="hljs-variable">$dir</span>) || <span class="hljs-keyword">die</span>;
""");
    }

    [Fact]
    public void Hashes()
    {
        AssertHighlighter("perl",
"""
my %h = (
    name  => 'Bob',
    age   => 42,
    -flag => 1,
);
foreach my $key (sort keys %h) {
    printf "%s: %s\n", $key, $h{$key};
}
my $v = $h{name};
my @vals = @h{qw(name age)};
delete $h{age};
print exists $h{name} ? "yes" : "no";
""",
"""
<span class="hljs-keyword">my</span> <span class="hljs-variable">%h</span> = (
    <span class="hljs-string">name  =&gt;</span> <span class="hljs-string">&#x27;Bob&#x27;</span>,
    <span class="hljs-string">age   =&gt;</span> <span class="hljs-number">42</span>,
    <span class="hljs-string">-flag =&gt;</span> <span class="hljs-number">1</span>,
);
<span class="hljs-keyword">foreach</span> <span class="hljs-keyword">my</span> <span class="hljs-variable">$key</span> (<span class="hljs-keyword">sort</span> <span class="hljs-keyword">keys</span> <span class="hljs-variable">%h</span>) {
    <span class="hljs-keyword">printf</span> <span class="hljs-string">&quot;%s: %s\n&quot;</span>, <span class="hljs-variable">$key</span>, <span class="hljs-variable">$h</span>{<span class="hljs-variable">$key</span>};
}
<span class="hljs-keyword">my</span> <span class="hljs-variable">$v</span> = <span class="hljs-variable">$h</span><span class="hljs-string">{name}</span>;
<span class="hljs-keyword">my</span> <span class="hljs-variable">@vals</span> = <span class="hljs-variable">@h</span>{<span class="hljs-string">qw(name age)</span>};
<span class="hljs-keyword">delete</span> <span class="hljs-variable">$h</span><span class="hljs-string">{age}</span>;
<span class="hljs-keyword">print</span> <span class="hljs-keyword">exists</span> <span class="hljs-variable">$h</span><span class="hljs-string">{name}</span> ? <span class="hljs-string">&quot;yes&quot;</span> : <span class="hljs-string">&quot;no&quot;</span>;
""");
    }

    [Fact]
    public void References()
    {
        AssertHighlighter("perl",
"""
my $aref = [1, 2, 3];
my $href = { a => 1, b => [2, 3] };
my $cref = sub { return shift * 2 };
print $aref->[0], $href->{a}, $$aref[1], @{$aref}, %{$href};
print $href->{b}->[1];
$cref->(5);
my @copy = @$aref;
""",
"""
<span class="hljs-keyword">my</span> <span class="hljs-variable">$aref</span> = [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>];
<span class="hljs-keyword">my</span> <span class="hljs-variable">$href</span> = { <span class="hljs-string">a =&gt;</span> <span class="hljs-number">1</span>, <span class="hljs-string">b =&gt;</span> [<span class="hljs-number">2</span>, <span class="hljs-number">3</span>] };
<span class="hljs-keyword">my</span> <span class="hljs-variable">$cref</span> = <span class="hljs-function"><span class="hljs-keyword">sub</span> </span>{ <span class="hljs-keyword">return</span> <span class="hljs-keyword">shift</span> * <span class="hljs-number">2</span> };
<span class="hljs-keyword">print</span> <span class="hljs-variable">$aref</span>-&gt;[<span class="hljs-number">0</span>], <span class="hljs-variable">$href</span>-&gt;{a}, <span class="hljs-variable">$$aref</span>[<span class="hljs-number">1</span>], @{<span class="hljs-variable">$aref</span>}, %{<span class="hljs-variable">$href</span>};
<span class="hljs-keyword">print</span> <span class="hljs-variable">$href</span>-&gt;{b}-&gt;[<span class="hljs-number">1</span>];
<span class="hljs-variable">$cref</span>-&gt;(<span class="hljs-number">5</span>);
<span class="hljs-keyword">my</span> <span class="hljs-variable">@copy</span> = <span class="hljs-variable">@$aref</span>;
""");
    }

    [Fact]
    public void Oop()
    {
        AssertHighlighter("perl",
"""
package Animal;
use parent -norequire, 'Base';

sub new {
    my ($class, %args) = @_;
    my $self = bless { name => $args{name} }, $class;
    return $self;
}

sub speak {
    my $self = shift;
    printf "%s says %s\n", $self->{name}, $self->sound;
}

1;
""",
"""
<span class="hljs-keyword">package</span> Animal;
<span class="hljs-keyword">use</span> parent -norequire, <span class="hljs-string">&#x27;Base&#x27;</span>;

<span class="hljs-function"><span class="hljs-keyword">sub</span> <span class="hljs-title">new</span> </span>{
    <span class="hljs-keyword">my</span> (<span class="hljs-variable">$class</span>, <span class="hljs-variable">%args</span>) = <span class="hljs-variable">@_</span>;
    <span class="hljs-keyword">my</span> <span class="hljs-variable">$self</span> = <span class="hljs-keyword">bless</span> { <span class="hljs-string">name =&gt;</span> <span class="hljs-variable">$args</span><span class="hljs-string">{name}</span> }, <span class="hljs-variable">$class</span>;
    <span class="hljs-keyword">return</span> <span class="hljs-variable">$self</span>;
}

<span class="hljs-function"><span class="hljs-keyword">sub</span> <span class="hljs-title">speak</span> </span>{
    <span class="hljs-keyword">my</span> <span class="hljs-variable">$self</span> = <span class="hljs-keyword">shift</span>;
    <span class="hljs-keyword">printf</span> <span class="hljs-string">&quot;%s says %s\n&quot;</span>, <span class="hljs-variable">$self</span>-&gt;{name}, <span class="hljs-variable">$self</span>-&gt;sound;
}

<span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void ModernClass()
    {
        AssertHighlighter("perl",
"""
use v5.38;
use experimental 'class';

class Point 1.0 {
    field $x :param = 0;
    field $y :param = 0;

    method coords { return ($x, $y) }
}
""",
"""
<span class="hljs-keyword">use</span> <span class="hljs-number">v5.38</span>;
<span class="hljs-keyword">use</span> experimental <span class="hljs-string">&#x27;class&#x27;</span>;

<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Point</span> <span class="hljs-number">1.0</span> </span>{
    <span class="hljs-keyword">field</span> <span class="hljs-variable">$x<span class="hljs-attr"> :param</span></span> = <span class="hljs-number">0</span>;
    <span class="hljs-keyword">field</span> <span class="hljs-variable">$y<span class="hljs-attr"> :param</span></span> = <span class="hljs-number">0</span>;

    <span class="hljs-function"><span class="hljs-keyword">method</span> <span class="hljs-title">coords</span> </span>{ <span class="hljs-keyword">return</span> (<span class="hljs-variable">$x</span>, <span class="hljs-variable">$y</span>) }
}
""");
    }

    [Fact]
    public void Pod()
    {
        AssertHighlighter("perl",
"""
=pod

=head1 NAME

My::Module - does things

=head2 SYNOPSIS

  use My::Module;

=cut

sub foo { 1 }
""",
"""
<span class="hljs-comment">=pod

=head1 NAME

My::Module - does things

=head2 SYNOPSIS

  use My::Module;

=cut</span>

<span class="hljs-function"><span class="hljs-keyword">sub</span> <span class="hljs-title">foo</span> </span>{ <span class="hljs-number">1</span> }
""");
    }

    [Fact]
    public void PodEnd()
    {
        AssertHighlighter("perl",
"""
1;
__END__

=head1 DESCRIPTION

Some text.
""",
"""
<span class="hljs-number">1</span>;
__END__

<span class="hljs-comment">=head1 DESCRIPTION

Some text.</span>
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("perl",
"""
# a comment
my $x = 1; # trailing
# TODO: fix me
""",
"""
<span class="hljs-comment"># a comment</span>
<span class="hljs-keyword">my</span> <span class="hljs-variable">$x</span> = <span class="hljs-number">1</span>; <span class="hljs-comment"># trailing</span>
<span class="hljs-comment"># <span class="hljs-doctag">TODO:</span> fix me</span>
""");
    }

    [Fact]
    public void HeredocDouble()
    {
        AssertHighlighter("perl",
"""
my $text = <<"EOF";
Hello $name,
  Welcome to @{[ $site ]}
EOF
print $text;
""",
"""
<span class="hljs-keyword">my</span> <span class="hljs-variable">$text</span> = <span class="hljs-string">&lt;&lt;&quot;EOF&quot;;
Hello <span class="hljs-variable">$name</span>,
  Welcome to <span class="hljs-subst">@{[ <span class="hljs-variable">$site</span> ]}</span>
EOF</span>
<span class="hljs-keyword">print</span> <span class="hljs-variable">$text</span>;
""");
    }

    [Fact]
    public void HeredocSingle()
    {
        AssertHighlighter("perl",
"""
print <<'END';
No $interpolation here
END
print "done\n";
""",
"""
<span class="hljs-keyword">print</span> <span class="hljs-string">&lt;&lt;&#x27;END&#x27;;
No $interpolation here
END</span>
<span class="hljs-keyword">print</span> <span class="hljs-string">&quot;done\n&quot;</span>;
""");
    }

    [Fact]
    public void HeredocBare()
    {
        AssertHighlighter("perl",
"""
print <<EOT;
Dear $name
EOT
""",
"""
<span class="hljs-keyword">print</span> <span class="hljs-string">&lt;&lt;EOT;
Dear <span class="hljs-variable">$name</span>
EOT</span>
""");
    }

    [Fact]
    public void HeredocIndented()
    {
        AssertHighlighter("perl",
"""
if ($x) {
    print <<~EOT;
        indented $x
        EOT
}
""",
"""
<span class="hljs-keyword">if</span> (<span class="hljs-variable">$x</span>) {
    <span class="hljs-keyword">print</span> <span class="hljs-string">&lt;&lt;~EOT;
        indented <span class="hljs-variable">$x</span>
        EOT</span>
}
""");
    }

    [Fact]
    public void HeredocNot()
    {
        AssertHighlighter("perl",
"""
my $x = 1 << 2;
my $y = $a<<$b;
""",
"""
<span class="hljs-keyword">my</span> <span class="hljs-variable">$x</span> = <span class="hljs-number">1</span> &lt;&lt; <span class="hljs-number">2</span>;
<span class="hljs-keyword">my</span> <span class="hljs-variable">$y</span> = <span class="hljs-variable">$a</span>&lt;&lt;<span class="hljs-variable">$b</span>;
""");
    }

    [Fact]
    public void DataSection()
    {
        AssertHighlighter("perl",
"""
while (<DATA>) { print }
__DATA__
@@ index.html
<html>$x</html>
__END__
""",
"""
<span class="hljs-keyword">while</span> (&lt;DATA&gt;) { <span class="hljs-keyword">print</span> }
__DATA__
<span class="hljs-comment">@@ index.html</span>
&lt;html&gt;$x&lt;/html&gt;
__END__
""");
    }

    [Fact]
    public void SortMap()
    {
        AssertHighlighter("perl",
"""
my @sorted = sort { $a <=> $b } @nums;
my @names = map { $_->{name} } @people;
my %seen; my @uniq = grep { !$seen{$_}++ } @list;
my @r = reverse sort @x;
""",
"""
<span class="hljs-keyword">my</span> <span class="hljs-variable">@sorted</span> = <span class="hljs-keyword">sort</span> { <span class="hljs-variable">$a</span> &lt;=&gt; <span class="hljs-variable">$b</span> } <span class="hljs-variable">@nums</span>;
<span class="hljs-keyword">my</span> <span class="hljs-variable">@names</span> = <span class="hljs-keyword">map</span> { <span class="hljs-variable">$_</span>-&gt;{name} } <span class="hljs-variable">@people</span>;
<span class="hljs-keyword">my</span> <span class="hljs-variable">%seen</span>; <span class="hljs-keyword">my</span> <span class="hljs-variable">@uniq</span> = <span class="hljs-keyword">grep</span> { !<span class="hljs-variable">$seen</span>{<span class="hljs-variable">$_</span>}++ } <span class="hljs-variable">@list</span>;
<span class="hljs-keyword">my</span> <span class="hljs-variable">@r</span> = <span class="hljs-keyword">reverse</span> <span class="hljs-keyword">sort</span> <span class="hljs-variable">@x</span>;
""");
    }

    [Fact]
    public void StringOps()
    {
        AssertHighlighter("perl",
"""
my $s = "a" . "b" x 3;
my $len = length($s);
my $up = uc $s;
if ($a eq $b || $a ne $c || $a lt $d) { }
my $joined = join(", ", @list);
my $sub = substr($s, 0, 5);
""",
"""
<span class="hljs-keyword">my</span> <span class="hljs-variable">$s</span> = <span class="hljs-string">&quot;a&quot;</span> . <span class="hljs-string">&quot;b&quot;</span> <span class="hljs-keyword">x</span> <span class="hljs-number">3</span>;
<span class="hljs-keyword">my</span> <span class="hljs-variable">$len</span> = <span class="hljs-keyword">length</span>(<span class="hljs-variable">$s</span>);
<span class="hljs-keyword">my</span> <span class="hljs-variable">$up</span> = <span class="hljs-keyword">uc</span> <span class="hljs-variable">$s</span>;
<span class="hljs-keyword">if</span> (<span class="hljs-variable">$a</span> <span class="hljs-keyword">eq</span> <span class="hljs-variable">$b</span> || <span class="hljs-variable">$a</span> <span class="hljs-keyword">ne</span> <span class="hljs-variable">$c</span> || <span class="hljs-variable">$a</span> <span class="hljs-keyword">lt</span> <span class="hljs-variable">$d</span>) { }
<span class="hljs-keyword">my</span> <span class="hljs-variable">$joined</span> = <span class="hljs-keyword">join</span>(<span class="hljs-string">&quot;, &quot;</span>, <span class="hljs-variable">@list</span>);
<span class="hljs-keyword">my</span> <span class="hljs-variable">$sub</span> = <span class="hljs-keyword">substr</span>(<span class="hljs-variable">$s</span>, <span class="hljs-number">0</span>, <span class="hljs-number">5</span>);
""");
    }

    [Fact]
    public void UseStatements()
    {
        AssertHighlighter("perl",
"""
use strict;
use warnings;
use List::Util qw(sum max min first);
use Data::Dumper;
use POSIX ();
require Exporter;
our @EXPORT_OK = qw(foo bar);
no warnings 'redefine';
""",
"""
<span class="hljs-keyword">use</span> strict;
<span class="hljs-keyword">use</span> warnings;
<span class="hljs-keyword">use</span> List::Util <span class="hljs-string">qw(sum max min first)</span>;
<span class="hljs-keyword">use</span> Data::Dumper;
<span class="hljs-keyword">use</span> POSIX ();
<span class="hljs-keyword">require</span> Exporter;
<span class="hljs-keyword">our</span> <span class="hljs-variable">@EXPORT_OK</span> = <span class="hljs-string">qw(foo bar)</span>;
<span class="hljs-keyword">no</span> warnings <span class="hljs-string">&#x27;redefine&#x27;</span>;
""");
    }

    [Fact]
    public void EvalDie()
    {
        AssertHighlighter("perl",
"""
eval {
    risky();
    1;
} or do {
    my $err = $@ || 'unknown';
    warn "Error: $err";
};
die "fatal\n" unless defined $x;
""",
"""
<span class="hljs-keyword">eval</span> {
    risky();
    <span class="hljs-number">1</span>;
} <span class="hljs-keyword">or</span> <span class="hljs-keyword">do</span> {
    <span class="hljs-keyword">my</span> <span class="hljs-variable">$err</span> = <span class="hljs-variable">$@</span> || <span class="hljs-string">&#x27;unknown&#x27;</span>;
    <span class="hljs-keyword">warn</span> <span class="hljs-string">&quot;Error: <span class="hljs-variable">$err</span>&quot;</span>;
};
<span class="hljs-keyword">die</span> <span class="hljs-string">&quot;fatal\n&quot;</span> <span class="hljs-keyword">unless</span> <span class="hljs-keyword">defined</span> <span class="hljs-variable">$x</span>;
""");
    }

    [Fact]
    public void LocalOur()
    {
        AssertHighlighter("perl",
"""
local $/ = undef;
our %CACHE;
state $count = 0;
""",
"""
<span class="hljs-keyword">local</span> <span class="hljs-variable">$/</span> = <span class="hljs-keyword">undef</span>;
<span class="hljs-keyword">our</span> <span class="hljs-variable">%CACHE</span>;
<span class="hljs-keyword">state</span> <span class="hljs-variable">$count</span> = <span class="hljs-number">0</span>;
""");
    }

    [Fact]
    public void Wantarray()
    {
        AssertHighlighter("perl",
"""
sub ctx { return wantarray ? "list" : "scalar" }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">sub</span> <span class="hljs-title">ctx</span> </span>{ <span class="hljs-keyword">return</span> <span class="hljs-keyword">wantarray</span> ? <span class="hljs-string">&quot;list&quot;</span> : <span class="hljs-string">&quot;scalar&quot;</span> }
""");
    }

    [Fact]
    public void TernaryChain()
    {
        AssertHighlighter("perl",
"""
my $size = $n < 10 ? 'small' : $n < 100 ? 'medium' : 'large';
""",
"""
<span class="hljs-keyword">my</span> <span class="hljs-variable">$size</span> = <span class="hljs-variable">$n</span> &lt; <span class="hljs-number">10</span> ? <span class="hljs-string">&#x27;small&#x27;</span> : <span class="hljs-variable">$n</span> &lt; <span class="hljs-number">100</span> ? <span class="hljs-string">&#x27;medium&#x27;</span> : <span class="hljs-string">&#x27;large&#x27;</span>;
""");
    }

    [Fact]
    public void PrintfFormat()
    {
        AssertHighlighter("perl",
"""
printf("%-10s %5.2f\n", $name, $value);
sprintf('%03d', $n);
""",
"""
<span class="hljs-keyword">printf</span>(<span class="hljs-string">&quot;%-10s %5.2f\n&quot;</span>, <span class="hljs-variable">$name</span>, <span class="hljs-variable">$value</span>);
<span class="hljs-keyword">sprintf</span>(<span class="hljs-string">&#x27;%03d&#x27;</span>, <span class="hljs-variable">$n</span>);
""");
    }

    [Fact]
    public void Filehandles()
    {
        AssertHighlighter("perl",
"""
print STDOUT "out\n";
print {$fh} "data\n";
open(FH, ">", "out.txt");
print FH "x";
""",
"""
<span class="hljs-keyword">print</span> STDOUT <span class="hljs-string">&quot;out\n&quot;</span>;
<span class="hljs-keyword">print</span> {<span class="hljs-variable">$fh</span>} <span class="hljs-string">&quot;data\n&quot;</span>;
<span class="hljs-keyword">open</span>(FH, <span class="hljs-string">&quot;&gt;&quot;</span>, <span class="hljs-string">&quot;out.txt&quot;</span>);
<span class="hljs-keyword">print</span> FH <span class="hljs-string">&quot;x&quot;</span>;
""");
    }

    [Fact]
    public void AnonNested()
    {
        AssertHighlighter("perl",
"""
my $data = {
    users => [
        { name => 'a', roles => [qw(admin user)] },
    ],
};
print $data->{users}[0]{name};
""",
"""
<span class="hljs-keyword">my</span> <span class="hljs-variable">$data</span> = {
    <span class="hljs-string">users =&gt;</span> [
        { <span class="hljs-string">name =&gt;</span> <span class="hljs-string">&#x27;a&#x27;</span>, <span class="hljs-string">roles =&gt;</span> [<span class="hljs-string">qw(admin user)</span>] },
    ],
};
<span class="hljs-keyword">print</span> <span class="hljs-variable">$data</span>-&gt;{users}[<span class="hljs-number">0</span>]<span class="hljs-string">{name}</span>;
""");
    }

    [Fact]
    public void MethodCalls()
    {
        AssertHighlighter("perl",
"""
my $obj = My::Class->new(verbose => 1);
$obj->run();
My::Class->can('run');
$obj->$method_name(@args);
""",
"""
<span class="hljs-keyword">my</span> <span class="hljs-variable">$obj</span> = My::Class-&gt;new(<span class="hljs-string">verbose =&gt;</span> <span class="hljs-number">1</span>);
<span class="hljs-variable">$obj</span>-&gt;run();
My::Class-&gt;can(<span class="hljs-string">&#x27;run&#x27;</span>);
<span class="hljs-variable">$obj</span>-&gt;<span class="hljs-variable">$method_name</span>(<span class="hljs-variable">@args</span>);
""");
    }

    [Fact]
    public void GlobAndMisc()
    {
        AssertHighlighter("perl",
"""
my @files = glob("*.txt");
my @lines = <$fh>;
my $count = () = $str =~ /x/g;
""",
"""
<span class="hljs-keyword">my</span> <span class="hljs-variable">@files</span> = <span class="hljs-keyword">glob</span>(<span class="hljs-string">&quot;*.txt&quot;</span>);
<span class="hljs-keyword">my</span> <span class="hljs-variable">@lines</span> = &lt;<span class="hljs-variable">$fh</span>&gt;;
<span class="hljs-keyword">my</span> <span class="hljs-variable">$count</span> = () = <span class="hljs-variable">$str</span> =~ <span class="hljs-regexp">/x/g</span>;
""");
    }

    [Fact]
    public void Division()
    {
        AssertHighlighter("perl",
"""
my $avg = $total / $count;
my $half = $x / 2;
""",
"""
<span class="hljs-keyword">my</span> <span class="hljs-variable">$avg</span> = <span class="hljs-variable">$total</span> / <span class="hljs-variable">$count</span>;
<span class="hljs-keyword">my</span> <span class="hljs-variable">$half</span> = <span class="hljs-variable">$x</span> / <span class="hljs-number">2</span>;
""");
    }

    [Fact]
    public void ChainedStringOps()
    {
        AssertHighlighter("perl",
"""
$str =~ s/(\w+)/\u$1/g;
""",
"""
<span class="hljs-variable">$str</span> =~ <span class="hljs-regexp">s/(\w+)/\u$1/g</span>;
""");
    }

    [Fact]
    public void Smartmatch()
    {
        AssertHighlighter("perl",
"""
given ($x) {
    when (1) { say "one" }
    default { say "other" }
}
""",
"""
<span class="hljs-keyword">given</span> (<span class="hljs-variable">$x</span>) {
    <span class="hljs-keyword">when</span> (<span class="hljs-number">1</span>) { <span class="hljs-keyword">say</span> <span class="hljs-string">&quot;one&quot;</span> }
    default { <span class="hljs-keyword">say</span> <span class="hljs-string">&quot;other&quot;</span> }
}
""");
    }

    [Fact]
    public void Labels()
    {
        AssertHighlighter("perl",
"""
OUTER: for my $i (1..10) {
    INNER: for my $j (1..10) {
        next OUTER if $j > $i;
    }
}
""",
"""
OUTER: <span class="hljs-keyword">for</span> <span class="hljs-keyword">my</span> <span class="hljs-variable">$i</span> (<span class="hljs-number">1</span>..<span class="hljs-number">10</span>) {
    INNER: <span class="hljs-keyword">for</span> <span class="hljs-keyword">my</span> <span class="hljs-variable">$j</span> (<span class="hljs-number">1</span>..<span class="hljs-number">10</span>) {
        <span class="hljs-keyword">next</span> OUTER <span class="hljs-keyword">if</span> <span class="hljs-variable">$j</span> &gt; <span class="hljs-variable">$i</span>;
    }
}
""");
    }

    [Fact]
    public void HashSlices()
    {
        AssertHighlighter("perl",
"""
my %sub; @sub{qw(a b)} = @h{qw(a b)};
my ($first, @rest) = @list;
""",
"""
<span class="hljs-keyword">my</span> <span class="hljs-variable">%sub</span>; <span class="hljs-variable">@sub</span>{<span class="hljs-string">qw(a b)</span>} = <span class="hljs-variable">@h</span>{<span class="hljs-string">qw(a b)</span>};
<span class="hljs-keyword">my</span> (<span class="hljs-variable">$first</span>, <span class="hljs-variable">@rest</span>) = <span class="hljs-variable">@list</span>;
""");
    }

    [Fact]
    public void SpecialLiterals()
    {
        AssertHighlighter("perl",
"""
print __FILE__, __LINE__, __PACKAGE__;
""",
"""
<span class="hljs-keyword">print</span> __FILE__, __LINE__, __PACKAGE__;
""");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("perl",
"""
my $s = "héllo wörld";
""",
"""
<span class="hljs-keyword">my</span> <span class="hljs-variable">$s</span> = <span class="hljs-string">&quot;héllo wörld&quot;</span>;
""");
    }

    [Fact]
    public void RegexOtherDelimiters()
    {
        AssertHighlighter("perl",
"""
$path =~ s{/+$}{};
$url =~ m!^https?://!;
$x =~ s|/|\\|g;
$s =~ s{(\w+)}{uc $1}ge;
$s =~ tr/a-zA-Z//cd;
""",
"""
<span class="hljs-variable">$path</span> =~ <span class="hljs-regexp">s{/+$}{}</span>;
<span class="hljs-variable">$url</span> =~ <span class="hljs-regexp">m!^https?://!</span>;
<span class="hljs-variable">$x</span> =~ <span class="hljs-regexp">s|/|\\|g</span>;
<span class="hljs-variable">$s</span> =~ <span class="hljs-regexp">s{(\w+)}{uc $1}ge</span>;
<span class="hljs-variable">$s</span> =~ <span class="hljs-regexp">tr/a-zA-Z//cd</span>;
""");
    }

    [Fact]
    public void Dereference()
    {
        AssertHighlighter("perl",
"""
my @a = @$aref;
my %h = %$href;
my $v = $$aref[0];
my $pid = $$;
print "pid $$\n";
""",
"""
<span class="hljs-keyword">my</span> <span class="hljs-variable">@a</span> = <span class="hljs-variable">@$aref</span>;
<span class="hljs-keyword">my</span> <span class="hljs-variable">%h</span> = <span class="hljs-variable">%$href</span>;
<span class="hljs-keyword">my</span> <span class="hljs-variable">$v</span> = <span class="hljs-variable">$$aref</span>[<span class="hljs-number">0</span>];
<span class="hljs-keyword">my</span> <span class="hljs-variable">$pid</span> = <span class="hljs-variable">$$</span>;
<span class="hljs-keyword">print</span> <span class="hljs-string">&quot;pid <span class="hljs-variable">$$</span>\n&quot;</span>;
""");
    }

    [Fact]
    public void PrintfPercent()
    {
        AssertHighlighter("perl",
"""
printf "%d items, %s\n", $n, $name;
my $s = sprintf("%-5s|%5.1f%%", $a, $b);
my %h = (x => 1);
""",
"""
<span class="hljs-keyword">printf</span> <span class="hljs-string">&quot;%d items, %s\n&quot;</span>, <span class="hljs-variable">$n</span>, <span class="hljs-variable">$name</span>;
<span class="hljs-keyword">my</span> <span class="hljs-variable">$s</span> = <span class="hljs-keyword">sprintf</span>(<span class="hljs-string">&quot;%-5s|%5.1f%%&quot;</span>, <span class="hljs-variable">$a</span>, <span class="hljs-variable">$b</span>);
<span class="hljs-keyword">my</span> <span class="hljs-variable">%h</span> = (<span class="hljs-string">x =&gt;</span> <span class="hljs-number">1</span>);
""");
    }

    [Fact]
    public void QwSlash()
    {
        AssertHighlighter("perl",
"""
my @list = qw/alpha beta/;
my $ratio = $freq/2;
my $re = qr/abc/;
""",
"""
<span class="hljs-keyword">my</span> <span class="hljs-variable">@list</span> = <span class="hljs-string">qw/alpha beta/</span>;
<span class="hljs-keyword">my</span> <span class="hljs-variable">$ratio</span> = <span class="hljs-variable">$freq</span>/<span class="hljs-number">2</span>;
<span class="hljs-keyword">my</span> <span class="hljs-variable">$re</span> = <span class="hljs-regexp">qr/abc/</span>;
""");
    }

    [Fact]
    public void Comparisons()
    {
        AssertHighlighter("perl",
"""
if ($a eq 'x' and $b le 'y' or $c ge 'z') { }
my @s = sort { $a cmp $b } @list;
""",
"""
<span class="hljs-keyword">if</span> (<span class="hljs-variable">$a</span> <span class="hljs-keyword">eq</span> <span class="hljs-string">&#x27;x&#x27;</span> <span class="hljs-keyword">and</span> <span class="hljs-variable">$b</span> <span class="hljs-keyword">le</span> <span class="hljs-string">&#x27;y&#x27;</span> <span class="hljs-keyword">or</span> <span class="hljs-variable">$c</span> <span class="hljs-keyword">ge</span> <span class="hljs-string">&#x27;z&#x27;</span>) { }
<span class="hljs-keyword">my</span> <span class="hljs-variable">@s</span> = <span class="hljs-keyword">sort</span> { <span class="hljs-variable">$a</span> <span class="hljs-keyword">cmp</span> <span class="hljs-variable">$b</span> } <span class="hljs-variable">@list</span>;
""");
    }

    [Fact]
    public void NumberForms()
    {
        AssertHighlighter("perl",
"""
my @n = (.5, 0.5, 1e10, 1.5e-3, 2E+5, 10..20, 1_000.5);
""",
"""
<span class="hljs-keyword">my</span> <span class="hljs-variable">@n</span> = (<span class="hljs-number">.5</span>, <span class="hljs-number">0.5</span>, <span class="hljs-number">1e10</span>, <span class="hljs-number">1.5e-3</span>, <span class="hljs-number">2E+5</span>, <span class="hljs-number">10</span>..<span class="hljs-number">20</span>, <span class="hljs-number">1_000.5</span>);
""");
    }

    [Fact]
    public void SignatureSpace()
    {
        AssertHighlighter("perl",
"""
sub add ($x, $y) {
    return $x + $y;
}
sub noop { }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">sub</span> <span class="hljs-title">add</span> (<span class="hljs-variable">$x</span>, <span class="hljs-variable">$y</span>) </span>{
    <span class="hljs-keyword">return</span> <span class="hljs-variable">$x</span> + <span class="hljs-variable">$y</span>;
}
<span class="hljs-function"><span class="hljs-keyword">sub</span> <span class="hljs-title">noop</span> </span>{ }
""");
    }
}
