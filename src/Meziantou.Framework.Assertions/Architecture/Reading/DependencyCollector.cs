#pragma warning disable MA0012 // Invalid metadata is reported with BadImageFormatException, like System.Reflection.Metadata does
using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace Meziantou.Framework.Assertions.Architecture.Reading;

// Collects the dependencies of a type: its declaration, the declarations of its members, the IL of its methods,
// and the code the compiler generated for it in nested types (lambdas, local functions, iterators, async methods).
internal sealed class DependencyCollector
{
    // Attributes the compiler adds on its own; they say nothing about the code that was written
    private static readonly HashSet<string> CompilerAttributes = new(StringComparer.Ordinal)
    {
        "System.ParamArrayAttribute",
        "System.Diagnostics.DebuggerBrowsableAttribute",
        "System.Diagnostics.DebuggerHiddenAttribute",
        "System.Diagnostics.DebuggerStepThroughAttribute",
        "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute",
        "System.Reflection.DefaultMemberAttribute",
        "System.Runtime.CompilerServices.AsyncIteratorStateMachineAttribute",
        "System.Runtime.CompilerServices.AsyncStateMachineAttribute",
        "System.Runtime.CompilerServices.CompilerFeatureRequiredAttribute",
        "System.Runtime.CompilerServices.CompilerGeneratedAttribute",
        "System.Runtime.CompilerServices.DecimalConstantAttribute",
        "System.Runtime.CompilerServices.DynamicAttribute",
        "System.Runtime.CompilerServices.ExtensionAttribute",
        "System.Runtime.CompilerServices.ExtensionMarkerAttribute",
        "System.Runtime.CompilerServices.IsByRefLikeAttribute",
        "System.Runtime.CompilerServices.IsReadOnlyAttribute",
        "System.Runtime.CompilerServices.IsUnmanagedAttribute",
        "System.Runtime.CompilerServices.IteratorStateMachineAttribute",
        "System.Runtime.CompilerServices.NativeIntegerAttribute",
        "System.Runtime.CompilerServices.NullableAttribute",
        "System.Runtime.CompilerServices.NullableContextAttribute",
        "System.Runtime.CompilerServices.NullablePublicOnlyAttribute",
        "System.Runtime.CompilerServices.ParamCollectionAttribute",
        "System.Runtime.CompilerServices.PreserveBaseOverridesAttribute",
        "System.Runtime.CompilerServices.RefSafetyRulesAttribute",
        "System.Runtime.CompilerServices.RequiredMemberAttribute",
        "System.Runtime.CompilerServices.ScopedRefAttribute",
        "System.Runtime.CompilerServices.TupleElementNamesAttribute",
    };

    private readonly TypeElement _owner;
    private readonly AssemblyImage _image;
    private readonly MetadataReader _reader;
    private readonly TypeReferenceProvider _provider;
    private readonly ArchitectureModel _architecture;
    private readonly Dictionary<(string Target, DependencyKind Kind, MemberElement? Source, string? TargetMember), Dependency> _dependencies = [];
    private readonly List<Dependency> _result = [];
    private readonly List<TypeReference> _namedTypes = [];
    private readonly Dictionary<MethodElement, AccessorDependencies> _accessorDependencies = [];
    private Dictionary<TypeDefinitionHandle, CodeOwner>? _generatedTypeOwners;

    // The accessor whose code is being read: its dependencies are also recorded in its own list
    private MethodElement? _currentAccessor;

    public DependencyCollector(TypeElement owner)
    {
        _owner = owner;
        _image = owner.Image;
        _reader = _image.Reader;
        _provider = owner.Provider;
        _architecture = owner.Architecture;
    }

    public static TypeReference DecodeType(TypeReferenceProvider provider, MetadataReader reader, EntityHandle handle, GenericContext context)
    {
        return handle.Kind switch
        {
            HandleKind.TypeDefinition or HandleKind.TypeReference => provider.GetNamedType(handle),
            HandleKind.TypeSpecification => provider.GetTypeFromSpecification(reader, context, (TypeSpecificationHandle)handle, rawTypeKind: 0),
            _ => throw new BadImageFormatException($"The handle {handle.Kind} is not a type."),
        };
    }

