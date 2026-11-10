namespace Meziantou.Framework;

/// <summary>Pairs blocks of deleted chunks with equal blocks of inserted chunks found elsewhere in the diff.</summary>
internal static class MoveDetector
{
    // Bounds the work for a chunk that is inserted at many places, such as an empty line
    private const int MaxCandidatesPerChunk = 64;

    internal static void Apply(List<TextDiffEntry> entries, IEqualityComparer<string> comparer, DiffBudget budget)
    {
        var count = entries.Count;

        // Consecutive changed entries form a block; a chunk deleted and inserted in the same block did not move
        var blocks = new int[count];
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
            foreach (var candidate in candidates)
            {
                if (moveIds[candidate] is not 0 || blocks[candidate] == blocks[i])
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
