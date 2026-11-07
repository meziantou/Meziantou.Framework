namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    private JsFunction? _abstractModuleSource;

    /// <summary>%AbstractModuleSource%, created on first use: it is not a global, so its creation is not observable.</summary>
    public JsFunction AbstractModuleSource => _abstractModuleSource ??= ModuleSourceBuiltins.Initialize(this);
}
