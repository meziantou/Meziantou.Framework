namespace Meziantou.Framework.SnapshotTesting;

/// <summary>
/// Identifies the test an assertion belongs to, as far as snapshot naming is concerned. Two assertions with equal
/// owners may share a snapshot name: they are the same test running again, or tests the user deliberately gave the
/// same <see cref="SnapshotTestContext.TestName" />.
/// </summary>
internal sealed record SnapshotNameOwner(string? SourceFilePath, string? ClassName, string? MethodName, string? TestName, string? Metadata, string? TestId)
{
    public override string ToString()
    {
        var sb = new StringBuilder();
        if (ClassName is not null || MethodName is not null)
        {
            sb.Append(ClassName is null ? MethodName : ClassName + "." + MethodName);
        }

        if (TestName is not null)
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append("(test name: '").Append(TestName).Append("')");
        }

        if (!string.IsNullOrEmpty(Metadata))
        {
            sb.Append(" (metadata: ").Append(Metadata).Append(')');
        }

        if (TestId is not null)
        {
            sb.Append(" (test id: ").Append(TestId).Append(')');
        }

        return sb.ToString();
    }
}
