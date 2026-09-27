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
    // The value is tokens[start..(start + count)]; the tokens can be shared with other buffers, and so can the index of the
    // container ends, which covers at least the tokens of the value
    public TomlReaderBuffer(TomlReaderToken[] tokens, int start, int count, TomlSerializerOptions options, TomlSerializationOperationState operationState, int[]? containerEnds = null)
    {
        Tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        Start = start;
        Count = count;
        Options = options ?? TomlSerializerOptions.Default;
        OperationState = operationState ?? throw new ArgumentNullException(nameof(operationState));
        ContainerEnds = containerEnds ?? CreateContainerEnds(tokens, start, count);
    }

    public TomlReaderToken[] Tokens { get; }

    // The index of the end token of each StartTable/StartArray token, so that nested values are skipped or captured without
    // scanning them: a polymorphic value is scanned again at every nesting level otherwise. A container without an end ends
    // at the last token.
    public int[] ContainerEnds { get; }

    public int Start { get; }

    public int Count { get; }

    public ReadOnlySpan<TomlReaderToken> Span => Tokens.AsSpan(Start, Count);

    public TomlSerializerOptions Options { get; }

    public TomlSerializationOperationState OperationState { get; }

    private static int[] CreateContainerEnds(TomlReaderToken[] tokens, int start, int count)
    {
        var ends = new int[tokens.Length];
        var openContainers = new Stack<int>();
        for (var i = start; i < start + count; i++)
        {
            switch (tokens[i].TokenType)
            {
                case TomlTokenType.StartTable or TomlTokenType.StartArray:
                    openContainers.Push(i);
                    break;
                case TomlTokenType.EndTable or TomlTokenType.EndArray when openContainers.Count > 0:
                    ends[openContainers.Pop()] = i;
                    break;
            }
        }

        while (openContainers.Count > 0)
        {
            ends[openContainers.Pop()] = start + count - 1;
        }

        return ends;
    }
}
