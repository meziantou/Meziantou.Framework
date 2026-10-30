---
title: Modules
description: Write a module loader, run an ES module and its imports, load JSON, text and .NET modules, and use dynamic import.
---

## Write a module loader

The engine does not read files: the modules that a script or a module imports are given by the module loader of the
engine, a class that derives from [JsModuleLoader](cref:Meziantou.Framework.JavaScriptEngine.JsModuleLoader). This loader serves the files of a dictionary, JSON and text files
for the `type` import attribute, and a module implemented in .NET:

<!-- ref:../../../samples/Meziantou.Framework.JavaScriptEngine.Samples/AppModuleLoader.cs;region=JavaScriptEngine_ModuleLoader;format=md-fenced-code-block;dedent=true -->
```csharp
/// <summary>
/// Loads the modules of an application: the JavaScript, JSON and text files of a dictionary, and the "app:log" module
/// implemented in .NET.
/// </summary>
public sealed class AppModuleLoader(IReadOnlyDictionary<string, string> files, ICollection<string> logs) : JsModuleLoader
{
    // The same specifier must give the same module each time
    private readonly Dictionary<(string Specifier, string? Type), JsModule> _modules = [];

    protected override void LoadImportedModule(JsModuleLoadRequest request)
    {
        var key = (request.Specifier, request.Type);
        if (!_modules.TryGetValue(key, out var module))
        {
            try
            {
                module = CreateModule(request);
            }
            catch (JsException exception)
            {
                // e.g. a SyntaxError in the module or in the JSON file
                request.Fail(exception);
                return;
            }

            if (module is null)
            {
                request.Fail("Cannot find the module '" + request.Specifier + "'");
                return;
            }

            _modules[key] = module;
        }

        request.Complete(module);
    }

    protected override void InitializeImportMeta(JsSourceTextModule module, JsObject importMeta)
    {
        importMeta.SetProperty("url", module.Url ?? "");
    }

    private JsModule? CreateModule(JsModuleLoadRequest request)
    {
        var realm = request.Realm;
        if (request.Specifier is "app:log")
        {
            // A module whose exports are .NET functions
            return realm.CreateSyntheticModule(["log"], module => module.SetExport("log", realm.CreateFunction("log", 1, (_, arguments) =>
            {
                logs.Add(realm.Engine.ToString(arguments.Length > 0 ? arguments[0] : JsValue.Undefined));
                return JsValue.Undefined;
            })), request.Specifier);
        }

        if (!files.TryGetValue(request.Specifier, out var content))
            return null;

        // The type import attribute: import config from './config.json' with { type: 'json' }
        return request.Type switch
        {
            null => realm.ParseModule(content, request.Specifier),
            "json" => realm.CreateJsonModule(content, request.Specifier),
            "text" => realm.CreateTextModule(content, request.Specifier),
            _ => null,
        };
    }
}
```
<!-- endref -->

- [LoadImportedModule](cref:Meziantou.Framework.JavaScriptEngine.JsModuleLoader.LoadImportedModule) receives each request: its [Specifier](cref:Meziantou.Framework.JavaScriptEngine.JsModuleLoadRequest.Specifier) (`'./format.js'`), its [Type](cref:Meziantou.Framework.JavaScriptEngine.JsModuleLoadRequest.Type) (the `type` import
  attribute, or null), its [Realm](cref:Meziantou.Framework.JavaScriptEngine.JsModuleLoadRequest.Realm) (where to create the module), and the module or script that imports it
  ([ReferrerModule](cref:Meziantou.Framework.JavaScriptEngine.JsModuleLoadRequest.ReferrerModule), [ReferrerUrl](cref:Meziantou.Framework.JavaScriptEngine.JsModuleLoadRequest.ReferrerUrl)). Resolve relative specifiers against [ReferrerUrl](cref:Meziantou.Framework.JavaScriptEngine.JsModuleLoadRequest.ReferrerUrl) when your modules are in folders.
- Complete the request with [Complete](cref:Meziantou.Framework.JavaScriptEngine.JsModuleLoadRequest.Complete), or fail it with [Fail](cref:Meziantou.Framework.JavaScriptEngine.JsModuleLoadRequest.Fail). A loader can complete a request later, for instance
  once a file is downloaded.
- The same specifier from the same module must give the same module object each time: keep the modules you created.
- `realm.ParseModule` creates a JavaScript module; [CreateJsonModule](cref:Meziantou.Framework.JavaScriptEngine.JsRealm.CreateJsonModule), [CreateTextModule](cref:Meziantou.Framework.JavaScriptEngine.JsRealm.CreateTextModule) and [CreateBytesModule](cref:Meziantou.Framework.JavaScriptEngine.JsRealm.CreateBytesModule)
  create the modules of the `json`, `text` and `bytes` types; [CreateSyntheticModule](cref:Meziantou.Framework.JavaScriptEngine.JsRealm.CreateSyntheticModule) creates a module whose exports
  are set from .NET.
- [InitializeImportMeta](cref:Meziantou.Framework.JavaScriptEngine.JsModuleLoader.InitializeImportMeta) fills the `import.meta` object of each JavaScript module.

## Run a module

