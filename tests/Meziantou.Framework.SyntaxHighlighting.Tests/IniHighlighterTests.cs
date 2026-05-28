namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class IniHighlighterTests
{

    [Fact]
    public void Section_Simple()
    {
        AssertHighlighter("ini",
"""
[database]
""",
"""
<span class="hljs-section">[database]</span>
""");
    }

    [Fact]
    public void Section_Dotted()
    {
        AssertHighlighter("ini",
"""
[database.production]
""",
"""
<span class="hljs-section">[database.production]</span>
""");
    }

    [Fact]
    public void Section_DeepDotted()
    {
        AssertHighlighter("ini",
"""
[a.b.c.d]
""",
"""
<span class="hljs-section">[a.b.c.d]</span>
""");
    }

    [Fact]
    public void Section_Hyphenated()
    {
        AssertHighlighter("ini",
"""
[my-section]
""",
"""
<span class="hljs-section">[my-section]</span>
""");
    }

    [Fact]
    public void Section_Underscored()
    {
        AssertHighlighter("ini",
"""
[my_section]
""",
"""
<span class="hljs-section">[my_section]</span>
""");
    }

    [Fact]
    public void Section_WithSpaces()
    {
        AssertHighlighter("ini",
"""
[ database ]
""",
"""
<span class="hljs-section">[ database ]</span>
""");
    }

    [Fact]
    public void Section_QuotedKey()
    {
        AssertHighlighter("ini",
"""
["complex section"]
""",
"""
<span class="hljs-section">[&quot;complex section&quot;]</span>
""");
    }

    [Fact]
    public void Section_ArrayOfTables()
    {
        AssertHighlighter("ini",
"""
[[products]]
""",
"""
<span class="hljs-section">[[products]]</span>
""");
    }

    [Fact]
    public void Section_ArrayOfTablesDotted()
    {
        AssertHighlighter("ini",
"""
[[products.colors]]
""",
"""
<span class="hljs-section">[[products.colors]]</span>
""");
    }

    [Fact]
    public void KeyValue_Simple()
    {
        AssertHighlighter("ini",
"""
name = alice
""",
"""
<span class="hljs-attr">name</span> = alice
""");
    }

    [Fact]
    public void KeyValue_NoSpaces()
    {
        AssertHighlighter("ini",
"""
name=alice
""",
"""
<span class="hljs-attr">name</span>=alice
""");
    }

    [Fact]
    public void KeyValue_ColonSeparator()
    {
        AssertHighlighter("ini",
"""
name: alice
""",
"""
name: alice
""");
    }

    [Fact]
    public void KeyValue_EmptyValue()
    {
        AssertHighlighter("ini",
"""
name =
""",
"""
name =
""");
    }

    [Fact]
    public void KeyValue_KeyDotted()
    {
        AssertHighlighter("ini",
"""
site.url = https://example.com
""",
"""
<span class="hljs-attr">site.url</span> = https://example.com
""");
    }

    [Fact]
    public void KeyValue_KeyHyphenated()
    {
        AssertHighlighter("ini",
"""
my-key = value
""",
"""
<span class="hljs-attr">my-key</span> = value
""");
    }

    [Fact]
    public void KeyValue_KeyUnderscored()
    {
        AssertHighlighter("ini",
"""
my_key = value
""",
"""
<span class="hljs-attr">my_key</span> = value
""");
    }

    [Fact]
    public void KeyValue_KeyQuotedDouble()
    {
        AssertHighlighter("ini",
"""
"with spaces" = 1
""",
"""
<span class="hljs-attr">&quot;with spaces&quot;</span> = 1
""");
    }

    [Fact]
    public void KeyValue_KeyQuotedSingle()
    {
        AssertHighlighter("ini",
"""
'literal key' = 1
""",
"""
<span class="hljs-attr">&#x27;literal key&#x27;</span> = 1
""");
    }

    [Fact]
    public void KeyValue_KeyNumericStart()
    {
        AssertHighlighter("ini",
"""
42 = answer
""",
"""
<span class="hljs-attr">42</span> = answer
""");
    }

    [Fact]
    public void KeyValue_ValueWithSpaces()
    {
        AssertHighlighter("ini",
"""
title = The Quick Brown Fox
""",
"""
<span class="hljs-attr">title</span> = The Quick Brown Fox
""");
    }

    [Fact]
    public void StringValue_DoubleQuoted()
    {
        AssertHighlighter("ini",
"""
name = "alice"
""",
"""
<span class="hljs-attr">name</span> = &quot;alice&quot;
""");
    }

    [Fact]
    public void StringValue_SingleQuoted()
    {
        AssertHighlighter("ini",
"""
name = 'alice'
""",
"""
<span class="hljs-attr">name</span> = &#x27;alice&#x27;
""");
    }

    [Fact]
    public void StringValue_Empty()
    {
        AssertHighlighter("ini",
"""
name = ""
""",
"""
<span class="hljs-attr">name</span> = &quot;&quot;
""");
    }

    [Fact]
    public void StringValue_EscapeNewline()
    {
        AssertHighlighter("ini",
"""
msg = "line1\nline2"
""",
"""
<span class="hljs-attr">msg</span> = &quot;line1\nline2&quot;
""");
    }

    [Fact]
    public void StringValue_EscapeTab()
    {
        AssertHighlighter("ini",
"""
msg = "a\tb"
""",
"""
<span class="hljs-attr">msg</span> = &quot;a\tb&quot;
""");
    }

    [Fact]
    public void StringValue_EscapeQuote()
    {
        AssertHighlighter("ini",
"""
msg = "She said \"hi\""
""",
"""
<span class="hljs-attr">msg</span> = &quot;She said \&quot;hi\&quot;&quot;
""");
    }

    [Fact]
    public void StringValue_UnicodeEscape()
    {
        AssertHighlighter("ini",
"""
msg = "\u0041"
""",
"""
<span class="hljs-attr">msg</span> = &quot;\u0041&quot;
""");
    }

    [Fact]
    public void StringValue_LiteralSingle()
    {
        AssertHighlighter("ini",
"""
path = 'C:\\Users\\alice'
""",
"""
<span class="hljs-attr">path</span> = &#x27;C:\\Users\\alice&#x27;
""");
    }

    [Fact]
    public void StringValue_TripleDoubleMultiLine()
    {
        AssertHighlighter("ini",
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
    public void StringValue_TripleSingleLiteral()
    {
        AssertHighlighter("ini",
"""
regex = '''\d{3}-\d{4}'''
""",
"""
<span class="hljs-attr">regex</span> = &#x27;&#x27;&#x27;\d{3}-\d{4}&#x27;&#x27;&#x27;
""");
    }

    [Fact]
    public void NumberValue_Integer()
    {
        AssertHighlighter("ini",
"""
count = 42
""",
"""
<span class="hljs-attr">count</span> = 42
""");
    }

    [Fact]
    public void NumberValue_NegativeInteger()
    {
        AssertHighlighter("ini",
"""
temp = -10
""",
"""
<span class="hljs-attr">temp</span> = -10
""");
    }

    [Fact]
    public void NumberValue_PositiveInteger()
    {
        AssertHighlighter("ini",
"""
temp = +10
""",
"""
<span class="hljs-attr">temp</span> = +10
""");
    }

    [Fact]
    public void NumberValue_Zero()
    {
        AssertHighlighter("ini",
"""
count = 0
""",
"""
<span class="hljs-attr">count</span> = 0
""");
    }

    [Fact]
    public void NumberValue_Float()
    {
        AssertHighlighter("ini",
"""
pi = 3.14
""",
"""
<span class="hljs-attr">pi</span> = 3.14
""");
    }

    [Fact]
    public void NumberValue_NegativeFloat()
    {
        AssertHighlighter("ini",
"""
temp = -3.14
""",
"""
<span class="hljs-attr">temp</span> = -3.14
""");
    }

    [Fact]
    public void NumberValue_ExponentLower()
    {
        AssertHighlighter("ini",
"""
big = 1e10
""",
"""
<span class="hljs-attr">big</span> = 1e10
""");
    }

    [Fact]
    public void NumberValue_ExponentNegative()
    {
        AssertHighlighter("ini",
"""
small = 1.5e-3
""",
"""
<span class="hljs-attr">small</span> = 1.5e-3
""");
    }

    [Fact]
    public void NumberValue_ExponentUpper()
    {
        AssertHighlighter("ini",
"""
big = 1E5
""",
"""
<span class="hljs-attr">big</span> = 1E5
""");
    }

    [Fact]
    public void NumberValue_Separator()
    {
        AssertHighlighter("ini",
"""
big = 1_000_000
""",
"""
<span class="hljs-attr">big</span> = 1_000_000
""");
    }

    [Fact]
    public void NumberValue_Hex()
    {
        AssertHighlighter("ini",
"""
mask = 0xDEADBEEF
""",
"""
<span class="hljs-attr">mask</span> = 0xDEADBEEF
""");
    }

    [Fact]
    public void NumberValue_Octal()
    {
        AssertHighlighter("ini",
"""
mode = 0o755
""",
"""
<span class="hljs-attr">mode</span> = 0o755
""");
    }

    [Fact]
    public void NumberValue_Binary()
    {
        AssertHighlighter("ini",
"""
flags = 0b10101
""",
"""
<span class="hljs-attr">flags</span> = 0b10101
""");
    }

    [Fact]
    public void NumberValue_Infinity()
    {
        AssertHighlighter("ini",
"""
sentinel = inf
""",
"""
<span class="hljs-attr">sentinel</span> = inf
""");
    }

    [Fact]
    public void NumberValue_NegativeInfinity()
    {
        AssertHighlighter("ini",
"""
sentinel = -inf
""",
"""
<span class="hljs-attr">sentinel</span> = -inf
""");
    }

    [Fact]
    public void NumberValue_NaN()
    {
        AssertHighlighter("ini",
"""
sentinel = nan
""",
"""
<span class="hljs-attr">sentinel</span> = nan
""");
    }

    [Fact]
    public void BooleanValue_TrueLower()
    {
        AssertHighlighter("ini",
"""
flag = true
""",
"""
<span class="hljs-attr">flag</span> = true
""");
    }

    [Fact]
    public void BooleanValue_TrueTitle()
    {
        AssertHighlighter("ini",
"""
flag = True
""",
"""
<span class="hljs-attr">flag</span> = True
""");
    }

    [Fact]
    public void BooleanValue_TrueUpper()
    {
        AssertHighlighter("ini",
"""
flag = TRUE
""",
"""
<span class="hljs-attr">flag</span> = TRUE
""");
    }

    [Fact]
    public void BooleanValue_FalseLower()
    {
        AssertHighlighter("ini",
"""
flag = false
""",
"""
<span class="hljs-attr">flag</span> = false
""");
    }

    [Fact]
    public void BooleanValue_FalseTitle()
    {
        AssertHighlighter("ini",
"""
flag = False
""",
"""
<span class="hljs-attr">flag</span> = False
""");
    }

    [Fact]
    public void BooleanValue_FalseUpper()
    {
        AssertHighlighter("ini",
"""
flag = FALSE
""",
"""
<span class="hljs-attr">flag</span> = FALSE
""");
    }

    [Fact]
    public void BooleanValue_Yes()
    {
        AssertHighlighter("ini",
"""
flag = yes
""",
"""
<span class="hljs-attr">flag</span> = yes
""");
    }

    [Fact]
    public void BooleanValue_No()
    {
        AssertHighlighter("ini",
"""
flag = no
""",
"""
<span class="hljs-attr">flag</span> = no
""");
    }

    [Fact]
    public void BooleanValue_On()
    {
        AssertHighlighter("ini",
"""
flag = on
""",
"""
<span class="hljs-attr">flag</span> = on
""");
    }

    [Fact]
    public void BooleanValue_Off()
    {
        AssertHighlighter("ini",
"""
flag = off
""",
"""
<span class="hljs-attr">flag</span> = off
""");
    }

    [Fact]
    public void DateValue_OffsetDateTime()
    {
        AssertHighlighter("ini",
"""
created = 2026-05-26T10:30:00Z
""",
"""
<span class="hljs-attr">created</span> = 2026-05-26T10:30:00Z
""");
    }

    [Fact]
    public void DateValue_OffsetWithOffset()
    {
        AssertHighlighter("ini",
"""
created = 2026-05-26T10:30:00-05:00
""",
"""
<span class="hljs-attr">created</span> = 2026-05-26T10:30:00-05:00
""");
    }

    [Fact]
    public void DateValue_LocalDateTime()
    {
        AssertHighlighter("ini",
"""
created = 2026-05-26T10:30:00
""",
"""
<span class="hljs-attr">created</span> = 2026-05-26T10:30:00
""");
    }

    [Fact]
    public void DateValue_LocalDate()
    {
        AssertHighlighter("ini",
"""
birthday = 2026-05-26
""",
"""
<span class="hljs-attr">birthday</span> = 2026-05-26
""");
    }

    [Fact]
    public void DateValue_LocalTime()
    {
        AssertHighlighter("ini",
"""
lunch = 12:00:00
""",
"""
<span class="hljs-attr">lunch</span> = 12:00:00
""");
    }

    [Fact]
    public void DateValue_Fractional()
    {
        AssertHighlighter("ini",
"""
precise = 2026-05-26T10:30:00.123456Z
""",
"""
<span class="hljs-attr">precise</span> = 2026-05-26T10:30:00.123456Z
""");
    }

    [Fact]
    public void ArrayValue_EmptyArray()
    {
        AssertHighlighter("ini",
"""
list = []
""",
"""
<span class="hljs-attr">list</span> = []
""");
    }

    [Fact]
    public void ArrayValue_IntArray()
    {
        AssertHighlighter("ini",
"""
list = [1, 2, 3]
""",
"""
<span class="hljs-attr">list</span> = [1, 2, 3]
""");
    }

    [Fact]
    public void ArrayValue_StringArray()
    {
        AssertHighlighter("ini",
"""
list = ["a", "b", "c"]
""",
"""
<span class="hljs-attr">list</span> = [&quot;a&quot;, &quot;b&quot;, &quot;c&quot;]
""");
    }

    [Fact]
    public void ArrayValue_MixedArray()
    {
        AssertHighlighter("ini",
"""
list = [1, "two", true]
""",
"""
<span class="hljs-attr">list</span> = [1, &quot;two&quot;, true]
""");
    }

    [Fact]
    public void ArrayValue_NestedArray()
    {
        AssertHighlighter("ini",
"""
matrix = [[1, 2], [3, 4]]
""",
"""
<span class="hljs-attr">matrix</span> = [[1, 2], [3, 4]]
""");
    }

    [Fact]
    public void ArrayValue_MultiLineArray()
    {
        AssertHighlighter("ini",
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
    public void InlineTable_Empty()
    {
        AssertHighlighter("ini",
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
        AssertHighlighter("ini",
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
        AssertHighlighter("ini",
"""
point = { x = 1, y = 2 }
""",
"""
<span class="hljs-attr">point</span> = { x = 1, y = 2 }
""");
    }

    [Fact]
    public void InlineTable_Nested()
    {
        AssertHighlighter("ini",
"""
config = { db = { host = "localhost", port = 5432 } }
""",
"""
<span class="hljs-attr">config</span> = { db = { host = &quot;localhost&quot;, port = 5432 } }
""");
    }

    [Fact]
    public void Comment_Semicolon()
    {
        AssertHighlighter("ini",
"""
; this is a comment
""",
"""
<span class="hljs-comment">; this is a comment</span>
""");
    }

    [Fact]
    public void Comment_Hash()
    {
        AssertHighlighter("ini",
"""
# this is a comment
""",
"""
<span class="hljs-comment"># this is a comment</span>
""");
    }

    [Fact]
    public void Comment_InlineSemicolon()
    {
        AssertHighlighter("ini",
"""
name = alice ; trailing
""",
"""
<span class="hljs-attr">name</span> = alice ; trailing
""");
    }

    [Fact]
    public void Comment_InlineHash()
    {
        AssertHighlighter("ini",
"""
name = alice # trailing
""",
"""
<span class="hljs-attr">name</span> = alice # trailing
""");
    }

    [Fact]
    public void Comment_AfterSection()
    {
        AssertHighlighter("ini",
"""
[section] ; this section
""",
"""
<span class="hljs-section">[section]</span> <span class="hljs-comment">; this section</span>
""");
    }

    [Fact]
    public void Comment_AboveKey()
    {
        AssertHighlighter("ini",
"""
# user name
name = alice
""",
"""
<span class="hljs-comment"># user name</span>
<span class="hljs-attr">name</span> = alice
""");
    }

    [Fact]
    public void Comment_BlockOfComments()
    {
        AssertHighlighter("ini",
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
    public void Composite_AppConfig()
    {
        AssertHighlighter("ini",
"""
[server]
host = localhost
port = 8080

[database]
url = "postgres://localhost/mydb"
pool_size = 10
enabled = true
""",
"""
<span class="hljs-section">[server]</span>
<span class="hljs-attr">host</span> = localhost
<span class="hljs-attr">port</span> = 8080

<span class="hljs-section">[database]</span>
<span class="hljs-attr">url</span> = &quot;postgres://localhost/mydb&quot;
<span class="hljs-attr">pool_size</span> = 10
<span class="hljs-attr">enabled</span> = true
""");
    }

    [Fact]
    public void Composite_GitConfig()
    {
        AssertHighlighter("ini",
"""
[user]
  name = Alice
  email = alice@example.com

[core]
  editor = vim
  autocrlf = input

[alias]
  st = status
  co = checkout
""",
"""
<span class="hljs-section">[user]</span>
  <span class="hljs-attr">name</span> = Alice
  <span class="hljs-attr">email</span> = alice@example.com

<span class="hljs-section">[core]</span>
  <span class="hljs-attr">editor</span> = vim
  <span class="hljs-attr">autocrlf</span> = input

<span class="hljs-section">[alias]</span>
  <span class="hljs-attr">st</span> = status
  <span class="hljs-attr">co</span> = checkout
""");
    }

    [Fact]
    public void Composite_TomlPackage()
    {
        AssertHighlighter("ini",
"""
name = "demo"
version = "1.0.0"
authors = ["Alice <alice@example.com>"]

[dependencies]
foo = "^1.0.0"
bar = { version = "2.0", optional = true }

[[bin]]
name = "demo"
path = "src/main.rs"
""",
"""
<span class="hljs-attr">name</span> = &quot;demo&quot;
<span class="hljs-attr">version</span> = &quot;1.0.0&quot;
<span class="hljs-attr">authors</span> = [&quot;Alice &lt;alice@example.com&gt;&quot;]

<span class="hljs-section">[dependencies]</span>
<span class="hljs-attr">foo</span> = &quot;^1.0.0&quot;
<span class="hljs-attr">bar</span> = { version = &quot;2.0&quot;, optional = true }

<span class="hljs-section">[[bin]]</span>
<span class="hljs-attr">name</span> = &quot;demo&quot;
<span class="hljs-attr">path</span> = &quot;src/main.rs&quot;
""");
    }

    [Fact]
    public void Composite_EditorConfig()
    {
        AssertHighlighter("ini",
"""
root = true

[*]
indent_style = space
indent_size = 4
end_of_line = lf
insert_final_newline = true

[*.{js,ts}]
indent_size = 2
""",
"""
<span class="hljs-attr">root</span> = true

<span class="hljs-section">[*]</span>
<span class="hljs-attr">indent_style</span> = space
<span class="hljs-attr">indent_size</span> = 4
<span class="hljs-attr">end_of_line</span> = lf
<span class="hljs-attr">insert_final_newline</span> = true

<span class="hljs-section">[*.{js,ts}]</span>
<span class="hljs-attr">indent_size</span> = 2
""");
    }

    [Fact]
    public void Composite_Systemd()
    {
        AssertHighlighter("ini",
"""
[Unit]
Description=My App
After=network.target

[Service]
ExecStart=/usr/bin/myapp
Restart=on-failure

[Install]
WantedBy=multi-user.target
""",
"""
<span class="hljs-section">[Unit]</span>
<span class="hljs-attr">Description</span>=My App
<span class="hljs-attr">After</span>=network.target

<span class="hljs-section">[Service]</span>
<span class="hljs-attr">ExecStart</span>=/usr/bin/myapp
<span class="hljs-attr">Restart</span>=on-failure

<span class="hljs-section">[Install]</span>
<span class="hljs-attr">WantedBy</span>=multi-user.target
""");
    }

    [Fact]
    public void SpecialEdge_Empty()
    {
        AssertHighlighter("ini",
"""

""",
"""

""");
    }

    [Fact]
    public void SpecialEdge_OnlyWhitespace()
    {
        AssertHighlighter("ini",
"""


""",
"""


""");
    }

    [Fact]
    public void SpecialEdge_OnlyComment()
    {
        AssertHighlighter("ini",
"""
; just a comment
""",
"""
<span class="hljs-comment">; just a comment</span>
""");
    }

    [Fact]
    public void SpecialEdge_BlankLineBetween()
    {
        AssertHighlighter("ini",
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
    public void SpecialEdge_NoSectionHeader()
    {
        AssertHighlighter("ini",
"""
global_key = value
""",
"""
<span class="hljs-attr">global_key</span> = value
""");
    }

    [Fact]
    public void SpecialEdge_TrailingNewline()
    {
        AssertHighlighter("ini",
"""
a = 1

""",
"""
<span class="hljs-attr">a</span> = 1

""");
    }
}
