using Meziantou.Framework.JavaScriptEngine;

namespace Meziantou.Framework.WebPlatform.Playwright.Server;

/// <summary>
/// The scripts of Playwright that run in the pages (see <c>Scripts/README.md</c>): the injected script (the selector engines,
/// the actionability checks, the assertions, the aria snapshots) and the utility script (the serialization of the values of
/// <c>evaluate()</c>). They run in the main world of the pages, after a snapshot of the built-ins taken before the scripts of
/// the page run (as Playwright does for the browsers without utility world).
/// </summary>
internal static class PlaywrightScripts
{
    // The built-ins of the main world used by the scripts (snapshottedFunctionBuiltins, snapshottedObjectBuiltins and
    // snapshottedTimerBuiltins of javascript.ts)
    private static readonly string[] FunctionBuiltins = ["Node", "Element", "NodeFilter", "HTMLElement", "Document", "ShadowRoot", "MutationObserver", "Event", "CustomEvent", "EventTarget", "Map", "Set", "WeakMap", "WeakSet", "Promise", "Symbol", "Error", "TypeError", "RegExp", "Array", "Object"];
    private static readonly string[] ObjectBuiltins = ["JSON", "Math"];
    private static readonly string[] TimerBuiltins = ["setTimeout"];

    private static readonly Lazy<JsPreparedScript> InjectedScriptFactory = new(() => JsPreparedScript.Parse(
        "(function (options) {\n" + MainWorldGlobalsSnapshotSource + "\nconst module = {};\n" + ReadResource("injectedScriptSource.js") + "\nreturn new (module.exports.InjectedScript())(globalThis, options);\n})",
        "playwright://injected-script.js"));

    private static readonly Lazy<JsPreparedScript> UtilityScriptFactory = new(() => JsPreparedScript.Parse(
        "(function () {\n" + MainWorldGlobalsSnapshotSource + "\nconst module = {};\n" + ReadResource("utilityScriptSource.js") + "\nreturn new (module.exports.UtilityScript())(globalThis, false);\n})",
        "playwright://utility-script.js"));

    private static readonly Lazy<JsPreparedScript> SaveGlobalsSnapshotScript = new(() => JsPreparedScript.Parse(SaveGlobalsSnapshotSource, "playwright://globals-snapshot.js"));

    /// <summary>Gets the script whose completion value is the factory of the injected script (called with its options).</summary>
    public static JsPreparedScript InjectedScript => InjectedScriptFactory.Value;

    /// <summary>Gets the script whose completion value is the factory of the utility script.</summary>
    public static JsPreparedScript UtilityScript => UtilityScriptFactory.Value;

    /// <summary>Gets the script that saves the built-ins of a window before the scripts of the page run (an init script of each document).</summary>
    public static JsPreparedScript SaveGlobalsSnapshot => SaveGlobalsSnapshotScript.Value;

    private static string SaveGlobalsSnapshotSource => "window.__pwSnapshotGlobals = {\n" + string.Join(",\n", FunctionBuiltins.Concat(ObjectBuiltins).Concat(TimerBuiltins).Select(name => "  " + name + ": window." + name)) + "\n};";

    private static string MainWorldGlobalsSnapshotSource =>
        "const __snap = globalThis.__pwSnapshotGlobals || {};\n"
        + string.Join('\n', FunctionBuiltins.Select(name => $"const {name} = (typeof globalThis.{name} === 'function' ? globalThis.{name} : __snap.{name});")) + "\n"
        + string.Join('\n', ObjectBuiltins.Select(name => $"const {name} = (typeof globalThis.{name} === 'object' && globalThis.{name} ? globalThis.{name} : __snap.{name});")) + "\n";

    private static string ReadResource(string name)
    {
        using var stream = typeof(PlaywrightScripts).Assembly.GetManifestResourceStream("Meziantou.Framework.WebPlatform.Playwright.Scripts." + name)
            ?? throw new InvalidOperationException($"The resource '{name}' is missing");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
