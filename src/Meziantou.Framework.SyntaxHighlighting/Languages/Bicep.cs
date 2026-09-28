using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

/// <summary>
/// Bicep, the Azure Resource Manager language, including Bicep parameters files (<c>.bicepparam</c>).
/// </summary>
/// <remarks>
/// highlight.js has no Bicep grammar, so this one is written from scratch following the scopes of the other grammars:
/// the symbolic names declared by <c>param</c>, <c>var</c>, <c>output</c>, <c>metadata</c>, <c>resource</c> and
/// <c>module</c> are variables, the names of user-defined types and functions are class and function titles,
/// decorators (<c>@description(...)</c>) and directives (<c>#disable-next-line</c>) are meta, object property keys
/// are attrs, calls to the standard functions (<c>resourceGroup()</c>, <c>uniqueString()</c>, …) are built-ins and
/// other calls are function invocations, and string interpolations (<c>${...}</c>) are substitutions.
/// See https://learn.microsoft.com/azure/azure-resource-manager/bicep/file.
/// </remarks>
internal static class Bicep
{
    private const string IdentifierRe = "[a-zA-Z_][a-zA-Z0-9_]*";

    // An identifier can only start where no identifier character precedes it, which also keeps the patterns below
    // from being retried from every character of a long identifier.
    private static readonly string IdentifierStart = CommonModes.RunStart(@"\w", "a-zA-Z_");

    private static readonly string[] ReservedKeywords =
    [
        "metadata", "targetScope", "import", "extension", "provider", "using", "extends", "param", "var", "resource",
        "existing", "module", "output", "type", "func", "assert", "if", "for", "in", "as", "with", "from",
    ];

    private static readonly string[] Literals = ["true", "false", "null"];

    private static readonly string[] Types = ["string", "int", "bool", "object", "array", "resourceInput", "resourceOutput"];

