using System.Globalization;

namespace Meziantou.Framework.WasmEngine.Components.Validation;

/// <summary>
/// Checks that a type is a subtype of an expected type, such as the type of an argument and the type of an import. The
/// resource types of the expected type that can be bound (the imports of a component) are bound to the resource types
/// of the actual type, in order.
/// </summary>
internal sealed class ComponentTypeMatcher(ComponentTypeMapper mapper, IReadOnlySet<ComponentResourceType> bindable)
{
    public ComponentTypeMapper Mapper { get; } = mapper;

    /// <summary>Checks that <paramref name="actual"/> is a subtype of <paramref name="expected"/>.</summary>
    public void Match(ComponentExternType actual, ComponentExternType expected)
    {
        if (actual.Kind != expected.Kind)
            throw Error("expected " + Describe(expected.Kind) + ", found " + Describe(actual.Kind));

        switch (expected.Kind)
        {
            case ComponentExternKind.Type:
                MatchType(actual.Type!, expected.Type!);
                break;

            case ComponentExternKind.Function:
                CheckFunction(Mapper.Map((ComponentFunctionType)expected.Type!), (ComponentFunctionType)actual.Type!);
                break;

            case ComponentExternKind.Instance:
                MatchInstance((ComponentInstanceType)actual.Type!, (ComponentInstanceType)expected.Type!);
                break;

            case ComponentExternKind.Component:
                MatchComponent((ComponentComponentType)actual.Type!, Mapper.Map((ComponentComponentType)expected.Type!));
                break;

            case ComponentExternKind.CoreModule:
                MatchModule(actual.ModuleType!, expected.ModuleType!);
                break;

            default:
                CheckValueType(Mapper.Map(expected.ValueType!), actual.ValueType!);
                break;
        }
    }

    private void MatchType(ComponentType actual, ComponentType expected)
    {
        if (expected is ComponentResourceType resource && bindable.Contains(resource.Root) && !Mapper.IsMapped(resource.Root))
        {
            if (actual is not ComponentResourceType actualResource)
                throw Error("expected resource, found " + DescribeType(actual));

            Mapper.MapRoot(resource.Root, actualResource);
            return;
        }

        var mapped = Mapper.Map(expected);
        switch (mapped, actual)
        {
            case (ComponentResourceType a, ComponentResourceType b):
                if (!ReferenceEquals(a.Root, b.Root))
                    throw Error("resource types are not the same");

                break;

            case (ComponentValueType a, ComponentValueType b):
                CheckValueType(a, b);
                break;

            case (ComponentFunctionType a, ComponentFunctionType b):
                CheckFunction(a, b);
                break;

            case (ComponentInstanceType a, ComponentInstanceType b):
                MatchInstance(b, a);
                break;

            case (ComponentComponentType a, ComponentComponentType b):
                MatchComponent(b, a);
                break;

            case var (a, _):
                throw Error("expected " + DescribeType(a) + ", found " + DescribeType(actual));
        }

        // The type of the import is the type of the argument: the types that refer to it refer to the argument (and its name)
        if (expected is ComponentResourceType or ComponentRecordType or ComponentVariantType or ComponentLabelsType)
        {
            Mapper.MapObject(expected, actual);
        }
    }

    private void MatchInstance(ComponentInstanceType actual, ComponentInstanceType expected)
    {
        foreach (var export in expected.Exports)
        {
            var actualExport = actual.GetExport(export.Name) ?? throw Error("missing expected export `" + export.Name + "`");
            try
            {
                Match(actualExport, export.Type);
            }
            catch (WasmCompileException ex)
            {
                throw Error("type mismatch in instance export `" + export.Name + "`: " + ex.Message);
            }
        }
    }

    private static void MatchComponent(ComponentComponentType actual, ComponentComponentType expected)
    {
        // The imports of the expected type are given to the imports of the actual component (contravariance)
        var importMatcher = new ComponentTypeMatcher(new ComponentTypeMapper(), ComponentTypeMapper.CreateSet(actual.ImportedResources));
        foreach (var import in actual.Imports)
        {
            var expectedImport = Find(expected.Imports, import.Name) ?? throw Error("missing expected import `" + import.Name + "`");
            try
            {
                importMatcher.Match(expectedImport, import.Type);
            }
            catch (WasmCompileException ex)
            {
                throw Error("type mismatch in import `" + import.Name + "`: " + ex.Message);
            }
        }

        _ = importMatcher.Mapper.Freshen(actual.DefinedResources);
        var exportMatcher = new ComponentTypeMatcher(new ComponentTypeMapper(), ComponentTypeMapper.CreateSet(expected.DefinedResources));
        foreach (var export in expected.Exports)
        {
            var actualExport = Find(actual.Exports, export.Name) ?? throw Error("missing expected export `" + export.Name + "`");
            try
            {
                exportMatcher.Match(importMatcher.Mapper.Map(actualExport), export.Type);
            }
            catch (WasmCompileException ex)
            {
                throw Error("type mismatch in export `" + export.Name + "`: " + ex.Message);
            }
        }
    }

