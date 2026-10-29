namespace Meziantou.Framework.JavaScriptEngine.Samples;

#region JavaScriptEngine_NoStringCompilationHost
/// <summary>Forbids <c>eval</c>, <c>new Function(...)</c> and the other ways to run code given as a string.</summary>
public sealed class NoStringCompilationHost : JsHost
{
    public override void EnsureCanCompileStrings(JsEngine engine, JsRealm realm, IReadOnlyList<string> parameterStrings, string bodyString, string codeString, JsCompilationType compilationType, IReadOnlyList<JsValue> parameterArgs, JsValue bodyArg)
    {
        throw realm.CreateException(JsErrorType.EvalError, "Code generation from strings is disabled");
    }
}
#endregion
