using System.Text.Json;
using System.Text.RegularExpressions;

namespace SpecGen;

/// <summary>
/// Generates the CSS property registry (Css/Values/CssProperties.g.cs) from the CSS definitions of webref
/// (tests/Meziantou.Framework.WebPlatform.SpecData/webref/ed/css): the properties with their value grammar, initial value, inheritance and longhands, and the
/// value types referenced by the grammars.
/// </summary>
internal static partial class CssPropertiesGenerator
{
    private sealed record PropertyEntry(string File, JsonElement Element);

    public static void Generate(GeneratorContext context)
    {
        var properties = new SortedDictionary<string, List<PropertyEntry>>(StringComparer.Ordinal);
        var types = new SortedDictionary<string, List<PropertyEntry>>(StringComparer.Ordinal);
        var descriptors = new SortedDictionary<string, List<PropertyEntry>>(StringComparer.Ordinal);

        // The types defined by the prose of a property or another type (e.g. <grid-line> in grid-row-start, blur() in filter)
        var nestedTypes = new SortedDictionary<string, List<PropertyEntry>>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(context.SpecData("webref", "ed", "css"), "*.json").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            if (document.RootElement.TryGetProperty("properties", out var propertyArray))
            {
                foreach (var property in propertyArray.EnumerateArray())
                {
                    Add(properties, property.GetProperty("name").GetString()!, new PropertyEntry(name, property.Clone()));
                    AddNestedTypes(nestedTypes, name, property);
                }
            }

            if (document.RootElement.TryGetProperty("atrules", out var atruleArray))
            {
                foreach (var atrule in atruleArray.EnumerateArray())
                {
                    if (!atrule.TryGetProperty("descriptors", out var descriptorArray))
                        continue;

                    foreach (var descriptor in descriptorArray.EnumerateArray())
                    {
                        if (descriptor.TryGetProperty("value", out _) && GetString(descriptor, "name") is { } descriptorName && !descriptorName.StartsWith('@', StringComparison.Ordinal))
                        {
                            Add(descriptors, atrule.GetProperty("name").GetString() + " " + descriptorName, new PropertyEntry(name, descriptor.Clone()));
                        }
                    }
                }
            }

            if (document.RootElement.TryGetProperty("values", out var valueArray))
            {
                foreach (var value in valueArray.EnumerateArray())
                {
                    if (value.TryGetProperty("value", out _))
                    {
                        Add(types, value.GetProperty("name").GetString()!, new PropertyEntry(name, value.Clone()));
                    }

                    AddNestedTypes(nestedTypes, name, value);
                }
            }
        }

        // The nested definitions compete with the other definitions of the type (e.g. <content-list> of css-content and css-gcpm)
        foreach (var (typeName, entries) in nestedTypes)
        {
            if (types.TryGetValue(typeName, out var existing))
            {
                existing.AddRange(entries);
            }
            else
            {
                types[typeName] = entries;
            }
        }

