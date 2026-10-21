using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.JavaScript;

// The compilation of strings (https://w3c.github.io/webappsec-csp/#can-compile-strings): the engine calls the
// HostEnsureCanCompileStrings hook with the code, the original arguments and the realm of eval or of the Function
// constructor, and the HostGetCodeForEval hook gives the data of a TrustedScript to eval (see JavaScriptHost).
public partial class JavaScriptScriptEngine
{
    /// <summary>
    /// HostEnsureCanCompileStrings (https://html.spec.whatwg.org/multipage/webappapis.html#hostensurecancompilestrings(realm,-parameterstrings,-bodystring,-codestring,-compilationtype,-parameterargs,-bodyarg)):
    /// the content security policies and Trusted Types of the global object of the realm.
    /// </summary>
    internal void EnsureCanCompileStrings(JsRealm engineRealm, IReadOnlyList<string> parameterStrings, string bodyString, string codeString, JsCompilationType compilationType, IReadOnlyList<JsValue> parameterArgs, JsValue bodyArg)
    {
        // A realm without global object of the library (e.g. a ShadowRealm) has no policy
        if (Agent.GetRealm(engineRealm) is not { } realm)
            return;

        var trustedParameterArgs = new TrustedScript?[parameterArgs.Count];
        for (var i = 0; i < trustedParameterArgs.Length; i++)
        {
            trustedParameterArgs[i] = AsTrustedScript(realm, parameterArgs[i]);
        }

        var type = compilationType == JsCompilationType.Function ? StringCompilationType.Function : StringCompilationType.Eval;
        try
        {
            ContentSecurityPolicyEval.EnsureCanCompileStrings(realm.GlobalScope, parameterStrings, bodyString, codeString, type, trustedParameterArgs, AsTrustedScript(realm, bodyArg));
        }
        catch (EvalErrorException ex)
        {
            throw realm.ToJsException(ex);
        }

        static TrustedScript? AsTrustedScript(WebIdlRealm<JavaScriptAdapter, object> realm, JsValue value) => value.TryGetObject() is { } obj ? realm.Unwrap(obj) as TrustedScript : null;
    }
}
