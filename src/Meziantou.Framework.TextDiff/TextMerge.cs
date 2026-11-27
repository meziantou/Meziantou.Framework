namespace Meziantou.Framework;

/// <summary>Merges two texts that were both changed from a common base, as <c>diff3</c> and <c>git merge</c> do.</summary>
public static class TextMerge
{
    /// <summary>Merges the changes made to <paramref name="baseText"/> in <paramref name="oursText"/> and in <paramref name="theirsText"/>.</summary>
    /// <param name="baseText">The common ancestor of both texts.</param>
    /// <param name="oursText">The first changed version.</param>
    /// <param name="theirsText">The second changed version.</param>
    /// <param name="options">The diff options, and how to write the conflicts.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The regions of the merge and the merged text.</returns>
    /// <remarks>
    /// Both versions are compared to the base. A region changed in only one of them takes that change; a region changed
    /// the same way in both takes it once; a region changed differently in both is a conflict. Changes that touch the same
    /// place, such as two insertions after the same line, are a conflict even when they do not overlap.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="baseText"/>, <paramref name="oursText"/> or
    /// <paramref name="theirsText"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option of <paramref name="options"/> is out of range.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
    public static TextMergeResult Merge(string baseText, string oursText, string theirsText, TextMergeOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseText);
        ArgumentNullException.ThrowIfNull(oursText);
        ArgumentNullException.ThrowIfNull(theirsText);

        options ??= new TextMergeOptions();
        if (!Enum.IsDefined(options.ConflictStyle))
            throw new ArgumentOutOfRangeException(nameof(options), options.ConflictStyle, $"'{nameof(TextMergeOptions.ConflictStyle)}' is not a valid {nameof(TextMergeConflictStyle)} value.");

        var diffOptions = TextDiff.ValidateAndGetOptions(options.DiffOptions);
        var comparer = TextDiff.CreateComparer(diffOptions);
        var budget = DiffBudget.Create(diffOptions, cancellationToken);

        // The base is chunked once for both diffs
        var baseChunks = TextDiff.Chunk(TextDiff.Normalize(baseText, diffOptions), diffOptions);
        var oursChunks = TextDiff.Chunk(TextDiff.Normalize(oursText, diffOptions), diffOptions);
        var oursDiff = DiffAlgorithmDispatcher.Compute(diffOptions.Algorithm, baseChunks, oursChunks, comparer, budget);
        var theirsChunks = TextDiff.Chunk(TextDiff.Normalize(theirsText, diffOptions), diffOptions);
        var theirsDiff = DiffAlgorithmDispatcher.Compute(diffOptions.Algorithm, baseChunks, theirsChunks, comparer, budget);

        // Chunks that the ordinal comparer considers equal are the same text, so the sides of a region that are equal
        // can share one string instead of building a copy of the region per side.
        var equalChunksAreIdentical = ReferenceEquals(comparer, StringComparer.Ordinal);

        var oursMatches = GetMatches(oursDiff);
        var theirsMatches = GetMatches(theirsDiff);

        var chunks = new List<TextMergeChunk>();
        var hasConflicts = false;
        var baseIndex = 0;
        var oursIndex = 0;
        var theirsIndex = 0;
        while (true)
        {
            // The chunks of the base that both sides keep, in place
            var baseStart = baseIndex;
            var oursStart = oursIndex;
            var theirsStart = theirsIndex;
            while (baseIndex < baseChunks.Length && oursMatches[baseIndex] == oursIndex && theirsMatches[baseIndex] == theirsIndex)
            {
                baseIndex++;
                oursIndex++;
                theirsIndex++;
            }

            if (baseIndex > baseStart)
            {
                AddChunk(chunks, TextMergeChunkKind.Unchanged, equalChunksAreIdentical, baseChunks, baseStart, baseIndex, oursChunks, oursStart, oursIndex, theirsChunks, theirsStart, theirsIndex);
            }

            if (baseIndex >= baseChunks.Length && oursIndex >= oursChunks.Length && theirsIndex >= theirsChunks.Length)
                break;

            // The next chunk of the base that both sides keep ends the changed region
            var baseEnd = baseIndex;
            while (baseEnd < baseChunks.Length && (oursMatches[baseEnd] < 0 || theirsMatches[baseEnd] < 0))
            {
                baseEnd++;
            }

            var oursEnd = baseEnd < baseChunks.Length ? oursMatches[baseEnd] : oursChunks.Length;
            var theirsEnd = baseEnd < baseChunks.Length ? theirsMatches[baseEnd] : theirsChunks.Length;

            var oursUnchanged = RangeEquals(baseChunks, baseIndex, baseEnd, oursChunks, oursIndex, oursEnd, comparer);
            var theirsUnchanged = RangeEquals(baseChunks, baseIndex, baseEnd, theirsChunks, theirsIndex, theirsEnd, comparer);
            var kind = (oursUnchanged, theirsUnchanged) switch
            {
                (true, true) => TextMergeChunkKind.Unchanged,
                (true, false) => TextMergeChunkKind.ChangedInTheirs,
                (false, true) => TextMergeChunkKind.ChangedInOurs,
                _ when RangeEquals(oursChunks, oursIndex, oursEnd, theirsChunks, theirsIndex, theirsEnd, comparer) => TextMergeChunkKind.ChangedInBoth,
                _ => TextMergeChunkKind.Conflict,
            };

            hasConflicts |= kind is TextMergeChunkKind.Conflict;
            AddChunk(chunks, kind, equalChunksAreIdentical, baseChunks, baseIndex, baseEnd, oursChunks, oursIndex, oursEnd, theirsChunks, theirsIndex, theirsEnd);
            baseIndex = baseEnd;
            oursIndex = oursEnd;
            theirsIndex = theirsEnd;
        }

