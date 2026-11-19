namespace Meziantou.Framework.JavaScriptEngine.Samples;

#region JavaScriptEngine_HostDefinedModule
/// <summary>
/// Loads the JavaScript files of a dictionary, and the "app:counter" module implemented in .NET: it imports the step of
/// './config.js', and its count export is a live binding.
/// </summary>
public sealed class CounterModuleLoader(IReadOnlyDictionary<string, string> files) : JsModuleLoader
{
    // The same specifier must give the same module each time
    private readonly Dictionary<string, JsModule> _modules = [];

    protected override void LoadImportedModule(JsModuleLoadRequest request)
    {
        if (!_modules.TryGetValue(request.Specifier, out var module))
        {
            if (request.Specifier is "app:counter")
            {
                module = CreateCounterModule(request.Realm);
            }
            else if (files.TryGetValue(request.Specifier, out var code))
            {
                module = request.Realm.ParseModule(code, request.Specifier);
            }
            else
            {
                request.Fail("Cannot find the module '" + request.Specifier + "'");
                return;
            }

            _modules[request.Specifier] = module;
        }

        request.Complete(module);
    }

    private static JsHostDefinedModule CreateCounterModule(JsRealm realm)
    {
        var count = 0.0;

        // The modules it requests are evaluated before it: the evaluation steps read their exports
        return realm.CreateHostDefinedModule(["./config.js"], ["count", "increment"], module =>
        {
            var step = module.GetImport("./config.js", "step").GetValue().AsNumber();
            module.SetExportGetter("count", () => count); // the getter runs on each access to the binding
            module.SetExport("increment", realm.CreateFunction("increment", 0, (_, _) =>
            {
                count += step;
                return JsValue.Undefined;
            }));
        }, "app:counter");
    }
}
#endregion
