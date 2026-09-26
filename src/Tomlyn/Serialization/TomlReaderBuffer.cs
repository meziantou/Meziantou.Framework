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
