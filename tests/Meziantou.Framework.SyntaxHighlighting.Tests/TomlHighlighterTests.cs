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
<span class="hljs-attr">name</span> = &quot;alice&quot;
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
<span class="hljs-attr">key2</span> = 1
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
<span class="hljs-attr">my_key</span> = 1
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
<span class="hljs-attr">my-key</span> = 1
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
<span class="hljs-attr">&quot;my key&quot;</span> = 1
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
<span class="hljs-attr">&#x27;my key&#x27;</span> = 1
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
<span class="hljs-attr">site.name</span> = &quot;demo&quot;
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
<span class="hljs-attr">a.b.c.d</span> = 1
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
<span class="hljs-attr">site.&quot;my key&quot;.value</span> = 1
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
<span class="hljs-attr">1234</span> = &quot;value&quot;
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
<span class="hljs-attr">host</span> = &quot;localhost&quot;
<span class="hljs-attr">port</span> = 8080
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
<span class="hljs-attr">url</span> = &quot;postgres://localhost/db&quot;
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
<span class="hljs-attr">value</span> = 1
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
<span class="hljs-attr">ip</span> = &quot;10.0.0.1&quot;
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
<span class="hljs-attr">color</span> = &quot;red&quot;

<span class="hljs-section">[animal]</span>
<span class="hljs-attr">name</span> = &quot;cat&quot;

<span class="hljs-section">[fruit.orange]</span>
<span class="hljs-attr">color</span> = &quot;orange&quot;
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
<span class="hljs-attr">name</span> = &quot;Widget&quot;
<span class="hljs-attr">price</span> = 9.99
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
<span class="hljs-attr">name</span> = &quot;Widget&quot;

<span class="hljs-section">[[products]]</span>
<span class="hljs-attr">name</span> = &quot;Gadget&quot;
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
<span class="hljs-attr">name</span> = &quot;red delicious&quot;

<span class="hljs-section">[[fruit.varieties]]</span>
<span class="hljs-attr">name</span> = &quot;granny smith&quot;
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
<span class="hljs-attr">name</span> = &quot;Widget&quot;

