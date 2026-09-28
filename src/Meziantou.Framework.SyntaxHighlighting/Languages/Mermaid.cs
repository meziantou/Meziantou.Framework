using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

/// <summary>
/// Mermaid diagrams.
/// </summary>
/// <remarks>
/// highlight.js has no Mermaid grammar, so this one is written from scratch. It aims at readable real-world diagrams, not
/// at validating them. The diagram type declaration (<c>flowchart LR</c>, <c>sequenceDiagram</c>, ...) starts a mode that
/// lasts to the end of the document and knows the keywords of that diagram type only, so a word is a keyword only where
/// the diagram gives it a meaning. Scopes: diagram types, statements and directions are keywords; arrows and links are
/// operators; node text (<c>A[text]</c>, <c>B((text))</c>, ...), link text (<c>-- text --&gt;</c>, <c>|text|</c>), quoted
/// text and message text after <c>:</c> are strings; <c>%%</c> comments are comments; <c>%%{init: ...}%%</c> directives and
/// annotations (<c>&lt;&lt;interface&gt;&gt;</c>) are meta; style properties are attrs, and class names are
/// <c>title.class</c>. A YAML front matter block (<c>---</c> ... <c>---</c>) is highlighted as YAML.
/// See https://mermaid.js.org/intro/syntax-reference.html.
/// </remarks>
internal static class Mermaid
{
    private const string KeywordPattern = @"[A-Za-z_][\w-]*";
    private const string WordEnd = @"(?![\w-])";

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var comment = CommonModes.Comment(@"%%(?!\{)", "$");

        // A directive can span lines. An unterminated one ends before a blank line or a line made of words only (such as a
        // diagram declaration), which cannot be part of its JSON. Words must be separated by spaces: letting a word be split
        // anywhere made a line of letters that is not followed by the end of the line exponential.
        var directive = new Mode
        {
            Scope = "meta",
            Begin = @"%%\{",
            End = @"\}%%|(?=\n[ \t]*(?:[A-Za-z][\w-]*(?:[ \t]+[A-Za-z][\w-]*)*[ \t]*)?$)",
            Keywords = Keywords.FromWords(["init", "initialize", "wrap", "config"]),
            Contains = [CommonModes.QuoteStringMode, CommonModes.AposStringMode],
        };

        // A front matter block is only allowed at the very start of the document.
        var frontMatter = new Mode
        {
            Begin = @"(?<![\s\S])---[ \t]*$",
            End = @"^---[ \t]*$",
            SubLanguage = "yaml",
        };

        // Markdown strings (`"`text`"`) can span lines, but not a blank line.
        var markdownString = new Mode { Scope = "string", Begin = "\"`", End = "`\"|(?=\\n[ \\t]*$)" };

        // Other strings cannot span lines, so an unterminated one ends with its line.
        var quotedString = new Mode { Scope = "string", Begin = "\"", End = "\"|$" };

        Mode[] strings = [markdownString, quotedString];

        // In a text that is already a string, a quoted text can contain the closing delimiter of the text.
        Mode[] quotedTexts = [new Mode { Begin = markdownString.Begin, End = markdownString.End }, new Mode { Begin = quotedString.Begin, End = quotedString.End }];

        // `quadrant-1` is a keyword, but `0-15` is a range.
        var number = new Mode { Scope = "number", Match = @"(?<![\w.]|[A-Za-z_]-)-?\d+(?:\.\d+)?(?![\w.])" };

        // Text after `:` (messages, notes, descriptions, labels).
        var colonText = new Mode { Scope = "string", Begin = @":[ \t]*", End = "$", ExcludeBegin = true };

        // `-->`, `---`, `-.->`, `==>`, `~~~`, `<-->`, `o--o`, `x--x`, `--x`, and their longer forms.
        var arrow = new Mode
        {
            Scope = "operator",
            Match = @"(?:<|(?<!\w)[ox])?(?:-{2,}|={2,}|-\.+-|~{3,})(?:>|[ox](?!\w))?",
        };

