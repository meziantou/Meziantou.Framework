namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class TomlHighlighterTests
{

    [Fact]
    public void Key_Bare()
    {
        AssertHighlighter("toml",
"""
name = "alice"
""",
"""
<span class="hljs-attr">name</span> = <span class="hljs-string">&quot;alice&quot;</span>
""");
    }

    [Fact]
    public void Key_BareDigitsAllowed()
    {
        AssertHighlighter("toml",
"""
key2 = 1
""",
"""
<span class="hljs-attr">key2</span> = <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Key_BareUnderscore()
    {
        AssertHighlighter("toml",
"""
my_key = 1
""",
"""
<span class="hljs-attr">my_key</span> = <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Key_BareHyphen()
    {
        AssertHighlighter("toml",
"""
my-key = 1
""",
"""
<span class="hljs-attr">my-key</span> = <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Key_QuotedBasic()
    {
        AssertHighlighter("toml",
"""
"my key" = 1
""",
"""
<span class="hljs-attr">&quot;my key&quot;</span> = <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Key_QuotedLiteral()
    {
        AssertHighlighter("toml",
"""
'my key' = 1
""",
"""
<span class="hljs-attr">&#x27;my key&#x27;</span> = <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Key_Dotted()
    {
        AssertHighlighter("toml",
"""
site.name = "demo"
""",
"""
<span class="hljs-attr">site.name</span> = <span class="hljs-string">&quot;demo&quot;</span>
""");
    }

    [Fact]
    public void Key_DeepDotted()
    {
        AssertHighlighter("toml",
"""
a.b.c.d = 1
""",
"""
<span class="hljs-attr">a.b.c.d</span> = <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Key_DottedQuoted()
    {
        AssertHighlighter("toml",
"""
site."my key".value = 1
""",
"""
<span class="hljs-attr">site.&quot;my key&quot;.value</span> = <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Key_NumericBare()
    {
        AssertHighlighter("toml",
"""
1234 = "value"
""",
"""
<span class="hljs-attr">1234</span> = <span class="hljs-string">&quot;value&quot;</span>
""");
    }

    [Fact]
    public void Table_Simple()
    {
        AssertHighlighter("toml",
"""
[server]
host = "localhost"
port = 8080
""",
"""
<span class="hljs-section">[server]</span>
<span class="hljs-attr">host</span> = <span class="hljs-string">&quot;localhost&quot;</span>
<span class="hljs-attr">port</span> = <span class="hljs-number">8080</span>
""");
    }

    [Fact]
    public void Table_Dotted()
    {
        AssertHighlighter("toml",
"""
[database.primary]
url = "postgres://localhost/db"
""",
"""
<span class="hljs-section">[database.primary]</span>
<span class="hljs-attr">url</span> = <span class="hljs-string">&quot;postgres://localhost/db&quot;</span>
""");
    }

    [Fact]
    public void Table_DeepDotted()
    {
        AssertHighlighter("toml",
"""
[a.b.c.d]
value = 1
""",
"""
<span class="hljs-section">[a.b.c.d]</span>
<span class="hljs-attr">value</span> = <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Table_QuotedSegment()
    {
        AssertHighlighter("toml",
"""
[servers."east-us"]
ip = "10.0.0.1"
""",
"""
<span class="hljs-section">[servers.&quot;east-us&quot;]</span>
<span class="hljs-attr">ip</span> = <span class="hljs-string">&quot;10.0.0.1&quot;</span>
""");
    }

    [Fact]
    public void Table_EmptyTable()
    {
        AssertHighlighter("toml",
"""
[empty]
""",
"""
<span class="hljs-section">[empty]</span>
""");
    }

    [Fact]
    public void Table_OutOfOrderSiblings()
    {
        AssertHighlighter("toml",
"""
[fruit.apple]
color = "red"

[animal]
name = "cat"

[fruit.orange]
color = "orange"
""",
"""
<span class="hljs-section">[fruit.apple]</span>
<span class="hljs-attr">color</span> = <span class="hljs-string">&quot;red&quot;</span>

<span class="hljs-section">[animal]</span>
<span class="hljs-attr">name</span> = <span class="hljs-string">&quot;cat&quot;</span>

<span class="hljs-section">[fruit.orange]</span>
<span class="hljs-attr">color</span> = <span class="hljs-string">&quot;orange&quot;</span>
""");
    }

    [Fact]
    public void ArrayOfTables_Single()
    {
        AssertHighlighter("toml",
"""
[[products]]
name = "Widget"
price = 9.99
""",
"""
<span class="hljs-section">[[products]]</span>
<span class="hljs-attr">name</span> = <span class="hljs-string">&quot;Widget&quot;</span>
<span class="hljs-attr">price</span> = <span class="hljs-number">9.99</span>
""");
    }

    [Fact]
    public void ArrayOfTables_Multiple()
    {
        AssertHighlighter("toml",
"""
[[products]]
name = "Widget"

[[products]]
name = "Gadget"
""",
"""
<span class="hljs-section">[[products]]</span>
<span class="hljs-attr">name</span> = <span class="hljs-string">&quot;Widget&quot;</span>

<span class="hljs-section">[[products]]</span>
<span class="hljs-attr">name</span> = <span class="hljs-string">&quot;Gadget&quot;</span>
""");
    }

    [Fact]
    public void ArrayOfTables_Dotted()
    {
        AssertHighlighter("toml",
"""
[[fruit.varieties]]
name = "red delicious"

[[fruit.varieties]]
name = "granny smith"
""",
"""
<span class="hljs-section">[[fruit.varieties]]</span>
<span class="hljs-attr">name</span> = <span class="hljs-string">&quot;red delicious&quot;</span>

<span class="hljs-section">[[fruit.varieties]]</span>
<span class="hljs-attr">name</span> = <span class="hljs-string">&quot;granny smith&quot;</span>
""");
    }

    [Fact]
    public void ArrayOfTables_NestedSubtable()
    {
        AssertHighlighter("toml",
"""
[[products]]
name = "Widget"

[products.dimensions]
width = 10
height = 20
""",
"""
<span class="hljs-section">[[products]]</span>
<span class="hljs-attr">name</span> = <span class="hljs-string">&quot;Widget&quot;</span>

<span class="hljs-section">[products.dimensions]</span>
<span class="hljs-attr">width</span> = <span class="hljs-number">10</span>
<span class="hljs-attr">height</span> = <span class="hljs-number">20</span>
""");
    }

    [Fact]
    public void BasicString_Simple()
    {
        AssertHighlighter("toml",
"""
name = "alice"
""",
"""
<span class="hljs-attr">name</span> = <span class="hljs-string">&quot;alice&quot;</span>
""");
    }

    [Fact]
    public void BasicString_Empty()
    {
        AssertHighlighter("toml",
"""
name = ""
""",
"""
<span class="hljs-attr">name</span> = <span class="hljs-string">&quot;&quot;</span>
""");
    }

    [Fact]
    public void BasicString_WithSpaces()
    {
        AssertHighlighter("toml",
"""
title = "The Quick Brown Fox"
""",
"""
<span class="hljs-attr">title</span> = <span class="hljs-string">&quot;The Quick Brown Fox&quot;</span>
""");
    }

    [Fact]
    public void BasicString_EscapeNewline()
    {
        AssertHighlighter("toml",
"""
msg = "line1\nline2"
""",
"""
<span class="hljs-attr">msg</span> = <span class="hljs-string">&quot;line1<span class="hljs-char escape_">\n</span>line2&quot;</span>
""");
    }

    [Fact]
    public void BasicString_EscapeTab()
    {
        AssertHighlighter("toml",
"""
msg = "a\tb"
""",
"""
<span class="hljs-attr">msg</span> = <span class="hljs-string">&quot;a<span class="hljs-char escape_">\t</span>b&quot;</span>
""");
    }

    [Fact]
    public void BasicString_EscapeQuote()
    {
        AssertHighlighter("toml",
"""
msg = "She said \"hi\""
""",
"""
<span class="hljs-attr">msg</span> = <span class="hljs-string">&quot;She said <span class="hljs-char escape_">\&quot;</span>hi<span class="hljs-char escape_">\&quot;</span>&quot;</span>
""");
    }

    [Fact]
    public void BasicString_EscapeBackslash()
    {
        AssertHighlighter("toml",
"""
path = "a\\b"
""",
"""
<span class="hljs-attr">path</span> = <span class="hljs-string">&quot;a<span class="hljs-char escape_">\\</span>b&quot;</span>
""");
    }

    [Fact]
    public void BasicString_EscapeUnicode4()
    {
        AssertHighlighter("toml",
"""
msg = "\u0041"
""",
"""
<span class="hljs-attr">msg</span> = <span class="hljs-string">&quot;<span class="hljs-char escape_">\u0041</span>&quot;</span>
""");
    }

    [Fact]
    public void BasicString_EscapeUnicode8()
    {
        AssertHighlighter("toml",
"""
msg = "\U0001F600"
""",
"""
<span class="hljs-attr">msg</span> = <span class="hljs-string">&quot;<span class="hljs-char escape_">\U0001F600</span>&quot;</span>
""");
    }

    [Fact]
    public void LiteralString_Simple()
    {
        AssertHighlighter("toml",
"""
path = 'C:\Users\alice'
""",
"""
<span class="hljs-attr">path</span> = <span class="hljs-string">&#x27;C:\Users\alice&#x27;</span>
""");
    }

    [Fact]
    public void LiteralString_Empty()
    {
        AssertHighlighter("toml",
"""
name = ''
""",
"""
<span class="hljs-attr">name</span> = <span class="hljs-string">&#x27;&#x27;</span>
""");
    }

    [Fact]
    public void LiteralString_Regex()
    {
        AssertHighlighter("toml",
"""
pattern = '\d{3}-\d{4}'
""",
"""
<span class="hljs-attr">pattern</span> = <span class="hljs-string">&#x27;\d{3}-\d{4}&#x27;</span>
""");
    }

    [Fact]
    public void LiteralString_WithQuotes()
    {
        AssertHighlighter("toml",
"""
quote = 'She said hi'
""",
"""
<span class="hljs-attr">quote</span> = <span class="hljs-string">&#x27;She said hi&#x27;</span>
""");
    }

    [Fact]
    public void MultiLineString_BasicSimple()
    {
        AssertHighlighter("toml",
""""
desc = """
first line
second line
"""
"""",
"""
<span class="hljs-attr">desc</span> = <span class="hljs-string">&quot;&quot;&quot;
first line
second line
&quot;&quot;&quot;</span>
""");
    }

    [Fact]
    public void MultiLineString_BasicTrim()
    {
        AssertHighlighter("toml",
""""
desc = """\
first line \
stays on one line\
"""
"""",
"""
<span class="hljs-attr">desc</span> = <span class="hljs-string">&quot;&quot;&quot;<span class="hljs-char escape_">\</span>
first line <span class="hljs-char escape_">\</span>
stays on one line<span class="hljs-char escape_">\</span>
&quot;&quot;&quot;</span>
""");
    }

    [Fact]
    public void MultiLineString_BasicEscapes()
    {
        AssertHighlighter("toml",
""""
msg = """
line with \"quote\"\nand newline
"""
"""",
"""
<span class="hljs-attr">msg</span> = <span class="hljs-string">&quot;&quot;&quot;
line with <span class="hljs-char escape_">\&quot;</span>quote<span class="hljs-char escape_">\&quot;</span><span class="hljs-char escape_">\n</span>and newline
&quot;&quot;&quot;</span>
""");
    }

    [Fact]
    public void MultiLineString_LiteralSimple()
    {
        AssertHighlighter("toml",
"""
regex = '''
\d{3}-\d{4}
'''
""",
"""
<span class="hljs-attr">regex</span> = <span class="hljs-string">&#x27;&#x27;&#x27;
\d{3}-\d{4}
&#x27;&#x27;&#x27;</span>
""");
    }

    [Fact]
    public void MultiLineString_LiteralWithQuotes()
    {
        AssertHighlighter("toml",
"""
msg = '''
She said "hi"
'''
""",
"""
<span class="hljs-attr">msg</span> = <span class="hljs-string">&#x27;&#x27;&#x27;
She said &quot;hi&quot;
&#x27;&#x27;&#x27;</span>
""");
    }

    [Fact]
    public void Integer_Decimal()
    {
        AssertHighlighter("toml",
"""
count = 42
""",
"""
<span class="hljs-attr">count</span> = <span class="hljs-number">42</span>
""");
    }

    [Fact]
    public void Integer_PositiveSign()
    {
        AssertHighlighter("toml",
"""
count = +42
""",
"""
<span class="hljs-attr">count</span> = <span class="hljs-number">+42</span>
""");
    }

    [Fact]
    public void Integer_NegativeSign()
    {
        AssertHighlighter("toml",
"""
count = -42
""",
"""
<span class="hljs-attr">count</span> = <span class="hljs-number">-42</span>
""");
    }

    [Fact]
    public void Integer_Zero()
    {
        AssertHighlighter("toml",
"""
count = 0
""",
"""
<span class="hljs-attr">count</span> = <span class="hljs-number">0</span>
""");
    }

    [Fact]
    public void Integer_UnderscoreSep()
    {
        AssertHighlighter("toml",
"""
big = 1_000_000
""",
"""
<span class="hljs-attr">big</span> = <span class="hljs-number">1_000_000</span>
""");
    }

    [Fact]
    public void Integer_UnderscoreNested()
    {
        AssertHighlighter("toml",
"""
big = 1_000_000_000
""",
"""
<span class="hljs-attr">big</span> = <span class="hljs-number">1_000_000_000</span>
""");
    }

    [Fact]
    public void Integer_Hex()
    {
        AssertHighlighter("toml",
"""
mask = 0xDEADBEEF
""",
"""
<span class="hljs-attr">mask</span> = <span class="hljs-number">0xDEADBEEF</span>
""");
    }

    [Fact]
    public void Integer_HexUnderscore()
    {
        AssertHighlighter("toml",
"""
mask = 0xDEAD_BEEF
""",
"""
<span class="hljs-attr">mask</span> = <span class="hljs-number">0xDEAD_BEEF</span>
""");
    }

    [Fact]
    public void Integer_Octal()
    {
        AssertHighlighter("toml",
"""
mode = 0o755
""",
"""
<span class="hljs-attr">mode</span> = <span class="hljs-number">0o755</span>
""");
    }

    [Fact]
    public void Integer_Binary()
    {
        AssertHighlighter("toml",
"""
flags = 0b10101100
""",
"""
<span class="hljs-attr">flags</span> = <span class="hljs-number">0b10101100</span>
""");
    }

    [Fact]
    public void Integer_BinaryUnderscore()
    {
        AssertHighlighter("toml",
"""
flags = 0b1010_1100
""",
"""
<span class="hljs-attr">flags</span> = <span class="hljs-number">0b1010_1100</span>
""");
    }

    [Fact]
    public void Float_Simple()
    {
        AssertHighlighter("toml",
"""
pi = 3.14
""",
"""
<span class="hljs-attr">pi</span> = <span class="hljs-number">3.14</span>
""");
    }

    [Fact]
    public void Float_Negative()
    {
        AssertHighlighter("toml",
"""
temp = -3.14
""",
"""
<span class="hljs-attr">temp</span> = <span class="hljs-number">-3.14</span>
""");
    }

    [Fact]
    public void Float_PositiveSign()
    {
        AssertHighlighter("toml",
"""
temp = +3.14
""",
"""
<span class="hljs-attr">temp</span> = <span class="hljs-number">+3.14</span>
""");
    }

    [Fact]
    public void Float_ExponentLower()
    {
        AssertHighlighter("toml",
"""
big = 1e10
""",
"""
<span class="hljs-attr">big</span> = <span class="hljs-number">1e10</span>
""");
    }

    [Fact]
    public void Float_ExponentUpper()
    {
        AssertHighlighter("toml",
"""
big = 1E10
""",
"""
<span class="hljs-attr">big</span> = <span class="hljs-number">1E10</span>
""");
    }

    [Fact]
    public void Float_ExponentSigned()
    {
        AssertHighlighter("toml",
"""
small = 1.5e-3
""",
"""
<span class="hljs-attr">small</span> = <span class="hljs-number">1.5e-3</span>
""");
    }

    [Fact]
    public void Float_ExponentPositive()
    {
        AssertHighlighter("toml",
"""
big = 2.5e+4
""",
"""
<span class="hljs-attr">big</span> = <span class="hljs-number">2.5e+4</span>
""");
    }

    [Fact]
    public void Float_UnderscoreFloat()
    {
        AssertHighlighter("toml",
"""
big = 9_224_617.445_991_228
""",
"""
<span class="hljs-attr">big</span> = <span class="hljs-number">9_224_617.445_991_228</span>
""");
    }

    [Fact]
    public void Float_Infinity()
    {
        AssertHighlighter("toml",
"""
sentinel = inf
""",
"""
<span class="hljs-attr">sentinel</span> = <span class="hljs-number">inf</span>
""");
    }

    [Fact]
    public void Float_PositiveInfinity()
    {
        AssertHighlighter("toml",
"""
sentinel = +inf
""",
"""
<span class="hljs-attr">sentinel</span> = <span class="hljs-number">+inf</span>
""");
    }

    [Fact]
    public void Float_NegativeInfinity()
    {
        AssertHighlighter("toml",
"""
sentinel = -inf
""",
"""
<span class="hljs-attr">sentinel</span> = <span class="hljs-number">-inf</span>
""");
    }

    [Fact]
    public void Float_NaN()
    {
        AssertHighlighter("toml",
"""
sentinel = nan
""",
"""
<span class="hljs-attr">sentinel</span> = <span class="hljs-number">nan</span>
""");
    }

    [Fact]
    public void Float_PositiveNaN()
    {
        AssertHighlighter("toml",
"""
sentinel = +nan
""",
"""
<span class="hljs-attr">sentinel</span> = <span class="hljs-number">+nan</span>
""");
    }

    [Fact]
    public void Float_NegativeNaN()
    {
        AssertHighlighter("toml",
"""
sentinel = -nan
""",
"""
<span class="hljs-attr">sentinel</span> = <span class="hljs-number">-nan</span>
""");
    }

    [Fact]
    public void Boolean_TrueLower()
    {
        AssertHighlighter("toml",
"""
flag = true
""",
"""
<span class="hljs-attr">flag</span> = <span class="hljs-literal">true</span>
""");
    }

    [Fact]
    public void Boolean_FalseLower()
    {
        AssertHighlighter("toml",
"""
flag = false
""",
"""
<span class="hljs-attr">flag</span> = <span class="hljs-literal">false</span>
""");
    }

    [Fact]
    public void DateTime_OffsetZ()
    {
        AssertHighlighter("toml",
"""
created = 2026-05-26T10:30:00Z
""",
"""
<span class="hljs-attr">created</span> = <span class="hljs-number">2026-05-26T10:30:00Z</span>
""");
    }

    [Fact]
    public void DateTime_OffsetPositive()
    {
        AssertHighlighter("toml",
"""
created = 2026-05-26T10:30:00+02:00
""",
"""
<span class="hljs-attr">created</span> = <span class="hljs-number">2026-05-26T10:30:00+02:00</span>
""");
    }

    [Fact]
    public void DateTime_OffsetNegative()
    {
        AssertHighlighter("toml",
"""
created = 2026-05-26T10:30:00-05:00
""",
"""
<span class="hljs-attr">created</span> = <span class="hljs-number">2026-05-26T10:30:00-05:00</span>
""");
    }

    [Fact]
    public void DateTime_OffsetFractional()
    {
        AssertHighlighter("toml",
"""
precise = 2026-05-26T10:30:00.123456Z
""",
"""
<span class="hljs-attr">precise</span> = <span class="hljs-number">2026-05-26T10:30:00.123456Z</span>
""");
    }

    [Fact]
    public void DateTime_OffsetSpaceSeparator()
    {
        AssertHighlighter("toml",
"""
created = 2026-05-26 10:30:00Z
""",
"""
<span class="hljs-attr">created</span> = <span class="hljs-number">2026-05-26 10:30:00Z</span>
""");
    }

    [Fact]
    public void DateTime_LocalDateTime()
    {
        AssertHighlighter("toml",
"""
created = 2026-05-26T10:30:00
""",
"""
<span class="hljs-attr">created</span> = <span class="hljs-number">2026-05-26T10:30:00</span>
""");
    }

    [Fact]
    public void DateTime_LocalDateTimeFractional()
    {
        AssertHighlighter("toml",
"""
created = 2026-05-26T10:30:00.5
""",
"""
<span class="hljs-attr">created</span> = <span class="hljs-number">2026-05-26T10:30:00.5</span>
""");
    }

    [Fact]
    public void DateTime_LocalDate()
    {
        AssertHighlighter("toml",
"""
birthday = 2026-05-26
""",
"""
<span class="hljs-attr">birthday</span> = <span class="hljs-number">2026-05-26</span>
""");
    }

    [Fact]
    public void DateTime_LocalTime()
    {
        AssertHighlighter("toml",
"""
lunch = 12:00:00
""",
"""
<span class="hljs-attr">lunch</span> = <span class="hljs-number">12:00:00</span>
""");
    }

    [Fact]
    public void DateTime_LocalTimeFractional()
    {
        AssertHighlighter("toml",
"""
lunch = 12:00:00.123
""",
"""
<span class="hljs-attr">lunch</span> = <span class="hljs-number">12:00:00.123</span>
""");
    }

    [Fact]
    public void Array_Empty()
    {
        AssertHighlighter("toml",
"""
list = []
""",
"""
<span class="hljs-attr">list</span> = []
""");
    }

    [Fact]
    public void Array_Integers()
    {
        AssertHighlighter("toml",
"""
list = [1, 2, 3]
""",
"""
<span class="hljs-attr">list</span> = [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>]
""");
    }

    [Fact]
    public void Array_Strings()
    {
        AssertHighlighter("toml",
"""
list = ["a", "b", "c"]
""",
"""
<span class="hljs-attr">list</span> = [<span class="hljs-string">&quot;a&quot;</span>, <span class="hljs-string">&quot;b&quot;</span>, <span class="hljs-string">&quot;c&quot;</span>]
""");
    }

    [Fact]
    public void Array_Mixed()
    {
        AssertHighlighter("toml",
"""
list = [1, "two", true]
""",
"""
<span class="hljs-attr">list</span> = [<span class="hljs-number">1</span>, <span class="hljs-string">&quot;two&quot;</span>, <span class="hljs-literal">true</span>]
""");
    }

    [Fact]
    public void Array_Nested()
    {
        AssertHighlighter("toml",
"""
matrix = [[1, 2], [3, 4]]
""",
"""
<span class="hljs-attr">matrix</span> = [[<span class="hljs-number">1</span>, <span class="hljs-number">2</span>], [<span class="hljs-number">3</span>, <span class="hljs-number">4</span>]]
""");
    }

    [Fact]
    public void Array_MultiLine()
    {
        AssertHighlighter("toml",
"""
list = [
  1,
  2,
  3,
]
""",
"""
<span class="hljs-attr">list</span> = [
  <span class="hljs-number">1</span>,
  <span class="hljs-number">2</span>,
  <span class="hljs-number">3</span>,
]
""");
    }

    [Fact]
    public void Array_MultiLineWithComments()
    {
        AssertHighlighter("toml",
"""
list = [
  1,  # first
  2,  # second
  3,  # third
]
""",
"""
<span class="hljs-attr">list</span> = [
  <span class="hljs-number">1</span>,  <span class="hljs-comment"># first</span>
  <span class="hljs-number">2</span>,  <span class="hljs-comment"># second</span>
  <span class="hljs-number">3</span>,  <span class="hljs-comment"># third</span>
]
""");
    }

    [Fact]
    public void Array_TrailingComma()
    {
        AssertHighlighter("toml",
"""
list = [1, 2, 3,]
""",
"""
<span class="hljs-attr">list</span> = [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>,]
""");
    }

    [Fact]
    public void Array_ArrayOfTablesValue()
    {
        AssertHighlighter("toml",
"""
inline_list = [{ x = 1 }, { x = 2 }]
""",
"""
<span class="hljs-attr">inline_list</span> = [{ <span class="hljs-attr">x</span> = <span class="hljs-number">1</span> }, { <span class="hljs-attr">x</span> = <span class="hljs-number">2</span> }]
""");
    }

    [Fact]
    public void Array_ArrayOfDateTimes()
    {
        AssertHighlighter("toml",
"""
when = [2026-05-26, 2026-06-01, 2026-07-04]
""",
"""
<span class="hljs-attr">when</span> = [<span class="hljs-number">2026-05-26</span>, <span class="hljs-number">2026-06-01</span>, <span class="hljs-number">2026-07-04</span>]
""");
    }

    [Fact]
    public void InlineTable_Empty()
    {
        AssertHighlighter("toml",
"""
point = {}
""",
"""
<span class="hljs-attr">point</span> = {}
""");
    }

    [Fact]
    public void InlineTable_Single()
    {
        AssertHighlighter("toml",
"""
point = { x = 1 }
""",
"""
<span class="hljs-attr">point</span> = { <span class="hljs-attr">x</span> = <span class="hljs-number">1</span> }
""");
    }

    [Fact]
    public void InlineTable_Multiple()
    {
        AssertHighlighter("toml",
"""
point = { x = 1, y = 2 }
""",
"""
<span class="hljs-attr">point</span> = { <span class="hljs-attr">x</span> = <span class="hljs-number">1</span>, <span class="hljs-attr">y</span> = <span class="hljs-number">2</span> }
""");
    }

    [Fact]
    public void InlineTable_TypedMix()
    {
        AssertHighlighter("toml",
"""
config = { name = "demo", version = "1.0", debug = false }
""",
"""
<span class="hljs-attr">config</span> = { <span class="hljs-attr">name</span> = <span class="hljs-string">&quot;demo&quot;</span>, <span class="hljs-attr">version</span> = <span class="hljs-string">&quot;1.0&quot;</span>, <span class="hljs-attr">debug</span> = <span class="hljs-literal">false</span> }
""");
    }

    [Fact]
    public void InlineTable_Nested()
    {
        AssertHighlighter("toml",
"""
server = { host = "localhost", db = { name = "main", port = 5432 } }
""",
"""
<span class="hljs-attr">server</span> = { <span class="hljs-attr">host</span> = <span class="hljs-string">&quot;localhost&quot;</span>, <span class="hljs-attr">db</span> = { <span class="hljs-attr">name</span> = <span class="hljs-string">&quot;main&quot;</span>, <span class="hljs-attr">port</span> = <span class="hljs-number">5432</span> } }
""");
    }

    [Fact]
    public void InlineTable_DottedKey()
    {
        AssertHighlighter("toml",
"""
address = { city.name = "Paris", country.code = "FR" }
""",
"""
<span class="hljs-attr">address</span> = { <span class="hljs-attr">city.name</span> = <span class="hljs-string">&quot;Paris&quot;</span>, <span class="hljs-attr">country.code</span> = <span class="hljs-string">&quot;FR&quot;</span> }
""");
    }

    [Fact]
    public void Comment_FullLine()
    {
        AssertHighlighter("toml",
"""
# a comment
""",
"""
<span class="hljs-comment"># a comment</span>
""");
    }

    [Fact]
    public void Comment_Inline()
    {
        AssertHighlighter("toml",
"""
name = "alice"  # the user
""",
"""
<span class="hljs-attr">name</span> = <span class="hljs-string">&quot;alice&quot;</span>  <span class="hljs-comment"># the user</span>
""");
    }

    [Fact]
    public void Comment_AboveTable()
    {
        AssertHighlighter("toml",
"""
# server config
[server]
host = "localhost"
""",
"""
<span class="hljs-comment"># server config</span>
<span class="hljs-section">[server]</span>
<span class="hljs-attr">host</span> = <span class="hljs-string">&quot;localhost&quot;</span>
""");
    }

    [Fact]
    public void Comment_AboveKey()
    {
        AssertHighlighter("toml",
"""
# username
name = "alice"
""",
"""
<span class="hljs-comment"># username</span>
<span class="hljs-attr">name</span> = <span class="hljs-string">&quot;alice&quot;</span>
""");
    }

    [Fact]
    public void Comment_MultipleConsecutive()
    {
        AssertHighlighter("toml",
"""
# line 1
# line 2
# line 3
""",
"""
<span class="hljs-comment"># line 1</span>
<span class="hljs-comment"># line 2</span>
<span class="hljs-comment"># line 3</span>
""");
    }

    [Fact]
    public void Composite_CargoToml()
    {
        AssertHighlighter("toml",
"""
[package]
name = "demo"
version = "1.0.0"
authors = ["Alice <alice@example.com>"]
edition = "2021"

[dependencies]
serde = { version = "1.0", features = ["derive"] }
tokio = { version = "1", features = ["full"] }

[dev-dependencies]
criterion = "0.5"

[[bin]]
name = "demo"
path = "src/main.rs"
""",
"""
<span class="hljs-section">[package]</span>
<span class="hljs-attr">name</span> = <span class="hljs-string">&quot;demo&quot;</span>
<span class="hljs-attr">version</span> = <span class="hljs-string">&quot;1.0.0&quot;</span>
<span class="hljs-attr">authors</span> = [<span class="hljs-string">&quot;Alice &lt;alice@example.com&gt;&quot;</span>]
<span class="hljs-attr">edition</span> = <span class="hljs-string">&quot;2021&quot;</span>

<span class="hljs-section">[dependencies]</span>
<span class="hljs-attr">serde</span> = { <span class="hljs-attr">version</span> = <span class="hljs-string">&quot;1.0&quot;</span>, <span class="hljs-attr">features</span> = [<span class="hljs-string">&quot;derive&quot;</span>] }
<span class="hljs-attr">tokio</span> = { <span class="hljs-attr">version</span> = <span class="hljs-string">&quot;1&quot;</span>, <span class="hljs-attr">features</span> = [<span class="hljs-string">&quot;full&quot;</span>] }

<span class="hljs-section">[dev-dependencies]</span>
<span class="hljs-attr">criterion</span> = <span class="hljs-string">&quot;0.5&quot;</span>

<span class="hljs-section">[[bin]]</span>
<span class="hljs-attr">name</span> = <span class="hljs-string">&quot;demo&quot;</span>
<span class="hljs-attr">path</span> = <span class="hljs-string">&quot;src/main.rs&quot;</span>
""");
    }

    [Fact]
    public void Composite_PyProject()
    {
        AssertHighlighter("toml",
"""
[build-system]
requires = ["setuptools>=64"]
build-backend = "setuptools.build_meta"

[project]
name = "demo"
version = "1.0.0"
description = "A demo project"
requires-python = ">=3.10"
dependencies = [
  "requests>=2.31",
  "pydantic>=2.0",
]

[project.urls]
Homepage = "https://example.com"
Issues = "https://github.com/example/demo/issues"
""",
"""
<span class="hljs-section">[build-system]</span>
<span class="hljs-attr">requires</span> = [<span class="hljs-string">&quot;setuptools&gt;=64&quot;</span>]
<span class="hljs-attr">build-backend</span> = <span class="hljs-string">&quot;setuptools.build_meta&quot;</span>

<span class="hljs-section">[project]</span>
<span class="hljs-attr">name</span> = <span class="hljs-string">&quot;demo&quot;</span>
<span class="hljs-attr">version</span> = <span class="hljs-string">&quot;1.0.0&quot;</span>
<span class="hljs-attr">description</span> = <span class="hljs-string">&quot;A demo project&quot;</span>
<span class="hljs-attr">requires-python</span> = <span class="hljs-string">&quot;&gt;=3.10&quot;</span>
<span class="hljs-attr">dependencies</span> = [
  <span class="hljs-string">&quot;requests&gt;=2.31&quot;</span>,
  <span class="hljs-string">&quot;pydantic&gt;=2.0&quot;</span>,
]

<span class="hljs-section">[project.urls]</span>
<span class="hljs-attr">Homepage</span> = <span class="hljs-string">&quot;https://example.com&quot;</span>
<span class="hljs-attr">Issues</span> = <span class="hljs-string">&quot;https://github.com/example/demo/issues&quot;</span>
""");
    }

    [Fact]
    public void Composite_AppConfig()
    {
        AssertHighlighter("toml",
"""
title = "TOML Example"

[owner]
name = "Alice"
dob = 1990-01-15T00:00:00Z

[database]
enabled = true
ports = [8000, 8001, 8002]
data = [["delta", "phi"], [3.14]]
temp_targets = { cpu = 79.5, case = 72.0 }

[servers]

[servers.alpha]
ip = "10.0.0.1"
role = "frontend"

[servers.beta]
ip = "10.0.0.2"
role = "backend"
""",
"""
<span class="hljs-attr">title</span> = <span class="hljs-string">&quot;TOML Example&quot;</span>

<span class="hljs-section">[owner]</span>
<span class="hljs-attr">name</span> = <span class="hljs-string">&quot;Alice&quot;</span>
<span class="hljs-attr">dob</span> = <span class="hljs-number">1990-01-15T00:00:00Z</span>

<span class="hljs-section">[database]</span>
<span class="hljs-attr">enabled</span> = <span class="hljs-literal">true</span>
<span class="hljs-attr">ports</span> = [<span class="hljs-number">8000</span>, <span class="hljs-number">8001</span>, <span class="hljs-number">8002</span>]
<span class="hljs-attr">data</span> = [[<span class="hljs-string">&quot;delta&quot;</span>, <span class="hljs-string">&quot;phi&quot;</span>], [<span class="hljs-number">3.14</span>]]
<span class="hljs-attr">temp_targets</span> = { <span class="hljs-attr">cpu</span> = <span class="hljs-number">79.5</span>, <span class="hljs-attr">case</span> = <span class="hljs-number">72.0</span> }

<span class="hljs-section">[servers]</span>

<span class="hljs-section">[servers.alpha]</span>
<span class="hljs-attr">ip</span> = <span class="hljs-string">&quot;10.0.0.1&quot;</span>
<span class="hljs-attr">role</span> = <span class="hljs-string">&quot;frontend&quot;</span>

<span class="hljs-section">[servers.beta]</span>
<span class="hljs-attr">ip</span> = <span class="hljs-string">&quot;10.0.0.2&quot;</span>
<span class="hljs-attr">role</span> = <span class="hljs-string">&quot;backend&quot;</span>
""");
    }

    [Fact]
    public void Composite_NetlifyToml()
    {
        AssertHighlighter("toml",
"""
[build]
  command = "npm run build"
  publish = "dist"

[[redirects]]
  from = "/old"
  to   = "/new"
  status = 301

[[redirects]]
  from = "/api/*"
  to   = "https://api.example.com/:splat"
  status = 200
  force = true
""",
"""
<span class="hljs-section">[build]</span>
  <span class="hljs-attr">command</span> = <span class="hljs-string">&quot;npm run build&quot;</span>
  <span class="hljs-attr">publish</span> = <span class="hljs-string">&quot;dist&quot;</span>

<span class="hljs-section">[[redirects]]</span>
  <span class="hljs-attr">from</span> = <span class="hljs-string">&quot;/old&quot;</span>
  <span class="hljs-attr">to</span>   = <span class="hljs-string">&quot;/new&quot;</span>
  <span class="hljs-attr">status</span> = <span class="hljs-number">301</span>

<span class="hljs-section">[[redirects]]</span>
  <span class="hljs-attr">from</span> = <span class="hljs-string">&quot;/api/*&quot;</span>
  <span class="hljs-attr">to</span>   = <span class="hljs-string">&quot;https://api.example.com/:splat&quot;</span>
  <span class="hljs-attr">status</span> = <span class="hljs-number">200</span>
  <span class="hljs-attr">force</span> = <span class="hljs-literal">true</span>
""");
    }

    [Fact]
    public void SpecialEdge_Empty()
    {
        AssertHighlighter("toml",
"""

""",
"""

""");
    }

    [Fact]
    public void SpecialEdge_OnlyWhitespace()
    {
        AssertHighlighter("toml",
"""


""",
"""


""");
    }

    [Fact]
    public void SpecialEdge_OnlyComment()
    {
        AssertHighlighter("toml",
"""
# just a comment
""",
"""
<span class="hljs-comment"># just a comment</span>
""");
    }

    [Fact]
    public void SpecialEdge_BlankBetween()
    {
        AssertHighlighter("toml",
"""
a = 1

b = 2
""",
"""
<span class="hljs-attr">a</span> = <span class="hljs-number">1</span>

<span class="hljs-attr">b</span> = <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void SpecialEdge_NoTableHeader()
    {
        AssertHighlighter("toml",
"""
global_key = "value"
""",
"""
<span class="hljs-attr">global_key</span> = <span class="hljs-string">&quot;value&quot;</span>
""");
    }

    [Fact]
    public void SpecialEdge_TrailingNewline()
    {
        AssertHighlighter("toml",
"""
a = 1

""",
"""
<span class="hljs-attr">a</span> = <span class="hljs-number">1</span>

""");
    }

    [Fact]
    public void MultiLineStringQuotesBeforeClosingDelimiter()
    {
        AssertHighlighter("toml",
""""""
a = """He said "hi"."""
b = """ends with two quotes"""""
c = '''it''s'''
d = ''''quoted'''''
"""""",
"""
<span class="hljs-attr">a</span> = <span class="hljs-string">&quot;&quot;&quot;He said &quot;hi&quot;.&quot;&quot;&quot;</span>
<span class="hljs-attr">b</span> = <span class="hljs-string">&quot;&quot;&quot;ends with two quotes&quot;&quot;&quot;&quot;&quot;</span>
<span class="hljs-attr">c</span> = <span class="hljs-string">&#x27;&#x27;&#x27;it&#x27;&#x27;s&#x27;&#x27;&#x27;</span>
<span class="hljs-attr">d</span> = <span class="hljs-string">&#x27;&#x27;&#x27;&#x27;quoted&#x27;&#x27;&#x27;&#x27;&#x27;</span>
""");
    }

    [Fact]
    public void MultiLineStringCommentMarkersAreText()
    {
        AssertHighlighter("toml",
""""
s = """
# not a comment
"""  # a comment
"""",
"""
<span class="hljs-attr">s</span> = <span class="hljs-string">&quot;&quot;&quot;
# not a comment
&quot;&quot;&quot;</span>  <span class="hljs-comment"># a comment</span>
""");
    }

    [Fact]
    public void MultiLineStringUnterminated()
    {
        AssertHighlighter("toml",
""""
s = """
never closed
key = 1
"""",
"""
<span class="hljs-attr">s</span> = <span class="hljs-string">&quot;&quot;&quot;
never closed
key = 1</span>
""");
    }

    [Fact]
    public void BasicStringToml11Escapes()
    {
        AssertHighlighter("toml",
"""
a = "\e[0m"
b = "\x41"
""",
"""
<span class="hljs-attr">a</span> = <span class="hljs-string">&quot;<span class="hljs-char escape_">\e</span>[0m&quot;</span>
<span class="hljs-attr">b</span> = <span class="hljs-string">&quot;<span class="hljs-char escape_">\x41</span>&quot;</span>
""");
    }

    [Fact]
    public void BasicStringInvalidEscapeDoesNotEndString()
    {
        AssertHighlighter("toml",
"""
a = "\q\" still a string"
""",
"""
<span class="hljs-attr">a</span> = <span class="hljs-string">&quot;\q<span class="hljs-char escape_">\&quot;</span> still a string&quot;</span>
""");
    }

    [Fact]
    public void BasicStringUnterminatedEndsAtEndOfLine()
    {
        AssertHighlighter("toml",
"""
a = "unterminated
b = 'also unterminated
c = 1
""",
"""
<span class="hljs-attr">a</span> = <span class="hljs-string">&quot;unterminated</span>
<span class="hljs-attr">b</span> = <span class="hljs-string">&#x27;also unterminated</span>
<span class="hljs-attr">c</span> = <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void StringHashInside()
    {
        AssertHighlighter("toml",
"""
url = "https://example.com/#anchor" # comment
lit = 'C:\#dir'
""",
"""
<span class="hljs-attr">url</span> = <span class="hljs-string">&quot;https://example.com/#anchor&quot;</span> <span class="hljs-comment"># comment</span>
<span class="hljs-attr">lit</span> = <span class="hljs-string">&#x27;C:\#dir&#x27;</span>
""");
    }

    [Fact]
    public void ArrayNestedOnSeparateLines()
    {
        AssertHighlighter("toml",
"""
matrix = [
  [1, 2],
  [3],
  [
    "a", # comment
    'b',
  ],
]
[table]
k = 1
""",
"""
<span class="hljs-attr">matrix</span> = [
  [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>],
  [<span class="hljs-number">3</span>],
  [
    <span class="hljs-string">&quot;a&quot;</span>, <span class="hljs-comment"># comment</span>
    <span class="hljs-string">&#x27;b&#x27;</span>,
  ],
]
<span class="hljs-section">[table]</span>
<span class="hljs-attr">k</span> = <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void ArrayUnterminatedEndsBeforeNextKey()
    {
        AssertHighlighter("toml",
"""
a = [1, 2,
  3
b = "next"
[table]
c = true
""",
"""
<span class="hljs-attr">a</span> = [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>,
  <span class="hljs-number">3</span>
<span class="hljs-attr">b</span> = <span class="hljs-string">&quot;next&quot;</span>
<span class="hljs-section">[table]</span>
<span class="hljs-attr">c</span> = <span class="hljs-literal">true</span>
""");
    }

    [Fact]
    public void InlineTableUnterminatedEndsBeforeTableHeader()
    {
        AssertHighlighter("toml",
"""
a = { x = 1, y = [2, 3]
b = 2
[table]
c = 3
""",
"""
<span class="hljs-attr">a</span> = { <span class="hljs-attr">x</span> = <span class="hljs-number">1</span>, <span class="hljs-attr">y</span> = [<span class="hljs-number">2</span>, <span class="hljs-number">3</span>]
<span class="hljs-attr">b</span> = <span class="hljs-number">2</span>
<span class="hljs-section">[table]</span>
<span class="hljs-attr">c</span> = <span class="hljs-number">3</span>
""");
    }

    [Fact]
    public void InlineTableMultiLineToml11()
    {
        AssertHighlighter("toml",
"""
point = {
  x = 1, # comment
  y = 2,
}
""",
"""
<span class="hljs-attr">point</span> = {
  <span class="hljs-attr">x</span> = <span class="hljs-number">1</span>, <span class="hljs-comment"># comment</span>
  <span class="hljs-attr">y</span> = <span class="hljs-number">2</span>,
}
""");
    }

    [Fact]
    public void TableIndented()
    {
        AssertHighlighter("toml",
"""
  [indented]
  key = 1
	[[also.indented]]
""",
"""
  <span class="hljs-section">[indented]</span>
  <span class="hljs-attr">key</span> = <span class="hljs-number">1</span>
	<span class="hljs-section">[[also.indented]]</span>
""");
    }

    [Fact]
    public void TableSpacesAroundDots()
    {
        AssertHighlighter("toml",
"""
[ a . "b c" . 'd' ]
[[ e . f ]]
""",
"""
<span class="hljs-section">[ a . &quot;b c&quot; . &#x27;d&#x27; ]</span>
<span class="hljs-section">[[ e . f ]]</span>
""");
    }

    [Fact]
    public void TableWithComment()
    {
        AssertHighlighter("toml",
"""
[server] # the server
[[items]]# item
""",
"""
<span class="hljs-section">[server]</span> <span class="hljs-comment"># the server</span>
<span class="hljs-section">[[items]]</span><span class="hljs-comment"># item</span>
""");
    }

    [Fact]
    public void KeySpacesAroundDots()
    {
        AssertHighlighter("toml",
"""
fruit . color = "red"
"quoted" . bare = 1
""",
"""
<span class="hljs-attr">fruit . color</span> = <span class="hljs-string">&quot;red&quot;</span>
<span class="hljs-attr">&quot;quoted&quot; . bare</span> = <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void KeyLooksLikeValue()
    {
        AssertHighlighter("toml",
"""
true = 1
inf = 2
2024-01-01 = 3
3.14 = 4
""",
"""
<span class="hljs-attr">true</span> = <span class="hljs-number">1</span>
<span class="hljs-attr">inf</span> = <span class="hljs-number">2</span>
<span class="hljs-attr">2024-01-01</span> = <span class="hljs-number">3</span>
<span class="hljs-attr">3.14</span> = <span class="hljs-number">4</span>
""");
    }

    [Fact]
    public void KeyEscapedQuote()
    {
        AssertHighlighter("toml",
"""
"a \"b\" c" = 1
""",
"""
<span class="hljs-attr">&quot;a \&quot;b\&quot; c&quot;</span> = <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void KeyWithoutValue()
    {
        AssertHighlighter("toml",
"""
lonely
= 1
""",
"""
lonely
= <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void DateTimeOptionalSecondsToml11()
    {
        AssertHighlighter("toml",
"""
a = 07:32
b = 1979-05-27 07:32Z
c = 1979-05-27T07:32
""",
"""
<span class="hljs-attr">a</span> = <span class="hljs-number">07:32</span>
<span class="hljs-attr">b</span> = <span class="hljs-number">1979-05-27 07:32Z</span>
<span class="hljs-attr">c</span> = <span class="hljs-number">1979-05-27T07:32</span>
""");
    }

    [Fact]
    public void DateTimeLowercaseSeparators()
    {
        AssertHighlighter("toml",
"""
a = 1987-07-05t17:45:00z
""",
"""
<span class="hljs-attr">a</span> = <span class="hljs-number">1987-07-05t17:45:00z</span>
""");
    }

    [Fact]
    public void DateTimeFollowedByComment()
    {
        AssertHighlighter("toml",
"""
a = 1979-05-27 # a date
b = 07:32:00 # a time
""",
"""
<span class="hljs-attr">a</span> = <span class="hljs-number">1979-05-27</span> <span class="hljs-comment"># a date</span>
<span class="hljs-attr">b</span> = <span class="hljs-number">07:32:00</span> <span class="hljs-comment"># a time</span>
""");
    }

    [Fact]
    public void NumberNotInsideWords()
    {
        AssertHighlighter("toml",
"""
a = [1a, 0x, 1__0, 1.2.3]
""",
"""
<span class="hljs-attr">a</span> = [1a, 0x, 1__0, 1.2.3]
""");
    }

    [Fact]
    public void Crlf()
    {
        AssertHighlighter("toml", "[t]\r\na = 1\r\nb = \"x\" # c\r\n", "<span class=\"hljs-section\">[t]</span>\r\n<span class=\"hljs-attr\">a</span> = <span class=\"hljs-number\">1</span>\r\n<span class=\"hljs-attr\">b</span> = <span class=\"hljs-string\">&quot;x&quot;</span> <span class=\"hljs-comment\"># c</span>\r\n");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("toml",
"""
"clé" = "valeur é"
[tableau."é"]
emoji = "😀"
""",
"""
<span class="hljs-attr">&quot;clé&quot;</span> = <span class="hljs-string">&quot;valeur é&quot;</span>
<span class="hljs-section">[tableau.&quot;é&quot;]</span>
<span class="hljs-attr">emoji</span> = <span class="hljs-string">&quot;😀&quot;</span>
""");
    }

    [Fact]
    public void RealWorldRustToolchain()
    {
        AssertHighlighter("toml",
"""
[toolchain]
channel = "1.80.0"
components = ["rustfmt", "clippy"]
targets = [
    "x86_64-unknown-linux-gnu",
    "aarch64-apple-darwin",
]
profile = "minimal"
""",
"""
<span class="hljs-section">[toolchain]</span>
<span class="hljs-attr">channel</span> = <span class="hljs-string">&quot;1.80.0&quot;</span>
<span class="hljs-attr">components</span> = [<span class="hljs-string">&quot;rustfmt&quot;</span>, <span class="hljs-string">&quot;clippy&quot;</span>]
<span class="hljs-attr">targets</span> = [
    <span class="hljs-string">&quot;x86_64-unknown-linux-gnu&quot;</span>,
    <span class="hljs-string">&quot;aarch64-apple-darwin&quot;</span>,
]
<span class="hljs-attr">profile</span> = <span class="hljs-string">&quot;minimal&quot;</span>
""");
    }

    [Fact]
    public void RealWorldHugoConfig()
    {
        AssertHighlighter("toml",
""""
baseURL = 'https://example.org/'
languageCode = 'en-us'
title = 'My New Hugo Site'
paginate = 10

[params]
  description = """
  A blog about \
  things."""
  showReadingTime = true
  dateFormat = "Jan 2, 2006"

[[menu.main]]
  identifier = "posts"
  name = "Posts"
  url = "/posts/"
  weight = 10

[markup.goldmark.renderer]
  unsafe = true
"""",
"""
<span class="hljs-attr">baseURL</span> = <span class="hljs-string">&#x27;https://example.org/&#x27;</span>
<span class="hljs-attr">languageCode</span> = <span class="hljs-string">&#x27;en-us&#x27;</span>
<span class="hljs-attr">title</span> = <span class="hljs-string">&#x27;My New Hugo Site&#x27;</span>
<span class="hljs-attr">paginate</span> = <span class="hljs-number">10</span>

<span class="hljs-section">[params]</span>
  <span class="hljs-attr">description</span> = <span class="hljs-string">&quot;&quot;&quot;
  A blog about <span class="hljs-char escape_">\</span>
  things.&quot;&quot;&quot;</span>
  <span class="hljs-attr">showReadingTime</span> = <span class="hljs-literal">true</span>
  <span class="hljs-attr">dateFormat</span> = <span class="hljs-string">&quot;Jan 2, 2006&quot;</span>

<span class="hljs-section">[[menu.main]]</span>
  <span class="hljs-attr">identifier</span> = <span class="hljs-string">&quot;posts&quot;</span>
  <span class="hljs-attr">name</span> = <span class="hljs-string">&quot;Posts&quot;</span>
  <span class="hljs-attr">url</span> = <span class="hljs-string">&quot;/posts/&quot;</span>
  <span class="hljs-attr">weight</span> = <span class="hljs-number">10</span>

<span class="hljs-section">[markup.goldmark.renderer]</span>
  <span class="hljs-attr">unsafe</span> = <span class="hljs-literal">true</span>
""");
    }
}
