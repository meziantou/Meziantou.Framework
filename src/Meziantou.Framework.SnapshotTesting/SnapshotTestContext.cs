using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

namespace Meziantou.Framework.SnapshotTesting;

public sealed record SnapshotTestContext(string? TestName = null, IReadOnlyDictionary<string, string?>? Metadata = null)
{
    private const int MaxFormattedArgumentLength = 512;
    private const int MaxFormattedArgumentDepth = 4;

    private static Func<SnapshotTestContext?>? s_xunitV3GetContext;
    private static Func<SnapshotTestContext?>? s_tunitGetContext;
    private static Func<SnapshotTestContext?>? s_nunitGetContext;

    // Key: class, method and test name. Value: the types of the arguments of the first case that used the name.
    private static readonly ConcurrentDictionary<string, string> ArgumentTypeSignatures = new(StringComparer.Ordinal);

    /// <summary>
    /// Simple name of the class the running test belongs to, when the test framework exposes it. When set, it
    /// is used as <see cref="SnapshotPathContext.ClassName" /> without walking the call stack; the stack is
    /// only consulted when it is <see langword="null" />.
    /// </summary>
    public string? ClassName { get; init; }

    /// <summary>
    /// Name of the running test method, when the test framework exposes it. Unlike <see cref="TestName" />,
    /// it never contains the test arguments. When set, it is used as <see cref="SnapshotPathContext.MethodName" />
    /// without walking the call stack; the stack is only consulted when it is <see langword="null" />.
    /// </summary>
    public string? MethodName { get; init; }

    /// <summary>
    /// Indicates whether the context was detected from the test framework rather than set through
    /// <see cref="Snapshot.TestContext" />. A name the user chose may be shared by several tests on purpose; a
    /// name derived from the test framework is shared by accident.
    /// </summary>
    internal bool IsDetectedFromTestFramework { get; init; }

    /// <summary>
    /// Test name that earlier versions of the library derived from the same test, when it differs from
    /// <see cref="TestName" />. Those versions parsed the display name of the test and formatted its arguments
    /// in ways that could give several tests the same name. Snapshot files created under that name are still
    /// used while they exist, so fixing the naming does not orphan them.
    /// </summary>
    internal string? LegacyTestName { get; init; }

    /// <summary>
    /// Identifier of the running test, set when <see cref="TestName" /> cannot tell the test apart from the
    /// other cases of the same method: an argument does not override <see cref="object.ToString" />, so every
    /// case gets the same name. The identifier lets the engine report the collision instead of letting the
    /// cases share a snapshot.
    /// </summary>
    internal string? AmbiguousTestId { get; init; }

    internal static SnapshotTestContext Get()
    {
        return GetContext(ref s_xunitV3GetContext, TryCreateXunitV3GetContext).Invoke() ??
               GetContext(ref s_tunitGetContext, TryCreateTUnitGetContext).Invoke() ??
               GetContext(ref s_nunitGetContext, TryCreateNUnitGetContext).Invoke() ??
               new SnapshotTestContext();
    }

    /// <summary>
    /// Returns the cached context accessor of a test framework, creating it on first use. A test framework that is not
    /// loaded is remembered too: probing for it again would repeat a failed type load, and fire the assembly resolve
    /// handlers, on every assertion.
    /// </summary>
    internal static Func<SnapshotTestContext?> GetContext(ref Func<SnapshotTestContext?>? cachedFactory, Func<Func<SnapshotTestContext?>?> factory)
    {
        var getContext = cachedFactory;
        if (getContext is not null)
            return getContext;

        getContext = factory() ?? UnavailableContext;
        return Interlocked.CompareExchange(ref cachedFactory, getContext, comparand: null) ?? getContext;
    }

    private static SnapshotTestContext? UnavailableContext() => null;

    private static SnapshotTestContext? Create(TestNames names, string? className, string? methodName, object?[]? arguments, Func<string?> getTestId)
    {
        className = NormalizeClassName(className);
        if (names.TestName is null && className is null && methodName is null)
            return null;

        var hasAmbiguousArguments = names.HasAmbiguousArguments || HasArgumentsOfAnotherType(className, methodName, names.TestName, arguments);
        return new SnapshotTestContext(TestName: names.TestName)
        {
            ClassName = className,
            MethodName = methodName,
            IsDetectedFromTestFramework = true,
            LegacyTestName = string.Equals(names.TestName, names.LegacyTestName, StringComparison.Ordinal) ? null : names.LegacyTestName,
            AmbiguousTestId = hasAmbiguousArguments ? getTestId() : null,
        };
    }

