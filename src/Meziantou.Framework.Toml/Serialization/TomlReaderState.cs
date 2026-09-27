using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Parsing;
using Meziantou.Framework.Toml.Serialization.Internal;
using Meziantou.Framework.Toml.Syntax;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Serialization;

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
