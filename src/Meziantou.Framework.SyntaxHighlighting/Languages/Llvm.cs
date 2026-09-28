using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Llvm
{
    private const string IdentRe = @"([-a-zA-Z$._][\w$.-]*)";

    private const string KeywordList =
        "begin end true false declare define global constant private linker_private internal available_externally linkonce " +
        "linkonce_odr weak weak_odr appending dllimport dllexport common default hidden protected extern_weak external " +
        "thread_local zeroinitializer undef null to tail target triple datalayout volatile nuw nsw nnan ninf nsz arcp fast " +
        "exact inbounds align addrspace section alias module asm sideeffect gc dbg linker_private_weak attributes " +
        "blockaddress initialexec localdynamic localexec prefix unnamed_addr ccc fastcc coldcc x86_stdcallcc x86_fastcallcc " +
        "arm_apcscc arm_aapcscc arm_aapcs_vfpcc ptx_device ptx_kernel intel_ocl_bicc msp430_intrcc spir_func spir_kernel " +
        "x86_64_sysvcc x86_64_win64cc x86_thiscallcc cc c signext zeroext inreg sret nounwind noreturn noalias nocapture " +
        "byval nest readnone readonly inlinehint noinline alwaysinline optsize ssp sspreq noredzone noimplicitfloat naked " +
        "builtin cold nobuiltin noduplicate nonlazybind optnone returns_twice sanitize_address sanitize_memory " +
        "sanitize_thread sspstrong uwtable returned type opaque eq ne slt sgt sle sge ult ugt ule uge oeq one olt ogt ole " +
        "oge ord uno ueq une x acq_rel acquire alignstack atomic catch cleanup filter inteldialect max min monotonic nand " +
        "personality release seq_cst singlethread umax umin unordered xchg add fadd sub fsub mul fmul udiv sdiv fdiv urem " +
        "srem frem shl lshr ashr and or xor icmp fcmp phi call trunc zext sext fptrunc fpext uitofp sitofp fptoui fptosi " +
        "inttoptr ptrtoint bitcast addrspacecast select va_arg ret br switch invoke unwind unreachable indirectbr " +
        "landingpad resume malloc alloca free load store getelementptr extractelement insertelement shufflevector " +
        "getresult extractvalue insertvalue atomicrmw cmpxchg fence argmemonly";

    // `opaque` is also in the keyword list; like upstream, the later group wins.
    private const string TypeList = "void half bfloat float double fp128 x86_fp80 ppc_fp128 x86_amx x86_mmx ptr label token metadata opaque";

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode() => new()
    {
        Keywords = Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["keyword"] = KeywordList,
            ["type"] = TypeList,
        }),
        Contains =
        [
            new Mode { Scope = "type", Begin = @"\bi\d+(?=\s|\b)" },

            // An "empty comment" (a `;` that ends its line) is more likely a statement terminator of another language.
            new Mode { Scope = "comment", Begin = @";\s*$" },
            CommonModes.Comment(";", "$"),
            new Mode
            {
                Scope = "string",
                Begin = "\"",
                End = "\"",

                // Deviation from highlight.js, whose escape is a backslash followed by two decimal digits: escapes are
                // hexadecimal, so `\0A` or `\5C` were not highlighted.
                Contains = [new Mode { Scope = "char.escape", Begin = @"\\[0-9A-Fa-f]{2}" }],
            },
            new Mode
            {
                Scope = "title",
                Variants =
                [
                    new Mode { Begin = "@" + IdentRe },
                    new Mode { Begin = @"@\d+" },
                    new Mode { Begin = "!" + IdentRe },
                    new Mode { Begin = @"!\d+" + IdentRe },
                    new Mode { Begin = @"!\d+" },
                ],
            },
            new Mode { Scope = "punctuation", Begin = "," },
            new Mode { Scope = "operator", Begin = "=" },
            new Mode
            {
                Scope = "variable",
                Variants =
                [
                    new Mode { Begin = "%" + IdentRe },
                    new Mode { Begin = @"%\d+" },
                    new Mode { Begin = @"#\d+" },
                ],
            },

            // Deviation from highlight.js, whose label is lowercase letters only: a label can contain digits, dots and
            // the other identifier characters, or be a number (`if.then:`, `bb1:`, `42:`), as clang emits them.
            new Mode { Scope = "symbol", Begin = CommonModes.IndentedLineStartRe + @"(?:[-a-zA-Z$._][\w$.-]*|\d+):" },

            // Deviation from highlight.js: a number cannot start in the middle of an identifier, so the digits of
            // `x86_fp80`, `ppc_fp128` or `DW_LANG_C99` do not hide the keyword or split the identifier.
            new Mode
            {
                Scope = "number",
                Variants =
                [
                    new Mode { Begin = @"(?<![\w$.])[su]?0[xX][KMLHR]?[a-fA-F0-9]+" },
                    new Mode { Begin = @"(?<![\w$.])[-+]?\d+(?:[.]\d+)?(?:[eE][-+]?\d+(?:[.]\d+)?)?" },
                ],
            },
        ],
    };
}