        // `A -- text --> B`, `A -. text .-> B`, `A == text ==> B`. The text ends at the first closing arrow, which is also
        // the closing arrow of any earlier opening one on the line: only the first opening one (after the scan start) is
        // tried, which keeps a long line of unclosed ones linear.
        var textLink = new Mode
        {
            BeginParts = [@"(?=(?:--|==|-\.)[ \t]+\S)(?:\G|(?<!(?:--|==|-\.)[ \t]+\S(?:(?!\G)[^\n])*?))(?:--|==|-\.)", @"[ \t]+", @"\S[^\n]*?", @"[ \t]+", @"-{2,}>|={2,}>|\.-+>|-{3,}|={3,}|\.-"],
            BeginScope = new Dictionary<int, string> { [1] = "operator", [3] = "string", [5] = "operator" },
        };

        // `-->|text|`.
        var pipeText = new Mode
        {
            Scope = "string",
            Begin = @"\|",
            End = @"\||$",
            ExcludeBegin = true,
            ExcludeEnd = true,
            Contains = quotedTexts,
        };

        // `A:::className`.
        var classShorthand = new Mode
        {
            BeginParts = [":::", @"[\w-]+"],
            BeginScope = new Dictionary<int, string> { [2] = "title.class" },
        };

        // `A@{ shape: rect, label: "text" }`.
        var shapeData = new Mode
        {
            Begin = @"@\{",
            End = @"\}",
            Contains = [new Mode { Scope = "attr", Match = @"[\w-]+(?=[ \t]*:)" }, .. strings, CommonModes.AposStringMode, number],
        };

        // `classDef name fill:#f9f,stroke:#333,stroke-width:4px`, `style id fill:#f9f`, `linkStyle 0 stroke:#ff3`.
        var styleStatement = new Mode
        {
            BeginParts = [@"(?<![\w-])(?:classDef|style|linkStyle)" + WordEnd],
            BeginScope = new Dictionary<int, string> { [1] = "keyword" },
            End = "$",
            Keywords = Keywords.FromWords(["default", "interpolate"]),
            KeywordPattern = KeywordPattern,
            Contains =
            [
                comment,
                new Mode { Scope = "title.class", Match = @"(?<=(?<![\w-])classDef[ \t]+)[\w-]+" },
                new Mode { Scope = "attr", Match = @"[\w-]+(?=[ \t]*:)" },
                new Mode { Scope = "number", Match = @"#[0-9A-Fa-f]{3,8}(?![\w-])|(?<![\w.#-])\d+(?:\.\d+)?(?:px|em|rem|%)?(?![\w.])" },
            ],
        };

        // `<<interface>>`, `<<fork>>`, `<<choice>>`.
        var annotation = new Mode { Scope = "meta", Match = @"<<[\w-]+>>" };

        Mode[] nodeShapes = NodeShapes(quotedTexts);

        // `title`, `accTitle` and `accDescr` are followed by free text.
        var commonStatements = Statement("title|accTitle|accDescr");

        var flowchart = Diagram(
            "flowchart-elk|flowchart|graph",
            "subgraph end direction class click callback call href TB TD BT RL LR _blank _self _parent _top",
            [commonStatements, styleStatement, .. nodeShapes, shapeData, pipeText, textLink, arrow, classShorthand, .. strings]);

        var sequence = Diagram(
            "sequenceDiagram",
            "participant actor boundary control entity database collections queue create destroy end activate deactivate Note note over left right of autonumber off link links",
            [
                commonStatements,

                // The label of a block is free text.
                Statement("loop|alt|else|opt|par|and|critical|option|break|rect|box"),

                // So is the alias of a participant.
                new Mode
                {
                    BeginParts = [@"(?<![\w-])as" + WordEnd],
                    BeginScope = new Dictionary<int, string> { [1] = "keyword" },
                    End = "$",
                    Contains = [comment],
                },
                new Mode { Scope = "operator", Match = @"(?:<<)?-{1,2}(?:>>|>|x|\))[+-]?" },
                shapeData,
                colonText,
                number,
                .. strings,
            ]);

