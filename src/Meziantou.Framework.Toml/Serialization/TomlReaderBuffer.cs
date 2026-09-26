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

internal sealed class TomlReaderBuffer
{
    // The value is tokens[start..(start + count)]; the tokens can be shared with other buffers
    public TomlReaderBuffer(TomlReaderToken[] tokens, int start, int count, TomlSerializerOptions options, TomlSerializationOperationState operationState)
    {
        Tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        Start = start;
        Count = count;
        Options = options ?? TomlSerializerOptions.Default;
        OperationState = operationState ?? throw new ArgumentNullException(nameof(operationState));
    }

    public TomlReaderToken[] Tokens { get; }

    public int Start { get; }

    public int Count { get; }

    public ReadOnlySpan<TomlReaderToken> Span => Tokens.AsSpan(Start, Count);

    public TomlSerializerOptions Options { get; }

    public TomlSerializationOperationState OperationState { get; }
}
