// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.IO;
using Tomlyn;
using Tomlyn.Syntax;
using Tomlyn.Text;

namespace Tomlyn.Parsing;

/// <summary>
/// Provides a full-fidelity TOML syntax parser producing a round-trippable <see cref="DocumentSyntax"/>.
/// </summary>
public static class SyntaxParser
{
    /// <summary>
    /// Parses a TOML payload into a syntax tree.
    /// </summary>
    /// <param name="lexer">A TOML lexer positioned before the first token.</param>
    /// <param name="validate">When <c>true</c>, runs semantic validation after parsing.</param>
    /// <returns>The parsed syntax tree, possibly containing diagnostics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lexer"/> is <c>null</c>.</exception>
    public static DocumentSyntax Parse(TomlLexer lexer, bool validate = true)
        => Parse(lexer, TomlSerializerOptions.Default, validate);

    /// <summary>
    /// Parses a TOML payload into a syntax tree.
    /// </summary>
    /// <param name="lexer">A TOML lexer positioned before the first token.</param>
    /// <param name="options">Serializer options supplying shared behaviors such as <see cref="TomlSerializerOptions.MaxDepth"/>.</param>
    /// <param name="validate">When <c>true</c>, runs semantic validation after parsing.</param>
    /// <returns>The parsed syntax tree, possibly containing diagnostics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lexer"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
    public static DocumentSyntax Parse(TomlLexer lexer, TomlSerializerOptions options, bool validate = true)
    {
        ArgumentNullException.ThrowIfNull(lexer);

        ArgumentNullException.ThrowIfNull(options);

        var parser = new Parser(lexer.InternalLexer, options);
        var doc = parser.Run();
        if (validate && !doc.HasErrors)
        {
            var validator = new SyntaxValidator(doc.Diagnostics);
            validator.Visit(doc);
        }

        return doc;
    }

    /// <summary>
    /// Parses a TOML payload into a syntax tree.
    /// </summary>
    /// <param name="toml">The TOML payload.</param>
    /// <param name="sourceName">An optional source name used in diagnostics.</param>
    /// <param name="validate">When <c>true</c>, runs semantic validation after parsing.</param>
    /// <returns>The parsed syntax tree, possibly containing diagnostics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="toml"/> is <c>null</c>.</exception>
    public static DocumentSyntax Parse(string toml, string? sourceName = null, bool validate = true)
        => Parse(toml, TomlSerializerOptions.Default, sourceName, validate);

    /// <summary>
    /// Parses a TOML payload into a syntax tree.
    /// </summary>
    /// <param name="toml">The TOML payload.</param>
    /// <param name="options">Serializer options supplying shared behaviors such as <see cref="TomlSerializerOptions.MaxDepth"/>.</param>
    /// <param name="sourceName">An optional source name used in diagnostics.</param>
    /// <param name="validate">When <c>true</c>, runs semantic validation after parsing.</param>
    /// <returns>The parsed syntax tree, possibly containing diagnostics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="toml"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
    public static DocumentSyntax Parse(string toml, TomlSerializerOptions options, string? sourceName = null, bool validate = true)
    {
        ArgumentNullException.ThrowIfNull(toml);

        ArgumentNullException.ThrowIfNull(options);

        var lexer = TomlLexer.Create(toml, new TomlLexerOptions { DecodeScalars = true }, sourceName);
        return Parse(lexer, options, validate);
    }

    /// <summary>
    /// Parses a TOML payload from a <see cref="TextReader"/> into a syntax tree.
    /// </summary>
    /// <param name="reader">The text reader.</param>
    /// <param name="sourceName">An optional source name used in diagnostics.</param>
    /// <param name="validate">When <c>true</c>, runs semantic validation after parsing.</param>
    /// <returns>The parsed syntax tree, possibly containing diagnostics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <c>null</c>.</exception>
    public static DocumentSyntax Parse(TextReader reader, string? sourceName = null, bool validate = true)
        => Parse(reader, TomlSerializerOptions.Default, sourceName, validate);

