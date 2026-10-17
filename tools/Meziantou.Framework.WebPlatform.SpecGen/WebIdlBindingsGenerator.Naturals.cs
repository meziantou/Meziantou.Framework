using System.Reflection;
using System.Text.Json;
using Meziantou.Framework.WebPlatform;
using Meziantou.Framework.WebPlatform.Dom;
using SpecGen.Idl;

namespace SpecGen;

/// <summary>The kind of the .NET value an IDL value is converted to, before it is coerced to the type of a .NET parameter.</summary>
internal enum NaturalKind
{
    /// <summary>A .NET value of <see cref="Natural.ClrType"/> (string, bool, numbers, platform objects, dictionaries).</summary>
    Clr,

    /// <summary>A JavaScript value (the IDL <c>object</c> type): TValue.</summary>
    JsValue,

    /// <summary>A JavaScript function, or null (a callback function): TValue?.</summary>
    Function,

    /// <summary>A callback interface: JsCallbackInterface.</summary>
    CallbackInterface,

    /// <summary>The IDL <c>any</c> type: object?.</summary>
    Any,

    /// <summary>A union: the natural value of one of its member types, boxed (object?).</summary>
    Union,

    /// <summary>A sequence: List&lt;natural of the element type&gt;.</summary>
    Sequence,

    /// <summary>A record: List&lt;KeyValuePair&lt;string, natural of the value type&gt;&gt;.</summary>
    Record,

    /// <summary>The value of an enumeration: a string.</summary>
    Enumeration,

    /// <summary>A buffer source type, or a union of buffer source types: JsBufferSource.</summary>
    Buffer,
}

/// <summary>The .NET representation of an IDL value (https://webidl.spec.whatwg.org/#es-type-mapping).</summary>
internal sealed record Natural(NaturalKind Kind, string CSharpType, Type? ClrType = null, Natural? Element = null, string? Name = null, IdlType? IdlType = null)
{
    public static Natural FromClr(Type type) => new(NaturalKind.Clr, WebIdlBindingsGenerator.TypeName(type), type);
}

internal sealed partial class WebIdlBindingsGenerator
{
    private const string CallbackInterfaceType = "global::Meziantou.Framework.WebPlatform.Scripting.JsCallbackInterface<TAdapter, TValue>";

    private const string BufferSourceType = "global::Meziantou.Framework.WebPlatform.Scripting.JsBufferSource";

    /// <summary>The buffer source types (https://webidl.spec.whatwg.org/#idl-buffer-source-types), in the order of JsBufferKind.</summary>
    private static readonly string[] BufferTypeNames = ["ArrayBuffer", "SharedArrayBuffer", "DataView", "Int8Array", "Uint8Array", "Uint8ClampedArray", "Int16Array", "Uint16Array", "Int32Array", "Uint32Array", "BigInt64Array", "BigUint64Array", "Float16Array", "Float32Array", "Float64Array"];

    private static bool IsBufferIdlType(IdlType type) => !type.Union && type.Generic.Length == 0 && type.Name is not null && BufferTypeNames.Contains(type.Name);

    /// <summary>
    /// Returns the set of the JsBufferKind (1 &lt;&lt; kind) of the buffer source member types of a type, and whether a view of
    /// a SharedArrayBuffer is allowed ([AllowShared], which applies to the member types of a typedef, e.g. [AllowShared] ArrayBufferView).
    /// </summary>
    private (uint Mask, bool AllowShared) GetBufferKinds(IdlType type, IReadOnlyList<IdlExtendedAttribute>? extAttrs)
    {
        var mask = 0u;
        var allowShared = false;
        void Visit(IdlType current, bool inheritedAllowShared)
        {
            current = _idl.ResolveTypedefs(current);
            var currentAllowShared = inheritedAllowShared || current.HasExtAttr("AllowShared");
            if (current.Union)
            {
                foreach (var subtype in current.Subtypes)
                {
                    Visit(subtype, currentAllowShared);
                }

                return;
            }

            if (!IsBufferIdlType(current))
                return;

            mask |= 1u << Array.IndexOf(BufferTypeNames, current.Name);
            allowShared |= currentAllowShared;
        }

        Visit(type, extAttrs?.Any(a => a.Name == "AllowShared") ?? false);
        return (mask, allowShared);
    }

