using System.Reflection;

namespace Meziantou.Framework.SnapshotTesting;

public sealed record SnapshotTestContext(string? TestName = null, IReadOnlyDictionary<string, string?>? Metadata = null)
{
    private static readonly Lazy<Func<string?>?> XunitV3GetDisplayName = new(TryCreateXunitV3GetDisplayName);

    internal static SnapshotTestContext Get()
    {
        var displayName = XunitV3GetDisplayName.Value?.Invoke();
        if (displayName is not null)
            return new SnapshotTestContext(TestName: displayName);

        return new();
    }

    private static Func<string?>? TryCreateXunitV3GetDisplayName()
    {
        // Xunit v3: Xunit.TestContext.Current?.Test?.DisplayName
        // We use reflection so we don't take a hard dependency on xunit.
        try
        {
            var testContextType = Type.GetType("Xunit.TestContext, xunit.v3.core", throwOnError: false);
            if (testContextType is null)
                return null;

            var currentProperty = testContextType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
            if (currentProperty is null)
                return null;

            var testProperty = testContextType.GetProperty("Test", BindingFlags.Public | BindingFlags.Instance);
            if (testProperty is null)
                return null;

            var testType = testProperty.PropertyType;
            var displayNameProperty = testType.GetProperty("DisplayName", BindingFlags.Public | BindingFlags.Instance);
            if (displayNameProperty is null)
                return null;

            return () =>
            {
                try
                {
                    var current = currentProperty.GetValue(null);
                    if (current is null)
                        return null;

                    var test = testProperty.GetValue(current);
                    if (test is null)
                        return null;

                    return displayNameProperty.GetValue(test) as string;
                }
                catch
                {
                    return null;
                }
            };
        }
        catch
        {
            return null;
        }
    }
}