A module is loaded (with its dependencies), linked, then evaluated. Set the module loader in the options of the
engine:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_LoadModules;format=md-fenced-code-block;dedent=true -->
```csharp
var files = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["main.js"] = """
        import { log } from 'app:log';
        import { formatPrice } from './format.js';
        import config from './config.json' with { type: 'json' };

        log(`${import.meta.url}: ${formatPrice(config.price, config.currency)}`);
        export const ready = true;
        """,
    ["./format.js"] = "export function formatPrice(amount, currency) { return `${amount.toFixed(2)} ${currency}`; }",
    ["./config.json"] = """{ "price": 12.5, "currency": "EUR" }""",
};

var logs = new List<string>();
using var engine = new JsEngine(new JsEngineOptions { ModuleLoader = new AppModuleLoader(files, logs) });
var realm = engine.CreateRealm();

// Load the module and its dependencies, link them, then evaluate them
var module = realm.ParseModule(files["main.js"], "main.js");
module.LoadRequestedModules();
engine.RunJobs();
module.Link();
var evaluation = module.Evaluate().AsObject();
engine.RunJobs();

Assert.True(evaluation.TryGetPromiseState(out var state, out _, out _));
Assert.Equal(JsPromiseState.Fulfilled, state);
Assert.Equal(["main.js: 12.50 EUR"], logs);

// The exports of the module
using (engine.Enter())
{
    var exports = module.GetNamespace().AsObject();
    Assert.True(exports.GetProperty("ready").AsBoolean());
}
```
<!-- endref -->

- [LoadRequestedModules](cref:Meziantou.Framework.JavaScriptEngine.JsModule.LoadRequestedModules) asks the loader for every module of the graph. It returns a promise, settled from [RunJobs](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.RunJobs).
- [Link](cref:Meziantou.Framework.JavaScriptEngine.JsModule.Link) resolves the imports. It throws a [JsException](cref:Meziantou.Framework.JavaScriptEngine.JsException) (a `SyntaxError`) when a module does not export a name that
  another one imports.
- [Evaluate](cref:Meziantou.Framework.JavaScriptEngine.JsModule.Evaluate) runs the modules and returns a promise. A module that uses top-level `await` completes from [RunJobs](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.RunJobs).
- [GetNamespace](cref:Meziantou.Framework.JavaScriptEngine.JsModule.GetNamespace) returns the namespace object of a linked module: its exports are its properties, and the default
  export is the `default` property.
- [Status](cref:Meziantou.Framework.JavaScriptEngine.JsModule.Status) tells where the module is ([Unlinked](cref:Meziantou.Framework.JavaScriptEngine.JsModuleStatus.Unlinked), [Linked](cref:Meziantou.Framework.JavaScriptEngine.JsModuleStatus.Linked), [Evaluated](cref:Meziantou.Framework.JavaScriptEngine.JsModuleStatus.Evaluated)...).

A module without imports needs no loader: parse it and run the same steps.

## Handle the errors of a module

The errors of the loading and of the evaluation reject the promises:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_ModuleErrors;format=md-fenced-code-block;dedent=true -->
```csharp
var files = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["main.js"] = "import { missing } from './missing.js';",
};

using var engine = new JsEngine(new JsEngineOptions { ModuleLoader = new AppModuleLoader(files, []) });
var realm = engine.CreateRealm();
var module = realm.ParseModule(files["main.js"], "main.js");

// A module that cannot be loaded rejects the promise of LoadRequestedModules
var loading = module.LoadRequestedModules().AsObject();
engine.RunJobs();
Assert.True(loading.TryGetPromiseState(out var state, out var reason, out _));
Assert.Equal(JsPromiseState.Rejected, state);
Assert.Equal("TypeError: Cannot find the module './missing.js'", engine.ToString(reason));
```
<!-- endref -->

- `Fail(string)` rejects the loading with a `TypeError`; `Fail(JsException)` rejects it with the error of the
  exception, such as the `SyntaxError` of a module that is not valid.
- When the evaluation throws, `module.HasEvaluationError` is true, [EvaluationError](cref:Meziantou.Framework.JavaScriptEngine.JsModule.EvaluationError) is the thrown value and
  [EvaluationErrorLocation](cref:Meziantou.Framework.JavaScriptEngine.JsModule.EvaluationErrorLocation) is where it was thrown.
- `realm.ParseModule` itself throws a [JsException](cref:Meziantou.Framework.JavaScriptEngine.JsException) (a `SyntaxError`) for a module that is not valid.

## Import modules from scripts

A script can import a module with `import()`. The request goes to the same loader, with [IsDynamicImport](cref:Meziantou.Framework.JavaScriptEngine.JsModuleLoadRequest.IsDynamicImport) set, and the
module loads from the job queue:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_DynamicImport;format=md-fenced-code-block;dedent=true -->
```csharp
var files = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["./plugin.js"] = "export default function run(input) { return input.toUpperCase(); }",
};

using var engine = new JsEngine(new JsEngineOptions { ModuleLoader = new AppModuleLoader(files, []) });
var realm = engine.CreateRealm();

// A script can import a module with import(): the loading runs from the job queue
realm.Execute("import('./plugin.js').then(plugin => { globalThis.result = plugin.default('done'); });", "app.js");
engine.RunJobs();

Assert.Equal("DONE", realm.Execute("result").AsString());
```
<!-- endref -->

Without a module loader, the scripts and the modules cannot import modules.

## Source phase imports

`import source x from '...'` and `import.source()` give the *module source* of a module, set by the loader in
`module.ModuleSource` before it completes the request. The object is a [JsHostObject](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject) that extends
`realm.AbstractModuleSource` and whose [ModuleSourceClassName](cref:Meziantou.Framework.JavaScriptEngine.JsHostObject.ModuleSourceClassName) is set (for instance `WebAssembly.Module`). The source
phase import of a module without module source, such as a JavaScript module, throws a `SyntaxError`.
