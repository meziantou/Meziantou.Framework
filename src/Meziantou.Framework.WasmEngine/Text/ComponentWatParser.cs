using System.Text;

namespace Meziantou.Framework.WasmEngine.Text;

/// <summary>
/// Converts a component of the text format to the binary format (https://github.com/WebAssembly/component-model/blob/main/design/mvp/Explainer.md).
/// </summary>
/// <remarks>
/// The definitions are encoded in the order of the text, and the abbreviations are expanded as the reference tools do: an inline
/// type is a new type, defined before the definition that uses it (in a core module type, an inline function type reuses a
/// function type declaration with the same signature), an inline instance of a <c>with</c> argument is defined before the
/// instantiation, the inline export aliases and the implicit outer
/// aliases (a name of an enclosing scope) are defined before the definition that uses them, and the inline exports are defined after
/// the last definition of the component (so that they don't change the indices of the following definitions). The consecutive
/// definitions of the same kind share a section.
/// </remarks>
internal sealed partial class ComponentWatParser
{
    private static readonly byte[] Preamble = [0x00, 0x61, 0x73, 0x6D, 0x0D, 0x00, 0x01, 0x00];

    private enum Sort
    {
        CoreFunc,
        CoreTable,
        CoreMemory,
        CoreGlobal,
        CoreTag,
        CoreType,
        CoreModule,
        CoreInstance,
        Func,
        Value,
        Type,
        Component,
        Instance,
    }

    private enum ScopeKind
    {
        Component,
        ComponentType,
        InstanceType,
        ModuleType,
    }

    /// <summary>The kinds of definitions: the ids of the sections of a component.</summary>
    private enum ItemKind : byte
    {
        CoreModule = 1,
        CoreInstance = 2,
        CoreType = 3,
        Component = 4,
        Instance = 5,
        Alias = 6,
        Type = 7,
        Canon = 8,
        Start = 9,
        Import = 10,
        Export = 11,
        Value = 12,
    }

    private sealed class Namespace
    {
        public Dictionary<string, uint> Names { get; } = new(StringComparer.Ordinal);

        public uint Count { get; private set; }

        public uint Add(string? name, Sort sort, int position)
        {
            if (name is not null && !Names.TryAdd(name, Count))
                throw new WatException("duplicate " + SortName(sort) + " identifier $" + name, position);

            return Count++;
        }
    }

    /// <summary>A component, a component type, an instance type or a core module type: the index spaces, and the encoded definitions or declarations.</summary>
    private sealed class Scope(ScopeKind kind, string? id, Scope? parent)
    {
        private readonly Namespace[] _namespaces = [.. Enumerable.Range(0, 13).Select(_ => new Namespace())];

        public ScopeKind Kind { get; } = kind;

        public string? Id { get; } = id;

        public Scope? Parent { get; } = parent;

        public Namespace this[Sort sort] => _namespaces[(int)sort];

        /// <summary>The function types of a module type, that an inline function type with the same signature reuses (the key is the signature).</summary>
        public Dictionary<string, uint> FunctionTypes { get; } = new(StringComparer.Ordinal);

        public List<(byte Id, ByteWriter Content, int Count)> Sections { get; } = [];

        /// <summary>The inline exports of the definitions, which are defined after the last definition of the scope.</summary>
        public List<(SNode Export, Sort Sort, uint Index)> InlineExports { get; } = [];

        public ByteWriter Declarations { get; } = new();

        public int DeclarationCount { get; private set; }

        public void Add(ItemKind kind, ByteWriter content, int position)
        {
            if (Kind == ScopeKind.Component)
            {
                var single = kind is ItemKind.CoreModule or ItemKind.Component or ItemKind.Start;
                if (!single && Sections.Count > 0 && Sections[^1].Id == (byte)kind)
                {
                    var last = Sections[^1];
                    last.Content.Append(content);
                    Sections[^1] = (last.Id, last.Content, last.Count + 1);
                }
                else
                {
                    Sections.Add(((byte)kind, content, single ? -1 : 1));
                }

                return;
            }

            byte tag;
            if (Kind == ScopeKind.ModuleType)
            {
                tag = kind switch
                {
                    ItemKind.Import => 0x00,
                    ItemKind.CoreType => 0x01,
                    ItemKind.Alias => 0x02,
                    ItemKind.Export => 0x03,
                    _ => throw new WatException("this definition is not allowed in a module type", position),
                };
            }
            else
            {
                tag = kind switch
                {
                    ItemKind.CoreType => 0x00,
                    ItemKind.Type => 0x01,
                    ItemKind.Alias => 0x02,
                    ItemKind.Import => 0x03,
                    ItemKind.Export => 0x04,
                    _ => throw new WatException("this definition is not allowed in a component or instance type", position),
                };
            }

            Declarations.Byte(tag);
            Declarations.Append(content);
            DeclarationCount++;
        }
    }

    private static readonly Dictionary<string, Sort> CoreSorts = new(StringComparer.Ordinal)
    {
        ["func"] = Sort.CoreFunc,
        ["table"] = Sort.CoreTable,
        ["memory"] = Sort.CoreMemory,
        ["global"] = Sort.CoreGlobal,
        ["tag"] = Sort.CoreTag,
        ["type"] = Sort.CoreType,
        ["module"] = Sort.CoreModule,
        ["instance"] = Sort.CoreInstance,
    };

