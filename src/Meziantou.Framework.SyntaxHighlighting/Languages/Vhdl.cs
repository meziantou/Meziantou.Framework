using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Vhdl
{
    private const string IntegerRe = @"\d(_|\d)*";
    private const string ExponentRe = "[eE][-+]?" + IntegerRe;
    private const string DecimalLiteralRe = IntegerRe + @"(\." + IntegerRe + ")?" + "(" + ExponentRe + ")?";
    private const string BasedIntegerRe = @"\w+";
    private const string BasedLiteralRe = IntegerRe + "#" + BasedIntegerRe + @"(\." + BasedIntegerRe + ")?" + "#" + "(" + ExponentRe + ")?";
    private const string NumberRe = @"\b(" + BasedLiteralRe + "|" + DecimalLiteralRe + ")";

    private const string KeywordList =
        "abs access after alias all and architecture array assert assume assume_guarantee attribute begin block body " +
        "buffer bus case component configuration constant context cover disconnect downto default else elsif end " +
        "entity exit fairness file for force function generate generic group guarded if impure in inertial inout is " +
        "label library linkage literal loop map mod nand new next nor not null of on open or others out package " +
        "parameter port postponed procedure process property protected pure range record register reject release rem " +
        "report restrict restrict_guarantee return rol ror select sequence severity shared signal sla sll sra srl " +
        "strong subtype then to transport type unaffected units until use variable view vmode vprop vunit wait when " +
        "while with xnor xor";

    private const string BuiltInList =
        "boolean bit character integer time delay_length natural positive string bit_vector file_open_kind " +
        "file_open_status std_logic std_logic_vector unsigned signed boolean_vector integer_vector std_ulogic " +
        "std_ulogic_vector unresolved_unsigned u_unsigned unresolved_signed u_signed real_vector time_vector";

    private const string LiteralList =
        "false true note warning error failure line text side width";

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode() => new()
    {
        CaseInsensitive = true,
        Keywords = Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["keyword"] = KeywordList,
            ["built_in"] = BuiltInList,
            ["literal"] = LiteralList,
        }),
        Illegal = @"\{",
        Contains =
        [
            // VHDL-2008 block comments.
            CommonModes.CBlockCommentMode,
            CommonModes.Comment("--", "$"),
            CommonModes.QuoteStringMode,
            new Mode { Scope = "number", Begin = NumberRe },
            new Mode { Scope = "string", Begin = "'(U|X|0|1|Z|W|L|H|-)'" },

            // An attribute (`clk'event`).
            new Mode { Scope = "symbol", Begin = "'[A-Za-z](_?[A-Za-z0-9])*" },
        ],
    };
}