    // https://learn.microsoft.com/azure/azure-resource-manager/bicep/bicep-functions
    private static readonly string[] BuiltInFunctions =
    [
        "any", "array", "base64", "base64ToJson", "base64ToString", "bool", "buildUri", "cidrHost", "cidrSubnet", "concat",
        "contains", "dataUri", "dataUriToString", "dateTimeAdd", "dateTimeFromEpoch", "dateTimeToEpoch", "deployer",
        "deployment", "distinct", "empty", "endsWith", "environment", "existingResource", "exists", "extensionResourceId",
        "externalInput", "fail", "filter", "first", "flatten", "format", "getSecret", "groupBy", "guid", "indexOf", "int",
        "intersection", "items", "join", "json", "last", "lastIndexOf", "length", "like", "loadFileAsBase64",
        "loadJsonContent", "loadTextContent", "loadYamlContent", "managementGroup", "managementGroupResourceId", "map",
        "mapValues", "max", "min", "newGuid", "objectKeys", "padLeft", "parseCidr", "parseUri", "pickZones", "providers",
        "range", "readEnvironmentVariable", "reduce", "reference", "replace", "resourceGroup", "resourceId",
        "roleDefinitions", "shallowMerge", "skip", "sort", "split", "startsWith", "string", "subscription",
        "subscriptionResourceId", "substring", "take", "tenant", "tenantResourceId", "toLogicalZone", "toLogicalZones",
        "toLower", "toObject", "toPhysicalZone", "toPhysicalZones", "toUpper", "trim", "union", "uniqueString", "uri",
        "uriComponent", "uriComponentToString", "utcNow",
    ];

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var keywords = Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["keyword"] = ReservedKeywords,
            ["literal"] = Literals,
            ["type"] = Types,
        });

        var comments = new[] { CommonModes.CLineCommentMode, CommonModes.CBlockCommentMode };

        var number = new Mode
        {
            Scope = "number",
            Match = @"\b\d+\b",
        };

        // `${...}` in a string; `$${...}` in a `$$'''` multi-line string.
        var interpolation = new Mode
        {
            Scope = "subst",
            Begin = @"\$\{",
            End = @"\}",
            Keywords = keywords,
            KeywordPattern = IdentifierRe,
        };

        var doubleDollarInterpolation = new Mode(interpolation) { Begin = @"\$\$\{" };

        var strings = new Mode
        {
            Scope = "string",
            Variants =
            [
                // Multi-line strings have no escape sequences. `$'''` and `$$'''` strings have interpolations.
                new Mode { Begin = "\\$\\$'''", End = "'''(?!')", Contains = [doubleDollarInterpolation] },
                new Mode { Begin = "\\$'''", End = "'''(?!')", Contains = [interpolation] },
                new Mode { Begin = "'''", End = "'''(?!')" },

                // A single-line string cannot span lines, so an unterminated one ends with its line.
                new Mode { Begin = "'", End = "'|$", Contains = [CommonModes.BackslashEscape, interpolation] },
            ],
        };

        // `@description('...')`, `@secure()`, `@sys.minLength(3)`.
        var decorator = new Mode
        {
            Scope = "meta",
            Match = "@(?:" + IdentifierRe + @"\.)?" + IdentifierRe,
        };

        // `#disable-next-line no-unused-params BCP081`
        var directive = new Mode
        {
            Scope = "meta",
            Begin = @"#[a-zA-Z][\w-]*",
            End = "$",
        };

        // An object property key, `name: value` or `'quoted-key': value`, starts a line or follows `{` or `,`, so that
        // the first branch of a conditional (`cond ? a : b`) and the source of a loop (`for x in items:`) are not keys.
        const string PropertyKeyStart = @"(?<=^[ \t]*|[{,][ \t]*)";
        const string PropertyKeyEnd = @"(?=[ \t]*:(?!:))";
        var propertyKey = new Mode
        {
            Scope = "attr",
            Match = IdentifierStart + PropertyKeyStart + IdentifierRe + PropertyKeyEnd,
        };

        var quotedPropertyKey = new Mode
        {
            Scope = "attr",
            Match = "(?=')" + PropertyKeyStart + @"'(?:[^'\\\n]|\\.)*'" + PropertyKeyEnd,
        };

        var symbolDeclaration = new Mode
        {
            BeginParts = [@"\b(?:param|var|output|metadata|resource|module)", @"[ \t]+", IdentifierRe],
            BeginScope = new Dictionary<int, string>
            {
                [1] = "keyword",
                [3] = "variable",
            },
        };

        var typeDeclaration = new Mode
        {
            BeginParts = [@"\btype", @"[ \t]+", IdentifierRe],
            BeginScope = new Dictionary<int, string>
            {
                [1] = "keyword",
                [3] = "title.class",
            },
        };

        var functionDeclaration = new Mode
        {
            BeginParts = [@"\bfunc", @"[ \t]+", IdentifierRe],
            BeginScope = new Dictionary<int, string>
            {
                [1] = "keyword",
                [3] = "title.function",
            },
        };

        // `resourceGroup()`, `sys.concat(...)`, `stg.listKeys()`.
        var builtInCall = new Mode
        {
            Scope = "built_in",
            Match = IdentifierStart + @"(?:" + string.Join('|', BuiltInFunctions) + @"|list[A-Z]\w*)\b(?=\s*\()",
        };

        // Keywords followed by `(` (`if (cond)`, `for (item, i) in`) are not calls.
        var functionCall = new Mode
        {
            Scope = "title.function.invoke",
            Match = IdentifierStart + @"(?!(?:" + string.Join('|', ReservedKeywords.Concat(Literals).Concat(Types)) + @")\b)" + IdentifierRe + @"(?=\s*\()",
        };

        // A property access is not a keyword: `stg.properties.type`, `vnet.id`; a method call is left to the call
        // patterns above.
        var propertyAccess = new Mode { Match = @"\." + IdentifierRe + @"\b(?!\s*\()" };

        // An object literal inside an interpolation must not end it: `${ {a: 1}.a }`.
        var nestedBraces = new Mode
        {
            Begin = @"\{",
            End = @"\}",
            Keywords = keywords,
            KeywordPattern = IdentifierRe,
        };

        Mode[] expression = [strings, number, decorator, builtInCall, functionCall, propertyAccess];
        nestedBraces.Contains = [.. comments, propertyKey, quotedPropertyKey, .. expression, nestedBraces];
        interpolation.Contains = [.. comments, .. expression, nestedBraces];
        doubleDollarInterpolation.Contains = interpolation.Contains;

        return new Mode
        {
            Keywords = keywords,
            KeywordPattern = IdentifierRe,
            Contains =
            [
                .. comments,
                directive,
                propertyKey,
                quotedPropertyKey,
                symbolDeclaration,
                typeDeclaration,
                functionDeclaration,
                .. expression,
            ],
        };
    }
}
