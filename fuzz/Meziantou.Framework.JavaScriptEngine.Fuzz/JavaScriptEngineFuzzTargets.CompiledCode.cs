using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.JavaScriptEngine.Fuzz;

/// <summary>
/// The compilation of the hot functions to .NET code (<see cref="JsEngineOptions.EnableCompilation"/>) does not change what
/// a script does: a script gives the same result interpreted, with every function compiled on its first call, and with the
/// functions compiled after a few calls or loop iterations (the interpreter enters the compiled code in the middle of a
/// function, and the compiled code leaves instructions to the interpreter).
/// </summary>
public static partial class JavaScriptEngineFuzzTargets
{
    private static readonly (string Name, string Code)[] CompiledCodeScripts =
    [
        ("compiled-loop", "function f() { let s = 0; for (let i = 0; i < 100; i++) { s += i % 7; s ^= i << 2; s = ~s >>> 1; } return s; } f()"),
        ("compiled-mixed-types", "function f(a, b) { return [a + b, a - b, a * b, a / b, a % b, a < b, a == b, a === b, -a, typeof a, a | b, a >> b]; } [f(1, 2), f('1', 2), f(1n, 2n), f({ valueOf() { return 3; } }, null)].join(';')"),
        ("compiled-exceptions", "function g(i) { if (i % 3 === 0) throw new Error('e' + i); return i; } function f() { let r = []; for (let i = 0; i < 10; i++) { try { r.push(g(i)); r.push(null.x); } catch (e) { r.push(e.message); } finally { r.push('f'); } } return r.join(); } f()"),
        ("compiled-calls", "class P { constructor(x) { this.x = x; } add(o) { return new P(this.x + o.x); } } function sum(...a) { return a.reduce((x, y) => x + y, 0); } let p = new P(0); for (let i = 0; i < 20; i++) { p = p.add(new P(i)); p.x += sum(...[1, 2], i) + Math.max(i, 3); } p.x"),
        ("compiled-iteration", "function f() { let r = []; for (const x of [1, 2, 3]) for (const k in { a: 1 }) r.push(x + k); const [a, ...b] = 'xyz'; return r.join() + a + b; } f() + f()"),
        ("compiled-generators", "function* g(n) { for (let i = 0; i < n; i++) { try { yield i; } finally { } } } async function a() { let s = 0; for (const v of g(5)) s += await v; return s; } [...g(4)].join() + a().then(v => v)"),
        ("compiled-closures", "function f() { const fs = []; for (let i = 0; i < 5; i++) fs.push(() => i); let c = 0; const inc = () => ++c; for (const g of fs) inc(); return fs.map(g => g()).join() + c; } f()"),
        ("compiled-tail-calls", "'use strict'; function loop(n, acc) { return n === 0 ? acc : loop(n - 1, acc + n); } loop(500, 0)"),
    ];

    // The script runs interpreted, compiled on the first call, and compiled after a few calls or iterations (the first
    // byte of the input): the outcomes must be the same
    private static void CompiledCode(ReadOnlySpan<byte> data)
    {
        var threshold = 2 + (FuzzInput.ReadByte(ref data) % 30);
        var code = FuzzText.Decode(data);
        var interpreted = RunScript(realm => realm.Execute(code, "script.js"), new JsEngineOptions { EnableCompilation = false });
        CheckSameOutcome(interpreted, RunScript(realm => realm.Execute(code, "script.js"), new JsEngineOptions { CompilationThreshold = 1 }), "The compiled code and the interpreter run the script differently");
        CheckSameOutcome(interpreted, RunScript(realm => realm.Execute(code, "script.js"), new JsEngineOptions { CompilationThreshold = threshold }), $"The script runs differently when its functions are compiled after {threshold} calls or iterations");
    }

    private static IEnumerable<FuzzSeed> GetCompiledCodeSeeds()
    {
        foreach (var (name, code) in JavaScriptSeeds.Scripts.Concat(CompiledCodeScripts))
        {
            yield return FuzzSeed.FromText(name, [5], code);
        }
    }
}
