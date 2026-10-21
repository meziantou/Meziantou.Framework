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
    public void Evaluate(string code, string expected)
    {
        Assert.Equal(expected, Run(code));
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
}
