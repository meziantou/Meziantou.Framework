namespace Meziantou.Framework.SnapshotTesting;

public static class SnapshotNamingStrategies
{
    public static SnapshotNamingStrategy TestName { get; } = GetTestName;

    public static SnapshotNamingStrategy ClassName_TestName { get; } = GetClassNameAndTestName;

    public static SnapshotNamingStrategy FullName { get; } = GetFullName;

    private static string GetTestName(SnapshotPathContext context)
    {
        return context.TestContext?.TestName ?? context.MethodName;
    }

    private static string GetClassNameAndTestName(SnapshotPathContext context)
    {
        var value = GetTestName(context);
        if (string.IsNullOrWhiteSpace(context.ClassName))
            return value;

        if (string.IsNullOrWhiteSpace(value))
            return context.ClassName;

        if (IsClassNameAlreadyIncluded(value, context.ClassName, context.MethodName))
            return value;

        return context.ClassName + "_" + value;
    }

    private static string GetFullName(SnapshotPathContext context)
    {
        var value = GetTestName(context);
        if (string.IsNullOrWhiteSpace(context.ClassName))
            return value;

        if (string.IsNullOrWhiteSpace(value))
            return context.ClassName;

        if (IsClassNameAlreadyIncluded(value, context.ClassName, context.MethodName))
            return value;

        return context.ClassName + "." + value;
    }

    private static bool IsClassNameAlreadyIncluded(string value, string className, string methodName)
    {
        return string.Equals(value, className, StringComparison.Ordinal) ||
               value.StartsWith(className + "_", StringComparison.Ordinal) ||
               value.StartsWith(className + ".", StringComparison.Ordinal) ||
               value.Contains($"{className}.{methodName}", StringComparison.Ordinal);
    }
}

