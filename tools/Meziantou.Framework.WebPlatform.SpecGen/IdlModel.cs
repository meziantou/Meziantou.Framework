using System.Text.Json;

namespace SpecGen.Idl;

internal enum IdlDefinitionKind
{
    Interface,
    InterfaceMixin,
    CallbackInterface,
    Namespace,
    Dictionary,
    Enum,
    Typedef,
    Callback,
}

internal enum IdlMemberKind
{
    Attribute,
    Operation,
    Const,
    Constructor,
    Iterable,
    AsyncIterable,
    Maplike,
    Setlike,
    Field,
}

internal sealed class IdlExtendedAttribute
{
    public required string Name { get; init; }

    /// <summary>The right-hand side of the extended attribute (e.g. <c>value</c> for <c>[PutForwards=value]</c>).</summary>
    public string? Value { get; init; }

    /// <summary>The values of a list right-hand side (e.g. <c>[ReflectRange=(1, 1000)]</c>).</summary>
    public List<string> Values { get; init; } = [];
}

internal sealed class IdlType
{
    /// <summary>The type name, for non-generic and non-union types.</summary>
    public string? Name { get; set; }

    public string Generic { get; init; } = "";

    public bool Nullable { get; set; }

    public bool Union { get; init; }

    public List<IdlType> Subtypes { get; init; } = [];

    public List<IdlExtendedAttribute> ExtAttrs { get; init; } = [];

    public bool HasExtAttr(string name) => ExtAttrs.Exists(a => a.Name == name);

    public override string ToString()
    {
        var value = Union ? "(" + string.Join(" or ", Subtypes) + ")" : Generic.Length > 0 ? Generic + "<" + string.Join(", ", Subtypes) + ">" : Name!;
        return Nullable ? value + "?" : value;
    }
}

internal sealed class IdlArgument
{
    public required string Name { get; init; }

    public required IdlType Type { get; set; }

    public bool Optional { get; set; }

    public bool Variadic { get; init; }

    public JsonElement? Default { get; init; }

    public List<IdlExtendedAttribute> ExtAttrs { get; init; } = [];
}

internal sealed class IdlMember
{
    public required IdlMemberKind Kind { get; init; }

    public string Name { get; init; } = "";

    /// <summary>The type of an attribute, the return type of an operation, or the type of a field or constant.</summary>
    public IdlType? Type { get; init; }

    /// <summary>The key/value types of iterable, maplike and setlike declarations.</summary>
    public List<IdlType> DeclarationTypes { get; init; } = [];

    public List<IdlArgument> Arguments { get; init; } = [];

    public bool Readonly { get; init; }

    /// <summary>"static", "getter", "setter", "deleter", "stringifier", "inherit" or "".</summary>
    public string Special { get; set; } = "";

    public bool Required { get; init; }

    public JsonElement? Default { get; set; }

    public JsonElement? ConstValue { get; init; }

    public List<IdlExtendedAttribute> ExtAttrs { get; init; } = [];

    public string? Href { get; init; }

    /// <summary>The name of the webref file (the specification) that defines the member, e.g. "dom".</summary>
    public string Source { get; set; } = "";

    /// <summary>The name of the definition (interface, mixin, partial interface) that declares the member.</summary>
    public string DeclaringDefinition { get; set; } = "";

    /// <summary>
    /// The name of the constant of <c>ExperimentalApis</c> (src/Meziantou.Framework.WebPlatform/Specification/ExperimentalApis.cs)
    /// of the proposal that adds the member, when no specification defines it yet: its .NET member must have the
    /// <see cref="System.Diagnostics.CodeAnalysis.ExperimentalAttribute"/> with this diagnostic identifier.
    /// </summary>
    public string? ExperimentalApi { get; init; }

    public bool IsStatic => Special == "static";

    public IdlExtendedAttribute? GetExtAttr(string name) => ExtAttrs.Find(a => a.Name == name);

    public override string ToString() => $"{Kind} {Name}";
}

internal sealed class IdlDefinition
{
    public required string Name { get; init; }

    public required IdlDefinitionKind Kind { get; init; }

    public string? Inheritance { get; init; }

    public List<IdlMember> Members { get; init; } = [];

    public List<IdlExtendedAttribute> ExtAttrs { get; init; } = [];

    public List<string> EnumValues { get; init; } = [];

    /// <summary>The aliased type of a typedef, or the return type of a callback function.</summary>
    public IdlType? Type { get; init; }

    /// <summary>The arguments of a callback function.</summary>
    public List<IdlArgument> Arguments { get; init; } = [];

    /// <summary>The name of the webref file (the specification) that defines the definition.</summary>
    public string Source { get; set; } = "";

    public string? Href { get; init; }

    /// <summary>The URL of the specification that defines the definition.</summary>
    public string? SpecUrl { get; set; }

    /// <summary>
    /// The name of the constant of <c>ExperimentalApis</c> (src/Meziantou.Framework.WebPlatform/Specification/ExperimentalApis.cs)
    /// of the proposal that adds the definition, when no specification defines it yet: its .NET type must have the
    /// <see cref="System.Diagnostics.CodeAnalysis.ExperimentalAttribute"/> with this diagnostic identifier.
    /// </summary>
    public string? ExperimentalApi { get; init; }

    public bool HasExtAttr(string name) => ExtAttrs.Exists(a => a.Name == name);

    public IdlExtendedAttribute? GetExtAttr(string name) => ExtAttrs.Find(a => a.Name == name);

    public override string ToString() => $"{Kind} {Name}";
}

/// <summary>The merged IDL of all the specifications of webref (tests/Meziantou.Framework.WebPlatform.SpecData/webref/ed/idlparsed).</summary>
/// <remarks>This file is shared with tools/Meziantou.Framework.WebPlatform.SpecGen.</remarks>
internal sealed partial class IdlModel
{
    public Dictionary<string, IdlDefinition> Definitions { get; } = new(StringComparer.Ordinal);

    /// <summary>The legacy window aliases of the interfaces ([LegacyWindowAlias], e.g. SVGPoint → DOMPoint).</summary>
    public Dictionary<string, string> Aliases { get; } = new(StringComparer.Ordinal);

    public IdlDefinition? Get(string name) => Definitions.GetValueOrDefault(name) ?? (Aliases.TryGetValue(name, out var target) ? Definitions.GetValueOrDefault(target) : null);