    public Result Collect()
    {
        var definition = _reader.GetTypeDefinition(_owner.Handle);
        var context = _owner.GenericContext;

        Try(() =>
        {
            if (!definition.BaseType.IsNil)
            {
                Add(DecodeType(definition.BaseType, context), DependencyKind.BaseType, sourceMember: null);
            }

            foreach (var handle in definition.GetInterfaceImplementations())
            {
                Add(DecodeType(_reader.GetInterfaceImplementation(handle).Interface, context), DependencyKind.Interface, sourceMember: null);
            }

            AddAttributes(definition.GetCustomAttributes(), sourceMember: null, location: null);
            AddGenericConstraints(definition.GetGenericParameters(), context, sourceMember: null, location: null);
        });

        foreach (var handle in definition.GetFields())
        {
            if (_owner.GetField(handle) is not { } field)
                continue; // backing fields: the property gives the type

            Try(() =>
            {
                Add(_reader.GetFieldDefinition(handle).DecodeSignature(_provider, context), DependencyKind.FieldType, field);
                AddAttributes(_reader.GetFieldDefinition(handle).GetCustomAttributes(), field, location: null);
            });
        }

        foreach (var property in _owner.Properties)
        {
            Try(() =>
            {
                var propertyDefinition = _reader.GetPropertyDefinition(property.Handle);
                var signature = propertyDefinition.DecodeSignature(_provider, context);
                Add(signature.ReturnType, DependencyKind.PropertyType, property, location: property.Location);
                foreach (var parameterType in signature.ParameterTypes)
                {
                    Add(parameterType, DependencyKind.PropertyType, property, location: property.Location);
                }

                AddAttributes(propertyDefinition.GetCustomAttributes(), property, property.Location);
            });
        }

        foreach (var @event in _owner.Events)
        {
            Try(() =>
            {
                var eventDefinition = _reader.GetEventDefinition(@event.Handle);
                Add(DecodeType(eventDefinition.Type, context), DependencyKind.EventType, @event, location: @event.Location);
                AddAttributes(eventDefinition.GetCustomAttributes(), @event, @event.Location);
            });
        }

        foreach (var handle in definition.GetMethods())
        {
            var name = _reader.GetString(_reader.GetMethodDefinition(handle).Name);
            var isGenerated = MetadataNames.IsCompilerGeneratedMemberName(name);
            var owner = isGenerated ? FindGeneratedCodeOwner(name, generatedType: default) : new CodeOwner(_owner.GetSourceMember(handle), _owner.GetAccessor(handle));
            AddMethod(handle, context, owner, isGenerated);
        }

        _generatedTypeOwners = [];
        FindGeneratedTypeOwners(_generatedTypeOwners);
        foreach (var generatedType in _owner.GeneratedTypes)
        {
            AddGeneratedType(generatedType);
        }

        return new Result(_result, _accessorDependencies.ToDictionary(item => item.Key, item => (IReadOnlyList<Dependency>)item.Value.Dependencies));
    }

    private void AddGeneratedType(TypeDefinitionHandle handle)
    {
        var definition = _reader.GetTypeDefinition(handle);
        var context = new GenericContext([.. definition.GetGenericParameters().Select(parameter => _reader.GetString(_reader.GetGenericParameter(parameter).Name))], []);

        // The fields of a closure or a state machine are the captured variables
        foreach (var fieldHandle in definition.GetFields())
        {
            Try(() =>
            {
                var field = _reader.GetFieldDefinition(fieldHandle);
                var owner = FindGeneratedCodeOwner(memberName: null, handle);
                _currentAccessor = owner.Accessor;
                try
                {
                    Add(field.DecodeSignature(_provider, context), DependencyKind.LocalVariable, owner.Member);
                }
                finally
                {
                    _currentAccessor = null;
                }
            });
        }

        foreach (var methodHandle in definition.GetMethods())
        {
            var name = _reader.GetString(_reader.GetMethodDefinition(methodHandle).Name);
            AddMethod(methodHandle, context, FindGeneratedCodeOwner(name, handle), isGenerated: true);
        }
    }

    private void AddMethod(MethodDefinitionHandle handle, GenericContext typeContext, CodeOwner owner, bool isGenerated)
    {
        var sourceMember = owner.Member;
        _currentAccessor = owner.Accessor;
        try
        {
            AddMethodCore(handle, typeContext, sourceMember, isGenerated);
        }
        finally
        {
            _currentAccessor = null;
        }
    }

