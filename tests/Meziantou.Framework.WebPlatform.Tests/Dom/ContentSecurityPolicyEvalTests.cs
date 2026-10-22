using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;
using Meziantou.Framework.WebPlatform.Wasmtime;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>
/// The compilation of strings and of WebAssembly modules (<c>'unsafe-eval'</c> and <c>'wasm-unsafe-eval'</c>,
/// https://w3c.github.io/webappsec-csp/#can-compile-strings and https://w3c.github.io/webappsec-csp/#can-compile-wasm-bytes).
/// </summary>
public sealed class ContentSecurityPolicyEvalTests : IDisposable
{
    private static readonly WasmtimeRuntime WebAssemblyRuntime = new();

    // Records the violations in the title of the document
    private const string ViolationListener = """
        <script>
        document.addEventListener("securitypolicyviolation", e => document.title += "[" + [e.blockedURI, e.effectiveDirective, e.disposition, e.sample].join(" ") + "]");
        </script>
        """;

    private readonly BrowsingContextScope _contexts = new();
    private readonly ScriptEngine _engine = TestScriptEngines.Create();
    private readonly List<ScriptError> _errors = [];

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    [Fact]
    public void Eval_Blocked_ThrowsEvalErrorAndReportsTheViolation()
    {
        var document = Load(ViolationListener + """
            <script>
            var ran = false;
            try { eval("ran = true"); } catch (e) { document.title += e.constructor.name + (e instanceof EvalError) + ";"; }
            try { (0, eval)("ran = true"); } catch (e) { document.title += e.constructor.name + ";"; }
            document.title += ran + ";";
            </script>
            """, "script-src 'unsafe-inline' 'report-sample'");

        Assert.Equal("EvalErrortrue;EvalError;false;[eval script-src enforce ran = true][eval script-src enforce ran = true]", document.Title);
    }

    [Fact]
    public void Eval_Violation_LocationIsTheCallee()
    {
        var document = Load("""
            <script>
            document.addEventListener("securitypolicyviolation", e => document.title += e.sourceFile + ":" + e.lineNumber + ":" + e.columnNumber + ";");
            function f() {
              return 1;
            }
            f(
              2);
            try {
                eval("x");
            } catch { }
            try {
                new Function("x");
            } catch { }
            </script>
            """, "script-src 'unsafe-inline'");

        Assert.Equal("https://example.com/:9:5;https://example.com/:12:9;", document.Title);
    }

    [Theory]
    [InlineData("https://user:pass@example.org/a.js?query#fragment", "https://example.org/a.js")]
    [InlineData("wss://example.org/a.js?query", "wss://example.org/a.js")]
    [InlineData("ws://example.org/a.js", "ws://example.org/a.js")]
    [InlineData("webpack://example/a.js", "webpack")]
    public void Eval_Violation_SourceFileIsStrippedWithoutItsQuery(string sourceUrl, string expected)
    {
        // The trusted code compiled by eval() has a sourceURL comment, and its call of eval() with a string is a violation
        var document = Load($$"""
            <script>
            document.addEventListener("securitypolicyviolation", e => document.title += e.sourceFile + ";");
            const policy = trustedTypes.createPolicy("p", { createScript: x => x });
            try { eval(policy.createScript("eval('1');\n//# sourceURL={{sourceUrl}}")); } catch { }
            </script>
            """, "require-trusted-types-for 'script'");

        Assert.Equal(expected + ";", document.Title);
    }

    [Fact]
    public void Eval_WithoutReportSample_HasNoSample()
    {
        var document = Load(ViolationListener + """<script>try { eval("1"); } catch { }</script>""", "default-src 'unsafe-inline'");
        Assert.Equal("[eval script-src enforce ]", document.Title);
    }

    [Fact]
    public void Eval_ReportOnly_RunsAndReportsTheViolation()
    {
        var document = Load(ViolationListener + """<script>document.title += eval("1 + 1") + ";";</script>""", "script-src 'unsafe-inline'", reportOnly: true);
        Assert.Equal("2;[eval script-src report ]", document.Title);
    }

