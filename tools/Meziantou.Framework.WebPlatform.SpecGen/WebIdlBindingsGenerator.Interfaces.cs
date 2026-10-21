using System.Collections;
using System.Reflection;
using System.Text;
using Meziantou.Framework.WebPlatform;
using Meziantou.Framework.WebPlatform.Dom;
using SpecGen.Idl;

namespace SpecGen;

internal sealed partial class WebIdlBindingsGenerator
{
    private static string BindingClass(string interfaceName) => $"Binding_{SafeName(interfaceName)}<TAdapter, TValue>";

    private string GenerateInterface(InterfaceModel model)
    {
        var writer = new CodeWriter("the webref IDL of " + model.Name + " and the .NET members annotated with [IdlMember]");
        writer.Line("namespace Meziantou.Framework.WebPlatform.Scripting.Generated;");
        writer.Line();
        var clrType = model.ClrType!;
        var clrName = TypeName(clrType);
        using (writer.Block($"internal static class {BindingClass(model.Name)} {Constraints}"))
        {
            var flags = new List<string>();
            if (model.Definition.Kind == IdlDefinitionKind.Namespace)
                flags.Add("Namespace");

            if (model.HasHtmlConstructor)
                flags.Add("HtmlConstructor");

            if (model.Definition.HasExtAttr("Global"))
                flags.Add("Global");

            if (model.IsLegacyOverrideBuiltIns)
                flags.Add("LegacyOverrideBuiltIns");

            if (model.IsLegacyUnenumerableNamedProperties)
                flags.Add("LegacyUnenumerableNamedProperties");

            if (model.SpecialOperationsHaveCEReactions)
                flags.Add("SpecialOperationsHaveCEReactions");

            if (model.Definition.HasExtAttr("SecureContext"))
                flags.Add("SecureContext");

            var flagsExpression = flags.Count == 0 ? "global::Meziantou.Framework.WebPlatform.Scripting.InterfaceBindingFlags.None" : string.Join(" | ", flags.Select(f => "global::Meziantou.Framework.WebPlatform.Scripting.InterfaceBindingFlags." + f));
            writer.Line($"public static readonly global::Meziantou.Framework.WebPlatform.Scripting.InterfaceBinding<TAdapter, TValue> Binding = new()");
            writer.Line("{");
            writer.Line($"    Name = {Literal(model.Name)},");
            if (model.Definition.Inheritance is { } parent && model.Parent is not null)
            {
                writer.Line($"    ParentName = {Literal(parent)},");
            }

            writer.Line($"    ClrType = typeof({clrName}),");
            writer.Line($"    Flags = {flagsExpression},");
            writer.Line($"    Exposure = {EmitExposure(GetExposure(model.Definition))},");
            writer.Line($"    ConstructorLength = {model.Constructor?.Length ?? 0},");
            if (model.Constructor is not null)
            {
                writer.Line("    Constructor = Construct,");
            }

            writer.Line("    DefineMembers = DefineMembers,");
            if (!model.Definition.HasExtAttr("Global") && HasUnforgeableMembers(model))
            {
                writer.Line("    DefineUnforgeableMembers = DefineUnforgeableMembers,");
            }

            if (model.SupportsIndexedProperties || model.SupportsNamedProperties)
            {
                writer.Line("    LegacyPlatformObject = CreateLegacyPlatformObject(),");
            }

            writer.Line("};");
            writer.Line();

            var body = new CodeWriter(string.Empty);
            var members = new StringBuilder();
            var unforgeableMembers = new StringBuilder();
            GenerateMembers(model, writer, members, unforgeableMembers);
            using (writer.Block($"private static void DefineMembers({Realm} r, TValue interfaceObject, TValue prototype, TValue instanceTarget)"))
            {
                foreach (var line in members.ToString().Split('\n'))
                {
                    if (line.Length > 0)
                    {
                        writer.Line(line);
                    }
                }
            }

            if (!model.Definition.HasExtAttr("Global") && HasUnforgeableMembers(model))
            {
                using (writer.Block($"private static void DefineUnforgeableMembers({Realm} r, TValue instance)"))
                {
                    foreach (var line in unforgeableMembers.ToString().Split('\n'))
                    {
                        if (line.Length > 0)
                        {
                            writer.Line(line);
                        }
                    }
                }
            }

            if (model.Constructor is not null)
            {
                writer.Line();
                GenerateConstructor(model, writer);
            }

            if (model.SupportsIndexedProperties || model.SupportsNamedProperties)
            {
                writer.Line();
                GenerateLegacyPlatformObject(model, writer);
            }
        }

        return writer.ToString();
    }

    /// <summary>Whether the interface has [LegacyUnforgeable] regular attributes or operations (https://webidl.spec.whatwg.org/#LegacyUnforgeable).</summary>
    private static bool HasUnforgeableMembers(InterfaceModel model)
    {
        bool IsUnforgeable(IdlMember member) => !member.IsStatic && (member.GetExtAttr("LegacyUnforgeable") is not null || model.Definition.HasExtAttr("LegacyUnforgeable"));
        return model.Attributes.Any(a => IsUnforgeable(a.Member)) || model.Operations.Any(o => IsUnforgeable(o.Overloads[0]));
    }

