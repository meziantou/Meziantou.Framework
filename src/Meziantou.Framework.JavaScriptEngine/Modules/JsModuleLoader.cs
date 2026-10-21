using Meziantou.Framework.JavaScriptEngine.Interpreter;
using Meziantou.Framework.JavaScriptEngine.Modules;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// Loads the modules imported by the scripts and the modules (HostLoadImportedModule,
/// https://tc39.es/ecma262/#sec-HostLoadImportedModule). Set it in <see cref="JsEngineOptions.ModuleLoader"/>.
/// </summary>
public abstract class JsModuleLoader
{
    /// <summary>
    /// Loads a module. The loader completes the request with <see cref="JsModuleLoadRequest.Complete"/> or
    /// <see cref="JsModuleLoadRequest.Fail(JsValue)"/>, synchronously or later (e.g. once a file is downloaded). The
    /// same referrer and specifier must give the same module each time.
    /// </summary>
    protected internal abstract void LoadImportedModule(JsModuleLoadRequest request);

    /// <summary>Adds the properties of the <c>import.meta</c> object of a module (HostGetImportMetaProperties and HostFinalizeImportMeta).</summary>
    protected internal virtual void InitializeImportMeta(JsSourceTextModule module, JsObject importMeta)
    {
    }
}

/// <summary>A request to load a module (the arguments of HostLoadImportedModule).</summary>
public sealed class JsModuleLoadRequest
{
    private readonly IScriptOrModule? _referrer;
    private readonly object _payload;
    private bool _completed;

    internal JsModuleLoadRequest(JsRealm realm, JsRealm currentRealm, IScriptOrModule? referrer, ModuleRequest request, object? hostDefined, object payload)
    {
        Realm = realm;
        CurrentRealm = currentRealm;
        _referrer = referrer;
        Request = request;
        HostDefined = hostDefined;
        _payload = payload;
    }

    internal ModuleRequest Request { get; }

    /// <summary>Gets the realm of the referrer: the realm of the module to create.</summary>
    public JsRealm Realm { get; }

    /// <summary>
    /// Gets the current realm when the module is requested: the realm of the code that calls <c>import()</c>, which is not
    /// the realm of the referrer when the code runs in another realm (e.g. the code of another window evaluated by its
    /// <c>eval</c>); the realm of the importing module for a static import.
    /// </summary>
    public JsRealm CurrentRealm { get; }

    /// <summary>Gets the module that imports the module, if the referrer is a module.</summary>
    public JsModule? ReferrerModule => _referrer as JsModule;

    /// <summary>Gets the URL of the script or the module that imports the module, if any.</summary>
    public string? ReferrerUrl => _referrer?.Url;

    /// <summary>Gets the data of the host associated with the referrer.</summary>
    public object? ReferrerHostDefined => _referrer?.HostDefined;

    /// <summary>Gets the module specifier.</summary>
    public string Specifier => Request.Specifier;

    /// <summary>Gets the import attributes (sorted by key).</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Attributes => Request.Attributes;

    /// <summary>Gets the value of the <c>type</c> import attribute (e.g. <c>"json"</c>), or null.</summary>
    public string? Type => Request.Type;

    /// <summary>Gets the value given to <see cref="JsModule.LoadRequestedModules"/>, or null for a dynamic import.</summary>
    public object? HostDefined { get; }

    /// <summary>Gets whether the request comes from an <c>import()</c> call.</summary>
    public bool IsDynamicImport => _payload is DynamicImportPayload;

    /// <summary>Completes the request with the loaded module (FinishLoadingImportedModule).</summary>
    public void Complete(JsModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        Finish(module, default);
    }

    /// <summary>Fails the request: the error is the rejection reason of the loading.</summary>
    public void Fail(JsValue error) => Finish(module: null, error);

    /// <summary>Fails the request with the value of a JavaScript exception (e.g. the SyntaxError of an invalid module).</summary>
    public void Fail(JsException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Fail(exception.Value);
    }

    /// <summary>Fails the request with a TypeError of the realm.</summary>
    public void Fail(string message)
    {
        using var scope = Realm.Engine.Enter();
        Fail(Realm.CreateError(ErrorType.TypeError, message));
    }

    private void Finish(JsModule? module, JsValue error)
    {
        if (_completed)
            throw new InvalidOperationException("The module load request is already completed");

        _completed = true;
        using var scope = Realm.Engine.Enter();
        ModuleOperations.FinishLoadingImportedModule(_referrer, Request, _payload, module, error);
    }
}
