using System.Runtime.InteropServices;

// The native library of Wasmtime is a native asset of the Wasmtime package, resolved by the host from the dependencies of the application
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]

namespace Meziantou.Framework.WebPlatform.Wasmtime.Interop;

/// <summary>
/// The functions of the C API of Wasmtime (https://docs.wasmtime.dev/c-api/), in the native library of the Wasmtime package.
/// The functions that return a <c>wasmtime_error_t*</c> return 0 on success.
/// </summary>
/// <remarks>
/// <c>wasm_valtype_kind</c> aborts the process for the types other than the number types, <c>externref</c> and
/// <c>funcref</c>: the value types are read with <see cref="wasmtime_valtype_new"/>.
/// </remarks>
internal static unsafe partial class NativeMethods
{
    private const string LibraryName = "wasmtime";

    // ----------------------------------------------------------------------------------------------------------------
    // Configuration and engine
    // ----------------------------------------------------------------------------------------------------------------

    [LibraryImport(LibraryName)]
    public static partial nint wasm_config_new();

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_config_epoch_interruption_set(nint config, [MarshalAs(UnmanagedType.U1)] bool value);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_config_max_wasm_stack_set(nint config, nuint value);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_config_wasm_threads_set(nint config, [MarshalAs(UnmanagedType.U1)] bool value);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_config_shared_memory_set(nint config, [MarshalAs(UnmanagedType.U1)] bool value);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_config_wasm_tail_call_set(nint config, [MarshalAs(UnmanagedType.U1)] bool value);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_config_wasm_reference_types_set(nint config, [MarshalAs(UnmanagedType.U1)] bool value);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_config_wasm_function_references_set(nint config, [MarshalAs(UnmanagedType.U1)] bool value);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_config_wasm_gc_set(nint config, [MarshalAs(UnmanagedType.U1)] bool value);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_config_gc_support_set(nint config, [MarshalAs(UnmanagedType.U1)] bool value);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_config_wasm_simd_set(nint config, [MarshalAs(UnmanagedType.U1)] bool value);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_config_wasm_relaxed_simd_set(nint config, [MarshalAs(UnmanagedType.U1)] bool value);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_config_wasm_relaxed_simd_deterministic_set(nint config, [MarshalAs(UnmanagedType.U1)] bool value);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_config_wasm_bulk_memory_set(nint config, [MarshalAs(UnmanagedType.U1)] bool value);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_config_wasm_multi_value_set(nint config, [MarshalAs(UnmanagedType.U1)] bool value);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_config_wasm_multi_memory_set(nint config, [MarshalAs(UnmanagedType.U1)] bool value);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_config_wasm_memory64_set(nint config, [MarshalAs(UnmanagedType.U1)] bool value);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_config_wasm_wide_arithmetic_set(nint config, [MarshalAs(UnmanagedType.U1)] bool value);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_config_wasm_exceptions_set(nint config, [MarshalAs(UnmanagedType.U1)] bool value);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_config_wasm_custom_page_sizes_set(nint config, [MarshalAs(UnmanagedType.U1)] bool value);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_config_parallel_compilation_set(nint config, [MarshalAs(UnmanagedType.U1)] bool value);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_config_cranelift_opt_level_set(nint config, byte value);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_config_signals_based_traps_set(nint config, [MarshalAs(UnmanagedType.U1)] bool value);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_config_native_unwind_info_set(nint config, [MarshalAs(UnmanagedType.U1)] bool value);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_config_memory_may_move_set(nint config, [MarshalAs(UnmanagedType.U1)] bool value);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_config_memory_reservation_set(nint config, ulong value);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_config_memory_guard_size_set(nint config, ulong value);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_config_memory_reservation_for_growth_set(nint config, ulong value);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_config_memory_init_cow_set(nint config, [MarshalAs(UnmanagedType.U1)] bool value);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_config_host_memory_creator_set(nint config, WasmtimeMemoryCreator* creator);

    /// <summary>Takes the ownership of the configuration.</summary>
    [LibraryImport(LibraryName)]
    public static partial nint wasm_engine_new_with_config(nint config);

