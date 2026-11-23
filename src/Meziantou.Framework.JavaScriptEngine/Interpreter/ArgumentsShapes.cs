namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

/// <summary>
/// The shapes of the properties of a new arguments object (length, @@iterator, callee): the object gets its final shape
/// and slots at once, instead of one transition and possibly one resize of its slots per property.
/// </summary>
/// <remarks>The shapes are the ones the transitions of the engine give (they belong to the engine, as the other shapes).</remarks>
internal sealed class ArgumentsShapes(ShapeRoots roots)
{
    private static readonly PropertyKey LengthKey = new("length");
    private static readonly PropertyKey IteratorKey = new(WellKnownSymbols.Iterator);
    private static readonly PropertyKey CalleeKey = new("callee");

    /// <summary>Gets the shape of an unmapped arguments object whose callee is the function (an ordinary object).</summary>
    public Shape Callee => field ??= Create(ShapeKind.Ordinary, PropertyFlags.BuiltIn);

    /// <summary>Gets the shape of an unmapped arguments object whose callee is the %ThrowTypeError% accessor (strict code).</summary>
    public Shape ThrowingCallee => field ??= Create(ShapeKind.Ordinary, PropertyFlags.Accessor);

    /// <summary>Gets the shape of a mapped arguments object (an exotic object whose indices may alias the parameters).</summary>
    public Shape MappedCallee => field ??= Create(ShapeKind.IndexedExotic, PropertyFlags.BuiltIn);

    private Shape Create(ShapeKind kind, PropertyFlags calleeFlags)
    {
        var shape = Add(roots.GetRoot(kind), LengthKey, PropertyFlags.BuiltIn);
        shape = Add(shape, IteratorKey, PropertyFlags.BuiltIn);
        return Add(shape, CalleeKey, calleeFlags);
    }

    private static Shape Add(Shape shape, in PropertyKey key, PropertyFlags flags) => shape.AddProperty(key, flags) ?? throw new InvalidOperationException("The shape of an arguments object cannot be created");
}