        // A member (`Animal : +int age`), a relation label (`A --> B : label`) or the body of a class.
        var functionName = new Mode { Scope = "title.function", Match = @"[A-Za-z_]\w*(?=\()" };
        var classBody = new Mode
        {
            Begin = @"(?<=(?<![\w-])class[ \t]+[^\n{]*)\{",
            End = @"\}",
            Contains = [comment, annotation, functionName],
        };
        var classDiagram = Diagram(
            "classDiagram-v2|classDiagram",
            "class namespace note for link click callback call href cssClass direction TB TD BT RL LR",
            [
                commonStatements,
                styleStatement,
                new Mode { Scope = "title.class", Match = @"(?<=(?<![\w-])class[ \t]+)[A-Za-z_][\w-]*" },
                classBody,
                annotation,
                new Mode { Scope = "operator", Match = @"(?:<\||\*|(?<!\w)o|\(\)|<)?(?:--|\.\.)(?:\|>|\*|o(?!\w)|>|\(\))?" },
                new Mode { Begin = ":", End = "$", Contains = [functionName] },
                classShorthand,
                .. strings,
            ]);

        // A note that spans lines ends with `end note`.
        var multiLineNote = new Mode
        {
            BeginParts = [@"(?<![\w-])note", @"[ \t]+", "left|right", @"[ \t]+", "of", @"[ \t]+[\w-]+(?=[ \t]*$)"],
            BeginScope = new Dictionary<int, string> { [1] = "keyword", [3] = "keyword", [5] = "keyword" },
            Starts = new Mode
            {
                Scope = "string",
                End = @"^[ \t]*end[ \t]+note" + WordEnd,
                ExcludeEnd = true,
            },
        };
        var state = Diagram(
            "stateDiagram-v2|stateDiagram",
            "state as note end left right of direction class TB TD BT RL LR",
            [
                commonStatements,
                styleStatement,
                multiLineNote,
                annotation,
                new Mode { Scope = "symbol", Match = @"\[\*\]" },
                new Mode { Scope = "operator", Match = @"-->|(?<=^[ \t]*)--(?=[ \t]*$)" },
                classShorthand,
                colonText,
                .. strings,
            ]);

        // `string name PK "comment"`: the first word of an attribute is its type.
        var entityBody = new Mode
        {
            Begin = @"\{",
            End = @"\}",
            Keywords = Keywords.FromWords(["PK", "FK", "UK"]),
            Contains = [comment, new Mode { Scope = "type", Match = @"(?<=^[ \t]*)[A-Za-z_][\w\[\]()-]*" }, .. strings],
        };
        var er = Diagram(
            "erDiagram",
            "to optionally one zero many only or more direction TB BT RL LR",
            [
                commonStatements,
                styleStatement,
                entityBody,
                new Mode { Scope = "operator", Match = @"[|}][o|](?:--|\.\.)[o|][|{]" },
                classShorthand,
                colonText,
                .. strings,
            ]);

        // `Task name :done, des1, 2014-01-06, 3d`.
        var taskMetadata = new Mode
        {
            Begin = ":",
            End = "$",
            Keywords = Keywords.FromWords(["done", "active", "crit", "milestone", "vert", "after", "until"]),
            Contains =
            [
                comment,
                new Mode { Scope = "number", Match = @"(?<![\w.-])(?:\d{4}-\d{2}-\d{2}(?:[T ]\d{1,2}:\d{2}(?::\d{2})?)?|\d{1,2}:\d{2}(?::\d{2})?|\d+(?:\.\d+)?(?:ms|s|m|h|d|w|M|y)?)(?![\w.-])" },
            ],
        };
        var gantt = Diagram(
            "gantt",
            "click call href",
            [
                commonStatements,
                Statement("dateFormat|axisFormat|tickInterval|excludes|includes|todayMarker|section|weekday|weekend|displayMode|inclusiveEndDates|topAxis"),
                taskMetadata,
                .. strings,
            ]);

        var pie = Diagram("pie", "showData", [commonStatements, number, .. strings]);

        var journey = Diagram(
            "journey",
            keywords: null,
            [commonStatements, Statement("section"), new Mode { Begin = ":", End = "$", Contains = [number] }]);

