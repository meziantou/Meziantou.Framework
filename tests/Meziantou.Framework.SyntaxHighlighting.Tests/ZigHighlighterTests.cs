namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class ZigHighlighterTests
{
    [Fact]
    public void Comments()
    {
        AssertHighlighter("zig",
"""
//! Container doc comment.
/// Doc comment.
// Line comment.
//// Not a doc comment.
const a = 1; // trailing
// TODO: fix
""",
"""
<span class="hljs-comment">//! Container doc comment.</span>
<span class="hljs-comment">/// Doc comment.</span>
<span class="hljs-comment">// Line comment.</span>
<span class="hljs-comment">//// Not a doc comment.</span>
<span class="hljs-keyword">const</span> a = <span class="hljs-number">1</span>; <span class="hljs-comment">// trailing</span>
<span class="hljs-comment">// <span class="hljs-doctag">TODO:</span> fix</span>
""");
    }

    [Fact]
    public void HelloWorld()
    {
        AssertHighlighter("zig",
"""
const std = @import("std");

pub fn main() !void {
    const stdout = std.io.getStdOut().writer();
    try stdout.print("Hello, {s}!\n", .{"world"});
}
""",
"""
<span class="hljs-keyword">const</span> std = <span class="hljs-built_in">@import</span>(<span class="hljs-string">&quot;std&quot;</span>);

<span class="hljs-keyword">pub</span> <span class="hljs-keyword">fn</span> <span class="hljs-title function_">main</span>() !<span class="hljs-type">void</span> {
    <span class="hljs-keyword">const</span> stdout = std.io.<span class="hljs-title function_ invoke__">getStdOut</span>().<span class="hljs-title function_ invoke__">writer</span>();
    <span class="hljs-keyword">try</span> stdout.<span class="hljs-title function_ invoke__">print</span>(<span class="hljs-string">&quot;Hello, {s}!\n&quot;</span>, .{<span class="hljs-string">&quot;world&quot;</span>});
}
""");
    }

    [Fact]
    public void FunctionDeclarations()
    {
        AssertHighlighter("zig",
"""
fn add(a: i32, b: i32) i32 {
    return a + b;
}

pub inline fn generic(comptime T: type, value: anytype) T {
    return @intCast(value);
}

export fn c_func(a: c_int) callconv(.C) c_int {
    return a;
}
""",
"""
<span class="hljs-keyword">fn</span> <span class="hljs-title function_">add</span>(a: <span class="hljs-type">i32</span>, b: <span class="hljs-type">i32</span>) <span class="hljs-type">i32</span> {
    <span class="hljs-keyword">return</span> a + b;
}

<span class="hljs-keyword">pub</span> <span class="hljs-keyword">inline</span> <span class="hljs-keyword">fn</span> <span class="hljs-title function_">generic</span>(<span class="hljs-keyword">comptime</span> T: <span class="hljs-type">type</span>, value: <span class="hljs-keyword">anytype</span>) T {
    <span class="hljs-keyword">return</span> <span class="hljs-built_in">@intCast</span>(value);
}

<span class="hljs-keyword">export</span> <span class="hljs-keyword">fn</span> <span class="hljs-title function_">c_func</span>(a: <span class="hljs-type">c_int</span>) <span class="hljs-keyword">callconv</span>(<span class="hljs-symbol">.C</span>) <span class="hljs-type">c_int</span> {
    <span class="hljs-keyword">return</span> a;
}
""");
    }

    [Fact]
    public void ContainerDeclarations()
    {
        AssertHighlighter("zig",
"""
const Point = struct {
    x: f32 = 0,
    y: f32 = 0,
};
const Color = enum(u8) { red, green, blue = 4 };
pub const Header = extern struct { magic: u32 };
const Flags = packed struct(u8) { a: bool, b: u7 };
const Value = union(enum) { int: i64, none };
const FileError = error{ AccessDenied, OutOfMemory };
const Handle = opaque {};
const err = error.OutOfMemory;
const Self = @This();
""",
"""
<span class="hljs-keyword">const</span> <span class="hljs-title class_">Point</span> = <span class="hljs-keyword">struct</span> {
    x: <span class="hljs-type">f32</span> = <span class="hljs-number">0</span>,
    y: <span class="hljs-type">f32</span> = <span class="hljs-number">0</span>,
};
<span class="hljs-keyword">const</span> <span class="hljs-title class_">Color</span> = <span class="hljs-keyword">enum</span>(<span class="hljs-type">u8</span>) { red, green, blue = <span class="hljs-number">4</span> };
<span class="hljs-keyword">pub</span> <span class="hljs-keyword">const</span> <span class="hljs-title class_">Header</span> = <span class="hljs-keyword">extern</span> <span class="hljs-keyword">struct</span> { magic: <span class="hljs-type">u32</span> };
<span class="hljs-keyword">const</span> <span class="hljs-title class_">Flags</span> = <span class="hljs-keyword">packed</span> <span class="hljs-keyword">struct</span>(<span class="hljs-type">u8</span>) { a: <span class="hljs-type">bool</span>, b: <span class="hljs-type">u7</span> };
<span class="hljs-keyword">const</span> <span class="hljs-title class_">Value</span> = <span class="hljs-keyword">union</span>(<span class="hljs-keyword">enum</span>) { int: <span class="hljs-type">i64</span>, none };
<span class="hljs-keyword">const</span> <span class="hljs-title class_">FileError</span> = <span class="hljs-keyword">error</span>{ AccessDenied, OutOfMemory };
<span class="hljs-keyword">const</span> <span class="hljs-title class_">Handle</span> = <span class="hljs-keyword">opaque</span> {};
<span class="hljs-keyword">const</span> err = <span class="hljs-keyword">error</span>.OutOfMemory;
<span class="hljs-keyword">const</span> Self = <span class="hljs-built_in">@This</span>();
""");
    }

    [Fact]
    public void PrimitiveTypes()
    {
        AssertHighlighter("zig",
"""
var a: u8 = 0;
var b: i128 = 0;
var c: u7 = 0;
var d: i0 = 0;
var e: usize = 0;
var f: c_longlong = 0;
var g: f80 = 0;
var h: comptime_float = 0;
var i: anyerror!noreturn = undefined;
var j: ?*const [5:0]u8 = null;
var k: anyopaque = undefined;
var u8x: u8 = 0;
""",
"""
<span class="hljs-keyword">var</span> a: <span class="hljs-type">u8</span> = <span class="hljs-number">0</span>;
<span class="hljs-keyword">var</span> b: <span class="hljs-type">i128</span> = <span class="hljs-number">0</span>;
<span class="hljs-keyword">var</span> c: <span class="hljs-type">u7</span> = <span class="hljs-number">0</span>;
<span class="hljs-keyword">var</span> d: <span class="hljs-type">i0</span> = <span class="hljs-number">0</span>;
<span class="hljs-keyword">var</span> e: <span class="hljs-type">usize</span> = <span class="hljs-number">0</span>;
<span class="hljs-keyword">var</span> f: <span class="hljs-type">c_longlong</span> = <span class="hljs-number">0</span>;
<span class="hljs-keyword">var</span> g: <span class="hljs-type">f80</span> = <span class="hljs-number">0</span>;
<span class="hljs-keyword">var</span> h: <span class="hljs-type">comptime_float</span> = <span class="hljs-number">0</span>;
<span class="hljs-keyword">var</span> i: <span class="hljs-type">anyerror</span>!<span class="hljs-type">noreturn</span> = <span class="hljs-literal">undefined</span>;
<span class="hljs-keyword">var</span> j: ?*<span class="hljs-keyword">const</span> [<span class="hljs-number">5</span>:<span class="hljs-number">0</span>]<span class="hljs-type">u8</span> = <span class="hljs-literal">null</span>;
<span class="hljs-keyword">var</span> k: <span class="hljs-type">anyopaque</span> = <span class="hljs-literal">undefined</span>;
<span class="hljs-keyword">var</span> u8x: <span class="hljs-type">u8</span> = <span class="hljs-number">0</span>;
""");
    }

    [Fact]
    public void Literals()
    {
        AssertHighlighter("zig",
"""
const values = .{ true, false, null, undefined };
fn f() void {
    unreachable;
}
""",
"""
<span class="hljs-keyword">const</span> values = .{ <span class="hljs-literal">true</span>, <span class="hljs-literal">false</span>, <span class="hljs-literal">null</span>, <span class="hljs-literal">undefined</span> };
<span class="hljs-keyword">fn</span> <span class="hljs-title function_">f</span>() <span class="hljs-type">void</span> {
    <span class="hljs-literal">unreachable</span>;
}
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("zig",
"""
const a = 42;
const b = 1_000_000;
const c = 0xFF_FF;
const d = 0o755;
const e = 0b1010_1010;
const f = 3.14;
const g = 1.5e-3;
const h = 6.02E+23;
const i = 0x1.8p3;
const j = 0x10p-2;
const range = 0..10;
const slice = items[1..];
""",
"""
<span class="hljs-keyword">const</span> a = <span class="hljs-number">42</span>;
<span class="hljs-keyword">const</span> b = <span class="hljs-number">1_000_000</span>;
<span class="hljs-keyword">const</span> c = <span class="hljs-number">0xFF_FF</span>;
<span class="hljs-keyword">const</span> d = <span class="hljs-number">0o755</span>;
<span class="hljs-keyword">const</span> e = <span class="hljs-number">0b1010_1010</span>;
<span class="hljs-keyword">const</span> f = <span class="hljs-number">3.14</span>;
<span class="hljs-keyword">const</span> g = <span class="hljs-number">1.5e-3</span>;
<span class="hljs-keyword">const</span> h = <span class="hljs-number">6.02E+23</span>;
<span class="hljs-keyword">const</span> i = <span class="hljs-number">0x1.8p3</span>;
<span class="hljs-keyword">const</span> j = <span class="hljs-number">0x10p-2</span>;
<span class="hljs-keyword">const</span> range = <span class="hljs-number">0</span>..<span class="hljs-number">10</span>;
<span class="hljs-keyword">const</span> slice = items[<span class="hljs-number">1</span>..];
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("zig",
"""
const s = "tab\there \"quoted\" \x41 \u{00e9}";
const empty = "";
const c = 'a';
const nl = '\n';
const quote = '\'';
const emoji = '\u{1F600}';
const pair = '😀';
const unterminated = "oops
const next = 1;
""",
"""
<span class="hljs-keyword">const</span> s = <span class="hljs-string">&quot;tab\there \&quot;quoted\&quot; \x41 \u{00e9}&quot;</span>;
<span class="hljs-keyword">const</span> empty = <span class="hljs-string">&quot;&quot;</span>;
<span class="hljs-keyword">const</span> c = <span class="hljs-string">&#x27;a&#x27;</span>;
<span class="hljs-keyword">const</span> nl = <span class="hljs-string">&#x27;\n&#x27;</span>;
<span class="hljs-keyword">const</span> quote = <span class="hljs-string">&#x27;\&#x27;&#x27;</span>;
<span class="hljs-keyword">const</span> emoji = <span class="hljs-string">&#x27;\u{1F600}&#x27;</span>;
<span class="hljs-keyword">const</span> pair = <span class="hljs-string">&#x27;😀&#x27;</span>;
<span class="hljs-keyword">const</span> unterminated = <span class="hljs-string">&quot;oops</span>
<span class="hljs-keyword">const</span> next = <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void MultilineStrings()
    {
        AssertHighlighter("zig",
"""
const text =
    \\first line
    \\  "quotes" and \n are not escapes
    \\
;
""",
"""
<span class="hljs-keyword">const</span> text =
    <span class="hljs-string">\\first line</span>
    <span class="hljs-string">\\  &quot;quotes&quot; and \n are not escapes</span>
    <span class="hljs-string">\\</span>
;
""");
    }

    [Fact]
    public void BuiltinFunctions()
    {
        AssertHighlighter("zig",
"""
const std = @import("std");
const n = @as(u32, @intCast(x));
const size = @sizeOf(Point);
const @"weird name" = @field(obj, "name");
""",
"""
<span class="hljs-keyword">const</span> std = <span class="hljs-built_in">@import</span>(<span class="hljs-string">&quot;std&quot;</span>);
<span class="hljs-keyword">const</span> n = <span class="hljs-built_in">@as</span>(<span class="hljs-type">u32</span>, <span class="hljs-built_in">@intCast</span>(x));
<span class="hljs-keyword">const</span> size = <span class="hljs-built_in">@sizeOf</span>(Point);
<span class="hljs-keyword">const</span> @&quot;weird name&quot; = <span class="hljs-built_in">@field</span>(obj, <span class="hljs-string">&quot;name&quot;</span>);
""");
    }

    [Fact]
    public void Labels()
    {
        AssertHighlighter("zig",
"""
const idx = blk: {
    for (items, 0..) |v, i| {
        if (v == target) break :blk i;
    }
    break :blk null;
};
outer: while (true) {
    inline for (.{ 1, 2 }) |n| {
        if (n == 2) continue :outer;
    }
}
const Pair = struct { key: u8, value: u8 };
""",
"""
<span class="hljs-keyword">const</span> idx = <span class="hljs-symbol">blk</span>: {
    <span class="hljs-keyword">for</span> (items, <span class="hljs-number">0</span>..) |v, i| {
        <span class="hljs-keyword">if</span> (v == target) <span class="hljs-keyword">break</span> :<span class="hljs-symbol">blk</span> i;
    }
    <span class="hljs-keyword">break</span> :<span class="hljs-symbol">blk</span> <span class="hljs-literal">null</span>;
};
<span class="hljs-symbol">outer</span>: <span class="hljs-keyword">while</span> (<span class="hljs-literal">true</span>) {
    <span class="hljs-keyword">inline</span> <span class="hljs-keyword">for</span> (.{ <span class="hljs-number">1</span>, <span class="hljs-number">2</span> }) |n| {
        <span class="hljs-keyword">if</span> (n == <span class="hljs-number">2</span>) <span class="hljs-keyword">continue</span> :<span class="hljs-symbol">outer</span>;
    }
}
<span class="hljs-keyword">const</span> <span class="hljs-title class_">Pair</span> = <span class="hljs-keyword">struct</span> { key: <span class="hljs-type">u8</span>, value: <span class="hljs-type">u8</span> };
""");
    }

    [Fact]
    public void EnumLiteralsAndStructLiterals()
    {
        AssertHighlighter("zig",
"""
const color: Color = .red;
switch (color) {
    .red => {},
    .green, .blue => {},
    else => unreachable,
}
const p = Point{ .x = 1, .y = 2 };
const q: Point = .{ .x = 1, .y = 2 };
const ok = a == .red and b.c == .blue;
""",
"""
<span class="hljs-keyword">const</span> color: Color = <span class="hljs-symbol">.red</span>;
<span class="hljs-keyword">switch</span> (color) {
    <span class="hljs-symbol">.red</span> =&gt; {},
    <span class="hljs-symbol">.green</span>, <span class="hljs-symbol">.blue</span> =&gt; {},
    <span class="hljs-keyword">else</span> =&gt; <span class="hljs-literal">unreachable</span>,
}
<span class="hljs-keyword">const</span> p = Point{ <span class="hljs-attr">.x</span> = <span class="hljs-number">1</span>, <span class="hljs-attr">.y</span> = <span class="hljs-number">2</span> };
<span class="hljs-keyword">const</span> q: Point = .{ <span class="hljs-attr">.x</span> = <span class="hljs-number">1</span>, <span class="hljs-attr">.y</span> = <span class="hljs-number">2</span> };
<span class="hljs-keyword">const</span> ok = a == <span class="hljs-symbol">.red</span> <span class="hljs-keyword">and</span> b.c == <span class="hljs-symbol">.blue</span>;
""");
    }

    [Fact]
    public void FieldAccess()
    {
        AssertHighlighter("zig",
"""
const deref = ptr.*.field;
const len = maybe.?.len;
const mode = builtin.mode;
const arch = builtin.cpu.arch.error;
const tag = foo().bar;
const x = list.items[0].value;
""",
"""
<span class="hljs-keyword">const</span> deref = ptr.*.field;
<span class="hljs-keyword">const</span> len = maybe.?.len;
<span class="hljs-keyword">const</span> mode = builtin.mode;
<span class="hljs-keyword">const</span> arch = builtin.cpu.arch.error;
<span class="hljs-keyword">const</span> tag = <span class="hljs-title function_ invoke__">foo</span>().bar;
<span class="hljs-keyword">const</span> x = list.items[<span class="hljs-number">0</span>].value;
""");
    }

    [Fact]
    public void MethodCalls()
    {
        AssertHighlighter("zig",
"""
var list = std.ArrayList(u8).init(allocator);
defer list.deinit();
try list.append('a');
const res = mayFail() catch |err| switch (err) {
    error.OutOfMemory => return null,
    else => return err,
};
""",
"""
<span class="hljs-keyword">var</span> list = std.<span class="hljs-title function_ invoke__">ArrayList</span>(<span class="hljs-type">u8</span>).<span class="hljs-title function_ invoke__">init</span>(allocator);
<span class="hljs-keyword">defer</span> list.<span class="hljs-title function_ invoke__">deinit</span>();
<span class="hljs-keyword">try</span> list.<span class="hljs-title function_ invoke__">append</span>(<span class="hljs-string">&#x27;a&#x27;</span>);
<span class="hljs-keyword">const</span> res = <span class="hljs-title function_ invoke__">mayFail</span>() <span class="hljs-keyword">catch</span> |err| <span class="hljs-keyword">switch</span> (err) {
    <span class="hljs-keyword">error</span>.OutOfMemory =&gt; <span class="hljs-keyword">return</span> <span class="hljs-literal">null</span>,
    <span class="hljs-keyword">else</span> =&gt; <span class="hljs-keyword">return</span> err,
};
""");
    }

    [Fact]
    public void Control()
    {
        AssertHighlighter("zig",
"""
test "basic add" {
    try std.testing.expectEqual(@as(i32, 42), add(40, 2));
}
comptime var i: u32 = 0;
errdefer allocator.free(buf);
const val = optional orelse 0;
threadlocal var tls: u32 = 0;
if (a and b or !c) {} else {}
""",
"""
<span class="hljs-keyword">test</span> <span class="hljs-string">&quot;basic add&quot;</span> {
    <span class="hljs-keyword">try</span> std.testing.<span class="hljs-title function_ invoke__">expectEqual</span>(<span class="hljs-built_in">@as</span>(<span class="hljs-type">i32</span>, <span class="hljs-number">42</span>), <span class="hljs-title function_ invoke__">add</span>(<span class="hljs-number">40</span>, <span class="hljs-number">2</span>));
}
<span class="hljs-keyword">comptime</span> <span class="hljs-keyword">var</span> i: <span class="hljs-type">u32</span> = <span class="hljs-number">0</span>;
<span class="hljs-keyword">errdefer</span> allocator.<span class="hljs-title function_ invoke__">free</span>(buf);
<span class="hljs-keyword">const</span> val = optional <span class="hljs-keyword">orelse</span> <span class="hljs-number">0</span>;
<span class="hljs-keyword">threadlocal</span> <span class="hljs-keyword">var</span> tls: <span class="hljs-type">u32</span> = <span class="hljs-number">0</span>;
<span class="hljs-keyword">if</span> (a <span class="hljs-keyword">and</span> b <span class="hljs-keyword">or</span> !c) {} <span class="hljs-keyword">else</span> {}
""");
    }

    [Fact]
    public void ZonFile()
    {
        AssertHighlighter("zon",
"""
.{
    .name = .my_project,
    .version = "0.1.0",
    .fingerprint = 0xa1b2c3d4e5f60718,
    .dependencies = .{
        .ziglyph = .{
            .url = "https://example.com/ziglyph.tar.gz",
            .lazy = true,
        },
    },
    .paths = .{ "build.zig", "src" },
}
""",
"""
.{
    <span class="hljs-attr">.name</span> = <span class="hljs-symbol">.my_project</span>,
    <span class="hljs-attr">.version</span> = <span class="hljs-string">&quot;0.1.0&quot;</span>,
    <span class="hljs-attr">.fingerprint</span> = <span class="hljs-number">0xa1b2c3d4e5f60718</span>,
    <span class="hljs-attr">.dependencies</span> = .{
        <span class="hljs-attr">.ziglyph</span> = .{
            <span class="hljs-attr">.url</span> = <span class="hljs-string">&quot;https://example.com/ziglyph.tar.gz&quot;</span>,
            <span class="hljs-attr">.lazy</span> = <span class="hljs-literal">true</span>,
        },
    },
    <span class="hljs-attr">.paths</span> = .{ <span class="hljs-string">&quot;build.zig&quot;</span>, <span class="hljs-string">&quot;src&quot;</span> },
}
""");
    }
}
