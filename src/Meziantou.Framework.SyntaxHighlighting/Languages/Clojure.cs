using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Clojure
{
    private const string SymbolStart = @"a-zA-Z_\-!.?+*=<>&'";
    private const string SymbolRe = "[#]?[" + SymbolStart + "][" + SymbolStart + "0-9/;:$#]*";

    private const string Globals = "def defonce defprotocol defstruct defmulti defmethod defn- defn defmacro deftype defrecord";

    private const string BuiltIns =
        Globals + " " +
        "cond apply if-not if-let if not not= = < > <= >= == + / * - rem " +
        "quot neg? pos? delay? symbol? keyword? true? false? integer? empty? coll? list? " +
        "set? ifn? fn? associative? sequential? sorted? counted? reversible? number? decimal? " +
        "class? distinct? isa? float? rational? reduced? ratio? odd? even? char? seq? vector? " +
        "string? map? nil? contains? zero? instance? not-every? not-any? libspec? -> ->> .. . " +
        "inc compare do dotimes mapcat take remove take-while drop letfn drop-last take-last " +
        "drop-while while intern condp case reduced cycle split-at split-with repeat replicate " +
        "iterate range merge zipmap declare line-seq sort comparator sort-by dorun doall nthnext " +
        "nthrest partition eval doseq await await-for let agent atom send send-off release-pending-sends " +
        "add-watch mapv filterv remove-watch agent-error restart-agent set-error-handler error-handler " +
        "set-error-mode! error-mode shutdown-agents quote var fn loop recur throw try monitor-enter " +
        "monitor-exit macroexpand macroexpand-1 for dosync and or " +
        "when when-not when-let comp juxt partial sequence memoize constantly complement identity assert " +
        "peek pop doto proxy first rest cons cast coll last butlast " +
        "sigs reify second ffirst fnext nfirst nnext meta with-meta ns in-ns create-ns import " +
        "refer keys select-keys vals key val rseq name namespace promise into transient persistent! conj! " +
        "assoc! dissoc! pop! disj! use class type num float double short byte boolean bigint biginteger " +
        "bigdec print-method print-dup throw-if printf format load compile get-in update-in pr pr-on newline " +
        "flush read slurp read-line subvec with-open memfn time re-find re-groups rand-int rand mod locking " +
        "assert-valid-fdecl alias resolve ref deref refset swap! reset! set-validator! compare-and-set! alter-meta! " +
        "reset-meta! commute get-validator alter ref-set ref-history-count ref-min-history ref-max-history ensure sync io! " +
        "new next conj set! to-array future future-call into-array aset gen-class reduce map filter find empty " +
        "hash-map hash-set sorted-map sorted-map-by sorted-set sorted-set-by vec vector seq flatten reverse assoc dissoc list " +
        "disj get union difference intersection extend extend-type extend-protocol int nth delay count concat chunk chunk-buffer " +
        "chunk-append chunk-first chunk-rest max min dec unchecked-inc-int unchecked-inc unchecked-dec-inc unchecked-dec unchecked-negate " +
        "unchecked-add-int unchecked-add unchecked-subtract-int unchecked-subtract chunk-next chunk-cons chunked-seq? prn vary-meta " +
        "lazy-seq spread list* str find-keyword keyword symbol gensym force rationalize";

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var symbol = new Mode { Begin = SymbolRe };

        var number = new Mode
        {
            Scope = "number",
            Variants =
            [
                new Mode { Match = "[-+]?0[xX][0-9a-fA-F]+N?" }, // hexadecimal: 0x2a
                new Mode { Match = "[-+]?0[0-7]+N?" }, // octal: 052
                new Mode { Match = "[-+]?[1-9][0-9]?[rR][0-9a-zA-Z]+N?" }, // variable radix from 2 to 36: 2r101010, 8r52, 36r16
                new Mode { Match = @"[-+]?[0-9]+\/[0-9]+N?" }, // ratio: 1/2
                new Mode { Match = @"[-+]?[0-9]+((\.[0-9]*([eE][+-]?[0-9]+)?M?)|([eE][+-]?[0-9]+M?|M))" }, // float: 0.42 4.2E-1M 42E1 42M
                new Mode { Match = "[-+]?([1-9][0-9]*|0)N?" }, // int (don't match leading 0): 42 42N
            ],
        };

        var character = new Mode
        {
            Scope = "character",
            Variants =
            [
                new Mode { Match = @"\\o[0-3]?[0-7]{1,2}" }, // Unicode Octal 0 - 377
                new Mode { Match = @"\\u[0-9a-fA-F]{4}" }, // Unicode Hex 0000 - FFFF
                new Mode { Match = @"\\(newline|space|tab|formfeed|backspace|return)" }, // special characters
                new Mode { Match = @"\\\S" }, // any non-whitespace char
            ],
        };

        var regex = new Mode
        {
            Scope = "regex",
            Begin = "#\"",
            End = "\"",
            Contains = [CommonModes.BackslashEscape],
        };

        var @string = new Mode(CommonModes.QuoteStringMode) { Illegal = null };

        var comma = new Mode
        {
            Scope = "punctuation",
            Match = ",",
        };

        var comment = CommonModes.Comment(";", "$");

        var literal = new Mode
        {
            Scope = "literal",
            Begin = @"\b(true|false|nil)\b",
        };

        var collection = new Mode
        {
            Begin = @"\[|(#::?" + SymbolRe + @")?\{",
            End = @"[\]\}]",
        };

        var key = new Mode
        {
            Scope = "symbol",
            Begin = "[:]{1,2}" + SymbolRe,
        };

        var list = new Mode
        {
            Begin = @"\(",
            End = @"\)",
        };

        var body = new Mode
        {
            EndsWithParent = true,
        };

        var name = new Mode
        {
            Scope = "name",
            Begin = SymbolRe,
            KeywordPattern = SymbolRe,
            Keywords = Engine.Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["built_in"] = BuiltIns,
            }),
            Starts = body,
        };

        Mode[] defaultContains =
        [
            comma,
            list,
            character,
            regex,
            @string,
            comment,
            key,
            collection,
            number,
            literal,
            symbol,
        ];

        var global = new Mode
        {
            BeginKeywords = Globals.Split(' '),
            KeywordPattern = SymbolRe,
            Keywords = Engine.Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["keyword"] = Globals,
            }),
            End = @"(\[|#|\d|""|:|\{|\)|\(|$)",
            Contains =
            [
                new Mode
                {
                    Scope = "title",
                    // Not in highlight.js: a type hint (`(defn ^String name [])`) is not the title.
                    Begin = @"(?<!\^)" + SymbolRe,
                    ExcludeEnd = true,
                    // we can only have a single title
                    EndsParent = true,
                },
                .. defaultContains,
            ],
        };

        list.Contains = [global, name, body];
        body.Contains = defaultContains;
        collection.Contains = defaultContains;

        return new Mode
        {
            Illegal = @"\S",
            Contains =
            [
                comma,
                list,
                character,
                regex,
                @string,
                comment,
                key,
                collection,
                number,
                literal,
            ],
        };
    }
}
