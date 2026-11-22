using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Meziantou.Framework.WasmEngine.SourceGeneration;

/// <summary>
/// Reads the exports of a module of the text format, and the types of its exported functions
/// (https://webassembly.github.io/spec/core/text/modules.html). It reads the types, the imports, the definitions and
/// the exports, and skips the bodies of the functions and the other fields: it does not validate the module.
/// </summary>
internal static class WatDescriptionReader
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly Dictionary<string, WasmHeapTypeKind> AbstractHeapTypes = new(StringComparer.Ordinal)
    {
        ["func"] = WasmHeapTypeKind.Func,
        ["extern"] = WasmHeapTypeKind.Extern,
        ["any"] = WasmHeapTypeKind.Any,
        ["eq"] = WasmHeapTypeKind.Eq,
        ["i31"] = WasmHeapTypeKind.I31,
        ["struct"] = WasmHeapTypeKind.Struct,
        ["array"] = WasmHeapTypeKind.Array,
        ["exn"] = WasmHeapTypeKind.Exn,
        ["none"] = WasmHeapTypeKind.None,
        ["noextern"] = WasmHeapTypeKind.NoExtern,
        ["nofunc"] = WasmHeapTypeKind.NoFunc,
        ["noexn"] = WasmHeapTypeKind.NoExn,
    };

    private static readonly Dictionary<string, WasmHeapTypeKind> ShorthandReferenceTypes = new(StringComparer.Ordinal)
    {
        ["funcref"] = WasmHeapTypeKind.Func,
        ["externref"] = WasmHeapTypeKind.Extern,
        ["anyref"] = WasmHeapTypeKind.Any,
        ["eqref"] = WasmHeapTypeKind.Eq,
        ["i31ref"] = WasmHeapTypeKind.I31,
        ["structref"] = WasmHeapTypeKind.Struct,
        ["arrayref"] = WasmHeapTypeKind.Array,
        ["exnref"] = WasmHeapTypeKind.Exn,
        ["nullref"] = WasmHeapTypeKind.None,
        ["nullexternref"] = WasmHeapTypeKind.NoExtern,
        ["nullfuncref"] = WasmHeapTypeKind.NoFunc,
        ["nullexnref"] = WasmHeapTypeKind.NoExn,
    };

    /// <exception cref="WasmDescriptionException">The text is malformed, or the module is malformed.</exception>
    public static WasmModuleDescription Read(string text)
    {
        var nodes = Tokenizer.Parse(text);
        if (nodes.Count == 1 && nodes[0].IsListOf("module"))
        {
            var items = nodes[0].Items!;
            var index = 1;
            if (index < items.Count && items[index].IsKeyword("definition"))
            {
                index++;
            }

            if (index < items.Count && items[index] is { IsList: false, Kind: TokenKind.Id })
            {
                index++;
            }

            if (index < items.Count && (items[index].IsKeyword("binary") || items[index].IsKeyword("quote")))
            {
                var bytes = new List<byte>();
                for (var i = index + 1; i < items.Count; i++)
                {
                    if (items[i] is not { IsList: false, Kind: TokenKind.String } part)
                        throw new WasmDescriptionException("expected a string", items[i].Position);

                    bytes.AddRange(part.Bytes);
                }

                if (items[index].IsKeyword("binary"))
                    return WasmBinaryDescriptionReader.Read([.. bytes]);

                string source;
                try
                {
                    source = Utf8.GetString([.. bytes]);
                }
                catch (DecoderFallbackException)
                {
                    throw new WasmDescriptionException("malformed UTF-8 encoding", items[index].Position);
                }

                // The text of a quoted module is the content of the module
                return Read("(module " + source + "\n)");
            }

            return ReadFields(items, index);
        }

        return ReadFields(nodes, 0);
    }

    private static WasmModuleDescription ReadFields(List<Node> fields, int start)
    {
        var module = new ModuleState();
        for (var f = start; f < fields.Count; f++)
        {
            var field = fields[f];
            if (!field.IsList || field.Head is not { } head)
                throw new WasmDescriptionException("malformed module field", field.Position);

            var items = field.Items!;
            switch (head)
            {
                case "type":
                    DeclareType(module, field);
                    break;

                case "rec":
                    for (var i = 1; i < items.Count; i++)
                    {
                        if (!items[i].IsListOf("type"))
                            throw new WasmDescriptionException("expected a type definition", items[i].Position);

                        DeclareType(module, items[i]);
                    }

                    break;

                case "import":
                {
                    if (items.Count != 4 || items[1].Kind != TokenKind.String || items[2].Kind != TokenKind.String || !items[3].IsList || items[3].Head is not { } kind)
                        throw new WasmDescriptionException("malformed import", field.Position);

                    var descriptor = items[3].Items!;
                    var index = 1;
                    var entity = new Entity(GetId(descriptor, ref index), descriptor, index);
                    module.GetSpace(kind, items[3].Position).Add(entity, imported: true);
                    break;
                }

                case "func" or "table" or "memory" or "global" or "tag":
                {
                    var index = 1;
                    var name = GetId(items, ref index);
                    var exportNames = new List<(string Name, int Position)>();
                    var imported = false;
                    while (index < items.Count)
                    {
                        if (items[index].IsListOf("export"))
                        {
                            var export = items[index].Items!;
                            if (export.Count != 2 || export[1].Kind != TokenKind.String || export[1].IsList)
                                throw new WasmDescriptionException("malformed export", items[index].Position);

                            exportNames.Add((export[1].Text, items[index].Position));
                        }
                        else if (items[index].IsListOf("import") && !imported)
                        {
                            var import = items[index].Items!;
                            if (import.Count != 3 || import[1].Kind != TokenKind.String || import[2].Kind != TokenKind.String)
                                throw new WasmDescriptionException("malformed import", items[index].Position);

                            imported = true;
                        }
                        else
                        {
                            break;
                        }

                        index++;
                    }

                    var entity = new Entity(name, items, index);
                    var space = module.GetSpace(head, field.Position);
                    space.Add(entity, imported);
                    foreach (var (exportName, position) in exportNames)
                    {
                        module.Exports.Add(new PendingExport(exportName, space.Kind, entity, Reference: null, position));
                    }

                    break;
                }

                case "export":
                {
                    if (items.Count != 3 || items[1].Kind != TokenKind.String || items[1].IsList || !items[2].IsList || items[2].Head is not { } kind || items[2].Items!.Count != 2)
                        throw new WasmDescriptionException("malformed export", field.Position);

                    var space = module.GetSpace(kind, items[2].Position);
                    module.Exports.Add(new PendingExport(items[1].Text, space.Kind, Entity: null, items[2].Items![1], field.Position));
                    break;
                }

                case "start" or "elem" or "data":
                    break;

                default:
                    throw new WasmDescriptionException("unknown module field " + head, field.Position);
            }
        }

        var exports = new List<WasmExportDescription>(module.Exports.Count);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var export in module.Exports)
        {
            if (!names.Add(export.Name))
                throw new WasmDescriptionException("duplicate export name", export.Position);

            var space = module.Spaces[(int)export.Kind];
            var entity = export.Entity ?? space.Resolve(export.Reference!);
            WasmFunctionSignature? signature = null;
            if (export.Kind == WasmExportKind.Function)
            {
                signature = ReadTypeUse(module, entity.Items, entity.Start);
            }

            exports.Add(new WasmExportDescription(export.Name, export.Kind, signature));
        }

        return new WasmModuleDescription(exports);
    }

    private static void DeclareType(ModuleState module, Node node)
    {
        var items = node.Items!;
        var index = 1;
        var name = GetId(items, ref index);
        if (index != items.Count - 1 || !items[index].IsList)
            throw new WasmDescriptionException("malformed type definition", node.Position);

        var definition = items[index];
        if (definition.IsListOf("sub"))
        {
            var subItems = definition.Items!;
            if (subItems.Count < 2 || !subItems[subItems.Count - 1].IsList)
                throw new WasmDescriptionException("malformed sub type", definition.Position);

            definition = subItems[subItems.Count - 1];
        }

        var kind = definition.Head switch
        {
            "func" => WasmHeapTypeKind.ConcreteFunc,
            "struct" => WasmHeapTypeKind.ConcreteStruct,
            "array" => WasmHeapTypeKind.ConcreteArray,
            _ => throw new WasmDescriptionException("malformed composite type", definition.Position),
        };

        var type = new TypeDefinition(kind, definition);
        if (name is not null)
        {
            if (module.TypeNames.ContainsKey(name))
                throw new WasmDescriptionException("duplicate type $" + name, node.Position);

            module.TypeNames[name] = type;
        }

        module.Types.Add(type);
    }

    /// <summary>Reads a type use: <c>(type x)? (param ...)* (result ...)*</c>; the parameters and the results define the type when they are present.</summary>
    private static WasmFunctionSignature ReadTypeUse(ModuleState module, List<Node> items, int index)
    {
        TypeDefinition? referenced = null;
        if (index < items.Count && items[index].IsListOf("type"))
        {
            var typeItems = items[index].Items!;
            if (typeItems.Count != 2)
                throw new WasmDescriptionException("malformed type use", items[index].Position);

            referenced = ResolveType(module, typeItems[1]);
            if (referenced is null || referenced.Kind != WasmHeapTypeKind.ConcreteFunc)
                throw new WasmDescriptionException("type " + typeItems[1] + " is not a function type", typeItems[1].Position);

            index++;
        }

        var parameters = new List<WasmTypeInfo>();
        var parameterNames = new List<string?>();
        var results = new List<WasmTypeInfo>();
        ReadParametersAndResults(module, items, index, parameters, parameterNames, results);
        if (referenced is not null && parameters.Count == 0 && results.Count == 0)
        {
            // The parameters and the results of the referenced type
            var definition = referenced.Definition.Items!;
            ReadParametersAndResults(module, definition, 1, parameters, parameterNames, results);
        }

        return new WasmFunctionSignature(parameters, parameterNames, results);
    }

    private static void ReadParametersAndResults(ModuleState module, List<Node> items, int index, List<WasmTypeInfo> parameters, List<string?> parameterNames, List<WasmTypeInfo> results)
    {
        while (index < items.Count && items[index].IsListOf("param"))
        {
            var parameter = items[index].Items!;
            if (parameter.Count >= 2 && parameter[1] is { IsList: false, Kind: TokenKind.Id } id)
            {
                if (parameter.Count != 3)
                    throw new WasmDescriptionException("malformed parameter", items[index].Position);

                parameters.Add(ReadValueType(module, parameter[2]));
                parameterNames.Add(id.Text);
            }
            else
            {
                for (var i = 1; i < parameter.Count; i++)
                {
                    parameters.Add(ReadValueType(module, parameter[i]));
                    parameterNames.Add(null);
                }
            }

            index++;
        }

        while (index < items.Count && items[index].IsListOf("result"))
        {
            var result = items[index].Items!;
            for (var i = 1; i < result.Count; i++)
            {
                results.Add(ReadValueType(module, result[i]));
            }

            index++;
        }
    }

    private static WasmTypeInfo ReadValueType(ModuleState module, Node node)
    {
        if (!node.IsList)
        {
            if (node.Kind == TokenKind.Keyword)
            {
                switch (node.Text)
                {
                    case "i32":
                        return WasmTypeInfo.I32;
                    case "i64":
                        return WasmTypeInfo.I64;
                    case "f32":
                        return WasmTypeInfo.F32;
                    case "f64":
                        return WasmTypeInfo.F64;
                    case "v128":
                        return WasmTypeInfo.V128;
                }

                if (ShorthandReferenceTypes.TryGetValue(node.Text, out var heap))
                    return WasmTypeInfo.Reference(heap, nullable: true);
            }

            throw new WasmDescriptionException("unknown value type " + node, node.Position);
        }

        var items = node.Items!;
        if (node.Head == "ref")
        {
            var index = 1;
            var nullable = false;
            if (index < items.Count && items[index].IsKeyword("null"))
            {
                nullable = true;
                index++;
            }

            if (index == items.Count - 1)
            {
                var heapType = items[index];
                if (heapType is { IsList: false, Kind: TokenKind.Keyword } && AbstractHeapTypes.TryGetValue(heapType.Text, out var heap))
                    return WasmTypeInfo.Reference(heap, nullable);

                return WasmTypeInfo.Reference(ResolveType(module, heapType)?.Kind ?? WasmHeapTypeKind.ConcreteFunc, nullable);
            }
        }

        throw new WasmDescriptionException("malformed value type", node.Position);
    }

    /// <summary>
    /// The type of an index: a name, or the index of a type. The types that the functions define with their parameters
    /// follow the types of the module, and are function types: an index after the types of the module returns null.
    /// </summary>
    private static TypeDefinition? ResolveType(ModuleState module, Node node)
    {
        if (node is { IsList: false, Kind: TokenKind.Id })
        {
            if (!module.TypeNames.TryGetValue(node.Text, out var named))
                throw new WasmDescriptionException("unknown type $" + node.Text, node.Position);

            return named;
        }

        var index = ParseIndex(node);
        return index < (uint)module.Types.Count ? module.Types[(int)index] : null;
    }

    private static uint ParseIndex(Node node)
    {
        if (node is { IsList: false, Kind: TokenKind.Number })
        {
            var text = RemoveUnderscores(node.Text);
            var hex = text.StartsWith("0x", StringComparison.Ordinal);
            if (hex)
            {
                text = text.Substring(2);
            }

            if (text.Length > 0 && !node.Text.EndsWith("_", StringComparison.Ordinal) && node.Text.IndexOf("__", StringComparison.Ordinal) < 0 &&
                uint.TryParse(text, hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
        }

        throw new WasmDescriptionException("malformed index " + node, node.Position);
    }

    private static string RemoveUnderscores(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c != '_')
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static string? GetId(List<Node> items, ref int index)
    {
        if (index < items.Count && items[index] is { IsList: false, Kind: TokenKind.Id } id)
        {
            index++;
            return id.Text;
        }

        return null;
    }

    private sealed class ModuleState
    {
        public ModuleState()
        {
            Spaces = [new(WasmExportKind.Function), new(WasmExportKind.Table), new(WasmExportKind.Memory), new(WasmExportKind.Global), new(WasmExportKind.Tag)];
        }

        public List<TypeDefinition> Types { get; } = [];

        public Dictionary<string, TypeDefinition> TypeNames { get; } = new(StringComparer.Ordinal);

        public IndexSpace[] Spaces { get; }

        public List<PendingExport> Exports { get; } = [];

        public IndexSpace GetSpace(string kind, int position) => kind switch
        {
            "func" => Spaces[(int)WasmExportKind.Function],
            "table" => Spaces[(int)WasmExportKind.Table],
            "memory" => Spaces[(int)WasmExportKind.Memory],
            "global" => Spaces[(int)WasmExportKind.Global],
            "tag" => Spaces[(int)WasmExportKind.Tag],
            _ => throw new WasmDescriptionException("malformed import or export kind", position),
        };
    }

    private sealed class TypeDefinition(WasmHeapTypeKind kind, Node definition)
    {
        public WasmHeapTypeKind Kind { get; } = kind;

        public Node Definition { get; } = definition;
    }

    /// <summary>A function, a table, a memory, a global or a tag: <see cref="Items"/> are the items of its definition, and its type starts at <see cref="Start"/>.</summary>
    private sealed class Entity(string? name, List<Node> items, int start)
    {
        public string? Name { get; } = name;

        public List<Node> Items { get; } = items;

        public int Start { get; } = start;
    }

    private sealed record PendingExport(string Name, WasmExportKind Kind, Entity? Entity, Node? Reference, int Position);

    /// <summary>The entities of a kind: the imports precede the definitions, as in the binary format.</summary>
    private sealed class IndexSpace(WasmExportKind kind)
    {
        private readonly List<Entity> _imports = [];
        private readonly List<Entity> _definitions = [];
        private readonly Dictionary<string, Entity> _names = new(StringComparer.Ordinal);

        public WasmExportKind Kind { get; } = kind;

        public void Add(Entity entity, bool imported)
        {
            if (entity.Name is not null)
            {
                if (_names.ContainsKey(entity.Name))
                    throw new WasmDescriptionException("duplicate " + Kind.ToString().ToLowerInvariant() + " $" + entity.Name, entity.Items.Count > 0 ? entity.Items[0].Position : 0);

                _names[entity.Name] = entity;
            }

            (imported ? _imports : _definitions).Add(entity);
        }

        public Entity Resolve(Node reference)
        {
            if (reference is { IsList: false, Kind: TokenKind.Id })
            {
                if (_names.TryGetValue(reference.Text, out var named))
                    return named;

                throw new WasmDescriptionException("unknown " + Kind.ToString().ToLowerInvariant() + " $" + reference.Text, reference.Position);
            }

            var index = ParseIndex(reference);
            if (index < (uint)_imports.Count)
                return _imports[(int)index];

            if (index - (uint)_imports.Count < (uint)_definitions.Count)
                return _definitions[(int)(index - (uint)_imports.Count)];

            throw new WasmDescriptionException("unknown " + Kind.ToString().ToLowerInvariant() + " " + index, reference.Position);
        }
    }

    private enum TokenKind : byte
    {
        List,
        Keyword,
        Id,
        String,
        Number,
        Reserved,
    }

    /// <summary>A token, or a parenthesized list of nodes. The text of an id has no '$'; the text of a string is its UTF-8 decoding.</summary>
    private sealed class Node
    {
        public Node(int position, List<Node> items)
        {
            Position = position;
            Items = items;
            Kind = TokenKind.List;
        }

        public Node(int position, TokenKind kind, string text, byte[]? bytes = null)
        {
            Position = position;
            Kind = kind;
            Text = text;
            Bytes = bytes ?? [];
        }

        public int Position { get; }

        public TokenKind Kind { get; }

        public List<Node>? Items { get; }

        public string Text { get; } = "";

        public byte[] Bytes { get; } = [];

        public bool IsList => Items is not null;

        public string? Head => IsList && Items!.Count > 0 && Items[0].Kind == TokenKind.Keyword ? Items[0].Text : null;

        public bool IsKeyword(string keyword) => Kind == TokenKind.Keyword && Text == keyword;

        public bool IsListOf(string keyword) => Head == keyword;

        public override string ToString() => Kind switch
        {
            TokenKind.List => "(...)",
            TokenKind.Id => "$" + Text,
            TokenKind.String => "\"" + Text + "\"",
            _ => Text,
        };
    }

    /// <summary>Reads the tokens and the lists of the text format (https://webassembly.github.io/spec/core/text/lexical.html). The annotations are skipped.</summary>
    private static class Tokenizer
    {
        private const int MaxDepth = 1000;

        public static List<Node> Parse(string text)
        {
            var position = 0;
            var result = new List<Node>();
            while (true)
            {
                SkipWhitespace(text, ref position);
                if (position >= text.Length)
                    return result;

                if (ReadNode(text, ref position, depth: 0) is { } node)
                {
                    result.Add(node);
                }
            }
        }

        private static void SkipWhitespace(string text, ref int position)
        {
            while (position < text.Length)
            {
                var c = text[position];
                if (c is ' ' or '\t' or '\n' or '\r')
                {
                    position++;
                }
                else if (c == ';' && position + 1 < text.Length && text[position + 1] == ';')
                {
                    while (position < text.Length && text[position] is not '\n' and not '\r')
                    {
                        position++;
                    }
                }
                else if (c == '(' && position + 1 < text.Length && text[position + 1] == ';')
                {
                    SkipBlockComment(text, ref position);
                }
                else
                {
                    return;
                }
            }
        }

        private static void SkipBlockComment(string text, ref int position)
        {
            var start = position;
            var depth = 0;
            while (position < text.Length)
            {
                if (text[position] == '(' && position + 1 < text.Length && text[position + 1] == ';')
                {
                    depth++;
                    position += 2;
                }
                else if (text[position] == ';' && position + 1 < text.Length && text[position + 1] == ')')
                {
                    depth--;
                    position += 2;
                    if (depth == 0)
                        return;
                }
                else
                {
                    position++;
                }
            }

            throw new WasmDescriptionException("unclosed comment", start);
        }

        /// <summary>Reads a node, or returns null for an annotation.</summary>
        private static Node? ReadNode(string text, ref int position, int depth)
        {
            var start = position;
            var c = text[position];
            if (c == '(')
            {
                if (depth >= MaxDepth)
                    throw new WasmDescriptionException("nesting too deep", start);

                position++;
                if (position < text.Length && text[position] == '@')
                {
                    SkipAnnotation(text, ref position, start);
                    return null;
                }

                var items = new List<Node>();
                while (true)
                {
                    SkipWhitespace(text, ref position);
                    if (position >= text.Length)
                        throw new WasmDescriptionException("unclosed parenthesis", start);

                    if (text[position] == ')')
                    {
                        position++;
                        return new Node(start, items);
                    }

                    if (ReadNode(text, ref position, depth + 1) is { } item)
                    {
                        items.Add(item);
                    }
                }
            }

            if (c == ')')
                throw new WasmDescriptionException("unexpected token", start);

            if (c == '"')
            {
                var bytes = ReadString(text, ref position);
                return new Node(start, TokenKind.String, Encoding.UTF8.GetString(bytes), bytes);
            }

            while (position < text.Length && IsIdChar(text[position]))
            {
                position++;
            }

            // An id can be a string: $"name"
            if (position < text.Length && text[position] == '"' && position == start + 1 && c == '$')
            {
                var bytes = ReadString(text, ref position);
                if (bytes.Length == 0)
                    throw new WasmDescriptionException("empty identifier", start);

                try
                {
                    return new Node(start, TokenKind.Id, Utf8.GetString(bytes));
                }
                catch (DecoderFallbackException)
                {
                    throw new WasmDescriptionException("malformed UTF-8 encoding", start);
                }
            }

            if (position == start)
                throw new WasmDescriptionException("unexpected character", start);

            var token = text.Substring(start, position - start);
            if (c == '$')
            {
                if (token.Length == 1)
                    throw new WasmDescriptionException("empty identifier", start);

                return new Node(start, TokenKind.Id, token.Substring(1));
            }

            if (c is >= 'a' and <= 'z')
                return new Node(start, TokenKind.Keyword, token);

            if (c is >= '0' and <= '9' or '+' or '-')
                return new Node(start, TokenKind.Number, token);

            return new Node(start, TokenKind.Reserved, token);
        }

        /// <summary>Skips an annotation, <c>(@id ...)</c>: its tokens are any balanced sequence of tokens, strings and comments.</summary>
        private static void SkipAnnotation(string text, ref int position, int start)
        {
            var depth = 1;
            while (true)
            {
                SkipWhitespace(text, ref position);
                if (position >= text.Length)
                    throw new WasmDescriptionException("unclosed annotation", start);

                var c = text[position];
                if (c == '(')
                {
                    depth++;
                    position++;
                }
                else if (c == ')')
                {
                    depth--;
                    position++;
                    if (depth == 0)
                        return;
                }
                else if (c == '"')
                {
                    ReadString(text, ref position);
                }
                else
                {
                    position++;
                }
            }
        }

        private static bool IsIdChar(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '/' or ':' or '<' or '=' or '>' or '?' or '@' or '\\' or '^' or '_' or '`' or '|' or '~';

        private static byte[] ReadString(string text, ref int position)
        {
            var start = position;
            position++;
            var result = new List<byte>();
            while (true)
            {
                if (position >= text.Length)
                    throw new WasmDescriptionException("unclosed string", start);

                var c = text[position++];
                if (c == '"')
                    return [.. result];

                if (c < 0x20 || c == 0x7F)
                    throw new WasmDescriptionException("malformed string: control character", position - 1);

                if (c != '\\')
                {
                    if (c < 0x80)
                    {
                        result.Add((byte)c);
                    }
                    else if (char.IsHighSurrogate(c) && position < text.Length && char.IsLowSurrogate(text[position]))
                    {
                        result.AddRange(Encoding.UTF8.GetBytes(new[] { c, text[position++] }));
                    }
                    else
                    {
                        result.AddRange(Encoding.UTF8.GetBytes(new[] { c }));
                    }

                    continue;
                }

                if (position >= text.Length)
                    throw new WasmDescriptionException("unclosed string", start);

                var escape = text[position++];
                switch (escape)
                {
                    case 't': result.Add((byte)'\t'); break;
                    case 'n': result.Add((byte)'\n'); break;
                    case 'r': result.Add((byte)'\r'); break;
                    case '"': result.Add((byte)'"'); break;
                    case '\'': result.Add((byte)'\''); break;
                    case '\\': result.Add((byte)'\\'); break;
                    case 'u':
                    {
                        if (position >= text.Length || text[position] != '{')
                            throw new WasmDescriptionException("malformed escape", position);

                        var end = text.IndexOf("}", position, StringComparison.Ordinal);
                        if (end < 0)
                            throw new WasmDescriptionException("malformed escape", position);

                        var digits = RemoveUnderscores(text.Substring(position + 1, end - position - 1));
                        if (digits.Length == 0 || !int.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var codePoint) || codePoint > 0x10FFFF || codePoint is >= 0xD800 and <= 0xDFFF)
                            throw new WasmDescriptionException("malformed escape: invalid code point", position);

                        result.AddRange(Encoding.UTF8.GetBytes(char.ConvertFromUtf32(codePoint)));
                        position = end + 1;
                        break;
                    }

                    default:
                        if (position >= text.Length || !IsHexDigit(escape) || !IsHexDigit(text[position]))
                            throw new WasmDescriptionException("malformed escape", position - 1);

                        result.Add((byte)((FromHex(escape) << 4) | FromHex(text[position++])));
                        break;
                }
            }
        }

        private static bool IsHexDigit(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

        private static int FromHex(char c) => c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            _ => c - 'A' + 10,
        };
    }
}
