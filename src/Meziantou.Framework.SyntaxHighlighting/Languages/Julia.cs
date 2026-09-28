using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Julia
{
    // highlight.js uses `[A-Za-z_X][A-Za-z_0-9X]*` where X is the range U+00A1-U+FFFF. These classes are the same sets,
    // written as the complement of the ASCII characters and U+0080-U+00A0 that are not allowed.
    private const string VariableNameStartRe = @"[^\x00-\x40\x5B-\x5E\x60\x7B-\xA0]";
    private const string VariableNamePartRe = @"[^\x00-\x2F\x3A-\x40\x5B-\x5E\x60\x7B-\xA0]";
    private const string VariableNameRe = VariableNameStartRe + VariableNamePartRe + "*";

    // Names are generated from Julia 1.5.2 by highlight.js; see the generator scripts in highlight.js's julia.js.
    private static readonly string[] KeywordList =
    [
        "baremodule", "begin", "break", "catch", "ccall", "const", "continue", "do", "else", "elseif", "end", "export",
        "false", "finally", "for", "function", "global", "if", "import", "in", "isa", "let", "local", "macro", "module",
        "quote", "return", "true", "try", "using", "where", "while",
    ];

    private static readonly string[] LiteralList =
    [
        "ARGS", "C_NULL", "DEPOT_PATH", "ENDIAN_BOM", "ENV", "Inf", "Inf16", "Inf32", "Inf64", "InsertionSort",
        "LOAD_PATH", "MergeSort", "NaN", "NaN16", "NaN32", "NaN64", "PROGRAM_FILE", "QuickSort", "RoundDown",
        "RoundFromZero", "RoundNearest", "RoundNearestTiesAway", "RoundNearestTiesUp", "RoundToZero", "RoundUp",
        "VERSION", "devnull", "false", "im", "missing", "nothing", "pi", "stderr", "stdin", "stdout", "true", "undef",
        "π", "ℯ",
    ];

    private static readonly string[] BuiltInList =
    [
        "AbstractArray", "AbstractChannel", "AbstractChar", "AbstractDict", "AbstractDisplay", "AbstractFloat",
        "AbstractIrrational", "AbstractMatrix", "AbstractRange", "AbstractSet", "AbstractString", "AbstractUnitRange",
        "AbstractVecOrMat", "AbstractVector", "Any", "ArgumentError", "Array", "AssertionError", "BigFloat", "BigInt",
        "BitArray", "BitMatrix", "BitSet", "BitVector", "Bool", "BoundsError", "CapturedException", "CartesianIndex",
        "CartesianIndices", "Cchar", "Cdouble", "Cfloat", "Channel", "Char", "Cint", "Cintmax_t", "Clong", "Clonglong",
        "Cmd", "Colon", "Complex", "ComplexF16", "ComplexF32", "ComplexF64", "CompositeException", "Condition",
        "Cptrdiff_t", "Cshort", "Csize_t", "Cssize_t", "Cstring", "Cuchar", "Cuint", "Cuintmax_t", "Culong",
        "Culonglong", "Cushort", "Cvoid", "Cwchar_t", "Cwstring", "DataType", "DenseArray", "DenseMatrix",
        "DenseVecOrMat", "DenseVector", "Dict", "DimensionMismatch", "Dims", "DivideError", "DomainError", "EOFError",
        "Enum", "ErrorException", "Exception", "ExponentialBackOff", "Expr", "Float16", "Float32", "Float64",
        "Function", "GlobalRef", "HTML", "IO", "IOBuffer", "IOContext", "IOStream", "IdDict", "IndexCartesian",
        "IndexLinear", "IndexStyle", "InexactError", "InitError", "Int", "Int128", "Int16", "Int32", "Int64", "Int8",
        "Integer", "InterruptException", "InvalidStateException", "Irrational", "KeyError", "LinRange",
        "LineNumberNode", "LinearIndices", "LoadError", "MIME", "Matrix", "Method", "MethodError", "Missing",
        "MissingException", "Module", "NTuple", "NamedTuple", "Nothing", "Number", "OrdinalRange", "OutOfMemoryError",
        "OverflowError", "Pair", "PartialQuickSort", "PermutedDimsArray", "Pipe", "ProcessFailedException", "Ptr",
        "QuoteNode", "Rational", "RawFD", "ReadOnlyMemoryError", "Real", "ReentrantLock", "Ref", "Regex", "RegexMatch",
        "RoundingMode", "SegmentationFault", "Set", "Signed", "Some", "StackOverflowError", "StepRange", "StepRangeLen",
        "StridedArray", "StridedMatrix", "StridedVecOrMat", "StridedVector", "String", "StringIndexError", "SubArray",
        "SubString", "SubstitutionString", "Symbol", "SystemError", "Task", "TaskFailedException", "Text",
        "TextDisplay", "Timer", "Tuple", "Type", "TypeError", "TypeVar", "UInt", "UInt128", "UInt16", "UInt32",
        "UInt64", "UInt8", "UndefInitializer", "UndefKeywordError", "UndefRefError", "UndefVarError", "Union",
        "UnionAll", "UnitRange", "Unsigned", "Val", "Vararg", "VecElement", "VecOrMat", "Vector", "VersionNumber",
        "WeakKeyDict", "WeakRef",
    ];

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var keywords = Engine.Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["keyword"] = KeywordList,
            ["literal"] = LiteralList,
            ["built_in"] = BuiltInList,
        });

        var number = new Mode
        {
            Scope = "number",
            // binary (0b10), octal (0o76543210), hexadecimal (0xfedcba876543210), hexadecimal floating point (0x1p0,
            // 0x1.2p2), decimal (9876543210, 100_000_000) and floating point (1.2, 1.2f, .2, 1., 1.2e10, 1.2e-10) literals.
            Begin = @"(\b0x[\d_]*(\.[\d_]*)?|0x\.\d[\d_]*)p[-+]?\d+|\b0[box][a-fA-F0-9][a-fA-F0-9_]*|(\b\d[\d_]*(\.[\d_]*)?|\.\d[\d_]*)([eEfF][-+]?\d+)?",
        };

        var @char = new Mode
        {
            Scope = "string",
            // highlight.js uses `'(.|\\[xXuU][a-zA-Z0-9]+)'`, which misses the common escapes (`'\n'`, `'\''`).
            Begin = @"'(\\[xXuU][a-zA-Z0-9]+|\\[0-7]{1,3}|\\[abefnrtv\\'""$]|[^\\\n])'",
        };

        // Not in highlight.js, where the first `)` ends an interpolation (`"$(f(x))"`).
        var nestedParentheses = new Mode
        {
            Begin = @"\(",
            End = @"\)",
            Keywords = keywords,
            KeywordPattern = VariableNameRe,
        };

        var interpolation = new Mode
        {
            Scope = "subst",
            Begin = @"\$\(",
            End = @"\)",
            Keywords = keywords,
            KeywordPattern = VariableNameRe,
        };

        var interpolatedVariable = new Mode
        {
            Scope = "variable",
            Begin = @"\$" + VariableNameRe,
        };

        // The prefix of a string macro (`r"..."`): only try the first position of a word, otherwise each character of a
        // long word that is not followed by a quote would rescan the rest of the word.
        var stringPrefixRe = CommonModes.RunStart(@"\w") + @"\w*";
        var @string = new Mode
        {
            Scope = "string",
            Contains = [CommonModes.BackslashEscape, interpolation, interpolatedVariable],
            Variants =
            [
                new Mode { Begin = stringPrefixRe + "\"\"\"", End = "\"\"\"\\w*" },
                new Mode { Begin = stringPrefixRe + "\"", End = "\"\\w*" },
            ],
        };

        var command = new Mode
        {
            Scope = "string",
            Contains = [CommonModes.BackslashEscape, interpolation, interpolatedVariable],
            Begin = "`",
            End = "`",
        };

        var macroCall = new Mode
        {
            Scope = "meta",
            // The broadcast macro `@.` is not in highlight.js.
            Begin = @"@(?:" + VariableNameRe + @"|\.)",
        };

        var comment = new Mode
        {
            Scope = "comment",
            Variants =
            [
                new Mode { Begin = "#=", End = "=#" },
                new Mode { Begin = "#", End = "$" },
            ],
        };

        Mode[] contains =
        [
            number,
            @char,
            @string,
            command,
            macroCall,
            comment,
            CommonModes.HashCommentMode,
            new Mode
            {
                Scope = "keyword",
                Begin = @"\b(((abstract|primitive)\s+)type|(mutable\s+)?struct)\b",
            },
            new Mode { Begin = "<:" },
        ];
        nestedParentheses.Contains = [.. contains, nestedParentheses];
        interpolation.Contains = [.. contains, nestedParentheses];

        return new Mode
        {
            Keywords = keywords,
            KeywordPattern = VariableNameRe,
            Illegal = "</",
            Contains = contains,
        };
    }
}
