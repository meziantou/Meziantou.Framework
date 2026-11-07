namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class CHighlighterTests
{
    [Fact]
    public void Hello()
    {
        AssertHighlighter("c",
"""
#include <stdio.h>

int main(void) {
    printf("Hello, world!\n");
    return 0;
}
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&lt;stdio.h&gt;</span></span>

<span class="hljs-type">int</span> <span class="hljs-title function_">main</span><span class="hljs-params">(<span class="hljs-type">void</span>)</span> {
    <span class="hljs-built_in">printf</span>(<span class="hljs-string">&quot;Hello, world!\n&quot;</span>);
    <span class="hljs-keyword">return</span> <span class="hljs-number">0</span>;
}
""");
    }

    [Fact]
    public void MainArgs()
    {
        AssertHighlighter("c",
"""
int main(int argc, char *argv[])
{
    if (argc < 2) {
        fprintf(stderr, "usage: %s file\n", argv[0]);
        return 1;
    }
    return EXIT_SUCCESS;
}
""",
"""
<span class="hljs-type">int</span> <span class="hljs-title function_">main</span><span class="hljs-params">(<span class="hljs-type">int</span> argc, <span class="hljs-type">char</span> *argv[])</span>
{
    <span class="hljs-keyword">if</span> (argc &lt; <span class="hljs-number">2</span>) {
        <span class="hljs-built_in">fprintf</span>(<span class="hljs-built_in">stderr</span>, <span class="hljs-string">&quot;usage: %s file\n&quot;</span>, argv[<span class="hljs-number">0</span>]);
        <span class="hljs-keyword">return</span> <span class="hljs-number">1</span>;
    }
    <span class="hljs-keyword">return</span> EXIT_SUCCESS;
}
""");
    }

    [Fact]
    public void Preprocessor()
    {
        AssertHighlighter("c",
"""
#include <stdlib.h>
#include "config.h"
#define MAX(a, b) ((a) > (b) ? (a) : (b))
#define VERSION "1.0"
#ifdef DEBUG
#  define LOG(x) printf x
#elif defined(TRACE)
#elifdef FOO
#elifndef BAR
#else
#endif
#pragma once
#undef MAX
#error "bad" // comment
#line 42 "file.c"
#if 0 /* block */
#endif
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&lt;stdlib.h&gt;</span></span>
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&quot;config.h&quot;</span></span>
<span class="hljs-meta">#<span class="hljs-keyword">define</span> MAX(a, b) ((a) &gt; (b) ? (a) : (b))</span>
<span class="hljs-meta">#<span class="hljs-keyword">define</span> VERSION <span class="hljs-string">&quot;1.0&quot;</span></span>
<span class="hljs-meta">#<span class="hljs-keyword">ifdef</span> DEBUG</span>
<span class="hljs-meta">#  <span class="hljs-keyword">define</span> LOG(x) printf x</span>
<span class="hljs-meta">#<span class="hljs-keyword">elif</span> defined(TRACE)</span>
<span class="hljs-meta">#<span class="hljs-keyword">elifdef</span> FOO</span>
<span class="hljs-meta">#<span class="hljs-keyword">elifndef</span> BAR</span>
<span class="hljs-meta">#<span class="hljs-keyword">else</span></span>
<span class="hljs-meta">#<span class="hljs-keyword">endif</span></span>
<span class="hljs-meta">#<span class="hljs-keyword">pragma</span> once</span>
<span class="hljs-meta">#<span class="hljs-keyword">undef</span> MAX</span>
<span class="hljs-meta">#<span class="hljs-keyword">error</span> <span class="hljs-string">&quot;bad&quot;</span> <span class="hljs-comment">// comment</span></span>
<span class="hljs-meta">#<span class="hljs-keyword">line</span> 42 <span class="hljs-string">&quot;file.c&quot;</span></span>
<span class="hljs-meta">#<span class="hljs-keyword">if</span> 0 <span class="hljs-comment">/* block */</span></span>
<span class="hljs-meta">#<span class="hljs-keyword">endif</span></span>
""");
    }

    [Fact]
    public void PreprocessorMultiline()
    {
        AssertHighlighter("c",
"""
#define SWAP(a, b) \
    do { \
        int t = (a); \
        (a) = (b); \
    } while (0)
int x;
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">define</span> SWAP(a, b) \
    do { \
        int t = (a); \
        (a) = (b); \
    } while (0)</span>
<span class="hljs-type">int</span> x;
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("c",
"""
// line comment
/* block
   comment */
int x; // trailing TODO: fix
/* FIXME: later */
// continued \
still comment
int y;
""",
"""
<span class="hljs-comment">// line comment</span>
<span class="hljs-comment">/* block
   comment */</span>
<span class="hljs-type">int</span> x; <span class="hljs-comment">// trailing <span class="hljs-doctag">TODO:</span> fix</span>
<span class="hljs-comment">/* <span class="hljs-doctag">FIXME:</span> later */</span>
<span class="hljs-comment">// continued \
still comment</span>
<span class="hljs-type">int</span> y;
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("c",
"""
const char *s = "hello\tworld\n";
const char *e = "";
const char *q = "say \"hi\"";
wchar_t *w = L"wide";
char16_t *u = u"utf16";
char32_t *U = U"utf32";
const char *u8s = u8"utf8";
const char *multi = "a" "b";
char *bad = "unterminated
int z;
""",
"""
<span class="hljs-type">const</span> <span class="hljs-type">char</span> *s = <span class="hljs-string">&quot;hello\tworld\n&quot;</span>;
<span class="hljs-type">const</span> <span class="hljs-type">char</span> *e = <span class="hljs-string">&quot;&quot;</span>;
<span class="hljs-type">const</span> <span class="hljs-type">char</span> *q = <span class="hljs-string">&quot;say \&quot;hi\&quot;&quot;</span>;
<span class="hljs-type">wchar_t</span> *w = <span class="hljs-string">L&quot;wide&quot;</span>;
<span class="hljs-type">char16_t</span> *u = <span class="hljs-string">u&quot;utf16&quot;</span>;
<span class="hljs-type">char32_t</span> *U = <span class="hljs-string">U&quot;utf32&quot;</span>;
<span class="hljs-type">const</span> <span class="hljs-type">char</span> *u8s = <span class="hljs-string">u8&quot;utf8&quot;</span>;
<span class="hljs-type">const</span> <span class="hljs-type">char</span> *multi = <span class="hljs-string">&quot;a&quot;</span> <span class="hljs-string">&quot;b&quot;</span>;
<span class="hljs-type">char</span> *bad = <span class="hljs-string">&quot;unterminated
int z;</span>
""");
    }

    [Fact]
    public void Chars()
    {
        AssertHighlighter("c",
"""
char c = 'a';
char n = '\n';
char z = '\0';
char h = '\x41';
char o = '\101';
char q = '\'';
wchar_t w = L'w';
int mc = 'ab';
""",
"""
<span class="hljs-type">char</span> c = <span class="hljs-string">&#x27;a&#x27;</span>;
<span class="hljs-type">char</span> n = <span class="hljs-string">&#x27;\n&#x27;</span>;
<span class="hljs-type">char</span> z = <span class="hljs-string">&#x27;\0&#x27;</span>;
<span class="hljs-type">char</span> h = <span class="hljs-string">&#x27;\x41&#x27;</span>;
<span class="hljs-type">char</span> o = <span class="hljs-string">&#x27;\101&#x27;</span>;
<span class="hljs-type">char</span> q = <span class="hljs-string">&#x27;\&#x27;&#x27;</span>;
<span class="hljs-type">wchar_t</span> w = <span class="hljs-string">L&#x27;w&#x27;</span>;
<span class="hljs-type">int</span> mc = <span class="hljs-string">&#x27;ab&#x27;</span>;
""");
    }

    [Fact]
    public void RawString()
    {
        AssertHighlighter("c",
"""
const char *r = R"(raw \n string)";
const char *d = R"delim(with ) paren)delim";
""",
"""
<span class="hljs-type">const</span> <span class="hljs-type">char</span> *r = <span class="hljs-string">R&quot;(raw \n string)&quot;</span>;
<span class="hljs-type">const</span> <span class="hljs-type">char</span> *d = <span class="hljs-string">R&quot;delim(with ) paren)delim&quot;</span>;
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("c",
"""
int a = 42;
int b = -17;
unsigned c = 42u;
long d = 42L;
unsigned long long e = 42ULL;
long long f = 42ll;
int g = 0x1F;
int h = 0XdeadBEEF;
int i = 0b1010;
int j = 0777;
int k = 1'000'000;
double l = 3.14;
double m = 1e10;
double n = 1.5e-3;
float o = 2.5f;
float p = .5f;
double q = 1.;
double r = 0x1.8p3;
int s = 0xFFul;
float t = 1e10f;
double u = .25;
int v = 0x1F'FF;
""",
"""
<span class="hljs-type">int</span> a = <span class="hljs-number">42</span>;
<span class="hljs-type">int</span> b = <span class="hljs-number">-17</span>;
<span class="hljs-type">unsigned</span> c = <span class="hljs-number">42u</span>;
<span class="hljs-type">long</span> d = <span class="hljs-number">42L</span>;
<span class="hljs-type">unsigned</span> <span class="hljs-type">long</span> <span class="hljs-type">long</span> e = <span class="hljs-number">42ULL</span>;
<span class="hljs-type">long</span> <span class="hljs-type">long</span> f = <span class="hljs-number">42ll</span>;
<span class="hljs-type">int</span> g = <span class="hljs-number">0x1F</span>;
<span class="hljs-type">int</span> h = <span class="hljs-number">0XdeadBEEF</span>;
<span class="hljs-type">int</span> i = <span class="hljs-number">0b1010</span>;
<span class="hljs-type">int</span> j = <span class="hljs-number">0777</span>;
<span class="hljs-type">int</span> k = <span class="hljs-number">1&#x27;000&#x27;000</span>;
<span class="hljs-type">double</span> l = <span class="hljs-number">3.14</span>;
<span class="hljs-type">double</span> m = <span class="hljs-number">1e10</span>;
<span class="hljs-type">double</span> n = <span class="hljs-number">1.5e-3</span>;
<span class="hljs-type">float</span> o = <span class="hljs-number">2.5f</span>;
<span class="hljs-type">float</span> p = <span class="hljs-number">.5f</span>;
<span class="hljs-type">double</span> q = <span class="hljs-number">1.</span>;
<span class="hljs-type">double</span> r = <span class="hljs-number">0x1.8p3</span>;
<span class="hljs-type">int</span> s = <span class="hljs-number">0xFFul</span>;
<span class="hljs-type">float</span> t = <span class="hljs-number">1e10f</span>;
<span class="hljs-type">double</span> u = <span class="hljs-number">.25</span>;
<span class="hljs-type">int</span> v = <span class="hljs-number">0x1F&#x27;FF</span>;
""");
    }

    [Fact]
    public void Types()
    {
        AssertHighlighter("c",
"""
int a; short b; long c; long long d; unsigned int e; signed char f;
float g; double h; long double i; void *j; _Bool k; bool l;
size_t m; uint32_t n; int64_t o; ptrdiff_t p; my_type_t q;
_Complex double r; _Atomic int s; atomic_int t; atomic_flag u;
const volatile int v; static int w; extern int x; register int y;
constexpr int z = 1;
""",
"""
<span class="hljs-type">int</span> a; <span class="hljs-type">short</span> b; <span class="hljs-type">long</span> c; <span class="hljs-type">long</span> <span class="hljs-type">long</span> d; <span class="hljs-type">unsigned</span> <span class="hljs-type">int</span> e; <span class="hljs-type">signed</span> <span class="hljs-type">char</span> f;
<span class="hljs-type">float</span> g; <span class="hljs-type">double</span> h; <span class="hljs-type">long</span> <span class="hljs-type">double</span> i; <span class="hljs-type">void</span> *j; <span class="hljs-type">_Bool</span> k; <span class="hljs-type">bool</span> l;
<span class="hljs-type">size_t</span> m; <span class="hljs-type">uint32_t</span> n; <span class="hljs-type">int64_t</span> o; <span class="hljs-type">ptrdiff_t</span> p; <span class="hljs-type">my_type_t</span> q;
<span class="hljs-type">_Complex</span> <span class="hljs-type">double</span> r; <span class="hljs-keyword">_Atomic</span> <span class="hljs-type">int</span> s; <span class="hljs-type">atomic_int</span> t; <span class="hljs-type">atomic_flag</span> u;
<span class="hljs-type">const</span> <span class="hljs-keyword">volatile</span> <span class="hljs-type">int</span> v; <span class="hljs-type">static</span> <span class="hljs-type">int</span> w; <span class="hljs-keyword">extern</span> <span class="hljs-type">int</span> x; <span class="hljs-keyword">register</span> <span class="hljs-type">int</span> y;
<span class="hljs-type">constexpr</span> <span class="hljs-type">int</span> z = <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void Keywords()
    {
        AssertHighlighter("c",
"""
for (int i = 0; i < n; i++) {
    if (i % 2) continue;
    else if (i > 10) break;
}
while (1) { }
do { x--; } while (x > 0);
switch (c) {
case 1:
    goto end;
default:
    break;
}
end:
return;
""",
"""
<span class="hljs-keyword">for</span> (<span class="hljs-type">int</span> i = <span class="hljs-number">0</span>; i &lt; n; i++) {
    <span class="hljs-keyword">if</span> (i % <span class="hljs-number">2</span>) <span class="hljs-keyword">continue</span>;
    <span class="hljs-keyword">else</span> <span class="hljs-keyword">if</span> (i &gt; <span class="hljs-number">10</span>) <span class="hljs-keyword">break</span>;
}
<span class="hljs-keyword">while</span> (<span class="hljs-number">1</span>) { }
<span class="hljs-keyword">do</span> { x--; } <span class="hljs-keyword">while</span> (x &gt; <span class="hljs-number">0</span>);
<span class="hljs-keyword">switch</span> (c) {
<span class="hljs-keyword">case</span> <span class="hljs-number">1</span>:
    <span class="hljs-keyword">goto</span> end;
<span class="hljs-keyword">default</span>:
    <span class="hljs-keyword">break</span>;
}
end:
<span class="hljs-keyword">return</span>;
""");
    }

    [Fact]
    public void FunctionDecls()
    {
        AssertHighlighter("c",
"""
static int add(int a, int b);
extern void f(int), g(char);
void noop(void) { }
static inline unsigned long hash(const char *str)
{
    unsigned long h = 5381;
    int c;
    while ((c = *str++))
        h = ((h << 5) + h) + c;
    return h;
}
char **split(char *s, const char *delim, size_t *count);
struct point make_point(int x, int y);
const char *name(void);
void (*signal(int sig, void (*func)(int)))(int);
""",
"""
<span class="hljs-type">static</span> <span class="hljs-type">int</span> <span class="hljs-title function_">add</span><span class="hljs-params">(<span class="hljs-type">int</span> a, <span class="hljs-type">int</span> b)</span>;
<span class="hljs-keyword">extern</span> <span class="hljs-type">void</span> <span class="hljs-title function_">f</span><span class="hljs-params">(<span class="hljs-type">int</span>)</span>, <span class="hljs-title function_">g</span><span class="hljs-params">(<span class="hljs-type">char</span>)</span>;
<span class="hljs-type">void</span> <span class="hljs-title function_">noop</span><span class="hljs-params">(<span class="hljs-type">void</span>)</span> { }
<span class="hljs-type">static</span> <span class="hljs-keyword">inline</span> <span class="hljs-type">unsigned</span> <span class="hljs-type">long</span> <span class="hljs-title function_">hash</span><span class="hljs-params">(<span class="hljs-type">const</span> <span class="hljs-type">char</span> *str)</span>
{
    <span class="hljs-type">unsigned</span> <span class="hljs-type">long</span> h = <span class="hljs-number">5381</span>;
    <span class="hljs-type">int</span> c;
    <span class="hljs-keyword">while</span> ((c = *str++))
        h = ((h &lt;&lt; <span class="hljs-number">5</span>) + h) + c;
    <span class="hljs-keyword">return</span> h;
}
<span class="hljs-type">char</span> **<span class="hljs-title function_">split</span><span class="hljs-params">(<span class="hljs-type">char</span> *s, <span class="hljs-type">const</span> <span class="hljs-type">char</span> *delim, <span class="hljs-type">size_t</span> *count)</span>;
<span class="hljs-keyword">struct</span> point <span class="hljs-title function_">make_point</span><span class="hljs-params">(<span class="hljs-type">int</span> x, <span class="hljs-type">int</span> y)</span>;
<span class="hljs-type">const</span> <span class="hljs-type">char</span> *<span class="hljs-title function_">name</span><span class="hljs-params">(<span class="hljs-type">void</span>)</span>;
<span class="hljs-type">void</span> (*signal(<span class="hljs-type">int</span> sig, <span class="hljs-type">void</span> (*func)(<span class="hljs-type">int</span>)))(<span class="hljs-type">int</span>);
""");
    }

    [Fact]
    public void FunctionPointer()
    {
        AssertHighlighter("c",
"""
typedef int (*compare_fn)(const void *, const void *);
void qsort(void *base, size_t n, size_t size, int (*cmp)(const void *, const void *));
int (*fp)(int) = NULL;
""",
"""
<span class="hljs-keyword">typedef</span> <span class="hljs-type">int</span> (*compare_fn)(<span class="hljs-type">const</span> <span class="hljs-type">void</span> *, <span class="hljs-type">const</span> <span class="hljs-type">void</span> *);
<span class="hljs-type">void</span> <span class="hljs-title function_">qsort</span><span class="hljs-params">(<span class="hljs-type">void</span> *base, <span class="hljs-type">size_t</span> n, <span class="hljs-type">size_t</span> size, <span class="hljs-type">int</span> (*cmp)(<span class="hljs-type">const</span> <span class="hljs-type">void</span> *, <span class="hljs-type">const</span> <span class="hljs-type">void</span> *))</span>;
<span class="hljs-type">int</span> (*fp)(<span class="hljs-type">int</span>) = <span class="hljs-literal">NULL</span>;
""");
    }

    [Fact]
    public void Structs()
    {
        AssertHighlighter("c",
"""
struct point {
    int x;
    int y;
};

struct node {
    int value;
    struct node *next;
};

typedef struct {
    char name[32];
    int age;
} person_t;

typedef struct list list_t;
struct point p = {1, 2};
struct point *pp = &p;
union value { int i; float f; };
enum color { RED, GREEN = 5, BLUE };
enum color c = RED;
struct node **head;
""",
"""
<span class="hljs-class"><span class="hljs-keyword">struct</span> <span class="hljs-title">point</span> {</span>
    <span class="hljs-type">int</span> x;
    <span class="hljs-type">int</span> y;
};

<span class="hljs-class"><span class="hljs-keyword">struct</span> <span class="hljs-title">node</span> {</span>
    <span class="hljs-type">int</span> value;
    <span class="hljs-class"><span class="hljs-keyword">struct</span> <span class="hljs-title">node</span> *next;</span>
};

<span class="hljs-keyword">typedef</span> <span class="hljs-class"><span class="hljs-keyword">struct</span> {</span>
    <span class="hljs-type">char</span> name[<span class="hljs-number">32</span>];
    <span class="hljs-type">int</span> age;
} <span class="hljs-type">person_t</span>;

<span class="hljs-keyword">typedef</span> <span class="hljs-class"><span class="hljs-keyword">struct</span> <span class="hljs-title">list</span> list_t;</span>
<span class="hljs-class"><span class="hljs-keyword">struct</span> <span class="hljs-title">point</span> p =</span> {<span class="hljs-number">1</span>, <span class="hljs-number">2</span>};
<span class="hljs-class"><span class="hljs-keyword">struct</span> <span class="hljs-title">point</span> *pp =</span> &amp;p;
<span class="hljs-class"><span class="hljs-keyword">union</span> <span class="hljs-title">value</span> {</span> <span class="hljs-type">int</span> i; <span class="hljs-type">float</span> f; };
<span class="hljs-class"><span class="hljs-keyword">enum</span> <span class="hljs-title">color</span> {</span> RED, GREEN = <span class="hljs-number">5</span>, BLUE };
<span class="hljs-class"><span class="hljs-keyword">enum</span> <span class="hljs-title">color</span> c =</span> RED;
<span class="hljs-class"><span class="hljs-keyword">struct</span> <span class="hljs-title">node</span> **head;</span>
""");
    }

    [Fact]
    public void StructInFunction()
    {
        AssertHighlighter("c",
"""
void f(void) {
    struct node *n = malloc(sizeof(struct node));
    n->next = NULL;
    free(n);
}
""",
"""
<span class="hljs-type">void</span> <span class="hljs-title function_">f</span><span class="hljs-params">(<span class="hljs-type">void</span>)</span> {
    <span class="hljs-class"><span class="hljs-keyword">struct</span> <span class="hljs-title">node</span> *n =</span> <span class="hljs-built_in">malloc</span>(<span class="hljs-keyword">sizeof</span>(<span class="hljs-keyword">struct</span> node));
    n-&gt;next = <span class="hljs-literal">NULL</span>;
    <span class="hljs-built_in">free</span>(n);
}
""");
    }

    [Fact]
    public void Expressions()
    {
        AssertHighlighter("c",
"""
x = a + b * (c - d) / e % f;
y = a << 2 | b >> 1 & ~c ^ d;
z = !a && b || c;
p = &x; q = *p; r = p->field; s = obj.field;
t = cond ? 1 : 0;
u = (int)3.5;
v = sizeof(int);
w = sizeof x;
arr[0] = 1;
x += 1; x -= 1; x *= 2; x /= 2;
""",
"""
x = a + b * (c - d) / e % f;
y = a &lt;&lt; <span class="hljs-number">2</span> | b &gt;&gt; <span class="hljs-number">1</span> &amp; ~c ^ d;
z = !a &amp;&amp; b || c;
p = &amp;x; q = *p; r = p-&gt;field; s = obj.field;
t = cond ? <span class="hljs-number">1</span> : <span class="hljs-number">0</span>;
u = (<span class="hljs-type">int</span>)<span class="hljs-number">3.5</span>;
v = <span class="hljs-keyword">sizeof</span>(<span class="hljs-type">int</span>);
w = <span class="hljs-keyword">sizeof</span> x;
arr[<span class="hljs-number">0</span>] = <span class="hljs-number">1</span>;
x += <span class="hljs-number">1</span>; x -= <span class="hljs-number">1</span>; x *= <span class="hljs-number">2</span>; x /= <span class="hljs-number">2</span>;
""");
    }

    [Fact]
    public void Literals()
    {
        AssertHighlighter("c",
"""
bool t = true;
bool f = false;
void *p = NULL;
int *q = nullptr;
""",
"""
<span class="hljs-type">bool</span> t = <span class="hljs-literal">true</span>;
<span class="hljs-type">bool</span> f = <span class="hljs-literal">false</span>;
<span class="hljs-type">void</span> *p = <span class="hljs-literal">NULL</span>;
<span class="hljs-type">int</span> *q = nullptr;
""");
    }

    [Fact]
    public void Generic()
    {
        AssertHighlighter("c",
"""
#define type_name(x) _Generic((x), int: "int", default: "other")
_Static_assert(sizeof(int) == 4, "int must be 4 bytes");
static_assert(1, "ok");
_Noreturn void die(void);
_Thread_local int tls;
alignas(16) char buf[16];
typeof(x) y;
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">define</span> type_name(x) _Generic((x), int: <span class="hljs-string">&quot;int&quot;</span>, default: <span class="hljs-string">&quot;other&quot;</span>)</span>
<span class="hljs-keyword">_Static_assert</span>(<span class="hljs-keyword">sizeof</span>(<span class="hljs-type">int</span>) == <span class="hljs-number">4</span>, <span class="hljs-string">&quot;int must be 4 bytes&quot;</span>);
<span class="hljs-keyword">static_assert</span>(<span class="hljs-number">1</span>, <span class="hljs-string">&quot;ok&quot;</span>);
<span class="hljs-keyword">_Noreturn</span> <span class="hljs-type">void</span> <span class="hljs-title function_">die</span><span class="hljs-params">(<span class="hljs-type">void</span>)</span>;
<span class="hljs-keyword">_Thread_local</span> <span class="hljs-type">int</span> tls;
<span class="hljs-keyword">alignas</span>(<span class="hljs-number">16</span>) <span class="hljs-type">char</span> buf[<span class="hljs-number">16</span>];
<span class="hljs-keyword">typeof</span>(x) y;
""");
    }

    [Fact]
    public void Builtins()
    {
        AssertHighlighter("c",
"""
char *s = malloc(10);
memset(s, 0, 10);
strcpy(s, "hi");
size_t n = strlen(s);
double r = sqrt(pow(x, 2));
exit(0);
abort();
FILE *f = fopen("a", "r");
puts(s);
""",
"""
<span class="hljs-type">char</span> *s = <span class="hljs-built_in">malloc</span>(<span class="hljs-number">10</span>);
<span class="hljs-built_in">memset</span>(s, <span class="hljs-number">0</span>, <span class="hljs-number">10</span>);
<span class="hljs-built_in">strcpy</span>(s, <span class="hljs-string">&quot;hi&quot;</span>);
<span class="hljs-type">size_t</span> n = <span class="hljs-built_in">strlen</span>(s);
<span class="hljs-type">double</span> r = <span class="hljs-built_in">sqrt</span>(<span class="hljs-built_in">pow</span>(x, <span class="hljs-number">2</span>));
<span class="hljs-built_in">exit</span>(<span class="hljs-number">0</span>);
<span class="hljs-built_in">abort</span>();
FILE *f = fopen(<span class="hljs-string">&quot;a&quot;</span>, <span class="hljs-string">&quot;r&quot;</span>);
<span class="hljs-built_in">puts</span>(s);
""");
    }

    [Fact]
    public void ScopeQualifier()
    {
        AssertHighlighter("c",
"""
std::vector<int> v;
int x = ns::value;
""",
"""
<span class="hljs-built_in">std</span>::<span class="hljs-built_in">vector</span>&lt;<span class="hljs-type">int</span>&gt; v;
<span class="hljs-type">int</span> x = ns::value;
""");
    }

    [Fact]
    public void Illegal()
    {
        AssertHighlighter("c",
"""
int x = 1; </script>
int y;
""",
"""
<span class="hljs-type">int</span> x = <span class="hljs-number">1</span>; &lt;/script&gt;
<span class="hljs-type">int</span> y;
""");
    }

    [Fact]
    public void Asm()
    {
        AssertHighlighter("c",
"""
asm("nop");
__asm__ volatile ("mov %0, %1" : "=r"(x) : "r"(y));
""",
"""
<span class="hljs-keyword">asm</span>(<span class="hljs-string">&quot;nop&quot;</span>);
__asm__ <span class="hljs-keyword">volatile</span> (<span class="hljs-string">&quot;mov %0, %1&quot;</span> : <span class="hljs-string">&quot;=r&quot;</span>(x) : <span class="hljs-string">&quot;r&quot;</span>(y));
""");
    }

    [Fact]
    public void Restrict()
    {
        AssertHighlighter("c",
"""
void copy(char *restrict dst, const char *restrict src, size_t n);
""",
"""
<span class="hljs-type">void</span> <span class="hljs-title function_">copy</span><span class="hljs-params">(<span class="hljs-type">char</span> *<span class="hljs-keyword">restrict</span> dst, <span class="hljs-type">const</span> <span class="hljs-type">char</span> *<span class="hljs-keyword">restrict</span> src, <span class="hljs-type">size_t</span> n)</span>;
""");
    }

    [Fact]
    public void ArrayInit()
    {
        AssertHighlighter("c",
"""
int primes[] = {2, 3, 5, 7, 11};
char grid[3][3] = {{0}};
const char *names[] = {"alice", "bob", NULL};
struct point pts[] = {{.x = 1, .y = 2}, [2] = {0}};
""",
"""
<span class="hljs-type">int</span> primes[] = {<span class="hljs-number">2</span>, <span class="hljs-number">3</span>, <span class="hljs-number">5</span>, <span class="hljs-number">7</span>, <span class="hljs-number">11</span>};
<span class="hljs-type">char</span> grid[<span class="hljs-number">3</span>][<span class="hljs-number">3</span>] = {{<span class="hljs-number">0</span>}};
<span class="hljs-type">const</span> <span class="hljs-type">char</span> *names[] = {<span class="hljs-string">&quot;alice&quot;</span>, <span class="hljs-string">&quot;bob&quot;</span>, <span class="hljs-literal">NULL</span>};
<span class="hljs-class"><span class="hljs-keyword">struct</span> <span class="hljs-title">point</span> pts[] =</span> {{.x = <span class="hljs-number">1</span>, .y = <span class="hljs-number">2</span>}, [<span class="hljs-number">2</span>] = {<span class="hljs-number">0</span>}};
""");
    }

    [Fact]
    public void LongProgram()
    {
        AssertHighlighter("c",
"""
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define BUFFER_SIZE 1024

typedef struct buffer {
    char *data;
    size_t length;
    size_t capacity;
} buffer_t;

static buffer_t *buffer_new(size_t capacity)
{
    buffer_t *b = malloc(sizeof(*b));
    if (b == NULL)
        return NULL;
    b->data = calloc(capacity, 1);
    b->length = 0;
    b->capacity = capacity;
    return b;
}

/* Appends a string, growing the buffer when needed. */
int buffer_append(buffer_t *b, const char *s)
{
    size_t n = strlen(s);
    while (b->length + n + 1 > b->capacity) {
        b->capacity *= 2;
        b->data = realloc(b->data, b->capacity);
    }
    memcpy(b->data + b->length, s, n + 1);
    b->length += n;
    return 0;
}
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&lt;stdio.h&gt;</span></span>
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&lt;stdlib.h&gt;</span></span>
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&lt;string.h&gt;</span></span>

<span class="hljs-meta">#<span class="hljs-keyword">define</span> BUFFER_SIZE 1024</span>

<span class="hljs-keyword">typedef</span> <span class="hljs-class"><span class="hljs-keyword">struct</span> <span class="hljs-title">buffer</span> {</span>
    <span class="hljs-type">char</span> *data;
    <span class="hljs-type">size_t</span> length;
    <span class="hljs-type">size_t</span> capacity;
} <span class="hljs-type">buffer_t</span>;

<span class="hljs-type">static</span> <span class="hljs-type">buffer_t</span> *<span class="hljs-title function_">buffer_new</span><span class="hljs-params">(<span class="hljs-type">size_t</span> capacity)</span>
{
    <span class="hljs-type">buffer_t</span> *b = <span class="hljs-built_in">malloc</span>(<span class="hljs-keyword">sizeof</span>(*b));
    <span class="hljs-keyword">if</span> (b == <span class="hljs-literal">NULL</span>)
        <span class="hljs-keyword">return</span> <span class="hljs-literal">NULL</span>;
    b-&gt;data = <span class="hljs-built_in">calloc</span>(capacity, <span class="hljs-number">1</span>);
    b-&gt;length = <span class="hljs-number">0</span>;
    b-&gt;capacity = capacity;
    <span class="hljs-keyword">return</span> b;
}

<span class="hljs-comment">/* Appends a string, growing the buffer when needed. */</span>
<span class="hljs-type">int</span> <span class="hljs-title function_">buffer_append</span><span class="hljs-params">(<span class="hljs-type">buffer_t</span> *b, <span class="hljs-type">const</span> <span class="hljs-type">char</span> *s)</span>
{
    <span class="hljs-type">size_t</span> n = <span class="hljs-built_in">strlen</span>(s);
    <span class="hljs-keyword">while</span> (b-&gt;length + n + <span class="hljs-number">1</span> &gt; b-&gt;capacity) {
        b-&gt;capacity *= <span class="hljs-number">2</span>;
        b-&gt;data = <span class="hljs-built_in">realloc</span>(b-&gt;data, b-&gt;capacity);
    }
    <span class="hljs-built_in">memcpy</span>(b-&gt;data + b-&gt;length, s, n + <span class="hljs-number">1</span>);
    b-&gt;length += n;
    <span class="hljs-keyword">return</span> <span class="hljs-number">0</span>;
}
""");
    }

    [Fact]
    public void KAndR()
    {
        AssertHighlighter("c",
"""
int
main(argc, argv)
int argc;
char **argv;
{
    return 0;
}
""",
"""
<span class="hljs-type">int</span>
<span class="hljs-title function_">main</span><span class="hljs-params">(argc, argv)</span>
<span class="hljs-type">int</span> argc;
<span class="hljs-type">char</span> **argv;
{
    <span class="hljs-keyword">return</span> <span class="hljs-number">0</span>;
}
""");
    }

    [Fact]
    public void Bitfields()
    {
        AssertHighlighter("c",
"""
struct flags {
    unsigned int a : 1;
    unsigned int b : 3;
};
""",
"""
<span class="hljs-class"><span class="hljs-keyword">struct</span> <span class="hljs-title">flags</span> {</span>
    <span class="hljs-type">unsigned</span> <span class="hljs-type">int</span> a : <span class="hljs-number">1</span>;
    <span class="hljs-type">unsigned</span> <span class="hljs-type">int</span> b : <span class="hljs-number">3</span>;
};
""");
    }

    [Fact]
    public void HeaderGuard()
    {
        AssertHighlighter("c",
"""
#ifndef MY_HEADER_H
#define MY_HEADER_H

#ifdef __cplusplus
extern "C" {
#endif

void api(void);

#ifdef __cplusplus
}
#endif

#endif /* MY_HEADER_H */
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">ifndef</span> MY_HEADER_H</span>
<span class="hljs-meta">#<span class="hljs-keyword">define</span> MY_HEADER_H</span>

<span class="hljs-meta">#<span class="hljs-keyword">ifdef</span> __cplusplus</span>
<span class="hljs-keyword">extern</span> <span class="hljs-string">&quot;C&quot;</span> {
<span class="hljs-meta">#<span class="hljs-keyword">endif</span></span>

<span class="hljs-type">void</span> <span class="hljs-title function_">api</span><span class="hljs-params">(<span class="hljs-type">void</span>)</span>;

<span class="hljs-meta">#<span class="hljs-keyword">ifdef</span> __cplusplus</span>
}
<span class="hljs-meta">#<span class="hljs-keyword">endif</span></span>

<span class="hljs-meta">#<span class="hljs-keyword">endif</span> <span class="hljs-comment">/* MY_HEADER_H */</span></span>
""");
    }

    [Fact]
    public void Variadic()
    {
        AssertHighlighter("c",
"""
int sum(int count, ...)
{
    va_list args;
    va_start(args, count);
    va_end(args);
    return 0;
}
""",
"""
<span class="hljs-type">int</span> <span class="hljs-title function_">sum</span><span class="hljs-params">(<span class="hljs-type">int</span> count, ...)</span>
{
    va_list args;
    va_start(args, count);
    va_end(args);
    <span class="hljs-keyword">return</span> <span class="hljs-number">0</span>;
}
""");
    }

    [Fact]
    public void NegativeNumbers()
    {
        AssertHighlighter("c",
"""
x = y-1;
z = -1.5f;
w = a - -2;
""",
"""
x = y-<span class="hljs-number">1</span>;
z = <span class="hljs-number">-1.5f</span>;
w = a - <span class="hljs-number">-2</span>;
""");
    }

    [Fact]
    public void NumberSuffixes()
    {
        AssertHighlighter("c",
"""
unsigned long a = 0xFFFFFFFFUL;
unsigned long long b = 0x10ull;
long long c = 10LLU;
float d = 1.0e-6f;
float e = 1e10F;
long double f = 1.5L;
double g = 0x1p-3;
float h = 0x1.fp+2f;
int i = 0B11u;
int j = 0b1'0000'0001;
""",
"""
<span class="hljs-type">unsigned</span> <span class="hljs-type">long</span> a = <span class="hljs-number">0xFFFFFFFFUL</span>;
<span class="hljs-type">unsigned</span> <span class="hljs-type">long</span> <span class="hljs-type">long</span> b = <span class="hljs-number">0x10ull</span>;
<span class="hljs-type">long</span> <span class="hljs-type">long</span> c = <span class="hljs-number">10LLU</span>;
<span class="hljs-type">float</span> d = <span class="hljs-number">1.0e-6f</span>;
<span class="hljs-type">float</span> e = <span class="hljs-number">1e10F</span>;
<span class="hljs-type">long</span> <span class="hljs-type">double</span> f = <span class="hljs-number">1.5L</span>;
<span class="hljs-type">double</span> g = <span class="hljs-number">0x1p-3</span>;
<span class="hljs-type">float</span> h = <span class="hljs-number">0x1.fp+2f</span>;
<span class="hljs-type">int</span> i = <span class="hljs-number">0B11u</span>;
<span class="hljs-type">int</span> j = <span class="hljs-number">0b1&#x27;0000&#x27;0001</span>;
""");
    }

    [Fact]
    public void NumberSigns()
    {
        AssertHighlighter("c",
"""
x = n-1;
y = a[i-1];
z = f(x)-1;
w = -1;
v = (-2);
u = a - -3;
t = -0x10;
s = x*-1;
r = {-1, -2};
""",
"""
x = n-<span class="hljs-number">1</span>;
y = a[i-<span class="hljs-number">1</span>];
z = f(x)-<span class="hljs-number">1</span>;
w = <span class="hljs-number">-1</span>;
v = (<span class="hljs-number">-2</span>);
u = a - <span class="hljs-number">-3</span>;
t = <span class="hljs-number">-0x10</span>;
s = x*<span class="hljs-number">-1</span>;
r = {<span class="hljs-number">-1</span>, <span class="hljs-number">-2</span>};
""");
    }

    [Fact]
    public void NumberFractions()
    {
        AssertHighlighter("c",
"""
double a = .5;
float b = .5f;
double c = 1.;
double d = 1.e5;
double e = .5e-3;
x = s.field5;
y = arr[.5];
""",
"""
<span class="hljs-type">double</span> a = <span class="hljs-number">.5</span>;
<span class="hljs-type">float</span> b = <span class="hljs-number">.5f</span>;
<span class="hljs-type">double</span> c = <span class="hljs-number">1.</span>;
<span class="hljs-type">double</span> d = <span class="hljs-number">1.e5</span>;
<span class="hljs-type">double</span> e = <span class="hljs-number">.5e-3</span>;
x = s.field5;
y = arr[<span class="hljs-number">.5</span>];
""");
    }

    [Fact]
    public void FunctionPointerTypedefs()
    {
        AssertHighlighter("c",
"""
typedef void (*callback_t)(int);
typedef int (*compare_fn)(const void *, const void *);
unsigned int (*get(void))(int);
static void (*handlers[4])(void);
void register_handler(void (*handler)(int));
""",
"""
<span class="hljs-keyword">typedef</span> <span class="hljs-type">void</span> (*<span class="hljs-type">callback_t</span>)(<span class="hljs-type">int</span>);
<span class="hljs-keyword">typedef</span> <span class="hljs-type">int</span> (*compare_fn)(<span class="hljs-type">const</span> <span class="hljs-type">void</span> *, <span class="hljs-type">const</span> <span class="hljs-type">void</span> *);
<span class="hljs-type">unsigned</span> <span class="hljs-type">int</span> (*get(<span class="hljs-type">void</span>))(<span class="hljs-type">int</span>);
<span class="hljs-type">static</span> <span class="hljs-type">void</span> (*handlers[<span class="hljs-number">4</span>])(<span class="hljs-type">void</span>);
<span class="hljs-type">void</span> <span class="hljs-title function_">register_handler</span><span class="hljs-params">(<span class="hljs-type">void</span> (*handler)(<span class="hljs-type">int</span>))</span>;
""");
    }

    [Fact]
    public void LabelAndGoto()
    {
        AssertHighlighter("c",
"""
void f(void) {
retry:
    if (fail()) goto retry;
}
""",
"""
<span class="hljs-type">void</span> <span class="hljs-title function_">f</span><span class="hljs-params">(<span class="hljs-type">void</span>)</span> {
retry:
    <span class="hljs-keyword">if</span> (fail()) <span class="hljs-keyword">goto</span> retry;
}
""");
    }

    [Fact]
    public void HeaderAliases_HIsCButHppIsCpp()
    {
        const string Code = "auto p = nullptr;";
        Assert.Equal(SyntaxHighlighter.Highlight(Code, "c"), SyntaxHighlighter.Highlight(Code, "h"));
        Assert.Equal(SyntaxHighlighter.Highlight(Code, "cpp"), SyntaxHighlighter.Highlight(Code, "hpp"));
        Assert.Equal(SyntaxHighlighter.Highlight(Code, "cpp"), SyntaxHighlighter.Highlight(Code, "h++"));
        Assert.NotEqual(SyntaxHighlighter.Highlight(Code, "c"), SyntaxHighlighter.Highlight(Code, "hpp"));
    }
}