        var gitGraph = Diagram(
            "gitGraph",
            "commit branch checkout switch merge cherry-pick reset NORMAL REVERSE HIGHLIGHT LR TB BT",
            [commonStatements, new Mode { Scope = "attr", Match = @"(?<![\w-])(?:id|tag|type|order|parent|msg)(?=[ \t]*:)" }, number, .. strings]);

        var mindmap = Diagram(
            "mindmap",
            keywords: null,
            [
                commonStatements,
                new Mode { Scope = "meta", Match = @"::icon\([^)\n]*\)" },
                new Mode { Scope = "string", Begin = @"(?<=\w)\)\)", End = @"\(\(|$", ExcludeBegin = true, ExcludeEnd = true },
                new Mode { Scope = "string", Begin = @"(?<=\w)\)", End = @"\(|$", ExcludeBegin = true, ExcludeEnd = true },
                .. nodeShapes,
                classShorthand,
                .. strings,
            ]);

        var timeline = Diagram("timeline", keywords: null, [commonStatements, Statement("section"), .. strings]);

        var quadrant = Diagram(
            "quadrantChart",
            "x-axis y-axis quadrant-1 quadrant-2 quadrant-3 quadrant-4",
            [commonStatements, styleStatement, arrow, classShorthand, number, .. strings]);

