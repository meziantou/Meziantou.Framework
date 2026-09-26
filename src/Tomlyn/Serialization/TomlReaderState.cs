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

internal readonly struct TomlReaderState
{
    public TomlReaderState(TomlTokenType tokenType, TomlSourceSpan? span, int bufferIndex)
    {
        TokenType = tokenType;
        Span = span;
        BufferIndex = bufferIndex;
    }

    public TomlTokenType TokenType { get; }

    public TomlSourceSpan? Span { get; }

    public int BufferIndex { get; }
}
