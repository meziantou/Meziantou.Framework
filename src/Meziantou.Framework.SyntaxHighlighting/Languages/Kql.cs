using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

/// <summary>
/// Kusto Query Language (KQL), used by Azure Data Explorer, Azure Monitor, Microsoft Sentinel and Microsoft Fabric.
/// </summary>
/// <remarks>
/// highlight.js has no KQL grammar, so this one is written from scratch. Scopes: tabular operators (<c>where</c>,
/// <c>summarize</c>, <c>mv-expand</c>, ...), clauses (<c>by</c>, <c>on</c>, <c>kind</c>, ...), join kinds and word string
/// operators (<c>has</c>, <c>!contains</c>, <c>in~</c>, ...) are keywords; scalar, aggregation and plugin functions are
/// built-ins when they are called; data types are types; <c>true</c>/<c>false</c> and chart types are literals; numbers,
/// timespans (<c>1d</c>, <c>30m</c>) and the content of <c>datetime(...)</c>/<c>timespan(...)</c> literals are numbers;
/// management commands (<c>.show</c>, <c>.create</c>, ...) are keywords; the name bound by <c>let</c> is a variable;
/// <c>$left</c>/<c>$right</c> are language variables; symbolic operators are operators.
/// See https://learn.microsoft.com/kusto/query/.
/// </remarks>
internal static class Kql
{
    // `mv-expand` and `hint.strategy` are single words; a trailing dash is a minus sign.
    private const string KeywordPattern = @"[A-Za-z_](?:[\w-]*\w)?(?:\.[A-Za-z_]\w*)*";

    private static readonly string[] TabularOperators =
    [
        "as", "consume", "count", "datatable", "distinct", "evaluate", "extend", "externaldata", "facet", "filter", "find", "fork",
        "getschema", "invoke", "join", "limit", "lookup", "make-series", "make-graph", "graph-match", "graph-merge", "graph-to-table",
        "graph-shortest-paths", "graph-mark-components", "macro-expand", "mv-apply", "mv-expand", "order", "parse", "parse-kv",
        "parse-where", "partition", "print", "project", "project-away", "project-keep", "project-rename", "project-reorder", "range",
        "reduce", "render", "sample", "sample-distinct", "scan", "search", "serialize", "sort", "summarize", "take", "top",
        "top-hitters", "top-nested", "union", "where",
    ];

    private static readonly string[] OtherKeywords =
    [
        "let", "set", "alias", "declare", "pattern", "query_parameters", "restrict", "access", "materialize", "view",
        "by", "on", "kind", "with", "of", "to", "from", "step", "and", "or", "asc", "desc", "nulls", "first", "last", "granny-asc",
        "granny-desc", "in", "between", "typeof", "withsource", "isfuzzy", "bagexpansion", "with_itemindex", "with_match_id",
        "hint.strategy", "hint.shufflekey", "hint.num_partitions", "hint.remote", "hint.concurrency", "hint.spread",
        "hint.materialized", "hint.pass_filters", "hint.pass_filters_column", "hint.distribution", "hint.progressive_top",
        "inner", "innerunique", "leftouter", "rightouter", "fullouter", "leftanti", "rightanti", "leftsemi", "rightsemi", "anti",
        "leftantisemi", "rightantisemi", "semi", "contains", "contains_cs", "startswith", "startswith_cs", "endswith", "endswith_cs",
        "has", "has_cs", "hasprefix", "hasprefix_cs", "hassuffix", "hassuffix_cs", "has_any", "has_all", "matches", "regex",
        "cluster", "database", "table", "external_table", "materialized_view", "entity_group", "stored_query_result", "local",
        "shards", "broadcast", "shuffle", "default", "function", "cross", "expand", "every", "output", "match",
    ];

    private static readonly string[] Types =
    [
        "bool", "boolean", "int", "long", "real", "double", "decimal", "string", "datetime", "date", "timespan", "time", "dynamic",
        "guid", "uniqueid",
    ];

