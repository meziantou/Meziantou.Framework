// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace Tomlyn.Parsing;

/// <summary>
/// Configures the behavior of <see cref="TomlParser"/>.
/// </summary>
public sealed record TomlParserOptions
{
    /// <summary>
    /// Gets or sets how the parser handles syntax errors.
    /// </summary>
    public TomlParserMode Mode { get; init; } = TomlParserMode.Strict;

    /// <summary>
    /// Gets or sets a value indicating whether the parser eagerly decodes scalar string literals.
    /// When <c>false</c> (default), string values are decoded on demand (for example via <see cref="TomlParser.GetString"/>).
    /// </summary>
    public bool DecodeScalars { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the parser should eagerly materialize simple string values
    /// (single-line basic strings without escape sequences) even when <see cref="DecodeScalars"/> is <c>false</c>.
    /// </summary>
    /// <remarks>
    /// This option can improve performance for deserialization scenarios that always consume string values
    /// (for example, via <see cref="Tomlyn.Serialization.TomlReader.GetString"/>), while still avoiding eager
    /// decoding for complex strings that contain escape sequences.
    /// </remarks>
    public bool EagerStringValues { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the parser captures comment trivia.
    /// </summary>
    /// <remarks>
    /// When enabled, the parser records comment trivia around property names and scalar values so that higher-level
    /// layers can populate <see cref="Tomlyn.Serialization.ITomlMetadataStore"/> instances.
    /// </remarks>
    public bool CaptureTrivia { get; init; }
}
