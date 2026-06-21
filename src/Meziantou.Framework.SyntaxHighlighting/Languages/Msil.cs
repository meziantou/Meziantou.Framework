using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Msil
{
    private const string DirectiveList =
        ".addon .algorithm .assembly .cctor .class .corflags .ctor .custom .data .emitbyte .entrypoint .event .export algorithm " +
        ".field .file .fire .get .hash .imagebase .language .line .locals .localized .maxstack .method .module .mresource " +
        ".namespace .other .override .pack .param .permissionset .property .publickey .publickeytoken .removeon .set .size " +
        ".subsystem .try .ver .vtable .vtentry .vtfixup .zeroinit";

    private const string KeywordList =
        "abstract ansi any auto autochar beforefieldinit blob byref bytearray callmostderived cdecl cil class default " +
        "explicit extends false famandassem family famorassem final float fromunmanaged handler hidebysig implements implicitcom " +
        "implicitresilient init initonly instance interface internalcall lasterr literal managed marshal native nested newslot " +
        "noappdomain noinlining nomangle nomdappdomain nomdappdomainseal noncasdemand noncasinheritance noncaslinkdemand nometadata " +
        "not_in_gc_heap notremotable notserialized nullopt optil overlapped pinvokeimpl preservesig private privatescope " +
        "public reqmin reqopt reqrefuse retval rtspecialname runtime sealed sequential serializable specialname static strict " +
        "synchronized syschar thiscall tls true unicode unmanaged unmanagedexp value valuetype vararg variant vector virtual winapi " +
        "abstract autochar beforefieldinit cil managed";

    private const string OpcodeList =
        "add add.ovf add.ovf.un and arglist beq beq.s bge bge.s bge.un bge.un.s bgt bgt.s bgt.un bgt.un.s ble ble.s ble.un " +
        "ble.un.s blt blt.s blt.un blt.un.s bne.un bne.un.s box br br.s break brfalse brfalse.s brinst brinst.s brnull brnull.s " +
        "brtrue brtrue.s brzero brzero.s call calli callvirt castclass ceq cgt cgt.un ckfinite clt clt.un constrained. conv.i " +
        "conv.i1 conv.i2 conv.i4 conv.i8 conv.ovf.i conv.ovf.i.un conv.ovf.i1 conv.ovf.i1.un conv.ovf.i2 conv.ovf.i2.un " +
        "conv.ovf.i4 conv.ovf.i4.un conv.ovf.i8 conv.ovf.i8.un conv.ovf.u conv.ovf.u.un conv.ovf.u1 conv.ovf.u1.un " +
        "conv.ovf.u2 conv.ovf.u2.un conv.ovf.u4 conv.ovf.u4.un conv.ovf.u8 conv.ovf.u8.un conv.r.un conv.r4 conv.r8 conv.u " +
        "conv.u1 conv.u2 conv.u4 conv.u8 cpblk cpobj div div.un dup endfault endfilter endfinally initblk initobj isinst jmp " +
        "ldarg ldarg.0 ldarg.1 ldarg.2 ldarg.3 ldarg.s ldarga ldarga.s ldc.i4 ldc.i4.0 ldc.i4.1 ldc.i4.2 ldc.i4.3 ldc.i4.4 " +
        "ldc.i4.5 ldc.i4.6 ldc.i4.7 ldc.i4.8 ldc.i4.M1 ldc.i4.m1 ldc.i4.s ldc.i8 ldc.r4 ldc.r8 ldelem ldelem.i ldelem.i1 " +
        "ldelem.i2 ldelem.i4 ldelem.i8 ldelem.r4 ldelem.r8 ldelem.ref ldelem.u1 ldelem.u2 ldelem.u4 ldelema ldfld ldflda " +
        "ldftn ldind.i ldind.i1 ldind.i2 ldind.i4 ldind.i8 ldind.r4 ldind.r8 ldind.ref ldind.u1 ldind.u2 ldind.u4 ldlen " +
        "ldloc ldloc.0 ldloc.1 ldloc.2 ldloc.3 ldloc.s ldloca ldloca.s ldnull ldobj ldsfld ldsflda ldstr ldtoken ldvirtftn " +
        "leave leave.s localloc mkrefany mul mul.ovf mul.ovf.un neg newarr newobj no. nop not or pop readonly. refanytype " +
        "refanyval rem rem.un ret rethrow shl shr shr.un sizeof starg starg.s stelem stelem.i stelem.i1 stelem.i2 stelem.i4 " +
        "stelem.i8 stelem.r4 stelem.r8 stelem.ref stfld stind.i stind.i1 stind.i2 stind.i4 stind.i8 stind.r4 stind.r8 " +
        "stind.ref stloc stloc.0 stloc.1 stloc.2 stloc.3 stloc.s stobj stsfld sub sub.ovf sub.ovf.un switch tail. throw " +
        "unaligned. unbox unbox.any volatile. xor";

    private const string BuiltInList =
        "bool char float32 float64 int int8 int16 int32 int64 native object string typedref uint uint8 uint16 uint32 uint64 " +
        "void";

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode() => new()
    {
        KeywordPattern = @"\.?[A-Za-z_][\w.]*",
        Keywords = Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["built_in"] = BuiltInList,
            ["keyword"] = KeywordList + " " + OpcodeList,
            ["meta"] = DirectiveList,
        }),
        Contains =
        [
            CommonModes.CLineCommentMode,
            CommonModes.QuoteStringMode,
            CommonModes.AposStringMode,
            new()
            {
                Scope = "number",
                Variants =
                [
                    new Mode { Begin = @"\b0x[0-9A-Fa-f]+\b" },
                    new Mode { Begin = @"\b[0-9A-Fa-f]{2}\b" },
                    new Mode { Begin = @"[-+]?\b\d+(?:\.\d+)?\b" },
                ],
            },
            new()
            {
                BeginParts = [@"^[ \t]*", @"[A-Za-z_.$@?][\w.$@?]*:"],
                BeginScope = new Dictionary<int, string> { [2] = "symbol" },
            },
        ],
    };
}
