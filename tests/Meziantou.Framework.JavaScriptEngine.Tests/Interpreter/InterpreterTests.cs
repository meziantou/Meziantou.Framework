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
    public void ThrowFromScript()
    {
        using var engine = new JsEngine();
        var realm = engine.CreateRealm();
        var exception = Assert.Throws<JsException>(() => realm.Execute("throw new TypeError('boom')"));
        Assert.Equal("TypeError: boom", exception.Message);
    }
}
