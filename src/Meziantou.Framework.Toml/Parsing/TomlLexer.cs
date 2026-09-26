using System;
using System.Collections.Generic;
using System.IO;
using Meziantou.Framework.Toml.Syntax;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Parsing;

/// <summary>
/// Provides incremental lexical tokenization for TOML text.
/// </summary>
public sealed class TomlLexer
{
    private readonly Lexer _lexer;
    private TomlToken _current;

    private TomlLexer(Lexer lexer)
    {
        _lexer = lexer;
        _current = default;
    }

    internal Lexer InternalLexer => _lexer;

    /// <summary>
    /// Creates a lexer over TOML text.
    /// </summary>
    /// <param name="toml">The TOML payload.</param>
    /// <param name="sourceName">An optional source name used in diagnostics.</param>
    /// <returns>A lexer instance positioned before the first token.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="toml"/> is <c>null</c>.</exception>
    public static TomlLexer Create(string toml, string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(toml);

        var memory = toml.AsMemory();
        var hasByteOrderMark = !memory.IsEmpty && memory.Span[0] == '\uFEFF';
        if (hasByteOrderMark)
        {
            memory = memory.Slice(1);
        }

        var lexer = new Lexer(memory, sourceName ?? string.Empty)
        {
            DecodeScalars = false,
            HasByteOrderMark = hasByteOrderMark,
        };
        return new TomlLexer(lexer);
    }

    /// <summary>
    /// Creates a lexer over TOML text.
    /// </summary>
    /// <param name="toml">The TOML payload.</param>
    /// <param name="lexerOptions">Options controlling lexer behavior.</param>
    /// <param name="sourceName">An optional source name used in diagnostics.</param>
    /// <returns>A lexer instance positioned before the first token.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="toml"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="lexerOptions"/> is <c>null</c>.</exception>
    public static TomlLexer Create(string toml, TomlLexerOptions lexerOptions, string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(toml);

        ArgumentNullException.ThrowIfNull(lexerOptions);

        var memory = toml.AsMemory();
        var hasByteOrderMark = !memory.IsEmpty && memory.Span[0] == '\uFEFF';
        if (hasByteOrderMark)
        {
            memory = memory.Slice(1);
        }

        var lexer = new Lexer(memory, sourceName ?? string.Empty)
        {
            DecodeScalars = lexerOptions.DecodeScalars,
            HasByteOrderMark = hasByteOrderMark,
        };
        return new TomlLexer(lexer);
    }

    /// <summary>
    /// Creates a lexer over TOML text from a <see cref="TextReader"/>.
    /// </summary>
    /// <param name="reader">The text reader.</param>
    /// <param name="sourceName">An optional source name used in diagnostics.</param>
    /// <returns>A lexer instance positioned before the first token.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <c>null</c>.</exception>
    public static TomlLexer Create(TextReader reader, string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(reader);

        return Create(reader.ReadToEnd(), sourceName);
    }

    /// <summary>
    /// Creates a lexer over TOML text from a <see cref="TextReader"/>.
    /// </summary>
    /// <param name="reader">The text reader.</param>
    /// <param name="lexerOptions">Options controlling lexer behavior.</param>
    /// <param name="sourceName">An optional source name used in diagnostics.</param>
    /// <returns>A lexer instance positioned before the first token.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="lexerOptions"/> is <c>null</c>.</exception>
    public static TomlLexer Create(TextReader reader, TomlLexerOptions lexerOptions, string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(reader);

        ArgumentNullException.ThrowIfNull(lexerOptions);

        return Create(reader.ReadToEnd(), lexerOptions, sourceName);
    }

    /// <summary>
    /// Gets the current token.
    /// </summary>
    public TomlToken Current => _current;

    /// <summary>
    /// Gets the source name associated with this lexer.
    /// </summary>
    public string SourceName => _lexer.SourcePath;

    /// <summary>
    /// Gets the source span associated with the current token.
    /// </summary>
    public TomlSourceSpan CurrentSpan => new TomlSourceSpan(SourceName, _current.Start, _current.End);

    /// <summary>
    /// Gets or sets the lexer mode.
    /// </summary>
    public TomlLexerMode Mode
    {
        get => _lexer.State == LexerState.Key ? TomlLexerMode.Key : TomlLexerMode.Value;
        set => _lexer.State = value == TomlLexerMode.Key ? LexerState.Key : LexerState.Value;
    }

    /// <summary>
    /// Gets a value indicating whether lexer diagnostics include errors.
    /// </summary>
    public bool HasErrors => _lexer.HasErrors;

    /// <summary>
    /// Gets diagnostics produced by the lexer.
    /// </summary>
    public IEnumerable<DiagnosticMessage> Errors => _lexer.Errors;

    /// <summary>
    /// Advances to the next token.
    /// </summary>
    /// <returns><c>true</c> when a token is available; otherwise <c>false</c>.</returns>
    public bool MoveNext()
    {
        if (!_lexer.MoveNext())
        {
            return false;
        }

        ref readonly var token = ref _lexer.Token;
        var data = token.Kind.IsInteger() || token.Kind.IsFloat() || token.Kind is TokenKind.True or TokenKind.False
            ? token.Data
            : 0;
        _current = new TomlToken(
            token.Kind,
            new TomlTextPosition(token.Start.Offset, token.Start.Line, token.Start.Column),
            new TomlTextPosition(token.End.Offset, token.End.Line, token.End.Column),
            token.StringValue,
            data);
        return true;
    }

    /// <summary>
    /// Gets the raw source text represented by a token.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <returns>The token text, or <c>null</c> if out of range.</returns>
    public string? GetText(in TomlToken token) => _lexer.GetString(token.Start.Offset, token.Length);

    // Legacy syntax tree parser uses the internal lexer directly.
}
