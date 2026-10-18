using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>Reports the exceptions thrown by the scripts and the callbacks (https://html.spec.whatwg.org/multipage/webappapis.html#report-an-exception).</summary>
internal static class ScriptErrors
{
    /// <summary>
    /// Reports an exception thrown by a callback (e.g. an event listener) or by a script to <paramref name="window"/>, or
    /// to the current global object when it is null.
    /// </summary>
    public static void Report(Exception exception, GlobalScope? window = null)
    {
        EventDispatcher.ReportedExceptionCount++;
#pragma warning disable CS0618 // The obsolete event is still raised
        ExceptionReporter.Report(exception);
#pragma warning restore CS0618

        window ??= GlobalScope.Current;
        if (window is null)
            return;

        // The script of a terminated worker is aborted: the exception is not reported
        if (window is WorkerGlobalScope { Agent.IsTerminated: true })
            return;

        var engine = window.ScriptEngine;
        var error = engine is null ? new ScriptError(exception.Message, exception) : engine.ExtractErrorInformation(exception);

        // Without a script engine, no error event is fired: the error is only given to the host
        if (engine is null)
        {
            window.ReportUnhandledError(error);
            return;
        }

        window.ReportException(error);
    }

    /// <summary>Reports an exception thrown by a script whose errors may be muted (https://html.spec.whatwg.org/multipage/webappapis.html#muted-errors).</summary>
    public static void Report(Exception exception, GlobalScope window, bool mutedErrors)
    {
        if (!mutedErrors)
        {
            Report(exception, window);
            return;
        }

#pragma warning disable CS0618 // The obsolete event is still raised
        ExceptionReporter.Report(exception);
#pragma warning restore CS0618
        if (window is WorkerGlobalScope { Agent.IsTerminated: true })
            return;

        window.ReportException(ScriptError.Muted());
    }
}
