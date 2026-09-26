// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using Tomlyn.Syntax;

namespace Tomlyn.Parsing;

internal static class TomlParseEventData
{
    private const int PropertyNameTokenKindBits = 8;
    private const ulong PropertyNameTokenKindMask = (1UL << PropertyNameTokenKindBits) - 1;
    private const ulong PropertyNameHashMask = 0x00FF_FFFF_FFFF_FFFFUL;

    public static ulong PackPropertyName(TokenKind tokenKind, ulong hash)
        => ((hash & PropertyNameHashMask) << PropertyNameTokenKindBits) | (ulong)(byte)tokenKind;

    public static TokenKind UnpackPropertyNameTokenKind(ulong data)
        => (TokenKind)(data & PropertyNameTokenKindMask);

    public static ulong UnpackPropertyNameHash(ulong data)
        => data >> PropertyNameTokenKindBits;
}
