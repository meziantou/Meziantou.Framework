using Meziantou.Framework.Toml.Syntax;

namespace Meziantou.Framework.Toml.Parsing;

internal static class TomlParseEventData
{
    private const int PropertyNameTokenKindBits = 8;
    private const ulong PropertyNameTokenKindMask = (1UL << PropertyNameTokenKindBits) - 1;
    private const ulong PropertyNameHashMask = 0x00FF_FFFF_FFFF_FFFFUL;

    // The data of a StartTable or StartArray event: set for an inline table or array, clear for a table opened by a header or
    // a dotted key, and for the document
    public const ulong InlineContainer = 1;

    public static bool IsInlineContainer(ulong data) => (data & InlineContainer) != 0;

    public static ulong PackPropertyName(TokenKind tokenKind, ulong hash)
        => ((hash & PropertyNameHashMask) << PropertyNameTokenKindBits) | (ulong)(byte)tokenKind;

    public static TokenKind UnpackPropertyNameTokenKind(ulong data)
        => (TokenKind)(data & PropertyNameTokenKindMask);

    public static ulong UnpackPropertyNameHash(ulong data)
        => data >> PropertyNameTokenKindBits;
}
