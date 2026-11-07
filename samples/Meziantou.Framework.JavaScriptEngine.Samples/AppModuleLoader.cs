namespace Meziantou.Framework.JavaScriptEngine.Samples;

#region JavaScriptEngine_ModuleLoader
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
#endregion
