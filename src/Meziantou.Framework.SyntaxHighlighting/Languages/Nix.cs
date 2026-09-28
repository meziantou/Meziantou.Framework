using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Nix
{
    private const string IdentifierRe = @"[A-Za-z_][A-Za-z0-9_'-]*";
    private const string IdentifierChars = @"A-Za-z0-9_'\-";
    private const string PathPieceRe = @"[A-Za-z0-9_\+\.-]+";
    private const string OperatorWithoutMinusRe = @"(?:==|=|\+\+|\+|<=|<\||<|>=|>|->|//|/|!=|!|\|\||\|>|\?|\*|&&)";

    private static readonly string[] Builtins =
    [
        "abort", "add", "addDrvOutputDependencies", "addErrorContext", "all", "any", "appendContext", "attrNames",
        "attrValues", "baseNameOf", "bitAnd", "bitOr", "bitXor", "break", "builtins", "catAttrs", "ceil",
        "compareVersions", "concatLists", "concatMap", "concatStringsSep", "convertHash", "currentSystem",
        "currentTime", "deepSeq", "derivation", "derivationStrict", "dirOf", "div", "elem", "elemAt", "false",
        "fetchGit", "fetchMercurial", "fetchTarball", "fetchTree", "fetchurl", "filter", "filterSource", "findFile",
        "flakeRefToString", "floor", "foldl'", "fromJSON", "fromTOML", "functionArgs", "genList", "genericClosure",
        "getAttr", "getContext", "getEnv", "getFlake", "groupBy", "hasAttr", "hasContext", "hashFile", "hashString",
        "head", "import", "intersectAttrs", "isAttrs", "isBool", "isFloat", "isFunction", "isInt", "isList", "isNull",
        "isPath", "isString", "langVersion", "length", "lessThan", "listToAttrs", "map", "mapAttrs", "match", "mul",
        "nixPath", "nixVersion", "null", "parseDrvName", "parseFlakeRef", "partition", "path", "pathExists",
        "placeholder", "readDir", "readFile", "readFileType", "removeAttrs", "replaceStrings", "scopedImport", "seq",
        "sort", "split", "splitVersion", "storeDir", "storePath", "stringLength", "sub", "substring", "tail", "throw",
        "toFile", "toJSON", "toPath", "toString", "toXML", "trace", "traceVerbose", "true", "tryEval", "typeOf",
        "unsafeDiscardOutputDependency", "unsafeDiscardStringContext", "unsafeGetAttrPos", "warn", "zipAttrsWith",
    ];

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var keywords = Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["keyword"] = ["assert", "else", "if", "in", "inherit", "let", "or", "rec", "then", "with"],
            ["literal"] = ["true", "false", "null"],
            ["built_in"] =
            [
                // toplevel builtins
                "abort", "baseNameOf", "builtins", "derivation", "derivationStrict", "dirOf", "fetchGit", "fetchMercurial",
                "fetchTarball", "fetchTree", "fromTOML", "import", "isNull", "map", "placeholder", "removeAttrs",
                "scopedImport", "throw", "toString",
            ],
        });

        // Deviation from highlight.js, whose keywords are runs of `\w`: `-` and `'` are identifier characters in Nix,
        // so the `map` of `lib-map` or the `in` of `with-in` are not keywords.
        const string KeywordPattern = IdentifierRe;

        // Deviation from highlight.js, which does not require the name to end there: the first alternative that is a
        // prefix of the name won, so `builtins.mapAttrs` was highlighted as `builtins.map` followed by plain `Attrs`.
        var builtins = new Mode
        {
            Scope = "built_in",
            Match = @"builtins\.(?:" + string.Join('|', Builtins) + ")(?![" + IdentifierChars + "])",
        };

        var lookupPath = new Mode { Scope = "symbol", Match = "<" + IdentifierRe + "(/" + IdentifierRe + ")*>" };
        // A path can only end right before a whitespace or a `;`, so it fails or succeeds identically from each `/` of
        // a long `a/b/c/…` run, and trying it from each of them is quadratic. A `/` followed by a path piece is only
        // tried when the previous `/` of the run cannot be: from that one, the pattern matches the same pieces and ends
        // at the same position. The previous `/` does not count when the scan starts after it (\G).
        const string PathSlashRe = @"(?=/)(?:\G|(?!(?<=/(?:(?!\G)[A-Za-z0-9_\+\.-])+)/[A-Za-z0-9_\+\.-]))/";
        var path = new Mode { Scope = "symbol", Match = @"(\.\.|\.|~)?" + PathSlashRe + "(" + PathPieceRe + ")?(/" + PathPieceRe + @")*(?=[\s;])" };

        var operatorMode = new Mode { Scope = "operator", Match = OperatorWithoutMinusRe + "(?!-)" };

        // '-' is being handled by itself to ensure we are able to tell the difference between a dash in an identifier
        // and a minus operator.
        var number = new Mode { Scope = "number", Match = CommonModes.NumberRe + "(?!-)" };

        // highlight.js's `beforeMatch` (the text that must precede the match, consumed as plain text) is expressed as
        // a lookbehind in this file.
        var minusOperator = new Mode
        {
            Variants =
            [
                // The (?!>) is used to ensure this doesn't collide with the '->' operator
                new Mode { Scope = "operator", Begin = @"(?<=\s)-(?!>)" },
                new Mode
                {
                    BeginParts = [CommonModes.NumberRe, "-", "(?!>)"],
                    BeginScope = new Dictionary<int, string> { [1] = "number", [2] = "operator" },
                },
                new Mode
                {
                    BeginParts = [OperatorWithoutMinusRe, "-", "(?!>)"],
                    BeginScope = new Dictionary<int, string> { [1] = "operator", [2] = "operator" },
                },
            ],
        };

        // Deviation from highlight.js, whose `beforeMatch` does not work when it matches empty text: an attribute
        // at the start of a line was not highlighted (`x = 1;`), or lost its first character (`f<attr>oo.bar</attr>`).
        var attrs = new Mode
        {
            Begin = "(?=[A-Za-z_])(?<=(?:^|[{;])\\s*)" + IdentifierRe + @"(\." + IdentifierRe + @")*\s*=(?!=)",
            ReturnBegin = true,
            Contains =
            [
                new Mode { Scope = "attr", Match = IdentifierRe + @"(\." + IdentifierRe + @")*(?=\s*=)" },
            ],
        };

        var antiquote = new Mode
        {
            Scope = "subst",
            Begin = @"\$\{",
            End = @"\}",
            Keywords = keywords,
            KeywordPattern = KeywordPattern,
        };

        var escapedLiteral = new Mode { Scope = "char.escape", Match = @"\\(?!\$)." };
        var strings = new Mode
        {
            Scope = "string",
            Variants =
            [
                new Mode
                {
                    Begin = "''",
                    End = "''",
                    Contains =
                    [
                        new Mode { Scope = "char.escape", Match = @"''\$" },
                        antiquote,
                        new Mode { Scope = "char.escape", Match = "'''" },

                        // Deviation from highlight.js, which only knows the `'''` and `''$` escapes: the `''` of the
                        // `''\n` escape ended the string, so the rest of it was code and its end started a new string.
                        new Mode { Scope = "char.escape", Match = @"''\\[\s\S]" },
                        escapedLiteral,
                    ],
                },
                new Mode
                {
                    Begin = "\"",
                    End = "\"",
                    Contains =
                    [
                        new Mode { Scope = "char.escape", Match = @"\\\$" },
                        antiquote,
                        escapedLiteral,
                    ],
                },
            ],
        };

        // Only the first position of an identifier is tried: otherwise, each position of a long identifier that is not
        // followed by `:` would rescan the rest of it.
        var functionParams = new Mode
        {
            Scope = "params",
            Match = CommonModes.RunStart(IdentifierChars, "A-Za-z_") + IdentifierRe + @"\s*:(?=\s)",
        };

        // highlight.js also has a `/** */` comment highlighted as Markdown, but it is listed after the `/* */` comment,
        // which always matches first, so it is never used.
        List<Mode> expressions =
        [
            number,
            CommonModes.HashCommentMode,
            CommonModes.CBlockCommentMode,
            builtins,
            strings,
            lookupPath,
            path,
            functionParams,
            attrs,
            minusOperator,
            operatorMode,
        ];

        antiquote.Contains = expressions;

        return new Mode
        {
            Keywords = keywords,
            KeywordPattern = KeywordPattern,
            Contains =
            [
                .. expressions,
                new Mode { Scope = "meta.prompt", Match = @"^nix-repl>(?=\s)" },
                new Mode { Scope = "meta", Begin = @"(?<=\s):([a-z]+|\?)" },
            ],
        };
    }
}