<span class="hljs-section">[products.dimensions]</span>
<span class="hljs-attr">width</span> = 10
<span class="hljs-attr">height</span> = 20
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
<span class="hljs-attr">name</span> = &quot;alice&quot;
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
<span class="hljs-attr">name</span> = &quot;&quot;
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
<span class="hljs-attr">title</span> = &quot;The Quick Brown Fox&quot;
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
<span class="hljs-attr">msg</span> = &quot;line1\nline2&quot;
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
<span class="hljs-attr">msg</span> = &quot;a\tb&quot;
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
<span class="hljs-attr">msg</span> = &quot;She said \&quot;hi\&quot;&quot;
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
<span class="hljs-attr">path</span> = &quot;a\\b&quot;
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
<span class="hljs-attr">msg</span> = &quot;\u0041&quot;
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
<span class="hljs-attr">msg</span> = &quot;\U0001F600&quot;
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
<span class="hljs-attr">path</span> = &#x27;C:\Users\alice&#x27;
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
<span class="hljs-attr">name</span> = &#x27;&#x27;
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
<span class="hljs-attr">pattern</span> = &#x27;\d{3}-\d{4}&#x27;
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
<span class="hljs-attr">quote</span> = &#x27;She said hi&#x27;
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
desc = &quot;&quot;&quot;
first line
second line
&quot;&quot;&quot;
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
desc = &quot;&quot;&quot;\
first line \
stays on one line\
&quot;&quot;&quot;
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
msg = &quot;&quot;&quot;
line with \&quot;quote\&quot;\nand newline
&quot;&quot;&quot;
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
regex = &#x27;&#x27;&#x27;
\d{3}-\d{4}
&#x27;&#x27;&#x27;
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
msg = &#x27;&#x27;&#x27;
She said &quot;hi&quot;
&#x27;&#x27;&#x27;
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
<span class="hljs-attr">count</span> = 42
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
<span class="hljs-attr">count</span> = +42
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
<span class="hljs-attr">count</span> = -42
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
<span class="hljs-attr">count</span> = 0
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
<span class="hljs-attr">big</span> = 1_000_000
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
<span class="hljs-attr">big</span> = 1_000_000_000
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
<span class="hljs-attr">mask</span> = 0xDEADBEEF
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
<span class="hljs-attr">mask</span> = 0xDEAD_BEEF
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
<span class="hljs-attr">mode</span> = 0o755
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
<span class="hljs-attr">flags</span> = 0b10101100
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
<span class="hljs-attr">flags</span> = 0b1010_1100
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
<span class="hljs-attr">pi</span> = 3.14
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
<span class="hljs-attr">temp</span> = -3.14
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
<span class="hljs-attr">temp</span> = +3.14
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
<span class="hljs-attr">big</span> = 1e10
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
<span class="hljs-attr">big</span> = 1E10
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
<span class="hljs-attr">small</span> = 1.5e-3
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
<span class="hljs-attr">big</span> = 2.5e+4
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
<span class="hljs-attr">big</span> = 9_224_617.445_991_228
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
<span class="hljs-attr">sentinel</span> = inf
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
<span class="hljs-attr">sentinel</span> = +inf
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
<span class="hljs-attr">sentinel</span> = -inf
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
<span class="hljs-attr">sentinel</span> = nan
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
<span class="hljs-attr">sentinel</span> = +nan
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
<span class="hljs-attr">sentinel</span> = -nan
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
<span class="hljs-attr">flag</span> = true
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
<span class="hljs-attr">flag</span> = false
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
<span class="hljs-attr">created</span> = 2026-05-26T10:30:00Z
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
<span class="hljs-attr">created</span> = 2026-05-26T10:30:00+02:00
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
<span class="hljs-attr">created</span> = 2026-05-26T10:30:00-05:00
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
<span class="hljs-attr">precise</span> = 2026-05-26T10:30:00.123456Z
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
<span class="hljs-attr">created</span> = 2026-05-26 10:30:00Z
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
<span class="hljs-attr">created</span> = 2026-05-26T10:30:00
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
<span class="hljs-attr">created</span> = 2026-05-26T10:30:00.5
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
<span class="hljs-attr">birthday</span> = 2026-05-26
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
<span class="hljs-attr">lunch</span> = 12:00:00
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
<span class="hljs-attr">lunch</span> = 12:00:00.123
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
<span class="hljs-attr">list</span> = [1, 2, 3]
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
<span class="hljs-attr">list</span> = [&quot;a&quot;, &quot;b&quot;, &quot;c&quot;]
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
<span class="hljs-attr">list</span> = [1, &quot;two&quot;, true]
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
<span class="hljs-attr">matrix</span> = [[1, 2], [3, 4]]
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
list = [
  1,
  2,
  3,
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
list = [
  1,  # first
  2,  # second
  3,  # third
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
<span class="hljs-attr">list</span> = [1, 2, 3,]
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
<span class="hljs-attr">inline_list</span> = [{ x = 1 }, { x = 2 }]
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
<span class="hljs-attr">when</span> = [2026-05-26, 2026-06-01, 2026-07-04]
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
<span class="hljs-attr">point</span> = { x = 1 }
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
<span class="hljs-attr">point</span> = { x = 1, y = 2 }
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
<span class="hljs-attr">config</span> = { name = &quot;demo&quot;, version = &quot;1.0&quot;, debug = false }
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
<span class="hljs-attr">server</span> = { host = &quot;localhost&quot;, db = { name = &quot;main&quot;, port = 5432 } }
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
<span class="hljs-attr">address</span> = { city.name = &quot;Paris&quot;, country.code = &quot;FR&quot; }
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
<span class="hljs-attr">name</span> = &quot;alice&quot;  # the user
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
<span class="hljs-attr">host</span> = &quot;localhost&quot;
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
<span class="hljs-attr">name</span> = &quot;alice&quot;
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
<span class="hljs-attr">name</span> = &quot;demo&quot;
<span class="hljs-attr">version</span> = &quot;1.0.0&quot;
<span class="hljs-attr">authors</span> = [&quot;Alice &lt;alice@example.com&gt;&quot;]
<span class="hljs-attr">edition</span> = &quot;2021&quot;

<span class="hljs-section">[dependencies]</span>
<span class="hljs-attr">serde</span> = { version = &quot;1.0&quot;, features = [&quot;derive&quot;] }
<span class="hljs-attr">tokio</span> = { version = &quot;1&quot;, features = [&quot;full&quot;] }

<span class="hljs-section">[dev-dependencies]</span>
<span class="hljs-attr">criterion</span> = &quot;0.5&quot;

<span class="hljs-section">[[bin]]</span>
<span class="hljs-attr">name</span> = &quot;demo&quot;
<span class="hljs-attr">path</span> = &quot;src/main.rs&quot;
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
[build-system]
requires = [&quot;setuptools&gt;=64&quot;]
build-backend = &quot;setuptools.build_meta&quot;

[project]
name = &quot;demo&quot;
version = &quot;1.0.0&quot;
description = &quot;A demo project&quot;
requires-python = &quot;&gt;=3.10&quot;
dependencies = [
  &quot;requests&gt;=2.31&quot;,
  &quot;pydantic&gt;=2.0&quot;,
]

[project.urls]
Homepage = &quot;https://example.com&quot;
Issues = &quot;https://github.com/example/demo/issues&quot;
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
<span class="hljs-attr">title</span> = &quot;TOML Example&quot;

<span class="hljs-section">[owner]</span>
<span class="hljs-attr">name</span> = &quot;Alice&quot;
<span class="hljs-attr">dob</span> = 1990-01-15T00:00:00Z

<span class="hljs-section">[database]</span>
<span class="hljs-attr">enabled</span> = true
<span class="hljs-attr">ports</span> = [8000, 8001, 8002]
<span class="hljs-attr">data</span> = [[&quot;delta&quot;, &quot;phi&quot;], [3.14]]
<span class="hljs-attr">temp_targets</span> = { cpu = 79.5, case = 72.0 }

<span class="hljs-section">[servers]</span>

<span class="hljs-section">[servers.alpha]</span>
<span class="hljs-attr">ip</span> = &quot;10.0.0.1&quot;
<span class="hljs-attr">role</span> = &quot;frontend&quot;

<span class="hljs-section">[servers.beta]</span>
<span class="hljs-attr">ip</span> = &quot;10.0.0.2&quot;
<span class="hljs-attr">role</span> = &quot;backend&quot;
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
  <span class="hljs-attr">command</span> = &quot;npm run build&quot;
  <span class="hljs-attr">publish</span> = &quot;dist&quot;

<span class="hljs-section">[[redirects]]</span>
  <span class="hljs-attr">from</span> = &quot;/old&quot;
  <span class="hljs-attr">to</span>   = &quot;/new&quot;
  <span class="hljs-attr">status</span> = 301

<span class="hljs-section">[[redirects]]</span>
  <span class="hljs-attr">from</span> = &quot;/api/*&quot;
  <span class="hljs-attr">to</span>   = &quot;https://api.example.com/:splat&quot;
  <span class="hljs-attr">status</span> = 200
  <span class="hljs-attr">force</span> = true
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
<span class="hljs-attr">a</span> = 1

<span class="hljs-attr">b</span> = 2
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
<span class="hljs-attr">global_key</span> = &quot;value&quot;
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
<span class="hljs-attr">a</span> = 1

""");
    }
}