        var writer = new CodeWriter("tests/Meziantou.Framework.WebPlatform.SpecData/webref/ed/css");
        writer.Line("namespace Meziantou.Framework.WebPlatform.Css.Values;");
        writer.Line();
        using (writer.Block("internal static partial class CssPropertyRegistry"))
        {
            writer.Line("/// <summary>The properties: name, value grammar, initial value, inherited, longhands, legacy alias target, logical property group, animation type.</summary>");
            writer.Line("private static readonly CssPropertyDefinition[] PropertyDefinitions =");
            writer.Line("[");
            foreach (var (name, entries) in properties)
            {
                var definitions = entries.Where(e => e.Element.TryGetProperty("value", out _) || e.Element.TryGetProperty("legacyAliasOf", out _)).ToList();
                if (definitions.Count == 0)
                    continue;

                var main = SelectHighestLevel(name, definitions, PropertySources);

                var element = main.Element;
                var grammar = GrammarOverrides.TryGetValue(name, out var grammarOverride) ? grammarOverride : GetString(element, "value") ?? "";
                foreach (var entry in entries)
                {
                    if (GetString(entry.Element, "newValues") is { } newValues)
                    {
                        grammar = grammar.Length == 0 ? newValues : grammar + " | " + newValues;
                    }
                }

                var initial = GetString(element, "initial");
                var inherited = GetString(element, "inherited") == "yes";
                var longhands = element.TryGetProperty("longhands", out var longhandArray) ? longhandArray.EnumerateArray().Select(l => l.GetString()!).ToList()
                    : MissingLonghands.TryGetValue(name, out var missing) ? [.. missing] : [];
                var alias = GetString(element, "legacyAliasOf");
                var logicalGroup = entries.Select(e => GetString(e.Element, "logicalPropertyGroup")).FirstOrDefault(g => g is not null);
                var animationType = GetString(element, "animationType") ?? entries.Select(e => GetString(e.Element, "animationType")).FirstOrDefault(t => t is not null);
                var animationKind = MapAnimationType(animationType);
                var comment = animationKind == "Special" ? " // " + Normalize(animationType!) : "";
                writer.Line($"    new({CodeWriter.Literal(name)}, {CodeWriter.Literal(Normalize(grammar))}, {(initial is null ? "null" : CodeWriter.Literal(initial))}, {(inherited ? "true" : "false")}, [{string.Join(", ", longhands.Select(CodeWriter.Literal))}], {(alias is null ? "null" : CodeWriter.Literal(alias))}, {(logicalGroup is null ? "null" : CodeWriter.Literal(logicalGroup))}, CssAnimationTypeKind.{animationKind}),{comment}");
            }

            writer.Line("];");
            writer.Line();
            writer.Line("/// <summary>The value types (e.g. &lt;bg-layer&gt;) and functional notations (e.g. rgb()) and their grammar.</summary>");
            using (writer.Block("private static readonly Dictionary<string, string> Types = new(StringComparer.Ordinal)"))
            {
                foreach (var (name, entries) in types)
                {
                    var main = SelectHighestLevel(name, entries, TypeSources);
                    writer.Line($"[{CodeWriter.Literal(name)}] = {CodeWriter.Literal(Normalize(GetString(main.Element, "value")!))},");
                }
            }

            writer.Line(";");
            writer.Line();
            writer.Line("/// <summary>The descriptors of the at-rules (including the media features of @media): at-rule, name, value grammar, initial value.</summary>");
            writer.Line("private static readonly CssDescriptorDefinition[] Descriptors =");
            writer.Line("[");
            foreach (var (key, entries) in descriptors)
            {
                var main = SelectPreferred(entries)!;
                var separator = key.IndexOf(' ', StringComparison.Ordinal);
                var initial = GetString(main.Element, "initial");
                writer.Line($"    new({CodeWriter.Literal(key[..separator])}, {CodeWriter.Literal(key[(separator + 1)..])}, {CodeWriter.Literal(Normalize(GetString(main.Element, "value")!))}, {(initial is null ? "null" : CodeWriter.Literal(initial))}),");
            }

            writer.Line("];");
        }

