using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Yaml
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        string[] literals = ["true", "false", "yes", "no", "null"];
        const string UriChars = @"[\w#;/?:@&=+$,.~*'()[\]]+";

        var key = new Mode
        {
            Scope = "attr",
            Variants =
            [
                new Mode { Begin = @"[\w*@][\w*@ :()\./-]*:(?=[ \t]|$)" },
                new Mode { Begin = @"""[\w*@][\w*@ :()\./-]*"":(?=[ \t]|$)" },
                new Mode { Begin = @"'[\w*@][\w*@ :()\./-]*':(?=[ \t]|$)" },
            ],
        };

        var templateVariables = new Mode
        {
            Scope = "template-variable",
            Variants =
            [
                new Mode { Begin = @"\{\{", End = @"\}\}" },
                new Mode { Begin = @"%\{", End = @"\}" },
            ],
        };

        var singleQuoteString = new Mode
        {
            Scope = "string",
            Begin = "'",
            End = "'",
            Contains =
            [
                new() { Scope = "char.escape", Match = "''" },
            ],
        };

        var stringMode = new Mode
        {
            Scope = "string",
            Variants =
            [
                new Mode { Begin = "\"", End = "\"" },
                new Mode { Begin = @"\S+" },
            ],
            Contains =
            [
                CommonModes.BackslashEscape,
                templateVariables,
            ],
        };

        var containerString = new Mode
        {
            Scope = "string",
            Variants =
            [
                new Mode
                {
                    Begin = "'", End = "'",
                    Contains = [new() { Begin = "''" }],
                },
                new Mode { Begin = "\"", End = "\"" },
                new Mode { Begin = @"[^\s,{}[\]]+" },
            ],
            Contains =
            [
                CommonModes.BackslashEscape,
                templateVariables,
            ],
        };

        const string DateRe = "[0-9]{4}(-[0-9][0-9]){0,2}";
        const string TimeRe = @"([Tt \t][0-9][0-9]?(:[0-9][0-9]){2})?";
        const string FractionRe = @"(\.[0-9]*)?";
        const string ZoneRe = @"([ \t])*(Z|[-+][0-9][0-9]?(:[0-9][0-9])?)?";
        var timestamp = new Mode { Scope = "number", Begin = @"\b" + DateRe + TimeRe + FractionRe + ZoneRe + @"\b" };

        var valueContainer = new Mode
        {
            End = ",",
            EndsWithParent = true,
            ExcludeEnd = true,
            Keywords = Keywords.FromWords(literals),
        };
        var obj = new Mode
        {
            Begin = @"\{",
            End = @"\}",
            Contains = [valueContainer],
            Illegal = @"\n",
        };
        var arr = new Mode
        {
            Begin = @"\[",
            End = @"\]",
            Contains = [valueContainer],
            Illegal = @"\n",
        };

        var modes = new List<Mode>
        {
            key,
            new() { Scope = "meta", Begin = @"^---\s*$" },
            new() { Scope = "string", Begin = @"[\|>]([1-9]?[+-])?[ ]*\n( +)[^ ][^\n]*\n(\2[^\n]+\n?)*" },
            new()
            {
                Begin = "<%[%=-]?", End = "[%-]?%>",
                ExcludeBegin = true, ExcludeEnd = true,
            },
            new() { Scope = "type", Begin = "!\\w+!" + UriChars },
            new() { Scope = "type", Begin = "!<" + UriChars + ">" },
            new() { Scope = "type", Begin = "!" + UriChars },
            new() { Scope = "type", Begin = "!!" + UriChars },
            new() { Scope = "meta", Begin = "&" + CommonModes.UnderscoreIdentRe + "$" },
            new() { Scope = "meta", Begin = @"\*" + CommonModes.UnderscoreIdentRe + "$" },
            new() { Scope = "bullet", Begin = @"-(?=[ ]|$)" },
            CommonModes.HashCommentMode,
            new()
            {
                BeginKeywords = literals,
                Keywords = Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["literal"] = literals }),
            },
            timestamp,
            new() { Scope = "number", Begin = CommonModes.CNumberRe + @"\b" },
            obj,
            arr,
            singleQuoteString,
            stringMode,
        };

        var valueModes = new List<Mode>(modes);
        valueModes.RemoveAt(valueModes.Count - 1);
        valueModes.Add(containerString);
        valueContainer.Contains = valueModes;

        return new Mode
        {
            CaseInsensitive = true,
            Contains = modes,
        };
    }
}