    private static readonly string[] ChartTypes =
    [
        "anomalychart", "areachart", "barchart", "card", "columnchart", "ladderchart", "linechart", "piechart", "pivotchart",
        "scatterchart", "stackedareachart", "timechart", "timepivot", "treemap", "plotly",
    ];

    private static readonly string[] Functions =
    [
        // Aggregation functions.
        "arg_max", "arg_min", "avg", "avgif", "binary_all_and", "binary_all_or", "binary_all_xor", "buildschema", "count", "countif",
        "count_distinct", "count_distinctif", "dcount", "dcountif", "hll", "hll_if", "hll_merge", "make_bag", "make_bag_if",
        "make_list", "make_list_if", "make_list_with_nulls", "make_set", "make_set_if", "max", "maxif", "min", "minif", "percentile",
        "percentiles", "percentiles_array", "percentilesw", "percentilesw_array", "stdev", "stdevif", "stdevp", "sum", "sumif",
        "take_any", "take_anyif", "tdigest", "tdigest_merge", "variance", "varianceif", "variancep", "variancepif", "any", "anyif",
        "makelist", "makeset",

        // Scalar functions.
        "abs", "acos", "ago", "array_concat", "array_iff", "array_iif", "array_index_of", "array_length", "array_reverse",
        "array_rotate_left", "array_rotate_right", "array_shift_left", "array_shift_right", "array_slice", "array_sort_asc",
        "array_sort_desc", "array_split", "array_sum", "asin", "atan", "atan2", "bag_has_key", "bag_keys", "bag_merge", "bag_pack",
        "bag_pack_columns", "bag_remove_keys", "bag_set_key", "base64_decode_toarray", "base64_decode_toguid",
        "base64_decode_tostring", "base64_encode_fromguid", "base64_encode_tostring", "beta_cdf", "beta_inv", "beta_pdf", "bin",
        "bin_at", "binary_and", "binary_not", "binary_or", "binary_shift_left", "binary_shift_right", "binary_xor",
        "bitset_count_ones", "case", "ceiling", "coalesce", "column_ifexists", "convert_angle", "convert_energy", "convert_force",
        "convert_length", "convert_mass", "convert_speed", "convert_temperature", "convert_volume", "cos", "cot", "countof",
        "current_cluster_endpoint", "current_database", "current_principal", "current_principal_details",
        "current_principal_is_member_of", "cursor_after", "cursor_before_or_at", "cursor_current", "datetime_add", "datetime_diff",
        "datetime_local_to_utc", "datetime_part", "datetime_utc_to_local", "dayofmonth", "dayofweek", "dayofyear", "dcount_hll",
        "degrees", "endofday", "endofmonth", "endofweek", "endofyear", "erf", "erfc", "estimate_data_size", "exp", "exp10", "exp2",
        "extent_id", "extent_tags", "extract", "extract_all", "extract_json", "extractjson", "floor", "format_bytes",
        "format_datetime", "format_ipv4", "format_ipv4_mask", "format_timespan", "gamma", "geo_info_from_ip_address", "getmonth",
        "gettype", "getyear", "has_any_index", "has_any_ipv4", "has_any_ipv4_prefix", "has_ipv4", "has_ipv4_prefix", "hash",
        "hash_combine", "hash_many", "hash_md5", "hash_sha1", "hash_sha256", "hash_xxhash64", "hourofday", "iff", "iif", "indexof",
        "indexof_regex", "ingestion_time", "ipv4_compare", "ipv4_is_in_any_range", "ipv4_is_in_range", "ipv4_is_match",
        "ipv4_is_private", "ipv4_netmask_suffix", "ipv4_range_to_cidr_list", "ipv6_compare", "ipv6_is_in_any_range",
        "ipv6_is_in_range", "ipv6_is_match", "isascii", "isempty", "isfinite", "isinf", "isnan", "isnotempty", "isnotnull",
        "isnull", "isutf8", "jaccard_index", "log", "log10", "log2", "loggamma", "make_datetime", "make_string", "make_timespan",
        "max_of", "merge_tdigest", "min_of", "monthofyear", "new_guid", "next", "not", "now", "pack", "pack_all", "pack_array",
        "pack_dictionary", "parse_command_line", "parse_csv", "parse_ipv4", "parse_ipv4_mask", "parse_ipv6", "parse_ipv6_mask",
        "parse_json", "parse_path", "parse_url", "parse_urlquery", "parse_user_agent", "parse_version", "parse_xml", "parsejson",
        "percentile_array_tdigest", "percentile_tdigest", "percentrank_tdigest", "pi", "pow", "prev", "punycode_from_string",
        "punycode_to_string", "radians", "rand", "range", "rank_tdigest", "regex_quote", "repeat", "replace", "replace_regex",
        "replace_string", "replace_strings", "reverse", "round", "row_cumsum", "row_number", "row_rank_dense", "row_rank_min",
        "row_window_session", "series_abs", "series_acos", "series_add", "series_asin", "series_atan", "series_ceiling",
        "series_cos", "series_cosine_similarity", "series_decompose", "series_decompose_anomalies", "series_decompose_forecast",
        "series_divide", "series_dot_product", "series_equals", "series_exp", "series_fft", "series_fill_backward",
        "series_fill_const", "series_fill_forward", "series_fill_linear", "series_fir", "series_fit_2lines",
        "series_fit_2lines_dynamic", "series_fit_line", "series_fit_line_dynamic", "series_fit_poly", "series_floor",
        "series_greater", "series_greater_equals", "series_ifft", "series_iir", "series_less", "series_less_equals", "series_log",
        "series_magnitude", "series_multiply", "series_not_equals", "series_outliers", "series_pearson_correlation",
        "series_periods_detect", "series_periods_validate", "series_pow", "series_product", "series_seasonal", "series_sign",
        "series_sin", "series_stats", "series_stats_dynamic", "series_subtract", "series_sum", "series_tan", "set_difference",
        "set_has_element", "set_intersect", "set_union", "sign", "sin", "split", "sqrt", "startofday", "startofmonth", "startofweek",
        "startofyear", "strcat", "strcat_array", "strcat_delim", "strcmp", "string_size", "strlen", "strrep", "substring", "tan",
        "tobool", "toboolean", "todatetime", "todecimal", "todouble", "todynamic", "toguid", "tohex", "toint", "tolong", "tolower",
        "toobject", "toreal", "toscalar", "tostring", "totimespan", "toupper", "translate", "treepath", "trim", "trim_end",
        "trim_start", "unicode_codepoints_from_string", "unicode_codepoints_to_string", "unixtime_microseconds_todatetime",
        "unixtime_milliseconds_todatetime", "unixtime_nanoseconds_todatetime", "unixtime_seconds_todatetime", "url_decode",
        "url_encode", "url_encode_component", "week_of_year", "weekofyear", "welch_test", "zip", "table",
        "geo_angle", "geo_azimuth", "geo_distance_2points", "geo_distance_point_to_line", "geo_distance_point_to_polygon",
        "geo_from_wkt", "geo_geohash_to_central_point", "geo_geohash_to_polygon", "geo_point_in_circle", "geo_point_in_polygon",
        "geo_point_to_geohash", "geo_point_to_h3cell", "geo_point_to_s2cell", "geo_polygon_area", "geo_s2cell_to_central_point",

        // Plugins (`evaluate bag_unpack(Properties)`).
        "autocluster", "bag_unpack", "basket", "cosmosdb_sql_request", "diffpatterns", "diffpatterns_text", "http_request",
        "http_request_post", "infer_storage_schema", "ipv4_lookup", "ipv6_lookup", "mysql_request", "narrow", "pivot", "postgresql_request",
        "preview", "python", "r", "rolling_percentile", "rows_near", "schema_merge", "sequence_detect", "sliding_window_counts",
        "sql_request", "active_users_count", "activity_counts_metrics", "activity_engagement", "activity_metrics", "funnel_sequence",
        "funnel_sequence_completion", "new_activity_metrics", "session_count",
    ];