    private void AddMethodCore(MethodDefinitionHandle handle, GenericContext typeContext, MemberElement? sourceMember, bool isGenerated)
    {
        Try(() =>
        {
            var method = _reader.GetMethodDefinition(handle);
            var context = new GenericContext(typeContext.TypeParameters, [.. method.GetGenericParameters().Select(parameter => _reader.GetString(_reader.GetGenericParameter(parameter).Name))]);
            var body = _image.GetMethodBody(method);
            var sequencePoints = SequencePointMap.Create(_image, handle);
            var declarationLocation = sequencePoints?.First ?? (isGenerated ? null : sourceMember?.Location);

            var signature = method.DecodeSignature(_provider, context);
            Add(signature.ReturnType, DependencyKind.ReturnType, sourceMember, location: declarationLocation);
            foreach (var parameterType in signature.ParameterTypes)
            {
                Add(parameterType, DependencyKind.ParameterType, sourceMember, location: declarationLocation);
            }

            if (!isGenerated)
            {
                AddAttributes(method.GetCustomAttributes(), sourceMember, declarationLocation);
                foreach (var parameterHandle in method.GetParameters())
                {
                    AddAttributes(_reader.GetParameter(parameterHandle).GetCustomAttributes(), sourceMember, declarationLocation);
                }
            }

            AddGenericConstraints(method.GetGenericParameters(), context, sourceMember, declarationLocation);

            if (body is null)
                return;

            if (!body.LocalSignature.IsNil)
            {
                foreach (var localType in _reader.GetStandaloneSignature(body.LocalSignature).DecodeLocalSignature(_provider, context))
                {
                    Add(localType, DependencyKind.LocalVariable, sourceMember, location: declarationLocation);
                }
            }

            foreach (var region in body.ExceptionRegions)
            {
                if (region.Kind == ExceptionRegionKind.Catch && !region.CatchType.IsNil)
                {
                    Add(DecodeType(region.CatchType, context), DependencyKind.CatchClause, sourceMember, location: sequencePoints?.Find(region.HandlerOffset));
                }
            }

            foreach (var reference in ILScanner.Scan(body.GetILReader()))
            {
                var location = sequencePoints?.Find(reference.Offset);
                switch (reference.Kind)
                {
                    case ILTokenKind.Method:
                        AddMember(reference.Handle, DependencyKind.MethodCall, context, sourceMember, location);
                        break;

                    case ILTokenKind.ObjectCreation:
                        AddMember(reference.Handle, DependencyKind.ObjectCreation, context, sourceMember, location);
                        break;

                    case ILTokenKind.Field:
                        AddMember(reference.Handle, DependencyKind.FieldAccess, context, sourceMember, location);
                        break;

                    case ILTokenKind.Type:
                        Add(DecodeType(reference.Handle, context), DependencyKind.TypeUsage, sourceMember, location: location);
                        break;

                    case ILTokenKind.Token when reference.Handle.Kind is HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification:
                        Add(DecodeType(reference.Handle, context), DependencyKind.TypeUsage, sourceMember, location: location);
                        break;

                    case ILTokenKind.Token when reference.Handle.Kind is HandleKind.FieldDefinition:
                        AddMember(reference.Handle, DependencyKind.FieldAccess, context, sourceMember, location);
                        break;

                    case ILTokenKind.Token:
                        AddMember(reference.Handle, DependencyKind.MethodCall, context, sourceMember, location);
                        break;

                    case ILTokenKind.Signature when reference.Handle.Kind == HandleKind.StandaloneSignature:
                        var callSignature = _reader.GetStandaloneSignature((StandaloneSignatureHandle)reference.Handle).DecodeMethodSignature(_provider, context);
                        Add(callSignature.ReturnType, DependencyKind.TypeUsage, sourceMember, location: location);
                        foreach (var parameterType in callSignature.ParameterTypes)
                        {
                            Add(parameterType, DependencyKind.TypeUsage, sourceMember, location: location);
                        }

                        break;
                }
            }
        });
    }

    // A call, an object creation or a field access: the declaring type of the member, with its generic arguments, and the generic arguments of the method
    private void AddMember(EntityHandle handle, DependencyKind kind, GenericContext context, MemberElement? sourceMember, SourceLocation? location)
    {
        ImmutableArray<TypeReference> methodArguments = [];
        if (handle.Kind == HandleKind.MethodSpecification)
        {
            var specification = _reader.GetMethodSpecification((MethodSpecificationHandle)handle);
            methodArguments = specification.DecodeSignature(_provider, context);
            handle = specification.Method;
        }

        TypeReference declaringType;
        string name;
        MemberReferenceKind memberKind;
        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
                {
                    var method = _reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                    declaringType = _provider.GetNamedType(method.GetDeclaringType());
                    name = _reader.GetString(method.Name);
                    memberKind = MemberReferenceKind.Method;
                    break;
                }

            case HandleKind.FieldDefinition:
                {
                    var field = _reader.GetFieldDefinition((FieldDefinitionHandle)handle);
                    declaringType = _provider.GetNamedType(field.GetDeclaringType());
                    name = _reader.GetString(field.Name);
                    memberKind = MemberReferenceKind.Field;
                    break;
                }

            case HandleKind.MemberReference:
                {
                    var member = _reader.GetMemberReference((MemberReferenceHandle)handle);
                    name = _reader.GetString(member.Name);
                    memberKind = member.GetKind() == System.Reflection.Metadata.MemberReferenceKind.Field ? MemberReferenceKind.Field : MemberReferenceKind.Method;
                    switch (member.Parent.Kind)
                    {
                        case HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification:
                            declaringType = DecodeType(member.Parent, context);
                            break;

                        case HandleKind.MethodDefinition:
                            declaringType = _provider.GetNamedType(_reader.GetMethodDefinition((MethodDefinitionHandle)member.Parent).GetDeclaringType());
                            break;

                        default:
                            return;
                    }

                    break;
                }

            default:
                return;
        }