    [LibraryImport(LibraryName)]
    public static partial void wasm_engine_delete(nint engine);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_engine_increment_epoch(nint engine);

    // ----------------------------------------------------------------------------------------------------------------
    // Errors and traps
    // ----------------------------------------------------------------------------------------------------------------

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_error_message(nint error, WasmByteVec* message);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_error_delete(nint error);

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_error_new(byte* message);

    [LibraryImport(LibraryName)]
    public static partial void wasm_trap_message(nint trap, WasmByteVec* message);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool wasmtime_trap_code(nint trap, byte* code);

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_trap_new(byte* message, nuint length);

    [LibraryImport(LibraryName)]
    public static partial void wasm_trap_delete(nint trap);

    [LibraryImport(LibraryName)]
    public static partial void wasm_byte_vec_delete(WasmByteVec* vec);

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_wat2wasm(byte* wat, nuint length, WasmByteVec* result);

    // ----------------------------------------------------------------------------------------------------------------
    // Types
    // ----------------------------------------------------------------------------------------------------------------

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_valtype_new(nint type, WasmtimeValType* result);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_valtype_delete(WasmtimeValType* type);

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_valtype_to_wasm(nint engine, WasmtimeValType* type);

    [LibraryImport(LibraryName)]
    public static partial void wasm_valtype_delete(nint type);

    [LibraryImport(LibraryName)]
    public static partial void wasm_valtype_vec_new(WasmPointerVec* result, nuint size, nint* data);

    [LibraryImport(LibraryName)]
    public static partial void wasm_valtype_vec_new_empty(WasmPointerVec* result);

    /// <summary>Takes the ownership of the vectors.</summary>
    [LibraryImport(LibraryName)]
    public static partial nint wasm_functype_new(WasmPointerVec* parameters, WasmPointerVec* results);

    [LibraryImport(LibraryName)]
    public static partial WasmPointerVec* wasm_functype_params(nint type);

    [LibraryImport(LibraryName)]
    public static partial WasmPointerVec* wasm_functype_results(nint type);

    [LibraryImport(LibraryName)]
    public static partial nint wasm_functype_copy(nint type);

    [LibraryImport(LibraryName)]
    public static partial void wasm_functype_delete(nint type);

    /// <summary>Takes the ownership of the value type.</summary>
    [LibraryImport(LibraryName)]
    public static partial nint wasm_globaltype_new(nint content, byte mutability);

    [LibraryImport(LibraryName)]
    public static partial nint wasm_globaltype_content(nint type);

    [LibraryImport(LibraryName)]
    public static partial byte wasm_globaltype_mutability(nint type);

    [LibraryImport(LibraryName)]
    public static partial nint wasm_globaltype_copy(nint type);

    [LibraryImport(LibraryName)]
    public static partial void wasm_globaltype_delete(nint type);

    /// <summary>Takes the ownership of the value type.</summary>
    [LibraryImport(LibraryName)]
    public static partial nint wasm_tabletype_new(nint element, WasmLimits* limits);

    [LibraryImport(LibraryName)]
    public static partial nint wasm_tabletype_element(nint type);

    [LibraryImport(LibraryName)]
    public static partial WasmLimits* wasm_tabletype_limits(nint type);

    [LibraryImport(LibraryName)]
    public static partial nint wasm_tabletype_copy(nint type);

    [LibraryImport(LibraryName)]
    public static partial void wasm_tabletype_delete(nint type);

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_memorytype_new(ulong min, [MarshalAs(UnmanagedType.U1)] bool maxPresent, ulong max, [MarshalAs(UnmanagedType.U1)] bool is64, [MarshalAs(UnmanagedType.U1)] bool shared, byte pageSizeLog2, nint* result);

    [LibraryImport(LibraryName)]
    public static partial ulong wasmtime_memorytype_minimum(nint type);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool wasmtime_memorytype_maximum(nint type, ulong* max);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool wasmtime_memorytype_is64(nint type);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool wasmtime_memorytype_isshared(nint type);

    [LibraryImport(LibraryName)]
    public static partial void wasm_memorytype_delete(nint type);

