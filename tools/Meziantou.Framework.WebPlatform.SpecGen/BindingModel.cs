using System.Reflection;
using Meziantou.Framework.WebPlatform;
using Meziantou.Framework.WebPlatform.Dom;
using SpecGen.Idl;

namespace SpecGen.Idl;

internal sealed class AttributeModel
{
    public required IdlMember Member { get; init; }

    public required PropertyInfo Property { get; init; }

    public IdlType Type { get; init; } = null!;
}

internal sealed class OperationModel
{
    public required string Name { get; init; }

    public bool IsStatic { get; init; }

    public List<IdlMember> Overloads { get; } = [];

    public List<MethodBase> ClrMethods { get; } = [];

    /// <summary>Whether the operation is a toJSON operation with [Default], whose steps are the default toJSON steps (https://webidl.spec.whatwg.org/#default-tojson-steps).</summary>
    public bool IsDefaultToJson => Name == "toJSON" && !IsStatic && Overloads.Count == 1 && Overloads[0].GetExtAttr("Default") is not null;

    /// <summary>The value of the <c>length</c> property of the function object (https://webidl.spec.whatwg.org/#dfn-create-operation-function).</summary>
    public int Length => Overloads.Min(o => o.Arguments.Count(a => !a.Optional && !a.Variadic));
}

internal sealed class InterfaceModel
{
    public required string Name { get; init; }

    public required IdlDefinition Definition { get; init; }

    public Type? ClrType { get; init; }

    public InterfaceModel? Parent { get; set; }

    public List<AttributeModel> Attributes { get; } = [];

    public List<OperationModel> Operations { get; } = [];

    public List<IdlMember> Constants { get; } = [];

    public OperationModel? Constructor { get; set; }

    public MethodInfo? IndexedGetter { get; set; }

    private bool? _specialOperationsHaveCEReactions;
    private bool? _hasHtmlConstructor;

    public MethodInfo? NamedGetter { get; set; }

    public MethodInfo? SupportedPropertyNames { get; set; }

    public MethodInfo? NamedSetter { get; set; }

    public MethodInfo? IndexedSetter { get; set; }

    public MethodInfo? NamedDeleter { get; set; }

    public PropertyInfo? LengthProperty { get; set; }

    public IdlMember? Iterable { get; set; }

    public AttributeModel? StringifierAttribute { get; set; }

    public bool HasStringifierOperation { get; set; }

    public bool IsLegacyUnenumerableNamedProperties => Definition.HasExtAttr("LegacyUnenumerableNamedProperties");

    /// <summary>Whether the indexed and named setters and deleters are decorated with [CEReactions] (e.g. DOMStringMap).</summary>
    public bool SpecialOperationsHaveCEReactions => _specialOperationsHaveCEReactions ??= Definition.Members.Exists(m => m.Special is "setter" or "deleter" && m.GetExtAttr("CEReactions") is not null);

    /// <summary>Whether the constructor is an HTML element constructor (https://html.spec.whatwg.org/multipage/dom.html#htmlconstructor).</summary>
    public bool HasHtmlConstructor => _hasHtmlConstructor ??= Definition.Members.Exists(m => m.Kind == IdlMemberKind.Constructor && m.GetExtAttr("HTMLConstructor") is not null);

    /// <summary>The setlike declaration of the interface (https://webidl.spec.whatwg.org/#idl-setlike), if any.</summary>
    public IdlMember? Setlike => Definition.Members.Find(m => m.Kind == IdlMemberKind.Setlike);

    /// <summary>The maplike declaration of the interface (https://webidl.spec.whatwg.org/#idl-maplike), if any.</summary>
    public IdlMember? Maplike => Definition.Members.Find(m => m.Kind == IdlMemberKind.Maplike);

    /// <summary>Whether the interface, or an interface it inherits from (e.g. Document for XMLDocument), has [LegacyOverrideBuiltIns].</summary>
    public bool IsLegacyOverrideBuiltIns => Definition.HasExtAttr("LegacyOverrideBuiltIns") || Parent is { IsLegacyOverrideBuiltIns: true };

    public bool SupportsIndexedProperties => IndexedGetter is not null && LengthProperty is not null;

    public bool SupportsNamedProperties => NamedGetter is not null && SupportedPropertyNames is not null;

    public bool IsA(string name)
    {
        for (var model = this; model is not null; model = model.Parent)
        {
            if (model.Name == name)
                return true;
        }

        return false;
    }

    public override string ToString() => Name;
}

/// <summary>Maps the IDL definitions (from webref) to the .NET types and members annotated with <see cref="IdlInterfaceAttribute"/> and <see cref="IdlMemberAttribute"/>.</summary>
internal sealed partial class BindingModel
{
    public IdlModel Idl { get; }