    /// <summary>
    /// Indicates whether another case of the test got the same name from arguments of other types. The name only
    /// contains the text of the arguments, so an <see cref="object" /> parameter receiving <c>1</c>, <c>1L</c> and
    /// <c>"1"</c> names the three cases alike. Such a case is flagged as ambiguous, so its owner contains the test
    /// identifier and the engine reports the collision. The first case seen keeps its name without the identifier:
    /// one differing owner is enough to detect the collision, and a test that runs again in the process keeps an
    /// equal owner.
    /// </summary>
    internal static bool HasArgumentsOfAnotherType(string? className, string? methodName, string? testName, object?[]? arguments)
    {
        if (arguments is null || arguments.Length == 0 || testName is null)
            return false;

        var typeSignature = GetArgumentTypeSignature(arguments);
        var key = string.Join('\0', className, methodName, testName);
        var firstTypeSignature = ArgumentTypeSignatures.GetOrAdd(key, typeSignature);
        return !string.Equals(firstTypeSignature, typeSignature, StringComparison.Ordinal);
    }

    private static string GetArgumentTypeSignature(object?[] arguments)
    {
        var result = new StringBuilder();
        foreach (var argument in arguments)
        {
            AppendArgumentTypeSignature(result, argument, depth: 0);
            result.Append(';');
        }

        return result.ToString();
    }

