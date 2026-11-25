using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using SpecGen.Idl;

namespace Meziantou.Framework.WebPlatform.Wpt;

/// <summary>
/// Checks that every IDL member of the in-scope specifications has a .NET member annotated with <see cref="IdlMemberAttribute"/>,
/// or is listed in idl-exclusions.json with a reason. Stale exclusions (members that are now implemented) also fail the test.
/// The attributes are conditional: they are read from the build of the library that emits them (idl-metadata/, see the
/// project file), loaded in its own context, so their types are compared by name.
/// </summary>
public sealed class IdlCoverageTests
{
    /// <summary>The webref files (specifications) whose IDL must be fully implemented.</summary>
    private static readonly string[] Specifications = ["dom", "html", "SVG2", "filter-effects-1", "css-masking-1", "geometry-1", "mathml-core", "svg-animations", "svg-paths", "wai-aria-1.3",
        "cssom-1", "css-conditional-3", "css-nesting-1", "css-cascade-5", "css-cascade-6", "css-fonts-5", "css-counter-styles-3", "css-font-loading-3", "trusted-types", "selection-api", "cssom-view-1", "urlpattern", "xhr", "fetch", "css-highlight-api-1", "css-mixins-1", "css-shadow-1", "css-properties-values-api-1", "mediaqueries-5", "css-typed-om-1", "css-parser-api", "clipboard-apis", "cookiestore", "console", "beacon", "compat", "websockets", "IndexedDB-3", "service-workers", "resize-observer-1", "intersection-observer", "layout-instability", "scroll-animations-1", "css-scroll-snap-2", "css-anchor-position-1", "css-viewport-1", "paint-timing", "largest-contentful-paint", "element-timing", "container-timing",
        "web-animations-1", "web-animations-2", "css-animations-1", "css-animations-2", "css-transitions-1", "css-transitions-2", "css-view-transitions-2", "webvtt1", "webcrypto-2", "webcrypto-secure-curves", "webcrypto-modern-algos", "css-paint-api-1", "css-layout-api-1", "css-animation-worklet-1", "observable", "webmcp", "entries-api"];

    [Fact]
    public void AllIdlMembersAreImplementedOrExcluded()
    {
        // The full IDL of webref
        var idl = IdlModel.Load(WptPaths.SpecData("webref", "ed", "idlparsed"));
        var exclusionsPath = Path.Combine(WptPaths.Root, "tests", "Meziantou.Framework.WebPlatform.Wpt", "idl-exclusions.json");
        var exclusions = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(exclusionsPath))!;
        var loadContext = new AssemblyLoadContext(nameof(IdlCoverageTests), isCollectible: true);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, "idl-metadata", "Meziantou.Framework.WebPlatform.dll"));
            CheckCoverage(idl, exclusions, assembly);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static void CheckCoverage(IdlModel idl, Dictionary<string, string> exclusions, Assembly assembly)
    {
        var clrTypes = assembly.GetTypes()
            .Select(t => (Type: t, Name: GetIdlNames(t, typeof(IdlInterfaceAttribute)).FirstOrDefault()))
            .Where(t => t.Name is not null)
            .GroupBy(t => t.Name!)
            .ToDictionary(g => g.Key, g => g.Select(t => t.Type).ToList());
        Assert.NotEmpty(clrTypes);

        var missing = new List<string>();
        var implemented = 0;
        foreach (var definition in idl.Definitions.Values.OrderBy(d => d.Name, StringComparer.Ordinal))
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
                    result.UnionWith(GetIdlNames(member, typeof(IdlMemberAttribute)));
                }
            }
        }

        return result;
    }

    /// <summary>The first argument (the IDL name) of the attributes of <paramref name="attributeType"/>, compared by name: the member comes from another load context.</summary>
    private static IEnumerable<string> GetIdlNames(MemberInfo member, Type attributeType)
        => member.GetCustomAttributesData()
            .Where(attribute => attribute.AttributeType.FullName == attributeType.FullName)
            .Select(attribute => (string)attribute.ConstructorArguments[0].Value!);
}
