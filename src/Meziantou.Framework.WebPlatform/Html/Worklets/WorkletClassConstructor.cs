namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The class a script registers in a worklet global scope (e.g. the <c>paintCtor</c> of
/// <see cref="PaintWorkletGlobalScope.RegisterPaint"/>): a constructor of the script engine, whose static properties
/// describe the class and whose prototype has the callbacks. The objects are created by the WebIDL bindings of the script
/// engine.
/// </summary>
public abstract class WorkletClassConstructor
{
    private protected WorkletClassConstructor()
    {
    }

    /// <summary>Whether the value is a constructor (https://tc39.es/ecma262/#sec-isconstructor).</summary>
    internal abstract bool IsConstructor { get; }

    /// <summary>
    /// Gets a static property of the class converted to a <c>sequence&lt;DOMString&gt;</c> (e.g. <c>inputProperties</c>), or
    /// null when it is undefined. The exceptions of the getter and of the conversion are rethrown.
    /// </summary>
    internal abstract IReadOnlyList<string>? GetStringSequence(string name);

    /// <summary>
    /// Gets a boolean member of a static property of the class converted to a dictionary (e.g. the <c>alpha</c> member of
    /// <c>contextOptions</c>), or <paramref name="defaultValue"/> when the property or the member is undefined. Throws a
    /// TypeError when the property is not an object.
    /// </summary>
    internal abstract bool GetDictionaryBoolean(string name, string member, bool defaultValue);

    /// <summary>
    /// Gets a member of a static property of the class converted to a dictionary (e.g. the <c>childDisplay</c> member of
    /// <c>layoutOptions</c>), converted to a <c>DOMString</c>, or null when the property or the member is undefined. Throws a
    /// TypeError when the property is not an object.
    /// </summary>
    internal abstract string? GetDictionaryString(string name, string member);

    /// <summary>Whether the <c>prototype</c> property of the class is an object.</summary>
    internal abstract bool HasPrototypeObject { get; }

    /// <summary>
    /// Gets a property of the prototype of the class converted to the <c>Function</c> callback function type
    /// (https://webidl.spec.whatwg.org/#es-callback-function): throws a TypeError when it is not callable.
    /// </summary>
    internal abstract object GetPrototypeFunction(string name);

    /// <summary>Constructs the class with no argument (https://tc39.es/ecma262/#sec-construct). The exception of the constructor is rethrown.</summary>
    internal abstract object Construct();

    /// <summary>
    /// Invokes a callback function of the class with an instance as the callback this value, and "rethrow" as the exception
    /// behavior (https://webidl.spec.whatwg.org/#invoke-a-callback-function): the .NET arguments are converted with their
    /// runtime type, and the result is the value of the script engine.
    /// </summary>
    internal abstract object? Invoke(object function, object instance, object?[] arguments);

    /// <summary>
    /// Constructs the class with arguments (https://tc39.es/ecma262/#sec-construct), converted as the ones of <see cref="Invoke"/>.
    /// The exception of the constructor is rethrown.
    /// </summary>
    internal abstract object Construct(object?[] arguments);

    /// <summary>
    /// When a value returned by <see cref="Invoke"/> is a promise (https://tc39.es/ecma262/#sec-ispromise), observes it: the
    /// returned object is updated by a microtask once the promise is settled. Returns null for the other values.
    /// </summary>
    internal abstract WorkletPromise? ObservePromise(object? value);

    /// <summary>Returns the .NET object of a value of the script engine that is a platform object, else null.</summary>
    internal abstract object? AsPlatformObject(object? value);

    /// <summary>
    /// Converts a value of the script engine to an IDL dictionary (https://webidl.spec.whatwg.org/#js-dictionary) the callbacks
    /// of a worklet return: <see cref="FragmentResultOptions"/> or <see cref="IntrinsicSizesResultOptions"/>. The exceptions of
    /// the conversion are rethrown.
    /// </summary>
    internal abstract T ConvertToDictionary<T>(object? value)
        where T : class;

    /// <summary>Whether a value returned by <see cref="Invoke"/> is undefined.</summary>
    internal abstract bool IsUndefined(object? value);

    /// <summary>Reports the rejection reason of the promise of a callback (see <see cref="WorkletPromise.Value"/>) to the global scope of the class, as an exception.</summary>
    internal abstract void ReportRejection(object? reason);

    /// <summary>Reports an exception thrown by the constructor or a callback to the global scope of the class.</summary>
    internal abstract void ReportException(Exception exception);

    /// <summary>
    /// Gets the static <c>parameterDescriptors</c> property of an audio processor class converted to a
    /// <c>sequence&lt;AudioParamDescriptor&gt;</c> (https://webaudio.github.io/web-audio-api/#dom-audioworkletglobalscope-registerprocessor),
    /// or null when it is undefined. The exceptions of the getters and of the conversion are rethrown.
    /// </summary>
    internal abstract IReadOnlyList<WebAudio.AudioParamDescriptor>? GetAudioParamDescriptors();

    /// <summary>
    /// Creates an object of the script engine in the realm of the class, with properties: strings, numbers, lists of numbers
    /// (arrays), lists of name-number pairs (objects), or values of the script engine.
    /// </summary>
    internal abstract object CreateObject(IReadOnlyList<KeyValuePair<string, object?>> properties);

    /// <summary>Creates the call site of the <c>process()</c> method of an instance of an audio processor class.</summary>
    internal abstract WebAudio.Rendering.AudioWorkletProcessCall CreateProcessCall(object processor);
}

/// <summary>The state of a promise returned by a callback of a worklet (see <see cref="WorkletClassConstructor.ObservePromise"/>).</summary>
internal sealed class WorkletPromise
{
    /// <summary>Whether the promise is fulfilled or rejected.</summary>
    public bool IsSettled { get; private set; }

    /// <summary>Whether the promise is fulfilled.</summary>
    public bool IsFulfilled { get; private set; }

    /// <summary>The fulfillment value or the rejection reason (a value of the script engine).</summary>
    public object? Value { get; private set; }

    public void Settle(bool fulfilled, object? value)
    {
        IsSettled = true;
        IsFulfilled = fulfilled;
        Value = value;
    }
}
