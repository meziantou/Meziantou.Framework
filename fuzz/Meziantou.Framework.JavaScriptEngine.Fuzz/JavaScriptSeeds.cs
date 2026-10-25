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
        ("missing", "import './missing.js';"),
        ("bad-export", "export { notDefined };"),
        ("duplicate-export", "export const a = 1; export { a };"),
        ("defer", "import defer * as ns from './a.js'; ns.x;"),
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
