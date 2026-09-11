using System.Reflection;

namespace Meziantou.Framework.SnapshotTesting;

public sealed record SnapshotTestContext(string? TestName = null, IReadOnlyDictionary<string, string?>? Metadata = null)
{
    private static Func<SnapshotTestContext?>? s_xunitV3GetContext;
    private static Func<SnapshotTestContext?>? s_tunitGetContext;
    private static Func<SnapshotTestContext?>? s_nunitGetContext;

    /// <summary>
    /// Simple name of the class declaring the running test, when the test framework exposes it. It is used
    /// when the call stack does not contain the test method, which happens when the assertion runs in a
    /// helper method that awaited before calling <see cref="Snapshot.Validate(object?, SnapshotType?, SnapshotSettings?, string?, int, string?)" />.
    /// </summary>
    public string? ClassName { get; init; }

    /// <summary>
    /// Name of the running test method, when the test framework exposes it. Unlike <see cref="TestName" />,
    /// it never contains the test arguments.
    /// </summary>
    public string? MethodName { get; init; }

    internal static SnapshotTestContext Get()
    {
        return GetContext(ref s_xunitV3GetContext, TryCreateXunitV3GetContext)?.Invoke() ??
               GetContext(ref s_tunitGetContext, TryCreateTUnitGetContext)?.Invoke() ??
               GetContext(ref s_nunitGetContext, TryCreateNUnitGetContext)?.Invoke() ??
               new SnapshotTestContext();
    }

    private static Func<SnapshotTestContext?>? GetContext(ref Func<SnapshotTestContext?>? cachedFactory, Func<Func<SnapshotTestContext?>?> factory)
    {
        var getContext = cachedFactory;
        if (getContext is not null)
            return getContext;

        getContext = factory();
        if (getContext is not null)
        {
            Interlocked.CompareExchange(ref cachedFactory, getContext, comparand: null);
        }

        return getContext;
    }

    private static SnapshotTestContext? Create(string? testName, string? className, string? methodName)
    {
        className = NormalizeClassName(className);
        if (testName is null && className is null && methodName is null)
            return null;

        return new SnapshotTestContext(TestName: testName) { ClassName = className, MethodName = methodName };
    }

    /// <summary>
    /// Reduces a type name to the simple name produced by the call stack analysis, so both sources agree:
    /// no namespace, no declaring types and no generic arity suffix.
    /// </summary>
    private static string? NormalizeClassName(string? className)
    {
        if (string.IsNullOrWhiteSpace(className))
            return null;

        var separatorIndex = className.LastIndexOfAny(['.', '+']);
        if (separatorIndex >= 0)
        {
            className = className[(separatorIndex + 1)..];
        }

        var genericSeparatorIndex = className.IndexOf('`', StringComparison.Ordinal);
        if (genericSeparatorIndex >= 0)
        {
            className = className[..genericSeparatorIndex];
        }

        return string.IsNullOrWhiteSpace(className) ? null : className;
    }

    private static Func<SnapshotTestContext?>? TryCreateXunitV3GetContext()
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
                    var methodName = GetStringPropertyValue(test, "MethodName") ?? GetMethodName(displayName);

                    // The class and the undecorated method name are only exposed by the test case.
                    var testCase = GetPropertyValue(test, "TestCase");
                    var className = testCase is null ? null : GetStringPropertyValue(testCase, "TestClassSimpleName") ?? GetStringPropertyValue(testCase, "TestClassName");
                    var testMethodName = testCase is null ? methodName : GetStringPropertyValue(testCase, "TestMethodName") ?? methodName;

                    if (methodName is null)
                        return Create(displayName, className, testMethodName);

                    var arguments = GetObjectArrayPropertyValue(test, "TestMethodArguments");
                    if (arguments is null || arguments.Length == 0)
                        return Create(methodName, className, testMethodName);

                    return Create(methodName + "_" + string.Join('_', arguments.Select(FormatArgument)), className, testMethodName);
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

    private static Func<SnapshotTestContext?>? TryCreateTUnitGetContext()
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
            var testDetailsProperty = metadataType.GetProperty("TestDetails", BindingFlags.Public | BindingFlags.Instance);

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

                    var displayName = displayNameProperty?.GetValue(metadata) as string ??
                                      testNameProperty?.GetValue(metadata) as string;

