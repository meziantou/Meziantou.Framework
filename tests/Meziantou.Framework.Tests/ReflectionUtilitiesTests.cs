namespace Meziantou.Framework.Tests;

public class ReflectionUtilitiesTests
{
    [Fact]
    public void Documentation_TypeChecks()
    {
        #region ReflectionUtilities_TypeChecks
        Assert.True(typeof(int?).IsNullableOfT());
        Assert.False(typeof(int).IsNullableOfT());

        Assert.True(typeof(FileAttributes).IsFlagsEnum());
        Assert.False(ReflectionUtilities.IsFlagsEnum<DayOfWeek>());

        // string declares an implicit conversion to ReadOnlySpan<char>
        var conversion = ReflectionUtilities.GetImplicitConversion("text", typeof(ReadOnlySpan<char>));
        Assert.NotNull(conversion);
        Assert.Equal("op_Implicit", conversion.Name);

        // The conversions built into the language, such as int to long, are not operators
        Assert.Null(ReflectionUtilities.GetImplicitConversion(42, typeof(long)));
        #endregion
    }

    [Fact]
    public void Documentation_MethodLocation()
    {
        #region ReflectionUtilities_GetMethodLocation
        Func<int> getAnswer = () => 42;

        // Reads the portable PDB, embedded or next to the assembly
        var location = getAnswer.Method.GetMethodLocation();
        Assert.NotNull(location);
        Assert.EndsWith("ReflectionUtilitiesTests.cs", location.Value.FilePath);
        Assert.True(location.Value.SequencePoint.StartLine > 0);
        #endregion
    }

    [Fact]
    public void Documentation_EnumParse()
    {
        #region EnumExtensions_Parse
        // Parse and TryParse are available on every enum type
        var day = DayOfWeek.Parse("friday", ignoreCase: true);
        Assert.Equal(DayOfWeek.Friday, day);

        Assert.True(DayOfWeek.TryParse("Sunday", ignoreCase: false, out var sunday));
        Assert.Equal(DayOfWeek.Sunday, sunday);
        #endregion
    }

    [Fact]
    public void IsFlagsEnum_ShouldDetectNonEnumeration()
    {
        Assert.False(ReflectionUtilities.IsFlagsEnum(typeof(ReflectionUtilitiesTests)));
    }

    [Fact]
    public void IsFlagsEnum_ShouldDetectNonFlagsEnumeration()
    {
        Assert.False(ReflectionUtilities.IsFlagsEnum(typeof(NonFlagsEnum)));
    }

    [Fact]
    public void IsFlagsEnum_ShouldDetectFlagsEnumeration()
    {
        Assert.True(ReflectionUtilities.IsFlagsEnum(typeof(FlagsEnum)));
    }

    [Theory]
    [InlineData(typeof(int), false)]
    [InlineData(typeof(int?), true)]
    [InlineData(typeof(MyNullable<int>), false)]
    public void IsNullableOf_ShouldDetectType(Type type, bool expectedResult)
    {
        Assert.Equal(expectedResult, ReflectionUtilities.IsNullableOfT(type));
    }

    private enum NonFlagsEnum
    {
        A,
        B,
    }

    [Flags]
    private enum FlagsEnum
    {
        A,
        B,
    }

    private static class MyNullable<T>
    {
    }

    [Fact]
    public void GetImplicitConversion_OperatorDeclaredByTargetType()
    {
        var conversion = ReflectionUtilities.GetImplicitConversion(42, typeof(Wrapper));
        Assert.NotNull(conversion);
        Assert.Equal(typeof(Wrapper), conversion.DeclaringType);

        var wrapper = (Wrapper)conversion.Invoke(obj: null, [42])!;
        Assert.Equal(42, wrapper.Value);
    }

    [Fact]
    public void GetImplicitConversion_OperatorDeclaredBySourceType()
    {
        var conversion = ReflectionUtilities.GetImplicitConversion(new Wrapper(1), typeof(int));
        Assert.NotNull(conversion);
        Assert.Equal(typeof(Wrapper), conversion.DeclaringType);
    }

    [Fact]
    public void GetImplicitConversion_NoOperator()
    {
        Assert.Null(ReflectionUtilities.GetImplicitConversion("text", typeof(Wrapper)));
    }

    private sealed class Wrapper(int value)
    {
        public int Value { get; } = value;

        public static implicit operator Wrapper(int value) => new(value);

        public static implicit operator int(Wrapper wrapper) => wrapper.Value;
    }
}
