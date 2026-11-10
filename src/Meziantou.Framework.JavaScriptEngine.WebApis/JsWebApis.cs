using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.JavaScriptEngine.WebApis;

/// <summary>
/// Adds the common web APIs to a realm: the ones of the Minimum Common Web API of WinterTC
/// (https://min-common-api.proposal.wintertc.org/) that most scripts and npm packages use, implemented as the browsers do.
/// </summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.JavaScriptEngine.WebApis.Tests/JsWebApisTests.cs" ref:region="JsWebApis_Install" ref:dedent="true">
/// using var engine = new JsEngine();
/// var realm = engine.CreateRealm();
/// var messages = new List&lt;string&gt;();
/// var webApis = JsWebApis.Install(realm, new JsWebApisOptions
/// {
///     ConsoleMessage = message =&gt; messages.Add(message.Text),
/// });
///
/// realm.Execute("""
///     const url = new URL('/docs?page=2', 'https://example.com');
///     setTimeout(() =&gt; console.log(url.searchParams.get('page'), btoa('hi')), 10);
///     """);
///
/// // Runs the jobs and the timers until there is nothing left to run
/// await webApis.RunUntilIdleAsync();
/// Assert.Equal(["2 aGk="], messages);
/// </code>
/// </example>
public static class JsWebApis
{
    internal const string ScriptUrl = "meziantou:web-apis";

    /// <summary>
    /// Adds the web APIs enabled by <paramref name="options"/> to the global object of the realm: <c>console</c>,
    /// <c>setTimeout</c>, <c>setInterval</c>, <c>clearTimeout</c>, <c>clearInterval</c>, <c>queueMicrotask</c>,
    /// <c>structuredClone</c>, <c>URL</c>, <c>URLSearchParams</c>, <c>TextEncoder</c>, <c>TextDecoder</c>, <c>atob</c>,
    /// <c>btoa</c> and <c>DOMException</c>. Call it before running the scripts of the realm: the APIs use the built-ins of
    /// the realm as they are when they are installed.
    /// </summary>
    /// <param name="realm">The realm.</param>
    /// <param name="options">The APIs to add, or null to add all of them.</param>
    /// <returns>The host of the APIs of the realm, which runs its event loop (<see cref="JsWebApiHost.RunUntilIdleAsync"/>).</returns>
    public static JsWebApiHost Install(JsRealm realm, JsWebApisOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(realm);
        options ??= new JsWebApisOptions();

        var host = new JsWebApiHost(realm, options);
        var engine = realm.Engine;
        using var scope = engine.Enter();
        engine.RunInRealm(realm, () =>
        {
            var natives = WebApisNatives.Create(host);
            var jsOptions = realm.CreateObject();
            jsOptions.SetProperty("console", options.Console);
            jsOptions.SetProperty("timers", options.Timers);
            jsOptions.SetProperty("queueMicrotask", options.QueueMicrotask);
            jsOptions.SetProperty("structuredClone", options.StructuredClone);
            jsOptions.SetProperty("url", options.Url);
            jsOptions.SetProperty("encoding", options.Encoding);
            jsOptions.SetProperty("base64", options.Base64);
            jsOptions.SetProperty("domException", options.DomException);

            var structuredCloneFactory = options.StructuredClone ? realm.Execute(StructuredCloneScript.Source, ScriptUrl) : JsValue.Undefined;
            var bootstrap = realm.Execute(WebApisScript.Source, ScriptUrl);
            return engine.Call(bootstrap, JsValue.Undefined, natives, realm.GlobalObject, jsOptions, structuredCloneFactory);
        });

        return host;
    }
}
