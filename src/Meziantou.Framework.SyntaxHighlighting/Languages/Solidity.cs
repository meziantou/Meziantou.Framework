using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

/// <summary>
/// Solidity, including the Yul code of inline <c>assembly</c> blocks.
/// </summary>
/// <remarks>
/// highlight.js has no Solidity grammar, so this one is written from scratch following the scopes of the other
/// grammars: elementary types (<c>uint256</c>, <c>bytes32</c>, <c>address</c>, …) are types, ether and time units
/// (<c>ether</c>, <c>days</c>) are literals like <c>true</c>, the global variables and functions (<c>msg</c>,
/// <c>block</c>, <c>require</c>, <c>keccak256</c>, …) are built-ins, <c>this</c> and <c>super</c> are language
/// variables, the names of contracts, interfaces, libraries, structs, enums and user-defined value types are class
/// titles (inherited ones after <c>is</c> too), the names of functions, modifiers, events and errors are function
/// titles, <c>pragma</c> directives are meta, and NatSpec tags (<c>@notice</c>, <c>@param</c>) are doctags of the
/// <c>///</c> and <c>/** */</c> comments. In <c>assembly { }</c> blocks, the Yul keywords are keywords and the EVM
/// opcodes are built-ins.
/// See https://docs.soliditylang.org/en/latest/grammar.html.
/// </remarks>
internal static class Solidity
{
    private const string IdentifierRe = @"[a-zA-Z_$][\w$]*";

    // An identifier can only start where no identifier character precedes it, which also keeps the patterns below
    // from being retried from every character of a long identifier.
    private static readonly string IdentifierStart = CommonModes.RunStart(@"\w$", "a-zA-Z_$");

    private static readonly string[] ReservedKeywords =
    [
        "abstract", "anonymous", "as", "assembly", "break", "calldata", "catch", "constant", "constructor", "continue",
        "contract", "delete", "do", "else", "emit", "enum", "event", "external", "fallback", "for", "function", "if",
        "immutable", "import", "indexed", "interface", "internal", "is", "library", "mapping", "memory", "modifier", "new",
        "override", "payable", "pragma", "private", "public", "pure", "receive", "return", "returns", "revert", "storage",
        "struct", "transient", "try", "type", "unchecked", "using", "view", "virtual", "while",
    ];

    // `fixedMxN` and `ufixedMxN` are matched by a pattern, not listed here.
    private static readonly string[] Types =
    [
        "address", "bool", "string", "bytes", "byte", "int", "uint", "fixed", "ufixed",
        .. Enumerable.Range(1, 32).Select(size => "int" + (size * 8).ToString(CultureInfo.InvariantCulture)),
        .. Enumerable.Range(1, 32).Select(size => "uint" + (size * 8).ToString(CultureInfo.InvariantCulture)),
        .. Enumerable.Range(1, 32).Select(size => "bytes" + size.ToString(CultureInfo.InvariantCulture)),
    ];

    private static readonly string[] Literals =
    [
        "true", "false",

        // Ether and time units (`szabo`, `finney` and `years` were removed from the language, but are common in older
        // contracts).
        "wei", "gwei", "szabo", "finney", "ether", "seconds", "minutes", "hours", "days", "weeks", "years",
    ];

    private static readonly string[] BuiltIns =
    [
        "msg", "block", "tx", "abi", "now", "gasleft", "blockhash", "blobhash", "keccak256", "sha256", "ripemd160",
        "ecrecover", "addmod", "mulmod", "selfdestruct", "require", "assert",
    ];

    private static readonly string[] LanguageVariables = ["this", "super"];

    private static readonly string[] YulKeywords = ["let", "if", "switch", "case", "default", "for", "break", "continue", "leave", "function"];

