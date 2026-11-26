namespace Meziantou.Framework.JavaScriptEngine.Fuzz;

/// <summary>The scripts and the modules the corpus starts from: they cover the syntax and the built-ins of the engine.</summary>
internal static class JavaScriptSeeds
{
    public static IReadOnlyList<(string Name, string Code)> Scripts { get; } =
    [
        ("arithmetic", "let a = 1 + 2 * 3 ** 2 % 5; a >>>= 1; a ||= 4; typeof a"),
        ("function", "function fib(n) { return n < 2 ? n : fib(n - 1) + fib(n - 2); } fib(10)"),
        ("closure", "const counter = (() => { let n = 0; return () => ++n; })(); counter(); counter()"),
        ("class", "class A { #p = 1; static s = 2; get v() { return this.#p; } static { A.t = 3; } } class B extends A { constructor() { super(); } } new B().v"),
        ("generator", "function* g() { const x = yield 1; yield* [x, 3]; } [...g()]"),
        ("async", "async function f() { await null; for await (const x of [1, Promise.resolve(2)]) {} return 1; } f().then(v => v)"),
        ("destructuring", "const { a, b: [c, ...d], ...rest } = { a: 1, b: [2, 3, 4], e: 5 }; [a, c, d, rest]"),
        ("template", "const t = (s, ...v) => s.raw.join(v); t`a${1}b${2}\\n`"),
        ("regexp-literal", "/(?<y>\\d{4})-(?<m>\\d{2})/u.exec('2026-10').groups.y.replace(/\\d/g, 'x')"),
        ("labels", "outer: for (let i = 0; i < 3; i++) { for (let j = 0; j < 3; j++) { if (j == 1) continue outer; if (i == 2) break outer; } }"),
        ("exceptions", "try { null.x } catch ({ message }) { message } finally { 1 }"),
        ("switch", "switch (3) { case 1: break; case 3: let x = 1; default: x }"),
        ("proxy-reflect", "const p = new Proxy({}, { get: (t, k) => Reflect.ownKeys({ [k]: 1 }) }); p.a"),
        ("symbols", "const s = Symbol('d'); ({ [s]: 1, [Symbol.toPrimitive]() { return 1; } }) + 1"),
        ("collections", "const m = new Map([[1, 2]]); const s = new Set([1, 1, 2]); new WeakMap().set({}, 1); [m.size, s.size, Object.groupBy([1, 2, 3], x => x % 2)]"),
        ("typed-arrays", "const b = new ArrayBuffer(8, { maxByteLength: 16 }); const v = new DataView(b); v.setFloat64(0, Math.PI); new Uint8Array(b).toSorted()"),
        ("bigint", "(2n ** 100n).toString(16) + BigInt.asUintN(8, -1n)"),
        ("json", "JSON.stringify(JSON.parse('{\"a\":[1,2,{\"b\":null}]}', (k, v) => v), null, 2)"),
        ("date-temporal", "new Date(0).toISOString() + Temporal.PlainDate.from('2026-10-21').add({ months: 1 }).toString()"),
        ("intl", "new Intl.NumberFormat('fr-FR', { style: 'currency', currency: 'EUR' }).format(1234.5) + new Intl.DateTimeFormat('en', { timeZone: 'Asia/Tokyo' }).format(0)"),
        ("string-methods", "'abc'.padStart(6, 'x').normalize('NFD').at(-1) + 'a-b'.split('-').toString() + '\u00DF'.toUpperCase() + 'x'.localeCompare('y')"),
        ("array-methods", "[3, 1, 2].toSorted().flatMap(x => [x, [x]]).findLast(x => x > 1) + [1, 2, 3].with(0, 9).join()"),
        ("atomics", "const sab = new SharedArrayBuffer(16); const i = new Int32Array(sab); Atomics.add(i, 0, 5); Atomics.load(i, 0)"),
        ("eval-function", "eval('var z = 1; z + 1') + new Function('a', 'b', 'return a + b')(1, 2)"),
        ("with-sloppy", "with ({ q: 1 }) { q } var arguments_ = (function () { return arguments.length; })(1, 2)"),
        ("promises", "Promise.allSettled([Promise.reject(1), Promise.resolve(2)]).then(r => r); queueMicrotask(() => {})"),
        ("weakref", "const r = new WeakRef({}); new FinalizationRegistry(() => {}).register({}, 1); r.deref()"),
        ("limits", "let i = 0; while (true) { i++; }"),
        ("recursion", "function r() { r(); } r()"),
        ("hashbang", "#!/usr/bin/env node\n'use strict'; 1"),
        ("html-comments", "var x = 1; <!-- comment\n--> also a comment\nx"),
        ("to-int32", "[2 ** 31, -(2 ** 31) - 1, 2 ** 32 + 5.7, 2 ** 53 + 2, 2 ** 63 - 1024, -(2 ** 63), 2 ** 63, 2 ** 84 + 2 ** 32, -1e20, NaN].map(x => [x | 0, x >>> 0, x << 1, ~x])"),
        ("arguments-objects", "function u() { return arguments; } function s() { 'use strict'; return arguments; } function m(a) { a = 2; return arguments; } const f = function () { return [].slice.call(arguments).join(); }; [u(1, 'x'), s(1), m(1)].map(a => Reflect.ownKeys(a).map(String).join() + f.apply(null, a) + Math.max.apply(null, { length: 2, 0: 1, 1: 9 }))"),
        ("spread-packed-arrays", "function f() { return [...arguments]; } const big = Array.from({ length: 300 }, (_, i) => i); const holes = [1, , 3]; [f(...[1.5, NaN, -0]), f(...[{}, 'x', null, undefined]), f(...[1, 'a', {}]), f(0, ...big).length, f(...holes), f(...[], ...[-0])]"),
        ("global-stores", "var v = 0; let l = 0; globalThis.p = 0; function run(x) { for (var i = 0; i < 20; i++) { v += i; l += x; p = p + 1; } } run(1); v = 'v'; l = {}; p = null; run('!'); [v, l, p]"),
        ("property-table-deletions", "const o = {}; let s = 0; for (let i = 0; i < 300; i++) { o['k' + i] = i; if (i >= 20) delete o['k' + (i - 20)]; s += o['k' + (i >> 1)] ?? -1; } [s, Object.keys(o), 'k279' in o, 'k280' in o]"),
        ("date-components", "[-8.64e15, 8.64e15, -1, -86400001, -62198755200001, 0].map(t => { const d = new Date(t); return [d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate(), d.getUTCDay(), d.getUTCHours(), d.getUTCMinutes(), d.getUTCSeconds(), d.getUTCMilliseconds(), d.getHours()]; }).concat([-400, -100, 1600, 1700, 2000, 2100].map(y => Date.UTC(y, 1, 29)))"),
        ("array-copies", "const a = [1, 'x', , null, 2.5]; const b = [1, 2, 3]; class S extends Array {} const r = [a.slice(1, 4), b.slice(-2), b.concat(a, 7, [8, , 9]), [...b, ...a], [0, ...b], Array.from(b), Array.from(a), S.from(b).slice(1), b.map(x => x), b.slice({ valueOf() { b.length = 1; return 0; } }, 3)]; Object.defineProperty(Array, Symbol.species, { get() { return S; } }); r.push(b.slice(0) instanceof S, b.concat([]) instanceof S); r"),
        ("object-keys-values", "const o = { a: 1, b: 'x', [Symbol()]: 2, 3: 'n' }; Object.defineProperty(o, 'h', { value: 1, enumerable: false }); const g = { get a() { delete this.b; return 1; }, b: 2, c: 3 }; [Object.keys(o), Object.values(o), Object.entries(o), Object.values(g), Object.entries(g), Object.keys([1, , 3]), Object.entries('ab')]"),
        ("to-fixed", "[0.5, 1.005, 0.1, 12345.6789, 1e-20, 2.5e-15, 99.995, -1.45, 4503599627370495.5, 1e21].flatMap(x => [0, 2, 17, 19, 20, 100].map(f => x.toFixed(f)))"),
        ("set-prototype-chain","const b = Object.defineProperties({ w: 1 }, { ro: { value: 1 }, acc: { set(v) { this.log = v; } } }); const o = Object.create(b); for (const k of ['w', 'ro', 'acc', 'n' + 1]) o[k] = 2; const p = Object.create(new Proxy({}, {})); p['q'] = 3; const d = {}; for (let i = 0; i < 20; i++) { d['k' + i] = i; delete d['k' + (i >> 1)]; } [Object.keys(o), o.ro, Object.keys(p), Object.keys(d), Reflect.set(Object.preventExtensions({}), 'x', 1)]"),
    ];