        var output = writer.ToString().Replace("\n    }\n    ;", "\n    };", StringComparison.Ordinal);
        context.WriteFile(context.Source("Css", "Values", "CssProperties.g.cs"), output);
        GenerateStyleProperties(context, properties);
    }

    /// <summary>
    /// Generates the IDL attributes of CSSStyleProperties (https://drafts.csswg.org/cssom/#cssstyleproperties): the
    /// camel-cased and webkit-cased attributes are .NET properties; the dashed attributes are mapped to the same property.
    /// </summary>
    private static void GenerateStyleProperties(GeneratorContext context, SortedDictionary<string, List<PropertyEntry>> properties)
    {
        var members = new SortedDictionary<string, (string Property, List<(string IdlName, string Anchor)> IdlNames)>(StringComparer.Ordinal);
        foreach (var (name, entries) in properties)
        {
            if (!entries.Exists(e => e.Element.TryGetProperty("value", out _) || e.Element.TryGetProperty("legacyAliasOf", out _)))
                continue;

            var idlNames = new List<(string IdlName, string Anchor)> { (ToCamelCase(name, lowercaseFirst: false), "camel_cased_attribute") };
            if (name.StartsWith("-webkit-", StringComparison.Ordinal))
            {
                idlNames.Add((ToCamelCase(name[1..], lowercaseFirst: true), "webkit_cased_attribute"));
            }

            if (name.Contains('-', StringComparison.Ordinal))
            {
                idlNames.Add((name, "dashed_attribute"));
            }

            var clrName = ToCamelCase(name.TrimStart('-'), lowercaseFirst: false);
            clrName = char.ToUpperInvariant(clrName[0]) + clrName[1..];
            members[clrName] = (name, idlNames);
        }

        var writer = new CodeWriter("tests/Meziantou.Framework.WebPlatform.SpecData/webref/ed/css");

        writer.Line("namespace Meziantou.Framework.WebPlatform.Css.Om;");
        writer.Line();
        using (writer.Block("public sealed partial class CssStyleProperties"))
        {
            var first = true;
            foreach (var (clrName, (property, idlNames)) in members)
            {
                if (!first)
                {
                    writer.Line();
                }

                first = false;
                writer.Summary($"Gets or sets the value of the <c>{CodeWriter.XmlText(property)}</c> property (an empty string removes the declaration).");
                foreach (var (idlName, anchor) in idlNames)
                {
                    writer.Line($"[IdlMember({CodeWriter.Literal(idlName)}, \"https://drafts.csswg.org/cssom/#dom-cssstyleproperties-{anchor}\")]");
                }

                using (writer.Block($"public string {clrName}"))
                {
                    writer.Line($"get => GetPropertyValue({CodeWriter.Literal(property)});");
                    writer.Line($"set => SetProperty({CodeWriter.Literal(property)}, value ?? \"\");");
                }
            }
        }

        context.WriteFile(context.Source("Css", "Om", "CssStyleProperties.g.cs"), writer.ToString());
    }

    /// <summary>https://drafts.csswg.org/cssom/#css-property-to-idl-attribute</summary>
    private static string ToCamelCase(string property, bool lowercaseFirst)
    {
        var builder = new System.Text.StringBuilder(property.Length);
        var uppercaseNext = false;
        foreach (var c in property)
        {
            if (c == '-')
            {
                uppercaseNext = true;
                continue;
            }

            builder.Append(uppercaseNext ? char.ToUpperInvariant(c) : c);
            uppercaseNext = false;
        }

        if (lowercaseFirst && builder.Length > 0)
        {
            builder[0] = char.ToLowerInvariant(builder[0]);
        }

        return builder.ToString();
    }

    /// <summary>Adds the types defined in the "values" of a definition, recursively (their value is a grammar, not a keyword).</summary>
    private static void AddNestedTypes(SortedDictionary<string, List<PropertyEntry>> map, string file, JsonElement definition)
    {
        if (!definition.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array)
            return;

        foreach (var value in values.EnumerateArray())
        {
            if (GetString(value, "name") is { } name && GetString(value, "value") is { } grammar && TypeNameRegex().IsMatch(name) && Normalize(grammar) != name && Normalize(grammar) != "<" + name + ">" && !IgnoredNestedTypes.Contains(name))
            {
                Add(map, name, new PropertyEntry(file, value.Clone()));
            }

            AddNestedTypes(map, file, value);
        }
    }

    /// <summary>
    /// The nested types that are not used: their value is prose rather than a grammar (minmax(min, max)), there are two
    /// functions with the same name (the rect() of clip and the rect() basic shape), or the grammar does not allow the
    /// &lt;shape-radius&gt; of the implementations (circle(), ellipse()).
    /// </summary>
    private static readonly HashSet<string> IgnoredNestedTypes = new(StringComparer.Ordinal) { "minmax()", "rect()", "circle()", "ellipse()" };

    private static void Add(SortedDictionary<string, List<PropertyEntry>> map, string name, PropertyEntry entry)
    {
        if (!map.TryGetValue(name, out var list))
        {
            list = [];
            map[name] = list;
        }

        list.Add(entry);
    }

    // Prefers the current level of a specification (e.g. css-backgrounds over css-backgrounds-4), then the CSS specifications
    private static PropertyEntry? SelectPreferred(List<PropertyEntry> entries) => entries
        .OrderBy(e => LevelRegex().IsMatch(e.File) ? 1 : 0)
        .ThenBy(e => e.File.StartsWith("css-", StringComparison.Ordinal) ? 0 : 1)
        .ThenBy(e => e.File, StringComparer.Ordinal)
        .FirstOrDefault();

    // The next levels of a specification redefine the properties and the value types they extend (e.g. letter-spacing of
    // css-text-4 accepts percentages, <linear-gradient-syntax> of css-images-4 adds <color-interpolation-method>, <color> of
    // css-color-5 adds color-mix()), so they prefer the highest level of the preferred specification
    private static PropertyEntry SelectHighestLevel(string name, List<PropertyEntry> entries, Dictionary<string, string> sources)
    {
        if (sources.TryGetValue(name, out var source) && entries.Find(e => e.File == source) is { } preferred)
            return preferred;

        var series = GetSeries(SelectPreferred(entries)!.File);
        return entries
            .Where(e => GetSeries(e.File) == series)
            .OrderByDescending(e => LevelRegex().Match(e.File) is { Success: true } match ? int.Parse(match.Value[1..], System.Globalization.CultureInfo.InvariantCulture) : 0)
            .First();
    }

    /// <summary>The name of the specification without its level (css-images-4 → css-images).</summary>
    private static string GetSeries(string file) => LevelRegex().Replace(file, "");

    /// <summary>The properties whose highest level is not implemented by browsers.</summary>
    private static readonly Dictionary<string, string> PropertySources = new(StringComparer.Ordinal)
    {
        // css-animations-2 makes animation-delay a shorthand of animation-delay-start and animation-delay-end
        ["animation-delay"] = "css-animations",

        // css-text-4 makes white-space a shorthand of white-space-collapse, text-wrap-mode and white-space-trim
        ["white-space"] = "css-text",
    };

    /// <summary>The grammars of the properties extended by the prose of another specification (webref has no newValues for them).</summary>
    private static readonly Dictionary<string, string> GrammarOverrides = new(StringComparer.Ordinal)
    {
        // https://drafts.csswg.org/css-anchor-position-2/#container-type-anchored: "This specification extends the container-type
        // property [...] with a new anchored keyword"
        ["container-type"] = "normal | [ [ size | inline-size ] || scroll-state || anchored ]",
    };

    /// <summary>The shorthands whose property definition is not marked as a shorthand (webref has no longhands for them).</summary>
    private static readonly Dictionary<string, string[]> MissingLonghands = new(StringComparer.Ordinal)
    {
        // https://drafts.csswg.org/css-inline-3/#text-box-shorthand: "This property is a shorthand for setting the text-box-trim and text-box-edge properties"
        ["text-box"] = ["text-box-trim", "text-box-edge"],
    };

    /// <summary>The types whose highest level is an early draft that browsers do not implement.</summary>
    private static readonly Dictionary<string, string> TypeSources = new(StringComparer.Ordinal)
    {
        // css-color-6 has placeholder keywords (tbd-fg, tbd-bg)
        ["contrast-color()"] = "css-color-5",

        // css-images-5 adds image sources and directions to image()
        ["image()"] = "css-images-4",

    };

    /// <summary>
    /// Maps the animation type of a property definition (https://drafts.csswg.org/web-animations-1/#animation-type) to a
    /// CssAnimationTypeKind member. The types described by prose are "Special": their interpolation is hand-written.
    /// </summary>
    private static string MapAnimationType(string? animationType)
    {
        if (animationType is null)
            return "Unspecified";

        var value = Normalize(animationType).ToLowerInvariant().TrimEnd('.');
        return value switch
        {
            "discrete" => "Discrete",
            "not animatable" or "none" or "n/a" => "NotAnimatable",
            "by computed value type" or "by computed value" or "number" or "as length" or "as integer" => "ByComputedValue",
            "by computed value, recursing into fit-content()" or "by computed value type, recursing into fit-content()" => "ByComputedValue",
            "see individual properties" => "Shorthand",
            "as shadow list" => "ShadowList",
            _ when value.StartsWith("repeatable list", StringComparison.Ordinal) => "RepeatableList",
            _ => "Special",
        };
    }

    private static string? GetString(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Normalize(string value) => WhitespaceRegex().Replace(value, " ").Trim();

    [GeneratedRegex(@"-\d+$")]
    private static partial Regex LevelRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    // A type (<grid-line>) or a functional notation (blur()), without range or multiplier
    [GeneratedRegex(@"^(<[a-z0-9-]+(\(\))?>|[a-z0-9-]+\(\))$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture)]
    private static partial Regex TypeNameRegex();
}