    /// <summary>Takes the ownership of the function type.</summary>
    [LibraryImport(LibraryName)]
    public static partial nint wasm_tagtype_new(nint functionType);

    [LibraryImport(LibraryName)]
    public static partial nint wasm_tagtype_functype(nint type);

    [LibraryImport(LibraryName)]
    public static partial void wasm_tagtype_delete(nint type);

    [LibraryImport(LibraryName)]
    public static partial WasmExternTypeKind wasm_externtype_kind(nint type);

    [LibraryImport(LibraryName)]
    public static partial nint wasm_externtype_as_functype_const(nint type);

    [LibraryImport(LibraryName)]
    public static partial nint wasm_externtype_as_globaltype_const(nint type);

    [LibraryImport(LibraryName)]
    public static partial nint wasm_externtype_as_tabletype_const(nint type);

    [LibraryImport(LibraryName)]
    public static partial nint wasm_externtype_as_memorytype_const(nint type);

    [LibraryImport(LibraryName)]
    public static partial nint wasm_externtype_as_tagtype_const(nint type);

    [LibraryImport(LibraryName)]
    public static partial WasmByteVec* wasm_importtype_module(nint type);

    [LibraryImport(LibraryName)]
    public static partial WasmByteVec* wasm_importtype_name(nint type);

    [LibraryImport(LibraryName)]
    public static partial nint wasm_importtype_type(nint type);

    [LibraryImport(LibraryName)]
    public static partial void wasm_importtype_vec_delete(WasmPointerVec* vec);

    [LibraryImport(LibraryName)]
    public static partial WasmByteVec* wasm_exporttype_name(nint type);

    [LibraryImport(LibraryName)]
    public static partial nint wasm_exporttype_type(nint type);

    [LibraryImport(LibraryName)]
    public static partial void wasm_exporttype_vec_delete(WasmPointerVec* vec);

    // ----------------------------------------------------------------------------------------------------------------
    // Modules
    // ----------------------------------------------------------------------------------------------------------------

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_module_new(nint engine, byte* wasm, nuint length, nint* result);

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_module_validate(nint engine, byte* wasm, nuint length);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_module_delete(nint module);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_module_imports(nint module, WasmPointerVec* result);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_module_exports(nint module, WasmPointerVec* result);

    // ----------------------------------------------------------------------------------------------------------------
    // Stores
    // ----------------------------------------------------------------------------------------------------------------

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_store_new(nint engine, nint data, delegate* unmanaged<nint, void> finalizer);

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_store_context(nint store);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_store_delete(nint store);

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_context_get_data(nint context);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_context_set_epoch_deadline(nint context, ulong ticksBeyondCurrent);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_store_epoch_deadline_callback(nint store, delegate* unmanaged<nint, nint, ulong*, byte*, nint> callback, nint data, delegate* unmanaged<nint, void> finalizer);

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_context_gc(nint context);

    // ----------------------------------------------------------------------------------------------------------------
    // Instances and functions
    // ----------------------------------------------------------------------------------------------------------------

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_instance_new(nint context, nint module, WasmtimeExtern* imports, nuint importCount, WasmtimeInstance* instance, nint* trap);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool wasmtime_instance_export_nth(nint context, WasmtimeInstance* instance, nuint index, byte** name, nuint* nameLength, WasmtimeExtern* item);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_func_new(nint context, nint type, delegate* unmanaged<nint, nint, WasmtimeVal*, nuint, WasmtimeVal*, nuint, nint> callback, nint env, delegate* unmanaged<nint, void> finalizer, WasmtimeFunc* result);

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_func_type(nint context, WasmtimeFunc* function);

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_func_call(nint context, WasmtimeFunc* function, WasmtimeVal* arguments, nuint argumentCount, WasmtimeVal* results, nuint resultCount, nint* trap);

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_caller_context(nint caller);

    // ----------------------------------------------------------------------------------------------------------------
    // Memories, tables, globals and tags
    // ----------------------------------------------------------------------------------------------------------------

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_memory_new(nint context, nint type, WasmtimeIndexedExtern* result);

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_memory_type(nint context, WasmtimeIndexedExtern* memory);

