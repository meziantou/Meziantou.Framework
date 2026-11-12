namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// The operations of a cross-origin object (https://html.spec.whatwg.org/multipage/nav-history-apis.html#cross-origin-objects):
/// a <c>Location</c> object, or the <c>Window</c> of a WindowProxy (<see cref="IJsWindowProxyHandler{TValue}"/>). The engine
/// implements the internal methods of the object with them (see <see cref="IJsAdapter{TValue}.CreateCrossOriginPlatformObject"/>):
/// the ordinary internal methods when <see cref="IsSameOrigin"/> is true, else the cross-origin ones.
/// </summary>
/// <typeparam name="TValue">The type of the JavaScript values of the engine.</typeparam>
public interface IJsCrossOriginObjectHandler<TValue>
    where TValue : class
{
    /// <summary>
    /// IsPlatformObjectSameOrigin(O) (https://html.spec.whatwg.org/multipage/nav-history-apis.html#isplatformobjectsameorigin-(-o-)):
    /// whether the origin of the current realm is same origin-domain with the origin of the object.
    /// </summary>
    bool IsSameOrigin();

    /// <summary>
    /// The [[GetOwnProperty]] of the object for a cross-origin caller, for a key that is not an array index: CrossOriginGetOwnPropertyHelper(O, P)
    /// (https://html.spec.whatwg.org/multipage/nav-history-apis.html#crossorigingetownpropertyhelper-(-o,-p-)), the named
    /// child navigables of a window, then CrossOriginPropertyFallback(P). The functions of the descriptor belong to the current realm.
    /// </summary>
    /// <param name="key">The key: a string or a symbol.</param>
    /// <returns>The property descriptor.</returns>
    /// <exception cref="Exception">The "SecurityError" DOMException of the current realm: the property is not a cross-origin property.</exception>
    JsPropertyDescriptor<TValue> GetCrossOriginOwnProperty(TValue key);

    /// <summary>
    /// CrossOriginOwnPropertyKeys(O) (https://html.spec.whatwg.org/multipage/nav-history-apis.html#crossoriginownpropertykeys-(-o-)):
    /// the names of the cross-origin properties, then <c>then</c> and the symbols of CrossOriginPropertyFallback.
    /// </summary>
    IReadOnlyList<TValue> GetCrossOriginOwnPropertyKeys();

    /// <summary>Returns the exception that throws the "SecurityError" DOMException of the current realm.</summary>
    Exception CreateSecurityError();
}

/// <summary>
/// The operations of a WindowProxy exotic object (https://html.spec.whatwg.org/multipage/nav-history-apis.html#the-windowproxy-exotic-object),
/// created by <see cref="IJsAdapter{TValue}.CreateWindowProxy"/>. Its internal methods are the ones of the specification:
/// the indexed properties are the WindowProxy objects of the child navigables, the other properties are the properties of
/// the global object of <see cref="Window"/> (same origin) or the cross-origin properties.
/// </summary>
/// <typeparam name="TValue">The type of the JavaScript values of the engine.</typeparam>
public interface IJsWindowProxyHandler<TValue> : IJsCrossOriginObjectHandler<TValue>
    where TValue : class
{
    /// <summary>Gets the global object of the window of the WindowProxy ([[Window]]): the active window of its browsing context.</summary>
    TValue Window { get; }

    /// <summary>Gets the number of document-tree child navigables of the document of the window.</summary>
    int ChildCount { get; }

    /// <summary>Returns the WindowProxy of the document-tree child navigable at <paramref name="index"/>, or null.</summary>
    TValue? GetChild(int index);

    /// <summary>
    /// Whether the [[Get]] and [[Set]] internal methods of the WindowProxy must report the access for the code of the current
    /// realm (https://html.spec.whatwg.org/multipage/browsers.html#coop-check-access-report): the windows are in different
    /// virtual browsing context groups and an opener policy reports the accesses. The engine then calls
    /// <see cref="ReportAccess"/> with the name of the property.
    /// </summary>
    bool ShouldReportAccess() => false;

    /// <summary>
    /// Whether <paramref name="name"/> is a named property of the window (https://html.spec.whatwg.org/multipage/nav-history-apis.html#named-access-on-the-window-object):
    /// a named child navigable, or a named element when the window is same origin-domain with the current realm.
    /// </summary>
    /// <param name="name">The name of the property.</param>
    bool IsNamedProperty(string name) => false;

    /// <summary>
    /// Reports an access to the window by the code of the current realm (https://html.spec.whatwg.org/multipage/browsers.html#coop-check-access-report).
    /// </summary>
    /// <param name="propertyName">The name of a cross-origin accessible window property, <c>indexed</c> for a child navigable, or <c>named</c> for a named property.</param>
    void ReportAccess(string propertyName)
    {
    }
}