    [Theory]
    [InlineData("script-src 'unsafe-inline' 'unsafe-eval'")]
    [InlineData("script-src 'unsafe-inline' 'UNSAFE-EVAL'")]
    [InlineData("img-src 'none'")]
    public void Eval_Allowed(string policy)
    {
        var document = Load(ViolationListener + """<script>document.title += [eval("1 + 1"), new Function("a", "b", "return a + b")(1, 2)].join(",") + ";";</script>""", policy);
        Assert.Equal("2,3;", document.Title);
    }

    [Fact]
    public void Eval_Hooked_DirectEvalKeepsItsSemantics()
    {
        var document = Load("""
            <script>
            var x = "global";
            function direct() { var x = "local"; return eval("x"); }
            function indirect() { var x = "local"; return (0, eval)("x"); }
            function member() { var x = "local"; return window.eval("x"); }
            function declares() { eval("var y = 1"); return typeof y; }
            function strict() { "use strict"; eval("var z = 1"); return typeof z; }
            function args() { return eval("arguments.length"); }
            function thisValue() { return eval("this.value"); }
            document.title = [direct(), indirect(), member(), declares(), strict(), args(1, 2, 3), thisValue.call({ value: "this" }), eval(1), eval === window.eval, eval.name, eval.length].join(",");
            </script>
            """, "script-src 'unsafe-inline' 'unsafe-eval'");

        Assert.Equal("local,global,global,number,undefined,3,this,1,true,eval,1", document.Title);
    }

    [Fact]
    public void FunctionConstructors_Blocked()
    {
        var document = Load(ViolationListener + """
            <script>
            const constructors = [Function, (async function () {}).constructor, (function* () {}).constructor, (async function* () {}).constructor];
            for (const constructor of constructors) {
              try { new constructor("a", "return a"); document.title += "created;"; } catch (e) { document.title += constructor.name + ":" + e.constructor.name + ";"; }
            }
            try { Function("return 1"); } catch (e) { document.title += "call:" + e.constructor.name + ";"; }
            </script>
            """, "script-src 'unsafe-inline' 'report-sample'");

        Assert.Equal(
            "Function:EvalError;AsyncFunction:EvalError;GeneratorFunction:EvalError;AsyncGeneratorFunction:EvalError;call:EvalError;"
            + "[eval script-src enforce function anonymous(a ) { return a }]"
            + "[eval script-src enforce async function anonymous(a ) { return a ]"
            + "[eval script-src enforce function* anonymous(a ) { return a }]"
            + "[eval script-src enforce async function* anonymous(a ) { return a]"
            + "[eval script-src enforce function anonymous( ) { return 1 }]",
            document.Title);
    }

    [Fact]
    public void FunctionConstructors_Hooked_KeepTheirIdentity()
    {
        var document = Load("""
            <script>
            const AsyncFunction = (async function () {}).constructor;
            class Derived extends Function {}
            const derived = new Derived("return 42");
            let converted = 0;
            const f = new Function({ toString() { converted++; return "a"; } }, "return a * 2");
            document.title = [
              Function.prototype.constructor === Function,
              (function () {}).constructor === Function,
              Object.getPrototypeOf(AsyncFunction) === Function,
              new Function("return 1") instanceof Function,
              Function.name, Function.length, AsyncFunction.name,
              derived instanceof Derived, derived(),
              f(2), converted,
              Function.prototype.toString.call(Function).includes("[native code]"),
            ].join(",");
            </script>
            """, "script-src 'unsafe-inline' 'unsafe-eval'");

        Assert.Equal("true,true,true,true,Function,1,AsyncFunction,true,42,4,1,true", document.Title);
    }

