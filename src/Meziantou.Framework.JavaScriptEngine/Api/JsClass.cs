namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The JavaScript class of a .NET class. The source generator derives it for each class marked with
/// <see cref="JsExportAttribute"/>, and exposes it as the static <c>JsClass</c> property of the class:
/// <see cref="JsRealm.GetClass(JsClass)"/> creates its constructor and its prototype in a realm.
/// </summary>
public abstract class JsClass
{
    /// <summary>Initializes the class.</summary>
    /// <param name="name">The name of the JavaScript class.</param>
    /// <param name="type">The .NET class.</param>
    /// <param name="baseClass">The JavaScript class of the nearest base class marked with <see cref="JsExportAttribute"/>, or null.</param>
    protected JsClass(string name, Type type, JsClass? baseClass)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(type);
        Name = name;
        Type = type;
        BaseClass = baseClass;
    }

    /// <summary>Gets the name of the JavaScript class.</summary>
    public string Name { get; }

    /// <summary>Gets the .NET class.</summary>
    public Type Type { get; }

    /// <summary>Gets the JavaScript class of the nearest base class marked with <see cref="JsExportAttribute"/>: the prototypes of its constructor and of its prototype.</summary>
    public JsClass? BaseClass { get; }

    /// <summary>Gets whether the .NET class is static: its JavaScript value is a plain object whose properties are its static members.</summary>
    public bool IsStatic => Type.IsAbstract && Type.IsSealed;

    /// <summary>Gets the <c>length</c> of the constructor: the number of its required parameters.</summary>
    protected virtual int ConstructorLength => 0;

    /// <summary>Creates the .NET object of a JavaScript object created by <c>new</c>.</summary>
    /// <returns>The .NET object, or null when the scripts cannot create the class (the constructor throws a TypeError).</returns>
    protected virtual object? CreateInstance(JsRealm realm, ReadOnlySpan<JsValue> arguments) => null;

    /// <summary>Defines the static members on <paramref name="constructor"/>, and the instance members on <paramref name="prototype"/>.</summary>
    /// <param name="realm">The realm of the class.</param>
    /// <param name="constructor">The constructor of the class, or the object of a static class.</param>
    /// <param name="prototype">The prototype of the class, or null for a static class.</param>
    protected abstract void DefineMembers(JsRealm realm, JsObject constructor, JsObject? prototype);

    // The realm calls the protected members: an override of a protected internal member would have to be protected internal
    // in an assembly that sees the internal members of the engine, and protected in the others
    internal int GetConstructorLength() => ConstructorLength;

    internal object? CreateInstanceCore(JsRealm realm, ReadOnlySpan<JsValue> arguments) => CreateInstance(realm, arguments);

    internal void DefineMembersCore(JsRealm realm, JsObject constructor, JsObject? prototype) => DefineMembers(realm, constructor, prototype);

    /// <summary>Gets the .NET object of the <c>this</c> value of a method.</summary>
    /// <exception cref="JsException">The value is not an object of the class (a TypeError).</exception>
    protected static T GetThis<T>(JsValue thisValue)
        where T : class
    {
        return thisValue.TryGetObject() is JsHostObject { Target: T target }
            ? target
            : ThrowHelper.ThrowTypeError<T>("Illegal invocation");
    }

    /// <summary>
    /// Converts an argument as <see cref="JsEngine.ToDotNet{T}(JsValue)"/> does; a missing argument is undefined. A value
    /// that cannot be converted to <see cref="object"/> (a function, a symbol, a cycle) is a TypeError.
    /// </summary>
    /// <exception cref="JsException">The argument cannot be converted (a TypeError or a RangeError).</exception>
    protected static T GetArgument<T>(JsRealm realm, ReadOnlySpan<JsValue> arguments, int index)
    {
        ArgumentNullException.ThrowIfNull(realm);
        return Convert<T>(realm, arguments, index);
    }

    /// <summary>Converts an optional argument: <paramref name="defaultValue"/> when it is missing or undefined.</summary>
    /// <exception cref="JsException">The argument cannot be converted (a TypeError or a RangeError).</exception>
    protected static T GetArgument<T>(JsRealm realm, ReadOnlySpan<JsValue> arguments, int index, T defaultValue)
    {
        ArgumentNullException.ThrowIfNull(realm);
        if (index >= arguments.Length || arguments[index].IsUndefined)
            return defaultValue;

        return Convert<T>(realm, arguments, index);
    }

    /// <summary>Converts an argument whose type is a non-nullable reference type: undefined and null throw a TypeError.</summary>
    /// <exception cref="JsException">The argument is missing, undefined or null, or it cannot be converted (a TypeError).</exception>
    protected static T GetRequiredArgument<T>(JsRealm realm, ReadOnlySpan<JsValue> arguments, int index)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(realm);
        if (index >= arguments.Length || arguments[index].IsNullOrUndefined)
        {
            var description = index >= arguments.Length ? "undefined" : JsOperations.DescribeForError(arguments[index]);
            return ThrowHelper.ThrowTypeError<T>("A value is required: " + description + " cannot be converted");
        }

        return Convert<T>(realm, arguments, index);
    }

    /// <summary>
    /// Converts an optional argument whose type is a non-nullable reference type: <paramref name="defaultValue"/> when it is
    /// missing or undefined, and a TypeError for null.
    /// </summary>
    /// <exception cref="JsException">The argument is null, or it cannot be converted (a TypeError).</exception>
    protected static T GetRequiredArgument<T>(JsRealm realm, ReadOnlySpan<JsValue> arguments, int index, T defaultValue)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(realm);
        if (index >= arguments.Length || arguments[index].IsUndefined)
            return defaultValue;

        return GetRequiredArgument<T>(realm, arguments, index);
    }

    // The conversion to object converts the objects to dictionaries and lists: a function, a symbol or a cycle is a TypeError
    // of the script, as the other conversions that fail, instead of an exception of the host
    private static T Convert<T>(JsRealm realm, ReadOnlySpan<JsValue> arguments, int index)
    {
        if (typeof(T) != typeof(object))
            return realm.Engine.ConvertArgument<T>(arguments, index);

        try
        {
            return realm.Engine.ConvertArgument<T>(arguments, index);
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
        {
            return ThrowHelper.ThrowTypeError<T>(exception.Message);
        }
    }

    /// <summary>Defines a method as the methods of a JavaScript class: writable, not enumerable and configurable.</summary>
    protected static void DefineMethod(JsRealm realm, JsObject target, string name, int length, JsNativeFunction function)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(target);
        target.DefineProperty(name, JsPropertyDescriptor.Data(realm.CreateFunction(name, length, function), writable: true, enumerable: false, configurable: true));
    }

    /// <summary>Defines an accessor as the accessors of a JavaScript class: not enumerable and configurable.</summary>
    /// <param name="realm">The realm of the functions.</param>
    /// <param name="target">The object that receives the property.</param>
    /// <param name="name">The name of the property.</param>
    /// <param name="getter">The getter.</param>
    /// <param name="setter">The setter, or null for a read-only property.</param>
    protected static void DefineAccessor(JsRealm realm, JsObject target, string name, JsNativeFunction getter, JsNativeFunction? setter)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(target);
        var getterFunction = realm.CreateFunction("get " + name, 0, getter);
        var setterFunction = setter is null ? JsValue.Undefined : realm.CreateFunction("set " + name, 1, setter);
        target.DefineProperty(name, JsPropertyDescriptor.Accessor(getterFunction, setterFunction, enumerable: false, configurable: true));
    }
}
