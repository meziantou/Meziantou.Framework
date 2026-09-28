namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class VbScriptHighlighterTests
{
    [Fact]
    public void MsgBox()
    {
        AssertHighlighter("vbscript",
"""
MsgBox "Hello, World!"
""",
"""
MsgBox <span class="hljs-string">&quot;Hello, World!&quot;</span>
""");
    }

    [Fact]
    public void DimAndAssignments()
    {
        AssertHighlighter("vbscript",
"""
Option Explicit
Dim name, count
name = "World"
count = 42
WScript.Echo "Hello, " & name
""",
"""
<span class="hljs-keyword">Option</span> <span class="hljs-keyword">Explicit</span>
<span class="hljs-keyword">Dim</span> name, count
name = <span class="hljs-string">&quot;World&quot;</span>
count = <span class="hljs-number">42</span>
WScript.Echo <span class="hljs-string">&quot;Hello, &quot;</span> &amp; name
""");
    }

    [Fact]
    public void FunctionAndSub()
    {
        AssertHighlighter("vbscript",
"""
Function Add(a, b)
    Add = a + b
End Function

Sub ShowResult()
    MsgBox Add(1, 2)
End Sub
""",
"""
<span class="hljs-keyword">Function</span> Add(a, b)
    Add = a + b
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Function</span>

<span class="hljs-keyword">Sub</span> ShowResult()
    MsgBox Add(<span class="hljs-number">1</span>, <span class="hljs-number">2</span>)
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
""");
    }

    [Fact]
    public void IfElseIf()
    {
        AssertHighlighter("vbscript",
"""
If x > 10 Then
    WScript.Echo "big"
ElseIf x > 5 Then
    WScript.Echo "medium"
Else
    WScript.Echo "small"
End If
""",
"""
<span class="hljs-keyword">If</span> x &gt; <span class="hljs-number">10</span> <span class="hljs-keyword">Then</span>
    WScript.Echo <span class="hljs-string">&quot;big&quot;</span>
<span class="hljs-keyword">ElseIf</span> x &gt; <span class="hljs-number">5</span> <span class="hljs-keyword">Then</span>
    WScript.Echo <span class="hljs-string">&quot;medium&quot;</span>
<span class="hljs-keyword">Else</span>
    WScript.Echo <span class="hljs-string">&quot;small&quot;</span>
<span class="hljs-keyword">End</span> <span class="hljs-keyword">If</span>
""");
    }

    [Fact]
    public void ForLoops()
    {
        AssertHighlighter("vbscript",
"""
For i = 1 To 10 Step 2
    total = total + i
Next

For Each item In collection
    WScript.Echo item
Next
""",
"""
<span class="hljs-keyword">For</span> i = <span class="hljs-number">1</span> <span class="hljs-keyword">To</span> <span class="hljs-number">10</span> <span class="hljs-keyword">Step</span> <span class="hljs-number">2</span>
    total = total + i
<span class="hljs-keyword">Next</span>

<span class="hljs-keyword">For</span> <span class="hljs-keyword">Each</span> item <span class="hljs-keyword">In</span> collection
    WScript.Echo item
<span class="hljs-keyword">Next</span>
""");
    }

    [Fact]
    public void SelectCase()
    {
        AssertHighlighter("vbscript",
"""
Select Case LCase(answer)
    Case "yes"
        result = True
    Case Else
        result = False
End Select
""",
"""
<span class="hljs-keyword">Select</span> <span class="hljs-keyword">Case</span> <span class="hljs-built_in">LCase</span>(answer)
    <span class="hljs-keyword">Case</span> <span class="hljs-string">&quot;yes&quot;</span>
        result = <span class="hljs-literal">True</span>
    <span class="hljs-keyword">Case</span> <span class="hljs-keyword">Else</span>
        result = <span class="hljs-literal">False</span>
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Select</span>
""");
    }

    [Fact]
    public void CreateObject()
    {
        AssertHighlighter("vbscript",
"""
Set fso = CreateObject("Scripting.FileSystemObject")
Set file = fso.OpenTextFile("C:\\temp\\log.txt", 8, True)
file.WriteLine Now()
file.Close
Set file = Nothing
""",
"""
<span class="hljs-keyword">Set</span> fso = <span class="hljs-built_in">CreateObject</span>(<span class="hljs-string">&quot;Scripting.FileSystemObject&quot;</span>)
<span class="hljs-keyword">Set</span> file = fso.OpenTextFile(<span class="hljs-string">&quot;C:\\temp\\log.txt&quot;</span>, <span class="hljs-number">8</span>, <span class="hljs-literal">True</span>)
file.WriteLine <span class="hljs-built_in">Now</span>()
file.Close
<span class="hljs-keyword">Set</span> file = <span class="hljs-literal">Nothing</span>
""");
    }

    [Fact]
    public void OnErrorResumeNext()
    {
        AssertHighlighter("vbscript",
"""
On Error Resume Next
x = 1 / 0
If Err.Number <> 0 Then
    WScript.Echo "Error: " & Err.Description
    Err.Clear
End If
""",
"""
<span class="hljs-keyword">On</span> <span class="hljs-keyword">Error</span> <span class="hljs-keyword">Resume</span> <span class="hljs-keyword">Next</span>
x = <span class="hljs-number">1</span> / <span class="hljs-number">0</span>
<span class="hljs-keyword">If</span> Err.Number &lt;&gt; <span class="hljs-number">0</span> <span class="hljs-keyword">Then</span>
    WScript.Echo <span class="hljs-string">&quot;Error: &quot;</span> &amp; Err.Description
    Err.Clear
<span class="hljs-keyword">End</span> <span class="hljs-keyword">If</span>
""");
    }

    [Fact]
    public void Class()
    {
        AssertHighlighter("vbscript",
"""
Class Person
    Private m_name
    Public Property Get Name()
        Name = m_name
    End Property
    Public Property Let Name(value)
        m_name = value
    End Property
    Private Sub Class_Initialize()
        m_name = Empty
    End Sub
End Class
""",
"""
<span class="hljs-keyword">Class</span> Person
    <span class="hljs-keyword">Private</span> m_name
    <span class="hljs-keyword">Public</span> <span class="hljs-keyword">Property</span> <span class="hljs-keyword">Get</span> Name()
        Name = m_name
    <span class="hljs-keyword">End</span> <span class="hljs-keyword">Property</span>
    <span class="hljs-keyword">Public</span> <span class="hljs-keyword">Property</span> <span class="hljs-keyword">Let</span> Name(value)
        m_name = value
    <span class="hljs-keyword">End</span> <span class="hljs-keyword">Property</span>
    <span class="hljs-keyword">Private</span> <span class="hljs-keyword">Sub</span> <span class="hljs-keyword">Class_Initialize</span>()
        m_name = <span class="hljs-literal">Empty</span>
    <span class="hljs-keyword">End</span> <span class="hljs-keyword">Sub</span>
<span class="hljs-keyword">End</span> <span class="hljs-keyword">Class</span>
""");
    }

    [Fact]
    public void StringEscapeAndComment()
    {
        AssertHighlighter("vbscript",
""""
s = "She said ""hi""" ' a comment
"""",
"""
s = <span class="hljs-string">&quot;She said &quot;&quot;hi&quot;&quot;&quot;</span> <span class="hljs-comment">&#x27; a comment</span>
""");
    }

    [Fact]
    public void BuiltInFunctions()
    {
        AssertHighlighter("vbscript",
"""
d = DateAdd("d", 1, Date())
n = Len (s) + InStr(s, "a") + Round(3.7)
u = UCase(Trim(s))
""",
"""
d = <span class="hljs-built_in">DateAdd</span>(<span class="hljs-string">&quot;d&quot;</span>, <span class="hljs-number">1</span>, <span class="hljs-built_in">Date</span>())
n = <span class="hljs-built_in">Len</span> (s) + <span class="hljs-built_in">InStr</span>(s, <span class="hljs-string">&quot;a&quot;</span>) + <span class="hljs-built_in">Round</span>(<span class="hljs-number">3.7</span>)
u = <span class="hljs-built_in">UCase</span>(<span class="hljs-built_in">Trim</span>(s))
""");
    }

    [Fact]
    public void BuiltInFunctionWithoutParentheses_IsNotHighlighted()
    {
        AssertHighlighter("vbscript",
"""
t = Time
d = Date
r = Round
""",
"""
t = Time
d = Date
r = Round
""");
    }

    [Fact]
    public void BuiltInFunctionNameAtTheEndOfAWord_IsNotHighlighted()
    {
        AssertHighlighter("vbscript",
"""
x = MyRound(1)
DoRound(2)
y = Round(2)
""",
"""
x = MyRound(<span class="hljs-number">1</span>)
DoRound(<span class="hljs-number">2</span>)
y = <span class="hljs-built_in">Round</span>(<span class="hljs-number">2</span>)
""");
    }

    [Fact]
    public void AspObjects()
    {
        AssertHighlighter("vbscript",
"""
Response.Write Request.QueryString("id")
Server.MapPath("/")
""",
"""
<span class="hljs-built_in">Response</span>.Write <span class="hljs-built_in">Request</span>.QueryString(<span class="hljs-string">&quot;id&quot;</span>)
<span class="hljs-built_in">Server</span>.MapPath(<span class="hljs-string">&quot;/&quot;</span>)
""");
    }

    [Fact]
    public void ReDim()
    {
        AssertHighlighter("vbscript",
"""
Dim arr()
ReDim Preserve arr(10)
WScript.Echo UBound(arr)
""",
"""
<span class="hljs-keyword">Dim</span> arr()
<span class="hljs-keyword">ReDim</span> <span class="hljs-keyword">Preserve</span> arr(<span class="hljs-number">10</span>)
WScript.Echo <span class="hljs-built_in">UBound</span>(arr)
""");
    }

    [Fact]
    public void DoLoop()
    {
        AssertHighlighter("vbscript",
"""
Do While i < 10
    i = i + 1
Loop
Do
    i = i - 1
Loop Until i = 0
""",
"""
<span class="hljs-keyword">Do</span> <span class="hljs-keyword">While</span> i &lt; <span class="hljs-number">10</span>
    i = i + <span class="hljs-number">1</span>
<span class="hljs-keyword">Loop</span>
<span class="hljs-keyword">Do</span>
    i = i - <span class="hljs-number">1</span>
<span class="hljs-keyword">Loop</span> Until i = <span class="hljs-number">0</span>
""");
    }

    [Fact]
    public void RemIsAKeyword()
    {
        AssertHighlighter("vbscript",
"""
Rem this is a comment
x = 1 ' comment
""",
"""
<span class="hljs-keyword">Rem</span> this <span class="hljs-keyword">is</span> a comment
x = <span class="hljs-number">1</span> <span class="hljs-comment">&#x27; comment</span>
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("vbscript",
"""
x = 42 + 3.14 + &HFF + 1E3
""",
"""
x = <span class="hljs-number">42</span> + <span class="hljs-number">3.14</span> + &amp;HFF + <span class="hljs-number">1E3</span>
""");
    }

    [Fact]
    public void UnterminatedString()
    {
        AssertHighlighter("vbscript",
"""
s = "never closed
x = 1
""",
"""
s = <span class="hljs-string">&quot;never closed
x = 1</span>
""");
    }

    [Fact]
    public void CaseInsensitive()
    {
        AssertHighlighter("vbscript",
"""
DIM X
IF x THEN x = TRUE END IF
set o = NOTHING
""",
"""
<span class="hljs-keyword">DIM</span> X
<span class="hljs-keyword">IF</span> x <span class="hljs-keyword">THEN</span> x = <span class="hljs-literal">TRUE</span> <span class="hljs-keyword">END</span> <span class="hljs-keyword">IF</span>
<span class="hljs-keyword">set</span> o = <span class="hljs-literal">NOTHING</span>
""");
    }

    [Fact]
    public void DoubleSlash()
    {
        AssertHighlighter("vbscript",
"""
x = 1 // 2
y = 3
""",
"""
x = <span class="hljs-number">1</span> // <span class="hljs-number">2</span>
y = <span class="hljs-number">3</span>
""");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("vbscript",
"""
s = "héllo ☕" ' café
""",
"""
s = <span class="hljs-string">&quot;héllo ☕&quot;</span> <span class="hljs-comment">&#x27; café</span>
""");
    }

    [Fact]
    public void VbsAlias()
    {
        AssertHighlighter("vbs",
"""
WScript.Echo "vbs"
""",
"""
WScript.Echo <span class="hljs-string">&quot;vbs&quot;</span>
""");
    }

    [Fact]
    public void EmptyInput()
    {
        AssertHighlighter("vbscript",
"",
"");
    }
}
