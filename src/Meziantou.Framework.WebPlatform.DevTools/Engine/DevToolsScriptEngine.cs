using Meziantou.Framework.WebPlatform.DevTools.Browser;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.JavaScript;

namespace Meziantou.Framework.WebPlatform.DevTools.Engine;

/// <summary>
/// The script engine of a browser context: when a document becomes the active document of a frame, before its scripts run,
/// the frame commits the document (the events of the protocol, the execution contexts, the bindings and the scripts of
/// <c>Page.addScriptToEvaluateOnNewDocument</c>).
/// </summary>
internal sealed class DevToolsScriptEngine(BrowserContextHost context, JavaScriptScriptEngineOptions options) : JavaScriptScriptEngine(options)
{
    protected internal override void OnDocumentActivated(Document document)
    {
        base.OnDocumentActivated(document);
        context.OnDocumentActivated(document);
    }
}