    private static readonly Dictionary<string, Sort> ComponentSorts = new(StringComparer.Ordinal)
    {
        ["func"] = Sort.Func,
        ["value"] = Sort.Value,
        ["type"] = Sort.Type,
        ["component"] = Sort.Component,
        ["instance"] = Sort.Instance,
    };

    /// <summary>The inline definitions (types and instances) that are defined before the definition that uses them, and their indices.</summary>
    private readonly Dictionary<SNode, uint> _hoisted = [];
    private Scope _scope;

    private ComponentWatParser(Scope scope)
    {
        _scope = scope;
    }

    /// <summary>Converts a text that is a component: <c>(component ...)</c>.</summary>
    /// <exception cref="FormatException">The text is not valid.</exception>
    public static byte[] ParseComponent(string text)
    {
        var nodes = SExpressionReader.Parse(text);
        if (nodes is not [var node] || !node.IsListOf("component"))
            throw new WatException("expected a component", nodes.Count > 0 ? nodes[0].Position : 0);

        return ParseComponent(node);
    }

    /// <summary>
    /// Converts a <c>(component ...)</c> node: the text format, <c>(component binary "..."*)</c> or <c>(component quote "..."*)</c>,
    /// optionally with the <c>definition</c> keyword of the scripts (<c>(component definition $id? ...)</c>).
    /// </summary>
    /// <exception cref="FormatException">The text is not valid.</exception>
    public static byte[] ParseComponent(SNode component)
    {
        if (!component.IsListOf("component"))
            throw new WatException("expected a component", component.Position);

        var items = component.Items!;
        var index = 1;
        if (index < items.Count && items[index].IsKeyword("definition"))
        {
            index++;
        }

        var id = ReadId(items, ref index);
        var parser = new ComponentWatParser(new Scope(ScopeKind.Component, id, parent: null));
        return parser.EncodeComponentContent(items, index, id, parent: null);
    }

    /// <summary>Encodes the content of a component (after its id): the binary format, a quoted text, or the fields.</summary>
    private byte[] EncodeComponentContent(List<SNode> items, int index, string? id, Scope? parent)
    {
        if (index < items.Count && items[index].IsKeyword("binary"))
            return ConcatenateStrings(items, index + 1);

        if (index < items.Count && items[index].IsKeyword("quote"))
        {
            string text;
            var position = items[index].Position;
            try
            {
                text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(ConcatenateStrings(items, index + 1));
            }
            catch (DecoderFallbackException)
            {
                throw new WatException("malformed UTF-8 encoding", position);
            }

            var fields = SExpressionReader.Parse(text);
            return EncodeComponentFields(fields, 0, id, parent);
        }

        return EncodeComponentFields(items, index, id, parent);
    }

    private static byte[] ConcatenateStrings(List<SNode> items, int start)
    {
        var bytes = new List<byte>();
        for (var i = start; i < items.Count; i++)
        {
            if (items[i] is not { IsList: false, Kind: TokenKind.String } part)
                throw new WatException("expected a string", items[i].Position);

            bytes.AddRange(part.Bytes);
        }

        return [.. bytes];
    }

    private byte[] EncodeComponentFields(List<SNode> fields, int start, string? id, Scope? parent)
    {
        var scope = parent is null ? _scope : new Scope(ScopeKind.Component, id, parent);
        var previous = _scope;
        _scope = scope;
        try
        {
            for (var i = start; i < fields.Count; i++)
            {
                WatException.ThrowIfNestedTooDeeply(fields[i].Position);
                EncodeComponentField(fields[i]);
            }

            EncodePendingInlineExports();
        }
        finally
        {
            _scope = previous;
        }

        var writer = new ByteWriter();
        writer.Bytes(Preamble);
        foreach (var (sectionId, content, count) in scope.Sections)
        {
            if (count < 0)
            {
                writer.Section(sectionId, content);
            }
            else
            {
                writer.Section(sectionId, content, count);
            }
        }

        return writer.ToArray();
    }

    // ------------------------------------------------------------------------------------------------------------
    // The fields of a component
    // ------------------------------------------------------------------------------------------------------------

    private void EncodeComponentField(SNode field)
    {
        if (!field.IsList || field.Head is not { } head)
            throw new WatException("malformed component field", field.Position);

        switch (head)
        {
            case "core":
                EncodeCoreField(field);
                break;

            case "component":
                EncodeNestedComponent(field);
                break;

            case "instance":
                EncodeInstance(field);
                break;

            case "alias":
                EncodeAlias(field);
                break;

            case "type":
                EncodeTypeField(field);
                break;

            case "canon":
                EncodeCanon(field, inverted: false, invertedId: null, invertedFuncType: null);
                break;

            case "start":
                EncodeStart(field);
                break;

            case "import":
                EncodeImportOrExportDeclaration(field, ItemKind.Import);
                break;

            case "export":
                EncodeExport(field);
                break;

            case "func":
                EncodeFuncField(field);
                break;

            case "value":
                EncodeValueField(field);
                break;

            default:
                throw new WatException("unknown component field " + head, field.Position);
        }
    }

