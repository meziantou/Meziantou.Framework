# Meziantou.Framework.Assertions

Assertion helpers for .NET tests.

## Assertion methods

`Assert` provides the following assertion methods:

- `True`: Asserts a condition is true.
- `False`: Asserts a condition is false.
- `Null`: Asserts a value is null.
- `NotNull`: Asserts a value is not null and returns it.
- `Same`: Asserts two references point to the same instance.
- `NotSame`: Asserts two references are different instances.
- `Equal`: Asserts two values are equal.
- `NotEqual`: Asserts two values are not equal.
- `EqualUnordered`: Asserts two sequences contain the same values regardless of order.
- `NotEqualUnordered`: Asserts two sequences differ when compared without ordering.
- `EqualByStructure`: Asserts two objects are equal by recursively comparing members.
- `NotEqualByStructure`: Asserts two objects are structurally different.
- `Equivalent`: Alias of structural equality checks.
- `IsType`: Asserts a value is exactly of the specified type and returns it.
- `IsNotType`: Asserts a value is not exactly of the specified type.
- `IsAssignableTo`: Asserts a value is assignable to the specified type and returns it.
- `IsNotAssignableTo`: Asserts a value is not assignable to the specified type.
- `InRange`: Asserts a value is within the inclusive range.
- `NotInRange`: Asserts a value is outside the inclusive range.
- `StartsWith`: Asserts text or sequence starts with an expected prefix.
- `EndsWith`: Asserts text or sequence ends with an expected suffix.
- `DoesNotStartWith`: Asserts text or sequence does not start with a prefix.
- `DoesNotEndWith`: Asserts text or sequence does not end with a suffix.
- `Contains`: Asserts a value, key, predicate, or subsequence is present.
- `DoesNotContain`: Asserts a value, key, predicate, or subsequence is not present.
- `Matches`: Asserts text matches a regular expression.
- `DoesNotMatch`: Asserts text does not match a regular expression.
- `All`: Asserts all items in a collection satisfy an assertion.
- `Collection`: Asserts collection items match a list of inspectors.
- `Distinct`: Asserts all items in a sequence are distinct.
- `NotDistinct`: Asserts a sequence contains duplicate items.
- `Empty`: Asserts a collection or sequence is empty.
- `NotEmpty`: Asserts a collection or sequence is not empty.
- `Single`: Asserts a sequence has exactly one item and returns it.
- `HasCount`: Asserts a collection has an exact count.
- `DoesNotHaveCount`: Asserts a collection count differs from the specified count.
- `HasCountGreaterThan`: Asserts a collection count is greater than a value.
- `HasCountGreaterThanOrEqual`: Asserts a collection count is greater than or equal to a value.
- `HasCountLessThan`: Asserts a collection count is less than a value.
- `HasCountLessThanOrEqual`: Asserts a collection count is less than or equal to a value.
- `ProperSubset`: Asserts a set is a proper subset of another set.
- `NotProperSubset`: Asserts a set is not a proper subset of another set.
- `ProperSuperset`: Asserts a set is a proper superset of another set.
- `NotProperSuperset`: Asserts a set is not a proper superset of another set.
- `Throws`: Asserts a specific exception type is thrown and returns it.
- `ThrowsAny`: Asserts an exception assignable to the specified type is thrown and returns it.
- `ThrowsAsync`: Asserts a specific exception type is thrown by an async delegate.
- `ThrowsAnyAsync`: Asserts an assignable exception type is thrown by an async delegate.
- `DoesNotThrow`: Asserts a delegate completes without throwing.
- `DoesNotThrowAny`: Asserts a delegate completes without throwing any exception type.
- `Raise`: Asserts a specific event is raised and captures event data.
- `RaiseAny`: Asserts an event with a compatible event args type is raised.
- `DoesNotRaise`: Asserts a specific event is not raised.
- `DoesNotRaiseAny`: Asserts no compatible event is raised.
- `Fail`: Fails the test with a custom message.

## Use as the default `Assert` class

You can override the `Assert` type used in your tests with a global alias:

```xml
<Using Include="Meziantou.Framework.Assertions.Assert" Alias="Assert" />
```

or:

```csharp
global using Assert = Meziantou.Framework.Assertions.Assert;
```

## Analyzer rules

The package ships analyzers and code fixes to help write clearer assertions.

<!-- analyzer-rules -->
| Id | Category | Description | Severity | Enabled |
| -- | -- | -- | :--: | :--: |
| `MFAS0001` | Assertions | Pass the expected value before the actual value | Warning | ✔️ |
| `MFAS0002` | Assertions | Use Assert.Same instead of Assert.ReferenceEquals | Error | ✔️ |
| `MFAS0003` | Assertions | Do not use Assert.IsType with static or abstract types | Error | ✔️ |
| `MFAS0004` | Assertions | Use Assert.Empty for zero count checks | Warning | ✔️ |
| `MFAS0005` | Assertions | Use specialized count assertions | Warning | ✔️ |
| `MFAS0006` | Assertions | Use Assert.Null for null comparisons | Warning | ✔️ |
| `MFAS0007` | Assertions | Use Assert.NotNull for null comparisons | Warning | ✔️ |
| `MFAS0008` | Assertions | Do not use Assert.Null with value types | Error | ✔️ |
| `MFAS0009` | Assertions | Do not use Assert.NotNull with value types | Error | ✔️ |
| `MFAS0010` | Assertions | Do not use Assert.Same with value types | Error | ✔️ |
| `MFAS0011` | Assertions | Do not use Assert.NotSame with value types | Error | ✔️ |
<!-- analyzer-rules -->
