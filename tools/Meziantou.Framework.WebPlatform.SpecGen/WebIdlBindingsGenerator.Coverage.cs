using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Meziantou.Framework.WebPlatform;
using SpecGen.Idl;

namespace SpecGen;

internal sealed partial class WebIdlBindingsGenerator
{
    /// <summary>
    /// The webref files (specifications) whose IDL must be fully implemented. Keep in sync with IdlCoverageTests, which also
    /// checks the interfaces that have no .NET type.
    /// </summary>
    private static readonly HashSet<string> InScopeSpecifications = new(StringComparer.Ordinal)
    {
        "dom", "html", "SVG2", "filter-effects-1", "css-masking-1", "geometry-1", "mathml-core", "svg-animations", "svg-paths", "wai-aria-1.3",
        "cssom-1", "css-conditional-3", "css-nesting-1", "css-cascade-5", "css-cascade-6", "css-fonts-5", "css-counter-styles-3", "css-font-loading-3", "trusted-types", "selection-api", "cssom-view-1", "xhr", "fetch", "css-highlight-api-1", "css-mixins-1", "css-properties-values-api-1", "mediaqueries-5", "css-typed-om-1", "css-parser-api", "clipboard-apis", "cookiestore", "console", "beacon", "compat", "websockets", "IndexedDB-3", "service-workers", "resize-observer-1", "intersection-observer", "layout-instability", "scroll-animations-1", "css-scroll-snap-2", "css-anchor-position-1", "css-viewport-1", "paint-timing", "largest-contentful-paint", "element-timing",
        "web-animations-1", "web-animations-2", "css-animations-1", "css-animations-2", "css-transitions-1", "css-transitions-2", "webvtt1",
    };

