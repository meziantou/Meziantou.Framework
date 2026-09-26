// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace Tomlyn.Parsing;

/// <summary>
/// Controls whether the lexer reads key-oriented or value-oriented tokens.
/// </summary>
public enum TomlLexerMode
{
    /// <summary>Key context.</summary>
    Key = 0,

    /// <summary>Value context.</summary>
    Value = 1,
}
