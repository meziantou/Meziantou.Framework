---
title: CommonJS modules
description: Run code written for Node.js that uses require and module.exports, with a loader that you control.
---

The engine runs ES modules only: `require`, `module` and `exports` are not defined. They are a convention of Node.js,
which runs each file in a function that receives them. When you cannot convert a package to an ES module, your
application can provide them the same way.

## Prefer a bundle or an ES module

Convert CommonJS code before it reaches the engine when you can: a bundler such as esbuild or Rollup turns a package
and its dependencies into one ES module or one script, which you run with the [module loader](03-Modules.md) or with
`realm.Execute`. The bundle is resolved and checked once, at build time, and the engine keeps the semantics of ES
modules: static imports, live bindings, top-level `await`.

## Load CommonJS modules

To run CommonJS files as they are, implement `require` in .NET. This loader runs files given by the host, by absolute
path:

<!-- ref:../../../samples/Meziantou.Framework.JavaScriptEngine.Samples/CommonJsLoader.cs;region=JavaScriptEngine_CommonJsLoader;format=md-fenced-code-block;dedent=true -->
```csharp
/// <summary>
/// Runs CommonJS modules (<c>require</c>, <c>module.exports</c>) whose sources the host gives, by absolute path
/// (<c>/app/index.js</c>). Nothing else is reachable: the paths are resolved inside the dictionary, not on the disk.
/// </summary>
public sealed class CommonJsLoader(JsRealm realm, IReadOnlyDictionary<string, string> files)
{
    private readonly Dictionary<string, JsObject> _cache = new(StringComparer.Ordinal);

    /// <summary>Runs a module, once, and returns its <c>module.exports</c>. Call it inside a scope of the engine.</summary>
    public JsValue Require(string path)
    {
        // A module runs once: the next require calls get the same exports
        if (_cache.TryGetValue(path, out var cached))
            return cached.GetProperty("exports");

        if (!files.TryGetValue(path, out var code))
            throw realm.CreateException(JsErrorType.Error, "Cannot find module '" + path + "'");

        var engine = realm.Engine;
        var module = realm.CreateObject();
        var exports = realm.CreateObject();
        module.SetProperty("id", path);
        module.SetProperty("exports", exports);

        // The module is cached before it runs: a circular require gets the exports defined so far
        _cache[path] = module;
        try
        {
            if (path.EndsWith(".json", StringComparison.Ordinal))
            {
                var parse = realm.GlobalObject.GetProperty("JSON").AsObject().GetProperty("parse");
                module.SetProperty("exports", engine.Call(parse, JsValue.Undefined, code));
            }
            else
            {
                // The wrapper of Node.js. It starts on the first line, so the lines of the errors are those of the file.
                var wrapper = realm.Execute("(function (exports, require, module, __filename, __dirname) {" + code + "\n})", path);
                var directory = path[..path.AsSpan().LastIndexOf('/')];
                var require = realm.CreateFunction("require", 1, (_, arguments) =>
                {
                    var specifier = arguments.Length > 0 && arguments[0].IsString ? arguments[0].AsString() : "";
                    return Require(Resolve(directory, specifier));
                });

                engine.Call(wrapper, exports, exports, require, module, path, directory.Length is 0 ? "/" : directory);
            }
        }
        catch
        {
            // As in Node.js, a module that throws is not cached: the next require runs it again
            _cache.Remove(path);
            throw;
        }

        return module.GetProperty("exports");
    }

    /// <summary>Resolves a relative specifier (<c>./util</c>, <c>../data.json</c>) against the directory of a module.</summary>
    private string Resolve(string directory, string specifier)
    {
        // Only relative paths: a bare name ("lodash") would need the node_modules lookup of Node.js
        if (!specifier.StartsWith("./", StringComparison.Ordinal) && !specifier.StartsWith("../", StringComparison.Ordinal))
            throw realm.CreateException(JsErrorType.Error, "Cannot find module '" + specifier + "'");

        var segments = new List<string>(directory.Split('/', StringSplitOptions.RemoveEmptyEntries));
        foreach (var segment in specifier.Split('/'))
        {
            if (segment is ".." && segments.Count > 0)
            {
                segments.RemoveAt(segments.Count - 1);
            }
            else if (segment is not ("." or "" or ".."))
            {
                segments.Add(segment);
            }
        }

        // The file, then the extensions and the index file that Node.js tries
        var path = "/" + string.Join('/', segments);
        foreach (var candidate in (string[])[path, path + ".js", path + ".json", path + "/index.js"])
        {
            if (files.ContainsKey(candidate))
                return candidate;
        }

        return path;
    }
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_CommonJs;format=md-fenced-code-block;dedent=true -->
```csharp
var files = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["/app/index.js"] = """
        const { greet } = require('./greeting');
        const config = require('../config.json');
        module.exports = greet(config.name);
        """,
    ["/app/greeting/index.js"] = """
        const a = require('./a');
        exports.greet = name => `${a.prefix} ${name} (${a.seenB})`;
        """,
    ["/app/greeting/a.js"] = """
        exports.prefix = 'Hello';
        // A circular require gets the exports defined so far
        exports.seenB = require('./b').seenPrefix;
        """,
    ["/app/greeting/b.js"] = "exports.seenPrefix = require('./a').prefix;",
    ["/config.json"] = """{ "name": "world" }""",
};

