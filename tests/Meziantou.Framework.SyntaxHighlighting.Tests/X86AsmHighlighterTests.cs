namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class X86AsmHighlighterTests
{

    [Fact]
    public void DataMov_MovImm()
    {
        AssertHighlighter("x86asm",
"""
mov eax, 42
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">42</span>
""");
    }

    [Fact]
    public void DataMov_MovReg()
    {
        AssertHighlighter("x86asm",
"""
mov eax, ebx
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, <span class="hljs-built_in">ebx</span>
""");
    }

    [Fact]
    public void DataMov_MovMem()
    {
        AssertHighlighter("x86asm",
"""
mov eax, [ebx]
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, [<span class="hljs-built_in">ebx</span>]
""");
    }

    [Fact]
    public void DataMov_MovMemDisp()
    {
        AssertHighlighter("x86asm",
"""
mov eax, [ebx+4]
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, [<span class="hljs-built_in">ebx</span>+<span class="hljs-number">4</span>]
""");
    }

    [Fact]
    public void DataMov_MovMemIndex()
    {
        AssertHighlighter("x86asm",
"""
mov eax, [ebx+ecx*4]
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, [<span class="hljs-built_in">ebx</span>+<span class="hljs-built_in">ecx</span>*<span class="hljs-number">4</span>]
""");
    }

    [Fact]
    public void DataMov_MovMemFull()
    {
        AssertHighlighter("x86asm",
"""
mov eax, [ebx+ecx*4+0x10]
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, [<span class="hljs-built_in">ebx</span>+<span class="hljs-built_in">ecx</span>*<span class="hljs-number">4</span>+<span class="hljs-number">0x10</span>]
""");
    }

    [Fact]
    public void DataMov_MovBytePtr()
    {
        AssertHighlighter("x86asm",
"""
mov byte [edi], 0
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">byte</span> [<span class="hljs-built_in">edi</span>], <span class="hljs-number">0</span>
""");
    }

    [Fact]
    public void DataMov_MovWordPtr()
    {
        AssertHighlighter("x86asm",
"""
mov word [edi], 0xFFFF
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">word</span> [<span class="hljs-built_in">edi</span>], <span class="hljs-number">0xFFFF</span>
""");
    }

    [Fact]
    public void DataMov_MovDwordPtr()
    {
        AssertHighlighter("x86asm",
"""
mov dword [edi], 0xDEADBEEF
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">dword</span> [<span class="hljs-built_in">edi</span>], <span class="hljs-number">0xDEADBEEF</span>
""");
    }

    [Fact]
    public void DataMov_MovQwordPtr()
    {
        AssertHighlighter("x86asm",
"""
mov qword [rdi], rax
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">qword</span> [<span class="hljs-built_in">rdi</span>], <span class="hljs-built_in">rax</span>
""");
    }

    [Fact]
    public void DataMov_MovLabel()
    {
        AssertHighlighter("x86asm",
"""
mov eax, [data]
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, [data]
""");
    }

    [Fact]
    public void DataMov_Lea()
    {
        AssertHighlighter("x86asm",
"""
lea eax, [ebx+ecx*4+0x10]
""",
"""
<span class="hljs-keyword">lea</span> <span class="hljs-built_in">eax</span>, [<span class="hljs-built_in">ebx</span>+<span class="hljs-built_in">ecx</span>*<span class="hljs-number">4</span>+<span class="hljs-number">0x10</span>]
""");
    }

    [Fact]
    public void DataMov_Push()
    {
        AssertHighlighter("x86asm",
"""
push eax
""",
"""
<span class="hljs-keyword">push</span> <span class="hljs-built_in">eax</span>
""");
    }

    [Fact]
    public void DataMov_PushImm()
    {
        AssertHighlighter("x86asm",
"""
push 0x10
""",
"""
<span class="hljs-keyword">push</span> <span class="hljs-number">0x10</span>
""");
    }

    [Fact]
    public void DataMov_Pop()
    {
        AssertHighlighter("x86asm",
"""
pop eax
""",
"""
<span class="hljs-keyword">pop</span> <span class="hljs-built_in">eax</span>
""");
    }

    [Fact]
    public void DataMov_Xchg()
    {
        AssertHighlighter("x86asm",
"""
xchg eax, ebx
""",
"""
<span class="hljs-keyword">xchg</span> <span class="hljs-built_in">eax</span>, <span class="hljs-built_in">ebx</span>
""");
    }

    [Fact]
    public void DataMov_Movzx()
    {
        AssertHighlighter("x86asm",
"""
movzx eax, byte [esi]
""",
"""
<span class="hljs-keyword">movzx</span> <span class="hljs-built_in">eax</span>, <span class="hljs-built_in">byte</span> [<span class="hljs-built_in">esi</span>]
""");
    }

    [Fact]
    public void DataMov_Movsx()
    {
        AssertHighlighter("x86asm",
"""
movsx eax, word [esi]
""",
"""
<span class="hljs-keyword">movsx</span> <span class="hljs-built_in">eax</span>, <span class="hljs-built_in">word</span> [<span class="hljs-built_in">esi</span>]
""");
    }

    [Fact]
    public void Arithmetic_Add()
    {
        AssertHighlighter("x86asm",
"""
add eax, 1
""",
"""
<span class="hljs-keyword">add</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Arithmetic_AddReg()
    {
        AssertHighlighter("x86asm",
"""
add eax, ebx
""",
"""
<span class="hljs-keyword">add</span> <span class="hljs-built_in">eax</span>, <span class="hljs-built_in">ebx</span>
""");
    }

    [Fact]
    public void Arithmetic_Sub()
    {
        AssertHighlighter("x86asm",
"""
sub eax, 1
""",
"""
<span class="hljs-keyword">sub</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Arithmetic_Mul()
    {
        AssertHighlighter("x86asm",
"""
mul ebx
""",
"""
<span class="hljs-keyword">mul</span> <span class="hljs-built_in">ebx</span>
""");
    }

    [Fact]
    public void Arithmetic_Imul()
    {
        AssertHighlighter("x86asm",
"""
imul eax, ebx, 3
""",
"""
<span class="hljs-keyword">imul</span> <span class="hljs-built_in">eax</span>, <span class="hljs-built_in">ebx</span>, <span class="hljs-number">3</span>
""");
    }

    [Fact]
    public void Arithmetic_Div()
    {
        AssertHighlighter("x86asm",
"""
div ebx
""",
"""
<span class="hljs-keyword">div</span> <span class="hljs-built_in">ebx</span>
""");
    }

    [Fact]
    public void Arithmetic_Idiv()
    {
        AssertHighlighter("x86asm",
"""
idiv ebx
""",
"""
<span class="hljs-keyword">idiv</span> <span class="hljs-built_in">ebx</span>
""");
    }

    [Fact]
    public void Arithmetic_Inc()
    {
        AssertHighlighter("x86asm",
"""
inc eax
""",
"""
<span class="hljs-keyword">inc</span> <span class="hljs-built_in">eax</span>
""");
    }

    [Fact]
    public void Arithmetic_Dec()
    {
        AssertHighlighter("x86asm",
"""
dec eax
""",
"""
<span class="hljs-keyword">dec</span> <span class="hljs-built_in">eax</span>
""");
    }

    [Fact]
    public void Arithmetic_Neg()
    {
        AssertHighlighter("x86asm",
"""
neg eax
""",
"""
<span class="hljs-keyword">neg</span> <span class="hljs-built_in">eax</span>
""");
    }

    [Fact]
    public void Arithmetic_Adc()
    {
        AssertHighlighter("x86asm",
"""
adc eax, ebx
""",
"""
<span class="hljs-keyword">adc</span> <span class="hljs-built_in">eax</span>, <span class="hljs-built_in">ebx</span>
""");
    }

    [Fact]
    public void Arithmetic_Sbb()
    {
        AssertHighlighter("x86asm",
"""
sbb eax, ebx
""",
"""
<span class="hljs-keyword">sbb</span> <span class="hljs-built_in">eax</span>, <span class="hljs-built_in">ebx</span>
""");
    }

    [Fact]
    public void Logic_And()
    {
        AssertHighlighter("x86asm",
"""
and eax, 0xFF
""",
"""
<span class="hljs-keyword">and</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">0xFF</span>
""");
    }

    [Fact]
    public void Logic_Or()
    {
        AssertHighlighter("x86asm",
"""
or eax, 0x80
""",
"""
<span class="hljs-keyword">or</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">0x80</span>
""");
    }

    [Fact]
    public void Logic_Xor()
    {
        AssertHighlighter("x86asm",
"""
xor eax, eax
""",
"""
<span class="hljs-keyword">xor</span> <span class="hljs-built_in">eax</span>, <span class="hljs-built_in">eax</span>
""");
    }

    [Fact]
    public void Logic_Not()
    {
        AssertHighlighter("x86asm",
"""
not eax
""",
"""
<span class="hljs-keyword">not</span> <span class="hljs-built_in">eax</span>
""");
    }

    [Fact]
    public void Logic_Test()
    {
        AssertHighlighter("x86asm",
"""
test eax, eax
""",
"""
<span class="hljs-keyword">test</span> <span class="hljs-built_in">eax</span>, <span class="hljs-built_in">eax</span>
""");
    }

    [Fact]
    public void Shift_Shl()
    {
        AssertHighlighter("x86asm",
"""
shl eax, 1
""",
"""
<span class="hljs-keyword">shl</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Shift_Shr()
    {
        AssertHighlighter("x86asm",
"""
shr eax, 2
""",
"""
<span class="hljs-keyword">shr</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void Shift_Sar()
    {
        AssertHighlighter("x86asm",
"""
sar eax, 3
""",
"""
<span class="hljs-keyword">sar</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">3</span>
""");
    }

    [Fact]
    public void Shift_Sal()
    {
        AssertHighlighter("x86asm",
"""
sal eax, 4
""",
"""
<span class="hljs-keyword">sal</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">4</span>
""");
    }

    [Fact]
    public void Shift_Rol()
    {
        AssertHighlighter("x86asm",
"""
rol eax, 5
""",
"""
<span class="hljs-keyword">rol</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">5</span>
""");
    }

    [Fact]
    public void Shift_Ror()
    {
        AssertHighlighter("x86asm",
"""
ror eax, 6
""",
"""
<span class="hljs-keyword">ror</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">6</span>
""");
    }

    [Fact]
    public void Shift_Rcl()
    {
        AssertHighlighter("x86asm",
"""
rcl eax, 1
""",
"""
<span class="hljs-keyword">rcl</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Shift_Rcr()
    {
        AssertHighlighter("x86asm",
"""
rcr eax, 1
""",
"""
<span class="hljs-keyword">rcr</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Shift_ShlCl()
    {
        AssertHighlighter("x86asm",
"""
shl eax, cl
""",
"""
<span class="hljs-keyword">shl</span> <span class="hljs-built_in">eax</span>, <span class="hljs-built_in">cl</span>
""");
    }

    [Fact]
    public void Shift_Shrd()
    {
        AssertHighlighter("x86asm",
"""
shrd eax, ebx, 4
""",
"""
<span class="hljs-keyword">shrd</span> <span class="hljs-built_in">eax</span>, <span class="hljs-built_in">ebx</span>, <span class="hljs-number">4</span>
""");
    }

    [Fact]
    public void ControlFlow_Cmp()
    {
        AssertHighlighter("x86asm",
"""
cmp eax, ebx
""",
"""
<span class="hljs-keyword">cmp</span> <span class="hljs-built_in">eax</span>, <span class="hljs-built_in">ebx</span>
""");
    }

    [Fact]
    public void ControlFlow_Jmp()
    {
        AssertHighlighter("x86asm",
"""
jmp .loop
""",
"""
<span class="hljs-keyword">jmp</span> .loop
""");
    }

    [Fact]
    public void ControlFlow_Je()
    {
        AssertHighlighter("x86asm",
"""
je .equal
""",
"""
<span class="hljs-keyword">je</span> .equal
""");
    }

    [Fact]
    public void ControlFlow_Jne()
    {
        AssertHighlighter("x86asm",
"""
jne .not_equal
""",
"""
<span class="hljs-keyword">jne</span> .not_equal
""");
    }

    [Fact]
    public void ControlFlow_Jl()
    {
        AssertHighlighter("x86asm",
"""
jl .less
""",
"""
<span class="hljs-keyword">jl</span> .less
""");
    }

    [Fact]
    public void ControlFlow_Jle()
    {
        AssertHighlighter("x86asm",
"""
jle .less_equal
""",
"""
<span class="hljs-keyword">jle</span> .less_equal
""");
    }

    [Fact]
    public void ControlFlow_Jg()
    {
        AssertHighlighter("x86asm",
"""
jg .greater
""",
"""
<span class="hljs-keyword">jg</span> .greater
""");
    }

    [Fact]
    public void ControlFlow_Jge()
    {
        AssertHighlighter("x86asm",
"""
jge .greater_equal
""",
"""
<span class="hljs-keyword">jge</span> .greater_equal
""");
    }

    [Fact]
    public void ControlFlow_Ja()
    {
        AssertHighlighter("x86asm",
"""
ja .above
""",
"""
<span class="hljs-keyword">ja</span> .above
""");
    }

    [Fact]
    public void ControlFlow_Jb()
    {
        AssertHighlighter("x86asm",
"""
jb .below
""",
"""
<span class="hljs-keyword">jb</span> .below
""");
    }

    [Fact]
    public void ControlFlow_Jc()
    {
        AssertHighlighter("x86asm",
"""
jc .carry
""",
"""
<span class="hljs-keyword">jc</span> .carry
""");
    }

    [Fact]
    public void ControlFlow_Jnc()
    {
        AssertHighlighter("x86asm",
"""
jnc .no_carry
""",
"""
<span class="hljs-keyword">jnc</span> .no_carry
""");
    }

    [Fact]
    public void ControlFlow_Jz()
    {
        AssertHighlighter("x86asm",
"""
jz .zero
""",
"""
<span class="hljs-keyword">jz</span> .zero
""");
    }

    [Fact]
    public void ControlFlow_Jnz()
    {
        AssertHighlighter("x86asm",
"""
jnz .non_zero
""",
"""
<span class="hljs-keyword">jnz</span> .non_zero
""");
    }

    [Fact]
    public void ControlFlow_Js()
    {
        AssertHighlighter("x86asm",
"""
js .negative
""",
"""
<span class="hljs-keyword">js</span> .negative
""");
    }

    [Fact]
    public void ControlFlow_Jo()
    {
        AssertHighlighter("x86asm",
"""
jo .overflow
""",
"""
<span class="hljs-keyword">jo</span> .overflow
""");
    }

    [Fact]
    public void ControlFlow_Loop()
    {
        AssertHighlighter("x86asm",
"""
loop .loop
""",
"""
<span class="hljs-keyword">loop</span> .loop
""");
    }

    [Fact]
    public void ControlFlow_Loope()
    {
        AssertHighlighter("x86asm",
"""
loope .loop
""",
"""
<span class="hljs-keyword">loope</span> .loop
""");
    }

    [Fact]
    public void ControlFlow_Loopne()
    {
        AssertHighlighter("x86asm",
"""
loopne .loop
""",
"""
<span class="hljs-keyword">loopne</span> .loop
""");
    }

    [Fact]
    public void ControlFlow_Call()
    {
        AssertHighlighter("x86asm",
"""
call printf
""",
"""
<span class="hljs-keyword">call</span> printf
""");
    }

    [Fact]
    public void ControlFlow_CallReg()
    {
        AssertHighlighter("x86asm",
"""
call eax
""",
"""
<span class="hljs-keyword">call</span> <span class="hljs-built_in">eax</span>
""");
    }

    [Fact]
    public void ControlFlow_Ret()
    {
        AssertHighlighter("x86asm",
"""
ret
""",
"""
<span class="hljs-keyword">ret</span>
""");
    }

    [Fact]
    public void ControlFlow_RetN()
    {
        AssertHighlighter("x86asm",
"""
ret 8
""",
"""
<span class="hljs-keyword">ret</span> <span class="hljs-number">8</span>
""");
    }

    [Fact]
    public void ControlFlow_Leave()
    {
        AssertHighlighter("x86asm",
"""
leave
""",
"""
<span class="hljs-keyword">leave</span>
""");
    }

    [Fact]
    public void ControlFlow_Enter()
    {
        AssertHighlighter("x86asm",
"""
enter 32, 0
""",
"""
<span class="hljs-keyword">enter</span> <span class="hljs-number">32</span>, <span class="hljs-number">0</span>
""");
    }

    [Fact]
    public void ConditionalSet_Sete()
    {
        AssertHighlighter("x86asm",
"""
sete al
""",
"""
<span class="hljs-keyword">sete</span> <span class="hljs-built_in">al</span>
""");
    }

    [Fact]
    public void ConditionalSet_Setne()
    {
        AssertHighlighter("x86asm",
"""
setne al
""",
"""
<span class="hljs-keyword">setne</span> <span class="hljs-built_in">al</span>
""");
    }

    [Fact]
    public void ConditionalSet_Setl()
    {
        AssertHighlighter("x86asm",
"""
setl al
""",
"""
<span class="hljs-keyword">setl</span> <span class="hljs-built_in">al</span>
""");
    }

    [Fact]
    public void ConditionalSet_Setg()
    {
        AssertHighlighter("x86asm",
"""
setg al
""",
"""
<span class="hljs-keyword">setg</span> <span class="hljs-built_in">al</span>
""");
    }

    [Fact]
    public void ConditionalSet_Cmove()
    {
        AssertHighlighter("x86asm",
"""
cmove eax, ebx
""",
"""
<span class="hljs-keyword">cmove</span> <span class="hljs-built_in">eax</span>, <span class="hljs-built_in">ebx</span>
""");
    }

    [Fact]
    public void ConditionalSet_Cmovne()
    {
        AssertHighlighter("x86asm",
"""
cmovne eax, ebx
""",
"""
<span class="hljs-keyword">cmovne</span> <span class="hljs-built_in">eax</span>, <span class="hljs-built_in">ebx</span>
""");
    }

    [Fact]
    public void ConditionalSet_Cmovl()
    {
        AssertHighlighter("x86asm",
"""
cmovl eax, ebx
""",
"""
<span class="hljs-keyword">cmovl</span> <span class="hljs-built_in">eax</span>, <span class="hljs-built_in">ebx</span>
""");
    }

    [Fact]
    public void String_Movsb()
    {
        AssertHighlighter("x86asm",
"""
movsb
""",
"""
<span class="hljs-keyword">movsb</span>
""");
    }

    [Fact]
    public void String_Movsw()
    {
        AssertHighlighter("x86asm",
"""
movsw
""",
"""
<span class="hljs-keyword">movsw</span>
""");
    }

    [Fact]
    public void String_Movsd()
    {
        AssertHighlighter("x86asm",
"""
movsd
""",
"""
<span class="hljs-keyword">movsd</span>
""");
    }

    [Fact]
    public void String_Lodsb()
    {
        AssertHighlighter("x86asm",
"""
lodsb
""",
"""
<span class="hljs-keyword">lodsb</span>
""");
    }

    [Fact]
    public void String_Stosb()
    {
        AssertHighlighter("x86asm",
"""
stosb
""",
"""
<span class="hljs-keyword">stosb</span>
""");
    }

    [Fact]
    public void String_Scasb()
    {
        AssertHighlighter("x86asm",
"""
scasb
""",
"""
<span class="hljs-keyword">scasb</span>
""");
    }

    [Fact]
    public void String_Cmpsb()
    {
        AssertHighlighter("x86asm",
"""
cmpsb
""",
"""
<span class="hljs-keyword">cmpsb</span>
""");
    }

    [Fact]
    public void String_RepMovsb()
    {
        AssertHighlighter("x86asm",
"""
rep movsb
""",
"""
<span class="hljs-keyword">rep</span> <span class="hljs-keyword">movsb</span>
""");
    }

    [Fact]
    public void String_RepneScasb()
    {
        AssertHighlighter("x86asm",
"""
repne scasb
""",
"""
<span class="hljs-keyword">repne</span> <span class="hljs-keyword">scasb</span>
""");
    }

    [Fact]
    public void Stack_Pushf()
    {
        AssertHighlighter("x86asm",
"""
pushf
""",
"""
<span class="hljs-keyword">pushf</span>
""");
    }

    [Fact]
    public void Stack_Popf()
    {
        AssertHighlighter("x86asm",
"""
popf
""",
"""
<span class="hljs-keyword">popf</span>
""");
    }

    [Fact]
    public void Stack_Pushad()
    {
        AssertHighlighter("x86asm",
"""
pushad
""",
"""
<span class="hljs-keyword">pushad</span>
""");
    }

    [Fact]
    public void Stack_Popad()
    {
        AssertHighlighter("x86asm",
"""
popad
""",
"""
<span class="hljs-keyword">popad</span>
""");
    }

    [Fact]
    public void Bit_Bt()
    {
        AssertHighlighter("x86asm",
"""
bt eax, 0
""",
"""
<span class="hljs-keyword">bt</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">0</span>
""");
    }

    [Fact]
    public void Bit_Bts()
    {
        AssertHighlighter("x86asm",
"""
bts eax, 3
""",
"""
<span class="hljs-keyword">bts</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">3</span>
""");
    }

    [Fact]
    public void Bit_Btr()
    {
        AssertHighlighter("x86asm",
"""
btr eax, 3
""",
"""
<span class="hljs-keyword">btr</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">3</span>
""");
    }

    [Fact]
    public void Bit_Btc()
    {
        AssertHighlighter("x86asm",
"""
btc eax, 3
""",
"""
<span class="hljs-keyword">btc</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">3</span>
""");
    }

    [Fact]
    public void Bit_Bsf()
    {
        AssertHighlighter("x86asm",
"""
bsf ebx, eax
""",
"""
<span class="hljs-keyword">bsf</span> <span class="hljs-built_in">ebx</span>, <span class="hljs-built_in">eax</span>
""");
    }

    [Fact]
    public void Bit_Bsr()
    {
        AssertHighlighter("x86asm",
"""
bsr ebx, eax
""",
"""
<span class="hljs-keyword">bsr</span> <span class="hljs-built_in">ebx</span>, <span class="hljs-built_in">eax</span>
""");
    }

    [Fact]
    public void Bit_Popcnt()
    {
        AssertHighlighter("x86asm",
"""
popcnt eax, ebx
""",
"""
<span class="hljs-keyword">popcnt</span> <span class="hljs-built_in">eax</span>, <span class="hljs-built_in">ebx</span>
""");
    }

    [Fact]
    public void Bit_Lzcnt()
    {
        AssertHighlighter("x86asm",
"""
lzcnt eax, ebx
""",
"""
<span class="hljs-keyword">lzcnt</span> <span class="hljs-built_in">eax</span>, <span class="hljs-built_in">ebx</span>
""");
    }

    [Fact]
    public void Bit_Tzcnt()
    {
        AssertHighlighter("x86asm",
"""
tzcnt eax, ebx
""",
"""
<span class="hljs-keyword">tzcnt</span> <span class="hljs-built_in">eax</span>, <span class="hljs-built_in">ebx</span>
""");
    }

    [Fact]
    public void Atomic_LockAdd()
    {
        AssertHighlighter("x86asm",
"""
lock add [counter], 1
""",
"""
<span class="hljs-keyword">lock</span> <span class="hljs-keyword">add</span> [counter], <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Atomic_LockInc()
    {
        AssertHighlighter("x86asm",
"""
lock inc dword [counter]
""",
"""
<span class="hljs-keyword">lock</span> <span class="hljs-keyword">inc</span> <span class="hljs-built_in">dword</span> [counter]
""");
    }

    [Fact]
    public void Atomic_XchgMem()
    {
        AssertHighlighter("x86asm",
"""
xchg eax, [lock_addr]
""",
"""
<span class="hljs-keyword">xchg</span> <span class="hljs-built_in">eax</span>, [lock_addr]
""");
    }

    [Fact]
    public void Atomic_CmpXchg()
    {
        AssertHighlighter("x86asm",
"""
lock cmpxchg [ptr], ebx
""",
"""
<span class="hljs-keyword">lock</span> <span class="hljs-keyword">cmpxchg</span> [<span class="hljs-built_in">ptr</span>], <span class="hljs-built_in">ebx</span>
""");
    }

    [Fact]
    public void Atomic_CmpXchg8b()
    {
        AssertHighlighter("x86asm",
"""
lock cmpxchg8b [ptr]
""",
"""
<span class="hljs-keyword">lock</span> <span class="hljs-keyword">cmpxchg8b</span> [<span class="hljs-built_in">ptr</span>]
""");
    }

    [Fact]
    public void Atomic_CmpXchg16b()
    {
        AssertHighlighter("x86asm",
"""
lock cmpxchg16b [ptr]
""",
"""
<span class="hljs-keyword">lock</span> <span class="hljs-keyword">cmpxchg16b</span> [<span class="hljs-built_in">ptr</span>]
""");
    }

    [Fact]
    public void Atomic_Mfence()
    {
        AssertHighlighter("x86asm",
"""
mfence
""",
"""
<span class="hljs-keyword">mfence</span>
""");
    }

    [Fact]
    public void Atomic_Lfence()
    {
        AssertHighlighter("x86asm",
"""
lfence
""",
"""
<span class="hljs-keyword">lfence</span>
""");
    }

    [Fact]
    public void Atomic_Sfence()
    {
        AssertHighlighter("x86asm",
"""
sfence
""",
"""
<span class="hljs-keyword">sfence</span>
""");
    }

    [Fact]
    public void SystemControl_Nop()
    {
        AssertHighlighter("x86asm",
"""
nop
""",
"""
<span class="hljs-keyword">nop</span>
""");
    }

    [Fact]
    public void SystemControl_Hlt()
    {
        AssertHighlighter("x86asm",
"""
hlt
""",
"""
<span class="hljs-keyword">hlt</span>
""");
    }

    [Fact]
    public void SystemControl_Cli()
    {
        AssertHighlighter("x86asm",
"""
cli
""",
"""
<span class="hljs-keyword">cli</span>
""");
    }

    [Fact]
    public void SystemControl_Sti()
    {
        AssertHighlighter("x86asm",
"""
sti
""",
"""
<span class="hljs-keyword">sti</span>
""");
    }

    [Fact]
    public void SystemControl_Cld()
    {
        AssertHighlighter("x86asm",
"""
cld
""",
"""
<span class="hljs-keyword">cld</span>
""");
    }

    [Fact]
    public void SystemControl_Std()
    {
        AssertHighlighter("x86asm",
"""
std
""",
"""
<span class="hljs-keyword">std</span>
""");
    }

    [Fact]
    public void SystemControl_Int()
    {
        AssertHighlighter("x86asm",
"""
int 0x80
""",
"""
<span class="hljs-keyword">int</span> <span class="hljs-number">0x80</span>
""");
    }

    [Fact]
    public void SystemControl_Syscall()
    {
        AssertHighlighter("x86asm",
"""
syscall
""",
"""
<span class="hljs-keyword">syscall</span>
""");
    }

    [Fact]
    public void SystemControl_Sysenter()
    {
        AssertHighlighter("x86asm",
"""
sysenter
""",
"""
<span class="hljs-keyword">sysenter</span>
""");
    }

    [Fact]
    public void SystemControl_Iret()
    {
        AssertHighlighter("x86asm",
"""
iret
""",
"""
<span class="hljs-keyword">iret</span>
""");
    }

    [Fact]
    public void SystemControl_Cpuid()
    {
        AssertHighlighter("x86asm",
"""
cpuid
""",
"""
<span class="hljs-keyword">cpuid</span>
""");
    }

    [Fact]
    public void SystemControl_Rdtsc()
    {
        AssertHighlighter("x86asm",
"""
rdtsc
""",
"""
<span class="hljs-keyword">rdtsc</span>
""");
    }

    [Fact]
    public void SystemControl_Rdmsr()
    {
        AssertHighlighter("x86asm",
"""
rdmsr
""",
"""
<span class="hljs-keyword">rdmsr</span>
""");
    }

    [Fact]
    public void SystemControl_Wrmsr()
    {
        AssertHighlighter("x86asm",
"""
wrmsr
""",
"""
<span class="hljs-keyword">wrmsr</span>
""");
    }

    [Fact]
    public void Fpu_Fld()
    {
        AssertHighlighter("x86asm",
"""
fld qword [x]
""",
"""
<span class="hljs-keyword">fld</span> <span class="hljs-built_in">qword</span> [x]
""");
    }

    [Fact]
    public void Fpu_Fst()
    {
        AssertHighlighter("x86asm",
"""
fst qword [y]
""",
"""
<span class="hljs-keyword">fst</span> <span class="hljs-built_in">qword</span> [y]
""");
    }

    [Fact]
    public void Fpu_Fadd()
    {
        AssertHighlighter("x86asm",
"""
fadd st0, st1
""",
"""
<span class="hljs-keyword">fadd</span> <span class="hljs-built_in">st0</span>, <span class="hljs-built_in">st1</span>
""");
    }

    [Fact]
    public void Fpu_Fsub()
    {
        AssertHighlighter("x86asm",
"""
fsub st0, st1
""",
"""
<span class="hljs-keyword">fsub</span> <span class="hljs-built_in">st0</span>, <span class="hljs-built_in">st1</span>
""");
    }

    [Fact]
    public void Fpu_Fmul()
    {
        AssertHighlighter("x86asm",
"""
fmul st0, st1
""",
"""
<span class="hljs-keyword">fmul</span> <span class="hljs-built_in">st0</span>, <span class="hljs-built_in">st1</span>
""");
    }

    [Fact]
    public void Fpu_Fdiv()
    {
        AssertHighlighter("x86asm",
"""
fdiv st0, st1
""",
"""
<span class="hljs-keyword">fdiv</span> <span class="hljs-built_in">st0</span>, <span class="hljs-built_in">st1</span>
""");
    }

    [Fact]
    public void Fpu_Fsin()
    {
        AssertHighlighter("x86asm",
"""
fsin
""",
"""
<span class="hljs-keyword">fsin</span>
""");
    }

    [Fact]
    public void Fpu_Fcos()
    {
        AssertHighlighter("x86asm",
"""
fcos
""",
"""
<span class="hljs-keyword">fcos</span>
""");
    }

    [Fact]
    public void Sse_MovssXmm()
    {
        AssertHighlighter("x86asm",
"""
movss xmm0, [x]
""",
"""
<span class="hljs-keyword">movss</span> <span class="hljs-built_in">xmm0</span>, [x]
""");
    }

    [Fact]
    public void Sse_MovapsXmm()
    {
        AssertHighlighter("x86asm",
"""
movaps xmm0, [x]
""",
"""
<span class="hljs-keyword">movaps</span> <span class="hljs-built_in">xmm0</span>, [x]
""");
    }

    [Fact]
    public void Sse_AddssXmm()
    {
        AssertHighlighter("x86asm",
"""
addss xmm0, xmm1
""",
"""
<span class="hljs-keyword">addss</span> <span class="hljs-built_in">xmm0</span>, <span class="hljs-built_in">xmm1</span>
""");
    }

    [Fact]
    public void Sse_AddpsXmm()
    {
        AssertHighlighter("x86asm",
"""
addps xmm0, xmm1
""",
"""
<span class="hljs-keyword">addps</span> <span class="hljs-built_in">xmm0</span>, <span class="hljs-built_in">xmm1</span>
""");
    }

    [Fact]
    public void Sse_MovdqaXmm()
    {
        AssertHighlighter("x86asm",
"""
movdqa xmm0, [x]
""",
"""
<span class="hljs-keyword">movdqa</span> <span class="hljs-built_in">xmm0</span>, [x]
""");
    }

    [Fact]
    public void Sse_PadddXmm()
    {
        AssertHighlighter("x86asm",
"""
paddd xmm0, xmm1
""",
"""
<span class="hljs-keyword">paddd</span> <span class="hljs-built_in">xmm0</span>, <span class="hljs-built_in">xmm1</span>
""");
    }

    [Fact]
    public void Sse_PxorXmm()
    {
        AssertHighlighter("x86asm",
"""
pxor xmm0, xmm0
""",
"""
<span class="hljs-keyword">pxor</span> <span class="hljs-built_in">xmm0</span>, <span class="hljs-built_in">xmm0</span>
""");
    }

    [Fact]
    public void Sse_ShufpsXmm()
    {
        AssertHighlighter("x86asm",
"""
shufps xmm0, xmm1, 0x1B
""",
"""
<span class="hljs-keyword">shufps</span> <span class="hljs-built_in">xmm0</span>, <span class="hljs-built_in">xmm1</span>, <span class="hljs-number">0x1B</span>
""");
    }

    [Fact]
    public void Avx_VmovapsYmm()
    {
        AssertHighlighter("x86asm",
"""
vmovaps ymm0, [x]
""",
"""
<span class="hljs-keyword">vmovaps</span> <span class="hljs-built_in">ymm0</span>, [x]
""");
    }

    [Fact]
    public void Avx_VaddpsYmm()
    {
        AssertHighlighter("x86asm",
"""
vaddps ymm0, ymm1, ymm2
""",
"""
<span class="hljs-keyword">vaddps</span> <span class="hljs-built_in">ymm0</span>, <span class="hljs-built_in">ymm1</span>, <span class="hljs-built_in">ymm2</span>
""");
    }

    [Fact]
    public void Avx_VpandYmm()
    {
        AssertHighlighter("x86asm",
"""
vpand ymm0, ymm1, ymm2
""",
"""
<span class="hljs-keyword">vpand</span> <span class="hljs-built_in">ymm0</span>, <span class="hljs-built_in">ymm1</span>, <span class="hljs-built_in">ymm2</span>
""");
    }

    [Fact]
    public void Avx_VbroadcastSs()
    {
        AssertHighlighter("x86asm",
"""
vbroadcastss ymm0, [x]
""",
"""
<span class="hljs-keyword">vbroadcastss</span> <span class="hljs-built_in">ymm0</span>, [x]
""");
    }

    [Fact]
    public void Avx512_VmovapsZmm()
    {
        AssertHighlighter("x86asm",
"""
vmovaps zmm0, [x]
""",
"""
<span class="hljs-keyword">vmovaps</span> <span class="hljs-built_in">zmm0</span>, [x]
""");
    }

    [Fact]
    public void Avx512_VaddpsMask()
    {
        AssertHighlighter("x86asm",
"""
vaddps zmm0 {k1}, zmm1, zmm2
""",
"""
<span class="hljs-keyword">vaddps</span> <span class="hljs-built_in">zmm0</span> {<span class="hljs-built_in">k1</span>}, <span class="hljs-built_in">zmm1</span>, <span class="hljs-built_in">zmm2</span>
""");
    }

    [Fact]
    public void Register_Eight()
    {
        AssertHighlighter("x86asm",
"""
mov al, 1
mov ah, 2
mov bl, 3
mov bh, 4
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">al</span>, <span class="hljs-number">1</span>
<span class="hljs-keyword">mov</span> <span class="hljs-number">ah</span>, <span class="hljs-number">2</span>
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">bl</span>, <span class="hljs-number">3</span>
<span class="hljs-keyword">mov</span> <span class="hljs-number">bh</span>, <span class="hljs-number">4</span>
""");
    }

    [Fact]
    public void Register_Sixteen()
    {
        AssertHighlighter("x86asm",
"""
mov ax, 0x1000
mov bx, 0x2000
mov cx, 0
mov dx, 0
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">ax</span>, <span class="hljs-number">0x1000</span>
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">bx</span>, <span class="hljs-number">0x2000</span>
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">cx</span>, <span class="hljs-number">0</span>
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">dx</span>, <span class="hljs-number">0</span>
""");
    }

    [Fact]
    public void Register_Index16()
    {
        AssertHighlighter("x86asm",
"""
mov si, 0
mov di, 0
mov bp, sp
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">si</span>, <span class="hljs-number">0</span>
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">di</span>, <span class="hljs-number">0</span>
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">bp</span>, <span class="hljs-built_in">sp</span>
""");
    }

    [Fact]
    public void Register_ThirtyTwo()
    {
        AssertHighlighter("x86asm",
"""
mov eax, 0
mov ebx, 1
mov ecx, 2
mov edx, 3
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">0</span>
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">ebx</span>, <span class="hljs-number">1</span>
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">ecx</span>, <span class="hljs-number">2</span>
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">edx</span>, <span class="hljs-number">3</span>
""");
    }

    [Fact]
    public void Register_SixtyFour()
    {
        AssertHighlighter("x86asm",
"""
mov rax, 0
mov rbx, 1
mov rcx, 2
mov rdx, 3
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">rax</span>, <span class="hljs-number">0</span>
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">rbx</span>, <span class="hljs-number">1</span>
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">rcx</span>, <span class="hljs-number">2</span>
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">rdx</span>, <span class="hljs-number">3</span>
""");
    }

    [Fact]
    public void Register_R8R15()
    {
        AssertHighlighter("x86asm",
"""
mov r8, 0
mov r9, 1
mov r10d, 2
mov r11w, 3
mov r12b, 4
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">r8</span>, <span class="hljs-number">0</span>
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">r9</span>, <span class="hljs-number">1</span>
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">r10d</span>, <span class="hljs-number">2</span>
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">r11w</span>, <span class="hljs-number">3</span>
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">r12b</span>, <span class="hljs-number">4</span>
""");
    }

    [Fact]
    public void Register_Segment()
    {
        AssertHighlighter("x86asm",
"""
mov ax, cs
mov ds, ax
mov es, ax
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">ax</span>, <span class="hljs-built_in">cs</span>
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">ds</span>, <span class="hljs-built_in">ax</span>
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">es</span>, <span class="hljs-built_in">ax</span>
""");
    }

    [Fact]
    public void Register_FsGs()
    {
        AssertHighlighter("x86asm",
"""
mov rax, [fs:0]
mov rbx, [gs:0x28]
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">rax</span>, [<span class="hljs-built_in">fs</span>:<span class="hljs-number">0</span>]
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">rbx</span>, [<span class="hljs-built_in">gs</span>:<span class="hljs-number">0x28</span>]
""");
    }

    [Fact]
    public void Register_Control()
    {
        AssertHighlighter("x86asm",
"""
mov rax, cr3
mov cr3, rbx
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">rax</span>, <span class="hljs-built_in">cr3</span>
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">cr3</span>, <span class="hljs-built_in">rbx</span>
""");
    }

    [Fact]
    public void Register_Debug()
    {
        AssertHighlighter("x86asm",
"""
mov rax, dr0
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">rax</span>, <span class="hljs-built_in">dr0</span>
""");
    }

    [Fact]
    public void Register_XmmAll()
    {
        AssertHighlighter("x86asm",
"""
movaps xmm0, xmm15
""",
"""
<span class="hljs-keyword">movaps</span> <span class="hljs-built_in">xmm0</span>, <span class="hljs-built_in">xmm15</span>
""");
    }

    [Fact]
    public void Register_YmmAll()
    {
        AssertHighlighter("x86asm",
"""
vmovaps ymm0, ymm15
""",
"""
<span class="hljs-keyword">vmovaps</span> <span class="hljs-built_in">ymm0</span>, <span class="hljs-built_in">ymm15</span>
""");
    }

    [Fact]
    public void Register_ZmmAll()
    {
        AssertHighlighter("x86asm",
"""
vmovaps zmm0, zmm31
""",
"""
<span class="hljs-keyword">vmovaps</span> <span class="hljs-built_in">zmm0</span>, <span class="hljs-built_in">zmm31</span>
""");
    }

    [Fact]
    public void Register_FpuStack()
    {
        AssertHighlighter("x86asm",
"""
fld st0
fld st7
""",
"""
<span class="hljs-keyword">fld</span> <span class="hljs-built_in">st0</span>
<span class="hljs-keyword">fld</span> <span class="hljs-built_in">st7</span>
""");
    }

    [Fact]
    public void Register_MmxRegs()
    {
        AssertHighlighter("x86asm",
"""
movd mm0, eax
movq mm1, mm2
""",
"""
<span class="hljs-keyword">movd</span> <span class="hljs-built_in">mm0</span>, <span class="hljs-built_in">eax</span>
<span class="hljs-keyword">movq</span> <span class="hljs-built_in">mm1</span>, <span class="hljs-built_in">mm2</span>
""");
    }

    [Fact]
    public void AddressingMode_Direct()
    {
        AssertHighlighter("x86asm",
"""
mov eax, [0x1000]
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, [<span class="hljs-number">0x1000</span>]
""");
    }

    [Fact]
    public void AddressingMode_Indirect()
    {
        AssertHighlighter("x86asm",
"""
mov eax, [ebx]
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, [<span class="hljs-built_in">ebx</span>]
""");
    }

    [Fact]
    public void AddressingMode_BaseDisp()
    {
        AssertHighlighter("x86asm",
"""
mov eax, [ebx+8]
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, [<span class="hljs-built_in">ebx</span>+<span class="hljs-number">8</span>]
""");
    }

    [Fact]
    public void AddressingMode_BaseDispNeg()
    {
        AssertHighlighter("x86asm",
"""
mov eax, [ebp-8]
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, [<span class="hljs-built_in">ebp</span>-<span class="hljs-number">8</span>]
""");
    }

    [Fact]
    public void AddressingMode_BaseIndex()
    {
        AssertHighlighter("x86asm",
"""
mov eax, [ebx+ecx]
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, [<span class="hljs-built_in">ebx</span>+<span class="hljs-built_in">ecx</span>]
""");
    }

    [Fact]
    public void AddressingMode_BaseIndexScale()
    {
        AssertHighlighter("x86asm",
"""
mov eax, [ebx+ecx*8]
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, [<span class="hljs-built_in">ebx</span>+<span class="hljs-built_in">ecx</span>*<span class="hljs-number">8</span>]
""");
    }

    [Fact]
    public void AddressingMode_BaseIndexScaleDisp()
    {
        AssertHighlighter("x86asm",
"""
mov eax, [ebx+ecx*4+12]
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, [<span class="hljs-built_in">ebx</span>+<span class="hljs-built_in">ecx</span>*<span class="hljs-number">4</span>+<span class="hljs-number">12</span>]
""");
    }

    [Fact]
    public void AddressingMode_RipRelative()
    {
        AssertHighlighter("x86asm",
"""
mov rax, [rel data]
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">rax</span>, [<span class="hljs-built_in">rel</span> data]
""");
    }

    [Fact]
    public void AddressingMode_SegmentOverride()
    {
        AssertHighlighter("x86asm",
"""
mov eax, [fs:0x18]
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, [<span class="hljs-built_in">fs</span>:<span class="hljs-number">0x18</span>]
""");
    }

    [Fact]
    public void AddressingMode_LabelArith()
    {
        AssertHighlighter("x86asm",
"""
mov eax, [data+4]
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, [data+<span class="hljs-number">4</span>]
""");
    }

    [Fact]
    public void Number_Decimal()
    {
        AssertHighlighter("x86asm",
"""
mov eax, 42
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">42</span>
""");
    }

    [Fact]
    public void Number_Hex0x()
    {
        AssertHighlighter("x86asm",
"""
mov eax, 0xDEADBEEF
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">0xDEADBEEF</span>
""");
    }

    [Fact]
    public void Number_HexH()
    {
        AssertHighlighter("x86asm",
"""
mov eax, 0DEADBEEFh
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">0DEADBEEFh</span>
""");
    }

    [Fact]
    public void Number_HexDollar()
    {
        AssertHighlighter("x86asm",
"""
mov eax, $DEADBEEF
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, $DEADBEEF
""");
    }

    [Fact]
    public void Number_Binary0b()
    {
        AssertHighlighter("x86asm",
"""
mov eax, 0b10101100
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">0b10101100</span>
""");
    }

    [Fact]
    public void Number_BinaryB()
    {
        AssertHighlighter("x86asm",
"""
mov eax, 10101100b
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">10101100b</span>
""");
    }

    [Fact]
    public void Number_Octal0o()
    {
        AssertHighlighter("x86asm",
"""
mov eax, 0o755
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">0o755</span>
""");
    }

    [Fact]
    public void Number_OctalQ()
    {
        AssertHighlighter("x86asm",
"""
mov eax, 755q
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">755q</span>
""");
    }

    [Fact]
    public void Number_Negative()
    {
        AssertHighlighter("x86asm",
"""
mov eax, -1
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, -<span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Number_CharLiteral()
    {
        AssertHighlighter("x86asm",
"""
mov al, 'A'
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">al</span>, <span class="hljs-string">&#x27;A&#x27;</span>
""");
    }

    [Fact]
    public void Number_FloatConst()
    {
        AssertHighlighter("x86asm",
"""
flt dq 3.14159
""",
"""
flt <span class="hljs-built_in">dq</span> <span class="hljs-number">3.14159</span>
""");
    }

    [Fact]
    public void Label_Global()
    {
        AssertHighlighter("x86asm",
"""
main:
  mov eax, 0
  ret
""",
"""
<span class="hljs-symbol">main:</span>
  <span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">0</span>
  <span class="hljs-keyword">ret</span>
""");
    }

    [Fact]
    public void Label_Local()
    {
        AssertHighlighter("x86asm",
"""
.loop:
  dec ecx
  jnz .loop
""",
"""
<span class="hljs-symbol">.loop:</span>
  <span class="hljs-keyword">dec</span> <span class="hljs-built_in">ecx</span>
  <span class="hljs-keyword">jnz</span> .loop
""");
    }

    [Fact]
    public void Label_WithColon()
    {
        AssertHighlighter("x86asm",
"""
start:
""",
"""
<span class="hljs-symbol">start:</span>
""");
    }

    [Fact]
    public void Label_NumberStart()
    {
        AssertHighlighter("x86asm",
"""
_42:
""",
"""
<span class="hljs-symbol">_42:</span>
""");
    }

    [Fact]
    public void Label_Anonymous()
    {
        AssertHighlighter("x86asm",
"""
@@:
  jmp @b
""",
"""
@@:
  <span class="hljs-keyword">jmp</span> @b
""");
    }

    [Fact]
    public void Directive_SectionText()
    {
        AssertHighlighter("x86asm",
"""
section .text
""",
"""
<span class="hljs-meta">section</span> .text
""");
    }

    [Fact]
    public void Directive_SectionData()
    {
        AssertHighlighter("x86asm",
"""
section .data
""",
"""
<span class="hljs-meta">section</span> .data
""");
    }

    [Fact]
    public void Directive_SectionBss()
    {
        AssertHighlighter("x86asm",
"""
section .bss
""",
"""
<span class="hljs-meta">section</span> .bss
""");
    }

    [Fact]
    public void Directive_SectionRodata()
    {
        AssertHighlighter("x86asm",
"""
section .rodata
""",
"""
<span class="hljs-meta">section</span> .rodata
""");
    }

    [Fact]
    public void Directive_Global()
    {
        AssertHighlighter("x86asm",
"""
global main
""",
"""
<span class="hljs-meta">global</span> main
""");
    }

    [Fact]
    public void Directive_Extern()
    {
        AssertHighlighter("x86asm",
"""
extern printf
""",
"""
<span class="hljs-meta">extern</span> printf
""");
    }

    [Fact]
    public void Directive_Db()
    {
        AssertHighlighter("x86asm",
"""
msg db 'hello', 0
""",
"""
msg <span class="hljs-built_in">db</span> <span class="hljs-string">&#x27;hello&#x27;</span>, <span class="hljs-number">0</span>
""");
    }

    [Fact]
    public void Directive_Dw()
    {
        AssertHighlighter("x86asm",
"""
words dw 0x1234, 0x5678
""",
"""
words <span class="hljs-built_in">dw</span> <span class="hljs-number">0x1234</span>, <span class="hljs-number">0x5678</span>
""");
    }

    [Fact]
    public void Directive_Dd()
    {
        AssertHighlighter("x86asm",
"""
dwords dd 0xDEADBEEF, 0xCAFEBABE
""",
"""
dwords <span class="hljs-built_in">dd</span> <span class="hljs-number">0xDEADBEEF</span>, <span class="hljs-number">0xCAFEBABE</span>
""");
    }

    [Fact]
    public void Directive_Dq()
    {
        AssertHighlighter("x86asm",
"""
qwords dq 0x0123456789ABCDEF
""",
"""
qwords <span class="hljs-built_in">dq</span> <span class="hljs-number">0x0123456789ABCDEF</span>
""");
    }

    [Fact]
    public void Directive_Resb()
    {
        AssertHighlighter("x86asm",
"""
buffer resb 256
""",
"""
buffer <span class="hljs-built_in">resb</span> <span class="hljs-number">256</span>
""");
    }

    [Fact]
    public void Directive_Resw()
    {
        AssertHighlighter("x86asm",
"""
wbuf resw 128
""",
"""
wbuf <span class="hljs-built_in">resw</span> <span class="hljs-number">128</span>
""");
    }

    [Fact]
    public void Directive_Resd()
    {
        AssertHighlighter("x86asm",
"""
dbuf resd 64
""",
"""
dbuf <span class="hljs-built_in">resd</span> <span class="hljs-number">64</span>
""");
    }

    [Fact]
    public void Directive_Resq()
    {
        AssertHighlighter("x86asm",
"""
qbuf resq 32
""",
"""
qbuf <span class="hljs-built_in">resq</span> <span class="hljs-number">32</span>
""");
    }

    [Fact]
    public void Directive_Equ()
    {
        AssertHighlighter("x86asm",
"""
PAGE_SIZE equ 4096
""",
"""
PAGE_SIZE <span class="hljs-built_in">equ</span> <span class="hljs-number">4096</span>
""");
    }

    [Fact]
    public void Directive_Times()
    {
        AssertHighlighter("x86asm",
"""
fill times 16 db 0
""",
"""
fill <span class="hljs-built_in">times</span> <span class="hljs-number">16</span> <span class="hljs-built_in">db</span> <span class="hljs-number">0</span>
""");
    }

    [Fact]
    public void Directive_BitsDirective()
    {
        AssertHighlighter("x86asm",
"""
bits 64
""",
"""
<span class="hljs-meta">bits</span> <span class="hljs-number">64</span>
""");
    }

    [Fact]
    public void Directive_DefaultRel()
    {
        AssertHighlighter("x86asm",
"""
default rel
""",
"""
<span class="hljs-meta">default</span> <span class="hljs-built_in">rel</span>
""");
    }

    [Fact]
    public void Macro_Define()
    {
        AssertHighlighter("x86asm",
"""
%define MAX_SIZE 1024
""",
"""
<span class="hljs-meta">%define</span> MAX_SIZE <span class="hljs-number">1024</span>
""");
    }

    [Fact]
    public void Macro_DefineParam()
    {
        AssertHighlighter("x86asm",
"""
%define square(x) ((x)*(x))
""",
"""
<span class="hljs-meta">%define</span> square(x) ((x)*(x))
""");
    }

    [Fact]
    public void Macro_Undef()
    {
        AssertHighlighter("x86asm",
"""
%undef MAX_SIZE
""",
"""
<span class="hljs-meta">%undef</span> MAX_SIZE
""");
    }

    [Fact]
    public void Macro_IfDef()
    {
        AssertHighlighter("x86asm",
"""
%ifdef DEBUG
  call debug_print
%endif
""",
"""
%ifdef DEBUG
  <span class="hljs-keyword">call</span> debug_print
<span class="hljs-meta">%endif</span>
""");
    }

    [Fact]
    public void Macro_IfElseEndif()
    {
        AssertHighlighter("x86asm",
"""
%if BITS == 64
  default rel
%else
  bits 32
%endif
""",
"""
<span class="hljs-meta">%if</span> <span class="hljs-meta">BITS</span> == <span class="hljs-number">64</span>
  <span class="hljs-meta">default</span> <span class="hljs-built_in">rel</span>
<span class="hljs-meta">%else</span>
  <span class="hljs-meta">bits</span> <span class="hljs-number">32</span>
<span class="hljs-meta">%endif</span>
""");
    }

    [Fact]
    public void Macro_Include()
    {
        AssertHighlighter("x86asm",
"""
%include "macros.inc"
""",
"""
<span class="hljs-meta">%include</span> <span class="hljs-string">&quot;macros.inc&quot;</span>
""");
    }

    [Fact]
    public void Macro_MacroDefinition()
    {
        AssertHighlighter("x86asm",
"""
%macro PROLOG 0
  push rbp
  mov rbp, rsp
%endmacro
""",
"""
%macro PROLOG <span class="hljs-number">0</span>
  <span class="hljs-keyword">push</span> <span class="hljs-built_in">rbp</span>
  <span class="hljs-keyword">mov</span> <span class="hljs-built_in">rbp</span>, <span class="hljs-built_in">rsp</span>
%endmacro
""");
    }

    [Fact]
    public void Macro_MacroParam()
    {
        AssertHighlighter("x86asm",
"""
%macro SAVE_REGS 1-*
%rep %0
  push %1
%rotate 1
%endrep
%endmacro
""",
"""
%macro SAVE_REGS <span class="hljs-number">1</span>-*
<span class="hljs-meta">%rep</span> <span class="hljs-subst">%0</span>
  <span class="hljs-keyword">push</span> <span class="hljs-subst">%1</span>
<span class="hljs-meta">%rotate</span> <span class="hljs-number">1</span>
<span class="hljs-meta">%endrep</span>
%endmacro
""");
    }

    [Fact]
    public void Comment_Semicolon()
    {
        AssertHighlighter("x86asm",
"""
; this is a comment
""",
"""
<span class="hljs-comment">; this is a comment</span>
""");
    }

    [Fact]
    public void Comment_InlineSemi()
    {
        AssertHighlighter("x86asm",
"""
mov eax, 1   ; load value
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">1</span>   <span class="hljs-comment">; load value</span>
""");
    }

    [Fact]
    public void Comment_BetweenInstructions()
    {
        AssertHighlighter("x86asm",
"""
; setup
mov eax, 0
; loop
loop_start:
""",
"""
<span class="hljs-comment">; setup</span>
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">0</span>
<span class="hljs-comment">; loop</span>
<span class="hljs-symbol">loop_start:</span>
""");
    }

    [Fact]
    public void Composite_HelloWorldLinux64()
    {
        AssertHighlighter("x86asm",
"""
section .data
msg: db "Hello, world!", 10
msg_len equ $ - msg

section .text
global _start

_start:
  mov rax, 1          ; sys_write
  mov rdi, 1          ; stdout
  mov rsi, msg
  mov rdx, msg_len
  syscall

  mov rax, 60         ; sys_exit
  xor rdi, rdi
  syscall
""",
"""
<span class="hljs-meta">section</span> .data
<span class="hljs-symbol">msg:</span> <span class="hljs-built_in">db</span> <span class="hljs-string">&quot;Hello, world!&quot;</span>, <span class="hljs-number">10</span>
msg_len <span class="hljs-built_in">equ</span> $ - msg

<span class="hljs-meta">section</span> .text
<span class="hljs-meta">global</span> _start
<span class="hljs-symbol">
_start:</span>
  <span class="hljs-keyword">mov</span> <span class="hljs-built_in">rax</span>, <span class="hljs-number">1</span>          <span class="hljs-comment">; sys_write</span>
  <span class="hljs-keyword">mov</span> <span class="hljs-built_in">rdi</span>, <span class="hljs-number">1</span>          <span class="hljs-comment">; stdout</span>
  <span class="hljs-keyword">mov</span> <span class="hljs-built_in">rsi</span>, msg
  <span class="hljs-keyword">mov</span> <span class="hljs-built_in">rdx</span>, msg_len
  <span class="hljs-keyword">syscall</span>

  <span class="hljs-keyword">mov</span> <span class="hljs-built_in">rax</span>, <span class="hljs-number">60</span>         <span class="hljs-comment">; sys_exit</span>
  <span class="hljs-keyword">xor</span> <span class="hljs-built_in">rdi</span>, <span class="hljs-built_in">rdi</span>
  <span class="hljs-keyword">syscall</span>
""");
    }

    [Fact]
    public void Composite_FunctionPrologEpilog()
    {
        AssertHighlighter("x86asm",
"""
global add
add:
  push rbp
  mov rbp, rsp
  mov rax, rdi
  add rax, rsi
  pop rbp
  ret
""",
"""
<span class="hljs-meta">global</span> <span class="hljs-keyword">add</span>
<span class="hljs-symbol">add:</span>
  <span class="hljs-keyword">push</span> <span class="hljs-built_in">rbp</span>
  <span class="hljs-keyword">mov</span> <span class="hljs-built_in">rbp</span>, <span class="hljs-built_in">rsp</span>
  <span class="hljs-keyword">mov</span> <span class="hljs-built_in">rax</span>, <span class="hljs-built_in">rdi</span>
  <span class="hljs-keyword">add</span> <span class="hljs-built_in">rax</span>, <span class="hljs-built_in">rsi</span>
  <span class="hljs-keyword">pop</span> <span class="hljs-built_in">rbp</span>
  <span class="hljs-keyword">ret</span>
""");
    }

    [Fact]
    public void Composite_StrlenLoop()
    {
        AssertHighlighter("x86asm",
"""
strlen:
  xor rax, rax
.loop:
  cmp byte [rdi+rax], 0
  je .done
  inc rax
  jmp .loop
.done:
  ret
""",
"""
<span class="hljs-symbol">strlen:</span>
  <span class="hljs-keyword">xor</span> <span class="hljs-built_in">rax</span>, <span class="hljs-built_in">rax</span>
<span class="hljs-symbol">.loop:</span>
  <span class="hljs-keyword">cmp</span> <span class="hljs-built_in">byte</span> [<span class="hljs-built_in">rdi</span>+<span class="hljs-built_in">rax</span>], <span class="hljs-number">0</span>
  <span class="hljs-keyword">je</span> .done
  <span class="hljs-keyword">inc</span> <span class="hljs-built_in">rax</span>
  <span class="hljs-keyword">jmp</span> .loop
<span class="hljs-symbol">.done:</span>
  <span class="hljs-keyword">ret</span>
""");
    }

    [Fact]
    public void Composite_MemcpyRep()
    {
        AssertHighlighter("x86asm",
"""
memcpy:
  mov rcx, rdx
  rep movsb
  ret
""",
"""
<span class="hljs-symbol">memcpy:</span>
  <span class="hljs-keyword">mov</span> <span class="hljs-built_in">rcx</span>, <span class="hljs-built_in">rdx</span>
  <span class="hljs-keyword">rep</span> <span class="hljs-keyword">movsb</span>
  <span class="hljs-keyword">ret</span>
""");
    }

    [Fact]
    public void Composite_SpinLock()
    {
        AssertHighlighter("x86asm",
"""
spin_lock:
.try:
  mov eax, 1
  xchg eax, [rdi]
  test eax, eax
  jnz .try
  ret

spin_unlock:
  mov dword [rdi], 0
  ret
""",
"""
<span class="hljs-symbol">spin_lock:</span>
<span class="hljs-symbol">.try:</span>
  <span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">1</span>
  <span class="hljs-keyword">xchg</span> <span class="hljs-built_in">eax</span>, [<span class="hljs-built_in">rdi</span>]
  <span class="hljs-keyword">test</span> <span class="hljs-built_in">eax</span>, <span class="hljs-built_in">eax</span>
  <span class="hljs-keyword">jnz</span> .try
  <span class="hljs-keyword">ret</span>
<span class="hljs-symbol">
spin_unlock:</span>
  <span class="hljs-keyword">mov</span> <span class="hljs-built_in">dword</span> [<span class="hljs-built_in">rdi</span>], <span class="hljs-number">0</span>
  <span class="hljs-keyword">ret</span>
""");
    }

    [Fact]
    public void Composite_AvxAdd4()
    {
        AssertHighlighter("x86asm",
"""
global add4_f32
add4_f32:
  vmovups xmm0, [rdi]
  vmovups xmm1, [rsi]
  vaddps xmm0, xmm0, xmm1
  vmovups [rdx], xmm0
  ret
""",
"""
<span class="hljs-meta">global</span> add4_f32
<span class="hljs-symbol">add4_f32:</span>
  <span class="hljs-keyword">vmovups</span> <span class="hljs-built_in">xmm0</span>, [<span class="hljs-built_in">rdi</span>]
  <span class="hljs-keyword">vmovups</span> <span class="hljs-built_in">xmm1</span>, [<span class="hljs-built_in">rsi</span>]
  <span class="hljs-keyword">vaddps</span> <span class="hljs-built_in">xmm0</span>, <span class="hljs-built_in">xmm0</span>, <span class="hljs-built_in">xmm1</span>
  <span class="hljs-keyword">vmovups</span> [<span class="hljs-built_in">rdx</span>], <span class="hljs-built_in">xmm0</span>
  <span class="hljs-keyword">ret</span>
""");
    }

    [Fact]
    public void SpecialEdge_Empty()
    {
        AssertHighlighter("x86asm",
"""

""",
"""

""");
    }

    [Fact]
    public void SpecialEdge_OnlyComment()
    {
        AssertHighlighter("x86asm",
"""
; nothing here
""",
"""
<span class="hljs-comment">; nothing here</span>
""");
    }

    [Fact]
    public void SpecialEdge_OnlyLabel()
    {
        AssertHighlighter("x86asm",
"""
main:
""",
"""
<span class="hljs-symbol">main:</span>
""");
    }

    [Fact]
    public void SpecialEdge_BlankBetween()
    {
        AssertHighlighter("x86asm",
"""
mov eax, 1

mov ebx, 2
""",
"""
<span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">1</span>

<span class="hljs-keyword">mov</span> <span class="hljs-built_in">ebx</span>, <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void SpecialEdge_IndentedInstr()
    {
        AssertHighlighter("x86asm",
"""
    mov eax, 1
""",
"""
    <span class="hljs-keyword">mov</span> <span class="hljs-built_in">eax</span>, <span class="hljs-number">1</span>
""");
    }
}
