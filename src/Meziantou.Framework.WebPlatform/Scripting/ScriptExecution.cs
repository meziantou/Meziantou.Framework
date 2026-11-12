using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>Runs the scripts with the <see cref="ScriptEngine"/> of the browsing contexts.</summary>
internal static class ScriptExecution
{
    /// <summary>
    /// https://html.spec.whatwg.org/multipage/webappapis.html#run-a-classic-script: runs a classic script, reports its
    /// exception, and cleans up after running the script. Returns the completion value, or null when the script cannot
    /// run or throws.
    /// </summary>
    public static object? RunClassicScript(ScriptEngine engine, ClassicScript script) => RunClassicScript(engine, script, out _);

    /// <summary>Runs a classic script; <paramref name="threw"/> is true when the script threw an exception (an abrupt completion, reported to its global object).</summary>
    public static object? RunClassicScript(ScriptEngine engine, ClassicScript script, out bool threw)
    {
        threw = false;
        var eventLoop = script.Global.EventLoop;
        // https://html.spec.whatwg.org/multipage/webappapis.html#check-if-we-can-run-script
        var window = script.Global;
        if (!window.CanRunScript)
            return null;

        object? result;

        // https://w3c.github.io/long-animation-frames/#record-classic-script-creation-time: the script is compiled and run in the same task
        if (window is Window scriptWindow && !eventLoop.HasPendingScriptEntryPoint)
        {
            eventLoop.RecordScriptBlock(scriptWindow, ScriptInvokerType.ClassicScript, script.OriginalSourceUrl ?? script.BaseUrl, script.MutedErrors);
        }

        eventLoop.RecordScriptEvaluation(window);
        var previousDocument = Document.CurrentGlobalDocumentOrNull;
        Document.CurrentGlobalDocument = window.EnvironmentDocument;

        // The exception is reported before cleaning up after running the script: the error event listeners are invoked
        // while the JavaScript execution context stack is not empty, so the microtasks they queue run once all of them return
        using (engine.PrepareToRunScript(window))
        {
            try
            {
                using (AuthorCodeScope.Enter())
                {
                    result = engine.ExecuteClassicScript(script);
                }
            }
#pragma warning disable CA1031 // The exceptions of the scripts are reported
            catch (Exception ex)
#pragma warning restore CA1031
            {
                ScriptErrors.Report(ex, window, script.MutedErrors);
                threw = true;
                result = null;
            }
            finally
            {
                Document.CurrentGlobalDocumentOrNull = previousDocument;
            }
        }

        // https://html.spec.whatwg.org/multipage/webappapis.html#clean-up-after-running-script
        if (!engine.IsScriptRunning)
        {
            eventLoop.PerformMicrotaskCheckpoint();
        }

        return result;
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/webappapis.html#run-a-module-script: runs a module script (the engine reports
    /// its errors), and cleans up after running the script.
    /// </summary>
    public static void RunModuleScript(ScriptEngine engine, ModuleScript script)
    {
        var window = script.Global;
        if (!window.CanRunScript)
            return;

        // https://w3c.github.io/long-animation-frames/#record-module-script-execution-start-time
        if (window is Window scriptWindow && !scriptWindow.EventLoop.HasPendingScriptEntryPoint)
        {
            scriptWindow.EventLoop.RecordScriptBlock(scriptWindow, ScriptInvokerType.ModuleScript, script.BaseUrl, mutedErrors: false);
        }

        window.EventLoop.RecordScriptEvaluation(window);
        var previousDocument = Document.CurrentGlobalDocumentOrNull;
        Document.CurrentGlobalDocument = window.EnvironmentDocument;
        try
        {
            using (AuthorCodeScope.Enter())
            {
                engine.ExecuteModuleScript(script);
            }
        }
#pragma warning disable CA1031 // The exceptions of the scripts are reported
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ScriptErrors.Report(ex, window);
        }
        finally
        {
            Document.CurrentGlobalDocumentOrNull = previousDocument;
        }

        if (!engine.IsScriptRunning)
        {
            window.EventLoop.PerformMicrotaskCheckpoint();
        }
    }
}
