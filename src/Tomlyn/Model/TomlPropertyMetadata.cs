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
/// Metadata describing a single TOML property.
/// </summary>
public class TomlPropertyMetadata
{
    /// <summary>
    /// Gets the leading trivia attached to this node. Might be null if no leading trivias.
    /// </summary>
#pragma warning disable CA1002 // List<T> is part of the Tomlyn public API
    public List<TomlSyntaxTriviaMetadata>? LeadingTrivia { get; set; }
#pragma warning restore CA1002

    /// <summary>
    /// Gets or sets the preferred display kind for this property.
    /// </summary>
    public TomlPropertyDisplayKind DisplayKind { get; set; }

    /// <summary>
    /// Gets or sets the preferred array-of-tables style for this property.
    /// </summary>
    public TomlTableArrayStyle? TableArrayStyle { get; set; }

    /// <summary>
    /// Gets or sets the preferred inline table policy for this property.
    /// </summary>
    public TomlInlineTablePolicy? InlineTablePolicy { get; set; }

    /// <summary>
    /// Gets or sets the preferred string style for this property.
    /// </summary>
    public TomlStringStyle? StringStyle { get; set; }

    /// <summary>
    /// Gets or sets whether literal strings should be preferred when no escaping is required.
    /// </summary>
    public bool? PreferLiteralWhenNoEscapes { get; set; }

    /// <summary>
    /// Gets or sets whether hexadecimal escapes may be emitted for control characters.
    /// </summary>
    public bool? AllowHexEscapes { get; set; }

    /// <summary>
    /// Gets the trailing trivia attached to this node. Might be null if no trailing trivias.
    /// </summary>
#pragma warning disable CA1002 // List<T> is part of the Tomlyn public API
    public List<TomlSyntaxTriviaMetadata>? TrailingTrivia { get; set; }
#pragma warning restore CA1002

    /// <summary>
    /// Gets the trailing trivia attached to this node. Might be null if no trailing trivias.
    /// </summary>
#pragma warning disable CA1002 // List<T> is part of the Tomlyn public API
    public List<TomlSyntaxTriviaMetadata>? TrailingTriviaAfterEndOfLine { get; set; }
#pragma warning restore CA1002

    /// <summary>
    /// Gets or sets the source span for the property.
    /// </summary>
    public SourceSpan Span {get; set;}

    internal void MergeFormattingFrom(TomlPropertyMetadata metadata)
    {
        if (metadata.DisplayKind != TomlPropertyDisplayKind.Default)
        {
            DisplayKind = metadata.DisplayKind;
        }

        TableArrayStyle = metadata.TableArrayStyle ?? TableArrayStyle;
        InlineTablePolicy = metadata.InlineTablePolicy ?? InlineTablePolicy;
        StringStyle = metadata.StringStyle ?? StringStyle;
        PreferLiteralWhenNoEscapes = metadata.PreferLiteralWhenNoEscapes ?? PreferLiteralWhenNoEscapes;
        AllowHexEscapes = metadata.AllowHexEscapes ?? AllowHexEscapes;
    }
}
