using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Meziantou.Framework.WebPlatform;
using Meziantou.Framework.WebPlatform.Dom;
using SpecGen.Idl;

namespace SpecGen;

internal sealed partial class WebIdlBindingsGenerator
{
    private const string HelpersClass = "WebIdlHelpers<TAdapter, TValue>";

    private static readonly Type UnionValueType = typeof(Node).Assembly.GetType("Meziantou.Framework.WebPlatform.Dom.IUnionValue", throwOnError: true)!;

    /// <summary>Whether the type is a .NET struct that represents an IDL union: it has implicit conversions from the member types.</summary>
    private static bool IsUnionStruct(Type type) => UnionValueType.IsAssignableFrom(type)
        || (type.IsValueType && !type.IsPrimitive && !type.IsEnum && type.GetMethods(BindingFlags.Public | BindingFlags.Static).Any(m => m.Name == "op_Implicit" && m.ReturnType == type));

    /// <summary>Adds a member to the helper class (once) and returns its name.</summary>
    private string GetHelper(string name, string code)
    {
        _helpers.TryAdd(name, code);
        return name;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // CLR to JavaScript
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Returns the C# expression that converts a .NET value of type <paramref name="type"/> to a JavaScript value.</summary>
    private string EmitToJs(Type type, IdlType? idlType, string expression)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        var isNullableValueType = Nullable.GetUnderlyingType(type) is not null;
        if (type == typeof(void))
            return "r.Adapter.Undefined";

        if (underlying == typeof(string))
            return $"r.FromString({expression})";

        if (underlying == typeof(bool))
            return $"r.FromBoolean({expression})";

        // The nullable value types are tested with HasValue (a pattern variable would conflict with the one of another
        // conversion in the same scope, e.g. two nullable members of a dictionary)
        if (IsNumeric(underlying))
            return isNullableValueType ? $"({expression}.HasValue ? r.FromNumber((double){expression}.GetValueOrDefault()) : r.Adapter.Null)" : $"r.FromNumber((double){expression})";

        if (underlying == typeof(System.Numerics.BigInteger))
            return isNullableValueType ? $"({expression}.HasValue ? r.Adapter.FromBigInt({expression}.GetValueOrDefault()) : r.Adapter.Null)" : $"r.Adapter.FromBigInt({expression})";

        if (underlying.IsEnum && underlying.GetCustomAttribute<IdlInterfaceAttribute>() is not null)
            return isNullableValueType ? $"({expression}.HasValue ? r.Adapter.FromString({GetEnumToString(underlying)}({expression}.GetValueOrDefault())) : r.Adapter.Null)" : $"r.Adapter.FromString({GetEnumToString(underlying)}({expression}))";

        if (underlying == typeof(CustomElementConstructor))
            return $"r.FromCustomElementConstructor({expression})";

        // https://drafts.csswg.org/web-animations-1/#dom-keyframeeffect-getkeyframes: objects whose property values are the other members
        if (underlying == typeof(IReadOnlyList<Meziantou.Framework.WebPlatform.Animations.ComputedKeyframe>))
            return $"r.FromComputedKeyframes({expression})";

        // A buffer source type (e.g. the Uint8Array of TextEncoder.encode()): a new object containing a copy of the bytes
        if (idlType is not null && _idl.ResolveTypedefs(idlType) is var resolvedType && IsBufferIdlType(resolvedType) && (underlying == typeof(byte[]) || underlying == typeof(Memory<byte>) || underlying == typeof(ReadOnlyMemory<byte>)))
        {
            var kind = "global::Meziantou.Framework.WebPlatform.Scripting.JsBufferKind." + resolvedType.Name;
            return underlying == typeof(byte[]) ? $"({expression} is {{ }} bytes ? r.FromBuffer({kind}, bytes) : r.Adapter.Null)"
                : isNullableValueType ? $"({expression} is {{ }} bytes ? r.FromBuffer({kind}, bytes) : r.Adapter.Null)"
                : $"r.FromBuffer({kind}, {expression})";
        }

        // The elements of a typed array (e.g. float[] for a Float32Array), in the byte order of the platform
        if (idlType is not null && _idl.ResolveTypedefs(idlType) is var resolvedArrayType && IsBufferIdlType(resolvedArrayType) && underlying.IsArray && underlying.GetElementType() is { IsPrimitive: true })
        {
            var kind = "global::Meziantou.Framework.WebPlatform.Scripting.JsBufferKind." + resolvedArrayType.Name;
            return $"({expression} is {{ }} elements ? r.FromBuffer({kind}, unsafe(global::System.Runtime.InteropServices.MemoryMarshal.AsBytes(elements.AsSpan())).ToArray()) : r.Adapter.Null)";
        }

        if (typeof(Delegate).IsAssignableFrom(underlying))
            return $"r.GetDelegateFunction({expression})";

        if (underlying == typeof(Task))
            return $"r.ToPromise({expression})";

        if (underlying.IsGenericType && underlying.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = underlying.GetGenericArguments()[0];
            _taskResultTypes.Add(underlying);
            var promiseType = idlType is { Generic: "Promise" } ? idlType.Subtypes[0] : null;

            // A null result of a promise whose type includes undefined (e.g. Promise<(Client or undefined)>) is undefined
            if (promiseType is not null && !resultType.IsValueType && resultType != typeof(object) && FlattenUnion(_idl.ResolveTypedefs(promiseType)).Any(t => t.Name == "undefined"))
                return $"r.ToPromise({expression}, static (r, value) => value is null ? r.Adapter.Undefined : {EmitToJs(resultType, promiseType, "value")})";

            return $"r.ToPromise({expression}, static (r, value) => {EmitToJs(resultType, promiseType, "value")})";
        }

        if (UnionValueType.IsAssignableFrom(underlying))
            return $"r.ToJs((object?){expression})";

        // A Date object that is not nullable in the IDL (e.g. HTMLMediaElement.getStartDate(), an object): a null DateTime is a
        // Date object whose time value is NaN (an "Invalid Date")
        if (type == typeof(DateTime?) && idlType is { Name: "object", Nullable: false })
            return $"r.FromDate({expression})";

        if (underlying == typeof(object) || underlying == typeof(DateTime))
            return $"r.ToJs((object?){expression})";

        if (_model.GetInterface(underlying) is not null || underlying == typeof(Window))
            return $"r.Wrap({expression})";

        // An abstract dictionary (e.g. ReportBody): the conversion of the dictionary of its runtime type
        if (underlying.IsAbstract && underlying.GetCustomAttribute<IdlInterfaceAttribute>() is { } abstractDictionary && _idl.Get(abstractDictionary.Name)?.Kind == IdlDefinitionKind.Dictionary)
            return $"r.ToJs((object?){expression})";

        if (underlying.GetCustomAttribute<IdlInterfaceAttribute>() is { } dictionary && _model.ClrTypes.ContainsKey(dictionary.Name) && _idl.Get(dictionary.Name)?.Kind == IdlDefinitionKind.Dictionary)
            return $"({expression} is {{ }} dictionaryValue ? {GetDictionaryToJs(underlying)}(r, dictionaryValue) : r.Adapter.Null)";

        // A record (https://webidl.spec.whatwg.org/#js-record): an object with a data property per entry
        if (idlType is not null && _idl.ResolveTypedefs(idlType) is { Generic: "record" } recordType
            && underlying.GetInterfaces().Append(underlying).FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0] is { IsGenericType: true } pairType
            && pairType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>) && pairType.GetGenericArguments()[0] == typeof(string))
        {
            var valueType = pairType.GetGenericArguments()[1];
            var valueIdlType = recordType.Subtypes[1];
            var valueToJs = EmitToJs(valueType, valueIdlType, "v");

            // A null .NET value is undefined when the value type includes undefined, e.g. (USVString or undefined)
            if (FlattenUnion(_idl.ResolveTypedefs(valueIdlType)).Any(t => t.Name == "undefined") && (!valueType.IsValueType || Nullable.GetUnderlyingType(valueType) is not null))
            {
                valueToJs = $"v is null ? r.Adapter.Undefined : {valueToJs}";
            }

            return $"r.FromRecord({expression}, static (r, v) => {valueToJs})";
        }

        if (underlying != typeof(string) && typeof(IEnumerable).IsAssignableFrom(underlying))
        {
            var elementType = underlying.IsArray ? underlying.GetElementType() : underlying.GetInterfaces().Concat([underlying]).FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];
            if (elementType is null)
                return $"r.ToJs((object?){expression})";

            var elementIdlType = idlType is { Generic: "sequence" or "FrozenArray" } ? idlType.Subtypes[0] : null;
            var method = idlType is { Generic: "FrozenArray" } ? "FromFrozenArray" : "FromSequence";
            return $"r.{method}({expression}, static (r, e) => {EmitToJs(elementType, elementIdlType, "e")})";
        }

        return $"r.ToJs((object?){expression})";
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Enumerations (https://webidl.spec.whatwg.org/#js-enumeration)
    // ----------------------------------------------------------------------------------------------------------------

    private static string EnumValueName(FieldInfo field) => field.GetCustomAttribute<IdlMemberAttribute>()?.Name ?? field.Name;

    /// <summary>Returns the helper that converts an IDL enumeration value (a valid string) to a .NET enum.</summary>
    private string GetEnumConverter(Type enumType)
    {
        if (_enumConverters.TryGetValue(enumType, out var name))
            return name;

        name = "ToEnum_" + SafeName(enumType);
        _enumConverters[enumType] = HelpersClass + "." + name;
        var code = new StringBuilder();
        code.Append($"internal static {TypeName(enumType)} {name}(string value) => value switch\n{{\n");
        foreach (var field in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            code.Append($"    {Literal(EnumValueName(field))} => {TypeName(enumType)}.{field.Name},\n");
        }

        code.Append("    _ => throw new global::System.ArgumentException(\"Invalid enumeration value\", nameof(value)),\n};");
        GetHelper(name, code.ToString());
        return _enumConverters[enumType];
    }

    /// <summary>Returns the helper that converts a .NET enum to its IDL enumeration value.</summary>
    private string GetEnumToString(Type enumType)
    {
        var name = "FromEnum_" + SafeName(enumType);
        var code = new StringBuilder();
        code.Append($"internal static string {name}({TypeName(enumType)} value) => value switch\n{{\n");
        foreach (var field in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            code.Append($"    {TypeName(enumType)}.{field.Name} => {Literal(EnumValueName(field))},\n");
        }

        code.Append("    _ => value.ToString(),\n};");
        GetHelper(name, code.ToString());
        return HelpersClass + "." + name;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Dictionaries (https://webidl.spec.whatwg.org/#js-dictionary)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The dictionaries of a dictionary and its inherited dictionaries, the inherited ones first.</summary>
    private List<IdlDefinition> GetDictionaryHierarchy(IdlDefinition definition)
    {
        var dictionaries = new List<IdlDefinition>();
        for (var current = definition; current is not null; current = current.Inheritance is null ? null : _idl.Get(current.Inheritance))
        {
            dictionaries.Insert(0, current);
        }

        return dictionaries;
    }

    /// <summary>Returns the helper that converts a JavaScript value to a .NET dictionary.</summary>
    private string GetDictionaryConverter(Type type, IdlDefinition definition)
    {
        if (_dictionaryConverters.TryGetValue(type, out var name))
            return name;

        name = "ToDictionary_" + SafeName(type);
        _dictionaryConverters[type] = HelpersClass + "." + name;
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var code = new StringBuilder();
        code.Append($"internal static {TypeName(type)} {name}({Realm} r, TValue value)\n{{\n");
        code.Append($"    r.CheckDictionary(value, {Literal(definition.Name)});\n");
        var assignments = new List<string>();

        // The members whose presence is tracked by the .NET type (e.g. ElementCreationOptions.HasCustomElementRegistry) are
        // only assigned when they are present
        var presenceTracked = new List<(string Condition, string Assignment)>();
        var defaults = $"Defaults_{SafeName(type)}";
        var needsDefaults = false;
        var index = 0;

        // The members of the inherited dictionaries first, then in lexicographical order
        foreach (var dictionary in GetDictionaryHierarchy(definition))
        {
            foreach (var member in dictionary.Members.Where(m => m.Kind == IdlMemberKind.Field).OrderBy(m => m.Name, StringComparer.Ordinal))
            {
                // Members without a .NET property are ignored, like unknown members
                var property = properties.FirstOrDefault(p => p.GetCustomAttributes<IdlMemberAttribute>().Any(a => a.Name == member.Name));
                if (property?.SetMethod is null)
                    continue;

                var variable = "m" + index++;
                var (natural, _) = EmitNatural(member.Type!, member.ExtAttrs, "v");
                code.Append($"    var {variable}_value = r.GetDictionaryMember(value, {Literal(member.Name)});\n");
                string naturalExpression;
                if (member.Default is { } defaultValue && EmitDefaultValue(defaultValue, member.Type!, member.ExtAttrs, out _) is { } defaultExpression)
                {
                    naturalExpression = EmitMemberConversion(member, $"{variable}_value", natural, defaultExpression);
                    code.Append($"    var {variable} = {naturalExpression};\n");
                    var coerced = EmitCoerce(natural, variable, property.PropertyType, out var condition);
                    if (coerced is null || condition is not null)
                    {
                        Error($"Cannot convert the member {definition.Name}.{member.Name} to {property.PropertyType}");
                        continue;
                    }

                    assignments.Add($"{property.Name} = {coerced}");
                }
                else if (member.Required)
                {
                    code.Append($"    if (r.Adapter.IsUndefined({variable}_value)) throw r.MissingRequiredDictionaryMember({Literal(definition.Name)}, {Literal(member.Name)});\n");
                    code.Append($"    var {variable} = {EmitMemberConversion(member, $"{variable}_value", natural, null)};\n");
                    var coerced = EmitCoerce(natural, variable, property.PropertyType, out var condition);
                    if (coerced is null || condition is not null)
                    {
                        Error($"Cannot convert the member {definition.Name}.{member.Name} to {property.PropertyType}");
                        continue;
                    }

                    assignments.Add($"{property.Name} = {coerced}");
                }
                else
                {
                    // A missing member keeps the default value of the .NET property
                    var nullableType = natural.CSharpType.EndsWith('?', StringComparison.Ordinal) ? natural.CSharpType : natural.CSharpType + "?";
                    code.Append($"    var {variable}_present = !r.Adapter.IsUndefined({variable}_value);\n");
                    code.Append($"    var {variable} = {variable}_present ? ({nullableType}){EmitMemberConversion(member, $"{variable}_value", natural, null)} : default;\n");
                    var coerced = EmitCoerce(natural with { CSharpType = nullableType }, variable, property.PropertyType, out var condition);
                    if (coerced is null || condition is not null)
                    {
                        coerced = EmitCoerce(natural, $"{variable}!", property.PropertyType, out condition);
                        if (natural.ClrType is { IsValueType: true } && Nullable.GetUnderlyingType(property.PropertyType) is null)
                        {
                            coerced = EmitCoerce(natural, $"{variable}.GetValueOrDefault()", property.PropertyType, out condition);
                        }
                    }

                    if (coerced is null || condition is not null)
                    {
                        Error($"Cannot convert the member {definition.Name}.{member.Name} to {property.PropertyType}");
                        continue;
                    }

                    if (type.GetProperty("Has" + property.Name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance) is not null)
                    {
                        presenceTracked.Add(($"{variable}_present", $"{property.Name} = {coerced}"));
                        continue;
                    }

                    needsDefaults = true;
                    assignments.Add($"{property.Name} = {variable}_present ? {coerced} : {defaults}.{property.Name}");
                }
            }
        }

        // One object initializer per combination of the present tracked members
        for (var combination = (1 << presenceTracked.Count) - 1; combination >= 0; combination--)
        {
            var isLast = combination == 0;
            var indent = isLast ? "    " : "        ";
            if (!isLast)
            {
                var conditions = presenceTracked.Select((m, i) => (combination & (1 << i)) != 0 ? m.Condition : "!" + m.Condition);
                code.Append($"    if ({string.Join(" && ", conditions)})\n    {{\n");
            }

            code.Append($"{indent}return new {TypeName(type)}\n{indent}{{\n");
            foreach (var assignment in assignments.Concat(presenceTracked.Where((_, i) => (combination & (1 << i)) != 0).Select(m => m.Assignment)))
            {
                code.Append($"{indent}    {assignment},\n");
            }

            code.Append($"{indent}}};\n");
            if (!isLast)
            {
                code.Append("    }\n\n");
            }
        }

        code.Append('}');
        if (needsDefaults)
        {
            var requiredMembers = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.IsDefined(typeof(System.Runtime.CompilerServices.RequiredMemberAttribute))).ToList();
            var initializer = requiredMembers.Count == 0 ? "" : " { " + string.Join(", ", requiredMembers.Select(p => $"{p.Name} = default!")) + " }";
            GetHelper(defaults, $"private static readonly {TypeName(type)} {defaults} = new(){initializer};");
        }

        GetHelper(name, code.ToString());
        return _dictionaryConverters[type];
    }

    private string EmitMemberConversion(IdlMember member, string value, Natural natural, string? defaultExpression)
    {
        var (_, expression) = EmitNatural(member.Type!, member.ExtAttrs, value);
        return defaultExpression is null ? expression : $"r.Adapter.IsUndefined({value}) ? ({natural.CSharpType}){defaultExpression} : {expression}";
    }

    /// <summary>Returns the helper that converts a .NET dictionary to a JavaScript object (https://webidl.spec.whatwg.org/#js-dictionary).</summary>
    private string GetDictionaryToJs(Type type)
    {
        if (_dictionaryToJs.TryGetValue(type, out var name))
            return name;

        name = "FromDictionary_" + SafeName(type);
        _dictionaryToJs[type] = HelpersClass + "." + name;
        var attribute = type.GetCustomAttribute<IdlInterfaceAttribute>()!;
        var definition = _idl.Get(attribute.Name)!;
        var properties = type.GetProperties();
        var code = new StringBuilder();
        code.Append($"internal static TValue {name}({Realm} r, {TypeName(type)} value)\n{{\n");
        code.Append("    var result = r.CreateObject();\n");

        // The members of the inherited dictionaries first, then in lexicographical order. A null member without default value does not exist.
        var dictionaryValues = 0;
        foreach (var dictionary in GetDictionaryHierarchy(definition))
        {
            foreach (var member in dictionary.Members.Where(m => m.Kind == IdlMemberKind.Field).OrderBy(m => m.Name, StringComparer.Ordinal))
            {
                var property = properties.FirstOrDefault(p => p.GetCustomAttributes<IdlMemberAttribute>().Any(a => a.Name == member.Name));
                if (property is null)
                    continue;

                var toJs = EmitToJs(property.PropertyType, member.Type, $"value.{property.Name}");

                // The pattern variables of the members that are dictionaries are in the scope of the method: they must be unique
                if (toJs.Contains(" dictionaryValue ", StringComparison.Ordinal) && dictionaryValues++ > 0)
                {
                    toJs = toJs.Replace("dictionaryValue", "dictionaryValue" + dictionaryValues.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
                }

                // A null value of a nullable IDL type is the null value (e.g. ComputedEffectTiming.progress), not a missing member
                var canBeNull = !property.PropertyType.IsValueType || Nullable.GetUnderlyingType(property.PropertyType) is not null;
                if (type.GetProperty("Has" + property.Name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance) is not null)
                {
                    code.Append($"    if (value.Has{property.Name}) r.SetMember(result, {Literal(member.Name)}, {toJs});\n");
                }
                else if (canBeNull && member.Default is null && !member.Required && !_idl.ResolveTypedefs(member.Type!).Nullable)
                {
                    code.Append($"    if (value.{property.Name} is not null) r.SetMember(result, {Literal(member.Name)}, {toJs});\n");
                }
                else
                {
                    code.Append($"    r.SetMember(result, {Literal(member.Name)}, {toJs});\n");
                }
            }
        }

        code.Append("    return result;\n}");
        GetHelper(name, code.ToString());
        return _dictionaryToJs[type];
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Callback functions (https://webidl.spec.whatwg.org/#es-callback-function)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Returns the helper that converts a JavaScript function to a .NET delegate.</summary>
    private string GetDelegateConverter(Type delegateType)
    {
        if (_delegateConverters.TryGetValue(delegateType, out var name))
            return name;

        name = "ToDelegate_" + SafeName(delegateType);
        _delegateConverters[delegateType] = HelpersClass + "." + name;
        var invoke = delegateType.GetMethod("Invoke")!;
        var parameters = invoke.GetParameters();
        var parameterList = string.Join(", ", parameters.Select((p, i) => "p" + i));
        var isVariadic = parameters.Length > 0 && parameters[^1].IsDefined(typeof(ParamArrayAttribute));

        // The callback this value: the current target of an event, the observer of a mutation, resize, intersection, reporting or performance observer, the window of a timer handler
        string thisValue;
        if (parameters.Length > 0 && typeof(Event).IsAssignableFrom(parameters[0].ParameterType))
        {
            thisValue = "r.ToJs(p0?.CurrentTarget)";
        }
        else if (parameters.Length > 1 && (parameters[1].ParameterType == typeof(MutationObserver) || parameters[1].ParameterType == typeof(ResizeObserver) || parameters[1].ParameterType == typeof(IntersectionObserver) || parameters[1].ParameterType == typeof(ReportingObserver) || parameters[1].ParameterType == typeof(PerformanceObserver)))
        {
            thisValue = "r.ToJs(p1)";
        }
        else if (delegateType == typeof(FunctionCallback))
        {
            thisValue = "r.ToJs(r.GlobalScope)";
        }
        else
        {
            thisValue = "r.Adapter.Undefined";
        }

        var behavior = new List<string>();
        if (parameters.Length > 0 && typeof(Event).IsAssignableFrom(parameters[0].ParameterType))
        {
            behavior.Add("ReportExceptions");
            behavior.Add("IgnoreInactive");
        }
        else if (delegateType == typeof(FunctionCallback) || delegateType == typeof(Action) || delegateType == typeof(FrameRequestCallback) || delegateType == typeof(MutationCallback) || delegateType == typeof(ResizeObserverCallback) || delegateType == typeof(IntersectionObserverCallback) || delegateType == typeof(BlobCallback))
        {
            // https://html.spec.whatwg.org/multipage/canvas.html#dom-canvas-toblob: the callback is invoked with "report"
            behavior.Add("ReportExceptions");
        }

        var behaviorExpression = behavior.Count == 0 ? "global::Meziantou.Framework.WebPlatform.Scripting.CallbackBehavior.None" : string.Join(" | ", behavior.Select(b => "global::Meziantou.Framework.WebPlatform.Scripting.CallbackBehavior." + b));

        // The arguments
        var arguments = new StringBuilder();
        var fixedCount = isVariadic ? parameters.Length - 1 : parameters.Length;
        arguments.Append("var arguments = new global::System.Collections.Generic.List<TValue>();");
        for (var i = 0; i < fixedCount; i++)
        {
            arguments.Append($" arguments.Add({EmitToJs(parameters[i].ParameterType, null, "p" + i)});");
        }

        if (isVariadic)
        {
            arguments.Append($" foreach (var item in p{parameters.Length - 1}) {{ arguments.Add(r.ToJs(item)); }}");
        }

        // The result, converted to the return type of the IDL callback
        var idlReturnType = delegateType.GetCustomAttribute<IdlInterfaceAttribute>() is { } idlInterface && _idl.Get(idlInterface.Name) is { Kind: IdlDefinitionKind.Callback, Type: { } returnType } ? returnType : null;
        string returnStatement;
        if (invoke.ReturnType == typeof(void))
        {
            returnStatement = "r.InvokeCallback(function, " + thisValue + ", global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(arguments), " + behaviorExpression + ");";
        }
        else
        {
            string conversion;
            if (idlReturnType is not null && idlReturnType.Name != "any")
            {
                var (natural, expression) = EmitNatural(idlReturnType, null, "result");
                conversion = EmitCoerce(natural, expression, invoke.ReturnType, out var condition) ?? throw new InvalidOperationException($"Cannot convert the result of {delegateType}");
                if (condition is not null)
                {
                    conversion = $"({condition.Replace(expression, "x", StringComparison.Ordinal)}) ? {conversion} : throw r.CreateTypeError(\"Invalid callback result\")";
                }
            }
            else if (invoke.ReturnType == typeof(bool))
            {
                conversion = "r.ToBoolean(result)";
            }
            else if (invoke.ReturnType == typeof(string))
            {
                conversion = "r.ToDomString(result)";
            }
            else if (IsNumeric(invoke.ReturnType))
            {
                conversion = $"({TypeName(invoke.ReturnType)})r.Adapter.ToNumber(result)";
            }
            else
            {
                conversion = "result";
            }

            returnStatement = "var result = r.InvokeCallback(function, " + thisValue + ", global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(arguments), " + behaviorExpression + "); return " + conversion + ";";
        }

        var code = $"internal static {TypeName(delegateType)} {name}({Realm} r, TValue function) => r.GetDelegate(function, static (r, function) => new {TypeName(delegateType)}(({parameterList}) => {{ {arguments} {returnStatement} }}));";
        GetHelper(name, code);
        return _delegateConverters[delegateType];
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Unions (https://webidl.spec.whatwg.org/#js-union)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The IDL type with the extended attributes of its member types (e.g. [LegacyNullToEmptyString] DOMString).</summary>
    private static string TypeKey(IdlType type)
    {
        var prefix = type.ExtAttrs.Count == 0 ? "" : "[" + string.Join(",", type.ExtAttrs.Select(a => a.Name)) + "] ";
        var name = type.Union ? "(" + string.Join(" or ", type.Subtypes.Select(TypeKey)) + ")"
            : type.Generic.Length > 0 ? type.Generic + "<" + string.Join(", ", type.Subtypes.Select(TypeKey)) + ">"
            : type.Name;
        return prefix + name + (type.Nullable ? "?" : "");
    }

    /// <summary>Returns a name that depends only on the union, so adding a union does not rename the others.</summary>
    private static string GetUnionNaturalName(string key) => "ToUnion_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..8];

    /// <summary>Returns the helper that converts a JavaScript value to the natural value of a union type (boxed).</summary>
    private string GetUnionNatural(IdlType type, IReadOnlyList<IdlExtendedAttribute>? extAttrs)
    {
        var key = TypeKey(type) + "|" + string.Join(",", extAttrs?.Select(a => a.Name) ?? []);
        if (_unionNaturals.TryGetValue(key, out var name))
            return name;

        name = GetUnionNaturalName(key);
        if (!_unionNaturalKeys.TryAdd(name, key))
            throw new InvalidOperationException($"The unions '{_unionNaturalKeys[name]}' and '{key}' have the same helper name '{name}'");

        _unionNaturals[key] = HelpersClass + "." + name;
        var types = FlattenUnion(type).ToList();
        var code = new StringBuilder();
        code.Append($"/// <summary>{System.Security.SecurityElement.Escape(type.ToString())}</summary>\n");
        code.Append($"internal static object? {name}({Realm} r, TValue value)\n{{\n");
        string Convert(IdlType memberType)
        {
            var (natural, expression) = EmitNatural(memberType, extAttrs, "value");
            return natural.Kind == NaturalKind.Function ? $"new global::Meziantou.Framework.WebPlatform.Scripting.JsFunctionValue<TValue>({expression}{(natural.CSharpType.EndsWith('?', StringComparison.Ordinal) ? "!" : "")})" : expression;
        }

        if (types.Exists(t => t.Name == "undefined"))
            code.Append("    if (r.Adapter.IsUndefined(value)) return null;\n");

        if (type.Nullable || types.Exists(t => t.Nullable))
            code.Append("    if (r.IsNullOrUndefined(value)) return null;\n");

        if (types.Find(IsDictionaryType) is { } nullDictionary)
            code.Append($"    if (r.IsNullOrUndefined(value)) return {Convert(nullDictionary)};\n");

        var interfaceTypes = types.Where(t => GetInterfaceClrType(t) is not null).ToList();
        if (interfaceTypes.Count > 0 || types.Exists(t => t.Name == "object"))
        {
            code.Append("    if (r.Unwrap(value) is { } platformObject)\n    {\n");
            foreach (var interfaceType in interfaceTypes)
            {
                code.Append($"        if (platformObject is {TypeName(GetInterfaceClrType(interfaceType)!)}) return platformObject;\n");
            }

            if (types.Exists(t => t.Name == "object"))
                code.Append("        return value;\n");

            code.Append("    }\n");
        }

        code.Append("    if (r.Adapter.IsObject(value))\n    {\n");

        // The streams are implemented in JavaScript (Streams.js): a .NET member only knows that its argument is a stream
        if (types.Exists(t => t.Name == "ReadableStream" && GetInterfaceClrType(t) is null))
            code.Append("        if (r.IsReadableStream(value)) return global::Meziantou.Framework.WebPlatform.Scripting.JsReadableStream.Instance;\n");

        if (types.Exists(IsBufferIdlType))
        {
            var (mask, allowShared) = GetBufferKinds(type, extAttrs);
            code.Append($"        if (r.TryToBufferSource(value, {mask}u, {Bool(allowShared)}) is {{ }} buffer) return buffer;\n");
        }

        if (types.Find(t => Definition(t)?.Kind == IdlDefinitionKind.Callback) is { } callbackType)
            code.Append($"        if (r.Adapter.IsCallable(value)) return {Convert(callbackType)};\n");

        if (types.Find(t => t.Generic is "sequence" or "FrozenArray") is { } sequenceType)
            code.Append($"        if (r.HasIterator(value)) return {Convert(sequenceType)};\n");

        if (types.Find(t => t.Generic == "record") is { } recordType)
            code.Append($"        return {Convert(recordType)};\n");
        else if (types.Find(t => Definition(t)?.Kind == IdlDefinitionKind.CallbackInterface) is { } callbackInterfaceType)
            code.Append($"        return {Convert(callbackInterfaceType)};\n");
        else if (types.Find(IsDictionaryType) is { } dictionaryType)
            code.Append($"        return {Convert(dictionaryType)};\n");
        else if (types.Exists(t => t.Name == "object"))
            code.Append("        return value;\n");

        code.Append("    }\n");
        if (types.Find(t => t.Name == "boolean") is { } booleanType)
            code.Append($"    if (r.Adapter.IsBoolean(value)) return {Convert(booleanType)};\n");

        if (types.Find(IsNumericIdlType) is { } numberType)
            code.Append($"    if (r.Adapter.IsNumber(value)) return {Convert(numberType)};\n");

        if (types.Find(IsStringIdlType) is { } stringType)
            code.Append($"    return {Convert(stringType)};\n");
        else if (types.Find(IsNumericIdlType) is { } numericType)
            code.Append($"    return {Convert(numericType)};\n");
        else if (types.Find(t => t.Name == "boolean") is { } booleanFallback)
            code.Append($"    return {Convert(booleanFallback)};\n");
        else
            code.Append($"    throw r.CreateTypeError({Literal($"The value cannot be converted to {type}")});\n");

        code.Append('}');
        GetHelper(name, code.ToString());
        return _unionNaturals[key];
    }

    /// <summary>Returns the helper that converts a natural value (boxed) to a .NET union struct, using its implicit conversions.</summary>
    private string GetUnionConverter(Type unionType)
    {
        if (_unionConverters.TryGetValue(unionType, out var name))
            return name;

        name = "ToUnionStruct_" + SafeName(unionType);
        _unionConverters[unionType] = HelpersClass + "." + name;
        var conversions = unionType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "op_Implicit" && m.ReturnType == unionType && m.GetParameters().Length == 1)
            .Select(m => m.GetParameters()[0].ParameterType)
            .OrderByDescending(Depth)
            .ToList();
        var code = new StringBuilder();
        code.Append($"internal static {TypeName(unionType)} {name}({Realm} r, object? value) => value switch\n{{\n");
        var annotatedDelegate = conversions.Find(t => typeof(Delegate).IsAssignableFrom(t) && t.IsDefined(typeof(IdlInterfaceAttribute)));
        foreach (var type in conversions)
        {
            if (typeof(Delegate).IsAssignableFrom(type))
                continue;

            if (type.IsEnum)
            {
                code.Append($"    string enumValue when {HelpersClass}.TryToEnum_{SafeName(type)}(enumValue, out var enumResult) => enumResult,\n");
                GetEnumConverter(type);
                GetHelper("TryToEnum_" + SafeName(type), $"internal static bool TryToEnum_{SafeName(type)}(string value, out {TypeName(type)} result)\n{{\n    try\n    {{\n        result = {GetEnumConverter(type)}(value);\n        return true;\n    }}\n    catch (global::System.ArgumentException)\n    {{\n        result = default;\n        return false;\n    }}\n}}");
                continue;
            }

            if (type.IsValueType && IsNumeric(type))
            {
                code.Append($"    {TypeName(type)} v => v,\n");
                foreach (var numeric in new[] { typeof(int), typeof(uint), typeof(double), typeof(float), typeof(short), typeof(ushort), typeof(long), typeof(ulong), typeof(byte), typeof(sbyte) })
                {
                    if (numeric != type && !conversions.Contains(numeric))
                    {
                        code.Append($"    {TypeName(numeric)} v => ({TypeName(type)})v,\n");
                    }
                }

                continue;
            }

            code.Append($"    {TypeName(type)} v => v,\n");
        }

        // A buffer source (e.g. AllowSharedBufferSource for a ReadOnlySpan<byte> parameter)
        if (unionType == typeof(ReadOnlySpan<byte>) || unionType == typeof(Span<byte>))
        {
            code.Append($"    {BufferSourceType} v => v.Bytes.Span,\n");
        }
        else if (conversions.Contains(typeof(byte[])))
        {
            code.Append($"    {BufferSourceType} v => v.ToArray(),\n");
        }

        if (annotatedDelegate is not null)
        {
            // A function is converted to the delegate type of the IDL callback function of the union (e.g. Function in TimerHandler)
            code.Append($"    global::Meziantou.Framework.WebPlatform.Scripting.JsFunctionValue<TValue> function => {GetDelegateConverter(annotatedDelegate)}(r, function.Function),\n");
        }

        code.Append("    _ => throw new global::System.InvalidCastException(\"The value cannot be converted to " + unionType.Name + "\"),\n};");
        GetHelper(name, code.ToString());
        return _unionConverters[unionType];
    }
}
