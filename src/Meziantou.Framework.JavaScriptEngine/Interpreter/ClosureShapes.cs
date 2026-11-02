namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

/// <summary>
/// The shapes of the properties that <see cref="JsInterpreter.CreateClosure(JsRealm, Compiler.FunctionTemplate, Environment, IScriptOrModule?)"/>
/// defines on a new function object (and on its prototype object): the closure gets its final shape and slots at once,
/// instead of one transition and possibly one resize of its slots per property.
/// </summary>
/// <remarks>The shapes are the ones the transitions of the engine give (they belong to the engine, as the other shapes).</remarks>
internal sealed class ClosureShapes(ShapeRoots roots)
{
    private static readonly PropertyKey LengthKey = new("length");
    private static readonly PropertyKey NameKey = new("name");
    private static readonly PropertyKey PrototypeKey = new("prototype");
    private static readonly PropertyKey ArgumentsKey = new("arguments");
    private static readonly PropertyKey CallerKey = new("caller");
    private static readonly PropertyKey ConstructorKey = new("constructor");

    /// <summary>Gets the shape of an arrow function, a method or an async function: length, name.</summary>
    public Shape LengthName => field ??= Add(Add(roots.GetRoot(ShapeKind.Ordinary), LengthKey, PropertyFlags.Configurable), NameKey, PropertyFlags.Configurable);

    /// <summary>Gets the shape of a strict function or a generator: length, name, prototype.</summary>
    public Shape LengthNamePrototype => field ??= Add(LengthName, PrototypeKey, PropertyFlags.Writable);

    /// <summary>Gets the shape of a sloppy function: length, name, arguments, caller, prototype.</summary>
    public Shape LengthNameLegacyPrototype => field ??= Add(Add(Add(LengthName, ArgumentsKey, PropertyFlags.Accessor), CallerKey, PropertyFlags.Accessor), PrototypeKey, PropertyFlags.Writable);

    /// <summary>Gets the shape of the prototype object of a constructor: constructor.</summary>
    public Shape Constructor => field ??= Add(roots.GetRoot(ShapeKind.Ordinary), ConstructorKey, PropertyFlags.BuiltIn);

    private static Shape Add(Shape shape, in PropertyKey key, PropertyFlags flags) => shape.AddProperty(key, flags) ?? throw new InvalidOperationException("The shape of a function cannot be created");
}
