using System.Globalization;
using System.Text;
using System.Text.Json;
using SpecGen.Idl;

namespace SpecGen;

/// <summary>
/// Generates the element interfaces (HTML, SVG, MathML) from the IDL (tests/Meziantou.Framework.WebPlatform.SpecData/webref/ed/idlparsed), the element
/// definitions (tests/Meziantou.Framework.WebPlatform.SpecData/webref/ed/elements) and the HTML specification source (tests/Meziantou.Framework.WebPlatform.SpecData/html-source):
/// the class hierarchy, the element factory, the reflected IDL attributes, and the event handler IDL attributes.
/// Other members are hand-written.
/// </summary>
internal static class ElementInterfacesGenerator
{
    private sealed record Family(string Name, string BaseInterface, string[] IdlSources, string[] ElementFiles, string OutputDirectory, string GlobalOwner);

    private static readonly Family[] Families =
    [
        new("Html", "HTMLElement", ["html"], ["html.json"], "Html", "html-global"),
        new("Svg", "SVGElement", ["SVG2", "filter-effects-1", "css-masking-1", "svg-animations", "svg-paths"], ["SVG2.json", "svg-animations.json", "filter-effects-1.json", "css-masking-1.json", "svg-paths.json"], "Svg", "svg-global"),
        new("MathML", "MathMLElement", ["mathml-core"], ["mathml-core.json"], "MathML", "mathml-global"),
    ];

    // The element interfaces defined in prose that the element definitions of webref do not extract
    private static readonly (string Family, string Name, string Interface)[] ElementInterfaceOverrides =
    [
        // https://w3c.github.io/mathml-core/#the-a-element: "The <a> element must implement [...] MathMLAnchorElement"
        ("MathML", "a", "MathMLAnchorElement"),

        // https://html.spec.whatwg.org/multipage/dom.html#element-interface: the obsolete menuitem element is not listed, so
        // its interface is HTMLUnknownElement (webref lists HTMLElement)
        ("Html", "menuitem", "HTMLUnknownElement"),
    ];

    // Non-element interfaces that get the generated reflected attributes and event handlers of their mixins
    private static readonly string[] OtherInterfaces = ["Element", "Document", "Window"];

    // The specifications whose members are generated (the other specifications extending these interfaces are out of scope)
    private static readonly HashSet<string> MemberSources = new(StringComparer.Ordinal)
    {
        "html", "dom", "SVG2", "filter-effects-1", "css-masking-1", "svg-animations", "svg-paths", "mathml-core", "wai-aria-1.3",

        // Event handlers of GlobalEventHandlers
        // (touch-events is not included: "expose legacy touch event APIs" is false, as on desktop browsers)
        "css-animations-1", "css-transitions-1", "pointerevents4", "pointerlock-2", "uievents", "selection-api", "fullscreen",
        "css-contain-2", "css-scroll-snap-2",
    };

    /// <summary>The reflected IDL attributes whose setter has additional steps (Trusted Types): they are hand-written.</summary>
    private static readonly HashSet<string> HandWrittenReflectedAttributes = new(StringComparer.Ordinal) { "HTMLScriptElement.src" };

    // The supported tokens of the DOMTokenList attributes that define them (the members of Html.SupportedTokens): supports()
    // throws a TypeError for the other ones
    private static readonly Dictionary<string, string> SupportedTokens = new(StringComparer.Ordinal)
    {
        ["HTMLLinkElement.relList"] = "SupportedTokens.LinkRel",
        ["HTMLAnchorElement.relList"] = "SupportedTokens.HyperlinkRel",
        ["HTMLAreaElement.relList"] = "SupportedTokens.HyperlinkRel",
        ["HTMLFormElement.relList"] = "SupportedTokens.HyperlinkRel",
        ["SVGAElement.relList"] = "SupportedTokens.HyperlinkRel",
        ["HTMLIFrameElement.sandbox"] = "SupportedTokens.Sandbox",
        ["HTMLLinkElement.blocking"] = "SupportedTokens.Blocking",
        ["HTMLScriptElement.blocking"] = "SupportedTokens.Blocking",
        ["HTMLStyleElement.blocking"] = "SupportedTokens.Blocking",
    };

    private static readonly HashSet<string> EventHandlerTypes = new(StringComparer.Ordinal) { "EventHandler", "OnErrorEventHandler", "OnBeforeUnloadEventHandler" };

    // Words used to convert event handler names to PascalCase (onmousedown -> OnMouseDown)
    private static readonly string[] EventWords =
    [
        "abort", "activate", "after", "animation", "auto", "aux", "before", "blur", "cancel", "can", "capture", "change", "click", "close", "command",
        "content", "context", "copy", "cue", "cut", "data", "dbl", "down", "drag", "drop", "duration", "emptied", "end", "ended", "enter", "error",
        "focus", "form", "fullscreen", "got", "hash", "input", "invalid", "iteration", "key", "language", "leave", "load", "loaded", "lock",
        "lost", "match", "menu", "message", "metadata", "mouse", "move", "offline", "online", "out", "over", "page", "paste", "pause", "play",
        "playing", "pointer", "policy", "pop", "press", "print", "progress", "rate", "ready", "rejection", "handled", "reset", "resize", "restored",
        "reveal", "run", "scroll", "security", "seeked", "seeking", "select", "selection", "show", "hide", "slot", "stalled", "start", "state",
        "storage", "submit", "suspend", "swap", "through", "time", "toggle", "touch", "transition", "unhandled", "unload", "up", "update",
        "violation", "visibility", "volume", "waiting", "webkit", "wheel", "lost", "contextlost", "restored", "raw", "update", "match", "snap", "changing",
    ];