    /// <summary>The scripts of the debugger target, in addition to <see cref="Scripts"/>: the code that raises events from other places than the script.</summary>
    public static IReadOnlyList<(string Name, string Code)> DebuggerScripts { get; } =
    [
        ("debugger-statement", "let a = 1;\ndebugger;\nfunction f() { debugger; return a; }\nf();"),
        ("nested-calls", "function a(x) { return b(x) + 1; }\nfunction b(x) { if (x > 0) { return a(x - 1); } return 0; }\na(5)"),
        ("eval-and-function", "eval('var e = 1;\\ndebugger;\\ne + 1');\nnew Function('x', 'debugger; return x * 2')(3);\n(0, eval)('1; 2')"),
        ("source-url", "eval('debugger;\\n1 + 1\\n//# sourceURL=other.js');\nlet x = 1;"),
        ("generator-async", "function* g() { yield 1; debugger; yield 2; }\nconst it = g(); it.next(); it.next();\nasync function f() { await null; debugger; return 1; }\nf().then(v => { v; });"),
        ("host-callbacks", "[3, 1, 2].sort((a, b) => { return a - b; });\n[1, 2].map(x => { return x * 2; });\nJSON.parse('[1,2]', (k, v) => { return v; });\nJSON.stringify({ toJSON() { return 1; } });\n'abc'.replace(/b/, () => { return 'x'; });"),
        ("getters-proxies", "const o = { get v() { return 1; } };\no.v;\nconst p = new Proxy({}, { get() { return 2; } });\np.x;\nclass C { static #s = 1; static { C.t = C.#s; } }"),
        ("try-finally", "let log = [];\ntry { try { throw 1; } finally { log.push(1); } } catch (e) { log.push(e); }\nlog"),
        ("loop-limit", "let i = 0;\nwhile (true) {\n  i++;\n}"),
        ("deep-recursion", "function r(n) { return n === 0 ? 0 : r(n - 1) + 1; }\nr(990)"),
        ("line-terminators", "let a = 1;\r\nlet b = 2;\rlet c = 3;\u2028let d = 4;\u2029a + b + c + d"),
        ("iterators", "for (const x of [1, 2]) { x; }\nfor (const k in { a: 1 }) { k; }\nconst [y, ...z] = new Set([1, 2, 3]);\n[...'ab']"),
    ];