        return new TextMergeResult(chunks, hasConflicts, WriteText(chunks, options));
    }

    /// <summary>Returns, for each chunk of the base, the index of the chunk it matches in the other text, or -1.</summary>
    private static int[] GetMatches(DiffComputationResult diff)
    {
        var leftModified = diff.LeftModified;
        var rightModified = diff.RightModified;
        var matches = new int[leftModified.Length];
        var left = 0;
        var right = 0;
        while (left < leftModified.Length)
        {
            if (leftModified[left])
            {
                matches[left++] = -1;
            }
            else if (right < rightModified.Length && rightModified[right])
            {
                right++;
            }
            else if (right < rightModified.Length)
            {
                matches[left++] = right++;
            }
            else
            {
                matches[left++] = -1;
            }
        }

        return matches;
    }

    private static bool RangeEquals(string[] left, int leftStart, int leftEnd, string[] right, int rightStart, int rightEnd, IEqualityComparer<string> comparer)
    {
        if (leftEnd - leftStart != rightEnd - rightStart)
            return false;

        for (var i = 0; i < leftEnd - leftStart; i++)
        {
            if (!comparer.Equals(left[leftStart + i], right[rightStart + i]))
                return false;
        }

        return true;
    }

    private static void AddChunk(List<TextMergeChunk> chunks, TextMergeChunkKind kind, bool equalChunksAreIdentical, string[] baseChunks, int baseStart, int baseEnd, string[] oursChunks, int oursStart, int oursEnd, string[] theirsChunks, int theirsStart, int theirsEnd)
    {
        var baseText = string.Concat(baseChunks.AsSpan(baseStart, baseEnd - baseStart));
        var oursText = equalChunksAreIdentical && kind is TextMergeChunkKind.Unchanged or TextMergeChunkKind.ChangedInTheirs
            ? baseText
            : string.Concat(oursChunks.AsSpan(oursStart, oursEnd - oursStart));
        var theirsText = equalChunksAreIdentical
            ? kind switch
            {
                TextMergeChunkKind.Unchanged or TextMergeChunkKind.ChangedInOurs => baseText,
                TextMergeChunkKind.ChangedInBoth => oursText,
                _ => string.Concat(theirsChunks.AsSpan(theirsStart, theirsEnd - theirsStart)),
            }
            : string.Concat(theirsChunks.AsSpan(theirsStart, theirsEnd - theirsStart));

        // Two adjacent regions of the same kind are one region, except conflicts, which are resolved one by one
        if (kind is not TextMergeChunkKind.Conflict && chunks.Count > 0 && chunks[^1].Kind == kind)
        {
            var previous = chunks[^1];
            chunks[^1] = new TextMergeChunk(kind, previous.BaseText + baseText, previous.OursText + oursText, previous.TheirsText + theirsText);
            return;
        }

        chunks.Add(new TextMergeChunk(kind, baseText, oursText, theirsText));
    }

    private static string WriteText(List<TextMergeChunk> chunks, TextMergeOptions options)
    {
        // Measure the text first, so it is written once, in a string of the right length
        var measure = new MergedTextWriter(destination: default);
        Write(ref measure, chunks, options);

        // The exception StringBuilder throws past its maximum capacity
        if (measure.Length > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(chunks), "The merged text is longer than the maximum length of a string.");

        return string.Create((int)measure.Length, (Chunks: chunks, Options: options), static (destination, state) =>
        {
            var writer = new MergedTextWriter(destination);
            Write(ref writer, state.Chunks, state.Options);
        });

        static void Write(ref MergedTextWriter output, List<TextMergeChunk> chunks, TextMergeOptions options)
        {
            foreach (var chunk in chunks)
            {
                if (chunk.Text is { } text)
                {
                    output.Append(text);
                    continue;
                }

                AppendMarker(ref output, '<', options.OursLabel);
                AppendSection(ref output, chunk.OursText);
                if (options.ConflictStyle is TextMergeConflictStyle.Diff3)
                {
                    AppendMarker(ref output, '|', options.BaseLabel);
                    AppendSection(ref output, chunk.BaseText);
                }

                AppendMarker(ref output, '=', label: null);
                AppendSection(ref output, chunk.TheirsText);
                AppendMarker(ref output, '>', options.TheirsLabel);
            }
        }

        static void AppendMarker(ref MergedTextWriter output, char marker, string? label)
        {
            if (output.Length > 0 && output.LastChar != '\n')
            {
                output.Append("\n");
            }

            output.Append(marker switch
            {
                '<' => "<<<<<<<",
                '|' => "|||||||",
                '=' => "=======",
                _ => ">>>>>>>",
            });
            if (!string.IsNullOrEmpty(label))
            {
                output.Append(" ");
                output.Append(label);
            }

            output.Append("\n");
        }

        static void AppendSection(ref MergedTextWriter output, string text)
        {
            output.Append(text);
            if (text.Length > 0 && text[^1] != '\n')
            {
                output.Append("\n");
            }
        }
    }

    /// <summary>Writes text to a span, or only measures it when the span is empty.</summary>
    private ref struct MergedTextWriter(Span<char> destination)
    {
        private readonly Span<char> _destination = destination;

        public long Length { get; private set; }

        public char LastChar { get; private set; }

        public void Append(ReadOnlySpan<char> value)
        {
            if (value.IsEmpty)
                return;

            if (!_destination.IsEmpty)
            {
                value.CopyTo(_destination[(int)Length..]);
            }

            Length += value.Length;
            LastChar = value[^1];
        }
    }
}