    /// <summary>Whether all the member types of a union are buffer source types.</summary>
    private bool IsBufferUnion(IdlType type) => type.Union && FlattenUnion(type).All(IsBufferIdlType);

    /// <summary>
    /// Returns the C# expression that converts the JavaScript value <paramref name="value"/> to the natural .NET value of an
    /// IDL type (https://webidl.spec.whatwg.org/#es-type-mapping). The expression may throw a TypeError.
    /// </summary>
    private (Natural Natural, string Expression) EmitNatural(IdlType type, IReadOnlyList<IdlExtendedAttribute>? extAttrs, string value)
    {
        type = _idl.ResolveTypedefs(type);
        if (type.Name == "any")
            return (new Natural(NaturalKind.Any, "object?"), $"r.ToAny({value})");

        // Nullable types (the dictionaries accept null and undefined themselves, but a nullable dictionary, which is only
        // allowed as the result of an operation or a callback, converts them to null, e.g. CreateParserOptionsCallback)
        if (type.Nullable && !type.Union)
        {
            var nonNullable = new IdlType { Name = type.Name, Generic = type.Generic, Union = type.Union, Subtypes = type.Subtypes, ExtAttrs = type.ExtAttrs };
            var definition = Definition(nonNullable);
            if (definition?.Kind == IdlDefinitionKind.Callback)
            {
                var treatNonObjectAsNull = definition.HasExtAttr("LegacyTreatNonObjectAsNull");
                return (new Natural(NaturalKind.Function, "TValue?", Name: definition.Name, IdlType: nonNullable), $"(r.IsNullOrUndefined({value}) ? null : r.ToCallbackFunction({value}, {Literal(definition.Name)}, {Bool(treatNonObjectAsNull)}))");
            }

            var (inner, expression) = EmitNatural(nonNullable, extAttrs, value);
            var nullableType = inner.CSharpType.EndsWith('?', StringComparison.Ordinal) ? inner.CSharpType : inner.CSharpType + "?";
            return (inner with { CSharpType = nullableType }, $"(r.IsNullOrUndefined({value}) ? ({nullableType})null : {expression})");
        }

        // A buffer source type, or a union of buffer source types (e.g. BufferSource): the type is checked by the conversion
        if (IsBufferIdlType(type) || IsBufferUnion(type))
        {
            var (mask, allowShared) = GetBufferKinds(type, extAttrs);
            var bufferConversion = $"r.ToBufferSource({value}, {mask}u, {Bool(allowShared)}, {Literal(type.ToString().TrimEnd('?'))})";
            if (type.Union && (type.Nullable || FlattenUnion(type).Any(t => t.Nullable)))
                return (new Natural(NaturalKind.Buffer, BufferSourceType + "?", IdlType: type), $"(r.IsNullOrUndefined({value}) ? null : {bufferConversion})");

            return (new Natural(NaturalKind.Buffer, BufferSourceType, IdlType: type), bufferConversion);
        }

        if (type.Union)
            return (new Natural(NaturalKind.Union, "object?", IdlType: type), $"{GetUnionNatural(type, extAttrs)}(r, {value})");

        switch (type.Generic)
        {
            case "sequence" or "FrozenArray" or "ObservableArray":
            {
                var (element, elementExpression) = EmitNatural(type.Subtypes[0], extAttrs: null, "e");
                return (new Natural(NaturalKind.Sequence, $"global::System.Collections.Generic.List<{element.CSharpType}>", Element: element), $"r.ToSequence<{element.CSharpType}>({value}, static (r, e) => {elementExpression})");
            }

            case "record":
            {
                var (key, keyExpression) = EmitNatural(type.Subtypes[0], extAttrs: null, "k");
                var (element, elementExpression) = EmitNatural(type.Subtypes[1], extAttrs: null, "e");
                if (key.CSharpType != "string")
                {
                    Error("Unsupported record key type " + type);
                }

                return (new Natural(NaturalKind.Record, $"global::System.Collections.Generic.List<global::System.Collections.Generic.KeyValuePair<string, {element.CSharpType}>>", Element: element), $"r.ToRecord({value}, static (r, k) => {keyExpression}, static (r, e) => {elementExpression})");
            }

            case "Promise":
                // The promise is converted when the .NET parameter is a Task (see EmitCoerce), it is passed as is otherwise
                return (new Natural(NaturalKind.JsValue, "TValue", IdlType: type), value);
        }

        var conversion = HasExtAttr(type, extAttrs, "EnforceRange") ? "global::Meziantou.Framework.WebPlatform.Scripting.IntegerConversion.EnforceRange"
            : HasExtAttr(type, extAttrs, "Clamp") ? "global::Meziantou.Framework.WebPlatform.Scripting.IntegerConversion.Clamp"
            : "global::Meziantou.Framework.WebPlatform.Scripting.IntegerConversion.None";
        switch (type.Name)
        {
            case "WindowProxy":
                return (Natural.FromClr(typeof(Window)), $"r.ToInterface<global::Meziantou.Framework.WebPlatform.Dom.Window>({value}, \"WindowProxy\")");
            case "undefined":
                return (new Natural(NaturalKind.Any, "object?"), "null");
            case "boolean":
                return (Natural.FromClr(typeof(bool)), $"r.ToBoolean({value})");
            case "byte":
                return (Natural.FromClr(typeof(sbyte)), $"r.ToByte({value}, {conversion})");
            case "octet":
                return (Natural.FromClr(typeof(byte)), $"r.ToOctet({value}, {conversion})");
            case "short":
                return (Natural.FromClr(typeof(short)), $"r.ToShort({value}, {conversion})");
            case "unsigned short":
                return (Natural.FromClr(typeof(ushort)), $"r.ToUnsignedShort({value}, {conversion})");
            case "long":
                return (Natural.FromClr(typeof(int)), $"r.ToLong({value}, {conversion})");
            case "unsigned long":
                return (Natural.FromClr(typeof(uint)), $"r.ToUnsignedLong({value}, {conversion})");
            case "long long":
                return (Natural.FromClr(typeof(long)), $"r.ToLongLong({value}, {conversion})");
            case "unsigned long long":
                return (Natural.FromClr(typeof(ulong)), $"r.ToUnsignedLongLong({value}, {conversion})");
            case "bigint":
                return (Natural.FromClr(typeof(System.Numerics.BigInteger)), $"r.ToBigInt({value})");
            case "float":
                return (Natural.FromClr(typeof(float)), $"r.ToFloat({value}, restricted: true)");
            case "unrestricted float":
                return (Natural.FromClr(typeof(float)), $"r.ToFloat({value}, restricted: false)");
            case "double":
                return (Natural.FromClr(typeof(double)), $"r.ToDouble({value}, restricted: true)");
            case "unrestricted double":
                return (Natural.FromClr(typeof(double)), $"r.ToDouble({value}, restricted: false)");
            case "DOMString" or "CSSOMString":
                return (Natural.FromClr(typeof(string)), HasExtAttr(type, extAttrs, "LegacyNullToEmptyString") ? $"r.ToDomString({value}, legacyNullToEmptyString: true)" : $"r.ToDomString({value})");
            case "USVString":
                return (Natural.FromClr(typeof(string)), $"r.ToUsvString({value})");
            case "ByteString":
                return (Natural.FromClr(typeof(string)), $"r.ToByteString({value})");
            case "object":
                return (new Natural(NaturalKind.JsValue, "TValue"), $"r.ToObject({value})");
        }

        var idlDefinition = Definition(type);
        switch (idlDefinition?.Kind)
        {
            case IdlDefinitionKind.Interface:
            {
                var clrType = GetClrType(idlDefinition.Name);
                if (clrType is null && idlDefinition.Name is "Request" or "Response")
                {
                    // The Request and Response objects are implemented in JavaScript (Fetch.js): a .NET member gets the value of
                    // the script engine, after its brand is checked
                    return (new Natural(NaturalKind.Any, "object?"), $"r.ToFetchObject({value}, {Literal(idlDefinition.Name)})");
                }

                if (clrType is null)
                {
                    // An interface that is not implemented: no value can be converted
                    return (new Natural(NaturalKind.Any, "object?"), $"throw r.CreateTypeError({Literal($"The value is not of type '{idlDefinition.Name}'")})");
                }

                return (Natural.FromClr(clrType), $"r.ToInterface<{TypeName(clrType)}>({value}, {Literal(idlDefinition.Name)})");
            }

            case IdlDefinitionKind.CallbackInterface:
                return (new Natural(NaturalKind.CallbackInterface, CallbackInterfaceType), $"r.ToCallbackInterface({value}, {Literal(idlDefinition.Name)})");

            case IdlDefinitionKind.Callback:
            {
                var treatNonObjectAsNull = idlDefinition.HasExtAttr("LegacyTreatNonObjectAsNull");
                // Only [LegacyTreatNonObjectAsNull] callbacks convert a value to null (https://webidl.spec.whatwg.org/#js-callback-function)
                return treatNonObjectAsNull
                    ? (new Natural(NaturalKind.Function, "TValue?", Name: idlDefinition.Name, IdlType: type), $"r.ToCallbackFunction({value}, {Literal(idlDefinition.Name)}, true)")
                    : (new Natural(NaturalKind.Function, "TValue", Name: idlDefinition.Name, IdlType: type), $"r.ToCallbackFunction({value}, {Literal(idlDefinition.Name)}, false)!");
            }

            case IdlDefinitionKind.Dictionary:
            {
                var clrType = GetClrType(idlDefinition.Name);
                if (clrType is null)
                {
                    Error($"The dictionary '{idlDefinition.Name}' has no .NET type");
                    return (new Natural(NaturalKind.Any, "object?"), "null");
                }

                return (Natural.FromClr(clrType), $"{GetDictionaryConverter(clrType, idlDefinition)}(r, {value})");
            }

            case IdlDefinitionKind.Enum:
            {
                var values = "new[] { " + string.Join(", ", idlDefinition.EnumValues.Select(Literal)) + " }";
                var field = GetHelper($"Enum_{SafeName(idlDefinition.Name)}_Values", $"internal static readonly string[] Enum_{SafeName(idlDefinition.Name)}_Values = {values};");
                return (new Natural(NaturalKind.Enumeration, "string", typeof(string), Name: idlDefinition.Name), $"r.ToEnumerationValue({value}, {Literal(idlDefinition.Name)}, {HelpersClass}.{field})");
            }
        }

        // A type that is not supported (e.g. the typed arrays): a TypeError is thrown when the value is converted
        return (new Natural(NaturalKind.Any, "object?"), $"(object?)r.ThrowUnsupportedType({Literal(type.ToString())})");
    }

