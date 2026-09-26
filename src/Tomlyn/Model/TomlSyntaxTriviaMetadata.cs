// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Tomlyn;
using Tomlyn.Syntax;

namespace Tomlyn.Model;

/// <summary>
/// Serializable metadata for a trivia element.
/// </summary>
public record struct TomlSyntaxTriviaMetadata(TokenKind Kind, string? Text)
{
    /// <summary>
    /// Converts a <see cref="SyntaxTrivia"/> into metadata.
    /// </summary>
    /// <param name="trivia">The trivia to convert.</param>
    /// <returns>The metadata value.</returns>
    public static implicit operator TomlSyntaxTriviaMetadata(SyntaxTrivia trivia)
    {
        return new TomlSyntaxTriviaMetadata(trivia.Kind, trivia.Text);
    }
}
