using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class PowerShell
{
    private static readonly string[] Types =
    [
        "string","char","byte","int","long","bool","decimal","single","double","DateTime",
        "xml","array","hashtable","void",
    ];

    private const string ValidVerbs =
        "Add|Clear|Close|Copy|Enter|Exit|Find|Format|Get|Hide|Join|Lock|" +
        "Move|New|Open|Optimize|Pop|Push|Redo|Remove|Rename|Reset|Resize|" +
        "Search|Select|Set|Show|Skip|Split|Step|Switch|Undo|Unlock|" +
        "Watch|Backup|Checkpoint|Compare|Compress|Convert|ConvertFrom|" +
        "ConvertTo|Dismount|Edit|Expand|Export|Group|Import|Initialize|" +
        "Limit|Merge|Mount|Out|Publish|Restore|Save|Sync|Unpublish|Update|" +
        "Approve|Assert|Build|Complete|Confirm|Deny|Deploy|Disable|Enable|Install|Invoke|" +
        "Register|Request|Restart|Resume|Start|Stop|Submit|Suspend|Uninstall|" +
        "Unregister|Wait|Debug|Measure|Ping|Repair|Resolve|Test|Trace|Connect|" +
        "Disconnect|Read|Receive|Send|Write|Block|Grant|Protect|Revoke|Unblock|" +
        "Unprotect|Use|ForEach|Sort|Tee|Where";

    private const string ComparisonOperators =
        "-and|-as|-band|-bnot|-bor|-bxor|-casesensitive|-ccontains|-ceq|-cge|-cgt|" +
        "-cle|-clike|-clt|-cmatch|-cne|-cnotcontains|-cnotlike|-cnotmatch|-contains|" +
        "-creplace|-csplit|-eq|-exact|-f|-file|-ge|-gt|-icontains|-ieq|-ige|-igt|" +
        "-ile|-ilike|-ilt|-imatch|-in|-ine|-inotcontains|-inotlike|-inotmatch|" +
        "-ireplace|-is|-isnot|-isplit|-join|-le|-like|-lt|-match|-ne|-not|" +
        "-notcontains|-notin|-notlike|-notmatch|-or|-regex|-replace|-shl|-shr|" +
        "-split|-wildcard|-xor";

    private const string KeywordList =
        "if else foreach return do while until elseif begin for trap data dynamicparam " +
        "end break throw param continue finally in switch exit filter try process catch " +
        "hidden static parameter";

    private const string BuiltInList =
        "ac asnp cat cd CFS chdir clc clear clhy cli clp cls clv cnsn compare copy cp " +
        "cpi cpp curl cvpa dbp del diff dir dnsn ebp echo|0 epal epcsv epsn erase etsn exsn fc fhx " +
        "fl ft fw gal gbp gc gcb gci gcm gcs gdr gerr ghy gi gin gjb gl gm gmo gp gps gpv group " +
        "gsn gsnp gsv gtz gu gv gwmi h history icm iex ihy ii ipal ipcsv ipmo ipsn irm ise iwmi " +
        "iwr kill lp ls man md measure mi mount move mp mv nal ndr ni nmo npssc nsn nv ogv oh " +
        "popd ps pushd pwd r rbp rcjb rcsn rd rdr ren ri rjb rm rmdir rmo rni rnp rp rsn rsnp " +
        "rujb rv rvpa rwmi sajb sal saps sasv sbp sc scb select set shcm si sl sleep sls sort sp " +
        "spjb spps spsv start stz sujb sv swmi tee trcm type wget where wjb write";

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var keywords = Engine.Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["keyword"] = KeywordList,
            ["built_in"] = BuiltInList,
        });

        var backtickEscape = new Mode { Begin = @"`[\s\S]" };

        var var = new Mode
        {
            Scope = "variable",
            Variants =
            [
                new Mode { Begin = @"\$\B" },
                new Mode { Scope = "keyword", Begin = @"\$this" },
                new Mode { Begin = @"\$[\w\d][\w\d_:]*" },
            ],
        };

        var literal = new Mode { Scope = "literal", Begin = @"\$(null|true|false)\b" };

        var quoteString = new Mode
        {
            Scope = "string",
            Variants =
            [
                new Mode { Begin = "\"", End = "\"" },
                new Mode { Begin = "@\"", End = "^\"@" },
            ],
            Contains =
            [
                backtickEscape,
                var,
                new() { Scope = "variable", Begin = @"\$[A-z]", End = "[^A-z]" },
            ],
        };

        var aposString = new Mode
        {
            Scope = "string",
            Variants =
            [
                new Mode { Begin = "'", End = "'" },
                new Mode { Begin = "@'", End = "^'@" },
            ],
        };

        var psHelpTags = new Mode
        {
            Scope = "doctag",
            Variants =
            [
                new Mode { Begin = @"\.(synopsis|description|example|inputs|outputs|notes|link|component|role|functionality)" },
                new Mode { Begin = @"\.(parameter|forwardhelptargetname|forwardhelpcategory|remotehelprunspace|externalhelp)\s+\S+" },
            ],
        };

        var psComment = new Mode
        {
            Scope = "comment",
            Variants =
            [
                new Mode { Begin = "#", End = "$" },
                new Mode { Begin = "<#", End = "#>" },
            ],
            Contains = [psHelpTags],
        };

        var cmdlets = new Mode
        {
            Scope = "built_in",
            Variants =
            [
                new Mode { Begin = "(" + ValidVerbs + @")+(-)[\w\d]+" },
            ],
        };

        var psClass = new Mode
        {
            Scope = "class",
            BeginKeywords = ["class", "enum"],
            End = @"\s*\{",
            ExcludeEnd = true,
            Contains = [CommonModes.TitleMode],
        };

        var psFunction = new Mode
        {
            Scope = "function",
            Begin = @"function\s+",
            End = @"\s*\{|$",
            ExcludeEnd = true,
            ReturnBegin = true,
            Contains =
            [
                new() { Scope = "keyword", Begin = "function" },
                new() { Scope = "title", Begin = @"\w[\w\d]*((-)[\w\d]+)*" },
                new()
                {
                    Scope = "params",
                    Begin = @"\(",
                    End = @"\)",
                    Contains = [var],
                },
            ],
        };

        var psUsing = new Mode
        {
            Begin = @"using\s",
            End = "$",
            ReturnBegin = true,
            Contains =
            [
                quoteString,
                aposString,
                new() { Scope = "keyword", Begin = @"(using|assembly|command|module|namespace|type)" },
            ],
        };

        var psArguments = new Mode
        {
            Variants =
            [
                new Mode { Scope = "operator", Begin = "(" + ComparisonOperators + @")\b" },
                new Mode { Scope = "operator", Begin = @"\?\?=|\?\?|\?\.|&&|\|\||\?" },
                new Mode { Scope = "literal", Begin = @"(-){1,2}[\w\d-]+" },
            ],
        };

        var hashSigns = new Mode { Scope = "selector-tag", Begin = @"@\B" };

        var psMethodsTitle = new Mode { Scope = "title", Begin = CommonModes.IdentRe, EndsParent = true };
        var psMethods = new Mode
        {
            Scope = "function",
            Begin = @"\[.*\]\s*[\w]+[ ]??\(",
            End = "$",
            ReturnBegin = true,
            Contains =
            [
                new()
                {
                    Scope = "keyword",
                    Begin = "(" + KeywordList.Replace(' ', '|') + @")\b",
                    EndsParent = true,
                },
                psMethodsTitle,
            ],
        };

        var gentlemansSet = new List<Mode>
        {
            psMethods,
            psComment,
            backtickEscape,
            CommonModes.NumberMode,
            quoteString,
            aposString,
            cmdlets,
            var,
            literal,
            hashSigns,
        };

        var psType = new Mode
        {
            Begin = @"\[",
            End = @"\]",
            ExcludeBegin = true,
            ExcludeEnd = true,
        };
        var psTypeContains = new List<Mode>
        {
            psType, // self
        };
        psTypeContains.AddRange(gentlemansSet);
        psTypeContains.Add(new Mode { Scope = "built_in", Begin = "(" + string.Join('|', Types) + ")" });
        psTypeContains.Add(new Mode { Scope = "type", Begin = @"[\.\w\d]+" });
        psType.Contains = psTypeContains;

        // PS_METHODS.contains.unshift(PS_TYPE) → insert at front
        psMethods.Contains = [psType, .. psMethods.Contains];

        var rootContains = new List<Mode>(gentlemansSet)
        {
            psClass,
            psFunction,
            psUsing,
            psArguments,
            psType,
        };

        return new Mode
        {
            CaseInsensitive = true,
            Keywords = keywords,
            KeywordPattern = @"-?[A-z\.\-]+\b",
            Contains = rootContains,
        };
    }
}
