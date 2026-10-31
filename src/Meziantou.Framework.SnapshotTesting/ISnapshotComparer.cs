namespace Meziantou.Framework.SnapshotTesting;

/// <summary>
/// Decides whether the snapshot produced by a test matches the verified snapshot. A comparer is registered for a
/// <see cref="SnapshotType"/> in <see cref="SnapshotComparerCollection"/>.
/// </summary>
public interface ISnapshotComparer
{
    /// <summary>
    /// Compares two snapshots.
    /// </summary>
    /// <param name="expected">The verified snapshot.</param>
    /// <param name="actual">The snapshot produced by the test.</param>
    /// <returns><see langword="true"/> when the snapshots match.</returns>
    bool Equals(SnapshotData expected, SnapshotData actual);

    /// <summary>
    /// Compares two snapshots and, when they do not match, describes why.
    /// </summary>
    /// <param name="expected">The verified snapshot.</param>
    /// <param name="actual">The snapshot produced by the test.</param>
    /// <param name="mismatchReason">
    /// When the snapshots do not match, a short description of the difference that is added to the assertion message
    /// (for example, the similarity score and the threshold it did not reach), or <see langword="null"/> when the
    /// comparer has nothing to add. Always <see langword="null"/> when the snapshots match.
    /// </param>
    /// <returns><see langword="true"/> when the snapshots match.</returns>
    /// <remarks>The default implementation calls <see cref="Equals(SnapshotData, SnapshotData)"/> and gives no reason.</remarks>
    bool Equals(SnapshotData expected, SnapshotData actual, out string? mismatchReason)
    {
        mismatchReason = null;
        return Equals(expected, actual);
    }
}
