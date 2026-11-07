using System.Collections.Concurrent;

namespace Meziantou.Framework.SnapshotTesting;

/// <summary>
/// Remembers the snapshot names the assertions of the current process used, so a name that two tests would share
/// is reported instead of letting them overwrite each other's snapshot files.
/// </summary>
/// <remarks>
/// Only the names produced by the default path strategy and a built-in naming strategy are tracked: a custom
/// strategy may give several tests the same file on purpose. The registry cannot see the tests that did not run
/// in the process, so it detects a collision only when both tests run.
/// </remarks>
internal static class SnapshotNameRegistry
{
    // The key ignores case: 'Name_A' and 'Name_a' are the same file on Windows and macOS.
    private static readonly ConcurrentDictionary<string, SnapshotNameClaim> Claims = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, List<int>> CallSites = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns the 1-based position of a call site among the call sites of a test that assert under the same name.
    /// </summary>
    /// <remarks>
    /// Call sites are numbered in the order they first run. The line identifies the call rather than a counter
    /// incremented by each call: a test that runs again in the same process - a repeated or retried test, the
    /// cases of an NUnit parameterized fixture - gets the same numbers again.
    /// </remarks>
    public static int GetCallOrdinal(string testKey, int lineNumber)
    {
        var lines = CallSites.GetOrAdd(testKey, static _ => []);
        lock (lines)
        {
            var index = lines.IndexOf(lineNumber);
            if (index < 0)
            {
                lines.Add(lineNumber);
                index = lines.Count - 1;
            }

            return index + 1;
        }
    }

    /// <summary>
    /// Records that an assertion stores its snapshots under a name, and throws when another test already uses
    /// that name.
    /// </summary>
    /// <param name="directory">Directory containing the snapshot files.</param>
    /// <param name="snapshotName">Name of the snapshot files, without the index, <c>.verified</c> and the extension.</param>
    /// <param name="owner">Test that stores its snapshots under the name.</param>
    /// <param name="isLegacyName">Indicates whether the name is the one an earlier version of the library gave to the test.</param>
    public static void Claim(FullPath directory, string snapshotName, SnapshotNameOwner owner, bool isLegacyName)
    {
        var claim = new SnapshotNameClaim(snapshotName, owner, isLegacyName);
        var existingClaim = Claims.GetOrAdd(GetKey(directory, snapshotName), claim);
        if (ReferenceEquals(existingClaim, claim))
            return;

        if (!string.Equals(existingClaim.SnapshotName, snapshotName, StringComparison.Ordinal))
        {
            throw new SnapshotException(
                $"""
                The snapshot names '{existingClaim.SnapshotName}' and '{snapshotName}' in '{directory}' only differ by case.
                On a case-insensitive file system (the default on Windows and macOS) they are the same file, so these tests would overwrite each other's snapshots:
                  - {existingClaim.Owner}
                  - {owner}
                Give one of the tests a name that differs by more than case, for example by renaming it or by setting Snapshot.TestContext.
                """);
        }

        if (existingClaim.Owner != owner)
        {
            var message = $"""
                The snapshot name '{snapshotName}' in '{directory}' is used by several tests, which would overwrite each other's snapshots:
                  - {existingClaim.Owner}
                  - {owner}
                """;

            if (isLegacyName || existingClaim.IsLegacyName)
            {
                message += Environment.NewLine + $"""
                    The snapshot files were named by an earlier version of Meziantou.Framework.SnapshotTesting, which could not tell these tests apart.
                    Delete the '{snapshotName}.verified.*' files and run the tests again: each test then creates its own snapshot.
                    """;
            }
            else if (existingClaim.Owner with { Metadata = owner.Metadata } == owner)
            {
                message += Environment.NewLine + "SnapshotTestContext.Metadata is not part of the snapshot name. Give each test context a distinct TestName.";
            }
            else
            {
                message += Environment.NewLine + "Give each test a distinct name, for example by renaming it, by changing its display name, by making its arguments override ToString, or by setting Snapshot.TestContext.";
            }

            throw new SnapshotException(message);
        }
    }

    /// <summary>
    /// Indicates whether an assertion of the current process stores its snapshots under the name.
    /// </summary>
    public static bool IsClaimed(FullPath directory, string snapshotName) => Claims.ContainsKey(GetKey(directory, snapshotName));

    /// <summary>
    /// Indicates whether an assertion of another test of the current process stores its snapshots under the name.
    /// </summary>
    public static bool IsClaimedByAnotherTest(FullPath directory, string snapshotName, SnapshotNameOwner owner)
    {
        return Claims.TryGetValue(GetKey(directory, snapshotName), out var claim) && claim.Owner != owner;
    }

    private static string GetKey(FullPath directory, string snapshotName) => directory.Value + Path.DirectorySeparatorChar + snapshotName;

    private sealed record SnapshotNameClaim(string SnapshotName, SnapshotNameOwner Owner, bool IsLegacyName);
}