    /// <summary>
    /// Parses a TOML payload from a <see cref="TextReader"/> into a syntax tree.
    /// </summary>
    /// <param name="reader">The text reader.</param>
    /// <param name="options">Serializer options supplying shared behaviors such as <see cref="TomlSerializerOptions.MaxDepth"/>.</param>
    /// <param name="sourceName">An optional source name used in diagnostics.</param>
    /// <param name="validate">When <c>true</c>, runs semantic validation after parsing.</param>
    /// <returns>The parsed syntax tree, possibly containing diagnostics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
    public static DocumentSyntax Parse(TextReader reader, TomlSerializerOptions options, string? sourceName = null, bool validate = true)
    {
        ArgumentNullException.ThrowIfNull(reader);

        ArgumentNullException.ThrowIfNull(options);

        return Parse(reader.ReadToEnd(), options, sourceName, validate);
    }

    /// <summary>
    /// Parses a TOML payload into a syntax tree and throws on any parse/validation error.
    /// </summary>
    /// <param name="toml">The TOML payload.</param>
    /// <param name="sourceName">An optional source name used in diagnostics.</param>
    /// <param name="validate">When <c>true</c>, runs semantic validation after parsing.</param>
    /// <returns>The parsed syntax tree.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="toml"/> is <c>null</c>.</exception>
    /// <exception cref="TomlException">The TOML payload is invalid.</exception>
    public static DocumentSyntax ParseStrict(string toml, string? sourceName = null, bool validate = true)
        => ParseStrict(toml, TomlSerializerOptions.Default, sourceName, validate);

    /// <summary>
    /// Parses a TOML payload into a syntax tree and throws on any parse/validation error.
    /// </summary>
    /// <param name="toml">The TOML payload.</param>
    /// <param name="options">Serializer options supplying shared behaviors such as <see cref="TomlSerializerOptions.MaxDepth"/>.</param>
    /// <param name="sourceName">An optional source name used in diagnostics.</param>
    /// <param name="validate">When <c>true</c>, runs semantic validation after parsing.</param>
    /// <returns>The parsed syntax tree.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="toml"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
    /// <exception cref="TomlException">The TOML payload is invalid.</exception>
    public static DocumentSyntax ParseStrict(string toml, TomlSerializerOptions options, string? sourceName = null, bool validate = true)
    {
        var doc = Parse(toml, options, sourceName, validate);
        if (doc.HasErrors)
        {
            throw new TomlException(doc.Diagnostics);
        }

        return doc;
    }

    /// <summary>
    /// Parses a TOML payload into a syntax tree and throws on any parse/validation error.
    /// </summary>
    /// <param name="lexer">A TOML lexer positioned before the first token.</param>
    /// <param name="validate">When <c>true</c>, runs semantic validation after parsing.</param>
    /// <returns>The parsed syntax tree.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lexer"/> is <c>null</c>.</exception>
    /// <exception cref="TomlException">The TOML payload is invalid.</exception>
    public static DocumentSyntax ParseStrict(TomlLexer lexer, bool validate = true)
        => ParseStrict(lexer, TomlSerializerOptions.Default, validate);

    /// <summary>
    /// Parses a TOML payload into a syntax tree and throws on any parse/validation error.
    /// </summary>
    /// <param name="lexer">A TOML lexer positioned before the first token.</param>
    /// <param name="options">Serializer options supplying shared behaviors such as <see cref="TomlSerializerOptions.MaxDepth"/>.</param>
    /// <param name="validate">When <c>true</c>, runs semantic validation after parsing.</param>
    /// <returns>The parsed syntax tree.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lexer"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
    /// <exception cref="TomlException">The TOML payload is invalid.</exception>
    public static DocumentSyntax ParseStrict(TomlLexer lexer, TomlSerializerOptions options, bool validate = true)
    {
        var doc = Parse(lexer, options, validate);
        if (doc.HasErrors)
        {
            throw new TomlException(doc.Diagnostics);
        }

        return doc;
    }
}
