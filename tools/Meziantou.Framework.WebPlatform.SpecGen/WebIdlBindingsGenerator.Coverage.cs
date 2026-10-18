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
    /// be experimental, and must not be anymore once the IDL of webref defines it.
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
                    var idlMember = definition.Members.Find(m => m.Name == attribute.Name && m.ExperimentalApi is not null) ?? definition.Members.Find(m => m.Name == attribute.Name);
                    Check($"The member {type.FullName}.{member.Name} ({definition.Name}.{attribute.Name})", GetDiagnosticId(idlMember?.ExperimentalApi) ?? definitionId, memberId);
                }
            }
        }
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