    /// <summary>Returns the type, resolving typedefs. The model is shared by the tests running in parallel, so the types are not mutated.</summary>
    public IdlType ResolveTypedefs(IdlType type)
    {
        if (type.Union || type.Generic.Length > 0)
        {
            List<IdlType>? subtypes = null;
            for (var i = 0; i < type.Subtypes.Count; i++)
            {
                var resolvedSubtype = ResolveTypedefs(type.Subtypes[i]);
                if (!ReferenceEquals(resolvedSubtype, type.Subtypes[i]))
                {
                    subtypes ??= [.. type.Subtypes];
                    subtypes[i] = resolvedSubtype;
                }
            }

            if (subtypes is null)
                return type;

            return new IdlType { Name = type.Name, Generic = type.Generic, Nullable = type.Nullable, Union = type.Union, Subtypes = subtypes, ExtAttrs = [.. type.ExtAttrs] };
        }

        // https://drafts.csswg.org/cssom/#cssomstring-type: CSSOMString is DOMString (it is not a typedef in the IDL)
        if (type.Name == "CSSOMString")
            return new IdlType { Name = "DOMString", Nullable = type.Nullable, ExtAttrs = [.. type.ExtAttrs] };

        if (type.Name is not null && Get(type.Name) is { Kind: IdlDefinitionKind.Typedef, Type: { } aliased })
        {
            var resolved = ResolveTypedefs(Clone(aliased));
            resolved.Nullable |= type.Nullable;
            resolved.ExtAttrs.AddRange(type.ExtAttrs);
            return resolved;
        }

        return type;
    }

    private static IdlType Clone(IdlType type) => new()
    {
        Name = type.Name,
        Generic = type.Generic,
        Nullable = type.Nullable,
        Union = type.Union,
        Subtypes = type.Subtypes.Select(Clone).ToList(),
        ExtAttrs = [.. type.ExtAttrs],
    };