    private static void MatchModule(ComponentModuleType actual, ComponentModuleType expected)
    {
        foreach (var import in actual.Imports)
        {
            var expectedImport = expected.Imports.FirstOrDefault(other => string.Equals(other.Module, import.Module, StringComparison.Ordinal) && string.Equals(other.Name, import.Name, StringComparison.Ordinal))
                ?? throw Error("missing expected import `" + import.Module + "::" + import.Name + "`");

            try
            {
                MatchCore(expectedImport.Type, import.Type);
            }
            catch (WasmCompileException ex)
            {
                throw Error("type mismatch in import `" + import.Module + "::" + import.Name + "`: " + ex.Message);
            }
        }

        foreach (var export in expected.Exports)
        {
            var actualExport = actual.Exports.FirstOrDefault(other => string.Equals(other.Name, export.Name, StringComparison.Ordinal)) ?? throw Error("missing expected export `" + export.Name + "`");
            try
            {
                MatchCore(actualExport.Type, export.Type);
            }
            catch (WasmCompileException ex)
            {
                throw Error("type mismatch in export `" + export.Name + "`: " + ex.Message);
            }
        }
    }

    /// <summary>Checks that a core definition of type <paramref name="actual"/> can be given to an import of type <paramref name="expected"/>.</summary>
    public static void MatchCore(WasmExternType actual, WasmExternType expected)
    {
        if (actual.Kind != expected.Kind)
            throw Error("expected " + expected.Kind.ToString().ToLowerInvariant() + ", found " + actual.Kind.ToString().ToLowerInvariant());

        switch (expected, actual)
        {
            case (WasmFunctionType a, WasmFunctionType b):
                if (!AreEqual(a.Parameters, b.Parameters) || !AreEqual(a.Results, b.Results))
                    throw Error("type mismatch: expected: " + Format(a) + ", found " + Format(b));

                break;

            case (WasmGlobalType a, WasmGlobalType b):
                if (a.Mutable != b.Mutable)
                    throw Error("mismatch in global mutability");

                if (!AreEqual(a.ValueType, b.ValueType))
                    throw Error("expected global type " + a.ValueType + ", found " + b.ValueType);

                break;

            case (WasmTableType a, WasmTableType b):
                if (!AreEqual(a.ElementType, b.ElementType))
                    throw Error("expected table element type " + a.ElementType + ", found " + b.ElementType);

                if (a.Is64 != b.Is64 || !AreLimitsCompatible(b.Minimum, b.Maximum, a.Minimum, a.Maximum))
                    throw Error("mismatch in table limits");

                break;

            case (WasmMemoryType a, WasmMemoryType b):
                if (a.Shared != b.Shared)
                    throw Error("mismatch in the shared flag for memories");

                if (a.Is64 != b.Is64)
                    throw Error("mismatch in index type used for memories");

                if (!AreLimitsCompatible(b.Minimum, b.Maximum, a.Minimum, a.Maximum))
                    throw Error("mismatch in memory limits");

                break;

            case (WasmTagType a, WasmTagType b):
                if (!AreEqual(a.Parameters, b.Parameters))
                    throw Error("tag types are not the same");

                break;
        }
    }

    private static bool AreLimitsCompatible(ulong actualMinimum, ulong? actualMaximum, ulong expectedMinimum, ulong? expectedMaximum)
    {
        if (actualMinimum < expectedMinimum)
            return false;

        return expectedMaximum is not { } maximum || (actualMaximum is { } actual && actual <= maximum);
    }

    private static bool AreEqual(WasmValueType[] left, WasmValueType[] right)
    {
        if (left.Length != right.Length)
            return false;

        for (var i = 0; i < left.Length; i++)
        {
            if (!AreEqual(left[i], right[i]))
                return false;
        }

        return true;
    }