        var targetMember = new MemberReference(declaringType, name, memberKind);
        Add(declaringType, kind, sourceMember, targetMember, location);
        foreach (var argument in methodArguments)
        {
            Add(argument, kind, sourceMember, targetMember, location);
        }
    }

    private void AddAttributes(CustomAttributeHandleCollection handles, MemberElement? sourceMember, SourceLocation? location)
    {
        foreach (var attribute in AttributeElement.Create(_provider, _reader, handles))
        {
            if (CompilerAttributes.Contains(attribute.Type.FullName))
                continue;

            Add(attribute.Type, DependencyKind.Attribute, sourceMember, location: location);
            foreach (var type in attribute.GetTypeArguments())
            {
                Add(type, DependencyKind.Attribute, sourceMember, location: location);
            }
        }
    }

    private void AddGenericConstraints(GenericParameterHandleCollection parameters, GenericContext context, MemberElement? sourceMember, SourceLocation? location)
    {
        foreach (var parameterHandle in parameters)
        {
            foreach (var constraintHandle in _reader.GetGenericParameter(parameterHandle).GetConstraints())
            {
                Add(DecodeType(_reader.GetGenericParameterConstraint(constraintHandle).Type, context), DependencyKind.GenericConstraint, sourceMember, location: location);
            }
        }
    }

    // The member whose code the compiler moved to a lambda, a local function or a state machine. The name of a lambda or a local function
    // contains the name of the member ("<Run>b__0_0"). A closure has no such name ("<>c__DisplayClass0_0"): it belongs to the member that creates it.
    private CodeOwner FindGeneratedCodeOwner(string? memberName, TypeDefinitionHandle generatedType)
    {
        if (memberName is not null && MetadataNames.GetGeneratedCodeOwnerName(memberName) is { } ownerName && _owner.FindMemberByMethodName(ownerName) is { } member)
            return new CodeOwner(member, _owner.FindAccessorByMethodName(ownerName));

        for (var type = generatedType; !type.IsNil && type != _owner.Handle; type = _reader.GetTypeDefinition(type).GetDeclaringType())
        {
            if (_generatedTypeOwners is not null && _generatedTypeOwners.TryGetValue(type, out var owner))
                return owner;

            if (MetadataNames.GetGeneratedCodeOwnerName(_reader.GetString(_reader.GetTypeDefinition(type).Name)) is { } typeOwnerName)
                return new CodeOwner(_owner.FindMemberByMethodName(typeOwnerName), _owner.FindAccessorByMethodName(typeOwnerName));
        }

        return default;
    }

    // Finds the member that creates each closure and state machine (newobj, or initobj for a struct state machine)
    private void FindGeneratedTypeOwners(Dictionary<TypeDefinitionHandle, CodeOwner> owners)
    {
        if (_owner.GeneratedTypes.Count == 0)
            return;

        var generatedTypes = _owner.GeneratedTypes.ToHashSet();
        foreach (var handle in _reader.GetTypeDefinition(_owner.Handle).GetMethods())
        {
            var name = _reader.GetString(_reader.GetMethodDefinition(handle).Name);
            ScanCreatedTypes(handle, MetadataNames.IsCompilerGeneratedMemberName(name) ? FindGeneratedCodeOwner(name, generatedType: default) : new CodeOwner(_owner.GetSourceMember(handle), _owner.GetAccessor(handle)));
        }

        // The closures created by the code of a lambda or of another closure
        for (var pass = 0; pass < 8; pass++)
        {
            var count = owners.Count;
            foreach (var generatedType in _owner.GeneratedTypes)
            {
                foreach (var methodHandle in _reader.GetTypeDefinition(generatedType).GetMethods())
                {
                    ScanCreatedTypes(methodHandle, FindGeneratedCodeOwner(_reader.GetString(_reader.GetMethodDefinition(methodHandle).Name), generatedType));
                }
            }

            if (owners.Count == count)
                break;
        }

        void ScanCreatedTypes(MethodDefinitionHandle methodHandle, CodeOwner owner)
        {
            if (owner.Member is null || _image.GetMethodBody(_reader.GetMethodDefinition(methodHandle)) is not { } body)
                return;

            Try(() =>
            {
                foreach (var reference in ILScanner.Scan(body.GetILReader()))
                {
                    if (reference.Kind is ILTokenKind.ObjectCreation or ILTokenKind.Type && GetTypeDefinition(reference.Handle) is { IsNil: false } type && generatedTypes.Contains(type))
                    {
                        owners.TryAdd(type, owner);
                    }
                }
            });
        }
    }

    // The type definition of this assembly a token refers to: the type itself, or the declaring type of a method
    private TypeDefinitionHandle GetTypeDefinition(EntityHandle handle)
    {
        var typeHandle = handle.Kind switch
        {
            HandleKind.MethodDefinition => _reader.GetMethodDefinition((MethodDefinitionHandle)handle).GetDeclaringType(),
            HandleKind.MemberReference => MetadataNames.GetNamedTypeHandle(_reader, _reader.GetMemberReference((MemberReferenceHandle)handle).Parent),
            HandleKind.MethodSpecification => GetTypeDefinition(_reader.GetMethodSpecification((MethodSpecificationHandle)handle).Method),
            _ => MetadataNames.GetNamedTypeHandle(_reader, handle),
        };

        return typeHandle.Kind == HandleKind.TypeDefinition ? (TypeDefinitionHandle)typeHandle : default;
    }

    private TypeReference DecodeType(EntityHandle handle, GenericContext context) => DecodeType(_provider, _reader, handle, context);

    private void Add(TypeReference type, DependencyKind kind, MemberElement? sourceMember, MemberReference? targetMember = null, SourceLocation? location = null)
    {
        _namedTypes.Clear();
        type.CollectNamedTypes(_namedTypes);
        var accessor = _currentAccessor is { } current && current.AccessorOwner == sourceMember ? current : null;
        foreach (var target in _namedTypes)
        {
            if (IsExcluded(target))
                continue;

            if (accessor is not null)
            {
                if (!_accessorDependencies.TryGetValue(accessor, out var accessorDependencies))
                {
                    accessorDependencies = new AccessorDependencies();
                    _accessorDependencies.Add(accessor, accessorDependencies);
                }

                if (accessorDependencies.Keys.Add((target.FullName, kind, targetMember?.Name)))
                {
                    accessorDependencies.Dependencies.Add(new Dependency(_owner, accessor, target, targetMember, kind, location));
                }
            }

            var key = (target.FullName, kind, sourceMember, targetMember?.Name);
            if (_dependencies.ContainsKey(key))
                continue;

            var dependency = new Dependency(_owner, sourceMember, target, targetMember, kind, location);
            _dependencies.Add(key, dependency);
            _result.Add(dependency);
        }
    }

    private bool IsExcluded(TypeReference target)
    {
        var fullName = target.FullName;
        if (string.Equals(fullName, _owner.FullName, StringComparison.Ordinal) || fullName is "System.Void")
            return true;

        if (_architecture.IsCompilerGeneratedType(fullName))
            return true;

        // Types generated by the compiler in other assemblies, which cannot be referenced from the code
        if (target.Definition is null)
        {
            for (var type = target; type is not null; type = type.DeclaringType)
            {
                if (MetadataNames.IsGeneratedName(type.MetadataName))
                    return true;
            }
        }

        return false;
    }

    private static void Try(Action action)
    {
        try
        {
            action();
        }
        catch (BadImageFormatException)
        {
        }
    }

    // The dependencies of the type, and the ones of the code of each accessor of its properties and events
    public sealed record Result(IReadOnlyList<Dependency> Dependencies, Dictionary<MethodElement, IReadOnlyList<Dependency>> AccessorDependencies);

    // The member a piece of code belongs to, and the accessor when it is the code of an accessor of a property or an event
    private readonly record struct CodeOwner(MemberElement? Member, MethodElement? Accessor);

    private sealed class AccessorDependencies
    {
        public HashSet<(string Target, DependencyKind Kind, string? TargetMember)> Keys { get; } = [];

        public List<Dependency> Dependencies { get; } = [];
    }
}
