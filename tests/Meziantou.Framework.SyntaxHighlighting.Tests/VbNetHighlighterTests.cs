namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class VbNetHighlighterTests
{

    [Fact]
    public void Declaration_DimSimple()
    {
        AssertHighlighter("vbnet",
"""
Dim x = 42
""",
"""
<span class="hljs-keyword">Dim</span> x = <span class="hljs-number">42</span>
""");
    }

    [Fact]
    public void Declaration_DimTyped()
    {
        AssertHighlighter("vbnet",
"""
Dim x As Integer = 42
""",
"""
<span class="hljs-keyword">Dim</span> x <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span> = <span class="hljs-number">42</span>
""");
    }

    [Fact]
    public void Declaration_DimMultiple()
    {
        AssertHighlighter("vbnet",
"""
Dim a, b, c As Integer
""",
"""
<span class="hljs-keyword">Dim</span> a, b, c <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span>
""");
    }

    [Fact]
    public void Declaration_DimAsNew()
    {
        AssertHighlighter("vbnet",
"""
Dim list As New List(Of Integer)
""",
"""
<span class="hljs-keyword">Dim</span> list <span class="hljs-keyword">As</span> <span class="hljs-built_in">New</span> List(<span class="hljs-keyword">Of</span> <span class="hljs-type">Integer</span>)
""");
    }

    [Fact]
    public void Declaration_Const()
    {
        AssertHighlighter("vbnet",
"""
Const Pi As Double = 3.14159
""",
"""
<span class="hljs-keyword">Const</span> Pi <span class="hljs-keyword">As</span> <span class="hljs-type">Double</span> = <span class="hljs-number">3.14159</span>
""");
    }

    [Fact]
    public void Declaration_PrivateField()
    {
        AssertHighlighter("vbnet",
"""
Private _count As Integer
""",
"""
<span class="hljs-keyword">Private</span> _count <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span>
""");
    }

    [Fact]
    public void Declaration_PublicField()
    {
        AssertHighlighter("vbnet",
"""
Public Name As String = "alice"
""",
"""
<span class="hljs-keyword">Public</span> Name <span class="hljs-keyword">As</span> <span class="hljs-type">String</span> = <span class="hljs-string">&quot;alice&quot;</span>
""");
    }

    [Fact]
    public void Declaration_ReadOnlyField()
    {
        AssertHighlighter("vbnet",
"""
Public ReadOnly Id As Guid = Guid.NewGuid()
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">ReadOnly</span> Id <span class="hljs-keyword">As</span> Guid = Guid.NewGuid()
""");
    }

    [Fact]
    public void Declaration_StaticField()
    {
        AssertHighlighter("vbnet",
"""
Public Shared Counter As Integer
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Shared</span> Counter <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span>
""");
    }

    [Fact]
    public void Declaration_WithEventsField()
    {
        AssertHighlighter("vbnet",
"""
Private WithEvents _timer As Timer
""",
"""
<span class="hljs-keyword">Private</span> <span class="hljs-keyword">WithEvents</span> _timer <span class="hljs-keyword">As</span> Timer
""");
    }

    [Fact]
    public void Declaration_Static()
    {
        AssertHighlighter("vbnet",
"""
Static count As Integer = 0
""",
"""
<span class="hljs-keyword">Static</span> count <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span> = <span class="hljs-number">0</span>
""");
    }

    [Fact]
    public void PrimitiveType_Integer()
    {
        AssertHighlighter("vbnet",
"""
Dim x As Integer = 0
""",
"""
<span class="hljs-keyword">Dim</span> x <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span> = <span class="hljs-number">0</span>
""");
    }

    [Fact]
    public void PrimitiveType_Long()
    {
        AssertHighlighter("vbnet",
"""
Dim x As Long = 0L
""",
"""
<span class="hljs-keyword">Dim</span> x <span class="hljs-keyword">As</span> <span class="hljs-type">Long</span> = <span class="hljs-number">0L</span>
""");
    }

    [Fact]
    public void PrimitiveType_Short()
    {
        AssertHighlighter("vbnet",
"""
Dim x As Short = 0S
""",
"""
<span class="hljs-keyword">Dim</span> x <span class="hljs-keyword">As</span> <span class="hljs-type">Short</span> = <span class="hljs-number">0S</span>
""");
    }

    [Fact]
    public void PrimitiveType_Byte()
    {
        AssertHighlighter("vbnet",
"""
Dim x As Byte = 0
""",
"""
<span class="hljs-keyword">Dim</span> x <span class="hljs-keyword">As</span> <span class="hljs-type">Byte</span> = <span class="hljs-number">0</span>
""");
    }

    [Fact]
    public void PrimitiveType_SByte()
    {
        AssertHighlighter("vbnet",
"""
Dim x As SByte = 0
""",
"""
<span class="hljs-keyword">Dim</span> x <span class="hljs-keyword">As</span> <span class="hljs-type">SByte</span> = <span class="hljs-number">0</span>
""");
    }

    [Fact]
    public void PrimitiveType_UInteger()
    {
        AssertHighlighter("vbnet",
"""
Dim x As UInteger = 0UI
""",
"""
<span class="hljs-keyword">Dim</span> x <span class="hljs-keyword">As</span> <span class="hljs-type">UInteger</span> = <span class="hljs-number">0UI</span>
""");
    }

    [Fact]
    public void PrimitiveType_ULong()
    {
        AssertHighlighter("vbnet",
"""
Dim x As ULong = 0UL
""",
"""
<span class="hljs-keyword">Dim</span> x <span class="hljs-keyword">As</span> <span class="hljs-type">ULong</span> = <span class="hljs-number">0UL</span>
""");
    }

    [Fact]
    public void PrimitiveType_UShort()
    {
        AssertHighlighter("vbnet",
"""
Dim x As UShort = 0US
""",
"""
<span class="hljs-keyword">Dim</span> x <span class="hljs-keyword">As</span> <span class="hljs-type">UShort</span> = <span class="hljs-number">0US</span>
""");
    }

    [Fact]
    public void PrimitiveType_Single()
    {
        AssertHighlighter("vbnet",
"""
Dim x As Single = 1.5F
""",
"""
<span class="hljs-keyword">Dim</span> x <span class="hljs-keyword">As</span> <span class="hljs-type">Single</span> = <span class="hljs-number">1.5F</span>
""");
    }

    [Fact]
    public void PrimitiveType_Double()
    {
        AssertHighlighter("vbnet",
"""
Dim x As Double = 3.14R
""",
"""
<span class="hljs-keyword">Dim</span> x <span class="hljs-keyword">As</span> <span class="hljs-type">Double</span> = <span class="hljs-number">3.14R</span>
""");
    }

    [Fact]
    public void PrimitiveType_Decimal()
    {
        AssertHighlighter("vbnet",
"""
Dim x As Decimal = 9.99D
""",
"""
<span class="hljs-keyword">Dim</span> x <span class="hljs-keyword">As</span> <span class="hljs-type">Decimal</span> = <span class="hljs-number">9.99D</span>
""");
    }

    [Fact]
    public void PrimitiveType_Boolean()
    {
        AssertHighlighter("vbnet",
"""
Dim x As Boolean = True
""",
"""
<span class="hljs-keyword">Dim</span> x <span class="hljs-keyword">As</span> <span class="hljs-type">Boolean</span> = <span class="hljs-literal">True</span>
""");
    }

    [Fact]
    public void PrimitiveType_Char()
    {
        AssertHighlighter("vbnet",
"""
Dim x As Char = "A"c
""",
"""
<span class="hljs-keyword">Dim</span> x <span class="hljs-keyword">As</span> <span class="hljs-type">Char</span> = <span class="hljs-string">&quot;A&quot;c</span>
""");
    }

    [Fact]
    public void PrimitiveType_String()
    {
        AssertHighlighter("vbnet",
"""
Dim x As String = "hello"
""",
"""
<span class="hljs-keyword">Dim</span> x <span class="hljs-keyword">As</span> <span class="hljs-type">String</span> = <span class="hljs-string">&quot;hello&quot;</span>
""");
    }

    [Fact]
    public void PrimitiveType_Object()
    {
        AssertHighlighter("vbnet",
"""
Dim x As Object = Nothing
""",
"""
<span class="hljs-keyword">Dim</span> x <span class="hljs-keyword">As</span> <span class="hljs-type">Object</span> = <span class="hljs-literal">Nothing</span>
""");
    }

    [Fact]
    public void PrimitiveType_Date()
    {
        AssertHighlighter("vbnet",
"""
Dim today As Date = #2026-05-26#
""",
"""
<span class="hljs-keyword">Dim</span> today <span class="hljs-keyword">As</span> <span class="hljs-type">Date</span> = <span class="hljs-literal">#2026-05-26#</span>
""");
    }

    [Fact]
    public void PrimitiveType_DateTime()
    {
        AssertHighlighter("vbnet",
"""
Dim now As DateTime = #2026-05-26 10:30:00#
""",
"""
<span class="hljs-keyword">Dim</span> now <span class="hljs-keyword">As</span> DateTime = <span class="hljs-literal">#2026-05-26 10:30:00#</span>
""");
    }

    [Fact]
    public void Number_Integer()
    {
        AssertHighlighter("vbnet",
"""
Dim n = 42
""",
"""
<span class="hljs-keyword">Dim</span> n = <span class="hljs-number">42</span>
""");
    }

    [Fact]
    public void Number_Negative()
    {
        AssertHighlighter("vbnet",
"""
Dim n = -42
""",
"""
<span class="hljs-keyword">Dim</span> n = -<span class="hljs-number">42</span>
""");
    }

    [Fact]
    public void Number_Hex()
    {
        AssertHighlighter("vbnet",
"""
Dim n = &HDEADBEEF
""",
"""
<span class="hljs-keyword">Dim</span> n = <span class="hljs-number">&amp;HDEADBEEF</span>
""");
    }

    [Fact]
    public void Number_Octal()
    {
        AssertHighlighter("vbnet",
"""
Dim n = &O755
""",
"""
<span class="hljs-keyword">Dim</span> n = <span class="hljs-number">&amp;O755</span>
""");
    }

    [Fact]
    public void Number_Binary()
    {
        AssertHighlighter("vbnet",
"""
Dim n = &B1010_1100
""",
"""
<span class="hljs-keyword">Dim</span> n = <span class="hljs-number">&amp;B1010_1100</span>
""");
    }

    [Fact]
    public void Number_DigitSeparator()
    {
        AssertHighlighter("vbnet",
"""
Dim n = 1_000_000
""",
"""
<span class="hljs-keyword">Dim</span> n = <span class="hljs-number">1_000_000</span>
""");
    }

    [Fact]
    public void Number_Exponent()
    {
        AssertHighlighter("vbnet",
"""
Dim n = 1.5E10
""",
"""
<span class="hljs-keyword">Dim</span> n = <span class="hljs-number">1.5E10</span>
""");
    }

    [Fact]
    public void Number_LongSuffix()
    {
        AssertHighlighter("vbnet",
"""
Dim n = 42L
""",
"""
<span class="hljs-keyword">Dim</span> n = <span class="hljs-number">42L</span>
""");
    }

    [Fact]
    public void Number_SingleSuffix()
    {
        AssertHighlighter("vbnet",
"""
Dim n = 3.14F
""",
"""
<span class="hljs-keyword">Dim</span> n = <span class="hljs-number">3.14F</span>
""");
    }

    [Fact]
    public void Number_DecimalSuffix()
    {
        AssertHighlighter("vbnet",
"""
Dim n = 9.99D
""",
"""
<span class="hljs-keyword">Dim</span> n = <span class="hljs-number">9.99D</span>
""");
    }

    [Fact]
    public void String_Simple()
    {
        AssertHighlighter("vbnet",
"""
Dim s = "hello"
""",
"""
<span class="hljs-keyword">Dim</span> s = <span class="hljs-string">&quot;hello&quot;</span>
""");
    }

    [Fact]
    public void String_EscapeQuote()
    {
        AssertHighlighter("vbnet",
"""
Dim s = "She said ""hi""."
""",
"""
<span class="hljs-keyword">Dim</span> s = <span class="hljs-string">&quot;She said &quot;&quot;hi&quot;&quot;.&quot;</span>
""");
    }

    [Fact]
    public void String_Empty()
    {
        AssertHighlighter("vbnet",
"""
Dim s = ""
""",
"""
<span class="hljs-keyword">Dim</span> s = <span class="hljs-string">&quot;&quot;</span>
""");
    }

    [Fact]
    public void String_Concat()
    {
        AssertHighlighter("vbnet",
"""
Dim s = "Hello, " & name
""",
"""
<span class="hljs-keyword">Dim</span> s = <span class="hljs-string">&quot;Hello, &quot;</span> &amp; name
""");
    }

    [Fact]
    public void String_CharLiteral()
    {
        AssertHighlighter("vbnet",
"""
Dim c = "A"c
""",
"""
<span class="hljs-keyword">Dim</span> c = <span class="hljs-string">&quot;A&quot;c</span>
""");
    }

    [Fact]
    public void String_Interpolation()
    {
        AssertHighlighter("vbnet",
"""
Dim msg = $"Hello {name}"
""",
"""
<span class="hljs-keyword">Dim</span> msg = $<span class="hljs-string">&quot;Hello {name}&quot;</span>
""");
    }

    [Fact]
    public void String_InterpolationFormat()
    {
        AssertHighlighter("vbnet",
"""
Dim msg = $"Price: {price:C2}"
""",
"""
<span class="hljs-keyword">Dim</span> msg = $<span class="hljs-string">&quot;Price: {price:C2}&quot;</span>
""");
    }

    [Fact]
    public void String_InterpolationExpr()
    {
        AssertHighlighter("vbnet",
"""
Dim msg = $"Sum: {a + b}"
""",
"""
<span class="hljs-keyword">Dim</span> msg = $<span class="hljs-string">&quot;Sum: {a + b}&quot;</span>
""");
    }

    [Fact]
    public void String_MultiLineContinuation()
    {
        AssertHighlighter("vbnet",
"""
Dim s = "first " &
        "second " &
        "third"
""",
"""
<span class="hljs-keyword">Dim</span> s = <span class="hljs-string">&quot;first &quot;</span> &amp;
        <span class="hljs-string">&quot;second &quot;</span> &amp;
        <span class="hljs-string">&quot;third&quot;</span>
""");
    }

    [Fact]
    public void Operator_Arithmetic()
    {
        AssertHighlighter("vbnet",
"""
Dim r = (a + b) * c - d \ 2
""",
"""
<span class="hljs-keyword">Dim</span> r = (a + b) * c - d \ <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void Operator_Power()
    {
        AssertHighlighter("vbnet",
"""
Dim r = 2 ^ 10
""",
"""
<span class="hljs-keyword">Dim</span> r = <span class="hljs-number">2</span> ^ <span class="hljs-number">10</span>
""");
    }

    [Fact]
    public void Operator_Modulo()
    {
        AssertHighlighter("vbnet",
"""
Dim r = a Mod b
""",
"""
<span class="hljs-keyword">Dim</span> r = a <span class="hljs-built_in">Mod</span> b
""");
    }

    [Fact]
    public void Operator_IntegerDiv()
    {
        AssertHighlighter("vbnet",
"""
Dim r = a \ b
""",
"""
<span class="hljs-keyword">Dim</span> r = a \ b
""");
    }

    [Fact]
    public void Operator_Comparison()
    {
        AssertHighlighter("vbnet",
"""
If a = b OrElse c <> d Then Exit Sub
""",
"""
<span class="hljs-keyword">If</span> a = b <span class="hljs-built_in">OrElse</span> c &lt;&gt; d <span class="hljs-keyword">Then</span> <span class="hljs-keyword">Exit</span> <span class="hljs-keyword">Sub</span>
""");
    }

    [Fact]
    public void Operator_Logical()
    {
        AssertHighlighter("vbnet",
"""
If a And b Or Not c Then Run()
""",
"""
<span class="hljs-keyword">If</span> a <span class="hljs-built_in">And</span> b <span class="hljs-built_in">Or</span> <span class="hljs-built_in">Not</span> c <span class="hljs-keyword">Then</span> Run()
""");
    }

    [Fact]
    public void Operator_ShortCircuit()
    {
        AssertHighlighter("vbnet",
"""
If x IsNot Nothing AndAlso x.IsValid Then Process()
""",
"""
<span class="hljs-keyword">If</span> x <span class="hljs-built_in">IsNot</span> <span class="hljs-literal">Nothing</span> <span class="hljs-built_in">AndAlso</span> x.IsValid <span class="hljs-keyword">Then</span> Process()
""");
    }

    [Fact]
    public void Operator_StringConcat()
    {
        AssertHighlighter("vbnet",
"""
Dim full = first & " " & last
""",
"""
<span class="hljs-keyword">Dim</span> full = first &amp; <span class="hljs-string">&quot; &quot;</span> &amp; last
""");
    }

    [Fact]
    public void Operator_LikeOperator()
    {
        AssertHighlighter("vbnet",
"""
If name Like "Mr*" Then Greet()
""",
"""
<span class="hljs-keyword">If</span> name <span class="hljs-built_in">Like</span> <span class="hljs-string">&quot;Mr*&quot;</span> <span class="hljs-keyword">Then</span> Greet()
""");
    }

    [Fact]
    public void Operator_Is()
    {
        AssertHighlighter("vbnet",
"""
If TypeOf x Is User Then DoUser()
""",
"""
<span class="hljs-keyword">If</span> <span class="hljs-built_in">TypeOf</span> x <span class="hljs-built_in">Is</span> User <span class="hljs-keyword">Then</span> DoUser()
""");
    }

    [Fact]
    public void Operator_IsNothing()
    {
        AssertHighlighter("vbnet",
"""
If x Is Nothing Then Return
""",
"""
<span class="hljs-keyword">If</span> x <span class="hljs-built_in">Is</span> <span class="hljs-literal">Nothing</span> <span class="hljs-keyword">Then</span> <span class="hljs-keyword">Return</span>
""");
    }

    [Fact]
    public void Operator_IsNotNothing()
    {
        AssertHighlighter("vbnet",
"""
If x IsNot Nothing Then Process(x)
""",
"""
<span class="hljs-keyword">If</span> x <span class="hljs-built_in">IsNot</span> <span class="hljs-literal">Nothing</span> <span class="hljs-keyword">Then</span> Process(x)
""");
    }

    [Fact]
    public void Operator_Assign()
    {
        AssertHighlighter("vbnet",
"""
a += 1
""",
"""
a += <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Operator_AssignAll()
    {
        AssertHighlighter("vbnet",
"""
a += 1
b -= 1
c *= 2
d \= 2
e ^= 2
f &= "x"
""",
"""
a += <span class="hljs-number">1</span>
b -= <span class="hljs-number">1</span>
c *= <span class="hljs-number">2</span>
d \= <span class="hljs-number">2</span>
e ^= <span class="hljs-number">2</span>
f &amp;= <span class="hljs-string">&quot;x&quot;</span>
""");
    }

    [Fact]
    public void Operator_Shift()
    {
        AssertHighlighter("vbnet",
"""
Dim r = x << 2
Dim s = y >> 1
""",
"""
<span class="hljs-keyword">Dim</span> r = x &lt;&lt; <span class="hljs-number">2</span>
<span class="hljs-keyword">Dim</span> s = y &gt;&gt; <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void ControlFlow_IfThenSingleLine()
    {
        AssertHighlighter("vbnet",
"""
If x > 0 Then Run()
""",
"""
<span class="hljs-keyword">If</span> x &gt; <span class="hljs-number">0</span> <span class="hljs-keyword">Then</span> Run()
""");
    }

    [Fact]
    public void ControlFlow_IfThenBlock()
    {
        AssertHighlighter("vbnet",
"""
If x > 0 Then
    Run()
End If
""",
"""
<span class="hljs-keyword">If</span> x &gt; <span class="hljs-number">0</span> <span class="hljs-keyword">Then</span>
    Run()
<span class="hljs-keyword">End</span> <span class="hljs-keyword">If</span>
""");
    }

    [Fact]
    public void ControlFlow_IfElse()
    {
        AssertHighlighter("vbnet",
"""
If x > 0 Then
    Positive()
Else
    NonPositive()
End If
""",
"""
<span class="hljs-keyword">If</span> x &gt; <span class="hljs-number">0</span> <span class="hljs-keyword">Then</span>
    Positive()
<span class="hljs-keyword">Else</span>
    NonPositive()
<span class="hljs-keyword">End</span> <span class="hljs-keyword">If</span>
""");
    }

    [Fact]
    public void ControlFlow_IfElseIf()
    {
        AssertHighlighter("vbnet",
"""
If x > 0 Then
    Positive()
ElseIf x < 0 Then
    Negative()
Else
    Zero()
End If
""",
"""
<span class="hljs-keyword">If</span> x &gt; <span class="hljs-number">0</span> <span class="hljs-keyword">Then</span>
    Positive()
<span class="hljs-keyword">ElseIf</span> x &lt; <span class="hljs-number">0</span> <span class="hljs-keyword">Then</span>
    Negative()
<span class="hljs-keyword">Else</span>
    Zero()
<span class="hljs-keyword">End</span> <span class="hljs-keyword">If</span>
""");
    }

    [Fact]
    public void ControlFlow_SelectCase()
    {
        AssertHighlighter("vbnet",
"""
Select Case x
    Case 1
        One()
    Case 2, 3
        TwoOrThree()
    Case Else
        Other()
End Select
""",
"""
<span class="hljs-keyword">Select</span> <span class="hljs-keyword">Case</span> x
    <span class="hljs-keyword">Case</span> <span class="hljs-number">1</span>
        One()
    <span class="hljs-keyword">Case</span> <span class="hljs-number">2</span>, <span class="hljs-number">3</span>
        TwoOrThree()
    <span class="hljs-keyword">Case</span> <span class="hljs-keyword">Else</span>
        Other()
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Select</span>
""");
    }

    [Fact]
    public void ControlFlow_SelectCaseRange()
    {
        AssertHighlighter("vbnet",
"""
Select Case age
    Case 0 To 12
        Child()
    Case 13 To 19
        Teen()
    Case Is >= 20
        Adult()
End Select
""",
"""
<span class="hljs-keyword">Select</span> <span class="hljs-keyword">Case</span> age
    <span class="hljs-keyword">Case</span> <span class="hljs-number">0</span> <span class="hljs-keyword">To</span> <span class="hljs-number">12</span>
        Child()
    <span class="hljs-keyword">Case</span> <span class="hljs-number">13</span> <span class="hljs-keyword">To</span> <span class="hljs-number">19</span>
        Teen()
    <span class="hljs-keyword">Case</span> <span class="hljs-built_in">Is</span> &gt;= <span class="hljs-number">20</span>
        Adult()
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Select</span>
""");
    }

    [Fact]
    public void ControlFlow_SelectCaseTypeOf()
    {
        AssertHighlighter("vbnet",
"""
Select Case True
    Case TypeOf shape Is Circle
        DrawCircle()
End Select
""",
"""
<span class="hljs-keyword">Select</span> <span class="hljs-keyword">Case</span> <span class="hljs-literal">True</span>
    <span class="hljs-keyword">Case</span> <span class="hljs-built_in">TypeOf</span> shape <span class="hljs-built_in">Is</span> Circle
        DrawCircle()
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Select</span>
""");
    }

    [Fact]
    public void ControlFlow_ForNext()
    {
        AssertHighlighter("vbnet",
"""
For i As Integer = 0 To 9
    Console.WriteLine(i)
Next
""",
"""
<span class="hljs-keyword">For</span> i <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span> = <span class="hljs-number">0</span> <span class="hljs-keyword">To</span> <span class="hljs-number">9</span>
    Console.WriteLine(i)
<span class="hljs-keyword">Next</span>
""");
    }

    [Fact]
    public void ControlFlow_ForStep()
    {
        AssertHighlighter("vbnet",
"""
For i As Integer = 10 To 1 Step -1
    Console.WriteLine(i)
Next
""",
"""
<span class="hljs-keyword">For</span> i <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span> = <span class="hljs-number">10</span> <span class="hljs-keyword">To</span> <span class="hljs-number">1</span> <span class="hljs-keyword">Step</span> -<span class="hljs-number">1</span>
    Console.WriteLine(i)
<span class="hljs-keyword">Next</span>
""");
    }

    [Fact]
    public void ControlFlow_ForEach()
    {
        AssertHighlighter("vbnet",
"""
For Each item In items
    Process(item)
Next
""",
"""
<span class="hljs-keyword">For</span> <span class="hljs-keyword">Each</span> item <span class="hljs-keyword">In</span> items
    Process(item)
<span class="hljs-keyword">Next</span>
""");
    }

    [Fact]
    public void ControlFlow_ForEachTyped()
    {
        AssertHighlighter("vbnet",
"""
For Each user As User In users
    Process(user)
Next
""",
"""
<span class="hljs-keyword">For</span> <span class="hljs-keyword">Each</span> user <span class="hljs-keyword">As</span> User <span class="hljs-keyword">In</span> users
    Process(user)
<span class="hljs-keyword">Next</span>
""");
    }

    [Fact]
    public void ControlFlow_WhileLoop()
    {
        AssertHighlighter("vbnet",
"""
While i < 10
    i += 1
End While
""",
"""
<span class="hljs-keyword">While</span> i &lt; <span class="hljs-number">10</span>
    i += <span class="hljs-number">1</span>
<span class="hljs-keyword">End</span> <span class="hljs-keyword">While</span>
""");
    }

    [Fact]
    public void ControlFlow_DoWhileTop()
    {
        AssertHighlighter("vbnet",
"""
Do While count > 0
    Pop()
Loop
""",
"""
<span class="hljs-keyword">Do</span> <span class="hljs-keyword">While</span> count &gt; <span class="hljs-number">0</span>
    Pop()
<span class="hljs-keyword">Loop</span>
""");
    }

    [Fact]
    public void ControlFlow_DoWhileBottom()
    {
        AssertHighlighter("vbnet",
"""
Do
    Pop()
Loop While count > 0
""",
"""
<span class="hljs-keyword">Do</span>
    Pop()
<span class="hljs-keyword">Loop</span> <span class="hljs-keyword">While</span> count &gt; <span class="hljs-number">0</span>
""");
    }

    [Fact]
    public void ControlFlow_DoUntilTop()
    {
        AssertHighlighter("vbnet",
"""
Do Until done
    Step()
Loop
""",
"""
<span class="hljs-keyword">Do</span> <span class="hljs-keyword">Until</span> done
    <span class="hljs-keyword">Step</span>()
<span class="hljs-keyword">Loop</span>
""");
    }

    [Fact]
    public void ControlFlow_DoUntilBottom()
    {
        AssertHighlighter("vbnet",
"""
Do
    Step()
Loop Until done
""",
"""
<span class="hljs-keyword">Do</span>
    <span class="hljs-keyword">Step</span>()
<span class="hljs-keyword">Loop</span> <span class="hljs-keyword">Until</span> done
""");
    }

    [Fact]
    public void ControlFlow_ExitFor()
    {
        AssertHighlighter("vbnet",
"""
For i = 0 To 99
    If items(i).IsBad Then Exit For
Next
""",
"""
<span class="hljs-keyword">For</span> i = <span class="hljs-number">0</span> <span class="hljs-keyword">To</span> <span class="hljs-number">99</span>
    <span class="hljs-keyword">If</span> items(i).IsBad <span class="hljs-keyword">Then</span> <span class="hljs-keyword">Exit</span> <span class="hljs-keyword">For</span>
<span class="hljs-keyword">Next</span>
""");
    }

    [Fact]
    public void ControlFlow_ExitSub()
    {
        AssertHighlighter("vbnet",
"""
If x Is Nothing Then Exit Sub
""",
"""
<span class="hljs-keyword">If</span> x <span class="hljs-built_in">Is</span> <span class="hljs-literal">Nothing</span> <span class="hljs-keyword">Then</span> <span class="hljs-keyword">Exit</span> <span class="hljs-keyword">Sub</span>
""");
    }

    [Fact]
    public void ControlFlow_ContinueFor()
    {
        AssertHighlighter("vbnet",
"""
For Each x In items
    If Not x.IsValid Then Continue For
    Process(x)
Next
""",
"""
<span class="hljs-keyword">For</span> <span class="hljs-keyword">Each</span> x <span class="hljs-keyword">In</span> items
    <span class="hljs-keyword">If</span> <span class="hljs-built_in">Not</span> x.IsValid <span class="hljs-keyword">Then</span> <span class="hljs-keyword">Continue</span> <span class="hljs-keyword">For</span>
    Process(x)
<span class="hljs-keyword">Next</span>
""");
    }

    [Fact]
    public void ControlFlow_TernaryIf()
    {
        AssertHighlighter("vbnet",
"""
Dim s = If(x > 0, "pos", "non-pos")
""",
"""
<span class="hljs-keyword">Dim</span> s = <span class="hljs-keyword">If</span>(x &gt; <span class="hljs-number">0</span>, <span class="hljs-string">&quot;pos&quot;</span>, <span class="hljs-string">&quot;non-pos&quot;</span>)
""");
    }

    [Fact]
    public void ControlFlow_NullCoalesceIf()
    {
        AssertHighlighter("vbnet",
"""
Dim s = If(maybeNull, "default")
""",
"""
<span class="hljs-keyword">Dim</span> s = <span class="hljs-keyword">If</span>(maybeNull, <span class="hljs-string">&quot;default&quot;</span>)
""");
    }

    [Fact]
    public void ControlFlow_GotoLabel()
    {
        AssertHighlighter("vbnet",
"""
start:
    If count < 10 Then
        count += 1
        GoTo start
    End If
""",
"""
<span class="hljs-symbol">start:</span>
    <span class="hljs-keyword">If</span> count &lt; <span class="hljs-number">10</span> <span class="hljs-keyword">Then</span>
        count += <span class="hljs-number">1</span>
        <span class="hljs-keyword">GoTo</span> start
    <span class="hljs-keyword">End</span> <span class="hljs-keyword">If</span>
""");
    }

    [Fact]
    public void ControlFlow_OnErrorGoto()
    {
        AssertHighlighter("vbnet",
"""
On Error GoTo Cleanup
DoWork()
Exit Sub
Cleanup:
Log(Err.Description)
""",
"""
<span class="hljs-keyword">On</span> <span class="hljs-keyword">Error</span> <span class="hljs-keyword">GoTo</span> Cleanup
DoWork()
<span class="hljs-keyword">Exit</span> <span class="hljs-keyword">Sub</span>
<span class="hljs-symbol">Cleanup:</span>
Log(Err.Description)
""");
    }

    [Fact]
    public void ControlFlow_OnErrorResume()
    {
        AssertHighlighter("vbnet",
"""
On Error Resume Next
DoWork()
If Err.Number <> 0 Then Log(Err.Description)
""",
"""
<span class="hljs-keyword">On</span> <span class="hljs-keyword">Error</span> <span class="hljs-keyword">Resume</span> <span class="hljs-keyword">Next</span>
DoWork()
<span class="hljs-keyword">If</span> Err.Number &lt;&gt; <span class="hljs-number">0</span> <span class="hljs-keyword">Then</span> Log(Err.Description)
""");
    }

    [Fact]
    public void Sub_Empty()
    {
        AssertHighlighter("vbnet",
"""
Sub Greet()
End Sub
""",
"""
<span class="hljs-keyword">Sub</span> Greet()
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
""");
    }

    [Fact]
    public void Sub_WithBody()
    {
        AssertHighlighter("vbnet",
"""
Sub Greet(name As String)
    Console.WriteLine($"Hello {name}")
End Sub
""",
"""
<span class="hljs-keyword">Sub</span> Greet(name <span class="hljs-keyword">As</span> <span class="hljs-type">String</span>)
    Console.WriteLine($<span class="hljs-string">&quot;Hello {name}&quot;</span>)
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
""");
    }

    [Fact]
    public void Sub_Public()
    {
        AssertHighlighter("vbnet",
"""
Public Sub Run()
End Sub
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Sub</span> Run()
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
""");
    }

    [Fact]
    public void Sub_Private()
    {
        AssertHighlighter("vbnet",
"""
Private Sub Cleanup()
End Sub
""",
"""
<span class="hljs-keyword">Private</span> <span class="hljs-keyword">Sub</span> Cleanup()
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
""");
    }

    [Fact]
    public void Sub_Shared()
    {
        AssertHighlighter("vbnet",
"""
Public Shared Sub Main()
End Sub
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Shared</span> <span class="hljs-keyword">Sub</span> Main()
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
""");
    }

    [Fact]
    public void Sub_MainArgs()
    {
        AssertHighlighter("vbnet",
"""
Public Shared Sub Main(args As String())
    For Each arg In args
        Console.WriteLine(arg)
    Next
End Sub
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Shared</span> <span class="hljs-keyword">Sub</span> Main(args <span class="hljs-keyword">As</span> <span class="hljs-type">String</span>())
    <span class="hljs-keyword">For</span> <span class="hljs-keyword">Each</span> arg <span class="hljs-keyword">In</span> args
        Console.WriteLine(arg)
    <span class="hljs-keyword">Next</span>
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
""");
    }

    [Fact]
    public void Sub_Overloads()
    {
        AssertHighlighter("vbnet",
"""
Public Overloads Sub Log(msg As String)
End Sub
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Overloads</span> <span class="hljs-keyword">Sub</span> Log(msg <span class="hljs-keyword">As</span> <span class="hljs-type">String</span>)
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
""");
    }

    [Fact]
    public void Sub_Overridable()
    {
        AssertHighlighter("vbnet",
"""
Public Overridable Sub Run()
End Sub
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Overridable</span> <span class="hljs-keyword">Sub</span> Run()
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
""");
    }

    [Fact]
    public void Sub_Overrides()
    {
        AssertHighlighter("vbnet",
"""
Public Overrides Sub Run()
    MyBase.Run()
End Sub
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Overrides</span> <span class="hljs-keyword">Sub</span> Run()
    <span class="hljs-keyword">MyBase</span>.Run()
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
""");
    }

    [Fact]
    public void Sub_MustOverride()
    {
        AssertHighlighter("vbnet",
"""
Public MustOverride Sub Process()
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">MustOverride</span> <span class="hljs-keyword">Sub</span> Process()
""");
    }

    [Fact]
    public void Sub_NotOverridable()
    {
        AssertHighlighter("vbnet",
"""
Public NotOverridable Overrides Sub Run()
End Sub
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">NotOverridable</span> <span class="hljs-keyword">Overrides</span> <span class="hljs-keyword">Sub</span> Run()
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
""");
    }

    [Fact]
    public void Sub_Async()
    {
        AssertHighlighter("vbnet",
"""
Public Async Sub OnClick(sender As Object, e As EventArgs)
    Await DoAsync()
End Sub
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Async</span> <span class="hljs-keyword">Sub</span> OnClick(sender <span class="hljs-keyword">As</span> <span class="hljs-type">Object</span>, e <span class="hljs-keyword">As</span> EventArgs)
    <span class="hljs-built_in">Await</span> DoAsync()
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
""");
    }

    [Fact]
    public void Sub_ByRefParam()
    {
        AssertHighlighter("vbnet",
"""
Sub Swap(ByRef a As Integer, ByRef b As Integer)
    Dim t = a
    a = b
    b = t
End Sub
""",
"""
<span class="hljs-keyword">Sub</span> Swap(<span class="hljs-keyword">ByRef</span> a <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span>, <span class="hljs-keyword">ByRef</span> b <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span>)
    <span class="hljs-keyword">Dim</span> t = a
    a = b
    b = t
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
""");
    }

    [Fact]
    public void Sub_ByValParam()
    {
        AssertHighlighter("vbnet",
"""
Sub Log(ByVal message As String)
    Console.WriteLine(message)
End Sub
""",
"""
<span class="hljs-keyword">Sub</span> Log(<span class="hljs-keyword">ByVal</span> message <span class="hljs-keyword">As</span> <span class="hljs-type">String</span>)
    Console.WriteLine(message)
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
""");
    }

    [Fact]
    public void Sub_OptionalParam()
    {
        AssertHighlighter("vbnet",
"""
Sub Greet(name As String, Optional greeting As String = "Hello")
    Console.WriteLine($"{greeting} {name}")
End Sub
""",
"""
<span class="hljs-keyword">Sub</span> Greet(name <span class="hljs-keyword">As</span> <span class="hljs-type">String</span>, <span class="hljs-keyword">Optional</span> greeting <span class="hljs-keyword">As</span> <span class="hljs-type">String</span> = <span class="hljs-string">&quot;Hello&quot;</span>)
    Console.WriteLine($<span class="hljs-string">&quot;{greeting} {name}&quot;</span>)
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
""");
    }

    [Fact]
    public void Sub_ParamArray()
    {
        AssertHighlighter("vbnet",
"""
Sub PrintAll(ParamArray items() As Object)
    For Each i In items
        Console.WriteLine(i)
    Next
End Sub
""",
"""
<span class="hljs-keyword">Sub</span> PrintAll(<span class="hljs-keyword">ParamArray</span> items() <span class="hljs-keyword">As</span> <span class="hljs-type">Object</span>)
    <span class="hljs-keyword">For</span> <span class="hljs-keyword">Each</span> i <span class="hljs-keyword">In</span> items
        Console.WriteLine(i)
    <span class="hljs-keyword">Next</span>
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
""");
    }

    [Fact]
    public void Function_Simple()
    {
        AssertHighlighter("vbnet",
"""
Function Add(a As Integer, b As Integer) As Integer
    Return a + b
End Function
""",
"""
<span class="hljs-keyword">Function</span> Add(a <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span>, b <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span>) <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span>
    <span class="hljs-keyword">Return</span> a + b
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Function</span>
""");
    }

    [Fact]
    public void Function_ImplicitReturn()
    {
        AssertHighlighter("vbnet",
"""
Function Square(x As Integer) As Integer
    Square = x * x
End Function
""",
"""
<span class="hljs-keyword">Function</span> Square(x <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span>) <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span>
    Square = x * x
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Function</span>
""");
    }

    [Fact]
    public void Function_Generic()
    {
        AssertHighlighter("vbnet",
"""
Function Identity(Of T)(value As T) As T
    Return value
End Function
""",
"""
<span class="hljs-keyword">Function</span> Identity(<span class="hljs-keyword">Of</span> T)(value <span class="hljs-keyword">As</span> T) <span class="hljs-keyword">As</span> T
    <span class="hljs-keyword">Return</span> value
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Function</span>
""");
    }

    [Fact]
    public void Function_GenericConstraint()
    {
        AssertHighlighter("vbnet",
"""
Function Create(Of T As {Class, New})() As T
    Return New T()
End Function
""",
"""
Function Create(Of T As {Class, New})() As T
    Return New T()
End Function
""");
    }

    [Fact]
    public void Function_GenericMultipleConstraints()
    {
        AssertHighlighter("vbnet",
"""
Function Process(Of T As {IComparable(Of T), Class})(value As T) As T
    Return value
End Function
""",
"""
Function Process(Of T As {IComparable(Of T), Class})(value As T) As T
    Return value
End Function
""");
    }

    [Fact]
    public void Function_AsyncReturnsTask()
    {
        AssertHighlighter("vbnet",
"""
Async Function GetAsync() As Task(Of String)
    Return Await client.GetStringAsync(url)
End Function
""",
"""
<span class="hljs-keyword">Async</span> <span class="hljs-keyword">Function</span> GetAsync() <span class="hljs-keyword">As</span> Task(<span class="hljs-keyword">Of</span> <span class="hljs-type">String</span>)
    <span class="hljs-keyword">Return</span> <span class="hljs-built_in">Await</span> client.GetStringAsync(url)
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Function</span>
""");
    }

    [Fact]
    public void Function_AsyncIterator()
    {
        AssertHighlighter("vbnet",
"""
Iterator Function Range(n As Integer) As IEnumerable(Of Integer)
    For i = 0 To n - 1
        Yield i
    Next
End Function
""",
"""
<span class="hljs-keyword">Iterator</span> <span class="hljs-keyword">Function</span> Range(n <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span>) <span class="hljs-keyword">As</span> IEnumerable(<span class="hljs-keyword">Of</span> <span class="hljs-type">Integer</span>)
    <span class="hljs-keyword">For</span> i = <span class="hljs-number">0</span> <span class="hljs-keyword">To</span> n - <span class="hljs-number">1</span>
        <span class="hljs-keyword">Yield</span> i
    <span class="hljs-keyword">Next</span>
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Function</span>
""");
    }

    [Fact]
    public void Function_ExtensionMethod()
    {
        AssertHighlighter("vbnet",
"""
<Extension()>
Public Function IsEmpty(s As String) As Boolean
    Return s.Length = 0
End Function
""",
"""
&lt;Extension()&gt;
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Function</span> IsEmpty(s <span class="hljs-keyword">As</span> <span class="hljs-type">String</span>) <span class="hljs-keyword">As</span> <span class="hljs-type">Boolean</span>
    <span class="hljs-keyword">Return</span> s.Length = <span class="hljs-number">0</span>
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Function</span>
""");
    }

    [Fact]
    public void Function_NestedReturn()
    {
        AssertHighlighter("vbnet",
"""
Function Compute() As Integer
    If condition Then
        Return 1
    End If
    Return 0
End Function
""",
"""
<span class="hljs-keyword">Function</span> Compute() <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span>
    <span class="hljs-keyword">If</span> condition <span class="hljs-keyword">Then</span>
        <span class="hljs-keyword">Return</span> <span class="hljs-number">1</span>
    <span class="hljs-keyword">End</span> <span class="hljs-keyword">If</span>
    <span class="hljs-keyword">Return</span> <span class="hljs-number">0</span>
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Function</span>
""");
    }

    [Fact]
    public void ClassStructure_Empty()
    {
        AssertHighlighter("vbnet",
"""
Class Foo
End Class
""",
"""
<span class="hljs-keyword">Class</span> Foo
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Class</span>
""");
    }

    [Fact]
    public void ClassStructure_WithFields()
    {
        AssertHighlighter("vbnet",
"""
Public Class User
    Public Name As String
    Public Age As Integer
End Class
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Class</span> User
    <span class="hljs-keyword">Public</span> Name <span class="hljs-keyword">As</span> <span class="hljs-type">String</span>
    <span class="hljs-keyword">Public</span> Age <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span>
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Class</span>
""");
    }

    [Fact]
    public void ClassStructure_Inherits()
    {
        AssertHighlighter("vbnet",
"""
Public Class Manager
    Inherits Employee
End Class
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Class</span> Manager
    <span class="hljs-keyword">Inherits</span> Employee
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Class</span>
""");
    }

    [Fact]
    public void ClassStructure_InheritsAndImpl()
    {
        AssertHighlighter("vbnet",
"""
Public Class FileService
    Inherits Service
    Implements IDisposable
End Class
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Class</span> FileService
    <span class="hljs-keyword">Inherits</span> Service
    <span class="hljs-keyword">Implements</span> IDisposable
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Class</span>
""");
    }

    [Fact]
    public void ClassStructure_Implements()
    {
        AssertHighlighter("vbnet",
"""
Public Class Logger
    Implements ILogger, IDisposable
End Class
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Class</span> Logger
    <span class="hljs-keyword">Implements</span> ILogger, IDisposable
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Class</span>
""");
    }

    [Fact]
    public void ClassStructure_Generic()
    {
        AssertHighlighter("vbnet",
"""
Public Class Repository(Of T As Class)
    Private _items As New List(Of T)
End Class
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Class</span> Repository(<span class="hljs-keyword">Of</span> T <span class="hljs-keyword">As</span> <span class="hljs-keyword">Class</span>)
    <span class="hljs-keyword">Private</span> _items <span class="hljs-keyword">As</span> <span class="hljs-built_in">New</span> List(<span class="hljs-keyword">Of</span> T)
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Class</span>
""");
    }

    [Fact]
    public void ClassStructure_GenericConstraints()
    {
        AssertHighlighter("vbnet",
"""
Public Class Calculator(Of T As {Structure, IConvertible})
End Class
""",
"""
Public Class Calculator(Of T As {Structure, IConvertible})
End Class
""");
    }

    [Fact]
    public void ClassStructure_NotInheritable()
    {
        AssertHighlighter("vbnet",
"""
Public NotInheritable Class FinalUser
End Class
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">NotInheritable</span> <span class="hljs-keyword">Class</span> FinalUser
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Class</span>
""");
    }

    [Fact]
    public void ClassStructure_MustInherit()
    {
        AssertHighlighter("vbnet",
"""
Public MustInherit Class Shape
    Public MustOverride Function Area() As Double
End Class
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">MustInherit</span> <span class="hljs-keyword">Class</span> Shape
    <span class="hljs-keyword">Public</span> <span class="hljs-keyword">MustOverride</span> <span class="hljs-keyword">Function</span> Area() <span class="hljs-keyword">As</span> <span class="hljs-type">Double</span>
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Class</span>
""");
    }

    [Fact]
    public void ClassStructure_Partial()
    {
        AssertHighlighter("vbnet",
"""
Partial Public Class Foo
End Class
""",
"""
<span class="hljs-keyword">Partial</span> <span class="hljs-keyword">Public</span> <span class="hljs-keyword">Class</span> Foo
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Class</span>
""");
    }

    [Fact]
    public void ClassStructure_Static()
    {
        AssertHighlighter("vbnet",
"""
Public NotInheritable Class Math
    Private Sub New()
    End Sub
    Public Shared Function Square(x As Double) As Double
        Return x * x
    End Function
End Class
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">NotInheritable</span> <span class="hljs-keyword">Class</span> Math
    <span class="hljs-keyword">Private</span> <span class="hljs-keyword">Sub</span> <span class="hljs-built_in">New</span>()
    <span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
    <span class="hljs-keyword">Public</span> <span class="hljs-keyword">Shared</span> <span class="hljs-keyword">Function</span> Square(x <span class="hljs-keyword">As</span> <span class="hljs-type">Double</span>) <span class="hljs-keyword">As</span> <span class="hljs-type">Double</span>
        <span class="hljs-keyword">Return</span> x * x
    <span class="hljs-keyword">End</span> <span class="hljs-keyword">Function</span>
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Class</span>
""");
    }

    [Fact]
    public void ClassStructure_Nested()
    {
        AssertHighlighter("vbnet",
"""
Class Outer
    Class Inner
    End Class
End Class
""",
"""
<span class="hljs-keyword">Class</span> Outer
    <span class="hljs-keyword">Class</span> Inner
    <span class="hljs-keyword">End</span> <span class="hljs-keyword">Class</span>
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Class</span>
""");
    }

    [Fact]
    public void ClassStructure_Module()
    {
        AssertHighlighter("vbnet",
"""
Module Helpers
    Public Sub Run()
        Console.WriteLine("hi")
    End Sub
End Module
""",
"""
<span class="hljs-keyword">Module</span> Helpers
    <span class="hljs-keyword">Public</span> <span class="hljs-keyword">Sub</span> Run()
        Console.WriteLine(<span class="hljs-string">&quot;hi&quot;</span>)
    <span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Module</span>
""");
    }

    [Fact]
    public void ClassStructure_Structure()
    {
        AssertHighlighter("vbnet",
"""
Public Structure Point
    Public X As Double
    Public Y As Double
End Structure
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Structure</span> Point
    <span class="hljs-keyword">Public</span> X <span class="hljs-keyword">As</span> <span class="hljs-type">Double</span>
    <span class="hljs-keyword">Public</span> Y <span class="hljs-keyword">As</span> <span class="hljs-type">Double</span>
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Structure</span>
""");
    }

    [Fact]
    public void ClassStructure_StructureMethods()
    {
        AssertHighlighter("vbnet",
"""
Public Structure Money
    Public Amount As Decimal
    Public Currency As String

    Public Function Add(other As Money) As Money
        Return New Money With { .Amount = Amount + other.Amount, .Currency = Currency }
    End Function
End Structure
""",
"""
Public Structure Money
    Public Amount As Decimal
    Public Currency As String

    Public Function Add(other As Money) As Money
        Return New Money With { .Amount = Amount + other.Amount, .Currency = Currency }
    End Function
End Structure
""");
    }

    [Fact]
    public void ClassStructure_Enum()
    {
        AssertHighlighter("vbnet",
"""
Public Enum Color
    Red
    Green
    Blue
End Enum
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Enum</span> Color
    Red
    Green
    Blue
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Enum</span>
""");
    }

    [Fact]
    public void ClassStructure_EnumExplicit()
    {
        AssertHighlighter("vbnet",
"""
Public Enum Status As Integer
    Active = 1
    Inactive = 2
    Pending = 4
End Enum
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Enum</span> Status <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span>
    Active = <span class="hljs-number">1</span>
    Inactive = <span class="hljs-number">2</span>
    Pending = <span class="hljs-number">4</span>
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Enum</span>
""");
    }

    [Fact]
    public void ClassStructure_EnumFlags()
    {
        AssertHighlighter("vbnet",
"""
<Flags>
Public Enum Permissions
    None = 0
    Read = 1
    Write = 2
    Execute = 4
    All = Read Or Write Or Execute
End Enum
""",
"""
&lt;Flags&gt;
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Enum</span> Permissions
    None = <span class="hljs-number">0</span>
    Read = <span class="hljs-number">1</span>
    Write = <span class="hljs-number">2</span>
    Execute = <span class="hljs-number">4</span>
    All = Read <span class="hljs-built_in">Or</span> Write <span class="hljs-built_in">Or</span> Execute
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Enum</span>
""");
    }

    [Fact]
    public void ClassStructure_Interface()
    {
        AssertHighlighter("vbnet",
"""
Public Interface IShape
    ReadOnly Property Area As Double
    Sub Draw()
End Interface
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Interface</span> IShape
    <span class="hljs-keyword">ReadOnly</span> <span class="hljs-keyword">Property</span> Area <span class="hljs-keyword">As</span> <span class="hljs-type">Double</span>
    <span class="hljs-keyword">Sub</span> Draw()
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Interface</span>
""");
    }

    [Fact]
    public void ClassStructure_InterfaceInherit()
    {
        AssertHighlighter("vbnet",
"""
Public Interface IResource
    Inherits IDisposable, ICloneable
End Interface
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Interface</span> IResource
    <span class="hljs-keyword">Inherits</span> IDisposable, ICloneable
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Interface</span>
""");
    }

    [Fact]
    public void Property_AutoSimple()
    {
        AssertHighlighter("vbnet",
"""
Public Property Name As String
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Property</span> Name <span class="hljs-keyword">As</span> <span class="hljs-type">String</span>
""");
    }

    [Fact]
    public void Property_AutoDefault()
    {
        AssertHighlighter("vbnet",
"""
Public Property Count As Integer = 0
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Property</span> Count <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span> = <span class="hljs-number">0</span>
""");
    }

    [Fact]
    public void Property_ReadOnly()
    {
        AssertHighlighter("vbnet",
"""
Public ReadOnly Property Id As Guid = Guid.NewGuid()
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">ReadOnly</span> <span class="hljs-keyword">Property</span> Id <span class="hljs-keyword">As</span> Guid = Guid.NewGuid()
""");
    }

    [Fact]
    public void Property_ReadOnlyComputed()
    {
        AssertHighlighter("vbnet",
"""
Public ReadOnly Property FullName As String
    Get
        Return $"{First} {Last}"
    End Get
End Property
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">ReadOnly</span> <span class="hljs-keyword">Property</span> FullName <span class="hljs-keyword">As</span> <span class="hljs-type">String</span>
    <span class="hljs-keyword">Get</span>
        <span class="hljs-keyword">Return</span> $<span class="hljs-string">&quot;{First} {Last}&quot;</span>
    <span class="hljs-keyword">End</span> <span class="hljs-keyword">Get</span>
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Property</span>
""");
    }

    [Fact]
    public void Property_FullBody()
    {
        AssertHighlighter("vbnet",
"""
Public Property Count As Integer
    Get
        Return _count
    End Get
    Set(value As Integer)
        _count = value
    End Set
End Property
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Property</span> Count <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span>
    <span class="hljs-keyword">Get</span>
        <span class="hljs-keyword">Return</span> _count
    <span class="hljs-keyword">End</span> <span class="hljs-keyword">Get</span>
    <span class="hljs-keyword">Set</span>(value <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span>)
        _count = value
    <span class="hljs-keyword">End</span> <span class="hljs-keyword">Set</span>
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Property</span>
""");
    }

    [Fact]
    public void Property_PrivateSet()
    {
        AssertHighlighter("vbnet",
"""
Public Property Id As Guid
    Get
        Return _id
    End Get
    Private Set(value As Guid)
        _id = value
    End Set
End Property
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Property</span> Id <span class="hljs-keyword">As</span> Guid
    <span class="hljs-keyword">Get</span>
        <span class="hljs-keyword">Return</span> _id
    <span class="hljs-keyword">End</span> <span class="hljs-keyword">Get</span>
    <span class="hljs-keyword">Private</span> <span class="hljs-keyword">Set</span>(value <span class="hljs-keyword">As</span> Guid)
        _id = value
    <span class="hljs-keyword">End</span> <span class="hljs-keyword">Set</span>
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Property</span>
""");
    }

    [Fact]
    public void Property_Indexer()
    {
        AssertHighlighter("vbnet",
"""
Default Public Property Item(i As Integer) As Integer
    Get
        Return _items(i)
    End Get
    Set(value As Integer)
        _items(i) = value
    End Set
End Property
""",
"""
<span class="hljs-keyword">Default</span> <span class="hljs-keyword">Public</span> <span class="hljs-keyword">Property</span> Item(i <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span>) <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span>
    <span class="hljs-keyword">Get</span>
        <span class="hljs-keyword">Return</span> _items(i)
    <span class="hljs-keyword">End</span> <span class="hljs-keyword">Get</span>
    <span class="hljs-keyword">Set</span>(value <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span>)
        _items(i) = value
    <span class="hljs-keyword">End</span> <span class="hljs-keyword">Set</span>
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Property</span>
""");
    }

    [Fact]
    public void Property_Overridable()
    {
        AssertHighlighter("vbnet",
"""
Public Overridable Property Name As String
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Overridable</span> <span class="hljs-keyword">Property</span> Name <span class="hljs-keyword">As</span> <span class="hljs-type">String</span>
""");
    }

    [Fact]
    public void Constructor_Default()
    {
        AssertHighlighter("vbnet",
"""
Public Sub New()
End Sub
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Sub</span> <span class="hljs-built_in">New</span>()
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
""");
    }

    [Fact]
    public void Constructor_WithArgs()
    {
        AssertHighlighter("vbnet",
"""
Public Sub New(name As String, age As Integer)
    Me.Name = name
    Me.Age = age
End Sub
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Sub</span> <span class="hljs-built_in">New</span>(name <span class="hljs-keyword">As</span> <span class="hljs-type">String</span>, age <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span>)
    <span class="hljs-keyword">Me</span>.Name = name
    <span class="hljs-keyword">Me</span>.Age = age
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
""");
    }

    [Fact]
    public void Constructor_ChainMyBase()
    {
        AssertHighlighter("vbnet",
"""
Public Sub New(name As String)
    MyBase.New(name)
End Sub
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Sub</span> <span class="hljs-built_in">New</span>(name <span class="hljs-keyword">As</span> <span class="hljs-type">String</span>)
    <span class="hljs-keyword">MyBase</span>.<span class="hljs-built_in">New</span>(name)
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
""");
    }

    [Fact]
    public void Constructor_ChainMe()
    {
        AssertHighlighter("vbnet",
"""
Public Sub New()
    Me.New(0)
End Sub
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Sub</span> <span class="hljs-built_in">New</span>()
    <span class="hljs-keyword">Me</span>.<span class="hljs-built_in">New</span>(<span class="hljs-number">0</span>)
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
""");
    }

    [Fact]
    public void Constructor_Shared()
    {
        AssertHighlighter("vbnet",
"""
Shared Sub New()
    Items = New List(Of Integer)
End Sub
""",
"""
<span class="hljs-keyword">Shared</span> <span class="hljs-keyword">Sub</span> <span class="hljs-built_in">New</span>()
    Items = <span class="hljs-built_in">New</span> List(<span class="hljs-keyword">Of</span> <span class="hljs-type">Integer</span>)
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
""");
    }

    [Fact]
    public void Event_Declaration()
    {
        AssertHighlighter("vbnet",
"""
Public Event Clicked(sender As Object, e As EventArgs)
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Event</span> Clicked(sender <span class="hljs-keyword">As</span> <span class="hljs-type">Object</span>, e <span class="hljs-keyword">As</span> EventArgs)
""");
    }

    [Fact]
    public void Event_CustomEvent()
    {
        AssertHighlighter("vbnet",
"""
Public Custom Event PropertyChanged As PropertyChangedEventHandler
    AddHandler(value As PropertyChangedEventHandler)
    End AddHandler
    RemoveHandler(value As PropertyChangedEventHandler)
    End RemoveHandler
    RaiseEvent(sender As Object, e As PropertyChangedEventArgs)
    End RaiseEvent
End Event
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Custom</span> <span class="hljs-keyword">Event</span> PropertyChanged <span class="hljs-keyword">As</span> PropertyChangedEventHandler
    <span class="hljs-keyword">AddHandler</span>(value <span class="hljs-keyword">As</span> PropertyChangedEventHandler)
    <span class="hljs-keyword">End</span> <span class="hljs-keyword">AddHandler</span>
    <span class="hljs-keyword">RemoveHandler</span>(value <span class="hljs-keyword">As</span> PropertyChangedEventHandler)
    <span class="hljs-keyword">End</span> <span class="hljs-keyword">RemoveHandler</span>
    <span class="hljs-keyword">RaiseEvent</span>(sender <span class="hljs-keyword">As</span> <span class="hljs-type">Object</span>, e <span class="hljs-keyword">As</span> PropertyChangedEventArgs)
    <span class="hljs-keyword">End</span> <span class="hljs-keyword">RaiseEvent</span>
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Event</span>
""");
    }

    [Fact]
    public void Event_RaiseEvent()
    {
        AssertHighlighter("vbnet",
"""
RaiseEvent Clicked(Me, EventArgs.Empty)
""",
"""
<span class="hljs-keyword">RaiseEvent</span> Clicked(<span class="hljs-keyword">Me</span>, EventArgs.Empty)
""");
    }

    [Fact]
    public void Event_AddHandler()
    {
        AssertHighlighter("vbnet",
"""
AddHandler timer.Tick, AddressOf OnTick
""",
"""
<span class="hljs-keyword">AddHandler</span> timer.Tick, <span class="hljs-built_in">AddressOf</span> OnTick
""");
    }

    [Fact]
    public void Event_RemoveHandler()
    {
        AssertHighlighter("vbnet",
"""
RemoveHandler timer.Tick, AddressOf OnTick
""",
"""
<span class="hljs-keyword">RemoveHandler</span> timer.Tick, <span class="hljs-built_in">AddressOf</span> OnTick
""");
    }

    [Fact]
    public void Event_HandlesClause()
    {
        AssertHighlighter("vbnet",
"""
Private Sub OnTick(sender As Object, e As EventArgs) Handles timer.Tick
End Sub
""",
"""
<span class="hljs-keyword">Private</span> <span class="hljs-keyword">Sub</span> OnTick(sender <span class="hljs-keyword">As</span> <span class="hljs-type">Object</span>, e <span class="hljs-keyword">As</span> EventArgs) <span class="hljs-keyword">Handles</span> timer.Tick
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
""");
    }

    [Fact]
    public void Delegate_Sub()
    {
        AssertHighlighter("vbnet",
"""
Public Delegate Sub Callback(value As Integer)
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Delegate</span> <span class="hljs-keyword">Sub</span> Callback(value <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span>)
""");
    }

    [Fact]
    public void Delegate_Function()
    {
        AssertHighlighter("vbnet",
"""
Public Delegate Function Selector(Of T, TResult)(value As T) As TResult
""",
"""
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Delegate</span> <span class="hljs-keyword">Function</span> Selector(<span class="hljs-keyword">Of</span> T, TResult)(value <span class="hljs-keyword">As</span> T) <span class="hljs-keyword">As</span> TResult
""");
    }

    [Fact]
    public void Lambda_SubInline()
    {
        AssertHighlighter("vbnet",
"""
Dim run = Sub() Console.WriteLine("hi")
""",
"""
<span class="hljs-keyword">Dim</span> run = <span class="hljs-keyword">Sub</span>() Console.WriteLine(<span class="hljs-string">&quot;hi&quot;</span>)
""");
    }

    [Fact]
    public void Lambda_SubMultiLine()
    {
        AssertHighlighter("vbnet",
"""
Dim run = Sub()
               Console.WriteLine("hi")
               Console.WriteLine("bye")
           End Sub
""",
"""
<span class="hljs-keyword">Dim</span> run = <span class="hljs-keyword">Sub</span>()
               Console.WriteLine(<span class="hljs-string">&quot;hi&quot;</span>)
               Console.WriteLine(<span class="hljs-string">&quot;bye&quot;</span>)
           <span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
""");
    }

    [Fact]
    public void Lambda_FunctionInline()
    {
        AssertHighlighter("vbnet",
"""
Dim sq = Function(x As Integer) x * x
""",
"""
<span class="hljs-keyword">Dim</span> sq = <span class="hljs-keyword">Function</span>(x <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span>) x * x
""");
    }

    [Fact]
    public void Lambda_FunctionTyped()
    {
        AssertHighlighter("vbnet",
"""
Dim add As Func(Of Integer, Integer, Integer) = Function(a, b) a + b
""",
"""
<span class="hljs-keyword">Dim</span> add <span class="hljs-keyword">As</span> Func(<span class="hljs-keyword">Of</span> <span class="hljs-type">Integer</span>, <span class="hljs-type">Integer</span>, <span class="hljs-type">Integer</span>) = <span class="hljs-keyword">Function</span>(a, b) a + b
""");
    }

    [Fact]
    public void Lambda_FunctionMultiLine()
    {
        AssertHighlighter("vbnet",
"""
Dim process = Function(x As Integer) As Integer
                     Dim doubled = x * 2
                     Return doubled + 1
                 End Function
""",
"""
<span class="hljs-keyword">Dim</span> process = <span class="hljs-keyword">Function</span>(x <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span>) <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span>
                     <span class="hljs-keyword">Dim</span> doubled = x * <span class="hljs-number">2</span>
                     <span class="hljs-keyword">Return</span> doubled + <span class="hljs-number">1</span>
                 <span class="hljs-keyword">End</span> <span class="hljs-keyword">Function</span>
""");
    }

    [Fact]
    public void Lambda_Async()
    {
        AssertHighlighter("vbnet",
"""
Dim fetch = Async Function() As Task(Of String)
                Return Await client.GetStringAsync(url)
            End Function
""",
"""
<span class="hljs-keyword">Dim</span> fetch = <span class="hljs-keyword">Async</span> <span class="hljs-keyword">Function</span>() <span class="hljs-keyword">As</span> Task(<span class="hljs-keyword">Of</span> <span class="hljs-type">String</span>)
                <span class="hljs-keyword">Return</span> <span class="hljs-built_in">Await</span> client.GetStringAsync(url)
            <span class="hljs-keyword">End</span> <span class="hljs-keyword">Function</span>
""");
    }

    [Fact]
    public void Array_Declare()
    {
        AssertHighlighter("vbnet",
"""
Dim arr() As Integer = {1, 2, 3}
""",
"""
Dim arr() As Integer = {1, 2, 3}
""");
    }

    [Fact]
    public void Array_DeclareSize()
    {
        AssertHighlighter("vbnet",
"""
Dim arr(9) As Integer
""",
"""
<span class="hljs-keyword">Dim</span> arr(<span class="hljs-number">9</span>) <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span>
""");
    }

    [Fact]
    public void Array_MultiDim()
    {
        AssertHighlighter("vbnet",
"""
Dim grid(,) As Integer = {{1, 2}, {3, 4}}
""",
"""
Dim grid(,) As Integer = {{1, 2}, {3, 4}}
""");
    }

    [Fact]
    public void Array_Jagged()
    {
        AssertHighlighter("vbnet",
"""
Dim jagged()() As Integer = New Integer(2)() {New Integer() {1, 2}, New Integer() {3, 4, 5}, New Integer() {6}}
""",
"""
Dim jagged()() As Integer = New Integer(2)() {New Integer() {1, 2}, New Integer() {3, 4, 5}, New Integer() {6}}
""");
    }

    [Fact]
    public void Array_NewArrayWithSize()
    {
        AssertHighlighter("vbnet",
"""
Dim arr = New Integer(9) {}
""",
"""
Dim arr = New Integer(9) {}
""");
    }

    [Fact]
    public void Array_ReDim()
    {
        AssertHighlighter("vbnet",
"""
ReDim Preserve arr(19)
""",
"""
<span class="hljs-keyword">ReDim</span> <span class="hljs-keyword">Preserve</span> arr(<span class="hljs-number">19</span>)
""");
    }

    [Fact]
    public void Array_Indexed()
    {
        AssertHighlighter("vbnet",
"""
Dim first = arr(0)
""",
"""
<span class="hljs-keyword">Dim</span> first = arr(<span class="hljs-number">0</span>)
""");
    }

    [Fact]
    public void Collection_ListOf()
    {
        AssertHighlighter("vbnet",
"""
Dim names As New List(Of String) From {"alice", "bob"}
""",
"""
Dim names As New List(Of String) From {&quot;alice&quot;, &quot;bob&quot;}
""");
    }

    [Fact]
    public void Collection_DictionaryOf()
    {
        AssertHighlighter("vbnet",
"""
Dim ages As New Dictionary(Of String, Integer) From {{"alice", 30}, {"bob", 25}}
""",
"""
Dim ages As New Dictionary(Of String, Integer) From {{&quot;alice&quot;, 30}, {&quot;bob&quot;, 25}}
""");
    }

    [Fact]
    public void Collection_ObjectInitializer()
    {
        AssertHighlighter("vbnet",
"""
Dim user = New User With { .Name = "alice", .Age = 30 }
""",
"""
Dim user = New User With { .Name = &quot;alice&quot;, .Age = 30 }
""");
    }

    [Fact]
    public void Collection_AnonymousType()
    {
        AssertHighlighter("vbnet",
"""
Dim p = New With { .X = 1, .Y = 2 }
""",
"""
Dim p = New With { .X = 1, .Y = 2 }
""");
    }

    [Fact]
    public void Collection_AnonymousTypeKey()
    {
        AssertHighlighter("vbnet",
"""
Dim p = New With { Key .Name = "alice", .Age = 30 }
""",
"""
Dim p = New With { Key .Name = &quot;alice&quot;, .Age = 30 }
""");
    }

    [Fact]
    public void Linq_QuerySimple()
    {
        AssertHighlighter("vbnet",
"""
Dim q = From u In users Where u.IsActive Select u.Name
""",
"""
<span class="hljs-keyword">Dim</span> q = <span class="hljs-keyword">From</span> u <span class="hljs-keyword">In</span> users <span class="hljs-keyword">Where</span> u.IsActive <span class="hljs-keyword">Select</span> u.Name
""");
    }

    [Fact]
    public void Linq_QueryGroupBy()
    {
        AssertHighlighter("vbnet",
"""
Dim q = From u In users
        Group u By u.Country Into g = Group
        Select New With { .Country = Country, .Count = g.Count() }
""",
"""
Dim q = From u In users
        Group u By u.Country Into g = Group
        Select New With { .Country = Country, .Count = g.Count() }
""");
    }

    [Fact]
    public void Linq_QueryJoin()
    {
        AssertHighlighter("vbnet",
"""
Dim q = From u In users
        Join o In orders On u.Id Equals o.UserId
        Select New With { u.Name, o.Total }
""",
"""
Dim q = From u In users
        Join o In orders On u.Id Equals o.UserId
        Select New With { u.Name, o.Total }
""");
    }

    [Fact]
    public void Linq_QueryOrderBy()
    {
        AssertHighlighter("vbnet",
"""
Dim q = From u In users Order By u.Age Descending, u.Name Select u
""",
"""
<span class="hljs-keyword">Dim</span> q = <span class="hljs-keyword">From</span> u <span class="hljs-keyword">In</span> users <span class="hljs-keyword">Order</span> <span class="hljs-keyword">By</span> u.Age Descending, u.Name <span class="hljs-keyword">Select</span> u
""");
    }

    [Fact]
    public void Linq_QueryLet()
    {
        AssertHighlighter("vbnet",
"""
Dim q = From u In users
        Let total = u.Orders.Sum(Function(o) o.Total)
        Where total > 100
        Select New With { u.Name, total }
""",
"""
Dim q = From u In users
        Let total = u.Orders.Sum(Function(o) o.Total)
        Where total &gt; 100
        Select New With { u.Name, total }
""");
    }

    [Fact]
    public void Linq_MethodSyntax()
    {
        AssertHighlighter("vbnet",
"""
Dim names = users.Where(Function(u) u.IsActive).Select(Function(u) u.Name).ToList()
""",
"""
<span class="hljs-keyword">Dim</span> names = users.<span class="hljs-keyword">Where</span>(<span class="hljs-keyword">Function</span>(u) u.IsActive).<span class="hljs-keyword">Select</span>(<span class="hljs-keyword">Function</span>(u) u.Name).ToList()
""");
    }

    [Fact]
    public void Linq_Aggregate()
    {
        AssertHighlighter("vbnet",
"""
Dim total = items.Aggregate(0, Function(acc, x) acc + x.Price)
""",
"""
<span class="hljs-keyword">Dim</span> total = items.<span class="hljs-keyword">Aggregate</span>(<span class="hljs-number">0</span>, <span class="hljs-keyword">Function</span>(acc, x) acc + x.Price)
""");
    }

    [Fact]
    public void ExceptionHandling_TryCatch()
    {
        AssertHighlighter("vbnet",
"""
Try
    Risk()
Catch ex As Exception
    Log(ex)
End Try
""",
"""
<span class="hljs-keyword">Try</span>
    Risk()
<span class="hljs-keyword">Catch</span> ex <span class="hljs-keyword">As</span> Exception
    Log(ex)
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Try</span>
""");
    }

    [Fact]
    public void ExceptionHandling_TryCatchFinally()
    {
        AssertHighlighter("vbnet",
"""
Try
    Risk()
Catch ex As IOException
    Log("io", ex)
Finally
    Cleanup()
End Try
""",
"""
<span class="hljs-keyword">Try</span>
    Risk()
<span class="hljs-keyword">Catch</span> ex <span class="hljs-keyword">As</span> IOException
    Log(<span class="hljs-string">&quot;io&quot;</span>, ex)
<span class="hljs-keyword">Finally</span>
    Cleanup()
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Try</span>
""");
    }

    [Fact]
    public void ExceptionHandling_TryCatchWhen()
    {
        AssertHighlighter("vbnet",
"""
Try
    Risk()
Catch ex As WebException When ex.Status = WebExceptionStatus.Timeout
    Retry()
End Try
""",
"""
<span class="hljs-keyword">Try</span>
    Risk()
<span class="hljs-keyword">Catch</span> ex <span class="hljs-keyword">As</span> WebException <span class="hljs-keyword">When</span> ex.Status = WebExceptionStatus.Timeout
    Retry()
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Try</span>
""");
    }

    [Fact]
    public void ExceptionHandling_TryCatchRethrow()
    {
        AssertHighlighter("vbnet",
"""
Try
    Risk()
Catch ex As Exception
    Log(ex)
    Throw
End Try
""",
"""
<span class="hljs-keyword">Try</span>
    Risk()
<span class="hljs-keyword">Catch</span> ex <span class="hljs-keyword">As</span> Exception
    Log(ex)
    <span class="hljs-keyword">Throw</span>
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Try</span>
""");
    }

    [Fact]
    public void ExceptionHandling_ThrowStmt()
    {
        AssertHighlighter("vbnet",
"""
Throw New ArgumentNullException(NameOf(name))
""",
"""
<span class="hljs-keyword">Throw</span> <span class="hljs-built_in">New</span> ArgumentNullException(<span class="hljs-built_in">NameOf</span>(name))
""");
    }

    [Fact]
    public void XmlLiteral_Simple()
    {
        AssertHighlighter("vbnet",
"""
Dim doc = <book><title>VB</title></book>
""",
"""
<span class="hljs-keyword">Dim</span> doc = &lt;book&gt;&lt;title&gt;VB&lt;/title&gt;&lt;/book&gt;
""");
    }

    [Fact]
    public void XmlLiteral_WithAttrs()
    {
        AssertHighlighter("vbnet",
"""
Dim el = <book id="b-001" lang="en"><title>Hello</title></book>
""",
"""
<span class="hljs-keyword">Dim</span> el = &lt;book id=<span class="hljs-string">&quot;b-001&quot;</span> lang=<span class="hljs-string">&quot;en&quot;</span>&gt;&lt;title&gt;Hello&lt;/title&gt;&lt;/book&gt;
""");
    }

    [Fact]
    public void XmlLiteral_WithExpr()
    {
        AssertHighlighter("vbnet",
"""
Dim doc = <user name=<%= name %>><age><%= age %></age></user>
""",
"""
<span class="hljs-keyword">Dim</span> doc = &lt;user name=&lt;%= name %&gt;&gt;&lt;age&gt;&lt;%= age %&gt;&lt;/age&gt;&lt;/user&gt;
""");
    }

    [Fact]
    public void XmlLiteral_MultiLine()
    {
        AssertHighlighter("vbnet",
"""
Dim doc =
    <catalog>
        <book>
            <title>One</title>
        </book>
        <book>
            <title>Two</title>
        </book>
    </catalog>
""",
"""
<span class="hljs-keyword">Dim</span> doc =
    &lt;catalog&gt;
        &lt;book&gt;
            &lt;title&gt;One&lt;/title&gt;
        &lt;/book&gt;
        &lt;book&gt;
            &lt;title&gt;Two&lt;/title&gt;
        &lt;/book&gt;
    &lt;/catalog&gt;
""");
    }

    [Fact]
    public void XmlLiteral_AxisDescendant()
    {
        AssertHighlighter("vbnet",
"""
Dim titles = doc...<title>
""",
"""
<span class="hljs-keyword">Dim</span> titles = doc...&lt;title&gt;
""");
    }

    [Fact]
    public void XmlLiteral_AxisAttribute()
    {
        AssertHighlighter("vbnet",
"""
Dim id = book.@id
""",
"""
<span class="hljs-keyword">Dim</span> id = book.@id
""");
    }

    [Fact]
    public void XmlLiteral_AxisChild()
    {
        AssertHighlighter("vbnet",
"""
Dim title = book.<title>.Value
""",
"""
<span class="hljs-keyword">Dim</span> title = book.&lt;title&gt;.Value
""");
    }

    [Fact]
    public void KeywordsModern_NameOfOperator()
    {
        AssertHighlighter("vbnet",
"""
Throw New ArgumentNullException(NameOf(input))
""",
"""
<span class="hljs-keyword">Throw</span> <span class="hljs-built_in">New</span> ArgumentNullException(<span class="hljs-built_in">NameOf</span>(input))
""");
    }

    [Fact]
    public void KeywordsModern_GetTypeOperator()
    {
        AssertHighlighter("vbnet",
"""
Dim t = GetType(String)
""",
"""
<span class="hljs-keyword">Dim</span> t = <span class="hljs-built_in">GetType</span>(<span class="hljs-type">String</span>)
""");
    }

    [Fact]
    public void KeywordsModern_CTypeCast()
    {
        AssertHighlighter("vbnet",
"""
Dim n = CType(value, Integer)
""",
"""
<span class="hljs-keyword">Dim</span> n = CType(value, <span class="hljs-type">Integer</span>)
""");
    }

    [Fact]
    public void KeywordsModern_TryCast()
    {
        AssertHighlighter("vbnet",
"""
Dim u = TryCast(obj, User)
""",
"""
<span class="hljs-keyword">Dim</span> u = <span class="hljs-built_in">TryCast</span>(obj, User)
""");
    }

    [Fact]
    public void KeywordsModern_DirectCast()
    {
        AssertHighlighter("vbnet",
"""
Dim count = DirectCast(value, Integer)
""",
"""
<span class="hljs-keyword">Dim</span> count = <span class="hljs-built_in">DirectCast</span>(value, <span class="hljs-type">Integer</span>)
""");
    }

    [Fact]
    public void KeywordsModern_Await()
    {
        AssertHighlighter("vbnet",
"""
Dim data = Await client.GetStringAsync(url)
""",
"""
<span class="hljs-keyword">Dim</span> data = <span class="hljs-built_in">Await</span> client.GetStringAsync(url)
""");
    }

    [Fact]
    public void KeywordsModern_YieldStatement()
    {
        AssertHighlighter("vbnet",
"""
Yield i
""",
"""
<span class="hljs-keyword">Yield</span> i
""");
    }

    [Fact]
    public void KeywordsModern_AddressOf()
    {
        AssertHighlighter("vbnet",
"""
Dim h = AddressOf OnClick
""",
"""
<span class="hljs-keyword">Dim</span> h = <span class="hljs-built_in">AddressOf</span> OnClick
""");
    }

    [Fact]
    public void KeywordsModern_Me()
    {
        AssertHighlighter("vbnet",
"""
Me.Process()
""",
"""
<span class="hljs-keyword">Me</span>.Process()
""");
    }

    [Fact]
    public void KeywordsModern_MyBase()
    {
        AssertHighlighter("vbnet",
"""
MyBase.Initialize()
""",
"""
<span class="hljs-keyword">MyBase</span>.Initialize()
""");
    }

    [Fact]
    public void KeywordsModern_MyClass()
    {
        AssertHighlighter("vbnet",
"""
MyClass.Run()
""",
"""
<span class="hljs-keyword">MyClass</span>.Run()
""");
    }

    [Fact]
    public void KeywordsModern_IsTypeOf()
    {
        AssertHighlighter("vbnet",
"""
If TypeOf shape Is Circle Then DrawCircle()
""",
"""
<span class="hljs-keyword">If</span> <span class="hljs-built_in">TypeOf</span> shape <span class="hljs-built_in">Is</span> Circle <span class="hljs-keyword">Then</span> DrawCircle()
""");
    }

    [Fact]
    public void Attribute_Simple()
    {
        AssertHighlighter("vbnet",
"""
<Obsolete>
Public Sub Old()
End Sub
""",
"""
&lt;Obsolete&gt;
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Sub</span> Old()
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
""");
    }

    [Fact]
    public void Attribute_WithArgs()
    {
        AssertHighlighter("vbnet",
"""
<Obsolete("Use NewMethod", True)>
Public Sub Old()
End Sub
""",
"""
&lt;Obsolete(<span class="hljs-string">&quot;Use NewMethod&quot;</span>, <span class="hljs-literal">True</span>)&gt;
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Sub</span> Old()
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
""");
    }

    [Fact]
    public void Attribute_Multiple()
    {
        AssertHighlighter("vbnet",
"""
<Serializable, DebuggerDisplay("{Name}")>
Public Class User
End Class
""",
"""
&lt;Serializable, DebuggerDisplay(<span class="hljs-string">&quot;{Name}&quot;</span>)&gt;
<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Class</span> User
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Class</span>
""");
    }

    [Fact]
    public void Attribute_Assembly()
    {
        AssertHighlighter("vbnet",
"""
<Assembly: AssemblyVersion("1.0.0.0")>
""",
"""
&lt;<span class="hljs-keyword">Assembly</span>: AssemblyVersion(<span class="hljs-string">&quot;1.0.0.0&quot;</span>)&gt;
""");
    }

    [Fact]
    public void Attribute_ParamAttr()
    {
        AssertHighlighter("vbnet",
"""
Sub Greet(<Required> name As String)
End Sub
""",
"""
<span class="hljs-keyword">Sub</span> Greet(&lt;Required&gt; name <span class="hljs-keyword">As</span> <span class="hljs-type">String</span>)
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
""");
    }

    [Fact]
    public void UsingStatement_Statement()
    {
        AssertHighlighter("vbnet",
"""
Using f As FileStream = File.OpenRead(path)
    f.Read(buf, 0, buf.Length)
End Using
""",
"""
<span class="hljs-keyword">Using</span> f <span class="hljs-keyword">As</span> FileStream = File.OpenRead(path)
    f.Read(buf, <span class="hljs-number">0</span>, buf.Length)
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Using</span>
""");
    }

    [Fact]
    public void UsingStatement_MultiResource()
    {
        AssertHighlighter("vbnet",
"""
Using a As Stream = Open(), b As Stream = Open()
    Use(a, b)
End Using
""",
"""
<span class="hljs-keyword">Using</span> a <span class="hljs-keyword">As</span> Stream = Open(), b <span class="hljs-keyword">As</span> Stream = Open()
    Use(a, b)
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Using</span>
""");
    }

    [Fact]
    public void Preprocessor_IfDirective()
    {
        AssertHighlighter("vbnet",
"""
#If DEBUG Then
    Console.WriteLine("debug")
#End If
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">If</span> DEBUG <span class="hljs-keyword">Then</span></span>
    Console.WriteLine(<span class="hljs-string">&quot;debug&quot;</span>)
<span class="hljs-meta">#<span class="hljs-keyword">End</span> <span class="hljs-keyword">If</span></span>
""");
    }

    [Fact]
    public void Preprocessor_IfElseDirective()
    {
        AssertHighlighter("vbnet",
"""
#If NET8_0_OR_GREATER Then
    UseNet8()
#ElseIf NET6_0_OR_GREATER Then
    UseNet6()
#Else
    UseLegacy()
#End If
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">If</span> NET8_0_OR_GREATER <span class="hljs-keyword">Then</span></span>
    UseNet8()
<span class="hljs-meta">#<span class="hljs-keyword">ElseIf</span> NET6_0_OR_GREATER <span class="hljs-keyword">Then</span></span>
    UseNet6()
<span class="hljs-meta">#<span class="hljs-keyword">Else</span></span>
    UseLegacy()
<span class="hljs-meta">#<span class="hljs-keyword">End</span> <span class="hljs-keyword">If</span></span>
""");
    }

    [Fact]
    public void Preprocessor_Const()
    {
        AssertHighlighter("vbnet",
"""
#Const TRACE = True
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">Const</span> TRACE = True</span>
""");
    }

    [Fact]
    public void Preprocessor_Region()
    {
        AssertHighlighter("vbnet",
"""
#Region "Helpers"
Private Sub Log()
End Sub
#End Region
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">Region</span> &quot;Helpers&quot;</span>
<span class="hljs-keyword">Private</span> <span class="hljs-keyword">Sub</span> Log()
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
<span class="hljs-meta">#<span class="hljs-keyword">End</span> <span class="hljs-keyword">Region</span></span>
""");
    }

    [Fact]
    public void Preprocessor_ExternalSource()
    {
        AssertHighlighter("vbnet",
"""
#ExternalSource ("file.vb", 42)
Dim x = 1
#End ExternalSource
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">ExternalSource</span> (&quot;file.vb&quot;, 42)</span>
<span class="hljs-keyword">Dim</span> x = <span class="hljs-number">1</span>
<span class="hljs-meta">#<span class="hljs-keyword">End</span> <span class="hljs-keyword">ExternalSource</span></span>
""");
    }

    [Fact]
    public void Preprocessor_Disable()
    {
        AssertHighlighter("vbnet",
"""
#Disable Warning BC42024
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">Disable</span> Warning BC42024</span>
""");
    }

    [Fact]
    public void Preprocessor_Enable()
    {
        AssertHighlighter("vbnet",
"""
#Enable Warning BC42024
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">Enable</span> Warning BC42024</span>
""");
    }

    [Fact]
    public void Imports_Simple()
    {
        AssertHighlighter("vbnet",
"""
Imports System
""",
"""
<span class="hljs-keyword">Imports</span> System
""");
    }

    [Fact]
    public void Imports_Generic()
    {
        AssertHighlighter("vbnet",
"""
Imports System.Collections.Generic
""",
"""
<span class="hljs-keyword">Imports</span> System.Collections.Generic
""");
    }

    [Fact]
    public void Imports_Alias()
    {
        AssertHighlighter("vbnet",
"""
Imports Sys = System.Diagnostics
""",
"""
<span class="hljs-keyword">Imports</span> Sys = System.Diagnostics
""");
    }

    [Fact]
    public void Imports_AliasType()
    {
        AssertHighlighter("vbnet",
"""
Imports StringList = System.Collections.Generic.List(Of String)
""",
"""
<span class="hljs-keyword">Imports</span> StringList = System.Collections.Generic.List(<span class="hljs-keyword">Of</span> <span class="hljs-type">String</span>)
""");
    }

    [Fact]
    public void Imports_Xmlns()
    {
        AssertHighlighter("vbnet",
"""
Imports <xmlns:atom="http://www.w3.org/2005/Atom">
""",
"""
<span class="hljs-keyword">Imports</span> &lt;xmlns:atom=<span class="hljs-string">&quot;http://www.w3.org/2005/Atom&quot;</span>&gt;
""");
    }

    [Fact]
    public void OptionDirective_Strict()
    {
        AssertHighlighter("vbnet",
"""
Option Strict On
""",
"""
<span class="hljs-keyword">Option</span> <span class="hljs-keyword">Strict</span> <span class="hljs-keyword">On</span>
""");
    }

    [Fact]
    public void OptionDirective_Explicit()
    {
        AssertHighlighter("vbnet",
"""
Option Explicit On
""",
"""
<span class="hljs-keyword">Option</span> <span class="hljs-keyword">Explicit</span> <span class="hljs-keyword">On</span>
""");
    }

    [Fact]
    public void OptionDirective_Infer()
    {
        AssertHighlighter("vbnet",
"""
Option Infer On
""",
"""
<span class="hljs-keyword">Option</span> Infer <span class="hljs-keyword">On</span>
""");
    }

    [Fact]
    public void OptionDirective_Compare()
    {
        AssertHighlighter("vbnet",
"""
Option Compare Text
""",
"""
<span class="hljs-keyword">Option</span> <span class="hljs-keyword">Compare</span> <span class="hljs-keyword">Text</span>
""");
    }

    [Fact]
    public void Comment_Apostrophe()
    {
        AssertHighlighter("vbnet",
"""
' a comment using an apostrophe
""",
"""
<span class="hljs-comment">&#x27; a comment using an apostrophe</span>
""");
    }

    [Fact]
    public void Comment_Rem()
    {
        AssertHighlighter("vbnet",
"""
REM an old-style remark
""",
"""
<span class="hljs-comment">REM an old-style remark</span>
""");
    }

    [Fact]
    public void Comment_Inline()
    {
        AssertHighlighter("vbnet",
"""
Dim x = 1   ' set to one
""",
"""
<span class="hljs-keyword">Dim</span> x = <span class="hljs-number">1</span>   <span class="hljs-comment">&#x27; set to one</span>
""");
    }

    [Fact]
    public void Comment_XmlDoc()
    {
        AssertHighlighter("vbnet",
"""
''' <summary>
''' Adds two integers.
''' </summary>
''' <param name="a">First operand.</param>
Function Add(a As Integer, b As Integer) As Integer
    Return a + b
End Function
""",
"""
<span class="hljs-comment">&#x27;&#x27;&#x27; <span class="hljs-doctag">&lt;summary&gt;</span></span>
<span class="hljs-comment">&#x27;&#x27;&#x27; Adds two integers.</span>
<span class="hljs-comment">&#x27;&#x27;&#x27; <span class="hljs-doctag">&lt;/summary&gt;</span></span>
<span class="hljs-comment">&#x27;&#x27;&#x27; <span class="hljs-doctag">&lt;param name=&quot;a&quot;&gt;</span>First operand.<span class="hljs-doctag">&lt;/param&gt;</span></span>
<span class="hljs-keyword">Function</span> Add(a <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span>, b <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span>) <span class="hljs-keyword">As</span> <span class="hljs-type">Integer</span>
    <span class="hljs-keyword">Return</span> a + b
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Function</span>
""");
    }

    [Fact]
    public void Composite_ConsoleAppModule()
    {
        AssertHighlighter("vbnet",
"""
Imports System

Module Program
    Sub Main(args As String())
        Dim name = If(args.Length > 0, args(0), "world")
        Console.WriteLine($"Hello, {name}!")
    End Sub
End Module
""",
"""
<span class="hljs-keyword">Imports</span> System

<span class="hljs-keyword">Module</span> Program
    <span class="hljs-keyword">Sub</span> Main(args <span class="hljs-keyword">As</span> <span class="hljs-type">String</span>())
        <span class="hljs-keyword">Dim</span> name = <span class="hljs-keyword">If</span>(args.Length &gt; <span class="hljs-number">0</span>, args(<span class="hljs-number">0</span>), <span class="hljs-string">&quot;world&quot;</span>)
        Console.WriteLine($<span class="hljs-string">&quot;Hello, {name}!&quot;</span>)
    <span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Module</span>
""");
    }

    [Fact]
    public void Composite_ConsoleAppClass()
    {
        AssertHighlighter("vbnet",
"""
Imports System

Namespace MyApp
    Public Class Program
        Public Shared Sub Main(args As String())
            For Each arg In args
                Console.WriteLine(arg)
            Next
        End Sub
    End Class
End Namespace
""",
"""
<span class="hljs-keyword">Imports</span> System

<span class="hljs-keyword">Namespace</span> MyApp
    <span class="hljs-keyword">Public</span> <span class="hljs-keyword">Class</span> Program
        <span class="hljs-keyword">Public</span> <span class="hljs-keyword">Shared</span> <span class="hljs-keyword">Sub</span> Main(args <span class="hljs-keyword">As</span> <span class="hljs-type">String</span>())
            <span class="hljs-keyword">For</span> <span class="hljs-keyword">Each</span> arg <span class="hljs-keyword">In</span> args
                Console.WriteLine(arg)
            <span class="hljs-keyword">Next</span>
        <span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
    <span class="hljs-keyword">End</span> <span class="hljs-keyword">Class</span>
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Namespace</span>
""");
    }

    [Fact]
    public void Composite_AsyncApiClient()
    {
        AssertHighlighter("vbnet",
"""
Imports System.Net.Http
Imports System.Threading.Tasks

Public Class ApiClient
    Private ReadOnly _http As HttpClient

    Public Sub New(http As HttpClient)
        _http = http
    End Sub

    Public Async Function FetchAsync(url As String) As Task(Of String)
        Using response = Await _http.GetAsync(url)
            response.EnsureSuccessStatusCode()
            Return Await response.Content.ReadAsStringAsync()
        End Using
    End Function
End Class
""",
"""
<span class="hljs-keyword">Imports</span> System.Net.Http
<span class="hljs-keyword">Imports</span> System.Threading.Tasks

<span class="hljs-keyword">Public</span> <span class="hljs-keyword">Class</span> ApiClient
    <span class="hljs-keyword">Private</span> <span class="hljs-keyword">ReadOnly</span> _http <span class="hljs-keyword">As</span> HttpClient

    <span class="hljs-keyword">Public</span> <span class="hljs-keyword">Sub</span> <span class="hljs-built_in">New</span>(http <span class="hljs-keyword">As</span> HttpClient)
        _http = http
    <span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>

    <span class="hljs-keyword">Public</span> <span class="hljs-keyword">Async</span> <span class="hljs-keyword">Function</span> FetchAsync(url <span class="hljs-keyword">As</span> <span class="hljs-type">String</span>) <span class="hljs-keyword">As</span> Task(<span class="hljs-keyword">Of</span> <span class="hljs-type">String</span>)
        <span class="hljs-keyword">Using</span> response = <span class="hljs-built_in">Await</span> _http.GetAsync(url)
            response.EnsureSuccessStatusCode()
            <span class="hljs-keyword">Return</span> <span class="hljs-built_in">Await</span> response.Content.ReadAsStringAsync()
        <span class="hljs-keyword">End</span> <span class="hljs-keyword">Using</span>
    <span class="hljs-keyword">End</span> <span class="hljs-keyword">Function</span>
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Class</span>
""");
    }

    [Fact]
    public void Composite_LinqReport()
    {
        AssertHighlighter("vbnet",
"""
Dim summary = From u In users
              Where u.IsActive
              Group u By u.Country Into g = Group
              Order By Country
              Select New With {
                  .Country = Country,
                  .Users = g.Count(),
                  .AvgAge = g.Average(Function(x) x.Age)
              }

For Each row In summary
    Console.WriteLine($"{row.Country}: {row.Users} users, avg age {row.AvgAge:F1}")
Next
""",
"""
Dim summary = From u In users
              Where u.IsActive
              Group u By u.Country Into g = Group
              Order By Country
              Select New With {
                  .Country = Country,
                  .Users = g.Count(),
                  .AvgAge = g.Average(Function(x) x.Age)
              }

For Each row In summary
    Console.WriteLine($&quot;{row.Country}: {row.Users} users, avg age {row.AvgAge:F1}&quot;)
Next
""");
    }

    [Fact]
    public void Composite_WinFormsHandler()
    {
        AssertHighlighter("vbnet",
"""
Public Class MainForm
    Inherits Form

    Private WithEvents _button As Button

    Public Sub New()
        _button = New Button With { .Text = "Click me" }
        Controls.Add(_button)
    End Sub

    Private Sub OnButtonClick(sender As Object, e As EventArgs) Handles _button.Click
        MessageBox.Show("Hello, world!")
    End Sub
End Class
""",
"""
Public Class MainForm
    Inherits Form

    Private WithEvents _button As Button

    Public Sub New()
        _button = New Button With { .Text = &quot;Click me&quot; }
        Controls.Add(_button)
    End Sub

    Private Sub OnButtonClick(sender As Object, e As EventArgs) Handles _button.Click
        MessageBox.Show(&quot;Hello, world!&quot;)
    End Sub
End Class
""");
    }

    [Fact]
    public void SpecialEdge_Empty()
    {
        AssertHighlighter("vbnet",
"""

""",
"""

""");
    }

    [Fact]
    public void SpecialEdge_OnlyComment()
    {
        AssertHighlighter("vbnet",
"""
' just a comment
""",
"""
<span class="hljs-comment">&#x27; just a comment</span>
""");
    }

    [Fact]
    public void SpecialEdge_LineContinuation()
    {
        AssertHighlighter("vbnet",
"""
Dim x = 1 +
        2 +
        3
""",
"""
<span class="hljs-keyword">Dim</span> x = <span class="hljs-number">1</span> +
        <span class="hljs-number">2</span> +
        <span class="hljs-number">3</span>
""");
    }

    [Fact]
    public void SpecialEdge_ColonStatementSep()
    {
        AssertHighlighter("vbnet",
"""
Dim a = 1 : Dim b = 2 : Console.WriteLine(a + b)
""",
"""
<span class="hljs-keyword">Dim</span> a = <span class="hljs-number">1</span> : <span class="hljs-keyword">Dim</span> b = <span class="hljs-number">2</span> : Console.WriteLine(a + b)
""");
    }

    [Fact]
    public void SpecialEdge_TrailingNewline()
    {
        AssertHighlighter("vbnet",
"""
Class Foo
End Class

""",
"""
<span class="hljs-keyword">Class</span> Foo
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Class</span>

""");
    }
}
