using System.Text;

namespace Meziantou.Framework.WasmEngine.Text;

// The types: value types, function types, component and instance types, resource types, core types and core module types
internal sealed partial class ComponentWatParser
{
    private static readonly Dictionary<string, byte> PrimitiveValueTypes = new(StringComparer.Ordinal)
    {
        ["bool"] = 0x7F,
        ["s8"] = 0x7E,
        ["u8"] = 0x7D,
        ["s16"] = 0x7C,
        ["u16"] = 0x7B,
        ["s32"] = 0x7A,
        ["u32"] = 0x79,
        ["s64"] = 0x78,
        ["u64"] = 0x77,
        ["f32"] = 0x76,
        ["float32"] = 0x76,
        ["f64"] = 0x75,
        ["float64"] = 0x75,
        ["char"] = 0x74,
        ["string"] = 0x73,
        ["error-context"] = 0x64,
    };

    private static bool IsDefinedValueTypeList(SNode node) => node.Head is "record" or "variant" or "list" or "tuple" or "flags" or "enum" or "option" or "result" or "own" or "borrow" or "stream" or "future" or "map";

    /// <summary>A reference to a type: <c>(type idx "name"*)</c>.</summary>
    private static bool IsTypeReference(SNode node)
    {
        if (!node.IsListOf("type") || node.Items!.Count < 2 || !IsIndex(node.Items[1]))
            return false;

        for (var i = 2; i < node.Items.Count; i++)
        {
            if (node.Items[i] is not { IsList: false, Kind: TokenKind.String })
                return false;
        }

        return true;
    }

    /// <summary>Defines a type of the scope (an inline type, or a type definition).</summary>
    private uint DefineType(ByteWriter content, string? id, int position)
    {
        _scope.Add(ItemKind.Type, content, position);
        return _scope[Sort.Type].Add(id, Sort.Type, position);
    }

