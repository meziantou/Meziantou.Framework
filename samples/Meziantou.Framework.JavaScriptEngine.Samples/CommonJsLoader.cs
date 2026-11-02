namespace Meziantou.Framework.JavaScriptEngine.Samples;

#region JavaScriptEngine_CommonJsLoader
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
#endregion
