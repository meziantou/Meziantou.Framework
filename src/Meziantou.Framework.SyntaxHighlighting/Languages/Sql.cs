using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Sql
{
    private static readonly string[] Literals = ["true", "false", "unknown"];

    private static readonly string[] MultiWordTypes =
    [
        "double precision","large object","with timezone","without timezone",
    ];

    private static readonly string[] Types =
    [
        "bigint","binary","blob","boolean","char","character","clob","date","dec","decfloat",
        "decimal","float","int","integer","interval","nchar","nclob","national","numeric",
        "real","row","smallint","time","timestamp","varchar","varying","varbinary",
    ];

    private static readonly string[] NonReservedWords =
    [
        "add","asc","collation","desc","final","first","last","view",
    ];

    private static readonly string[] ReservedWords =
    [
        "abs","acos","all","allocate","alter","and","any","are","array","array_agg",
        "array_max_cardinality","as","asensitive","asin","asymmetric","at","atan","atomic",
        "authorization","avg","begin","begin_frame","begin_partition","between","bigint","binary",
        "blob","boolean","both","by","call","called","cardinality","cascaded","case","cast","ceil",
        "ceiling","char","char_length","character","character_length","check","classifier","clob",
        "close","coalesce","collate","collect","column","commit","condition","connect","constraint",
        "contains","convert","copy","corr","corresponding","cos","cosh","count","covar_pop",
        "covar_samp","create","cross","cube","cume_dist","current","current_catalog","current_date",
        "current_default_transform_group","current_path","current_role","current_row","current_schema",
        "current_time","current_timestamp","current_path","current_role",
        "current_transform_group_for_type","current_user","cursor","cycle","date","day","deallocate",
        "dec","decimal","decfloat","declare","default","define","delete","dense_rank","deref",
        "describe","deterministic","disconnect","distinct","double","drop","dynamic","each","element",
        "else","empty","end","end_frame","end_partition","end-exec","equals","escape","every","except",
        "exec","execute","exists","exp","external","extract","false","fetch","filter","first_value",
        "float","floor","for","foreign","frame_row","free","from","full","function","fusion","get",
        "global","grant","group","grouping","groups","having","hold","hour","identity","in","indicator",
        "initial","inner","inout","insensitive","insert","int","integer","intersect","intersection",
        "interval","into","is","join","json_array","json_arrayagg","json_exists","json_object",
        "json_objectagg","json_query","json_table","json_table_primitive","json_value","lag",
        "language","large","last_value","lateral","lead","leading","left","like","like_regex",
        "listagg","ln","local","localtime","localtimestamp","log","log10","lower","match",
        "match_number","match_recognize","matches","max","member","merge","method","min","minute",
        "mod","modifies","module","month","multiset","national","natural","nchar","nclob","new","no",
        "none","normalize","not","nth_value","ntile","null","nullif","numeric","octet_length",
        "occurrences_regex","of","offset","old","omit","on","one","only","open","or","order","out",
        "outer","over","overlaps","overlay","parameter","partition","pattern","per","percent",
        "percent_rank","percentile_cont","percentile_disc","period","portion","position",
        "position_regex","power","precedes","precision","prepare","primary","procedure","ptf","range",
        "rank","reads","real","recursive","ref","references","referencing","regr_avgx","regr_avgy",
        "regr_count","regr_intercept","regr_r2","regr_slope","regr_sxx","regr_sxy","regr_syy",
        "release","result","return","returns","revoke","right","rollback","rollup","row","row_number",
        "rows","running","savepoint","scope","scroll","search","second","seek","select","sensitive",
        "session_user","set","show","similar","sin","sinh","skip","smallint","some","specific",
        "specifictype","sql","sqlexception","sqlstate","sqlwarning","sqrt","start","static",
        "stddev_pop","stddev_samp","submultiset","subset","substring","substring_regex","succeeds",
        "sum","symmetric","system","system_time","system_user","table","tablesample","tan","tanh",
        "then","time","timestamp","timezone_hour","timezone_minute","to","trailing","translate",
        "translate_regex","translation","treat","trigger","trim","trim_array","true","truncate",
        "uescape","union","unique","unknown","unnest","update","upper","user","using","value",
        "values","value_of","var_pop","var_samp","varbinary","varchar","varying","versioning","when",
        "whenever","where","width_bucket","window","with","within","without","year",
    ];

    private static readonly string[] ReservedFunctions =
    [
        "abs","acos","array_agg","asin","atan","avg","cast","ceil","ceiling","coalesce","corr","cos",
        "cosh","count","covar_pop","covar_samp","cume_dist","dense_rank","deref","element","exp",
        "extract","first_value","floor","json_array","json_arrayagg","json_exists","json_object",
        "json_objectagg","json_query","json_table","json_table_primitive","json_value","lag",
        "last_value","lead","listagg","ln","log","log10","lower","max","min","mod","nth_value","ntile",
        "nullif","percent_rank","percentile_cont","percentile_disc","position","position_regex",
        "power","rank","regr_avgx","regr_avgy","regr_count","regr_intercept","regr_r2","regr_slope",
        "regr_sxx","regr_sxy","regr_syy","row_number","sin","sinh","sqrt","stddev_pop","stddev_samp",
        "substring","substring_regex","sum","tan","tanh","translate","translate_regex","treat","trim",
        "trim_array","unnest","upper","value_of","var_pop","var_samp","width_bucket",
    ];

    private static readonly string[] PossibleWithoutParens =
    [
        "current_catalog","current_date","current_default_transform_group","current_path",
        "current_role","current_schema","current_transform_group_for_type","current_user",
        "session_user","system_time","system_user","current_time","localtime","current_timestamp",
        "localtimestamp",
    ];

    private static readonly string[] Combos =
    [
        "create table","insert into","primary key","foreign key","not null","alter table",
        "add constraint","grouping sets","on overflow","character set","respect nulls","ignore nulls",
        "nulls first","nulls last","depth first","breadth first",
    ];

    private static string KwsToRegex(IEnumerable<string> list)
    {
        var alts = list.Select(kw => kw.Replace(" ", @"\s+", StringComparison.Ordinal));
        return @"\b(" + string.Join('|', alts) + @")\b";
    }

    private static string[] ReduceRelevancy(IEnumerable<string> list, Func<string, bool> when)
    {
        return [.. list.Select(item =>
        {
            if (item.Contains('|', StringComparison.Ordinal) || !when(item)) return item;
            return item + "|0";
        })];
    }

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var functions = ReservedFunctions;
        var keywordList = ReservedWords.Concat(NonReservedWords)
            .Where(k => !ReservedFunctions.Contains(k))
            .ToArray();
        var reducedKeywords = ReduceRelevancy(keywordList, k => k.Length < 3);

        var stringMode = new Mode
        {
            Scope = "string",
            Variants =
            [
                new Mode { Begin = "'", End = "'", Contains = [new() { Match = "''" }] },
            ],
        };
        var quotedIdentifier = new Mode
        {
            Begin = "\"",
            End = "\"",
            Contains = [new() { Match = "\"\"" }],
        };

        var variable = new Mode { Scope = "variable", Match = "@[a-z0-9][a-z0-9_]*" };
        var op = new Mode { Scope = "operator", Match = @"[-+*/=%^~]|&&?|\|\|?|!=?|<(?:=>?|<|>)?|>[>=]?" };
        var functionCall = new Mode
        {
            Match = @"\b(?:" + string.Join('|', functions) + @")\s*\(",
            Keywords = Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["built_in"] = functions }),
        };
        var multiWordKeywords = new Mode { Scope = "keyword", Match = KwsToRegex(Combos) };

        return new Mode
        {
            CaseInsensitive = true,
            Illegal = @"[{}]|<\/",
            KeywordPattern = @"\b[\w\.]+",
            Keywords = Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["keyword"] = reducedKeywords,
                ["literal"] = Literals,
                ["type"] = Types,
                ["built_in"] = PossibleWithoutParens,
            }),
            Contains =
            [
                new() { Scope = "type", Match = KwsToRegex(MultiWordTypes) },
                multiWordKeywords,
                functionCall,
                variable,
                stringMode,
                quotedIdentifier,
                CommonModes.CNumberMode,
                CommonModes.CBlockCommentMode,
                CommonModes.Comment("--", "$"),
                op,
            ],
        };
    }
}
