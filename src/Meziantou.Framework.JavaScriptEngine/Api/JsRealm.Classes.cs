using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine;

// The JavaScript classes of the .NET classes marked with [JsExport]
public sealed partial class JsRealm
{
    private Dictionary<JsClass, ExportedClass>? _exportedClasses;
    private ConditionalWeakTable<object, JsObject>? _exportedObjects;

    /// <summary>
    /// Gets the JavaScript class of a .NET class marked with <see cref="JsExportAttribute"/>: its constructor, or the object
    /// of a static class. It is created on the first call, and the next calls of the realm return the same object.
    /// </summary>
    /// <param name="jsClass">The static <c>JsClass</c> property generated for the class.</param>
    /// <returns>The constructor of the class, or the object of a static class. Store it in a property (e.g. of <see cref="GlobalObject"/>) to make it visible to the scripts.</returns>
    public JsObject GetClass(JsClass jsClass)
    {
        ArgumentNullException.ThrowIfNull(jsClass);
        using var scope = Engine.Enter();
        return GetExportedClass(jsClass).Constructor;
    }

    private ExportedClass GetExportedClass(JsClass jsClass)
    {
        _exportedClasses ??= [];
        if (_exportedClasses.TryGetValue(jsClass, out var exportedClass))
            return exportedClass;

        if (jsClass.IsStatic)
        {
            var obj = CreateObject();
            obj.DefineProperty(JsSymbol.ToStringTag, JsPropertyDescriptor.Data(jsClass.Name, writable: false, enumerable: false, configurable: true));
            exportedClass = new ExportedClass(obj, Prototype: null);
            _exportedClasses.Add(jsClass, exportedClass);
            jsClass.DefineMembersCore(this, obj, prototype: null);
            return exportedClass;
        }

        var baseClass = jsClass.BaseClass is { } baseJsClass ? GetExportedClass(baseJsClass) : null;
        var prototype = CreateObject(baseClass?.Prototype ?? ObjectPrototype);
        var constructor = CreateConstructor(jsClass.Name, jsClass.GetConstructorLength(), (_, arguments, newTarget) =>
        {
            if (newTarget is null)
                throw CreateException(JsErrorType.TypeError, "Class constructor " + jsClass.Name + " cannot be invoked without 'new'");

            var target = jsClass.CreateInstanceCore(this, arguments) ?? throw CreateException(JsErrorType.TypeError, "Illegal constructor");

            // A class of the scripts that extends the class creates its instances with its own prototype
            var instancePrototype = newTarget.GetProperty("prototype").TryGetObject() ?? prototype;

            // The constructor may have converted the object already (e.g. to store it in a global): new returns that object
            if (ExportedObjects.TryGetValue(target, out var existing))
            {
                if (!existing.SetPrototype(instancePrototype))
                    throw CreateException(JsErrorType.TypeError, "Cannot set the prototype of the object created by the constructor of " + jsClass.Name);

                return existing;
            }

            var instance = new ExportedObject(instancePrototype, target);
            ExportedObjects.Add(target, instance);
            return instance;
        });

        if (baseClass is not null)
        {
            constructor.SetPrototype(baseClass.Constructor);
        }

        constructor.DefineProperty("prototype", JsPropertyDescriptor.Data(prototype, writable: false, enumerable: false, configurable: false));
        prototype.DefineProperty("constructor", JsPropertyDescriptor.Data(constructor, writable: true, enumerable: false, configurable: true));
        exportedClass = new ExportedClass(constructor, prototype);
        _exportedClasses.Add(jsClass, exportedClass);
        jsClass.DefineMembersCore(this, constructor, prototype);
        return exportedClass;
    }

    private ConditionalWeakTable<object, JsObject> ExportedObjects => _exportedObjects ??= [];

    // The same .NET object is always the same JavaScript object of the realm
    internal JsObject GetExportedObject(IJsExportable value)
    {
        if (ExportedObjects.TryGetValue(value, out var obj))
            return obj;

        var exportedClass = GetExportedClass(value.JsClass);
        if (exportedClass.Prototype is null)
            throw new InvalidOperationException($"The class '{value.JsClass.Name}' is static: it has no instances");

        obj = new ExportedObject(exportedClass.Prototype, value);
        ExportedObjects.Add(value, obj);
        return obj;
    }

    private sealed record ExportedClass(JsObject Constructor, JsObject? Prototype);

    // An instance of an exported class: an ordinary object whose target is the .NET object
    private sealed class ExportedObject(JsObject prototype, object target) : JsHostObject(prototype, target, hasOrdinaryInternalMethods: true);
}
