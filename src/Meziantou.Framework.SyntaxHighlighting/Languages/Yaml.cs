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

        // The run class contains both ' ' and ':', so an unbounded `*` lets a candidate key span a
        // whole line and then backtrack over it looking for the terminating ':'. Repeated at every
        // start position that is quadratic in the line length, which makes a single long line (a
        // multi-word description value, for example) pathologically slow. YAML restricts a simple
        // key to 1024 characters, so bounding the run keeps every real key matching while making
        // the scan linear.
        const string KeyRun = @"[\w*@ :()\./-]{0,1024}";

        // Even bounded, the scan still costs up to 1024 steps from each position of a long run. A key
        // is only highlighted when it starts at the first non-blank character (any other start is
        // inside the plain scalar that begins there), so an unquoted key is only tried from the first
        // position of the run where it can start.
        var key = new Mode
        {
            Scope = "attr",
            Variants =
            [
                new Mode { Begin = CommonModes.RunStart(@"\w*@ :()\./-", @"\w*@") + @"[\w*@]" + KeyRun + @":(?=[ \t]|$)" },
                new Mode { Begin = @"""[\w*@]" + KeyRun + @""":(?=[ \t]|$)" },
                new Mode { Begin = @"'[\w*@]" + KeyRun + @"':(?=[ \t]|$)" },
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

        const string DateRe = "[0-9]{4}(-[0-9][0-9]){0,2}";
        const string TimeRe = @"([Tt \t][0-9][0-9]?(:[0-9][0-9]){2})?";
        const string FractionRe = @"(\.[0-9]*)?";
        const string ZoneRe = @"([ \t])*(Z|[-+][0-9][0-9]?(:[0-9][0-9])?)?";
        const string TimestampRe = @"\b" + DateRe + TimeRe + FractionRe + ZoneRe + @"\b";
        const string NumberRe = CommonModes.CNumberRe + @"\b";
        const string NamedTagRe = "!\\w+!" + UriChars;
        const string VerbatimTagRe = "!<" + UriChars + ">";
        const string PrimaryTagRe = "!" + UriChars;
        const string SecondaryTagRe = "!!" + UriChars;

        // What `BeginKeywords = literals` builds (see Compiler), written out to be shared with PlainScalarLosesRe.
        const string LiteralRe = @"(?<!\.)\b(true|false|yes|no|null)(?!\.)(?=\b|\s)";

        // A plain scalar (`\S+`, or a run without flow indicators in a flow collection) comes after the other modes, so
        // it loses wherever one of them starts. But its match spans the whole rest of the run, and each mode that wins
        // instead makes the tokenizer look for it again from the end of that mode: after each quoted string or number of
        // a run such as `"a""b"` or `1-1-1`, it was matched again up to the end of the run (quadratic on a long one).
        // Where one of the modes that can end inside a run starts, it only matches one character: that match always
        // loses, so its length does not matter.
        const string PlainScalarLosesRe = @"(?=[""'{[]|<%|" + NamedTagRe + "|" + VerbatimTagRe + "|" + PrimaryTagRe + "|" + SecondaryTagRe + "|" + LiteralRe + "|" + TimestampRe + "|" + NumberRe + ")";

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
                new Mode { Begin = "(?:" + PlainScalarLosesRe + @"\S|\S+)" },
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
                new Mode { Begin = "(?:" + PlainScalarLosesRe + @"[^\s,{}[\]]|[^\s,{}[\]]+)" },
            ],
            Contains =
            [
                CommonModes.BackslashEscape,
                templateVariables,
            ],
        };

        var timestamp = new Mode { Scope = "number", Begin = TimestampRe };

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
            new() { Scope = "type", Begin = NamedTagRe },
            new() { Scope = "type", Begin = VerbatimTagRe },
            new() { Scope = "type", Begin = PrimaryTagRe },
            new() { Scope = "type", Begin = SecondaryTagRe },
            new() { Scope = "meta", Begin = "&" + CommonModes.UnderscoreIdentRe + "$" },
            new() { Scope = "meta", Begin = @"\*" + CommonModes.UnderscoreIdentRe + "$" },
            new() { Scope = "bullet", Begin = @"-(?=[ ]|$)" },
            CommonModes.HashCommentMode,
            new()
            {
                Begin = LiteralRe,
                Keywords = Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["literal"] = literals }),
            },
            timestamp,
            new() { Scope = "number", Begin = NumberRe },
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