    private static string Bool(bool value) => value ? "true" : "false";

    /// <summary>
    /// Returns the C# expression that coerces a natural value to the .NET type <paramref name="target"/>, and the condition
    /// the natural value must satisfy (null when the coercion always succeeds), used to select among .NET overloads.
    /// </summary>
    private string? EmitCoerce(Natural natural, string expression, Type target, out string? condition)
    {
        condition = null;
        var underlying = Nullable.GetUnderlyingType(target) ?? target;
        var targetName = TypeName(target);

        switch (natural.Kind)
        {
            case NaturalKind.Clr:
            {
                var clrType = natural.ClrType!;
                var naturalIsNullable = natural.CSharpType.EndsWith('?', StringComparison.Ordinal);
                if (target == typeof(object))
                    return expression;

                if (clrType.IsValueType)
                {
                    if (underlying == clrType)
                        return naturalIsNullable && target == underlying ? $"({targetName})({expression}!)" : expression;

                    if (IsNumeric(clrType) && IsNumeric(underlying))
                        return $"({targetName})({expression})";
                }
                else if (target.IsAssignableFrom(clrType))
                {
                    return expression;
                }

                if (IsUnionStruct(underlying))
                    return $"{GetUnionConverter(underlying)}(r, {expression})";

                return null;
            }

            case NaturalKind.Enumeration:
            {
                if (target == typeof(string) || target == typeof(object))
                    return expression;

                if (underlying.IsEnum)
                {
                    // The pattern variable is named after the expression (e.g. the members of a dictionary are converted in the same initializer)
                    var enumVariable = System.Text.RegularExpressions.Regex.IsMatch(expression, "^[A-Za-z_][A-Za-z0-9_]*$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)) ? "enumValue_" + expression : "enumValue";
                    return natural.CSharpType.EndsWith('?', StringComparison.Ordinal) ? $"({expression} is {{ }} {enumVariable} ? {GetEnumConverter(underlying)}({enumVariable}) : ({targetName})null)" : $"{GetEnumConverter(underlying)}({expression})";
                }

                if (IsUnionStruct(underlying))
                    return $"{GetUnionConverter(underlying)}(r, {expression})";

                return null;
            }

            case NaturalKind.JsValue:
                if (target == typeof(object))
                    return expression;

                // https://webidl.spec.whatwg.org/#js-promise: a Task completed when the promise is settled, with the result
                // converted to the type of the promise
                if (natural.IdlType is { Generic: "Promise" } promiseType)
                {
                    if (underlying == typeof(Task))
                        return $"r.ToTask({expression}!)";

                    if (underlying.IsGenericType && underlying.GetGenericTypeDefinition() == typeof(Task<>))
                    {
                        var resultType = underlying.GetGenericArguments()[0];
                        var (resultNatural, resultExpression) = EmitNatural(promiseType.Subtypes[0], extAttrs: null, "v");
                        var resultCoerce = EmitCoerce(resultNatural, resultExpression, resultType, out var resultCondition);
                        if (resultCoerce is null || resultCondition is not null)
                            return null;

                        // The value of a promise of any is a nullable object (null for undefined)
                        var resultTypeName = resultType == typeof(object) ? "object?" : TypeName(resultType);
                        return $"r.ToTask<{resultTypeName}>({expression}!, static (r, v) => {resultCoerce})";
                    }
                }

                if (underlying == typeof(DateTime))
                    return $"r.ToDate({expression})";

                // https://drafts.csswg.org/web-animations-1/#processing-a-keyframes-argument: the keyframes of animate() and KeyframeEffect
                if (target == typeof(IEnumerable<Meziantou.Framework.WebPlatform.Animations.Keyframe>))
                    return $"r.ToKeyframes({expression})";

                return null;

            case NaturalKind.Function:
            {
                if (target == typeof(object))
                    return expression;

                if (target == typeof(CustomElementConstructor))
                    return $"r.ToCustomElementConstructor({expression}!)";

                // The class registered in a worklet global scope (e.g. the VoidFunction of registerPaint())
                if (target == typeof(WorkletClassConstructor))
                    return $"r.ToWorkletClassConstructor({expression}!)";

                if (typeof(Delegate).IsAssignableFrom(target))
                {
                    if (!natural.CSharpType.EndsWith('?', StringComparison.Ordinal))
                        return $"{GetDelegateConverter(target)}(r, {expression})";

                    var variable = NewId("function");
                    return $"({expression} is {{ }} {variable} ? {GetDelegateConverter(target)}(r, {variable}) : null)";
                }

                if (IsUnionStruct(underlying))
                    return $"{GetUnionConverter(underlying)}(r, new global::Meziantou.Framework.WebPlatform.Scripting.JsFunctionValue<TValue>({expression}{(natural.CSharpType.EndsWith('?', StringComparison.Ordinal) ? "!" : "")}))";

                return null;
            }

            case NaturalKind.CallbackInterface:
                if (target == typeof(object) || target == typeof(IEventListener) || target == typeof(INodeFilter) || target == typeof(IXPathNSResolver))
                    return expression;

                return null;

            case NaturalKind.Buffer:
            {
                var isNullable = natural.CSharpType.EndsWith('?', StringComparison.Ordinal);
                if (target == typeof(object))
                    return expression;

                if (underlying == typeof(ReadOnlySpan<byte>) || underlying == typeof(Span<byte>))
                    return isNullable ? $"({expression} is {{ }} buffer ? buffer.Bytes.Span : default)" : $"{expression}.Bytes.Span";

                if (underlying == typeof(Memory<byte>) || underlying == typeof(ReadOnlyMemory<byte>))
                    return isNullable && target == underlying ? $"({expression} is {{ }} buffer ? buffer.Bytes : default)" : isNullable ? $"{expression}?.Bytes" : $"{expression}.Bytes";

                if (underlying == typeof(byte[]))
                    return isNullable ? $"{expression}?.ToArray()" : $"{expression}.ToArray()";

                // The elements of a typed array (e.g. float[] for a Float32Array), in the byte order of the platform
                if (underlying.IsArray && underlying.GetElementType() is { IsPrimitive: true } elementType)
                {
                    var cast = $"unsafe(global::System.Runtime.InteropServices.MemoryMarshal.Cast<byte, {TypeName(elementType)}>(BUFFER.Bytes.Span)).ToArray()";
                    return isNullable ? $"({expression} is {{ }} buffer ? {cast.Replace("BUFFER", "buffer", StringComparison.Ordinal)} : null)" : cast.Replace("BUFFER", expression, StringComparison.Ordinal);
                }

                return null;
            }

            case NaturalKind.Any:
                if (target == typeof(object))
                    return expression;

                // A dictionary (e.g. the options of HTMLCanvasElement.getContext()): an object is converted to it; as in the
                // browsers, another value is ignored (the default options) rather than rejected
                if (underlying.GetCustomAttribute<IdlInterfaceAttribute>(inherit: false) is { } dictionaryAttribute && _idl.Get(dictionaryAttribute.Name) is { Kind: IdlDefinitionKind.Dictionary } dictionaryDefinition)
                    return $"(r.ToJs({expression}) is var dictionaryValue && r.Adapter.IsObject(dictionaryValue) ? {GetDictionaryConverter(underlying, dictionaryDefinition)}(r, dictionaryValue) : null)";

                // A number (e.g. the quality of HTMLCanvasElement.toDataURL()): another value is null
                if (underlying == typeof(double) && target != underlying)
                    return $"(r.ToJs({expression}) is var numberValue && r.Adapter.IsNumber(numberValue) ? r.ToDouble(numberValue, restricted: false) : ({targetName})null)";

                // A type that is not supported: the conversion of the value throws
                if (underlying.IsByRefLike)
                    return $"default({targetName})";

                condition = $"{expression} is null or {TypeName(underlying)}";
                return $"({targetName}){expression}!";

            case NaturalKind.Union:
            {
                if (target == typeof(object))
                    return expression;

                if (IsUnionStruct(underlying))
                    return target != underlying ? $"({expression} is null ? ({targetName})null : {GetUnionConverter(underlying)}(r, {expression}))" : $"{GetUnionConverter(underlying)}(r, {expression})";

                // createElement(localName, options): a string options (web compatibility) is ignored (https://dom.spec.whatwg.org/#dom-document-createelement)
                if (target == typeof(ElementCreationOptions))
                    return $"{expression} as {targetName}";

                if (underlying.IsEnum)
                {
                    condition = $"{expression} is string";
                    return $"{GetEnumConverter(underlying)}((string){expression})";
                }

                if (underlying.IsValueType)
                {
                    condition = target != underlying ? $"{expression} is null or {TypeName(underlying)}" : $"{expression} is {TypeName(underlying)}";
                    return $"({targetName}){expression}!";
                }

                if (typeof(Delegate).IsAssignableFrom(target))
                {
                    condition = $"{expression} is global::Meziantou.Framework.WebPlatform.Scripting.JsFunctionValue<TValue>";
                    return $"{GetDelegateConverter(target)}(r, ((global::Meziantou.Framework.WebPlatform.Scripting.JsFunctionValue<TValue>){expression}!).Function)";
                }

                if (target == typeof(IEventListener) || target == typeof(INodeFilter) || target == typeof(IXPathNSResolver))
                {
                    condition = $"{expression} is null or {CallbackInterfaceType}";
                    return $"({targetName}){expression}!";
                }

                // The union members are all instances of the type (e.g. (Element or Text) for a Node parameter)
                if (natural.IdlType is { } unionType && FlattenUnion(unionType).All(t => IsInterfaceIdlType(t) && (GetInterfaceClrType(t) is not { } memberType || underlying.IsAssignableFrom(memberType))))
                    return $"({targetName}){expression}!";

                // A null value only matches the overloads of a nullable union
                condition = natural.CSharpType.EndsWith('?', StringComparison.Ordinal) ? $"{expression} is null or {targetName.TrimEnd('?')}" : $"{expression} is {targetName.TrimEnd('?')}";
                return $"({targetName}){expression}!";
            }

            case NaturalKind.Sequence:
            {
                var elementType = GetEnumerableElementType(target);
                if (elementType is null)
                    return target == typeof(object) ? expression : null;

                var elementExpression = EmitCoerce(natural.Element!, "e", elementType, out var elementCondition);
                if (elementExpression is null || elementCondition is not null)
                    return null;

                var isIdentity = elementExpression == "e";
                var list = isIdentity ? expression : $"global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Select({expression}!, e => {elementExpression}))";
                if (target.IsArray || (target.IsGenericType && target.GetGenericTypeDefinition() == typeof(ReadOnlySpan<>)))
                    return $"{list}.ToArray()";

                var listType = typeof(List<>).MakeGenericType(elementType);
                if (target.IsAssignableFrom(listType))
                {
                    // e.g. List<object?> for a sequence<any> passed to an IReadOnlyList<object> parameter: the cast only changes the annotations
                    return isIdentity && natural.Element!.CSharpType.EndsWith('?', StringComparison.Ordinal) && !elementType.IsValueType ? $"(({targetName}){list}!)" : list;
                }

                return null;
            }

            case NaturalKind.Record:
            {
                var pairType = GetEnumerableElementType(target);
                if (pairType is null || !pairType.IsGenericType || pairType.GetGenericTypeDefinition() != typeof(KeyValuePair<,>) || pairType.GetGenericArguments()[0] != typeof(string))
                    return null;

                var valueType = pairType.GetGenericArguments()[1];
                var valueExpression = EmitCoerce(natural.Element!, "p.Value", valueType, out var valueCondition);
                if (valueExpression is null || valueCondition is not null)
                    return null;

                var list = valueExpression == "p.Value" ? expression : $"global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Select({expression}, p => new global::System.Collections.Generic.KeyValuePair<string, {TypeName(valueType)}>(p.Key, {valueExpression})))";
                return target.IsAssignableFrom(typeof(List<>).MakeGenericType(pairType)) ? list : null;
            }
        }

        return null;
    }

    /// <summary>Returns T when the type is T[], ReadOnlySpan&lt;T&gt;, or an interface implemented by List&lt;T&gt; (IEnumerable&lt;T&gt;, IReadOnlyList&lt;T&gt;...).</summary>
    private static Type? GetEnumerableElementType(Type type)
    {
        if (type.IsArray)
            return type.GetElementType();

        if (type.IsGenericType && type.GetGenericArguments() is [var argument])
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(ReadOnlySpan<>) || type.IsAssignableFrom(typeof(List<>).MakeGenericType(argument)))
                return argument;
        }

