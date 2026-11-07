using System.Text;

namespace Meziantou.Framework.JavaScriptEngine.Samples;

#region JavaScriptEngine_HostConsole
/// <summary>A <c>console</c> object whose methods write their arguments to a <see cref="TextWriter"/>.</summary>
public static class HostConsole
{
    /// <summary>Adds <c>console</c> to the global object of the realm. Call it inside a scope of the engine.</summary>
    public static void Install(JsRealm realm, TextWriter output, int maxMessageLength = 10_000)
    {
        var console = realm.CreateObject();
        foreach (var level in (string[])["log", "info", "warn", "error", "debug"])
        {
            console.SetProperty(level, realm.CreateFunction(level, 0, (_, arguments) =>
            {
                var message = new StringBuilder();
                foreach (var argument in arguments)
                {
                    if (message.Length > 0)
                    {
                        message.Append(' ');
                    }

                    // JsValue.ToString() runs no code of the script: an object is "[object Array]"...
                    // engine.ToString(argument) would call its toString method, which can throw or never return.
                    message.Append(argument.ToString());
                    if (message.Length > maxMessageLength)
                    {
                        message.Length = maxMessageLength;
                        message.Append('…');
                        break;
                    }
                }

                output.WriteLine($"[{level}] {message}");
                return JsValue.Undefined;
            }));
        }

        realm.GlobalObject.SetProperty("console", console);
    }
}
#endregion