    public static IReadOnlyList<(string Name, string Code)> Modules { get; } =
    [
        ("named-import", "import { x } from './a.js'; globalThis.r = x;"),
        ("default-import", "import f from './a.js'; f();"),
        ("namespace-import", "import * as ns from './a.js'; Object.keys(ns);"),
        ("reexport", "export * from './a.js'; export { y as z } from './b.js'; export * as n from './b.js';"),
        ("json-import", "import data from './json.json' with { type: 'json' }; data.a;"),
        ("dynamic-import", "const ns = await import('./a.js'); ns.x;"),
        ("import-meta", "import.meta.url; export const m = import.meta;"),
        ("top-level-await", "await Promise.resolve(1); export const v = await 2;"),
        ("export-forms", "export var v = 1; export let l = 2; export const c = 3; export function fn() {} export class K {} export { v as w, l as default };"),
        ("cycle", "import './a.js'; import './b.js'; export const self = 1;"),
        ("host-defined-module", "import { value, count, read, late } from './host.js'; import * as host from './host.js'; globalThis.r = [value, count, count, read(), late]; try { host.uninitialized; } catch {} export { count } from './host.js';"),
        ("host-defined-module-first", "import { late } from './host-user.js'; import * as host from './host.js'; Reflect.ownKeys(host); host.count;"),
        ("host-defined-module-missing-import", "import './a.js'; import { value } from './host-missing.js'; globalThis.r = value; const ns = await import('./host-missing.js');"),
        ("missing", "import './missing.js';"),
        ("bad-export", "export { notDefined };"),
        ("duplicate-export", "export const a = 1; export { a };"),
        ("defer", "import defer * as ns from './a.js'; ns.x;"),
        ("export-defer", "import { x, z, all, some, eager, filtered } from './barrel.js'; globalThis.r = [x, z, all.y, some.g(), eager, filtered.y];"),
        ("export-defer-namespace", "import * as ns from './barrel.js'; import defer * as lazy from './barrel.js'; import { x, all } as filtered from './proxy.js'; Object.keys(ns); lazy.x; filtered.all.y;"),
        ("export-defer-reexport", "export defer { x, z } from './barrel.js'; export defer * as deferred from './proxy.js'; export { again } as some from './proxy.js'; import { again } from './proxy.js';"),
        ("export-defer-dynamic", "const ns = await import('./barrel.js', { exports: ['x', 'all'] }); const lazy = await import.defer('./proxy.js', { exports: ['again'] }); ns.all.y; lazy.again;"),
    ];