    /// <summary>Generates the member methods in <paramref name="writer"/>, and the statements that define them in <paramref name="define"/>.</summary>
    private void GenerateMembers(InterfaceModel model, CodeWriter writer, StringBuilder define, StringBuilder unforgeableDefine)
    {
        // https://webidl.spec.whatwg.org/#LegacyUnforgeable: the members are non-configurable properties of the instances (of
        // the global object for [Global] interfaces), defined by DefineUnforgeableMembers when the object is created
        var isGlobal = model.Definition.HasExtAttr("Global");
        bool IsUnforgeable(IdlMember member) => !member.IsStatic && (member.GetExtAttr("LegacyUnforgeable") is not null || model.Definition.HasExtAttr("LegacyUnforgeable"));

        var clrType = model.ClrType!;
        var binding = "Binding";
        foreach (var constant in model.Constants)
        {
            define.Append($"r.DefineConstant(interfaceObject, prototype, {Literal(constant.Name)}, {EmitConstant(constant)});\n");
        }

        foreach (var attribute in model.Attributes)
        {
            var member = attribute.Member;
            var target = member.IsStatic ? "interfaceObject" : "instanceTarget";
            var unforgeable = IsUnforgeable(member);
            var sink = unforgeable && !isGlobal ? unforgeableDefine : define;
            var configurable = unforgeable ? ", configurable: false" : "";
            if (unforgeable && !isGlobal)
            {
                target = "instance";
            }

            // https://webidl.spec.whatwg.org/#SecureContext: the member is only exposed in a secure context
            // https://webidl.spec.whatwg.org/#dfn-exposed: the member is only exposed in the global objects of its exposure set
            if (GetMemberExposureCondition(model, member, out var exposure) is false)
                continue;
            var getterName = "Get_" + SafeName(member.Name) + (member.IsStatic ? "_Static" : "");
            var setterName = "Set_" + SafeName(member.Name) + (member.IsStatic ? "_Static" : "");
            var instance = member.IsStatic ? TypeName(attribute.Property.DeclaringType!) : "o";
            var thisStatement = member.IsStatic ? "" : $"var o = r.This<{TypeName(clrType)}>(t, {binding}); ";
            var type = attribute.Type;

            // https://html.spec.whatwg.org/multipage/webappapis.html#event-handler-idl-attributes
            if (type.Name is "EventHandlerNonNull" or "OnErrorEventHandlerNonNull" or "OnBeforeUnloadEventHandlerNonNull")
            {
                var eventType = member.Name[2..];
                var isOnBeforeUnloadEventHandler = Bool(type.Name == "OnBeforeUnloadEventHandlerNonNull");
                if (member.GetExtAttr("LegacyLenientThis") is not null)
                {
                    // https://webidl.spec.whatwg.org/#LegacyLenientThis: an invalid this value is ignored
                    writer.Line($"private static TValue {getterName}({Realm} r, TValue t) => r.TryThis<global::Meziantou.Framework.WebPlatform.Dom.EventTarget>(t, {binding}) is {{ }} o ? r.GetEventHandler(o, {Literal(eventType)}) : r.Adapter.Undefined;");
                    writer.Line($"private static void {setterName}({Realm} r, TValue t, TValue v) {{ if (r.TryThis<global::Meziantou.Framework.WebPlatform.Dom.EventTarget>(t, {binding}) is {{ }} o) r.SetEventHandler(o, {Literal(eventType)}, v, isOnBeforeUnloadEventHandler: {isOnBeforeUnloadEventHandler}); }}");
                }
                else
                {
                    writer.Line($"private static TValue {getterName}({Realm} r, TValue t) => r.GetEventHandler(r.This<global::Meziantou.Framework.WebPlatform.Dom.EventTarget>(t, {binding}), {Literal(eventType)});");
                    writer.Line($"private static void {setterName}({Realm} r, TValue t, TValue v) => r.SetEventHandler(r.This<global::Meziantou.Framework.WebPlatform.Dom.EventTarget>(t, {binding}), {Literal(eventType)}, v, isOnBeforeUnloadEventHandler: {isOnBeforeUnloadEventHandler});");
                }

                define.Append($"{exposure}r.DefineAttribute({target}, {Literal(member.Name)}, {getterName}, {setterName});\n");
                continue;
            }

            // Getter
            string getterExpression;
            var propertyExpression = $"{instance}.{attribute.Property.Name}";
            if (type.Generic == "ObservableArray" && attribute.Property.SetMethod is not null)
            {
                var elementType = GetEnumerableElementType(attribute.Property.PropertyType) ?? attribute.Property.PropertyType.GetGenericArguments()[0];
                var toJs = EmitToJs(elementType, type.Subtypes[0], "e");
                var (natural, naturalExpression) = EmitNatural(new IdlType { Generic = "sequence", Subtypes = [type.Subtypes[0]] }, null, "v");
                var coerce = EmitCoerce(natural, naturalExpression, attribute.Property.PropertyType, out var condition);
                if (coerce is null || condition is not null)
                {
                    Error($"Cannot bind the observable array {model.Name}.{member.Name}");
                    continue;
                }

                getterExpression = $"r.GetObservableArray(o, {Literal(member.Name)}, r => r.FromSequence({propertyExpression}, static (r, e) => {toJs}), (r, v) => {propertyExpression} = {coerce})";
            }
            else if (type.Generic == "FrozenArray")
            {
                var elementType = GetEnumerableElementType(attribute.Property.PropertyType) ?? typeof(object);
                getterExpression = $"r.FromFrozenArray({propertyExpression}, static (r, e) => {EmitToJs(elementType, type.Subtypes[0], "e")})";
            }
            else if (type.Name == "any" && (model.Name, member.Name) is not (("CustomEvent", "detail") or ("MessageEvent", "data") or ("PopStateEvent", "state") or ("History", "state") or ("Window", "opener") or ("XMLHttpRequest", "response") or ("PerformanceMark", "detail") or ("PerformanceMeasure", "detail")))
            {
                // .NET cannot distinguish null from undefined: for "any" attributes (e.g. AbortSignal.reason), null means undefined
                getterExpression = $"{propertyExpression} is {{ }} value ? r.ToJs(value) : r.Adapter.Undefined";
            }
            else if (type.Union && type.Subtypes.Exists(t => t.Name == "undefined") && (!attribute.Property.PropertyType.IsValueType || Nullable.GetUnderlyingType(attribute.Property.PropertyType) is not null))
            {
                // A null .NET value is undefined when the type includes undefined, e.g. Window.event: (Event or undefined)
                getterExpression = $"{propertyExpression} is {{ }} value ? {EmitToJs(attribute.Property.PropertyType, type, "value")} : r.Adapter.Undefined";
            }
            else
            {
                getterExpression = EmitToJs(attribute.Property.PropertyType, type, propertyExpression);
            }

            // The getter of a static attribute (e.g. CSS.highlights) runs with the window of its realm as the current global object
            if (member.IsStatic)
            {
                getterExpression = $"r.InCurrentGlobal(static r => {getterExpression})";
            }

            writer.Line($"private static TValue {getterName}({Realm} r, TValue t) {{ {thisStatement}return {getterExpression}; }}");

            // Setter
            string? setterStatement = null;
            if (!member.Readonly)
            {
                if (type.Generic == "ObservableArray")
                {
                    var (natural, naturalExpression) = EmitNatural(new IdlType { Generic = "sequence", Subtypes = [type.Subtypes[0]] }, null, "v");
                    var coerce = attribute.Property.SetMethod is null ? null : EmitCoerce(natural, naturalExpression, attribute.Property.PropertyType, out _);
                    setterStatement = coerce is null ? $"_ = {naturalExpression};" : $"{propertyExpression} = {coerce};";
                }
                else if ((model.Name, member.Name) is ("Window", "opener"))
                {
                    // https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-opener: null disowns the opener, the
                    // other values define an own data property
                    setterStatement = $"if (r.Adapter.IsNull(v)) {{ {propertyExpression} = null; }} else {{ r.CreateDataPropertyOrThrow(t, {Literal(member.Name)}, v); }}";
                }
                else
                {
                    setterStatement = GenerateAttributeSetter(model, attribute, propertyExpression);
                }
            }

            if (setterStatement is not null)
            {
                writer.Line($"private static void {setterName}({Realm} r, TValue t, TValue v) {{ {thisStatement}{setterStatement} }}");
                sink.Append($"{exposure}r.DefineAttribute({target}, {Literal(member.Name)}, {getterName}, {setterName}{configurable});\n");
            }
            else if (member.GetExtAttr("PutForwards") is { Value: { } forwardName })
            {
                sink.Append($"{exposure}r.DefinePutForwardsAttribute({target}, {Literal(member.Name)}, {getterName}, {Literal(forwardName)}{configurable});\n");
            }
            else if (member.GetExtAttr("Replaceable") is not null)
            {
                sink.Append($"{exposure}r.DefineReplaceableAttribute({target}, {Literal(member.Name)}, {getterName}{configurable});\n");
            }
            else if (member.GetExtAttr("LegacyLenientSetter") is not null)
            {
                sink.Append($"{exposure}r.DefineLenientSetterAttribute({target}, {Literal(member.Name)}, {getterName}{configurable});\n");
            }
            else
            {
                var returnsPromise = type is { Generic: "Promise" } ? ", returnsPromise: true" : "";
                sink.Append($"{exposure}r.DefineAttribute({target}, {Literal(member.Name)}, {getterName}, null{configurable}{returnsPromise});\n");
            }
        }

        // https://webidl.spec.whatwg.org/#interface-prototype-object: the @@unscopables property
        var unscopables = model.Definition.Members.Where(m => m.GetExtAttr("Unscopable") is not null).Select(m => m.Name).Distinct().ToList();
        if (unscopables.Count > 0)
        {
            define.Append($"r.DefineUnscopables(prototype, new[] {{ {string.Join(", ", unscopables.Select(Literal))} }});\n");
        }

        foreach (var operation in model.Operations)
        {
            // https://webidl.spec.whatwg.org/#dfn-exposed
            if (GetMemberExposureCondition(model, operation.Overloads[0], out var operationExposure) is false)
                continue;

            var target = operation.IsStatic ? "interfaceObject" : "instanceTarget";
            var methodName = "Op_" + SafeName(operation.Name) + (operation.IsStatic ? "_Static" : "");
            if (operation.IsDefaultToJson)
            {
                GenerateDefaultToJson(model, methodName, writer);
            }
            else if (!GenerateOperation(model, operation, methodName, writer))
            {
                continue;
            }

            var ceReactions = operation.Overloads[0].GetExtAttr("CEReactions") is not null;

            // https://webidl.spec.whatwg.org/#es-operations: an operation that returns a promise returns a rejected promise
            // instead of throwing (e.g. when this is not a platform object or an argument cannot be converted)
            var returnsPromise = operation.Overloads[0].Type is { Generic: "Promise" };
            var options = (ceReactions ? ", ceReactions: true" : "") + (returnsPromise ? ", returnsPromise: true" : "");
            if (IsUnforgeable(operation.Overloads[0]))
            {
                (isGlobal ? define : unforgeableDefine).Append($"{operationExposure}r.DefineOperation({(isGlobal ? target : "instance")}, {Literal(operation.Name)}, {operation.Length}, {methodName}{options}, configurable: false);\n");
                continue;
            }

            define.Append($"{operationExposure}r.DefineOperation({target}, {Literal(operation.Name)}, {operation.Length}, {methodName}{options});\n");
        }

        // Stringifiers
        if (model.StringifierAttribute is { } stringifierAttribute)
        {
            var getterName = "Get_" + SafeName(stringifierAttribute.Member.Name);
            writer.Line($"private static TValue Op_toString({Realm} r, TValue t, global::System.ReadOnlySpan<TValue> a) => {getterName}(r, t);");
            if (IsUnforgeable(stringifierAttribute.Member) && !isGlobal)
            {
                unforgeableDefine.Append("r.DefineOperation(instance, \"toString\", 0, Op_toString, configurable: false);\n");
            }
            else
            {
                define.Append("r.DefineOperation(prototype, \"toString\", 0, Op_toString);\n");
            }
        }
        else if (model.HasStringifierOperation)
        {
            // The stringifier may be exposed in fewer global objects than its interface (e.g. [Exposed=Window] stringifier of DOMMatrixReadOnly)
            var stringifier = model.Definition.Members.Find(m => m.Kind == IdlMemberKind.Operation && m.Special == "stringifier" && m.Name.Length == 0);
            var stringifierExposure = "";
            if (stringifier is null || GetMemberExposureCondition(model, stringifier, out stringifierExposure))
            {
                writer.Line($"private static TValue Op_toString({Realm} r, TValue t, global::System.ReadOnlySpan<TValue> a) => r.Adapter.FromString(r.This<{TypeName(clrType)}>(t, Binding).ToString() ?? \"\");");
                define.Append($"{stringifierExposure}r.DefineOperation(prototype, \"toString\", 0, Op_toString);\n");
            }
        }

        // Iterables (https://webidl.spec.whatwg.org/#es-iterator)
        if (model.Iterable is { DeclarationTypes.Count: 1 })
        {
            define.Append("r.DefineValueIterator(prototype, iterable: true);\n");
        }
        else if (model.Iterable is { DeclarationTypes.Count: 2 } pairIterable)
        {
            var pairType = clrType.GetInterfaces().Concat([clrType])
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                .Select(i => i.GetGenericArguments()[0])
                .FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(KeyValuePair<,>));
            if (pairType is null)
            {
                Error($"The pair iterator of {model.Name} needs IEnumerable<KeyValuePair<K, V>>");
            }
            else
            {
                var arguments = pairType.GetGenericArguments();
                var keyToJs = EmitToJs(arguments[0], pairIterable.DeclarationTypes[0], "p.Key");
                var valueToJs = EmitToJs(arguments[1], pairIterable.DeclarationTypes[1], "p.Value");
                writer.Line($"private static (TValue Key, TValue Value)? PairAt({Realm} r, object o, int index) => global::Meziantou.Framework.WebPlatform.Scripting.PairIterable.TryGetPair(({TypeName(typeof(IEnumerable<>).MakeGenericType(pairType))})o, index, out var p) ? ({keyToJs}, {valueToJs}) : null;");
                define.Append("r.DefinePairIterator(prototype, PairAt, Binding);\n");
            }
        }
        else if (model.SupportsIndexedProperties)
        {
            define.Append("r.DefineValueIterator(prototype, iterable: false);\n");
        }

