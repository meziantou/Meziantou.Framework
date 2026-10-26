using System.Reflection;
using System.Text;
using System.Text.Json;
using Meziantou.Framework.WebPlatform.Dom;
using SpecGen.Idl;

namespace Meziantou.Framework.WebPlatform.Wpt;

/// <summary>
/// Checks that every IDL member of the in-scope specifications has a .NET member annotated with <see cref="IdlMemberAttribute"/>,
/// or is listed in idl-exclusions.json with a reason. Stale exclusions (members that are now implemented) also fail the test.
/// </summary>
public sealed class IdlCoverageTests
{
    /// <summary>The webref files (specifications) whose IDL must be fully implemented.</summary>
    private static readonly string[] Specifications = ["dom", "html", "SVG2", "filter-effects-1", "css-masking-1", "geometry-1", "mathml-core", "svg-animations", "svg-paths", "wai-aria-1.3",
        "cssom-1", "css-conditional-3", "css-nesting-1", "css-cascade-5", "css-cascade-6", "css-fonts-5", "css-counter-styles-3", "css-font-loading-3", "trusted-types", "selection-api", "cssom-view-1", "urlpattern", "xhr", "fetch", "css-highlight-api-1", "css-mixins-1", "css-shadow-1", "css-properties-values-api-1", "mediaqueries-5", "css-typed-om-1", "css-parser-api", "clipboard-apis", "cookiestore", "console", "beacon", "compat", "websockets", "IndexedDB-3", "service-workers", "resize-observer-1", "intersection-observer", "layout-instability", "scroll-animations-1", "css-scroll-snap-2", "css-anchor-position-1", "css-viewport-1", "paint-timing", "largest-contentful-paint", "element-timing",
        "web-animations-1", "web-animations-2", "css-animations-1", "css-animations-2", "css-transitions-1", "css-transitions-2", "css-view-transitions-2", "webvtt1", "webcrypto-2", "webcrypto-secure-curves", "webcrypto-modern-algos", "css-paint-api-1", "css-layout-api-1", "css-animation-worklet-1", "observable"];

    [Fact]
    public void AllIdlMembersAreImplementedOrExcluded()
    {
        // The full IDL of webref
        var model = new BindingModel(IdlModel.Load(WptPaths.SpecData("webref", "ed", "idlparsed")));
        var exclusionsPath = Path.Combine(WptPaths.Root, "tests", "Meziantou.Framework.WebPlatform.Wpt", "idl-exclusions.json");
        var exclusions = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(exclusionsPath))!;
        var clrTypes = typeof(Node).Assembly.GetTypes()
            .Select(t => (Type: t, Attribute: t.GetCustomAttribute<IdlInterfaceAttribute>(inherit: false)))
            .Where(t => t.Attribute is not null)
            .GroupBy(t => t.Attribute!.Name)
            .ToDictionary(g => g.Key, g => g.Select(t => t.Type).ToList());

        var missing = new List<string>();
        var implemented = 0;
        foreach (var definition in model.Idl.Definitions.Values.OrderBy(d => d.Name, StringComparer.Ordinal))
        {
            if (definition.Kind is not (IdlDefinitionKind.Interface or IdlDefinitionKind.CallbackInterface or IdlDefinitionKind.Dictionary or IdlDefinitionKind.Namespace))
                continue;

            if (!IsExposedOnWindow(definition))
                continue;

            var members = definition.Members.Where(m => Specifications.Contains(m.Source) && m.Kind is IdlMemberKind.Attribute or IdlMemberKind.Operation or IdlMemberKind.Const or IdlMemberKind.Constructor or IdlMemberKind.Field).ToList();
            if (members.Count == 0 && !Specifications.Contains(definition.Source))
                continue;

            if (exclusions.ContainsKey(definition.Name))
                continue;

            if (!clrTypes.TryGetValue(definition.Name, out var types))
            {
                missing.Add(definition.Name);
                continue;
            }

            var names = GetIdlMemberNames(types);
            foreach (var member in members)
            {
                var name = member.Kind == IdlMemberKind.Constructor ? "constructor" : member.Name;
                if (name.Length == 0)
                    continue; // anonymous getters and stringifiers

                var key = definition.Name + "." + name;
                if (names.Contains(name))
                {
                    implemented++;
                    if (exclusions.ContainsKey(key))
                    {
                        missing.Add(key + " (implemented, remove the exclusion)");
                    }
                }
                else if (!exclusions.ContainsKey(key))
                {
                    missing.Add(key);
                }
            }
        }

        if (missing.Count > 0)
        {
            var message = new StringBuilder();
            message.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"{implemented} IDL members are implemented. The following members are missing and not excluded in idl-exclusions.json:");
            foreach (var item in missing.Distinct())
            {
                message.AppendLine(item);
            }

            Assert.Fail(message.ToString());
        }
    }

    private static bool IsExposedOnWindow(IdlDefinition definition)
    {
        if (definition.Kind is IdlDefinitionKind.Dictionary or IdlDefinitionKind.CallbackInterface)
            return true;

        var exposed = definition.ExtAttrs.Find(a => a.Name == "Exposed");
        return exposed?.Value is null or "Window" or "*";
    }

    private static HashSet<string> GetIdlMemberNames(List<Type> types)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in types)
        {
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
        }

        return result;
    }
}
