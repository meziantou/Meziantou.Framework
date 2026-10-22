namespace Meziantou.Framework.JavaScriptEngine.Tests.Interpreter;

public sealed class InterpreterTests
{
    private static string Run(string code)
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        var result = realm.Execute(code);
        engine.RunJobs();
        using var scope = engine.Enter();
        return result.IsString ? result.AsString() : result.ToString();
    }

    [Theory]
    [InlineData("1 + 2", "3")]
    [InlineData("'a' + 1", "a1")]
    [InlineData("var x = 1; x += 2; x", "3")]
    [InlineData("let x = 2; x ** 10", "1024")]
    [InlineData("function f(a, b) { return a * b; } f(6, 7)", "42")]
    [InlineData("var f = function fact(n) { return n <= 1 ? 1 : n * fact(n - 1); }; f(10)", "3628800")]
    [InlineData("var s = 0; for (var i = 0; i < 10; i++) { s += i; } s", "45")]
    [InlineData("var s = 0; for (let i = 0; i < 10; i++) { if (i === 5) continue; if (i === 8) break; s += i; } s", "23")]
    [InlineData("var fs = []; for (let i = 0; i < 3; i++) fs.push(() => i); fs.map(f => f()).join()", "0,1,2")]
    [InlineData("var o = { a: 1, get b() { return this.a + 1; } }; o.b", "2")]
    [InlineData("var r; try { throw new Error('x'); } catch (e) { r = e.message; } finally { r += '!'; } r", "x!")]
    [InlineData("function f() { try { return 1; } finally { g = 2; } } var g; f() + g", "3")]
    [InlineData("class A { #x = 1; static s = 5; get x() { return this.#x; } } new A().x + A.s", "6")]
    [InlineData("class A { constructor(v) { this.v = v; } } class B extends A { constructor() { super(3); } m() { return this.v; } } new B().m()", "3")]
    [InlineData("var [a, , b = 3, ...c] = [1, 2, undefined, 4, 5]; a + b + c.length", "6")]
    [InlineData("var { a, b: { c }, ...rest } = { a: 1, b: { c: 2 }, d: 3, e: 4 }; a + c + Object.keys(rest).join()", "3d,e")]
    [InlineData("function* g() { yield 1; yield 2; return 3; } [...g()].join()", "1,2")]
    [InlineData("var a = []; for (var k in { x: 1, y: 2 }) a.push(k); a.join()", "x,y")]
    [InlineData("var a = []; for (const v of [1, 2, 3]) a.push(v * 2); a.join()", "2,4,6")]
    [InlineData("eval('var z = 4; z * 2')", "8")]
    [InlineData("typeof undeclared", "undefined")]
    [InlineData("`a${1 + 1}b`", "a2b")]
    [InlineData("var o = { a: { b: null } }; String(o?.a?.b?.c)", "undefined")]
    [InlineData("var x = null; x ?? 'default'", "default")]
    [InlineData("[1, 2, 3].reduce((a, b) => a + b, 0)", "6")]
    [InlineData("(function () { return arguments.length; })(1, 2, 3)", "3")]
    [InlineData("switch (3) { case 1: 'one'; break; case 3: 'three'; break; default: 'other'; }", "three")]
    [InlineData("label: for (var i = 0; i < 3; i++) { for (var j = 0; j < 3; j++) { if (j === 1) continue label; if (i === 2) break label; } } i", "2")]
    [InlineData("new Function('a', 'b', 'return a + b')(1, 2)", "3")]
    [InlineData("var r = 0; Promise.resolve(5).then(v => { globalThis.r = v; }); r", "0")]
    [InlineData("var x = []; for (var k = 0; k <= 32; k++) x[Math.pow(2, k) - 2] = k; x.length + ',' + x[4294967294] + ',' + x[2147483646]", "4294967295,32,31")]
    [InlineData("var n = 0; function f() { n++; } var r = []; for (var code of ['f() = 1', 'f() += 1', 'f()++', '--f()', 'for (f() in [1]) {}', 'for (f() of [1]) {}']) { try { eval(code); } catch (e) { r.push(e.constructor.name); } } r.length + ',' + r[0] + ',' + n", "6,ReferenceError,6")]
    [InlineData("class C extends Object { constructor() { super(); try { return 0; } catch (e) { return; } } } var r; try { new C(); } catch (e) { r = e.constructor.name; } r", "TypeError")]
    [InlineData("class C extends Object { constructor() { try { return; } catch (e) { super(); } } } var r; try { new C(); } catch (e) { r = e.constructor.name; } r", "ReferenceError")]
    [InlineData("class C extends class { } { constructor() { try { throw null; } catch (e) { return; } finally { super(); } } } typeof new C()", "object")]
    [InlineData("class C extends class { } { constructor() { var f = () => super(); try { return; } finally { f(); } } } typeof new C()", "object")]
    [InlineData("var iter = { [Symbol.iterator]() { return this; }, next() { return { done: false }; }, return() { this.f(); return { done: true }; } }; class C extends class { } { constructor() { iter.f = () => super(); for (var k of iter) { return; } } } typeof new C()", "object")]
    [InlineData("var r; (function () { eval('let f = 123; { function f() { } } r = f;'); })(); r", "123")]
    [InlineData("var r; (function () { eval('{ function f() { } } r = typeof f;'); })(); r", "function")]
    [InlineData("function f() { var r = typeof arguments; var arguments; return r + typeof arguments; } f()", "objectobject")]
    [InlineData("function f() { { function arguments() { } } return String(arguments); } f()", "[object Arguments]")]
    [InlineData("var obj = { x: 1 }; with (obj) { var x = delete obj.x; } [obj.x, x].join()", "true,")]
    [InlineData("var f = (p = eval('var y = 1'), q = () => y) => y + q(); [f(), typeof y].join()", "2,undefined")]
    [InlineData("function f(p = eval('var y = 1')) { return y; } [f(), typeof y].join()", "1,undefined")]
    [InlineData("var f = (p = eval('var y = 1'), y) => { }; var r; try { f(); } catch (e) { r = e.constructor.name; } [r, typeof y].join()", "SyntaxError,undefined")]
    [InlineData("var o = { undefined: 40 }; var n = 0; [o?.[undefined], (void 0)?.[n++], n].join()", "40,,0")]
    [InlineData("var key = { toString() { throw new Error('key'); } }; var r = []; for (var base of [null, undefined]) { try { base[key]; } catch (e) { r.push(e.constructor.name); } try { base[key] = 1; } catch (e) { r.push(e.constructor.name); } } r.join()", "TypeError,TypeError,TypeError,TypeError")]
    [InlineData("var proto = { p: 1 }; var proto2 = { p: -1 }; var obj = { __proto__: proto, m() { return ++super[key]; }, n() { super[key] += 5; return obj.p; }, g() { return super[key]; } }; var key = { toString() { Object.setPrototypeOf(obj, proto2); return 'p'; } }; var r = [obj.m()]; Object.setPrototypeOf(obj, proto); delete obj.p; r.push(obj.n()); Object.setPrototypeOf(obj, proto); r.push(obj.g()); r.join()", "2,6,1")]
    [InlineData("var obj = { m() { super.x = 8; Object.freeze(obj); super.y = 9; super['z'] = 10; return Object.keys(obj).join(); } }; obj.m()", "m,x")]
    [InlineData("var r; class C extends class { } { m() { super[(() => { throw new RangeError(); })()] = (() => { throw new Error(); })(); } } try { new C().m(); } catch (e) { r = e.constructor.name; } r", "RangeError")]
    [InlineData("var str = 'abc'; var r; try { str.slice(0, 1).foo(''); } catch (e) { r = e.message; } r", "str.slice(...).foo is not a function")]
    [InlineData("var o = { f() { return []; } }; var r; try { (o.f(')')[0])(); } catch (e) { r = e.message; } r", "o.f(...)[0] is not a function")]
    [InlineData("var o = { a: {} }; var r; try { new o.a(1); } catch (e) { r = e.message; } r", "o.a is not a constructor")]
    [InlineData("var r; try { (1 + 2).foo(); } catch (e) { r = e.message; } r", "(1 + 2).foo is not a function")]
    [InlineData("var fn, g, h; (fn) = function () { }; g = function () { }; (h) ??= () => { }; JSON.stringify([fn.name, g.name, h.name])", "[\"\",\"g\",\"\"]")]
    [InlineData("var log = []; var source = { get p() { log.push('get'); } }; var env = new Proxy({}, { has(t, key) { log.push('has ' + String(key)); return false; } }); var target; with (env) { var { p: target = 0 } = source; } log.join()", "has source,has target,get")]
    public void Evaluate(string code, string expected)
    {
        Assert.Equal(expected, Run(code));
    }

    // The inline caches of the property accesses: each script fills the cache of an access, then changes the objects
    [Theory]
    [InlineData("function get(o) { return o.x; } var p = { x: 1 }; var o = Object.create(p); var r = [get(o), get(o)]; o.x = 2; r.push(get(o)); delete o.x; r.push(get(o)); p.x = 3; r.push(get(o)); r.join()", "1,1,2,1,3")]
    [InlineData("function get(o) { return o.x; } var p = { x: 1 }; var q = { x: 2 }; var o = Object.create(p); var r = [get(o), get(o)]; Object.setPrototypeOf(o, q); r.push(get(o)); Object.setPrototypeOf(q, p); delete q.x; r.push(get(o)); r.join()", "1,1,2,1")]
    [InlineData("function get(o) { return o.x; } var p = { x: 1 }; var m = {}; Object.setPrototypeOf(m, p); var o = Object.create(m); var r = [get(o), get(o)]; m.x = 5; r.push(get(o)); Object.defineProperty(m, 'x', { get() { return 6; } }); r.push(get(o)); r.join()", "1,1,5,6")]
    [InlineData("function get(o) { return o.x; } var o = { get x() { return 1; } }; var r = [get(o), get(o)]; Object.defineProperty(o, 'x', { get() { return 2; } }); r.push(get(o)); Object.defineProperty(o, 'x', { value: 3 }); r.push(get(o)); r.join()", "1,1,2,3")]
    [InlineData("function get(o) { return o.x; } var a = { x: 1, y: 2 }; var b = { x: 3, y: 4 }; var c = { y: 5, x: 6 }; [get(a), get(b), get(c), get(a), get({}), get([])].join()", "1,3,6,1,,")]
    [InlineData("function set(o, v) { 'use strict'; o.x = v; } var o = { x: 1 }; set(o, 2); set(o, 3); Object.freeze(o); var r; try { set(o, 4); } catch (e) { r = e.constructor.name; } [o.x, r].join()", "3,TypeError")]
    [InlineData("function set(o) { o.x = 1; } var p = {}; var a = Object.create(p); var b = Object.create(p); set(a); var log = []; Object.defineProperty(p, 'x', { set(v) { log.push(v); } }); set(b); [a.x, Object.hasOwn(b, 'x'), log.join()].join()", "1,false,1")]
    [InlineData("function set(o) { 'use strict'; o.x = 1; } var p = {}; set(Object.create(p)); Object.defineProperty(p, 'x', { value: 0, writable: false }); var r; try { set(Object.create(p)); } catch (e) { r = e.constructor.name; } r", "TypeError")]
    [InlineData("function set(o) { 'use strict'; o.y = 1; } var a = { x: 0 }; var b = { x: 0 }; set(a); Object.preventExtensions(b); var r; try { set(b); } catch (e) { r = e.constructor.name; } [a.y, b.y, r].join()", "1,,TypeError")]
    [InlineData("function make(v) { return { a: v, b: v + 1 }; } var x = make(1); var y = make(2); Object.keys(y).join() + ',' + y.a + ',' + y.b + ',' + x.b", "a,b,2,3,2")]
    [InlineData("function len(a) { return a.length; } var r = [len([1, 2]), len([1, 2, 3]), len({ length: 7 }), len('abcd'), len([])]; r.join()", "2,3,7,4,0")]
    [InlineData("function get(o) { return o.x; } var o = {}; for (var i = 0; i < 100; i++) o['k' + i] = i; o.x = 'big'; var r = [get(o), get(o)]; delete o.k5; o.x = 'changed'; r.push(get(o)); r.join()", "big,big,changed")]
    [InlineData("function get(o) { return o.toString === Object.prototype.toString; } var r = [get({}), get({})]; Object.prototype.toString = function () { }; r.push(get({}), get(Object.create(null))); r.join()", "true,true,true,false")]
    [InlineData("var x = 1; function f() { return x; } var r = [f(), f()]; x = 2; r.push(f()); r.join()", "1,1,2")]
    [InlineData("function get(o) { return o.x; } var s = 0; for (var i = 0; i < 60; i++) { var o = {}; o['p' + (i % 30)] = 0; o.x = i; s += get(o); } var p = { x: 100 }; s + get(Object.create(p)) + get({ x: 1000 })", "2870")]
    [InlineData("function f() { return target; } globalThis.target = 'a'; var r = [f(), f()]; for (var i = 0; i < 40; i++) globalThis['g' + i] = i; for (var i = 0; i < 40; i++) delete globalThis['g' + i]; for (var i = 0; i < 40; i++) globalThis['h' + i] = i; globalThis.target = 'b'; r.push(f()); r.join()", "a,a,b")]
    [InlineData("function up(s) { return s.toUpperCase(); } var r = [up('a'), up('b')]; String.prototype.toUpperCase = function () { return 'X'; }; r.push(up('c')); delete String.prototype.toUpperCase; r.push(typeof 'd'.toUpperCase); r.join()", "A,B,X,undefined")]
    [InlineData("function len(s) { return s.length; } function fix(n) { return n.toFixed; } [len('ab'), len('abc'), len([1]), typeof fix(1), typeof fix(true), typeof fix(2)].join()", "2,3,1,function,undefined,function")]
    // Polymorphic sites (up to 4 receivers) and megamorphic sites (the cache of the engine): the entries are validated like a single entry
    [InlineData("function get(o) { return o.x; } var p = { x: 'p' }; var a = Object.create(p); a.a = 1; var b = Object.create(p); b.b = 1; var c = { x: 'c' }; var r = [get(a), get(b), get(c), get(a), get(b), get(c)]; p.x = 'q'; r.push(get(a), get(b), get(c)); Object.defineProperty(p, 'x', { get() { return 'g' + this.a; } }); r.push(get(a), get(b)); delete p.x; r.push(get(a)); r.join()", "p,p,c,p,p,c,q,q,c,g1,gundefined,")]
    [InlineData("class A { get v() { return 'A' + this.n; } } class B { get v() { return 'B' + this.n; } } function get(o) { return o.v; } var a = new A(); a.n = 1; var b = new B(); b.n = 2; var r = [get(a), get(b), get(a), get(b)]; Object.setPrototypeOf(b, A.prototype); r.push(get(b)); r.join()", "A1,B2,A1,B2,A2")]
    [InlineData("function get(o) { return o.x; } function make(i) { var o = Object.create(i % 3 ? p : q); o['k' + i] = i; if (i % 3 === 0) o.x = i; return o; } var p = { x: 'p' }; var q = {}; var objs = []; for (var i = 0; i < 30; i++) objs.push(make(i)); var r = [objs.map(get).join(''), objs.map(get).join('')]; p.x = 'P'; q.x = 'Q'; r.push(objs.map(get).join('')); Object.setPrototypeOf(objs[1], q); Object.defineProperty(p, 'x', { get() { return this.k2 === undefined ? '-' : '+'; } }); r.push(objs.map(get).join('')); r.join()", "0pp3pp6pp9pp12pp15pp18pp21pp24pp27pp,0pp3pp6pp9pp12pp15pp18pp21pp24pp27pp,0PP3PP6PP9PP12PP15PP18PP21PP24PP27PP,0Q+3--6--9--12--15--18--21--24--27--")]
    [InlineData("function set(o, v) { o.x = v; } function make(i) { var o = Object.create(p); o['k' + i] = i; return o; } var p = {}; var objs = []; for (var i = 0; i < 30; i++) objs.push(make(i)); objs.forEach(o => set(o, 1)); objs.forEach(o => set(o, 2)); var fresh = []; for (var i = 0; i < 30; i++) fresh.push(make(i)); fresh.slice(0, 10).forEach(o => set(o, 3)); var log = []; Object.defineProperty(p, 'x', { set(v) { log.push(v); }, configurable: true }); fresh.forEach(o => set(o, 4)); [objs.every(o => o.x === 2), fresh.filter(o => Object.hasOwn(o, 'x')).length, log.length].join()", "true,10,20")]
    [InlineData("function set(o, v) { 'use strict'; o.x = v; } var objs = []; for (var i = 0; i < 30; i++) { var o = {}; o['k' + i] = i; o.x = 0; objs.push(o); } objs.forEach(o => set(o, 1)); Object.freeze(objs[3]); var r; try { objs.forEach(o => set(o, 2)); } catch (e) { r = e.constructor.name; } [r, objs[3].x, objs[2].x, objs[4].x].join()", "TypeError,1,2,1")]
    [InlineData("function len(a) { return a.length; } var items = ['ab', [1, 2, 3], new Uint8Array(4), { length: 5 }, function (a, b) { }, 'xyz']; for (var i = 0; i < 20; i++) { var o = { length: i }; o['k' + i] = 0; items.push(o); } var s = 0; for (var j = 0; j < 3; j++) for (var x of items) s += len(x); s", "627")]
    [InlineData("function make(k) { return { [k]: 1, b: 2, c: 3 }; } var r = []; for (var i = 0; i < 20; i++) { var o = make('p' + (i % 10)); r.push(Object.keys(o).join('') + o.b + o.c); } r.slice(8, 12).join()", "p8bc23,p9bc23,p0bc23,p1bc23")]
    [InlineData("function call(o) { return o.m(); } var objs = []; for (let i = 0; i < 12; i++) { var o = {}; o['k' + i] = i; o.m = function () { return this['k' + i]; }; objs.push(o); } class C { m() { return 'c'; } } objs.push(new C()); var s = ''; for (var j = 0; j < 2; j++) for (var o of objs) s += call(o); C.prototype.m = function () { return 'C'; }; s + call(new C())", "01234567891011c01234567891011cC")]
    public void Evaluate_InlineCaches(string code, string expected)
    {
        Assert.Equal(expected, Run(code));
    }

    // The iteration of the arrays without an iterator object: the observable changes disable it
    [Theory]
    [InlineData("var a = [1, 2]; var r = []; for (var x of a) r.push(x); Array.prototype[Symbol.iterator] = function* () { yield 9; }; for (var x of a) r.push(x); r.join()", "1,2,9")]
    [InlineData("var a = [1, 2, 3]; var proto = Object.getPrototypeOf([][Symbol.iterator]()); var next = proto.next; var r = []; proto.next = function () { var v = next.call(this); if (!v.done) v.value *= 10; return v; }; for (var x of a) r.push(x); r.join()", "10,20,30")]
    [InlineData("var log = []; Object.prototype.return = function () { log.push('return'); return {}; }; var [x] = [1, 2]; delete Object.prototype.return; var [y] = [3, 4]; log.join() + x + y", "return13")]
    [InlineData("var log = ''; Object.defineProperty(Array.prototype, 1, { get() { Object.getPrototypeOf([][Symbol.iterator]()).return = function () { log += 'return,'; return {}; }; return 0; }, configurable: true }); var [x, , y] = [5, , 7, 8]; delete Array.prototype[1]; log + x + ',' + y", "return,5,7")]
    [InlineData("var a = [1, 2]; a[Symbol.iterator] = function* () { yield 'own'; }; [...a].join()", "own")]
    [InlineData("function* g() { yield* [1, 2]; } [...g()].join()", "1,2")]
    [InlineData("var it = Iterator.from([1, 2, 3]); it.next(); [...it].join()", "2,3")]
    [InlineData("var a = [1, 2]; var r = []; for (var x of a) { r.push(x); if (x === 1) a.push(3); } r.join()", "1,2,3")]
    public void Evaluate_ArrayIteration(string code, string expected)
    {
        Assert.Equal(expected, Run(code));
    }

    // The dense elements are stored by kind (numbers, references, mixed): the transitions keep the values and the holes
    [Theory]
    [InlineData("var a = [1, 2.5, NaN]; a.push('s'); a.push(undefined); a.join() + a.length", "1,2.5,NaN,s,5")]
    [InlineData("var a = ['a', null, undefined]; a.push(3); a[1] + ',' + a[2] + ',' + a[3] + ',' + a.length", "null,undefined,3,4")]
    [InlineData("var a = [1, , 3]; a.push('x'); (1 in a) + ',' + a.length + ',' + a[2] + a[3]", "false,4,3x")]
    [InlineData("var a = ['a', , 'c']; a[0] = 1; (1 in a) + ',' + a[0] + ',' + a[2]", "false,1,c")]
    [InlineData("var a = [1, 2, 3]; a[1] = 'x'; delete a[0]; (0 in a) + ',' + a.join()", "false,,x,3")]
    [InlineData("var a = [1, 2, 3, 4]; a.splice(1, 0, 9, 8); a.reverse().join()", "4,3,2,8,9,1")]
    [InlineData("var a = ['a', 'b', 'c']; a.splice(0, 1, 'x', 'y'); a.reverse().join()", "c,b,y,x")]
    [InlineData("var a = [1, 2, 3]; a.length = 1; a.push(5); a.join() + (2 in a)", "1,5false")]
    [InlineData("var f = new Float64Array(new BigUint64Array([0x7FF8DEADBEEF0001n]).buffer); var a = [1, 2]; a[1] = f[0]; (1 in a) + ',' + a[1] + ',' + a.length", "true,NaN,2")]
    [InlineData("var a = []; a[3] = 1.5; a[1] = 2; (0 in a) + ',' + a[3] + a[1] + ',' + Object.keys(a).join()", "false,1.52,1,3")]
    // The large storages come from the array pools and go back to them when they grow: a reused storage has no stale value
    [InlineData("var a = []; for (var i = 0; i < 40000; i++) a.push({ v: i }); var b = ['x']; for (var i = 1; i < 30000; i += 2) b[i] = 'y'; [a.length, a[39999].v, 0 in b, 2 in b, 29998 in b, b[29999], b.filter(x => x === 'y').length, b.length].join()", "40000,39999,true,false,false,y,15000,30000")]
    [InlineData("var a = []; for (var i = 0; i < 40000; i++) a.push(i * 1.5); var m = [0.5]; for (var i = 2; i < 30000; i += 2) m[i] = i; [a[39999], 1 in m, 29999 in m, m[29998], m.reduce((s, x) => s + x, 0), m.length].join()", "59998.5,false,false,29998,224985000.5,29999")]
    [InlineData("var a = []; for (var i = 0; i < 20000; i++) a.push(i); a.push('s'); for (var i = 0; i < 20000; i++) a.push(null); [a.length, a[19999], a[20000], a[40000], a.indexOf('s'), 40001 in a].join()", "40001,19999,s,,20000,false")]
    [InlineData("var r = new Array(50000); for (var i = 0; i < 30000; i++) r[i] = i; [r.length, 29999 in r, 30000 in r, r[49999], Object.keys(r).length].join()", "50000,true,false,,30000")]
    [InlineData("var src = Array.from({ length: 30000 }, (_, i) => i); var m = src.map(x => x % 2 ? 'o' + x : x); [m.length, m[29999], m[29998], m.filter(x => typeof x === 'string').length].join()", "30000,o29999,29998,15000")]
    [InlineData("var all = []; for (var k = 0; k < 4; k++) { var a = []; for (var i = 0; i < 25000; i++) a.push(k % 2 ? { k } : k); delete a[100]; all.push(a); } all.map(a => [a.length, 100 in a, typeof a[24999], typeof a[5]].join('/')).join()", "25000/false/number/number,25000/false/object/object,25000/false/number/number,25000/false/object/object")]
    public void Evaluate_ElementKinds(string code, string expected)
    {
        Assert.Equal(expected, Run(code));
    }

    // for-in: the keys of the shape, the prototypes without enumerable keys, the names hidden by the visited objects
    [Theory]
    [InlineData("var o = { a: 1, b: 2, c: 3 }; var r = []; for (var k in o) { r.push(k); if (k === 'a') delete o.b; } r.join()", "a,c")]
    [InlineData("var o = { a: 1, b: 2 }; var r = []; for (var k in o) { r.push(k); if (k === 'a') { o.z = 1; Object.defineProperty(o, 'b', { enumerable: false }); } } r.join()", "a")]
    [InlineData("var p = { x: 1, y: 2 }; var o = Object.create(p); o.a = 1; Object.defineProperty(o, 'x', { value: 0, enumerable: false }); var r = []; for (var k in o) r.push(k); r.join()", "a,y")]
    [InlineData("var q = { deep: 1 }; var p = Object.create(q); Object.defineProperty(p, 'deep', { value: 0, enumerable: false }); var o = Object.create(p); o.own = 1; var r = []; for (var k in o) r.push(k); r.join()", "own")]
    [InlineData("var o = { 2: 'c', 0: 'a', b: 1, [Symbol.iterator]: 0 }; var r = []; for (var k in o) r.push(k); r.join()", "0,2,b")]
    [InlineData("Object.prototype.extra = 1; var r = []; for (var k in { a: 1 }) r.push(k); delete Object.prototype.extra; for (var k in { b: 1 }) r.push(k); r.join()", "a,extra,b")]
    [InlineData("var o = {}; for (var i = 0; i < 100; i++) o['k' + i] = i; delete o.k1; var n = 0; for (var k in o) n++; n", "99")]
    public void Evaluate_ForIn(string code, string expected)
    {
        Assert.Equal(expected, Run(code));
    }

    [Fact]
    public void InlineCaches_GlobalLexicalDeclarationShadowsAGlobalProperty()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        realm.Execute("x = 1; function f() { return x; } f(); f();");
        realm.Execute("let x = 2;");
        Assert.Equal(2, realm.Execute("f()").AsNumber());
    }

    [Fact]
    public void InlineCaches_TransitionsAreNotSharedByEngines()
    {
        // The objects of an engine get the shapes of their engine (the shapes are not thread-safe), even when the cached
        // transition of the compiled code starts from a static empty shape
        var script = JsPreparedScript.Parse("function make() { var o = {}; o.a = 1; return { b: 2 }; } make(); var o1 = {}; o1.a = 1; [o1, make()]");
        using var engine1 = new JsEngine();
        using var engine2 = new JsEngine();
        var result1 = engine1.CreateRealm().Execute(script);
        var result2 = engine2.CreateRealm().Execute(script);
        using (engine1.Enter())
        {
            using (engine2.Enter())
            {
                var array1 = result1.AsObject();
                var array2 = result2.AsObject();
                for (var i = 0u; i < 2; i++)
                {
                    var map1 = array1.Get(new PropertyKey(i)).AsObject().Map;
                    var map2 = array2.Get(new PropertyKey(i)).AsObject().Map;
                    Assert.NotSame(map1, map2);
                }
            }
        }
    }

    [Fact]
    public void InlineCaches_PreparedScriptSharedByEngines()
    {
        // The caches are in the compiled code: an entry of one engine must not apply to the objects of another one
        var script = JsPreparedScript.Parse("function get(o) { return o.x; } var a = { x: 1 }; var b = Object.create({ x: 2 }); get(a) + get(a) + get(b) + get(b)");
        using var engine1 = new JsEngine();
        using var engine2 = new JsEngine();
        var realm1 = engine1.CreateRealm();
        var realm2 = engine2.CreateRealm();
        Assert.Equal(6, realm1.Execute(script).AsNumber());
        Assert.Equal(6, realm2.Execute(script).AsNumber());
        Assert.Equal(6, realm1.Execute(script).AsNumber());
    }

    [Fact]
    public void InlineCaches_MegamorphicSiteSharedByEngines()
    {
        // A megamorphic site uses the cache of the engine that runs it
        var script = JsPreparedScript.Parse("function get(o) { return o.x; } var s = 0; for (var i = 0; i < 40; i++) { var o = Object.create(proto); o['k' + (i % 20)] = i; s += get(o); } s");
        using var engine1 = new JsEngine();
        using var engine2 = new JsEngine();
        var realm1 = engine1.CreateRealm();
        var realm2 = engine2.CreateRealm();
        realm1.Execute("var proto = { x: 1 };");
        realm2.Execute("var proto = { x: 2 };");
        Assert.Equal(40, realm1.Execute(script).AsNumber());
        Assert.Equal(80, realm2.Execute(script).AsNumber());
        realm1.Execute("proto.x = 3;");
        Assert.Equal(120, realm1.Execute(script).AsNumber());
        Assert.Equal(80, realm2.Execute(script).AsNumber());
    }

    // The nested functions are compiled on their first call, in the scopes that enclose them
    [Theory]
    [InlineData("function outer(a) { let b = 2; const c = 3; var d = 4; function inner() { return a + b + c + d; } return inner(); } outer(1)", "10")]
    [InlineData("function f() { let x = 1; function g() { x++; } g(); g(); return x; } f()", "3")]
    [InlineData("function a() { var x = 1; return function b() { var y = 2; return function c() { return x + y; }; }; } a()()()", "3")]
    [InlineData("function F() { const g = () => this.v + arguments.length + (new.target === F); this.v = 1; this.r = g(); } new F(1, 2).r", "4")]
    [InlineData("function f() { return () => () => this.x; } f.call({ x: 7 })()()", "7")]
    [InlineData("class A { m() { return 1; } } class B extends A { m() { return (() => super.m() + 1)(); } } new B().m()", "2")]
    [InlineData("class A { constructor() { this.a = 1; } } class B extends A { constructor() { const f = () => super(); f(); } } new B().a", "1")]
    [InlineData("class A { x = () => this.y; y = 5; } new A().x()", "5")]
    [InlineData("class C { #x = 1; static has(o) { return #x in o; } get() { return this.#x; } } [C.has(new C()), new C().get()].join()", "true,1")]
    [InlineData("var f = function g() { g = 1; return typeof g; }; f()", "function")]
    [InlineData("var f = function g() { 'use strict'; try { g = 1; } catch (e) { return e.constructor.name; } }; f()", "TypeError")]
    [InlineData("function f() { function g() { return x; } try { g(); } catch (e) { return e.constructor.name; } let x; } f()", "ReferenceError")]
    [InlineData("function f() { const x = 1; return () => { try { x = 2; } catch (e) { return e.constructor.name; } }; } f()()", "TypeError")]
    [InlineData("function f() { var x = 5; return function () { return eval('x'); }(); } f()", "5")]
    [InlineData("function f() { eval('var y = 2'); return function () { return y; }(); } f()", "2")]
    [InlineData("function f() { var o = { x: 1 }; with (o) { return (() => x)(); } } f()", "1")]
    [InlineData("function f(a, g = () => a) { var a = 2; return g(); } f(1)", "1")]
    [InlineData("function f(a) { var g = () => arguments[0]; a = 3; return g(); } f(1)", "3")]
    [InlineData("function f(a) { 'use strict'; var g = () => arguments[0]; a = 3; return g(); } f(1)", "1")]
    [InlineData("var o = { get x() { return () => this.y; }, y: 4, m(a = 1) { return a; } }; o.x() + o.m()", "5")]
    [InlineData("var r; async function f() { r = await (async () => 1)(); } f(); var s = (async () => 2)(); typeof s.then", "function")]
    [InlineData("var r; async function f() { r = (() => 1)(); await 0; } f(); r", "1")]
    [InlineData("function* g(a) { yield a; yield* (function* () { yield a + 1; })(); } [...g(1)].join()", "1,2")]
    [InlineData("for (var f = () => 'arrow' in {}); f()", "arrow")]
    [InlineData("function f() { <!-- an HTML-like comment\n return 1; } f()", "1")]
    [InlineData("var yield = 1; function f() { return yield; } function* gen() { yield 2; } f() + gen().next().value", "3")]
    [InlineData("function* yield() { yield 1; } yield().next().value", "1")]
    [InlineData("function f() { 'use strict'; return function () { return this; }(); } String(f())", "undefined")]
    [InlineData("function f(x) { return { [x]: function () { }, x: () => { } }; } var o = f('k'); o.k.name + o.x.name", "kx")]
    [InlineData("function f() { return function g ( a ) { /* c */ }; } f().toString()", "function g ( a ) { /* c */ }")]
    [InlineData("var o = { async *m() { } }; o.m.toString()", "async *m() { }")]
    [InlineData("function f() { return class { static m() { return 1; } }; } f().m.toString() + f().m()", "m() { return 1; }1")]
    // The scopes of the lazily compiled functions: the names they declare hide the bindings of the enclosing code
    [InlineData("function f() { let i = 10; var g = () => { let i = 1; return i; }; return g() + i; } f()", "11")]
    [InlineData("function f() { var x = 'outer'; return function () { var r = typeof x; { let x = 1; } return r; }(); } f()", "string")]
    [InlineData("function f(a) { return function (b = a) { var a = 2; return b; }(); } f(1)", "1")]
    [InlineData("function f() { var e = 'outer'; return function () { try { throw 1; } catch (e) { } return e; }(); } f()", "outer")]
    [InlineData("function f() { let x = [1]; return function () { try { for (let x of x) { } } catch (e) { return e.constructor.name; } }(); } f()", "ReferenceError")]
    [InlineData("function f() { var C = 1; return class C { m() { return typeof C; } }; } new (f())().m()", "function")]
    [InlineData("function f() { var v = 'outer'; return class { static { var v = 1; } static m() { return v; } }; } f().m()", "outer")]
    [InlineData("function f() { let k = 'outer'; return function () { switch (1) { case 1: let k = 'inner'; return k; } }() + k; } f()", "innerouter")]
    [InlineData("function f() { var g = 'outer'; return function () { return (function g() { return typeof g; })() + typeof g; }(); } f()", "functionstring")]
    [InlineData("function f() { var h = 'outer'; return function () { { function h() { } } return typeof h; }(); } f()", "function")]
    [InlineData("function f(a) { return () => function () { return arguments.length; }; } f(1)()(1, 2, 3)", "3")]
    [InlineData("function f() { return () => arguments.length; } f(1, 2)()", "2")]
    public void Evaluate_LazyCompilation(string code, string expected)
    {
        Assert.Equal(expected, Run(code));
    }

    [Fact]
    public void LazyCompilation_OnlyTheFreeNamesOfTheNestedFunctionsAreCaptured()
    {
        // The bindings of outer that a nested function declares again stay in registers, and outer has no arguments object
        var source = new JavaScriptEngine.Text.SourceText("function outer(a, b) { let i = 0; var g = function (c) { let i = 1; for (let b = 0; b < 1; b++) { } return [arguments, i, a, b]; }; return g; }");
        var program = JavaScriptEngine.Parsing.JsParser.ParseScript(source, JavaScriptEngine.Parsing.ParserOptions.Script);
        var outer = ((JavaScriptEngine.Ast.FunctionDeclaration)program.Body[0]).Function;
        var analysis = JavaScriptEngine.Compiler.ScopeAnalyzer.Analyze(program, JavaScriptEngine.Compiler.ProgramKind.Script, isStrict: false, eagerFunction: outer);
        var info = analysis.Functions[outer];
        Assert.True(info.Scope.Find("a")!.IsCaptured);
        Assert.True(info.Scope.Find("b")!.IsCaptured);
        Assert.False(info.LexicalScope!.Find("i")!.IsCaptured);
        Assert.False(info.ArgumentsVariable!.IsReferenced);
    }

    [Fact]
    public void LazyCompilation_FunctionsAreCompiledOnTheirFirstCall()
    {
        var script = JsPreparedScript.Parse("function called() { return 1; } function notCalled() { return 2; } called()");
        var templates = script.Template.Objects.OfType<JavaScriptEngine.Compiler.FunctionTemplate>().ToDictionary(t => t.Name, StringComparer.Ordinal);
        Assert.False(templates["called"].IsCompiled);
        Assert.False(templates["notCalled"].IsCompiled);

        using var engine = new JsEngine();
        Assert.Equal(1, engine.CreateRealm().Execute(script).AsNumber());
        Assert.True(templates["called"].IsCompiled);
        Assert.False(templates["notCalled"].IsCompiled);
    }

    [Fact]
    public void LazyCompilation_EarlyErrorsOfFunctionsThatAreNotCalled()
    {
        Assert.False(JsPreparedScript.TryParse("function f() { let a; let a; }", "script.js", 0, 0, out _, out var error));
        Assert.Equal(1, error.Line);
        Assert.False(JsPreparedScript.TryParse("function f() { return () => { super.x; }; }", "script.js", 0, 0, out _, out _));
        Assert.False(JsPreparedScript.TryParse("class C { m() { return this.#x; } }", "script.js", 0, 0, out _, out _));
        Assert.False(JsPreparedScript.TryParse("function f() { 'use strict'; with (a) { } }", "script.js", 0, 0, out _, out _));
    }

    [Fact]
    public void LazyCompilation_LocationsOfTheErrors()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        var exception = Assert.Throws<JsException>(() => realm.Execute(JsPreparedScript.Parse("function f() {\n  return () => {\n    null.x;\n  };\n}\nf()()", "lazy.js")));
        Assert.Equal(new JsSourceLocation("lazy.js", 3, 5), exception.Location);
    }

    [Fact]
    public void LazyCompilation_PreparedScriptSharedByThreads()
    {
        // The functions are compiled once, by the first thread that calls them
        var script = JsPreparedScript.Parse("""
            function make(n) { let total = 0; return { add: v => { total += v * n; return total; }, get: function () { return total; } }; }
            class Counter { #count = 0; increment() { return ++this.#count; } }
            var counter = make(2); counter.add(1); counter.add(2);
            var c = new Counter(); c.increment();
            counter.get() + c.increment()
            """);
        var results = new double[16];
        Parallel.For(0, results.Length, i =>
        {
            using var engine = new JsEngine();
            results[i] = engine.CreateRealm().Execute(script).AsNumber();
        });

        foreach (var result in results)
        {
            Assert.Equal(8, result);
        }
    }

    [Fact]
    public void AsyncFunction()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        realm.Execute("var log = []; async function f() { log.push(1); var v = await 2; log.push(v); return 3; } f().then(v => log.push(v)); log.push(0);");
        engine.RunJobs();
        using var scope = engine.Enter();
        Assert.Equal("1,0,2,3", realm.Execute("log.join()").AsString());
    }

    [Fact]
    public void Realms_ErrorsOfClassConstructors()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        var other = engine.CreateRealm();
        using (engine.Enter())
        {
            realm.GlobalObject.CreateDataProperty(new PropertyKey("other"), new JsValue(other.GlobalObject));
        }

        // Calling a class constructor: the TypeError of the realm of the class
        Assert.Equal("true", realm.Execute("var C = other.eval('(class {})'); var r; try { C(); } catch (e) { r = e.constructor === other.TypeError; } r").ToString());

        // The result of a derived constructor: the errors of the realm of the caller
        Assert.Equal("true", realm.Execute("var C = other.eval('0, class extends Object { constructor() { return null; } }'); var r; try { new C(); } catch (e) { r = e.constructor === TypeError; } r").ToString());
        Assert.Equal("true", realm.Execute("var C = other.eval('0, class extends Object { constructor() { } }'); var r; try { new C(); } catch (e) { r = e.constructor === ReferenceError; } r").ToString());
        Assert.Equal("true", realm.Execute("var C = other.eval('0, class extends Object { constructor() { return null; } }'); var r; try { Reflect.construct(C, []); } catch (e) { r = e.constructor === TypeError; } r").ToString());
    }

    [Fact]
    public void Scripts_LexicalDeclarationOverVarOfEval()
    {
        // The vars created by eval code are configurable: a later script can declare a lexical binding of the same name
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        realm.Execute("eval('var x = 1; if (true) { function f() { } }')");
        Assert.Equal("2,3", realm.Execute("let x = 2; let f = 3; x + ',' + f").AsString());
        realm.Execute("var y;");
        var exception = Assert.Throws<JsException>(() => realm.Execute("let y;"));
        Assert.StartsWith("SyntaxError:", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AsyncGenerator_YieldStarAwaitsTheReturnValue()
    {
        // AsyncGeneratorUnwrapYieldResumption: the value of a return resumption is awaited before the inner return method is looked up
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        realm.Execute("""
            var log = [];
            var inner = { [Symbol.asyncIterator]() { return this; }, next() { return { done: false }; }, get return() { log.push('get return'); } };
            async function* f() { yield* inner; }
            var it = f();
            it.next();
            it.return({ get then() { log.push('get then'); } });
            var rejected = { [Symbol.asyncIterator]() { return this; }, next() { return { done: false }; }, throw(e) { log.push('throw ' + e); return { done: true, value: 'caught' }; } };
            async function* g() { return yield* rejected; }
            var it2 = g();
            it2.next();
            it2.return(Promise.reject('error')).then(r => log.push(r.value));
            """);
        engine.RunJobs();
        using var scope = engine.Enter();
        Assert.Equal("get then,get return,get then,throw error,caught", realm.Execute("log.join()").AsString());
    }

    [Fact]
    public void ThrowFromScript()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        var exception = Assert.Throws<JsException>(() => realm.Execute("throw new TypeError('boom')"));
        Assert.Equal("TypeError: boom", exception.Message);
    }

    private sealed class InMemoryModuleLoader(Dictionary<string, string> files) : JsModuleLoader
    {
        private readonly Dictionary<string, JsModule> _modules = new(StringComparer.Ordinal);

        protected internal override void LoadImportedModule(JsModuleLoadRequest request)
        {
            if (!_modules.TryGetValue(request.Specifier, out var module))
            {
                if (!files.TryGetValue(request.Specifier, out var code))
                {
                    request.Fail("Cannot find the module '" + request.Specifier + "'");
                    return;
                }

                module = request.Type == "json" ? request.Realm.CreateJsonModule(code, request.Specifier) : request.Realm.ParseModule(code, request.Specifier);
                _modules[request.Specifier] = module;
            }

            request.Complete(module);
        }

        public void Register(string specifier, JsModule module) => _modules[specifier] = module;
    }

    private static string RunModule(Dictionary<string, string> files, string main = "main")
    {
        var loader = new InMemoryModuleLoader(files);
        using var engine = new JsEngine(new JsEngineOptions { ModuleLoader = loader });
        var realm = engine.CreateRealm();
        var module = realm.ParseModule(files[main], main);
        loader.Register(main, module);
        var loading = module.LoadRequestedModules();
        engine.RunJobs();
        AssertFulfilled(loading);
        module.Link();
        var evaluation = module.Evaluate();
        engine.RunJobs();
        AssertFulfilled(evaluation);
        using var scope = engine.Enter();
        var result = realm.GlobalObject.Get(new PropertyKey("result"));
        return result.IsString ? result.AsString() : result.ToString();
    }

    private static void AssertFulfilled(JsValue promise)
    {
        var jsPromise = (JsPromise)promise.Cell!;
        if (jsPromise.State == PromiseState.Rejected)
            throw new JsException(jsPromise.Result);

        Assert.Equal(PromiseState.Fulfilled, jsPromise.State);
    }

    [Fact]
    public void Module_ImportDefer_EvaluatesOnFirstAccess()
    {
        var result = RunModule(new()
        {
            ["main"] = "import defer * as ns from 'dep'; globalThis.log = ['main']; const tag = ns[Symbol.toStringTag]; const value = ns.value; import.defer('dep').then(d => { globalThis.result = log.join('|') + ',' + tag + ',' + value + ',' + (d === ns) + ',' + ('then' in ns); });",
            ["dep"] = "globalThis.log.push('dep'); export const value = 42; export function then() { }",
        });

        Assert.Equal("main|dep,Deferred Module,42,true,false", result);
    }

    [Fact]
    public void Module_ImportSource_TheSourceOfAJavaScriptModuleIsNotAvailable()
    {
        var result = RunModule(new()
        {
            ["main"] = "import.source('dep').then(() => { globalThis.result = 'loaded'; }, e => { globalThis.result = e.name + ',' + globalThis.evaluated; });",
            ["dep"] = "globalThis.evaluated = true;",
        });

        Assert.Equal("SyntaxError,undefined", result);
    }

    [Fact]
    public void Module_ImportsAreLiveBindings()
    {
        var result = RunModule(new()
        {
            ["main"] = "import { count, increment } from 'counter'; import def, * as ns from 'counter'; increment(); globalThis.result = count + ',' + ns.count + ',' + def() + ',' + Object.keys(ns).join('|');",
            ["counter"] = "export let count = 0; export function increment() { count++; } export default function () { return 'd'; }",
        });

        Assert.Equal("1,1,d,count|default|increment", result);
    }

    [Fact]
    public void Module_ReexportedNamespaceIsUnambiguous()
    {
        var result = RunModule(new()
        {
            ["main"] = "export * from 'a'; export * from 'b'; import { foo } from 'main'; globalThis.result = typeof foo;",
            ["a"] = "import * as foo from 'empty'; export { foo };",
            ["b"] = "import * as foo from 'empty'; export { foo };",
            ["empty"] = "",
        });

        Assert.Equal("object", result);
    }

    [Fact]
    public void Module_TopLevelAwaitAndDynamicImport()
    {
        var result = RunModule(new()
        {
            ["main"] = "import { value } from 'tla'; const json = await import('data', { with: { type: 'json' } }); globalThis.result = value + json.default.x;",
            ["tla"] = "export const value = await Promise.resolve(40);",
            ["data"] = """{ "x": 2 }""",
        });

        Assert.Equal("42", result);
    }

    [Fact]
    public void Module_CycleCanCallHoistedFunctions()
    {
        var result = RunModule(new()
        {
            ["main"] = "import { a } from 'a'; globalThis.result = a();",
            ["a"] = "import { b } from 'b'; export function a() { return 'a' + b(); }",
            ["b"] = "import { a } from 'a'; export function b() { return 'b'; } export const fromA = typeof a;",
        });

        Assert.Equal("ab", result);
    }

    [Fact]
    public void Module_MissingExportIsASyntaxError()
    {
        var exception = Assert.Throws<JsException>(() => RunModule(new()
        {
            ["main"] = "import { missing } from 'a';",
            ["a"] = "export const x = 1;",
        }));

        Assert.StartsWith("SyntaxError:", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("(", ")")]
    [InlineData("[", "]")]
    [InlineData("{", "}")]
    [InlineData("!", "")]
    [InlineData("a = ", "")]
    [InlineData("x => ", "")]
    [InlineData("function f() { return ", "}")]
    [InlineData("[", "] = [0]")]
    [InlineData("if (a) ", "")]
    public void DeepestAcceptedNesting_RunsOnASmallStack(string open, string close)
    {
        // The deepest code that the parser accepts: the scope analysis, the code generator and the interpreter must handle
        // it on the stack of a secondary thread (1 MB on Windows), without a stack overflow
        string Code(int depth) => string.Concat(Enumerable.Repeat(open, depth)) + "0" + string.Concat(Enumerable.Repeat(close, depth));
        bool IsAccepted(int depth)
        {
            try
            {
                JavaScriptEngine.Parsing.JsParser.ParseScript(new JavaScriptEngine.Text.SourceText(Code(depth)), JavaScriptEngine.Parsing.ParserOptions.Script);
                return true;
            }
            catch (JsSyntaxErrorException)
            {
                return false;
            }
        }

        var low = 1;
        var high = 100_000;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (IsAccepted(middle))
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        Assert.True(low < 100_000);
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var engine = new JsEngine();
                engine.CreateRealm().Execute("var a = 1; " + Code(low));
            }
            catch (JsException ex)
            {
                error = ex;
            }
        }, maxStackSize: 1024 * 1024);
        thread.Start();
        thread.Join();
        Assert.True(error is null or JsException);
    }
}
