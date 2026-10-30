---
title: Members
description: Choose, rename and order the members written by the serializer, with attributes or options, including on types you do not own.
---

## Use attributes

Attributes on your types control how their members are written:

<!-- ref:../../tests/Meziantou.Framework.HumanReadableSerializer.Tests/HumanReadableSerializerOptionsTests.cs;region=HumanReadableSerializer_AttributesType;format=md-fenced-code-block;dedent=true -->
```csharp
private sealed class Invoice
{
    [HumanReadablePropertyName("Total (EUR)")]
    public decimal Total { get; set; }

    [HumanReadablePropertyOrder(0)]
    public string Number { get; set; } = "";

    [HumanReadableIgnore]
    public string? InternalNote { get; set; }

    [HumanReadableIgnore(Condition = HumanReadableIgnoreCondition.WhenWritingNull)]
    public string? Discount { get; set; }

    // Non-public members are serialized when they have [HumanReadableInclude]
    [HumanReadableInclude]
    internal int Revision { get; set; }
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.HumanReadableSerializer.Tests/HumanReadableSerializerOptionsTests.cs;region=HumanReadableSerializer_AttributesUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var invoice = new Invoice
{
    Number = "INV-001",
    Total = 99.9m,
    InternalNote = "VIP customer",
    Discount = null,
    Revision = 3,
};

Assert.Equal("""
    Number: INV-001
    Total (EUR): 99.9
    Revision: 3
    """, HumanReadableSerializer.Serialize(invoice));
```
<!-- endref -->

| Attribute | Effect |
| --- | --- |
| [\[HumanReadableIgnore\]](cref:Meziantou.Framework.HumanReadable.HumanReadableIgnoreAttribute) | Skips the member. Set [Condition](cref:Meziantou.Framework.HumanReadable.HumanReadableIgnoreAttribute.Condition) to skip it only in some cases |
| [\[HumanReadableInclude\]](cref:Meziantou.Framework.HumanReadable.HumanReadableIncludeAttribute) | Writes a member that is not written by default, such as a non-public property or field |
| `[HumanReadablePropertyName("name")]` | Writes the member under another name |
| `[HumanReadablePropertyOrder(n)]` | Writes the members with this attribute first, from the lowest value to the highest |
| `[HumanReadableDefaultValue(value)]` | Sets the default value used by the [WhenWritingDefault](cref:Meziantou.Framework.HumanReadable.HumanReadableIgnoreCondition.WhenWritingDefault) conditions |
| `[HumanReadableConverter(typeof(MyConverter))]` | Writes the member, or every value of the type, with a converter. See [Converters](05-Converters.md) |

By default, the serializer writes the public properties that have a public getter. It skips fields unless
[IncludeFields](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions.IncludeFields) is `true` (public fields only), and members marked `[Obsolete]` unless [IncludeObsoleteMembers](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions.IncludeObsoleteMembers) is
`true`.

## Skip default and empty values

[DefaultIgnoreCondition](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions.DefaultIgnoreCondition) applies a condition to every member, and `[HumanReadableIgnore(Condition = ...)]` to one member:

<!-- ref:../../tests/Meziantou.Framework.HumanReadableSerializer.Tests/HumanReadableSerializerOptionsTests.cs;region=HumanReadableSerializer_DefaultIgnoreCondition;format=md-fenced-code-block;dedent=true -->
```csharp
var user = new { Name = "Ada", Age = 0, Email = (string?)null, Roles = Array.Empty<string>() };

var options = new HumanReadableSerializerOptions
{
    DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingDefaultOrEmptyCollection,
};

Assert.Equal("Name: Ada", HumanReadableSerializer.Serialize(user, options));
```
<!-- endref -->

| Condition | The member is skipped when |
| --- | --- |
| [Never](cref:Meziantou.Framework.HumanReadable.HumanReadableIgnoreCondition.Never) (the default) | Never |
| [Always](cref:Meziantou.Framework.HumanReadable.HumanReadableIgnoreCondition.Always) | Always |
| [WhenWritingNull](cref:Meziantou.Framework.HumanReadable.HumanReadableIgnoreCondition.WhenWritingNull) | The value is `null` (reference types and `Nullable<T>`) |
| [WhenWritingDefault](cref:Meziantou.Framework.HumanReadable.HumanReadableIgnoreCondition.WhenWritingDefault) | The value is the default of its type, or the value of [\[HumanReadableDefaultValue\]](cref:Meziantou.Framework.HumanReadable.HumanReadableDefaultValueAttribute) |
| [WhenWritingEmptyCollection](cref:Meziantou.Framework.HumanReadable.HumanReadableIgnoreCondition.WhenWritingEmptyCollection) | The value is an empty collection |
| [WhenWritingDefaultOrEmptyCollection](cref:Meziantou.Framework.HumanReadable.HumanReadableIgnoreCondition.WhenWritingDefaultOrEmptyCollection) | The value is the default or an empty collection |
| [Custom](cref:Meziantou.Framework.HumanReadable.HumanReadableIgnoreCondition.Custom) | [CustomCondition](cref:Meziantou.Framework.HumanReadable.HumanReadableIgnoreAttribute.CustomCondition) returns `true` |

