namespace Meziantou.Framework.JavaScriptEngine.Benchmarks;

/// <summary>The scripts of the benchmarks: each defines a function <c>run()</c> called by the benchmark.</summary>
internal static class Workloads
{
    public static IReadOnlyDictionary<string, string> All { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Loop"] = "function run() { let s = 0; for (let i = 0; i < 100000; i++) { s += i % 7; } return s; }",
        ["Calls"] = "function fib(n) { return n < 2 ? n : fib(n - 1) + fib(n - 2); } function run() { return fib(20); }",
        ["Properties"] = """
            function run() {
              const o = { a: 1, b: 2, c: 3, d: 4 };
              let s = 0;
              for (let i = 0; i < 20000; i++) { o.a = o.b + o.c; o.d = o.a - 1; s += o.d; }
              return s;
            }
            """,
        ["Allocation"] = "function run() { const items = []; for (let i = 0; i < 10000; i++) { items.push({ x: i, y: i * 2, name: 'p' + i }); } return items.length; }",
        ["ArrayMethods"] = """
            const data = Array.from({ length: 5000 }, (_, i) => i);
            function run() { return data.map(x => x * 2).filter(x => x % 3 === 0).reduce((a, b) => a + b, 0); }
            """,
        ["Strings"] = """
            function run() {
              let s = '';
              for (let i = 0; i < 5000; i++) { s += String.fromCharCode(97 + (i % 26)); }
              const parts = [];
              for (let i = 0; i < 2000; i++) { parts.push(s.slice(i, i + 5).toUpperCase()); }
              return parts.join(',').length + s.indexOf('xyz') + s.split('a').length;
            }
            """,
        ["Classes"] = """
            class Vector { constructor(x, y) { this.x = x; this.y = y; } add(o) { return new Vector(this.x + o.x, this.y + o.y); } get length() { return Math.sqrt(this.x * this.x + this.y * this.y); } }
            function run() { let v = new Vector(0, 0); const d = new Vector(1, 2); for (let i = 0; i < 10000; i++) { v = v.add(d); } return v.length; }
            """,
        ["Json"] = """
            const text = JSON.stringify(Array.from({ length: 500 }, (_, i) => ({ id: i, name: 'item ' + i, tags: ['a', 'b'], nested: { value: i / 3 } })));
            function run() { return JSON.stringify(JSON.parse(text)).length; }
            """,
        ["RegExp"] = """
            const lines = Array.from({ length: 1000 }, (_, i) => i % 3 === 0 ? 'user' + i + '@example.com' : 'not an email ' + i);
            function run() { let n = 0; const re = /^[\w.+-]+@[\w-]+\.[\w.]+$/; for (const line of lines) { if (re.test(line)) n++; } return n + 'a-b_c d'.replace(/[-_ ]/g, '').length; }
            """,
        ["Closures"] = """
            function counter() { let c = 0; return { inc: () => ++c, get: () => c }; }
            function run() { const counters = []; for (let i = 0; i < 1000; i++) counters.push(counter()); let s = 0; for (const c of counters) { c.inc(); c.inc(); s += c.get(); } return s; }
            """,
        ["MapSet"] = """
            function run() { const m = new Map(); const s = new Set(); for (let i = 0; i < 5000; i++) { m.set('k' + i, i); s.add(i % 100); } let t = 0; for (const [k, v] of m) t += v; return t + s.size; }
            """,
    };

    /// <summary>A test file of the web-platform-tests style, run after testharness.js (the harness command).</summary>
    public static string HarnessTests { get; } = """
        setup({ explicit_done: true });
        add_completion_callback(tests => { globalThis.passed = tests.filter(t => t.status === 0).length; });
        for (let i = 0; i < 20; i++) {
          test(() => {
            const items = Array.from({ length: 20 }, (_, j) => ({ id: j, name: 'item ' + j }));
            assert_equals(items.length, 20, 'length');
            assert_array_equals(items.map(x => x.id).slice(0, 3), [0, 1, 2]);
            assert_true(items.every(x => typeof x.name === 'string'), 'names');
            assert_throws_js(TypeError, () => { null.foo; });
            assert_object_equals({ a: i, b: [1, 2] }, { a: i, b: [1, 2] });
          }, 'test ' + i);
          promise_test(async () => {
            const value = await Promise.resolve(i);
            assert_equals(value, i);
          }, 'promise test ' + i);
        }
        done();
        """;

    /// <summary>A script of about 100 KB to measure the parser and the compiler.</summary>
    public static string LargeScript { get; } = string.Concat(Enumerable.Range(0, 400).Select(i => $$"""
        function f{{i}}(a, b, ...rest) {
          const obj = { a, b, [`k${a}`]: rest.length, method() { return this.a + {{i}}; } };
          for (let i = 0; i < rest.length; i++) { if (rest[i] > a) { b += rest[i]; } else { b -= i; } }
          class C{{i}} extends Object { #x = {{i}}; get x() { return this.#x; } static create() { return new C{{i}}(); } }
          return [obj.method(), C{{i}}.create().x, `${a}-${b}`, typeof rest, /a+b/g.test('aab')];
        }

        """));
}
