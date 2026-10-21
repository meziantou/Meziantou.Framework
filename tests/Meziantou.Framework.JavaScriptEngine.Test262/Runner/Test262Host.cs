using System.Text;

namespace Meziantou.Framework.JavaScriptEngine.Test262.Runner;

/// <summary>The host-defined functions of test262: <c>print</c> and the <c>$262</c> object.</summary>
internal static class Test262Host
{
    public static JsObject Install(JsRealm realm, StringBuilder output)
    {
        var global = realm.GlobalObject;
        realm.DefineMethod(global, "print", 1, (_, _, arguments, _) =>
        {
            lock (output)
            {
                output.AppendLine(arguments.Length > 0 ? JsOperations.ToString(arguments[0]) : "");
            }

            return default;
        });

        var host = new JsObject(realm.Intrinsics.ObjectPrototype);
        realm.DefineMethod(host, "createRealm", 0, (_, _, _, _) =>
        {
            var newRealm = realm.Engine.CreateRealm();
            return Install(newRealm, output);
        });

        realm.DefineMethod(host, "evalScript", 1, (_, _, arguments, _) =>
        {
            var code = arguments.Length > 0 ? JsOperations.ToString(arguments[0]) : "";
            return realm.Execute(code);
        });

        realm.DefineMethod(host, "gc", 0, (_, _, _, _) =>
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            return default;
        });

        realm.DefineMethod(host, "detachArrayBuffer", 1, (_, _, _, _) => throw ThrowHelper.CreateException(ErrorType.TypeError, "detachArrayBuffer is not supported yet"));
        host.DefineBuiltIn("global", global);

        // [[IsHTMLDDA]]: typeof is "undefined", ToBoolean is false, == null, and calling it returns null
        var htmlDda = realm.CreateBuiltinFunction("IsHTMLDDA", 0, (_, _, _, _) => JsValue.Null);
        htmlDda.Flags |= ObjectFlags.IsHtmlDda;
        host.DefineBuiltIn("IsHTMLDDA", htmlDda);

        global.DefineBuiltIn("$262", host);
        return host;
    }
}
