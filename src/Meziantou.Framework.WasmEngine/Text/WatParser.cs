using System.Globalization;
using System.Text;

namespace Meziantou.Framework.WasmEngine.Text;

/// <summary>Converts a module of the text format to the binary format (https://webassembly.github.io/spec/core/text/modules.html).</summary>
internal sealed partial class WatParser
{
    private sealed class IndexSpace(string kind)
    {
        public string Kind { get; } = kind;

        public Dictionary<string, int> Names { get; } = new(StringComparer.Ordinal);

        public int Count { get; set; }

        public int Add(string? name, int position)
        {
            if (name is not null && !Names.TryAdd(name, Count))
                throw new WatException("duplicate " + Kind + " $" + name, position);

            return Count++;
        }
    }

    private sealed class TypeDefinition
    {
        public string? Name { get; init; }

        public required SNode Composite { get; init; }

        public bool Final { get; init; } = true;

        /// <summary>The declared supertypes (more than one is valid text, but not a valid module).</summary>
        public List<SNode> Supers { get; init; } = [];

        /// <summary>Whether the type is a function type of its own (not in a rec group, final, without supertype): an implicit type use can reuse it.</summary>
        public bool IsPlain { get; init; }

        public Dictionary<string, int> FieldNames { get; } = new(StringComparer.Ordinal);
    }

    private readonly List<TypeDefinition> _types = [];
    private readonly List<(int Start, int Count, bool IsRec)> _recGroups = [];
    private readonly List<(string Key, ByteWriter Encoding)> _implicitTypes = [];
    private readonly IndexSpace _typeSpace = new("type");
    private readonly IndexSpace _functionSpace = new("function");
    private readonly IndexSpace _tableSpace = new("table");
    private readonly IndexSpace _memorySpace = new("memory");
    private readonly IndexSpace _globalSpace = new("global");
    private readonly IndexSpace _tagSpace = new("tag");
    private readonly IndexSpace _elementSpace = new("elem segment");
    private readonly IndexSpace _dataSpace = new("data segment");

    private readonly ByteWriter _imports = new();
    private int _importCount;
    private readonly ByteWriter _functions = new();
    private int _functionCount;
    private readonly ByteWriter _tables = new();
    private int _tableCount;
    private readonly ByteWriter _memories = new();
    private int _memoryCount;
    private readonly ByteWriter _tags = new();
    private int _tagCount;
    private readonly ByteWriter _globals = new();
    private int _globalCount;
    private readonly ByteWriter _exports = new();
    private int _exportCount;
    private int _start = -1;
    private readonly List<(string Placement, byte[] Name, byte[] Data, int Position)> _customSections = [];
    private readonly ByteWriter _elements = new();
    private int _elementCount;
    private readonly ByteWriter _code = new();
    private readonly ByteWriter _data = new();
    private int _dataCount;
    private readonly List<(SNode Node, int TypeIndex, List<string?> ParameterNames)> _functionBodies = [];

    /// <summary>Converts a text that is a module, or the fields of a module.</summary>
    /// <exception cref="FormatException">The text is not valid.</exception>
    public static byte[] ParseModule(string text) => ParseModule(SExpressionReader.Parse(text));

    /// <summary>Converts the nodes of a text that is a module, or the fields of a module.</summary>
    /// <exception cref="FormatException">The text is not valid.</exception>
    public static byte[] ParseModule(List<SNode> nodes)
    {
        if (nodes.Count == 1 && nodes[0].IsListOf("module"))
            return EncodeModule(nodes[0]);

        return new WatParser().Encode(nodes, 0, nodes.Count > 0 ? nodes[0].Position : 0);
    }

    /// <summary>Converts a <c>(module ...)</c> node: the text format, <c>(module binary "..."*)</c> or <c>(module quote "..."*)</c>.</summary>
    public static byte[] EncodeModule(SNode module)
    {
        var items = module.Items!;
        var index = 1;
        if (index < items.Count && items[index].IsKeyword("definition"))
        {
            index++;
        }

        if (index < items.Count && items[index] is { IsList: false, Kind: TokenKind.Id })
        {
            index++;
        }

        if (index < items.Count && items[index].IsKeyword("binary"))
        {
            var bytes = new List<byte>();
            for (var i = index + 1; i < items.Count; i++)
            {
                if (items[i] is not { IsList: false, Kind: TokenKind.String } part)
                    throw new WatException("expected a string", items[i].Position);

                bytes.AddRange(part.Bytes);
            }

            return [.. bytes];
        }

        if (index < items.Count && items[index].IsKeyword("quote"))
        {
            // The strings are concatenated, then decoded
            var source = new List<byte>();
            for (var i = index + 1; i < items.Count; i++)
            {
                if (items[i] is not { IsList: false, Kind: TokenKind.String } part)
                    throw new WatException("expected a string", items[i].Position);

                source.AddRange(part.Bytes);
            }

            string text;
            try
            {
                text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString([.. source]);
            }
            catch (DecoderFallbackException)
            {
                throw new WatException("malformed UTF-8 encoding", module.Position);
            }

            return ParseModule("(module " + text + "\n)");
        }

        return new WatParser().Encode(items, index, module.Position);
    }

