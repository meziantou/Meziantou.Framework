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
}
