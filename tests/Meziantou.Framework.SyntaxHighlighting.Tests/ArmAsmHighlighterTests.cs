namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public sealed class ArmAsmHighlighterTests
{
    [Fact]
    public void GnuHello()
    {
        AssertHighlighter("armasm",
"""
    .global _start
    .text
_start:
    mov r0, #1          @ stdout
    ldr r1, =message    @ address
    mov r2, #13
    mov r7, #4          @ write syscall
    svc #0
    mov r7, #1
    svc 0
    .data
message:
    .ascii "Hello, ARM!\n"
""",
"""
    <span class="hljs-meta">.global</span> _start
    <span class="hljs-meta">.text</span>
<span class="hljs-symbol">_start:</span>
    <span class="hljs-keyword">mov</span> <span class="hljs-built_in">r0</span>, <span class="hljs-number">#1</span>          <span class="hljs-comment">@ stdout</span>
    <span class="hljs-keyword">ldr</span> <span class="hljs-built_in">r1</span>, <span class="hljs-symbol">=message</span>    <span class="hljs-comment">@ address</span>
    <span class="hljs-keyword">mov</span> <span class="hljs-built_in">r2</span>, <span class="hljs-number">#13</span>
    <span class="hljs-keyword">mov</span> <span class="hljs-built_in">r7</span>, <span class="hljs-number">#4</span>          <span class="hljs-comment">@ write syscall</span>
    <span class="hljs-keyword">svc</span> <span class="hljs-number">#0</span>
    <span class="hljs-keyword">mov</span> <span class="hljs-built_in">r7</span>, <span class="hljs-number">#1</span>
    <span class="hljs-keyword">svc</span> <span class="hljs-number">0</span>
    <span class="hljs-meta">.data</span>
<span class="hljs-symbol">message:</span>
    <span class="hljs-meta">.ascii</span> <span class="hljs-string">&quot;Hello, ARM!\n&quot;</span>
""");
    }

    [Fact]
    public void Armcc()
    {
        AssertHighlighter("armasm",
"""
        AREA    Example, CODE, READONLY
        ENTRY
start   MOV     r0, #10
        MOV     r1, #3
        ADD     r0, r0, r1
loop    SUBS    r1, r1, #1
        BNE     loop
stop    B       stop
        END
""",
"""
        <span class="hljs-meta">AREA</span>    Example, <span class="hljs-meta">CODE</span>, <span class="hljs-meta">READONLY</span>
        <span class="hljs-meta">ENTRY</span>
<span class="hljs-symbol">start</span>   <span class="hljs-keyword">MOV</span>     <span class="hljs-built_in">r0</span>, <span class="hljs-number">#10</span>
        <span class="hljs-keyword">MOV</span>     <span class="hljs-built_in">r1</span>, <span class="hljs-number">#3</span>
        <span class="hljs-keyword">ADD</span>     <span class="hljs-built_in">r0</span>, <span class="hljs-built_in">r0</span>, <span class="hljs-built_in">r1</span>
<span class="hljs-symbol">loop</span>    <span class="hljs-keyword">SUBS</span>    <span class="hljs-built_in">r1</span>, <span class="hljs-built_in">r1</span>, <span class="hljs-number">#1</span>
        <span class="hljs-keyword">BNE</span>     loop
<span class="hljs-symbol">stop</span>    <span class="hljs-keyword">B</span>       stop
        <span class="hljs-meta">END</span>
""");
    }

    [Fact]
    public void Conditions()
    {
        AssertHighlighter("armasm",
"""
    cmp r0, #0
    moveq r1, #1
    movne r1, #0
    addgt r2, r2, r3
    bleq func
    bx lr
    ldmfd sp!, {r4-r11, pc}
    stmfd sp!, {r4, lr}
    push {r4, r5, lr}
    pop {r4, r5, pc}
    ittt eq
""",
"""
    <span class="hljs-keyword">cmp</span> <span class="hljs-built_in">r0</span>, <span class="hljs-number">#0</span>
    <span class="hljs-keyword">moveq</span> <span class="hljs-built_in">r1</span>, <span class="hljs-number">#1</span>
    <span class="hljs-keyword">movne</span> <span class="hljs-built_in">r1</span>, <span class="hljs-number">#0</span>
    <span class="hljs-keyword">addgt</span> <span class="hljs-built_in">r2</span>, <span class="hljs-built_in">r2</span>, <span class="hljs-built_in">r3</span>
    <span class="hljs-keyword">bleq</span> func
    <span class="hljs-keyword">bx</span> <span class="hljs-built_in">lr</span>
    <span class="hljs-keyword">ldmfd</span> <span class="hljs-built_in">sp</span>!, {<span class="hljs-built_in">r4</span>-<span class="hljs-built_in">r11</span>, <span class="hljs-built_in">pc</span>}
    stmfd <span class="hljs-built_in">sp</span>!, {<span class="hljs-built_in">r4</span>, <span class="hljs-built_in">lr</span>}
    <span class="hljs-keyword">push</span> {<span class="hljs-built_in">r4</span>, <span class="hljs-built_in">r5</span>, <span class="hljs-built_in">lr</span>}
    <span class="hljs-keyword">pop</span> {<span class="hljs-built_in">r4</span>, <span class="hljs-built_in">r5</span>, <span class="hljs-built_in">pc</span>}
    <span class="hljs-keyword">ittt</span> eq
""");
    }

    [Fact]
    public void Aarch64()
    {
        AssertHighlighter("armasm",
"""
    stp x29, x30, [sp, #-16]!
    mov x29, sp
    ldr w0, [x1, #8]
    add x0, x0, x1
    ldp x29, x30, [sp], #16
    ret
""",
"""
    stp <span class="hljs-built_in">x29</span>, <span class="hljs-built_in">x30</span>, [<span class="hljs-built_in">sp</span>, #-<span class="hljs-number">16</span>]!
    <span class="hljs-keyword">mov</span> <span class="hljs-built_in">x29</span>, <span class="hljs-built_in">sp</span>
    <span class="hljs-keyword">ldr</span> <span class="hljs-built_in">w0</span>, [<span class="hljs-built_in">x1</span>, <span class="hljs-number">#8</span>]
    <span class="hljs-keyword">add</span> <span class="hljs-built_in">x0</span>, <span class="hljs-built_in">x0</span>, <span class="hljs-built_in">x1</span>
    ldp <span class="hljs-built_in">x29</span>, <span class="hljs-built_in">x30</span>, [<span class="hljs-built_in">sp</span>], <span class="hljs-number">#16</span>
    ret
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("armasm",
"""
# preprocessor-style comment
  # indented hash comment
; semicolon comment
// line comment
/* block
   comment */
mov r0, r1 // trailing
""",
"""
<span class="hljs-comment"># preprocessor-style comment</span>
  <span class="hljs-comment"># indented hash comment</span>
<span class="hljs-comment">; semicolon comment</span>
<span class="hljs-comment">// line comment</span>
<span class="hljs-comment">/* block
   comment */</span>
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">r0</span>, <span class="hljs-built_in">r1</span> <span class="hljs-comment">// trailing</span>
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("armasm",
"""
    mov r0, #0x1F
    mov r1, #0b1010
    mov r2, #42
    ldr r3, =0xDEADBEEF
    .word 12345, 0XFF
""",
"""
    <span class="hljs-keyword">mov</span> <span class="hljs-built_in">r0</span>, <span class="hljs-number">#0x1F</span>
    <span class="hljs-keyword">mov</span> <span class="hljs-built_in">r1</span>, <span class="hljs-number">#0b1010</span>
    <span class="hljs-keyword">mov</span> <span class="hljs-built_in">r2</span>, <span class="hljs-number">#42</span>
    <span class="hljs-keyword">ldr</span> <span class="hljs-built_in">r3</span>, <span class="hljs-number">=0xDEADBEEF</span>
    <span class="hljs-meta">.word</span> <span class="hljs-number">12345</span>, <span class="hljs-number">0XFF</span>
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("armasm",
"""
    .asciz "with \"escape\""
    mov r0, #'A'
    DCB "unterminated
    DCB 'x'
""",
"""
    <span class="hljs-meta">.asciz</span> <span class="hljs-string">&quot;with \&quot;escape\&quot;&quot;</span>
    <span class="hljs-keyword">mov</span> <span class="hljs-built_in">r0</span>, #<span class="hljs-string">&#x27;A&#x27;</span>
    <span class="hljs-meta">DCB</span> <span class="hljs-string">&quot;unterminated
    DCB &#x27;x&#x27;</span>
""");
    }

    [Fact]
    public void Title()
    {
        AssertHighlighter("armasm",
"""
|.text| DCD |symbol name|
|unterminated
next
""",
"""
<span class="hljs-title">|.text|</span> <span class="hljs-meta">DCD</span> <span class="hljs-title">|symbol name|</span>
|unterminated
<span class="hljs-symbol">next</span>
""");
    }

    [Fact]
    public void Labels()
    {
        AssertHighlighter("armasm",
"""
1:  b 1b
.Lloop:
    subs r0, r0, #1
    bne .Lloop
""",
"""
<span class="hljs-number">1</span>:  <span class="hljs-keyword">b</span> <span class="hljs-number">1</span>b
<span class="hljs-symbol">.Lloop:</span>
    <span class="hljs-keyword">subs</span> <span class="hljs-built_in">r0</span>, <span class="hljs-built_in">r0</span>, <span class="hljs-number">#1</span>
    <span class="hljs-keyword">bne</span> .Lloop
""");
    }

    [Fact]
    public void Psr()
    {
        AssertHighlighter("armasm",
"""
    mrs r0, cpsr
    msr cpsr_c, r0
    vadd.f32 s0, s1, s2
    vldr d0, [r0]
""",
"""
    <span class="hljs-keyword">mrs</span> <span class="hljs-built_in">r0</span>, <span class="hljs-keyword">cpsr</span>
    <span class="hljs-keyword">msr</span> <span class="hljs-built_in">cpsr_c</span>, <span class="hljs-built_in">r0</span>
    vadd.f32 <span class="hljs-built_in">s0</span>, <span class="hljs-built_in">s1</span>, <span class="hljs-built_in">s2</span>
    vldr <span class="hljs-built_in">d0</span>, [<span class="hljs-built_in">r0</span>]
""");
    }

    [Fact]
    public void Empty()
    {
        AssertHighlighter("armasm", "", "");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("armasm",
"""
; héllo
mov r0, #1 @ ünïcode
""",
"""
<span class="hljs-comment">; héllo</span>
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">r0</span>, <span class="hljs-number">#1</span> <span class="hljs-comment">@ ünïcode</span>
""");
    }

    [Fact]
    public void AliasArm()
    {
        AssertHighlighter("arm",
"""
    mov r0, #1
    bx lr
""",
"""
    <span class="hljs-keyword">mov</span> <span class="hljs-built_in">r0</span>, <span class="hljs-number">#1</span>
    <span class="hljs-keyword">bx</span> <span class="hljs-built_in">lr</span>
""");
    }

    [Fact]
    public void OrOperator()
    {
        AssertHighlighter("armasm",
"""
    mov r0, #(1 | 2)
    mov r1, #3
""",
"""
    <span class="hljs-keyword">mov</span> <span class="hljs-built_in">r0</span>, #(<span class="hljs-number">1</span> | <span class="hljs-number">2</span>)
    <span class="hljs-keyword">mov</span> <span class="hljs-built_in">r1</span>, <span class="hljs-number">#3</span>
""");
    }
}