    /// <summary>The abbreviations of a definition: <c>(sort $id? (export "name")* (import "name")? ...)</c>, or <c>(sort $id? (alias ...))</c>.</summary>
    private readonly record struct DefinitionHeader(string? Id, List<SNode> Exports, SNode? Import, SNode? Alias, int BodyStart);

    private static DefinitionHeader ReadDefinitionHeader(List<SNode> items, int index)
    {
        var id = ReadId(items, ref index);
        var exports = new List<SNode>();
        SNode? import = null;
        while (index < items.Count)
        {
            if (IsInlineExport(items[index]))
            {
                exports.Add(items[index++]);
            }
            else if (import is null && IsInlineImport(items[index]))
            {
                import = items[index++];
            }
            else
            {
                break;
            }
        }

        // (sort $id? (alias export $i "name")): an alias definition (alias ... (sort $id?)) ends with its sort instead (it is a field of a component)
        SNode? alias = null;
        if (import is null && index == items.Count - 1 && items[index].IsListOf("alias") && !items[index].Items![^1].IsList)
        {
            alias = items[index];
        }

        return new DefinitionHeader(id, exports, import, alias, index);
    }

    private static bool IsAttribute(SNode node) => node.IsListOf("implements") || node.IsListOf("version-suffix") || node.IsListOf("external-id");

    /// <summary>An inline export of a definition: <c>(export "name" attribute*)</c>.</summary>
    private static bool IsInlineExport(SNode node) => node.IsListOf("export") && IsNameAndAttributes(node.Items!);

    /// <summary>An inline import of a definition: <c>(import "name" attribute*)</c>.</summary>
    private static bool IsInlineImport(SNode node) => node.IsListOf("import") && IsNameAndAttributes(node.Items!);

    private static bool IsNameAndAttributes(List<SNode> items)
    {
        if (items.Count < 2 || items[1] is not { IsList: false, Kind: TokenKind.String })
            return false;

        for (var i = 2; i < items.Count; i++)
        {
            if (!IsAttribute(items[i]))
                return false;
        }

        return true;
    }

    /// <summary>Creates the extern type of an inline import: <c>(sort $id? body...)</c>.</summary>
    private static SNode CreateExternType(SNode field, List<SNode> heads, string? id, List<SNode> items, int bodyStart)
    {
        var result = new List<SNode>(heads);
        if (id is not null)
        {
            result.Add(SNode.Token(field.Position, TokenKind.Id, id));
        }

        for (var i = bodyStart; i < items.Count; i++)
        {
            result.Add(items[i]);
        }

        return SNode.List(field.Position, result);
    }

    /// <summary>Encodes an import of the abbreviation <c>(sort $id? (export ...)* (import "name") type)</c>, and its inline exports.</summary>
    private void EncodeInlineImport(SNode field, List<SNode> heads, DefinitionHeader header)
    {
        var import = header.Import!;
        var externType = CreateExternType(field, heads, header.Id, field.Items!, header.BodyStart);
        var (sort, index) = EncodeImportOrExportDeclaration(import, import.Items!, 1, externType, ItemKind.Import);
        EncodeInlineExports(header.Exports, sort, index);
    }

    private void EncodeCoreField(SNode field)
    {
        var items = field.Items!;
        if (items.Count < 2 || items[1] is not { IsList: false, Kind: TokenKind.Keyword } kind)
            throw new WatException("malformed core definition", field.Position);

        switch (kind.Text)
        {
            case "module":
                EncodeCoreModule(field);
                break;

            case "instance":
                EncodeCoreInstance(field);
                break;

            case "type" or "rec":
                EncodeCoreType(field);
                break;

            case "func" or "table" or "memory" or "global" or "tag":
            {
                var sort = CoreSorts[kind.Text];
                var header = ReadDefinitionHeader(items, 2);
                if (header.Alias is not null)
                {
                    EncodeAliasTarget(header.Alias, sort, header.Id);
                }
                else if (sort == Sort.CoreFunc && header.Exports.Count == 0 && header.Import is null && header.BodyStart == items.Count - 1 && items[^1].IsListOf("canon"))
                {
                    EncodeCanon(items[^1], inverted: true, header.Id, invertedFuncType: null);
                }
                else
                {
                    throw new WatException("malformed core " + kind.Text, field.Position);
                }

                break;
            }

            default:
                throw new WatException("unknown core definition " + kind.Text, field.Position);
        }
    }

    private void EncodeCoreModule(SNode field)
    {
        var items = field.Items!;
        var header = ReadDefinitionHeader(items, 2);
        if (header.Alias is not null)
        {
            var aliasIndex = EncodeAliasTarget(header.Alias, Sort.CoreModule, header.Id);
            EncodeInlineExports(header.Exports, Sort.CoreModule, aliasIndex);
            return;
        }

        if (header.Import is not null)
        {
            EncodeInlineImport(field, [SNode.Token(field.Position, TokenKind.Keyword, "core"), SNode.Token(field.Position, TokenKind.Keyword, "module")], header);
            return;
        }

        var module = new List<SNode> { SNode.Token(field.Position, TokenKind.Keyword, "module") };
        for (var i = header.BodyStart; i < items.Count; i++)
        {
            module.Add(items[i]);
        }

        var content = new ByteWriter();
        content.Bytes(WatParser.EncodeModule(SNode.List(field.Position, module)));
        _scope.Add(ItemKind.CoreModule, content, field.Position);
        var index = _scope[Sort.CoreModule].Add(header.Id, Sort.CoreModule, field.Position);
        EncodeInlineExports(header.Exports, Sort.CoreModule, index);
    }

