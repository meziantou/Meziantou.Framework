// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Tomlyn.Helpers;
using Tomlyn.Model;
using Tomlyn.Parsing;
using Tomlyn.Serialization.Internal;
using Tomlyn.Syntax;
using Tomlyn.Text;

namespace Tomlyn.Serialization;

internal readonly struct TomlReaderToken
{
    public TomlReaderToken(
        TomlTokenType tokenType,
        TomlSourceSpan? span,
        string? rawText,
        TomlSyntaxTriviaMetadata[]? leadingTrivia,
        TomlSyntaxTriviaMetadata[]? trailingTrivia,
        string? propertyName,
        string? stringValue,
        ulong data,
        TokenKind stringTokenKind,
        TomlDateTime dateTime,
        bool hasDateTime)
    {
        TokenType = tokenType;
        Span = span;
        RawText = rawText;
        LeadingTrivia = leadingTrivia;
        TrailingTrivia = trailingTrivia;
        PropertyName = propertyName;
        StringValue = stringValue;
        Data = data;
        StringTokenKind = stringTokenKind;
        DateTime = dateTime;
        HasDateTime = hasDateTime;
    }

    public TomlTokenType TokenType { get; }
    public TomlSourceSpan? Span { get; }
    public string? RawText { get; }
    public TomlSyntaxTriviaMetadata[]? LeadingTrivia { get; }
    public TomlSyntaxTriviaMetadata[]? TrailingTrivia { get; }
    public string? PropertyName { get; }
    public string? StringValue { get; }
    public ulong Data { get; }
    public TokenKind StringTokenKind { get; }
    public TomlDateTime DateTime { get; }
    public bool HasDateTime { get; }
}
