namespace Meziantou.Framework.WasmEngine.Text;

/// <summary>The immediates of an instruction of the text format.</summary>
internal enum Immediate : byte
{
    None,
    Label,
    BrTable,
    Function,
    CallIndirect,
    Type,
    Local,
    Global,
    Table,
    TableCopy,
    TableInit,
    MemArg,
    Memory,
    MemoryCopy,
    MemoryInit,
    Data,
    Element,
    I32,
    I64,
    F32,
    F64,
    V128,
    Shuffle,
    Lane,
    MemArgLane,
    Select,
    HeapType,
    RefTest,
    BrOnCast,
    StructField,
    TwoTypes,
    TypeAndCount,
    TypeAndData,
    TypeAndElement,
    Tag,
    Fence,
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly record struct InstructionInfo(byte Prefix, uint Code, Immediate Immediate, int Alignment);

/// <summary>The plain instructions of the text format (the block instructions are parsed by <see cref="WatParser"/>).</summary>
internal static class WatInstructions
{
    private static readonly Dictionary<string, InstructionInfo> Instructions = Create();

    public static bool TryGet(string name, out InstructionInfo info) => Instructions.TryGetValue(name, out info);

    private static Dictionary<string, InstructionInfo> Create()
    {
        var result = new Dictionary<string, InstructionInfo>(StringComparer.Ordinal);
        void Add(string name, byte prefix, uint code, Immediate immediate = Immediate.None, int alignment = 0) => result.Add(name, new InstructionInfo(prefix, code, immediate, alignment));

        Add("unreachable", 0, 0x00);
        Add("nop", 0, 0x01);
        Add("throw", 0, 0x08, Immediate.Tag);
        Add("rethrow", 0, 0x09, Immediate.Label);
        Add("throw_ref", 0, 0x0A);
        Add("br", 0, 0x0C, Immediate.Label);
        Add("br_if", 0, 0x0D, Immediate.Label);
        Add("br_table", 0, 0x0E, Immediate.BrTable);
        Add("return", 0, 0x0F);
        Add("call", 0, 0x10, Immediate.Function);
        Add("call_indirect", 0, 0x11, Immediate.CallIndirect);
        Add("return_call", 0, 0x12, Immediate.Function);
        Add("return_call_indirect", 0, 0x13, Immediate.CallIndirect);
        Add("call_ref", 0, 0x14, Immediate.Type);
        Add("return_call_ref", 0, 0x15, Immediate.Type);
        Add("drop", 0, 0x1A);
        Add("select", 0, 0x1B, Immediate.Select);
        Add("local.get", 0, 0x20, Immediate.Local);
        Add("local.set", 0, 0x21, Immediate.Local);
        Add("local.tee", 0, 0x22, Immediate.Local);
        Add("global.get", 0, 0x23, Immediate.Global);
        Add("global.set", 0, 0x24, Immediate.Global);
        Add("table.get", 0, 0x25, Immediate.Table);
        Add("table.set", 0, 0x26, Immediate.Table);

        string[] loads = ["i32.load", "i64.load", "f32.load", "f64.load", "i32.load8_s", "i32.load8_u", "i32.load16_s", "i32.load16_u", "i64.load8_s", "i64.load8_u", "i64.load16_s", "i64.load16_u", "i64.load32_s", "i64.load32_u", "i32.store", "i64.store", "f32.store", "f64.store", "i32.store8", "i32.store16", "i64.store8", "i64.store16", "i64.store32"];
        int[] alignments = [2, 3, 2, 3, 0, 0, 1, 1, 0, 0, 1, 1, 2, 2, 2, 3, 2, 3, 0, 1, 0, 1, 2];
        for (var i = 0; i < loads.Length; i++)
        {
            Add(loads[i], 0, (uint)(0x28 + i), Immediate.MemArg, alignments[i]);
        }

        Add("memory.size", 0, 0x3F, Immediate.Memory);
        Add("memory.grow", 0, 0x40, Immediate.Memory);
        Add("i32.const", 0, 0x41, Immediate.I32);
        Add("i64.const", 0, 0x42, Immediate.I64);
        Add("f32.const", 0, 0x43, Immediate.F32);
        Add("f64.const", 0, 0x44, Immediate.F64);

        string[] numeric =
        [
            "i32.eqz", "i32.eq", "i32.ne", "i32.lt_s", "i32.lt_u", "i32.gt_s", "i32.gt_u", "i32.le_s", "i32.le_u", "i32.ge_s", "i32.ge_u",
            "i64.eqz", "i64.eq", "i64.ne", "i64.lt_s", "i64.lt_u", "i64.gt_s", "i64.gt_u", "i64.le_s", "i64.le_u", "i64.ge_s", "i64.ge_u",
            "f32.eq", "f32.ne", "f32.lt", "f32.gt", "f32.le", "f32.ge",
            "f64.eq", "f64.ne", "f64.lt", "f64.gt", "f64.le", "f64.ge",
            "i32.clz", "i32.ctz", "i32.popcnt", "i32.add", "i32.sub", "i32.mul", "i32.div_s", "i32.div_u", "i32.rem_s", "i32.rem_u", "i32.and", "i32.or", "i32.xor", "i32.shl", "i32.shr_s", "i32.shr_u", "i32.rotl", "i32.rotr",
            "i64.clz", "i64.ctz", "i64.popcnt", "i64.add", "i64.sub", "i64.mul", "i64.div_s", "i64.div_u", "i64.rem_s", "i64.rem_u", "i64.and", "i64.or", "i64.xor", "i64.shl", "i64.shr_s", "i64.shr_u", "i64.rotl", "i64.rotr",
            "f32.abs", "f32.neg", "f32.ceil", "f32.floor", "f32.trunc", "f32.nearest", "f32.sqrt", "f32.add", "f32.sub", "f32.mul", "f32.div", "f32.min", "f32.max", "f32.copysign",
            "f64.abs", "f64.neg", "f64.ceil", "f64.floor", "f64.trunc", "f64.nearest", "f64.sqrt", "f64.add", "f64.sub", "f64.mul", "f64.div", "f64.min", "f64.max", "f64.copysign",
            "i32.wrap_i64", "i32.trunc_f32_s", "i32.trunc_f32_u", "i32.trunc_f64_s", "i32.trunc_f64_u", "i64.extend_i32_s", "i64.extend_i32_u", "i64.trunc_f32_s", "i64.trunc_f32_u", "i64.trunc_f64_s", "i64.trunc_f64_u",
            "f32.convert_i32_s", "f32.convert_i32_u", "f32.convert_i64_s", "f32.convert_i64_u", "f32.demote_f64", "f64.convert_i32_s", "f64.convert_i32_u", "f64.convert_i64_s", "f64.convert_i64_u", "f64.promote_f32",
            "i32.reinterpret_f32", "i64.reinterpret_f64", "f32.reinterpret_i32", "f64.reinterpret_i64", "i32.extend8_s", "i32.extend16_s", "i64.extend8_s", "i64.extend16_s", "i64.extend32_s",
        ];
        for (var i = 0; i < numeric.Length; i++)
        {
            Add(numeric[i], 0, (uint)(0x45 + i));
        }

        Add("ref.null", 0, 0xD0, Immediate.HeapType);
        Add("ref.is_null", 0, 0xD1);
        Add("ref.func", 0, 0xD2, Immediate.Function);
        Add("ref.eq", 0, 0xD3);
        Add("ref.as_non_null", 0, 0xD4);
        Add("br_on_null", 0, 0xD5, Immediate.Label);
        Add("br_on_non_null", 0, 0xD6, Immediate.Label);

        // 0xFB
        Add("struct.new", 0xFB, 0, Immediate.Type);
        Add("struct.new_default", 0xFB, 1, Immediate.Type);
        Add("struct.get", 0xFB, 2, Immediate.StructField);
        Add("struct.get_s", 0xFB, 3, Immediate.StructField);
        Add("struct.get_u", 0xFB, 4, Immediate.StructField);
        Add("struct.set", 0xFB, 5, Immediate.StructField);
        Add("array.new", 0xFB, 6, Immediate.Type);
        Add("array.new_default", 0xFB, 7, Immediate.Type);
        Add("array.new_fixed", 0xFB, 8, Immediate.TypeAndCount);
        Add("array.new_data", 0xFB, 9, Immediate.TypeAndData);
        Add("array.new_elem", 0xFB, 10, Immediate.TypeAndElement);
        Add("array.get", 0xFB, 11, Immediate.Type);
        Add("array.get_s", 0xFB, 12, Immediate.Type);
        Add("array.get_u", 0xFB, 13, Immediate.Type);
        Add("array.set", 0xFB, 14, Immediate.Type);
        Add("array.len", 0xFB, 15);
        Add("array.fill", 0xFB, 16, Immediate.Type);
        Add("array.copy", 0xFB, 17, Immediate.TwoTypes);
        Add("array.init_data", 0xFB, 18, Immediate.TypeAndData);
        Add("array.init_elem", 0xFB, 19, Immediate.TypeAndElement);
        Add("ref.test", 0xFB, 20, Immediate.RefTest);
        Add("ref.cast", 0xFB, 22, Immediate.RefTest);
        Add("br_on_cast", 0xFB, 24, Immediate.BrOnCast);
        Add("br_on_cast_fail", 0xFB, 25, Immediate.BrOnCast);
        Add("any.convert_extern", 0xFB, 26);
        Add("extern.convert_any", 0xFB, 27);
        Add("ref.i31", 0xFB, 28);
        Add("i31.get_s", 0xFB, 29);
        Add("i31.get_u", 0xFB, 30);

        // 0xFC
        string[] truncations = ["i32.trunc_sat_f32_s", "i32.trunc_sat_f32_u", "i32.trunc_sat_f64_s", "i32.trunc_sat_f64_u", "i64.trunc_sat_f32_s", "i64.trunc_sat_f32_u", "i64.trunc_sat_f64_s", "i64.trunc_sat_f64_u"];
        for (var i = 0; i < truncations.Length; i++)
        {
            Add(truncations[i], 0xFC, (uint)i);
        }

        Add("memory.init", 0xFC, 8, Immediate.MemoryInit);
        Add("data.drop", 0xFC, 9, Immediate.Data);
        Add("memory.copy", 0xFC, 10, Immediate.MemoryCopy);
        Add("memory.fill", 0xFC, 11, Immediate.Memory);
        Add("table.init", 0xFC, 12, Immediate.TableInit);
        Add("elem.drop", 0xFC, 13, Immediate.Element);
        Add("table.copy", 0xFC, 14, Immediate.TableCopy);
        Add("table.grow", 0xFC, 15, Immediate.Table);
        Add("table.size", 0xFC, 16, Immediate.Table);
        Add("table.fill", 0xFC, 17, Immediate.Table);
        Add("i64.add128", 0xFC, 19);
        Add("i64.sub128", 0xFC, 20);
        Add("i64.mul_wide_s", 0xFC, 21);
        Add("i64.mul_wide_u", 0xFC, 22);

        AddSimd(Add);
        AddAtomics(Add);
        return result;
    }

