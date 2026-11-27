namespace Meziantou.Framework.WasmEngine.Components.Binary;

/// <summary>The index spaces of a component (sorts): the core sorts, and the component sorts.</summary>
internal enum ComponentSort
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

/// <summary>A decoded component: its definitions in the order of the binary, with the indices of the binary.</summary>
internal sealed class ComponentAst
{
    public List<ComponentDefinition> Definitions { get; } = [];
}

internal abstract record ComponentDefinition;

/// <summary>A core module: the bytes of a core module binary.</summary>
internal sealed record CoreModuleDefinition(ReadOnlyMemory<byte> Bytes) : ComponentDefinition;

/// <summary>A core instance: the instantiation of a core module, or the inline exports of core definitions.</summary>
internal sealed record CoreInstanceDefinition(int ModuleIndex, (string Name, int InstanceIndex)[]? Arguments, (string Name, ComponentSort Sort, int Index)[]? Exports) : ComponentDefinition;

/// <summary>A core type: a core module type or a core function type.</summary>
internal sealed record CoreTypeDefinition(CoreTypeExpr Type) : ComponentDefinition;

/// <summary>A nested component.</summary>
internal sealed record NestedComponentDefinition(ComponentAst Component) : ComponentDefinition;

/// <summary>An instance: the instantiation of a component, or the inline exports of definitions.</summary>
internal sealed record InstanceDefinition(int ComponentIndex, (string Name, ComponentSort Sort, int Index)[]? Arguments, (string Name, ComponentSort Sort, int Index)[]? Exports, ComponentNameAttributes?[]? ExportAttributes = null) : ComponentDefinition;

internal enum AliasKind
{
    Export,
    CoreExport,
    Outer,
}

/// <summary>An alias: an export of an instance or a core instance, or a definition of an enclosing component.</summary>
internal sealed record AliasDefinition(ComponentSort Sort, AliasKind Kind, int Index, string? Name, int OuterCount) : ComponentDefinition;

/// <summary>A type definition.</summary>
internal sealed record TypeDefinition(TypeExpr Type) : ComponentDefinition;

/// <summary>A canonical definition: a lift, a lower, or a built-in.</summary>
internal sealed record CanonDefinition(CanonKind Kind, int Index, int Index2, CanonOptions Options, CoreValKind CoreType = CoreValKind.I32, bool Async = false, TypeExpr? ResultType = null, bool HasResultType = false) : ComponentDefinition;

/// <summary>An import.</summary>
internal sealed record ImportDefinition(string Name, ExternTypeExpr Type, ComponentNameAttributes? Attributes = null) : ComponentDefinition;

/// <summary>An export.</summary>
internal sealed record ExportDefinition(string Name, ComponentSort Sort, int Index, ExternTypeExpr? Type, ComponentNameAttributes? Attributes = null) : ComponentDefinition;

/// <summary>The attributes of the name of an import or an export (null when absent).</summary>
/// <param name="Implements">The interface that a plain name implements (<c>implements</c>).</param>
/// <param name="VersionSuffix">The end of the version of the interface (<c>version-suffix</c>).</param>
/// <param name="ExternalId">An identifier for the tools (<c>external-id</c>).</param>
internal sealed record ComponentNameAttributes(string? Implements, string? VersionSuffix, string? ExternalId);

/// <summary>A start definition (value definitions are not supported: decoding rejects them).</summary>
internal sealed record StartDefinition(int FunctionIndex, int[] Arguments, int ResultCount) : ComponentDefinition;

internal enum CanonKind
{
    Lift,
    Lower,
    ResourceNew,
    ResourceDrop,
    ResourceDropAsync,
    ResourceRep,
    BackpressureInc,
    BackpressureDec,
    TaskReturn,
    TaskCancel,
    ContextGet,
    ContextSet,
    ThreadYield,
    SubtaskCancel,
    SubtaskDrop,
    StreamNew,
    StreamRead,
    StreamWrite,
    StreamCancelRead,
    StreamCancelWrite,
    StreamDropReadable,
    StreamDropWritable,
    StreamForward,
    FutureNew,
    FutureRead,
    FutureWrite,
    FutureCancelRead,
    FutureCancelWrite,
    FutureDropReadable,
    FutureDropWritable,
    FutureForward,
    ErrorContextNew,
    ErrorContextDebugMessage,
    ErrorContextDrop,
    WaitableSetNew,
    WaitableSetWait,
    WaitableSetPoll,
    WaitableSetDrop,
    WaitableJoin,
    ThreadIndex,
    ThreadNewIndirect,
    ThreadResumeLater,
    ThreadSuspend,
    ThreadSuspendThenResume,
    ThreadYieldThenResume,
    ThreadSuspendThenPromote,
    ThreadYieldThenPromote,
}

/// <summary>The options of a canonical definition.</summary>
internal sealed record CanonOptions
{
    public static CanonOptions Empty { get; } = new();

    public ComponentStringEncoding? StringEncoding { get; init; }

    public int? Memory { get; init; }

    public int? Realloc { get; init; }

    public int? PostReturn { get; init; }

