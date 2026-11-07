using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Language.Fuzz;

/// <summary>
/// The seeds that fail on library bugs the fuzzer found and that are not fixed yet. Every other seed must pass
/// (Meziantou.Framework.Fuzzing.Tests); remove the entries of a bug once it is fixed.
/// </summary>
internal static class KnownIssueSeeds
{
    private static readonly HashSet<(string Target, string Seed)> Excluded =
    [
        // Formatter.Format lets the ArgumentException of XmlWriter escape for the reserved "xmlns" element prefix
        ("language-xml-format", "document-11"),
        // GreenNode.GetFirstTerminal and GetLastTerminal stop at an empty node: token navigation and node spans are wrong
        ("language-json", "document-6"),
        ("language-json-incremental", "edit-6"),
        ("language-regex", "net-invalid-21"),
        ("language-regex", "net-invalid-63"),
        ("language-regex", "net-invalid-64"),
        ("language-regex", "net-valid-5"),
        ("language-regex", "net-valid-6"),
        ("language-regex", "pcre-135"),
        ("language-regex", "pcre-2916"),
        ("language-regex", "posix-1316"),
        ("language-regex", "posix-1344"),
        ("language-regex-net", "net-invalid-21-0"),
        ("language-regex-net", "net-invalid-21-1"),
        ("language-regex-net", "net-invalid-21-2"),
        ("language-regex-net", "net-invalid-21-3"),
        ("language-regex-net", "net-invalid-63-0"),
        ("language-regex-net", "net-invalid-63-1"),
        ("language-regex-net", "net-invalid-63-2"),
        ("language-regex-net", "net-invalid-63-3"),
        ("language-regex-net", "net-invalid-64-0"),
        ("language-regex-net", "net-invalid-64-1"),
        ("language-regex-net", "net-invalid-64-2"),
        ("language-regex-net", "net-invalid-64-3"),
        ("language-regex-net", "net-valid-5-0"),
        ("language-regex-net", "net-valid-5-1"),
        ("language-regex-net", "net-valid-5-2"),
        ("language-regex-net", "net-valid-5-3"),
        ("language-regex-net", "net-valid-6-0"),
        ("language-regex-net", "net-valid-6-1"),
        ("language-regex-net", "net-valid-6-2"),
        ("language-regex-net", "net-valid-6-3"),
        ("language-toml", "invalid/control/bare-formfeed"),
        ("language-toml", "invalid/control/bare-vertical-tab"),
        ("language-toml", "invalid/control/rawstring-cr"),
        ("language-toml", "invalid/control/string-cr"),
        ("language-toml", "invalid/encoding/bom-not-at-start-01"),
        ("language-toml", "invalid/encoding/utf16-bom"),
        ("language-toml", "invalid/encoding/utf16-comment"),
        ("language-toml", "invalid/encoding/utf16-key"),
        ("language-toml", "invalid/key/end-in-escape"),
        ("language-toml", "invalid/key/hash"),
        ("language-toml", "invalid/key/newline-01"),
        ("language-toml", "invalid/key/newline-02"),
        ("language-toml", "invalid/key/newline-03"),
        ("language-toml", "invalid/key/newline-06"),
        ("language-toml", "invalid/key/only-float"),
        ("language-toml", "invalid/key/only-int"),
        ("language-toml", "invalid/key/only-str"),
        ("language-toml", "invalid/key/quoted-unclosed-01"),
        ("language-toml", "invalid/key/quoted-unclosed-02"),
        ("language-toml", "invalid/key/space"),
        ("language-toml", "invalid/key/without-value-01"),
        ("language-toml", "invalid/key/without-value-02"),
        ("language-toml", "invalid/key/without-value-03"),
        ("language-toml", "invalid/key/without-value-04"),
        ("language-toml", "invalid/key/without-value-05"),
        ("language-toml", "invalid/key/without-value-06"),
        ("language-toml", "invalid/key/without-value-07"),
        ("language-toml", "invalid/spec-1.0.0/key-value-pair-1"),
        ("language-toml", "invalid/spec-1.1.0/common-2"),
        ("language-toml", "invalid/string/bad-multiline"),
        ("language-toml", "invalid/string/no-close-09"),
        ("language-toml", "invalid/string/no-close-10"),
        ("language-toml", "invalid/table/newline-02"),
    ];

    public static FuzzTarget ExcludeKnownIssues(FuzzTarget target)
    {
        return new FuzzTarget(target.Name, target.Action)
        {
            Seeds = () => target.Seeds().Where(seed => !Excluded.Contains((target.Name, seed.Name))),
            Dictionary = target.Dictionary,
        };
    }
}