    public static void Generate(GeneratorContext context)
    {
        var idl = IdlModel.Load(context.SpecData("webref", "ed", "idlparsed"));
        var html = new HtmlSpecSource(File.ReadAllText(context.SpecData("html-source", "source")), idl.Get("ReferrerPolicy")!.EnumValues);
        var enumerations = new EnumeratedAttributeTable(html);

        foreach (var family in Families)
        {
            GenerateFamily(context, idl, html, enumerations, family);
        }

        foreach (var name in OtherInterfaces)
        {
            var definition = idl.Get(name)!;
            var writer = new CodeWriter("tests/Meziantou.Framework.WebPlatform.SpecData/webref/ed/idlparsed");
            writer.Line("using Meziantou.Framework.WebPlatform.Html;");
            writer.Line();
            writer.Line("namespace Meziantou.Framework.WebPlatform.Dom;");
            writer.Line();
            using (writer.Block($"public partial class {ClrNames.ToClrName(name)}"))
            {
                GenerateMembers(writer, idl, html, enumerations, definition, [], "html-global", isElement: name == "Element");
            }

            context.WriteFile(context.Source("Dom", ClrNames.ToClrName(name) + ".g.cs"), writer.ToString());
        }

        GenerateElementInternalsAria(context, idl);
        GenerateEnumeratedAttributes(context, enumerations);
        GenerateEventHandlerNames(context, idl);
    }

    /// <summary>
    /// The ARIAMixin members of ElementInternals, which reflect the internal content attribute map of the target element
    /// (https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#reflected-target).
    /// </summary>
    private static void GenerateElementInternalsAria(GeneratorContext context, IdlModel idl)
    {
        var writer = new CodeWriter("tests/Meziantou.Framework.WebPlatform.SpecData/webref/ed/idlparsed");
        writer.Line("namespace Meziantou.Framework.WebPlatform.Dom;");
        writer.Line();
        using (writer.Block("public sealed partial class ElementInternals"))
        {
            var first = true;
            foreach (var member in idl.Get("ARIAMixin")!.Members)
            {
                if (member.Kind != IdlMemberKind.Attribute || member.Type is null || !MemberSources.Contains(member.Source))
                    continue;

                var reflect = member.GetExtAttr("Reflect");
                if (reflect is null)
                    continue;

                var name = CodeWriter.Literal(Unquote(reflect.Value) ?? member.Name.ToLowerInvariant());
                var type = idl.ResolveTypedefs(member.Type);
                var (clrType, getter, setter) = (type.Name, type.Generic) switch
                {
                    ("DOMString", _) => ("string?", $"GetAriaString({name})", $"SetAriaString({name}, value)"),
                    ("Element", _) => ("Element?", $"GetAriaElement({name})", $"SetAriaElement({name}, value)"),
                    (_, "FrozenArray") => ("IReadOnlyList<Element>?", $"GetAriaElements({name})", $"SetAriaElements({name}, value)"),
                    _ => throw new InvalidOperationException($"Unsupported ARIAMixin type {type} for {member.Name}"),
                };

                if (!first)
                {
                    writer.Line();
                }

                first = false;
                writer.Summary($"Gets or sets the default value of the <c>{CodeWriter.XmlText(member.Name)}</c> ARIA property of the custom element (used when the element has no corresponding ARIA attribute).");
                writer.SeeAlso(member.Href ?? "https://w3c.github.io/aria/#ARIAMixin");
                writer.Line($"[IdlMember({CodeWriter.Literal(member.Name)}, {CodeWriter.Literal(member.Href ?? "https://w3c.github.io/aria/#ARIAMixin")})]");
                using (writer.Block($"public {clrType} {ToPascalCase(member.Name)}"))
                {
                    writer.Line($"get => {getter};");
                    writer.Line($"set => {setter};");
                }
            }
        }

        context.WriteFile(context.Source("Html", "CustomElements", "ElementInternals.g.cs"), writer.ToString());
    }

    // https://html.spec.whatwg.org/multipage/webappapis.html#window-reflecting-body-element-event-handler-set
    private static readonly string[] WindowReflectingBodyElementEventHandlerSet = ["blur", "error", "focus", "load", "resize", "scroll"];

    private static void GenerateEventHandlerNames(GeneratorContext context, IdlModel idl)
    {
        IEnumerable<string> GetEventTypes(string mixin) => (idl.Get(mixin)?.Members ?? [])
            .Where(m => m.Kind == IdlMemberKind.Attribute && MemberSources.Contains(m.Source) && m.Type?.Name is { } typeName && EventHandlerTypes.Contains(typeName) && m.Name.StartsWith("on", StringComparison.Ordinal))
            .Select(m => m.Name[2..]);

        var elementTypes = GetEventTypes("GlobalEventHandlers").Concat(GetEventTypes("DocumentAndElementEventHandlers")).Distinct().Order(StringComparer.Ordinal).ToList();
        var windowTypes = GetEventTypes("WindowEventHandlers").Concat(WindowReflectingBodyElementEventHandlerSet).Distinct().Order(StringComparer.Ordinal).ToList();

        var writer = new CodeWriter("tests/Meziantou.Framework.WebPlatform.SpecData/webref/ed/idlparsed");
        writer.Line("using System.Collections.Frozen;");
        writer.Line();
        writer.Line("namespace Meziantou.Framework.WebPlatform.Html;");
        writer.Line();
        writer.Line("/// <summary>The event types of the event handlers (https://html.spec.whatwg.org/multipage/webappapis.html#event-handlers-on-elements,-document-objects,-and-window-objects).</summary>");
        using (writer.Block("internal static class EventHandlerNames"))
        {
            writer.Line("/// <summary>The event handlers of GlobalEventHandlers and DocumentAndElementEventHandlers, supported by HTML, SVG and MathML elements.</summary>");
            writer.Line("public static readonly FrozenSet<string> Elements = FrozenSet.Create(StringComparer.Ordinal,");
            for (var i = 0; i < elementTypes.Count; i++)
            {
                writer.Line("    " + CodeWriter.Literal(elementTypes[i]) + (i == elementTypes.Count - 1 ? ");" : ","));
            }
            writer.Line();
            writer.Line("/// <summary>The event handlers of body and frameset elements that are forwarded to the Window: WindowEventHandlers and the Window-reflecting body element event handler set.</summary>");
            writer.Line("public static readonly FrozenSet<string> WindowReflecting = FrozenSet.Create(StringComparer.Ordinal,");
            for (var i = 0; i < windowTypes.Count; i++)
            {
                writer.Line("    " + CodeWriter.Literal(windowTypes[i]) + (i == windowTypes.Count - 1 ? ");" : ","));
            }
        }

        context.WriteFile(context.Source("Html", "EventHandlerNames.g.cs"), writer.ToString());
    }

    private static void GenerateFamily(GeneratorContext context, IdlModel idl, HtmlSpecSource html, EnumeratedAttributeTable enumerations, Family family)
    {
        // Elements: local name -> interface
        var elements = new List<(string Name, string Interface)>();
        foreach (var file in family.ElementFiles)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(context.SpecData("webref", "ed", "elements", file)));
            foreach (var element in document.RootElement.GetProperty("elements").EnumerateArray())
            {
                if (element.TryGetProperty("interface", out var interfaceName) && interfaceName.ValueKind == JsonValueKind.String)
                {
                    var name = element.GetProperty("name").GetString()!;
                    if (!elements.Exists(e => e.Name == name))
                    {
                        elements.Add((name, interfaceName.GetString()!));
                    }
                }
            }
        }

