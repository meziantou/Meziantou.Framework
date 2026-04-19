using System.Reflection;

namespace Meziantou.Framework.SnapshotTesting;

public sealed record SnapshotTestContext(string? TestName = null, IReadOnlyDictionary<string, string?>? Metadata = null)
{
    private static Func<string?>? s_xunitV3GetDisplayName;
    private static Func<string?>? s_tunitGetDisplayName;

    internal static SnapshotTestContext Get()
    {
        var displayName = GetDisplayName(ref s_xunitV3GetDisplayName, TryCreateXunitV3GetDisplayName)?.Invoke() ??
                          GetDisplayName(ref s_tunitGetDisplayName, TryCreateTUnitGetDisplayName)?.Invoke();
        if (displayName is not null)
            return new SnapshotTestContext(TestName: displayName);

        return new();
    }

    private static Func<string?>? GetDisplayName(ref Func<string?>? cachedFactory, Func<Func<string?>?> factory)
    {
        var getDisplayName = cachedFactory;
        if (getDisplayName is not null)
            return getDisplayName;

        getDisplayName = factory();
        if (getDisplayName is not null)
        {
            Interlocked.CompareExchange(ref cachedFactory, getDisplayName, comparand: null);
        }

        return getDisplayName;
    }

    private static Func<string?>? TryCreateXunitV3GetDisplayName()
    {
        // Xunit v3: Xunit.TestContext.Current?.Test?.TestDisplayName
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

                    var displayName = GetStringPropertyValue(test, "TestDisplayName") ??
                                      GetStringPropertyValue(test, "DisplayName");
                    var methodName = GetMethodName(displayName);
                    if (methodName is null)
                        return displayName;

                    var arguments = GetObjectArrayPropertyValue(test, "TestMethodArguments");
                    if (arguments is null || arguments.Length == 0)
                        return methodName;

                    return methodName + "_" + string.Join('_', arguments.Select(FormatArgument));
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

    private static Func<string?>? TryCreateTUnitGetDisplayName()
    {
        // TUnit: TUnit.Core.TestContext.Current?.Metadata?.DisplayName
        // We use reflection so we don't take a hard dependency on TUnit.
        try
        {
            var testContextType = Type.GetType("TUnit.Core.TestContext, TUnit.Core", throwOnError: false);
            if (testContextType is null)
                return null;

            var currentProperty = testContextType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
            if (currentProperty is null)
                return null;

            var metadataProperty = testContextType.GetProperty("Metadata", BindingFlags.Public | BindingFlags.Instance);
            if (metadataProperty is null)
                return null;

            var metadataType = metadataProperty.PropertyType;
            var displayNameProperty = metadataType.GetProperty("DisplayName", BindingFlags.Public | BindingFlags.Instance);
            var testNameProperty = metadataType.GetProperty("TestName", BindingFlags.Public | BindingFlags.Instance);

            return () =>
            {
                try
                {
                    var current = currentProperty.GetValue(null);
                    if (current is null)
                        return null;

                    var metadata = metadataProperty.GetValue(current);
                    if (metadata is null)
                        return null;

                    return displayNameProperty?.GetValue(metadata) as string ??
                           testNameProperty?.GetValue(metadata) as string;
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

    private static string? GetStringPropertyValue(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        return property?.GetValue(instance) as string;
    }

    private static object?[]? GetObjectArrayPropertyValue(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        return property?.GetValue(instance) as object?[];
    }

    private static string? GetMethodName(string? displayName)
    {
        if (displayName is null)
            return null;

        var typeSeparatorIndex = displayName.LastIndexOf('.');
        if (typeSeparatorIndex >= 0)
        {
            displayName = displayName[(typeSeparatorIndex + 1)..];
        }

        var parameterStartIndex = displayName.IndexOf('(', StringComparison.Ordinal);
        if (parameterStartIndex >= 0)
        {
            displayName = displayName[..parameterStartIndex];
        }

        return string.IsNullOrWhiteSpace(displayName) ? null : displayName;
    }

    private static string FormatArgument(object? argument)
    {
        return argument switch
        {
            null => "null",
            string value => value,
            IFormattable value => value.ToString(format: null, CultureInfo.InvariantCulture) ?? argument.ToString() ?? "",
            _ => argument.ToString() ?? "",
        };
    }
}
