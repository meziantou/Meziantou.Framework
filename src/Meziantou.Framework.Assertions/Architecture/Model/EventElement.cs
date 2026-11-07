using System.Reflection.Metadata;
using Meziantou.Framework.Assertions.Architecture.Reading;

namespace Meziantou.Framework.Assertions.Architecture;

/// <summary>An event.</summary>
public sealed class EventElement : MemberElement
{
    private readonly Lazy<TypeReference> _type;
    private readonly Lazy<IReadOnlyList<AttributeElement>> _customAttributes;

    internal EventElement(TypeElement declaringType, EventDefinitionHandle handle, MethodElement? adder, MethodElement? remover)
        : base(declaringType, declaringType.Image.Reader.GetString(declaringType.Image.Reader.GetEventDefinition(handle).Name))
    {
        Handle = handle;
        Adder = adder;
        Remover = remover;
        _type = new(() => DependencyCollector.DecodeType(DeclaringType.Provider, DeclaringType.Image.Reader, DeclaringType.Image.Reader.GetEventDefinition(Handle).Type, DeclaringType.GenericContext));
        _customAttributes = new(() => AttributeElement.Create(DeclaringType.Provider, DeclaringType.Image.Reader, DeclaringType.Image.Reader.GetEventDefinition(Handle).GetCustomAttributes()));
    }

    internal EventDefinitionHandle Handle { get; }

    /// <summary>Gets the type of the event, a delegate type.</summary>
    public TypeReference Type => _type.Value;

    /// <summary>Gets the <c>add</c> accessor, or <see langword="null"/>.</summary>
    public MethodElement? Adder { get; }

    /// <summary>Gets the <c>remove</c> accessor, or <see langword="null"/>.</summary>
    public MethodElement? Remover { get; }

    /// <inheritdoc />
    public override ElementAccessibility Accessibility => (Adder ?? Remover)?.Accessibility ?? ElementAccessibility.Private;

    /// <inheritdoc />
    public override bool IsStatic => (Adder ?? Remover)?.IsStatic ?? false;

    /// <inheritdoc />
    public override IReadOnlyList<AttributeElement> Attributes => _customAttributes.Value;

    /// <inheritdoc />
    public override SourceLocation? Location => Adder?.Location ?? Remover?.Location;
}
