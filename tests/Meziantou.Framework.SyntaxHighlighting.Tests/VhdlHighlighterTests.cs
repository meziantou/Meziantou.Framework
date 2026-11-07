namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public sealed class VhdlHighlighterTests
{
    [Fact]
    public void Entity()
    {
        AssertHighlighter("vhdl",
"""
library IEEE;
use IEEE.STD_LOGIC_1164.ALL;
use IEEE.NUMERIC_STD.ALL;

entity counter is
    generic (WIDTH : integer := 8);
    port (
        clk   : in  std_logic;
        rst   : in  std_logic;
        count : out std_logic_vector(WIDTH-1 downto 0)
    );
end entity counter;
""",
"""
<span class="hljs-keyword">library</span> IEEE;
<span class="hljs-keyword">use</span> IEEE.STD_LOGIC_1164.<span class="hljs-keyword">ALL</span>;
<span class="hljs-keyword">use</span> IEEE.NUMERIC_STD.<span class="hljs-keyword">ALL</span>;

<span class="hljs-keyword">entity</span> counter <span class="hljs-keyword">is</span>
    <span class="hljs-keyword">generic</span> (<span class="hljs-literal">WIDTH</span> : <span class="hljs-built_in">integer</span> := <span class="hljs-number">8</span>);
    <span class="hljs-keyword">port</span> (
        clk   : <span class="hljs-keyword">in</span>  <span class="hljs-built_in">std_logic</span>;
        rst   : <span class="hljs-keyword">in</span>  <span class="hljs-built_in">std_logic</span>;
        count : <span class="hljs-keyword">out</span> <span class="hljs-built_in">std_logic_vector</span>(<span class="hljs-literal">WIDTH</span>-<span class="hljs-number">1</span> <span class="hljs-keyword">downto</span> <span class="hljs-number">0</span>)
    );
<span class="hljs-keyword">end</span> <span class="hljs-keyword">entity</span> counter;
""");
    }

    [Fact]
    public void Architecture()
    {
        AssertHighlighter("vhdl",
"""
architecture rtl of counter is
    signal cnt : unsigned(WIDTH-1 downto 0) := (others => '0');
begin
    process(clk)
    begin
        if rising_edge(clk) then
            if rst = '1' then
                cnt <= (others => '0');
            else
                cnt <= cnt + 1;
            end if;
        end if;
    end process;
    count <= std_logic_vector(cnt);
end architecture rtl;
""",
"""
<span class="hljs-keyword">architecture</span> rtl <span class="hljs-keyword">of</span> counter <span class="hljs-keyword">is</span>
    <span class="hljs-keyword">signal</span> cnt : <span class="hljs-built_in">unsigned</span>(<span class="hljs-literal">WIDTH</span>-<span class="hljs-number">1</span> <span class="hljs-keyword">downto</span> <span class="hljs-number">0</span>) := (<span class="hljs-keyword">others</span> =&gt; <span class="hljs-string">&#x27;0&#x27;</span>);
<span class="hljs-keyword">begin</span>
    <span class="hljs-keyword">process</span>(clk)
    <span class="hljs-keyword">begin</span>
        <span class="hljs-keyword">if</span> rising_edge(clk) <span class="hljs-keyword">then</span>
            <span class="hljs-keyword">if</span> rst = <span class="hljs-string">&#x27;1&#x27;</span> <span class="hljs-keyword">then</span>
                cnt &lt;= (<span class="hljs-keyword">others</span> =&gt; <span class="hljs-string">&#x27;0&#x27;</span>);
            <span class="hljs-keyword">else</span>
                cnt &lt;= cnt + <span class="hljs-number">1</span>;
            <span class="hljs-keyword">end</span> <span class="hljs-keyword">if</span>;
        <span class="hljs-keyword">end</span> <span class="hljs-keyword">if</span>;
    <span class="hljs-keyword">end</span> <span class="hljs-keyword">process</span>;
    count &lt;= <span class="hljs-built_in">std_logic_vector</span>(cnt);
<span class="hljs-keyword">end</span> <span class="hljs-keyword">architecture</span> rtl;
""");
    }

    [Fact]
    public void Attributes()
    {
        AssertHighlighter("vhdl",
"""
if clk'event and clk = '1' then
    x <= a'length + b'high;
end if;
""",
"""
<span class="hljs-keyword">if</span> clk<span class="hljs-symbol">&#x27;event</span> <span class="hljs-keyword">and</span> clk = <span class="hljs-string">&#x27;1&#x27;</span> <span class="hljs-keyword">then</span>
    x &lt;= a<span class="hljs-symbol">&#x27;length</span> + b<span class="hljs-symbol">&#x27;high</span>;
<span class="hljs-keyword">end</span> <span class="hljs-keyword">if</span>;
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("vhdl",
"""
constant A : integer := 16#FF#;
constant B : real := 1.5E-3;
constant C : integer := 2#1010_1010#;
constant D : integer := 1_000_000;
constant E : real := 16#F.F#E2;
""",
"""
<span class="hljs-keyword">constant</span> A : <span class="hljs-built_in">integer</span> := <span class="hljs-number">16#FF#</span>;
<span class="hljs-keyword">constant</span> B : real := <span class="hljs-number">1.5E-3</span>;
<span class="hljs-keyword">constant</span> C : <span class="hljs-built_in">integer</span> := <span class="hljs-number">2#1010_1010#</span>;
<span class="hljs-keyword">constant</span> D : <span class="hljs-built_in">integer</span> := <span class="hljs-number">1_000_000</span>;
<span class="hljs-keyword">constant</span> E : real := <span class="hljs-number">16#F.F#E2</span>;
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("vhdl",
"""
report "Value is " & integer'image(x) severity note;
assert false report "failed" severity failure;
s <= "unterminated
wait;
""",
"""
<span class="hljs-keyword">report</span> <span class="hljs-string">&quot;Value is &quot;</span> &amp; <span class="hljs-built_in">integer</span><span class="hljs-symbol">&#x27;image</span>(x) <span class="hljs-keyword">severity</span> <span class="hljs-literal">note</span>;
<span class="hljs-keyword">assert</span> <span class="hljs-literal">false</span> <span class="hljs-keyword">report</span> <span class="hljs-string">&quot;failed&quot;</span> <span class="hljs-keyword">severity</span> <span class="hljs-literal">failure</span>;
s &lt;= <span class="hljs-string">&quot;unterminated
wait;</span>
""");
    }

    [Fact]
    public void Chars()
    {
        AssertHighlighter("vhdl",
"""
x <= 'U'; y <= 'Z'; z <= '-'; w <= 'a'; v <= 'x';
""",
"""
x &lt;= <span class="hljs-string">&#x27;U&#x27;</span>; y &lt;= <span class="hljs-string">&#x27;Z&#x27;</span>; z &lt;= <span class="hljs-string">&#x27;-&#x27;</span>; w &lt;= <span class="hljs-symbol">&#x27;a</span>&#x27;; v &lt;= <span class="hljs-string">&#x27;x&#x27;</span>;
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("vhdl",
"""
-- line comment
/* VHDL-2008
   block comment */
signal s : bit; -- trailing
""",
"""
<span class="hljs-comment">-- line comment</span>
<span class="hljs-comment">/* VHDL-2008
   block comment */</span>
<span class="hljs-keyword">signal</span> s : <span class="hljs-built_in">bit</span>; <span class="hljs-comment">-- trailing</span>
""");
    }

    [Fact]
    public void CaseInsensitive()
    {
        AssertHighlighter("vhdl",
"""
ENTITY Foo IS
  PORT (A : IN STD_LOGIC);
END ENTITY;
""",
"""
<span class="hljs-keyword">ENTITY</span> Foo <span class="hljs-keyword">IS</span>
  <span class="hljs-keyword">PORT</span> (A : <span class="hljs-keyword">IN</span> <span class="hljs-built_in">STD_LOGIC</span>);
<span class="hljs-keyword">END</span> <span class="hljs-keyword">ENTITY</span>;
""");
    }

    [Fact]
    public void IllegalBrace()
    {
        AssertHighlighter("vhdl",
"""
signal x : bit; { not vhdl }
""",
"""
<span class="hljs-keyword">signal</span> x : <span class="hljs-built_in">bit</span>; { <span class="hljs-keyword">not</span> vhdl }
""");
    }

    [Fact]
    public void Empty()
    {
        AssertHighlighter("vhdl", "", "");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("vhdl",
"""
-- héllo
signal héllo : bit;
""",
"""
<span class="hljs-comment">-- héllo</span>
<span class="hljs-keyword">signal</span> héllo : <span class="hljs-built_in">bit</span>;
""");
    }

    [Fact]
    public void AliasVhd()
    {
        AssertHighlighter("vhd",
"""
entity e is end;
""",
"""
<span class="hljs-keyword">entity</span> e <span class="hljs-keyword">is</span> <span class="hljs-keyword">end</span>;
""");
    }
}