    [LibraryImport(LibraryName)]
    public static partial byte* wasmtime_memory_data(nint context, WasmtimeIndexedExtern* memory);

    [LibraryImport(LibraryName)]
    public static partial ulong wasmtime_memory_size(nint context, WasmtimeIndexedExtern* memory);

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_memory_grow(nint context, WasmtimeIndexedExtern* memory, ulong delta, ulong* previousSize);

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_table_new(nint context, nint type, WasmtimeVal* init, WasmtimeIndexedExtern* result);

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_table_type(nint context, WasmtimeIndexedExtern* table);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool wasmtime_table_get(nint context, WasmtimeIndexedExtern* table, ulong index, WasmtimeVal* value);

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_table_set(nint context, WasmtimeIndexedExtern* table, ulong index, WasmtimeVal* value);

    [LibraryImport(LibraryName)]
    public static partial ulong wasmtime_table_size(nint context, WasmtimeIndexedExtern* table);

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_table_grow(nint context, WasmtimeIndexedExtern* table, ulong delta, WasmtimeVal* init, ulong* previousSize);

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_global_new(nint context, nint type, WasmtimeVal* value, WasmtimeGlobalHandle* result);

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_global_type(nint context, WasmtimeGlobalHandle* global);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_global_get(nint context, WasmtimeGlobalHandle* global, WasmtimeVal* value);

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_global_set(nint context, WasmtimeGlobalHandle* global, WasmtimeVal* value);

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_tag_new(nint context, nint type, WasmtimeIndexedExtern* result);

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_tag_type(nint context, WasmtimeIndexedExtern* tag);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool wasmtime_tag_eq(nint context, WasmtimeIndexedExtern* a, WasmtimeIndexedExtern* b);

    // ----------------------------------------------------------------------------------------------------------------
    // References
    // ----------------------------------------------------------------------------------------------------------------

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_val_unroot(WasmtimeVal* value);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_val_clone(WasmtimeVal* source, WasmtimeVal* destination);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool wasmtime_externref_new(nint context, nint data, delegate* unmanaged<nint, void> finalizer, WasmtimeRef* result);

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_externref_data(nint context, WasmtimeRef* reference);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_anyref_clone(WasmtimeRef* reference, WasmtimeRef* result);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_anyref_unroot(WasmtimeRef* reference);

    [LibraryImport(LibraryName)]
    public static partial uint wasmtime_anyref_to_raw(nint context, WasmtimeRef* reference);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_anyref_from_i31(nint context, uint value, WasmtimeRef* result);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool wasmtime_anyref_is_i31(nint context, WasmtimeRef* reference);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool wasmtime_anyref_i31_get_s(nint context, WasmtimeRef* reference, int* value);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool wasmtime_anyref_is_struct(nint context, WasmtimeRef* reference);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool wasmtime_anyref_is_array(nint context, WasmtimeRef* reference);

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_exnref_new(nint context, WasmtimeIndexedExtern* tag, WasmtimeVal* fields, nuint fieldCount, WasmtimeRef* result);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_exnref_clone(WasmtimeRef* reference, WasmtimeRef* result);

    [LibraryImport(LibraryName)]
    public static partial void wasmtime_exnref_unroot(WasmtimeRef* reference);

    [LibraryImport(LibraryName)]
    public static partial uint wasmtime_exnref_to_raw(nint context, WasmtimeRef* reference);

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_exnref_tag(nint context, WasmtimeRef* reference, WasmtimeIndexedExtern* tag);

    [LibraryImport(LibraryName)]
    public static partial nuint wasmtime_exnref_field_count(nint context, WasmtimeRef* reference);

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_exnref_field(nint context, WasmtimeRef* reference, nuint index, WasmtimeVal* value);

    [LibraryImport(LibraryName)]
    public static partial nint wasmtime_context_set_exception(nint context, WasmtimeRef* exception);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool wasmtime_context_take_exception(nint context, WasmtimeRef* exception);
}