        foreach (var (name, interfaceName) in ElementInterfaceOverrides.Where(o => o.Family == family.Name).Select(o => (o.Name, o.Interface)))
        {
            elements.RemoveAll(e => e.Name == name);
            elements.Add((name, interfaceName));
        }

        // Interfaces: the base interface and the interfaces that inherit from it, defined by the family's specifications
        var interfaces = idl.Definitions.Values
            .Where(d => d.Kind == IdlDefinitionKind.Interface && family.IdlSources.Contains(d.Source, StringComparer.Ordinal) && (d.Name == family.BaseInterface || InheritsFrom(idl, d, family.BaseInterface)))
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .ToList();

        var writer = new CodeWriter("tests/Meziantou.Framework.WebPlatform.SpecData/webref (idlparsed, elements) and tests/Meziantou.Framework.WebPlatform.SpecData/html-source");
        writer.Line("using Meziantou.Framework.WebPlatform.Html;");
        if (family.Name == "Svg")
        {
            writer.Line("using Meziantou.Framework.WebPlatform.Svg;");
        }

        writer.Line();
        writer.Line("namespace Meziantou.Framework.WebPlatform.Dom;");
        foreach (var definition in interfaces)
        {
            var hasElements = elements.Exists(e => e.Interface == definition.Name);
            var hasChildren = interfaces.Exists(i => i.Inheritance == definition.Name);
            var isBase = definition.Name == family.BaseInterface;
            var modifiers = isBase ? "" : !hasChildren ? "sealed " : !hasElements ? "abstract " : "";
            var elementNames = GetElementNames(interfaces, elements, definition.Name);
            if (isBase)
            {
                elementNames.Add(family.GlobalOwner);
            }

            writer.Line();
            writer.Line($"/// <summary>{GetHref(definition)}</summary>");
            writer.SeeAlso(GetHref(definition));
            writer.Line($"[IdlInterface({CodeWriter.Literal(definition.Name)}, {CodeWriter.Literal(GetHref(definition))})]");
            using (writer.Block($"public {modifiers}partial class {ClrNames.ToClrName(definition.Name)}" + (isBase ? "" : " : " + ClrNames.ToClrName(definition.Inheritance!))))
            {
                if (!isBase)
                {
                    var accessibility = modifiers == "abstract " ? "private protected" : "internal";
                    writer.Line($"{accessibility} {ClrNames.ToClrName(definition.Name)}(Document document, string localName, string? prefix)");
                    using (writer.Block("    : base(document, localName, prefix)"))
                    {
                        writer.Line("OnCreated();");
                    }

                    writer.Line();
                    writer.Line("/// <summary>Runs when the element is created.</summary>");
                    writer.Line("partial void OnCreated();");
                }

                GenerateMembers(writer, idl, html, enumerations, definition, elementNames, family.GlobalOwner);
            }
        }

        context.WriteFile(context.Source(family.OutputDirectory, family.Name + "Elements.g.cs"), writer.ToString());