With [Custom](cref:Meziantou.Framework.HumanReadable.HumanReadableIgnoreCondition.Custom), [CustomCondition](cref:Meziantou.Framework.HumanReadable.HumanReadableIgnoreAttribute.CustomCondition) is a `Func<HumanReadableIgnoreData, bool>` that receives the [Value](cref:Meziantou.Framework.HumanReadable.HumanReadableIgnoreData.Value) of the member,
and the [Exception](cref:Meziantou.Framework.HumanReadable.HumanReadableIgnoreData.Exception) thrown by its getter, if any. Use it with [AddAttribute](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions.AddAttribute) (below), as an attribute argument cannot
be a lambda.

## Configure types you do not own

The options add attributes to types you cannot change, such as the types of .NET or of a package:

<!-- ref:../../tests/Meziantou.Framework.HumanReadableSerializer.Tests/HumanReadableSerializerOptionsTests.cs;region=HumanReadableSerializer_IgnoreMembersOfTypeYouDoNotOwn;format=md-fenced-code-block;dedent=true -->
```csharp
var address = new MailAddress("ada@example.com", "Ada Lovelace");

var options = new HumanReadableSerializerOptions();
options.IgnoreMember<MailAddress>(x => new { x.User, x.Host });
options.AddAttribute(typeof(MailAddress), nameof(MailAddress.DisplayName), new HumanReadablePropertyNameAttribute("Name"));

Assert.Equal("""
    Name: Ada Lovelace
    Address: ada@example.com
    """, HumanReadableSerializer.Serialize(address, options));
```
<!-- endref -->

- `IgnoreMember<T>(x => x.Member)` skips a member; `x => new { x.A, x.B }` skips several. Overloads take the member
  names, a `Type`, a `PropertyInfo` or a `FieldInfo`.
- `AddAttribute(type, memberName, attribute)` adds any attribute to a member, and `AddAttribute<T>(x => x.Member,
  attribute)` does the same with an expression. `AddAttribute(type, attribute)` adds an attribute, such as a converter,
  to a type.
- [AddPropertyAttribute](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions.AddPropertyAttribute), [AddFieldAttribute](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions.AddFieldAttribute) and [AddTypeAttribute](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions.AddTypeAttribute) add an attribute to every property, field or type
  that matches a condition.
- [IgnoreMembersWithType\<T\>()](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptionsExtensions.IgnoreMembersWithType) skips every member of type `T`, for instance a `CancellationToken` or a service
  reference.

The attributes added by the options take precedence over the attributes declared in the code.

## Members that throw

By default, an exception thrown by a getter stops the serialization. [IgnoreMembersThatThrow()](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptionsExtensions.IgnoreMembersThatThrow) skips the members whose
getter throws, and [IgnoreMembersThatThrow\<TException\>()](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptionsExtensions.IgnoreMembersThatThrow) only those that throw a given exception type, such as
`NotSupportedException`.

## Order members and dictionary keys

<!-- ref:../../tests/Meziantou.Framework.HumanReadableSerializer.Tests/HumanReadableSerializerOptionsTests.cs;region=HumanReadableSerializer_Order;format=md-fenced-code-block;dedent=true -->
```csharp
var value = new
{
    Name = "Ada",
    Links = new Dictionary<string, string>(StringComparer.Ordinal) { ["web"] = "example.com", ["code"] = "github.com" },
    Age = 36,
};

var options = new HumanReadableSerializerOptions
{
    PropertyOrder = StringComparer.Ordinal,
    DictionaryKeyOrder = StringComparer.Ordinal,
};

Assert.Equal("""
    Age: 36
    Links:
      code: github.com
      web: example.com
    Name: Ada
    """, HumanReadableSerializer.Serialize(value, options));
```
<!-- endref -->

- [PropertyOrder](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions.PropertyOrder) sorts the members by name. Without it, the members are written in the order that reflection returns
  them, which is the declaration order on recent versions of .NET. Members with [\[HumanReadablePropertyOrder\]](cref:Meziantou.Framework.HumanReadable.HumanReadablePropertyOrderAttribute) still come
  first.
- [DictionaryKeyOrder](cref:Meziantou.Framework.HumanReadable.HumanReadableSerializerOptions.DictionaryKeyOrder) sorts the keys of dictionaries. Without it, the keys are written in enumeration order, except
  for the unordered collections listed in [Output format](03-OutputFormat.md#dictionaries-and-sets).

Sort both with `StringComparer.Ordinal` when the text must be the same on every runtime, as in snapshot tests.