    // The concrete reference types are not compared: the type indices are not kept
    private static bool AreEqual(WasmValueType left, WasmValueType right) => left == right || (left.Kind == WasmValueKind.Ref && right.Kind == WasmValueKind.Ref && IsConcrete(left.Heap) && IsConcrete(right.Heap));

    private static bool IsConcrete(WasmHeapKind heap) => heap is WasmHeapKind.ConcreteFunc or WasmHeapKind.ConcreteArray or WasmHeapKind.ConcreteStruct or WasmHeapKind.ConcreteExn;

    private static string Format(WasmFunctionType type)
    {
        var parameters = type.Parameters.Length == 0 ? "" : " (param " + string.Join(' ', type.Parameters) + ")";
        var results = type.Results.Length == 0 ? "" : " (result " + string.Join(' ', type.Results) + ")";
        return "(func" + parameters + results + ")";
    }

    // ------------------------------------------------------------------------------------------------------------
    // Value types and function types
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>Checks that two function types are equal (the resource types are compared by identity).</summary>
    public static void CheckFunction(ComponentFunctionType expected, ComponentFunctionType actual)
    {
        if (expected.IsAsync != actual.IsAsync)
            throw Error(expected.IsAsync ? "expected an async function, found a sync function" : "expected a sync function, found an async function");

        if (expected.Parameters.Length != actual.Parameters.Length)
            throw Error("expected " + Format(expected.Parameters.Length) + " parameters, found " + Format(actual.Parameters.Length));

        for (var i = 0; i < expected.Parameters.Length; i++)
        {
            var (expectedName, expectedType) = expected.Parameters[i];
            var (actualName, actualType) = actual.Parameters[i];
            if (!string.Equals(expectedName, actualName, StringComparison.Ordinal))
                throw Error("expected parameter named `" + expectedName + "`, found `" + actualName + "`");

            Wrap(() => CheckValueType(expectedType, actualType), "type mismatch in function parameter `" + expectedName + "`");
        }

        switch (expected.Result, actual.Result)
        {
            case (null, null):
                break;
            case (null, _):
                throw Error("expected no result, found one");
            case (_, null):
                throw Error("expected a result, found none");
            case var (a, b):
                Wrap(() => CheckValueType(a, b), "type mismatch with result type");
                break;
        }
    }

    /// <summary>Whether two value types are equal (the resource types are compared by identity).</summary>
    public static bool AreEqual(ComponentValueType expected, ComponentValueType actual)
    {
        try
        {
            CheckValueType(expected, actual);
            return true;
        }
        catch (WasmCompileException)
        {
            return false;
        }
    }