    private byte[] Encode(List<SNode> fields, int start, int position)
    {
        DeclareFields(fields, start);
        EncodeFields(fields, start);

        var module = new ByteWriter();
        module.Bytes([0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00]);
        WriteCustomSections(module, "before", "first");
        var types = EncodeTypes(out var typeCount);
        WriteSection(module, "type", typeCount > 0 ? (1, types, typeCount) : null);
        WriteSection(module, "import", _importCount > 0 ? (2, _imports, _importCount) : null);
        WriteSection(module, "func", _functionCount > 0 ? (3, _functions, _functionCount) : null);
        WriteSection(module, "table", _tableCount > 0 ? (4, _tables, _tableCount) : null);
        WriteSection(module, "memory", _memoryCount > 0 ? (5, _memories, _memoryCount) : null);
        WriteSection(module, "tag", _tagCount > 0 ? (13, _tags, _tagCount) : null);
        WriteSection(module, "global", _globalCount > 0 ? (6, _globals, _globalCount) : null);
        WriteSection(module, "export", _exportCount > 0 ? (7, _exports, _exportCount) : null);
        var startSection = new ByteWriter();
        startSection.U32((uint)Math.Max(_start, 0));
        WriteSection(module, "start", _start >= 0 ? (8, startSection, -1) : null);
        WriteSection(module, "elem", _elementCount > 0 ? (9, _elements, _elementCount) : null);
        var dataCountSection = new ByteWriter();
        dataCountSection.U32((uint)_dataCount);
        WriteSection(module, "datacount", _dataCount > 0 ? (12, dataCountSection, -1) : null);
        WriteSection(module, "code", _functionBodies.Count > 0 ? (10, _code, _functionBodies.Count) : null);
        WriteSection(module, "data", _dataCount > 0 ? (11, _data, _dataCount) : null);
        WriteCustomSections(module, "after", "last");
        if (_customSections.Count > 0)
            throw new WatException("malformed annotation: unknown placement of a custom section", _customSections[0].Position);

        _ = position;
        return module.ToArray();
    }

    private void WriteSection(ByteWriter module, string name, (byte Id, ByteWriter Content, int Count)? section)
    {
        WriteCustomSections(module, "before", name);
        if (section is { } value)
        {
            if (value.Count < 0)
            {
                module.Section(value.Id, value.Content);
            }
            else
            {
                module.Section(value.Id, value.Content, value.Count);
            }
        }

        WriteCustomSections(module, "after", name);
    }

    /// <summary>Writes the custom sections placed before or after a section (or first, or last).</summary>
    private void WriteCustomSections(ByteWriter module, string direction, string section)
    {
        var placement = direction + " " + section;
        for (var i = 0; i < _customSections.Count; i++)
        {
            var custom = _customSections[i];
            if (!string.Equals(custom.Placement, placement, StringComparison.Ordinal))
                continue;

            var content = new ByteWriter();
            content.U32((uint)custom.Name.Length);
            content.Bytes(custom.Name);
            content.Bytes(custom.Data);
            module.Section(0, content);
            _customSections.RemoveAt(i);
            i--;
        }
    }

    /// <summary>Reads a custom annotation (https://github.com/WebAssembly/annotations): <c>(@custom "name" (before|after section)? "data"*)</c>.</summary>
    private void ReadCustomSection(SNode field)
    {
        var items = field.Items!;
        if (items.Count < 2 || items[1] is not { IsList: false, Kind: TokenKind.String } name)
            throw new WatException("malformed annotation: expected the name of the custom section", field.Position);

        var placement = "after last";
        var index = 2;
        if (index < items.Count && items[index] is { IsList: true, Items: [{ Text: "before" or "after" } direction, { IsList: false } section] })
        {
            placement = direction.Text + " " + section.Text;
            index++;
        }

        var data = new List<byte>();
        for (; index < items.Count; index++)
        {
            if (items[index] is not { IsList: false, Kind: TokenKind.String } part)
                throw new WatException("malformed annotation: expected a string", items[index].Position);

            data.AddRange(part.Bytes);
        }

        _customSections.Add((placement, name.Bytes, [.. data], field.Position));
    }

    // ------------------------------------------------------------------------------------------------------------
    // The names (first pass)
    // ------------------------------------------------------------------------------------------------------------

    private static string? GetId(List<SNode> items, ref int index)
    {
        if (index < items.Count && items[index] is { IsList: false, Kind: TokenKind.Id } id)
        {
            index++;
            return id.Text;
        }

        return null;
    }

    /// <summary>The inline import of a definition, or null: <c>(import "module" "name")</c>.</summary>
    private static SNode? FindInlineImport(List<SNode> items, int index)
    {
        for (var i = index; i < items.Count; i++)
        {
            if (items[i].IsListOf("import"))
                return items[i];

            if (!items[i].IsListOf("export"))
                break;
        }

        return null;
    }

    private bool _hasDefinitions;

    private void DeclareFields(List<SNode> fields, int start)
    {
        for (var f = start; f < fields.Count; f++)
        {
            var field = fields[f];
            if (!field.IsList || field.Head is not { } head)
                throw new WatException("malformed module field", field.Position);

            var items = field.Items!;
            var index = 1;
            switch (head)
            {
                case "type":
                    DeclareType(field, inRecGroup: false);
                    break;

                case "rec":
                    var groupStart = _types.Count;
                    for (var i = 1; i < items.Count; i++)
                    {
                        if (!items[i].IsListOf("type"))
                            throw new WatException("expected a type definition", items[i].Position);

                        DeclareType(items[i], inRecGroup: true);
                    }

                    _recGroups.Add((groupStart, _types.Count - groupStart, true));
                    break;

                case "import":
                    if (_hasDefinitions)
                        throw new WatException("import after definition", field.Position);

                    if (items.Count != 4 || !items[3].IsList)
                        throw new WatException("malformed import", field.Position);

                    var descriptor = items[3].Items!;
                    var descriptorIndex = 1;
                    var name = GetId(descriptor, ref descriptorIndex);
                    GetSpace(items[3].Head, items[3].Position).Add(name, items[3].Position);
                    break;

                case "func" or "table" or "memory" or "global" or "tag":
                {
                    var name2 = GetId(items, ref index);
                    if (FindInlineImport(items, index) is not null)
                    {
                        if (_hasDefinitions)
                            throw new WatException("import after definition", field.Position);
                    }
                    else if (head != "tag")
                    {
                        // The imports must precede the definitions of functions, tables, memories and globals (the
                        // tags can precede the imports, as with wat2wasm)
                        _hasDefinitions = true;
                    }

                    GetSpace(head, field.Position).Add(name2, field.Position);

                    // A table or a memory with inline elements or data defines a segment
                    if (head == "table" && items.Any(item => item.IsListOf("elem")))
                    {
                        _elementSpace.Add(null, field.Position);
                    }
                    else if (head == "memory" && items.Any(item => item.IsListOf("data")))
                    {
                        _dataSpace.Add(null, field.Position);
                    }

                    break;
                }

                case "elem":
                    _elementSpace.Add(GetId(items, ref index), field.Position);
                    break;

                case "data":
                    _dataSpace.Add(GetId(items, ref index), field.Position);
                    break;

                case "export" or "start" or "@custom":
                    break;

                default:
                    throw new WatException("unknown module field " + head, field.Position);
            }
        }
    }

