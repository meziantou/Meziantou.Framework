namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The operations of the service workers on the <c>Request</c> and <c>Response</c> objects of a realm (the Fetch API is
/// implemented in JavaScript, see <see cref="Scripting.ScriptEngine.GetServiceWorkerFetchOperations"/>). The methods are
/// called on the thread of the realm.
/// </summary>
internal interface IServiceWorkerFetchOperations
{
    /// <summary>Creates the <c>Request</c> object of a request of the fetch algorithm, whose headers are immutable and whose signal is aborted by <paramref name="abortSignal"/>.</summary>
    object CreateRequest(FetchRequest request, AbortSignal abortSignal);

    /// <summary>Creates the <c>Response</c> object of a response of the fetch algorithm, whose headers are immutable.</summary>
    object CreateResponse(FetchResponse response);

    /// <summary>
    /// Reads a value given to <c>respondWith()</c>: <paramref name="callback"/> is called (from a task) with the response of
    /// a <c>Response</c> object once its body was read, or with null when the value is not a <c>Response</c> object, when its
    /// body is disturbed or locked, or when its body cannot be read.
    /// </summary>
    void ReadResponse(object value, Action<FetchResponse?> callback);
}