    private void EncodeCoreInstance(SNode field)
    {
        var items = field.Items!;
        var header = ReadDefinitionHeader(items, 2);
        if (header.Alias is not null)
        {
            EncodeAliasTarget(header.Alias, Sort.CoreInstance, header.Id);
            return;
        }

        if (header.Exports.Count > 0 || header.Import is not null)
            throw new WatException("a core instance cannot be imported or exported", field.Position);

        var content = new ByteWriter();
        var index = header.BodyStart;
        if (index < items.Count && items[index].IsListOf("instantiate"))
        {
            if (index != items.Count - 1)
                throw new WatException("malformed core instance", field.Position);

            var instantiate = items[index].Items!;
            if (instantiate.Count < 2)
                throw new WatException("malformed instantiate", items[index].Position);

            // The inline instances of the arguments are defined first
            for (var i = 2; i < instantiate.Count; i++)
            {
                if (GetWithArgument(instantiate[i]) is { } argument && IsInstanceBundle(argument))
                {
                    _hoisted[argument] = EncodeCoreInstanceBundle(argument, argument.Items!, 1, id: null);
                }
            }

            var module = ResolveReference(instantiate[1], Sort.CoreModule);
            var arguments = new ByteWriter();
            for (var i = 2; i < instantiate.Count; i++)
            {
                var argument = GetWithArgument(instantiate[i]) ?? throw new WatException("malformed instantiate argument", instantiate[i].Position);
                WriteName(instantiate[i].Items![1], arguments);
                arguments.Byte(0x12);
                arguments.U32(_hoisted.TryGetValue(argument, out var hoisted) ? hoisted : ResolveReference(argument, Sort.CoreInstance));
            }

            content.Byte(0x00);
            content.U32(module);
            content.Vector(arguments, instantiate.Count - 2);
            _scope.Add(ItemKind.CoreInstance, content, field.Position);
            _scope[Sort.CoreInstance].Add(header.Id, Sort.CoreInstance, field.Position);
            return;
        }

        EncodeCoreInstanceBundle(field, items, index, header.Id);
    }

    /// <summary>The argument of <c>(with "name" argument)</c>, or null when the node is not a with.</summary>
    private static SNode? GetWithArgument(SNode node) => node.IsListOf("with") && node.Items!.Count == 3 && node.Items[1] is { IsList: false, Kind: TokenKind.String } ? node.Items[2] : null;

    /// <summary>An inline instance of a with argument: <c>(instance (export ...)*)</c>.</summary>
    private static bool IsInstanceBundle(SNode node) => node.IsListOf("instance") && node.Items!.Skip(1).All(item => item.IsListOf("export"));

    /// <summary>Encodes a core instance of exports: <c>(export "name" (sort idx))*</c>.</summary>
    private uint EncodeCoreInstanceBundle(SNode node, List<SNode> items, int start, string? id)
    {
        var exports = new ByteWriter();
        for (var i = start; i < items.Count; i++)
        {
            var export = items[i];
            if (!export.IsListOf("export") || export.Items!.Count != 3)
                throw new WatException("malformed core instance export", export.Position);

            WriteName(export.Items[1], exports);
            var index = ResolveExternIndex(export.Items[2], core: true, out var sort);
            exports.Byte(CoreSortCode(sort, export.Position));
            exports.U32(index);
        }

        var content = new ByteWriter();
        content.Byte(0x01);
        content.Vector(exports, items.Count - start);
        _scope.Add(ItemKind.CoreInstance, content, node.Position);
        return _scope[Sort.CoreInstance].Add(id, Sort.CoreInstance, node.Position);
    }

    private void EncodeNestedComponent(SNode field)
    {
        var items = field.Items!;
        var header = ReadDefinitionHeader(items, 1);
        if (header.Alias is not null)
        {
            var aliasIndex = EncodeAliasTarget(header.Alias, Sort.Component, header.Id);
            EncodeInlineExports(header.Exports, Sort.Component, aliasIndex);
            return;
        }

        if (header.Import is not null)
        {
            EncodeInlineImport(field, [SNode.Token(field.Position, TokenKind.Keyword, "component")], header);
            return;
        }

        var bytes = EncodeComponentContent(items, header.BodyStart, header.Id, _scope);
        var content = new ByteWriter();
        content.Bytes(bytes);
        _scope.Add(ItemKind.Component, content, field.Position);
        var index = _scope[Sort.Component].Add(header.Id, Sort.Component, field.Position);
        EncodeInlineExports(header.Exports, Sort.Component, index);
    }

