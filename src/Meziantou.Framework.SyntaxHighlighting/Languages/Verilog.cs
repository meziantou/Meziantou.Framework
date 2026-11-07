using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Verilog
{
    private const string KeywordList =
        "accept_on alias always always_comb always_ff always_latch and assert assign assume automatic before begin " +
        "bind bins binsof bit break buf|0 bufif0 bufif1 byte case casex casez cell chandle checker class clocking cmos " +
        "config const constraint context continue cover covergroup coverpoint cross deassign default defparam design " +
        "disable dist do edge else end endcase endchecker endclass endclocking endconfig endfunction endgenerate " +
        "endgroup endinterface endmodule endpackage endprimitive endprogram endproperty endspecify endsequence " +
        "endtable endtask enum event eventually expect export extends extern final first_match for force foreach " +
        "forever fork forkjoin function generate|5 genvar global highz0 highz1 if iff ifnone ignore_bins illegal_bins " +
        "implements implies import incdir include initial inout input inside instance int integer interconnect " +
        "interface intersect join join_any join_none large let liblist library local localparam logic longint " +
        "macromodule matches medium modport module nand negedge nettype new nexttime nmos nor noshowcancelled not " +
        "notif0 notif1 or output package packed parameter pmos posedge primitive priority program property protected " +
        "pull0 pull1 pulldown pullup pulsestyle_ondetect pulsestyle_onevent pure rand randc randcase randsequence " +
        "rcmos real realtime ref reg reject_on release repeat restrict return rnmos rpmos rtran rtranif0 rtranif1 " +
        "s_always s_eventually s_nexttime s_until s_until_with scalared sequence shortint shortreal showcancelled " +
        "signed small soft solve specify specparam static string strong strong0 strong1 struct super supply0 supply1 " +
        "sync_accept_on sync_reject_on table tagged task this throughout time timeprecision timeunit tran tranif0 " +
        "tranif1 tri tri0 tri1 triand trior trireg type typedef union unique unique0 unsigned until until_with untyped " +
        "use uwire var vectored virtual void wait wait_order wand weak weak0 weak1 while wildcard wire with within wor " +
        "xnor xor";

    private const string BuiltInList =
        "$finish $stop $exit $fatal $error $warning $info $realtime $time $printtimescale $bitstoreal $bitstoshortreal " +
        "$itor $signed $cast $bits $stime $timeformat $realtobits $shortrealtobits $rtoi $unsigned $asserton " +
        "$assertkill $assertpasson $assertfailon $assertnonvacuouson $assertoff $assertcontrol $assertpassoff " +
        "$assertfailoff $assertvacuousoff $isunbounded $sampled $fell $changed $past_gclk $fell_gclk $changed_gclk " +
        "$rising_gclk $steady_gclk $coverage_control $coverage_get $coverage_save $set_coverage_db_name $rose $stable " +
        "$past $rose_gclk $stable_gclk $future_gclk $falling_gclk $changing_gclk $display $coverage_get_max " +
        "$coverage_merge $get_coverage $load_coverage_db $typename $unpacked_dimensions $left $low $increment $clog2 " +
        "$ln $log10 $exp $sqrt $pow $floor $ceil $sin $cos $tan $countbits $onehot $isunknown $fatal $warning " +
        "$dimensions $right $high $size $asin $acos $atan $atan2 $hypot $sinh $cosh $tanh $asinh $acosh $atanh " +
        "$countones $onehot0 $error $info $random $dist_chi_square $dist_erlang $dist_exponential $dist_normal " +
        "$dist_poisson $dist_t $dist_uniform $q_initialize $q_remove $q_exam $async$and$array $async$nand$array " +
        "$async$or$array $async$nor$array $sync$and$array $sync$nand$array $sync$or$array $sync$nor$array $q_add " +
        "$q_full $psprintf $async$and$plane $async$nand$plane $async$or$plane $async$nor$plane $sync$and$plane " +
        "$sync$nand$plane $sync$or$plane $sync$nor$plane $system $display $displayb $displayh $displayo $strobe " +
        "$strobeb $strobeh $strobeo $write $readmemb $readmemh $writememh $value$plusargs $dumpvars $dumpon $dumplimit " +
        "$dumpports $dumpportson $dumpportslimit $writeb $writeh $writeo $monitor $monitorb $monitorh $monitoro " +
        "$writememb $dumpfile $dumpoff $dumpall $dumpflush $dumpportsoff $dumpportsall $dumpportsflush $fclose " +
        "$fdisplay $fdisplayb $fdisplayh $fdisplayo $fstrobe $fstrobeb $fstrobeh $fstrobeo $swrite $swriteb $swriteh " +
        "$swriteo $fscanf $fread $fseek $fflush $feof $fopen $fwrite $fwriteb $fwriteh $fwriteo $fmonitor $fmonitorb " +
        "$fmonitorh $fmonitoro $sformat $sformatf $fgetc $ungetc $fgets $sscanf $rewind $ftell $ferror";

    private const string DirectiveList =
        "begin_keywords celldefine default_nettype default_decay_time default_trireg_strength define delay_mode_distributed " +
        "delay_mode_path delay_mode_unit delay_mode_zero else elsif end_keywords endcelldefine endif ifdef ifndef include line " +
        "nounconnected_drive pragma resetall timescale unconnected_drive undef undefineall";

    // A parameter list is only recognized when its parentheses are nested at most this deep. From a `#(` that is not
    // closed on its line, the list is looked for up to the end of the line; bounding the depth bounds the number of such
    // `#(` whose search covers a given character (each of them is still open there), which keeps a long line of them
    // linear instead of quadratic.
    private const int MaxNestedParentheses = 5;

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    // Text of a line in which the parentheses are balanced and nested at most `depth` deep. The groups are atomic: the
    // text can only be split one way, so backtracking into them could not find another match.
    private static string NestedParenthesesRe(int depth) => depth is 0
        ? @"(?>[^()\n]*)"
        : @"(?>(?:[^()\n]+|\(" + NestedParenthesesRe(depth - 1) + @"\))*)";

    private static Mode CreateMode() => new()
    {
        KeywordPattern = @"\$?[\w]+(\$[\w]+)*",
        Keywords = Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["keyword"] = KeywordList,
            ["literal"] = "null",
            ["built_in"] = BuiltInList,
        }),
        Contains =
        [
            CommonModes.CBlockCommentMode,
            CommonModes.CLineCommentMode,
            CommonModes.QuoteStringMode,
            new Mode
            {
                Scope = "number",
                Variants =
                [
                    new Mode { Begin = @"\b((\d+'([bhodBHOD]))[0-9xzXZa-fA-F_]+)" },
                    new Mode { Begin = @"\B(('([bhodBHOD]))[0-9xzXZa-fA-F_]+)" },
                    new Mode { Begin = @"\b[0-9][0-9_]*" },
                ],
            },

            // Parameters of an instance.
            new Mode
            {
                Scope = "variable",
                Variants =
                [
                    // Deviation from highlight.js, whose parameter list ends at the last `)` of the line: in
                    // `adder #(8) u1 (.a(a));`, the instance name and its ports were part of the parameters. The list
                    // ends at its matching parenthesis.
                    new Mode { Begin = @"#\((?!parameter)" + NestedParenthesesRe(MaxNestedParentheses) + @"\)" },
                    new Mode { Begin = @"\.\w+" },
                ],
            },
            new Mode { Scope = "variable.constant", Begin = "`(?:__FILE__|__LINE__)" },
            new Mode
            {
                Scope = "meta",
                Begin = "`(?:" + DirectiveList.Replace(' ', '|') + ")",
                End = @"$|//|/\*",
                ReturnEnd = true,
                Keywords = Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["keyword"] = DirectiveList,
                }),
            },
        ],
    };
}