    /// <summary>Loads the IDL of the webref files of a directory (tests/Meziantou.Framework.WebPlatform.SpecData/webref/ed/idlparsed).</summary>
    public static IdlModel Load(string directory)
    {
        var documents = new List<JsonDocument>();
        try
        {
            var sources = new List<IdlSource>();
            foreach (var file in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
            {
                using var stream = File.OpenRead(file);
                var document = JsonDocument.Parse(stream);
                documents.Add(document);
                var specUrl = document.RootElement.TryGetProperty("spec", out var spec) && spec.TryGetProperty("url", out var url) ? url.GetString() : null;
                sources.Add(new IdlSource(Path.GetFileNameWithoutExtension(file), specUrl, document.RootElement.GetProperty("idlparsed")));
            }

            return Load(sources);
        }
        finally
        {
            foreach (var document in documents)
            {
                document.Dispose();
            }
        }
    }

    /// <summary>The IDL of a specification: the <c>idlNames</c> and <c>idlExtendedNames</c> of a webref file.</summary>
    /// <summary>
    /// [SecureContext] on a partial interface or an interface mixin applies to its members
    /// (https://webidl.spec.whatwg.org/#SecureContext): it is copied to them before they are merged into the interface.
    /// </summary>
    private static void InheritSecureContext(IdlDefinition definition)
    {
        if (!definition.HasExtAttr("SecureContext"))
            return;

        foreach (var member in definition.Members)
        {
            if (member.GetExtAttr("SecureContext") is null)
            {
                member.ExtAttrs.Add(new IdlExtendedAttribute { Name = "SecureContext" });
            }
        }
    }

    /// <summary>
    /// [Exposed] on a partial interface or an interface mixin applies to its members (https://webidl.spec.whatwg.org/#Exposed):
    /// it is copied to them before they are merged into the interface (the members of a mixin are also exposed only where
    /// the interface that includes it is exposed).
    /// </summary>
    private static void InheritExposed(IdlDefinition definition)
    {
        if (definition.GetExtAttr("Exposed") is not { } exposed)
            return;

        foreach (var member in definition.Members)
        {
            if (member.GetExtAttr("Exposed") is null)
            {
                member.ExtAttrs.Add(exposed);
            }
        }
    }

    public readonly record struct IdlSource(string Name, string? SpecUrl, JsonElement Idl);

    private static IdlModel Load(List<IdlSource> sources)
    {
        var model = new IdlModel();
        var partials = new List<IdlDefinition>();
        var includes = new List<(string Target, string Mixin)>();
        foreach (var (source, specUrl, idl) in sources)
        {
            foreach (var property in idl.GetProperty("idlNames").EnumerateObject())
            {
                var definition = ParseDefinition(property.Value);
                if (definition is not null)
                {
                    definition.Source = source;
                    definition.SpecUrl = specUrl;
                    definition.Members.ForEach(m => m.Source = source);
                    model.Definitions.TryAdd(definition.Name, definition);
                }
            }

            foreach (var property in idl.GetProperty("idlExtendedNames").EnumerateObject())
            {
                foreach (var item in property.Value.EnumerateArray())
                {
                    var type = item.GetProperty("type").GetString();
                    if (type == "includes")
                    {
                        includes.Add((item.GetProperty("target").GetString()!, item.GetProperty("includes").GetString()!));
                    }
                    else if (ParseDefinition(item) is { } partial)
                    {
                        partial.Members.ForEach(m => m.Source = source);
                        partials.Add(partial);
                    }
                }
            }
        }

        foreach (var partial in partials)
        {
            if (model.Definitions.TryGetValue(partial.Name, out var definition) && definition.Kind == partial.Kind)
            {
                InheritSecureContext(partial);
                InheritExposed(partial);
                definition.Members.AddRange(partial.Members);

                // [LegacyOverrideBuiltIns] on a partial interface is considered to appear on the interface itself
                // (https://webidl.spec.whatwg.org/#LegacyOverrideBuiltIns), e.g. Document
                if (partial.GetExtAttr("LegacyOverrideBuiltIns") is { } legacyOverrideBuiltIns && !definition.HasExtAttr("LegacyOverrideBuiltIns"))
                {
                    definition.ExtAttrs.Add(legacyOverrideBuiltIns);
                }
            }
        }

        foreach (var (target, mixinName) in includes.Distinct())
        {
            if (model.Definitions.TryGetValue(target, out var definition) && model.Definitions.TryGetValue(mixinName, out var mixin) && mixin.Kind == IdlDefinitionKind.InterfaceMixin)
            {
                InheritSecureContext(mixin);
                InheritExposed(mixin);
                definition.Members.AddRange(mixin.Members);
            }
        }

        foreach (var definition in model.Definitions.Values)
        {
            if (definition.ExtAttrs.Find(a => a.Name == "LegacyWindowAlias") is { } alias)
            {
                foreach (var name in alias.Value is null ? alias.Values : [alias.Value])
                {
                    model.Aliases[name] = definition.Name;
                }
            }
        }

        foreach (var definition in model.Definitions.Values)
        {
            // Remove the members added twice (e.g. a mixin included by several specifications)
            var seen = new HashSet<string>(StringComparer.Ordinal);
            definition.Members.RemoveAll(m => m.Kind is IdlMemberKind.Attribute or IdlMemberKind.Const or IdlMemberKind.Field && !seen.Add(m.Kind + " " + m.Name + " " + m.Special));
        }

        SetExecCommandValueType(model);
        SetMouseEventCoordinateTypes(model);
        AddShadowRootGetSelection(model);
        AddShadowRootElementFromPoint(model);
        AddTrustedHtmlParserOptions(model);
        AddFontFaceWidth(model);
        AddUIEventPseudoTarget(model);
        AddElementMatchContainer(model);
        AddPaintWorkletExtensions(model);
        AddMediaSourceHandleToMediaProvider(model);
        AddLayoutAndAnimationWorkletExtensions(model);
        AddContentVisibilityAutoStateChangeEventHandler(model);
        RenameSnapEventHandlers(model);
        AddCssPrivateRule(model);
        AddTestReportBody(model);
        AddAnimationRanges(model);
        ReplaceSvgPathSegmentInterfaceWithDictionary(model);
        FixCssParserApiTypes(model);
        return model;
    }

    /// <summary>
    /// The parser options of Trusted Types (https://github.com/w3c/trusted-types/pull/606) integrated into the HTML parsing
    /// methods by https://github.com/whatwg/html/pull/12583 (2026-10-07), which the IDL of webref does not have yet:
    /// <code>
    /// [Exposed=Window] interface TrustedHTMLParserOptions {};
    /// partial interface TrustedTypePolicy {
    ///   [Exposed=Window] TrustedHTMLParserOptions createParserOptions(optional SetHTMLUnsafeOptions options = {}, any... arguments);
    /// };
    /// partial dictionary TrustedTypePolicyOptions { CreateParserOptionsCallback createParserOptions; };
    /// callback CreateParserOptionsCallback = SetHTMLUnsafeOptions? (SetHTMLUnsafeOptions options, any... arguments);
    /// </code>
    /// The options of <c>setHTMLUnsafe()</c> and <c>parseHTMLUnsafe()</c> become <c>(SetHTMLUnsafeOptions or
    /// TrustedHTMLParserOptions)</c> and <c>(ParseHTMLUnsafeOptions or TrustedHTMLParserOptions)</c>, and the
    /// <c>sanitizer</c> members of the dictionaries have no default value.
    /// </summary>
    private static void AddTrustedHtmlParserOptions(IdlModel model)
    {
        if (model.Definitions.ContainsKey("TrustedHTMLParserOptions")
            || !model.Definitions.TryGetValue("TrustedTypePolicy", out var policy)
            || !model.Definitions.TryGetValue("TrustedTypePolicyOptions", out var policyOptions)
            || !model.Definitions.TryGetValue("SetHTMLUnsafeOptions", out var setHtmlUnsafeOptions)
            || !model.Definitions.TryGetValue("ParseHTMLUnsafeOptions", out var parseHtmlUnsafeOptions))
        {
            return;
        }

        const string Source = "trusted-types";
        const string SpecUrl = "https://w3c.github.io/trusted-types/dist/spec/";
        var exposedWindow = new IdlExtendedAttribute { Name = "Exposed", Value = "Window" };
        model.Definitions["TrustedHTMLParserOptions"] = new IdlDefinition
        {
            Name = "TrustedHTMLParserOptions",
            Kind = IdlDefinitionKind.Interface,
            ExtAttrs = [exposedWindow],
            Members = [],
            Source = Source,
            SpecUrl = SpecUrl,
        };

        model.Definitions["CreateParserOptionsCallback"] = new IdlDefinition
        {
            Name = "CreateParserOptionsCallback",
            Kind = IdlDefinitionKind.Callback,
            Type = new IdlType { Name = "SetHTMLUnsafeOptions", Nullable = true },
            Arguments =
            [
                new IdlArgument { Name = "options", Type = new IdlType { Name = "SetHTMLUnsafeOptions" } },
                new IdlArgument { Name = "arguments", Type = new IdlType { Name = "any" }, Variadic = true },
            ],
            Source = Source,
            SpecUrl = SpecUrl,
        };

        policyOptions.Members.Add(new IdlMember
        {
            Kind = IdlMemberKind.Field,
            Name = "createParserOptions",
            Type = new IdlType { Name = "CreateParserOptionsCallback" },
            Href = SpecUrl + "#dom-trustedtypepolicyoptions-createparseroptions",
            Source = Source,
            DeclaringDefinition = policyOptions.Name,
        });

        using var emptyDictionary = JsonDocument.Parse("""{"type":"dictionary"}""");
        policy.Members.Add(new IdlMember
        {
            Kind = IdlMemberKind.Operation,
            Name = "createParserOptions",
            Type = new IdlType { Name = "TrustedHTMLParserOptions" },
            Arguments =
            [
                new IdlArgument { Name = "options", Type = new IdlType { Name = "SetHTMLUnsafeOptions" }, Optional = true, Default = emptyDictionary.RootElement.Clone() },
                new IdlArgument { Name = "arguments", Type = new IdlType { Name = "any" }, Variadic = true },
            ],
            ExtAttrs = [exposedWindow],
            Href = SpecUrl + "#dom-trustedtypepolicy-createparseroptions",
            Source = Source,
            DeclaringDefinition = policy.Name,
        });

        foreach (var dictionary in (ReadOnlySpan<IdlDefinition>)[setHtmlUnsafeOptions, parseHtmlUnsafeOptions])
        {
            if (dictionary.Members.Find(m => m.Name == "sanitizer") is { } sanitizer)
            {
                sanitizer.Default = null;
            }
        }

        foreach (var (interfaceName, operationName, optionsName) in (ReadOnlySpan<(string, string, string)>)[
            ("Element", "setHTMLUnsafe", "SetHTMLUnsafeOptions"),
            ("ShadowRoot", "setHTMLUnsafe", "SetHTMLUnsafeOptions"),
            ("Document", "parseHTMLUnsafe", "ParseHTMLUnsafeOptions")])
        {
            if (model.Definitions.TryGetValue(interfaceName, out var definition)
                && definition.Members.Find(m => m.Kind == IdlMemberKind.Operation && m.Name == operationName) is { Arguments: [_, { Type.Name: { } typeName } options] }
                && typeName == optionsName)
            {
                options.Type = new IdlType { Union = true, Subtypes = [new IdlType { Name = optionsName }, new IdlType { Name = "TrustedHTMLParserOptions" }] };
            }
        }
    }

    /// <summary>
    /// The <c>oncontentvisibilityautostatechange</c> event handler of GlobalEventHandlers: CSS Containment defines the
    /// <c>contentvisibilityautostatechange</c> event (https://drafts.csswg.org/css-contain-2/#content-visibility-auto-state-change),
    /// and the browsers implement its event handler (used by the css-contain tests), which no specification defines yet.
    /// </summary>
    private static void AddContentVisibilityAutoStateChangeEventHandler(IdlModel model)
    {
        // The members of the mixin are already merged into the interfaces that include it
        foreach (var definition in model.Definitions.Values)
        {
            if (!definition.Members.Exists(m => m.DeclaringDefinition == "GlobalEventHandlers") || definition.Members.Exists(m => m.Name == "oncontentvisibilityautostatechange"))
                continue;

            definition.Members.Add(new IdlMember
            {
                Kind = IdlMemberKind.Attribute,
                Name = "oncontentvisibilityautostatechange",
                Type = new IdlType { Name = "EventHandler" },
                Href = "https://drafts.csswg.org/css-contain-2/#content-visibility-auto-state-change",
                Source = "css-contain-2",
                DeclaringDefinition = "GlobalEventHandlers",
                ExperimentalApi = "ContentVisibilityAutoStateChangeEventHandler",
            });
        }
    }

    /// <summary>
    /// The event handlers of the snap events: the IDL of CSS Scroll Snap 2 names them <c>onsnapchanged</c> and
    /// <c>onsnapchanging</c>, but its prose (https://drafts.csswg.org/css-scroll-snap-2/#event-handlers-on-elements-document-and-window-objects)
    /// and the browsers name them after the <c>scrollsnapchange</c> and <c>scrollsnapchanging</c> events.
    /// </summary>
    private static void RenameSnapEventHandlers(IdlModel model)
    {
        foreach (var definition in model.Definitions.Values)
        {
            for (var i = 0; i < definition.Members.Count; i++)
            {
                var member = definition.Members[i];
                if (member.Source == "css-scroll-snap-2" && member.Name is "onsnapchanged" or "onsnapchanging")
                {
                    var name = member.Name == "onsnapchanged" ? "onscrollsnapchange" : "onscrollsnapchanging";
                    definition.Members[i] = new IdlMember
                    {
                        Kind = member.Kind,
                        Name = name,
                        Type = member.Type,
                        ExtAttrs = member.ExtAttrs,
                        Href = "https://drafts.csswg.org/css-scroll-snap-2/#dom-globaleventhandlers-" + name,
                        Source = member.Source,
                        DeclaringDefinition = member.DeclaringDefinition,
                    };
                }
            }
        }
    }

    /// <summary>
    /// ShadowRoot.getSelection() (https://www.w3.org/TR/2014/WD-shadow-dom-20140617/#widl-ShadowRoot-getSelection-Selection)
    /// is implemented by Blink and WebKit and used by the shadow-dom tests, but the Selection API does not define it.
    /// </summary>
    private static void AddShadowRootGetSelection(IdlModel model)
    {
        if (!model.Definitions.TryGetValue("ShadowRoot", out var definition))
            return;

        definition.Members.Add(new IdlMember
        {
            Kind = IdlMemberKind.Operation,
            Name = "getSelection",
            Type = new IdlType { Name = "Selection", Nullable = true },
            Source = "shadow-dom",
            DeclaringDefinition = definition.Name,
            ExperimentalApi = "ShadowRootExtensions",
        });
    }

    /// <summary>
    /// ShadowRoot.elementFromPoint() and ShadowRoot.elementsFromPoint() (https://www.w3.org/TR/2018/NOTE-shadow-dom-20180301/#extensions-to-the-documentorshadowroot-mixin)
    /// are implemented by the browsers and used by the shadow-dom tests, but CSSOM View only defines them for the documents
    /// (https://github.com/w3c/csswg-drafts/issues/556).
    /// </summary>
    private static void AddShadowRootElementFromPoint(IdlModel model)
    {
        if (!model.Definitions.TryGetValue("ShadowRoot", out var definition))
            return;

        definition.Members.Add(new IdlMember
        {
            Kind = IdlMemberKind.Operation,
            Name = "elementFromPoint",
            Type = new IdlType { Name = "Element", Nullable = true },
            Arguments = [new IdlArgument { Name = "x", Type = new IdlType { Name = "double" } }, new IdlArgument { Name = "y", Type = new IdlType { Name = "double" } }],
            Source = "shadow-dom",
            DeclaringDefinition = definition.Name,
            ExperimentalApi = "ShadowRootExtensions",
        });
        definition.Members.Add(new IdlMember
        {
            Kind = IdlMemberKind.Operation,
            Name = "elementsFromPoint",
            Type = new IdlType { Generic = "sequence", Subtypes = [new IdlType { Name = "Element" }] },
            Arguments = [new IdlArgument { Name = "x", Type = new IdlType { Name = "double" } }, new IdlArgument { Name = "y", Type = new IdlType { Name = "double" } }],
            Source = "shadow-dom",
            DeclaringDefinition = definition.Name,
            ExperimentalApi = "ShadowRootExtensions",
        });
    }

    /// <summary>
    /// The MediaProvider typedef of the HTML Standard (the type of HTMLMediaElement.srcObject) also includes
    /// MediaSourceHandle: "If a HTMLMediaElement's srcObject attribute is assigned a MediaSourceHandle..."
    /// (https://w3c.github.io/media-source/#htmlmediaelement-extensions-srcobject), which is prose in the specification
    /// ("MediaSourceHandle needs to be added to HTMLMediaElement's MediaProvider IDL typedef").
    /// </summary>
    private static void AddMediaSourceHandleToMediaProvider(IdlModel model)
    {
        if (model.Definitions.TryGetValue("MediaProvider", out var provider) && provider is { Kind: IdlDefinitionKind.Typedef, Type: { Union: true } union }
            && !union.Subtypes.Exists(type => type.Name == "MediaSourceHandle"))
        {
            union.Subtypes.Add(new IdlType { Name = "MediaSourceHandle" });
        }
    }

    /// <summary>
    /// The extensions of the HTML canvas IDL for the paint worklets (https://drafts.css-houdini.org/css-paint-api-1/):
    /// <list type="bullet">
    /// <item>"The CanvasImageSource typedef is extended to also include the CSSImageValue type" (https://drafts.css-houdini.org/css-paint-api-1/#drawing-a-cssimagevalue),
    /// which is prose in the specification.</item>
    /// <item>Path2D is also exposed in the paint worklets: PaintRenderingContext2D includes CanvasDrawPath, whose operations
    /// take a Path2D, but the HTML Standard only exposes the interface in the windows and the workers.</item>
    /// <item>PaintRenderingContext2D also includes CanvasFilters (the filter attribute), which the specification does not list.</item>
    /// <item>CSS.paintWorklet is [SecureContext], as the Worklet interface it returns (the worklets only exist in the secure
    /// contexts): the specification does not annotate the attribute.</item>
    /// </list>
    /// The last three follow Chromium, the only implementation of the CSS Painting API, and the web-platform-tests
    /// (css/css-paint-api/paint2d-paths.https.html, paint2d-filter.https.html).
    /// </summary>
    private static void AddPaintWorkletExtensions(IdlModel model)
    {
        if (model.Definitions.TryGetValue("CSS", out var css) && css.Members.Find(member => member.Name == "paintWorklet") is { } paintWorklet && paintWorklet.GetExtAttr("SecureContext") is null)
        {
            paintWorklet.ExtAttrs.Add(new IdlExtendedAttribute { Name = "SecureContext" });
        }

        if (model.Definitions.TryGetValue("CanvasImageSource", out var imageSource) && imageSource is { Kind: IdlDefinitionKind.Typedef, Type: { Union: true } union }
            && !union.Subtypes.Exists(type => type.Name == "CSSImageValue"))
        {
            union.Subtypes.Add(new IdlType { Name = "CSSImageValue" });
        }

        if (model.Definitions.TryGetValue("Path2D", out var path2D) && path2D.GetExtAttr("Exposed") is { } exposed)
        {
            var names = exposed.Value is { } value ? [value] : exposed.Values;
            if (!names.Contains("PaintWorklet") && !names.Contains("*"))
            {
                path2D.ExtAttrs.Remove(exposed);
                path2D.ExtAttrs.Add(new IdlExtendedAttribute { Name = "Exposed", Values = [.. names, "PaintWorklet"] });
            }
        }

        if (model.Definitions.TryGetValue("PaintRenderingContext2D", out var paintContext) && model.Definitions.TryGetValue("CanvasFilters", out var filters)
            && !paintContext.Members.Exists(member => member.Name == "filter"))
        {
            paintContext.Members.AddRange(filters.Members);
        }
    }

    /// <summary>
    /// The adjustments of the IDL of the CSS Layout API (https://drafts.css-houdini.org/css-layout-api-1/) and of the CSS
    /// Animation Worklet API (https://drafts.css-houdini.org/css-animation-worklet-1/):
    /// <list type="bullet">
    /// <item>CSS.layoutWorklet and CSS.animationWorklet are [SecureContext], as the Worklet interface they return (see
    /// <see cref="AddPaintWorkletExtensions"/>).</item>
    /// <item>The arguments of LayoutChild.layoutNextFragment() are optional, and the break token is nullable: the
    /// specification calls it with one argument (<c>child.layoutNextFragment({})</c> in its examples) and with the break token
    /// of a previous fragment, which is null for the last fragment of a child
    /// (<c>child.layoutNextFragment({}, fragment.breakToken)</c>); the web-platform-tests call it without argument
    /// (css/css-layout-api/fragment-data.https.html), as Chromium allows.</item>
    /// <item>The breakToken member of FragmentResultOptions has no default value: the specification gives it the default
    /// value null, which is not a default value of a dictionary type (https://webidl.spec.whatwg.org/#dfn-optional-argument-default-value).</item>
    /// </list>
    /// </summary>
    private static void AddLayoutAndAnimationWorkletExtensions(IdlModel model)
    {
        if (model.Definitions.TryGetValue("CSS", out var css))
        {
            foreach (var name in (ReadOnlySpan<string>)["layoutWorklet", "animationWorklet"])
            {
                if (css.Members.Find(member => member.Name == name) is { } worklet && worklet.GetExtAttr("SecureContext") is null)
                {
                    worklet.ExtAttrs.Add(new IdlExtendedAttribute { Name = "SecureContext" });
                }
            }
        }

        if (model.Definitions.TryGetValue("FragmentResultOptions", out var fragmentResultOptions) && fragmentResultOptions.Members.Find(member => member.Name == "breakToken") is { } breakToken)
        {
            breakToken.Default = null;
        }

        if (model.Definitions.TryGetValue("LayoutChild", out var layoutChild) && layoutChild.Members.Find(member => member.Name == "layoutNextFragment") is { } layoutNextFragment)
        {
            foreach (var argument in layoutNextFragment.Arguments)
            {
                argument.Optional = true;
                if (argument.Type.Name == "ChildBreakToken")
                {
                    argument.Type.Nullable = true;
                }
            }
        }
    }

    /// <summary>
    /// Element.matchContainer() and the ContainerQueryList interface, proposed in https://github.com/w3c/csswg-drafts/pull/13551
    /// and used by the tentative css-conditional tests. They are modeled on matchMedia() and MediaQueryList:
    /// <code>
    /// partial interface Element { ContainerQueryList matchContainer(CSSOMString query); };
    /// [Exposed=Window] interface ContainerQueryList : EventTarget {
    ///   readonly attribute CSSOMString query;
    ///   readonly attribute boolean matches;
    ///   attribute EventHandler onchange;
    /// };
    /// </code>
    /// </summary>
    private static void AddElementMatchContainer(IdlModel model)
    {
        if (!model.Definitions.TryGetValue("Element", out var element) || model.Definitions.ContainsKey("ContainerQueryList"))
            return;

        const string Source = "css-conditional-5";
        element.Members.Add(new IdlMember
        {
            Kind = IdlMemberKind.Operation,
            Name = "matchContainer",
            Type = new IdlType { Name = "ContainerQueryList" },
            Arguments = [new IdlArgument { Name = "query", Type = new IdlType { Name = "CSSOMString" } }],
            Source = Source,
            DeclaringDefinition = element.Name,
            ExperimentalApi = "MatchContainer",
        });

        IdlMember Attribute(string name, string type, bool readOnly) => new()
        {
            Kind = IdlMemberKind.Attribute,
            Name = name,
            Type = new IdlType { Name = type },
            Readonly = readOnly,
            Source = Source,
            DeclaringDefinition = "ContainerQueryList",
        };

        model.Definitions["ContainerQueryList"] = new IdlDefinition
        {
            Name = "ContainerQueryList",
            Kind = IdlDefinitionKind.Interface,
            Inheritance = "EventTarget",
            ExtAttrs = [new IdlExtendedAttribute { Name = "Exposed", Value = "Window" }],
            Members =
            [
                Attribute("query", "CSSOMString", readOnly: true),
                Attribute("matches", "boolean", readOnly: true),
                Attribute("onchange", "EventHandler", readOnly: false),
            ],
            Source = Source,
            SpecUrl = "https://drafts.csswg.org/css-conditional-5/",
            ExperimentalApi = "MatchContainer",
        };
    }

    /// <summary>
    /// The rangeStart and rangeEnd attributes of Animation (https://drafts.csswg.org/web-animations-2/#the-animation-interface):
    /// the editor's draft defines them in the prose of the partial interface, but not the IDL of webref yet:
    /// <code>
    /// partial interface Animation {
    ///   attribute (TimelineRangeOffset or CSSNumericValue or CSSKeywordValue or DOMString) rangeStart;
    ///   attribute (TimelineRangeOffset or CSSNumericValue or CSSKeywordValue or DOMString) rangeEnd;
    /// };
    /// </code>
    /// </summary>
    private static void AddAnimationRanges(IdlModel model)
    {
        if (!model.Definitions.TryGetValue("Animation", out var animation) || animation.Members.Exists(m => m.Name == "rangeStart"))
            return;

        foreach (var name in (ReadOnlySpan<string>)["rangeStart", "rangeEnd"])
        {
            animation.Members.Add(new IdlMember
            {
                Kind = IdlMemberKind.Attribute,
                Name = name,
                Type = new IdlType
                {
                    Union = true,
                    Subtypes = [new IdlType { Name = "TimelineRangeOffset" }, new IdlType { Name = "CSSNumericValue" }, new IdlType { Name = "CSSKeywordValue" }, new IdlType { Name = "DOMString" }],
                },
                Href = "https://drafts.csswg.org/web-animations-2/#dom-animation-" + name.ToLowerInvariant(),
                Source = "web-animations-2",
                DeclaringDefinition = animation.Name,
            });
        }
    }

    /// <summary>
    /// The body of the reports generated by the Generate Test Report WebDriver command (https://w3c.github.io/reporting/#generate-test-report-command),
    /// which the specification describes as an object with a single string member (named <c>body_message</c>, but
    /// <c>message</c> in the browsers and the tests):
    /// <code>dictionary TestReportBody : ReportBody { DOMString message; };</code>
    /// </summary>
    private static void AddTestReportBody(IdlModel model)
    {
        if (!model.Definitions.ContainsKey("ReportBody") || model.Definitions.ContainsKey("TestReportBody"))
            return;

        model.Definitions["TestReportBody"] = new IdlDefinition
        {
            Name = "TestReportBody",
            Kind = IdlDefinitionKind.Dictionary,
            Inheritance = "ReportBody",
            Members =
            [
                new IdlMember
                {
                    Kind = IdlMemberKind.Field,
                    Name = "message",
                    Type = new IdlType { Name = "DOMString" },
                    Source = "reporting",
                    DeclaringDefinition = "TestReportBody",
                },
            ],
            Source = "reporting",
            SpecUrl = "https://w3c.github.io/reporting/",
        };
    }

    /// <summary>
    /// The IDL of the CSS Parser API (https://wicg.github.io/css-parser-api/) is an unfinished draft whose types contradict
    /// its own model and CSS Syntax (https://drafts.csswg.org/css-syntax-3/): the preludes, the values of the declarations and
    /// the contents of the blocks and the functions are lists of component values, which contain tokens (strings and
    /// CSSStyleValue objects, the <c>CSSToken</c> typedef), not only <c>CSSParserValue</c> objects; the constructor of
    /// <c>CSSParserDeclaration</c> takes rules instead of values; and <c>atRules</c> is an untyped <c>object</c> described as a
    /// "dict of at-rule name => at-rule type". The types become <c>FrozenArray&lt;CSSToken&gt;</c>, <c>sequence&lt;CSSToken&gt;</c>
    /// and <c>record&lt;DOMString, DOMString&gt;</c>; the names and the number of arguments are unchanged.
    /// </summary>
    private static void FixCssParserApiTypes(IdlModel model)
    {
        foreach (var name in (ReadOnlySpan<string>)["CSSParserAtRule", "CSSParserQualifiedRule", "CSSParserDeclaration", "CSSParserBlock", "CSSParserFunction"])
        {
            if (!model.Definitions.TryGetValue(name, out var definition))
                continue;

            foreach (var member in definition.Members)
            {
                if (member.Type is { } type)
                {
                    ReplaceTypeName(type, "CSSParserValue", "CSSToken");
                }

                foreach (var argument in member.Arguments)
                {
                    ReplaceTypeName(argument.Type, "CSSParserValue", "CSSToken");
                    if (name == "CSSParserDeclaration" && argument.Name == "body")
                    {
                        ReplaceTypeName(argument.Type, "CSSParserRule", "CSSToken");
                    }
                }
            }
        }

        if (model.Definitions.TryGetValue("CSSParserOptions", out var options))
        {
            var index = options.Members.FindIndex(m => m is { Kind: IdlMemberKind.Field, Name: "atRules", Type.Name: "object" });
            if (index >= 0)
            {
                var atRules = options.Members[index];
                options.Members[index] = new IdlMember
                {
                    Kind = IdlMemberKind.Field,
                    Name = atRules.Name,
                    Type = new IdlType { Generic = "record", Subtypes = [new IdlType { Name = "DOMString" }, new IdlType { Name = "DOMString" }] },
                    ExtAttrs = atRules.ExtAttrs,
                    Href = atRules.Href,
                    Source = atRules.Source,
                    DeclaringDefinition = atRules.DeclaringDefinition,
                };
            }
        }

        static void ReplaceTypeName(IdlType type, string name, string replacement)
        {
            if (type.Name == name)
            {
                type.Name = replacement;
            }

            foreach (var subtype in type.Subtypes)
            {
                ReplaceTypeName(subtype, name, replacement);
            }
        }
    }

    /// <summary>
    /// The CSSPrivateRule interface of the @private rule of CSS Mixins (https://github.com/w3c/csswg-drafts/issues/14243#issuecomment-5180782117),
    /// used by the tentative css-mixins tests, which the specification does not define yet:
    /// <code>[Exposed=Window] interface CSSPrivateRule : CSSRule { };</code>
    /// </summary>
    private static void AddCssPrivateRule(IdlModel model)
    {
        if (!model.Definitions.ContainsKey("CSSRule") || model.Definitions.ContainsKey("CSSPrivateRule"))
            return;

        model.Definitions["CSSPrivateRule"] = new IdlDefinition
        {
            Name = "CSSPrivateRule",
            Kind = IdlDefinitionKind.Interface,
            Inheritance = "CSSRule",
            ExtAttrs = [new IdlExtendedAttribute { Name = "Exposed", Value = "Window" }],
            Members = [],
            Source = "css-mixins-1",
            SpecUrl = "https://drafts.csswg.org/css-mixins-1/",
            ExperimentalApi = "CssMixinResultAndPrivateRules",
        };
    }

    /// <summary>
    /// The width attribute of FontFace and the width member of FontFaceDescriptors (https://drafts.csswg.org/css-font-loading/#fontface-interface):
    /// stretch is a legacy name alias of width. The editor's draft defines them, but not the IDL of webref yet.
    /// </summary>
    private static void AddFontFaceWidth(IdlModel model)
    {
        if (model.Definitions.TryGetValue("FontFace", out var fontFace) && !fontFace.Members.Exists(m => m.Name == "width"))
        {
            fontFace.Members.Add(new IdlMember
            {
                Kind = IdlMemberKind.Attribute,
                Name = "width",
                Type = new IdlType { Name = "CSSOMString" },
                Source = "css-font-loading-3",
                DeclaringDefinition = fontFace.Name,
            });
        }

        if (model.Definitions.TryGetValue("FontFaceDescriptors", out var descriptors) && !descriptors.Members.Exists(m => m.Name == "width"))
        {
            descriptors.Members.Add(new IdlMember
            {
                Kind = IdlMemberKind.Field,
                Name = "width",
                Type = new IdlType { Name = "CSSOMString" },
                Source = "css-font-loading-3",
                DeclaringDefinition = descriptors.Name,
            });
        }
    }

    /// <summary>
    /// The <c>pseudoTarget</c> of the UI events proposed in https://github.com/w3c/uievents/pull/413 (resolved in
    /// https://github.com/w3c/csswg-drafts/issues/12163, implemented by Blink and used by the css-pseudo and uievents tests):
    /// <code>
    /// partial interface UIEvent { readonly attribute CSSPseudoElement? pseudoTarget; };
    /// partial dictionary UIEventInit { CSSPseudoElement? pseudoTarget = null; };
    /// </code>
    /// </summary>
    private static void AddUIEventPseudoTarget(IdlModel model)
    {
        if (model.Definitions.TryGetValue("UIEvent", out var uiEvent) && !uiEvent.Members.Exists(m => m.Name == "pseudoTarget"))
        {
            uiEvent.Members.Add(new IdlMember
            {
                Kind = IdlMemberKind.Attribute,
                Name = "pseudoTarget",
                Type = new IdlType { Name = "CSSPseudoElement", Nullable = true },
                Readonly = true,
                Href = "https://w3c.github.io/uievents/#dom-uievent-pseudotarget",
                Source = "uievents",
                DeclaringDefinition = uiEvent.Name,
                ExperimentalApi = "UIEventPseudoTarget",
            });
        }

        if (model.Definitions.TryGetValue("UIEventInit", out var init) && !init.Members.Exists(m => m.Name == "pseudoTarget"))
        {
            init.Members.Add(new IdlMember
            {
                Kind = IdlMemberKind.Field,
                Name = "pseudoTarget",
                Type = new IdlType { Name = "CSSPseudoElement", Nullable = true },
                Href = "https://w3c.github.io/uievents/#dom-uieventinit-pseudotarget",
                Source = "uievents",
                DeclaringDefinition = init.Name,
                ExperimentalApi = "UIEventPseudoTarget",
            });
        }
    }

    /// <summary>
    /// CSSOM View redefines the coordinates of MouseEvent and MouseEventInit as double
    /// (https://drafts.csswg.org/cssom-view/#extensions-to-the-mouseevent-interface: "partial dictionary MouseEventInit { double
    /// screenX = 0.0; ... }"), in partial definitions that webref does not include, as they redefine members of UI Events. The
    /// pointer events have fractional coordinates (https://w3c.github.io/pointerevents/#event-coordinates).
    /// </summary>
    private static void SetMouseEventCoordinateTypes(IdlModel model)
    {
        foreach (var name in (ReadOnlySpan<string>)["MouseEvent", "MouseEventInit"])
        {
            if (!model.Definitions.TryGetValue(name, out var definition))
                continue;

            for (var i = 0; i < definition.Members.Count; i++)
            {
                var member = definition.Members[i];
                if (member.Name is not ("screenX" or "screenY" or "clientX" or "clientY") || member.Kind is not (IdlMemberKind.Attribute or IdlMemberKind.Field) || member.Type is not { Name: "long" })
                    continue;

                definition.Members[i] = new IdlMember
                {
                    Kind = member.Kind,
                    Name = member.Name,
                    Type = new IdlType { Name = "double" },
                    Readonly = member.Readonly,
                    Default = member.Default,
                    ExtAttrs = member.ExtAttrs,
                    Href = member.Href,
                    Source = member.Source,
                    DeclaringDefinition = member.DeclaringDefinition,
                };
            }
        }
    }

    /// <summary>
    /// The Trusted Types integration of the execCommand specification (https://w3c.github.io/editing/docs/execCommand/#execcommand())
    /// makes the value of <c>Document.execCommand()</c> a <c>(TrustedHTML or DOMString)</c> (the value of the
    /// <c>insertHTML</c> command is a sink); the IDL of HTML extracted by webref still declares a <c>DOMString</c>.
    /// </summary>
    private static void SetExecCommandValueType(IdlModel model)
    {
        if (!model.Definitions.TryGetValue("Document", out var definition))
            return;

        foreach (var member in definition.Members)
        {
            if (member is { Kind: IdlMemberKind.Operation, Name: "execCommand", Arguments: [_, _, { Type: { Union: false, Name: "DOMString" } } value] })
            {
                value.Type = new IdlType { Union = true, Subtypes = [new IdlType { Name = "TrustedHTML" }, new IdlType { Name = "DOMString" }] };
            }
        }
    }

    /// <summary>
    /// The SVG Paths specification defines SVGPathSegment as an interface without interface object, whose objects cannot be
    /// created by scripts, but setPathData() takes a sequence of SVGPathSegment. The browsers (and the web-platform-tests) use a
    /// dictionary: <c>dictionary SVGPathSegment { required DOMString type; required sequence&lt;unrestricted float&gt; values; }</c>,
    /// so that setPathData() accepts plain objects and validates the values itself.
    /// </summary>
    private static void ReplaceSvgPathSegmentInterfaceWithDictionary(IdlModel model)
    {
        if (!model.Definitions.TryGetValue("SVGPathSegment", out var definition) || definition.Kind != IdlDefinitionKind.Interface)
            return;

        IdlMember Field(string name, IdlType type) => new()
        {
            Kind = IdlMemberKind.Field,
            Name = name,
            Type = type,
            Required = true,
            Source = definition.Source,
            DeclaringDefinition = definition.Name,
        };

        model.Definitions[definition.Name] = new IdlDefinition
        {
            Name = definition.Name,
            Kind = IdlDefinitionKind.Dictionary,
            Members =
            [
                Field("type", new IdlType { Name = "DOMString" }),
                Field("values", new IdlType { Generic = "sequence", Subtypes = [new IdlType { Name = "unrestricted float" }] }),
            ],
            Source = definition.Source,
            Href = definition.Href,
            SpecUrl = definition.SpecUrl,
        };
    }

    private static IdlDefinition? ParseDefinition(JsonElement element)
    {
        var kind = element.GetProperty("type").GetString() switch
        {
            "interface" => IdlDefinitionKind.Interface,
            "interface mixin" => IdlDefinitionKind.InterfaceMixin,
            "callback interface" => IdlDefinitionKind.CallbackInterface,
            "namespace" => IdlDefinitionKind.Namespace,
            "dictionary" => IdlDefinitionKind.Dictionary,
            "enum" => IdlDefinitionKind.Enum,
            "typedef" => IdlDefinitionKind.Typedef,
            "callback" => IdlDefinitionKind.Callback,
            _ => (IdlDefinitionKind?)null,
        };

        if (kind is null)
            return null;

        var definition = new IdlDefinition
        {
            Name = element.GetProperty("name").GetString()!,
            Kind = kind.Value,
            Inheritance = element.TryGetProperty("inheritance", out var inheritance) && inheritance.ValueKind == JsonValueKind.String ? inheritance.GetString() : null,
            ExtAttrs = ParseExtAttrs(element),
            Type = element.TryGetProperty("idlType", out var idlType) && idlType.ValueKind == JsonValueKind.Object ? ParseType(idlType) : null,
            Arguments = ParseArguments(element),
            Href = element.TryGetProperty("href", out var href) && href.ValueKind == JsonValueKind.String ? href.GetString() : null,
        };

        if (element.TryGetProperty("members", out var members))
        {
            foreach (var member in members.EnumerateArray())
            {
                if (ParseMember(member) is { } parsed)
                {
                    parsed.DeclaringDefinition = definition.Name;
                    definition.Members.Add(parsed);
                }
            }
        }

        if (kind == IdlDefinitionKind.Enum)
        {
            foreach (var value in element.GetProperty("values").EnumerateArray())
            {
                definition.EnumValues.Add(value.GetProperty("value").GetString()!);
            }
        }

        return definition;
    }

    private static IdlMember? ParseMember(JsonElement element)
    {
        var kind = element.GetProperty("type").GetString() switch
        {
            "attribute" => IdlMemberKind.Attribute,
            "operation" => IdlMemberKind.Operation,
            "const" => IdlMemberKind.Const,
            "constructor" => IdlMemberKind.Constructor,
            "iterable" => IdlMemberKind.Iterable,
            "async_iterable" => IdlMemberKind.AsyncIterable,
            "maplike" => IdlMemberKind.Maplike,
            "setlike" => IdlMemberKind.Setlike,
            "field" => IdlMemberKind.Field,
            _ => (IdlMemberKind?)null,
        };

        if (kind is null)
            return null;

        var member = new IdlMember
        {
            Kind = kind.Value,
            Name = element.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString()! : "",
            Type = element.TryGetProperty("idlType", out var idlType) && idlType.ValueKind == JsonValueKind.Object ? ParseType(idlType) : null,
            DeclarationTypes = idlType.ValueKind == JsonValueKind.Array ? idlType.EnumerateArray().Select(ParseType).ToList() : [],
            Arguments = ParseArguments(element),
            Readonly = element.TryGetProperty("readonly", out var ro) && ro.ValueKind == JsonValueKind.True,
            Special = element.TryGetProperty("special", out var special) && special.ValueKind == JsonValueKind.String ? special.GetString()! : "",
            Required = element.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.True,
            Default = element.TryGetProperty("default", out var def) && def.ValueKind == JsonValueKind.Object ? def.Clone() : null,
            ConstValue = element.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Object ? value.Clone() : null,
            ExtAttrs = ParseExtAttrs(element),
            Href = element.TryGetProperty("href", out var href) && href.ValueKind == JsonValueKind.String ? href.GetString() : null,
        };

        return member;
    }

    private static List<IdlArgument> ParseArguments(JsonElement element)
    {
        var result = new List<IdlArgument>();
        if (element.TryGetProperty("arguments", out var arguments) && arguments.ValueKind == JsonValueKind.Array)
        {
            foreach (var argument in arguments.EnumerateArray())
            {
                result.Add(new IdlArgument
                {
                    Name = argument.GetProperty("name").GetString()!,
                    Type = ParseType(argument.GetProperty("idlType")),
                    Optional = argument.TryGetProperty("optional", out var optional) && optional.ValueKind == JsonValueKind.True,
                    Variadic = argument.TryGetProperty("variadic", out var variadic) && variadic.ValueKind == JsonValueKind.True,
                    Default = argument.TryGetProperty("default", out var def) && def.ValueKind == JsonValueKind.Object ? def.Clone() : null,
                    ExtAttrs = ParseExtAttrs(argument),
                });
            }
        }

        return result;
    }

    private static IdlType ParseType(JsonElement element)
    {
        var generic = element.TryGetProperty("generic", out var g) ? g.GetString() ?? "" : "";
        var union = element.TryGetProperty("union", out var u) && u.GetBoolean();
        var inner = element.GetProperty("idlType");
        var type = new IdlType
        {
            Generic = generic,
            Union = union,
            Nullable = element.TryGetProperty("nullable", out var n) && n.GetBoolean(),
            ExtAttrs = ParseExtAttrs(element),
        };

        if (inner.ValueKind == JsonValueKind.String)
        {
            type.Name = inner.GetString();
        }
        else if (inner.ValueKind == JsonValueKind.Array)
        {
            type.Subtypes.AddRange(inner.EnumerateArray().Select(ParseType));
        }
        else if (inner.ValueKind == JsonValueKind.Object)
        {
            type.Subtypes.Add(ParseType(inner));
        }

        return type;
    }

    private static List<IdlExtendedAttribute> ParseExtAttrs(JsonElement element)
    {
        var result = new List<IdlExtendedAttribute>();
        if (element.TryGetProperty("extAttrs", out var extAttrs) && extAttrs.ValueKind == JsonValueKind.Array)
        {
            foreach (var extAttr in extAttrs.EnumerateArray())
            {
                string? value = null;
                var values = new List<string>();
                if (extAttr.TryGetProperty("rhs", out var rhs) && rhs.ValueKind == JsonValueKind.Object && rhs.TryGetProperty("value", out var rhsValue))
                {
                    if (rhsValue.ValueKind == JsonValueKind.String)
                    {
                        value = rhsValue.GetString();
                    }
                    else if (rhsValue.ValueKind == JsonValueKind.Array)
                    {
                        values.AddRange(rhsValue.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.Object && v.TryGetProperty("value", out _)).Select(v => v.GetProperty("value").GetString()!));
                    }
                    else if (rhs.TryGetProperty("type", out var rhsType) && rhsType.GetString() == "*")
                    {
                        // [Exposed=*] (https://webidl.spec.whatwg.org/#Exposed)
                        value = "*";
                    }
                }

                result.Add(new IdlExtendedAttribute { Name = extAttr.GetProperty("name").GetString()!, Value = value, Values = values });
            }
        }

        return result;
    }
}
