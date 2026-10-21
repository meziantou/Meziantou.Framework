using Meziantou.Framework.JavaScriptEngine.Compiler;

namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

/// <summary>An ECMAScript function object (https://tc39.es/ecma262/#sec-ecmascript-function-objects): compiled code and its environment.</summary>
internal sealed class JsClosure : JsFunction
{
    public JsClosure(JsRealm realm, FunctionTemplate template, Environment environment, JsObject prototype, IScriptOrModule? script)
        : base(realm, prototype)
    {
        Template = template;
        Environment = environment;
        Script = script;
        if ((template.Flags & FunctionTemplateFlags.Constructor) != 0)
        {
            Flags |= ObjectFlags.Constructor;
        }

        if (template.IsClassConstructor)
        {
            Flags |= ObjectFlags.ClassConstructor;
        }
    }

    /// <summary>Gets the compiled code.</summary>
    public FunctionTemplate Template { get; }

    /// <summary>Gets the [[Environment]] of the function.</summary>
    public Environment Environment { get; internal set; }

    /// <summary>Gets or sets the [[HomeObject]] (methods): the object whose prototype is the target of super.</summary>
    public JsObject? HomeObject { get; set; }

    /// <summary>Gets the [[ScriptOrModule]] of the function.</summary>
    public IScriptOrModule? Script { get; }

    // The slots of the class constructors (most closures are not: they are in a separate object)
    private ClassConstructorData? _classData;

    /// <summary>Gets or sets the class of a class constructor (the source text and the default constructor).</summary>
    public ClassTemplate? ClassDefinition
    {
        get => _classData?.Definition;
        set => (_classData ??= new()).Definition = value;
    }

    /// <summary>Gets or sets the [[Fields]] of a class constructor: the function that defines the instance fields.</summary>
    public JsClosure? FieldInitializer
    {
        get => _classData?.FieldInitializer;
        set => (_classData ??= new()).FieldInitializer = value;
    }

    /// <summary>Gets or sets the [[PrivateMethods]] of a class constructor.</summary>
    public List<PrivateMethodRecord>? PrivateMethods
    {
        get => _classData?.PrivateMethods;
        set => (_classData ??= new()).PrivateMethods = value;
    }

    /// <summary>Gets whether the function is the constructor of a class with an extends clause ([[ConstructorKind]] derived).</summary>
    public bool IsDerivedConstructor => ClassDefinition is not null ? ClassDefinition.HasHeritage : Template.IsDerivedConstructor;

    public bool IsClassConstructorFunction => (Flags & ObjectFlags.ClassConstructor) != 0;

    internal override string? GetSourceText()
    {
        if (ClassDefinition is not null)
            return ClassDefinition.Source.Text[ClassDefinition.SourceStart..ClassDefinition.SourceEnd];

        return Template.GetSourceText();
    }

    internal override JsValue Call(JsValue thisValue, ReadOnlySpan<JsValue> arguments)
    {
        // The error is created in the realm of the function (the callee context)
        if (IsClassConstructorFunction)
            throw new JsException(new JsValue(Realm.CreateError(ErrorType.TypeError, "Class constructor " + (InitialName ?? "") + " cannot be invoked without 'new'")));

        return JsInterpreter.CallClosure(this, thisValue, arguments, newTarget: null);
    }

    internal override JsValue Construct(ReadOnlySpan<JsValue> arguments, JsObject newTarget)
    {
        return JsInterpreter.ConstructClosure(this, arguments, newTarget);
    }
}

/// <summary>A private method or accessor of a class ([[PrivateMethods]]).</summary>
internal sealed record PrivateMethodRecord(PrivateName Name, PrivateElementKind Kind, JsValue Method, JsValue Getter, JsValue Setter);

/// <summary>The slots of a class constructor (<see cref="JsClosure.ClassDefinition"/>...).</summary>
internal sealed class ClassConstructorData
{
    public ClassTemplate? Definition { get; set; }

    public JsClosure? FieldInitializer { get; set; }

    public List<PrivateMethodRecord>? PrivateMethods { get; set; }
}