    private static void AppendArgumentTypeSignature(StringBuilder result, object? argument, int depth)
    {
        if (argument is null)
        {
            result.Append("null");
            return;
        }

        result.Append(argument.GetType().AssemblyQualifiedName);

        // The elements of a list are formatted one by one, so [1] and [1L] get the same name too.
        if (argument is IList list && depth < MaxFormattedArgumentDepth)
        {
            result.Append('[');
            try
            {
                var count = Math.Min(list.Count, 64);
                for (var i = 0; i < count; i++)
                {
                    AppendArgumentTypeSignature(result, list[i], depth + 1);
                    result.Append(',');
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A default ImmutableArray, for instance, cannot be enumerated.
            }

            result.Append(']');
        }
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

                    // The class and the undecorated method name are only exposed by the test case.
                    var testCase = GetPropertyValue(test, "TestCase");
                    var className = testCase is null ? null : GetStringPropertyValue(testCase, "TestClassSimpleName") ?? GetStringPropertyValue(testCase, "TestClassName");
                    var arguments = GetObjectArrayPropertyValue(test, "TestMethodArguments");
                    var names = GetXunitV3TestNames(
                        displayName,
                        testMethodNameOfTest: GetStringPropertyValue(test, "MethodName"),
                        hasTestCase: testCase is not null,
                        testCaseDisplayName: testCase is null ? null : GetStringPropertyValue(testCase, "TestCaseDisplayName"),
                        testClassName: testCase is null ? null : GetStringPropertyValue(testCase, "TestClassName"),
                        testMethodName: testCase is null ? null : GetStringPropertyValue(testCase, "TestMethodName"),
                        arguments);

                    return Create(names, className, names.MethodName, arguments, () => GetStringPropertyValue(test, "UniqueID"));
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
    /// Computes the test name of an Xunit v3 test.
    /// </summary>
    /// <param name="displayName">Display name of the test, such as <c>Namespace.Class.Method(value: 1.5)</c>.</param>
    /// <param name="testMethodNameOfTest">Method name exposed by the test itself, if any.</param>
    /// <param name="hasTestCase">Indicates whether the test exposes its test case.</param>
    /// <param name="testCaseDisplayName">Display name of the test case: the display name of the test, with or without the arguments depending on the Xunit version.</param>
    /// <param name="testClassName">Full name of the test class, such as <c>Namespace.Class+Nested</c>.</param>
    /// <param name="testMethodName">Name of the test method exposed by the test case.</param>
    /// <param name="arguments">Arguments of the test method.</param>
    internal static TestNames GetXunitV3TestNames(string? displayName, string? testMethodNameOfTest, bool hasTestCase, string? testCaseDisplayName, string? testClassName, string? testMethodName, object?[]? arguments)
    {
        // Earlier versions took everything after the last '.' of the display name before removing the argument
        // list, so an argument containing a '.' became the name: 'Method(value: 1.5)' was named '5)_1.5'.
        var legacyMethodName = testMethodNameOfTest ?? LegacyGetMethodName(displayName);
        var legacyTestName = legacyMethodName is null ? displayName : arguments is null || arguments.Length == 0 ? legacyMethodName : legacyMethodName + "_" + LegacyFormatArguments(arguments);
        var contextMethodName = hasTestCase ? testMethodName ?? legacyMethodName : legacyMethodName;

        var namePart = testMethodNameOfTest ?? GetXunitV3NamePart(displayName, testCaseDisplayName, testClassName, testMethodName, hasArguments: arguments is { Length: > 0 });
        if (namePart is null)
            return new TestNames(displayName, legacyTestName, contextMethodName, HasAmbiguousArguments: false);

        if (arguments is null || arguments.Length == 0)
            return new TestNames(namePart, legacyTestName, contextMethodName, HasAmbiguousArguments: false);

        var formattedArguments = FormatArguments(arguments, out var hasAmbiguousArguments);
        return new TestNames(namePart + "_" + formattedArguments, legacyTestName, contextMethodName, hasAmbiguousArguments);
    }

    /// <summary>
    /// Extracts the part of an Xunit v3 display name that names the test: the method name when the display name
    /// is the default one, or the whole custom display name otherwise.
    /// </summary>
    private static string? GetXunitV3NamePart(string? displayName, string? testCaseDisplayName, string? testClassName, string? testMethodName, bool hasArguments)
    {
        if (displayName is null)
            return null;

        // Without the test case, a custom display name cannot be told apart from the default one.
        if (testClassName is null && testMethodName is null)
            return GetMethodNameFromDisplayName(displayName);

        // The default display name is 'Namespace.Class.Method(arguments)'.
        if (testClassName is not null && testMethodName is not null)
        {
            var defaultName = testClassName + "." + testMethodName;
            if (displayName.StartsWith(defaultName, StringComparison.Ordinal) && (displayName.Length == defaultName.Length || (hasArguments && displayName[defaultName.Length] == '(')))
                return testMethodName;
        }

        var name = hasArguments ? RemoveXunitV3ArgumentList(displayName, testCaseDisplayName) : displayName;

        // Only the class name is removed: a '.' in a custom display name is part of the name.
        if (testClassName is not null && name.Length > testClassName.Length + 1 && name.StartsWith(testClassName, StringComparison.Ordinal) && name[testClassName.Length] == '.')
        {
            name = name[(testClassName.Length + 1)..];
        }
        else if (testMethodName is not null && name.EndsWith("." + testMethodName, StringComparison.Ordinal))
        {
            name = testMethodName;
        }

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>
    /// Removes the argument list Xunit appends to the display name of a theory, <c>(name: value, ...)</c>. Looking for
    /// the first parenthesis would cut a custom display name that contains one, such as <c>Case (fast)</c>.
    /// </summary>
    private static string RemoveXunitV3ArgumentList(string displayName, string? testCaseDisplayName)
    {
        // Depending on the version and the configuration, the display name of the test case is the display name of
        // its tests with or without the argument list.
        if (testCaseDisplayName is not null && testCaseDisplayName.Length < displayName.Length && displayName.StartsWith(testCaseDisplayName, StringComparison.Ordinal) && displayName[testCaseDisplayName.Length] == '(')
            return testCaseDisplayName;

        var firstParenthesisIndex = -1;
        for (var i = 0; i < displayName.Length; i++)
        {
            if (displayName[i] != '(')
                continue;

            if (firstParenthesisIndex < 0)
            {
                firstParenthesisIndex = i;
            }

            // An argument is formatted as 'name: value'.
            var nameEnd = i + 1;
            while (nameEnd < displayName.Length && (char.IsLetterOrDigit(displayName[nameEnd]) || displayName[nameEnd] is '_' or '@'))
            {
                nameEnd++;
            }

            if (nameEnd > i + 1 && displayName.AsSpan(nameEnd).StartsWith(": ", StringComparison.Ordinal))
                return displayName[..i];
        }

        return firstParenthesisIndex < 0 ? displayName : displayName[..firstParenthesisIndex];
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
                    var arguments = testDetails is null ? null : GetObjectArrayPropertyValue(testDetails, "TestMethodArguments");
                    var classArguments = testDetails is null ? null : GetObjectArrayPropertyValue(testDetails, "TestClassArguments");

                    var names = GetTUnitTestNames(hasTestDetails: testDetails is not null, displayName, methodName, arguments, classArguments);
                    return Create(names, className, methodName, ConcatArguments(arguments, classArguments), () => testDetails is null ? null : GetStringPropertyValue(testDetails, "TestId"));
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
    /// <param name="hasTestDetails">Indicates whether the test exposes its details.</param>
    /// <param name="displayName">Display name of the test.</param>
    /// <param name="methodName">Name of the test method.</param>
    /// <param name="arguments">Arguments of the test method.</param>
    /// <param name="classArguments">Arguments of the test class constructor.</param>
    internal static TestNames GetTUnitTestNames(bool hasTestDetails, string? displayName, string? methodName, object?[]? arguments, object?[]? classArguments = null)
    {
        return AppendClassArguments(GetTUnitTestNamesWithoutClassArguments(hasTestDetails, displayName, methodName, arguments), arguments, classArguments);
    }

    private static TestNames GetTUnitTestNamesWithoutClassArguments(bool hasTestDetails, string? displayName, string? methodName, object?[]? arguments)
    {
        if (!hasTestDetails)
            return new TestNames(displayName, displayName, methodName, HasAmbiguousArguments: false);

        var legacyMethodName = methodName ?? LegacyGetMethodName(displayName);
        string? legacyTestName;
        if (legacyMethodName is null || ShouldPreferDisplayName(displayName, legacyMethodName))
        {
            legacyTestName = displayName;
        }
        else
        {
            legacyTestName = arguments is null || arguments.Length == 0 ? legacyMethodName : legacyMethodName + "_" + LegacyFormatArguments(arguments);
        }

        var testMethodName = methodName ?? GetMethodNameFromDisplayName(displayName);
        if (testMethodName is null || ShouldPreferDisplayName(displayName, testMethodName))
            return new TestNames(displayName, legacyTestName, methodName, HasAmbiguousArguments: false);

        if (arguments is null || arguments.Length == 0)
            return new TestNames(testMethodName, legacyTestName, methodName, HasAmbiguousArguments: false);

        var formattedArguments = FormatArguments(arguments, out var hasAmbiguousArguments);
        return new TestNames(testMethodName + "_" + formattedArguments, legacyTestName, methodName, hasAmbiguousArguments);
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

                    var name = GetStringPropertyValue(test, "Name");
                    var nunitMethodName = GetStringPropertyValue(test, "MethodName");
                    var fullName = GetStringPropertyValue(test, "FullName");
                    var className = (GetPropertyValue(test, "Type") as Type)?.Name ?? GetStringPropertyValue(test, "ClassName");

                    var arguments = GetObjectArrayPropertyValue(test, "Arguments");
                    var fixtureArguments = GetNUnitFixtureArguments(test, name, fullName, out var hasUnknownFixtureArguments);

                    var names = GetNUnitTestNames(name, nunitMethodName, fullName, arguments, fixtureArguments);
                    if (hasUnknownFixtureArguments)
                    {
                        // The instances of the parameterized fixture cannot be told apart, so a collision is reported
                        // instead of letting them share a snapshot.
                        names = names with { HasAmbiguousArguments = true };
                    }

                    return Create(names, className, names.MethodName, ConcatArguments(arguments, fixtureArguments), () => GetStringPropertyValue(test, "ID"));
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
    /// Computes the test name of an NUnit test.
    /// </summary>
    /// <param name="name">Name of the test: the method name followed by the arguments, such as <c>Method(1.5d)</c>, or the name set with <c>TestName</c>.</param>
    /// <param name="methodName">Name of the test method.</param>
    /// <param name="fullName">Full name of the test.</param>
    /// <param name="arguments">Arguments of the test method.</param>
    /// <param name="fixtureArguments">Arguments of the fixture instance the test belongs to, such as <c>"en-US"</c> for <c>[TestFixture("en-US")]</c>.</param>
    internal static TestNames GetNUnitTestNames(string? name, string? methodName, string? fullName, object?[]? arguments, object?[]? fixtureArguments = null)
    {
        return AppendClassArguments(GetNUnitTestNamesWithoutFixtureArguments(name, methodName, fullName, arguments), arguments, fixtureArguments);
    }

    private static TestNames GetNUnitTestNamesWithoutFixtureArguments(string? name, string? methodName, string? fullName, object?[]? arguments)
    {
        var displayName = name ?? methodName ?? fullName;
        var legacyTestName = LegacyGetNUnitTestName(displayName, arguments);
        var contextMethodName = methodName ?? LegacyGetMethodName(displayName);

        // Without the method name, the name cannot be told apart from a custom one.
        if (methodName is null || displayName is null)
            return new TestNames(legacyTestName, legacyTestName, contextMethodName, HasAmbiguousArguments: false);

        if (arguments is null || arguments.Length == 0)
            return new TestNames(displayName, legacyTestName, contextMethodName, HasAmbiguousArguments: false);

        // The default name of a parameterized test is 'Method(arguments)', or 'Method<T>(arguments)' for a generic
        // method. Anything else was set with 'TestName' and is kept as is, including its '.' characters.
        var parameterStartIndex = displayName.StartsWith(methodName, StringComparison.Ordinal) && displayName.EndsWith(')', StringComparison.Ordinal) ? displayName.IndexOf("(", methodName.Length, StringComparison.Ordinal) : -1;
        if (parameterStartIndex < 0 || (parameterStartIndex > methodName.Length && displayName[methodName.Length] != '<'))
            return new TestNames(displayName, legacyTestName, contextMethodName, HasAmbiguousArguments: false);

        var formattedArguments = FormatArguments(arguments, out var hasAmbiguousArguments);
        return new TestNames(displayName[..parameterStartIndex] + "_" + formattedArguments, legacyTestName, contextMethodName, hasAmbiguousArguments);
    }

    private static string? LegacyGetNUnitTestName(string? displayName, object?[]? arguments)
    {
        var methodName = LegacyGetMethodName(displayName);
        if (methodName is null)
            return displayName;

        if (arguments is null || arguments.Length == 0)
            return displayName ?? methodName;

        var displayNameHasParameters = displayName?.IndexOf('(', StringComparison.Ordinal) >= 0;
        if (ShouldPreferDisplayName(displayName, methodName) || !displayNameHasParameters)
            return LegacyGetMethodName(displayName) ?? displayName;

        return methodName + "_" + LegacyFormatArguments(arguments);
    }

    /// <summary>
    /// Returns the arguments of the NUnit fixture instance the test belongs to. Every instance of a parameterized
    /// fixture, <c>[TestFixture("en-US")] [TestFixture("fr-FR")]</c>, runs the same methods under the same names, so
    /// only these arguments tell them apart.
    /// </summary>
    /// <param name="test">The <c>TestContext.TestAdapter</c> of the running test.</param>
    /// <param name="name">Name of the test.</param>
    /// <param name="fullName">Full name of the test.</param>
    /// <param name="hasUnknownFixtureArguments">Set when the fixture is parameterized but its arguments are not exposed.</param>
    private static object?[]? GetNUnitFixtureArguments(object test, string? name, string? fullName, out bool hasUnknownFixtureArguments)
    {
        hasUnknownFixtureArguments = false;
        try
        {
            var parentProperty = test.GetType().GetProperty("Parent", BindingFlags.Public | BindingFlags.Instance);
            if (parentProperty is not null)
            {
                // The parent of a test case is the suite of the parameterized method, whose parent is the fixture.
                var parent = parentProperty.GetValue(test);
                for (var depth = 0; parent is not null && depth < 4; depth++)
                {
                    if (string.Equals(GetStringPropertyValue(parent, "TestType"), "TestFixture", StringComparison.Ordinal))
                        return GetObjectArrayPropertyValue(parent, "Arguments");

                    parent = GetPropertyValue(parent, "Parent");
                }

                return null;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The internals of NUnit may change; the full name below is the fallback.
        }

        // Older versions of NUnit do not expose the parent. The full name of a test of a parameterized fixture is
        // 'Namespace.Fixture(arguments).Name'.
        if (name is not null && fullName is not null && fullName.Length > name.Length + 1 && fullName.EndsWith(name, StringComparison.Ordinal))
        {
            var fixtureName = fullName.AsSpan(0, fullName.Length - name.Length);
            hasUnknownFixtureArguments = fixtureName.EndsWith(").", StringComparison.Ordinal);
        }

        return null;
    }

    /// <summary>
    /// Appends the arguments of the test class, formatted like the arguments of the test method, to the test name.
    /// </summary>
    private static TestNames AppendClassArguments(TestNames names, object?[]? arguments, object?[]? classArguments)
    {
        if (classArguments is null || classArguments.Length == 0 || names.TestName is null)
            return names;

        // '_' separates the class arguments from the rest of the name, so a '_' in any string argument makes the name
        // ambiguous, whatever the number of arguments.
        var formattedClassArguments = FormatArguments(classArguments, splitsArguments: true, out var hasAmbiguousArguments);
        if (arguments is { Length: > 0 })
        {
            FormatArguments(arguments, splitsArguments: true, out var hasAmbiguousMethodArguments);
            hasAmbiguousArguments |= hasAmbiguousMethodArguments;
        }

        return names with
        {
            TestName = names.TestName + "_" + formattedClassArguments,
            HasAmbiguousArguments = names.HasAmbiguousArguments || hasAmbiguousArguments,
        };
    }

    private static object?[]? ConcatArguments(object?[]? arguments, object?[]? classArguments)
    {
        if (classArguments is null || classArguments.Length == 0)
            return arguments;

        if (arguments is null || arguments.Length == 0)
            return classArguments;

        return [.. arguments, .. classArguments];
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

    /// <summary>
    /// Extracts the method name from a display name such as <c>Namespace.Class.Method(value: "a.b")</c>. The
    /// argument list is removed first, as the arguments may contain '.' characters.
    /// </summary>
    private static string? GetMethodNameFromDisplayName(string? displayName)
    {
        if (displayName is null)
            return null;

        var parameterStartIndex = displayName.IndexOf('(', StringComparison.Ordinal);
        if (parameterStartIndex >= 0)
        {
            displayName = displayName[..parameterStartIndex];
        }

        var typeSeparatorIndex = displayName.LastIndexOf('.', StringComparison.Ordinal);
        if (typeSeparatorIndex >= 0)
        {
            displayName = displayName[(typeSeparatorIndex + 1)..];
        }

        return string.IsNullOrWhiteSpace(displayName) ? null : displayName;
    }

    /// <summary>
    /// The method name extraction of earlier versions. It is kept to find the snapshot files those versions
    /// created, see <see cref="LegacyTestName" />.
    /// </summary>
    private static string? LegacyGetMethodName(string? displayName)
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

    private static string LegacyFormatArguments(object?[] arguments)
    {
        return string.Join('_', arguments.Select(LegacyFormatArgument));
    }

    private static string LegacyFormatArgument(object? argument)
    {
        return argument switch
        {
            null => "null",
            string value => value,
            IFormattable value => value.ToString(format: null, CultureInfo.InvariantCulture) ?? argument.ToString() ?? "",
            _ => argument.ToString() ?? "",
        };
    }

    /// <summary>
    /// Formats the arguments of a test case so that two cases get two names. The result is the one of earlier
    /// versions for the common arguments (numbers, strings, enums, booleans...). It only differs where those
    /// versions gave several cases the same name:
    /// <list type="bullet">
    ///   <item>the string <c>"null"</c> is quoted, so it is not confused with <see langword="null" />;</item>
    ///   <item>arrays and lists are formatted element by element instead of as their type name;</item>
    ///   <item><see cref="DateTime" />, <see cref="DateTimeOffset" /> and <see cref="TimeOnly" /> use the round-trip format, which keeps the fraction of a second and the kind.</item>
    /// </list>
    /// The characters this adds are not valid in a snapshot name, so the name gets a hash of the formatted
    /// arguments and stays distinct. The arguments that still cannot be told apart - a value whose type does not
    /// override <see cref="object.ToString" />, a string containing the '_' separator - set
    /// <paramref name="hasAmbiguousArguments" />, so a collision is reported rather than renaming every test.
    /// </summary>
    internal static string FormatArguments(object?[] arguments, out bool hasAmbiguousArguments)
    {
        return FormatArguments(arguments, splitsArguments: arguments.Length > 1, out hasAmbiguousArguments);
    }

    private static string FormatArguments(object?[] arguments, bool splitsArguments, out bool hasAmbiguousArguments)
    {
        var state = new ArgumentFormattingState();
        var result = new StringBuilder();
        for (var i = 0; i < arguments.Length; i++)
        {
            if (i > 0)
            {
                result.Append('_');
            }

            FormatArgument(result, arguments[i], splitsArguments, depth: 0, state);
        }

        hasAmbiguousArguments = state.HasAmbiguousArguments;
        return result.ToString();
    }

    private static void FormatArgument(StringBuilder result, object? argument, bool splitsArguments, int depth, ArgumentFormattingState state)
    {
        switch (argument)
        {
            case null:
                result.Append("null");
                break;

            case string value when depth > 0 || string.Equals(value, "null", StringComparison.Ordinal):
                result.Append('"').Append(value).Append('"');
                break;

            case string value:
                // '_' also separates the arguments, so ("a_b", "c") and ("a", "b_c") get the same name. Quoting
                // the value would rename the snapshots of every test with such an argument, most of which do not
                // collide, so the collision is reported instead.
                if (splitsArguments && value.Contains('_', StringComparison.Ordinal))
                {
                    state.HasAmbiguousArguments = true;
                }

                result.Append(value);
                break;

            // The general format drops the fraction of a second, and the kind of a DateTime, so two values a few
            // milliseconds apart would share a name. The round-trip format keeps everything.
            case DateTime or DateTimeOffset or TimeOnly:
                result.Append(((IFormattable)argument).ToString("O", CultureInfo.InvariantCulture));
                break;

            case IFormattable value:
                result.Append(value.ToString(format: null, CultureInfo.InvariantCulture) ?? argument.ToString());
                break;

            case IList list when depth < MaxFormattedArgumentDepth:
                var start = result.Length;
                try
                {
                    FormatList(result, list, splitsArguments, depth, state);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // A default ImmutableArray, for instance, cannot be enumerated.
                    result.Length = start;
                    result.Append(argument.ToString());
                    state.HasAmbiguousArguments = true;
                }

                break;

            default:
                if (!OverridesToString(argument.GetType()))
                {
                    state.HasAmbiguousArguments = true;
                }

                result.Append(argument.ToString());
                break;
        }
    }

    private static void FormatList(StringBuilder result, IList list, bool splitsArguments, int depth, ArgumentFormattingState state)
    {
        result.Append('[');
        var count = list.Count;
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                result.Append(", ");
            }

            // A large array would make a name that only the hash can shorten, so the elements past the limit
            // only contribute their count.
            if (result.Length > MaxFormattedArgumentLength)
            {
                result.Append("...").Append(count.ToString(CultureInfo.InvariantCulture));
                state.HasAmbiguousArguments = true;
                break;
            }

            FormatArgument(result, list[i], splitsArguments, depth + 1, state);
        }

        result.Append(']');
    }

    /// <summary>
    /// Indicates whether the type provides its own <see cref="object.ToString" />. The default implementations
    /// return the name of the type, which is the same for every value.
    /// </summary>
    private static bool OverridesToString(Type type)
    {
        try
        {
            var declaringType = type.GetMethod(nameof(ToString), BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes)?.DeclaringType;
            return declaringType is not null && declaringType != typeof(object) && declaringType != typeof(ValueType);
        }
        catch (AmbiguousMatchException)
        {
            return true;
        }
    }

    private static bool ShouldPreferDisplayName(string? displayName, string methodName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return false;

        if (string.Equals(displayName, methodName, StringComparison.Ordinal))
            return false;

        return !displayName.StartsWith(methodName + "(", StringComparison.Ordinal);
    }

    /// <summary>
    /// Names derived from a test.
    /// </summary>
    /// <param name="TestName">Name of the test.</param>
    /// <param name="LegacyTestName">Name earlier versions derived from the same test.</param>
    /// <param name="MethodName">Name of the test method.</param>
    /// <param name="HasAmbiguousArguments">Indicates whether an argument does not contribute its value to the name.</param>
    internal readonly record struct TestNames(string? TestName, string? LegacyTestName, string? MethodName, bool HasAmbiguousArguments);

    private sealed class ArgumentFormattingState
    {
        public bool HasAmbiguousArguments { get; set; }
    }
}
