using System.Reflection;
using AssertionsAssert = Meziantou.Framework.Assertions.Assert;
using XunitAssert = Xunit.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertApiTests
{
    [Fact]
    public void PublicAssertionMethodsExposeNullableMessageParameter()
    {
        var nullabilityInfoContext = new NullabilityInfoContext();
        var methods = typeof(AssertionsAssert).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Where(method => !IsObjectGuardMethod(method))
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .ThenBy(method => string.Join(",", method.GetParameters().Select(parameter => parameter.ParameterType.FullName)), StringComparer.Ordinal);

        foreach (var method in methods)
        {
            var messageParameter = XunitAssert.Single(method.GetParameters(), parameter => string.Equals(parameter.Name, "message", StringComparison.Ordinal));

            XunitAssert.Equal(typeof(string), messageParameter.ParameterType);
            XunitAssert.True(messageParameter.IsOptional);
            XunitAssert.Null(messageParameter.DefaultValue);
            XunitAssert.Equal(NullabilityState.Nullable, nullabilityInfoContext.Create(messageParameter).WriteState);
        }
    }

    [Fact]
    public void MessageParameterIsAppendedToFormattedAssertionException()
    {
        var actual = "Hello";

        AssertionTestHelpers.Validate(() => AssertionsAssert.Null(actual, "custom message"), """
            Assert.Null() assertion failed.
            Message: custom message
            Expression: actual
            Expected: <null>
            Actual:   "Hello"
            """);
    }

    private static bool IsObjectGuardMethod(MethodInfo method)
    {
        return string.Equals(method.Name, nameof(Equals), StringComparison.Ordinal) ||
               string.Equals(method.Name, nameof(ReferenceEquals), StringComparison.Ordinal);
    }
}
