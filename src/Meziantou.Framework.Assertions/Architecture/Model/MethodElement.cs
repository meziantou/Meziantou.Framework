using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using Meziantou.Framework.Assertions.Architecture.Reading;

namespace Meziantou.Framework.Assertions.Architecture;

/// <summary>A method, a constructor, or the accessor of a property or an event.</summary>
public sealed class MethodElement : MemberElement
{
    // MethodImplAttributes.Async: the compiler emits no state machine when it targets runtime async (.NET 11)
    private const MethodImplAttributes RuntimeAsyncFlag = (MethodImplAttributes)0x2000;

    private readonly MethodAttributes _attributes;
    private readonly Lazy<(TypeReference ReturnType, IReadOnlyList<ParameterElement> Parameters)> _signature;
    private readonly Lazy<IReadOnlyList<AttributeElement>> _customAttributes;
    private readonly Lazy<SourceLocation?> _location;

    internal MethodElement(TypeElement declaringType, MethodDefinitionHandle handle)
        : base(declaringType, declaringType.Image.Reader.GetString(declaringType.Image.Reader.GetMethodDefinition(handle).Name))
    {
        Handle = handle;
        var reader = declaringType.Image.Reader;
        var definition = reader.GetMethodDefinition(handle);
        _attributes = definition.Attributes;
        GenericParameters = [.. definition.GetGenericParameters().Select(parameter => reader.GetString(reader.GetGenericParameter(parameter).Name))];
        GenericContext = new GenericContext(declaringType.GenericContext.TypeParameters, [.. GenericParameters]);
        IsAsync = definition.ImplAttributes.HasFlag(RuntimeAsyncFlag)
            || MetadataNames.HasAttribute(reader, definition.GetCustomAttributes(), "System.Runtime.CompilerServices.AsyncStateMachineAttribute")
            || MetadataNames.HasAttribute(reader, definition.GetCustomAttributes(), "System.Runtime.CompilerServices.AsyncIteratorStateMachineAttribute");
        IsExtension = MetadataNames.HasAttribute(reader, definition.GetCustomAttributes(), "System.Runtime.CompilerServices.ExtensionAttribute");

        _signature = new(DecodeSignature);
        _customAttributes = new(() => AttributeElement.Create(DeclaringType.Provider, DeclaringType.Image.Reader, DeclaringType.Image.Reader.GetMethodDefinition(Handle).GetCustomAttributes()));
        _location = new(() => SequencePointMap.Create(DeclaringType.Image, Handle)?.First ?? DeclaringType.FindGeneratedCodeLocation(Name));
    }

    internal MethodDefinitionHandle Handle { get; }

    internal GenericContext GenericContext { get; }

    // The property or the event when the method is one of its accessors
    internal MemberElement? AccessorOwner { get; set; }

    /// <inheritdoc />
    public override ElementAccessibility Accessibility => GetAccessibility(_attributes);

    /// <inheritdoc />
    public override bool IsStatic => _attributes.HasFlag(MethodAttributes.Static);

    /// <summary>Gets a value indicating whether the method is abstract, including the methods of an interface that have no default implementation.</summary>
    public bool IsAbstract => _attributes.HasFlag(MethodAttributes.Abstract);

    /// <summary>Gets a value indicating whether the method can be overridden: it is virtual or abstract, and not sealed.</summary>
    public bool IsVirtual => _attributes.HasFlag(MethodAttributes.Virtual) && !_attributes.HasFlag(MethodAttributes.Final);

    /// <summary>Gets a value indicating whether the method is an instance or a static constructor.</summary>
    public bool IsConstructor => Name is ".ctor" or ".cctor";

    /// <summary>Gets a value indicating whether the method is <c>async</c>.</summary>
    public bool IsAsync { get; }

    /// <summary>Gets a value indicating whether the method is an extension method.</summary>
    public bool IsExtension { get; }

    /// <summary>Gets the names of the generic parameters of the method.</summary>
    public IReadOnlyList<string> GenericParameters { get; }

    /// <summary>Gets the return type of the method (<c>System.Void</c> when it returns nothing).</summary>
    public TypeReference ReturnType => _signature.Value.ReturnType;

    /// <summary>Gets the parameters of the method.</summary>
    public IReadOnlyList<ParameterElement> Parameters => _signature.Value.Parameters;

    /// <inheritdoc />
    public override IReadOnlyList<AttributeElement> Attributes => _customAttributes.Value;

    /// <inheritdoc />
    public override SourceLocation? Location => _location.Value;

    /// <summary>Returns the method as <c>DeclaringType.Name(ParameterTypes)</c>.</summary>
    /// <returns>The method as <c>DeclaringType.Name(ParameterTypes)</c>.</returns>
    public override string ToString() => DeclaringType + "." + Name + "(" + string.Join(", ", Parameters.Select(parameter => parameter.Type)) + ")";

    private protected override IReadOnlyList<Dependency> GetDependencies() => AccessorOwner is null ? base.GetDependencies() : DeclaringType.GetAccessorDependencies(this);

    private (TypeReference ReturnType, IReadOnlyList<ParameterElement> Parameters) DecodeSignature()
    {
        var reader = DeclaringType.Image.Reader;
        var definition = reader.GetMethodDefinition(Handle);
        var signature = definition.DecodeSignature(DeclaringType.Provider, GenericContext);

        var names = new string[signature.ParameterTypes.Length];
        var attributes = new IReadOnlyList<AttributeElement>[signature.ParameterTypes.Length];
        foreach (var parameterHandle in definition.GetParameters())
        {
            var parameter = reader.GetParameter(parameterHandle);
            var index = parameter.SequenceNumber - 1;
            if (index >= 0 && index < names.Length)
            {
                names[index] = reader.GetString(parameter.Name);
                attributes[index] = AttributeElement.Create(DeclaringType.Provider, reader, parameter.GetCustomAttributes());
            }
        }

        var parameters = new ParameterElement[signature.ParameterTypes.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            parameters[i] = new ParameterElement(this, names[i] ?? string.Empty, i, signature.ParameterTypes[i], attributes[i] ?? []);
        }

        return (signature.ReturnType, parameters);
    }
}