    /// <summary>
    /// Reports the members of the in-scope specifications that have no .NET member and no exclusion in idl-exclusions.json:
    /// the bindings would silently miss them.
    /// </summary>
    private void CheckCoverage(List<InterfaceModel> interfaces)
    {
        var exclusionsPath = Path.Combine(_context.Root, "tests", "Meziantou.Framework.WebPlatform.Wpt", "idl-exclusions.json");
        var exclusions = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(exclusionsPath))!;
        foreach (var model in interfaces)
        {
            if (exclusions.ContainsKey(model.Name))
                continue;

            var names = GetIdlMemberNames(model.ClrType!);
            foreach (var member in model.Definition.Members)
            {
                if (member.Kind is not (IdlMemberKind.Attribute or IdlMemberKind.Operation or IdlMemberKind.Const or IdlMemberKind.Constructor) || !InScopeSpecifications.Contains(member.Source))
                    continue;

                // A member that is not exposed where its interface is exposed (e.g. a [Exposed=Window] member of a mixin) has no binding
                if (member.Kind != IdlMemberKind.Constructor && !GetMemberExposureCondition(model, member, out _))
                    continue;

                var name = member.Kind == IdlMemberKind.Constructor ? "constructor" : member.Name;
                if (name.Length == 0 || names.Contains(name) || exclusions.ContainsKey(model.Name + "." + name))
                    continue;

                Error($"The IDL member {model.Name}.{name} has no .NET member annotated with [IdlMember] and is not excluded in idl-exclusions.json");
            }
        }
    }

    /// <summary>
    /// Reports the .NET types and members whose <see cref="ExperimentalAttribute"/> does not match the proposal of their IDL
    /// (<see cref="IdlDefinition.ExperimentalApi"/>, <see cref="IdlMember.ExperimentalApi"/>): a public API of a proposal must
    /// be experimental, and must not be anymore once the IDL of webref defines it. Also reports the members that map an IDL
    /// member that does not exist (e.g. a member removed from the specification).
    /// </summary>
    private void CheckExperimentalApis()
    {
        var assembly = typeof(IdlMemberAttribute).Assembly;
        var constants = assembly.GetType("Meziantou.Framework.WebPlatform.ExperimentalApis", throwOnError: true)!;
        string? GetDiagnosticId(string? name)
        {
            if (name is null)
                return null;

            if (constants.GetField(name, BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() is string id)
                return id;

            Error($"The experimental API {name} is not a constant of ExperimentalApis");
            return null;
        }

        void Check(string description, string? expected, string? actual)
        {
            if (string.Equals(expected, actual, StringComparison.Ordinal))
                return;

            if (expected is null)
            {
                Error($"{description} has [Experimental(\"{actual}\")] but its IDL is not a proposal anymore: remove the attribute (and the entry of docs/experimental-apis.md)");
            }
            else
            {
                Error($"{description} implements a proposal: it must have [Experimental({expected})] (ExperimentalApis.cs, docs/experimental-apis.md){(actual is null ? "" : $", not [Experimental(\"{actual}\")]")}");
            }
        }

        foreach (var type in assembly.GetTypes())
        {
            if (type.GetCustomAttribute<IdlInterfaceAttribute>(inherit: false) is not { } interfaceAttribute || _idl.Get(interfaceAttribute.Name) is not { } definition)
                continue;

            var typeId = type.GetCustomAttribute<ExperimentalAttribute>(inherit: false)?.DiagnosticId;
            var definitionId = GetDiagnosticId(definition.ExperimentalApi);
            Check($"The type {type.FullName} ({definition.Name})", definitionId, typeId);

            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var memberId = member.GetCustomAttribute<ExperimentalAttribute>(inherit: false)?.DiagnosticId ?? typeId;
                foreach (var attribute in member.GetCustomAttributes<IdlMemberAttribute>(inherit: false))
                {
                    // The attributes of the CSS properties are not in the IDL: CheckCssStyleProperties checks them
                    if (IsCssPropertyAttribute(attribute))
                        continue;

                    if (!DefinesIdlName(definition, attribute.Name))
                    {
                        Error($"The member {type.FullName}.{member.Name} maps {definition.Name}.{attribute.Name}, which the IDL does not define: add the IDL to IdlModel.cs (with ExperimentalApi when no specification defines it), or remove the member");
                        continue;
                    }

                    var idlMember = definition.Members.Find(m => m.Name == attribute.Name && m.ExperimentalApi is not null) ?? definition.Members.Find(m => m.Name == attribute.Name);
                    Check($"The member {type.FullName}.{member.Name} ({definition.Name}.{attribute.Name})", GetDiagnosticId(idlMember?.ExperimentalApi) ?? definitionId, memberId);
                }
            }
        }
    }

    /// <summary>
    /// Reports the properties of CSSStyleProperties whose <see cref="ExperimentalAttribute"/> does not match the CSS
    /// definitions of webref: a property that no specification defines (e.g. a property of the tentative web-platform-tests,
    /// or a former name kept for compatibility) must be experimental, and must not be anymore once webref defines it.
    /// </summary>
    private void CheckCssStyleProperties()
    {
        var definedProperties = CssPropertiesGenerator.GetPropertyNames(_context);
        var experimentalIds = typeof(IdlMemberAttribute).Assembly.GetType("Meziantou.Framework.WebPlatform.ExperimentalApis", throwOnError: true)!
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => f.GetRawConstantValue())
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        foreach (var property in typeof(Meziantou.Framework.WebPlatform.Css.Om.CssStyleProperties).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var attributes = property.GetCustomAttributes<IdlMemberAttribute>(inherit: false).Where(IsCssPropertyAttribute).ToList();
            if (attributes.Count == 0)
                continue;

            var cssName = GetCssPropertyName(attributes);
            var experimentalId = property.GetCustomAttribute<ExperimentalAttribute>(inherit: false)?.DiagnosticId;
            if (definedProperties.Contains(cssName))
            {
                if (experimentalId is not null)
                {
                    Error($"The property CssStyleProperties.{property.Name} has [Experimental(\"{experimentalId}\")] but webref now defines {cssName}: remove the attribute (and its entry of docs/experimental-apis.md)");
                }
            }
            else if (experimentalId is null)
            {
                Error($"The property CssStyleProperties.{property.Name} maps {cssName}, which no specification of webref defines: add [Experimental] (ExperimentalApis.cs, docs/experimental-apis.md), or remove the property");
            }
            else if (!experimentalIds.Contains(experimentalId))
            {
                Error($"The property CssStyleProperties.{property.Name} has [Experimental(\"{experimentalId}\")], which is not a constant of ExperimentalApis");
            }
        }
    }

    /// <summary>
    /// Whether the IDL definition defines the name of an <see cref="IdlMemberAttribute"/>. The .NET type of an interface
    /// mixin can also implement the members of the other mixins of the interfaces that include it (e.g. the .NET type of
    /// CanvasState implements all the mixins of CanvasRenderingContext2D).
    /// </summary>
    private bool DefinesIdlName(IdlDefinition definition, string name)
    {
        if (DefinesOwnIdlName(definition, name))
            return true;

        return definition.Kind == IdlDefinitionKind.InterfaceMixin
            && _idl.Definitions.Values.Any(d => d.Kind == IdlDefinitionKind.Interface && d.Members.Exists(m => m.DeclaringDefinition == definition.Name) && DefinesOwnIdlName(d, name));
    }

    /// <summary>Whether the IDL definition defines the name of an <see cref="IdlMemberAttribute"/>: a member, an enum value, or a special member that has no name of its own.</summary>
    private static bool DefinesOwnIdlName(IdlDefinition definition, string name)
    {
        if (definition.Members.Exists(m => m.Name == name) || definition.EnumValues.Contains(name, StringComparer.Ordinal))
            return true;

        // https://webidl.spec.whatwg.org/#LegacyFactoryFunction (e.g. Option)
        if (definition.ExtAttrs.Exists(a => a.Name == "LegacyFactoryFunction" && a.Value == name))
            return true;

        return name switch
        {
            "constructor" => definition.Members.Exists(m => m.Kind == IdlMemberKind.Constructor),
            "getter" or "setter" or "deleter" or "stringifier" => definition.Members.Exists(m => m.Special == name),
            "iterable" => definition.Members.Exists(m => m.Kind is IdlMemberKind.Iterable or IdlMemberKind.AsyncIterable),
            "maplike" => definition.Members.Exists(m => m.Kind == IdlMemberKind.Maplike),
            "setlike" => definition.Members.Exists(m => m.Kind == IdlMemberKind.Setlike),
            _ => false,
        };
    }

    private static HashSet<string> GetIdlMemberNames(Type type)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                foreach (var attribute in member.GetCustomAttributes<IdlMemberAttribute>(inherit: false))
                {
                    result.Add(attribute.Name);
                }
            }
        }

        return result;
    }
}
