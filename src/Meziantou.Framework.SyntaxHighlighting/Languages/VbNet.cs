using Meziantou.Framework.SyntaxHighlighting.Engine;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class VbNet
{
    private const string KeywordList =
        "addhandler alias aggregate ansi as async assembly auto binary by byref byval " +
        "call case catch class compare const continue custom declare default delegate dim distinct do " +
        "each equals else elseif end enum erase error event exit explicit finally for friend from function " +
        "get global goto group handles if implements imports in inherits interface into iterator " +
        "join key let lib loop me mid module mustinherit mustoverride mybase myclass " +
        "namespace narrowing new next notinheritable notoverridable " +
        "of off on operator option optional order overloads overridable overrides " +
        "paramarray partial preserve private property protected public " +
        "raiseevent readonly redim removehandler resume return " +
        "select set shadows shared skip static step stop structure strict sub synclock " +
        "take text then throw to try unicode until using when where while widening with withevents writeonly yield";

    private const string BuiltInList =
        "addressof and andalso await directcast gettype getxmlnamespace is isfalse isnot istrue like mod nameof new not or orelse trycast typeof xor " +
        "cbool cbyte cchar cdate cdbl cdec cint clng cobj csbyte cshort csng cstr cuint culng cushort";

    private const string TypeList =
        "boolean byte char date decimal double integer long object sbyte short single string uinteger ulong ushort";

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var character = new Mode { Scope = "string", Begin = "\"(\"\"|[^/n])\"C\\b" };
        var stringMode = new Mode
        {
            Scope = "string",
            Begin = "\"",
            End = "\"",
            Illegal = @"\n",
            Contains = [new() { Begin = "\"\"" }],
        };

        const string MmDdYyyy = @"\d{1,2}/\d{1,2}/\d{4}";
        const string YyyyMmDd = @"\d{4}-\d{1,2}-\d{1,2}";
        const string Time12 = @"(\d|1[012])(:\d+){0,2} *(AM|PM)";
        const string Time24 = @"\d{1,2}(:\d{1,2}){1,2}";

        var date = new Mode
        {
            Scope = "literal",
            Variants =
            [
                new Mode { Begin = "# *(?:" + YyyyMmDd + "|" + MmDdYyyy + ") *#" },
                new Mode { Begin = "# *" + Time24 + " *#" },
                new Mode { Begin = "# *" + Time12 + " *#" },
                new Mode { Begin = "# *(?:" + YyyyMmDd + "|" + MmDdYyyy + ") +(?:" + Time12 + "|" + Time24 + ") *#" },
            ],
        };

        var number = new Mode
        {
            Scope = "number",
            Variants =
            [
                new Mode { Begin = @"\b\d[\d_]*((\.[\d_]+(E[+-]?[\d_]+)?)|(E[+-]?[\d_]+))[RFD@!#]?" },
                new Mode { Begin = @"\b\d[\d_]*((U?[SIL])|[%&])?" },
                new Mode { Begin = @"&H[\dA-F_]+((U?[SIL])|[%&])?" },
                new Mode { Begin = @"&O[0-7_]+((U?[SIL])|[%&])?" },
                new Mode { Begin = @"&B[01_]+((U?[SIL])|[%&])?" },
            ],
        };

        var label = new Mode { Scope = "label", Begin = @"^\w+:" };

        var docComment = new Mode
        {
            Scope = "comment",
            Begin = "'''",
            End = "$",
            Contains =
            [
                new() { Scope = "doctag", Begin = @"<\/?", End = ">" },
            ],
        };

        var comment = new Mode
        {
            Scope = "comment",
            End = "$",
            Variants =
            [
                new Mode { Begin = "'" },
                new Mode { Begin = @"([\t ]|^)REM(?=\s)" },
            ],
        };

        var directives = new Mode
        {
            Scope = "meta",
            Begin = @"[\t ]*#(const|disable|else|elseif|enable|end|externalsource|if|region)\b",
            End = "$",
            Keywords = Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["keyword"] = "const disable else elseif enable end externalsource if region then",
            }),
            Contains = [comment],
        };

        return new Mode
        {
            CaseInsensitive = true,
            ClassNameAliases = new Dictionary<string, string>(StringComparer.Ordinal) { ["label"] = "symbol" },
            Keywords = Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["keyword"] = KeywordList,
                ["built_in"] = BuiltInList,
                ["type"] = TypeList,
                ["literal"] = "true false nothing",
            }),
            Illegal = @"//|\{|\}|endif|gosub|variant|wend|^\$ ",
            Contains =
            [
                character,
                stringMode,
                date,
                number,
                label,
                docComment,
                comment,
                directives,
            ],
        };
    }
}