    private static readonly string IdentifierStart = CommonModes.RunStart(@"\w");

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var comment = CommonModes.Comment("//", "$");

        Mode[] strings =
        [
            // Multi-line string literal.
            new Mode { Scope = "string", Begin = "```", End = "```" },

            // Verbatim strings: `@"C:\temp"`, where a doubled quote is a quote.
            new Mode { Scope = "string", Begin = @"(?<!\w)[hH]?@""", End = "\"|$", Contains = [new Mode { Match = "\"\"" }] },
            new Mode { Scope = "string", Begin = @"(?<!\w)[hH]?@'", End = "'|$", Contains = [new Mode { Match = "''" }] },

            // Strings cannot span lines, so an unterminated one ends with its line. `h"..."` is an obfuscated string.
            new Mode { Scope = "string", Begin = @"(?<!\w)[hH]?""", End = "\"|$", Contains = [CommonModes.BackslashEscape] },
            new Mode { Scope = "string", Begin = @"(?<!\w)[hH]?'", End = "'|$", Contains = [CommonModes.BackslashEscape] },
        ];

        const string TimespanUnit = "(?:d|days?|h|hrs?|hours?|m|mins?|minutes?|s|secs?|seconds?|ms|millis?|milliseconds?|microseconds?|ticks?)";
        var number = new Mode
        {
            Scope = "number",
            Match = IdentifierStart + @"(?<![.$])(?:0[xX][0-9A-Fa-f]+|\d+(?:\.\d+)?(?:[eE][+-]?\d+)?" + TimespanUnit + "?)(?![\\w.])",
        };

