namespace Meziantou.Framework.WasmEngine.Text;

// The encoding of the core types of a component (the core type definitions and the declarations of the core module types)
internal sealed partial class WatParser
{
    /// <summary>Creates a parser that encodes core types: the type indices are resolved with <paramref name="typeNames"/>, and a new type has the index <paramref name="typeCount"/>.</summary>
    public static WatParser ForCoreTypes(Dictionary<string, uint> typeNames, uint typeCount)
    {
        var parser = new WatParser();
        foreach (var (name, index) in typeNames)
        {
            parser._typeSpace.Names[name] = (int)Math.Min(index, int.MaxValue);
        }

        parser._typeSpace.Count = (int)Math.Min(typeCount, int.MaxValue);
        return parser;
    }

    /// <summary>
    /// Encodes a type definition <c>(type $id? ...)</c> or a recursion group <c>(rec (type ...)*)</c>, and returns the names of the
    /// types (null for a type without name). In a type definition of a component, a non-final sub type is prefixed by <c>0x00</c> to
    /// distinguish it from a module type (<paramref name="prefixNonFinalSubType"/>).
    /// </summary>
    public List<string?> EncodeCoreTypes(SNode node, ByteWriter writer, bool prefixNonFinalSubType)
    {
        var start = _types.Count;
        if (node.IsListOf("rec"))
        {
            var items = node.Items!;
            for (var i = 1; i < items.Count; i++)
            {
                if (!items[i].IsListOf("type"))
                    throw new WatException("expected a type definition", items[i].Position);

                DeclareType(items[i], inRecGroup: true);
            }

            writer.Byte(0x4E);
            writer.U32((uint)(_types.Count - start));
            for (var i = start; i < _types.Count; i++)
            {
                EncodeTypeDefinition(_types[i], writer);
            }
        }
        else
        {
            DeclareType(node, inRecGroup: false);
            var type = _types[^1];
            if (prefixNonFinalSubType && !type.Final)
            {
                writer.Byte(0x00);
            }

            EncodeTypeDefinition(type, writer);
        }

        var names = new List<string?>();
        for (var i = start; i < _types.Count; i++)
        {
            names.Add(_types[i].Name);
        }

        return names;
    }

    public void EncodeCoreValueType(SNode node, ByteWriter writer) => WriteValType(node, writer);

    public void EncodeCoreTableType(List<SNode> items, ref int index, ByteWriter writer) => WriteTableType(items, ref index, writer);

    public static void EncodeCoreMemoryType(List<SNode> items, ref int index, ByteWriter writer) => WriteMemoryType(items, ref index, writer);

    public void EncodeCoreGlobalType(SNode node, ByteWriter writer) => WriteGlobalType(node, writer);
}