                    // The class and the undecorated method name are only exposed by the test details.
                    var testDetails = testDetailsProperty?.GetValue(metadata);
                    var className = testDetails is null ? null : (GetPropertyValue(testDetails, "ClassType") as Type)?.Name;
                    var methodName = testDetails is null ? null : GetStringPropertyValue(testDetails, "MethodName");

                    return Create(GetTUnitTestName(testDetails, displayName, methodName), className, methodName);
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

    /// <summary>
    /// Builds the TUnit test name from the method name and the arguments, as the xunit and NUnit contexts
    /// do. The display name of a parameterized test spells the arguments out between parentheses, which a
    /// file name cannot keep: sanitizing them away would let two cases of one theory claim the same
    /// snapshot. A name the user chose is returned unchanged.
    /// </summary>
    private static string? GetTUnitTestName(object? testDetails, string? displayName, string? methodName)
    {
        if (testDetails is null)
            return displayName;

        methodName ??= GetMethodName(displayName);
        if (methodName is null || ShouldPreferDisplayName(displayName, methodName))
            return displayName;

        var arguments = GetObjectArrayPropertyValue(testDetails, "TestMethodArguments");
        if (arguments is null || arguments.Length == 0)
            return methodName;

        return methodName + "_" + string.Join('_', arguments.Select(FormatArgument));
    }

    private static Func<SnapshotTestContext?>? TryCreateNUnitGetContext()
    {
        // NUnit: NUnit.Framework.TestContext.CurrentContext?.Test
        // We use reflection so we don't take a hard dependency on NUnit.
        try
        {
            var testContextType = Type.GetType("NUnit.Framework.TestContext, nunit.framework", throwOnError: false);
            if (testContextType is null)
                return null;

            var currentContextProperty = testContextType.GetProperty("CurrentContext", BindingFlags.Public | BindingFlags.Static);
            if (currentContextProperty is null)
                return null;

            var currentContextType = currentContextProperty.PropertyType;
            var testProperty = currentContextType.GetProperty("Test", BindingFlags.Public | BindingFlags.Instance);
            if (testProperty is null)
                return null;

            return () =>
            {
                try
                {
                    var currentContext = currentContextProperty.GetValue(null);
                    if (currentContext is null)
                        return null;

                    var test = testProperty.GetValue(currentContext);
                    if (test is null)
                        return null;

                    var displayName = GetStringPropertyValue(test, "Name") ??
                                      GetStringPropertyValue(test, "MethodName") ??
                                      GetStringPropertyValue(test, "FullName");

                    var methodName = GetMethodName(displayName);
                    var className = (GetPropertyValue(test, "Type") as Type)?.Name ?? GetStringPropertyValue(test, "ClassName");
                    var testMethodName = GetStringPropertyValue(test, "MethodName") ?? methodName;

                    if (methodName is null)
                        return Create(displayName, className, testMethodName);

                    var arguments = GetObjectArrayPropertyValue(test, "Arguments");
                    if (arguments is null || arguments.Length == 0)
                        return Create(displayName ?? methodName, className, testMethodName);

                    var displayNameHasParameters = displayName?.IndexOf('(', StringComparison.Ordinal) >= 0;
                    if (ShouldPreferDisplayName(displayName, methodName) || !displayNameHasParameters)
                        return Create(GetMethodName(displayName) ?? displayName, className, testMethodName);

                    return Create(methodName + "_" + string.Join('_', arguments.Select(FormatArgument)), className, testMethodName);
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

    // The parameter is deliberately not named propertyName: these are properties of the test framework
    // types read through reflection, not members of this type, so nameof does not apply to them.
    private static object? GetPropertyValue(object instance, string name)
    {
        var property = instance.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        return property?.GetValue(instance);
    }

    private static string? GetStringPropertyValue(object instance, string name)
    {
        return GetPropertyValue(instance, name) as string;
    }

    private static object?[]? GetObjectArrayPropertyValue(object instance, string name)
    {
        return GetPropertyValue(instance, name) as object?[];
    }

    private static string? GetMethodName(string? displayName)
    {
        if (displayName is null)
            return null;

        var typeSeparatorIndex = displayName.LastIndexOf('.', StringComparison.Ordinal);
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

    private static bool ShouldPreferDisplayName(string? displayName, string methodName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return false;

        if (string.Equals(displayName, methodName, StringComparison.Ordinal))
            return false;

        return !displayName.StartsWith(methodName + "(", StringComparison.Ordinal);
    }
}
