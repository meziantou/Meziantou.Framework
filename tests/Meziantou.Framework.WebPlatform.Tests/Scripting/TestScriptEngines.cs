using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.JavaScript;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Scripting;

/// <summary>
/// Creates the script engine of the tests (<see cref="JavaScriptScriptEngine"/>). The helpers hide the values of the engine:
/// the tests only use the strings and the numbers they return.
/// </summary>
/// <remarks>This file is linked into the other test projects that run scripts.</remarks>
internal static class TestScriptEngines
{
    /// <summary>Creates a script engine. Dispose it with <see cref="Dispose(ScriptEngine)"/>.</summary>
    /// <param name="options">The options, or null for the default options.</param>
    public static ScriptEngine Create(TestScriptEngineOptions? options = null) => new JavaScriptScriptEngine(ToJavaScriptOptions(options));

    /// <summary>Creates a script engine that can be used in a <c>using</c> declaration, and passed where a <see cref="ScriptEngine"/> is expected.</summary>
    /// <param name="options">The options, or null for the default options.</param>
    public static DisposableScriptEngine CreateDisposable(TestScriptEngineOptions? options = null) => new(Create(options));

    /// <summary>Disposes a script engine created by <see cref="Create(TestScriptEngineOptions?)"/>.</summary>
    public static void Dispose(ScriptEngine engine) => (engine as IDisposable)?.Dispose();

    /// <summary>
    /// Evaluates code in the realm of a window, as an indirect <c>eval</c>. The microtasks are not run: call
    /// <see cref="DomEventLoop.RunUntilIdle"/> to run them.
    /// </summary>
    /// <returns>The result converted to a string, or null when the result is <c>undefined</c> or <c>null</c>.</returns>
    public static string? Evaluate(ScriptEngine engine, Window window, string code)
    {
        var javaScriptEngine = (JavaScriptScriptEngine)engine;
        var result = javaScriptEngine.Evaluate(window, code);
        if (result.IsNullOrUndefined)
        {
            return null;
        }

        using var scope = javaScriptEngine.Engine.Enter();
        return javaScriptEngine.Engine.ToString(result);
    }

    /// <summary>Evaluates code in the realm of a window and returns the result, that must be a number.</summary>
    public static double EvaluateNumber(ScriptEngine engine, Window window, string code) => ((JavaScriptScriptEngine)engine).Evaluate(window, code).AsNumber();

    /// <summary>Gets the <c>name</c> of the exception value of a script error (<c>SyntaxError</c>, <c>TypeError</c>...).</summary>
    public static string GetErrorName(ScriptError error) => ((JsObject)error.Error!).GetProperty("name").ToString();

    private static JavaScriptScriptEngineOptions ToJavaScriptOptions(TestScriptEngineOptions? options)
    {
        var defaults = new JavaScriptScriptEngineOptions();
        if (options is null)
        {
            return defaults;
        }

        return new JavaScriptScriptEngineOptions
        {
            Timeout = options.Timeout ?? defaults.Timeout,
            MemoryLimit = options.MemoryLimit ?? defaults.MemoryLimit,
            MaxStatements = options.MaxStatements ?? defaults.MaxStatements,
            RecursionLimit = options.RecursionLimit ?? defaults.RecursionLimit,
        };
    }
}

/// <summary>The settings of a test script engine. A null value is the default of the engine.</summary>
internal sealed record TestScriptEngineOptions
{
    /// <summary>Gets the maximum duration of an entry into JavaScript (a script, an event listener, a timer callback...).</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Gets the maximum number of bytes allocated during an entry into JavaScript.</summary>
    public long? MemoryLimit { get; init; }

    /// <summary>Gets the maximum number of statements of an entry into JavaScript.</summary>
    public int? MaxStatements { get; init; }

    /// <summary>Gets the maximum depth of the JavaScript calls.</summary>
    public int? RecursionLimit { get; init; }
}

/// <summary>A script engine of the tests that is disposed with a <c>using</c> declaration, and converts to a <see cref="ScriptEngine"/>.</summary>
internal sealed class DisposableScriptEngine : IDisposable
{
    public DisposableScriptEngine(ScriptEngine engine) => Engine = engine;

    public ScriptEngine Engine { get; }

    public static implicit operator ScriptEngine(DisposableScriptEngine engine) => engine.Engine;

    public ScriptEngine ToScriptEngine() => Engine;

    public void Dispose() => TestScriptEngines.Dispose(Engine);
}