    /// <summary>Checks that two value types are equal (the resource types are compared by identity).</summary>
    public static void CheckValueType(ComponentValueType expected, ComponentValueType actual)
    {
        if (ReferenceEquals(expected, actual))
            return;

        if (expected is ComponentPrimitiveType expectedPrimitive)
        {
            if (actual is not ComponentPrimitiveType actualPrimitive)
                throw Error("expected primitive, found " + Describe(actual));

            if (expectedPrimitive.Kind != actualPrimitive.Kind)
                throw Error("expected primitive `" + Describe(expected) + "` found primitive `" + Describe(actual) + "`");

            return;
        }

        if (expected.Kind != actual.Kind)
            throw Error("expected " + Describe(expected) + ", found " + Describe(actual));

        switch (expected, actual)
        {
            case (ComponentRecordType a, ComponentRecordType b):
                if (a.Fields.Length != b.Fields.Length)
                    throw Error("expected " + Format(a.Fields.Length) + " fields, found " + Format(b.Fields.Length));

                for (var i = 0; i < a.Fields.Length; i++)
                {
                    if (!string.Equals(a.Fields[i].Name, b.Fields[i].Name, StringComparison.Ordinal))
                        throw Error("expected field name `" + a.Fields[i].Name + "`, found `" + b.Fields[i].Name + "`");

                    var (x, y) = (a.Fields[i].Type, b.Fields[i].Type);
                    Wrap(() => CheckValueType(x, y), "type mismatch in record field `" + a.Fields[i].Name + "`");
                }

                break;

            case (ComponentVariantType a, ComponentVariantType b):
                if (a.Cases.Length != b.Cases.Length)
                    throw Error("expected " + Format(a.Cases.Length) + " cases, found " + Format(b.Cases.Length));

                for (var i = 0; i < a.Cases.Length; i++)
                {
                    var (name, x) = a.Cases[i];
                    var (otherName, y) = b.Cases[i];
                    if (!string.Equals(name, otherName, StringComparison.Ordinal))
                        throw Error("expected case named `" + name + "`, found `" + otherName + "`");

                    switch (x, y)
                    {
                        case (null, null):
                            break;
                        case (null, _):
                            throw Error("expected case `" + name + "` to have no type");
                        case (_, null):
                            throw Error("expected case `" + name + "` to have a type, found none");
                        default:
                            Wrap(() => CheckValueType(x, y), "type mismatch in variant case `" + name + "`");
                            break;
                    }
                }

                break;

            case (ComponentTupleType a, ComponentTupleType b):
                if (a.Types.Length != b.Types.Length)
                    throw Error("expected " + Format(a.Types.Length) + " types, found " + Format(b.Types.Length));

                for (var i = 0; i < a.Types.Length; i++)
                {
                    var (x, y, index) = (a.Types[i], b.Types[i], i);
                    Wrap(() => CheckValueType(x, y), "type mismatch in tuple field " + Format(index));
                }

                break;

            case (ComponentLabelsType a, ComponentLabelsType b):
                if (!a.Labels.AsSpan().SequenceEqual(b.Labels.AsSpan(), StringComparer.Ordinal))
                    throw Error("mismatch in " + (a is ComponentFlagsType ? "flags" : "enum") + " elements");

                break;

            case (ComponentListType a, ComponentListType b):
                if (a.FixedLength != b.FixedLength)
                    throw Error("mismatch in list length");

                Wrap(() => CheckValueType(a.ElementType, b.ElementType), "type mismatch in list element");
                break;

            case (ComponentOptionType a, ComponentOptionType b):
                Wrap(() => CheckValueType(a.Type, b.Type), "type mismatch in option");
                break;

            case (ComponentResultType a, ComponentResultType b):
                CheckOptional(a.Ok, b.Ok, "ok");
                CheckOptional(a.Error, b.Error, "err");
                break;

            case (ComponentHandleType a, ComponentHandleType b):
                if (!ReferenceEquals(a.Resource.Root, b.Resource.Root))
                    throw Error("resource types are not the same");

                break;

            case (ComponentAsyncValueType a, ComponentAsyncValueType b):
                CheckOptional(a.ElementType, b.ElementType, a is ComponentStreamType ? "stream" : "future");
                break;

            case (ComponentMapType a, ComponentMapType b):
                Wrap(() => CheckValueType(a.KeyType, b.KeyType), "type mismatch in map key");
                Wrap(() => CheckValueType(a.ValueType, b.ValueType), "type mismatch in map value");
                break;
        }
    }

    private static void CheckOptional(ComponentValueType? expected, ComponentValueType? actual, string name)
    {
        switch (expected, actual)
        {
            case (null, null):
                break;
            case (null, _):
                throw Error("expected " + name + " type to not be present");
            case (_, null):
                throw Error("expected " + name + " type, but found none");
            default:
                Wrap(() => CheckValueType(expected, actual), "type mismatch in " + name + " variant");
                break;
        }
    }

    private static void Wrap(Action check, string context)
    {
        try
        {
            check();
        }
        catch (WasmCompileException ex)
        {
            throw Error(context + ": " + ex.Message);
        }
    }

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Describe(ComponentValueType type) => type.Kind switch
    {
        ComponentValueTypeKind.ErrorContext => "error-context",
        _ => type.Kind.ToString().ToLowerInvariant(),
    };

    private static string DescribeType(ComponentType type) => type switch
    {
        ComponentResourceType => "resource",
        ComponentValueType => "defined type",
        ComponentFunctionType => "func type",
        ComponentInstanceType => "instance type",
        _ => "component type",
    };

    public static string Describe(ComponentExternKind kind) => kind switch
    {
        ComponentExternKind.CoreModule => "module",
        ComponentExternKind.Function => "func",
        _ => kind.ToString().ToLowerInvariant(),
    };

    private static ComponentExternType? Find(System.Collections.Immutable.ImmutableArray<ComponentExternDescriptor> descriptors, string name)
    {
        foreach (var descriptor in descriptors)
        {
            if (string.Equals(descriptor.Name, name, StringComparison.Ordinal))
                return descriptor.Type;
        }

        return null;
    }

    private static WasmCompileException Error(string message) => new(message);
}