    private IndexSpace GetSpace(string? kind, int position) => kind switch
    {
        "func" => _functionSpace,
        "table" => _tableSpace,
        "memory" => _memorySpace,
        "global" => _globalSpace,
        "tag" => _tagSpace,
        _ => throw new WatException("malformed import or export kind", position),
    };

    private void DeclareType(SNode node, bool inRecGroup)
    {
        var items = node.Items!;
        var index = 1;
        var name = GetId(items, ref index);
        if (index != items.Count - 1 || !items[index].IsList)
            throw new WatException("malformed type definition", node.Position);

        var definition = items[index];
        var final = true;
        var supers = new List<SNode>();
        var plain = !inRecGroup;
        if (definition.IsListOf("sub"))
        {
            plain = false;
            var subItems = definition.Items!;
            var subIndex = 1;
            final = false;
            if (subIndex < subItems.Count && subItems[subIndex].IsKeyword("final"))
            {
                final = true;
                subIndex++;
            }

            while (subIndex < subItems.Count && !subItems[subIndex].IsList)
            {
                supers.Add(subItems[subIndex++]);
            }

            if (subIndex != subItems.Count - 1)
                throw new WatException("malformed sub type", definition.Position);

            definition = subItems[subIndex];
        }

        var type = new TypeDefinition { Name = name, Composite = definition, Final = final, Supers = supers, IsPlain = plain && definition.IsListOf("func") };
        if (definition.IsListOf("struct"))
        {
            var fieldIndex = 0;
            foreach (var field in definition.Items!.Skip(1))
            {
                if (!field.IsListOf("field"))
                    throw new WatException("malformed field", field.Position);

                var fieldItems = field.Items!;
                var nameIndex = 1;
                var fieldName = GetId(fieldItems, ref nameIndex);
                if (fieldName is not null)
                {
                    if (!type.FieldNames.TryAdd(fieldName, fieldIndex))
                        throw new WatException("duplicate field $" + fieldName, field.Position);

                    fieldIndex++;
                }
                else
                {
                    fieldIndex += fieldItems.Count - 1;
                }
            }
        }

        _types.Add(type);
        _typeSpace.Add(name, node.Position);
        if (!inRecGroup)
        {
            _recGroups.Add((_types.Count - 1, 1, false));
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // Indices and types
    // ------------------------------------------------------------------------------------------------------------

    private static int ResolveIndex(SNode node, IndexSpace space)
    {
        if (node.IsList)
            throw new WatException("expected a " + space.Kind + " index", node.Position);

        if (node.Kind == TokenKind.Id)
        {
            if (!space.Names.TryGetValue(node.Text, out var index))
                throw new WatException("unknown " + space.Kind + " $" + node.Text, node.Position);

            return index;
        }

        if (node.Kind == TokenKind.Number && WatNumbers.TryParseUnsigned(node.Text, uint.MaxValue, out var value))
            return (int)Math.Min(value, int.MaxValue);

        throw new WatException("expected a " + space.Kind + " index", node.Position);
    }

    private static bool IsIndex(SNode node) => !node.IsList && (node.Kind == TokenKind.Id || (node.Kind == TokenKind.Number && WatNumbers.TryParseUnsigned(node.Text, uint.MaxValue, out _)));

    private static readonly Dictionary<string, byte> AbstractHeapTypes = new(StringComparer.Ordinal)
    {
        ["func"] = 0x70,
        ["extern"] = 0x6F,
        ["any"] = 0x6E,
        ["eq"] = 0x6D,
        ["i31"] = 0x6C,
        ["struct"] = 0x6B,
        ["array"] = 0x6A,
        ["exn"] = 0x69,
        ["none"] = 0x71,
        ["noextern"] = 0x72,
        ["nofunc"] = 0x73,
        ["noexn"] = 0x74,
    };

    private static readonly Dictionary<string, byte> ReferenceShorthands = new(StringComparer.Ordinal)
    {
        ["funcref"] = 0x70,
        ["externref"] = 0x6F,
        ["anyref"] = 0x6E,
        ["eqref"] = 0x6D,
        ["i31ref"] = 0x6C,
        ["structref"] = 0x6B,
        ["arrayref"] = 0x6A,
        ["exnref"] = 0x69,
        ["nullref"] = 0x71,
        ["nullexternref"] = 0x72,
        ["nullfuncref"] = 0x73,
        ["nullexnref"] = 0x74,
    };

    private void WriteHeapType(SNode node, ByteWriter writer)
    {
        if (!node.IsList && node.Kind == TokenKind.Keyword && AbstractHeapTypes.TryGetValue(node.Text, out var code))
        {
            writer.Byte(code);
            return;
        }

        writer.S64(ResolveIndex(node, _typeSpace));
    }

    /// <summary>Writes a reference type and returns whether it is nullable.</summary>
    private bool WriteRefType(SNode node, ByteWriter writer)
    {
        if (!node.IsList)
        {
            if (node.Kind == TokenKind.Keyword && ReferenceShorthands.TryGetValue(node.Text, out var code))
            {
                writer.Byte(code);
                return true;
            }

            throw new WatException("malformed reference type", node.Position);
        }

        var items = node.Items!;
        if (!node.IsListOf("ref"))
            throw new WatException("malformed reference type", node.Position);

        var nullable = items.Count == 3 && items[1].IsKeyword("null");
        if (items.Count != (nullable ? 3 : 2))
            throw new WatException("malformed reference type", node.Position);

        writer.Byte(nullable ? (byte)0x63 : (byte)0x64);
        WriteHeapType(items[^1], writer);
        return nullable;
    }

    private void WriteValType(SNode node, ByteWriter writer)
    {
        if (!node.IsList && node.Kind == TokenKind.Keyword)
        {
            switch (node.Text)
            {
                case "i32":
                    writer.Byte(0x7F);
                    return;
                case "i64":
                    writer.Byte(0x7E);
                    return;
                case "f32":
                    writer.Byte(0x7D);
                    return;
                case "f64":
                    writer.Byte(0x7C);
                    return;
                case "v128":
                    writer.Byte(0x7B);
                    return;
            }
        }

        WriteRefType(node, writer);
    }

    private static bool IsValType(SNode node) => node.IsListOf("ref") || (!node.IsList && node.Kind == TokenKind.Keyword && (node.Text is "i32" or "i64" or "f32" or "f64" or "v128" || ReferenceShorthands.ContainsKey(node.Text)));

    private void WriteStorageType(SNode node, ByteWriter writer)
    {
        if (node.IsKeyword("i8"))
        {
            writer.Byte(0x78);
        }
        else if (node.IsKeyword("i16"))
        {
            writer.Byte(0x77);
        }
        else
        {
            WriteValType(node, writer);
        }
    }

    private void WriteFieldType(SNode node, ByteWriter writer)
    {
        if (node.IsListOf("mut"))
        {
            if (node.Items!.Count != 2)
                throw new WatException("malformed mutable type", node.Position);

            WriteStorageType(node.Items[1], writer);
            writer.Byte(1);
        }
        else
        {
            WriteStorageType(node, writer);
            writer.Byte(0);
        }
    }

    private ByteWriter EncodeTypes(out int count)
    {
        var writer = new ByteWriter();
        count = 0;
        foreach (var (start, length, isRec) in _recGroups)
        {
            var inRecGroup = isRec;
            if (inRecGroup)
            {
                writer.Byte(0x4E);
                writer.U32((uint)length);
            }

            for (var i = start; i < start + length; i++)
            {
                EncodeTypeDefinition(_types[i], writer);
            }

            count++;
        }

        foreach (var (_, encoding) in _implicitTypes)
        {
            writer.Append(encoding);
            count++;
        }

        return writer;
    }

    private void EncodeTypeDefinition(TypeDefinition type, ByteWriter writer)
    {
        if (!type.Final || type.Supers.Count > 0)
        {
            writer.Byte(type.Final ? (byte)0x4F : (byte)0x50);
            writer.U32((uint)type.Supers.Count);
            foreach (var super in type.Supers)
            {
                writer.U32((uint)ResolveIndex(super, _typeSpace));
            }
        }

        var composite = type.Composite;
        var items = composite.Items!;
        switch (composite.Head)
        {
            case "func":
            {
                var (parameters, results, _) = ParseSignature(items, 1, allowNames: true, out var end);
                if (end != items.Count)
                    throw new WatException("malformed function type", composite.Position);

                writer.Byte(0x60);
                WriteTypes(parameters, writer);
                WriteTypes(results, writer);
                break;
            }

            case "struct":
            {
                var fields = new ByteWriter();
                var fieldCount = 0;
                foreach (var field in items.Skip(1))
                {
                    var fieldItems = field.Items!;
                    var index = 1;
                    GetId(fieldItems, ref index);
                    for (; index < fieldItems.Count; index++)
                    {
                        WriteFieldType(fieldItems[index], fields);
                        fieldCount++;
                    }
                }

                writer.Byte(0x5F);
                writer.Vector(fields, fieldCount);
                break;
            }

            case "array":
                if (items.Count != 2)
                    throw new WatException("malformed array type", composite.Position);

                writer.Byte(0x5E);
                WriteFieldType(items[1], writer);
                break;

            default:
                throw new WatException("malformed type definition", composite.Position);
        }
    }

    /// <summary>
    /// Parses <c>(param ...)* (result ...)*</c> from <paramref name="start"/>: the types, and the names of the parameters.
    /// <paramref name="end"/> is the index after the last result.
    /// </summary>
    private static (List<SNode> Parameters, List<SNode> Results, List<string?> Names) ParseSignature(List<SNode> items, int start, bool allowNames, out int end)
    {
        var parameters = new List<SNode>();
        var results = new List<SNode>();
        var names = new List<string?>();
        var index = start;
        while (index < items.Count && items[index].IsListOf("param"))
        {
            var parameterItems = items[index].Items!;
            var nameIndex = 1;
            var name = GetId(parameterItems, ref nameIndex);
            if (name is not null)
            {
                if (!allowNames || parameterItems.Count != 3)
                    throw new WatException("malformed parameter", items[index].Position);

                parameters.Add(parameterItems[2]);
                names.Add(name);
            }
            else
            {
                for (var i = 1; i < parameterItems.Count; i++)
                {
                    parameters.Add(parameterItems[i]);
                    names.Add(null);
                }
            }

            index++;
        }

        while (index < items.Count && items[index].IsListOf("result"))
        {
            results.AddRange(items[index].Items!.Skip(1));
            index++;
        }

        end = index;
        return (parameters, results, names);
    }

    private string SignatureKey(List<SNode> parameters, List<SNode> results)
    {
        var writer = new ByteWriter();
        WriteTypes(parameters, writer);
        WriteTypes(results, writer);
        return Convert.ToHexString(writer.ToArray());
    }

    private void WriteTypes(List<SNode> types, ByteWriter writer)
    {
        var content = new ByteWriter();
        foreach (var type in types)
        {
            WriteValType(type, content);
        }

        writer.Vector(content, types.Count);
    }

    /// <summary>Returns the index of a function type with these parameters and results: a plain type of the module, or an implicit type added to the module.</summary>
    private int GetImplicitType(List<SNode> parameters, List<SNode> results)
    {
        var key = SignatureKey(parameters, results);
        for (var i = 0; i < _types.Count; i++)
        {
            var type = _types[i];
            if (type.IsPlain && !IsInRecGroup(i))
            {
                var items = type.Composite.Items!;
                var (typeParameters, typeResults, _) = ParseSignature(items, 1, allowNames: true, out _);
                if (SignatureKey(typeParameters, typeResults) == key)
                    return i;
            }
        }

        for (var i = 0; i < _implicitTypes.Count; i++)
        {
            if (_implicitTypes[i].Key == key)
                return _types.Count + i;
        }

        var encoding = new ByteWriter();
        encoding.Byte(0x60);
        WriteTypes(parameters, encoding);
        WriteTypes(results, encoding);
        _implicitTypes.Add((key, encoding));
        return _types.Count + _implicitTypes.Count - 1;
    }

    private bool IsInRecGroup(int typeIndex)
    {
        foreach (var (start, count, isRec) in _recGroups)
        {
            if (typeIndex >= start && typeIndex < start + count)
                return isRec || count != 1;
        }

        return false;
    }

    /// <summary>
    /// Parses a type use (https://webassembly.github.io/spec/core/text/modules.html#type-uses): <c>(type x)?</c> followed by
    /// the parameters and the results. Returns the type index, and the names of the parameters.
    /// </summary>
    private (int TypeIndex, List<string?> ParameterNames) ParseTypeUse(List<SNode> items, ref int index)
    {
        int? explicitType = null;
        if (index < items.Count && items[index].IsListOf("type"))
        {
            var typeItems = items[index].Items!;
            if (typeItems.Count != 2)
                throw new WatException("malformed type use", items[index].Position);

            explicitType = ResolveIndex(typeItems[1], _typeSpace);
            index++;
        }

        var (parameters, results, names) = ParseSignature(items, index, allowNames: true, out var end);
        index = end;
        if (explicitType is { } typeIndex)
        {
            if ((parameters.Count > 0 || results.Count > 0) && typeIndex < _types.Count && _types[typeIndex].Composite.IsListOf("func"))
            {
                var (typeParameters, typeResults, _) = ParseSignature(_types[typeIndex].Composite.Items!, 1, allowNames: true, out _);
                if (SignatureKey(typeParameters, typeResults) != SignatureKey(parameters, results))
                    throw new WatException("inline function type", items[Math.Max(index - 1, 0)].Position);
            }

            if (parameters.Count == 0 && typeIndex < _types.Count && _types[typeIndex].Composite.IsListOf("func"))
            {
                var (typeParameters, _, _) = ParseSignature(_types[typeIndex].Composite.Items!, 1, allowNames: true, out _);
                names = [.. Enumerable.Repeat<string?>(null, typeParameters.Count)];
            }

            return (typeIndex, names);
        }

        return (GetImplicitType(parameters, results), names);
    }

    // ------------------------------------------------------------------------------------------------------------
    // The fields (second pass)
    // ------------------------------------------------------------------------------------------------------------

    private void EncodeFields(List<SNode> fields, int start)
    {
        var definedFunctions = 0;
        for (var f = start; f < fields.Count; f++)
        {
            var field = fields[f];
            var items = field.Items!;
            var index = 1;
            switch (field.Head)
            {
                case "type" or "rec":
                    break;

                case "import":
                    EncodeImport(GetString(items[1]), GetString(items[2]), items[3]);
                    break;

                case "func":
                {
                    GetId(items, ref index);
                    var current = ExportsAndImport(items, ref index, WasmExternKind.Function, out var import);
                    if (import is not null)
                    {
                        var (typeIndex, _) = ParseTypeUse(items, ref index);
                        WriteImport(import, 0x00, writer => writer.U32((uint)typeIndex));
                        WriteExports(current, 0x00, _importedFunctions++);
                    }
                    else
                    {
                        var (typeIndex, names) = ParseTypeUse(items, ref index);
                        _functions.U32((uint)typeIndex);
                        _functionCount++;
                        var functionIndexValue = _importedFunctions + definedFunctions++;
                        WriteExports(current, 0x00, functionIndexValue);
                        _functionBodies.Add((field, typeIndex, names));
                        EncodeFunctionBody(items, index, typeIndex, names);
                    }

                    break;
                }

                case "table":
                    EncodeTable(field);
                    break;

                case "memory":
                    EncodeMemory(field);
                    break;

                case "global":
                    EncodeGlobal(field);
                    break;

                case "tag":
                {
                    GetId(items, ref index);
                    var exports = ExportsAndImport(items, ref index, WasmExternKind.Tag, out var import);
                    var (typeIndex, _) = ParseTypeUse(items, ref index);
                    if (import is not null)
                    {
                        WriteImport(import, 0x04, writer =>
                        {
                            writer.Byte(0);
                            writer.U32((uint)typeIndex);
                        });
                        WriteExports(exports, 0x04, _importedTags++);
                    }
                    else
                    {
                        _tags.Byte(0);
                        _tags.U32((uint)typeIndex);
                        WriteExports(exports, 0x04, _importedTags + _tagCount);
                        _tagCount++;
                    }

                    break;
                }

                case "export":
                {
                    if (items.Count != 3 || !items[2].IsList || items[2].Items!.Count != 2)
                        throw new WatException("malformed export", field.Position);

                    var name = GetString(items[1]);
                    var descriptor = items[2];
                    var space = GetSpace(descriptor.Head, descriptor.Position);
                    var kind = descriptor.Head switch { "func" => 0x00, "table" => 0x01, "memory" => 0x02, "global" => 0x03, _ => 0x04 };
                    WriteExport(name, (byte)kind, ResolveIndex(descriptor.Items![1], space));
                    break;
                }

                case "@custom":
                    ReadCustomSection(field);
                    break;

                case "start":
                    if (items.Count != 2)
                        throw new WatException("malformed start", field.Position);

                    if (_start >= 0)
                        throw new WatException("multiple start sections", field.Position);

                    _start = ResolveIndex(items[1], _functionSpace);
                    break;

                case "elem":
                    EncodeElementSegment(field);
                    break;

                case "data":
                    EncodeDataSegment(field);
                    break;
            }
        }
    }

    private int _importedFunctions;
    private int _importedTables;
    private int _importedMemories;
    private int _importedGlobals;
    private int _importedTags;

    private static string GetString(SNode node)
    {
        if (node is not { IsList: false, Kind: TokenKind.String })
            throw new WatException("expected a string", node.Position);

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(node.Bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new WatException("malformed UTF-8 encoding", node.Position);
        }
    }

    /// <summary>Reads the inline exports and the inline import of a definition.</summary>
    private static List<string> ExportsAndImport(List<SNode> items, ref int index, WasmExternKind kind, out SNode? import)
    {
        _ = kind;
        var exports = new List<string>();
        import = null;
        while (index < items.Count)
        {
            if (items[index].IsListOf("export"))
            {
                var exportItems = items[index].Items!;
                if (exportItems.Count != 2)
                    throw new WatException("malformed export", items[index].Position);

                exports.Add(GetString(exportItems[1]));
                index++;
            }
            else if (items[index].IsListOf("import") && import is null)
            {
                var importItems = items[index].Items!;
                if (importItems.Count != 3)
                    throw new WatException("malformed import", items[index].Position);

                import = items[index];
                index++;
            }
            else
            {
                break;
            }
        }

        return exports;
    }

    private void WriteImport(SNode import, byte kind, Action<ByteWriter> writeDescriptor)
    {
        var items = import.Items!;
        _imports.Name(GetString(items[1]));
        _imports.Name(GetString(items[2]));
        _imports.Byte(kind);
        writeDescriptor(_imports);
        _importCount++;
    }

    private void WriteExports(List<string> names, byte kind, int index)
    {
        foreach (var name in names)
        {
            WriteExport(name, kind, index);
        }
    }

    /// <summary>Writes an export (the duplicate names are reported by the validation).</summary>
    private void WriteExport(string name, byte kind, int index)
    {
        _exports.Name(name);
        _exports.Byte(kind);
        _exports.U32((uint)index);
        _exportCount++;
    }

    private void EncodeImport(string module, string name, SNode descriptor)
    {
        var items = descriptor.Items!;
        var index = 1;
        GetId(items, ref index);
        _imports.Name(module);
        _imports.Name(name);
        switch (descriptor.Head)
        {
            case "func":
            {
                var (typeIndex, _) = ParseTypeUse(items, ref index);
                _imports.Byte(0x00);
                _imports.U32((uint)typeIndex);
                _importedFunctions++;
                break;
            }

            case "table":
                _imports.Byte(0x01);
                WriteTableType(items, ref index, _imports);
                _importedTables++;
                break;

            case "memory":
                _imports.Byte(0x02);
                WriteMemoryType(items, ref index, _imports);
                _importedMemories++;
                break;

            case "global":
                if (index >= items.Count)
                    throw new WatException("malformed import: expected a global type", descriptor.Position);

                _imports.Byte(0x03);
                WriteGlobalType(items[index++], _imports);
                _importedGlobals++;
                break;

            case "tag":
            {
                var (typeIndex, _) = ParseTypeUse(items, ref index);
                _imports.Byte(0x04);
                _imports.Byte(0);
                _imports.U32((uint)typeIndex);
                _importedTags++;
                break;
            }

            default:
                throw new WatException("malformed import kind", descriptor.Position);
        }

        if (index != items.Count)
            throw new WatException("malformed import", descriptor.Position);

        _importCount++;
    }

    private void WriteGlobalType(SNode node, ByteWriter writer)
    {
        if (node.IsListOf("mut"))
        {
            if (node.Items!.Count != 2)
                throw new WatException("malformed mutable type", node.Position);

            WriteValType(node.Items[1], writer);
            writer.Byte(1);
        }
        else
        {
            WriteValType(node, writer);
            writer.Byte(0);
        }
    }

    /// <summary>Writes limits: <c>min max?</c> (with the address type of i64 limits).</summary>
    private static void WriteLimits(List<SNode> items, ref int index, ByteWriter writer, bool is64, bool shared, bool allowShared)
    {
        if (index >= items.Count || !WatNumbers.TryParseUnsigned(items[index].Text, ulong.MaxValue, out var minimum) || items[index].IsList)
            throw new WatException("malformed limits", index < items.Count ? items[index].Position : 0);

        index++;
        ulong? maximum = null;
        if (index < items.Count && !items[index].IsList && items[index].Kind == TokenKind.Number)
        {
            if (!WatNumbers.TryParseUnsigned(items[index].Text, ulong.MaxValue, out var value))
                throw new WatException("malformed limits", items[index].Position);

            maximum = value;
            index++;
        }

        if (allowShared && index < items.Count && items[index].IsKeyword("shared"))
        {
            shared = true;
            index++;
        }

        writer.Byte((byte)((maximum.HasValue ? 1 : 0) | (shared ? 2 : 0) | (is64 ? 4 : 0)));
        writer.U32(minimum);
        if (maximum is { } max)
        {
            writer.U32(max);
        }
    }

    private static bool ReadAddressType(List<SNode> items, ref int index)
    {
        if (index < items.Count && items[index].IsKeyword("i64"))
        {
            index++;
            return true;
        }

        if (index < items.Count && items[index].IsKeyword("i32"))
        {
            index++;
        }

        return false;
    }

    private void WriteTableType(List<SNode> items, ref int index, ByteWriter writer)
    {
        var is64 = ReadAddressType(items, ref index);
        var limits = new ByteWriter();
        WriteLimits(items, ref index, limits, is64, shared: false, allowShared: false);
        if (index >= items.Count)
            throw new WatException("malformed table type", items[^1].Position);

        WriteRefType(items[index++], writer);
        writer.Append(limits);
    }

    private static void WriteMemoryType(List<SNode> items, ref int index, ByteWriter writer)
    {
        var is64 = ReadAddressType(items, ref index);
        WriteLimits(items, ref index, writer, is64, shared: false, allowShared: true);
    }

    private void EncodeTable(SNode field)
    {
        var items = field.Items!;
        var index = 1;
        GetId(items, ref index);
        var exports = ExportsAndImport(items, ref index, WasmExternKind.Table, out var import);
        var tableIndex = import is not null ? _importedTables : _importedTables + _tableCount;
        if (import is not null)
        {
            var importIndex = index;
            WriteImport(import, 0x01, writer => WriteTableType(items, ref importIndex, writer));
            _importedTables++;
            WriteExports(exports, 0x01, tableIndex);
            return;
        }

        WriteExports(exports, 0x01, tableIndex);

        // (table i64? reftype (elem ...)): the limits are the number of elements
        var start = index;
        var is64 = ReadAddressType(items, ref index);
        if (index < items.Count && (IsValType(items[index]) || items[index].IsListOf("ref")) && index + 1 < items.Count && items[index + 1].IsListOf("elem"))
        {
            var elementType = items[index];
            var elem = items[index + 1];
            var elemItems = elem.Items!;
            var count = elemItems.Count - 1;
            var usesExpressions = elemItems.Skip(1).Any(item => item.IsList);
            var tableType = new ByteWriter();
            WriteRefType(elementType, tableType);
            tableType.Byte(is64 ? (byte)0x05 : (byte)0x01);
            tableType.U32((uint)count);
            tableType.U32((uint)count);
            _tables.Append(tableType);
            _tableCount++;

            // An active segment at offset 0 of the table
            var offset = new ByteWriter();
            offset.Byte(is64 ? (byte)0x42 : (byte)0x41);
            offset.S64(0);
            offset.Byte(0x0B);
            // The type of the segment is the type of the table: the function indices are ref.func expressions
            _elements.U32(6);
            _elements.U32((uint)tableIndex);
            _elements.Append(offset);
            WriteRefType(elementType, _elements);
            _elements.U32((uint)count);
            foreach (var item in elemItems.Skip(1))
            {
                if (usesExpressions)
                {
                    EncodeConstantExpression(item.IsListOf("item") ? item.Items!.Skip(1).ToList() : [item], _elements);
                }
                else
                {
                    _elements.Byte(0xD2);
                    _elements.U32((uint)ResolveIndex(item, _functionSpace));
                    _elements.Byte(0x0B);
                }
            }

            _elementCount++;
            return;
        }

        index = start;
        var type = new ByteWriter();
        WriteTableType(items, ref index, type);
        if (index < items.Count)
        {
            // A table with an initial value
            _tables.Byte(0x40);
            _tables.Byte(0);
            _tables.Append(type);
            EncodeConstantExpression(items.Skip(index).ToList(), _tables);
        }
        else
        {
            _tables.Append(type);
        }

        _tableCount++;
    }

    private void EncodeMemory(SNode field)
    {
        var items = field.Items!;
        var index = 1;
        GetId(items, ref index);
        var exports = ExportsAndImport(items, ref index, WasmExternKind.Memory, out var import);
        var memoryIndex = import is not null ? _importedMemories : _importedMemories + _memoryCount;
        if (import is not null)
        {
            var importIndex = index;
            WriteImport(import, 0x02, writer => WriteMemoryType(items, ref importIndex, writer));
            _importedMemories++;
            WriteExports(exports, 0x02, memoryIndex);
            return;
        }

        WriteExports(exports, 0x02, memoryIndex);
        var start = index;
        var is64 = ReadAddressType(items, ref index);
        if (index < items.Count && items[index].IsListOf("data"))
        {
            var bytes = new List<byte>();
            foreach (var part in items[index].Items!.Skip(1))
            {
                bytes.AddRange(GetDataString(part));
            }

            var pages = (uint)((bytes.Count + 65535) / 65536);
            _memories.Byte(is64 ? (byte)0x05 : (byte)0x01);
            _memories.U32(pages);
            _memories.U32(pages);
            _memoryCount++;

            _data.U32(memoryIndex == 0 ? 0u : 2u);
            if (memoryIndex != 0)
            {
                _data.U32((uint)memoryIndex);
            }

            _data.Byte(is64 ? (byte)0x42 : (byte)0x41);
            _data.S64(0);
            _data.Byte(0x0B);
            _data.U32((uint)bytes.Count);
            _data.Bytes([.. bytes]);
            _dataCount++;
            return;
        }

        index = start;
        WriteMemoryType(items, ref index, _memories);
        if (index != items.Count)
            throw new WatException("malformed memory", field.Position);

        _memoryCount++;
    }

    private static byte[] GetDataString(SNode node)
    {
        if (node is not { IsList: false, Kind: TokenKind.String })
            throw new WatException("expected a string", node.Position);

        return node.Bytes;
    }

    private void EncodeGlobal(SNode field)
    {
        var items = field.Items!;
        var index = 1;
        GetId(items, ref index);
        var exports = ExportsAndImport(items, ref index, WasmExternKind.Global, out var import);
        if (index >= items.Count)
            throw new WatException("malformed global", field.Position);

        if (import is not null)
        {
            var typeNode = items[index];
            WriteImport(import, 0x03, writer => WriteGlobalType(typeNode, writer));
            WriteExports(exports, 0x03, _importedGlobals++);
            return;
        }

        WriteGlobalType(items[index++], _globals);
        EncodeConstantExpression(items.Skip(index).ToList(), _globals);
        WriteExports(exports, 0x03, _importedGlobals + _globalCount);
        _globalCount++;
    }

    /// <summary>Encodes the instructions of a constant expression, and its end.</summary>
    private void EncodeConstantExpression(List<SNode> instructions, ByteWriter writer)
    {
        var context = new FunctionContext(new Dictionary<string, int>(StringComparer.Ordinal));
        var index = 0;
        while (index < instructions.Count)
        {
            EncodeInstruction(instructions, ref index, context, writer);
        }

        writer.Byte(0x0B);
    }

    /// <summary>The offset of a segment: <c>(offset instr*)</c> or a single folded instruction.</summary>
    private void EncodeOffset(SNode node, ByteWriter writer)
    {
        if (node.IsListOf("offset"))
        {
            EncodeConstantExpression(node.Items!.Skip(1).ToList(), writer);
        }
        else
        {
            EncodeConstantExpression([node], writer);
        }
    }

    private void EncodeElementSegment(SNode field)
    {
        var items = field.Items!;
        var index = 1;
        GetId(items, ref index);
        var mode = SegmentMode.Passive;
        SNode? tableNode = null;
        SNode? offset = null;
        if (index < items.Count && items[index].IsKeyword("declare"))
        {
            mode = SegmentMode.Declarative;
            index++;
        }
        else
        {
            if (index < items.Count && items[index].IsListOf("table"))
            {
                if (items[index].Items!.Count != 2)
                    throw new WatException("malformed table use", items[index].Position);

                tableNode = items[index].Items![1];
                index++;
                mode = SegmentMode.Active;
            }
            else if (index + 1 < items.Count && items[index] is { IsList: false, Kind: TokenKind.Number } && items[index + 1].IsList)
            {
                // The legacy form of the table index: (elem 0 (i32.const 0) ...)
                tableNode = items[index];
                index++;
                mode = SegmentMode.Active;
            }

            if (index < items.Count && items[index].IsList && !items[index].IsListOf("item") && !items[index].IsListOf("ref") && (items[index].IsListOf("offset") || tableNode is not null || IsInstructionList(items[index])))
            {
                offset = items[index];
                index++;
                mode = SegmentMode.Active;
            }

            if (tableNode is not null && offset is null)
                throw new WatException("malformed element segment: expected an offset", field.Position);
        }

        // The elements: func x*, x* (the abbreviation of an active segment), or a reference type and expressions
        var functionIndices = new List<int>();
        var expressions = new List<List<SNode>>();
        SNode? referenceType = null;
        var usesFunctionIndices = true;
        if (index < items.Count && items[index].IsKeyword("func"))
        {
            index++;
        }
        else if (index < items.Count && (IsValType(items[index]) && !items[index].IsKeyword("i32")))
        {
            referenceType = items[index];
            usesFunctionIndices = false;
            index++;
        }

        for (; index < items.Count; index++)
        {
            var item = items[index];
            if (usesFunctionIndices && !item.IsList)
            {
                functionIndices.Add(ResolveIndex(item, _functionSpace));
            }
            else
            {
                if (usesFunctionIndices && functionIndices.Count > 0)
                    throw new WatException("malformed element segment", item.Position);

                usesFunctionIndices = false;
                expressions.Add(item.IsListOf("item") ? item.Items!.Skip(1).ToList() : [item]);
            }
        }

        var tableIndex = tableNode is null ? 0 : ResolveIndex(tableNode, _tableSpace);
        var segment = _elements;
        if (usesFunctionIndices)
        {
            switch (mode)
            {
                case SegmentMode.Active when tableIndex == 0 && tableNode is null:
                    segment.U32(0);
                    EncodeOffset(offset!, segment);
                    break;
                case SegmentMode.Active:
                    segment.U32(2);
                    segment.U32((uint)tableIndex);
                    EncodeOffset(offset!, segment);
                    segment.Byte(0);
                    break;
                case SegmentMode.Passive:
                    segment.U32(1);
                    segment.Byte(0);
                    break;
                default:
                    segment.U32(3);
                    segment.Byte(0);
                    break;
            }

            segment.U32((uint)functionIndices.Count);
            foreach (var function in functionIndices)
            {
                segment.U32((uint)function);
            }
        }
        else
        {
            referenceType ??= SNode.Token(field.Position, TokenKind.Keyword, "funcref");
            switch (mode)
            {
                case SegmentMode.Active:
                    segment.U32(6);
                    segment.U32((uint)tableIndex);
                    EncodeOffset(offset!, segment);
                    WriteRefType(referenceType, segment);
                    break;
                case SegmentMode.Passive:
                    segment.U32(5);
                    WriteRefType(referenceType, segment);
                    break;
                default:
                    segment.U32(7);
                    WriteRefType(referenceType, segment);
                    break;
            }

            segment.U32((uint)expressions.Count);
            foreach (var expression in expressions)
            {
                EncodeConstantExpression(expression, segment);
            }
        }

        _elementCount++;
    }

    private static bool IsInstructionList(SNode node) => node.Head is { } head && (WatInstructions.TryGet(head, out _) || head is "block" or "loop" or "if");

    private void EncodeDataSegment(SNode field)
    {
        var items = field.Items!;
        var index = 1;
        GetId(items, ref index);
        SNode? memoryNode = null;
        SNode? offset = null;
        if (index < items.Count && items[index].IsListOf("memory"))
        {
            if (items[index].Items!.Count != 2)
                throw new WatException("malformed memory use", items[index].Position);

            memoryNode = items[index].Items![1];
            index++;
        }
        else if (index + 1 < items.Count && items[index] is { IsList: false, Kind: TokenKind.Number } && items[index + 1].IsList)
        {
            // The legacy form of the memory index: (data 0 (i32.const 0) ...)
            memoryNode = items[index];
            index++;
        }

        if (index < items.Count && items[index].IsList)
        {
            offset = items[index];
            index++;
        }

        var bytes = new List<byte>();
        for (; index < items.Count; index++)
        {
            bytes.AddRange(GetDataString(items[index]));
        }

        if (offset is null)
        {
            _data.U32(1);
        }
        else
        {
            var memoryIndex = memoryNode is null ? 0 : ResolveIndex(memoryNode, _memorySpace);
            if (memoryIndex == 0 && memoryNode is null)
            {
                _data.U32(0);
            }
            else
            {
                _data.U32(2);
                _data.U32((uint)memoryIndex);
            }

            EncodeOffset(offset, _data);
        }

        _data.U32((uint)bytes.Count);
        _data.Bytes([.. bytes]);
        _dataCount++;
    }
}

