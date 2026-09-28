using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

// Formal syntax is not published, helpful link:
// https://github.com/kornilova-l/matlab-IntelliJ-plugin/blob/master/src/main/grammar/Matlab.bnf
internal static class Matlab
{
    // `'` and `.'` (repeatable: a'') transpose the value before them, when they directly follow it. Anywhere else, `'`
    // starts a string.
    private const string TransposeRe = @"('|\.')+";

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        // Entered after a value: it consumes the transpose operators that directly follow it, if any, then ends.
        var transpose = new Mode
        {
            Contains = [new Mode { Begin = TransposeRe }],
        };

        var lineComment = CommonModes.Comment("%", "$");

        return new Mode
        {
            Keywords = Engine.Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["keyword"] =
                    "arguments break case catch classdef continue else elseif end enumeration events for function "
                    + "global if methods otherwise parfor persistent properties return spmd switch try while",
                ["built_in"] =
                    "sin sind sinh asin asind asinh cos cosd cosh acos acosd acosh tan tand tanh atan "
                    + "atand atan2 atanh sec secd sech asec asecd asech csc cscd csch acsc acscd acsch cot "
                    + "cotd coth acot acotd acoth hypot exp expm1 log log1p log10 log2 pow2 realpow reallog "
                    + "realsqrt sqrt nthroot nextpow2 abs angle complex conj imag real unwrap isreal "
                    + "cplxpair fix floor ceil round mod rem sign airy besselj bessely besselh besseli "
                    + "besselk beta betainc betaln ellipj ellipke erf erfc erfcx erfinv expint gamma "
                    + "gammainc gammaln psi legendre cross dot factor isprime primes gcd lcm rat rats perms "
                    + "nchoosek factorial cart2sph cart2pol pol2cart sph2cart hsv2rgb rgb2hsv zeros ones "
                    + "eye repmat rand randn linspace logspace freqspace meshgrid accumarray size length "
                    + "ndims numel disp isempty isequal isequalwithequalnans cat reshape diag blkdiag tril "
                    + "triu fliplr flipud flipdim rot90 find sub2ind ind2sub bsxfun ndgrid permute ipermute "
                    + "shiftdim circshift squeeze isscalar isvector ans eps realmax realmin pi i|0 inf nan "
                    + "isnan isinf isfinite j|0 why compan gallery hadamard hankel hilb invhilb magic pascal "
                    + "rosser toeplitz vander wilkinson max min nanmax nanmin mean nanmean type table "
                    + "readtable writetable sortrows sort figure plot plot3 scatter scatter3 cellfun "
                    + "legend intersect ismember procrustes hold num2cell",
            }),
            // RunStart: `\s+/\w+` fails identically from every position of a run of whitespace, which is quadratic on a
            // long one.
            Illegal = @"(//|""|#|/\*|" + CommonModes.RunStart(@"\s") + @"\s+/\w+)",
            Contains =
            [
                // Deviation from highlight.js, whose function signature does not contain comments (the words of
                // `function y = f(x) % comment` are titles): a comment ends the signature.
                new Mode
                {
                    Scope = "function",
                    BeginKeywords = ["function"],
                    End = "$",
                    Contains =
                    [
                        CommonModes.UnderscoreTitleMode,
                        new Mode
                        {
                            Scope = "params",
                            Variants =
                            [
                                new Mode { Begin = @"\(", End = @"\)" },
                                new Mode { Begin = @"\[", End = @"\]" },
                            ],
                        },
                        lineComment,
                    ],
                },

                // Deviation from highlight.js, which also matches true and false inside identifiers (trueCount,
                // isfalse): they must be whole words.
                new Mode
                {
                    Scope = "built_in",
                    Begin = @"\b(?:true|false)\b",
                    Starts = transpose,
                },

                // An identifier followed by transpose operators. RunStart only lets the pattern start at the first letter
                // of an identifier: it fails identically from every later position, which is quadratic on a long one.
                new Mode { Begin = CommonModes.RunStart("a-zA-Z_0-9", "a-zA-Z") + "[a-zA-Z][a-zA-Z_0-9]*" + TransposeRe },
                new Mode
                {
                    Scope = "number",
                    Begin = CommonModes.CNumberRe,
                    Starts = transpose,
                },
                new Mode
                {
                    Scope = "string",
                    Begin = "'",
                    End = "'",
                    Contains = [new Mode { Begin = "''" }],
                },
                new Mode
                {
                    Begin = @"\]|\}|\)",
                    Starts = transpose,
                },
                new Mode
                {
                    Scope = "string",
                    Begin = "\"",
                    End = "\"",
                    Contains = [new Mode { Begin = "\"\"" }],
                    Starts = transpose,
                },

                // %{ and %} must be alone on their line. IndentedLineStartRe is `^\s*`, without rescanning a run of blank
                // lines from each of its line starts.
                CommonModes.Comment(CommonModes.IndentedLineStartRe + @"%\{\s*$", CommonModes.IndentedLineStartRe + @"%\}\s*$"),
                lineComment,
            ],
        };
    }
}
