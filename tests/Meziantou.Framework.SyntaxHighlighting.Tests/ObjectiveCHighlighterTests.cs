namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class ObjectiveCHighlighterTests
{
    [Fact]
    public void HelloWorld()
    {
        AssertHighlighter("objectivec",
"""
#import <Foundation/Foundation.h>

int main(int argc, const char * argv[]) {
    @autoreleasepool {
        NSLog(@"Hello, World!");
    }
    return 0;
}
""",
"""
<span class="hljs-meta">#import <span class="hljs-string">&lt;Foundation/Foundation.h&gt;</span></span>

<span class="hljs-type">int</span> main(<span class="hljs-type">int</span> argc, <span class="hljs-keyword">const</span> <span class="hljs-type">char</span> * argv[]) {
    <span class="hljs-keyword">@autoreleasepool</span> {
        <span class="hljs-built_in">NSLog</span>(<span class="hljs-string">@&quot;Hello, World!&quot;</span>);
    }
    <span class="hljs-keyword">return</span> <span class="hljs-number">0</span>;
}
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("objectivec",
"""
// Line comment
/* Block
   comment */
/** Documentation TODO: write */
int x = 1; // trailing
""",
"""
<span class="hljs-comment">// Line comment</span>
<span class="hljs-comment">/* Block
   comment */</span>
<span class="hljs-comment">/** Documentation <span class="hljs-doctag">TODO:</span> write */</span>
<span class="hljs-type">int</span> x = <span class="hljs-number">1</span>; <span class="hljs-comment">// trailing</span>
""");
    }

    [Fact]
    public void Preprocessor()
    {
        AssertHighlighter("objectivec",
"""
#import "MyClass.h"
#include <stdio.h>
#define MAX_SIZE 100
#define SQUARE(x) ((x) * (x))
#define LONG_MACRO(a, b) \
    do { a = b; } while (0)
#ifdef DEBUG
#  define LOG(fmt, ...) NSLog(fmt, ##__VA_ARGS__)
#else
#  define LOG(...)
#endif
#pragma mark - Lifecycle
#if TARGET_OS_IPHONE && !defined(FOO) // comment
#elif __has_include(<UIKit/UIKit.h>)
#endif
#warning "Fix this"
#error Unsupported /* block */
""",
"""
<span class="hljs-meta">#import <span class="hljs-string">&quot;MyClass.h&quot;</span></span>
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&lt;stdio.h&gt;</span></span>
<span class="hljs-meta">#<span class="hljs-keyword">define</span> MAX_SIZE 100</span>
<span class="hljs-meta">#<span class="hljs-keyword">define</span> SQUARE(x) ((x) * (x))</span>
<span class="hljs-meta">#<span class="hljs-keyword">define</span> LONG_MACRO(a, b) \
    do { a = b; } while (0)</span>
<span class="hljs-meta">#<span class="hljs-keyword">ifdef</span> DEBUG</span>
<span class="hljs-meta">#  <span class="hljs-keyword">define</span> LOG(fmt, ...) NSLog(fmt, ##__VA_ARGS__)</span>
<span class="hljs-meta">#<span class="hljs-keyword">else</span></span>
<span class="hljs-meta">#  <span class="hljs-keyword">define</span> LOG(...)</span>
<span class="hljs-meta">#<span class="hljs-keyword">endif</span></span>
<span class="hljs-meta">#<span class="hljs-keyword">pragma</span> mark - Lifecycle</span>
<span class="hljs-meta">#<span class="hljs-keyword">if</span> TARGET_OS_IPHONE &amp;&amp; !defined(FOO) <span class="hljs-comment">// comment</span></span>
<span class="hljs-meta">#<span class="hljs-keyword">elif</span> __has_include(<span class="hljs-string">&lt;UIKit/UIKit.h&gt;)</span></span>
<span class="hljs-meta">#<span class="hljs-keyword">endif</span></span>
<span class="hljs-meta">#<span class="hljs-keyword">warning</span> <span class="hljs-string">&quot;Fix this&quot;</span></span>
<span class="hljs-meta">#<span class="hljs-keyword">error</span> Unsupported <span class="hljs-comment">/* block */</span></span>
""");
    }

    [Fact]
    public void Interface()
    {
        AssertHighlighter("objectivec",
"""
@interface Person : NSObject <NSCopying, NSCoding>

@property (nonatomic, strong) NSString *name;
@property (nonatomic, assign, readonly) NSInteger age;
@property (nonatomic, weak, nullable) id<PersonDelegate> delegate;
@property (nonatomic, copy) void (^completion)(BOOL success);
@property (atomic, getter=isEnabled, setter=setIsEnabled:) BOOL enabled;

- (instancetype)initWithName:(NSString *)name age:(NSInteger)age NS_DESIGNATED_INITIALIZER;
+ (instancetype)personWithName:(NSString *)name;
- (void)greet;

@end
""",
"""
<span class="hljs-class"><span class="hljs-keyword">@interface</span> <span class="hljs-title">Person</span> : <span class="hljs-title">NSObject</span> &lt;<span class="hljs-title">NSCopying</span>, <span class="hljs-title">NSCoding</span>&gt;</span>

<span class="hljs-keyword">@property</span> (<span class="hljs-keyword">nonatomic</span>, <span class="hljs-keyword">strong</span>) <span class="hljs-built_in">NSString</span> *name;
<span class="hljs-keyword">@property</span> (<span class="hljs-keyword">nonatomic</span>, <span class="hljs-keyword">assign</span>, <span class="hljs-keyword">readonly</span>) <span class="hljs-built_in">NSInteger</span> age;
<span class="hljs-keyword">@property</span> (<span class="hljs-keyword">nonatomic</span>, <span class="hljs-keyword">weak</span>, <span class="hljs-keyword">nullable</span>) <span class="hljs-type">id</span>&lt;PersonDelegate&gt; delegate;
<span class="hljs-keyword">@property</span> (<span class="hljs-keyword">nonatomic</span>, <span class="hljs-keyword">copy</span>) <span class="hljs-type">void</span> (^completion)(<span class="hljs-type">BOOL</span> success);
<span class="hljs-keyword">@property</span> (atomic, <span class="hljs-keyword">getter</span>=isEnabled, <span class="hljs-keyword">setter</span>=setIsEnabled:) <span class="hljs-type">BOOL</span> enabled;

- (<span class="hljs-keyword">instancetype</span>)initWithName:(<span class="hljs-built_in">NSString</span> *)name age:(<span class="hljs-built_in">NSInteger</span>)age <span class="hljs-built_in">NS_DESIGNATED_INITIALIZER</span>;
+ (<span class="hljs-keyword">instancetype</span>)personWithName:(<span class="hljs-built_in">NSString</span> *)name;
- (<span class="hljs-type">void</span>)greet;

<span class="hljs-keyword">@end</span>
""");
    }

    [Fact]
    public void Implementation()
    {
        AssertHighlighter("objectivec",
"""
@implementation Person

@synthesize name = _name;
@dynamic age;

- (instancetype)initWithName:(NSString *)name age:(NSInteger)age {
    self = [super init];
    if (self) {
        _name = [name copy];
        _age = age;
    }
    return self;
}

- (void)dealloc {
    [[NSNotificationCenter defaultCenter] removeObserver:self];
}

@end
""",
"""
<span class="hljs-class"><span class="hljs-keyword">@implementation</span> <span class="hljs-title">Person</span></span>

<span class="hljs-keyword">@synthesize</span> name = _name;
<span class="hljs-keyword">@dynamic</span> age;

- (<span class="hljs-keyword">instancetype</span>)initWithName:(<span class="hljs-built_in">NSString</span> *)name age:(<span class="hljs-built_in">NSInteger</span>)age {
    <span class="hljs-keyword">self</span> = [<span class="hljs-variable language_">super</span> init];
    <span class="hljs-keyword">if</span> (<span class="hljs-keyword">self</span>) {
        _name = [name <span class="hljs-keyword">copy</span>];
        _age = age;
    }
    <span class="hljs-keyword">return</span> <span class="hljs-keyword">self</span>;
}

- (<span class="hljs-type">void</span>)dealloc {
    [[<span class="hljs-built_in">NSNotificationCenter</span> defaultCenter] removeObserver:<span class="hljs-keyword">self</span>];
}

<span class="hljs-keyword">@end</span>
""");
    }

    [Fact]
    public void Categories()
    {
        AssertHighlighter("objectivec",
"""
@interface NSString (Utilities)
- (BOOL)isBlank;
@end

@interface ViewController () <UITableViewDataSource>
@property (nonatomic, strong) UITableView *tableView;
@end

@implementation NSString (Utilities)
- (BOOL)isBlank { return self.length == 0; }
@end
""",
"""
<span class="hljs-class"><span class="hljs-keyword">@interface</span> <span class="hljs-title">NSString</span> (<span class="hljs-title">Utilities</span>)</span>
- (<span class="hljs-type">BOOL</span>)isBlank;
<span class="hljs-keyword">@end</span>

<span class="hljs-class"><span class="hljs-keyword">@interface</span> <span class="hljs-title">ViewController</span> () &lt;<span class="hljs-title">UITableViewDataSource</span>&gt;</span>
<span class="hljs-keyword">@property</span> (<span class="hljs-keyword">nonatomic</span>, <span class="hljs-keyword">strong</span>) <span class="hljs-built_in">UITableView</span> *tableView;
<span class="hljs-keyword">@end</span>

<span class="hljs-class"><span class="hljs-keyword">@implementation</span> <span class="hljs-title">NSString</span> (<span class="hljs-title">Utilities</span>)</span>
- (<span class="hljs-type">BOOL</span>)isBlank { <span class="hljs-keyword">return</span> <span class="hljs-keyword">self</span>.length == <span class="hljs-number">0</span>; }
<span class="hljs-keyword">@end</span>
""");
    }

    [Fact]
    public void Protocol()
    {
        AssertHighlighter("objectivec",
"""
@protocol PersonDelegate <NSObject>
@required
- (void)personDidChange:(Person *)person;
@optional
- (void)personWillChange:(Person *)person;
@end

@class Person, Company;
""",
"""
<span class="hljs-class"><span class="hljs-keyword">@protocol</span> <span class="hljs-title">PersonDelegate</span> &lt;<span class="hljs-title">NSObject</span>&gt;</span>
<span class="hljs-keyword">@required</span>
- (<span class="hljs-type">void</span>)personDidChange:(Person *)person;
<span class="hljs-keyword">@optional</span>
- (<span class="hljs-type">void</span>)personWillChange:(Person *)person;
<span class="hljs-keyword">@end</span>

<span class="hljs-class"><span class="hljs-keyword">@class</span> <span class="hljs-title">Person</span>, <span class="hljs-title">Company</span>;</span>
""");
    }

    [Fact]
    public void MessageSends()
    {
        AssertHighlighter("objectivec",
"""
NSArray *items = [[NSArray alloc] initWithObjects:@"a", @"b", nil];
[self.tableView reloadData];
[array enumerateObjectsUsingBlock:^(id obj, NSUInteger idx, BOOL *stop) {
    NSLog(@"%@", obj);
}];
NSString *s = [NSString stringWithFormat:@"%d items, %@", count, name];
id result = [obj performSelector:@selector(doSomething:) withObject:nil];
[super viewDidLoad];
""",
"""
<span class="hljs-built_in">NSArray</span> *items = [[<span class="hljs-built_in">NSArray</span> alloc] initWithObjects:<span class="hljs-string">@&quot;a&quot;</span>, <span class="hljs-string">@&quot;b&quot;</span>, <span class="hljs-literal">nil</span>];
[<span class="hljs-keyword">self</span>.tableView reloadData];
[array enumerateObjectsUsingBlock:^(<span class="hljs-type">id</span> obj, <span class="hljs-built_in">NSUInteger</span> idx, <span class="hljs-type">BOOL</span> *stop) {
    <span class="hljs-built_in">NSLog</span>(<span class="hljs-string">@&quot;%@&quot;</span>, obj);
}];
<span class="hljs-built_in">NSString</span> *s = [<span class="hljs-built_in">NSString</span> stringWithFormat:<span class="hljs-string">@&quot;%d items, %@&quot;</span>, count, name];
<span class="hljs-type">id</span> result = [obj performSelector:<span class="hljs-keyword">@selector</span>(doSomething:) withObject:<span class="hljs-literal">nil</span>];
[<span class="hljs-variable language_">super</span> viewDidLoad];
""");
    }

    [Fact]
    public void Literals()
    {
        AssertHighlighter("objectivec",
"""
NSArray *array = @[@1, @2.5, @YES, @"string"];
NSDictionary *dict = @{@"key": @"value", @"count": @(count + 1)};
NSNumber *number = @42;
NSNumber *boolean = @NO;
NSNumber *character = @'c';
BOOL flag = YES;
id nothing = nil;
Class cls = Nil;
void *ptr = NULL;
""",
"""
<span class="hljs-built_in">NSArray</span> *array = @[@<span class="hljs-number">1</span>, @<span class="hljs-number">2.5</span>, @YES, <span class="hljs-string">@&quot;string&quot;</span>];
<span class="hljs-built_in">NSDictionary</span> *dict = @{<span class="hljs-string">@&quot;key&quot;</span>: <span class="hljs-string">@&quot;value&quot;</span>, <span class="hljs-string">@&quot;count&quot;</span>: @(count + <span class="hljs-number">1</span>)};
<span class="hljs-built_in">NSNumber</span> *number = @<span class="hljs-number">42</span>;
<span class="hljs-built_in">NSNumber</span> *boolean = @NO;
<span class="hljs-built_in">NSNumber</span> *character = @<span class="hljs-string">&#x27;c&#x27;</span>;
<span class="hljs-type">BOOL</span> flag = <span class="hljs-literal">YES</span>;
<span class="hljs-type">id</span> nothing = <span class="hljs-literal">nil</span>;
Class cls = Nil;
<span class="hljs-type">void</span> *ptr = <span class="hljs-literal">NULL</span>;
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("objectivec",
"""
NSString *a = @"Escapes: \n \t \" \\";
const char *b = "C string";
char c = 'x';
char d = '\n';
NSString *e = @"multi"
              @"part";
NSString *f = @"Unicode: é";
""",
"""
<span class="hljs-built_in">NSString</span> *a = <span class="hljs-string">@&quot;Escapes: \n \t \&quot; \\&quot;</span>;
<span class="hljs-keyword">const</span> <span class="hljs-type">char</span> *b = <span class="hljs-string">&quot;C string&quot;</span>;
<span class="hljs-type">char</span> c = <span class="hljs-string">&#x27;x&#x27;</span>;
<span class="hljs-type">char</span> d = <span class="hljs-string">&#x27;\n&#x27;</span>;
<span class="hljs-built_in">NSString</span> *e = <span class="hljs-string">@&quot;multi&quot;</span>
              <span class="hljs-string">@&quot;part&quot;</span>;
<span class="hljs-built_in">NSString</span> *f = <span class="hljs-string">@&quot;Unicode: é&quot;</span>;
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("objectivec",
"""
int a = 42;
int b = -17;
int c = 0x1F;
float d = 3.14f;
double e = 1.5e10;
double f = .5;
long g = 100L;
unsigned int h = 42u;
""",
"""
<span class="hljs-type">int</span> a = <span class="hljs-number">42</span>;
<span class="hljs-type">int</span> b = <span class="hljs-number">-17</span>;
<span class="hljs-type">int</span> c = <span class="hljs-number">0x1F</span>;
<span class="hljs-type">float</span> d = <span class="hljs-number">3.14</span>f;
<span class="hljs-type">double</span> e = <span class="hljs-number">1.5e10</span>;
<span class="hljs-type">double</span> f = <span class="hljs-number">.5</span>;
<span class="hljs-type">long</span> g = <span class="hljs-number">100</span>L;
<span class="hljs-type">unsigned</span> <span class="hljs-type">int</span> h = <span class="hljs-number">42</span>u;
""");
    }

    [Fact]
    public void Blocks()
    {
        AssertHighlighter("objectivec",
"""
void (^simpleBlock)(void) = ^{
    NSLog(@"Block");
};
int (^add)(int, int) = ^int(int a, int b) {
    return a + b;
};
__block int counter = 0;
__weak typeof(self) weakSelf = self;
dispatch_async(dispatch_get_main_queue(), ^{
    __strong typeof(weakSelf) strongSelf = weakSelf;
    [strongSelf update];
});
typedef void (^CompletionHandler)(NSData * _Nullable data, NSError * _Nullable error);
""",
"""
<span class="hljs-type">void</span> (^simpleBlock)(<span class="hljs-type">void</span>) = ^{
    <span class="hljs-built_in">NSLog</span>(<span class="hljs-string">@&quot;Block&quot;</span>);
};
<span class="hljs-type">int</span> (^add)(<span class="hljs-type">int</span>, <span class="hljs-type">int</span>) = ^<span class="hljs-type">int</span>(<span class="hljs-type">int</span> a, <span class="hljs-type">int</span> b) {
    <span class="hljs-keyword">return</span> a + b;
};
<span class="hljs-keyword">__block</span> <span class="hljs-type">int</span> counter = <span class="hljs-number">0</span>;
<span class="hljs-keyword">__weak</span> <span class="hljs-keyword">typeof</span>(<span class="hljs-keyword">self</span>) weakSelf = <span class="hljs-keyword">self</span>;
<span class="hljs-built_in">dispatch_async</span>(dispatch_get_main_queue(), ^{
    <span class="hljs-keyword">__strong</span> <span class="hljs-keyword">typeof</span>(weakSelf) strongSelf = weakSelf;
    [strongSelf update];
});
<span class="hljs-keyword">typedef</span> <span class="hljs-type">void</span> (^CompletionHandler)(<span class="hljs-built_in">NSData</span> * <span class="hljs-keyword">_Nullable</span> data, <span class="hljs-built_in">NSError</span> * <span class="hljs-keyword">_Nullable</span> error);
""");
    }

    [Fact]
    public void ControlFlow()
    {
        AssertHighlighter("objectivec",
"""
for (int i = 0; i < 10; i++) {
    if (i % 2 == 0) continue;
    else break;
}
for (NSString *item in items) { }
while (x > 0) { x--; }
do { x++; } while (x < 10);
switch (value) {
    case 1:
        break;
    default:
        break;
}
goto end;
""",
"""
<span class="hljs-keyword">for</span> (<span class="hljs-type">int</span> i = <span class="hljs-number">0</span>; i &lt; <span class="hljs-number">10</span>; i++) {
    <span class="hljs-keyword">if</span> (i % <span class="hljs-number">2</span> == <span class="hljs-number">0</span>) <span class="hljs-keyword">continue</span>;
    <span class="hljs-keyword">else</span> <span class="hljs-keyword">break</span>;
}
<span class="hljs-keyword">for</span> (<span class="hljs-built_in">NSString</span> *item <span class="hljs-keyword">in</span> items) { }
<span class="hljs-keyword">while</span> (x &gt; <span class="hljs-number">0</span>) { x--; }
<span class="hljs-keyword">do</span> { x++; } <span class="hljs-keyword">while</span> (x &lt; <span class="hljs-number">10</span>);
<span class="hljs-keyword">switch</span> (value) {
    <span class="hljs-keyword">case</span> <span class="hljs-number">1</span>:
        <span class="hljs-keyword">break</span>;
    <span class="hljs-keyword">default</span>:
        <span class="hljs-keyword">break</span>;
}
<span class="hljs-keyword">goto</span> end;
""");
    }

    [Fact]
    public void Exceptions()
    {
        AssertHighlighter("objectivec",
"""
@try {
    [self riskyOperation];
}
@catch (NSException *exception) {
    NSLog(@"%@", exception.reason);
    @throw;
}
@finally {
    [self cleanup];
}
@synchronized (self) {
    _count++;
}
""",
"""
<span class="hljs-keyword">@try</span> {
    [<span class="hljs-keyword">self</span> riskyOperation];
}
<span class="hljs-keyword">@catch</span> (<span class="hljs-built_in">NSException</span> *exception) {
    <span class="hljs-built_in">NSLog</span>(<span class="hljs-string">@&quot;%@&quot;</span>, exception.reason);
    <span class="hljs-keyword">@throw</span>;
}
<span class="hljs-keyword">@finally</span> {
    [<span class="hljs-keyword">self</span> cleanup];
}
<span class="hljs-keyword">@synchronized</span> (<span class="hljs-keyword">self</span>) {
    _count++;
}
""");
    }

    [Fact]
    public void Enums()
    {
        AssertHighlighter("objectivec",
"""
typedef NS_ENUM(NSInteger, Direction) {
    DirectionUp,
    DirectionDown = 5,
};
typedef NS_OPTIONS(NSUInteger, Options) {
    OptionNone = 0,
    OptionA = 1 << 0,
};
enum Color { Red, Green };
struct Point { int x; int y; };
typedef struct { float w; float h; } Size;
union Value { int i; float f; };
""",
"""
<span class="hljs-keyword">typedef</span> <span class="hljs-built_in">NS_ENUM</span>(<span class="hljs-built_in">NSInteger</span>, Direction) {
    DirectionUp,
    DirectionDown = <span class="hljs-number">5</span>,
};
<span class="hljs-keyword">typedef</span> <span class="hljs-built_in">NS_OPTIONS</span>(<span class="hljs-built_in">NSUInteger</span>, Options) {
    OptionNone = <span class="hljs-number">0</span>,
    OptionA = <span class="hljs-number">1</span> &lt;&lt; <span class="hljs-number">0</span>,
};
<span class="hljs-keyword">enum</span> Color { Red, Green };
<span class="hljs-keyword">struct</span> Point { <span class="hljs-type">int</span> x; <span class="hljs-type">int</span> y; };
<span class="hljs-keyword">typedef</span> <span class="hljs-keyword">struct</span> { <span class="hljs-type">float</span> w; <span class="hljs-type">float</span> h; } Size;
<span class="hljs-keyword">union</span> Value { <span class="hljs-type">int</span> i; <span class="hljs-type">float</span> f; };
""");
    }

    [Fact]
    public void Nullability()
    {
        AssertHighlighter("objectivec",
"""
NS_ASSUME_NONNULL_BEGIN
@interface Service : NSObject
- (nullable NSString *)fetch:(nonnull NSURL *)url error:(NSError * _Nullable * _Nullable)error;
@property (nonatomic, copy, null_resettable) NSString *title;
@end
NS_ASSUME_NONNULL_END
""",
"""
<span class="hljs-built_in">NS_ASSUME_NONNULL_BEGIN</span>
<span class="hljs-class"><span class="hljs-keyword">@interface</span> <span class="hljs-title">Service</span> : <span class="hljs-title">NSObject</span></span>
- (<span class="hljs-keyword">nullable</span> <span class="hljs-built_in">NSString</span> *)fetch:(<span class="hljs-keyword">nonnull</span> <span class="hljs-built_in">NSURL</span> *)url error:(<span class="hljs-built_in">NSError</span> * <span class="hljs-keyword">_Nullable</span> * <span class="hljs-keyword">_Nullable</span>)error;
<span class="hljs-keyword">@property</span> (<span class="hljs-keyword">nonatomic</span>, <span class="hljs-keyword">copy</span>, <span class="hljs-keyword">null_resettable</span>) <span class="hljs-built_in">NSString</span> *title;
<span class="hljs-keyword">@end</span>
<span class="hljs-built_in">NS_ASSUME_NONNULL_END</span>
""");
    }

    [Fact]
    public void Generics()
    {
        AssertHighlighter("objectivec",
"""
NSArray<NSString *> *names = @[];
NSDictionary<NSString *, NSNumber *> *map = @{};
@interface Box<__covariant ObjectType> : NSObject
- (ObjectType)value;
@end
__kindof UIView *view = nil;
""",
"""
<span class="hljs-built_in">NSArray</span>&lt;<span class="hljs-built_in">NSString</span> *&gt; *names = @[];
<span class="hljs-built_in">NSDictionary</span>&lt;<span class="hljs-built_in">NSString</span> *, <span class="hljs-built_in">NSNumber</span> *&gt; *map = @{};
<span class="hljs-class"><span class="hljs-keyword">@interface</span> <span class="hljs-title">Box</span>&lt;<span class="hljs-title">__covariant</span> <span class="hljs-title">ObjectType</span>&gt; : <span class="hljs-title">NSObject</span></span>
- (ObjectType)value;
<span class="hljs-keyword">@end</span>
<span class="hljs-keyword">__kindof</span> <span class="hljs-built_in">UIView</span> *view = <span class="hljs-literal">nil</span>;
""");
    }

    [Fact]
    public void DotAccess()
    {
        AssertHighlighter("objectivec",
"""
self.name = @"x";
self.copy = nil;
obj.class;
value.int;
self->_ivar = 1;
_name = name;
""",
"""
<span class="hljs-keyword">self</span>.name = <span class="hljs-string">@&quot;x&quot;</span>;
<span class="hljs-keyword">self</span>.copy = <span class="hljs-literal">nil</span>;
obj.class;
value.int;
<span class="hljs-keyword">self</span>-&gt;_ivar = <span class="hljs-number">1</span>;
_name = name;
""");
    }

    [Fact]
    public void Ivars()
    {
        AssertHighlighter("objectivec",
"""
@interface Counter : NSObject {
    @private
    int _count;
    @protected
    id _owner;
    @public
    BOOL _flag;
    @package
    char _c;
}
@end
""",
"""
<span class="hljs-class"><span class="hljs-keyword">@interface</span> <span class="hljs-title">Counter</span> : <span class="hljs-title">NSObject</span> </span>{
    <span class="hljs-keyword">@private</span>
    <span class="hljs-type">int</span> _count;
    <span class="hljs-keyword">@protected</span>
    <span class="hljs-type">id</span> _owner;
    <span class="hljs-keyword">@public</span>
    <span class="hljs-type">BOOL</span> _flag;
    <span class="hljs-keyword">@package</span>
    <span class="hljs-type">char</span> _c;
}
<span class="hljs-keyword">@end</span>
""");
    }

    [Fact]
    public void CFunctions()
    {
        AssertHighlighter("objectivec",
"""
static inline int max(int a, int b) { return a > b ? a : b; }
extern void helper(void);
const NSString *const kKey = @"key";
static dispatch_once_t onceToken;
dispatch_once(&onceToken, ^{ });
unsigned long long big = sizeof(int);
""",
"""
<span class="hljs-keyword">static</span> <span class="hljs-keyword">inline</span> <span class="hljs-type">int</span> max(<span class="hljs-type">int</span> a, <span class="hljs-type">int</span> b) { <span class="hljs-keyword">return</span> a &gt; b ? a : b; }
<span class="hljs-keyword">extern</span> <span class="hljs-type">void</span> helper(<span class="hljs-type">void</span>);
<span class="hljs-keyword">const</span> <span class="hljs-built_in">NSString</span> *<span class="hljs-keyword">const</span> kKey = <span class="hljs-string">@&quot;key&quot;</span>;
<span class="hljs-keyword">static</span> <span class="hljs-built_in">dispatch_once_t</span> onceToken;
<span class="hljs-built_in">dispatch_once</span>(&amp;onceToken, ^{ });
<span class="hljs-type">unsigned</span> <span class="hljs-type">long</span> <span class="hljs-type">long</span> big = <span class="hljs-keyword">sizeof</span>(<span class="hljs-type">int</span>);
""");
    }

    [Fact]
    public void Bridging()
    {
        AssertHighlighter("objectivec",
"""
CFStringRef cf = (__bridge CFStringRef)string;
NSString *ns = (__bridge_transfer NSString *)cfString;
CFTypeRef ref = (__bridge_retained CFTypeRef)obj;
""",
"""
<span class="hljs-built_in">CFStringRef</span> cf = (<span class="hljs-keyword">__bridge</span> <span class="hljs-built_in">CFStringRef</span>)string;
<span class="hljs-built_in">NSString</span> *ns = (<span class="hljs-keyword">__bridge_transfer</span> <span class="hljs-built_in">NSString</span> *)cfString;
<span class="hljs-built_in">CFTypeRef</span> ref = (<span class="hljs-keyword">__bridge_retained</span> <span class="hljs-built_in">CFTypeRef</span>)obj;
""");
    }

    [Fact]
    public void Attributes()
    {
        AssertHighlighter("objectivec",
"""
- (void)oldMethod __attribute__((deprecated("Use newMethod")));
- (void)viewDidLoad NS_REQUIRES_SUPER;
NSLog(@"%s", __PRETTY_FUNCTION__);
NSLog(@"%s", __FUNCTION__);
""",
"""
- (<span class="hljs-type">void</span>)oldMethod <span class="hljs-keyword">__attribute__</span>((deprecated(<span class="hljs-string">&quot;Use newMethod&quot;</span>)));
- (<span class="hljs-type">void</span>)viewDidLoad <span class="hljs-built_in">NS_REQUIRES_SUPER</span>;
<span class="hljs-built_in">NSLog</span>(<span class="hljs-string">@&quot;%s&quot;</span>, <span class="hljs-keyword">__PRETTY_FUNCTION__</span>);
<span class="hljs-built_in">NSLog</span>(<span class="hljs-string">@&quot;%s&quot;</span>, <span class="hljs-keyword">__FUNCTION__</span>);
""");
    }

    [Fact]
    public void Ib()
    {
        AssertHighlighter("objectivec",
"""
@property (nonatomic, weak) IBOutlet UILabel *label;
- (IBAction)buttonTapped:(id)sender;
""",
"""
<span class="hljs-keyword">@property</span> (<span class="hljs-keyword">nonatomic</span>, <span class="hljs-keyword">weak</span>) <span class="hljs-keyword">IBOutlet</span> <span class="hljs-built_in">UILabel</span> *label;
- (<span class="hljs-keyword">IBAction</span>)buttonTapped:(<span class="hljs-type">id</span>)sender;
""");
    }

    [Fact]
    public void SelectorsEncode()
    {
        AssertHighlighter("objectivec",
"""
SEL sel = @selector(tableView:cellForRowAtIndexPath:);
const char *type = @encode(int);
@compatibility_alias NewName OldName;
@import UIKit;
""",
"""
SEL sel = <span class="hljs-keyword">@selector</span>(tableView:cellForRowAtIndexPath:);
<span class="hljs-keyword">const</span> <span class="hljs-type">char</span> *type = <span class="hljs-keyword">@encode</span>(<span class="hljs-type">int</span>);
<span class="hljs-keyword">@compatibility_alias</span> NewName OldName;
<span class="hljs-keyword">@import</span> <span class="hljs-built_in">UIKit</span>;
""");
    }

    [Fact]
    public void ObjectiveCpp()
    {
        AssertHighlighter("objectivec",
"""
#include <vector>
std::vector<int> v;
class Foo { public: int x; };
auto lambda = [](int x) { return x * 2; };
""",
"""
<span class="hljs-meta">#<span class="hljs-keyword">include</span> <span class="hljs-string">&lt;vector&gt;</span></span>
std::vector&lt;<span class="hljs-type">int</span>&gt; v;
<span class="hljs-keyword">class</span> Foo { public: <span class="hljs-type">int</span> x; };
auto lambda = [](<span class="hljs-type">int</span> x) { <span class="hljs-keyword">return</span> x * <span class="hljs-number">2</span>; };
""");
    }

    [Fact]
    public void Modern()
    {
        AssertHighlighter("objectivec",
"""
NS_SWIFT_NAME(Person.Builder)
@interface PersonBuilder : NSObject
- (instancetype)init NS_UNAVAILABLE;
+ (instancetype)new NS_UNAVAILABLE;
@end
""",
"""
<span class="hljs-built_in">NS_SWIFT_NAME</span>(Person.Builder)
<span class="hljs-class"><span class="hljs-keyword">@interface</span> <span class="hljs-title">PersonBuilder</span> : <span class="hljs-title">NSObject</span></span>
- (<span class="hljs-keyword">instancetype</span>)init <span class="hljs-built_in">NS_UNAVAILABLE</span>;
+ (<span class="hljs-keyword">instancetype</span>)new <span class="hljs-built_in">NS_UNAVAILABLE</span>;
<span class="hljs-keyword">@end</span>
""");
    }

    [Fact]
    public void Gcd()
    {
        AssertHighlighter("objectivec",
"""
dispatch_queue_t queue = dispatch_queue_create("com.example.queue", DISPATCH_QUEUE_SERIAL);
dispatch_sync(queue, ^{ });
dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(1.0 * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{ });
""",
"""
<span class="hljs-built_in">dispatch_queue_t</span> queue = dispatch_queue_create(<span class="hljs-string">&quot;com.example.queue&quot;</span>, DISPATCH_QUEUE_SERIAL);
<span class="hljs-built_in">dispatch_sync</span>(queue, ^{ });
dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(<span class="hljs-number">1.0</span> * <span class="hljs-built_in">NSEC_PER_SEC</span>)), dispatch_get_main_queue(), ^{ });
""");
    }

    [Fact]
    public void ApiClasses()
    {
        AssertHighlighter("objectivec",
"""
UIView *view = [[UIView alloc] initWithFrame:CGRectZero];
CGFloat width = CGRectGetWidth(view.bounds);
NSUInteger count = 0;
CALayer *layer = view.layer;
AVPlayer *player = nil;
MKMapView *map;
WKWebView *web;
""",
"""
<span class="hljs-built_in">UIView</span> *view = [[<span class="hljs-built_in">UIView</span> alloc] initWithFrame:<span class="hljs-built_in">CGRectZero</span>];
<span class="hljs-built_in">CGFloat</span> width = <span class="hljs-built_in">CGRectGetWidth</span>(view.bounds);
<span class="hljs-built_in">NSUInteger</span> count = <span class="hljs-number">0</span>;
<span class="hljs-built_in">CALayer</span> *layer = view.layer;
<span class="hljs-built_in">AVPlayer</span> *player = <span class="hljs-literal">nil</span>;
<span class="hljs-built_in">MKMapView</span> *map;
<span class="hljs-built_in">WKWebView</span> *web;
""");
    }

    [Fact]
    public void HtmlClosingTag_IsIllegalButIgnored()
    {
        AssertHighlighter("objectivec",
"""
int a = 1; </b> int c = 2;
""",
"""
<span class="hljs-type">int</span> a = <span class="hljs-number">1</span>; &lt;/b&gt; <span class="hljs-type">int</span> c = <span class="hljs-number">2</span>;
""");
    }

    [Fact]
    public void ThisSuper()
    {
        AssertHighlighter("objectivec",
"""
[super init];
this->x = 1;
""",
"""
[<span class="hljs-variable language_">super</span> init];
<span class="hljs-variable language_">this</span>-&gt;x = <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void ClassEdgeCases()
    {
        AssertHighlighter("objectivec",
"""
@interface Foo
@end
@implementation Foo {
    int _x;
}
@end
@interface Bar : NSObject @end
""",
"""
<span class="hljs-class"><span class="hljs-keyword">@interface</span> <span class="hljs-title">Foo</span></span>
<span class="hljs-keyword">@end</span>
<span class="hljs-class"><span class="hljs-keyword">@implementation</span> <span class="hljs-title">Foo</span> </span>{
    <span class="hljs-type">int</span> _x;
}
<span class="hljs-keyword">@end</span>
<span class="hljs-class"><span class="hljs-keyword">@interface</span> <span class="hljs-title">Bar</span> : <span class="hljs-title">NSObject</span> @<span class="hljs-title">end</span></span>
""");
    }

    [Fact]
    public void NumberEdgeCases()
    {
        AssertHighlighter("objectivec",
"""
int a = arr[0];
float b = 1.;
int c = x-1;
int d = 1e5;
""",
"""
<span class="hljs-type">int</span> a = arr[<span class="hljs-number">0</span>];
<span class="hljs-type">float</span> b = <span class="hljs-number">1.</span>;
<span class="hljs-type">int</span> c = x<span class="hljs-number">-1</span>;
<span class="hljs-type">int</span> d = <span class="hljs-number">1e5</span>;
""");
    }

    [Fact]
    public void KeywordsStartingWithUnderscore()
    {
        AssertHighlighter("objectivec",
"""
_Bool flag = 1;
NSString * _Nonnull name;
NSString * _Null_unspecified other;
__autoreleasing NSError *error;
int _int = my_int + __x;
""",
"""
<span class="hljs-type">_Bool</span> flag = <span class="hljs-number">1</span>;
<span class="hljs-built_in">NSString</span> * <span class="hljs-keyword">_Nonnull</span> name;
<span class="hljs-built_in">NSString</span> * <span class="hljs-keyword">_Null_unspecified</span> other;
<span class="hljs-keyword">__autoreleasing</span> <span class="hljs-built_in">NSError</span> *error;
<span class="hljs-type">int</span> _int = my_int + __x;
""");
    }
}
