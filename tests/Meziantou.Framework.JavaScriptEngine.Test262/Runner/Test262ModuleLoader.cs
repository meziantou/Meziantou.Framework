namespace Meziantou.Framework.JavaScriptEngine.Test262.Runner;

/// <summary>Loads the modules of a test262 test: the specifiers are paths relative to the importing file.</summary>
internal sealed class Test262ModuleLoader(string baseDirectory) : JsModuleLoader
{
    /// <summary>The specifier of a module that has a module source (INTERPRETING.md, the source-phase-imports-module-source feature).</summary>
    private const string ModuleSourceSpecifier = "<module source>";

    private readonly Dictionary<(JsRealm Realm, string Path, string? Type), JsModule> _modules = [];
    private readonly Dictionary<JsRealm, JsModule> _moduleSourceModules = [];

    /// <summary>Registers the module of the test, so that the imports of its own file give the same module.</summary>
    public void Register(JsRealm realm, string path, JsModule module) => _modules[(realm, Path.GetFullPath(path), null)] = module;

    protected internal override void LoadImportedModule(JsModuleLoadRequest request)
    {
        if (request.Specifier == ModuleSourceSpecifier && request.Type is null)
        {
            request.Complete(GetModuleSourceModule(request.Realm));
            return;
        }

        var directory = request.ReferrerUrl is { } url && Path.IsPathRooted(url) ? Path.GetDirectoryName(url)! : baseDirectory;
        var path = Path.GetFullPath(Path.Combine(directory, request.Specifier));
        var type = request.Type;
        var key = (request.Realm, path, type);
        if (_modules.TryGetValue(key, out var existing))
        {
            request.Complete(existing);
            return;
        }

        if (type is not null and not ("json" or "text" or "bytes"))
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
            module = type switch
            {
                "json" => request.Realm.CreateJsonModule(File.ReadAllText(path), path),
                "text" => request.Realm.CreateTextModule(File.ReadAllText(path), path),
                "bytes" => request.Realm.CreateBytesModule(File.ReadAllBytes(path), path),
                _ => request.Realm.ParseModule(File.ReadAllText(path), path),
            };
        }
        catch (JsException ex)
        {
            request.Fail(ex);
            return;
        }

        _modules[key] = module;
        request.Complete(module);
    }

    /// <summary>
    /// The module of <c>&lt;module source&gt;</c> in a realm (the same record for each import): a module without exports whose
    /// module source is an instance of a class that extends %AbstractModuleSource%, as a WebAssembly.Module.
    /// </summary>
    private JsModule GetModuleSourceModule(JsRealm realm)
    {
        if (!_moduleSourceModules.TryGetValue(realm, out var module))
        {
            var abstractModuleSourcePrototype = realm.AbstractModuleSource.GetProperty("prototype").AsObject();
            module = realm.CreateSyntheticModule([], _ => { }, ModuleSourceSpecifier);
            module.ModuleSource = new JsHostObject(realm.CreateObject(abstractModuleSourcePrototype)) { ModuleSourceClassName = "Test262ModuleSource" };
            _moduleSourceModules[realm] = module;
        }

        return module;
    }
}
