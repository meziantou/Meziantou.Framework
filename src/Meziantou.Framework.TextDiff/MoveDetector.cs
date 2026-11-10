namespace Meziantou.Framework;

/// <summary>Pairs blocks of deleted chunks with equal blocks of inserted chunks found elsewhere in the diff.</summary>
internal static class MoveDetector
{
    // Bounds the work for a chunk that is inserted at many places, such as an empty line: the number of candidates
    // compared, and the number of candidates looked at, including the ones already part of a move
    private const int MaxCandidatesPerChunk = 64;
    private const int MaxScannedCandidatesPerChunk = 256;

    internal static void Apply(List<TextDiffEntry> entries, IEqualityComparer<string> comparer, DiffBudget budget)
    {
        var count = entries.Count;

        // Consecutive changed entries form a block; a chunk deleted and inserted in the same block did not move
        var blocks = new int[count];
        var blockEnds = new List<int>(); // The index of the last entry of each block
        var block = 0;
        for (var i = 0; i < count; i++)
        {
            if (entries[i].Operation is TextDiffOperation.Equal)
            {
                if (i > 0 && entries[i - 1].Operation is not TextDiffOperation.Equal)
                {
                    block++;
                }
            }
            else
            {
                blocks[i] = block;
                if (blockEnds.Count == block)
                {
                    blockEnds.Add(i);
                }
                else
                {
                    blockEnds[block] = i;
                }
            }
        }

        var insertions = new Dictionary<string, List<int>>(comparer);
        for (var i = 0; i < count; i++)
        {
            if (entries[i].Operation is TextDiffOperation.Insert)
            {
                var text = entries[i].NewText!;
                if (!insertions.TryGetValue(text, out var positions))
                {
                    positions = [];
                    insertions.Add(text, positions);
                }

                positions.Add(i);
            }
        }

        if (insertions.Count is 0)
            return;

        var moveIds = new int[count];
        var nextMoveId = 1; // 0 means "not moved"
        for (var i = 0; i < count; i++)
        {
            if (entries[i].Operation is not TextDiffOperation.Delete || moveIds[i] is not 0)
                continue;

            if (budget.IsExpired())
                break;

            if (!insertions.TryGetValue(entries[i].OldText!, out var candidates))
                continue;

            var bestStart = -1;
            var bestLength = 0;
            var inspected = 0;
            var scanned = 0;
            for (var c = 0; c < candidates.Count; c++)
            {
                var candidate = candidates[c];

                // The candidates are sorted, so the ones of the same block are consecutive: skip them at once
                if (blocks[candidate] == blocks[i])
                {
                    c = DiffAlgorithmHelpers.LowerBound(candidates, blockEnds[blocks[i]] + 1) - 1;
                    continue;
                }

                if (++scanned > MaxScannedCandidatesPerChunk)
                    break;

                if (moveIds[candidate] is not 0)
                    continue;

                if (++inspected > MaxCandidatesPerChunk)
                    break;

                var length = 0;
                while (IsMovable(entries, moveIds, i + length, TextDiffOperation.Delete)
                    && IsMovable(entries, moveIds, candidate + length, TextDiffOperation.Insert)
                    && comparer.Equals(entries[i + length].OldText, entries[candidate + length].NewText))
                {
                    length++;
                }

                if (length > bestLength)
                {
                    bestStart = candidate;
                    bestLength = length;
                }
            }

            if (bestLength is 0 || !HasNonWhitespace(entries, i, bestLength))
                continue;

            for (var k = 0; k < bestLength; k++)
            {
                moveIds[i + k] = nextMoveId;
                moveIds[bestStart + k] = nextMoveId;
            }

            nextMoveId++;
        }

        for (var i = 0; i < count; i++)
        {
            if (moveIds[i] is not 0)
            {
                var entry = entries[i];
                entries[i] = new TextDiffEntry(entry.Operation, entry.OldText, entry.NewText, moveIds[i] - 1);
            }
        }
    }

    private static bool IsMovable(List<TextDiffEntry> entries, int[] moveIds, int index, TextDiffOperation operation)
        => index < entries.Count && entries[index].Operation == operation && moveIds[index] is 0;

    private static bool HasNonWhitespace(List<TextDiffEntry> entries, int start, int length)
    {
        for (var i = start; i < start + length; i++)
        {
            if (!string.IsNullOrWhiteSpace(entries[i].OldText))
                return true;
        }

        return false;
    }
}
