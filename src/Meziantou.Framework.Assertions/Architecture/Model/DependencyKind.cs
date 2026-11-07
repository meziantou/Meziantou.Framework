namespace Meziantou.Framework.Assertions.Architecture;

/// <summary>Specifies how a type uses the type it depends on.</summary>
public enum DependencyKind
{
    /// <summary>The type inherits from the target.</summary>
    BaseType,

    /// <summary>The type implements the target.</summary>
    Interface,

    /// <summary>The type or one of its members has an attribute of this type, or an attribute argument is <c>typeof(Target)</c>.</summary>
    Attribute,

    /// <summary>A generic parameter of the type or of a method is constrained to the target.</summary>
    GenericConstraint,

    /// <summary>A field has the target as its type.</summary>
    FieldType,

    /// <summary>A property or an indexer has the target as its type or the type of a parameter.</summary>
    PropertyType,

    /// <summary>An event has the target as its type.</summary>
    EventType,

    /// <summary>A parameter of a method has the target as its type.</summary>
    ParameterType,

    /// <summary>A method returns the target.</summary>
    ReturnType,

    /// <summary>A local variable, or a variable captured by a lambda or a state machine, has the target as its type.</summary>
    LocalVariable,

    /// <summary>A method calls a method of the target, or uses it as a delegate.</summary>
    MethodCall,

    /// <summary>A method reads or writes a field of the target.</summary>
    FieldAccess,

    /// <summary>A method creates an instance of the target (<c>new</c>).</summary>
    ObjectCreation,

    /// <summary>A method uses the target in another instruction: cast, <c>is</c>, <c>typeof</c>, <c>sizeof</c>, <c>default</c>, boxing, array creation...</summary>
    TypeUsage,

    /// <summary>A method catches exceptions of the target type.</summary>
    CatchClause,
}
