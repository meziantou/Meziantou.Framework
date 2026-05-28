namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class BnfHighlighterTests
{

    [Fact]
    public void Production_Simple()
    {
        AssertHighlighter("bnf",
"""
<digit> ::= 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9
""",
"""
<span class="hljs-attribute">&lt;digit&gt;</span> ::= 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9
""");
    }

    [Fact]
    public void Production_TwoSymbols()
    {
        AssertHighlighter("bnf",
"""
<letter> ::= <upper> | <lower>
""",
"""
<span class="hljs-attribute">&lt;letter&gt;</span> ::= &lt;upper&gt; | &lt;lower&gt;
""");
    }

    [Fact]
    public void Production_TerminalString()
    {
        AssertHighlighter("bnf",
"""
<sign>  ::= "+" | "-"
""",
"""
<span class="hljs-attribute">&lt;sign&gt;</span>  ::= <span class="hljs-string">&quot;+&quot;</span> | <span class="hljs-string">&quot;-&quot;</span>
""");
    }

    [Fact]
    public void Production_SingleQuoted()
    {
        AssertHighlighter("bnf",
"""
<sign>  ::= '+' | '-'
""",
"""
<span class="hljs-attribute">&lt;sign&gt;</span>  ::= <span class="hljs-string">&#x27;+&#x27;</span> | <span class="hljs-string">&#x27;-&#x27;</span>
""");
    }

    [Fact]
    public void Production_EmptyRhs()
    {
        AssertHighlighter("bnf",
"""
<opt-sign> ::= <sign> |
""",
"""
<span class="hljs-attribute">&lt;opt-sign&gt;</span> ::= &lt;sign&gt; |
""");
    }

    [Fact]
    public void Production_Multiline()
    {
        AssertHighlighter("bnf",
"""
<expr> ::= <term>
        | <expr> "+" <term>
        | <expr> "-" <term>
""",
"""
<span class="hljs-attribute">&lt;expr&gt;</span> ::= &lt;term&gt;
        | <span class="hljs-attribute">&lt;expr&gt;</span> &quot;+&quot; <span class="hljs-attribute">&lt;term&gt;</span>
        | <span class="hljs-attribute">&lt;expr&gt;</span> &quot;-&quot; <span class="hljs-attribute">&lt;term&gt;</span>
""");
    }

    [Fact]
    public void Production_WithComment()
    {
        AssertHighlighter("bnf",
"""
<digit> ::= 0 | 1 | 2 | 3   ; one of ten symbols
""",
"""
<span class="hljs-attribute">&lt;digit&gt;</span> ::= 0 | 1 | 2 | 3   ; one of ten symbols
""");
    }

    [Fact]
    public void Production_EmptyRule()
    {
        AssertHighlighter("bnf",
"""
<epsilon> ::=
""",
"""
<span class="hljs-attribute">&lt;epsilon&gt;</span> ::=
""");
    }

    [Fact]
    public void Alternation_Pair()
    {
        AssertHighlighter("bnf",
"""
<bool> ::= "true" | "false"
""",
"""
<span class="hljs-attribute">&lt;bool&gt;</span> ::= <span class="hljs-string">&quot;true&quot;</span> | <span class="hljs-string">&quot;false&quot;</span>
""");
    }

    [Fact]
    public void Alternation_Many()
    {
        AssertHighlighter("bnf",
"""
<weekday> ::= "Mon" | "Tue" | "Wed" | "Thu" | "Fri"
""",
"""
<span class="hljs-attribute">&lt;weekday&gt;</span> ::= <span class="hljs-string">&quot;Mon&quot;</span> | <span class="hljs-string">&quot;Tue&quot;</span> | <span class="hljs-string">&quot;Wed&quot;</span> | <span class="hljs-string">&quot;Thu&quot;</span> | <span class="hljs-string">&quot;Fri&quot;</span>
""");
    }

    [Fact]
    public void Alternation_AcrossLines()
    {
        AssertHighlighter("bnf",
"""
<color>
  ::= "red"
    | "green"
    | "blue"
""",
"""
<span class="hljs-attribute">&lt;color&gt;</span>
  ::= <span class="hljs-string">&quot;red&quot;</span>
    | &quot;green&quot;
    | &quot;blue&quot;
""");
    }

    [Fact]
    public void RecursiveRules_LeftRecursive()
    {
        AssertHighlighter("bnf",
"""
<list> ::= <list> "," <item> | <item>
""",
"""
<span class="hljs-attribute">&lt;list&gt;</span> ::= &lt;list&gt; <span class="hljs-string">&quot;,&quot;</span> &lt;item&gt; | &lt;item&gt;
""");
    }

    [Fact]
    public void RecursiveRules_RightRecursive()
    {
        AssertHighlighter("bnf",
"""
<list> ::= <item> "," <list> | <item>
""",
"""
<span class="hljs-attribute">&lt;list&gt;</span> ::= &lt;item&gt; <span class="hljs-string">&quot;,&quot;</span> &lt;list&gt; | &lt;item&gt;
""");
    }

    [Fact]
    public void RecursiveRules_NestedExpr()
    {
        AssertHighlighter("bnf",
"""
<expr>   ::= <expr> "+" <expr> | <expr> "*" <expr> | "(" <expr> ")" | <number>
""",
"""
<span class="hljs-attribute">&lt;expr&gt;</span>   ::= &lt;expr&gt; <span class="hljs-string">&quot;+&quot;</span> &lt;expr&gt; | &lt;expr&gt; <span class="hljs-string">&quot;*&quot;</span> &lt;expr&gt; | <span class="hljs-string">&quot;(&quot;</span> &lt;expr&gt; <span class="hljs-string">&quot;)&quot;</span> | &lt;number&gt;
""");
    }

    [Fact]
    public void EbnfOptional_Brackets()
    {
        AssertHighlighter("bnf",
"""
sign = [ "+" | "-" ] ;
""",
"""
sign = [ &quot;+&quot; | &quot;-&quot; ] ;
""");
    }

    [Fact]
    public void EbnfOptional_NestedOpt()
    {
        AssertHighlighter("bnf",
"""
integer = [ sign ] digit { digit } ;
""",
"""
integer = [ sign ] digit { digit } ;
""");
    }

    [Fact]
    public void EbnfRepetition_ZeroOrMore()
    {
        AssertHighlighter("bnf",
"""
digits = digit { digit } ;
""",
"""
digits = digit { digit } ;
""");
    }

    [Fact]
    public void EbnfRepetition_NestedRep()
    {
        AssertHighlighter("bnf",
"""
string = '"' { character } '"' ;
""",
"""
string = &#x27;&quot;&#x27; { character } &#x27;&quot;&#x27; ;
""");
    }

    [Fact]
    public void EbnfGrouping_Parens()
    {
        AssertHighlighter("bnf",
"""
word = ( letter | digit ) { letter | digit } ;
""",
"""
word = ( letter | digit ) { letter | digit } ;
""");
    }

    [Fact]
    public void EbnfDefinition_EqualsAndSemicolon()
    {
        AssertHighlighter("bnf",
"""
identifier = letter , { letter | digit } ;
""",
"""
identifier = letter , { letter | digit } ;
""");
    }

    [Fact]
    public void EbnfDefinition_Concat()
    {
        AssertHighlighter("bnf",
"""
date = year , "-" , month , "-" , day ;
""",
"""
date = year , &quot;-&quot; , month , &quot;-&quot; , day ;
""");
    }

    [Fact]
    public void EbnfDefinition_TerminalChar()
    {
        AssertHighlighter("bnf",
"""
newline = ? ASCII character 10 ? ;
""",
"""
newline = ? ASCII character 10 ? ;
""");
    }

    [Fact]
    public void EbnfDefinition_ExceptOperator()
    {
        AssertHighlighter("bnf",
"""
safe = character - '"' ;
""",
"""
safe = character - &#x27;&quot;&#x27; ;
""");
    }

    [Fact]
    public void AbnfStyle_AbnfRule()
    {
        AssertHighlighter("bnf",
"""
CRLF = %x0D %x0A
""",
"""
CRLF = %x0D %x0A
""");
    }

    [Fact]
    public void AbnfStyle_AbnfRange()
    {
        AssertHighlighter("bnf",
"""
DIGIT = %x30-39
""",
"""
DIGIT = %x30-39
""");
    }

    [Fact]
    public void AbnfStyle_AbnfStringInsens()
    {
        AssertHighlighter("bnf",
"""
protocol = "HTTP" "/" version
""",
"""
protocol = &quot;HTTP&quot; &quot;/&quot; version
""");
    }

    [Fact]
    public void AbnfStyle_AbnfRepeat()
    {
        AssertHighlighter("bnf",
"""
time = 2DIGIT ":" 2DIGIT
""",
"""
time = 2DIGIT &quot;:&quot; 2DIGIT
""");
    }

    [Fact]
    public void AbnfStyle_AbnfRepeatRange()
    {
        AssertHighlighter("bnf",
"""
word = 1*ALPHA
""",
"""
word = 1*ALPHA
""");
    }

    [Fact]
    public void AbnfStyle_AbnfOptional()
    {
        AssertHighlighter("bnf",
"""
header = field-name ":" [ field-value ]
""",
"""
header = field-name &quot;:&quot; [ field-value ]
""");
    }

    [Fact]
    public void CharacterClass_RangeUpper()
    {
        AssertHighlighter("bnf",
"""
<upper> ::= "A" | "B" | "C" | "D" | "E" | "F"
""",
"""
<span class="hljs-attribute">&lt;upper&gt;</span> ::= <span class="hljs-string">&quot;A&quot;</span> | <span class="hljs-string">&quot;B&quot;</span> | <span class="hljs-string">&quot;C&quot;</span> | <span class="hljs-string">&quot;D&quot;</span> | <span class="hljs-string">&quot;E&quot;</span> | <span class="hljs-string">&quot;F&quot;</span>
""");
    }

    [Fact]
    public void CharacterClass_RangeLower()
    {
        AssertHighlighter("bnf",
"""
<lower> ::= "a" | "b" | "c"
""",
"""
<span class="hljs-attribute">&lt;lower&gt;</span> ::= <span class="hljs-string">&quot;a&quot;</span> | <span class="hljs-string">&quot;b&quot;</span> | <span class="hljs-string">&quot;c&quot;</span>
""");
    }

    [Fact]
    public void CharacterClass_Whitespace()
    {
        AssertHighlighter("bnf",
"""
<ws> ::= " " | "\t" | "\n"
""",
"""
<span class="hljs-attribute">&lt;ws&gt;</span> ::= <span class="hljs-string">&quot; &quot;</span> | <span class="hljs-string">&quot;\t&quot;</span> | <span class="hljs-string">&quot;\n&quot;</span>
""");
    }

    [Fact]
    public void CharacterClass_AnyChar()
    {
        AssertHighlighter("bnf",
"""
<any> ::= ? any printable character ?
""",
"""
<span class="hljs-attribute">&lt;any&gt;</span> ::= ? any printable character ?
""");
    }

    [Fact]
    public void Comment_Semicolon()
    {
        AssertHighlighter("bnf",
"""
; this is a comment
""",
"""
; this is a comment
""");
    }

    [Fact]
    public void Comment_Trailing()
    {
        AssertHighlighter("bnf",
"""
<digit> ::= 0 | 1 | 2 | 3   ; values
""",
"""
<span class="hljs-attribute">&lt;digit&gt;</span> ::= 0 | 1 | 2 | 3   ; values
""");
    }

    [Fact]
    public void Comment_BlockHash()
    {
        AssertHighlighter("bnf",
"""
# block-style commentary
""",
"""
# block-style commentary
""");
    }

    [Fact]
    public void Comment_Bracketed()
    {
        AssertHighlighter("bnf",
"""
(* EBNF-style comment *)
""",
"""
(* EBNF-style comment *)
""");
    }

    [Fact]
    public void Composite_JsonNumber()
    {
        AssertHighlighter("bnf",
"""
<number> ::= [ "-" ] <int> [ <frac> ] [ <exp> ]
<int>    ::= "0" | <one-nine> { <digit> }
<frac>   ::= "." <digit> { <digit> }
<exp>    ::= ( "e" | "E" ) [ "+" | "-" ] <digit> { <digit> }
<digit>  ::= "0" | <one-nine>
<one-nine> ::= "1" | "2" | "3" | "4" | "5" | "6" | "7" | "8" | "9"
""",
"""
<span class="hljs-attribute">&lt;number&gt;</span> ::= [ <span class="hljs-string">&quot;-&quot;</span> ] &lt;int&gt; [ &lt;frac&gt; ] [ &lt;exp&gt; ]
<span class="hljs-attribute">&lt;int&gt;</span>    ::= <span class="hljs-string">&quot;0&quot;</span> | &lt;one-nine&gt; { &lt;digit&gt; }
<span class="hljs-attribute">&lt;frac&gt;</span>   ::= <span class="hljs-string">&quot;.&quot;</span> &lt;digit&gt; { &lt;digit&gt; }
<span class="hljs-attribute">&lt;exp&gt;</span>    ::= ( <span class="hljs-string">&quot;e&quot;</span> | <span class="hljs-string">&quot;E&quot;</span> ) [ <span class="hljs-string">&quot;+&quot;</span> | <span class="hljs-string">&quot;-&quot;</span> ] &lt;digit&gt; { &lt;digit&gt; }
<span class="hljs-attribute">&lt;digit&gt;</span>  ::= <span class="hljs-string">&quot;0&quot;</span> | &lt;one-nine&gt;
<span class="hljs-attribute">&lt;one-nine&gt;</span> ::= <span class="hljs-string">&quot;1&quot;</span> | <span class="hljs-string">&quot;2&quot;</span> | <span class="hljs-string">&quot;3&quot;</span> | <span class="hljs-string">&quot;4&quot;</span> | <span class="hljs-string">&quot;5&quot;</span> | <span class="hljs-string">&quot;6&quot;</span> | <span class="hljs-string">&quot;7&quot;</span> | <span class="hljs-string">&quot;8&quot;</span> | <span class="hljs-string">&quot;9&quot;</span>
""");
    }

    [Fact]
    public void Composite_ArithExpr()
    {
        AssertHighlighter("bnf",
"""
<expression> ::= <term> { ("+" | "-") <term> }
<term>       ::= <factor> { ("*" | "/") <factor> }
<factor>     ::= <number> | "(" <expression> ")" | <identifier>
<number>     ::= <digit> { <digit> }
<identifier> ::= <letter> { <letter> | <digit> }
""",
"""
<span class="hljs-attribute">&lt;expression&gt;</span> ::= &lt;term&gt; { (<span class="hljs-string">&quot;+&quot;</span> | <span class="hljs-string">&quot;-&quot;</span>) &lt;term&gt; }
<span class="hljs-attribute">&lt;term&gt;</span>       ::= &lt;factor&gt; { (<span class="hljs-string">&quot;*&quot;</span> | <span class="hljs-string">&quot;/&quot;</span>) &lt;factor&gt; }
<span class="hljs-attribute">&lt;factor&gt;</span>     ::= &lt;number&gt; | <span class="hljs-string">&quot;(&quot;</span> &lt;expression&gt; <span class="hljs-string">&quot;)&quot;</span> | &lt;identifier&gt;
<span class="hljs-attribute">&lt;number&gt;</span>     ::= &lt;digit&gt; { &lt;digit&gt; }
<span class="hljs-attribute">&lt;identifier&gt;</span> ::= &lt;letter&gt; { &lt;letter&gt; | &lt;digit&gt; }
""");
    }

    [Fact]
    public void Composite_UrlGrammarAbnf()
    {
        AssertHighlighter("bnf",
"""
URI         = scheme ":" hier-part [ "?" query ] [ "#" fragment ]
hier-part   = "//" authority path-abempty
            / path-absolute
            / path-rootless
            / path-empty
scheme      = ALPHA *( ALPHA / DIGIT / "+" / "-" / "." )
authority   = [ userinfo "@" ] host [ ":" port ]
""",
"""
URI         = scheme &quot;:&quot; hier-part [ &quot;?&quot; query ] [ &quot;#&quot; fragment ]
hier-part   = &quot;//&quot; authority path-abempty
            / path-absolute
            / path-rootless
            / path-empty
scheme      = ALPHA *( ALPHA / DIGIT / &quot;+&quot; / &quot;-&quot; / &quot;.&quot; )
authority   = [ userinfo &quot;@&quot; ] host [ &quot;:&quot; port ]
""");
    }

    [Fact]
    public void Composite_CsvGrammar()
    {
        AssertHighlighter("bnf",
"""
file       = [ header CRLF ] record *( CRLF record ) [ CRLF ]
header     = name *( COMMA name )
record     = field *( COMMA field )
name       = field
field      = ( escaped / non-escaped )
escaped    = DQUOTE *( TEXTDATA / COMMA / CR / LF / 2DQUOTE ) DQUOTE
non-escaped = *TEXTDATA
""",
"""
file       = [ header CRLF ] record *( CRLF record ) [ CRLF ]
header     = name *( COMMA name )
record     = field *( COMMA field )
name       = field
field      = ( escaped / non-escaped )
escaped    = DQUOTE *( TEXTDATA / COMMA / CR / LF / 2DQUOTE ) DQUOTE
non-escaped = *TEXTDATA
""");
    }

    [Fact]
    public void SpecialEdge_Empty()
    {
        AssertHighlighter("bnf",
"""

""",
"""

""");
    }

    [Fact]
    public void SpecialEdge_OnlyComment()
    {
        AssertHighlighter("bnf",
"""
; only a comment
""",
"""
; only a comment
""");
    }

    [Fact]
    public void SpecialEdge_TrailingNewline()
    {
        AssertHighlighter("bnf",
"""
<a> ::= <b>

""",
"""
<span class="hljs-attribute">&lt;a&gt;</span> ::= &lt;b&gt;

""");
    }
}
