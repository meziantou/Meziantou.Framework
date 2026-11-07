using System.Reflection;
using System.Text;
using Meziantou.Framework.WebPlatform;
using SpecGen.Idl;

namespace SpecGen;

internal sealed partial class WebIdlBindingsGenerator
{
    private string GenerateRegistry(List<InterfaceModel> interfaces)
    {
        var writer = new CodeWriter("the webref IDL and the .NET types annotated with [IdlInterface]");
        writer.Line("using Meziantou.Framework.WebPlatform.Scripting.Generated;");
        writer.Line();
        writer.Line("namespace Meziantou.Framework.WebPlatform.Scripting;");
        writer.Line();
        using (writer.Block($"internal static partial class WebIdlBindings<TAdapter, TValue> {Constraints}"))
        {
            // The interfaces
            writer.Line("/// <summary>The interfaces implemented by the library, by name.</summary>");
            writer.Line($"public static readonly global::System.Collections.Generic.Dictionary<string, InterfaceBinding<TAdapter, TValue>> Interfaces = CreateInterfaces();");
            writer.Line();
            using (writer.Block("private static global::System.Collections.Generic.Dictionary<string, InterfaceBinding<TAdapter, TValue>> CreateInterfaces()"))
            {
                writer.Line("var result = new global::System.Collections.Generic.Dictionary<string, InterfaceBinding<TAdapter, TValue>>(global::System.StringComparer.Ordinal)");
                writer.Line("{");
                foreach (var model in interfaces)
                {
                    writer.Line($"    [{Literal(model.Name)}] = {BindingClass(model.Name)}.Binding,");
                }

                writer.Line("};");
                writer.Line("LinkParents(result);");
                writer.Line("return result;");
            }

            writer.Line();

            // The legacy window aliases
            writer.Line("/// <summary>The legacy window aliases (https://webidl.spec.whatwg.org/#LegacyWindowAlias).</summary>");
            writer.Line("public static readonly (string Alias, string Target)[] LegacyWindowAliases =");
            writer.Line("[");
            foreach (var (alias, target) in _idl.Aliases.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (_model.Interfaces.TryGetValue(target, out var model) && model.ClrType is not null)
                {
                    writer.Line($"    ({Literal(alias)}, {Literal(target)}),");
                }
            }

            writer.Line("];");
            writer.Line();

            // The legacy callback interface objects and the legacy factory functions
            using (writer.Block($"public static void DefineLegacyGlobals({Realm} r)"))
            {
                // https://webidl.spec.whatwg.org/#legacy-callback-interface-object, e.g. NodeFilter
                foreach (var definition in _idl.Definitions.Values.OrderBy(d => d.Name, StringComparer.Ordinal))
                {
                    if (definition.Kind == IdlDefinitionKind.CallbackInterface && definition.Members.Exists(m => m.Kind == IdlMemberKind.Const) && _model.ClrTypes.ContainsKey(definition.Name))
                    {
                        var constants = definition.Members.Where(m => m.Kind == IdlMemberKind.Const).Select(c => $"({Literal(c.Name)}, {EmitConstant(c)})");
                        writer.Line($"r.DefineLegacyCallbackInterface({Literal(definition.Name)}, [{string.Join(", ", constants)}]);");
                    }
                }

                // https://webidl.spec.whatwg.org/#LegacyFactoryFunction, e.g. Option(): the .NET static method annotated with the
                // name of the function takes the document as first parameter
                foreach (var model in interfaces)
                {
                    if (model.Definition.ExtAttrs.Find(a => a.Name == "LegacyFactoryFunction") is not { Value: { } factoryName })
                        continue;

                    var method = model.ClrType!.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m => m.GetCustomAttributes<IdlMemberAttribute>().Any(a => a.Name == factoryName));
                    if (method is null)
                        continue;

                    var parameters = method.GetParameters();
                    var arguments = new List<string> { "r.Window!.Document" };
                    for (var i = 1; i < parameters.Length; i++)
                    {
                        var parameter = parameters[i];
                        var value = $"r.Argument(a, {i - 1})";
                        var parameterType = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
                        var converted = parameterType == typeof(bool) ? $"r.ToBoolean({value})"
                            : parameterType == typeof(uint) ? $"r.ToUnsignedLong({value})"
                            : $"r.ToDomString({value})";
                        arguments.Add($"r.Adapter.IsUndefined({value}) ? {EmitParameterDefault(parameter)} : {converted}");
                    }

                    writer.Line($"r.DefineLegacyFactoryFunction({Literal(factoryName)}, {Literal(model.Name)}, static (r, a) => r.Wrap({TypeName(model.ClrType)}.{method.Name}({string.Join(", ", arguments)})));");
                }
            }

            writer.Line();

            // The conversions of the .NET values whose type is not known statically (the IDL type any)
            using (writer.Block($"public static bool TryConvertToJs({Realm} r, object value, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TValue? result)"))
            {
                writer.Line("switch (value)");
                writer.Line("{");
                // The abstract dictionaries have no instance of their own type (e.g. ReportBody: a body without IDL is converted by the realm)
                var dictionaries = _model.ClrTypes.Where(p => _idl.Get(p.Key)?.Kind == IdlDefinitionKind.Dictionary && !p.Value.IsAbstract).Select(p => p.Value).Distinct().OrderByDescending(Depth).ThenBy(t => t.FullName, StringComparer.Ordinal).ToList();
                foreach (var type in dictionaries)
                {
                    writer.Line($"    case {TypeName(type)} dictionary{SafeName(type)}:");
                    writer.Line($"        result = {GetDictionaryToJs(type)}(r, dictionary{SafeName(type)});");
                    writer.Line("        return true;");
                }

                var enums = _model.ClrTypes.Values.Where(t => t.IsEnum).Distinct().OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();
                foreach (var type in enums)
                {
                    writer.Line($"    case {TypeName(type)} enum{SafeName(type)}:");
                    writer.Line($"        result = r.Adapter.FromString({GetEnumToString(type)}(enum{SafeName(type)}));");
                    writer.Line("        return true;");
                }

                foreach (var taskType in _taskResultTypes.OrderBy(t => t.FullName, StringComparer.Ordinal))
                {
                    var resultType = taskType.GetGenericArguments()[0];
                    writer.Line($"    case {TypeName(taskType)} task{SafeName(taskType)}:");
                    writer.Line($"        result = r.ToPromise(task{SafeName(taskType)}, static (r, v) => {EmitToJs(resultType, null, "v")});");
                    writer.Line("        return true;");
                }

                writer.Line("}");
                writer.Line();
                writer.Line("result = null;");
                writer.Line("return false;");
            }
        }

        return writer.ToString();
    }

    private string GenerateHelpers()
    {
        var writer = new CodeWriter("the webref IDL and the .NET types annotated with [IdlInterface]");
        writer.Line("namespace Meziantou.Framework.WebPlatform.Scripting.Generated;");
        writer.Line();
        writer.Line("/// <summary>The conversions of the dictionaries, enumerations, callback functions and unions.</summary>");
        using (writer.Block($"internal static class {HelpersClass} {Constraints}"))
        {
            var first = true;
            foreach (var (_, code) in _helpers)
            {
                if (!first)
                {
                    writer.Line();
                }

                first = false;
                foreach (var line in code.Split('\n'))
                {
                    writer.Line(line);
                }
            }
        }

        return writer.ToString();
    }
}