    public static IReadOnlyList<(string Name, string Json)> Json { get; } =
    [
        ("json-module", "{\"a\":[1,2,3]}"),
        ("json-proto", "{\"__proto__\":{\"x\":1},\"constructor\":null,\"length\":0}"),
        ("json-numbers", "[-0,0,1e309,-1e309,5e-324,9007199254740993,1.7976931348623157e308,0.1,123456789012345678901234567890]"),
        ("json-index-keys", "{\"2\":0,\"1\":1,\"a\":2,\"0\":3,\"4294967295\":4,\"4294967294\":5,\"-0\":6,\"01\":7}"),
        ("json-duplicate-keys", "{\"a\":1,\"b\":2,\"a\":{\"c\":[]}}"),
        ("json-escapes", "\"\\u0000\\uD800\\uDC00\\uDBFF\\uDFFF\\b\\f\\n\\r\\t\\/\\\\\\\"\u2028\u2029\""),
        ("json-whitespace", " \t\r\n[ \t\r\n1 \t\r\n, \t\r\n{ \"a\" : null } ] \n"),
        ("json-bom", "\uFEFF{}"),
        ("json-deep", new string('[', 1500) + new string(']', 1500)),
        ("json-trailing-comma", "[1,]"),
        ("json-lone-surrogate", "\"\uD800\""),
    ];

    public static IReadOnlyList<(string Name, string TimeZone, string Locale)> EngineOptions { get; } =
    [
        ("options-default", "UTC", "en-US"),
        ("options-paris", "Europe/Paris", "fr-FR"),
        ("options-new-york", "America/New_York", "de"),
        ("options-extensions", "Asia/Kolkata", "ja-JP-u-ca-japanese-nu-latn"),
        ("options-script", "Pacific/Chatham", "zh-Hant-TW"),
        ("options-offset", "+05:30", "ar-EG-u-nu-arab"),
        ("options-etc", "Etc/GMT+5", "sr-Cyrl-RS"),
        ("options-link", "Asia/Calcutta", "en-GB-oed"),
        ("options-case", "europe/paris", "EN-us"),
        ("options-windows", "Romance Standard Time", "en_US"),
        ("options-empty", "", ""),
        ("options-invalid", "Invalid/Zone", "xx-invalid-!!"),
        ("options-grandfathered", "EST5EDT", "i-klingon"),
        ("options-private", "GMT", "x-private"),
        ("options-undetermined", "Australia/Lord_Howe", "und"),
        ("options-transform", "Z", "en-t-ja"),
    ];
}
