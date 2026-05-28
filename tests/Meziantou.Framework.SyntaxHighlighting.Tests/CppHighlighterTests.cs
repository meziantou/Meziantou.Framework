namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class CppHighlighterTests
{

    [Fact]
    public void Keyword_IntDecl()
    {
        AssertHighlighter("cpp",
"""
int x = 42;
""",
"""
<span class="hljs-type">int</span> x = <span class="hljs-number">42</span>;
""");
    }

    [Fact]
    public void Keyword_Auto()
    {
        AssertHighlighter("cpp",
"""
auto x = 42;
""",
"""
<span class="hljs-keyword">auto</span> x = <span class="hljs-number">42</span>;
""");
    }

    [Fact]
    public void Keyword_AutoReference()
    {
        AssertHighlighter("cpp",
"""
auto& x = obj;
""",
"""
<span class="hljs-keyword">auto</span>&amp; x = obj;
""");
    }

    [Fact]
    public void Keyword_AutoUniversal()
    {
        AssertHighlighter("cpp",
"""
auto&& x = make();
""",
"""
<span class="hljs-keyword">auto</span>&amp;&amp; x = <span class="hljs-built_in">make</span>();
""");
    }

    [Fact]
    public void Keyword_AutoStructured()
    {
        AssertHighlighter("cpp",
"""
auto [a, b] = pair;
""",
"""
<span class="hljs-keyword">auto</span> [a, b] = pair;
""");
    }

    [Fact]
    public void Keyword_Const()
    {
        AssertHighlighter("cpp",
"""
const int x = 0;
""",
"""
<span class="hljs-type">const</span> <span class="hljs-type">int</span> x = <span class="hljs-number">0</span>;
""");
    }

    [Fact]
    public void Keyword_Constexpr()
    {
        AssertHighlighter("cpp",
"""
constexpr int x = 42;
""",
"""
<span class="hljs-keyword">constexpr</span> <span class="hljs-type">int</span> x = <span class="hljs-number">42</span>;
""");
    }

    [Fact]
    public void Keyword_Consteval()
    {
        AssertHighlighter("cpp",
"""
consteval int square(int x) { return x * x; }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">consteval</span> <span class="hljs-type">int</span> <span class="hljs-title">square</span><span class="hljs-params">(<span class="hljs-type">int</span> x)</span> </span>{ <span class="hljs-keyword">return</span> x * x; }
""");
    }

    [Fact]
    public void Keyword_Constinit()
    {
        AssertHighlighter("cpp",
"""
constinit int value = 0;
""",
"""
<span class="hljs-keyword">constinit</span> <span class="hljs-type">int</span> value = <span class="hljs-number">0</span>;
""");
    }

    [Fact]
    public void Keyword_Inline()
    {
        AssertHighlighter("cpp",
"""
inline void run() { }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">inline</span> <span class="hljs-type">void</span> <span class="hljs-title">run</span><span class="hljs-params">()</span> </span>{ }
""");
    }

    [Fact]
    public void Keyword_InlineVariable()
    {
        AssertHighlighter("cpp",
"""
inline constexpr double pi = 3.14159;
""",
"""
<span class="hljs-keyword">inline</span> <span class="hljs-keyword">constexpr</span> <span class="hljs-type">double</span> pi = <span class="hljs-number">3.14159</span>;
""");
    }

    [Fact]
    public void Keyword_Static()
    {
        AssertHighlighter("cpp",
"""
static int counter = 0;
""",
"""
<span class="hljs-type">static</span> <span class="hljs-type">int</span> counter = <span class="hljs-number">0</span>;
""");
    }

    [Fact]
    public void Keyword_Extern()
    {
        AssertHighlighter("cpp",
"""
extern int sharedCounter;
""",
"""
<span class="hljs-keyword">extern</span> <span class="hljs-type">int</span> sharedCounter;
""");
    }

    [Fact]
    public void Keyword_ExternC()
    {
        AssertHighlighter("cpp",
"""
extern "C" void foo();
""",
"""
<span class="hljs-keyword">extern</span> <span class="hljs-string">&quot;C&quot;</span> <span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">foo</span><span class="hljs-params">()</span></span>;
""");
    }

    [Fact]
    public void Keyword_ThreadLocal()
    {
        AssertHighlighter("cpp",
"""
thread_local int tls_id = 0;
""",
"""
<span class="hljs-keyword">thread_local</span> <span class="hljs-type">int</span> tls_id = <span class="hljs-number">0</span>;
""");
    }

    [Fact]
    public void Keyword_Volatile()
    {
        AssertHighlighter("cpp",
"""
volatile int flag = 0;
""",
"""
<span class="hljs-keyword">volatile</span> <span class="hljs-type">int</span> flag = <span class="hljs-number">0</span>;
""");
    }

    [Fact]
    public void Keyword_Mutable()
    {
        AssertHighlighter("cpp",
"""
class C { mutable int cache; };
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">C</span> { <span class="hljs-keyword">mutable</span> <span class="hljs-type">int</span> cache; };
""");
    }

    [Fact]
    public void Keyword_Restrict()
    {
        AssertHighlighter("cpp",
"""
void* __restrict ptr;
""",
"""
<span class="hljs-type">void</span>* __restrict ptr;
""");
    }

    [Fact]
    public void Keyword_Sizeof()
    {
        AssertHighlighter("cpp",
"""
auto n = sizeof(int);
""",
"""
<span class="hljs-keyword">auto</span> n = <span class="hljs-built_in">sizeof</span>(<span class="hljs-type">int</span>);
""");
    }

    [Fact]
    public void Keyword_SizeofParenless()
    {
        AssertHighlighter("cpp",
"""
auto n = sizeof x;
""",
"""
<span class="hljs-keyword">auto</span> n = <span class="hljs-keyword">sizeof</span> x;
""");
    }

    [Fact]
    public void Keyword_Alignof()
    {
        AssertHighlighter("cpp",
"""
auto a = alignof(double);
""",
"""
<span class="hljs-keyword">auto</span> a = <span class="hljs-built_in">alignof</span>(<span class="hljs-type">double</span>);
""");
    }

    [Fact]
    public void Keyword_Alignas()
    {
        AssertHighlighter("cpp",
"""
alignas(16) char buffer[64];
""",
"""
<span class="hljs-built_in">alignas</span>(<span class="hljs-number">16</span>) <span class="hljs-type">char</span> buffer[<span class="hljs-number">64</span>];
""");
    }

    [Fact]
    public void Keyword_Decltype()
    {
        AssertHighlighter("cpp",
"""
decltype(x) y = x;
""",
"""
<span class="hljs-keyword">decltype</span>(x) y = x;
""");
    }

    [Fact]
    public void Keyword_DecltypeAuto()
    {
        AssertHighlighter("cpp",
"""
decltype(auto) get() { return obj; }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">decltype</span>(<span class="hljs-keyword">auto</span>) <span class="hljs-title">get</span><span class="hljs-params">()</span> </span>{ <span class="hljs-keyword">return</span> obj; }
""");
    }

    [Fact]
    public void Keyword_Typeid()
    {
        AssertHighlighter("cpp",
"""
auto& info = typeid(x);
""",
"""
<span class="hljs-keyword">auto</span>&amp; info = <span class="hljs-built_in">typeid</span>(x);
""");
    }

    [Fact]
    public void Keyword_Typedef()
    {
        AssertHighlighter("cpp",
"""
typedef unsigned long long u64;
""",
"""
<span class="hljs-keyword">typedef</span> <span class="hljs-type">unsigned</span> <span class="hljs-type">long</span> <span class="hljs-type">long</span> u64;
""");
    }

    [Fact]
    public void Keyword_UsingAlias()
    {
        AssertHighlighter("cpp",
"""
using u64 = unsigned long long;
""",
"""
<span class="hljs-keyword">using</span> u64 = <span class="hljs-type">unsigned</span> <span class="hljs-type">long</span> <span class="hljs-type">long</span>;
""");
    }

    [Fact]
    public void Keyword_UsingNamespace()
    {
        AssertHighlighter("cpp",
"""
using namespace std;
""",
"""
<span class="hljs-keyword">using</span> <span class="hljs-keyword">namespace</span> std;
""");
    }

    [Fact]
    public void Keyword_UsingType()
    {
        AssertHighlighter("cpp",
"""
using std::vector;
""",
"""
<span class="hljs-keyword">using</span> std::vector;
""");
    }

    [Fact]
    public void Modifier_Override()
    {
        AssertHighlighter("cpp",
"""
class D : public B { public: void Run() override; };
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">D</span> : <span class="hljs-keyword">public</span> B { <span class="hljs-keyword">public</span>: <span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">Run</span><span class="hljs-params">()</span> <span class="hljs-keyword">override</span></span>; };
""");
    }

    [Fact]
    public void Modifier_Final()
    {
        AssertHighlighter("cpp",
"""
class F final { };
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">F</span> <span class="hljs-keyword">final</span> { };
""");
    }

    [Fact]
    public void Modifier_Explicit()
    {
        AssertHighlighter("cpp",
"""
class T { public: explicit T(int x); };
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">T</span> { <span class="hljs-keyword">public</span>: <span class="hljs-function"><span class="hljs-keyword">explicit</span> <span class="hljs-title">T</span><span class="hljs-params">(<span class="hljs-type">int</span> x)</span></span>; };
""");
    }

    [Fact]
    public void Modifier_Friend()
    {
        AssertHighlighter("cpp",
"""
class A { friend class B; };
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">A</span> { <span class="hljs-keyword">friend</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">B</span>; };
""");
    }

    [Fact]
    public void Modifier_Virtual()
    {
        AssertHighlighter("cpp",
"""
class B { public: virtual void Run(); };
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">B</span> { <span class="hljs-keyword">public</span>: <span class="hljs-function"><span class="hljs-keyword">virtual</span> <span class="hljs-type">void</span> <span class="hljs-title">Run</span><span class="hljs-params">()</span></span>; };
""");
    }

    [Fact]
    public void Modifier_Noexcept()
    {
        AssertHighlighter("cpp",
"""
void run() noexcept;
""",
"""
<span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">run</span><span class="hljs-params">()</span> <span class="hljs-keyword">noexcept</span></span>;
""");
    }

    [Fact]
    public void Modifier_NoexceptExpr()
    {
        AssertHighlighter("cpp",
"""
void run() noexcept(noexcept(other()));
""",
"""
<span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">run</span><span class="hljs-params">()</span> <span class="hljs-title">noexcept</span><span class="hljs-params">(<span class="hljs-keyword">noexcept</span>(other()))</span></span>;
""");
    }

    [Fact]
    public void Modifier_Throw()
    {
        AssertHighlighter("cpp",
"""
void run() throw();
""",
"""
<span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">run</span><span class="hljs-params">()</span> <span class="hljs-title">throw</span><span class="hljs-params">()</span></span>;
""");
    }

    [Fact]
    public void PrimitiveType_IntegerFamily()
    {
        AssertHighlighter("cpp",
"""
short s; int i; long l; long long ll;
""",
"""
<span class="hljs-type">short</span> s; <span class="hljs-type">int</span> i; <span class="hljs-type">long</span> l; <span class="hljs-type">long</span> <span class="hljs-type">long</span> ll;
""");
    }

    [Fact]
    public void PrimitiveType_UnsignedFamily()
    {
        AssertHighlighter("cpp",
"""
unsigned int u; size_t n; ptrdiff_t d;
""",
"""
<span class="hljs-type">unsigned</span> <span class="hljs-type">int</span> u; <span class="hljs-type">size_t</span> n; <span class="hljs-type">ptrdiff_t</span> d;
""");
    }

    [Fact]
    public void PrimitiveType_CharFamily()
    {
        AssertHighlighter("cpp",
"""
char c; signed char sc; unsigned char uc; wchar_t w; char8_t c8; char16_t c16; char32_t c32;
""",
"""
<span class="hljs-type">char</span> c; <span class="hljs-type">signed</span> <span class="hljs-type">char</span> sc; <span class="hljs-type">unsigned</span> <span class="hljs-type">char</span> uc; <span class="hljs-type">wchar_t</span> w; <span class="hljs-type">char8_t</span> c8; <span class="hljs-type">char16_t</span> c16; <span class="hljs-type">char32_t</span> c32;
""");
    }

    [Fact]
    public void PrimitiveType_FloatFamily()
    {
        AssertHighlighter("cpp",
"""
float f; double d; long double ld;
""",
"""
<span class="hljs-type">float</span> f; <span class="hljs-type">double</span> d; <span class="hljs-type">long</span> <span class="hljs-type">double</span> ld;
""");
    }

    [Fact]
    public void PrimitiveType_Bool()
    {
        AssertHighlighter("cpp",
"""
bool flag = true;
""",
"""
<span class="hljs-type">bool</span> flag = <span class="hljs-literal">true</span>;
""");
    }

    [Fact]
    public void PrimitiveType_Void()
    {
        AssertHighlighter("cpp",
"""
void run();
""",
"""
<span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">run</span><span class="hljs-params">()</span></span>;
""");
    }

    [Fact]
    public void PrimitiveType_FixedWidth()
    {
        AssertHighlighter("cpp",
"""
#include <cstdint>
int8_t a; int16_t b; int32_t c; int64_t d;
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&lt;cstdint&gt;</span></span>
<span class="hljs-type">int8_t</span> a; <span class="hljs-type">int16_t</span> b; <span class="hljs-type">int32_t</span> c; <span class="hljs-type">int64_t</span> d;
""");
    }

    [Fact]
    public void PrimitiveType_Nullptr()
    {
        AssertHighlighter("cpp",
"""
int* p = nullptr;
""",
"""
<span class="hljs-type">int</span>* p = <span class="hljs-literal">nullptr</span>;
""");
    }

    [Fact]
    public void String_Simple()
    {
        AssertHighlighter("cpp",
"""
auto s = "hello";
""",
"""
<span class="hljs-keyword">auto</span> s = <span class="hljs-string">&quot;hello&quot;</span>;
""");
    }

    [Fact]
    public void String_EscapeQuote()
    {
        AssertHighlighter("cpp",
"""
auto s = "She said \"hi\"";
""",
"""
<span class="hljs-keyword">auto</span> s = <span class="hljs-string">&quot;She said \&quot;hi\&quot;&quot;</span>;
""");
    }

    [Fact]
    public void String_EscapeBackslash()
    {
        AssertHighlighter("cpp",
"""
auto s = "a\\b";
""",
"""
<span class="hljs-keyword">auto</span> s = <span class="hljs-string">&quot;a\\b&quot;</span>;
""");
    }

    [Fact]
    public void String_EscapeNewline()
    {
        AssertHighlighter("cpp",
"""
auto s = "line1\nline2";
""",
"""
<span class="hljs-keyword">auto</span> s = <span class="hljs-string">&quot;line1\nline2&quot;</span>;
""");
    }

    [Fact]
    public void String_EscapeOctal()
    {
        AssertHighlighter("cpp",
"""
auto s = "\101";
""",
"""
<span class="hljs-keyword">auto</span> s = <span class="hljs-string">&quot;\101&quot;</span>;
""");
    }

    [Fact]
    public void String_EscapeHex()
    {
        AssertHighlighter("cpp",
"""
auto s = "\x41";
""",
"""
<span class="hljs-keyword">auto</span> s = <span class="hljs-string">&quot;\x41&quot;</span>;
""");
    }

    [Fact]
    public void String_EscapeUnicodeU4()
    {
        AssertHighlighter("cpp",
"""
auto s = "\u0041";
""",
"""
<span class="hljs-keyword">auto</span> s = <span class="hljs-string">&quot;\u0041&quot;</span>;
""");
    }

    [Fact]
    public void String_EscapeUnicodeU8()
    {
        AssertHighlighter("cpp",
"""
auto s = "\U00000041";
""",
"""
<span class="hljs-keyword">auto</span> s = <span class="hljs-string">&quot;\U00000041&quot;</span>;
""");
    }

    [Fact]
    public void String_Wide()
    {
        AssertHighlighter("cpp",
"""
auto s = L"wide";
""",
"""
<span class="hljs-keyword">auto</span> s = <span class="hljs-string">L&quot;wide&quot;</span>;
""");
    }

    [Fact]
    public void String_Utf8()
    {
        AssertHighlighter("cpp",
"""
auto s = u8"utf8";
""",
"""
<span class="hljs-keyword">auto</span> s = <span class="hljs-string">u8&quot;utf8&quot;</span>;
""");
    }

    [Fact]
    public void String_Utf16()
    {
        AssertHighlighter("cpp",
"""
auto s = u"utf16";
""",
"""
<span class="hljs-keyword">auto</span> s = <span class="hljs-string">u&quot;utf16&quot;</span>;
""");
    }

    [Fact]
    public void String_Utf32()
    {
        AssertHighlighter("cpp",
"""
auto s = U"utf32";
""",
"""
<span class="hljs-keyword">auto</span> s = <span class="hljs-string">U&quot;utf32&quot;</span>;
""");
    }

    [Fact]
    public void String_Raw()
    {
        AssertHighlighter("cpp",
"""
auto s = R"(no escape needed here)";
""",
"""
<span class="hljs-keyword">auto</span> s = <span class="hljs-string">R&quot;(no escape needed here)&quot;</span>;
""");
    }

    [Fact]
    public void String_RawDelimiter()
    {
        AssertHighlighter("cpp",
"""
auto s = R"END(contains )" inside)END";
""",
"""
<span class="hljs-keyword">auto</span> s = <span class="hljs-string">R&quot;END(contains )&quot; inside)END&quot;</span>;
""");
    }

    [Fact]
    public void String_RawMultiLine()
    {
        AssertHighlighter("cpp",
"""
auto s = R"(
first line
second line
)";
""",
"""
<span class="hljs-keyword">auto</span> s = <span class="hljs-string">R&quot;(
first line
second line
)&quot;</span>;
""");
    }

    [Fact]
    public void String_StdStringLit()
    {
        AssertHighlighter("cpp",
"""
using namespace std::string_literals;
auto s = "hello"s;
""",
"""
<span class="hljs-keyword">using</span> <span class="hljs-keyword">namespace</span> std::string_literals;
<span class="hljs-keyword">auto</span> s = <span class="hljs-string">&quot;hello&quot;</span>s;
""");
    }

    [Fact]
    public void String_StdStringViewLit()
    {
        AssertHighlighter("cpp",
"""
using namespace std::string_view_literals;
auto s = "hello"sv;
""",
"""
<span class="hljs-keyword">using</span> <span class="hljs-keyword">namespace</span> std::string_view_literals;
<span class="hljs-keyword">auto</span> s = <span class="hljs-string">&quot;hello&quot;</span>sv;
""");
    }

    [Fact]
    public void String_CharLiteral()
    {
        AssertHighlighter("cpp",
"""
auto c = 'A';
""",
"""
<span class="hljs-keyword">auto</span> c = <span class="hljs-string">&#x27;A&#x27;</span>;
""");
    }

    [Fact]
    public void String_CharEscape()
    {
        AssertHighlighter("cpp",
"""
auto nl = '\n';
""",
"""
<span class="hljs-keyword">auto</span> nl = <span class="hljs-string">&#x27;\n&#x27;</span>;
""");
    }

    [Fact]
    public void Number_Integer()
    {
        AssertHighlighter("cpp",
"""
auto n = 42;
""",
"""
<span class="hljs-keyword">auto</span> n = <span class="hljs-number">42</span>;
""");
    }

    [Fact]
    public void Number_LongSuffix()
    {
        AssertHighlighter("cpp",
"""
auto n = 42L;
""",
"""
<span class="hljs-keyword">auto</span> n = <span class="hljs-number">42L</span>;
""");
    }

    [Fact]
    public void Number_LongLongSuffix()
    {
        AssertHighlighter("cpp",
"""
auto n = 42LL;
""",
"""
<span class="hljs-keyword">auto</span> n = <span class="hljs-number">42LL</span>;
""");
    }

    [Fact]
    public void Number_UnsignedSuffix()
    {
        AssertHighlighter("cpp",
"""
auto n = 42u;
""",
"""
<span class="hljs-keyword">auto</span> n = <span class="hljs-number">42u</span>;
""");
    }

    [Fact]
    public void Number_UnsignedLong()
    {
        AssertHighlighter("cpp",
"""
auto n = 42UL;
""",
"""
<span class="hljs-keyword">auto</span> n = <span class="hljs-number">42UL</span>;
""");
    }

    [Fact]
    public void Number_Float()
    {
        AssertHighlighter("cpp",
"""
auto n = 3.14f;
""",
"""
<span class="hljs-keyword">auto</span> n = <span class="hljs-number">3.14f</span>;
""");
    }

    [Fact]
    public void Number_Double()
    {
        AssertHighlighter("cpp",
"""
auto n = 3.14;
""",
"""
<span class="hljs-keyword">auto</span> n = <span class="hljs-number">3.14</span>;
""");
    }

    [Fact]
    public void Number_LongDouble()
    {
        AssertHighlighter("cpp",
"""
auto n = 3.14L;
""",
"""
<span class="hljs-keyword">auto</span> n = <span class="hljs-number">3.14L</span>;
""");
    }

    [Fact]
    public void Number_Hex()
    {
        AssertHighlighter("cpp",
"""
auto n = 0xDEADBEEF;
""",
"""
<span class="hljs-keyword">auto</span> n = <span class="hljs-number">0xDEADBEEF</span>;
""");
    }

    [Fact]
    public void Number_Binary()
    {
        AssertHighlighter("cpp",
"""
auto n = 0b1010'1100;
""",
"""
<span class="hljs-keyword">auto</span> n = <span class="hljs-number">0b1010&#x27;1100</span>;
""");
    }

    [Fact]
    public void Number_Octal()
    {
        AssertHighlighter("cpp",
"""
auto n = 0755;
""",
"""
<span class="hljs-keyword">auto</span> n = <span class="hljs-number">0755</span>;
""");
    }

    [Fact]
    public void Number_DigitSeparator()
    {
        AssertHighlighter("cpp",
"""
auto n = 1'000'000;
""",
"""
<span class="hljs-keyword">auto</span> n = <span class="hljs-number">1&#x27;000&#x27;000</span>;
""");
    }

    [Fact]
    public void Number_Exponent()
    {
        AssertHighlighter("cpp",
"""
auto n = 1.5e10;
""",
"""
<span class="hljs-keyword">auto</span> n = <span class="hljs-number">1.5e10</span>;
""");
    }

    [Fact]
    public void Number_UserLiteral()
    {
        AssertHighlighter("cpp",
"""
using namespace std::chrono_literals;
auto d = 100ms;
""",
"""
<span class="hljs-keyword">using</span> <span class="hljs-keyword">namespace</span> std::chrono_literals;
<span class="hljs-keyword">auto</span> d = <span class="hljs-number">100</span>ms;
""");
    }

    [Fact]
    public void Preprocessor_IncludeAngle()
    {
        AssertHighlighter("cpp",
"""
#include <iostream>
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&lt;iostream&gt;</span></span>
""");
    }

    [Fact]
    public void Preprocessor_IncludeQuotes()
    {
        AssertHighlighter("cpp",
"""
#include "myheader.h"
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&quot;myheader.h&quot;</span></span>
""");
    }

    [Fact]
    public void Preprocessor_Define()
    {
        AssertHighlighter("cpp",
"""
#define MAX_SIZE 1024
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">define</span> MAX_SIZE 1024</span>
""");
    }

    [Fact]
    public void Preprocessor_DefineFunctionLike()
    {
        AssertHighlighter("cpp",
"""
#define SQUARE(x) ((x) * (x))
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">define</span> SQUARE(x) ((x) * (x))</span>
""");
    }

    [Fact]
    public void Preprocessor_DefineMultiLine()
    {
        AssertHighlighter("cpp",
"""
#define DEBUG_PRINT(fmt, ...) \
    do { fprintf(stderr, fmt, __VA_ARGS__); } while (0)
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">define</span> DEBUG_PRINT(fmt, ...) \
    do { fprintf(stderr, fmt, __VA_ARGS__); } while (0)</span>
""");
    }

    [Fact]
    public void Preprocessor_Undef()
    {
        AssertHighlighter("cpp",
"""
#undef MAX_SIZE
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">undef</span> MAX_SIZE</span>
""");
    }

    [Fact]
    public void Preprocessor_IfDefined()
    {
        AssertHighlighter("cpp",
"""
#ifdef DEBUG
#include <iostream>
#endif
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">ifdef</span> DEBUG</span>
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&lt;iostream&gt;</span></span>
<span class="hljs-meta">#<span class="hljs-keyword">endif</span></span>
""");
    }

    [Fact]
    public void Preprocessor_IfElseElif()
    {
        AssertHighlighter("cpp",
"""
#if defined(_WIN32)
#include <windows.h>
#elif defined(__linux__)
#include <unistd.h>
#else
#error Unsupported platform
#endif
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">if</span> defined(_WIN32)</span>
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&lt;windows.h&gt;</span></span>
<span class="hljs-meta">#<span class="hljs-keyword">elif</span> defined(__linux__)</span>
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&lt;unistd.h&gt;</span></span>
<span class="hljs-meta">#<span class="hljs-keyword">else</span></span>
<span class="hljs-meta">#<span class="hljs-keyword">error</span> Unsupported platform</span>
<span class="hljs-meta">#<span class="hljs-keyword">endif</span></span>
""");
    }

    [Fact]
    public void Preprocessor_Pragma()
    {
        AssertHighlighter("cpp",
"""
#pragma once
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">pragma</span> once</span>
""");
    }

    [Fact]
    public void Preprocessor_PragmaPack()
    {
        AssertHighlighter("cpp",
"""
#pragma pack(push, 1)
struct Header { uint8_t version; uint32_t size; };
#pragma pack(pop)
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">pragma</span> pack(push, 1)</span>
<span class="hljs-keyword">struct</span> <span class="hljs-title class_">Header</span> { <span class="hljs-type">uint8_t</span> version; <span class="hljs-type">uint32_t</span> size; };
<span class="hljs-meta">#<span class="hljs-keyword">pragma</span> pack(pop)</span>
""");
    }

    [Fact]
    public void Preprocessor_Error()
    {
        AssertHighlighter("cpp",
"""
#error "Unsupported configuration"
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">error</span> <span class="hljs-string">&quot;Unsupported configuration&quot;</span></span>
""");
    }

    [Fact]
    public void Preprocessor_Warning()
    {
        AssertHighlighter("cpp",
"""
#warning "This is deprecated"
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">warning</span> <span class="hljs-string">&quot;This is deprecated&quot;</span></span>
""");
    }

    [Fact]
    public void Preprocessor_Line()
    {
        AssertHighlighter("cpp",
"""
#line 42 "myfile.cpp"
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">line</span> 42 <span class="hljs-string">&quot;myfile.cpp&quot;</span></span>
""");
    }

    [Fact]
    public void Namespace_Simple()
    {
        AssertHighlighter("cpp",
"""
namespace MyApp {
    int x;
}
""",
"""
<span class="hljs-keyword">namespace</span> MyApp {
    <span class="hljs-type">int</span> x;
}
""");
    }

    [Fact]
    public void Namespace_Nested()
    {
        AssertHighlighter("cpp",
"""
namespace A {
    namespace B {
        int x;
    }
}
""",
"""
<span class="hljs-keyword">namespace</span> A {
    <span class="hljs-keyword">namespace</span> B {
        <span class="hljs-type">int</span> x;
    }
}
""");
    }

    [Fact]
    public void Namespace_NestedShort()
    {
        AssertHighlighter("cpp",
"""
namespace A::B::C {
    int x;
}
""",
"""
<span class="hljs-keyword">namespace</span> A::B::C {
    <span class="hljs-type">int</span> x;
}
""");
    }

    [Fact]
    public void Namespace_Anonymous()
    {
        AssertHighlighter("cpp",
"""
namespace {
    int internalCounter = 0;
}
""",
"""
<span class="hljs-keyword">namespace</span> {
    <span class="hljs-type">int</span> internalCounter = <span class="hljs-number">0</span>;
}
""");
    }

    [Fact]
    public void Namespace_Alias()
    {
        AssertHighlighter("cpp",
"""
namespace fs = std::filesystem;
""",
"""
<span class="hljs-keyword">namespace</span> fs = std::filesystem;
""");
    }

    [Fact]
    public void Namespace_ScopeResolution()
    {
        AssertHighlighter("cpp",
"""
std::vector<int> nums;
""",
"""
std::vector&lt;<span class="hljs-type">int</span>&gt; nums;
""");
    }

    [Fact]
    public void ClassStruct_StructSimple()
    {
        AssertHighlighter("cpp",
"""
struct Point {
    double x;
    double y;
};
""",
"""
<span class="hljs-keyword">struct</span> <span class="hljs-title class_">Point</span> {
    <span class="hljs-type">double</span> x;
    <span class="hljs-type">double</span> y;
};
""");
    }

    [Fact]
    public void ClassStruct_ClassSimple()
    {
        AssertHighlighter("cpp",
"""
class User {
public:
    std::string name;
    int age;
};
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">User</span> {
<span class="hljs-keyword">public</span>:
    std::string name;
    <span class="hljs-type">int</span> age;
};
""");
    }

    [Fact]
    public void ClassStruct_WithAccess()
    {
        AssertHighlighter("cpp",
"""
class User {
public:
    void Run();
protected:
    int _id;
private:
    std::string _secret;
};
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">User</span> {
<span class="hljs-keyword">public</span>:
    <span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">Run</span><span class="hljs-params">()</span></span>;
<span class="hljs-keyword">protected</span>:
    <span class="hljs-type">int</span> _id;
<span class="hljs-keyword">private</span>:
    std::string _secret;
};
""");
    }

    [Fact]
    public void ClassStruct_Inheritance()
    {
        AssertHighlighter("cpp",
"""
class Manager : public Employee { };
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Manager</span> : <span class="hljs-keyword">public</span> Employee { };
""");
    }

    [Fact]
    public void ClassStruct_MultiInherit()
    {
        AssertHighlighter("cpp",
"""
class C : public A, protected B { };
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">C</span> : <span class="hljs-keyword">public</span> A, <span class="hljs-keyword">protected</span> B { };
""");
    }

    [Fact]
    public void ClassStruct_VirtualInherit()
    {
        AssertHighlighter("cpp",
"""
class D : virtual public B { };
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">D</span> : <span class="hljs-keyword">virtual</span> <span class="hljs-keyword">public</span> B { };
""");
    }

    [Fact]
    public void ClassStruct_Constructor()
    {
        AssertHighlighter("cpp",
"""
class User {
public:
    User(std::string n, int a) : name(std::move(n)), age(a) { }
    std::string name;
    int age;
};
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">User</span> {
<span class="hljs-keyword">public</span>:
    <span class="hljs-built_in">User</span>(std::string n, <span class="hljs-type">int</span> a) : <span class="hljs-built_in">name</span>(std::<span class="hljs-built_in">move</span>(n)), <span class="hljs-built_in">age</span>(a) { }
    std::string name;
    <span class="hljs-type">int</span> age;
};
""");
    }

    [Fact]
    public void ClassStruct_DefaultedCtor()
    {
        AssertHighlighter("cpp",
"""
class T {
public:
    T() = default;
    T(const T&) = default;
    T(T&&) noexcept = default;
};
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">T</span> {
<span class="hljs-keyword">public</span>:
    <span class="hljs-built_in">T</span>() = <span class="hljs-keyword">default</span>;
    <span class="hljs-built_in">T</span>(<span class="hljs-type">const</span> T&amp;) = <span class="hljs-keyword">default</span>;
    <span class="hljs-built_in">T</span>(T&amp;&amp;) <span class="hljs-keyword">noexcept</span> = <span class="hljs-keyword">default</span>;
};
""");
    }

    [Fact]
    public void ClassStruct_DeletedCtor()
    {
        AssertHighlighter("cpp",
"""
class T {
public:
    T(const T&) = delete;
    T& operator=(const T&) = delete;
};
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">T</span> {
<span class="hljs-keyword">public</span>:
    <span class="hljs-built_in">T</span>(<span class="hljs-type">const</span> T&amp;) = <span class="hljs-keyword">delete</span>;
    T&amp; <span class="hljs-keyword">operator</span>=(<span class="hljs-type">const</span> T&amp;) = <span class="hljs-keyword">delete</span>;
};
""");
    }

    [Fact]
    public void ClassStruct_Destructor()
    {
        AssertHighlighter("cpp",
"""
class T {
public:
    ~T() { Cleanup(); }
};
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">T</span> {
<span class="hljs-keyword">public</span>:
    ~<span class="hljs-built_in">T</span>() { <span class="hljs-built_in">Cleanup</span>(); }
};
""");
    }

    [Fact]
    public void ClassStruct_VirtualDestructor()
    {
        AssertHighlighter("cpp",
"""
class B {
public:
    virtual ~B() = default;
};
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">B</span> {
<span class="hljs-keyword">public</span>:
    <span class="hljs-keyword">virtual</span> ~<span class="hljs-built_in">B</span>() = <span class="hljs-keyword">default</span>;
};
""");
    }

    [Fact]
    public void ClassStruct_PureVirtual()
    {
        AssertHighlighter("cpp",
"""
class IShape {
public:
    virtual double area() const = 0;
    virtual ~IShape() = default;
};
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">IShape</span> {
<span class="hljs-keyword">public</span>:
    <span class="hljs-function"><span class="hljs-keyword">virtual</span> <span class="hljs-type">double</span> <span class="hljs-title">area</span><span class="hljs-params">()</span> <span class="hljs-type">const</span> </span>= <span class="hljs-number">0</span>;
    <span class="hljs-keyword">virtual</span> ~<span class="hljs-built_in">IShape</span>() = <span class="hljs-keyword">default</span>;
};
""");
    }

    [Fact]
    public void ClassStruct_Override()
    {
        AssertHighlighter("cpp",
"""
class Circle : public IShape {
public:
    double area() const override { return 3.14 * r * r; }
private:
    double r;
};
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Circle</span> : <span class="hljs-keyword">public</span> IShape {
<span class="hljs-keyword">public</span>:
    <span class="hljs-function"><span class="hljs-type">double</span> <span class="hljs-title">area</span><span class="hljs-params">()</span> <span class="hljs-type">const</span> <span class="hljs-keyword">override</span> </span>{ <span class="hljs-keyword">return</span> <span class="hljs-number">3.14</span> * r * r; }
<span class="hljs-keyword">private</span>:
    <span class="hljs-type">double</span> r;
};
""");
    }

    [Fact]
    public void ClassStruct_FinalMethod()
    {
        AssertHighlighter("cpp",
"""
class C : public B {
public:
    void Run() final;
};
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">C</span> : <span class="hljs-keyword">public</span> B {
<span class="hljs-keyword">public</span>:
    <span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">Run</span><span class="hljs-params">()</span> <span class="hljs-keyword">final</span></span>;
};
""");
    }

    [Fact]
    public void ClassStruct_NestedType()
    {
        AssertHighlighter("cpp",
"""
class Outer {
public:
    class Inner { };
    using Iter = Inner;
};
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Outer</span> {
<span class="hljs-keyword">public</span>:
    <span class="hljs-keyword">class</span> <span class="hljs-title class_">Inner</span> { };
    <span class="hljs-keyword">using</span> Iter = Inner;
};
""");
    }

    [Fact]
    public void ClassStruct_StaticMember()
    {
        AssertHighlighter("cpp",
"""
class T {
public:
    static int instances;
    static int Count() { return instances; }
};
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">T</span> {
<span class="hljs-keyword">public</span>:
    <span class="hljs-type">static</span> <span class="hljs-type">int</span> instances;
    <span class="hljs-function"><span class="hljs-type">static</span> <span class="hljs-type">int</span> <span class="hljs-title">Count</span><span class="hljs-params">()</span> </span>{ <span class="hljs-keyword">return</span> instances; }
};
""");
    }

    [Fact]
    public void ClassStruct_OperatorOverload()
    {
        AssertHighlighter("cpp",
"""
class Money {
public:
    Money operator+(const Money& other) const { return { amount + other.amount, currency }; }
    bool operator==(const Money&) const = default;
private:
    double amount;
    std::string currency;
};
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Money</span> {
<span class="hljs-keyword">public</span>:
    Money <span class="hljs-keyword">operator</span>+(<span class="hljs-type">const</span> Money&amp; other) <span class="hljs-type">const</span> { <span class="hljs-keyword">return</span> { amount + other.amount, currency }; }
    <span class="hljs-type">bool</span> <span class="hljs-keyword">operator</span>==(<span class="hljs-type">const</span> Money&amp;) <span class="hljs-type">const</span> = <span class="hljs-keyword">default</span>;
<span class="hljs-keyword">private</span>:
    <span class="hljs-type">double</span> amount;
    std::string currency;
};
""");
    }

    [Fact]
    public void ClassStruct_Spaceship()
    {
        AssertHighlighter("cpp",
"""
struct Item {
    int id;
    auto operator<=>(const Item&) const = default;
};
""",
"""
<span class="hljs-keyword">struct</span> <span class="hljs-title class_">Item</span> {
    <span class="hljs-type">int</span> id;
    <span class="hljs-keyword">auto</span> <span class="hljs-built_in">operator</span>&lt;=&gt;(<span class="hljs-type">const</span> Item&amp;) <span class="hljs-type">const</span> = <span class="hljs-keyword">default</span>;
};
""");
    }

    [Fact]
    public void ClassStruct_EnumBasic()
    {
        AssertHighlighter("cpp",
"""
enum Color { Red, Green, Blue };
""",
"""
<span class="hljs-keyword">enum</span> <span class="hljs-title class_">Color</span> { Red, Green, Blue };
""");
    }

    [Fact]
    public void ClassStruct_EnumClass()
    {
        AssertHighlighter("cpp",
"""
enum class Status : uint8_t { Active = 1, Inactive = 2 };
""",
"""
<span class="hljs-keyword">enum class</span> <span class="hljs-title class_">Status</span> : <span class="hljs-type">uint8_t</span> { Active = <span class="hljs-number">1</span>, Inactive = <span class="hljs-number">2</span> };
""");
    }

    [Fact]
    public void ClassStruct_UnionAnonymous()
    {
        AssertHighlighter("cpp",
"""
struct V {
    union { int i; float f; };
};
""",
"""
<span class="hljs-keyword">struct</span> <span class="hljs-title class_">V</span> {
    <span class="hljs-keyword">union</span> { <span class="hljs-type">int</span> i; <span class="hljs-type">float</span> f; };
};
""");
    }

    [Fact]
    public void Function_Simple()
    {
        AssertHighlighter("cpp",
"""
int add(int a, int b) { return a + b; }
""",
"""
<span class="hljs-function"><span class="hljs-type">int</span> <span class="hljs-title">add</span><span class="hljs-params">(<span class="hljs-type">int</span> a, <span class="hljs-type">int</span> b)</span> </span>{ <span class="hljs-keyword">return</span> a + b; }
""");
    }

    [Fact]
    public void Function_TrailingReturn()
    {
        AssertHighlighter("cpp",
"""
auto add(int a, int b) -> int { return a + b; }
""",
"""
auto add(int a, int b) -&gt; int { return a + b; }
""");
    }

    [Fact]
    public void Function_Constexpr()
    {
        AssertHighlighter("cpp",
"""
constexpr int square(int x) { return x * x; }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">constexpr</span> <span class="hljs-type">int</span> <span class="hljs-title">square</span><span class="hljs-params">(<span class="hljs-type">int</span> x)</span> </span>{ <span class="hljs-keyword">return</span> x * x; }
""");
    }

    [Fact]
    public void Function_Reference()
    {
        AssertHighlighter("cpp",
"""
void take(const std::string& s) { }
""",
"""
<span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">take</span><span class="hljs-params">(<span class="hljs-type">const</span> std::string&amp; s)</span> </span>{ }
""");
    }

    [Fact]
    public void Function_RvalueRef()
    {
        AssertHighlighter("cpp",
"""
void consume(std::string&& s) { }
""",
"""
<span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">consume</span><span class="hljs-params">(std::string&amp;&amp; s)</span> </span>{ }
""");
    }

    [Fact]
    public void Function_DefaultArgs()
    {
        AssertHighlighter("cpp",
"""
void greet(std::string name, std::string greeting = "Hello") { }
""",
"""
<span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">greet</span><span class="hljs-params">(std::string name, std::string greeting = <span class="hljs-string">&quot;Hello&quot;</span>)</span> </span>{ }
""");
    }

    [Fact]
    public void Function_Variadic()
    {
        AssertHighlighter("cpp",
"""
int sum(int n, ...);
""",
"""
<span class="hljs-function"><span class="hljs-type">int</span> <span class="hljs-title">sum</span><span class="hljs-params">(<span class="hljs-type">int</span> n, ...)</span></span>;
""");
    }

    [Fact]
    public void Function_VariadicTemplate()
    {
        AssertHighlighter("cpp",
"""
template <typename... Args>
void print(Args&&... args) {
    ((std::cout << args << ' '), ...);
}
""",
"""
<span class="hljs-keyword">template</span> &lt;<span class="hljs-keyword">typename</span>... Args&gt;
<span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">print</span><span class="hljs-params">(Args&amp;&amp;... args)</span> </span>{
    ((std::cout &lt;&lt; args &lt;&lt; <span class="hljs-string">&#x27; &#x27;</span>), ...);
}
""");
    }

    [Fact]
    public void Function_FunctionPointer()
    {
        AssertHighlighter("cpp",
"""
int (*op)(int, int) = &add;
""",
"""
<span class="hljs-built_in">int</span> (*op)(<span class="hljs-type">int</span>, <span class="hljs-type">int</span>) = &amp;add;
""");
    }

    [Fact]
    public void Function_StdFunction()
    {
        AssertHighlighter("cpp",
"""
#include <functional>
std::function<int(int, int)> op = std::plus<int>{};
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&lt;functional&gt;</span></span>
std::function&lt;<span class="hljs-type">int</span>(<span class="hljs-type">int</span>, <span class="hljs-type">int</span>)&gt; op = std::plus&lt;<span class="hljs-type">int</span>&gt;{};
""");
    }

    [Fact]
    public void Template_FunctionGeneric()
    {
        AssertHighlighter("cpp",
"""
template <typename T>
T identity(T value) { return value; }
""",
"""
<span class="hljs-keyword">template</span> &lt;<span class="hljs-keyword">typename</span> T&gt;
<span class="hljs-function">T <span class="hljs-title">identity</span><span class="hljs-params">(T value)</span> </span>{ <span class="hljs-keyword">return</span> value; }
""");
    }

    [Fact]
    public void Template_ClassGeneric()
    {
        AssertHighlighter("cpp",
"""
template <typename T>
class Box {
public:
    T value;
};
""",
"""
<span class="hljs-keyword">template</span> &lt;<span class="hljs-keyword">typename</span> T&gt;
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Box</span> {
<span class="hljs-keyword">public</span>:
    T value;
};
""");
    }

    [Fact]
    public void Template_TwoParam()
    {
        AssertHighlighter("cpp",
"""
template <typename TIn, typename TOut>
TOut convert(const TIn& src);
""",
"""
<span class="hljs-keyword">template</span> &lt;<span class="hljs-keyword">typename</span> TIn, <span class="hljs-keyword">typename</span> TOut&gt;
<span class="hljs-function">TOut <span class="hljs-title">convert</span><span class="hljs-params">(<span class="hljs-type">const</span> TIn&amp; src)</span></span>;
""");
    }

    [Fact]
    public void Template_NonTypeParam()
    {
        AssertHighlighter("cpp",
"""
template <int N>
struct Array {
    int data[N];
};
""",
"""
<span class="hljs-keyword">template</span> &lt;<span class="hljs-type">int</span> N&gt;
<span class="hljs-keyword">struct</span> <span class="hljs-title class_">Array</span> {
    <span class="hljs-type">int</span> data[N];
};
""");
    }

    [Fact]
    public void Template_DefaultParam()
    {
        AssertHighlighter("cpp",
"""
template <typename T = int>
class List { };
""",
"""
<span class="hljs-keyword">template</span> &lt;<span class="hljs-keyword">typename</span> T = <span class="hljs-type">int</span>&gt;
<span class="hljs-keyword">class</span> List { };
""");
    }

    [Fact]
    public void Template_Specialization()
    {
        AssertHighlighter("cpp",
"""
template <>
class Box<bool> {
    bool value;
};
""",
"""
<span class="hljs-keyword">template</span> &lt;&gt;
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Box</span>&lt;<span class="hljs-type">bool</span>&gt; {
    <span class="hljs-type">bool</span> value;
};
""");
    }

    [Fact]
    public void Template_PartialSpec()
    {
        AssertHighlighter("cpp",
"""
template <typename T>
class Box<T*> {
    T* value;
};
""",
"""
<span class="hljs-keyword">template</span> &lt;<span class="hljs-keyword">typename</span> T&gt;
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Box</span>&lt;T*&gt; {
    T* value;
};
""");
    }

    [Fact]
    public void Template_TemplateTemplate()
    {
        AssertHighlighter("cpp",
"""
template <template <typename> class Container, typename T>
void fill(Container<T>& c, T value);
""",
"""
<span class="hljs-keyword">template</span> &lt;<span class="hljs-keyword">template</span> &lt;<span class="hljs-keyword">typename</span>&gt; <span class="hljs-keyword">class</span> <span class="hljs-title class_">Container</span>, <span class="hljs-keyword">typename</span> T&gt;
<span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">fill</span><span class="hljs-params">(Container&lt;T&gt;&amp; c, T value)</span></span>;
""");
    }

    [Fact]
    public void Template_TypenameKeyword()
    {
        AssertHighlighter("cpp",
"""
template <typename T>
typename T::iterator first(T& container) { return container.begin(); }
""",
"""
<span class="hljs-keyword">template</span> &lt;<span class="hljs-keyword">typename</span> T&gt;
<span class="hljs-function"><span class="hljs-keyword">typename</span> T::iterator <span class="hljs-title">first</span><span class="hljs-params">(T&amp; container)</span> </span>{ <span class="hljs-keyword">return</span> container.<span class="hljs-built_in">begin</span>(); }
""");
    }

    [Fact]
    public void Template_IfConstexpr()
    {
        AssertHighlighter("cpp",
"""
template <typename T>
void log(T value) {
    if constexpr (std::is_integral_v<T>) {
        std::cout << "int: " << value;
    } else {
        std::cout << value;
    }
}
""",
"""
<span class="hljs-keyword">template</span> &lt;<span class="hljs-keyword">typename</span> T&gt;
<span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">log</span><span class="hljs-params">(T value)</span> </span>{
    <span class="hljs-function"><span class="hljs-keyword">if</span> <span class="hljs-title">constexpr</span> <span class="hljs-params">(std::is_integral_v&lt;T&gt;)</span> </span>{
        std::cout &lt;&lt; <span class="hljs-string">&quot;int: &quot;</span> &lt;&lt; value;
    } <span class="hljs-keyword">else</span> {
        std::cout &lt;&lt; value;
    }
}
""");
    }

    [Fact]
    public void Template_FoldExpression()
    {
        AssertHighlighter("cpp",
"""
template <typename... Args>
auto sum(Args... args) { return (args + ...); }
""",
"""
<span class="hljs-keyword">template</span> &lt;<span class="hljs-keyword">typename</span>... Args&gt;
<span class="hljs-function"><span class="hljs-keyword">auto</span> <span class="hljs-title">sum</span><span class="hljs-params">(Args... args)</span> </span>{ <span class="hljs-keyword">return</span> (args + ...); }
""");
    }

    [Fact]
    public void Template_FoldBinary()
    {
        AssertHighlighter("cpp",
"""
template <typename... Args>
void log_all(Args&&... args) { (std::cout << ... << args); }
""",
"""
<span class="hljs-keyword">template</span> &lt;<span class="hljs-keyword">typename</span>... Args&gt;
<span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">log_all</span><span class="hljs-params">(Args&amp;&amp;... args)</span> </span>{ (std::cout &lt;&lt; ... &lt;&lt; args); }
""");
    }

    [Fact]
    public void Concept_Definition()
    {
        AssertHighlighter("cpp",
"""
template <typename T>
concept Numeric = std::is_arithmetic_v<T>;
""",
"""
<span class="hljs-keyword">template</span> &lt;<span class="hljs-keyword">typename</span> T&gt;
<span class="hljs-keyword">concept</span> Numeric = std::is_arithmetic_v&lt;T&gt;;
""");
    }

    [Fact]
    public void Concept_RequiresExpression()
    {
        AssertHighlighter("cpp",
"""
template <typename T>
concept Sortable = requires(T a, T b) {
    { a < b } -> std::convertible_to<bool>;
    a.size();
};
""",
"""
<span class="hljs-keyword">template</span> &lt;<span class="hljs-keyword">typename</span> T&gt;
<span class="hljs-keyword">concept</span> Sortable = <span class="hljs-built_in">requires</span>(T a, T b) {
    { a &lt; b } -&gt; std::convertible_to&lt;<span class="hljs-type">bool</span>&gt;;
    a.<span class="hljs-built_in">size</span>();
};
""");
    }

    [Fact]
    public void Concept_ConstrainedTemplate()
    {
        AssertHighlighter("cpp",
"""
template <Numeric T>
T square(T x) { return x * x; }
""",
"""
<span class="hljs-keyword">template</span> &lt;Numeric T&gt;
<span class="hljs-function">T <span class="hljs-title">square</span><span class="hljs-params">(T x)</span> </span>{ <span class="hljs-keyword">return</span> x * x; }
""");
    }

    [Fact]
    public void Concept_RequiresClause()
    {
        AssertHighlighter("cpp",
"""
template <typename T>
requires std::integral<T>
T add_one(T x) { return x + 1; }
""",
"""
<span class="hljs-keyword">template</span> &lt;<span class="hljs-keyword">typename</span> T&gt;
<span class="hljs-function"><span class="hljs-keyword">requires</span> std::integral&lt;T&gt;
T <span class="hljs-title">add_one</span><span class="hljs-params">(T x)</span> </span>{ <span class="hljs-keyword">return</span> x + <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void Concept_AbbreviatedAuto()
    {
        AssertHighlighter("cpp",
"""
auto add_one(std::integral auto x) { return x + 1; }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">auto</span> <span class="hljs-title">add_one</span><span class="hljs-params">(std::integral <span class="hljs-keyword">auto</span> x)</span> </span>{ <span class="hljs-keyword">return</span> x + <span class="hljs-number">1</span>; }
""");
    }

    [Fact]
    public void Lambda_Simple()
    {
        AssertHighlighter("cpp",
"""
auto sq = [](int x) { return x * x; };
""",
"""
<span class="hljs-keyword">auto</span> sq = [](<span class="hljs-type">int</span> x) { <span class="hljs-keyword">return</span> x * x; };
""");
    }

    [Fact]
    public void Lambda_CaptureNothing()
    {
        AssertHighlighter("cpp",
"""
auto run = []() { std::cout << "hi"; };
""",
"""
<span class="hljs-keyword">auto</span> run = []() { std::cout &lt;&lt; <span class="hljs-string">&quot;hi&quot;</span>; };
""");
    }

    [Fact]
    public void Lambda_CaptureValue()
    {
        AssertHighlighter("cpp",
"""
int n = 0;
auto inc = [n]() { return n + 1; };
""",
"""
<span class="hljs-type">int</span> n = <span class="hljs-number">0</span>;
<span class="hljs-keyword">auto</span> inc = [n]() { <span class="hljs-keyword">return</span> n + <span class="hljs-number">1</span>; };
""");
    }

    [Fact]
    public void Lambda_CaptureReference()
    {
        AssertHighlighter("cpp",
"""
int n = 0;
auto inc = [&n]() { n++; };
""",
"""
<span class="hljs-type">int</span> n = <span class="hljs-number">0</span>;
<span class="hljs-keyword">auto</span> inc = [&amp;n]() { n++; };
""");
    }

    [Fact]
    public void Lambda_CaptureAllValue()
    {
        AssertHighlighter("cpp",
"""
auto f = [=](int x) { return x + offset; };
""",
"""
<span class="hljs-keyword">auto</span> f = [=](<span class="hljs-type">int</span> x) { <span class="hljs-keyword">return</span> x + offset; };
""");
    }

    [Fact]
    public void Lambda_CaptureAllRef()
    {
        AssertHighlighter("cpp",
"""
auto f = [&](int x) { count++; return x; };
""",
"""
<span class="hljs-keyword">auto</span> f = [&amp;](<span class="hljs-type">int</span> x) { count++; <span class="hljs-keyword">return</span> x; };
""");
    }

    [Fact]
    public void Lambda_Init()
    {
        AssertHighlighter("cpp",
"""
auto f = [n = std::move(value)]() mutable { return ++n; };
""",
"""
<span class="hljs-keyword">auto</span> f = [n = std::<span class="hljs-built_in">move</span>(value)]() <span class="hljs-keyword">mutable</span> { <span class="hljs-keyword">return</span> ++n; };
""");
    }

    [Fact]
    public void Lambda_Mutable()
    {
        AssertHighlighter("cpp",
"""
auto f = [n = 0]() mutable { return ++n; };
""",
"""
<span class="hljs-keyword">auto</span> f = [n = <span class="hljs-number">0</span>]() <span class="hljs-keyword">mutable</span> { <span class="hljs-keyword">return</span> ++n; };
""");
    }

    [Fact]
    public void Lambda_TrailingReturn()
    {
        AssertHighlighter("cpp",
"""
auto f = [](int x) -> double { return x / 2.0; };
""",
"""
<span class="hljs-keyword">auto</span> f = [](<span class="hljs-type">int</span> x) -&gt; <span class="hljs-type">double</span> { <span class="hljs-keyword">return</span> x / <span class="hljs-number">2.0</span>; };
""");
    }

    [Fact]
    public void Lambda_GenericAuto()
    {
        AssertHighlighter("cpp",
"""
auto f = [](auto a, auto b) { return a + b; };
""",
"""
<span class="hljs-keyword">auto</span> f = [](<span class="hljs-keyword">auto</span> a, <span class="hljs-keyword">auto</span> b) { <span class="hljs-keyword">return</span> a + b; };
""");
    }

    [Fact]
    public void Lambda_TemplateLambda()
    {
        AssertHighlighter("cpp",
"""
auto f = []<typename T>(T x) { return x * 2; };
""",
"""
<span class="hljs-keyword">auto</span> f = []&lt;<span class="hljs-keyword">typename</span> T&gt;(T x) { <span class="hljs-keyword">return</span> x * <span class="hljs-number">2</span>; };
""");
    }

    [Fact]
    public void Lambda_StatelessConversion()
    {
        AssertHighlighter("cpp",
"""
int (*fp)(int) = [](int x) { return x + 1; };
""",
"""
<span class="hljs-built_in">int</span> (*fp)(<span class="hljs-type">int</span>) = [](<span class="hljs-type">int</span> x) { <span class="hljs-keyword">return</span> x + <span class="hljs-number">1</span>; };
""");
    }

    [Fact]
    public void ControlFlow_IfElse()
    {
        AssertHighlighter("cpp",
"""
if (x > 0) {
    positive();
} else if (x < 0) {
    negative();
} else {
    zero();
}
""",
"""
<span class="hljs-keyword">if</span> (x &gt; <span class="hljs-number">0</span>) {
    <span class="hljs-built_in">positive</span>();
} <span class="hljs-keyword">else</span> <span class="hljs-keyword">if</span> (x &lt; <span class="hljs-number">0</span>) {
    <span class="hljs-built_in">negative</span>();
} <span class="hljs-keyword">else</span> {
    <span class="hljs-built_in">zero</span>();
}
""");
    }

    [Fact]
    public void ControlFlow_IfInit()
    {
        AssertHighlighter("cpp",
"""
if (auto it = map.find(key); it != map.end()) {
    use(*it);
}
""",
"""
<span class="hljs-keyword">if</span> (<span class="hljs-keyword">auto</span> it = map.<span class="hljs-built_in">find</span>(key); it != map.<span class="hljs-built_in">end</span>()) {
    <span class="hljs-built_in">use</span>(*it);
}
""");
    }

    [Fact]
    public void ControlFlow_For()
    {
        AssertHighlighter("cpp",
"""
for (int i = 0; i < 10; ++i) {
    std::cout << i;
}
""",
"""
<span class="hljs-keyword">for</span> (<span class="hljs-type">int</span> i = <span class="hljs-number">0</span>; i &lt; <span class="hljs-number">10</span>; ++i) {
    std::cout &lt;&lt; i;
}
""");
    }

    [Fact]
    public void ControlFlow_RangeFor()
    {
        AssertHighlighter("cpp",
"""
for (const auto& item : items) {
    process(item);
}
""",
"""
<span class="hljs-keyword">for</span> (<span class="hljs-type">const</span> <span class="hljs-keyword">auto</span>&amp; item : items) {
    <span class="hljs-built_in">process</span>(item);
}
""");
    }

    [Fact]
    public void ControlFlow_RangeForStructured()
    {
        AssertHighlighter("cpp",
"""
for (const auto& [key, value] : map) {
    process(key, value);
}
""",
"""
<span class="hljs-keyword">for</span> (<span class="hljs-type">const</span> <span class="hljs-keyword">auto</span>&amp; [key, value] : map) {
    <span class="hljs-built_in">process</span>(key, value);
}
""");
    }

    [Fact]
    public void ControlFlow_While()
    {
        AssertHighlighter("cpp",
"""
while (queue.size() > 0) {
    auto item = queue.front();
    queue.pop();
}
""",
"""
<span class="hljs-keyword">while</span> (queue.<span class="hljs-built_in">size</span>() &gt; <span class="hljs-number">0</span>) {
    <span class="hljs-keyword">auto</span> item = queue.<span class="hljs-built_in">front</span>();
    queue.<span class="hljs-built_in">pop</span>();
}
""");
    }

    [Fact]
    public void ControlFlow_DoWhile()
    {
        AssertHighlighter("cpp",
"""
do {
    refill();
} while (!buffer.full());
""",
"""
<span class="hljs-keyword">do</span> {
    <span class="hljs-built_in">refill</span>();
} <span class="hljs-keyword">while</span> (!buffer.<span class="hljs-built_in">full</span>());
""");
    }

    [Fact]
    public void ControlFlow_Switch()
    {
        AssertHighlighter("cpp",
"""
switch (status) {
    case 1: handleOne(); break;
    case 2: [[fallthrough]];
    case 3: handleTwoOrThree(); break;
    default: handleOther(); break;
}
""",
"""
<span class="hljs-keyword">switch</span> (status) {
    <span class="hljs-keyword">case</span> <span class="hljs-number">1</span>: <span class="hljs-built_in">handleOne</span>(); <span class="hljs-keyword">break</span>;
    <span class="hljs-keyword">case</span> <span class="hljs-number">2</span>: [[fallthrough]];
    <span class="hljs-keyword">case</span> <span class="hljs-number">3</span>: <span class="hljs-built_in">handleTwoOrThree</span>(); <span class="hljs-keyword">break</span>;
    <span class="hljs-keyword">default</span>: <span class="hljs-built_in">handleOther</span>(); <span class="hljs-keyword">break</span>;
}
""");
    }

    [Fact]
    public void ControlFlow_SwitchInit()
    {
        AssertHighlighter("cpp",
"""
switch (auto result = compute(); result) {
    case 0: ok(); break;
    default: fail(result); break;
}
""",
"""
<span class="hljs-keyword">switch</span> (<span class="hljs-keyword">auto</span> result = <span class="hljs-built_in">compute</span>(); result) {
    <span class="hljs-keyword">case</span> <span class="hljs-number">0</span>: <span class="hljs-built_in">ok</span>(); <span class="hljs-keyword">break</span>;
    <span class="hljs-keyword">default</span>: <span class="hljs-built_in">fail</span>(result); <span class="hljs-keyword">break</span>;
}
""");
    }

    [Fact]
    public void ControlFlow_Break()
    {
        AssertHighlighter("cpp",
"""
for (auto& item : items) {
    if (item.IsBad()) break;
}
""",
"""
<span class="hljs-keyword">for</span> (<span class="hljs-keyword">auto</span>&amp; item : items) {
    <span class="hljs-keyword">if</span> (item.<span class="hljs-built_in">IsBad</span>()) <span class="hljs-keyword">break</span>;
}
""");
    }

    [Fact]
    public void ControlFlow_Continue()
    {
        AssertHighlighter("cpp",
"""
for (auto& item : items) {
    if (!item.IsValid()) continue;
    process(item);
}
""",
"""
<span class="hljs-keyword">for</span> (<span class="hljs-keyword">auto</span>&amp; item : items) {
    <span class="hljs-keyword">if</span> (!item.<span class="hljs-built_in">IsValid</span>()) <span class="hljs-keyword">continue</span>;
    <span class="hljs-built_in">process</span>(item);
}
""");
    }

    [Fact]
    public void ControlFlow_Goto()
    {
        AssertHighlighter("cpp",
"""
start:
    if (count++ < 10) goto start;
""",
"""
start:
    <span class="hljs-keyword">if</span> (count++ &lt; <span class="hljs-number">10</span>) <span class="hljs-keyword">goto</span> start;
""");
    }

    [Fact]
    public void ControlFlow_Return()
    {
        AssertHighlighter("cpp",
"""
int sum(int a, int b) { return a + b; }
""",
"""
<span class="hljs-function"><span class="hljs-type">int</span> <span class="hljs-title">sum</span><span class="hljs-params">(<span class="hljs-type">int</span> a, <span class="hljs-type">int</span> b)</span> </span>{ <span class="hljs-keyword">return</span> a + b; }
""");
    }

    [Fact]
    public void ExceptionHandling_TryCatch()
    {
        AssertHighlighter("cpp",
"""
try {
    risky();
} catch (const std::exception& ex) {
    std::cerr << ex.what();
}
""",
"""
<span class="hljs-keyword">try</span> {
    <span class="hljs-built_in">risky</span>();
} <span class="hljs-built_in">catch</span> (<span class="hljs-type">const</span> std::exception&amp; ex) {
    std::cerr &lt;&lt; ex.<span class="hljs-built_in">what</span>();
}
""");
    }

    [Fact]
    public void ExceptionHandling_TryCatchAll()
    {
        AssertHighlighter("cpp",
"""
try {
    risky();
} catch (...) {
    std::cerr << "unknown error";
}
""",
"""
<span class="hljs-keyword">try</span> {
    <span class="hljs-built_in">risky</span>();
} <span class="hljs-built_in">catch</span> (...) {
    std::cerr &lt;&lt; <span class="hljs-string">&quot;unknown error&quot;</span>;
}
""");
    }

    [Fact]
    public void ExceptionHandling_MultipleCatch()
    {
        AssertHighlighter("cpp",
"""
try {
    risky();
} catch (const std::invalid_argument& ex) {
    std::cerr << "bad arg";
} catch (const std::exception& ex) {
    std::cerr << ex.what();
} catch (...) {
    std::cerr << "unknown";
}
""",
"""
<span class="hljs-keyword">try</span> {
    <span class="hljs-built_in">risky</span>();
} <span class="hljs-built_in">catch</span> (<span class="hljs-type">const</span> std::invalid_argument&amp; ex) {
    std::cerr &lt;&lt; <span class="hljs-string">&quot;bad arg&quot;</span>;
} <span class="hljs-built_in">catch</span> (<span class="hljs-type">const</span> std::exception&amp; ex) {
    std::cerr &lt;&lt; ex.<span class="hljs-built_in">what</span>();
} <span class="hljs-built_in">catch</span> (...) {
    std::cerr &lt;&lt; <span class="hljs-string">&quot;unknown&quot;</span>;
}
""");
    }

    [Fact]
    public void ExceptionHandling_Throw()
    {
        AssertHighlighter("cpp",
"""
throw std::runtime_error("something went wrong");
""",
"""
<span class="hljs-keyword">throw</span> std::<span class="hljs-built_in">runtime_error</span>(<span class="hljs-string">&quot;something went wrong&quot;</span>);
""");
    }

    [Fact]
    public void ExceptionHandling_Rethrow()
    {
        AssertHighlighter("cpp",
"""
try { risky(); } catch (...) { /* log */ throw; }
""",
"""
<span class="hljs-keyword">try</span> { <span class="hljs-built_in">risky</span>(); } <span class="hljs-built_in">catch</span> (...) { <span class="hljs-comment">/* log */</span> <span class="hljs-keyword">throw</span>; }
""");
    }

    [Fact]
    public void Pointer_Pointer()
    {
        AssertHighlighter("cpp",
"""
int* p = &x;
""",
"""
<span class="hljs-type">int</span>* p = &amp;x;
""");
    }

    [Fact]
    public void Pointer_PointerToConst()
    {
        AssertHighlighter("cpp",
"""
const int* p = &x;
""",
"""
<span class="hljs-type">const</span> <span class="hljs-type">int</span>* p = &amp;x;
""");
    }

    [Fact]
    public void Pointer_ConstPointer()
    {
        AssertHighlighter("cpp",
"""
int* const p = &x;
""",
"""
<span class="hljs-type">int</span>* <span class="hljs-type">const</span> p = &amp;x;
""");
    }

    [Fact]
    public void Pointer_PointerArith()
    {
        AssertHighlighter("cpp",
"""
int* q = p + 4;
""",
"""
<span class="hljs-type">int</span>* q = p + <span class="hljs-number">4</span>;
""");
    }

    [Fact]
    public void Pointer_Dereference()
    {
        AssertHighlighter("cpp",
"""
int v = *p;
""",
"""
<span class="hljs-type">int</span> v = *p;
""");
    }

    [Fact]
    public void Pointer_Reference()
    {
        AssertHighlighter("cpp",
"""
int& r = x;
""",
"""
<span class="hljs-type">int</span>&amp; r = x;
""");
    }

    [Fact]
    public void Pointer_RvalueReference()
    {
        AssertHighlighter("cpp",
"""
std::string&& s = std::move(other);
""",
"""
std::string&amp;&amp; s = std::<span class="hljs-built_in">move</span>(other);
""");
    }

    [Fact]
    public void Pointer_NullPtr()
    {
        AssertHighlighter("cpp",
"""
int* p = nullptr;
""",
"""
<span class="hljs-type">int</span>* p = <span class="hljs-literal">nullptr</span>;
""");
    }

    [Fact]
    public void Pointer_NewExpression()
    {
        AssertHighlighter("cpp",
"""
auto* p = new User("alice");
""",
"""
<span class="hljs-keyword">auto</span>* p = <span class="hljs-keyword">new</span> <span class="hljs-built_in">User</span>(<span class="hljs-string">&quot;alice&quot;</span>);
""");
    }

    [Fact]
    public void Pointer_NewArray()
    {
        AssertHighlighter("cpp",
"""
auto* arr = new int[16]{0};
""",
"""
<span class="hljs-keyword">auto</span>* arr = <span class="hljs-keyword">new</span> <span class="hljs-type">int</span>[<span class="hljs-number">16</span>]{<span class="hljs-number">0</span>};
""");
    }

    [Fact]
    public void Pointer_DeleteExpression()
    {
        AssertHighlighter("cpp",
"""
delete p;
""",
"""
<span class="hljs-keyword">delete</span> p;
""");
    }

    [Fact]
    public void Pointer_DeleteArray()
    {
        AssertHighlighter("cpp",
"""
delete[] arr;
""",
"""
<span class="hljs-keyword">delete</span>[] arr;
""");
    }

    [Fact]
    public void Pointer_PlacementNew()
    {
        AssertHighlighter("cpp",
"""
auto* obj = new (buffer) User("alice");
""",
"""
<span class="hljs-keyword">auto</span>* obj = <span class="hljs-built_in">new</span> (buffer) <span class="hljs-built_in">User</span>(<span class="hljs-string">&quot;alice&quot;</span>);
""");
    }

    [Fact]
    public void Pointer_UniquePtr()
    {
        AssertHighlighter("cpp",
"""
#include <memory>
auto p = std::make_unique<User>("alice");
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&lt;memory&gt;</span></span>
<span class="hljs-keyword">auto</span> p = std::<span class="hljs-built_in">make_unique</span>&lt;User&gt;(<span class="hljs-string">&quot;alice&quot;</span>);
""");
    }

    [Fact]
    public void Pointer_SharedPtr()
    {
        AssertHighlighter("cpp",
"""
auto p = std::make_shared<User>("alice");
""",
"""
<span class="hljs-keyword">auto</span> p = std::<span class="hljs-built_in">make_shared</span>&lt;User&gt;(<span class="hljs-string">&quot;alice&quot;</span>);
""");
    }

    [Fact]
    public void Pointer_WeakPtr()
    {
        AssertHighlighter("cpp",
"""
std::weak_ptr<User> w = sharedUser;
""",
"""
std::weak_ptr&lt;User&gt; w = sharedUser;
""");
    }

    [Fact]
    public void Cast_CStyle()
    {
        AssertHighlighter("cpp",
"""
int n = (int)x;
""",
"""
<span class="hljs-type">int</span> n = (<span class="hljs-type">int</span>)x;
""");
    }

    [Fact]
    public void Cast_StaticCast()
    {
        AssertHighlighter("cpp",
"""
auto n = static_cast<int>(x);
""",
"""
<span class="hljs-keyword">auto</span> n = <span class="hljs-built_in">static_cast</span>&lt;<span class="hljs-type">int</span>&gt;(x);
""");
    }

    [Fact]
    public void Cast_DynamicCast()
    {
        AssertHighlighter("cpp",
"""
auto* d = dynamic_cast<Derived*>(base);
""",
"""
<span class="hljs-keyword">auto</span>* d = <span class="hljs-built_in">dynamic_cast</span>&lt;Derived*&gt;(base);
""");
    }

    [Fact]
    public void Cast_ConstCast()
    {
        AssertHighlighter("cpp",
"""
auto* m = const_cast<int*>(p);
""",
"""
<span class="hljs-keyword">auto</span>* m = <span class="hljs-built_in">const_cast</span>&lt;<span class="hljs-type">int</span>*&gt;(p);
""");
    }

    [Fact]
    public void Cast_ReinterpretCast()
    {
        AssertHighlighter("cpp",
"""
auto* bytes = reinterpret_cast<uint8_t*>(p);
""",
"""
<span class="hljs-keyword">auto</span>* bytes = <span class="hljs-built_in">reinterpret_cast</span>&lt;<span class="hljs-type">uint8_t</span>*&gt;(p);
""");
    }

    [Fact]
    public void Cast_BitCast()
    {
        AssertHighlighter("cpp",
"""
#include <bit>
auto bits = std::bit_cast<uint32_t>(f);
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&lt;bit&gt;</span></span>
<span class="hljs-keyword">auto</span> bits = std::<span class="hljs-built_in">bit_cast</span>&lt;<span class="hljs-type">uint32_t</span>&gt;(f);
""");
    }

    [Fact]
    public void Cast_NarrowingExplicit()
    {
        AssertHighlighter("cpp",
"""
auto n = int{static_cast<int>(d)};
""",
"""
<span class="hljs-keyword">auto</span> n = <span class="hljs-type">int</span>{<span class="hljs-built_in">static_cast</span>&lt;<span class="hljs-type">int</span>&gt;(d)};
""");
    }

    [Fact]
    public void Stl_Vector()
    {
        AssertHighlighter("cpp",
"""
std::vector<int> nums{1, 2, 3, 4};
""",
"""
std::vector&lt;<span class="hljs-type">int</span>&gt; nums{<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>, <span class="hljs-number">4</span>};
""");
    }

    [Fact]
    public void Stl_Array()
    {
        AssertHighlighter("cpp",
"""
std::array<int, 4> nums{1, 2, 3, 4};
""",
"""
std::array&lt;<span class="hljs-type">int</span>, 4&gt; nums{<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>, <span class="hljs-number">4</span>};
""");
    }

    [Fact]
    public void Stl_Map()
    {
        AssertHighlighter("cpp",
"""
std::map<std::string, int> ages{{"alice", 30}, {"bob", 25}};
""",
"""
std::map&lt;std::string, <span class="hljs-type">int</span>&gt; ages{{<span class="hljs-string">&quot;alice&quot;</span>, <span class="hljs-number">30</span>}, {<span class="hljs-string">&quot;bob&quot;</span>, <span class="hljs-number">25</span>}};
""");
    }

    [Fact]
    public void Stl_UnorderedMap()
    {
        AssertHighlighter("cpp",
"""
std::unordered_map<int, std::string> users;
""",
"""
std::unordered_map&lt;<span class="hljs-type">int</span>, std::string&gt; users;
""");
    }

    [Fact]
    public void Stl_Set()
    {
        AssertHighlighter("cpp",
"""
std::set<int> seen;
""",
"""
std::set&lt;<span class="hljs-type">int</span>&gt; seen;
""");
    }

    [Fact]
    public void Stl_Tuple()
    {
        AssertHighlighter("cpp",
"""
std::tuple<int, std::string, double> t{1, "alice", 3.14};
""",
"""
std::tuple&lt;<span class="hljs-type">int</span>, std::string, <span class="hljs-type">double</span>&gt; t{<span class="hljs-number">1</span>, <span class="hljs-string">&quot;alice&quot;</span>, <span class="hljs-number">3.14</span>};
""");
    }

    [Fact]
    public void Stl_Pair()
    {
        AssertHighlighter("cpp",
"""
std::pair<int, std::string> p{1, "alice"};
""",
"""
std::pair&lt;<span class="hljs-type">int</span>, std::string&gt; p{<span class="hljs-number">1</span>, <span class="hljs-string">&quot;alice&quot;</span>};
""");
    }

    [Fact]
    public void Stl_Optional()
    {
        AssertHighlighter("cpp",
"""
std::optional<int> maybe = 42;
""",
"""
std::optional&lt;<span class="hljs-type">int</span>&gt; maybe = <span class="hljs-number">42</span>;
""");
    }

    [Fact]
    public void Stl_Variant()
    {
        AssertHighlighter("cpp",
"""
std::variant<int, std::string, double> v = "alice";
""",
"""
std::variant&lt;<span class="hljs-type">int</span>, std::string, <span class="hljs-type">double</span>&gt; v = <span class="hljs-string">&quot;alice&quot;</span>;
""");
    }

    [Fact]
    public void Stl_StringView()
    {
        AssertHighlighter("cpp",
"""
void log(std::string_view s);
""",
"""
<span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">log</span><span class="hljs-params">(std::string_view s)</span></span>;
""");
    }

    [Fact]
    public void Stl_Span()
    {
        AssertHighlighter("cpp",
"""
void fill(std::span<int> buffer, int value);
""",
"""
<span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">fill</span><span class="hljs-params">(std::span&lt;<span class="hljs-type">int</span>&gt; buffer, <span class="hljs-type">int</span> value)</span></span>;
""");
    }

    [Fact]
    public void Stl_Algorithm()
    {
        AssertHighlighter("cpp",
"""
std::sort(items.begin(), items.end(), [](auto& a, auto& b) { return a.priority < b.priority; });
""",
"""
std::<span class="hljs-built_in">sort</span>(items.<span class="hljs-built_in">begin</span>(), items.<span class="hljs-built_in">end</span>(), [](<span class="hljs-keyword">auto</span>&amp; a, <span class="hljs-keyword">auto</span>&amp; b) { <span class="hljs-keyword">return</span> a.priority &lt; b.priority; });
""");
    }

    [Fact]
    public void Stl_Ranges()
    {
        AssertHighlighter("cpp",
"""
#include <ranges>
auto evens = nums | std::views::filter([](int n) { return n % 2 == 0; });
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&lt;ranges&gt;</span></span>
<span class="hljs-keyword">auto</span> evens = nums | std::views::<span class="hljs-built_in">filter</span>([](<span class="hljs-type">int</span> n) { <span class="hljs-keyword">return</span> n % <span class="hljs-number">2</span> == <span class="hljs-number">0</span>; });
""");
    }

    [Fact]
    public void Stl_RangesTransform()
    {
        AssertHighlighter("cpp",
"""
auto squares = nums | std::views::transform([](int n) { return n * n; });
""",
"""
<span class="hljs-keyword">auto</span> squares = nums | std::views::<span class="hljs-built_in">transform</span>([](<span class="hljs-type">int</span> n) { <span class="hljs-keyword">return</span> n * n; });
""");
    }

    [Fact]
    public void Stl_Format()
    {
        AssertHighlighter("cpp",
"""
#include <format>
auto s = std::format("Hello, {}!", name);
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&lt;format&gt;</span></span>
<span class="hljs-keyword">auto</span> s = std::format(<span class="hljs-string">&quot;Hello, {}!&quot;</span>, name);
""");
    }

    [Fact]
    public void Initializer_Brace()
    {
        AssertHighlighter("cpp",
"""
int x{42};
""",
"""
<span class="hljs-type">int</span> x{<span class="hljs-number">42</span>};
""");
    }

    [Fact]
    public void Initializer_Aggregate()
    {
        AssertHighlighter("cpp",
"""
Point p{1.0, 2.0};
""",
"""
Point p{<span class="hljs-number">1.0</span>, <span class="hljs-number">2.0</span>};
""");
    }

    [Fact]
    public void Initializer_Designated()
    {
        AssertHighlighter("cpp",
"""
Point p{.x = 1.0, .y = 2.0};
""",
"""
Point p{.x = <span class="hljs-number">1.0</span>, .y = <span class="hljs-number">2.0</span>};
""");
    }

    [Fact]
    public void Initializer_NestedDesignated()
    {
        AssertHighlighter("cpp",
"""
Config c{.server = {.host = "localhost", .port = 8080}};
""",
"""
Config c{.server = {.host = <span class="hljs-string">&quot;localhost&quot;</span>, .port = <span class="hljs-number">8080</span>}};
""");
    }

    [Fact]
    public void Initializer_InitializerList()
    {
        AssertHighlighter("cpp",
"""
std::vector<int> nums{1, 2, 3};
""",
"""
std::vector&lt;<span class="hljs-type">int</span>&gt; nums{<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>};
""");
    }

    [Fact]
    public void Initializer_Default()
    {
        AssertHighlighter("cpp",
"""
int x{};
""",
"""
<span class="hljs-type">int</span> x{};
""");
    }

    [Fact]
    public void StructuredBinding_Pair()
    {
        AssertHighlighter("cpp",
"""
auto [first, second] = pair;
""",
"""
<span class="hljs-keyword">auto</span> [first, second] = pair;
""");
    }

    [Fact]
    public void StructuredBinding_Tuple()
    {
        AssertHighlighter("cpp",
"""
auto [a, b, c] = std::make_tuple(1, "two", 3.0);
""",
"""
<span class="hljs-keyword">auto</span> [a, b, c] = std::<span class="hljs-built_in">make_tuple</span>(<span class="hljs-number">1</span>, <span class="hljs-string">&quot;two&quot;</span>, <span class="hljs-number">3.0</span>);
""");
    }

    [Fact]
    public void StructuredBinding_MapIter()
    {
        AssertHighlighter("cpp",
"""
for (const auto& [key, value] : ages) {
    std::cout << key << '=' << value;
}
""",
"""
<span class="hljs-keyword">for</span> (<span class="hljs-type">const</span> <span class="hljs-keyword">auto</span>&amp; [key, value] : ages) {
    std::cout &lt;&lt; key &lt;&lt; <span class="hljs-string">&#x27;=&#x27;</span> &lt;&lt; value;
}
""");
    }

    [Fact]
    public void StructuredBinding_Reference()
    {
        AssertHighlighter("cpp",
"""
auto& [a, b] = pair;
""",
"""
<span class="hljs-keyword">auto</span>&amp; [a, b] = pair;
""");
    }

    [Fact]
    public void Coroutine_CoReturn()
    {
        AssertHighlighter("cpp",
"""
task<int> compute() {
    co_return 42;
}
""",
"""
<span class="hljs-function">task&lt;<span class="hljs-type">int</span>&gt; <span class="hljs-title">compute</span><span class="hljs-params">()</span> </span>{
    <span class="hljs-keyword">co_return</span> <span class="hljs-number">42</span>;
}
""");
    }

    [Fact]
    public void Coroutine_CoAwait()
    {
        AssertHighlighter("cpp",
"""
task<std::string> fetch() {
    auto body = co_await client.get(url);
    co_return body;
}
""",
"""
<span class="hljs-function">task&lt;std::string&gt; <span class="hljs-title">fetch</span><span class="hljs-params">()</span> </span>{
    <span class="hljs-keyword">auto</span> body = <span class="hljs-keyword">co_await</span> client.<span class="hljs-built_in">get</span>(url);
    <span class="hljs-keyword">co_return</span> body;
}
""");
    }

    [Fact]
    public void Coroutine_CoYield()
    {
        AssertHighlighter("cpp",
"""
generator<int> range(int n) {
    for (int i = 0; i < n; ++i) co_yield i;
}
""",
"""
<span class="hljs-function">generator&lt;<span class="hljs-type">int</span>&gt; <span class="hljs-title">range</span><span class="hljs-params">(<span class="hljs-type">int</span> n)</span> </span>{
    <span class="hljs-keyword">for</span> (<span class="hljs-type">int</span> i = <span class="hljs-number">0</span>; i &lt; n; ++i) <span class="hljs-keyword">co_yield</span> i;
}
""");
    }

    [Fact]
    public void Module_ModuleDecl()
    {
        AssertHighlighter("cpp",
"""
export module math;
""",
"""
<span class="hljs-keyword">export</span> <span class="hljs-keyword">module</span> math;
""");
    }

    [Fact]
    public void Module_ExportFunction()
    {
        AssertHighlighter("cpp",
"""
export module shapes;
export int area(int side) { return side * side; }
""",
"""
<span class="hljs-keyword">export</span> <span class="hljs-keyword">module</span> shapes;
<span class="hljs-function"><span class="hljs-keyword">export</span> <span class="hljs-type">int</span> <span class="hljs-title">area</span><span class="hljs-params">(<span class="hljs-type">int</span> side)</span> </span>{ <span class="hljs-keyword">return</span> side * side; }
""");
    }

    [Fact]
    public void Module_ImportModule()
    {
        AssertHighlighter("cpp",
"""
import std;
import math;
import <iostream>;
""",
"""
<span class="hljs-keyword">import</span> std;
<span class="hljs-keyword">import</span> math;
<span class="hljs-keyword">import</span> &lt;iostream&gt;;
""");
    }

    [Fact]
    public void Attribute_Nodiscard()
    {
        AssertHighlighter("cpp",
"""
[[nodiscard]] int compute();
""",
"""
[[nodiscard]] <span class="hljs-function"><span class="hljs-type">int</span> <span class="hljs-title">compute</span><span class="hljs-params">()</span></span>;
""");
    }

    [Fact]
    public void Attribute_NodiscardReason()
    {
        AssertHighlighter("cpp",
"""
[[nodiscard("ignoring leaks")]] FileHandle open(const char* path);
""",
"""
[[<span class="hljs-built_in">nodiscard</span>(<span class="hljs-string">&quot;ignoring leaks&quot;</span>)]] <span class="hljs-function">FileHandle <span class="hljs-title">open</span><span class="hljs-params">(<span class="hljs-type">const</span> <span class="hljs-type">char</span>* path)</span></span>;
""");
    }

    [Fact]
    public void Attribute_Deprecated()
    {
        AssertHighlighter("cpp",
"""
[[deprecated("Use newMethod instead")]] void oldMethod();
""",
"""
[[<span class="hljs-built_in">deprecated</span>(<span class="hljs-string">&quot;Use newMethod instead&quot;</span>)]] <span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">oldMethod</span><span class="hljs-params">()</span></span>;
""");
    }

    [Fact]
    public void Attribute_Maybe_Unused()
    {
        AssertHighlighter("cpp",
"""
[[maybe_unused]] int unused = 0;
""",
"""
[[maybe_unused]] <span class="hljs-type">int</span> unused = <span class="hljs-number">0</span>;
""");
    }

    [Fact]
    public void Attribute_Noreturn()
    {
        AssertHighlighter("cpp",
"""
[[noreturn]] void fail();
""",
"""
[[noreturn]] <span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">fail</span><span class="hljs-params">()</span></span>;
""");
    }

    [Fact]
    public void Attribute_Likely()
    {
        AssertHighlighter("cpp",
"""
if (ptr) [[likely]] {
    use(ptr);
}
""",
"""
<span class="hljs-keyword">if</span> (ptr) [[likely]] {
    <span class="hljs-built_in">use</span>(ptr);
}
""");
    }

    [Fact]
    public void Attribute_Unlikely()
    {
        AssertHighlighter("cpp",
"""
if (failed) [[unlikely]] {
    log();
}
""",
"""
<span class="hljs-keyword">if</span> (failed) [[unlikely]] {
    <span class="hljs-built_in">log</span>();
}
""");
    }

    [Fact]
    public void Attribute_NoUniqueAddress()
    {
        AssertHighlighter("cpp",
"""
struct C { [[no_unique_address]] Empty e; int x; };
""",
"""
<span class="hljs-keyword">struct</span> <span class="hljs-title class_">C</span> { [[no_unique_address]] Empty e; <span class="hljs-type">int</span> x; };
""");
    }

    [Fact]
    public void Attribute_AssumeAttr()
    {
        AssertHighlighter("cpp",
"""
void process(int n) {
    [[assume(n > 0)]];
}
""",
"""
<span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">process</span><span class="hljs-params">(<span class="hljs-type">int</span> n)</span> </span>{
    [[<span class="hljs-built_in">assume</span>(n &gt; <span class="hljs-number">0</span>)]];
}
""");
    }

    [Fact]
    public void Comment_Line()
    {
        AssertHighlighter("cpp",
"""
// this is a line comment
""",
"""
<span class="hljs-comment">// this is a line comment</span>
""");
    }

    [Fact]
    public void Comment_Block()
    {
        AssertHighlighter("cpp",
"""
/* this is a block comment */
""",
"""
<span class="hljs-comment">/* this is a block comment */</span>
""");
    }

    [Fact]
    public void Comment_BlockMultiLine()
    {
        AssertHighlighter("cpp",
"""
/*
 * multi-line
 * block comment
 */
""",
"""
<span class="hljs-comment">/*
 * multi-line
 * block comment
 */</span>
""");
    }

    [Fact]
    public void Comment_Doxygen()
    {
        AssertHighlighter("cpp",
"""
/**
 * Adds two integers.
 * @param a First operand.
 * @param b Second operand.
 */
int add(int a, int b);
""",
"""
<span class="hljs-comment">/**
 * Adds two integers.
 * @param a First operand.
 * @param b Second operand.
 */</span>
<span class="hljs-function"><span class="hljs-type">int</span> <span class="hljs-title">add</span><span class="hljs-params">(<span class="hljs-type">int</span> a, <span class="hljs-type">int</span> b)</span></span>;
""");
    }

    [Fact]
    public void Composite_HelloWorld()
    {
        AssertHighlighter("cpp",
"""
#include <iostream>

int main() {
    std::cout << "Hello, world!" << std::endl;
    return 0;
}
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&lt;iostream&gt;</span></span>

<span class="hljs-function"><span class="hljs-type">int</span> <span class="hljs-title">main</span><span class="hljs-params">()</span> </span>{
    std::cout &lt;&lt; <span class="hljs-string">&quot;Hello, world!&quot;</span> &lt;&lt; std::endl;
    <span class="hljs-keyword">return</span> <span class="hljs-number">0</span>;
}
""");
    }

    [Fact]
    public void Composite_ModernClass()
    {
        AssertHighlighter("cpp",
"""
#include <iostream>
#include <string>
#include <vector>

class User {
public:
    User(std::string name, int age) : name_(std::move(name)), age_(age) { }

    [[nodiscard]] const std::string& name() const noexcept { return name_; }
    [[nodiscard]] int age() const noexcept { return age_; }

    auto operator<=>(const User&) const = default;

private:
    std::string name_;
    int age_;
};

int main() {
    std::vector<User> users{ {"alice", 30}, {"bob", 25} };
    std::sort(users.begin(), users.end());
    for (const auto& u : users) {
        std::cout << u.name() << " (" << u.age() << ")\n";
    }
}
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&lt;iostream&gt;</span></span>
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&lt;string&gt;</span></span>
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&lt;vector&gt;</span></span>

<span class="hljs-keyword">class</span> <span class="hljs-title class_">User</span> {
<span class="hljs-keyword">public</span>:
    <span class="hljs-built_in">User</span>(std::string name, <span class="hljs-type">int</span> age) : <span class="hljs-built_in">name_</span>(std::<span class="hljs-built_in">move</span>(name)), <span class="hljs-built_in">age_</span>(age) { }

    [[nodiscard]] <span class="hljs-function"><span class="hljs-type">const</span> std::string&amp; <span class="hljs-title">name</span><span class="hljs-params">()</span> <span class="hljs-type">const</span> <span class="hljs-keyword">noexcept</span> </span>{ <span class="hljs-keyword">return</span> name_; }
    [[nodiscard]] <span class="hljs-function"><span class="hljs-type">int</span> <span class="hljs-title">age</span><span class="hljs-params">()</span> <span class="hljs-type">const</span> <span class="hljs-keyword">noexcept</span> </span>{ <span class="hljs-keyword">return</span> age_; }

    <span class="hljs-keyword">auto</span> <span class="hljs-built_in">operator</span>&lt;=&gt;(<span class="hljs-type">const</span> User&amp;) <span class="hljs-type">const</span> = <span class="hljs-keyword">default</span>;

<span class="hljs-keyword">private</span>:
    std::string name_;
    <span class="hljs-type">int</span> age_;
};

<span class="hljs-function"><span class="hljs-type">int</span> <span class="hljs-title">main</span><span class="hljs-params">()</span> </span>{
    std::vector&lt;User&gt; users{ {<span class="hljs-string">&quot;alice&quot;</span>, <span class="hljs-number">30</span>}, {<span class="hljs-string">&quot;bob&quot;</span>, <span class="hljs-number">25</span>} };
    std::<span class="hljs-built_in">sort</span>(users.<span class="hljs-built_in">begin</span>(), users.<span class="hljs-built_in">end</span>());
    <span class="hljs-keyword">for</span> (<span class="hljs-type">const</span> <span class="hljs-keyword">auto</span>&amp; u : users) {
        std::cout &lt;&lt; u.<span class="hljs-built_in">name</span>() &lt;&lt; <span class="hljs-string">&quot; (&quot;</span> &lt;&lt; u.<span class="hljs-built_in">age</span>() &lt;&lt; <span class="hljs-string">&quot;)\n&quot;</span>;
    }
}
""");
    }

    [Fact]
    public void Composite_TemplateConcepts()
    {
        AssertHighlighter("cpp",
"""
#include <concepts>
#include <iostream>

template <typename T>
concept Numeric = std::is_arithmetic_v<T>;

template <Numeric T>
T sum(const std::vector<T>& values) {
    T total{};
    for (const auto& v : values) total += v;
    return total;
}
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&lt;concepts&gt;</span></span>
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&lt;iostream&gt;</span></span>

<span class="hljs-keyword">template</span> &lt;<span class="hljs-keyword">typename</span> T&gt;
<span class="hljs-keyword">concept</span> Numeric = std::is_arithmetic_v&lt;T&gt;;

<span class="hljs-keyword">template</span> &lt;Numeric T&gt;
<span class="hljs-function">T <span class="hljs-title">sum</span><span class="hljs-params">(<span class="hljs-type">const</span> std::vector&lt;T&gt;&amp; values)</span> </span>{
    T total{};
    <span class="hljs-keyword">for</span> (<span class="hljs-type">const</span> <span class="hljs-keyword">auto</span>&amp; v : values) total += v;
    <span class="hljs-keyword">return</span> total;
}
""");
    }

    [Fact]
    public void Composite_Coroutine()
    {
        AssertHighlighter("cpp",
"""
#include <coroutine>
#include <iostream>

struct task {
    struct promise_type {
        task get_return_object() { return {}; }
        std::suspend_never initial_suspend() { return {}; }
        std::suspend_never final_suspend() noexcept { return {}; }
        void return_void() { }
        void unhandled_exception() { }
    };
};

task run() {
    std::cout << "hello\n";
    co_return;
}
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&lt;coroutine&gt;</span></span>
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&lt;iostream&gt;</span></span>

<span class="hljs-keyword">struct</span> <span class="hljs-title class_">task</span> {
    <span class="hljs-keyword">struct</span> <span class="hljs-title class_">promise_type</span> {
        <span class="hljs-function">task <span class="hljs-title">get_return_object</span><span class="hljs-params">()</span> </span>{ <span class="hljs-keyword">return</span> {}; }
        <span class="hljs-function">std::suspend_never <span class="hljs-title">initial_suspend</span><span class="hljs-params">()</span> </span>{ <span class="hljs-keyword">return</span> {}; }
        <span class="hljs-function">std::suspend_never <span class="hljs-title">final_suspend</span><span class="hljs-params">()</span> <span class="hljs-keyword">noexcept</span> </span>{ <span class="hljs-keyword">return</span> {}; }
        <span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">return_void</span><span class="hljs-params">()</span> </span>{ }
        <span class="hljs-function"><span class="hljs-type">void</span> <span class="hljs-title">unhandled_exception</span><span class="hljs-params">()</span> </span>{ }
    };
};

<span class="hljs-function">task <span class="hljs-title">run</span><span class="hljs-params">()</span> </span>{
    std::cout &lt;&lt; <span class="hljs-string">&quot;hello\n&quot;</span>;
    <span class="hljs-keyword">co_return</span>;
}
""");
    }

    [Fact]
    public void SpecialEdge_Empty()
    {
        AssertHighlighter("cpp",
"""

""",
"""

""");
    }

    [Fact]
    public void SpecialEdge_OnlyComment()
    {
        AssertHighlighter("cpp",
"""
// nothing here
""",
"""
<span class="hljs-comment">// nothing here</span>
""");
    }

    [Fact]
    public void SpecialEdge_OnlyInclude()
    {
        AssertHighlighter("cpp",
"""
#include <iostream>
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&lt;iostream&gt;</span></span>
""");
    }

    [Fact]
    public void SpecialEdge_TrailingNewline()
    {
        AssertHighlighter("cpp",
"""
int main() { return 0; }

""",
"""
<span class="hljs-function"><span class="hljs-type">int</span> <span class="hljs-title">main</span><span class="hljs-params">()</span> </span>{ <span class="hljs-keyword">return</span> <span class="hljs-number">0</span>; }

""");
    }
}
