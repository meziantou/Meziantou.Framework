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
    public TomlReaderBuffer(TomlReaderToken[] tokens, TomlSerializerOptions options, TomlSerializationOperationState operationState)
    {
        Tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        Options = options ?? TomlSerializerOptions.Default;
        OperationState = operationState ?? throw new ArgumentNullException(nameof(operationState));
    }

    public TomlReaderToken[] Tokens { get; }

    public TomlSerializerOptions Options { get; }

    public TomlSerializationOperationState OperationState { get; }
}