    private void EncodeInstance(SNode field)
    {
        var items = field.Items!;
        var header = ReadDefinitionHeader(items, 1);
        if (header.Alias is not null)
        {
            var aliasIndex = EncodeAliasTarget(header.Alias, Sort.Instance, header.Id);
            EncodeInlineExports(header.Exports, Sort.Instance, aliasIndex);
            return;
        }

        if (header.Import is not null)
        {
            EncodeInlineImport(field, [SNode.Token(field.Position, TokenKind.Keyword, "instance")], header);
            return;
        }

        var index = header.BodyStart;
        uint instanceIndex;
        if (index < items.Count && items[index].IsListOf("instantiate"))
        {
            if (index != items.Count - 1)
                throw new WatException("malformed instance", field.Position);

            var instantiate = items[index].Items!;
            if (instantiate.Count < 2)
                throw new WatException("malformed instantiate", items[index].Position);

            // The inline instances of the arguments are defined first
            for (var i = 2; i < instantiate.Count; i++)
            {
                if (GetWithArgument(instantiate[i]) is { } argument && IsInstanceBundle(argument))
                {
                    _hoisted[argument] = EncodeInstanceBundle(argument, argument.Items!, 1, id: null);
                }
            }

            var component = ResolveReference(instantiate[1], Sort.Component);
            var arguments = new ByteWriter();
            for (var i = 2; i < instantiate.Count; i++)
            {
                var argument = GetWithArgument(instantiate[i]) ?? throw new WatException("malformed instantiate argument", instantiate[i].Position);
                WriteName(instantiate[i].Items![1], arguments);
                if (_hoisted.TryGetValue(argument, out var hoisted))
                {
                    WriteSort(Sort.Instance, arguments);
                    arguments.U32(hoisted);
                }
                else
                {
                    var argumentIndex = ResolveExternIndex(argument, core: false, out var sort);
                    WriteSort(sort, arguments);
                    arguments.U32(argumentIndex);
                }
            }

            var content = new ByteWriter();
            content.Byte(0x00);
            content.U32(component);
            content.Vector(arguments, instantiate.Count - 2);
            _scope.Add(ItemKind.Instance, content, field.Position);
            instanceIndex = _scope[Sort.Instance].Add(header.Id, Sort.Instance, field.Position);
        }
        else
        {
            instanceIndex = EncodeInstanceBundle(field, items, index, header.Id);
        }

        EncodeInlineExports(header.Exports, Sort.Instance, instanceIndex);
    }

    /// <summary>Encodes an instance of exports: <c>(export "name" attribute* (sort idx))*</c>.</summary>
    private uint EncodeInstanceBundle(SNode node, List<SNode> items, int start, string? id)
    {
        var exports = new ByteWriter();
        for (var i = start; i < items.Count; i++)
        {
            var export = items[i];
            if (!export.IsListOf("export"))
                throw new WatException("malformed instance export", export.Position);

            var exportItems = export.Items!;
            var index = 1;
            WriteExternName(exportItems, ref index, exports);
            if (index != exportItems.Count - 1)
                throw new WatException("malformed instance export", export.Position);

            var exportIndex = ResolveExternIndex(exportItems[index], core: false, out var sort);
            WriteSort(sort, exports);
            exports.U32(exportIndex);
        }

        var content = new ByteWriter();
        content.Byte(0x01);
        content.Vector(exports, items.Count - start);
        _scope.Add(ItemKind.Instance, content, node.Position);
        return _scope[Sort.Instance].Add(id, Sort.Instance, node.Position);
    }

    private void EncodeFuncField(SNode field)
    {
        var items = field.Items!;
        var header = ReadDefinitionHeader(items, 1);
        if (header.Alias is not null)
        {
            var aliasIndex = EncodeAliasTarget(header.Alias, Sort.Func, header.Id);
            EncodeInlineExports(header.Exports, Sort.Func, aliasIndex);
            return;
        }

        if (header.Import is not null)
        {
            EncodeInlineImport(field, [SNode.Token(field.Position, TokenKind.Keyword, "func")], header);
            return;
        }

        // (func $id? (export ...)* typeuse (canon lift ...))
        if (header.BodyStart >= items.Count || !items[^1].IsListOf("canon"))
            throw new WatException("malformed func: expected a canonical definition", field.Position);

        var index = EncodeCanon(items[^1], inverted: true, header.Id, (field, header.BodyStart, items.Count - 1));
        EncodeInlineExports(header.Exports, Sort.Func, index);
    }

    private void EncodeTypeField(SNode field)
    {
        var items = field.Items!;
        var header = ReadDefinitionHeader(items, 1);
        if (header.Alias is not null)
        {
            var aliasIndex = EncodeAliasTarget(header.Alias, Sort.Type, header.Id);
            EncodeInlineExports(header.Exports, Sort.Type, aliasIndex);
            return;
        }

        if (header.Import is not null)
        {
            EncodeInlineImport(field, [SNode.Token(field.Position, TokenKind.Keyword, "type")], header);
            return;
        }

        if (header.BodyStart != items.Count - 1)
            throw new WatException("malformed type definition", field.Position);

        var index = EncodeTypeDefinition(items[header.BodyStart], header.Id, field.Position);
        EncodeInlineExports(header.Exports, Sort.Type, index);
    }

