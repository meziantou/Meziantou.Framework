namespace Meziantou.Framework.JavaScriptEngine.Test262.Runner;

/// <summary>Loads the modules of a test262 test: the specifiers are paths relative to the importing file.</summary>
internal sealed class Test262ModuleLoader(string baseDirectory) : JsModuleLoader
{
    private readonly Dictionary<(JsRealm Realm, string Path, string? Type), JsModule> _modules = [];

    /// <summary>Registers the module of the test, so that the imports of its own file give the same module.</summary>
    public void Register(JsRealm realm, string path, JsModule module) => _modules[(realm, Path.GetFullPath(path), null)] = module;

    protected internal override void LoadImportedModule(JsModuleLoadRequest request)
    {
        var directory = request.ReferrerUrl is { } url && Path.IsPathRooted(url) ? Path.GetDirectoryName(url)! : baseDirectory;
        var path = Path.GetFullPath(Path.Combine(directory, request.Specifier));
        var type = request.Type;
        var key = (request.Realm, path, type);
        if (_modules.TryGetValue(key, out var existing))
        {
            request.Complete(existing);
            return;
        }

        if (type is not null and not "json")
        {
            request.Fail("Unsupported module type '" + type + "'");
            return;
        }

        if (!File.Exists(path))
        {
            request.Fail("Cannot find the module '" + request.Specifier + "'");
            return;
        }

        JsModule module;
        try
        {
            var text = File.ReadAllText(path);
            module = type == "json" ? request.Realm.CreateJsonModule(text, path) : request.Realm.ParseModule(text, path);
        }
        catch (JsException ex)
        {
            request.Fail(ex);
            return;
        }

        _modules[key] = module;
        request.Complete(module);
    }
}
