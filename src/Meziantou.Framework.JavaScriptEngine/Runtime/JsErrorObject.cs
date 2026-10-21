using System.Text;
using Meziantou.Framework.JavaScriptEngine.Interpreter;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>An error object: [[ErrorData]], and the stack trace captured when it was created (the [[Stack]] of the error-stack-accessor proposal).</summary>
internal sealed class JsErrorObject(JsObject? prototype) : JsObject(prototype, ObjectClass.Error)
{
    private const int MaxFrames = 10;

    /// <summary>Gets or sets the stack trace (the value of the stack accessor).</summary>
    public string? StackTrace { get; set; }

    /// <summary>Captures the stack trace of the running code (after the message is defined).</summary>
    public void CaptureStackTrace(JsEngine? engine)
    {
        var builder = new StringBuilder();
        builder.Append(ErrorFormatting.GetDescription(new JsValue(this)));
        if (engine is not null)
        {
            var frames = engine.Frames;
            var count = 0;
            for (var i = frames.Count - 1; i >= 0 && count < MaxFrames; i--, count++)
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

        StackTrace = builder.ToString();
    }
}