    private void EncodeValueField(SNode field)
    {
        var items = field.Items!;
        var header = ReadDefinitionHeader(items, 1);
        if (header.Alias is not null)
        {
            var aliasIndex = EncodeAliasTarget(header.Alias, Sort.Value, header.Id);
            EncodeInlineExports(header.Exports, Sort.Value, aliasIndex);
            return;
        }

        if (header.Import is not null)
        {
            EncodeInlineImport(field, [SNode.Token(field.Position, TokenKind.Keyword, "value")], header);
            return;
        }

        var index = EncodeValue(field, items, header.BodyStart, header.Id);
        EncodeInlineExports(header.Exports, Sort.Value, index);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Imports and exports
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>Encodes an import or an export declaration (of a component type or an instance type): <c>(import "name" attribute* externtype)</c>.</summary>
    private void EncodeImportOrExportDeclaration(SNode field, ItemKind kind)
    {
        var items = field.Items!;
        if (items.Count < 3)
            throw new WatException("malformed " + (kind == ItemKind.Import ? "import" : "export"), field.Position);

        EncodeImportOrExportDeclaration(field, items, 1, items[^1], kind);
    }

    private (Sort Sort, uint Index) EncodeImportOrExportDeclaration(SNode field, List<SNode> items, int nameIndex, SNode externType, ItemKind kind)
    {
        var content = new ByteWriter();
        var index = nameIndex;
        WriteExternName(items, ref index, content);

        // The extern type is the last item, or the type of an inline import (it is not in the items)
        if (index != items.Count - (ReferenceEquals(items[^1], externType) ? 1 : 0))
            throw new WatException("malformed " + (kind == ItemKind.Import ? "import" : "export"), field.Position);

        PrepareExternType(externType);
        var (sort, id) = EncodeExternType(externType, content);
        _scope.Add(kind, content, field.Position);
        return (sort, _scope[sort].Add(id, sort, externType.Position));
    }

    /// <summary>Encodes an export definition: <c>(export $id? "name" attribute* (sort idx) externtype?)</c>.</summary>
    private void EncodeExport(SNode field)
    {
        var items = field.Items!;
        var index = 1;
        var id = ReadId(items, ref index);
        var content = new ByteWriter();
        WriteExternName(items, ref index, content);
        if (index >= items.Count || index < items.Count - 2)
            throw new WatException("malformed export", field.Position);

        var ascription = index + 1 < items.Count ? items[index + 1] : null;
        if (ascription is not null)
        {
            PrepareExternType(ascription);
        }

        var exportIndex = ResolveExternIndex(items[index], core: false, out var sort);
        WriteSort(sort, content);
        content.U32(exportIndex);
        if (ascription is null)
        {
            content.Byte(0x00);
        }
        else
        {
            content.Byte(0x01);
            var (ascriptionSort, _) = EncodeExternType(ascription, content);
            if (ascriptionSort != sort)
                throw new WatException("the type of the export is not of its sort", ascription.Position);
        }

        _scope.Add(ItemKind.Export, content, field.Position);
        _scope[sort].Add(id, sort, field.Position);
    }

    /// <summary>
    /// Adds the inline exports of a definition (<c>(export "name" attribute*)*</c>) to the scope: as with the reference tools, they are
    /// defined after the last definition, so that they don't change the indices of the following definitions.
    /// </summary>
    private void EncodeInlineExports(List<SNode> exports, Sort sort, uint index)
    {
        foreach (var export in exports)
        {
            _scope.InlineExports.Add((export, sort, index));
        }
    }

    /// <summary>Encodes the inline exports of the definitions of the scope.</summary>
    private void EncodePendingInlineExports()
    {
        foreach (var (export, sort, index) in _scope.InlineExports)
        {
            var content = new ByteWriter();
            var nameIndex = 1;
            WriteExternName(export.Items!, ref nameIndex, content);
            WriteSort(sort, content);
            content.U32(index);
            content.Byte(0x00);
            _scope.Add(ItemKind.Export, content, export.Position);
            _scope[sort].Add(null, sort, export.Position);
        }
    }

    /// <summary>Writes a name and its attributes: <c>"name" (implements "name")? (version-suffix "suffix")? (external-id "id")?</c>.</summary>
    private static void WriteExternName(List<SNode> items, ref int index, ByteWriter writer)
    {
        if (index >= items.Count)
            throw new WatException("expected a name", items[^1].Position);

        var name = items[index++];
        var attributes = new List<SNode>();
        while (index < items.Count && IsAttribute(items[index]))
        {
            var attribute = items[index++];
            if (attributes.Exists(other => other.Head == attribute.Head))
                throw new WatException("duplicate `" + attribute.Head + "` attribute", attribute.Position);

            attributes.Add(attribute);
        }

        if (attributes.Count == 0)
        {
            writer.Byte(0x00);
            WriteName(name, writer);
            return;
        }

        writer.Byte(0x02);
        WriteName(name, writer);
        writer.U32((uint)attributes.Count);
        foreach (var attribute in attributes)
        {
            var attributeItems = attribute.Items!;
            if (attributeItems.Count != 2)
                throw new WatException("malformed " + attribute.Head, attribute.Position);

            writer.Byte(attribute.Head switch
            {
                "implements" => 0x00,
                "version-suffix" => 0x01,
                _ => 0x02,
            });
            WriteName(attributeItems[1], writer);
        }
    }

    private static void WriteName(SNode node, ByteWriter writer)
    {
        if (node is not { IsList: false, Kind: TokenKind.String })
            throw new WatException("expected a string", node.Position);

        writer.U32((uint)node.Bytes.Length);
        writer.Bytes(node.Bytes);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Aliases
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>Encodes <c>(alias export idx "name" (sort $id?))</c>, <c>(alias core export idx "name" (core sort $id?))</c> or <c>(alias outer idx idx (sort $id?))</c>.</summary>
    private void EncodeAlias(SNode field)
    {
        var items = field.Items!;
        if (items.Count < 3 || !items[^1].IsList)
            throw new WatException("malformed alias", field.Position);

        var sortItems = items[^1].Items!;
        var index = 0;
        var sort = ReadSort(sortItems, ref index, core: _scope.Kind == ScopeKind.ModuleType, items[^1].Position);
        var id = ReadId(sortItems, ref index);
        if (index != sortItems.Count)
            throw new WatException("malformed alias", items[^1].Position);

        EncodeAliasTarget(field, items, 1, items.Count - 1, sort, id);
    }

    /// <summary>Encodes the alias of the abbreviation <c>(sort $id? (alias target))</c>.</summary>
    private uint EncodeAliasTarget(SNode alias, Sort sort, string? id) => EncodeAliasTarget(alias, alias.Items!, 1, alias.Items!.Count, sort, id);

    private uint EncodeAliasTarget(SNode node, List<SNode> items, int start, int end, Sort sort, string? id)
    {
        var content = new ByteWriter();
        var count = end - start;
        if (count == 3 && items[start].IsKeyword("export"))
        {
            var instance = ResolveReference(items[start + 1], Sort.Instance);
            WriteSort(sort, content);
            content.Byte(0x00);
            content.U32(instance);
            WriteName(items[start + 2], content);
        }
        else if (count == 4 && items[start].IsKeyword("core") && items[start + 1].IsKeyword("export"))
        {
            var instance = ResolveReference(items[start + 2], Sort.CoreInstance);
            WriteSort(sort, content);
            content.Byte(0x01);
            content.U32(instance);
            WriteName(items[start + 3], content);
        }
        else if (count == 3 && items[start].IsKeyword("outer"))
        {
            var (depth, index) = ResolveOuter(items[start + 1], items[start + 2], sort);
            WriteOuterAlias(sort, depth, index, content);
        }
        else
        {
            throw new WatException("malformed alias", node.Position);
        }

        _scope.Add(ItemKind.Alias, content, node.Position);
        return _scope[sort].Add(id, sort, node.Position);
    }

    private void WriteOuterAlias(Sort sort, uint depth, uint index, ByteWriter writer)
    {
        if (_scope.Kind == ScopeKind.ModuleType)
        {
            // core:alias: the sort is type (0x10) and the target is outer (0x01)
            writer.Byte(0x10);
            writer.Byte(0x01);
        }
        else
        {
            WriteSort(sort, writer);
            writer.Byte(0x02);
        }

        writer.U32(depth);
        writer.U32(index);
    }

    /// <summary>Resolves the target of an outer alias: the number of enclosing scopes, and the index in the target scope.</summary>
    private (uint Depth, uint Index) ResolveOuter(SNode scopeNode, SNode indexNode, Sort sort)
    {
        uint depth;
        Scope? target;
        if (scopeNode is { IsList: false, Kind: TokenKind.Id })
        {
            depth = 0;
            target = _scope;
            while (target is not null && !string.Equals(target.Id, scopeNode.Text, StringComparison.Ordinal))
            {
                target = target.Parent;
                depth++;
            }

            if (target is null)
                throw new WatException("outer component $" + scopeNode.Text + " not found", scopeNode.Position);
        }
        else
        {
            depth = ParseIndex(scopeNode);
            target = _scope;
            for (var i = 0u; i < depth && target is not null; i++)
            {
                target = target.Parent;
            }
        }

        if (indexNode is { IsList: false, Kind: TokenKind.Id })
        {
            if (target is null || !target[sort].Names.TryGetValue(indexNode.Text, out var index))
                throw new WatException("unknown " + SortName(sort) + " $" + indexNode.Text, indexNode.Position);

            return (depth, index);
        }

        return (depth, ParseIndex(indexNode));
    }

    // ------------------------------------------------------------------------------------------------------------
    // Indices
    // ------------------------------------------------------------------------------------------------------------

    private static string? ReadId(List<SNode> items, ref int index)
    {
        if (index < items.Count && items[index] is { IsList: false, Kind: TokenKind.Id } id)
        {
            index++;
            return id.Text;
        }

        return null;
    }

    private static bool IsIndex(SNode node) => node is { IsList: false, Kind: TokenKind.Id or TokenKind.Number };

    private static uint ParseIndex(SNode node)
    {
        if (node is { IsList: false, Kind: TokenKind.Number } && WatNumbers.TryParseUnsigned(node.Text, uint.MaxValue, out var value))
            return (uint)value;

        throw new WatException("expected an index", node.Position);
    }

    private static bool IsCoreSort(Sort sort) => sort <= Sort.CoreInstance;

    private static string SortName(Sort sort) => sort switch
    {
        Sort.CoreFunc => "core func",
        Sort.CoreTable => "core table",
        Sort.CoreMemory => "core memory",
        Sort.CoreGlobal => "core global",
        Sort.CoreTag => "core tag",
        Sort.CoreType => "core type",
        Sort.CoreModule => "core module",
        Sort.CoreInstance => "core instance",
        Sort.Func => "func",
        Sort.Value => "value",
        Sort.Type => "type",
        Sort.Component => "component",
        _ => "instance",
    };

    /// <summary>Reads a sort: <c>core? sort</c> (in a core context, the core sorts don't have the <c>core</c> prefix).</summary>
    private static Sort ReadSort(List<SNode> items, ref int index, bool core, int position)
    {
        if (index < items.Count && items[index].IsKeyword("core"))
        {
            core = true;
            index++;
        }

        if (index >= items.Count || items[index] is not { IsList: false, Kind: TokenKind.Keyword } keyword || !(core ? CoreSorts : ComponentSorts).TryGetValue(keyword.Text, out var sort))
            throw new WatException("expected a sort", position);

        index++;
        return sort;
    }

    private static void WriteSort(Sort sort, ByteWriter writer)
    {
        if (IsCoreSort(sort))
        {
            writer.Byte(0x00);
            writer.Byte(CoreSortCode(sort, 0));
            return;
        }

        writer.Byte(sort switch
        {
            Sort.Func => 0x01,
            Sort.Value => 0x02,
            Sort.Type => 0x03,
            Sort.Component => 0x04,
            _ => 0x05,
        });
    }

    private static byte CoreSortCode(Sort sort, int position) => sort switch
    {
        Sort.CoreFunc => 0x00,
        Sort.CoreTable => 0x01,
        Sort.CoreMemory => 0x02,
        Sort.CoreGlobal => 0x03,
        Sort.CoreTag => 0x04,
        Sort.CoreType => 0x10,
        Sort.CoreModule => 0x11,
        Sort.CoreInstance => 0x12,
        _ => throw new WatException("expected a core sort", position),
    };

    /// <summary>
    /// Resolves an index: a number, or a name of the scope. A name of an enclosing scope (a core module, a core type, a type or a
    /// component) defines an outer alias in the current scope, which binds the name.
    /// </summary>
    private uint ResolveIndex(SNode node, Sort sort)
    {
        if (node is not { IsList: false, Kind: TokenKind.Id })
            return ParseIndex(node);

        var depth = 0u;
        for (var scope = _scope; scope is not null; scope = scope.Parent, depth++)
        {
            if (!scope[sort].Names.TryGetValue(node.Text, out var index))
                continue;

            if (depth == 0)
                return index;

            if (sort is not (Sort.CoreModule or Sort.CoreType or Sort.Type or Sort.Component))
                throw new WatException("outer item `" + node.Text + "` is not a module, type, or component", node.Position);

            var content = new ByteWriter();
            WriteOuterAlias(sort, depth, index, content);
            _scope.Add(ItemKind.Alias, content, node.Position);
            return _scope[sort].Add(node.Text, sort, node.Position);
        }

        throw new WatException("unknown " + SortName(sort) + " $" + node.Text, node.Position);
    }

    /// <summary>Resolves a reference of a sort: an index, or <c>(sort idx "name"*)</c> (the names are inline export aliases).</summary>
    private uint ResolveReference(SNode node, Sort sort)
    {
        if (!node.IsList)
            return ResolveIndex(node, sort);

        var items = node.Items!;
        var index = 0;
        var actual = ReadSort(items, ref index, IsCoreSort(sort), node.Position);
        if (actual != sort)
            throw new WatException("expected a " + SortName(sort) + " reference", node.Position);

        return ResolveSortIndex(node, items, index, sort);
    }

    /// <summary>Resolves an extern index: <c>(sort idx "name"*)</c>.</summary>
    private uint ResolveExternIndex(SNode node, bool core, out Sort sort)
    {
        if (!node.IsList)
            throw new WatException("expected a sort and an index", node.Position);

        var items = node.Items!;
        var index = 0;
        sort = ReadSort(items, ref index, core, node.Position);
        return ResolveSortIndex(node, items, index, sort);
    }

    private uint ResolveSortIndex(SNode node, List<SNode> items, int index, Sort sort)
    {
        if (index >= items.Count || !IsIndex(items[index]))
            throw new WatException("expected an index", node.Position);

        var target = items[index++];
        if (index == items.Count)
            return ResolveIndex(target, sort);

        for (var i = index; i < items.Count; i++)
        {
            if (items[i] is not { IsList: false, Kind: TokenKind.String })
                throw new WatException("expected an export name", items[i].Position);
        }

        if (IsCoreSort(sort) && sort != Sort.CoreModule)
        {
            if (index != items.Count - 1)
                throw new WatException("core instance export lists can only have one entry", node.Position);

            var coreInstance = ResolveIndex(target, Sort.CoreInstance);
            var content = new ByteWriter();
            WriteSort(sort, content);
            content.Byte(0x01);
            content.U32(coreInstance);
            WriteName(items[index], content);
            _scope.Add(ItemKind.Alias, content, node.Position);
            return _scope[sort].Add(null, sort, node.Position);
        }

        var instance = ResolveIndex(target, Sort.Instance);
        for (var i = index; i < items.Count; i++)
        {
            var aliasSort = i == items.Count - 1 ? sort : Sort.Instance;
            var content = new ByteWriter();
            WriteSort(aliasSort, content);
            content.Byte(0x00);
            content.U32(instance);
            WriteName(items[i], content);
            _scope.Add(ItemKind.Alias, content, node.Position);
            instance = _scope[aliasSort].Add(null, aliasSort, node.Position);
        }

        return instance;
    }
}
