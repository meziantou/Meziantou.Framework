using Meziantou.Framework.WebPlatform.DevTools.Targets;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.DevTools.Browser;

// The callbacks of the browsing contexts of the pages (they run on the thread of the context)
internal sealed partial class BrowserContextHost
{
    private void OnBrowsingContextCreated(CallbackContext callbackContext)
    {
        if (_closed || callbackContext.BrowsingContext is not { } browsingContext)
            return;

        if (browsingContext.Parent is { } parent)
        {
            if (FindFrame(parent) is { } parentFrame)
            {
                parentFrame.Target.AttachFrame(browsingContext, parentFrame);
            }

            return;
        }

        if (browsingContext.IsAuxiliary)
        {
            var opener = FindFrame(browsingContext.CreatorBrowsingContext ?? browsingContext.Opener);
            var page = new PageTarget(this, browsingContext, opener?.Target, pendingUrl: null);
            AddPage(page);
            opener?.Target.OnWindowOpen(page);

            // As Chromium, the popup is attached at once. When the client waits for the new targets, the popup waits for it
            // (its requests wait, see ContextLoader), so that the scripts and the interception it sets up apply to the first
            // document of the popup.
            if (!Browser.AddTarget(page))
            {
                page.Resume();
            }
        }
    }

    private void OnBrowsingContextDestroyed(CallbackContext callbackContext)
    {
        if (FindFrame(callbackContext.BrowsingContext) is not { } frame)
            return;

        if (frame.ParentFrame is null)
        {
            frame.Target.CloseFromContext();
        }
        else
        {
            frame.Target.DetachFrame(frame);
        }
    }

    /// <summary>A document became the active document of a browsing context (the hook of the script engine, before the scripts of the document run).</summary>
    internal void OnDocumentActivated(Document document)
    {
        if (FindFrame(document.DefaultView?.BrowsingContext) is { } frame)
        {
            using var scope = ScriptEngine.Engine.Enter();
            frame.Target.OnDocumentCommitted(frame, document);
        }
    }

    // A document restored from the back/forward cache is activated without the hook of the script engine
    private void OnDocumentActivatedCallback(CallbackContext callbackContext, Document document)
    {
        if (FindFrame(callbackContext.BrowsingContext) is { } frame && !ReferenceEquals(frame.Document, document))
        {
            using var scope = ScriptEngine.Engine.Enter();
            frame.Target.OnDocumentCommitted(frame, document);
        }
    }

    private void OnLifecycleEventFired(Document document, string name)
    {
        if (FindFrame(document.BrowsingContext) is { } frame)
        {
            frame.Target.OnDocumentLifecycleEvent(frame, document, name);
        }
    }

    private void OnSameDocumentNavigated(CallbackContext callbackContext)
    {
        if (FindFrame(callbackContext.BrowsingContext) is { } frame)
        {
            frame.Target.OnSameDocumentNavigated(frame);
        }
    }

    private void OnConsoleMessage(CallbackContext callbackContext, ConsoleMessage message)
    {
        if (FindFrame(callbackContext.BrowsingContext) is { } frame)
        {
            // The callbacks of the event loop run outside the scripts: the objects of the scripts are used in a scope of the engine
            using var scope = ScriptEngine.Engine.Enter();
            frame.Target.OnConsoleMessage(frame, message);
        }
    }

    private void OnUnhandledScriptError(CallbackContext callbackContext, ScriptError error)
    {
        if (FindFrame(callbackContext.BrowsingContext) is { } frame)
        {
            using var scope = ScriptEngine.Engine.Enter();
            frame.Target.OnUnhandledScriptError(frame, error);
        }
    }

    private UserPromptResult OnUserPrompt(CallbackContext callbackContext, UserPrompt prompt)
    {
        if (_closed || FindFrame(callbackContext.BrowsingContext) is not { } frame)
            return prompt.Type == UserPromptType.BeforeUnload ? UserPromptResult.Accept() : UserPromptResult.Dismiss;

        return frame.Target.OnUserPrompt(frame, prompt);
    }

    private void OnShowFilePicker(CallbackContext callbackContext, HtmlInputElement input)
    {
        if (FindFrame(callbackContext.BrowsingContext) is { } frame)
        {
            frame.Target.OnFileChooser(frame, input);
        }
    }
}