        GenerateFactory(context, family, elements, interfaces);
    }

    private static void GenerateFactory(GeneratorContext context, Family family, List<(string Name, string Interface)> elements, List<IdlDefinition> interfaces)
    {
        var writer = new CodeWriter("tests/Meziantou.Framework.WebPlatform.SpecData/webref/ed/elements");
        if (family.Name == "Html")
        {
            writer.Line("using System.Collections.Frozen;");
        }

        writer.Line("using Meziantou.Framework.WebPlatform.Dom;");
        writer.Line();
        writer.Line("namespace Meziantou.Framework.WebPlatform.Html;");
        writer.Line();
        using (writer.Block("internal static partial class ElementFactory"))
        {
            writer.Line($"/// <summary>Creates an element of the {family.Name} namespace using the element interface of the local name, or null for an unknown local name.</summary>");
            using (writer.Block($"private static {ClrNames.ToClrName(family.BaseInterface)}? Create{family.Name}ElementOrNull(Document document, string localName, string? prefix)"))
            {
                // HTML names are atoms: switch on the tag id
                writer.Line(family.Name == "Html" ? "return TagNames.GetTagId(localName) switch" : "return localName switch");
                writer.Line("{");
                {
                    foreach (var (name, interfaceName) in elements.OrderBy(e => e.Name, StringComparer.Ordinal))
                    {
                        if (interfaceName == family.BaseInterface && family.Name != "Html")
                            continue;

                        if (interfaceName != family.BaseInterface && !interfaces.Exists(i => i.Name == interfaceName))
                            continue;

                        var pattern = family.Name == "Html" ? "TagId." + TagIdentifier(name) : CodeWriter.Literal(name);
                        writer.Line($"    {pattern} => new {ClrNames.ToClrName(interfaceName)}(document, localName, prefix),");
                    }

                    writer.Line("    _ => null,");
                }

                writer.Line("};");
            }

            if (family.Name == "Html")
            {
                // Type -> local names, used by Document.CreateElement<T>(). HTMLElement and HTMLUnknownElement are not
                // included, as they are the interface of many elements.
                var localNames = elements
                    .Where(e => e.Interface is not "HTMLElement" and not "HTMLUnknownElement" && interfaces.Exists(i => i.Name == e.Interface))
                    .GroupBy(e => e.Interface, StringComparer.Ordinal)
                    .OrderBy(g => ClrNames.ToClrName(g.Key), StringComparer.Ordinal);

                writer.Line();
                using (writer.Block("private static class HtmlLocalNamesByType"))
                {
                    using (writer.Block("public static readonly FrozenDictionary<Type, string[]> Value = new Dictionary<Type, string[]>"))
                    {
                        foreach (var group in localNames)
                        {
                            var names = string.Join(", ", group.Select(e => e.Name).Order(StringComparer.Ordinal).Select(CodeWriter.Literal));
                            writer.Line($"[typeof({ClrNames.ToClrName(group.Key)})] = [{names}],");
                        }
                    }

                    writer.Line(".ToFrozenDictionary();");
                }

                writer.Line();
                writer.Line("/// <summary>Returns the local names of the HTML elements whose element interface is the type, or an empty array (for HtmlElement and HtmlUnknownElement).</summary>");
                writer.Line("internal static string[] GetHtmlLocalNames(Type type) => HtmlLocalNamesByType.Value.GetValueOrDefault(type, []);");
            }
        }

        context.WriteFile(context.Source("Html", "ElementFactory." + family.Name + ".g.cs"), writer.ToString());
    }

    // The name of the TagId member of a tag name (see HtmlNamesGenerator)
    private static string TagIdentifier(string name)
    {
        var builder = new StringBuilder();
        var upper = true;
        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c))
            {
                upper = true;
                continue;
            }

            builder.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }

        return builder.ToString();
    }

    private static List<string> GetElementNames(List<IdlDefinition> interfaces, List<(string Name, string Interface)> elements, string interfaceName)
    {
        var result = elements.Where(e => e.Interface == interfaceName).Select(e => e.Name).ToList();
        foreach (var child in interfaces.Where(i => i.Inheritance == interfaceName))
        {
            result.AddRange(GetElementNames(interfaces, elements, child.Name));
        }

        return result;
    }

    private static bool InheritsFrom(IdlModel idl, IdlDefinition definition, string baseName)
    {
        for (var current = definition.Inheritance; current is not null; current = idl.Get(current)?.Inheritance)
        {
            if (current == baseName)
                return true;
        }

        return false;
    }

    /// <summary>The content attribute reflected by an IDL attribute of an SVG DOM object (see SvgProperties.GetContentAttribute).</summary>
    private static string GetSvgContentAttribute(string member)
    {
        switch (member)
        {
            case "className":
                return "class";
            case "in1":
                return "in";
            case "orientType" or "orientAngle":
                return "orient";
        }

        // The two numbers of a "number-optional-number" attribute
        foreach (var pair in (string[])["order", "kernelUnitLength", "baseFrequency", "stdDeviation", "radius"])
        {
            if (member.Length == pair.Length + 1 && member.StartsWith(pair, StringComparison.Ordinal) && member[^1] is 'X' or 'Y')
                return pair;
        }

        return member;
    }

    private static void GenerateMembers(CodeWriter writer, IdlModel idl, HtmlSpecSource html, EnumeratedAttributeTable enumerations, IdlDefinition definition, List<string> elementNames, string globalOwner, bool isElement = true)
    {
        // Attributes redeclared by a specification extending an interface (e.g. SVGPathElement.pathLength) are inherited
        var inherited = new HashSet<string>(StringComparer.Ordinal);
        for (var ancestor = definition.Inheritance is null ? null : idl.Get(definition.Inheritance); ancestor is not null && ancestor.Name != "Element"; ancestor = ancestor.Inheritance is null ? null : idl.Get(ancestor.Inheritance))
        {
            inherited.UnionWith(ancestor.Members.Where(m => m.Kind == IdlMemberKind.Attribute && MemberSources.Contains(m.Source)).Select(m => m.Name));
        }

        // Constants of the element interfaces (e.g. HTMLMediaElement.NETWORK_EMPTY)
        if (isElement)
        {
            foreach (var member in definition.Members)
            {
                if (member.Kind != IdlMemberKind.Const || member.DeclaringDefinition != definition.Name || !MemberSources.Contains(member.Source) || member.ConstValue is not { } constValue)
                    continue;

                var clrType = member.Type?.Name switch
                {
                    "unsigned short" => "ushort",
                    "short" => "short",
                    "unsigned long" => "uint",
                    "long" => "int",
                    _ => throw new InvalidOperationException($"Unsupported constant type {member.Type} for {definition.Name}.{member.Name}"),
                };

                writer.Line();
                writer.Summary($"The <c>{CodeWriter.XmlText(member.Name)}</c> constant ({CodeWriter.XmlText(constValue.GetProperty("value").GetString() ?? "")}).");
                writer.SeeAlso(member.Href ?? GetHref(definition));
                writer.Line($"[IdlMember({CodeWriter.Literal(member.Name)}, {CodeWriter.Literal(member.Href ?? GetHref(definition))})]");
                writer.Line($"public const {clrType} {ToConstantName(member.Name)} = {constValue.GetProperty("value").GetString()};");
            }
        }

        // The content attributes reflected by the SVG DOM objects of the interface (SVGAnimatedLength...), for the SMIL animations
        var animatedAttributes = new SortedDictionary<string, (string Type, string Key)>(StringComparer.Ordinal);

        foreach (var member in definition.Members)
        {
            if (member.Kind != IdlMemberKind.Attribute || member.IsStatic || member.Type is null || !MemberSources.Contains(member.Source) || inherited.Contains(member.Name))
                continue;

            var href = member.Href ?? GetHref(definition);
            if (member.Type.Name is not null && EventHandlerTypes.Contains(member.Type.Name) && member.Name.StartsWith("on", StringComparison.Ordinal))
            {
                var eventType = member.Name[2..];
                writer.Line();
                writer.Summary($"Gets or sets the event handler of the <c>{CodeWriter.XmlText(eventType)}</c> event (the <c>{CodeWriter.XmlText(member.Name)}</c> event handler attribute).");
                writer.SeeAlso(href);
                writer.Line($"[IdlMember({CodeWriter.Literal(member.Name)}, {CodeWriter.Literal(href)})]");
                if (member.ExperimentalApi is not null)
                {
                    writer.Line($"[Experimental(ExperimentalApis.{member.ExperimentalApi}, UrlFormat = ExperimentalApis.UrlFormat)]");
                }

                using (writer.Block($"public Action<Event>? {ToEventHandlerName(eventType)}"))
                {
                    writer.Line($"get => GetEventHandler({CodeWriter.Literal(eventType)});");
                    writer.Line($"set => SetEventHandler({CodeWriter.Literal(eventType)}, value);");
                }

                continue;
            }

            var type = idl.ResolveTypedefs(member.Type);

            // SVG DOM objects reflecting the attributes (SVGAnimatedLength...)
            if (isElement && member.Readonly && type.Name is { } svgType && (svgType.StartsWith("SVGAnimated", StringComparison.Ordinal) || svgType is "SVGStringList" or "SVGPointList"))
            {
                writer.Line();
                writer.Summary($"Gets the <c>{CodeWriter.XmlText(member.Name)}</c> attribute as a <c>{CodeWriter.XmlText(svgType)}</c> object.");
                writer.SeeAlso(href);
                writer.Line($"[IdlMember({CodeWriter.Literal(member.Name)}, {CodeWriter.Literal(href)})]");
                var modifier = member.Name == "className" ? "new " : "";
                var key = member.DeclaringDefinition + "." + member.Name;
                writer.Line($"public {modifier}{ClrNames.ToClrName(svgType)} {ToPascalCase(member.Name)} => SvgProperties.Get<{ClrNames.ToClrName(svgType)}>(this, {CodeWriter.Literal(member.Name)}, {CodeWriter.Literal(key)});");
                if (svgType != "SVGStringList" && member.Name != "animatedPoints")
                {
                    animatedAttributes.TryAdd(GetSvgContentAttribute(member.Name), (svgType, key));
                }

                continue;
            }

            if (HandWrittenReflectedAttributes.Contains(definition.Name + "." + member.Name))
                continue;

            var reflect = member.ExtAttrs.Find(a => a.Name is "Reflect" or "ReflectURL" or "ReflectNonNegative" or "ReflectPositive" or "ReflectPositiveWithFallback");
            var reflectSetter = member.GetExtAttr("ReflectSetter");
            var contentAttribute = Unquote((reflect ?? reflectSetter)?.Value) ?? member.Name.ToLowerInvariant();

            // Limited to only known values (defined in prose by the specification)
            EnumeratedAttributeDefinition? enumerated = null;
            if (type.Name is "DOMString" && reflectSetter is null && isElement && TryGetLimitedReflection(idl, html, definition, member, out var limitedContentAttribute))
            {
                enumerated = FindEnumeratedAttribute(html, elementNames, globalOwner, limitedContentAttribute);
                contentAttribute = limitedContentAttribute;
                if (enumerated is null)
                {
                    Console.Error.WriteLine($"warning: no enumerated attribute definition for {definition.Name}.{member.Name} ({limitedContentAttribute})");
                }
            }

            if (reflect is null && reflectSetter is null && enumerated is null)
                continue;

            var propertyName = ToPascalCase(member.Name);
            var name = CodeWriter.Literal(contentAttribute);
            var clrType = GetClrType(type);
            writer.Line();
            writer.Summary(GetReflectedAttributeSummary(member, type, reflect, contentAttribute, enumerated is not null));
            writer.SeeAlso(href);
            writer.Line($"[IdlMember({CodeWriter.Literal(member.Name)}, {CodeWriter.Literal(href)})]");
            if (enumerated is not null)
            {
                var table = "EnumeratedAttributes." + enumerations.GetName(enumerated, contentAttribute);
                using (writer.Block($"public {clrType} {propertyName}"))
                {
                    if (type.Nullable)
                    {
                        writer.Line($"get => Reflection.GetNullableEnumerated(this, {name}, {table});");
                        writer.Line($"set => Reflection.SetNullableString(this, {name}, value);");
                    }
                    else
                    {
                        writer.Line($"get => Reflection.GetEnumerated(this, {name}, {table});");
                        writer.Line($"set => Reflection.SetString(this, {name}, value);");
                    }
                }

                continue;
            }

            var defaultValue = member.GetExtAttr("ReflectDefault")?.Value;

            // Numeric reflections defined in prose
            if (reflect?.Name == "Reflect" && html.NumericReflections.TryGetValue(member.DeclaringDefinition + "." + member.Name, out var numeric))
            {
                reflect = new IdlExtendedAttribute { Name = numeric.Kind };
                defaultValue ??= numeric.DefaultValue;
            }
            var range = member.GetExtAttr("ReflectRange")?.Values;
            string getter;
            string? setter;
            switch (type.Name, type.Generic)
            {
                case ("DOMString", _) when type.Nullable:
                    getter = $"Reflection.GetNullableString(this, {name})";
                    setter = $"Reflection.SetNullableString(this, {name}, value)";
                    break;
                case ("DOMString", _) when reflect?.Name == "ReflectURL":
                case ("USVString", _) when reflect?.Name == "ReflectURL":
                    getter = $"Reflection.GetUrl(this, {name})";
                    setter = $"Reflection.SetString(this, {name}, value)";
                    break;
                case ("DOMString", _):
                    getter = $"Reflection.GetString(this, {name})";
                    setter = $"Reflection.SetString(this, {name}, value)";
                    break;
                case ("USVString", _):
                    getter = $"Reflection.GetUsvString(this, {name})";
                    setter = $"Reflection.SetString(this, {name}, value)";
                    break;
                case ("boolean", _):
                    getter = $"Reflection.GetBoolean(this, {name})";
                    setter = $"Reflection.SetBoolean(this, {name}, value)";
                    break;
                case ("long", _):
                    {
                        var nonNegative = reflect?.Name == "ReflectNonNegative" ? "true" : "false";
                        getter = $"Reflection.GetLong(this, {name}, nonNegative: {nonNegative}, defaultValue: {defaultValue ?? "null"})";
                        setter = $"Reflection.SetLong(this, {name}, nonNegative: {nonNegative}, value)";
                        break;
                    }

                case ("unsigned long", _):
                    {
                        var positive = reflect?.Name is "ReflectPositive" or "ReflectPositiveWithFallback" ? "true" : "false";
                        var positiveOnly = reflect?.Name == "ReflectPositive" ? "true" : "false";
                        var withFallback = reflect?.Name == "ReflectPositiveWithFallback" ? "true" : "false";
                        var rangeArguments = range is { Count: 2 } ? $", clampedMin: {range[0]}, clampedMax: {range[1]}" : "";
                        getter = $"Reflection.GetUnsignedLong(this, {name}, positive: {positive}, defaultValue: {defaultValue ?? "null"}{rangeArguments})";
                        setter = $"Reflection.SetUnsignedLong(this, {name}, positive: {positiveOnly}, positiveWithFallback: {withFallback}, defaultValue: {defaultValue ?? "null"}, value)";
                        break;
                    }

                case ("double", _):
                case ("unrestricted double", _):
                    {
                        var positive = reflect?.Name == "ReflectPositive" ? "true" : "false";
                        getter = $"Reflection.GetDouble(this, {name}, positive: {positive}, defaultValue: {(defaultValue is null ? "null" : FormatDouble(defaultValue))})";
                        setter = $"Reflection.SetDouble(this, {name}, positive: {positive}, value)";
                        break;
                    }

                case ("DOMTokenList", _):
                    getter = SupportedTokens.TryGetValue(definition.Name + "." + member.Name, out var supportedTokens)
                        ? $"Reflection.GetTokenList(this, {name}, {supportedTokens})"
                        : $"Reflection.GetTokenList(this, {name})";
                    setter = null;
                    break;
                case (_, "FrozenArray"):
                    getter = $"Reflection.GetElements(this, {name})";
                    setter = $"Reflection.SetElements(this, {name}, value)";
                    break;
                case ({ } elementType, "") when elementType.EndsWith("Element", StringComparison.Ordinal):
                    getter = $"Reflection.GetElement<{ClrNames.ToClrName(elementType)}>(this, {name})";
                    setter = $"Reflection.SetElement(this, {name}, value)";
                    break;
                default:
                    Console.Error.WriteLine($"warning: unsupported reflected type {type} for {definition.Name}.{member.Name}");
                    continue;
            }

            if (reflectSetter is not null)
            {
                // The getter is hand-written
                getter = $"Compute{propertyName}()";
            }

            using (writer.Block($"public {clrType} {propertyName}"))
            {
                writer.Line($"get => {getter};");
                if (setter is not null && !member.Readonly)
                {
                    writer.Line($"set => {setter};");
                }
            }

            if (reflectSetter is not null)
            {
                writer.Line();
                writer.Line($"private partial {clrType} Compute{propertyName}();");
            }
        }
        // https://svgwg.org/specs/animations/#TermAnimatableAttribute: the type of the animated value of the attributes
        if (isElement && animatedAttributes.Count > 0)
        {
            var isSvgBase = definition.Name == "SVGElement";
            writer.Line();
            writer.Summary("Returns the interface of the SVG DOM object that reflects a content attribute (e.g. SVGAnimatedLength) and the key of its definition, or null.");
            writer.Line($"internal {(isSvgBase ? "virtual" : "override")} (string Type, string Key)? GetAnimatedAttribute(string name) => name switch");
            writer.Line("{");
            foreach (var (attribute, (svgType, key)) in animatedAttributes)
            {
                writer.Line($"    {CodeWriter.Literal(attribute)} => ({CodeWriter.Literal(svgType)}, {CodeWriter.Literal(key)}),");
            }

            writer.Line(isSvgBase ? "    _ => null," : "    _ => base.GetAnimatedAttribute(name),");
            writer.Line("};");
        }
    }

    // Some specifications do not export the anchors of their definitions: use the SVG convention (#InterfaceName)
    private static string GetHref(IdlDefinition definition) => definition.Href ?? definition.SpecUrl + "#Interface" + definition.Name;

    private static bool TryGetLimitedReflection(IdlModel idl, HtmlSpecSource html, IdlDefinition definition, IdlMember member, out string contentAttribute)
    {
        if (html.LimitedReflections.TryGetValue(definition.Name + "." + member.Name, out contentAttribute!) || html.LimitedReflections.TryGetValue(member.DeclaringDefinition + "." + member.Name, out contentAttribute!))
            return true;

        // Members of a mixin can be defined in prose for one of the interfaces that include the mixin (e.g. popoverTargetAction)
        if (idl.Get(member.DeclaringDefinition)?.Kind == IdlDefinitionKind.InterfaceMixin)
        {
            foreach (var (key, value) in html.LimitedReflections)
            {
                // Not an attribute of the same name declared by another interface (HTMLButtonElement.type is not HyperlinkElementUtils.type).
                // The prose may use another interface than the mixin (popoverTargetAction is defined for HTMLElement).
                var separator = key.LastIndexOf('.', StringComparison.Ordinal);
                if (key[(separator + 1)..] == member.Name && idl.Get(key[..separator])?.Members.Exists(m => m.Name == member.Name && m.DeclaringDefinition != member.DeclaringDefinition) is not true)
                {
                    contentAttribute = value;
                    return true;
                }
            }
        }

        contentAttribute = "";
        return false;
    }

    private static EnumeratedAttributeDefinition? FindEnumeratedAttribute(HtmlSpecSource html, List<string> elementNames, string globalOwner, string contentAttribute)
    {
        foreach (var element in elementNames)
        {
            if (html.EnumeratedAttributes.TryGetValue(element + "/" + contentAttribute, out var definition))
                return definition;
        }

        if (html.EnumeratedAttributes.TryGetValue("html-global/" + contentAttribute, out var global))
            return global;

        _ = globalOwner;
        return html.EnumeratedAttributes.Where(p => p.Key.EndsWith("/" + contentAttribute, StringComparison.Ordinal)).OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Value).FirstOrDefault();
    }

    private static string GetClrType(IdlType type)
    {
        var result = (type.Name, type.Generic) switch
        {
            ("DOMString" or "USVString" or "ByteString", _) => "string",
            ("boolean", _) => "bool",
            ("long", _) => "int",
            ("unsigned long", _) => "uint",
            ("double" or "unrestricted double", _) => "double",
            (_, "FrozenArray") => "IReadOnlyList<" + GetClrType(type.Subtypes[0]) + ">",
            _ => ClrNames.ToClrName(type.Name!),
        };

        return type.Nullable ? result + "?" : result;
    }

    private static string FormatDouble(string value)
    {
        var parsed = double.Parse(value, CultureInfo.InvariantCulture);
        var text = parsed.ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('.', StringComparison.Ordinal) || text.Contains('E', StringComparison.Ordinal) ? text : text + ".0";
    }

    private static string? Unquote(string? value) => value is { Length: >= 2 } && value[0] == '"' ? value[1..^1] : value;

    /// <summary>Converts a constant name to PascalCase (SVG_FEBLEND_MODE_NORMAL → SvgFeblendModeNormal).</summary>
    private static string ToConstantName(string name) => string.Concat(name.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));

    public static string ToPascalCase(string name) => ClrNames.ToClrName(char.ToUpperInvariant(name[0]) + name[1..]);

    private static string ToEventHandlerName(string eventType)
    {
        // Greedy segmentation using the known words, longest word first
        var builder = new StringBuilder("On");
        var index = 0;
        while (index < eventType.Length)
        {
            var word = EventWords.Where(w => string.CompareOrdinal(eventType, index, w, 0, w.Length) == 0).OrderByDescending(w => w.Length).FirstOrDefault();
            if (word is null)
            {
                builder.Append(char.ToUpperInvariant(eventType[index])).Append(eventType, index + 1, eventType.Length - index - 1);
                break;
            }

            builder.Append(char.ToUpperInvariant(word[0])).Append(word, 1, word.Length - 1);
            index += word.Length;
        }

        return builder.ToString();
    }

    private static void GenerateEnumeratedAttributes(GeneratorContext context, EnumeratedAttributeTable enumerations)
    {
        var writer = new CodeWriter("tests/Meziantou.Framework.WebPlatform.SpecData/html-source");
        writer.Line("namespace Meziantou.Framework.WebPlatform.Html;");
        writer.Line();
        writer.Line("/// <summary>The enumerated attributes of the HTML specification (https://html.spec.whatwg.org/multipage/common-microsyntaxes.html#enumerated-attribute).</summary>");
        using (writer.Block("internal static class EnumeratedAttributes"))
        {
            var first = true;
            foreach (var (definition, name) in enumerations.Entries.OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                if (!first)
                {
                    writer.Line();
                }

                first = false;
                var states = definition.States;
                writer.Line($"/// <summary>{definition.Source}</summary>");
                writer.Line($"public static readonly EnumeratedAttribute {name} = new(");
                writer.Line("    new Dictionary<string, int>(StringComparer.Ordinal)");
                writer.Line("    {");
                foreach (var (keyword, state) in definition.Keywords)
                {
                    writer.Line($"        [{CodeWriter.Literal(keyword)}] = {states.IndexOf(state)},");
                }

                writer.Line("    },");
                writer.Line("    [" + string.Join(", ", states.Select(s => definition.GetCanonicalKeyword(s) is { } keyword ? CodeWriter.Literal(keyword) : "null")) + "],");
                writer.Line($"    MissingValueDefault: {IndexOf(states, definition.MissingValueDefault)},");
                writer.Line($"    InvalidValueDefault: {IndexOf(states, definition.InvalidValueDefault)},");
                writer.Line($"    EmptyValueDefault: {IndexOf(states, definition.EmptyValueDefault)});");
            }
        }

        context.WriteFile(context.Source("Html", "EnumeratedAttributes.g.cs"), writer.ToString());
    }

    private static int IndexOf(List<string> states, string? state) => state is null ? -1 : states.IndexOf(state);

    private sealed class EnumeratedAttributeTable
    {
        private readonly Dictionary<EnumeratedAttributeDefinition, string> _names = [];

        public EnumeratedAttributeTable(HtmlSpecSource html)
        {
            // Name each definition after its attribute, prefixed by its first owner when several definitions share the attribute name
            var definitions = html.EnumeratedAttributes
                .GroupBy(p => p.Value)
                .Select(g => (Definition: g.Key, Keys: g.Select(p => p.Key).Order(StringComparer.Ordinal).ToList()))
                .ToList();
            foreach (var group in definitions.GroupBy(d => d.Keys[0].Split('/')[1]))
            {
                foreach (var (definition, keys) in group)
                {
                    var owner = keys[0].Split('/')[0];
                    var name = group.Count() == 1 ? ToIdentifier(group.Key) : ToIdentifier(owner == "html-global" ? "global" : owner) + ToIdentifier(group.Key);
                    _names[definition] = name;
                }
            }
        }

        public IEnumerable<(EnumeratedAttributeDefinition Definition, string Name)> Entries => _names.Select(p => (p.Key, p.Value));

        public string GetName(EnumeratedAttributeDefinition definition, string contentAttribute)
        {
            _ = contentAttribute;
            return _names[definition];
        }

        private static string ToIdentifier(string value)
        {
            var builder = new StringBuilder();
            var upper = true;
            foreach (var c in value)
            {
                if (!char.IsAsciiLetterOrDigit(c))
                {
                    upper = true;
                    continue;
                }

                builder.Append(upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }

            return builder.ToString();
        }
    }

    /// <summary>The documentation of a reflected IDL attribute (https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#reflecting-content-attributes-in-idl-attributes).</summary>
    private static string GetReflectedAttributeSummary(IdlMember member, IdlType type, IdlExtendedAttribute? reflect, string contentAttribute, bool isEnumerated)
    {
        var accessors = member.Readonly ? "Gets" : "Gets or sets";
        var attribute = "the <c>" + CodeWriter.XmlText(contentAttribute) + "</c> content attribute";
        return (type.Name, type.Generic) switch
        {
            ("boolean", _) => $"{accessors} a value indicating whether the element has {attribute}.",
            ("DOMTokenList", _) => $"Gets the tokens of {attribute}.",
            ("Element", _) => $"{accessors} the element whose ID is the value of {attribute}.",
            (_, "FrozenArray") => $"{accessors} the elements whose IDs are the values of {attribute}.",
            _ when reflect?.Name == "ReflectURL" => $"{accessors} {attribute}, as a URL resolved against the base URL of the document.",
            _ when isEnumerated => $"{accessors} the value of {attribute}, limited to the known values (the canonical keyword, or the default value).",
            _ => $"{accessors} the value of {attribute}.",
        };
    }

}