        return null;
    }

    /// <summary>Returns the C# literal of an IDL default value (as serialized by webidl2.js) converted to a natural type.</summary>
    private string? EmitDefaultValue(JsonElement defaultValue, IdlType type, IReadOnlyList<IdlExtendedAttribute>? extAttrs, out Natural natural)
    {
        var (convertedNatural, expression) = EmitNatural(type, extAttrs, "r.Adapter.Undefined");
        natural = convertedNatural;
        var kind = defaultValue.GetProperty("type").GetString();
        switch (kind)
        {
            case "dictionary":
                return expression;
            case "sequence":
                // An empty sequence in a union (e.g. (DOMString or sequence<DOMString>) protocols = []): the natural value of a sequence member
                if (natural.Kind is NaturalKind.Union or NaturalKind.Any)
                    return "(object)new global::System.Collections.Generic.List<object?>()";

                return $"new {natural.CSharpType.TrimEnd('?')}()";
            case "null":
                return $"({natural.CSharpType.TrimEnd('?')}?)null";
            case "undefined":
                return null;
            case "boolean":
                return EmitLiteralNatural(natural, defaultValue.GetProperty("value").GetBoolean() ? "true" : "false");
            case "string":
                return EmitLiteralNatural(natural, Literal(defaultValue.GetProperty("value").GetString()!));
            case "number":
                return EmitLiteralNatural(natural, Literal(ParseIdlNumber(defaultValue.GetProperty("value").GetString()!)));
            case "Infinity":
                return EmitLiteralNatural(natural, defaultValue.TryGetProperty("negative", out var negative) && negative.GetBoolean() ? "double.NegativeInfinity" : "double.PositiveInfinity");
            case "NaN":
                return EmitLiteralNatural(natural, "double.NaN");
        }

        Error("Unsupported default value " + kind);
        return null;
    }

    private static string EmitLiteralNatural(Natural natural, string literal)
    {
        if (natural.Kind == NaturalKind.Clr && natural.ClrType is { } type && IsNumeric(Nullable.GetUnderlyingType(type) ?? type))
            return $"({natural.CSharpType})({literal})";

        if (natural.Kind == NaturalKind.Clr && natural.ClrType == typeof(System.Numerics.BigInteger))
            return $"new global::System.Numerics.BigInteger({literal})";

        if (natural.Kind is NaturalKind.Union or NaturalKind.Any)
        {
            // A literal in a union: the natural value of the member type of the literal
            return literal.EndsWith('d', StringComparison.Ordinal) && !literal.StartsWith('"', StringComparison.Ordinal) ? $"(object)({literal})" : $"(object){literal}";
        }

        return literal;
    }

    private static double ParseIdlNumber(string value)
    {
        var negative = value.StartsWith('-', StringComparison.Ordinal);
        var digits = negative ? value[1..] : value;
        double result;
        if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            result = Convert.ToInt64(digits[2..], 16);
        }
        else if (digits.Length > 1 && digits[0] == '0' && digits.All(char.IsDigit))
        {
            result = Convert.ToInt64(digits, 8);
        }
        else
        {
            result = double.Parse(digits, System.Globalization.CultureInfo.InvariantCulture);
        }

        return negative ? -result : result;
    }
}
