namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class PropertiesHighlighterTests
{
    [Fact]
    public void EqualsSeparator()
    {
        AssertHighlighter("properties",
"""
key=value
key2 = value with spaces
key3=  leading spaces
""",
"""
<span class="hljs-attr">key</span>=<span class="hljs-string">value</span>
<span class="hljs-attr">key2</span> = <span class="hljs-string">value with spaces</span>
<span class="hljs-attr">key3</span>=  <span class="hljs-string">leading spaces</span>
""");
    }

    [Fact]
    public void ColonSeparator()
    {
        AssertHighlighter("properties",
"""
key:value
key2 : value
website: https://example.com/path?q=1
""",
"""
<span class="hljs-attr">key</span>:<span class="hljs-string">value</span>
<span class="hljs-attr">key2</span> : <span class="hljs-string">value</span>
<span class="hljs-attr">website</span>: <span class="hljs-string">https://example.com/path?q=1</span>
""");
    }

    [Fact]
    public void WhitespaceSeparator()
    {
        AssertHighlighter("properties",
"""
key value
key2    value with spaces
key3	value
""",
"""
<span class="hljs-attr">key</span> <span class="hljs-string">value</span>
<span class="hljs-attr">key2</span>    <span class="hljs-string">value with spaces</span>
<span class="hljs-attr">key3</span>	<span class="hljs-string">value</span>
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("properties",
"""
# hash comment
! bang comment
  # indented comment
key = value # not a comment
""",
"""
<span class="hljs-comment"># hash comment</span>
<span class="hljs-comment">! bang comment</span>
<span class="hljs-comment">  # indented comment</span>
<span class="hljs-attr">key</span> = <span class="hljs-string">value # not a comment</span>
""");
    }

    [Fact]
    public void EscapedSeparators()
    {
        AssertHighlighter("properties",
"""
key\=with\=equals = value
key\:colon:value
key\ with\ spaces = value
path=c:\\temp\\file
""",
"""
<span class="hljs-attr">key\=with\=equals</span> = <span class="hljs-string">value</span>
<span class="hljs-attr">key\:colon</span>:<span class="hljs-string">value</span>
<span class="hljs-attr">key\ with\ spaces</span> = <span class="hljs-string">value</span>
<span class="hljs-attr">path</span>=<span class="hljs-string">c:\\temp\\file</span>
""");
    }

    [Fact]
    public void EscapeEdgeCases()
    {
        AssertHighlighter("properties",
"""
a\\\= b
\\\\key=v
k\\ey\ =v
\=start=1
\\
\ lead = x
x\
y=1
ab\\\\\\c d
\a\b\c

""",
"""
<span class="hljs-attr">a\\\=</span> <span class="hljs-string">b</span>
<span class="hljs-attr">\\\\key</span>=<span class="hljs-string">v</span>
<span class="hljs-attr">k\\ey\ </span>=<span class="hljs-string">v</span>
<span class="hljs-attr">\=start</span>=<span class="hljs-string">1</span>
<span class="hljs-attr">\\</span>
<span class="hljs-attr">\ lead</span> = <span class="hljs-string">x</span>
x\
<span class="hljs-attr">y</span>=<span class="hljs-string">1</span>
<span class="hljs-attr">ab\\\\\\c</span> <span class="hljs-string">d</span>
<span class="hljs-attr">\a\b\c</span>

""");
    }

    [Fact]
    public void LineContinuation()
    {
        AssertHighlighter("properties",
"""
fruits = apple, banana, pear, \
         cantaloupe, watermelon, \
         kiwi, mango
next = 1
""",
"""
<span class="hljs-attr">fruits</span> = <span class="hljs-string">apple, banana, pear, \
         cantaloupe, watermelon, \
         kiwi, mango</span>
<span class="hljs-attr">next</span> = <span class="hljs-string">1</span>
""");
    }

    [Fact]
    public void EmptyValues()
    {
        AssertHighlighter("properties",
"""
empty=
empty2 =
emptykey
""",
"""
<span class="hljs-attr">empty</span>=<span class="hljs-string"></span>
<span class="hljs-attr">empty2</span> =<span class="hljs-string"></span>
<span class="hljs-attr">emptykey</span>
""");
    }

    [Fact]
    public void KeyFollowedBySpaces()
    {
        AssertHighlighter("properties",
"keyonly   \nother",
"""
<span class="hljs-attr">keyonly</span>   <span class="hljs-string"></span>
<span class="hljs-attr">other</span>
""");
    }

    [Fact]
    public void UnicodeEscapes()
    {
        AssertHighlighter("properties",
"""
greeting = \u0048ello
key\u0020name = x
""",
"""
<span class="hljs-attr">greeting</span> = <span class="hljs-string">\u0048ello</span>
<span class="hljs-attr">key\u0020name</span> = <span class="hljs-string">x</span>
""");
    }

    [Fact]
    public void BlankLinesBeforeComments()
    {
        AssertHighlighter("properties",
"""
a=1


b=2

# c


! d
""",
"""
<span class="hljs-attr">a</span>=<span class="hljs-string">1</span>


<span class="hljs-attr">b</span>=<span class="hljs-string">2</span>

<span class="hljs-comment"># c</span>


<span class="hljs-comment">! d</span>
""");
    }

    [Fact]
    public void DottedKeys()
    {
        AssertHighlighter("properties",
"""
spring.datasource.url=jdbc:mysql://localhost:3306/db
spring.datasource.username=root
server.port=8080
logging.level.org.springframework=DEBUG
""",
"""
<span class="hljs-attr">spring.datasource.url</span>=<span class="hljs-string">jdbc:mysql://localhost:3306/db</span>
<span class="hljs-attr">spring.datasource.username</span>=<span class="hljs-string">root</span>
<span class="hljs-attr">server.port</span>=<span class="hljs-string">8080</span>
<span class="hljs-attr">logging.level.org.springframework</span>=<span class="hljs-string">DEBUG</span>
""");
    }

    [Fact]
    public void SeparatorsInValue()
    {
        AssertHighlighter("properties",
"""
url = http://a=b:c
expr = a = b
""",
"""
<span class="hljs-attr">url</span> = <span class="hljs-string">http://a=b:c</span>
<span class="hljs-attr">expr</span> = <span class="hljs-string">a = b</span>
""");
    }

    [Fact]
    public void IndentedKeys()
    {
        AssertHighlighter("properties",
"""
   indented.key = value
	key2=value
""",
"""
   <span class="hljs-attr">indented.key</span> = <span class="hljs-string">value</span>
	<span class="hljs-attr">key2</span>=<span class="hljs-string">value</span>
""");
    }

    [Fact]
    public void EscapedBackslashAtEndOfValue()
    {
        AssertHighlighter("properties",
"""
path = c:\\
next = value
""",
"""
<span class="hljs-attr">path</span> = <span class="hljs-string">c:\\</span>
<span class="hljs-attr">next</span> = <span class="hljs-string">value</span>
""");
    }

    [Fact]
    public void MessageBundle()
    {
        AssertHighlighter("properties",
"""
button.ok=OK
message.welcome=Welcome, {0}!
error.required={0} is required.
""",
"""
<span class="hljs-attr">button.ok</span>=<span class="hljs-string">OK</span>
<span class="hljs-attr">message.welcome</span>=<span class="hljs-string">Welcome, {0}!</span>
<span class="hljs-attr">error.required</span>=<span class="hljs-string">{0} is required.</span>
""");
    }

    [Fact]
    public void SpecialCharactersInKey()
    {
        AssertHighlighter("properties",
"""
key-with-dash=1
key_with_underscore=2
key[0]=3
key.#hash=4
key!bang=5
""",
"""
<span class="hljs-attr">key-with-dash</span>=<span class="hljs-string">1</span>
<span class="hljs-attr">key_with_underscore</span>=<span class="hljs-string">2</span>
<span class="hljs-attr">key[0]</span>=<span class="hljs-string">3</span>
<span class="hljs-attr">key.#hash</span>=<span class="hljs-string">4</span>
<span class="hljs-attr">key!bang</span>=<span class="hljs-string">5</span>
""");
    }

    [Fact]
    public void MissingKey()
    {
        AssertHighlighter("properties",
"""
=value
:value
""",
"""
=<span class="hljs-attr">value</span>
:<span class="hljs-attr">value</span>
""");
    }

    [Fact]
    public void CommentCharactersInValue()
    {
        AssertHighlighter("properties",
"""
color = #ff0000
bang = !important
""",
"""
<span class="hljs-attr">color</span> = <span class="hljs-string">#ff0000</span>
<span class="hljs-attr">bang</span> = <span class="hljs-string">!important</span>
""");
    }

    [Fact]
    public void NonAsciiKeys()
    {
        AssertHighlighter("properties",
"""
clé=valeur
日本=語
""",
"""
<span class="hljs-attr">clé</span>=<span class="hljs-string">valeur</span>
<span class="hljs-attr">日本</span>=<span class="hljs-string">語</span>
""");
    }

    [Fact]
    public void CrLf()
    {
        AssertHighlighter("properties",
"a=1\r\nb = 2\r\n# c\r\n",
"<span class=\"hljs-attr\">a</span>=<span class=\"hljs-string\">1</span>\r\n<span class=\"hljs-attr\">b</span> = <span class=\"hljs-string\">2</span>\r\n<span class=\"hljs-comment\"># c</span>\r\n");
    }

    [Fact]
    public void ContinuationAtEndOfInput()
    {
        AssertHighlighter("properties",
"""
a = b\
""",
"""
<span class="hljs-attr">a</span> = <span class="hljs-string">b\</span>
""");
    }

    [Fact]
    public void RealisticFile()
    {
        AssertHighlighter("properties",
"""
# Application settings
app.name=My Application
app.version=1.2.3
app.description=A long description that \
    spans multiple lines.

! Database
db.url = jdbc:postgresql://localhost:5432/app
db.user = admin
db.password =

# Paths
log.dir = /var/log/app
windows.path = C:\\Program Files\\App
""",
"""
<span class="hljs-comment"># Application settings</span>
<span class="hljs-attr">app.name</span>=<span class="hljs-string">My Application</span>
<span class="hljs-attr">app.version</span>=<span class="hljs-string">1.2.3</span>
<span class="hljs-attr">app.description</span>=<span class="hljs-string">A long description that \
    spans multiple lines.</span>

<span class="hljs-comment">! Database</span>
<span class="hljs-attr">db.url</span> = <span class="hljs-string">jdbc:postgresql://localhost:5432/app</span>
<span class="hljs-attr">db.user</span> = <span class="hljs-string">admin</span>
<span class="hljs-attr">db.password</span> =<span class="hljs-string"></span>

<span class="hljs-comment"># Paths</span>
<span class="hljs-attr">log.dir</span> = <span class="hljs-string">/var/log/app</span>
<span class="hljs-attr">windows.path</span> = <span class="hljs-string">C:\\Program Files\\App</span>
""");
    }
}