        // `datetime(2024-01-01 10:00)`, `timespan(1.02:03:04)`: the literal is not a string.
        var dateTimeLiteral = new Mode
        {
            BeginParts = [IdentifierStart + "(?:datetime|date|timespan|time)", @"[ \t]*\("],
            BeginScope = new Dictionary<int, string> { [1] = "type" },
            End = @"\)",
            Contains = [new Mode { Scope = "number", Match = @"[^()\s]+(?:[ \t]+[^()\s]+)*" }],
        };

        // `!has`, `!contains_cs`, `!in~`, `!between`, `in~`.
        var negatedOperator = new Mode
        {
            Scope = "keyword",
            Match = @"(?<![\w!])(?:!(?:contains|startswith|endswith|has|hasprefix|hassuffix)(?:_cs)?|!?in~?|!between)(?![\w~])",
        };

        var function = new Mode
        {
            Scope = "built_in",
            Match = IdentifierStart + @"(?<![.$])(?:" + string.Join('|', Functions.Distinct(StringComparer.Ordinal).OrderByDescending(name => name.Length)) + @")(?=[ \t]*\()",
        };

        // `let name = ...`.
        var letName = new Mode { Scope = "variable", Match = @"(?<=(?<![\w-])let[ \t]+)[A-Za-z_]\w*" };

        // `.show tables`, `.create-or-alter function`, `.set-or-append`: only at the start of a line.
        var managementCommand = new Mode { Scope = "keyword", Match = @"(?<=^[ \t]*)\.[A-Za-z][\w-]*" };

        var joinSide = new Mode { Scope = "variable.language", Match = @"\$(?:left|right)(?!\w)" };

        // A dash is also part of identifiers (`mv-expand`), so only a dash between spaces is an operator.
        var symbolOperator = new Mode { Scope = "operator", Match = @"<\||=~|!~|==|!=|<>|<=|>=|\.\.|[<>|+*/%]|(?<=\s)-(?=\s)" };

        return new Mode
        {
            Keywords = Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["keyword"] = [.. TabularOperators.Concat(OtherKeywords).Distinct(StringComparer.Ordinal)],
                ["type"] = Types,
                ["literal"] = ["true", "false", "TRUE", "FALSE", "True", "False", .. ChartTypes],
            }),
            KeywordPattern = KeywordPattern,
            Contains =
            [
                comment,
                .. strings,
                managementCommand,
                dateTimeLiteral,
                function,
                letName,
                joinSide,
                negatedOperator,
                number,
                symbolOperator,
            ],
        };
    }
}