    public bool Async { get; init; }

    public int? Callback { get; init; }
}

internal enum CoreValKind
{
    I32,
    I64,
    F32,
    F64,
    V128,
    Ref,
}

// ------------------------------------------------------------------------------------------------------------
// Types, with the indices of the binary
// ------------------------------------------------------------------------------------------------------------

internal abstract record TypeExpr;

/// <summary>A primitive value type.</summary>
internal sealed record PrimitiveTypeExpr(ComponentValueTypeKind Kind) : TypeExpr;

/// <summary>A reference to a type of the type index space (a <c>valtype</c> that is a type index).</summary>
internal sealed record IndexTypeExpr(int Index) : TypeExpr;

internal sealed record RecordTypeExpr((string Name, TypeExpr Type)[] Fields) : TypeExpr;

internal sealed record VariantTypeExpr((string Name, TypeExpr? Type)[] Cases) : TypeExpr;

internal sealed record ListTypeExpr(TypeExpr Element, int? Length) : TypeExpr;

internal sealed record TupleTypeExpr(TypeExpr[] Types) : TypeExpr;

internal sealed record FlagsTypeExpr(string[] Labels) : TypeExpr;

internal sealed record EnumTypeExpr(string[] Labels) : TypeExpr;

internal sealed record OptionTypeExpr(TypeExpr Type) : TypeExpr;

internal sealed record ResultTypeExpr(TypeExpr? Ok, TypeExpr? Error) : TypeExpr;

internal sealed record HandleTypeExpr(bool Own, int ResourceIndex) : TypeExpr;

internal sealed record AsyncValueTypeExpr(bool Stream, TypeExpr? Element) : TypeExpr;

internal sealed record MapTypeExpr(TypeExpr Key, TypeExpr Value) : TypeExpr;

internal sealed record FunctionTypeExpr((string Name, TypeExpr Type)[] Parameters, TypeExpr? Result, bool Async) : TypeExpr;

internal sealed record ResourceTypeExpr(bool Is64, int? Destructor) : TypeExpr;

internal sealed record ComponentTypeExpr(TypeDeclaration[] Declarations) : TypeExpr;

internal sealed record InstanceTypeExpr(TypeDeclaration[] Declarations) : TypeExpr;

internal abstract record TypeDeclaration;

internal sealed record CoreTypeDeclaration(CoreTypeExpr Type) : TypeDeclaration;

internal sealed record TypeTypeDeclaration(TypeExpr Type) : TypeDeclaration;

internal sealed record AliasTypeDeclaration(AliasDefinition Alias) : TypeDeclaration;

internal sealed record ExportTypeDeclaration(string Name, ExternTypeExpr Type, ComponentNameAttributes? Attributes = null) : TypeDeclaration;

internal sealed record ImportTypeDeclaration(string Name, ExternTypeExpr Type, ComponentNameAttributes? Attributes = null) : TypeDeclaration;

/// <summary>The type of an import or an export, with the indices of the binary.</summary>
/// <param name="Kind">The kind of the definition.</param>
/// <param name="Index">The type index (or core type index for a module), or the index of the type of an <c>eq</c> bound, or -1 for <c>sub resource</c>.</param>
/// <param name="ValueType">The type of a value.</param>
internal sealed record ExternTypeExpr(ComponentExternKind Kind, int Index, TypeExpr? ValueType = null);

// ------------------------------------------------------------------------------------------------------------
// Core types
// ------------------------------------------------------------------------------------------------------------

internal abstract record CoreTypeExpr;

/// <summary>A core function type: the kinds of its parameters and results, and their types (with the reference types).</summary>
internal sealed record CoreFunctionTypeExpr(CoreValKind[] Parameters, CoreValKind[] Results) : CoreTypeExpr
{
    public WasmValueType[]? ParameterTypes { get; init; }

    public WasmValueType[]? ResultTypes { get; init; }
}

/// <summary>A core type that is not a function type (a struct or an array of the GC types): it is decoded and kept opaque.</summary>
internal sealed record CoreOpaqueTypeExpr : CoreTypeExpr;

/// <summary>A core module type: its declarations.</summary>
internal sealed record CoreModuleTypeExpr(CoreModuleDeclaration[] Declarations) : CoreTypeExpr;

internal abstract record CoreModuleDeclaration;

internal sealed record CoreImportDeclaration(string Module, string Name, CoreExternTypeExpr Type) : CoreModuleDeclaration;

internal sealed record CoreExportDeclaration(string Name, CoreExternTypeExpr Type) : CoreModuleDeclaration;

internal sealed record CoreTypeModuleDeclaration(CoreTypeExpr Type) : CoreModuleDeclaration;

internal sealed record CoreAliasDeclaration(int OuterCount, int Index) : CoreModuleDeclaration;

/// <summary>A core extern type: a function (its type index), a table, a memory, a global or a tag.</summary>
internal sealed record CoreExternTypeExpr(WasmExternKind Kind, int TypeIndex, WasmMemoryType? Memory = null, WasmTableType? Table = null, WasmGlobalType? Global = null);