    private static void AddSimd(Action<string, byte, uint, Immediate, int> add)
    {
        string[] loads = ["v128.load", "v128.load8x8_s", "v128.load8x8_u", "v128.load16x4_s", "v128.load16x4_u", "v128.load32x2_s", "v128.load32x2_u", "v128.load8_splat", "v128.load16_splat", "v128.load32_splat", "v128.load64_splat", "v128.store"];
        int[] alignments = [4, 3, 3, 3, 3, 3, 3, 0, 1, 2, 3, 4];
        for (var i = 0; i < loads.Length; i++)
        {
            add(loads[i], 0xFD, (uint)i, Immediate.MemArg, alignments[i]);
        }

        add("v128.const", 0xFD, 0x0C, Immediate.V128, 0);
        add("i8x16.shuffle", 0xFD, 0x0D, Immediate.Shuffle, 0);
        add("v128.load32_zero", 0xFD, 0x5C, Immediate.MemArg, 2);
        add("v128.load64_zero", 0xFD, 0x5D, Immediate.MemArg, 3);
        string[] laneAccesses = ["v128.load8_lane", "v128.load16_lane", "v128.load32_lane", "v128.load64_lane", "v128.store8_lane", "v128.store16_lane", "v128.store32_lane", "v128.store64_lane"];
        for (var i = 0; i < laneAccesses.Length; i++)
        {
            add(laneAccesses[i], 0xFD, (uint)(0x54 + i), Immediate.MemArgLane, i % 4);
        }

        string[] lanes = ["i8x16.extract_lane_s", "i8x16.extract_lane_u", "i8x16.replace_lane", "i16x8.extract_lane_s", "i16x8.extract_lane_u", "i16x8.replace_lane", "i32x4.extract_lane", "i32x4.replace_lane", "i64x2.extract_lane", "i64x2.replace_lane", "f32x4.extract_lane", "f32x4.replace_lane", "f64x2.extract_lane", "f64x2.replace_lane"];
        for (var i = 0; i < lanes.Length; i++)
        {
            add(lanes[i], 0xFD, (uint)(0x15 + i), Immediate.Lane, 0);
        }

        (uint Code, string Name)[] simple =
        [
            (0x0E, "i8x16.swizzle"), (0x0F, "i8x16.splat"), (0x10, "i16x8.splat"), (0x11, "i32x4.splat"), (0x12, "i64x2.splat"), (0x13, "f32x4.splat"), (0x14, "f64x2.splat"),
            (0x4D, "v128.not"), (0x4E, "v128.and"), (0x4F, "v128.andnot"), (0x50, "v128.or"), (0x51, "v128.xor"), (0x52, "v128.bitselect"), (0x53, "v128.any_true"),
            (0x5E, "f32x4.demote_f64x2_zero"), (0x5F, "f64x2.promote_low_f32x4"),
            (0x60, "i8x16.abs"), (0x61, "i8x16.neg"), (0x62, "i8x16.popcnt"), (0x63, "i8x16.all_true"), (0x64, "i8x16.bitmask"), (0x65, "i8x16.narrow_i16x8_s"), (0x66, "i8x16.narrow_i16x8_u"),
            (0x67, "f32x4.ceil"), (0x68, "f32x4.floor"), (0x69, "f32x4.trunc"), (0x6A, "f32x4.nearest"),
            (0x6B, "i8x16.shl"), (0x6C, "i8x16.shr_s"), (0x6D, "i8x16.shr_u"), (0x6E, "i8x16.add"), (0x6F, "i8x16.add_sat_s"), (0x70, "i8x16.add_sat_u"), (0x71, "i8x16.sub"), (0x72, "i8x16.sub_sat_s"), (0x73, "i8x16.sub_sat_u"),
            (0x74, "f64x2.ceil"), (0x75, "f64x2.floor"), (0x76, "i8x16.min_s"), (0x77, "i8x16.min_u"), (0x78, "i8x16.max_s"), (0x79, "i8x16.max_u"), (0x7A, "f64x2.trunc"), (0x7B, "i8x16.avgr_u"),
            (0x7C, "i16x8.extadd_pairwise_i8x16_s"), (0x7D, "i16x8.extadd_pairwise_i8x16_u"), (0x7E, "i32x4.extadd_pairwise_i16x8_s"), (0x7F, "i32x4.extadd_pairwise_i16x8_u"),
            (0x80, "i16x8.abs"), (0x81, "i16x8.neg"), (0x82, "i16x8.q15mulr_sat_s"), (0x83, "i16x8.all_true"), (0x84, "i16x8.bitmask"), (0x85, "i16x8.narrow_i32x4_s"), (0x86, "i16x8.narrow_i32x4_u"),
            (0x87, "i16x8.extend_low_i8x16_s"), (0x88, "i16x8.extend_high_i8x16_s"), (0x89, "i16x8.extend_low_i8x16_u"), (0x8A, "i16x8.extend_high_i8x16_u"),
            (0x8B, "i16x8.shl"), (0x8C, "i16x8.shr_s"), (0x8D, "i16x8.shr_u"), (0x8E, "i16x8.add"), (0x8F, "i16x8.add_sat_s"), (0x90, "i16x8.add_sat_u"), (0x91, "i16x8.sub"), (0x92, "i16x8.sub_sat_s"), (0x93, "i16x8.sub_sat_u"),
            (0x94, "f64x2.nearest"), (0x95, "i16x8.mul"), (0x96, "i16x8.min_s"), (0x97, "i16x8.min_u"), (0x98, "i16x8.max_s"), (0x99, "i16x8.max_u"), (0x9B, "i16x8.avgr_u"),
            (0x9C, "i16x8.extmul_low_i8x16_s"), (0x9D, "i16x8.extmul_high_i8x16_s"), (0x9E, "i16x8.extmul_low_i8x16_u"), (0x9F, "i16x8.extmul_high_i8x16_u"),
            (0xA0, "i32x4.abs"), (0xA1, "i32x4.neg"), (0xA3, "i32x4.all_true"), (0xA4, "i32x4.bitmask"),
            (0xA7, "i32x4.extend_low_i16x8_s"), (0xA8, "i32x4.extend_high_i16x8_s"), (0xA9, "i32x4.extend_low_i16x8_u"), (0xAA, "i32x4.extend_high_i16x8_u"),
            (0xAB, "i32x4.shl"), (0xAC, "i32x4.shr_s"), (0xAD, "i32x4.shr_u"), (0xAE, "i32x4.add"), (0xB1, "i32x4.sub"), (0xB5, "i32x4.mul"), (0xB6, "i32x4.min_s"), (0xB7, "i32x4.min_u"), (0xB8, "i32x4.max_s"), (0xB9, "i32x4.max_u"), (0xBA, "i32x4.dot_i16x8_s"),
            (0xBC, "i32x4.extmul_low_i16x8_s"), (0xBD, "i32x4.extmul_high_i16x8_s"), (0xBE, "i32x4.extmul_low_i16x8_u"), (0xBF, "i32x4.extmul_high_i16x8_u"),
            (0xC0, "i64x2.abs"), (0xC1, "i64x2.neg"), (0xC3, "i64x2.all_true"), (0xC4, "i64x2.bitmask"),
            (0xC7, "i64x2.extend_low_i32x4_s"), (0xC8, "i64x2.extend_high_i32x4_s"), (0xC9, "i64x2.extend_low_i32x4_u"), (0xCA, "i64x2.extend_high_i32x4_u"),
            (0xCB, "i64x2.shl"), (0xCC, "i64x2.shr_s"), (0xCD, "i64x2.shr_u"), (0xCE, "i64x2.add"), (0xD1, "i64x2.sub"), (0xD5, "i64x2.mul"),
            (0xD6, "i64x2.eq"), (0xD7, "i64x2.ne"), (0xD8, "i64x2.lt_s"), (0xD9, "i64x2.gt_s"), (0xDA, "i64x2.le_s"), (0xDB, "i64x2.ge_s"),
            (0xDC, "i64x2.extmul_low_i32x4_s"), (0xDD, "i64x2.extmul_high_i32x4_s"), (0xDE, "i64x2.extmul_low_i32x4_u"), (0xDF, "i64x2.extmul_high_i32x4_u"),
            (0xE0, "f32x4.abs"), (0xE1, "f32x4.neg"), (0xE3, "f32x4.sqrt"), (0xE4, "f32x4.add"), (0xE5, "f32x4.sub"), (0xE6, "f32x4.mul"), (0xE7, "f32x4.div"), (0xE8, "f32x4.min"), (0xE9, "f32x4.max"), (0xEA, "f32x4.pmin"), (0xEB, "f32x4.pmax"),
            (0xEC, "f64x2.abs"), (0xED, "f64x2.neg"), (0xEF, "f64x2.sqrt"), (0xF0, "f64x2.add"), (0xF1, "f64x2.sub"), (0xF2, "f64x2.mul"), (0xF3, "f64x2.div"), (0xF4, "f64x2.min"), (0xF5, "f64x2.max"), (0xF6, "f64x2.pmin"), (0xF7, "f64x2.pmax"),
            (0xF8, "i32x4.trunc_sat_f32x4_s"), (0xF9, "i32x4.trunc_sat_f32x4_u"), (0xFA, "f32x4.convert_i32x4_s"), (0xFB, "f32x4.convert_i32x4_u"),
            (0xFC, "i32x4.trunc_sat_f64x2_s_zero"), (0xFD, "i32x4.trunc_sat_f64x2_u_zero"), (0xFE, "f64x2.convert_low_i32x4_s"), (0xFF, "f64x2.convert_low_i32x4_u"),
            (0x100, "i8x16.relaxed_swizzle"), (0x101, "i32x4.relaxed_trunc_f32x4_s"), (0x102, "i32x4.relaxed_trunc_f32x4_u"), (0x103, "i32x4.relaxed_trunc_f64x2_s_zero"), (0x104, "i32x4.relaxed_trunc_f64x2_u_zero"),
            (0x105, "f32x4.relaxed_madd"), (0x106, "f32x4.relaxed_nmadd"), (0x107, "f64x2.relaxed_madd"), (0x108, "f64x2.relaxed_nmadd"),
            (0x109, "i8x16.relaxed_laneselect"), (0x10A, "i16x8.relaxed_laneselect"), (0x10B, "i32x4.relaxed_laneselect"), (0x10C, "i64x2.relaxed_laneselect"),
            (0x10D, "f32x4.relaxed_min"), (0x10E, "f32x4.relaxed_max"), (0x10F, "f64x2.relaxed_min"), (0x110, "f64x2.relaxed_max"),
            (0x111, "i16x8.relaxed_q15mulr_s"), (0x112, "i16x8.relaxed_dot_i8x16_i7x16_s"), (0x113, "i32x4.relaxed_dot_i8x16_i7x16_add_s"),
        ];
        foreach (var (code, name) in simple)
        {
            add(name, 0xFD, code, Immediate.None, 0);
        }

        // The comparisons: 0x23-0x4C
        string[] integerComparisons = ["eq", "ne", "lt_s", "lt_u", "gt_s", "gt_u", "le_s", "le_u", "ge_s", "ge_u"];
        string[] floatComparisons = ["eq", "ne", "lt", "gt", "le", "ge"];
        var comparison = 0x23u;
        foreach (var shape in new[] { "i8x16", "i16x8", "i32x4" })
        {
            foreach (var name in integerComparisons)
            {
                add(shape + "." + name, 0xFD, comparison++, Immediate.None, 0);
            }
        }

        foreach (var shape in new[] { "f32x4", "f64x2" })
        {
            foreach (var name in floatComparisons)
            {
                add(shape + "." + name, 0xFD, comparison++, Immediate.None, 0);
            }
        }
    }

