namespace Meziantou.Framework.JavaScriptEngine.Samples;

#region JavaScriptEngine_RejectionTrackingHost
/// <summary>Records the promises that are rejected while no handler is attached to them.</summary>
public sealed class RejectionTrackingHost : JsHost
{
    private readonly HashSet<JsObject> _unhandledRejections = [];

    public IReadOnlyCollection<JsObject> UnhandledRejections => _unhandledRejections;

    public override void PromiseRejectionTracker(JsEngine engine, JsObject promise, JsPromiseRejectionOperation operation)
    {
        // Reject: the promise is rejected and has no handler. Handle: a handler was attached later.
        if (operation is JsPromiseRejectionOperation.Reject)
        {
            _unhandledRejections.Add(promise);
        }
        else
        {
            _unhandledRejections.Remove(promise);
        }
    }
}
#endregion
