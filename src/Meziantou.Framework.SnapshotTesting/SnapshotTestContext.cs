using System.Collections;
using System.Reflection;

namespace Meziantou.Framework.SnapshotTesting;

public sealed record SnapshotTestContext(string? TestName = null, IReadOnlyDictionary<string, string?>? Metadata = null)
{
    private const int MaxFormattedArgumentLength = 512;
    private const int MaxFormattedArgumentDepth = 4;

    private static Func<SnapshotTestContext?>? s_xunitV3GetContext;
    private static Func<SnapshotTestContext?>? s_tunitGetContext;
    private static Func<SnapshotTestContext?>? s_nunitGetContext;

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

    private static SnapshotTestContext? Create(TestNames names, string? className, string? methodName, Func<string?> getTestId)
    {
        className = NormalizeClassName(className);
        if (names.TestName is null && className is null && methodName is null)
            return null;

        return new SnapshotTestContext(TestName: names.TestName)
        {
            ClassName = className,
            MethodName = methodName,
            IsDetectedFromTestFramework = true,
            LegacyTestName = string.Equals(names.TestName, names.LegacyTestName, StringComparison.Ordinal) ? null : names.LegacyTestName,
            AmbiguousTestId = names.HasAmbiguousArguments ? getTestId() : null,
        };
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
                    var names = GetXunitV3TestNames(
                        displayName,
                        testMethodNameOfTest: GetStringPropertyValue(test, "MethodName"),
                        hasTestCase: testCase is not null,
                        testCaseDisplayName: testCase is null ? null : GetStringPropertyValue(testCase, "TestCaseDisplayName"),
                        testClassName: testCase is null ? null : GetStringPropertyValue(testCase, "TestClassName"),
                        testMethodName: testCase is null ? null : GetStringPropertyValue(testCase, "TestMethodName"),
                        arguments: GetObjectArrayPropertyValue(test, "TestMethodArguments"));

                    return Create(names, className, names.MethodName, () => GetStringPropertyValue(test, "UniqueID"));
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

                    var names = GetTUnitTestNames(hasTestDetails: testDetails is not null, displayName, methodName, arguments);
                    return Create(names, className, methodName, () => testDetails is null ? null : GetStringPropertyValue(testDetails, "TestId"));
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
    internal static TestNames GetTUnitTestNames(bool hasTestDetails, string? displayName, string? methodName, object?[]? arguments)
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

                    var names = GetNUnitTestNames(name, nunitMethodName, fullName, GetObjectArrayPropertyValue(test, "Arguments"));
                    return Create(names, className, names.MethodName, () => GetStringPropertyValue(test, "ID"));
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
    internal static TestNames GetNUnitTestNames(string? name, string? methodName, string? fullName, object?[]? arguments)
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
    ///   <item>arrays and lists are formatted element by element instead of as their type name.</item>
    /// </list>
    /// The characters this adds are not valid in a snapshot name, so the name gets a hash of the formatted
    /// arguments and stays distinct. The arguments that still cannot be told apart - a value whose type does not
    /// override <see cref="object.ToString" />, a string containing the '_' separator - set
    /// <paramref name="hasAmbiguousArguments" />, so a collision is reported rather than renaming every test.
    /// </summary>
    internal static string FormatArguments(object?[] arguments, out bool hasAmbiguousArguments)
    {
        var state = new ArgumentFormattingState();
        var result = new StringBuilder();
        for (var i = 0; i < arguments.Length; i++)
        {
            if (i > 0)
            {
                result.Append('_');
            }

            FormatArgument(result, arguments[i], splitsArguments: arguments.Length > 1, depth: 0, state);
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
