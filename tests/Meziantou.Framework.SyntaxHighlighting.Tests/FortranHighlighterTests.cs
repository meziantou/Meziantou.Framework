namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class FortranHighlighterTests
{    [Fact]
    public void Hello()
    {
        AssertHighlighter("fortran",
"""
program hello
  implicit none
  print *, 'Hello, World!'
end program hello
""",
"""
<span class="hljs-function"><span class="hljs-keyword">program</span> <span class="hljs-title">hello</span></span>
  <span class="hljs-keyword">implicit</span> <span class="hljs-keyword">none</span>
  <span class="hljs-built_in">print</span> *, <span class="hljs-string">&#x27;Hello, World!&#x27;</span>
<span class="hljs-keyword">end</span> <span class="hljs-function"><span class="hljs-keyword">program</span> <span class="hljs-title">hello</span></span>
""");
    }

    [Fact]
    public void Module()
    {
        AssertHighlighter("f90",
"""
module mathops
  use iso_fortran_env, only: real64
  implicit none
  private
  public :: dot, norm

  integer, parameter :: dp = real64
contains
  pure function dot(a, b) result(r)
    real(dp), intent(in) :: a(:), b(:)
    real(dp) :: r
    r = sum(a * b)
  end function dot

  subroutine norm(v, n)
    real(dp), intent(inout) :: v(:)
    real(dp), intent(out) :: n
    n = sqrt(dot_product(v, v))
    if (n > 0.0_dp) v = v / n
  end subroutine norm
end module mathops
""",
"""
<span class="hljs-keyword">module</span> mathops
  <span class="hljs-keyword">use</span> <span class="hljs-keyword">iso_fortran_env</span>, <span class="hljs-keyword">only</span>: real64
  <span class="hljs-keyword">implicit</span> <span class="hljs-keyword">none</span>
  <span class="hljs-keyword">private</span>
  <span class="hljs-keyword">public</span> :: dot, norm

  <span class="hljs-keyword">integer</span>, <span class="hljs-keyword">parameter</span> :: dp = real64
<span class="hljs-keyword">contains</span>
  <span class="hljs-keyword">pure</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">dot</span><span class="hljs-params">(a, b)</span></span> result(r)
    <span class="hljs-keyword">real</span>(dp), <span class="hljs-keyword">intent</span>(<span class="hljs-keyword">in</span>) :: a(:), b(:)
    <span class="hljs-keyword">real</span>(dp) :: r
    r = <span class="hljs-built_in">sum</span>(a * b)
  <span class="hljs-keyword">end</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">dot</span></span>

  <span class="hljs-function"><span class="hljs-keyword">subroutine</span> <span class="hljs-title">norm</span><span class="hljs-params">(v, n)</span></span>
    <span class="hljs-keyword">real</span>(dp), <span class="hljs-keyword">intent</span>(inout) :: v(:)
    <span class="hljs-keyword">real</span>(dp), <span class="hljs-keyword">intent</span>(<span class="hljs-keyword">out</span>) :: n
    n = <span class="hljs-built_in">sqrt</span>(<span class="hljs-built_in">dot_product</span>(v, v))
    <span class="hljs-keyword">if</span> (n &gt; <span class="hljs-number">0.0_dp</span>) v = v / n
  <span class="hljs-keyword">end</span> <span class="hljs-function"><span class="hljs-keyword">subroutine</span> <span class="hljs-title">norm</span></span>
<span class="hljs-keyword">end</span> <span class="hljs-keyword">module</span> mathops
""");
    }

    [Fact]
    public void DerivedTypes()
    {
        AssertHighlighter("f03",
"""
type :: point
  real :: x = 0.0, y = 0.0
contains
  procedure :: distance => point_distance
end type point

type, extends(point) :: point3d
  real :: z
end type

class(point), allocatable :: p
type(point3d), pointer :: q => null()
allocate(p, source=point(1.0, 2.0))
deallocate(p)
nullify(q)
""",
"""
<span class="hljs-keyword">type</span> :: point
  <span class="hljs-keyword">real</span> :: x = <span class="hljs-number">0.0</span>, y = <span class="hljs-number">0.0</span>
<span class="hljs-keyword">contains</span>
  <span class="hljs-keyword">procedure</span> :: distance =&gt; point_distance
<span class="hljs-keyword">end</span> <span class="hljs-keyword">type</span> point

<span class="hljs-keyword">type</span>, <span class="hljs-keyword">extends</span>(point) :: point3d
  <span class="hljs-keyword">real</span> :: z
<span class="hljs-keyword">end</span> <span class="hljs-keyword">type</span>

<span class="hljs-keyword">class</span>(point), <span class="hljs-keyword">allocatable</span> :: p
<span class="hljs-keyword">type</span>(point3d), <span class="hljs-keyword">pointer</span> :: q =&gt; null()
<span class="hljs-built_in">allocate</span>(p, source=point(<span class="hljs-number">1.0</span>, <span class="hljs-number">2.0</span>))
<span class="hljs-built_in">deallocate</span>(p)
<span class="hljs-built_in">nullify</span>(q)
""");
    }

    [Fact]
    public void ControlFlow()
    {
        AssertHighlighter("f08",
"""
do i = 1, n
  if (a(i) .gt. 0 .and. b(i) .ne. 0) then
    c(i) = a(i) / b(i)
  else if (a(i) == 0) then
    cycle
  else
    exit
  end if
end do

do while (err > tol)
  err = err * 0.5
enddo

select case (key)
case (1:9)
  call handle(key)
case default
  stop 1
end select

do concurrent (i = 1:n, j = 1:m)
  x(i, j) = 0
end do
where (a > 0) b = log(a)
forall (i = 1:n) z(i) = i**2
""",
"""
<span class="hljs-keyword">do</span> i = <span class="hljs-number">1</span>, n
  <span class="hljs-keyword">if</span> (a(i) <span class="hljs-keyword">.gt.</span> <span class="hljs-number">0</span> <span class="hljs-keyword">.and.</span> b(i) <span class="hljs-keyword">.ne.</span> <span class="hljs-number">0</span>) <span class="hljs-keyword">then</span>
    c(i) = a(i) / b(i)
  <span class="hljs-keyword">else</span> <span class="hljs-keyword">if</span> (a(i) == <span class="hljs-number">0</span>) <span class="hljs-keyword">then</span>
    <span class="hljs-keyword">cycle</span>
  <span class="hljs-keyword">else</span>
    <span class="hljs-keyword">exit</span>
  <span class="hljs-keyword">end</span> <span class="hljs-keyword">if</span>
<span class="hljs-keyword">end</span> <span class="hljs-keyword">do</span>

<span class="hljs-keyword">do</span> <span class="hljs-keyword">while</span> (err &gt; tol)
  err = err * <span class="hljs-number">0.5</span>
<span class="hljs-keyword">enddo</span>

<span class="hljs-keyword">select</span> <span class="hljs-keyword">case</span> (key)
<span class="hljs-keyword">case</span> (<span class="hljs-number">1</span>:<span class="hljs-number">9</span>)
  <span class="hljs-keyword">call</span> handle(key)
<span class="hljs-keyword">case</span> <span class="hljs-keyword">default</span>
  <span class="hljs-keyword">stop</span> <span class="hljs-number">1</span>
<span class="hljs-keyword">end</span> <span class="hljs-keyword">select</span>

<span class="hljs-keyword">do</span> <span class="hljs-keyword">concurrent</span> (i = <span class="hljs-number">1</span>:n, j = <span class="hljs-number">1</span>:m)
  x(i, j) = <span class="hljs-number">0</span>
<span class="hljs-keyword">end</span> <span class="hljs-keyword">do</span>
<span class="hljs-keyword">where</span> (a &gt; <span class="hljs-number">0</span>) b = <span class="hljs-built_in">log</span>(a)
<span class="hljs-keyword">forall</span> (i = <span class="hljs-number">1</span>:n) z(i) = i**<span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("fortran",
"""
x = 1
y = 1.5
z = 1.0d0
w = 6.02e23
v = 1.5e-3_dp
u = .5
t = 3.14159_real64
k = 2_int8
s = 1.
if (x.eq.1.and.y.lt.2.) z = -1.0D+10
b = 1.e5
""",
"""
x = <span class="hljs-number">1</span>
y = <span class="hljs-number">1.5</span>
z = <span class="hljs-number">1.0d0</span>
w = <span class="hljs-number">6.02e23</span>
v = <span class="hljs-number">1.5e-3_dp</span>
u = <span class="hljs-number">.5</span>
t = <span class="hljs-number">3.14159_real64</span>
k = <span class="hljs-number">2_int8</span>
s = <span class="hljs-number">1.</span>
<span class="hljs-keyword">if</span> (x<span class="hljs-keyword">.eq.</span><span class="hljs-number">1</span><span class="hljs-keyword">.and.</span>y<span class="hljs-keyword">.lt.</span><span class="hljs-number">2.</span>) z = -<span class="hljs-number">1.0D+10</span>
b = <span class="hljs-number">1.e5</span>
""");
    }

    [Fact]
    public void Strings_NoBackslashEscapes_DoubledQuotes()
    {
        AssertHighlighter("fortran",
""""
print *, 'It''s a string'
print *, "Say ""hi"""
print *, 'C:\temp\'
print *, "tab\t"
write(*, '(A, I5, F10.3)') 'value', n, x
x = 1
"""",
"""
<span class="hljs-built_in">print</span> *, <span class="hljs-string">&#x27;It&#x27;&#x27;s a string&#x27;</span>
<span class="hljs-built_in">print</span> *, <span class="hljs-string">&quot;Say &quot;&quot;hi&quot;&quot;&quot;</span>
<span class="hljs-built_in">print</span> *, <span class="hljs-string">&#x27;C:\temp\&#x27;</span>
<span class="hljs-built_in">print</span> *, <span class="hljs-string">&quot;tab\t&quot;</span>
<span class="hljs-built_in">write</span>(*, <span class="hljs-string">&#x27;(A, I5, F10.3)&#x27;</span>) <span class="hljs-string">&#x27;value&#x27;</span>, n, x
x = <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void UnterminatedString()
    {
        AssertHighlighter("fortran",
"""
print *, 'unterminated
x = 1
y = "also
z = 2

""",
"""
<span class="hljs-built_in">print</span> *, <span class="hljs-string">&#x27;unterminated
x = 1
y = &quot;also
z = 2
</span>
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("fortran",
"""
! a free-form comment
x = 1 ! trailing comment
! TODO: fix this
!$omp parallel do
y = 2
""",
"""
<span class="hljs-comment">! a free-form comment</span>
x = <span class="hljs-number">1</span> <span class="hljs-comment">! trailing comment</span>
<span class="hljs-comment">! <span class="hljs-doctag">TODO:</span> fix this</span>
<span class="hljs-comment">!$omp parallel do</span>
y = <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void FixedForm()
    {
        AssertHighlighter("f",
"""
C     FIXED-FORM FORTRAN 77 PROGRAM
      PROGRAM MAIN
      INTEGER I, N
      REAL X(100)
C     COMPUTE THE SUM
      N = 10
      DO 10 I = 1, N
         X(I) = FLOAT(I) * 2.5
   10 CONTINUE
      CALL REPORT(X, N,
     &            .TRUE.)
c lowercase comment
*     star comment
C-----------------------------------------------------------------
C
C=================================================================
      WRITE(6, 100) N
  100 FORMAT(1X, 'N = ', I5)
      STOP
      END
""",
"""
<span class="hljs-comment">C     FIXED-FORM FORTRAN 77 PROGRAM</span>
      <span class="hljs-function"><span class="hljs-keyword">PROGRAM</span> <span class="hljs-title">MAIN</span></span>
      <span class="hljs-keyword">INTEGER</span> I, N
      <span class="hljs-keyword">REAL</span> X(<span class="hljs-number">100</span>)
<span class="hljs-comment">C     COMPUTE THE SUM</span>
      N = <span class="hljs-number">10</span>
      <span class="hljs-keyword">DO</span> <span class="hljs-number">10</span> I = <span class="hljs-number">1</span>, N
         X(I) = <span class="hljs-built_in">FLOAT</span>(I) * <span class="hljs-number">2.5</span>
   <span class="hljs-number">10</span> <span class="hljs-keyword">CONTINUE</span>
      <span class="hljs-keyword">CALL</span> REPORT(X, N,
     &amp;            <span class="hljs-literal">.TRUE.</span>)
<span class="hljs-comment">c lowercase comment</span>
<span class="hljs-comment">*     star comment</span>
<span class="hljs-comment">C-----------------------------------------------------------------</span>
<span class="hljs-comment">C</span>
<span class="hljs-comment">C=================================================================</span>
      <span class="hljs-built_in">WRITE</span>(<span class="hljs-number">6</span>, <span class="hljs-number">100</span>) N
  <span class="hljs-number">100</span> <span class="hljs-keyword">FORMAT</span>(<span class="hljs-number">1</span>X, <span class="hljs-string">&#x27;N = &#x27;</span>, I5)
      <span class="hljs-keyword">STOP</span>
      <span class="hljs-keyword">END</span>
""");
    }

    [Fact]
    public void FixedForm_AssignmentToC_IsNotAComment()
    {
        AssertHighlighter("for",
"""
C = 1
C= A + B
C == D
CALL FOO
C(1) = 2
CX = 3
""",
"""
C = <span class="hljs-number">1</span>
C= A + B
<span class="hljs-comment">C == D</span>
<span class="hljs-keyword">CALL</span> FOO
C(<span class="hljs-number">1</span>) = <span class="hljs-number">2</span>
CX = <span class="hljs-number">3</span>
""");
    }

    [Fact]
    public void CaseInsensitiveKeywordsAndDottedOperators()
    {
        AssertHighlighter("f77",
"""
INTEGER :: I
Real :: X
LOGICAL :: FLAG = .FALSE.
flag = .True.
IF (.NOT. FLAG) THEN
ENDIF
x = .false. .eqv. .true.
y = a .neqv. b
""",
"""
<span class="hljs-keyword">INTEGER</span> :: I
<span class="hljs-keyword">Real</span> :: X
<span class="hljs-keyword">LOGICAL</span> :: FLAG = <span class="hljs-literal">.FALSE.</span>
flag = <span class="hljs-literal">.True.</span>
<span class="hljs-keyword">IF</span> (<span class="hljs-keyword">.NOT.</span> FLAG) <span class="hljs-keyword">THEN</span>
<span class="hljs-keyword">ENDIF</span>
x = <span class="hljs-literal">.false.</span> <span class="hljs-keyword">.eqv.</span> <span class="hljs-literal">.true.</span>
y = a <span class="hljs-keyword">.neqv.</span> b
""");
    }

    [Fact]
    public void FunctionDeclarations()
    {
        AssertHighlighter("fortran",
"""
recursive integer function fact(n) result(f)
  integer, intent(in) :: n
  if (n <= 1) then
    f = 1
  else
    f = n * fact(n - 1)
  end if
end function fact

subroutine c_api(x) bind(c, name="c_api") ! exported
  use iso_c_binding
  real(c_double), value :: x
end subroutine

function f(
  a, b)
end function

subroutine noargs
end subroutine noargs
subroutine a; end subroutine a
program = 1
""",
"""
<span class="hljs-keyword">recursive</span> <span class="hljs-keyword">integer</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">fact</span><span class="hljs-params">(n)</span></span> result(f)
  <span class="hljs-keyword">integer</span>, <span class="hljs-keyword">intent</span>(<span class="hljs-keyword">in</span>) :: n
  <span class="hljs-keyword">if</span> (n &lt;= <span class="hljs-number">1</span>) <span class="hljs-keyword">then</span>
    f = <span class="hljs-number">1</span>
  <span class="hljs-keyword">else</span>
    f = n * fact(n - <span class="hljs-number">1</span>)
  <span class="hljs-keyword">end</span> <span class="hljs-keyword">if</span>
<span class="hljs-keyword">end</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">fact</span></span>

<span class="hljs-function"><span class="hljs-keyword">subroutine</span> <span class="hljs-title">c_api</span><span class="hljs-params">(x)</span></span> <span class="hljs-keyword">bind</span>(c, <span class="hljs-keyword">name</span>=<span class="hljs-string">&quot;c_api&quot;</span>) <span class="hljs-comment">! exported</span>
  <span class="hljs-keyword">use</span> <span class="hljs-keyword">iso_c_binding</span>
  <span class="hljs-keyword">real</span>(<span class="hljs-keyword">c_double</span>), <span class="hljs-keyword">value</span> :: x
<span class="hljs-keyword">end</span> <span class="hljs-function"><span class="hljs-keyword">subroutine</span></span>

<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">f</span><span class="hljs-params">(
  a, b)</span></span>
<span class="hljs-keyword">end</span> <span class="hljs-function"><span class="hljs-keyword">function</span></span>

<span class="hljs-function"><span class="hljs-keyword">subroutine</span> <span class="hljs-title">noargs</span></span>
<span class="hljs-keyword">end</span> <span class="hljs-function"><span class="hljs-keyword">subroutine</span> <span class="hljs-title">noargs</span></span>
<span class="hljs-function"><span class="hljs-keyword">subroutine</span> <span class="hljs-title">a</span></span>; <span class="hljs-keyword">end</span> <span class="hljs-function"><span class="hljs-keyword">subroutine</span> <span class="hljs-title">a</span></span>
<span class="hljs-function"><span class="hljs-keyword">program</span> </span>= <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Interfaces()
    {
        AssertHighlighter("f95",
"""
interface
  subroutine callback(x) bind(c)
    import :: c_double
    real(c_double), intent(in) :: x
  end subroutine
end interface

abstract interface
  function func(x)
    real :: func, x
  end function
end interface

procedure(func), pointer :: fp => null()
""",
"""
<span class="hljs-keyword">interface</span>
  <span class="hljs-function"><span class="hljs-keyword">subroutine</span> <span class="hljs-title">callback</span><span class="hljs-params">(x)</span></span> <span class="hljs-keyword">bind</span>(c)
    <span class="hljs-keyword">import</span> :: <span class="hljs-keyword">c_double</span>
    <span class="hljs-keyword">real</span>(<span class="hljs-keyword">c_double</span>), <span class="hljs-keyword">intent</span>(<span class="hljs-keyword">in</span>) :: x
  <span class="hljs-keyword">end</span> <span class="hljs-function"><span class="hljs-keyword">subroutine</span></span>
<span class="hljs-keyword">end</span> <span class="hljs-keyword">interface</span>

<span class="hljs-keyword">abstract</span> <span class="hljs-keyword">interface</span>
  <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">func</span><span class="hljs-params">(x)</span></span>
    <span class="hljs-keyword">real</span> :: func, x
  <span class="hljs-keyword">end</span> <span class="hljs-function"><span class="hljs-keyword">function</span></span>
<span class="hljs-keyword">end</span> <span class="hljs-keyword">interface</span>

<span class="hljs-keyword">procedure</span>(func), <span class="hljs-keyword">pointer</span> :: fp =&gt; null()
""");
    }

    [Fact]
    public void Coarrays()
    {
        AssertHighlighter("fortran",
"""
real :: a[*]
integer :: me
me = this_image()
sync all
if (me == 1) print *, num_images()
call co_sum(a)
""",
"""
<span class="hljs-keyword">real</span> :: a[*]
<span class="hljs-keyword">integer</span> :: me
me = <span class="hljs-built_in">this_image</span>()
<span class="hljs-built_in">sync</span> <span class="hljs-built_in">all</span>
<span class="hljs-keyword">if</span> (me == <span class="hljs-number">1</span>) <span class="hljs-built_in">print</span> *, <span class="hljs-built_in">num_images</span>()
<span class="hljs-keyword">call</span> <span class="hljs-built_in">co_sum</span>(a)
""");
    }

    [Fact]
    public void IllegalBlockComment()
    {
        AssertHighlighter("fortran",
"""
x = 1 /* not a comment */
y = 2
""",
"""
x = <span class="hljs-number">1</span> /* not a comment */
y = <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("fortran",
"""
! commentaire : été
print *, 'héllo wörld ✓'
x = π
""",
"""
<span class="hljs-comment">! commentaire : été</span>
<span class="hljs-built_in">print</span> *, <span class="hljs-string">&#x27;héllo wörld ✓&#x27;</span>
x = π
""");
    }

    [Fact]
    public void IsoCBinding()
    {
        AssertHighlighter("fortran",
"""
use, intrinsic :: iso_c_binding, only: c_int, c_ptr, c_null_ptr, C_INTPTR_T
type(c_ptr) :: p = c_null_ptr
""",
"""
<span class="hljs-keyword">use</span>, <span class="hljs-keyword">intrinsic</span> :: <span class="hljs-keyword">iso_c_binding</span>, <span class="hljs-keyword">only</span>: <span class="hljs-keyword">c_int</span>, <span class="hljs-keyword">c_ptr</span>, <span class="hljs-keyword">c_null_ptr</span>, <span class="hljs-keyword">C_INTPTR_T</span>
<span class="hljs-keyword">type</span>(<span class="hljs-keyword">c_ptr</span>) :: p = <span class="hljs-keyword">c_null_ptr</span>
""");
    }

    [Fact]
    public void IeeeBuiltIns()
    {
        AssertHighlighter("fortran",
"""
use ieee_arithmetic
call ieee_set_underflow_mode(.true.)
""",
"""
<span class="hljs-keyword">use</span> <span class="hljs-built_in">ieee_arithmetic</span>
<span class="hljs-keyword">call</span> <span class="hljs-built_in">ieee_set_underflow_mode</span>(<span class="hljs-literal">.true.</span>)
""");
    }
}