    private static void AddAtomics(Action<string, byte, uint, Immediate, int> add)
    {
        add("memory.atomic.notify", 0xFE, 0x00, Immediate.MemArg, 2);
        add("memory.atomic.wait32", 0xFE, 0x01, Immediate.MemArg, 2);
        add("memory.atomic.wait64", 0xFE, 0x02, Immediate.MemArg, 3);
        add("atomic.fence", 0xFE, 0x03, Immediate.Fence, 0);
        string[] accesses = ["i32.atomic.{0}", "i64.atomic.{0}", "i32.atomic.{0}8{1}", "i32.atomic.{0}16{1}", "i64.atomic.{0}8{1}", "i64.atomic.{0}16{1}", "i64.atomic.{0}32{1}"];
        int[] alignments = [2, 3, 0, 1, 0, 1, 2];
        for (var i = 0; i < 7; i++)
        {
            add(string.Format(System.Globalization.CultureInfo.InvariantCulture, accesses[i], "load", "_u"), 0xFE, (uint)(0x10 + i), Immediate.MemArg, alignments[i]);
            add(string.Format(System.Globalization.CultureInfo.InvariantCulture, accesses[i], "store", ""), 0xFE, (uint)(0x17 + i), Immediate.MemArg, alignments[i]);
        }

        string[] operations = ["add", "sub", "and", "or", "xor", "xchg", "cmpxchg"];
        string[] rmw = ["i32.atomic.rmw.{0}", "i64.atomic.rmw.{0}", "i32.atomic.rmw8.{0}_u", "i32.atomic.rmw16.{0}_u", "i64.atomic.rmw8.{0}_u", "i64.atomic.rmw16.{0}_u", "i64.atomic.rmw32.{0}_u"];
        for (var operation = 0; operation < operations.Length; operation++)
        {
            for (var i = 0; i < 7; i++)
            {
                add(string.Format(System.Globalization.CultureInfo.InvariantCulture, rmw[i], operations[operation]), 0xFE, (uint)(0x1E + (operation * 7) + i), Immediate.MemArg, alignments[i]);
            }
        }
    }
}
