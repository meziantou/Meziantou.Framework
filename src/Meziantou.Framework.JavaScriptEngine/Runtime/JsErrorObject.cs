using System.Text;
using Meziantou.Framework.JavaScriptEngine.Interpreter;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>An error object: [[ErrorData]], and the stack trace captured when it was created (the [[Stack]] of the error-stack-accessor proposal).</summary>
internal sealed class JsErrorObject(JsObject? prototype) : JsObject(prototype, ObjectClass.Error)
{
    /// <summary>Gets or sets the stack trace (the value of the stack accessor).</summary>
    public string? StackTrace { get; set; }

    /// <summary>Captures the stack trace of the running code (after the message is defined).</summary>
    public void CaptureStackTrace(JsEngine? engine)
    {
        StackTrace = FormatStackTrace(ErrorFormatting.GetDescription(new JsValue(this)), engine, ErrorStackTraceApi.GetStackTraceLimit(engine?.CurrentRealm), skipUntil: null);
    }

    /// <summary>Gets whether an object has an [[ErrorData]] internal slot: an error object, or a host object that has one.</summary>
    public static bool HasErrorData(JsObject obj) => obj is JsErrorObject or JsHostObject { HasErrorData: true };

    /// <summary>Gets the stack trace of an object that has an [[ErrorData]] internal slot, or null.</summary>
    public static string? GetStackTrace(JsObject obj) => obj switch
    {
        JsErrorObject error => error.StackTrace,
        JsHostObject { HasErrorData: true } host => host.ErrorStackTrace,
        _ => null,
    };

    /// <summary>Sets the stack trace of an object that has an [[ErrorData]] internal slot; returns false for the other objects.</summary>
    public static bool TrySetStackTrace(JsObject obj, string stack)
    {
        switch (obj)
        {
            case JsErrorObject error:
                error.StackTrace = stack;
                return true;
            case JsHostObject { HasErrorData: true } host:
                host.ErrorStackTrace = stack;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Formats the stack trace of the running code: <paramref name="header"/>, then at most
    /// <paramref name="maxFrames"/> frames, the innermost first. When <paramref name="skipUntil"/> is not null, the frames above
    /// its innermost call, and this call, are omitted (no frame when it is not on the stack).
    /// </summary>
    public static string FormatStackTrace(string header, JsEngine? engine, int maxFrames, JsObject? skipUntil)
    {
        var builder = new StringBuilder();
        builder.Append(header);
        if (engine is not null)
        {
            var frames = engine.Frames;
            var start = frames.Count - 1;
            if (skipUntil is not null)
            {
                while (start >= 0 && !ReferenceEquals(frames[start].Function, skipUntil))
                {
                    start--;
                }

                start--;
            }

            var count = 0;
            for (var i = start; i >= 0 && count < maxFrames; i--, count++)
            {
                var frame = frames[i];
                var pc = i == frames.Count - 1 ? frame.Pc : Math.Max(0, frame.Pc - 1);
                var offset = frame.Code.GetSourceOffset(pc);
                var (line, column) = frame.Code.Source.GetLineAndColumn(Math.Max(0, offset));
                var location = (frame.Code.Source.Url ?? "<anonymous>") + ":" + line.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + column.ToString(System.Globalization.CultureInfo.InvariantCulture);
                builder.Append("\n    at ");
                var name = frame.Function?.InitialName;
                if (string.IsNullOrEmpty(name))
                {
                    builder.Append(location);
                }
                else
                {
                    builder.Append(name).Append(" (").Append(location).Append(')');
                }
            }
        }

        return builder.ToString();
    }
}