        if (model.Setlike is not null)
        {
            GenerateSetlike(model, writer, define);
        }

        if (model.Maplike is not null)
        {
            GenerateMaplike(model, writer, define);
        }

        if (model.Name == "CSSStyleProperties")
        {
            GenerateCssPropertyAccessors(writer, define);
        }
    }

    private string EmitConstant(IdlMember constant)
    {
        var value = constant.ConstValue!.Value;
        return value.GetProperty("type").GetString() switch
        {
            "boolean" => $"r.Adapter.FromBoolean({Bool(value.GetProperty("value").GetBoolean())})",
            "number" => $"r.Adapter.FromNumber({Literal(ParseIdlNumber(value.GetProperty("value").GetString()!))})",
            "Infinity" => value.TryGetProperty("negative", out var negative) && negative.GetBoolean() ? "r.Adapter.FromNumber(double.NegativeInfinity)" : "r.Adapter.FromNumber(double.PositiveInfinity)",
            "NaN" => "r.Adapter.FromNumber(double.NaN)",
            var type => throw new InvalidOperationException("Unsupported constant type " + type),
        };
    }

    private string GenerateAttributeSetter(InterfaceModel model, AttributeModel attribute, string propertyExpression)
    {
        var member = attribute.Member;
        var statements = new StringBuilder();

        // A platform object that the .NET property cannot represent is given to the setter method annotated with the name
        // of the attribute (e.g. a TrustedHTML assigned to innerHTML, or a TrustedScript assigned to the textContent of a script)
        var instanceType = attribute.Member.IsStatic ? null : model.ClrType;
        if (instanceType is not null && attribute.Property.SetMethod is not null)
        {
            var setterMethods = instanceType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => !m.IsSpecialName && m.ReturnType == typeof(void) && m.GetParameters() is [{ ParameterType.IsClass: true } parameter] && parameter.ParameterType != typeof(string)
                    && m.GetCustomAttributes<IdlMemberAttribute>(inherit: false).Any(a => a.Name == member.Name))
                .ToList();
            foreach (var setterMethod in setterMethods.OrderByDescending(m => Depth(m.GetParameters()[0].ParameterType)))
            {
                statements.Append($"if (r.Unwrap(v) is {TypeName(setterMethod.GetParameters()[0].ParameterType)} platformObject{setterMethods.IndexOf(setterMethod)}) {{ o.{setterMethod.Name}(platformObject{setterMethods.IndexOf(setterMethod)}); return; }} ");
            }
        }

        // https://webidl.spec.whatwg.org/#dfn-attribute-setter, step 4.4: a value that is not a value of the enumeration is ignored
        if (_idl.ResolveTypedefs(attribute.Type) is { Nullable: false, Union: false } enumType && Definition(enumType) is { Kind: IdlDefinitionKind.Enum } enumDefinition)
        {
            var values = "new[] { " + string.Join(", ", enumDefinition.EnumValues.Select(Literal)) + " }";
            var field = GetHelper($"Enum_{SafeName(enumDefinition.Name)}_Values", $"internal static readonly string[] Enum_{SafeName(enumDefinition.Name)}_Values = {values};");
            statements.Append($"var s = r.ToDomString(v); if (global::System.Array.IndexOf({HelpersClass}.{field}, s) < 0) return; v = r.Adapter.FromString(s); ");
        }

        var (natural, naturalExpression) = EmitNatural(attribute.Type, member.ExtAttrs, "v");
        if (attribute.Property.SetMethod is null)
        {
            statements.Append($"_ = {naturalExpression};");
            return statements.ToString();
        }

        var coerce = EmitCoerce(natural, "x", attribute.Property.PropertyType, out var condition);
        if (coerce is null)
        {
            Error($"Cannot convert the attribute {model.Name}.{member.Name} ({attribute.Type}) to {attribute.Property.PropertyType}");
            return "";
        }

        // https://webidl.spec.whatwg.org/#dfn-attribute-setter: a value that is not one of the values of the enumeration is ignored
        var enumerationPrefix = $"r.ToEnumerationValue(v, {Literal(natural.Name ?? "")}, ";
        if (natural.Kind == NaturalKind.Enumeration && naturalExpression.StartsWith(enumerationPrefix, StringComparison.Ordinal))
        {
            statements.Append($"var x = r.ToEnumerationValueOrNull(v, {naturalExpression[enumerationPrefix.Length..]}; if (x is null) return; ");
        }
        else
        {
            statements.Append($"var x = {naturalExpression}; ");
        }

        if (condition is not null)
        {
            statements.Append($"if (!({condition})) throw r.CreateTypeError({Literal($"Cannot convert the value to {attribute.Property.PropertyType.Name}")}); ");
        }

        statements.Append($"{propertyExpression} = {coerce};");
        return statements.ToString();
    }

    private static int Depth(Type type)
    {
        var depth = 0;
        for (var current = type; current is not null; current = current.BaseType)
        {
            depth++;
        }

        return depth;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Operations
    // ----------------------------------------------------------------------------------------------------------------

    private bool GenerateOperation(InterfaceModel model, OperationModel operation, string methodName, CodeWriter writer)
    {
        var body = new List<string>();
        if (!operation.IsStatic)
        {
            body.Add($"var o = r.This<{TypeName(model.ClrType!)}>(t, Binding);");
        }

        var overloadsField = operation.Overloads.Count > 1 ? GenerateOverloadInfos(operation, writer, methodName) : null;
        if (overloadsField is not null)
        {
            body.Add($"switch (r.SelectOverload({Literal(operation.Name)}, {overloadsField}, a))");
            body.Add("{");
        }

        for (var i = 0; i < operation.Overloads.Count; i++)
        {
            var overload = operation.Overloads[i];
            var statements = GenerateOverloadCall(model, operation, overload, operation.ClrMethods, isConstructor: false);
            if (statements is null)
                return false;

            if (overloadsField is not null)
            {
                body.Add($"    case {i}:");
                body.Add("    {");
                body.AddRange(statements.Select(s => "        " + s));
                body.Add("    }");
            }
            else
            {
                body.AddRange(statements);
            }
        }

        if (overloadsField is not null)
        {
            body.Add("    default:");
            body.Add("        throw new global::System.InvalidOperationException(\"Unexpected overload\");");
            body.Add("}");
        }

        using (writer.Block($"private static TValue {methodName}({Realm} r, TValue t, global::System.ReadOnlySpan<TValue> a)"))
        {
            foreach (var statement in body)
            {
                writer.Line(statement);
            }
        }

        return true;
    }

    /// <summary>
    /// Generates the default toJSON steps (https://webidl.spec.whatwg.org/#default-tojson-steps): the values of the attributes
    /// of the inheritance stack whose type is a JSON type, collected by the interfaces that declare a [Default] toJSON.
    /// </summary>
    private void GenerateDefaultToJson(InterfaceModel model, string methodName, CodeWriter writer)
    {
        using (writer.Block($"private static TValue {methodName}({Realm} r, TValue t, global::System.ReadOnlySpan<TValue> a)"))
        {
            writer.Line($"r.This<{TypeName(model.ClrType!)}>(t, Binding);");
            writer.Line("var result = r.CreateObject();");
            writer.Line("CollectAttributeValues(r, t, result);");
            writer.Line("return result;");
        }

        // https://webidl.spec.whatwg.org/#collect-attribute-values-of-an-inheritance-stack: the attributes of the inherited
        // interfaces first. The platform objects are wrapped in the relevant realm of the object (they belong to it), the
        // other values are converted in the current realm.
        using (writer.Block($"internal static void CollectAttributeValues({Realm} r, TValue t, TValue result)"))
        {
            for (var parent = model.Parent; parent is not null; parent = parent.Parent)
            {
                if (parent.Operations.Exists(o => o.IsDefaultToJson))
                {
                    writer.Line($"{BindingClass(parent.Name)}.CollectAttributeValues(r, t, result);");
                    break;
                }
            }

            foreach (var attribute in model.Attributes)
            {
                if (attribute.Member.IsStatic || !IsJsonType(attribute.Type, []))
                    continue;

                var realm = FlattenUnion(attribute.Type).Any(IsInterfaceIdlType) ? "r.GetRelevantRealm(t)" : "r";
                writer.Line($"r.SetMember(result, {Literal(attribute.Member.Name)}, Get_{SafeName(attribute.Member.Name)}({realm}, t));");
            }
        }
    }

    /// <summary>Whether the type is a JSON type (https://webidl.spec.whatwg.org/#dfn-json-types).</summary>
    private bool IsJsonType(IdlType type, HashSet<string> dictionaries)
    {
        type = _idl.ResolveTypedefs(type);
        if (type.Union)
            return type.Subtypes.TrueForAll(t => IsJsonType(t, dictionaries));

        switch (type.Generic)
        {
            case "sequence" or "FrozenArray":
                return IsJsonType(type.Subtypes[0], dictionaries);
            case "record":
                return IsStringIdlType(type.Subtypes[0]) && IsJsonType(type.Subtypes[1], dictionaries);
            case "":
                break;
            default:
                return false;
        }

        if (IsNumericIdlType(type) || IsStringIdlType(type) || type.Name is "boolean" or "object")
            return true;

        var definition = Definition(type);
        switch (definition?.Kind)
        {
            case IdlDefinitionKind.Interface:
                // An interface that declares a toJSON operation, or inherits one
                for (var current = definition; current is not null; current = current.Inheritance is { } parent ? _idl.Get(parent) : null)
                {
                    if (current.Members.Exists(m => m.Kind == IdlMemberKind.Operation && m.Name == "toJSON" && !m.IsStatic))
                        return true;
                }

                return false;

            case IdlDefinitionKind.Dictionary:
                // A dictionary whose members (including the inherited ones) are JSON types
                if (!dictionaries.Add(definition.Name))
                    return true;

                return GetDictionaryHierarchy(definition).TrueForAll(d => d.Members.Where(m => m.Kind == IdlMemberKind.Field).All(m => IsJsonType(m.Type!, dictionaries)));

            default:
                return false;
        }
    }

    private void GenerateConstructor(InterfaceModel model, CodeWriter writer)
    {
        var constructor = model.Constructor!;
        var body = new List<string>();
        var overloadsField = constructor.Overloads.Count > 1 ? GenerateOverloadInfos(constructor, writer, "Construct") : null;
        if (overloadsField is not null)
        {
            body.Add($"switch (r.SelectOverload(\"constructor\", {overloadsField}, a))");
            body.Add("{");
        }

        for (var i = 0; i < constructor.Overloads.Count; i++)
        {
            var statements = GenerateOverloadCall(model, constructor, constructor.Overloads[i], constructor.ClrMethods, isConstructor: true);
            if (statements is null)
                return;

            if (overloadsField is not null)
            {
                body.Add($"    case {i}:");
                body.Add("    {");
                body.AddRange(statements.Select(s => "        " + s));
                body.Add("    }");
            }
            else
            {
                body.AddRange(statements);
            }
        }

        if (overloadsField is not null)
        {
            body.Add("    default:");
            body.Add("        throw new global::System.InvalidOperationException(\"Unexpected overload\");");
            body.Add("}");
        }

        using (writer.Block($"private static object Construct({Realm} r, global::System.ReadOnlySpan<TValue> a)"))
        {
            foreach (var statement in body)
            {
                writer.Line(statement);
            }
        }
    }

    /// <summary>Generates the descriptors of the overloads, used by the overload resolution algorithm.</summary>
    private string GenerateOverloadInfos(OperationModel operation, CodeWriter writer, string methodName)
    {
        var field = methodName + "_Overloads";
        var overloads = new List<string>();
        foreach (var overload in operation.Overloads)
        {
            var arguments = overload.Arguments.Select(argument =>
            {
                var types = FlattenUnion(argument.Type).ToList();
                var kinds = new List<string>();
                var interfaceTypes = new List<string>();
                if (_idl.ResolveTypedefs(argument.Type).Nullable || types.Exists(t => t.Nullable))
                    kinds.Add("Nullable");

                foreach (var type in types)
                {
                    var definition = Definition(type);
                    if (type.Nullable)
                        kinds.Add("Nullable");

                    switch (definition?.Kind)
                    {
                        case IdlDefinitionKind.Dictionary:
                            kinds.Add("Dictionary");
                            break;
                        case IdlDefinitionKind.Interface:
                            kinds.Add("Interface");
                            if (GetInterfaceClrType(type) is { } interfaceType)
                            {
                                interfaceTypes.Add($"typeof({TypeName(interfaceType)})");
                            }

                            break;
                        case IdlDefinitionKind.Callback:
                            kinds.Add("CallbackFunction");
                            break;
                        case IdlDefinitionKind.CallbackInterface:
                            kinds.Add("CallbackInterface");
                            break;
                    }

                    if (type.Generic is "sequence" or "FrozenArray")
                        kinds.Add("Sequence");

                    if (type.Generic == "record")
                        kinds.Add("Record");

                    if (type.Name == "object")
                        kinds.Add("Object");

                    if (type.Name == "boolean")
                        kinds.Add("Boolean");

                    if (IsNumericIdlType(type))
                        kinds.Add("Numeric");

                    if (IsStringIdlType(type))
                        kinds.Add("String");

                    if (type.Name == "any")
                        kinds.Add("Any");

                    if (IsBufferIdlType(type))
                        kinds.Add("BufferSource");
                }

                var kindsExpression = kinds.Count == 0 ? "global::Meziantou.Framework.WebPlatform.Scripting.OverloadTypeKinds.None" : string.Join(" | ", kinds.Distinct().Select(k => "global::Meziantou.Framework.WebPlatform.Scripting.OverloadTypeKinds." + k));
                return $"new({Literal(argument.Type.ToString())}, {kindsExpression}, new global::System.Type[] {{ {string.Join(", ", interfaceTypes.Distinct())} }}, {Bool(argument.Optional)}, {Bool(argument.Variadic)})";
            });

            overloads.Add($"new(new global::Meziantou.Framework.WebPlatform.Scripting.OverloadArgument[] {{ {string.Join(", ", arguments)} }})");
        }

        writer.Line($"private static readonly global::Meziantou.Framework.WebPlatform.Scripting.OverloadInfo[] {field} = [{string.Join(", ", overloads)}];");
        return field;
    }

    /// <summary>
    /// Generates the statements that convert the arguments of an overload, select the .NET method (or constructor) that can
    /// take them, invoke it and return its converted result.
    /// </summary>
    private List<string>? GenerateOverloadCall(InterfaceModel model, OperationModel operation, IdlMember overload, List<MethodBase> clrMethods, bool isConstructor)
    {
        var statements = new List<string>();
        var required = overload.Arguments.Count(a => !a.Optional && !a.Variadic);
        if (required > 0)
        {
            statements.Add($"if (a.Length < {required}) throw r.NotEnoughArguments({Literal(isConstructor ? model.Name : operation.Name)}, {required}, a.Length);");
        }

        // The natural values of the arguments
        var naturals = new List<(Natural Natural, string Variable, string? HasVariable, bool Variadic)>();
        var defaultArguments = new Dictionary<int, string>(); // the optional arguments with a default value: the condition "missing"
        for (var i = 0; i < overload.Arguments.Count; i++)
        {
            var argument = overload.Arguments[i];
            var variable = "a" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (argument.Variadic)
            {
                var (element, elementExpression) = EmitNatural(argument.Type, argument.ExtAttrs, "a[i]");
                var listType = $"global::System.Collections.Generic.List<{element.CSharpType}>";
                statements.Add($"var {variable} = new {listType}(); for (var i = {i}; i < a.Length; i++) {{ {variable}.Add({elementExpression}); }}");
                naturals.Add((new Natural(NaturalKind.Sequence, listType, Element: element), variable, null, true));
                continue;
            }

            var (natural, expression) = EmitNatural(argument.Type, argument.ExtAttrs, $"a[{i}]");
            if (!argument.Optional)
            {
                statements.Add($"var {variable} = {expression};");
                naturals.Add((natural, variable, null, false));
                continue;
            }

            if (argument.Default is { } defaultValue && EmitDefaultValue(defaultValue, argument.Type, argument.ExtAttrs, out _) is { } defaultExpression)
            {
                var type = natural.CSharpType;
                statements.Add($"var {variable} = r.IsMissing(a, {i}) ? ({type}){defaultExpression} : {expression};");
                naturals.Add((natural, variable, null, false));
                defaultArguments[i] = $"r.IsMissing(a, {i})";
                continue;
            }

            // An optional argument without default value: it may be missing
            var hasVariable = "has" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var nullableType = natural.CSharpType.EndsWith('?', StringComparison.Ordinal) ? natural.CSharpType : natural.CSharpType + "?";
            statements.Add($"var {hasVariable} = !r.IsMissing(a, {i}); var {variable} = {hasVariable} ? ({nullableType}){expression} : default;");
            naturals.Add((natural with { CSharpType = nullableType }, variable, hasVariable, false));
        }

        // The .NET methods that can take the arguments
        var candidates = new List<(string Condition, string Call, Type ReturnType)>();
        foreach (var method in clrMethods.OrderBy(m => m.GetParameters().Length))
        {
            if (TryBindMethod(method, naturals, defaultArguments, out var condition, out var call))
            {
                var returnType = method is MethodInfo methodInfo ? methodInfo.ReturnType : method.DeclaringType!;
                var target = isConstructor ? $"new {TypeName(method.DeclaringType!)}" : method.IsStatic ? $"{TypeName(method.DeclaringType!)}.{method.Name}" : $"o.{method.Name}";
                candidates.Add((condition, $"{target}({call})", returnType));
            }
        }

        if (candidates.Count == 0)
        {
            Error($"No .NET method of {model.Name}.{operation.Name} matches the IDL overload ({string.Join(", ", overload.Arguments.Select(a => a.Type))}): {string.Join("; ", clrMethods.Select(m => m.ToString()))}");
            return null;
        }

        foreach (var (condition, call, returnType) in candidates)
        {
            string returnStatement;
            if (isConstructor)
            {
                returnStatement = $"return {call};";
            }
            else if (returnType == typeof(void))
            {
                returnStatement = $"{call}; return r.Adapter.Undefined;";
            }
            else
            {
                var returnIdlType = overload.Type ?? new IdlType { Name = "any" };
                var toJs = EmitToJs(returnType, returnIdlType, "result");
                if (returnIdlType.Union && returnIdlType.Subtypes.Exists(t => t.Name == "undefined"))
                {
                    toJs = $"result is null ? r.Adapter.Undefined : {toJs}";
                }

                returnStatement = $"var result = {call}; return {toJs};";
            }

            if (condition.Length == 0)
            {
                statements.Add(returnStatement);
                return statements;
            }

            statements.Add($"if ({condition}) {{ {returnStatement} }}");
        }

        statements.Add($"throw r.CreateTypeError({Literal($"Failed to execute '{operation.Name}': the arguments are not supported")});");
        return statements;
    }

    /// <summary>
    /// Binds the natural values of the arguments to the parameters of a .NET method: <paramref name="condition"/> is the
    /// runtime condition (empty when always true), <paramref name="call"/> the arguments of the call.
    /// </summary>
    private bool TryBindMethod(MethodBase method, List<(Natural Natural, string Variable, string? HasVariable, bool Variadic)> naturals, Dictionary<int, string> defaultArguments, out string condition, out string call)
    {
        var conditions = new List<string>();
        var arguments = new List<string>();
        var parameters = method.GetParameters();
        condition = "";
        call = "";
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            var isParams = parameter.IsDefined(typeof(ParamArrayAttribute)) || parameter.IsDefined(typeof(System.Runtime.CompilerServices.ParamCollectionAttribute));
            if (i >= naturals.Count)
            {
                if (isParams)
                {
                    arguments.Add(parameter.ParameterType.IsArray ? $"global::System.Array.Empty<{TypeName(parameter.ParameterType.GetElementType()!)}>()" : "default");
                    continue;
                }

                if (!parameter.HasDefaultValue && !parameter.IsOptional)
                    return false;

                arguments.Add(EmitParameterDefault(parameter));
                continue;
            }

            var (natural, variable, hasVariable, variadic) = naturals[i];
            if (isParams && !variadic)
            {
                // A params parameter that takes the remaining arguments (all of the same natural type)
                var elementType = parameter.ParameterType.GetElementType() ?? GetEnumerableElementType(parameter.ParameterType)!;
                var remaining = naturals.Skip(i).ToList();
                var items = new List<string>();
                foreach (var item in remaining)
                {
                    var itemExpression = EmitCoerce(item.Natural, item.Variable, elementType, out var itemCondition);
                    if (itemExpression is null || itemCondition is not null || item.HasVariable is not null)
                        return false;

                    items.Add(itemExpression);
                }

                arguments.Add($"new {TypeName(elementType)}[] {{ {string.Join(", ", items)} }}");
                naturals = naturals.Take(i).ToList();
                break;
            }

            var coerced = EmitCoerce(natural, variable, parameter.ParameterType, out var coerceCondition);
            if (coerced is null)
                return false;

            if (coerceCondition is not null)
            {
                conditions.Add($"({coerceCondition})");
            }

            if (hasVariable is not null)
            {
                // The argument may be missing: the default value of the parameter is used
                if (!parameter.HasDefaultValue && !parameter.IsOptional)
                {
                    conditions.Add(hasVariable);
                    arguments.Add(coerced);
                }
                else
                {
                    arguments.Add($"({hasVariable} ? {coerced} : {EmitParameterDefault(parameter)})");
                }
            }
            else
            {
                arguments.Add(coerced);
            }
        }

        // The extra arguments must be missing
        for (var i = parameters.Length; i < naturals.Count; i++)
        {
            var (_, variable, hasVariable, variadic) = naturals[i];
            if (variadic)
            {
                conditions.Add($"{variable}.Count == 0");
            }
            else if (hasVariable is not null)
            {
                conditions.Add("!" + hasVariable);
            }
            else if (defaultArguments.TryGetValue(i, out var missingCondition))
            {
                // An argument with a default value that the .NET method does not take: it must not be passed
                conditions.Add(missingCondition);
            }
            else
            {
                return false;
            }
        }

        condition = string.Join(" && ", conditions);
        call = string.Join(", ", arguments);
        return true;
    }

    private static string EmitParameterDefault(ParameterInfo parameter)
    {
        var type = parameter.ParameterType;
        if (!parameter.HasDefaultValue || parameter.DefaultValue is null)
            return type.IsValueType ? $"default({TypeName(type)})" : $"({TypeName(type)}?)null";

        var value = parameter.DefaultValue;
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying.IsEnum)
            return $"({TypeName(type)}){Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture)}";

        return value switch
        {
            string s => Literal(s),
            bool b => Bool(b),
            double d => Literal(d),
            float f => $"({TypeName(type)}){Literal(f)}",
            _ => $"({TypeName(type)})({Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)})",
        };
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Legacy platform objects, setlike, CSS properties
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Returns the IDL indexed setter of an interface or of its inherited interfaces (https://webidl.spec.whatwg.org/#dfn-indexed-property-setter).</summary>
    private static IdlMember? FindIndexedSetter(InterfaceModel model)
    {
        for (var current = model; current is not null; current = current.Parent)
        {
            if (current.Definition.Members.Find(m => m.Special == "setter" && m.Arguments is [{ Type.Name: "unsigned long" }, _]) is { } setter)
                return setter;
        }

        return null;
    }

    private void GenerateLegacyPlatformObject(InterfaceModel model, CodeWriter writer)
    {
        using (writer.Block($"private static global::Meziantou.Framework.WebPlatform.Scripting.LegacyPlatformObjectBinding<TAdapter, TValue> CreateLegacyPlatformObject() => new()"))
        {
            if (model.SupportsIndexedProperties)
            {
                var getter = model.IndexedGetter!;
                var indexType = TypeName(getter.GetParameters()[0].ParameterType);
                var call = getter.IsSpecialName ? $"(({TypeName(getter.DeclaringType!)})o)[({indexType})i]" : $"(({TypeName(getter.DeclaringType!)})o).{getter.Name}(({indexType})i)";
                writer.Line($"Length = static o => (uint)(({TypeName(model.LengthProperty!.DeclaringType!)})o).{model.LengthProperty.Name},");
                writer.Line($"IndexedGetter = static (r, o, i) => {EmitToJs(getter.ReturnType, new IdlType { Name = "any" }, call)},");
                if (model.IndexedSetter is { } unionSetter && IsUnionStruct(Nullable.GetUnderlyingType(unionSetter.GetParameters()[1].ParameterType) ?? unionSetter.GetParameters()[1].ParameterType)
                    && FindIndexedSetter(model) is { } idlSetter && EmitNatural(idlSetter.Arguments[1].Type, idlSetter.Arguments[1].ExtAttrs, "v") is var (setterNatural, setterNaturalExpression)
                    && EmitCoerce(setterNatural, "x", unionSetter.GetParameters()[1].ParameterType, out var setterCondition) is { } setterCoerce && setterCondition is null)
                {
                    // A union (e.g. the CSSUnparsedSegment of CSSUnparsedValue): the value is converted to the union, then to the .NET union struct
                    writer.Line($"IndexedSetter = static (r, o, i, v) => {{ var x = {setterNaturalExpression}; (({TypeName(unionSetter.DeclaringType!)})o)[({TypeName(unionSetter.GetParameters()[0].ParameterType)})i] = {setterCoerce}; }},");
                }
                else if (model.IndexedSetter is { } setter)
                {
                    var valueType = setter.GetParameters()[1].ParameterType;
                    var interfaceName = GetInterfaceName(valueType);
                    var conversion = new NullabilityInfoContext().Create(setter.GetParameters()[1]).WriteState == NullabilityState.Nullable ? "ToNullableInterface" : "ToInterface";
                    writer.Line($"IndexedSetter = static (r, o, i, v) => (({TypeName(setter.DeclaringType!)})o)[({TypeName(setter.GetParameters()[0].ParameterType)})i] = r.{conversion}<{TypeName(Nullable.GetUnderlyingType(valueType) ?? valueType).TrimEnd('?')}>(v, {Literal(interfaceName)}),");
                }
            }

            if (model.SupportsNamedProperties)
            {
                var getter = model.NamedGetter!;
                var call = getter.IsSpecialName ? $"(({TypeName(getter.DeclaringType!)})o)[n]" : $"(({TypeName(getter.DeclaringType!)})o).{getter.Name}(n)";
                writer.Line($"NamedGetter = static (r, o, n) => {EmitToJs(getter.ReturnType, new IdlType { Name = "any" }, call)},");
                var names = model.SupportedPropertyNames!;
                writer.Line($"SupportedPropertyNames = static o => global::System.Linq.Enumerable.ToList((({TypeName(names.DeclaringType!)})o).{names.Name}()),");
                if (model.NamedSetter is { } setter)
                {
                    writer.Line(setter.IsSpecialName
                        ? $"NamedSetter = static (r, o, n, v) => (({TypeName(setter.DeclaringType!)})o)[n] = r.ToDomString(v),"
                        : $"NamedSetter = static (r, o, n, v) => (({TypeName(setter.DeclaringType!)})o).{setter.Name}(n, r.ToDomString(v)),");
                }

                if (model.NamedDeleter is { } deleter)
                {
                    writer.Line($"NamedDeleter = static (r, o, n) => (({TypeName(deleter.DeclaringType!)})o).{deleter.Name}(n),");
                }
            }
        }

        writer.Line(";");
    }

    private string GetInterfaceName(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return _model.GetInterface(underlying)?.Name ?? underlying.Name;
    }

    private void GenerateSetlike(InterfaceModel model, CodeWriter writer, StringBuilder define)
    {
        var type = model.ClrType!;
        var name = TypeName(type);
        var readOnly = model.Setlike!.Readonly;

        // The values are strings (setlike<DOMString>) or platform objects (e.g. setlike<FontFace>)
        var valueType = model.Setlike.DeclarationTypes[0];
        var valueTypeName = valueType.Name ?? "";
        var elementType = valueTypeName is "DOMString" or "USVString" or "CSSOMString" ? null : _model.Interfaces.GetValueOrDefault(valueTypeName)?.ClrType;
        if (valueTypeName is not ("DOMString" or "USVString" or "CSSOMString") && elementType is null)
        {
            Error($"Unsupported setlike type {valueType} in {model.Name}");
        }

        var elementTypeName = elementType is null ? "string" : TypeName(elementType);
        writer.Line("private static readonly global::Meziantou.Framework.WebPlatform.Scripting.SetlikeBinding Setlike = new()");
        writer.Line("{");
        if (elementType is not null)
        {
            writer.Line($"    ElementType = typeof({elementTypeName}),");
            writer.Line($"    ElementName = {CodeWriter.Literal(valueTypeName)},");
        }

        writer.Line($"    Count = static o => (({name})o).Count,");
        writer.Line($"    Has = static (o, v) => (({name})o).Has(({elementTypeName})v),");
        writer.Line($"    GetEnumerator = static o => ((global::System.Collections.IEnumerable)(({name})o)).GetEnumerator(),");

        // https://webidl.spec.whatwg.org/#es-add-delete: the operations declared by the interface replace the ones of the setlike
        if (!readOnly && !model.Definition.Members.Exists(m => m.Kind == IdlMemberKind.Operation && m.Name is "add" or "delete" or "clear"))
        {
            writer.Line($"    Add = static (o, v) => (({name})o).Add(({elementTypeName})v),");
            writer.Line($"    Delete = static (o, v) => (({name})o).Delete(({elementTypeName})v),");
            writer.Line($"    Clear = static o => (({name})o).Clear(),");
        }

        writer.Line("};");
        define.Append("r.DefineSetlike(prototype, Binding, Setlike);\n");
    }

    private void GenerateMaplike(InterfaceModel model, CodeWriter writer, StringBuilder define)
    {
        var type = model.ClrType!;
        var name = TypeName(type);
        var readOnly = model.Maplike!.Readonly;

        // The keys are strings and the values are platform objects (e.g. maplike<DOMString, Highlight>), sequences of
        // unsigned long (maplike<CSSOMString, sequence<unsigned long>>, converted to arrays by ToJs) or unsigned long long
        var keyTypeName = model.Maplike.DeclarationTypes[0].Name ?? "";
        var valueIdlType = model.Maplike.DeclarationTypes[1];
        var valueTypeName = valueIdlType is { Generic: "sequence" } ? "sequence<" + valueIdlType.Subtypes[0].Name + ">" : valueIdlType.Name ?? "";
        var valueType = valueTypeName switch
        {
            "sequence<unsigned long>" => typeof(IReadOnlyList<uint>),
            // maplike<DOMString, unsigned long long> (EventCounts): the numbers are converted by ToJs
            "unsigned long long" => typeof(ulong),
            _ => _model.Interfaces.GetValueOrDefault(valueTypeName)?.ClrType,
        };
        if (keyTypeName is not ("DOMString" or "USVString" or "CSSOMString") || valueType is null)
        {
            Error($"Unsupported maplike types {keyTypeName}, {valueTypeName} in {model.Name}");
            return;
        }

        var valueClrName = TypeName(valueType);
        var pairType = $"global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, {valueClrName}>>";
        writer.Line("private static readonly global::Meziantou.Framework.WebPlatform.Scripting.MaplikeBinding Maplike = new()");
        writer.Line("{");
        writer.Line($"    ValueType = typeof({valueClrName}),");
        writer.Line($"    ValueName = {CodeWriter.Literal(valueTypeName)},");
        writer.Line($"    Count = static o => (({name})o).Count,");
        writer.Line($"    Get = static (o, k) => (({name})o).Get(k),");
        writer.Line($"    Has = static (o, k) => (({name})o).Has(k),");
        writer.Line($"    GetEnumerator = static o => global::System.Linq.Enumerable.Select(({pairType})o, static p => (p.Key, (object)p.Value)).GetEnumerator(),");

        // https://webidl.spec.whatwg.org/#es-map-set: the operations declared by the interface replace the ones of the maplike
        bool IsDeclared(string operation) => model.Definition.Members.Exists(m => m.Kind == IdlMemberKind.Operation && m.Name == operation);
        if (!readOnly && !IsDeclared("set"))
        {
            writer.Line($"    Set = static (o, k, v) => (({name})o).Set(k, ({valueClrName})v),");
        }

        if (!readOnly && !IsDeclared("delete"))
        {
            writer.Line($"    Delete = static (o, k) => (({name})o).Delete(k),");
        }

        if (!readOnly && !IsDeclared("clear"))
        {
            writer.Line($"    Clear = static o => (({name})o).Clear(),");
        }

        writer.Line("};");
        define.Append("r.DefineMaplike(prototype, Binding, Maplike);\n");
    }

    /// <summary>The camel-cased, webkit-cased and dashed attributes of the CSS properties (https://drafts.csswg.org/cssom/#cssstyleproperties).</summary>
    private static void GenerateCssPropertyAccessors(CodeWriter writer, StringBuilder define)
    {
        var properties = new List<(string Attribute, string Property)>();
        foreach (var property in typeof(Meziantou.Framework.WebPlatform.Css.Om.CssStyleProperties).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var attributes = property.GetCustomAttributes<IdlMemberAttribute>().Where(IsCssPropertyAttribute).ToList();
            if (attributes.Count == 0)
                continue;

            var cssName = GetCssPropertyName(attributes);
            foreach (var attribute in attributes)
            {
                properties.Add((attribute.Name, cssName));
            }
        }

        writer.Line("private static readonly (string AttributeName, string PropertyName)[] CssProperties =");
        writer.Line("[");
        foreach (var (attribute, property) in properties)
        {
            writer.Line($"    ({Literal(attribute)}, {Literal(property)}),");
        }

        writer.Line("];");
        define.Append("r.DefineCssPropertyAccessors(prototype, Binding, CssProperties);\n");
    }

    /// <summary>Whether the attribute is a camel-cased, webkit-cased or dashed attribute of a CSS property (cssFloat is a regular IDL attribute).</summary>
    private static bool IsCssPropertyAttribute(IdlMemberAttribute attribute) => attribute.SpecUrl.Contains("#dom-cssstyleproperties-", StringComparison.Ordinal) && attribute.Name != "cssFloat";

    /// <summary>The name of the CSS property is the dashed attribute, or the camel-cased attribute when there is no dash.</summary>
    private static string GetCssPropertyName(List<IdlMemberAttribute> attributes) => attributes.Find(a => a.SpecUrl.EndsWith("dashed_attribute", StringComparison.Ordinal))?.Name ?? attributes[0].Name;

    // ----------------------------------------------------------------------------------------------------------------
    // Exposure (https://webidl.spec.whatwg.org/#Exposed)
    // ----------------------------------------------------------------------------------------------------------------

    private const int ExposedWindow = 1;
    private const int ExposedDedicatedWorker = 2;
    private const int ExposedSharedWorker = 4;
    private const int ExposedServiceWorker = 8;
    private const int ExposedPaintWorklet = 16;
    private const int ExposedOther = 32;
    private const int ExposedLayoutWorklet = 64;
    private const int ExposedAnimationWorklet = 128;
    private const int ExposedAudioWorklet = 256;
    private const int ExposedWorklet = ExposedPaintWorklet | ExposedLayoutWorklet | ExposedAnimationWorklet | ExposedAudioWorklet;
    private const int ExposedAll = ExposedWindow | ExposedDedicatedWorker | ExposedSharedWorker | ExposedServiceWorker | ExposedWorklet | ExposedOther;

    /// <summary>Returns the exposure set of an [Exposed] extended attribute (the global names of the global interfaces), as the flags of <c>ExposureSet</c>.</summary>
    private static int GetExposure(IdlExtendedAttribute? exposed)
    {
        if (exposed is null)
            return ExposedWindow;

        var result = 0;
        foreach (var name in exposed.Value is { } value ? [value] : exposed.Values)
        {
            result |= name switch
            {
                "*" => ExposedAll,
                "Window" => ExposedWindow,
                "Worker" => ExposedDedicatedWorker | ExposedSharedWorker | ExposedServiceWorker,
                "DedicatedWorker" => ExposedDedicatedWorker,
                "SharedWorker" => ExposedSharedWorker,
                "ServiceWorker" => ExposedServiceWorker,
                "Worklet" => ExposedWorklet,
                "PaintWorklet" => ExposedPaintWorklet,
                "LayoutWorklet" => ExposedLayoutWorklet,
                "AnimationWorklet" => ExposedAnimationWorklet,
                "AudioWorklet" => ExposedAudioWorklet,
                _ => ExposedOther,
            };
        }

        return result;
    }

    /// <summary>The exposure set of an interface: its [Exposed] extended attribute (the interfaces without one are exposed in the windows).</summary>
    private static int GetExposure(IdlDefinition definition) => GetExposure(definition.GetExtAttr("Exposed"));

    /// <summary>
    /// Returns the condition (an <c>if</c> statement, or an empty string) under which a member is exposed: [SecureContext], and
    /// its [Exposed] set when it is narrower than the one of its interface. Returns false when the member is never exposed.
    /// </summary>
    private static bool GetMemberExposureCondition(InterfaceModel model, IdlMember member, out string condition)
    {
        var conditions = new List<string>();
        if (member.GetExtAttr("SecureContext") is not null)
        {
            conditions.Add("r.IsSecureContext");
        }

        var interfaceExposure = GetExposure(model.Definition);
        if (member.GetExtAttr("Exposed") is { } exposed)
        {
            var memberExposure = GetExposure(exposed) & interfaceExposure;
            if (memberExposure == 0)
            {
                condition = "";
                return false;
            }

            if (memberExposure != interfaceExposure)
            {
                conditions.Add($"r.IsExposed({EmitExposure(memberExposure)})");
            }
        }

        condition = conditions.Count == 0 ? "" : "if (" + string.Join(" && ", conditions) + ") ";
        return true;
    }

    private static string EmitExposure(int exposure)
    {
        const string Type = "global::Meziantou.Framework.WebPlatform.Scripting.ExposureSet";
        if (exposure == ExposedAll)
            return Type + ".All";

        var names = new List<string>();
        foreach (var (flag, name) in new[] { (ExposedWindow, "Window"), (ExposedDedicatedWorker, "DedicatedWorker"), (ExposedSharedWorker, "SharedWorker"), (ExposedServiceWorker, "ServiceWorker"), (ExposedPaintWorklet, "PaintWorklet"), (ExposedLayoutWorklet, "LayoutWorklet"), (ExposedAnimationWorklet, "AnimationWorklet"), (ExposedAudioWorklet, "AudioWorklet"), (ExposedOther, "Other") })
        {
            if ((exposure & flag) != 0)
            {
                names.Add(Type + "." + name);
            }
        }

        return string.Join(" | ", names);
    }
}
