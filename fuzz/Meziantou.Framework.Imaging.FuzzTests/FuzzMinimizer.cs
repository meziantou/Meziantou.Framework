namespace Meziantou.Framework.Imaging.FuzzTests;

/// <summary>
/// Shrinks a failing input while it keeps failing with the same <see cref="FuzzFailure.Signature"/>: removal of halving
/// ranges (delta debugging), then zeroing of single bytes. PNG candidates are also tried with recomputed chunk CRCs, so that
/// removals inside chunks do not merely turn the defect into a checksum error.
/// </summary>
internal static class FuzzMinimizer
{
    public static byte[] Minimize(byte[] input, string signature, Func<byte[], FuzzFailure?> check, int maxAttempts = 2000)
    {
        var attempts = 0;
        var current = input;

        bool Reproduces(byte[] candidate, out byte[] accepted)
        {
            accepted = candidate;
            if (attempts++ >= maxAttempts)
                return false;

            if (check(candidate)?.Signature == signature)
                return true;

            if (FuzzMutator.IsPng(candidate) && attempts++ < maxAttempts)
            {
                var fixedCrc = (byte[])candidate.Clone();
                FuzzMutator.FixPngCrcs(fixedCrc);
                if (!fixedCrc.AsSpan().SequenceEqual(candidate) && check(fixedCrc)?.Signature == signature)
                {
                    accepted = fixedCrc;
                    return true;
                }
            }

            return false;
        }

        for (var chunk = Math.Max(1, current.Length / 2); chunk >= 1 && attempts < maxAttempts; chunk /= 2)
        {
            var progress = true;
            while (progress && attempts < maxAttempts)
            {
                progress = false;
                for (var start = 0; start < current.Length && attempts < maxAttempts;)
                {
                    var length = Math.Min(chunk, current.Length - start);
                    var candidate = new byte[current.Length - length];
                    current.AsSpan(0, start).CopyTo(candidate);
                    current.AsSpan(start + length).CopyTo(candidate.AsSpan(start));
                    if (candidate.Length > 0 && Reproduces(candidate, out var accepted))
                    {
                        current = accepted;
                        progress = true;
                    }
                    else
                    {
                        start += length;
                    }
                }
            }

            if (chunk == 1)
                break;
        }

        for (var i = 0; i < current.Length && attempts < maxAttempts; i++)
        {
            if (current[i] == 0)
                continue;

            var candidate = (byte[])current.Clone();
            candidate[i] = 0;
            if (Reproduces(candidate, out var accepted))
            {
                current = accepted;
            }
        }

        return current;
    }
}