    /// <summary>Encodes a type definition (<c>(type $id? deftype)</c>) and returns its index.</summary>
    private uint EncodeTypeDefinition(SNode definition, string? id, int position)
    {
        var content = new ByteWriter();
        if (!definition.IsList)
        {
            EncodeDefinedValueType(definition, content);
        }
        else
        {
            var items = definition.Items!;
            switch (definition.Head)
            {
                case "func":
                    PrepareFunctionType(items, 1, items.Count);
                    EncodeFunctionType(definition, items, 1, items.Count, content);
                    break;

                case "component":
                    content = EncodeTypeScope(ScopeKind.ComponentType, id, items, 1);
                    break;

                case "instance":
                    content = EncodeTypeScope(ScopeKind.InstanceType, id, items, 1);
                    break;

                case "resource":
                    EncodeResourceType(definition, content);
                    break;

                default:
                    PrepareDefinedValueType(definition);
                    EncodeDefinedValueType(definition, content);
                    break;
            }
        }

        return DefineType(content, id, position);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Inline types: each one is a new type, defined before the definition that uses it (as the reference tools do, the
    // inline types of a component are not merged with the equal types)
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>Defines the inline type of a value type, after its own inline types.</summary>
    private void PrepareValueType(SNode node)
    {
        if (!IsDefinedValueTypeList(node) || _hoisted.ContainsKey(node))
            return;

        PrepareDefinedValueType(node);
        var content = new ByteWriter();
        EncodeDefinedValueType(node, content);
        _hoisted[node] = DefineType(content, id: null, node.Position);
    }

    /// <summary>Defines the inline types of the value types of a defined value type.</summary>
    private void PrepareDefinedValueType(SNode node)
    {
        if (!node.IsList)
            return;

        WatException.ThrowIfNestedTooDeeply(node.Position);
        var items = node.Items!;
        switch (node.Head)
        {
            case "record":
                foreach (var field in items.Skip(1))
                {
                    if (field.IsListOf("field") && field.Items!.Count == 3)
                    {
                        PrepareValueType(field.Items[2]);
                    }
                }

                break;

            case "variant":
                foreach (var @case in items.Skip(1))
                {
                    if (@case.IsListOf("case"))
                    {
                        var index = 1;
                        ReadId(@case.Items!, ref index);
                        if (index + 1 < @case.Items!.Count && !@case.Items[index + 1].IsListOf("refines"))
                        {
                            PrepareValueType(@case.Items[index + 1]);
                        }
                    }
                }

                break;

            case "list" or "option" or "stream" or "future":
                if (items.Count > 1)
                {
                    PrepareValueType(items[1]);
                }

                break;

            case "tuple" or "map":
                foreach (var item in items.Skip(1))
                {
                    PrepareValueType(item);
                }

                break;

            case "result":
                foreach (var item in items.Skip(1))
                {
                    if (item.IsListOf("error"))
                    {
                        if (item.Items!.Count == 2)
                        {
                            PrepareValueType(item.Items[1]);
                        }
                    }
                    else
                    {
                        PrepareValueType(item);
                    }
                }

                break;
        }
    }

    /// <summary>Defines the inline types of the parameters and of the result of a function type.</summary>
    private void PrepareFunctionType(List<SNode> items, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (items[i].IsListOf("param") || items[i].IsListOf("result"))
            {
                var clause = items[i].Items!;
                PrepareValueType(clause[^1]);
            }
        }
    }

    /// <summary>Defines the inline function type of a type use (<c>(type idx)</c>, or the parameters and the result).</summary>
    private void PrepareFunctionTypeUse(SNode owner, List<SNode> items, int start, int end)
    {
        if ((end - start == 1 && IsTypeReference(items[start])) || _hoisted.ContainsKey(owner))
            return;

        PrepareFunctionType(items, start, end);
        var content = new ByteWriter();
        EncodeFunctionType(owner, items, start, end, content);
        _hoisted[owner] = DefineType(content, id: null, owner.Position);
    }

    private uint EncodeFunctionTypeUse(SNode owner, List<SNode> items, int start, int end)
    {
        if (end - start == 1 && IsTypeReference(items[start]))
            return ResolveReference(items[start], Sort.Type);

        PrepareFunctionTypeUse(owner, items, start, end);
        return _hoisted[owner];
    }

    /// <summary>Defines the inline type of an extern type (of an import, of an export declaration, or of the type ascription of an export).</summary>
    private void PrepareExternType(SNode node)
    {
        if (!node.IsList || _hoisted.ContainsKey(node))
            return;

        var items = node.Items!;
        var index = 0;
        var sort = ReadSort(items, ref index, core: false, node.Position);
        ReadId(items, ref index);
        switch (sort)
        {
            case Sort.Func:
                PrepareFunctionTypeUse(node, items, index, items.Count);
                break;

            case Sort.Component or Sort.Instance:
            {
                if (index == items.Count - 1 && IsTypeReference(items[index]))
                    return;

                var content = EncodeTypeScope(sort == Sort.Component ? ScopeKind.ComponentType : ScopeKind.InstanceType, id: null, items, index);
                _hoisted[node] = DefineType(content, id: null, node.Position);
                break;
            }

            case Sort.CoreModule:
            {
                if (index == items.Count - 1 && IsTypeReference(items[index]))
                    return;

                var content = EncodeTypeScope(ScopeKind.ModuleType, id: null, items, index);
                _scope.Add(ItemKind.CoreType, content, node.Position);
                _hoisted[node] = _scope[Sort.CoreType].Add(null, Sort.CoreType, node.Position);
                break;
            }

            case Sort.Type:
                if (index < items.Count && items[index].IsListOf("eq") && items[index].Items!.Count == 2)
                {
                    PrepareValueType(items[index].Items![1]);
                }

                break;

            case Sort.Value:
                if (index < items.Count && !items[index].IsListOf("eq"))
                {
                    PrepareValueType(items[index]);
                }

                break;
        }
    }

    /// <summary>Encodes an extern type, and returns its sort and the name that it binds.</summary>
    private (Sort Sort, string? Id) EncodeExternType(SNode node, ByteWriter writer)
    {
        if (!node.IsList)
            throw new WatException("expected an extern type", node.Position);

        var items = node.Items!;
        var index = 0;
        var sort = ReadSort(items, ref index, core: false, node.Position);
        var id = ReadId(items, ref index);
        switch (sort)
        {
            case Sort.Func:
                writer.Byte(0x01);
                writer.U32(EncodeFunctionTypeUse(node, items, index, items.Count));
                break;

            case Sort.Component or Sort.Instance:
                writer.Byte(sort == Sort.Component ? (byte)0x04 : (byte)0x05);
                if (index == items.Count - 1 && IsTypeReference(items[index]))
                {
                    writer.U32(ResolveReference(items[index], Sort.Type));
                }
                else
                {
                    PrepareExternType(node);
                    writer.U32(_hoisted[node]);
                }

                break;

            case Sort.CoreModule:
                writer.Byte(0x00);
                writer.Byte(0x11);
                if (index == items.Count - 1 && IsTypeReference(items[index]))
                {
                    writer.U32(ResolveReference(items[index], Sort.CoreType));
                }
                else
                {
                    PrepareExternType(node);
                    writer.U32(_hoisted[node]);
                }

                break;

            case Sort.Type:
                if (index != items.Count - 1)
                    throw new WatException("malformed type bound", node.Position);

                writer.Byte(0x03);
                var bound = items[index];
                if (bound.IsListOf("eq") && bound.Items!.Count == 2)
                {
                    writer.Byte(0x00);
                    writer.U32(ResolveTypeIndex(bound.Items[1]));
                }
                else if (bound.IsListOf("sub") && bound.Items!.Count == 2 && bound.Items[1].IsKeyword("resource"))
                {
                    writer.Byte(0x01);
                }
                else
                {
                    throw new WatException("malformed type bound", bound.Position);
                }

                break;

            case Sort.Value:
                if (index != items.Count - 1)
                    throw new WatException("malformed value type", node.Position);

                writer.Byte(0x02);
                if (items[index].IsListOf("eq") && items[index].Items!.Count == 2)
                {
                    writer.Byte(0x00);
                    writer.U32(ResolveReference(items[index].Items![1], Sort.Value));
                }
                else
                {
                    writer.Byte(0x01);
                    EncodeValueType(items[index], writer);
                }

                break;

            default:
                throw new WatException("malformed extern type", node.Position);
        }

        return (sort, id);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Value types
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>Resolves a type index: an index, <c>(type idx "name"*)</c>, or an inline defined value type.</summary>
    private uint ResolveTypeIndex(SNode node)
    {
        if (IsDefinedValueTypeList(node))
        {
            PrepareValueType(node);
            return _hoisted[node];
        }

        return ResolveReference(node, Sort.Type);
    }

    private void EncodeValueType(SNode node, ByteWriter writer)
    {
        if (node is { IsList: false, Kind: TokenKind.Keyword })
        {
            if (!PrimitiveValueTypes.TryGetValue(node.Text, out var code))
                throw new WatException("unknown value type " + node.Text, node.Position);

            writer.Byte(code);
            return;
        }

        // A type index is a positive s33
        writer.S64(ResolveTypeIndex(node));
    }

    private void EncodeDefinedValueType(SNode node, ByteWriter writer)
    {
        if (!node.IsList)
        {
            if (node.Kind != TokenKind.Keyword || !PrimitiveValueTypes.TryGetValue(node.Text, out var code))
                throw new WatException("malformed defined type", node.Position);

            writer.Byte(code);
            return;
        }

        WatException.ThrowIfNestedTooDeeply(node.Position);
        var items = node.Items!;
        switch (node.Head)
        {
            case "record":
            {
                var fields = new ByteWriter();
                for (var i = 1; i < items.Count; i++)
                {
                    var field = items[i];
                    if (!field.IsListOf("field") || field.Items!.Count != 3)
                        throw new WatException("malformed record field", field.Position);

                    WriteName(field.Items[1], fields);
                    EncodeValueType(field.Items[2], fields);
                }

                writer.Byte(0x72);
                writer.Vector(fields, items.Count - 1);
                break;
            }

            case "variant":
            {
                var cases = new ByteWriter();
                for (var i = 1; i < items.Count; i++)
                {
                    var @case = items[i];
                    if (!@case.IsListOf("case"))
                        throw new WatException("malformed variant case", @case.Position);

                    var caseItems = @case.Items!;
                    var index = 1;
                    ReadId(caseItems, ref index);
                    if (index >= caseItems.Count)
                        throw new WatException("malformed variant case", @case.Position);

                    WriteName(caseItems[index++], cases);
                    if (index < caseItems.Count && !caseItems[index].IsListOf("refines"))
                    {
                        cases.Byte(0x01);
                        EncodeValueType(caseItems[index++], cases);
                    }
                    else
                    {
                        cases.Byte(0x00);
                    }

                    if (index < caseItems.Count && caseItems[index].IsListOf("refines"))
                    {
                        // The refinements were removed: the reserved byte is 0x00 without refinement
                        cases.Byte(0x01);
                        cases.U32(ParseIndex(caseItems[index].Items![^1]));
                        index++;
                    }
                    else
                    {
                        cases.Byte(0x00);
                    }

                    if (index != caseItems.Count)
                        throw new WatException("malformed variant case", @case.Position);
                }

                writer.Byte(0x71);
                writer.Vector(cases, items.Count - 1);
                break;
            }

            case "list":
                if (items.Count == 2)
                {
                    writer.Byte(0x70);
                    EncodeValueType(items[1], writer);
                }
                else if (items.Count == 3)
                {
                    writer.Byte(0x67);
                    EncodeValueType(items[1], writer);
                    writer.U32(ParseIndex(items[2]));
                }
                else
                {
                    throw new WatException("malformed list type", node.Position);
                }

                break;

            case "tuple":
            {
                var types = new ByteWriter();
                for (var i = 1; i < items.Count; i++)
                {
                    EncodeValueType(items[i], types);
                }

                writer.Byte(0x6F);
                writer.Vector(types, items.Count - 1);
                break;
            }

            case "flags" or "enum":
            {
                var labels = new ByteWriter();
                for (var i = 1; i < items.Count; i++)
                {
                    WriteName(items[i], labels);
                }

                writer.Byte(node.Head == "flags" ? (byte)0x6E : (byte)0x6D);
                writer.Vector(labels, items.Count - 1);
                break;
            }

            case "option":
                if (items.Count != 2)
                    throw new WatException("malformed option type", node.Position);

                writer.Byte(0x6B);
                EncodeValueType(items[1], writer);
                break;

            case "result":
            {
                SNode? ok = null;
                SNode? error = null;
                var index = 1;
                if (index < items.Count && !items[index].IsListOf("error"))
                {
                    ok = items[index++];
                }

                if (index < items.Count && items[index].IsListOf("error"))
                {
                    if (items[index].Items!.Count != 2)
                        throw new WatException("malformed result type", items[index].Position);

                    error = items[index].Items![1];
                    index++;
                }

                if (index != items.Count)
                    throw new WatException("malformed result type", node.Position);

                writer.Byte(0x6A);
                WriteOptionalValueType(ok, writer);
                WriteOptionalValueType(error, writer);
                break;
            }

            case "own" or "borrow":
                if (items.Count != 2)
                    throw new WatException("malformed handle type", node.Position);

                writer.Byte(node.Head == "own" ? (byte)0x69 : (byte)0x68);
                writer.U32(ResolveReference(items[1], Sort.Type));
                break;

            case "stream" or "future":
                if (items.Count > 2)
                    throw new WatException("malformed " + node.Head + " type", node.Position);

                writer.Byte(node.Head == "stream" ? (byte)0x66 : (byte)0x65);
                WriteOptionalValueType(items.Count == 2 ? items[1] : null, writer);
                break;

            case "map":
                if (items.Count != 3)
                    throw new WatException("malformed map type", node.Position);

                writer.Byte(0x63);
                EncodeValueType(items[1], writer);
                EncodeValueType(items[2], writer);
                break;

            default:
                throw new WatException("malformed defined type", node.Position);
        }
    }

    private void WriteOptionalValueType(SNode? node, ByteWriter writer)
    {
        if (node is null)
        {
            writer.Byte(0x00);
            return;
        }

        writer.Byte(0x01);
        EncodeValueType(node, writer);
    }

    /// <summary>Encodes a function type: <c>async? (param "label" valtype)* (result valtype)?</c>.</summary>
    private void EncodeFunctionType(SNode owner, List<SNode> items, int start, int end, ByteWriter writer)
    {
        var index = start;
        var isAsync = index < end && items[index].IsKeyword("async");
        if (isAsync)
        {
            index++;
        }

        var parameters = new ByteWriter();
        var parameterCount = 0;
        for (; index < end && items[index].IsListOf("param"); index++)
        {
            var parameter = items[index].Items!;
            if (parameter.Count != 3)
                throw new WatException("malformed parameter: expected a label and a type", items[index].Position);

            WriteName(parameter[1], parameters);
            EncodeValueType(parameter[2], parameters);
            parameterCount++;
        }

        writer.Byte(isAsync ? (byte)0x43 : (byte)0x40);
        writer.Vector(parameters, parameterCount);
        if (index < end && items[index].IsListOf("result"))
        {
            var result = items[index].Items!;
            if (result.Count == 2)
            {
                writer.Byte(0x00);
                EncodeValueType(result[1], writer);
            }
            else if (result.Count == 1)
            {
                writer.Byte(0x01);
                writer.Byte(0x00);
            }
            else
            {
                throw new WatException("malformed result: expected a type", items[index].Position);
            }

            index++;
        }
        else
        {
            writer.Byte(0x01);
            writer.Byte(0x00);
        }

        if (index != end)
            throw new WatException("malformed function type", index < items.Count ? items[index].Position : owner.Position);
    }

    /// <summary>Encodes <c>(resource (rep i32|i64) (dtor funcidx)?)</c>.</summary>
    private void EncodeResourceType(SNode node, ByteWriter writer)
    {
        var items = node.Items!;
        if (items.Count < 2 || !items[1].IsListOf("rep") || items[1].Items!.Count != 2)
            throw new WatException("malformed resource type: expected a representation", node.Position);

        writer.Byte(0x3F);
        writer.Byte(items[1].Items![1] switch
        {
            { IsList: false, Text: "i32" } => 0x7F,
            { IsList: false, Text: "i64" } => 0x7E,
            _ => throw new WatException("malformed resource type: unknown representation", items[1].Position),
        });
        if (items.Count == 2)
        {
            writer.Byte(0x00);
            return;
        }

        if (items.Count != 3 || !items[2].IsListOf("dtor") || items[2].Items!.Count != 2)
            throw new WatException("malformed resource type", node.Position);

        writer.Byte(0x01);
        writer.U32(ResolveReference(items[2].Items![1], Sort.CoreFunc));
    }

    // ------------------------------------------------------------------------------------------------------------
    // Component types, instance types and core module types
    // ------------------------------------------------------------------------------------------------------------

    private ByteWriter EncodeTypeScope(ScopeKind kind, string? id, List<SNode> items, int start)
    {
        var scope = new Scope(kind, id, _scope);
        var previous = _scope;
        _scope = scope;
        try
        {
            for (var i = start; i < items.Count; i++)
            {
                WatException.ThrowIfNestedTooDeeply(items[i].Position);
                if (kind == ScopeKind.ModuleType)
                {
                    EncodeModuleTypeDeclaration(items[i]);
                }
                else
                {
                    EncodeTypeDeclaration(items[i]);
                }
            }

            EncodePendingInlineExports();
        }
        finally
        {
            _scope = previous;
        }

        var writer = new ByteWriter();
        writer.Byte(kind switch
        {
            ScopeKind.ComponentType => 0x41,
            ScopeKind.InstanceType => 0x42,
            _ => 0x50,
        });
        writer.Vector(scope.Declarations, scope.DeclarationCount);
        return writer;
    }

    /// <summary>Encodes a declaration of a component type or of an instance type.</summary>
    private void EncodeTypeDeclaration(SNode declaration)
    {
        switch (declaration.Head)
        {
            case "core":
                if (declaration.Items!.Count < 2 || declaration.Items[1] is not { IsList: false, Text: "type" or "rec" })
                    throw new WatException("malformed declaration: expected a core type", declaration.Position);

                EncodeCoreType(declaration);
                break;

            case "type":
                EncodeTypeField(declaration);
                break;

            case "alias":
                EncodeAlias(declaration);
                break;

            case "import":
                EncodeImportOrExportDeclaration(declaration, ItemKind.Import);
                break;

            case "export":
                EncodeImportOrExportDeclaration(declaration, ItemKind.Export);
                break;

            default:
                throw new WatException("malformed declaration", declaration.Position);
        }
    }

    /// <summary>Encodes <c>(core type $id? deftype)</c> or <c>(core rec (type ...)*)</c>.</summary>
    private void EncodeCoreType(SNode field)
    {
        var items = field.Items!;
        var content = new ByteWriter();
        if (items[1].IsKeyword("rec"))
        {
            var names = CreateCoreTypeEncoder().EncodeCoreTypes(SNode.List(field.Position, items.Skip(1).ToList()), content, prefixNonFinalSubType: false);
            _scope.Add(ItemKind.CoreType, content, field.Position);
            foreach (var name in names)
            {
                _scope[Sort.CoreType].Add(name, Sort.CoreType, field.Position);
            }

            return;
        }

        var index = 2;
        var id = ReadId(items, ref index);
        if (index == items.Count - 1 && items[index].IsListOf("alias"))
        {
            EncodeAliasTarget(items[index], Sort.CoreType, id);
            return;
        }

        if (index != items.Count - 1)
            throw new WatException("malformed core type", field.Position);

        if (items[index].IsListOf("module"))
        {
            content = EncodeTypeScope(ScopeKind.ModuleType, id, items[index].Items!, 1);
            _scope.Add(ItemKind.CoreType, content, field.Position);
            _scope[Sort.CoreType].Add(id, Sort.CoreType, field.Position);
            return;
        }

        CreateCoreTypeEncoder().EncodeCoreTypes(SNode.List(field.Position, items.Skip(1).ToList()), content, prefixNonFinalSubType: true);
        _scope.Add(ItemKind.CoreType, content, field.Position);
        _scope[Sort.CoreType].Add(id, Sort.CoreType, field.Position);
    }

    private WatParser CreateCoreTypeEncoder()
    {
        var types = _scope[Sort.CoreType];
        return WatParser.ForCoreTypes(types.Names, types.Count);
    }

    /// <summary>Encodes a declaration of a core module type: an import, a type, an outer alias or an export.</summary>
    private void EncodeModuleTypeDeclaration(SNode declaration)
    {
        var items = declaration.Items ?? throw new WatException("malformed module type declaration", declaration.Position);
        switch (declaration.Head)
        {
            case "import":
            {
                if (items.Count != 4)
                    throw new WatException("malformed import", declaration.Position);

                PrepareCoreExternType(items[3]);
                var content = new ByteWriter();
                WriteName(items[1], content);
                WriteName(items[2], content);
                var (sort, id) = EncodeCoreExternType(items[3], content);
                _scope.Add(ItemKind.Import, content, declaration.Position);
                _scope[sort].Add(id, sort, declaration.Position);
                break;
            }

            case "export":
            {
                if (items.Count != 3)
                    throw new WatException("malformed export", declaration.Position);

                PrepareCoreExternType(items[2]);
                var content = new ByteWriter();
                WriteName(items[1], content);
                var (sort, _) = EncodeCoreExternType(items[2], content);
                _scope.Add(ItemKind.Export, content, declaration.Position);
                _scope[sort].Add(null, sort, declaration.Position);
                break;
            }

            case "type" or "rec":
            {
                var content = new ByteWriter();
                var names = CreateCoreTypeEncoder().EncodeCoreTypes(declaration, content, prefixNonFinalSubType: false);
                _scope.Add(ItemKind.CoreType, content, declaration.Position);
                var first = _scope[Sort.CoreType].Count;
                foreach (var name in names)
                {
                    _scope[Sort.CoreType].Add(name, Sort.CoreType, declaration.Position);
                }

                // A function type of its own is reused by the inline function types with the same signature
                if (declaration.IsListOf("type") && items[^1].IsListOf("func"))
                {
                    _scope.FunctionTypes.TryAdd(GetCoreSignatureKey(items[^1].Items!, 1), first);
                }

                break;
            }

            case "alias":
                EncodeAlias(declaration);
                break;

            default:
                throw new WatException("malformed module type declaration", declaration.Position);
        }
    }

    /// <summary>The key of the signature of a core function type: the types of the parameters and of the results.</summary>
    private static string GetCoreSignatureKey(List<SNode> items, int start)
    {
        var builder = new StringBuilder();
        for (var i = start; i < items.Count; i++)
        {
            var clause = items[i];
            if (!clause.IsListOf("param") && !clause.IsListOf("result"))
                continue;

            foreach (var type in clause.Items!.Skip(1))
            {
                if (type is { IsList: false, Kind: TokenKind.Id })
                    continue;

                builder.Append(clause.Head == "param" ? 'p' : 'r').Append(' ').Append(type).Append(';');
            }
        }

        return builder.ToString();
    }

    /// <summary>Defines the inline function type of an imported or exported core function or tag of a module type.</summary>
    private void PrepareCoreExternType(SNode node)
    {
        if (node.Head is not ("func" or "tag") || _hoisted.ContainsKey(node))
            return;

        var items = node.Items!;
        var index = 1;
        ReadId(items, ref index);
        if (index < items.Count && items[index].IsListOf("type"))
            return;

        // As the reference tools do, an inline function type reuses a function type declaration of the module type (not a
        // preceding inline function type)
        if (_scope.FunctionTypes.TryGetValue(GetCoreSignatureKey(items, index), out var existing))
        {
            _hoisted[node] = existing;
            return;
        }

        var function = new List<SNode> { SNode.Token(node.Position, TokenKind.Keyword, "func") };
        function.AddRange(items.Skip(index));
        var definition = SNode.List(node.Position, [SNode.Token(node.Position, TokenKind.Keyword, "type"), SNode.List(node.Position, function)]);
        var content = new ByteWriter();
        CreateCoreTypeEncoder().EncodeCoreTypes(definition, content, prefixNonFinalSubType: false);
        _scope.Add(ItemKind.CoreType, content, node.Position);
        _hoisted[node] = _scope[Sort.CoreType].Add(null, Sort.CoreType, node.Position);
    }

    /// <summary>Encodes the type of a core import or export: <c>(func ...)</c>, <c>(table ...)</c>, <c>(memory ...)</c>, <c>(global ...)</c> or <c>(tag ...)</c>.</summary>
    private (Sort Sort, string? Id) EncodeCoreExternType(SNode node, ByteWriter writer)
    {
        if (!node.IsList || node.Head is not { } head || !CoreSorts.TryGetValue(head, out var sort))
            throw new WatException("malformed core extern type", node.Position);

        var items = node.Items!;
        var index = 1;
        var id = ReadId(items, ref index);
        switch (sort)
        {
            case Sort.CoreFunc or Sort.CoreTag:
            {
                uint typeIndex;
                if (index < items.Count && items[index].IsListOf("type"))
                {
                    typeIndex = ResolveReference(items[index], Sort.CoreType);
                }
                else
                {
                    PrepareCoreExternType(node);
                    typeIndex = _hoisted[node];
                }

                if (sort == Sort.CoreFunc)
                {
                    writer.Byte(0x00);
                }
                else
                {
                    writer.Byte(0x04);
                    writer.Byte(0x00);
                }

                writer.U32(typeIndex);
                break;
            }

            case Sort.CoreTable:
                writer.Byte(0x01);
                CreateCoreTypeEncoder().EncodeCoreTableType(items, ref index, writer);
                break;

            case Sort.CoreMemory:
                writer.Byte(0x02);
                WatParser.EncodeCoreMemoryType(items, ref index, writer);
                break;

            case Sort.CoreGlobal:
                if (index >= items.Count)
                    throw new WatException("malformed global type", node.Position);

                writer.Byte(0x03);
                CreateCoreTypeEncoder().EncodeCoreGlobalType(items[index], writer);
                break;

            default:
                throw new WatException("malformed core extern type", node.Position);
        }

        return (sort, id);
    }
}