using var engine = new JsEngine();
var realm = engine.CreateRealm();
using (engine.Enter())
{
    var loader = new CommonJsLoader(realm, files);
    Assert.Equal("Hello world (Hello)", loader.Require("/app/index.js").AsString());

    // A module runs once: the next require returns the same exports
    Assert.Same(loader.Require("/app/greeting/a.js").AsObject(), loader.Require("/app/greeting/a.js").AsObject());
}
```
<!-- endref -->

What the loader does, as Node.js does:

- **The wrapper.** Each file runs in `function (exports, require, module, __filename, __dirname)`, so its variables
  stay local and `this` is `exports`. The wrapper starts on the first line of the file: the lines of the errors and of
  the stack traces are the lines of the file, with the path as their URL.
- **The resolution.** `./` and `../` are resolved against the directory of the requiring file, then the loader tries
  the path, `.js`, `.json` and `/index.js`. A bare name (`lodash`) fails: the `node_modules` lookup and the
  `exports` field of `package.json` are not implemented.
- **The cache.** A module runs once: the next calls of `require` return the same `module.exports`. A module that
  throws is removed from the cache, so a later `require` runs it again.
- **Circular dependencies.** The module is cached before it runs. When `a.js` requires `b.js` that requires `a.js`,
  `b.js` gets the exports that `a.js` has defined so far, as in Node.js.
- **JSON files** are parsed with `JSON.parse`, without running code.

Call `Require` inside a scope of the engine (`engine.Enter()`), and call [RunJobs](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.RunJobs) after it when the modules start
asynchronous work.

## Import a CommonJS module from an ES module

An ES module can import a CommonJS module through your module loader: return a module created by
[CreateSyntheticModule](cref:Meziantou.Framework.JavaScriptEngine.JsRealm.CreateSyntheticModule) whose `default` export is the result of `Require`, as Node.js does. Its exports are a
copy taken when it is evaluated: the bindings are not live. See [Modules](03-Modules.md).

## Security

The engine reads no file: what a script can `require` is what your loader returns.

- The sample resolves paths inside a dictionary, so `../` cannot leave it. A loader that reads the disk must do the
  same: resolve each path to a full path, and reject the paths outside of the root directory of the scripts, including
  through symbolic links.
- A CommonJS module runs with the same rights as the other scripts of its realm: it shares the global object and the
  built-ins. Do not load modules that you would not run as a script, and apply the
  [limits](../07-LimitsAndSandboxing.md) of the engine to untrusted code.
- Do not give the scripts the Node.js built-in modules (`fs`, `child_process`...) through `require`: they would reach
  your application with its rights.