        var requirement = Diagram(
            "requirementDiagram",
            keywords: null,
            [
                commonStatements,
                styleStatement,

                // The value of `id`, `text` and `docref` is free text.
                new Mode
                {
                    BeginParts = [@"(?<![\w-])(?:id|text|docref)", @"[ \t]*:"],
                    BeginScope = new Dictionary<int, string> { [1] = "attr" },
                    End = "$",
                    Contains = [.. strings],
                },
                new Mode { Scope = "attr", Match = @"(?<![\w-])(?:risk|verifymethod|type)(?=[ \t]*:)" },
                new Mode { Scope = "operator", Match = @"<-|->|(?<=[ \t])-(?=[ \t])" },
                classShorthand,
                .. strings,
            ],
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["keyword"] =
                [
                    "requirement", "functionalRequirement", "interfaceRequirement", "performanceRequirement", "physicalRequirement",
                    "designConstraint", "element", "contains", "copies", "derives", "satisfies", "verifies", "refines", "traces", "direction",
                    "TB", "BT", "LR", "RL",
                ],
                ["literal"] =
                [
                    "Low", "Medium", "High", "low", "medium", "high",
                    "Analysis", "Inspection", "Test", "Demonstration", "analysis", "inspection", "test", "demonstration",
                ],
            });

        var c4 = Diagram(
            "C4Context|C4Container|C4Component|C4Dynamic|C4Deployment",
            keywords: null,
            [commonStatements, new Mode { Scope = "variable", Match = @"\$[A-Za-z_]\w*" }, .. strings],
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["built_in"] =
                [
                    "Person", "Person_Ext", "System", "System_Ext", "SystemDb", "SystemDb_Ext", "SystemQueue", "SystemQueue_Ext",
                    "Container", "Container_Ext", "ContainerDb", "ContainerDb_Ext", "ContainerQueue", "ContainerQueue_Ext",
                    "Component", "Component_Ext", "ComponentDb", "ComponentDb_Ext", "ComponentQueue", "ComponentQueue_Ext",
                    "Boundary", "Enterprise_Boundary", "System_Boundary", "Container_Boundary", "Node", "Node_L", "Node_R", "Deployment_Node",
                    "Rel", "BiRel", "Rel_U", "Rel_Up", "Rel_D", "Rel_Down", "Rel_L", "Rel_Left", "Rel_R", "Rel_Right", "Rel_Back", "RelIndex",
                    "UpdateElementStyle", "UpdateRelStyle", "UpdateBoundaryStyle", "UpdateLayoutConfig", "AddElementTag", "AddRelTag",
                ],
            });

        var xyChart = Diagram("xychart-beta|xychart", "x-axis y-axis line bar horizontal", [commonStatements, arrow, number, .. strings]);
        var sankey = Diagram("sankey-beta|sankey", keywords: null, [number, .. strings]);
        var block = Diagram("block-beta|block", "columns space block end", [commonStatements, styleStatement, .. nodeShapes, arrow, classShorthand, number, .. strings]);
        var packet = Diagram("packet-beta|packet", keywords: null, [commonStatements, number, .. strings]);
        var architecture = Diagram("architecture-beta|architecture", "group service junction in", [commonStatements, .. nodeShapes, arrow, .. strings]);
        var kanban = Diagram("kanban", keywords: null, [commonStatements, .. nodeShapes, shapeData, .. strings]);
        var radar = Diagram("radar-beta|radar", "axis curve max min ticks showLegend graticule circle polygon", [commonStatements, .. nodeShapes, number, .. strings]);
        var treemap = Diagram("treemap-beta|treemap", keywords: null, [commonStatements, styleStatement, classShorthand, number, .. strings]);
        var info = Diagram("info", "showInfo", []);

        return new Mode
        {
            Contains =
            [
                frontMatter,
                comment,
                directive,
                flowchart,
                sequence,
                classDiagram,
                state,
                er,
                gantt,
                pie,
                journey,
                gitGraph,
                mindmap,
                timeline,
                quadrant,
                requirement,
                c4,
                xyChart,
                sankey,
                block,
                packet,
                architecture,
                kanban,
                radar,
                treemap,
                info,
                .. strings,
            ],
        };

        // The declaration of a diagram type starts a mode that lasts to the end of the document.
        Mode Diagram(string declarations, string? keywords, Mode[] contains, IReadOnlyDictionary<string, string[]>? keywordGroups = null)
        {
            var groups = keywordGroups ?? new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["keyword"] = keywords?.Split(' ') ?? [],
            };

            return new Mode
            {
                BeginParts = [@"^[ \t]*", "(?:" + declarations + ")" + WordEnd],
                BeginScope = new Dictionary<int, string> { [2] = "keyword" },
                EndsWithParent = true,
                Keywords = Keywords.FromMap(groups),
                KeywordPattern = KeywordPattern,
                Contains = [comment, directive, .. contains],
            };
        }

        // A keyword at the start of a line, followed by free text.
        Mode Statement(string statements)
        {
            return new Mode
            {
                BeginParts = [@"^[ \t]*", "(?:" + statements + ")" + WordEnd],
                BeginScope = new Dictionary<int, string> { [2] = "keyword" },
                End = "$",
                Contains = [comment, .. strings],
            };
        }
    }

    // `A[text]`, `A(text)`, `A([text])`, `A[[text]]`, `A[(text)]`, `A((text))`, `A(((text)))`, `A>text]`, `A{text}`,
    // `A{{text}}`, `A[/text/]`, `A[\text\]`, `A[/text\]`, `A[\text/]`. The text is a string; a shape cannot span lines.
    private static Mode[] NodeShapes(Mode[] quotedTexts)
    {
        (string Open, string Close)[] shapes =
        [
            (@"\(\(\(", @"\)\)\)"),
            (@"\(\(", @"\)\)"),
            (@"\(\[", @"\]\)"),
            (@"\[\[", @"\]\]"),
            (@"\[\(", @"\)\]"),
            (@"\{\{", @"\}\}"),
            (@"\[/", @"/\]|\\\]"),
            (@"\[\\", @"\\\]|/\]"),
            (@"\[", @"\]"),
            (@"\(", @"\)"),
            (@"\{", @"\}"),
        ];

        var result = new List<Mode>();
        foreach (var (open, close) in shapes)
        {
            result.Add(new Mode
            {
                Scope = "string",

                // The shape follows the node id, possibly after spaces (`subgraph id [title]`), or another shape
                // (`service db(database)[Database]`).
                Begin = @"(?<=[\w)][ \t]*)" + open,
                End = close + "|$",
                ExcludeBegin = true,
                ExcludeEnd = true,
                Contains = quotedTexts,
            });
        }

        result.Add(new Mode { Scope = "string", Begin = @"(?<=\w)>", End = @"\]|$", ExcludeBegin = true, ExcludeEnd = true, Contains = quotedTexts });
        return [.. result];
    }
}
