namespace Meziantou.Framework.WasmEngine;

internal readonly record struct ImportInfo(string Module, string Name, WasmExternKind Kind, int Index);

internal readonly record struct ExportInfo(string Name, WasmExternKind Kind, int Index);

internal sealed class TableInfo
{
    public ValType ElementType { get; init; }

    public ulong Minimum { get; init; }

    public ulong? Maximum { get; init; }

    public bool Is64 { get; init; }

    /// <summary>The initial value of the elements, or null (null references).</summary>
    public CompiledCode? Init { get; init; }

    public ValType AddressType => Is64 ? ValType.I64 : ValType.I32;
}

internal sealed class MemoryInfo
{
    public ulong Minimum { get; init; }

    public ulong? Maximum { get; init; }

    public bool Is64 { get; init; }

    public bool Shared { get; init; }

    public ValType AddressType => Is64 ? ValType.I64 : ValType.I32;
}

internal sealed class GlobalInfo
{
    public ValType Type { get; init; }

    public bool Mutable { get; init; }

    /// <summary>The initial value of a defined global, null for an import.</summary>
    public CompiledCode? Init { get; init; }
}

internal enum SegmentMode : byte
{
    Active,
    Passive,
    Declarative,
}

internal sealed class ElementSegment
{
    public SegmentMode Mode { get; init; }

    public int Table { get; init; }

    public CompiledCode? Offset { get; init; }

    public ValType Type { get; init; }

    /// <summary>The expressions of the elements.</summary>
    public CompiledCode[] Items { get; init; } = [];

    /// <summary>The function indices of the elements when they are all <c>ref.func</c> (the binary forms with function indices), or null.</summary>
    public int[]? FunctionIndices { get; init; }

    public int Count => FunctionIndices?.Length ?? Items.Length;
}

internal sealed class DataSegment
{
    public SegmentMode Mode { get; init; }

    public int Memory { get; init; }

    public CompiledCode? Offset { get; init; }

    public byte[] Bytes { get; init; } = [];
}

/// <summary>A decoded and validated module.</summary>
internal sealed class ModuleInfo
{
    public List<DefinedType> Types { get; } = [];

    public List<ImportInfo> Imports { get; } = [];

    /// <summary>The type of each function: the imported functions first.</summary>
    public List<DefinedType> Functions { get; } = [];

    public int ImportedFunctionCount { get; set; }

    public List<TableInfo> Tables { get; } = [];

    public int ImportedTableCount { get; set; }

    public List<MemoryInfo> Memories { get; } = [];

    public int ImportedMemoryCount { get; set; }

    public List<GlobalInfo> Globals { get; } = [];

    public int ImportedGlobalCount { get; set; }

    /// <summary>The type of each tag (a function type without results).</summary>
    public List<DefinedType> Tags { get; } = [];

    public int ImportedTagCount { get; set; }

    public List<ExportInfo> Exports { get; } = [];

    public int Start { get; set; } = -1;

    public List<ElementSegment> Elements { get; } = [];

    public List<DataSegment> Data { get; } = [];

    /// <summary>The number of data segments of the data count section, or null.</summary>
    public int? DataCount { get; set; }

    /// <summary>The code of the defined functions.</summary>
    public List<CompiledCode> Code { get; } = [];

    /// <summary>The functions that the code can reference with <c>ref.func</c> (referenced outside of the functions, https://webassembly.github.io/spec/core/valid/modules.html#valid-module).</summary>
    public HashSet<int> DeclaredFunctionReferences { get; } = [];
}