    public Dictionary<string, InterfaceModel> Interfaces { get; } = new(StringComparer.Ordinal);

    public Dictionary<Type, InterfaceModel> InterfacesByClrType { get; } = [];

    /// <summary>The .NET types of the dictionaries and enumerations.</summary>
    public Dictionary<string, Type> ClrTypes { get; } = new(StringComparer.Ordinal);

    internal BindingModel(IdlModel idl)
    {
        Idl = idl;
        var assembly = typeof(Node).Assembly;
        foreach (var type in assembly.GetTypes())
        {
            var attribute = type.GetCustomAttribute<IdlInterfaceAttribute>(inherit: false);
            if (attribute is null)
                continue;

            var definition = idl.Get(attribute.Name);
            if (definition is null)
                continue;

            if (definition.Kind is IdlDefinitionKind.Interface or IdlDefinitionKind.Namespace && type.IsClass)
            {
                var model = new InterfaceModel { Name = definition.Name, Definition = definition, ClrType = type };
                Interfaces[model.Name] = model;
                InterfacesByClrType[type] = model;
            }
            else
            {
                ClrTypes[definition.Name] = type;
            }
        }

        foreach (var model in Interfaces.Values)
        {
            if (model.Definition.Inheritance is { } parentName)
            {
                model.Parent = Interfaces.GetValueOrDefault(parentName);
            }

            if (model.ClrType is not null)
            {
                BindMembers(model);
            }
        }

        // The indexed and named properties are inherited (e.g. RadioNodeList inherits the indexed getter of NodeList)
        foreach (var model in Interfaces.Values)
        {
            for (var parent = model.Parent; parent is not null; parent = parent.Parent)
            {
                model.IndexedGetter ??= parent.IndexedGetter;
                model.IndexedSetter ??= parent.IndexedSetter;
                model.LengthProperty ??= parent.LengthProperty;
                if (model.NamedGetter is null && parent.NamedGetter is not null)
                {
                    model.NamedGetter = parent.NamedGetter;
                    model.SupportedPropertyNames = parent.SupportedPropertyNames;
                }
            }
        }
    }

    private static int GetDepth(Type type)
    {
        var depth = 0;
        for (var current = type; current is not null; current = current.BaseType)
        {
            depth++;
        }

        return depth;
    }

    private static IEnumerable<string> GetIdlNames(MemberInfo member) => member.GetCustomAttributes<IdlMemberAttribute>(inherit: false).Select(a => a.Name);

