using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Fortran
{
    // `[de][+-]?\d+` (exponent) and `(_[a-z_\d]+)?` (kind suffix: 1.0_dp), shared by every number form.
    private const string NumberSuffixRe = @"([de][+-]?\d+)?(_[a-z_\d]+)?";

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var parameters = new Mode
        {
            Scope = "params",
            Begin = @"\(",
            End = @"\)",
        };

        // Deviation from highlight.js, whose strings are its generic ones: a backslash escapes the next character, so a
        // string that ends with a backslash ('C:\temp\') never closes and swallows the rest of the document. Fortran has
        // no escape sequences: a backslash is a plain character and a quote is doubled ('It''s').
        var @string = new Mode
        {
            Scope = "string",
            Variants =
            [
                new Mode
                {
                    Begin = "'",
                    End = "'",
                    Illegal = @"\n",
                    Contains = [new Mode { Begin = "''" }],
                },
                new Mode
                {
                    Begin = "\"",
                    End = "\"",
                    Illegal = @"\n",
                    Contains = [new Mode { Begin = "\"\"" }],
                },
            ],
        };

        var comment = new Mode
        {
            Variants =
            [
                CommonModes.Comment("!", "$"),

                // Fixed-form (FORTRAN 77) comments: a C or * in the first column.
                CommonModes.Comment("^C[ ]", "$"),
                CommonModes.Comment("^C$", "$"),

                // Deviation from highlight.js, which only knows `C ` and a lone `C`: fixed-form code also uses `*` in the
                // first column and separator lines (C-----, C*****, C=====). None of these characters can follow a C that
                // starts a statement, except `=`, whose assignments (C = 1) are matched before the comments.
                CommonModes.Comment(@"^(?:\*|C[-*=+#~!\t])", "$"),
            ],
        };

        return new Mode
        {
            CaseInsensitive = true,
            KeywordPattern = @"\b[a-z][a-z0-9_]+\b|\.[a-z][a-z0-9_]+\.",
            Keywords = Engine.Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["keyword"] =
                    "kind do concurrent local shared while private call intrinsic where elsewhere type endtype endmodule "
                    + "endselect endinterface end enddo endif if forall endforall only contains default return stop then "
                    + "block endblock endassociate public subroutine|10 function program .and. .or. .not. .le. .eq. .ge. "
                    + ".gt. .lt. goto save else use module select case access blank direct exist file fmt form formatted "
                    + "iostat name named nextrec number opened rec recl sequential status unformatted unit continue format "
                    + "pause cycle exit c_null_char c_alert c_backspace c_form_feed flush wait decimal round iomsg "
                    + "synchronous nopass non_overridable pass protected volatile abstract extends import non_intrinsic "
                    + "value deferred generic final enumerator class associate bind enum c_int c_short c_long c_long_long "
                    + "c_signed_char c_size_t c_int8_t c_int16_t c_int32_t c_int64_t c_int_least8_t c_int_least16_t "
                    + "c_int_least32_t c_int_least64_t c_int_fast8_t c_int_fast16_t c_int_fast32_t c_int_fast64_t "
                    + "c_intmax_t c_intptr_t c_float c_double c_long_double c_float_complex c_double_complex "
                    + "c_long_double_complex c_bool c_char c_null_ptr c_null_funptr c_new_line c_carriage_return "
                    + "c_horizontal_tab c_vertical_tab iso_c_binding c_loc c_funloc c_associated c_f_pointer c_ptr c_funptr "
                    + "iso_fortran_env character_storage_size error_unit file_storage_size input_unit iostat_end iostat_eor "
                    + "numeric_storage_size output_unit c_f_procpointer "
                    // highlight.js also lists ieee_arithmetic, ieee_support_underflow_control, ieee_get_underflow_mode and
                    // ieee_set_underflow_mode here, but they are built-ins because they are listed there too (the last
                    // group wins).
                    + "newunit contiguous recursive pad position action "
                    + "delim readwrite eor advance nml interface procedure namelist include sequence elemental pure impure "
                    + "integer real character complex logical codimension dimension allocatable|10 parameter external "
                    + "implicit|10 none double precision assign intent optional pointer target in out common equivalence "
                    + "data "
                    // Deviation from highlight.js, which lists the other relational and logical operators but misses these.
                    + ".ne. .eqv. .neqv.",
                ["literal"] = ".false. .true.",
                ["built_in"] =
                    "alog alog10 amax0 amax1 amin0 amin1 amod cabs ccos cexp clog csin csqrt dabs dacos dasin datan "
                    + "datan2 dcos dcosh ddim dexp dint dlog dlog10 dmax1 dmin1 dmod dnint dsign dsin dsinh dsqrt dtan "
                    + "dtanh float iabs idim idint idnint ifix isign max0 max1 min0 min1 sngl algama cdabs cdcos cdexp "
                    + "cdlog cdsin cdsqrt cqabs cqcos cqexp cqlog cqsin cqsqrt dcmplx dconjg derf derfc dfloat dgamma dimag "
                    + "dlgama iqint qabs qacos qasin qatan qatan2 qcmplx qconjg qcos qcosh qdim qerf qerfc qexp qgamma "
                    + "qimag qlgama qlog qlog10 qmax1 qmin1 qmod qnint qsign qsin qsinh qsqrt qtan qtanh abs acos aimag "
                    + "aint anint asin atan atan2 char cmplx conjg cos cosh exp ichar index int log log10 max min nint sign "
                    + "sin sinh sqrt tan tanh print write dim lge lgt lle llt mod nullify allocate deallocate adjustl "
                    + "adjustr all allocated any associated bit_size btest ceiling count cshift date_and_time digits "
                    + "dot_product eoshift epsilon exponent floor fraction huge iand ibclr ibits ibset ieor ior ishft "
                    + "ishftc lbound len_trim matmul maxexponent maxloc maxval merge minexponent minloc minval modulo "
                    + "mvbits nearest pack present product radix random_number random_seed range repeat reshape rrspacing "
                    + "scale scan selected_int_kind selected_real_kind set_exponent shape size spacing spread sum "
                    + "system_clock tiny transpose trim ubound unpack verify achar iachar transfer dble entry dprod "
                    + "cpu_time command_argument_count get_command get_command_argument get_environment_variable "
                    + "is_iostat_end ieee_arithmetic ieee_support_underflow_control ieee_get_underflow_mode "
                    + "ieee_set_underflow_mode is_iostat_eor move_alloc new_line selected_char_kind same_type_as "
                    + "extends_type_of acosh asinh atanh bessel_j0 bessel_j1 bessel_jn bessel_y0 bessel_y1 bessel_yn erf "
                    + "erfc erfc_scaled gamma log_gamma hypot norm2 atomic_define atomic_ref execute_command_line leadz "
                    + "trailz storage_size merge_bits bge bgt ble blt dshiftl dshiftr findloc iall iany iparity image_index "
                    + "lcobound ucobound maskl maskr num_images parity popcnt poppar shifta shiftl shiftr this_image sync "
                    + "change team co_broadcast co_max co_min co_sum co_reduce",
            }),
            Illegal = @"/\*",
            Contains =
            [
                @string,

                // Deviation from highlight.js, whose function declaration has no end, so it ends right after the keyword:
                // the name and the parameters it contains are never highlighted (and its `[${=\n]` illegal pattern is
                // never tried). It now contains the name and the parameter list that follows it, and ends after them, so
                // what follows (`result(r)`, `bind(c, name="f")`, a comment) is highlighted as usual. It ends before
                // anything else than a name (`program = 1` is an assignment).
                new Mode
                {
                    Scope = "function",
                    BeginKeywords = ["subroutine", "function", "program"],
                    End = @"$|(?=[^\s\w])",
                    Contains =
                    [
                        new Mode
                        {
                            Scope = "title",
                            Begin = CommonModes.UnderscoreIdentRe,
                            Starts = new Mode
                            {
                                End = @"$|(?=[^\s(])",
                                EndsParent = true,
                                Contains = [new Mode(parameters) { EndsParent = true }],
                            },
                        },
                    ],
                },

                // Allow `C = value` for assignments so they aren't misdetected as Fortran 77 style comments.
                new Mode { Begin = @"^C\s*=(?!=)" },
                comment,
                new Mode
                {
                    Scope = "number",
                    Variants =
                    [
                        // Deviation from highlight.js, whose numbers take the dots of the dotted operators around them
                        // (`x.eq.1.and.y` has the number `.1`, and `1.and.` is `1.` followed by `and.`): a dot is only part
                        // of a number when it does not start an operator, nor end one (a word character precedes it).
                        new Mode { Begin = @"\b\d+\.(?![a-z]+\.)(\d*)" + NumberSuffixRe },
                        new Mode { Begin = @"\b\d+" + NumberSuffixRe },
                        new Mode { Begin = @"(?<!\w)\.\d+" + NumberSuffixRe },
                    ],
                },
            ],
        };
    }
}