    private static readonly string[] YulBuiltIns =
    [
        "stop", "add", "sub", "mul", "div", "sdiv", "mod", "smod", "exp", "not", "lt", "gt", "slt", "sgt", "eq", "iszero",
        "and", "or", "xor", "byte", "shl", "shr", "sar", "clz", "addmod", "mulmod", "signextend", "keccak256", "pc", "pop",
        "mload", "mstore", "mstore8", "sload", "sstore", "tload", "tstore", "msize", "gas", "address", "balance",
        "selfbalance", "caller", "callvalue", "calldataload", "calldatasize", "calldatacopy", "codesize", "codecopy",
        "extcodesize", "extcodecopy", "returndatasize", "returndatacopy", "mcopy", "extcodehash", "create", "create2",
        "call", "callcode", "delegatecall", "staticcall", "return", "revert", "selfdestruct", "invalid", "log0", "log1",
        "log2", "log3", "log4", "chainid", "origin", "gasprice", "blockhash", "blobhash", "coinbase", "timestamp", "number",
        "difficulty", "prevrandao", "gaslimit", "basefee", "blobbasefee", "slotnum", "datasize", "dataoffset", "datacopy",
        "setimmutable", "loadimmutable", "linkersymbol", "memoryguard",
    ];

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var keywords = Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["keyword"] = ReservedKeywords,
            ["type"] = Types,
            ["literal"] = Literals,
            ["built_in"] = BuiltIns,
            ["variable.language"] = LanguageVariables,
        });

        // `@notice`, `@param`, `@custom:security-contact`, ...
        var natSpecTag = new Mode
        {
            Scope = "doctag",
            Match = @"@(?:title|author|notice|dev|param|return|inheritdoc|custom:[a-z][a-z-]*)\b",
        };

        Mode[] comments =
        [
            CommonModes.Comment("///", "$", extraContains: [natSpecTag]),

            // `/**/` is an empty regular comment, not a NatSpec one.
            CommonModes.Comment(@"/\*\*(?!/)", @"\*/", extraContains: [natSpecTag]),
            CommonModes.CLineCommentMode,
            CommonModes.CBlockCommentMode,
        ];

        // A string literal cannot span lines, so an unterminated one ends with its line. `hex"00ff"` and
        // `unicode"Hello 😃"` are strings too.
        var strings = new Mode
        {
            Scope = "string",
            Contains = [CommonModes.BackslashEscape],
            Variants =
            [
                new Mode { Begin = @"(?:\b(?:hex|unicode))?""", End = "\"|$" },
                new Mode { Begin = @"(?:\b(?:hex|unicode))?'", End = "'|$" },
            ],
        };

        var numbers = new Mode
        {
            Scope = "number",
            Variants =
            [
                new Mode { Match = @"\b0x[0-9a-fA-F](?:_?[0-9a-fA-F])*\b" },
                new Mode { Match = @"(?:\b\d(?:_?\d)*(?:\.\d(?:_?\d)*)?|(?<![\w$.])\.\d(?:_?\d)*)(?:[eE]-?\d(?:_?\d)*)?" },
            ],
        };

        var fixedPointType = new Mode
        {
            Scope = "type",
            Match = @"\bu?fixed\d+x\d+\b",
        };

        var functionDeclaration = new Mode
        {
            BeginParts = [@"\b(?:function|modifier|event)", @"\s+", IdentifierRe],
            BeginScope = new Dictionary<int, string>
            {
                [1] = "keyword",
                [3] = "title.function",
            },
        };

        // `error` is not a reserved word: it is only a keyword when it declares an error.
        var errorDeclaration = new Mode
        {
            BeginParts = [@"(?<![\w$.])error", @"\s+", IdentifierRe + @"(?=\s*\()"],
            BeginScope = new Dictionary<int, string>
            {
                [1] = "keyword",
                [3] = "title.function",
            },
        };

        var typeDeclaration = new Mode
        {
            BeginParts = [@"\b(?:contract|interface|library|struct|enum)", @"\s+", IdentifierRe],
            BeginScope = new Dictionary<int, string>
            {
                [1] = "keyword",
                [3] = "title.class",
            },
        };

        // `type Price is uint128;`: the underlying type after `is` is not an inherited contract.
        var userDefinedValueType = new Mode
        {
            BeginParts = [@"\btype", @"\s+", IdentifierRe, @"\s+", @"is\b"],
            BeginScope = new Dictionary<int, string>
            {
                [1] = "keyword",
                [3] = "title.class",
                [5] = "keyword",
            },
        };

        // `from` is a common parameter name (`transferFrom(address from, ...)`), so it is only a keyword here.
        var importDirective = new Mode
        {
            BeginKeywords = ["import"],
            End = ";",
            Keywords = Keywords.FromWords(["import", "as", "from"]),
            Contains = [.. comments, strings],
        };

        var pragmaDirective = new Mode
        {
            Scope = "meta",
            Begin = @"\bpragma\b",
            End = ";|$",
            Keywords = Keywords.FromWords(["pragma", "solidity", "abicoder", "experimental"]),
            Contains = [.. comments],
        };

        // `is Ownable(msg.sender), ERC20("Token", "TKN")`: the constructor arguments are expressions.
        var inheritanceArguments = new Mode
        {
            Begin = @"\(",
            End = @"\)",
            Keywords = keywords,
            KeywordPattern = IdentifierRe,
        };

        var inheritance = new Mode
        {
            BeginKeywords = ["is"],
            End = @"(?=[{;])",
            Contains =
            [
                .. comments,
                inheritanceArguments,
                new Mode
                {
                    Scope = "title.class.inherited",
                    Match = IdentifierStart + IdentifierRe,
                },
            ],
        };

        // A field access is not a keyword (`msg.value`, `x.length`); a method call is left to the call pattern below.
        var memberAccess = new Mode { Match = @"\." + IdentifierRe + @"(?![\w$]|\s*\()" };

        var functionCall = new Mode
        {
            Scope = "title.function.invoke",
            Match = IdentifierStart + @"(?!(?:" + string.Join('|', ReservedKeywords.Concat(Types).Concat(Literals).Concat(BuiltIns).Concat(LanguageVariables)) + @")(?![\w$]))" + IdentifierRe + @"(?=\s*\()",
        };

        var assembly = CreateAssemblyMode(comments, strings);

        inheritanceArguments.Contains = [.. comments, strings, numbers, memberAccess, functionCall, Mode.Self];

        return new Mode
        {
            Keywords = keywords,
            KeywordPattern = IdentifierRe,
            Contains =
            [
                .. comments,
                pragmaDirective,
                importDirective,
                assembly,
                strings,
                numbers,
                fixedPointType,
                functionDeclaration,
                errorDeclaration,
                typeDeclaration,
                userDefinedValueType,
                inheritance,
                memberAccess,
                functionCall,
            ],
        };
    }

    // `assembly { ... }`, `assembly ("memory-safe") { ... }`, `assembly "evmasm" { ... }`.
    private static Mode CreateAssemblyMode(Mode[] comments, Mode strings)
    {
        // Yul identifiers can contain dots (`x.slot`, `abi_decode_tuple.x`).
        const string YulIdentifierRe = @"[a-zA-Z_$][\w$.]*";
        var yulIdentifierStart = CommonModes.RunStart(@"\w$.", "a-zA-Z_$");

        var yulKeywords = Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["keyword"] = YulKeywords,
            ["literal"] = ["true", "false"],
            ["built_in"] = YulBuiltIns,
        });

        var yulNumbers = new Mode
        {
            Scope = "number",
            Match = @"\b(?:0x[0-9a-fA-F]+|\d+)\b",
        };

        var yulFunctionDeclaration = new Mode
        {
            BeginParts = [@"\bfunction", @"\s+", YulIdentifierRe],
            BeginScope = new Dictionary<int, string>
            {
                [1] = "keyword",
                [3] = "title.function",
            },
        };

        var yulFunctionCall = new Mode
        {
            Scope = "title.function.invoke",
            Match = yulIdentifierStart + @"(?!(?:" + string.Join('|', YulKeywords.Concat(YulBuiltIns).Concat(["true", "false"])) + @")(?![\w$.]))" + YulIdentifierRe + @"(?=\s*\()",
        };

        // Blocks nest: `for { let i := 0 } lt(i, n) { i := add(i, 1) } { ... }`.
        var yulBlock = new Mode
        {
            Begin = @"\{",
            End = @"\}",
            Keywords = yulKeywords,
            KeywordPattern = YulIdentifierRe,
        };
        yulBlock.Contains = [.. comments, strings, yulNumbers, yulFunctionDeclaration, yulFunctionCall, yulBlock];

        return new Mode
        {
            BeginKeywords = ["assembly"],
            End = @"\}",
            Contains = [.. comments, strings, new Mode(yulBlock) { EndsParent = true }],
        };
    }
}