    private void BindMembers(InterfaceModel model)
    {
        var type = model.ClrType!;
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        var properties = type.GetProperties(Flags);
        var methods = type.GetMethods(Flags);

        // The members of namespaces are static .NET members (https://webidl.spec.whatwg.org/#idl-namespaces)
        var isNamespace = model.Definition.Kind == IdlDefinitionKind.Namespace;
        foreach (var member in model.Definition.Members)
        {
            if (isNamespace && member.Special.Length == 0)
            {
                member.Special = "static";
            }

            switch (member.Kind)
            {
                case IdlMemberKind.Const:
                    model.Constants.Add(member);
                    break;

                case IdlMemberKind.Attribute:
                {
                    var property = properties.FirstOrDefault(p => p.GetIndexParameters().Length == 0 && GetIdlNames(p).Contains(member.Name) && (p.GetMethod?.IsStatic ?? false) == member.IsStatic);
                    if (property is null)
                        break;

                    var attributeModel = new AttributeModel { Member = member, Property = property, Type = Idl.ResolveTypedefs(member.Type!) };
                    model.Attributes.Add(attributeModel);
                    if (member.Special == "stringifier")
                    {
                        model.StringifierAttribute = attributeModel;
                    }

                    if (member.Name == "length")
                    {
                        model.LengthProperty = property;
                    }

                    break;
                }

                case IdlMemberKind.Operation:
                {
                    ResolveTypes(member);
                    if (member.Special == "stringifier" && member.Name.Length == 0)
                    {
                        model.HasStringifierOperation = true;
                        break;
                    }

                    if (member.Name.Length == 0)
                    {
                        // Anonymous special operations: the .NET indexer or method is annotated with the special keyword ("getter", "setter", "deleter")
                        if (member.Special is "getter" or "setter" && member.Arguments.Count > 0 && member.Arguments[0].Type.Name == "unsigned long")
                        {
                            var indexer = properties.FirstOrDefault(p => p.GetIndexParameters() is [{ ParameterType: var t }] && t == typeof(uint) && GetIdlNames(p).Contains(member.Special));
                            if (member.Special == "getter" && indexer?.GetMethod is { } getMethod)
                            {
                                model.IndexedGetter = getMethod;
                            }
                            else if (member.Special == "setter" && indexer?.SetMethod is { } setMethod)
                            {
                                model.IndexedSetter = setMethod;
                            }
                        }
                        else if (member.Special is "getter" or "setter" && member.Arguments.Count > 0 && member.Arguments[0].Type.Name == "DOMString")
                        {
                            var indexer = properties.FirstOrDefault(p => p.GetIndexParameters() is [{ ParameterType: var t }] && t == typeof(string) && GetIdlNames(p).Contains(member.Special));
                            if (member.Special == "getter" && indexer?.GetMethod is { } getMethod)
                            {
                                model.NamedGetter = getMethod;
                                model.SupportedPropertyNames = type.GetMethod("GetSupportedPropertyNames", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                            }
                            else if (member.Special == "setter" && indexer?.SetMethod is { } setMethod)
                            {
                                model.NamedSetter = setMethod;
                            }
                        }
                        else if (member.Special == "deleter")
                        {
                            model.NamedDeleter = methods.FirstOrDefault(m => GetIdlNames(m).Contains("deleter"));
                        }

                        break;
                    }

                    // Methods hidden by a method of a derived type (e.g. HTMLFormControlsCollection.namedItem) are ignored
                    var clrMethods = methods.Where(m => !m.IsSpecialName && GetIdlNames(m).Contains(member.Name) && m.IsStatic == member.IsStatic)
                        .GroupBy(m => string.Join(",", m.GetParameters().Select(p => p.ParameterType.FullName)))
                        .Select(g => g.OrderByDescending(m => GetDepth(m.DeclaringType!)).First())
                        .ToList();

                    // A static operation of an interface shadows the static operation of the same name of the interfaces it
                    // inherits from, whatever its arguments (e.g. TaskSignal.any and AbortSignal.any)
                    if (member.IsStatic && clrMethods.Count > 1)
                    {
                        var depth = clrMethods.Max(m => GetDepth(m.DeclaringType!));
                        clrMethods = clrMethods.Where(m => GetDepth(m.DeclaringType!) == depth).ToList();
                    }

                    // The [Default] toJSON operations do not need a .NET method: the bindings implement the default toJSON steps
                    if (clrMethods.Count == 0 && !(member.Name == "toJSON" && member.GetExtAttr("Default") is not null))
                        break;

                    if (member.Special == "getter")
                    {
                        var method = clrMethods.OrderBy(m => m.GetParameters().Length).First();
                        if (member.Arguments[0].Type.Name == "unsigned long")
                        {
                            model.IndexedGetter = method;
                        }
                        else
                        {
                            model.NamedGetter = method;
                            model.SupportedPropertyNames = type.GetMethod("GetSupportedPropertyNames", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                        }
                    }
                    else if (member.Special == "setter" && member.Arguments[0].Type.Name == "DOMString")
                    {
                        // A named setter operation (e.g. Storage.setItem)
                        model.NamedSetter = clrMethods.First(m => m.GetParameters().Length == 2);
                    }
                    else if (member.Special == "deleter")
                    {
                        // A named deleter operation (e.g. Storage.removeItem)
                        model.NamedDeleter = clrMethods.First(m => m.GetParameters().Length == 1);
                    }

                    var operation = model.Operations.Find(o => o.Name == member.Name && o.IsStatic == member.IsStatic);
                    if (operation is null)
                    {
                        operation = new OperationModel { Name = member.Name, IsStatic = member.IsStatic };
                        operation.ClrMethods.AddRange(clrMethods.OrderBy(m => m.GetParameters().Length));
                        model.Operations.Add(operation);
                    }

                    operation.Overloads.Add(member);
                    break;
                }

                case IdlMemberKind.Constructor:
                {
                    ResolveTypes(member);
                    var constructors = type.GetConstructors().Where(c => GetIdlNames(c).Contains("constructor")).OrderBy(c => c.GetParameters().Length).ToList();
                    if (constructors.Count == 0)
                        break;

                    if (model.Constructor is null)
                    {
                        model.Constructor = new OperationModel { Name = "constructor" };
                        model.Constructor.ClrMethods.AddRange(constructors);
                    }

                    model.Constructor.Overloads.Add(member);
                    break;
                }

                case IdlMemberKind.Iterable:
                    model.Iterable = member;
                    break;
            }
        }
    }

    private void ResolveTypes(IdlMember member)
    {
        foreach (var argument in member.Arguments)
        {
            argument.Type = Idl.ResolveTypedefs(argument.Type);
        }
    }

    /// <summary>Returns the interface model of a .NET object (the most derived type annotated with an IDL interface).</summary>
    public InterfaceModel? GetInterface(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (InterfacesByClrType.TryGetValue(current, out var model))
                return model;
        }

        return null;
    }
}
