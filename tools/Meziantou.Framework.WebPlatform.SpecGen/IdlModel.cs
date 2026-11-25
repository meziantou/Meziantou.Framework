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
        AddWindowFind(model);
        AddMouseEventFromToElement(model);
        AddTrustedHtmlParserOptions(model);
        AddFontFaceStretch(model);
        AddFontFaceDescriptorsSizeAdjust(model);
        AddUIEventPseudoTarget(model);
        AddContainerQueryListChangeEvent(model);
        MakeScreenAnEventTarget(model);
        AddPaintWorkletExtensions(model);
        AddMediaSourceHandleToMediaProvider(model);
        AddRequestStorageAccessTypes(model);
        AddLayoutAndAnimationWorkletExtensions(model);
        AddContentVisibilityAutoStateChangeEventHandler(model);
        RenameSnapEventHandlers(model);
        AddCssPrivateRule(model);
        AddTestReportBody(model);
        AddAnimationRanges(model);
        AddUrlPatternTentativeMethods(model);
        AddLongTasks(model);
        AddScrollTiming(model);
        AddResourceTimingInitiatorUrl(model);
        ReplaceSvgPathSegmentInterfaceWithDictionary(model);
        RemoveSvgPathElementGeometryMembers(model);
        FixCssParserApiTypes(model);
        SetMediaMetadataArtworkType(model);
        AddIFrameElementPolicy(model);
        AddSvgScriptElementAsync(model);
        AddDocumentPolicyViolationReportBody(model);
        AddCanvasLayers(model);
        AddCanvasFilterObjects(model);
        AddCanvasTextClusters(model);
        AddCanvasShapedText(model);
        AddCanvasGradientInterpolation(model);
        AddWebMcpPullRequests(model);
        AddHtmlInCanvas(model);
        AddFedCmNonce(model);
        AddInsertableStreamsExtensions(model);
        AddWebSocketStream(model);
        return model;
    }

    /// <summary>
    /// The exposure of the tracks in the dedicated workers, and the legacy API of the insertable streams of Chromium, which the
    /// web-platform-tests of <c>mediacapture-insertable-streams</c> (<c>legacy/</c> and <c>tentative/</c>) test:
    /// <list type="bullet">
    /// <item>The empty partial interfaces of https://w3c.github.io/mediacapture-extensions/ that expose MediaStream,
    /// MediaStreamTrack, MediaStreamTrackEvent and OverconstrainedError in the dedicated workers (e.g.
    /// <c>[Exposed=(Window,DedicatedWorker), Transferable] partial interface MediaStreamTrack {}</c>) apply to the interfaces
    /// (the tracks can be transferred to the dedicated workers), not only to the members of the partial interfaces.</item>
    /// <item>MediaStreamTrackProcessor and VideoTrackGenerator are also exposed in the windows, and MediaStreamTrackProcessor
    /// also has the constructor of Chromium: <c>constructor(MediaStreamTrack track, optional unsigned short maxBufferSize)</c>.</item>
    /// <item>The MediaStreamTrackGenerator interface of Chromium (https://github.com/w3c/mediacapture-transform/blob/main/explainer.md):
    /// <c>[Exposed=(Window,DedicatedWorker)] interface MediaStreamTrackGenerator : MediaStreamTrack { constructor(DOMString kind);
    /// constructor(MediaStreamTrackGeneratorInit init); readonly attribute WritableStream writable; };
    /// dictionary MediaStreamTrackGeneratorInit { required DOMString kind; };</c></item>
    /// </list>
    /// </summary>
    private static void AddInsertableStreamsExtensions(IdlModel model)
    {
        const string Href = "https://github.com/w3c/mediacapture-transform/blob/main/explainer.md#mediastreamtrackgenerator";
        const string ProcessorHref = "https://github.com/w3c/mediacapture-transform/blob/main/explainer.md#mediastreamtrackprocessor";
        if (!model.Definitions.TryGetValue("MediaStreamTrack", out var track) || !model.Definitions.TryGetValue("MediaStreamTrackProcessor", out var processor)
            || model.Definitions.ContainsKey("MediaStreamTrackGenerator"))
        {
            return;
        }

        foreach (var name in (string[])["MediaStream", "MediaStreamTrack", "MediaStreamTrackEvent", "OverconstrainedError", "MediaStreamTrackProcessor", "VideoTrackGenerator"])
        {
            if (!model.Definitions.TryGetValue(name, out var definition))
                continue;

            definition.ExtAttrs.RemoveAll(a => a.Name == "Exposed");
            definition.ExtAttrs.Add(new IdlExtendedAttribute { Name = "Exposed", Values = ["Window", "DedicatedWorker"] });
        }

        if (!track.HasExtAttr("Transferable"))
        {
            track.ExtAttrs.Add(new IdlExtendedAttribute { Name = "Transferable" });
        }

        processor.Members.Add(new IdlMember
        {
            Kind = IdlMemberKind.Constructor,
            Arguments =
            [
                new IdlArgument { Name = "track", Type = new IdlType { Name = "MediaStreamTrack" } },
                new IdlArgument { Name = "maxBufferSize", Type = new IdlType { Name = "unsigned short" }, Optional = true },
            ],
            Source = processor.Source,
            DeclaringDefinition = "MediaStreamTrackProcessor",
            Href = ProcessorHref,
        });

        model.Definitions["MediaStreamTrackGeneratorInit"] = new IdlDefinition
        {
            Name = "MediaStreamTrackGeneratorInit",
            Kind = IdlDefinitionKind.Dictionary,
            Members =
            [
                new IdlMember
                {
                    Kind = IdlMemberKind.Field,
                    Name = "kind",
                    Type = new IdlType { Name = "DOMString" },
                    Required = true,
                    Source = processor.Source,
                    DeclaringDefinition = "MediaStreamTrackGeneratorInit",
                    Href = Href,
                },
            ],
            Source = processor.Source,
            Href = Href,
            SpecUrl = Href,
        };

        model.Definitions["MediaStreamTrackGenerator"] = new IdlDefinition
        {
            Name = "MediaStreamTrackGenerator",
            Kind = IdlDefinitionKind.Interface,
            Inheritance = "MediaStreamTrack",
            Members =
            [
                new IdlMember
                {
                    Kind = IdlMemberKind.Constructor,
                    Arguments = [new IdlArgument { Name = "kind", Type = new IdlType { Name = "DOMString" } }],
                    Source = processor.Source,
                    DeclaringDefinition = "MediaStreamTrackGenerator",
                    Href = Href,
                },
                new IdlMember
                {
                    Kind = IdlMemberKind.Constructor,
                    Arguments = [new IdlArgument { Name = "init", Type = new IdlType { Name = "MediaStreamTrackGeneratorInit" } }],
                    Source = processor.Source,
                    DeclaringDefinition = "MediaStreamTrackGenerator",
                    Href = Href,
                },
                new IdlMember
                {
                    Kind = IdlMemberKind.Attribute,
                    Name = "writable",
                    Type = new IdlType { Name = "WritableStream" },
                    Readonly = true,
                    Source = processor.Source,
                    DeclaringDefinition = "MediaStreamTrackGenerator",
                    Href = Href,
                },
            ],
            ExtAttrs = [new IdlExtendedAttribute { Name = "Exposed", Values = ["Window", "DedicatedWorker"] }],
            Source = processor.Source,
            Href = Href,
            SpecUrl = Href,
        };
    }

    /// <summary>
    /// <c>WebSocketStream</c> and <c>WebSocketError</c>, proposed for the WebSockets Standard
    /// (https://github.com/whatwg/websockets/pull/48), which the tentative web-platform-tests of <c>websockets/stream</c> test:
    /// <code>
    /// dictionary WebSocketOpenInfo { ReadableStream readable; WritableStream writable; DOMString extensions; DOMString protocol; };
    /// dictionary WebSocketCloseInfo { [EnforceRange] unsigned short closeCode; USVString reason = ""; };
    /// dictionary WebSocketStreamOptions { sequence&lt;USVString&gt; protocols; AbortSignal signal; };
    /// [Exposed=(Window,Worker)] interface WebSocketStream {
    ///   constructor(USVString url, optional WebSocketStreamOptions options = {});
    ///   readonly attribute USVString url;
    ///   readonly attribute Promise&lt;WebSocketOpenInfo&gt; opened;
    ///   readonly attribute Promise&lt;WebSocketCloseInfo&gt; closed;
    ///   undefined close(optional WebSocketCloseInfo closeInfo = {});
    /// };
    /// [Exposed=(Window,Worker)] interface WebSocketError : DOMException {
    ///   constructor(optional DOMString message = "", optional WebSocketCloseInfo init = {});
    ///   readonly attribute unsigned short? closeCode;
    ///   readonly attribute USVString reason;
    /// };
    /// </code>
    /// </summary>
    private static void AddWebSocketStream(IdlModel model)
    {
        const string SpecUrl = "https://whatpr.org/websockets/48.html";
        const string Experimental = "WebSocketStream";
        if (!model.Definitions.TryGetValue("WebSocket", out var webSocket) || model.Definitions.ContainsKey("WebSocketStream"))
            return;

        var source = webSocket.Source;
        using var emptyDictionary = JsonDocument.Parse("""{"type":"dictionary"}""");
        using var emptyString = JsonDocument.Parse("""{"type":"string","value":""}""");
        var emptyDictionaryDefault = emptyDictionary.RootElement.Clone();
        var emptyStringDefault = emptyString.RootElement.Clone();
        var exposed = new IdlExtendedAttribute { Name = "Exposed", Values = ["Window", "Worker"] };

        IdlType Type(string name, bool nullable = false, List<IdlExtendedAttribute>? extAttrs = null) => new() { Name = name, Nullable = nullable, ExtAttrs = extAttrs ?? [] };
        IdlType Promise(string name) => new() { Generic = "Promise", Subtypes = [Type(name)] };
        IdlMember Field(string declaring, string name, IdlType type, JsonElement? defaultValue = null) => new()
        {
            Kind = IdlMemberKind.Field,
            Name = name,
            Type = type,
            Default = defaultValue,
            Source = source,
            DeclaringDefinition = declaring,
            Href = SpecUrl + "#dom-" + declaring.ToLowerInvariant() + "-" + name.ToLowerInvariant(),
        };
        IdlMember Attribute(string declaring, string name, IdlType type) => new()
        {
            Kind = IdlMemberKind.Attribute,
            Name = name,
            Type = type,
            Readonly = true,
            Source = source,
            DeclaringDefinition = declaring,
            Href = SpecUrl + "#dom-" + declaring.ToLowerInvariant() + "-" + name.ToLowerInvariant(),
        };
        IdlMember Constructor(string declaring, List<IdlArgument> arguments) => new()
        {
            Kind = IdlMemberKind.Constructor,
            Arguments = arguments,
            Source = source,
            DeclaringDefinition = declaring,
            Href = SpecUrl + "#dom-" + declaring.ToLowerInvariant() + "-" + declaring.ToLowerInvariant(),
        };
        void AddDefinition(string name, IdlDefinitionKind kind, List<IdlMember> members, string? inheritance = null, List<IdlExtendedAttribute>? extAttrs = null)
        {
            model.Definitions[name] = new IdlDefinition
            {
                Name = name,
                Kind = kind,
                Inheritance = inheritance,
                Members = members,
                ExtAttrs = extAttrs ?? [],
                Href = SpecUrl + "#" + (kind == IdlDefinitionKind.Dictionary ? "dictdef-" : "") + name.ToLowerInvariant(),
                Source = source,
                SpecUrl = SpecUrl,
                ExperimentalApi = Experimental,
            };
        }

        AddDefinition("WebSocketOpenInfo", IdlDefinitionKind.Dictionary,
        [
            Field("WebSocketOpenInfo", "readable", Type("ReadableStream")),
            Field("WebSocketOpenInfo", "writable", Type("WritableStream")),
            Field("WebSocketOpenInfo", "extensions", Type("DOMString")),
            Field("WebSocketOpenInfo", "protocol", Type("DOMString")),
        ]);
        AddDefinition("WebSocketCloseInfo", IdlDefinitionKind.Dictionary,
        [
            Field("WebSocketCloseInfo", "closeCode", Type("unsigned short", extAttrs: [new IdlExtendedAttribute { Name = "EnforceRange" }])),
            Field("WebSocketCloseInfo", "reason", Type("USVString"), emptyStringDefault),
        ]);
        AddDefinition("WebSocketStreamOptions", IdlDefinitionKind.Dictionary,
        [
            Field("WebSocketStreamOptions", "protocols", new IdlType { Generic = "sequence", Subtypes = [Type("USVString")] }),
            Field("WebSocketStreamOptions", "signal", Type("AbortSignal")),
        ]);
        AddDefinition("WebSocketStream", IdlDefinitionKind.Interface,
        [
            Constructor("WebSocketStream", [new IdlArgument { Name = "url", Type = Type("USVString") }, new IdlArgument { Name = "options", Type = Type("WebSocketStreamOptions"), Optional = true, Default = emptyDictionaryDefault }]),
            Attribute("WebSocketStream", "url", Type("USVString")),
            Attribute("WebSocketStream", "opened", Promise("WebSocketOpenInfo")),
            Attribute("WebSocketStream", "closed", Promise("WebSocketCloseInfo")),
            new IdlMember
            {
                Kind = IdlMemberKind.Operation,
                Name = "close",
                Type = Type("undefined"),
                Arguments = [new IdlArgument { Name = "closeInfo", Type = Type("WebSocketCloseInfo"), Optional = true, Default = emptyDictionaryDefault }],
                Source = source,
                DeclaringDefinition = "WebSocketStream",
                Href = SpecUrl + "#dom-websocketstream-close",
            },
        ], extAttrs: [exposed]);
        AddDefinition("WebSocketError", IdlDefinitionKind.Interface,
        [
            Constructor("WebSocketError", [new IdlArgument { Name = "message", Type = Type("DOMString"), Optional = true, Default = emptyStringDefault }, new IdlArgument { Name = "init", Type = Type("WebSocketCloseInfo"), Optional = true, Default = emptyDictionaryDefault }]),
            Attribute("WebSocketError", "closeCode", Type("unsigned short", nullable: true)),
            Attribute("WebSocketError", "reason", Type("USVString")),
        ], inheritance: "DOMException", extAttrs: [exposed]);
    }

    /// <summary>
    /// The deprecated <c>nonce</c> member of the FedCM requests, which the specification moved to the <c>params</c> member
    /// (https://github.com/w3c-fedid/FedCM/pull/697), but which the browsers still send to the identity assertion endpoint and
    /// the web-platform-tests of <c>fedcm</c> use:
    /// <code>
    /// partial dictionary IdentityProviderRequestOptions { DOMString nonce; };
    /// </code>
    /// </summary>
    private static void AddFedCmNonce(IdlModel model)
    {
        if (!model.Definitions.TryGetValue("IdentityProviderRequestOptions", out var options) || options.Members.Exists(m => m.Name == "nonce"))
            return;

        options.Members.Add(new IdlMember
        {
            Kind = IdlMemberKind.Field,
            Name = "nonce",
            Type = new IdlType { Name = "DOMString" },
            Href = "https://w3c-fedid.github.io/FedCM/#dom-identityproviderrequestoptions-nonce",
            Source = "fedcm",
            DeclaringDefinition = options.Name,
        });
    }

    /// <summary>
    /// The HTML-in-canvas proposal (https://github.com/WICG/html-in-canvas), which the tentative web-platform-tests of
    /// html/canvas/element/manual/draw-element-image test (the WebGL and WebGPU members are not included: the library has no
    /// WebGL nor WebGPU context, and <c>ElementImage</c> is not transferable):
    /// <code>
    /// dictionary UpdateElementGeometryOptions { boolean preserveHitTestOrder = false; DOMRectInit canvasClip; DOMMatrixInit canvasTransform; };
    /// dictionary ElementImageDefaultSize { double width; double height; };
    /// partial interface HTMLCanvasElement {
    ///   [CEReactions, Reflect] attribute DOMString content;
    ///   attribute EventHandler onpaint;
    ///   undefined requestPaint();
    ///   ElementImage captureElementImage(Element element);
    ///   undefined updateElementGeometry((Element or ElementImage) element, optional UpdateElementGeometryOptions options = {});
    ///   undefined clearElementGeometry((Element or ElementImage) element);
    ///   [NewObject] DOMMatrix getElementTransform(Element element);
    ///   attribute EventHandler onelementgeometryupdate;
    ///   ElementImageDefaultSize getElementImageDefaultSize((Element or ElementImage) element);
    /// };
    /// partial interface OffscreenCanvas {
    ///   undefined updateElementGeometry((Element or ElementImage) element, optional UpdateElementGeometryOptions options = {});
    ///   undefined clearElementGeometry((Element or ElementImage) element);
    ///   ElementImageDefaultSize getElementImageDefaultSize((Element or ElementImage) element);
    /// };
    /// dictionary DrawElementImageOptions { boolean preserveElementGeometry = false; };
    /// interface mixin CanvasDrawElementImage {
    ///   undefined drawElementImage((Element or ElementImage) element, unrestricted double dx, unrestricted double dy, optional DrawElementImageOptions options = {});
    ///   undefined drawElementImage((Element or ElementImage) element, unrestricted double dx, unrestricted double dy, unrestricted double dwidth, unrestricted double dheight, optional DrawElementImageOptions options = {});
    ///   undefined drawElementImage((Element or ElementImage) element, unrestricted double sx, unrestricted double sy, unrestricted double swidth, unrestricted double sheight, unrestricted double dx, unrestricted double dy, optional DrawElementImageOptions options = {});
    ///   undefined drawElementImage((Element or ElementImage) element, unrestricted double sx, unrestricted double sy, unrestricted double swidth, unrestricted double sheight, unrestricted double dx, unrestricted double dy, unrestricted double dwidth, unrestricted double dheight, optional DrawElementImageOptions options = {});
    /// };
    /// CanvasRenderingContext2D includes CanvasDrawElementImage;
    /// OffscreenCanvasRenderingContext2D includes CanvasDrawElementImage;
    /// [Exposed=Window] interface CanvasPaintEvent : Event {
    ///   constructor(DOMString type, optional CanvasPaintEventInit eventInitDict = {});
    ///   [SameObject] readonly attribute FrozenArray&lt;Element&gt; changedElements;
    /// };
    /// dictionary CanvasPaintEventInit : EventInit { sequence&lt;Element&gt; changedElements = []; };
    /// [Exposed=(Window,Worker)] interface ElementImage { undefined close(); };
    /// [Exposed=Window] interface ElementGeometryUpdateEvent : Event {
    ///   constructor(DOMString type, optional ElementGeometryUpdateEventInit eventInitDict = {});
    ///   [SameObject] readonly attribute FrozenArray&lt;Element&gt; elements;
    /// };
    /// dictionary ElementGeometryUpdateEventInit : EventInit { sequence&lt;Element&gt; elements = []; };
    /// </code>
    /// </summary>
    private static void AddHtmlInCanvas(IdlModel model)
    {
        if (!model.Definitions.TryGetValue("HTMLCanvasElement", out var canvas) || model.Definitions.ContainsKey("ElementImage"))
            return;

        const string Href = "https://github.com/WICG/html-in-canvas#idl-changes";
        const string Experimental = "HtmlInCanvas";
        var source = canvas.Source;
        using var emptyDictionary = JsonDocument.Parse("""{"type":"dictionary"}""");
        using var emptySequence = JsonDocument.Parse("""{"type":"sequence","value":[]}""");
        using var falseValue = JsonDocument.Parse("""{"type":"boolean","value":false}""");
        var emptyDictionaryDefault = emptyDictionary.RootElement.Clone();
        var emptySequenceDefault = emptySequence.RootElement.Clone();
        var falseDefault = falseValue.RootElement.Clone();

        IdlType Type(string name, bool nullable = false) => new() { Name = name, Nullable = nullable };
        IdlType ElementOrImage() => new() { Union = true, Subtypes = [Type("Element"), Type("ElementImage")] };
        IdlType FrozenElements() => new() { Generic = "FrozenArray", Subtypes = [Type("Element")] };
        IdlType SequenceOfElements() => new() { Generic = "sequence", Subtypes = [Type("Element")] };
        IdlArgument Argument(string name, IdlType type, bool optional = false, JsonElement? defaultValue = null) => new() { Name = name, Type = type, Optional = optional, Default = defaultValue };
        IdlMember Field(string declaring, string name, IdlType type, JsonElement? defaultValue = null) => new()
        {
            Kind = IdlMemberKind.Field,
            Name = name,
            Type = type,
            Default = defaultValue,
            Source = source,
            DeclaringDefinition = declaring,
            Href = Href,
        };
        IdlMember Operation(string declaring, string name, IdlType returnType, List<IdlArgument> arguments, List<IdlExtendedAttribute>? extAttrs = null) => new()
        {
            Kind = IdlMemberKind.Operation,
            Name = name,
            Type = returnType,
            Arguments = arguments,
            ExtAttrs = extAttrs ?? [],
            Source = source,
            DeclaringDefinition = declaring,
            Href = Href,
            ExperimentalApi = Experimental,
        };
        IdlMember Attribute(string declaring, string name, IdlType type, bool readOnly = false, List<IdlExtendedAttribute>? extAttrs = null) => new()
        {
            Kind = IdlMemberKind.Attribute,
            Name = name,
            Type = type,
            Readonly = readOnly,
            ExtAttrs = extAttrs ?? [],
            Source = source,
            DeclaringDefinition = declaring,
            Href = Href,
            ExperimentalApi = Experimental,
        };
        void AddDefinition(string name, IdlDefinitionKind kind, List<IdlMember> members, string? inheritance = null, List<IdlExtendedAttribute>? extAttrs = null)
        {
            model.Definitions[name] = new IdlDefinition
            {
                Name = name,
                Kind = kind,
                Inheritance = inheritance,
                Members = members,
                ExtAttrs = extAttrs ?? [],
                Href = Href,
                Source = source,
                SpecUrl = Href,
                ExperimentalApi = Experimental,
            };
        }

        var exposedWindow = new IdlExtendedAttribute { Name = "Exposed", Value = "Window" };
        AddDefinition("UpdateElementGeometryOptions", IdlDefinitionKind.Dictionary,
        [
            Field("UpdateElementGeometryOptions", "preserveHitTestOrder", Type("boolean"), falseDefault),
            Field("UpdateElementGeometryOptions", "canvasClip", Type("DOMRectInit")),
            Field("UpdateElementGeometryOptions", "canvasTransform", Type("DOMMatrixInit")),
        ]);
        AddDefinition("ElementImageDefaultSize", IdlDefinitionKind.Dictionary,
        [
            Field("ElementImageDefaultSize", "width", Type("double")),
            Field("ElementImageDefaultSize", "height", Type("double")),
        ]);
        AddDefinition("DrawElementImageOptions", IdlDefinitionKind.Dictionary,
        [
            Field("DrawElementImageOptions", "preserveElementGeometry", Type("boolean"), falseDefault),
        ]);
        AddDefinition("CanvasPaintEventInit", IdlDefinitionKind.Dictionary, [Field("CanvasPaintEventInit", "changedElements", SequenceOfElements(), emptySequenceDefault)], inheritance: "EventInit");
        AddDefinition("ElementGeometryUpdateEventInit", IdlDefinitionKind.Dictionary, [Field("ElementGeometryUpdateEventInit", "elements", SequenceOfElements(), emptySequenceDefault)], inheritance: "EventInit");
        AddDefinition("ElementImage", IdlDefinitionKind.Interface,
        [
            Operation("ElementImage", "close", Type("undefined"), []),
        ], extAttrs: [new IdlExtendedAttribute { Name = "Exposed", Values = ["Window", "Worker"] }]);
        foreach (var (name, attribute, init) in (ReadOnlySpan<(string, string, string)>)[("CanvasPaintEvent", "changedElements", "CanvasPaintEventInit"), ("ElementGeometryUpdateEvent", "elements", "ElementGeometryUpdateEventInit")])
        {
            AddDefinition(name, IdlDefinitionKind.Interface,
            [
                new IdlMember
                {
                    Kind = IdlMemberKind.Constructor,
                    Arguments = [Argument("type", Type("DOMString")), Argument("eventInitDict", Type(init), optional: true, emptyDictionaryDefault)],
                    Source = source,
                    DeclaringDefinition = name,
                    Href = Href,
                    ExperimentalApi = Experimental,
                },
                Attribute(name, attribute, FrozenElements(), readOnly: true, extAttrs: [new IdlExtendedAttribute { Name = "SameObject" }]),
            ], inheritance: "Event", extAttrs: [exposedWindow]);
        }

        List<IdlMember> GeometryMembers(string declaring) =>
        [
            Operation(declaring, "updateElementGeometry", Type("undefined"), [Argument("element", ElementOrImage()), Argument("options", Type("UpdateElementGeometryOptions"), optional: true, emptyDictionaryDefault)]),
            Operation(declaring, "clearElementGeometry", Type("undefined"), [Argument("element", ElementOrImage())]),
            Operation(declaring, "getElementImageDefaultSize", Type("ElementImageDefaultSize"), [Argument("element", ElementOrImage())]),
        ];

        canvas.Members.AddRange(
        [
            Attribute("HTMLCanvasElement", "content", Type("DOMString"), extAttrs: [new IdlExtendedAttribute { Name = "CEReactions" }, new IdlExtendedAttribute { Name = "Reflect" }]),
            Attribute("HTMLCanvasElement", "onpaint", Type("EventHandler")),
            Operation("HTMLCanvasElement", "requestPaint", Type("undefined"), []),
            Operation("HTMLCanvasElement", "captureElementImage", Type("ElementImage"), [Argument("element", Type("Element"))]),
            .. GeometryMembers("HTMLCanvasElement"),
            Operation("HTMLCanvasElement", "getElementTransform", Type("DOMMatrix"), [Argument("element", Type("Element"))], [new IdlExtendedAttribute { Name = "NewObject" }]),
            Attribute("HTMLCanvasElement", "onelementgeometryupdate", Type("EventHandler")),
        ]);
        if (model.Definitions.TryGetValue("OffscreenCanvas", out var offscreenCanvas))
        {
            offscreenCanvas.Members.AddRange(GeometryMembers("OffscreenCanvas"));
        }

        var unrestrictedDouble = new IdlType { Name = "unrestricted double" };
        List<IdlArgument> Numbers(params string[] names) => [.. names.Select(name => Argument(name, unrestrictedDouble))];
        // (CanvasState, the .NET type of the members of the mixins of the 2D contexts, see CanvasLayers)
        foreach (var name in (string[])["CanvasState", "CanvasRenderingContext2D", "OffscreenCanvasRenderingContext2D"])
        {
            if (!model.Definitions.TryGetValue(name, out var context))
                continue;

            foreach (var numbers in (string[][])[["dx", "dy"], ["dx", "dy", "dwidth", "dheight"], ["sx", "sy", "swidth", "sheight", "dx", "dy"], ["sx", "sy", "swidth", "sheight", "dx", "dy", "dwidth", "dheight"]])
            {
                context.Members.Add(Operation("CanvasDrawElementImage", "drawElementImage", Type("undefined"),
                [
                    Argument("element", ElementOrImage()),
                    .. Numbers(numbers),
                    Argument("options", Type("DrawElementImageOptions"), optional: true, emptyDictionaryDefault),
                ]));
            }
        }
    }

    /// <summary>
    /// The interfaces of the Long Tasks API (https://w3c.github.io/longtasks/), a discontinued draft that webref no longer
    /// includes (removed 2026-10), which the browsers based on Chromium implement and the longtask-timing directory of the
    /// web-platform-tests tests:
    /// <code>
    /// [Exposed=Window] interface PerformanceLongTaskTiming : PerformanceEntry {
    ///     readonly attribute DOMHighResTimeStamp startTime;
    ///     readonly attribute DOMHighResTimeStamp duration;
    ///     readonly attribute DOMString name;
    ///     readonly attribute DOMString entryType;
    ///     readonly attribute FrozenArray&lt;TaskAttributionTiming&gt; attribution;
    ///     [Default] object toJSON();
    /// };
    /// [Exposed=Window] interface TaskAttributionTiming : PerformanceEntry {
    ///     readonly attribute DOMHighResTimeStamp startTime;
    ///     readonly attribute DOMHighResTimeStamp duration;
    ///     readonly attribute DOMString name;
    ///     readonly attribute DOMString entryType;
    ///     readonly attribute DOMString containerType;
    ///     readonly attribute DOMString containerSrc;
    ///     readonly attribute DOMString containerId;
    ///     readonly attribute DOMString containerName;
    ///     [Default] object toJSON();
    /// };
    /// </code>
    /// </summary>
    private static void AddLongTasks(IdlModel model)
    {
        if (!model.Definitions.ContainsKey("PerformanceEntry") || model.Definitions.ContainsKey("PerformanceLongTaskTiming"))
            return;

        AddInterface("PerformanceLongTaskTiming", "performancelongtasktiming", [("attribution", new IdlType { Generic = "FrozenArray", Subtypes = [new IdlType { Name = "TaskAttributionTiming" }] })]);
        AddInterface("TaskAttributionTiming", "taskattributiontiming",
        [
            ("containerType", new IdlType { Name = "DOMString" }),
            ("containerSrc", new IdlType { Name = "DOMString" }),
            ("containerId", new IdlType { Name = "DOMString" }),
            ("containerName", new IdlType { Name = "DOMString" }),
        ]);

        void AddInterface(string name, string anchor, List<(string Name, IdlType Type)> attributes)
        {
            const string Source = "longtasks";
            const string SpecUrl = "https://w3c.github.io/longtasks/";
            List<(string Name, IdlType Type)> allAttributes =
            [
                ("startTime", new IdlType { Name = "DOMHighResTimeStamp" }),
                ("duration", new IdlType { Name = "DOMHighResTimeStamp" }),
                ("name", new IdlType { Name = "DOMString" }),
                ("entryType", new IdlType { Name = "DOMString" }),
                .. attributes,
            ];
            var members = allAttributes.ConvertAll(attribute => new IdlMember
            {
                Kind = IdlMemberKind.Attribute,
                Name = attribute.Name,
                Type = attribute.Type,
                Readonly = true,
                Href = SpecUrl + "#dom-" + anchor + "-" + attribute.Name.ToLowerInvariant(),
                Source = Source,
                DeclaringDefinition = name,
            });
            members.Add(new IdlMember
            {
                Kind = IdlMemberKind.Operation,
                Name = "toJSON",
                Type = new IdlType { Name = "object" },
                ExtAttrs = [new IdlExtendedAttribute { Name = "Default" }],
                Href = SpecUrl + "#dom-" + anchor + "-tojson",
                Source = Source,
                DeclaringDefinition = name,
            });
            model.Definitions[name] = new IdlDefinition
            {
                Name = name,
                Kind = IdlDefinitionKind.Interface,
                Inheritance = "PerformanceEntry",
                Members = members,
                ExtAttrs = [new IdlExtendedAttribute { Name = "Exposed", Value = "Window" }],
                Href = SpecUrl + "#" + anchor,
                Source = Source,
                SpecUrl = SpecUrl,
            };
        }
    }

    /// <summary>
    /// The PerformanceScrollTiming interface of the scroll performance timing proposal
    /// (https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md), which no specification
    /// defines and the tentative web-platform-tests of <c>scroll-performance-timing</c> test:
    /// <code>
    /// [Exposed=Window] interface PerformanceScrollTiming : PerformanceEntry {
    ///   readonly attribute DOMString entryType;
    ///   readonly attribute DOMString name;
    ///   readonly attribute DOMHighResTimeStamp startTime;
    ///   readonly attribute DOMHighResTimeStamp firstFrameTime;
    ///   readonly attribute DOMHighResTimeStamp duration;
    ///   readonly attribute unsigned long framesExpected;
    ///   readonly attribute unsigned long framesProduced;
    ///   readonly attribute DOMHighResTimeStamp checkerboardTime;
    ///   readonly attribute long deltaX;
    ///   readonly attribute long deltaY;
    ///   readonly attribute DOMString scrollSource;
    ///   readonly attribute Node? target;
    /// };
    /// </code>
    /// </summary>
    private static void AddScrollTiming(IdlModel model)
    {
        const string Name = "PerformanceScrollTiming";
        if (!model.Definitions.ContainsKey("PerformanceEntry") || model.Definitions.ContainsKey(Name))
            return;

        const string Source = "scroll-timing";
        const string SpecUrl = "https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md";
        List<(string Name, IdlType Type)> attributes =
        [
            ("entryType", new IdlType { Name = "DOMString" }),
            ("name", new IdlType { Name = "DOMString" }),
            ("startTime", new IdlType { Name = "DOMHighResTimeStamp" }),
            ("firstFrameTime", new IdlType { Name = "DOMHighResTimeStamp" }),
            ("duration", new IdlType { Name = "DOMHighResTimeStamp" }),
            ("framesExpected", new IdlType { Name = "unsigned long" }),
            ("framesProduced", new IdlType { Name = "unsigned long" }),
            ("checkerboardTime", new IdlType { Name = "DOMHighResTimeStamp" }),
            ("deltaX", new IdlType { Name = "long" }),
            ("deltaY", new IdlType { Name = "long" }),
            ("scrollSource", new IdlType { Name = "DOMString" }),
            ("target", new IdlType { Name = "Node", Nullable = true }),
        ];
        model.Definitions[Name] = new IdlDefinition
        {
            Name = Name,
            Kind = IdlDefinitionKind.Interface,
            Inheritance = "PerformanceEntry",
            Members = attributes.ConvertAll(attribute => new IdlMember
            {
                Kind = IdlMemberKind.Attribute,
                Name = attribute.Name,
                Type = attribute.Type,
                Readonly = true,
                Href = SpecUrl + "#attribute-reference",
                Source = Source,
                DeclaringDefinition = Name,
            }),
            ExtAttrs = [new IdlExtendedAttribute { Name = "Exposed", Value = "Window" }],
            Href = SpecUrl + "#performancescrolltiming-interface",
            Source = Source,
            SpecUrl = SpecUrl,
            ExperimentalApi = "ScrollTiming",
        };
    }

    /// <summary>
    /// The <c>initiatorUrl</c> attribute of PerformanceResourceTiming (https://github.com/w3c/resource-timing/issues/263), which
    /// Chromium implements behind a flag and the tentative web-platform-tests of <c>resource-timing/tentative/initiator-url</c> test:
    /// <code>
    /// partial interface PerformanceResourceTiming { readonly attribute USVString initiatorUrl; };
    /// </code>
    /// </summary>
    private static void AddResourceTimingInitiatorUrl(IdlModel model)
    {
        if (!model.Definitions.TryGetValue("PerformanceResourceTiming", out var definition) || definition.Members.Exists(member => member.Name == "initiatorUrl"))
            return;

        definition.Members.Add(new IdlMember
        {
            Kind = IdlMemberKind.Attribute,
            Name = "initiatorUrl",
            Type = new IdlType { Name = "USVString" },
            Readonly = true,
            Href = "https://github.com/web-platform-tests/wpt/blob/c8fa86fb13d4596dad4c5367cb4555b585c05f8a/resource-timing/resources/test-initiator.js#L39",
            Source = "resource-timing",
            DeclaringDefinition = definition.Name,
            ExperimentalApi = "ResourceTimingInitiatorUrl",
        });
    }

    /// <summary>
    /// The tentative compareComponent() and generate() methods of URLPattern, tested by the WPT revision pinned in
    /// tests/Meziantou.Framework.WebPlatform.SpecData/versions.json.
    /// </summary>
    private static void AddUrlPatternTentativeMethods(IdlModel model)
    {
        if (!model.Definitions.TryGetValue("URLPattern", out var definition))
            return;

        model.Definitions["URLPatternComponent"] = new IdlDefinition
        {
            Name = "URLPatternComponent",
            Kind = IdlDefinitionKind.Enum,
            EnumValues = ["protocol", "username", "password", "hostname", "port", "pathname", "search", "hash"],
            Source = "urlpattern",
            SpecUrl = "https://github.com/web-platform-tests/wpt/tree/3d98b9b722ca38685e04dea4e5208d45ce7fd55f/urlpattern",
        };

        const string CompareComponentHref = "https://github.com/web-platform-tests/wpt/blob/3d98b9b722ca38685e04dea4e5208d45ce7fd55f/urlpattern/resources/urlpattern-compare-tests.tentative.js#L1";
        const string GenerateHref = "https://github.com/web-platform-tests/wpt/blob/3d98b9b722ca38685e04dea4e5208d45ce7fd55f/urlpattern/urlpattern-generate.tentative.any.js#L1";

        definition.Members.Add(new IdlMember
        {
            Kind = IdlMemberKind.Operation,
            Name = "compareComponent",
            Type = new IdlType { Name = "long" },
            Arguments =
            [
                new IdlArgument { Name = "component", Type = new IdlType { Name = "URLPatternComponent" } },
                new IdlArgument { Name = "left", Type = new IdlType { Name = "URLPattern" } },
                new IdlArgument { Name = "right", Type = new IdlType { Name = "URLPattern" } },
            ],
            Special = "static",
            Source = "urlpattern",
            DeclaringDefinition = definition.Name,
            ExperimentalApi = "UrlPatternTentativeMethods",
            Href = CompareComponentHref,
        });
        definition.Members.Add(new IdlMember
        {
            Kind = IdlMemberKind.Operation,
            Name = "generate",
            Type = new IdlType { Name = "USVString" },
            Arguments =
            [
                new IdlArgument { Name = "component", Type = new IdlType { Name = "URLPatternComponent" } },
                new IdlArgument
                {
                    Name = "groups",
                    Type = new IdlType
                    {
                        Generic = "record",
                        Subtypes = [new IdlType { Name = "USVString" }, new IdlType { Name = "USVString" }],
                    },
                },
            ],
            Source = "urlpattern",
            DeclaringDefinition = definition.Name,
            ExperimentalApi = "UrlPatternTentativeMethods",
            Href = GenerateHref,
        });
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
    /// window.find() (https://developer.mozilla.org/docs/Web/API/Window/find), the find-in-page API that the browsers
    /// implement and that the inert tests use, but that no specification defines:
    /// <code>
    /// partial interface Window {
    ///   boolean find(optional DOMString string = "", optional boolean caseSensitive = false, optional boolean backwards = false,
    ///                optional boolean wrapAround = false, optional boolean wholeWord = false, optional boolean searchInFrames = false,
    ///                optional boolean showDialog = false);
    /// };
    /// </code>
    /// </summary>
    private static void AddWindowFind(IdlModel model)
    {
        if (!model.Definitions.TryGetValue("Window", out var definition))
            return;

        using var emptyString = JsonDocument.Parse("""{"type":"string","value":""}""");
        using var falseValue = JsonDocument.Parse("""{"type":"boolean","value":false}""");
        var emptyStringDefault = emptyString.RootElement.Clone();
        var falseDefault = falseValue.RootElement.Clone();
        IdlArgument Boolean(string name) => new() { Name = name, Type = new IdlType { Name = "boolean" }, Optional = true, Default = falseDefault };
        definition.Members.Add(new IdlMember
        {
            Kind = IdlMemberKind.Operation,
            Name = "find",
            Type = new IdlType { Name = "boolean" },
            Arguments =
            [
                new IdlArgument { Name = "string", Type = new IdlType { Name = "DOMString" }, Optional = true, Default = emptyStringDefault },
                Boolean("caseSensitive"),
                Boolean("backwards"),
                Boolean("wrapAround"),
                Boolean("wholeWord"),
                Boolean("searchInFrames"),
                Boolean("showDialog"),
            ],
            Href = "https://developer.mozilla.org/docs/Web/API/Window/find#syntax",
            Source = "html",
            DeclaringDefinition = definition.Name,
            ExperimentalApi = "WindowFind",
        });
    }

    /// <summary>
    /// The legacy fromElement and toElement attributes of MouseEvent, which Blink and WebKit implement (the pointer events
    /// tests check that they are null for the pointer events, https://w3c.github.io/pointerevents/#attributes-and-default-actions),
    /// but that no specification defines:
    /// <code>
    /// partial interface MouseEvent {
    ///   readonly attribute Node? fromElement;
    ///   readonly attribute Node? toElement;
    /// };
    /// </code>
    /// </summary>
    private static void AddMouseEventFromToElement(IdlModel model)
    {
        if (!model.Definitions.TryGetValue("MouseEvent", out var definition))
            return;

        foreach (var name in (string[])["fromElement", "toElement"])
        {
            definition.Members.Add(new IdlMember
            {
                Kind = IdlMemberKind.Attribute,
                Name = name,
                Type = new IdlType { Name = "Node", Nullable = true },
                Readonly = true,
                Source = "uievents",
                DeclaringDefinition = definition.Name,
                ExperimentalApi = "MouseEventFromToElement",
            });
        }
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
    /// The <c>requestStorageAccess(types)</c> overload of Document defined by Extending Storage Access API (SAA) to non-cookie
    /// storage (https://privacycg.github.io/saa-non-cookie-storage/#request-storage-access-types), which the IDL extracted
    /// by webref does not include (only <c>hasUnpartitionedCookieAccess()</c>, <c>StorageAccessTypes</c> and
    /// <c>StorageAccessHandle</c>):
    /// <code>
    /// partial interface Document {
    ///   Promise&lt;StorageAccessHandle&gt; requestStorageAccess(StorageAccessTypes types);
    /// };
    /// </code>
    /// </summary>
    private static void AddRequestStorageAccessTypes(IdlModel model)
    {
        if (!model.Definitions.TryGetValue("Document", out var definition) || !model.Definitions.ContainsKey("StorageAccessHandle"))
            return;

        definition.Members.Add(new IdlMember
        {
            Kind = IdlMemberKind.Operation,
            Name = "requestStorageAccess",
            Type = new IdlType { Generic = "Promise", Subtypes = [new IdlType { Name = "StorageAccessHandle" }] },
            Arguments = [new IdlArgument { Name = "types", Type = new IdlType { Name = "StorageAccessTypes" } }],
            Href = "https://privacycg.github.io/saa-non-cookie-storage/#dom-document-requeststorageaccess-types",
            Source = "saa-non-cookie-storage",
            DeclaringDefinition = definition.Name,
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
    /// The Window Management API makes Screen derive from EventTarget (https://w3c.github.io/window-management/#api-extensions-to-screen,
    /// "Make Screen derive from EventTarget in CSSOM View"), for its <c>change</c> event: the partial interface of webref
    /// cannot express it (<c>partial interface Screen /* : EventTarget */</c>).
    /// </summary>
    private static void MakeScreenAnEventTarget(IdlModel model)
    {
        if (!model.Definitions.TryGetValue("Screen", out var screen) || screen.Inheritance is not null)
            return;

        model.Definitions["Screen"] = new IdlDefinition
        {
            Name = screen.Name,
            Kind = screen.Kind,
            Inheritance = "EventTarget",
            ExtAttrs = screen.ExtAttrs,
            Members = screen.Members,
            Source = screen.Source,
            Href = screen.Href,
            SpecUrl = screen.SpecUrl,
        };
    }

    /// <summary>
    /// The change events of ContainerQueryList, proposed in https://github.com/w3c/csswg-drafts/pull/13551 and used by the
    /// tentative css-conditional tests: CSS Conditional 5 defines Element.matchContainer() and ContainerQueryList, but not
    /// their change events yet. They are modeled on MediaQueryList:
    /// <code>
    /// [Exposed=Window] interface ContainerQueryList : EventTarget {
    ///   attribute EventHandler onchange;
    /// };
    /// </code>
    /// </summary>
    private static void AddContainerQueryListChangeEvent(IdlModel model)
    {
        if (!model.Definitions.TryGetValue("ContainerQueryList", out var list) || list.Inheritance is not null || list.Members.Exists(m => m.Name == "onchange"))
            return;

        model.Definitions["ContainerQueryList"] = new IdlDefinition
        {
            Name = list.Name,
            Kind = list.Kind,
            Inheritance = "EventTarget",
            ExtAttrs = list.ExtAttrs,
            Members =
            [
                .. list.Members,
                new IdlMember
                {
                    Kind = IdlMemberKind.Attribute,
                    Name = "onchange",
                    Type = new IdlType { Name = "EventHandler" },
                    Source = list.Source,
                    DeclaringDefinition = list.Name,
                    ExperimentalApi = "ContainerQueryListChangeEvent",
                },
            ],
            Source = list.Source,
            Href = list.Href,
            SpecUrl = list.SpecUrl,
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
            ExperimentalApi = "CssPrivateRule",
        };
    }

    /// <summary>
    /// The stretch attribute of FontFace (https://drafts.csswg.org/css-font-loading/#dom-fontface-stretch): the
    /// specification defines it in prose as a legacy alias of width, but not in its IDL.
    /// </summary>
    private static void AddFontFaceStretch(IdlModel model)
    {
        if (model.Definitions.TryGetValue("FontFace", out var fontFace) && !fontFace.Members.Exists(m => m.Name == "stretch"))
        {
            fontFace.Members.Add(new IdlMember
            {
                Kind = IdlMemberKind.Attribute,
                Name = "stretch",
                Type = new IdlType { Name = "CSSOMString" },
                Source = "css-font-loading-3",
                DeclaringDefinition = fontFace.Name,
            });
        }
    }

    /// <summary>
    /// The sizeAdjust member of FontFaceDescriptors: the FontFace interface has the sizeAdjust attribute of the size-adjust
    /// descriptor (https://drafts.csswg.org/css-fonts-5/#size-adjust-desc), but the dictionary of the constructor does not
    /// declare it, unlike the other descriptors (WPT css/css-font-loading/fontface-size-adjust-descriptor.html sets it, and the
    /// browsers support it): <c>partial dictionary FontFaceDescriptors { CSSOMString sizeAdjust = "100%"; };</c>
    /// </summary>
    private static void AddFontFaceDescriptorsSizeAdjust(IdlModel model)
    {
        if (model.Definitions.TryGetValue("FontFaceDescriptors", out var descriptors) && !descriptors.Members.Exists(m => m.Name == "sizeAdjust"))
        {
            using var defaultValue = JsonDocument.Parse("""{"type":"string","value":"100%"}""");
            descriptors.Members.Add(new IdlMember
            {
                Kind = IdlMemberKind.Field,
                Name = "sizeAdjust",
                Type = new IdlType { Name = "CSSOMString" },
                Default = defaultValue.RootElement.Clone(),
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
    /// The <c>artwork</c> attribute of MediaMetadata is a <c>FrozenArray&lt;object&gt;</c>, whose setter converts the objects
    /// to MediaImage dictionaries (https://w3c.github.io/mediasession/#convert-artwork-algorithm, which throws a TypeError for
    /// an image without <c>src</c>) and whose getter returns the converted dictionaries: it is a <c>FrozenArray&lt;MediaImage&gt;</c>.
    /// </summary>
    private static void SetMediaMetadataArtworkType(IdlModel model)
    {
        if (!model.Definitions.TryGetValue("MediaMetadata", out var definition))
            return;

        var index = definition.Members.FindIndex(m => m.Name == "artwork" && m.Kind == IdlMemberKind.Attribute);
        if (index < 0 || definition.Members[index] is not { Type: { Generic: "FrozenArray", Subtypes: [{ Name: "object" }] } } member)
            return;

        definition.Members[index] = new IdlMember
        {
            Kind = member.Kind,
            Name = member.Name,
            Type = new IdlType { Generic = "FrozenArray", Subtypes = [new IdlType { Name = "MediaImage" }] },
            Readonly = member.Readonly,
            ExtAttrs = member.ExtAttrs,
            Href = member.Href,
            Source = member.Source,
            DeclaringDefinition = member.DeclaringDefinition,
        };
    }

    /// <summary>
    /// The body of the <c>document-policy-violation</c> reports (https://wicg.github.io/document-policy/#reporting), which the
    /// draft describes without IDL; its members are the ones of the browsers:
    /// <code>dictionary DocumentPolicyViolationReportBody : ReportBody { DOMString featureId; DOMString? sourceFile; long? lineNumber; long? columnNumber; DOMString disposition; DOMString? message; };</code>
    /// </summary>
    private static void AddDocumentPolicyViolationReportBody(IdlModel model)
    {
        if (!model.Definitions.ContainsKey("ReportBody") || model.Definitions.ContainsKey("DocumentPolicyViolationReportBody"))
            return;

        IdlMember Field(string name, string type, bool nullable = false) => new()
        {
            Kind = IdlMemberKind.Field,
            Name = name,
            Type = new IdlType { Name = type, Nullable = nullable },
            Source = "document-policy",
            DeclaringDefinition = "DocumentPolicyViolationReportBody",
        };

        model.Definitions["DocumentPolicyViolationReportBody"] = new IdlDefinition
        {
            Name = "DocumentPolicyViolationReportBody",
            Kind = IdlDefinitionKind.Dictionary,
            Inheritance = "ReportBody",
            Members =
            [
                Field("featureId", "DOMString"),
                Field("sourceFile", "DOMString", nullable: true),
                Field("lineNumber", "long", nullable: true),
                Field("columnNumber", "long", nullable: true),
                Field("disposition", "DOMString"),
                Field("message", "DOMString", nullable: true),
            ],
            Source = "document-policy",
            SpecUrl = "https://wicg.github.io/document-policy/",
        };
    }

    /// <summary>
    /// The <c>policy</c> attribute of the <c>iframe</c> elements (https://wicg.github.io/document-policy/#iframe-policy-attribute):
    /// the required document policy of the iframe, which webref does not include (Document Policy is a WICG draft).
    /// <code>
    /// partial interface HTMLIFrameElement {
    ///   [CEReactions] attribute DOMString policy;
    /// };
    /// </code>
    /// </summary>
    private static void AddIFrameElementPolicy(IdlModel model)
    {
        if (!model.Definitions.TryGetValue("HTMLIFrameElement", out var definition) || definition.Members.Exists(m => m.Name == "policy"))
            return;

        definition.Members.Add(new IdlMember
        {
            Kind = IdlMemberKind.Attribute,
            Name = "policy",
            Type = new IdlType { Name = "DOMString" },
            ExtAttrs = [new IdlExtendedAttribute { Name = "CEReactions" }],
            Href = "https://wicg.github.io/document-policy/#iframe-policy-attribute",
            Source = "document-policy",
            DeclaringDefinition = definition.Name,
            ExperimentalApi = "DocumentPolicyIframeAttribute",
        });
    }

    /// <summary>
    /// The <c>async</c> attribute of the SVG <c>script</c> elements, as the one of HTMLScriptElement
    /// (https://html.spec.whatwg.org/multipage/scripting.html#dom-script-async), which SVG 2 does not define but the
    /// web-platform-tests of <c>svg/interact/scripted</c> test:
    /// <code>
    /// partial interface SVGScriptElement {
    ///   [CEReactions] attribute boolean async;
    /// };
    /// </code>
    /// </summary>
    private static void AddSvgScriptElementAsync(IdlModel model)
    {
        if (!model.Definitions.TryGetValue("SVGScriptElement", out var definition) || definition.Members.Exists(m => m.Name == "async"))
            return;

        definition.Members.Add(new IdlMember
        {
            Kind = IdlMemberKind.Attribute,
            Name = "async",
            Type = new IdlType { Name = "boolean" },
            ExtAttrs = [new IdlExtendedAttribute { Name = "CEReactions" }],
            Href = "https://html.spec.whatwg.org/multipage/scripting.html#dom-script-async",
            Source = "svg",
            DeclaringDefinition = definition.Name,
            ExperimentalApi = "SvgScriptAsync",
        });
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
    /// The SVG Paths specification still declares pathLength, getTotalLength() and getPointAtLength() on SVGPathElement, where
    /// SVG 2 moved them to SVGGeometryElement (https://svgwg.org/svg2-draft/types.html#InterfaceSVGGeometryElement). The browsers
    /// (and svg/historical.html) only define them on SVGGeometryElement.prototype, so the redeclarations are removed.
    /// </summary>
    private static void RemoveSvgPathElementGeometryMembers(IdlModel model)
    {
        if (!model.Definitions.TryGetValue("SVGPathElement", out var definition) || !model.Definitions.TryGetValue("SVGGeometryElement", out var geometry))
            return;

        definition.Members.RemoveAll(m => m is { Kind: IdlMemberKind.Attribute or IdlMemberKind.Operation, IsStatic: false } && geometry.Members.Exists(g => g.Kind == m.Kind && g.Name == m.Name));
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

    /// <summary>
    /// The canvas layers (https://github.com/whatwg/html/pull/9537), which Chromium implements behind a flag and the tentative
    /// web-platform-tests of html/canvas/element/layers test, on the contexts that include CanvasState:
    /// <code>
    /// partial interface mixin CanvasState {
    ///   undefined beginLayer(optional BeginLayerOptions options = {});
    ///   undefined endLayer();
    /// };
    /// dictionary BeginLayerOptions { any filter = null; };
    /// </code>
    /// The <c>filter</c> member is a CSS filter, a <c>CanvasFilter</c> or the description of one (a <c>CanvasFilterInput</c>
    /// or a sequence of them) in the proposal: it is <c>any</c>, and the .NET member converts the value (see
    /// <see cref="AddCanvasFilterObjects"/>). As the other dictionaries of the proposals, the dictionary is not experimental
    /// (the bindings do not support experimental dictionaries): the operations that take it are.
    /// </summary>
    private static void AddCanvasLayers(IdlModel model)
    {
        const string Href = "https://github.com/fserb/canvas2D/blob/master/spec/layers.md#proposal";
        if (!model.Definitions.TryGetValue("CanvasState", out var mixin) || model.Definitions.ContainsKey("BeginLayerOptions"))
            return;

        model.Definitions["BeginLayerOptions"] = new IdlDefinition
        {
            Name = "BeginLayerOptions",
            Kind = IdlDefinitionKind.Dictionary,
            Members =
            [
                new IdlMember
                {
                    Kind = IdlMemberKind.Field,
                    Name = "filter",
                    Type = new IdlType { Name = "any" },
                    Source = mixin.Source,
                    DeclaringDefinition = "BeginLayerOptions",
                    Href = Href,
                },
            ],
            Source = mixin.Source,
            Href = Href,
            SpecUrl = Href,
        };

        using var emptyDictionary = JsonDocument.Parse("""{"type":"dictionary"}""");
        foreach (var name in (string[])["CanvasState", "CanvasRenderingContext2D", "OffscreenCanvasRenderingContext2D", "PaintRenderingContext2D"])
        {
            if (!model.Definitions.TryGetValue(name, out var definition))
                continue;

            definition.Members.Add(new IdlMember
            {
                Kind = IdlMemberKind.Operation,
                Name = "beginLayer",
                Type = new IdlType { Name = "undefined" },
                Arguments = [new IdlArgument { Name = "options", Type = new IdlType { Name = "BeginLayerOptions" }, Optional = true, Default = emptyDictionary.RootElement.Clone() }],
                Source = mixin.Source,
                DeclaringDefinition = "CanvasState",
                ExperimentalApi = "CanvasLayers",
                Href = Href,
            });
            definition.Members.Add(new IdlMember
            {
                Kind = IdlMemberKind.Operation,
                Name = "endLayer",
                Type = new IdlType { Name = "undefined" },
                Source = mixin.Source,
                DeclaringDefinition = "CanvasState",
                ExperimentalApi = "CanvasLayers",
                Href = Href,
            });
        }
    }

    /// <summary>
    /// The <c>CanvasFilter</c> objects (https://github.com/whatwg/html/issues/5621, the explainer
    /// https://github.com/fserb/canvas2D/blob/master/spec/filters.md), which Chromium implements behind a flag and the tentative
    /// web-platform-tests of html/canvas/element/filters test:
    /// <code>
    /// typedef record&lt;DOMString, any&gt; CanvasFilterInput;
    /// [Exposed=(Window,Worker,PaintWorklet)]
    /// interface CanvasFilter {
    ///   constructor((CanvasFilterInput or sequence&lt;CanvasFilterInput&gt;) init);
    /// };
    /// partial interface mixin CanvasFilters {
    ///   attribute (DOMString or CanvasFilter) filter;
    /// };
    /// </code>
    /// The values of a <c>CanvasFilterInput</c> are converted as Chromium converts them (e.g. a number or a pair of numbers),
    /// not as Web IDL types: the argument of the constructor is <c>any</c>, and the .NET constructor converts it.
    /// </summary>
    private static void AddCanvasFilterObjects(IdlModel model)
    {
        const string Href = "https://github.com/fserb/canvas2D/blob/master/spec/filters.md#proposal";
        if (!model.Definitions.TryGetValue("CanvasFilters", out var mixin) || model.Definitions.ContainsKey("CanvasFilter"))
            return;

        model.Definitions["CanvasFilter"] = new IdlDefinition
        {
            Name = "CanvasFilter",
            Kind = IdlDefinitionKind.Interface,
            Members =
            [
                new IdlMember
                {
                    Kind = IdlMemberKind.Constructor,
                    Arguments = [new IdlArgument { Name = "init", Type = new IdlType { Name = "any" } }],
                    Source = mixin.Source,
                    DeclaringDefinition = "CanvasFilter",
                    Href = Href,
                },
            ],
            ExtAttrs = [new IdlExtendedAttribute { Name = "Exposed", Values = ["Window", "Worker", "PaintWorklet"] }],
            Source = mixin.Source,
            Href = Href,
            SpecUrl = Href,
            ExperimentalApi = "CanvasFilterObjects",
        };

        foreach (var name in (string[])["CanvasFilters", "CanvasRenderingContext2D", "OffscreenCanvasRenderingContext2D", "PaintRenderingContext2D"])
        {
            if (!model.Definitions.TryGetValue(name, out var definition))
                continue;

            var index = definition.Members.FindIndex(m => m.Kind == IdlMemberKind.Attribute && m.Name == "filter");
            if (index < 0)
                continue;

            var filter = definition.Members[index];
            definition.Members[index] = new IdlMember
            {
                Kind = filter.Kind,
                Name = filter.Name,
                Type = new IdlType { Union = true, Subtypes = [new IdlType { Name = "DOMString" }, new IdlType { Name = "CanvasFilter" }] },
                ExtAttrs = filter.ExtAttrs,
                Href = filter.Href,
                Source = filter.Source,
                DeclaringDefinition = filter.DeclaringDefinition,
            };
        }
    }

    /// <summary>
    /// The text clusters and the selection APIs of <c>TextMetrics</c> (https://github.com/whatwg/html/pull/11000, the explainer
    /// https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md), which Chromium implements
    /// behind a flag (ExtendedTextMetrics) and the tentative web-platform-tests of html/canvas/element/text test:
    /// <code>
    /// dictionary TextClusterOptions { CanvasTextAlign align; CanvasTextBaseline baseline; double x; double y; };
    /// [Exposed=(Window,Worker)] interface TextCluster {
    ///   readonly attribute double x;
    ///   readonly attribute double y;
    ///   readonly attribute unsigned long start;
    ///   readonly attribute unsigned long end;
    ///   readonly attribute CanvasTextAlign align;
    ///   readonly attribute CanvasTextBaseline baseline;
    /// };
    /// partial interface TextMetrics {
    ///   unsigned long getIndexFromOffset(double offset);
    ///   sequence&lt;DOMRectReadOnly&gt; getSelectionRects([EnforceRange] unsigned long start, [EnforceRange] unsigned long end);
    ///   DOMRectReadOnly getActualBoundingBox([EnforceRange] unsigned long start, [EnforceRange] unsigned long end);
    ///   sequence&lt;TextCluster&gt; getTextClusters(optional TextClusterOptions options = {});
    ///   sequence&lt;TextCluster&gt; getTextClusters([EnforceRange] unsigned long start, [EnforceRange] unsigned long end, optional TextClusterOptions options = {});
    /// };
    /// partial interface mixin CanvasText {
    ///   undefined fillTextCluster(TextCluster textCluster, double x, double y, optional TextClusterOptions options = {});
    ///   undefined strokeTextCluster(TextCluster textCluster, double x, double y, optional TextClusterOptions options = {});
    /// };
    /// </code>
    /// Chromium declares fillTextCluster() and strokeTextCluster() as two overloads, with and without the options, which is the
    /// same as an optional argument. As the other dictionaries of the proposals, the dictionary is not experimental.
    /// </summary>
    private static void AddCanvasTextClusters(IdlModel model)
    {
        const string Href = "https://github.com/Igalia/explainers/blob/main/canvas-formatted-text/text-metrics-additions.md#proposal";
        const string Experimental = "CanvasTextClusters";
        if (!model.Definitions.TryGetValue("TextMetrics", out var metrics) || model.Definitions.ContainsKey("TextCluster"))
            return;

        var source = metrics.Source;
        model.Definitions["TextClusterOptions"] = new IdlDefinition
        {
            Name = "TextClusterOptions",
            Kind = IdlDefinitionKind.Dictionary,
            Members =
            [
                CanvasTextField("TextClusterOptions", "align", "CanvasTextAlign", source, Href),
                CanvasTextField("TextClusterOptions", "baseline", "CanvasTextBaseline", source, Href),
                CanvasTextField("TextClusterOptions", "x", "double", source, Href),
                CanvasTextField("TextClusterOptions", "y", "double", source, Href),
            ],
            Source = source,
            Href = Href,
            SpecUrl = Href,
        };

        model.Definitions["TextCluster"] = new IdlDefinition
        {
            Name = "TextCluster",
            Kind = IdlDefinitionKind.Interface,
            Members =
            [
                CanvasTextAttribute("TextCluster", "x", "double", source, Href),
                CanvasTextAttribute("TextCluster", "y", "double", source, Href),
                CanvasTextAttribute("TextCluster", "start", "unsigned long", source, Href),
                CanvasTextAttribute("TextCluster", "end", "unsigned long", source, Href),
                CanvasTextAttribute("TextCluster", "align", "CanvasTextAlign", source, Href),
                CanvasTextAttribute("TextCluster", "baseline", "CanvasTextBaseline", source, Href),
            ],
            ExtAttrs = [new IdlExtendedAttribute { Name = "Exposed", Values = ["Window", "Worker"] }],
            Source = source,
            Href = Href,
            SpecUrl = Href,
            ExperimentalApi = Experimental,
        };

        using var emptyDictionary = JsonDocument.Parse("""{"type":"dictionary"}""");
        var options = new IdlArgument { Name = "options", Type = new IdlType { Name = "TextClusterOptions" }, Optional = true, Default = emptyDictionary.RootElement.Clone() };
        metrics.Members.Add(CanvasTextOperation("TextMetrics", "getIndexFromOffset", new IdlType { Name = "unsigned long" }, [new IdlArgument { Name = "offset", Type = new IdlType { Name = "double" } }], source, Href, Experimental));
        metrics.Members.Add(CanvasTextOperation("TextMetrics", "getSelectionRects", SequenceOf("DOMRectReadOnly"), [.. CanvasTextRange()], source, Href, Experimental));
        metrics.Members.Add(CanvasTextOperation("TextMetrics", "getActualBoundingBox", new IdlType { Name = "DOMRectReadOnly" }, [.. CanvasTextRange()], source, Href, Experimental));
        metrics.Members.Add(CanvasTextOperation("TextMetrics", "getTextClusters", SequenceOf("TextCluster"), [options], source, Href, Experimental));
        metrics.Members.Add(CanvasTextOperation("TextMetrics", "getTextClusters", SequenceOf("TextCluster"), [.. CanvasTextRange(), options], source, Href, Experimental));

        foreach (var definition in GetCanvasTextDefinitions(model))
        {
            foreach (var name in (string[])["fillTextCluster", "strokeTextCluster"])
            {
                definition.Members.Add(CanvasTextOperation("CanvasText", name, new IdlType { Name = "undefined" },
                [
                    new IdlArgument { Name = "textCluster", Type = new IdlType { Name = "TextCluster" } },
                    new IdlArgument { Name = "x", Type = new IdlType { Name = "double" } },
                    new IdlArgument { Name = "y", Type = new IdlType { Name = "double" } },
                    options,
                ], source, Href, Experimental));
            }
        }
    }

    /// <summary>
    /// The shaped text of the 2D contexts, a proposal that replaces the text clusters of <c>TextMetrics</c> (see
    /// <see cref="AddCanvasTextClusters"/>), which Chromium implements behind a flag (ExtendedTextMetrics) and the tentative
    /// web-platform-tests of html/canvas/element/text test (2d.text.shape.*):
    /// <code>
    /// [Exposed=(Window,Worker)] interface ShapedText {
    ///   readonly attribute double width;
    ///   sequence&lt;GlyphRun&gt; glyphRuns();
    ///   sequence&lt;GlyphRun&gt; glyphClusters();
    ///   sequence&lt;GlyphRun&gt; glyphClusters([EnforceRange] unsigned long start, [EnforceRange] unsigned long end);
    ///   sequence&lt;DOMRectReadOnly&gt; selectionRects([EnforceRange] unsigned long start, [EnforceRange] unsigned long end);
    ///   DOMRectReadOnly actualBoundingBox([EnforceRange] unsigned long start, [EnforceRange] unsigned long end);
    ///   unsigned long advanceToPosition(double advance);
    ///   double positionToAdvance([EnforceRange] unsigned long position);
    /// };
    /// [Exposed=(Window,Worker)] interface GlyphRun {
    ///   readonly attribute unsigned long start;
    ///   readonly attribute unsigned long end;
    ///   readonly attribute double x;
    ///   readonly attribute double y;
    ///   readonly attribute double width;
    ///   TextMetrics textMetrics();
    /// };
    /// partial interface mixin CanvasText {
    ///   ShapedText shapeText(DOMString text);
    ///   undefined fillGlyphRun(GlyphRun glyphRun, double x, double y);
    ///   undefined strokeGlyphRun(GlyphRun glyphRun, double x, double y);
    ///   undefined fillGlyphRuns(sequence&lt;GlyphRun&gt; glyphRuns, double x, double y);
    ///   undefined strokeGlyphRuns(sequence&lt;GlyphRun&gt; glyphRuns, double x, double y);
    /// };
    /// </code>
    /// No explainer describes it yet: the IDL is the one of Chromium (third_party/blink/renderer/core/html/canvas/shaped_text.idl).
    /// </summary>
    private static void AddCanvasShapedText(IdlModel model)
    {
        const string Href = "https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/renderer/core/html/canvas/shaped_text.idl#ShapedText";
        const string GlyphRunHref = "https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/renderer/core/html/canvas/glyph_run.idl#GlyphRun";
        const string Experimental = "CanvasShapedText";
        if (!model.Definitions.TryGetValue("TextMetrics", out var metrics) || model.Definitions.ContainsKey("ShapedText"))
            return;

        var source = metrics.Source;
        model.Definitions["GlyphRun"] = new IdlDefinition
        {
            Name = "GlyphRun",
            Kind = IdlDefinitionKind.Interface,
            Members =
            [
                CanvasTextAttribute("GlyphRun", "start", "unsigned long", source, GlyphRunHref),
                CanvasTextAttribute("GlyphRun", "end", "unsigned long", source, GlyphRunHref),
                CanvasTextAttribute("GlyphRun", "x", "double", source, GlyphRunHref),
                CanvasTextAttribute("GlyphRun", "y", "double", source, GlyphRunHref),
                CanvasTextAttribute("GlyphRun", "width", "double", source, GlyphRunHref),
                CanvasTextOperation("GlyphRun", "textMetrics", new IdlType { Name = "TextMetrics" }, [], source, GlyphRunHref, experimental: null),
            ],
            ExtAttrs = [new IdlExtendedAttribute { Name = "Exposed", Values = ["Window", "Worker"] }],
            Source = source,
            Href = GlyphRunHref,
            SpecUrl = GlyphRunHref,
            ExperimentalApi = Experimental,
        };

        model.Definitions["ShapedText"] = new IdlDefinition
        {
            Name = "ShapedText",
            Kind = IdlDefinitionKind.Interface,
            Members =
            [
                CanvasTextAttribute("ShapedText", "width", "double", source, Href),
                CanvasTextOperation("ShapedText", "glyphRuns", SequenceOf("GlyphRun"), [], source, Href, experimental: null),
                CanvasTextOperation("ShapedText", "glyphClusters", SequenceOf("GlyphRun"), [], source, Href, experimental: null),
                CanvasTextOperation("ShapedText", "glyphClusters", SequenceOf("GlyphRun"), [.. CanvasTextRange()], source, Href, experimental: null),
                CanvasTextOperation("ShapedText", "selectionRects", SequenceOf("DOMRectReadOnly"), [.. CanvasTextRange()], source, Href, experimental: null),
                CanvasTextOperation("ShapedText", "actualBoundingBox", new IdlType { Name = "DOMRectReadOnly" }, [.. CanvasTextRange()], source, Href, experimental: null),
                CanvasTextOperation("ShapedText", "advanceToPosition", new IdlType { Name = "unsigned long" }, [new IdlArgument { Name = "advance", Type = new IdlType { Name = "double" } }], source, Href, experimental: null),
                CanvasTextOperation("ShapedText", "positionToAdvance", new IdlType { Name = "double" }, [new IdlArgument { Name = "position", Type = new IdlType { Name = "unsigned long" }, ExtAttrs = [new IdlExtendedAttribute { Name = "EnforceRange" }] }], source, Href, experimental: null),
            ],
            ExtAttrs = [new IdlExtendedAttribute { Name = "Exposed", Values = ["Window", "Worker"] }],
            Source = source,
            Href = Href,
            SpecUrl = Href,
            ExperimentalApi = Experimental,
        };

        foreach (var definition in GetCanvasTextDefinitions(model))
        {
            definition.Members.Add(CanvasTextOperation("CanvasText", "shapeText", new IdlType { Name = "ShapedText" }, [new IdlArgument { Name = "text", Type = new IdlType { Name = "DOMString" } }], source, Href, Experimental));
            foreach (var name in (string[])["fillGlyphRun", "strokeGlyphRun"])
            {
                definition.Members.Add(CanvasTextOperation("CanvasText", name, new IdlType { Name = "undefined" },
                [
                    new IdlArgument { Name = "glyphRun", Type = new IdlType { Name = "GlyphRun" } },
                    new IdlArgument { Name = "x", Type = new IdlType { Name = "double" } },
                    new IdlArgument { Name = "y", Type = new IdlType { Name = "double" } },
                ], source, Href, Experimental));
            }

            foreach (var name in (string[])["fillGlyphRuns", "strokeGlyphRuns"])
            {
                definition.Members.Add(CanvasTextOperation("CanvasText", name, new IdlType { Name = "undefined" },
                [
                    new IdlArgument { Name = "glyphRuns", Type = SequenceOf("GlyphRun") },
                    new IdlArgument { Name = "x", Type = new IdlType { Name = "double" } },
                    new IdlArgument { Name = "y", Type = new IdlType { Name = "double" } },
                ], source, Href, Experimental));
            }
        }
    }

    /// <summary>Returns the mixin <c>CanvasText</c> and the interfaces that include it (the members of the mixins are copied into the interfaces).</summary>
    private static IEnumerable<IdlDefinition> GetCanvasTextDefinitions(IdlModel model)
    {
        foreach (var name in (string[])["CanvasText", "CanvasRenderingContext2D", "OffscreenCanvasRenderingContext2D"])
        {
            if (model.Definitions.TryGetValue(name, out var definition) && definition.Members.Exists(m => m.Name == "measureText"))
                yield return definition;
        }
    }

    private static IdlType SequenceOf(string name) => new() { Generic = "sequence", Subtypes = [new IdlType { Name = name }] };

    /// <summary>The <c>[EnforceRange] unsigned long start, [EnforceRange] unsigned long end</c> arguments of the text ranges.</summary>
    private static IdlArgument[] CanvasTextRange() =>
    [
        new IdlArgument { Name = "start", Type = new IdlType { Name = "unsigned long" }, ExtAttrs = [new IdlExtendedAttribute { Name = "EnforceRange" }] },
        new IdlArgument { Name = "end", Type = new IdlType { Name = "unsigned long" }, ExtAttrs = [new IdlExtendedAttribute { Name = "EnforceRange" }] },
    ];

    private static IdlMember CanvasTextField(string definition, string name, string type, string source, string href) => new()
    {
        Kind = IdlMemberKind.Field,
        Name = name,
        Type = new IdlType { Name = type },
        Source = source,
        DeclaringDefinition = definition,
        Href = href,
    };

    private static IdlMember CanvasTextAttribute(string definition, string name, string type, string source, string href) => new()
    {
        Kind = IdlMemberKind.Attribute,
        Name = name,
        Type = new IdlType { Name = type },
        Readonly = true,
        Source = source,
        DeclaringDefinition = definition,
        Href = href,
    };

    private static IdlMember CanvasTextOperation(string definition, string name, IdlType type, List<IdlArgument> arguments, string source, string href, string? experimental) => new()
    {
        Kind = IdlMemberKind.Operation,
        Name = name,
        Type = type,
        Arguments = arguments,
        Source = source,
        DeclaringDefinition = definition,
        ExperimentalApi = experimental,
        Href = href,
    };

    /// <summary>
    /// The interpolation methods of the canvas gradients (https://github.com/whatwg/html/pull/10986), which the
    /// web-platform-tests of html/canvas/element/fill-and-stroke-styles test (2d.gradient.colorInterpolationMethod.html,
    /// 2d.gradient.hueInterpolationMethod.html). The enumeration of the color spaces has display-p3-linear, which the tests
    /// use:
    /// <code>
    /// enum ColorInterpolationMethod { "srgb", "hsl", "hwb", "srgb-linear", "display-p3", "display-p3-linear", "a98-rgb",
    ///   "prophoto-rgb", "rec2020", "lab", "oklab", "lch", "oklch", "xyz", "xyz-d50", "xyz-d65" };
    /// enum HueInterpolationMethod { "shorter", "longer", "increasing", "decreasing" };
    /// partial interface CanvasGradient {
    ///   attribute ColorInterpolationMethod colorInterpolationMethod;
    ///   attribute HueInterpolationMethod hueInterpolationMethod;
    /// };
    /// </code>
    /// The .NET properties are strings (no .NET type maps the enumerations).
    /// </summary>
    private static void AddCanvasGradientInterpolation(IdlModel model)
    {
        const string Href = "https://github.com/whatwg/html/pull/10986";
        if (!model.Definitions.TryGetValue("CanvasGradient", out var definition) || model.Definitions.ContainsKey("ColorInterpolationMethod"))
            return;

        model.Definitions["ColorInterpolationMethod"] = new IdlDefinition
        {
            Name = "ColorInterpolationMethod",
            Kind = IdlDefinitionKind.Enum,
            EnumValues = ["srgb", "hsl", "hwb", "srgb-linear", "display-p3", "display-p3-linear", "a98-rgb", "prophoto-rgb", "rec2020", "lab", "oklab", "lch", "oklch", "xyz", "xyz-d50", "xyz-d65"],
            Source = definition.Source,
            Href = Href,
            SpecUrl = Href,
        };
        model.Definitions["HueInterpolationMethod"] = new IdlDefinition
        {
            Name = "HueInterpolationMethod",
            Kind = IdlDefinitionKind.Enum,
            EnumValues = ["shorter", "longer", "increasing", "decreasing"],
            Source = definition.Source,
            Href = Href,
            SpecUrl = Href,
        };

        foreach (var (name, type) in (ReadOnlySpan<(string, string)>)[("colorInterpolationMethod", "ColorInterpolationMethod"), ("hueInterpolationMethod", "HueInterpolationMethod")])
        {
            var memberHref = Href + "#dom-canvasgradient-" + name.ToLowerInvariant();
            definition.Members.Add(new IdlMember
            {
                Kind = IdlMemberKind.Attribute,
                Name = name,
                Type = new IdlType { Name = type },
                Source = definition.Source,
                DeclaringDefinition = definition.Name,
                ExperimentalApi = "CanvasGradientInterpolation",
                Href = memberHref,
            });
        }
    }

    /// <summary>
    /// The changes of the WebMCP specification (https://webmachinelearning.github.io/webmcp/) that the IDL of webref does not
    /// have, and that the web-platform-tests of webmcp test (as Chromium implements them):
    /// <code>
    /// partial dictionary ModelContextTool { object outputSchema; };  // webmachinelearning/webmcp#254 (merged)
    ///
    /// partial interface ModelContext {
    ///   // webmachinelearning/webmcp#373
    ///   readonly attribute boolean toolsReady;
    ///   undefined waitForToolsUntil(Promise&lt;any&gt; promise);
    /// };
    /// </code>
    /// </summary>
    private static void AddWebMcpPullRequests(IdlModel model)
    {
        if (!model.Definitions.TryGetValue("ModelContext", out var modelContext))
            return;

        const string ToolsReadyHref = "https://pr-preview.s3.amazonaws.com/webmachinelearning/webmcp/pull/373.html#dom-modelcontext-toolsready";
        const string WaitForToolsUntilHref = "https://pr-preview.s3.amazonaws.com/webmachinelearning/webmcp/pull/373.html#dom-modelcontext-waitfortoolsuntil";
        var source = modelContext.Source;
        if (model.Definitions.TryGetValue("ModelContextTool", out var modelContextTool) && !modelContextTool.Members.Exists(m => m.Name == "outputSchema"))
        {
            modelContextTool.Members.Add(new IdlMember
            {
                Kind = IdlMemberKind.Field,
                Name = "outputSchema",
                Type = new IdlType { Name = "object" },
                Source = source,
                DeclaringDefinition = modelContextTool.Name,
                Href = "https://webmachinelearning.github.io/webmcp/#dom-modelcontexttool-outputschema",
            });
        }

        if (!modelContext.Members.Exists(m => m.Name == "toolsReady"))
        {
            modelContext.Members.Add(new IdlMember
            {
                Kind = IdlMemberKind.Attribute,
                Name = "toolsReady",
                Type = new IdlType { Name = "boolean" },
                Readonly = true,
                Source = source,
                DeclaringDefinition = modelContext.Name,
                Href = ToolsReadyHref,
                ExperimentalApi = "WebMcpToolReadiness",
            });
        }

        if (!modelContext.Members.Exists(m => m.Name == "waitForToolsUntil"))
        {
            modelContext.Members.Add(new IdlMember
            {
                Kind = IdlMemberKind.Operation,
                Name = "waitForToolsUntil",
                Type = new IdlType { Name = "undefined" },
                Arguments = [new IdlArgument { Name = "promise", Type = new IdlType { Generic = "Promise", Subtypes = [new IdlType { Name = "any" }] } }],
                Source = source,
                DeclaringDefinition = modelContext.Name,
                Href = WaitForToolsUntilHref,
                ExperimentalApi = "WebMcpToolReadiness",
            });
        }
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
