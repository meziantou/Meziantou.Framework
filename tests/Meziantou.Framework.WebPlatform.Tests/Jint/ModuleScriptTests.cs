using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Jint;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Jint;

/// <summary>Module scripts, import maps and import() (https://html.spec.whatwg.org/multipage/webappapis.html#module-script).</summary>
public sealed class ModuleScriptTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly RecordingLoader _loader = new();
    private readonly JintScriptEngine _engine = new();

    public void Dispose()
    {
        _contexts.Dispose();
        _engine.Dispose();
    }

    private Document Load(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            DocumentLoader = _loader,
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });

        var document = context.Navigate("https://example.com/dir/page.html", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private void AddScript(string url, string source, params KeyValuePair<string, string>[] headers)
        => _loader.Resources[url] = new DocumentResource(source) { Headers = [new("Content-Type", "text/javascript"), .. headers] };

    [Fact]
    public void ModuleScripts_AreDeferredAndImportTheirDependencies()
    {
        AddScript("https://example.com/dir/main.js", "import { value } from './lib.js'; window.log.push('main ' + value + ' ' + import.meta.url);");
        AddScript("https://example.com/lib/lib.js", "export const value = 42;");
        AddScript("https://example.com/dir/lib.js", "export { value } from '../lib/lib.js';");
        var document = Load("""
            <script>window.log = [];</script>
            <script type=module src=main.js></script>
            <script type=module>window.log.push('inline ' + (document.currentScript === null));</script>
            <script>window.log.push('classic');</script>
            <script>document.addEventListener('DOMContentLoaded', () => document.title = window.log.join('|'));</script>
            """);

        Assert.Equal("classic|main 42 https://example.com/dir/main.js|inline true", document.Title);
        Assert.Empty(_errors);
        Assert.All(_loader.Requests.Where(r => r.Url.EndsWith(".js", StringComparison.Ordinal)), request => Assert.Equal(RequestMode.Cors, request.Mode));
    }

    [Fact]
    public void ImportMap_MapsTheBareSpecifiers()
    {
        AddScript("https://example.com/vendor/lib.js", "export default 'mapped';");
        var document = Load("""
            <script type=importmap>{ "imports": { "lib": "/vendor/lib.js", "pkg/": "/vendor/" } }</script>
            <script type=module>
              import value from 'lib';
              import again from 'pkg/lib.js';
              document.title = value + ' ' + (value === again) + ' ' + import.meta.resolve('lib');
            </script>
            """);

        Assert.Equal("mapped true https://example.com/vendor/lib.js", document.Title);
        Assert.Empty(_errors);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ImportMap_Integrity_IsCheckedForTheImports(bool matching)
    {
        const string Source = "export default 'checked';";
        var integrity = matching ? "sha384-" + Convert.ToBase64String(System.Security.Cryptography.SHA384.HashData(System.Text.Encoding.UTF8.GetBytes(Source))) : "sha384-AAAA";
        AddScript("https://example.com/dir/lib.js", Source);
        var document = Load($$"""
            <script type=importmap>{ "integrity": { "./lib.js": "{{integrity}}" } }</script>
            <script>import('./lib.js').then(module => document.title = module.default, error => document.title = error.name);</script>
            """);

        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(matching ? "checked" : "TypeError", document.Title);
        Assert.Equal(integrity, _loader.Requests.Single(request => request.Url.EndsWith("lib.js", StringComparison.Ordinal)).Integrity);
    }

    [Fact]
    public void IntegrityAttribute_IsCheckedForAModuleScript()
    {
        AddScript("https://example.com/dir/main.js", "document.title = 'executed';");
        var document = Load("""
            <script type=module src=main.js integrity="sha384-AAAA" onerror="document.title = 'error'"></script>
            """);

        Assert.Equal("error", document.Title);
    }

    [Fact]
    public void ImportMap_CannotChangeTheSpecifiersAlreadyResolved()
    {
        AddScript("https://example.com/dir/a.js", "export default 'a';");
        AddScript("https://example.com/dir/b.js", "export default 'b';");
        var document = Load("""
            <script type=module>import './a.js';</script>
            <script type=importmap>{ "imports": { "./a.js": "./b.js", "https://example.com/dir/": "/other/", "new": "./b.js" } }</script>
            <script type=module>
              document.title = [import.meta.resolve('./a.js'), import.meta.resolve('new')].join(' ');
            </script>
            """);

        Assert.Equal("https://example.com/dir/a.js https://example.com/dir/b.js", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void Module_IsEvaluatedOncePerWindow()
    {
        AddScript("https://example.com/dir/count.js", "window.count = (window.count ?? 0) + 1;");
        AddScript("https://example.com/dir/main.js", "import './count.js';");
        _loader.Resources["https://example.com/dir/frame.html"] = new DocumentResource("""
            <script type=module>import './count.js'; import './main.js';</script>
            <script>import('./count.js').then(() => parent.document.title += ' frame:' + window.count);</script>
            """) { Headers = [new("Content-Type", "text/html")] };
        var document = Load("""
            <script type=module src=main.js></script>
            <script type=module>import './count.js'; document.title = 'top:' + window.count;</script>
            <iframe src=frame.html></iframe>
            """);

        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("top:1 frame:1", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void DynamicImport_FromAClassicScript()
    {
        AddScript("https://example.com/dir/lib.js", "export const value = 'dynamic';");
        _loader.Resources["https://example.com/dir/data.json"] = new DocumentResource("{ \"a\": 1 }") { Headers = [new("Content-Type", "application/json")] };
        var document = Load("""
            <script>
              Promise.all([import('./lib.js'), import('./data.json', { with: { type: 'json' } }), import('./missing.js').catch(e => e.name)])
                .then(([module, json, error]) => document.title = [module.value, json.default.a, error].join());
            </script>
            """);

        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("dynamic,1,TypeError", document.Title);
    }

    [Fact]
    public void TextModule_ExportsTheSourceTextDecodedAsUtf8_WhateverItsMimeType()
    {
        _loader.Resources["https://example.com/dir/data.json"] = new DocumentResource(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'a', (byte)' ', 0xC3, 0xA9 }) { Headers = [new("Content-Type", "application/json; charset=windows-1252")] };
        var document = Load("""
            <script type=module>
              import text from './data.json' with { type: 'text' };
              document.title = typeof text + ' ' + text;
            </script>
            """);

        Assert.Equal("string a \u00e9", document.Title);
        Assert.Equal(RequestDestination.Text, _loader.Requests.Single(r => r.Url.EndsWith(".json", StringComparison.Ordinal)).Destination);
        Assert.Empty(_errors);
    }

    [Fact]
    public void DynamicImport_OfAModuleThatThrows_IsNotAnUnhandledRejection()
    {
        AddScript("https://example.com/dir/throw.js", "throw new Error('evaluation');");
        var document = Load("""
            <script>
              window.log = [];
              window.addEventListener('unhandledrejection', () => log.push('unhandled'));
              import('./throw.js').catch(e => log.push(e.message))
                .then(() => import('./throw.js')).catch(e => log.push(e.message))
                .then(() => setTimeout(() => document.title = log.join()));
            </script>
            """);

        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("evaluation,evaluation", document.Title);
    }

    [Fact]
    public void DynamicImport_InAnEventHandler_ResolvesAgainstTheDocumentBaseUrl()
    {
        AddScript("https://example.com/dir/sub/setter.js", "const d = document.getElementById('d'); d.setAttribute('onclick', \"import('./lib.js').then(m => document.title = m.value)\"); d.click();");
        AddScript("https://example.com/dir/lib.js", "export const value = 'document';");
        AddScript("https://example.com/dir/sub/lib.js", "export const value = 'script';");
        var document = Load("""
            <div id=d></div>
            <script src=sub/setter.js></script>
            """);

        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("document", document.Title);
    }

    [Fact]
    public void FetchFailure_FiresErrorEvent_ParseError_IsReported()
    {
        AddScript("https://example.com/dir/invalid.js", "export const = ;");
        var document = Load("""
            <script>window.log = [];</script>
            <script type=module src=missing.js onerror="log.push('error')"></script>
            <script type=module src=invalid.js onerror="log.push('invalid error')"></script>
            <script>window.addEventListener('load', () => document.title = log.join());</script>
            """);

        Assert.Equal("error", document.Title);
        var error = Assert.Single(_errors);
        Assert.Equal("SyntaxError", ((global::Jint.Native.JsValue)error.Error!).Get("name").ToString());
        Assert.Equal("https://example.com/dir/invalid.js", error.Filename);
    }

    [Fact]
    public void ExternalModuleScripts_ShareTheModuleMap()
    {
        AddScript("https://example.com/dir/lib.js", "window.log.push('lib');");
        AddScript("https://example.com/dir/main.js", "import './lib.js'; window.log.push('main');");
        var document = Load("""
            <script>window.log = [];</script>
            <script type=module src=main.js></script>
            <script type=module src=lib.js></script>
            <script type=module src=main.js></script>
            <script>window.addEventListener('load', () => document.title = log.join());</script>
            """);

        Assert.Equal("lib,main", document.Title);
        Assert.Single(_loader.Requests, request => request.Url == "https://example.com/dir/lib.js");
        Assert.Single(_loader.Requests, request => request.Url == "https://example.com/dir/main.js");
    }

    [Fact]
    public void UnsupportedImportAttribute_IsASyntaxError()
    {
        AddScript("https://example.com/dir/lib.js", "export default 1;");
        Load("""
            <script type=module>import './lib.js' with { unknown: 'value' };</script>
            """);

        var error = Assert.Single(_errors);
        Assert.Equal("SyntaxError", ((global::Jint.Native.JsValue)error.Error!).Get("name").ToString());
    }

    [Fact]
    public void ParseErrorOfADependency_IsLocatedInTheDependency()
    {
        _loader.Resources["https://example.com/dir/data.json"] = new DocumentResource("{ invalid") { Headers = [new("Content-Type", "application/json")] };
        Load("""
            <script type=module>import data from './data.json' with { type: 'json' };</script>
            """);

        var error = Assert.Single(_errors);
        Assert.Equal("SyntaxError", ((global::Jint.Native.JsValue)error.Error!).Get("name").ToString());
        Assert.Equal("https://example.com/dir/data.json", error.Filename);
    }

    [Fact]
    public void InlineModuleErrors_AreLocatedInTheDocument()
    {
        Load("""
            <p>line 1</p>
            <script type=module>
              undefinedVariable;
            </script>
            <script type=module>  let = ;</script>
            """);

        Assert.Equal(2, _errors.Count);
        Assert.Equal(("https://example.com/dir/page.html", 3), (_errors[0].Filename, _errors[0].LineNumber));
        Assert.Equal(("https://example.com/dir/page.html", 5), (_errors[1].Filename, _errors[1].LineNumber));
    }

    [Fact]
    public void CrossOriginModule_NeedsCors()
    {
        AddScript("https://other.example/allowed.js", "window.log.push('allowed');", new KeyValuePair<string, string>("Access-Control-Allow-Origin", "*"));
        AddScript("https://other.example/denied.js", "window.log.push('denied');");
        var document = Load("""
            <script>window.log = [];</script>
            <script type=module src=https://other.example/allowed.js></script>
            <script type=module src=https://other.example/denied.js onerror="log.push('cors error')"></script>
            <script>window.addEventListener('load', () => document.title = log.join());</script>
            """);

        Assert.Equal("allowed,cors error", document.Title);
    }

    [Fact]
    public void FailedFetch_IsNotKeptInTheModuleMap()
    {
        var document = Load("""
            <script>
              window.retry = () => import('./later.js').then(module => document.title = module.value, error => document.title = error.name);
              retry();
            </script>
            """);

        Assert.Equal("TypeError", document.Title);
        AddScript("https://example.com/dir/later.js", "export const value = 'fetched again';");
        _engine.Evaluate(document.DefaultView!, "retry()");
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("fetched again", document.Title);
    }

    [Fact]
    public void Descendants_AreFetchedWithTheReferrerAndTheReferrerPolicyOfTheirParent()
    {
        AddScript("https://example.com/dir/main.js", "import './dep.js'; import 'https://other.example/dep.js';", new KeyValuePair<string, string>("Referrer-Policy", "origin"));
        AddScript("https://example.com/dir/plain.js", "import './dep2.js';");
        AddScript("https://example.com/dir/dep.js", "");
        AddScript("https://example.com/dir/dep2.js", "");
        AddScript("https://other.example/dep.js", "", new KeyValuePair<string, string>("Access-Control-Allow-Origin", "*"));
        Load("<script type=module src=main.js></script><script type=module src=plain.js></script>");

        Assert.Equal("https://example.com/", _loader.Requests.Single(r => r.Url == "https://example.com/dir/dep.js").Referrer);
        Assert.Equal("https://example.com/", _loader.Requests.Single(r => r.Url == "https://other.example/dep.js").Referrer);
        Assert.Equal("https://example.com/dir/plain.js", _loader.Requests.Single(r => r.Url == "https://example.com/dir/dep2.js").Referrer);
        Assert.Equal("https://example.com/dir/page.html", _loader.Requests.Single(r => r.Url == "https://example.com/dir/plain.js").Referrer);
    }
}