    [Fact]
    public void Timer_StringHandler_BlockedAndReported()
    {
        var document = Load(ViolationListener + """
            <script>
            window.onerror = (message, source, line, column, error) => { document.title += "error:" + error.constructor.name + ";"; return true; };
            var ran = false;
            var id = setTimeout("ran = true", 0);
            setTimeout(() => document.title += "id:" + id + ",ran:" + ran + ";", 0);
            </script>
            """, "script-src 'unsafe-inline' 'report-sample'");

        // The string is checked when the timer is created, as in the browsers: no timer is created (the method returns 0),
        // the violation is reported and no exception is thrown nor reported
        Assert.DoesNotContain("error:", document.Title, StringComparison.Ordinal);
        Assert.Contains("id:0,ran:false;", document.Title, StringComparison.Ordinal);
        Assert.Contains("[eval script-src enforce ran = true]", document.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void TrustedTypes_EvalOfTrustedScript()
    {
        var document = Load(ViolationListener + """
            <script>
            const policy = trustedTypes.createPolicy("p", { createScript: s => s });
            document.title += eval(policy.createScript("1 + 1")) + ";";
            document.title += new Function(policy.createScript("a"), policy.createScript("return a * 3"))(2) + ";";
            try { eval("1"); } catch (e) { document.title += e.constructor.name + ";"; }
            </script>
            """, "require-trusted-types-for 'script'; script-src 'unsafe-inline' 'unsafe-eval'");

        Assert.Equal("2;6;EvalError;[trusted-types-sink require-trusted-types-for enforce eval|1]", document.Title);
    }

    [Fact]
    public void TrustedTypes_DefaultPolicy()
    {
        var document = Load("""
            <script>
            let log = "";
            trustedTypes.createPolicy("default", { createScript: (s, type, sink) => { log += sink + ";"; return s.includes("bad") ? null : s; } });
            const result = eval("1 + 1");
            log += result + ";";
            try { eval("bad"); } catch (e) { log += e.constructor.name + ";"; }
            document.title = log;
            </script>
            """, "require-trusted-types-for 'script'; script-src 'unsafe-inline' 'unsafe-eval'");

        Assert.Equal("eval;2;eval;EvalError;", document.Title);
    }

    [Theory]
    [InlineData("script-src 'unsafe-inline' 'trusted-types-eval'; require-trusted-types-for 'script'", "2;")]
    [InlineData("script-src 'unsafe-inline' 'trusted-types-eval'", "EvalError;[eval script-src enforce ]")]
    public void TrustedTypesEval(string policy, string expected)
    {
        var document = Load(ViolationListener + """
            <script>
            trustedTypes.createPolicy("default", { createScript: s => s });
            try { document.title += eval("1 + 1") + ";"; } catch (e) { document.title += e.constructor.name + ";"; }
            </script>
            """, policy);

        Assert.Equal(expected, document.Title);
    }

    [Theory]
    [InlineData("script-src 'unsafe-inline'", "CompileError;[wasm-eval script-src enforce ][wasm-eval script-src enforce ]CompileError;")]
    [InlineData("default-src 'unsafe-inline'", "CompileError;[wasm-eval script-src enforce ][wasm-eval script-src enforce ]CompileError;")]
    [InlineData("script-src 'unsafe-inline' 'wasm-unsafe-eval'", "Module;Module;")]
    [InlineData("script-src 'unsafe-inline' 'unsafe-eval'", "Module;Module;")]
    public async Task WebAssembly(string policy, string expected)
    {
        var document = Load(ViolationListener + """
            <script>
            const bytes = new Uint8Array([0, 0x61, 0x73, 0x6d, 1, 0, 0, 0]);
            try { document.title += new WebAssembly.Module(bytes).constructor.name + ";"; } catch (e) { document.title += e.constructor.name + ";"; }
            WebAssembly.compile(bytes).then(m => document.title += m.constructor.name + ";", e => document.title += e.constructor.name + ";");
            </script>
            """, policy);

        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(expected, document.Title);
    }

    private Document Load(string html, string policy, bool reportOnly = false)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            WebAssemblyRuntime = WebAssemblyRuntime,
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });

        var document = context.Navigate("https://example.com/", _ => new DocumentResource(html) { Headers = [new("Content-Type", "text/html"), new(reportOnly ? "Content-Security-Policy-Report-Only" : "Content-Security-Policy", policy)] });
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_errors);
        return document;
    }
}
